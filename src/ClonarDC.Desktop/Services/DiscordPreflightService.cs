using System.Net;
using System.Net.Http.Headers;
using System.Numerics;

namespace ClonarDC.Services;

public sealed class DiscordPreflightService : IDisposable
{
    private static readonly BigInteger Administrator = BigInteger.One << 3;
    private static readonly BigInteger ManageChannels = BigInteger.One << 4;
    private static readonly BigInteger ManageGuild = BigInteger.One << 5;
    private static readonly BigInteger ManageRoles = BigInteger.One << 28;
    private static readonly BigInteger ManageGuildExpressions = BigInteger.One << 30;
    private static readonly BigInteger CreateGuildExpressions = BigInteger.One << 43;

    private readonly HttpClient _http = new()
    {
        BaseAddress = new Uri("https://discord.com/api/v10/"),
        Timeout = TimeSpan.FromSeconds(30)
    };

    public DiscordPreflightService()
    {
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("GuildSync-Preflight/0.8.3.3");
    }

    public void SetToken(string token)
    {
        _http.DefaultRequestHeaders.Authorization = TokenAuthorization.Create(token);
    }

    public async Task<DiscordPreflightReport> CheckAsync(
        string guildId,
        string mode,
        bool requiresExpressions,
        IEnumerable<string>? sourceRolePermissions = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(guildId) || !guildId.All(char.IsDigit))
            throw new InvalidOperationException("ID do servidor de destino inválido.");

        mode = mode.Trim().ToLowerInvariant();
        var me = await GetAsync("users/@me", ct);
        var botId = me["id"]?.GetValue<string>()
                    ?? throw new InvalidDataException("O Discord não devolveu a identidade do bot.");
        var guild = await GetAsync($"guilds/{guildId}", ct);
        var member = await GetAsync($"guilds/{guildId}/members/{botId}", ct);
        var ownerId = guild["owner_id"]?.GetValue<string>();
        var roles = guild["roles"]?.AsArray()
                    ?? throw new InvalidDataException("O Discord não devolveu os cargos do servidor de destino.");
        var memberRoleIds = member["roles"]?.AsArray()
            .Select(node => node?.GetValue<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal) ?? [];

        var isOwner = string.Equals(ownerId, botId, StringComparison.Ordinal);
        var permissions = BigInteger.Zero;
        var highestPosition = 0;
        foreach (var roleNode in roles)
        {
            if (roleNode is null) continue;
            var roleId = roleNode["id"]?.GetValue<string>() ?? string.Empty;
            var included = string.Equals(roleId, guildId, StringComparison.Ordinal) || memberRoleIds.Contains(roleId);
            if (!included) continue;
            if (BigInteger.TryParse(roleNode["permissions"]?.GetValue<string>(), out var rolePermissions))
                permissions |= rolePermissions;
            highestPosition = Math.Max(highestPosition, roleNode["position"]?.GetValue<int>() ?? 0);
        }

        var administrator = isOwner || Has(permissions, Administrator);
        var report = new DiscordPreflightReport
        {
            BotId = botId,
            BotName = me["global_name"]?.GetValue<string>() ?? me["username"]?.GetValue<string>() ?? "GuildSync bot",
            HighestRolePosition = highestPosition,
            EffectivePermissions = permissions.ToString(),
            Administrator = administrator
        };

        if (!administrator && !Has(permissions, ManageGuild))
            report.BlockingIssues.Add("O bot precisa da permissão Manage Server para copiar o nome e o ícone do servidor.");
        if (!administrator && !Has(permissions, ManageChannels))
            report.BlockingIssues.Add("O bot precisa da permissão Manage Channels para criar, atualizar, ordenar ou remover canais.");
        if (!administrator && !Has(permissions, ManageRoles))
            report.BlockingIssues.Add("O bot precisa da permissão Manage Roles para criar cargos e aplicar permissões de canais.");

        if (requiresExpressions && !administrator)
        {
            if (mode == "exact" && !Has(permissions, ManageGuildExpressions))
                report.BlockingIssues.Add("O modo Exact precisa de Manage Expressions para remover e recriar emojis existentes.");
            else if (!Has(permissions, ManageGuildExpressions) && !Has(permissions, CreateGuildExpressions))
                report.BlockingIssues.Add("O bot precisa de Manage Expressions ou Create Expressions para copiar emojis.");
        }

        if (!administrator && sourceRolePermissions is not null)
        {
            var requestedPermissions = BigInteger.Zero;
            foreach (var value in sourceRolePermissions)
            {
                if (BigInteger.TryParse(value, out var parsed) && parsed >= 0)
                    requestedPermissions |= parsed;
            }
            var unavailable = requestedPermissions & ~permissions;
            if (unavailable != BigInteger.Zero)
                report.BlockingIssues.Add("A origem contém permissões de cargos que o bot não possui no destino. Conceda as permissões necessárias ao bot ou remova-as dos cargos de origem antes de clonar.");
        }

        var unmanageableRoles = roles
            .Where(roleNode => roleNode is not null)
            .Where(roleNode => !(roleNode!["managed"]?.GetValue<bool>() ?? false))
            .Where(roleNode => !string.Equals(roleNode!["id"]?.GetValue<string>(), guildId, StringComparison.Ordinal))
            .Where(roleNode => (roleNode!["position"]?.GetValue<int>() ?? 0) >= highestPosition)
            .Select(roleNode => roleNode!["name"]?.GetValue<string>() ?? "unnamed role")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();

        if (!administrator && unmanageableRoles.Count > 0)
        {
            var names = string.Join(", ", unmanageableRoles);
            if (mode == "exact")
                report.BlockingIssues.Add("O modo Exact não pode remover cargos acima ou no mesmo nível do cargo mais alto do bot: " + names + ". Mova o cargo do bot para cima.");
            else
                report.Warnings.Add("Alguns cargos estão acima do bot e não poderão ser atualizados ou reposicionados: " + names + ".");
        }

        var features = guild["features"]?.AsArray()
            .Select(node => node?.GetValue<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var protectedChannels = new[]
        {
            guild["rules_channel_id"]?.GetValue<string>(),
            guild["public_updates_channel_id"]?.GetValue<string>(),
            guild["safety_alerts_channel_id"]?.GetValue<string>()
        }.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).ToList();

        if (mode == "exact" && features.Contains("COMMUNITY") && protectedChannels.Count > 0)
            report.BlockingIssues.Add("O destino é um servidor Community com canais protegidos pelo Discord. Use Merge/Safe ou reconfigure/desative Community manualmente antes do modo Exact.");

        if (administrator)
            report.Warnings.Add("O bot possui Administrator. A operação terá acesso amplo; mantenha esta permissão apenas enquanto for necessária.");
        report.Passed = report.BlockingIssues.Count == 0;
        return report;
    }

    private async Task<JsonNode> GetAsync(string path, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            using var response = await _http.GetAsync(path, ct);
            var raw = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode)
                return JsonNode.Parse(raw) ?? throw new InvalidDataException("O Discord devolveu uma resposta vazia.");
            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < 4)
            {
                var seconds = 1.0;
                try { seconds = JsonNode.Parse(raw)?["retry_after"]?.GetValue<double>() ?? 1.0; } catch { }
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Clamp(seconds * 1000, 250, 15_000)), ct);
                continue;
            }
            throw new InvalidOperationException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "O Discord recusou o token do bot.",
                HttpStatusCode.Forbidden => "O bot não consegue consultar a própria associação ao servidor de destino.",
                HttpStatusCode.NotFound => "O servidor de destino não foi encontrado para este bot ou o bot não é membro dele.",
                _ => $"Falha no preflight do Discord: HTTP {(int)response.StatusCode}."
            });
        }
        throw new InvalidOperationException("O Discord não concluiu o preflight após várias tentativas.");
    }

    private static bool Has(BigInteger permissions, BigInteger flag) => (permissions & flag) == flag;

    public void Dispose() => _http.Dispose();
}

public sealed class DiscordPreflightReport
{
    public string BotId { get; set; } = string.Empty;
    public string BotName { get; set; } = string.Empty;
    public int HighestRolePosition { get; set; }
    public string EffectivePermissions { get; set; } = "0";
    public bool Administrator { get; set; }
    public bool Passed { get; set; }
    public List<string> BlockingIssues { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

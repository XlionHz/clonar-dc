using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;

namespace ClonarDC.Services;

public sealed class DiscordService : IDisposable
{
    private const string ApiBase = "https://discord.com/api/v10/";
    private readonly HttpClient _http = new() { BaseAddress = new Uri(ApiBase), Timeout = TimeSpan.FromSeconds(45) };
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private string _token = "";

    public void SetToken(string token)
    {
        token = token.Trim();
        if (token.StartsWith("Bot ", StringComparison.OrdinalIgnoreCase)) token = token[4..].Trim();
        _token = token;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bot", token);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("ClonarDC/0.3 (+desktop-app)");
    }

    public async Task<string> ValidateTokenAsync(CancellationToken ct = default)
    {
        EnsureToken();
        using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, "users/@me"), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(FriendlyDiscordError(response.StatusCode, body));
        var node = JsonNode.Parse(body)!;
        return node["username"]?.GetValue<string>() ?? node["id"]?.GetValue<string>() ?? "Bot";
    }

    public async Task<List<GuildSummary>> GetGuildsAsync(CancellationToken ct = default)
    {
        EnsureToken();
        using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, "users/@me/guilds?limit=200"), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(FriendlyDiscordError(response.StatusCode, body));
        var arr = JsonNode.Parse(body)?.AsArray() ?? [];
        return arr.Select(n => new GuildSummary(
            n?["id"]?.GetValue<string>() ?? "",
            n?["name"]?.GetValue<string>() ?? "Servidor",
            n?["icon"]?.GetValue<string>())).Where(x => x.Id.Length > 0).OrderBy(x => x.Name).ToList();
    }

    public async Task<GuildSnapshot> CaptureAsync(string guildId, IProgress<OperationLog>? progress = null, CancellationToken ct = default)
    {
        EnsureSnowflake(guildId);
        progress?.Report(Log("info", "Lendo informações do servidor…"));
        var guild = await GetJsonAsync($"guilds/{guildId}?with_counts=true", ct);
        var roles = (await GetJsonAsync($"guilds/{guildId}/roles", ct)).AsArray();
        var channels = (await GetJsonAsync($"guilds/{guildId}/channels", ct)).AsArray();
        JsonArray emojis;
        try { emojis = (await GetJsonAsync($"guilds/{guildId}/emojis", ct)).AsArray(); }
        catch { emojis = []; }

        var snapshot = new GuildSnapshot
        {
            SourceGuildId = guildId,
            Name = guild["name"]?.GetValue<string>() ?? "Servidor",
            Roles = roles.Select(ParseRole).OrderBy(r => r.Position).ToList(),
            Channels = channels.Select(ParseChannel).OrderBy(c => c.Position).ToList(),
            Emojis = emojis.Select(ParseEmoji).ToList()
        };

        var iconHash = guild["icon"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(iconHash))
        {
            try
            {
                var bytes = await new HttpClient().GetByteArrayAsync($"https://cdn.discordapp.com/icons/{guildId}/{iconHash}.png?size=256", ct);
                snapshot.IconData = "data:image/png;base64," + Convert.ToBase64String(bytes);
            }
            catch { progress?.Report(Log("warning", "Não foi possível baixar o ícone do servidor.")); }
        }

        foreach (var emoji in snapshot.Emojis)
        {
            try
            {
                var ext = emoji.Animated ? "gif" : "png";
                var bytes = await new HttpClient().GetByteArrayAsync($"https://cdn.discordapp.com/emojis/{emoji.Id}.{ext}?quality=lossless", ct);
                emoji.ImageData = $"data:image/{ext};base64," + Convert.ToBase64String(bytes);
            }
            catch { progress?.Report(Log("warning", $"Emoji {emoji.Name} será mantido sem asset no backup.")); }
        }

        progress?.Report(Log("success", $"Snapshot concluído: {snapshot.Roles.Count} cargos, {snapshot.Channels.Count} canais, {snapshot.Emojis.Count} emojis."));
        return snapshot;
    }

    public async Task<ClonePlan> AnalyzeAsync(string sourceGuildId, string targetGuildId, string mode, CancellationToken ct = default)
    {
        if (sourceGuildId == targetGuildId) throw new InvalidOperationException("O servidor original e o destino não podem ser iguais.");
        var source = await CaptureAsync(sourceGuildId, null, ct);
        var target = await CaptureAsync(targetGuildId, null, ct);
        var safe = !string.Equals(mode, "exact", StringComparison.OrdinalIgnoreCase);
        var sourceRoleNames = source.Roles.Where(r => !r.Managed && r.Name != "@everyone").Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targetRoleNames = target.Roles.Where(r => !r.Managed && r.Name != "@everyone").Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sourceChannelKeys = source.Channels.Select(ChannelKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targetChannelKeys = target.Channels.Select(ChannelKey).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var plan = new ClonePlan
        {
            SourceGuildId = sourceGuildId,
            TargetGuildId = targetGuildId,
            Mode = mode,
            RolesToCreate = sourceRoleNames.Count(x => !targetRoleNames.Contains(x)),
            ChannelsToCreate = sourceChannelKeys.Count(x => !targetChannelKeys.Contains(x)),
            EmojisToCreate = source.Emojis.Count,
            TargetRolesToDelete = safe ? 0 : target.Roles.Count(r => !r.Managed && r.Name != "@everyone"),
            TargetChannelsToDelete = safe ? 0 : target.Channels.Count
        };
        if (source.Roles.Any(r => r.Managed)) plan.Warnings.Add("Cargos gerenciados por bots/integrações não são clonáveis diretamente e serão ignorados.");
        if (source.Channels.SelectMany(c => c.PermissionOverwrites).Any(o => o.Type == 1)) plan.Warnings.Add("Sobrescritas de permissão específicas de membros não podem ser mapeadas automaticamente para outro servidor.");
        if (plan.IsDestructive) plan.Warnings.Add("Modo exato remove estrutura existente do destino; um backup automático é obrigatório antes da execução.");
        return plan;
    }

    public async Task ExecuteCloneAsync(GuildSnapshot source, string targetGuildId, string mode, IProgress<OperationLog>? progress = null, CancellationToken ct = default)
    {
        EnsureSnowflake(targetGuildId);
        if (source.SourceGuildId == targetGuildId) throw new InvalidOperationException("Origem e destino não podem ser o mesmo servidor.");
        var exact = string.Equals(mode, "exact", StringComparison.OrdinalIgnoreCase);
        var target = await CaptureAsync(targetGuildId, progress, ct);

        if (exact)
        {
            progress?.Report(Log("warning", "Modo exato: removendo canais existentes do destino…"));
            foreach (var channel in target.Channels.OrderByDescending(c => c.Position))
            {
                ct.ThrowIfCancellationRequested();
                await DeleteIgnoringExpectedAsync($"channels/{channel.Id}", progress, $"canal {channel.Name}", ct);
            }
            progress?.Report(Log("warning", "Modo exato: removendo cargos não gerenciados…"));
            foreach (var role in target.Roles.Where(r => !r.Managed && r.Name != "@everyone").OrderByDescending(r => r.Position))
            {
                ct.ThrowIfCancellationRequested();
                await DeleteIgnoringExpectedAsync($"guilds/{targetGuildId}/roles/{role.Id}", progress, $"cargo {role.Name}", ct);
            }
            target = await CaptureAsync(targetGuildId, null, ct);
        }

        try
        {
            var patch = new JsonObject { ["name"] = source.Name };
            if (!string.IsNullOrWhiteSpace(source.IconData)) patch["icon"] = source.IconData;
            await SendJsonAsync(HttpMethod.Patch, $"guilds/{targetGuildId}", patch, ct);
            progress?.Report(Log("success", "Nome e ícone processados."));
        }
        catch (Exception ex) { progress?.Report(Log("warning", "Configuração básica não pôde ser aplicada: " + ex.Message)); }

        var roleMap = new Dictionary<string, string>(StringComparer.Ordinal) { [source.SourceGuildId] = targetGuildId };
        var existingRoles = target.Roles.Where(r => !r.Managed).GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var role in source.Roles.Where(r => !r.Managed && r.Name != "@everyone").OrderBy(r => r.Position))
        {
            ct.ThrowIfCancellationRequested();
            if (!exact && existingRoles.TryGetValue(role.Name, out var existing))
            {
                roleMap[role.Id] = existing.Id;
                progress?.Report(Log("info", $"Cargo preservado: {role.Name}"));
                continue;
            }
            try
            {
                var body = new JsonObject
                {
                    ["name"] = role.Name,
                    ["permissions"] = role.Permissions,
                    ["color"] = role.Color,
                    ["hoist"] = role.Hoist,
                    ["mentionable"] = role.Mentionable
                };
                var created = await SendJsonAsync(HttpMethod.Post, $"guilds/{targetGuildId}/roles", body, ct);
                var newId = created["id"]?.GetValue<string>();
                if (newId is not null) roleMap[role.Id] = newId;
                progress?.Report(Log("success", $"Cargo criado: {role.Name}"));
            }
            catch (Exception ex) { progress?.Report(Log("error", $"Cargo {role.Name}: {ex.Message}")); }
        }

        var channelMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var existingChannels = target.Channels.GroupBy(ChannelKey, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var category in source.Channels.Where(c => c.Type == 4).OrderBy(c => c.Position))
            await CreateOrMapChannelAsync(category, targetGuildId, exact, existingChannels, roleMap, channelMap, progress, ct);
        foreach (var channel in source.Channels.Where(c => c.Type != 4).OrderBy(c => c.Position))
            await CreateOrMapChannelAsync(channel, targetGuildId, exact, existingChannels, roleMap, channelMap, progress, ct);

        var existingEmojiNames = target.Emojis.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var emoji in source.Emojis)
        {
            ct.ThrowIfCancellationRequested();
            if (!exact && existingEmojiNames.Contains(emoji.Name)) continue;
            if (string.IsNullOrWhiteSpace(emoji.ImageData)) { progress?.Report(Log("warning", $"Emoji {emoji.Name} sem asset; ignorado.")); continue; }
            try
            {
                await SendJsonAsync(HttpMethod.Post, $"guilds/{targetGuildId}/emojis", new JsonObject { ["name"] = emoji.Name, ["image"] = emoji.ImageData }, ct);
                progress?.Report(Log("success", $"Emoji criado: {emoji.Name}"));
            }
            catch (Exception ex) { progress?.Report(Log("warning", $"Emoji {emoji.Name}: {ex.Message}")); }
        }

        progress?.Report(Log("success", "Operação concluída. Faça uma nova análise para comparar origem e destino."));
    }

    private async Task CreateOrMapChannelAsync(ChannelSnapshot channel, string targetGuildId, bool exact, Dictionary<string, ChannelSnapshot> existingChannels, Dictionary<string, string> roleMap, Dictionary<string, string> channelMap, IProgress<OperationLog>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var key = ChannelKey(channel);
        if (!exact && existingChannels.TryGetValue(key, out var existing))
        {
            channelMap[channel.Id] = existing.Id;
            progress?.Report(Log("info", $"Canal preservado: {channel.Name}"));
            return;
        }
        try
        {
            var body = new JsonObject { ["name"] = channel.Name, ["type"] = channel.Type };
            if (channel.ParentId is not null && channelMap.TryGetValue(channel.ParentId, out var parent)) body["parent_id"] = parent;
            if (channel.Type is 0 or 5 or 15 or 16)
            {
                if (channel.Topic is not null) body["topic"] = channel.Topic;
                body["nsfw"] = channel.Nsfw;
                if (channel.RateLimitPerUser is not null) body["rate_limit_per_user"] = channel.RateLimitPerUser;
            }
            if (channel.Type is 2 or 13)
            {
                if (channel.Bitrate is not null) body["bitrate"] = channel.Bitrate;
                if (channel.UserLimit is not null) body["user_limit"] = channel.UserLimit;
            }
            var overwrites = new JsonArray();
            foreach (var overwrite in channel.PermissionOverwrites.Where(x => x.Type == 0))
            {
                if (!roleMap.TryGetValue(overwrite.Id, out var mapped)) continue;
                overwrites.Add(new JsonObject { ["id"] = mapped, ["type"] = overwrite.Type, ["allow"] = overwrite.Allow, ["deny"] = overwrite.Deny });
            }
            if (overwrites.Count > 0) body["permission_overwrites"] = overwrites;
            var created = await SendJsonAsync(HttpMethod.Post, $"guilds/{targetGuildId}/channels", body, ct);
            var id = created["id"]?.GetValue<string>();
            if (id is not null) channelMap[channel.Id] = id;
            progress?.Report(Log("success", $"Canal criado: {channel.Name}"));
        }
        catch (Exception ex) { progress?.Report(Log("error", $"Canal {channel.Name}: {ex.Message}")); }
    }

    private async Task<JsonNode> GetJsonAsync(string path, CancellationToken ct)
    {
        using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, path), ct);
        var raw = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(FriendlyDiscordError(response.StatusCode, raw));
        return JsonNode.Parse(raw) ?? new JsonObject();
    }

    private async Task<JsonNode> SendJsonAsync(HttpMethod method, string path, JsonNode body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, path) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        using var response = await SendAsync(req, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(FriendlyDiscordError(response.StatusCode, raw));
        return string.IsNullOrWhiteSpace(raw) ? new JsonObject() : JsonNode.Parse(raw) ?? new JsonObject();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage original, CancellationToken ct)
    {
        await _requestGate.WaitAsync(ct);
        try
        {
            for (var attempt = 0; attempt < 6; attempt++)
            {
                using var req = await CloneRequestAsync(original, ct);
                var response = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                if ((int)response.StatusCode != 429) return response;
                var raw = await response.Content.ReadAsStringAsync(ct);
                var retry = 1.0;
                try { retry = JsonNode.Parse(raw)?["retry_after"]?.GetValue<double>() ?? 1.0; } catch { }
                response.Dispose();
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(retry, 0.1, 30)), ct);
            }
            throw new InvalidOperationException("A API aplicou limite de requisições repetidamente. Aguarde e tente novamente.");
        }
        finally { _requestGate.Release(); }
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage source, CancellationToken ct)
    {
        var clone = new HttpRequestMessage(source.Method, source.RequestUri);
        foreach (var h in source.Headers) clone.Headers.TryAddWithoutValidation(h.Key, h.Value);
        if (source.Content is not null)
        {
            var bytes = await source.Content.ReadAsByteArrayAsync(ct);
            clone.Content = new ByteArrayContent(bytes);
            foreach (var h in source.Content.Headers) clone.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
        }
        return clone;
    }

    private async Task DeleteIgnoringExpectedAsync(string path, IProgress<OperationLog>? progress, string label, CancellationToken ct)
    {
        using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Delete, path), ct);
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound) progress?.Report(Log("info", $"Removido: {label}"));
        else progress?.Report(Log("warning", $"Não foi possível remover {label} (HTTP {(int)response.StatusCode})."));
    }

    private static RoleSnapshot ParseRole(JsonNode? n) => new()
    {
        Id = n?["id"]?.GetValue<string>() ?? "", Name = n?["name"]?.GetValue<string>() ?? "Cargo",
        Permissions = n?["permissions"]?.GetValue<string>() ?? "0", Color = n?["color"]?.GetValue<int>() ?? 0,
        Hoist = n?["hoist"]?.GetValue<bool>() ?? false, Mentionable = n?["mentionable"]?.GetValue<bool>() ?? false,
        Managed = n?["managed"]?.GetValue<bool>() ?? false, Position = n?["position"]?.GetValue<int>() ?? 0
    };

    private static ChannelSnapshot ParseChannel(JsonNode? n)
    {
        var c = new ChannelSnapshot
        {
            Id = n?["id"]?.GetValue<string>() ?? "", Name = n?["name"]?.GetValue<string>() ?? "canal",
            Type = n?["type"]?.GetValue<int>() ?? 0, ParentId = n?["parent_id"]?.GetValue<string>(), Position = n?["position"]?.GetValue<int>() ?? 0,
            Topic = n?["topic"]?.GetValue<string>(), Nsfw = n?["nsfw"]?.GetValue<bool>() ?? false,
            Bitrate = n?["bitrate"]?.GetValue<int?>(), UserLimit = n?["user_limit"]?.GetValue<int?>(), RateLimitPerUser = n?["rate_limit_per_user"]?.GetValue<int?>()
        };
        if (n?["permission_overwrites"] is JsonArray arr)
            c.PermissionOverwrites = arr.Select(o => new PermissionOverwriteSnapshot { Id = o?["id"]?.GetValue<string>() ?? "", Type = o?["type"]?.GetValue<int>() ?? 0, Allow = o?["allow"]?.GetValue<string>() ?? "0", Deny = o?["deny"]?.GetValue<string>() ?? "0" }).ToList();
        return c;
    }

    private static EmojiSnapshot ParseEmoji(JsonNode? n) => new() { Id = n?["id"]?.GetValue<string>() ?? "", Name = n?["name"]?.GetValue<string>() ?? "emoji", Animated = n?["animated"]?.GetValue<bool>() ?? false };
    private static string ChannelKey(ChannelSnapshot c) => $"{c.Type}:{c.Name}:{c.ParentId ?? "root"}";
    private static OperationLog Log(string level, string message) => new(DateTimeOffset.Now, level, message);
    private void EnsureToken() { if (string.IsNullOrWhiteSpace(_token)) throw new InvalidOperationException("Informe o Token antes de continuar."); }
    private static void EnsureSnowflake(string id) { if (string.IsNullOrWhiteSpace(id) || !id.All(char.IsDigit)) throw new InvalidOperationException("ID de servidor inválido."); }

    private static string FriendlyDiscordError(HttpStatusCode code, string raw)
    {
        string? message = null;
        try { message = JsonNode.Parse(raw)?["message"]?.GetValue<string>(); } catch { }
        return code switch
        {
            HttpStatusCode.Unauthorized => "Token inválido ou revogado.",
            HttpStatusCode.Forbidden => "O bot não possui permissão suficiente para esta ação.",
            HttpStatusCode.NotFound => "Recurso não encontrado ou o bot não tem acesso ao servidor.",
            _ => $"Discord API: {(int)code} {message ?? code.ToString()}"
        };
    }

    public void Dispose() { _http.Dispose(); _requestGate.Dispose(); }
}

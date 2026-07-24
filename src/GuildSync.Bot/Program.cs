using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Discord;
using Discord.WebSocket;

var settings = BotSettings.FromEnvironment();
settings.Validate();
await new GuildSyncBot(settings).RunAsync();

sealed class GuildSyncBot
{
    private readonly BotSettings _settings;
    private readonly DiscordSocketClient _client;
    private readonly GuildSyncApiClient _api;
    private int _commandsRegistered;

    public GuildSyncBot(BotSettings settings)
    {
        _settings = settings;
        _api = new GuildSyncApiClient(settings.ApiUrl, settings.ApiKey);
        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds,
            AlwaysDownloadUsers = false,
            LogGatewayIntentWarnings = false
        });
        _client.Log += LogAsync;
        _client.Ready += RegisterCommandsAsync;
        _client.SlashCommandExecuted += HandleSlashCommandAsync;
    }

    public async Task RunAsync()
    {
        await _client.LoginAsync(TokenType.Bot, _settings.BotToken);
        await _client.StartAsync();
        Console.WriteLine("GuildSync bot started. Press Ctrl+C to stop.");
        await Task.Delay(Timeout.InfiniteTimeSpan);
    }

    private async Task RegisterCommandsAsync()
    {
        if (Interlocked.Exchange(ref _commandsRegistered, 1) == 1) return;

        var commands = BuildCommands();
        if (_settings.TestGuildId is ulong guildId)
        {
            var guild = _client.GetGuild(guildId)
                        ?? throw new InvalidOperationException($"Discord test guild {guildId} was not found. Add the bot to that server first.");
            foreach (var command in commands)
                await guild.CreateApplicationCommandAsync(command);
            Console.WriteLine($"Registered {commands.Length} GuildSync commands in test guild {guildId}.");
            return;
        }

        foreach (var command in commands)
            await _client.CreateGlobalApplicationCommandAsync(command);
        Console.WriteLine($"Registered {commands.Length} global GuildSync commands.");
    }

    private async Task HandleSlashCommandAsync(SocketSlashCommand command)
    {
        await command.DeferAsync(ephemeral: true);
        try
        {
            switch (command.Data.Name)
            {
                case "guildsync":
                case "help":
                    await ReplyAsync(command,
                        "**GuildSync** — backup, clone and protect Discord communities.\n\n" +
                        "`/plans` View available licenses\n" +
                        "`/link` Generate a one-time account link code\n" +
                        "`/license` View your linked GuildSync license\n" +
                        "`/buy` Open a secure checkout\n" +
                        "`/unlink` Disconnect your GuildSync account");
                    break;

                case "plans":
                    await ShowPlansAsync(command);
                    break;

                case "link":
                    await CreateLinkCodeAsync(command);
                    break;

                case "license":
                    await ShowLicenseAsync(command);
                    break;

                case "buy":
                    await CreateCheckoutAsync(command);
                    break;

                case "unlink":
                    await UnlinkAsync(command);
                    break;

                default:
                    await ReplyAsync(command, "Unknown GuildSync command. Use `/help`.");
                    break;
            }
        }
        catch (Exception ex)
        {
            await ReplyAsync(command, "GuildSync could not complete this request. " + SafeMessage(ex.Message));
        }
    }

    private async Task ShowPlansAsync(SocketSlashCommand command)
    {
        var response = await _api.GetPlansAsync();
        if (response.Plans.Count == 0)
        {
            await ReplyAsync(command, "No GuildSync paid plan is currently available.");
            return;
        }

        var lines = response.Plans.Select(plan =>
            $"• **{plan.Name}** — {FormatPrice(plan.Price, plan.Currency)} (`{plan.Code}`)");
        var environment = response.Environment.Equals("test", StringComparison.OrdinalIgnoreCase)
            ? "Test environment — no real charge should be completed."
            : "Production environment — checkout creates a real payment.";
        await ReplyAsync(command, "**GuildSync plans**\n" + string.Join("\n", lines) + "\n\n" + environment);
    }

    private async Task CreateLinkCodeAsync(SocketSlashCommand command)
    {
        var result = await _api.CreateLinkCodeAsync(command.User.Id);
        await ReplyAsync(command,
            $"Your one-time GuildSync code is **`{result.Code}`**.\n" +
            "Open GuildSync → **Settings → Connect Discord**, enter the code, and confirm.\n" +
            $"The code expires <t:{result.ExpiresAt.ToUnixTimeSeconds()}:R>. Never share it with anyone.");
    }

    private async Task ShowLicenseAsync(SocketSlashCommand command)
    {
        var license = await _api.GetLicenseAsync(command.User.Id);
        var expiration = license.ExpiresAt is null
            ? "No expiration date"
            : $"<t:{license.ExpiresAt.Value.ToUnixTimeSeconds()}:F>";
        await ReplyAsync(command,
            $"**GuildSync license**\n" +
            $"Account: **{license.AccountName}**\n" +
            $"Status: **{license.Status}**\n" +
            $"Plan: **{license.License}**\n" +
            $"Expiration: {expiration}\n" +
            $"Device limit: {license.DeviceLimit}");
    }

    private async Task CreateCheckoutAsync(SocketSlashCommand command)
    {
        var plan = command.Data.Options.FirstOrDefault(option => option.Name == "plan")?.Value?.ToString() ?? "1m";
        var checkout = await _api.CreateCheckoutAsync(command.User.Id, plan);
        var component = new ComponentBuilder()
            .WithButton("Open secure checkout", style: ButtonStyle.Link, url: checkout.CheckoutUrl)
            .Build();

        await command.ModifyOriginalResponseAsync(message =>
        {
            message.Content =
                $"**{checkout.PlanName}** — {FormatPrice(checkout.Amount, checkout.Currency)}\n" +
                "The license is activated only after GuildSync confirms the approved payment.";
            message.Components = component;
        });
    }

    private async Task UnlinkAsync(SocketSlashCommand command)
    {
        await _api.UnlinkAsync(command.User.Id);
        await ReplyAsync(command, "Your Discord account was disconnected from GuildSync.");
    }

    private static Task ReplyAsync(SocketSlashCommand command, string content) =>
        command.ModifyOriginalResponseAsync(message => message.Content = content);

    private static string SafeMessage(string raw) =>
        raw.Length > 300 ? raw[..300] : raw;

    private static string FormatPrice(decimal price, string currency)
    {
        if (currency.Equals("BRL", StringComparison.OrdinalIgnoreCase))
            return price.ToString("C", CultureInfo.GetCultureInfo("pt-BR"));
        if (currency.Equals("USD", StringComparison.OrdinalIgnoreCase))
            return price.ToString("C", CultureInfo.GetCultureInfo("en-US"));
        return $"{price:0.00} {currency}";
    }

    private static SlashCommandProperties[] BuildCommands()
    {
        var planOption = new SlashCommandOptionBuilder()
            .WithName("plan")
            .WithDescription("GuildSync license plan")
            .WithType(ApplicationCommandOptionType.String)
            .WithRequired(true)
            .AddChoice("1 month", "1m")
            .AddChoice("3 months", "3m")
            .AddChoice("6 months", "6m")
            .AddChoice("12 months", "12m")
            .AddChoice("Permanent", "permanent");

        return
        [
            new SlashCommandBuilder().WithName("guildsync").WithDescription("Open the GuildSync command guide").Build(),
            new SlashCommandBuilder().WithName("help").WithDescription("Show GuildSync commands").Build(),
            new SlashCommandBuilder().WithName("plans").WithDescription("View available GuildSync license plans").Build(),
            new SlashCommandBuilder().WithName("link").WithDescription("Generate a one-time GuildSync account link code").Build(),
            new SlashCommandBuilder().WithName("license").WithDescription("View your linked GuildSync license").Build(),
            new SlashCommandBuilder().WithName("buy").WithDescription("Create a secure GuildSync checkout").AddOption(planOption).Build(),
            new SlashCommandBuilder().WithName("unlink").WithDescription("Disconnect your Discord account from GuildSync").Build()
        ];
    }

    private static Task LogAsync(LogMessage message)
    {
        Console.WriteLine($"[{DateTimeOffset.Now:O}] [{message.Severity}] {message.Source}: {message.Message} {message.Exception}");
        return Task.CompletedTask;
    }
}

sealed class GuildSyncApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;

    public GuildSyncApiClient(string apiUrl, string apiKey)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(apiUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(35)
        };
        _http.DefaultRequestHeaders.Add("X-GuildSync-Bot-Key", apiKey);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("GuildSync-Bot/0.1");
    }

    public async Task<PlansResponse> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("payments/plans", cancellationToken);
        return await ReadAsync<PlansResponse>(response, cancellationToken);
    }

    public async Task<LinkCodeResponse> CreateLinkCodeAsync(ulong discordUserId, CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsJsonAsync(
            "discord/bot/link-code",
            new { discordUserId = discordUserId.ToString(CultureInfo.InvariantCulture) },
            JsonOptions,
            cancellationToken);
        return await ReadAsync<LinkCodeResponse>(response, cancellationToken);
    }

    public async Task<LicenseResponse> GetLicenseAsync(ulong discordUserId, CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(
            "discord/bot/license/" + discordUserId.ToString(CultureInfo.InvariantCulture),
            cancellationToken);
        return await ReadAsync<LicenseResponse>(response, cancellationToken);
    }

    public async Task<BotCheckoutResponse> CreateCheckoutAsync(ulong discordUserId, string plan, CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsJsonAsync(
            "discord/bot/checkout",
            new { discordUserId = discordUserId.ToString(CultureInfo.InvariantCulture), plan },
            JsonOptions,
            cancellationToken);
        return await ReadAsync<BotCheckoutResponse>(response, cancellationToken);
    }

    public async Task UnlinkAsync(ulong discordUserId, CancellationToken cancellationToken = default)
    {
        using var response = await _http.DeleteAsync(
            "discord/bot/link/" + discordUserId.ToString(CultureInfo.InvariantCulture),
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        return JsonSerializer.Deserialize<T>(await response.Content.ReadAsStringAsync(cancellationToken), JsonOptions)
               ?? throw new InvalidOperationException("GuildSync API returned an empty response.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            var error = JsonSerializer.Deserialize<ApiError>(raw, JsonOptions)?.Error;
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? $"GuildSync API returned HTTP {(int)response.StatusCode}."
                : error);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException($"GuildSync API returned HTTP {(int)response.StatusCode}.");
        }
    }
}

sealed record BotSettings(string BotToken, string ApiUrl, string ApiKey, ulong? TestGuildId)
{
    public static BotSettings FromEnvironment()
    {
        var testGuildRaw = Environment.GetEnvironmentVariable("DISCORD_TEST_GUILD_ID");
        var testGuildId = ulong.TryParse(testGuildRaw, out var parsed) && parsed > 0 ? parsed : (ulong?)null;
        return new BotSettings(
            Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN")?.Trim() ?? string.Empty,
            Environment.GetEnvironmentVariable("GUILDSYNC_API_URL")?.Trim() ?? "https://clonar-dc-api.onrender.com",
            Environment.GetEnvironmentVariable("GUILDSYNC_BOT_API_KEY")?.Trim() ?? string.Empty,
            testGuildId);
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(BotToken))
            throw new InvalidOperationException("DISCORD_BOT_TOKEN is required.");
        if (!Uri.TryCreate(ApiUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("GUILDSYNC_API_URL must be a valid HTTPS URL.");
        if (ApiKey.Length < 32)
            throw new InvalidOperationException("GUILDSYNC_BOT_API_KEY must contain at least 32 characters.");
    }
}

sealed record ApiError(string? Error);
sealed record LinkCodeResponse(string Code, DateTimeOffset ExpiresAt);
sealed record LicenseResponse(bool Linked, string DiscordUserId, string AccountName, string Status, string License, DateTimeOffset? ExpiresAt, int DeviceLimit, DateTimeOffset LinkedAt);
sealed record BotCheckoutResponse(string OrderId, string CheckoutUrl, string Plan, string PlanName, decimal Amount, string Currency, string Environment);
sealed record PlansResponse(bool CheckoutConfigured, bool WebhookConfigured, string Environment, List<PlanItem> Plans);
sealed record PlanItem(string Code, string Name, decimal Price, string Currency);
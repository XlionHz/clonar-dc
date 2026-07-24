using System.Net.Http.Json;
using Discord;
using Discord.WebSocket;

var token = Environment.GetEnvironmentVariable("GUILDSYNC_DISCORD_BOT_TOKEN");
var apiUrl = (Environment.GetEnvironmentVariable("GUILDSYNC_API_URL") ?? "https://clonar-dc-api.onrender.com").TrimEnd('/');
var testGuildRaw = Environment.GetEnvironmentVariable("GUILDSYNC_DISCORD_TEST_GUILD_ID");

if (string.IsNullOrWhiteSpace(token))
    throw new InvalidOperationException("GUILDSYNC_DISCORD_BOT_TOKEN is required.");

using var http = new HttpClient { BaseAddress = new Uri(apiUrl + "/"), Timeout = TimeSpan.FromSeconds(20) };
var client = new DiscordSocketClient(new DiscordSocketConfig
{
    GatewayIntents = GatewayIntents.Guilds,
    AlwaysDownloadUsers = false,
    LogGatewayIntentWarnings = true
});

client.Log += message =>
{
    Console.WriteLine($"[{DateTimeOffset.Now:O}] {message.Severity}: {message.Message} {message.Exception}");
    return Task.CompletedTask;
};

client.Ready += async () =>
{
    var commands = BuildCommands();
    if (ulong.TryParse(testGuildRaw, out var guildId))
    {
        await client.Rest.BulkOverwriteGuildCommands(commands, guildId);
        Console.WriteLine($"GuildSync commands registered in test guild {guildId}.");
    }
    else
    {
        await client.Rest.BulkOverwriteGlobalCommands(commands);
        Console.WriteLine("GuildSync commands registered globally.");
    }
};

client.SlashCommandExecuted += async command =>
{
    try
    {
        switch (command.Data.Name)
        {
            case "help":
                await command.RespondAsync(
                    "**GuildSync** — backup, clone and sync Discord communities.\n" +
                    "`/plans` lists available plans.\n" +
                    "`/link` will connect your Discord account to GuildSync.\n" +
                    "`/license` will show your linked license.\n" +
                    "`/buy` will open a secure checkout.", ephemeral: true);
                break;

            case "plans":
                var plans = await http.GetFromJsonAsync<PlansResponse>("payments/plans");
                if (plans is null || plans.Plans.Count == 0)
                {
                    await command.RespondAsync("No GuildSync plans are available right now.", ephemeral: true);
                    break;
                }

                var lines = plans.Plans.Select(p => $"**{p.Name}** — {p.Price:0.00} {p.Currency}");
                await command.RespondAsync(string.Join("\n", lines), ephemeral: true);
                break;

            case "link":
                await command.RespondAsync(
                    "Account linking is being connected to the central GuildSync API. " +
                    "The final flow will generate a one-time code in the desktop app and confirm it here without exposing passwords.",
                    ephemeral: true);
                break;

            case "license":
                await command.RespondAsync(
                    "Your Discord account is not linked yet. Use `/link` after the account-linking endpoint is enabled.",
                    ephemeral: true);
                break;

            case "buy":
                await command.RespondAsync(
                    "Secure checkout through Discord will be enabled after account linking is completed. " +
                    "For now, purchases are available inside the GuildSync desktop app.",
                    ephemeral: true);
                break;

            default:
                await command.RespondAsync("Unknown GuildSync command.", ephemeral: true);
                break;
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex);
        if (!command.HasResponded)
            await command.RespondAsync("GuildSync could not complete that request. Try again shortly.", ephemeral: true);
    }
};

await client.LoginAsync(TokenType.Bot, token);
await client.StartAsync();
await Task.Delay(Timeout.Infinite);

static ApplicationCommandProperties[] BuildCommands() =>
[
    new SlashCommandBuilder().WithName("help").WithDescription("Show GuildSync commands.").Build(),
    new SlashCommandBuilder().WithName("plans").WithDescription("List available GuildSync license plans.").Build(),
    new SlashCommandBuilder().WithName("link").WithDescription("Link your Discord account to GuildSync.").Build(),
    new SlashCommandBuilder().WithName("license").WithDescription("Show your GuildSync license status.").Build(),
    new SlashCommandBuilder().WithName("buy").WithDescription("Open a secure GuildSync license checkout.").Build()
];

sealed record PlansResponse(bool CheckoutConfigured, bool WebhookConfigured, string Environment, List<PlanDto> Plans);
sealed record PlanDto(string Code, string Name, decimal Price, string Currency);
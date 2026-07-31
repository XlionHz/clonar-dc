using System.Text.Json;
using ClonarDC;
using ClonarDC.Services;

var outputPath = args.Length > 0 ? Path.GetFullPath(args[0]) : null;
var checks = new List<string>();
var arbitraryValue = "  any text | symbols !@#$%^&*() | Bot prefix is not required | line\n2  ";
var storeName = $"provider-flow-{Guid.NewGuid():N}.dat";
var store = new SecureTokenStore(storeName, "GuildSync provider flow test");

try
{
    store.Save(arbitraryValue);
    var restored = store.Load();
    Require(restored == arbitraryValue, "The secure store did not preserve the exact arbitrary value.");
    checks.Add("exact arbitrary credential persistence");

    using var official = new DiscordService();
    var coordinator = new DiscordProviderCoordinator(official);
    coordinator.SetCredential(arbitraryValue);
    Require(coordinator.RawCredential == arbitraryValue, "The coordinator changed the supplied value.");
    checks.Add("no interface token classification or normalization");

    Environment.SetEnvironmentVariable("GUILDSYNC_FORCE_SIMULATION", "1");
    var load = await coordinator.LoadGuildsAsync();
    Require(load.IsSimulated, "Forced simulation did not select the simulated provider.");
    Require(load.Guilds.Count >= 4, "The simulated provider did not expose a complete server list.");
    checks.Add("complete simulated server loading");

    var source = load.Guilds[0];
    var target = load.Guilds[1];
    var analysis = coordinator.CreateSimulatedAnalysis(source, target, "merge");
    Require(analysis.SourceSnapshot.Roles.Count >= 5, "Simulated roles are incomplete.");
    Require(analysis.SourceSnapshot.Channels.Count >= 8, "Simulated channels are incomplete.");
    Require(analysis.SourceSnapshot.Emojis.Count >= 4, "Simulated emojis are incomplete.");
    Require(analysis.Plan.RolesToCreate > 0 && analysis.Plan.ChannelsToCreate > 0, "Simulated plan is incomplete.");
    checks.Add("simulated roles, channels, emojis and plan");

    var progressEvents = new List<OperationLog>();
    await coordinator.RunSimulatedOperationAsync(new InlineProgress<OperationLog>(progressEvents.Add));
    Require(progressEvents.Count >= 8, "The simulated operation did not execute every stage.");
    Require(progressEvents.Any(item => item.Level == "success"), "The simulated operation did not report completion.");
    checks.Add("end-to-end simulated operation and verification");

    Environment.SetEnvironmentVariable("GUILDSYNC_REQUIRE_OFFICIAL_PROVIDER", "true");
    var futurePolicy = new RuntimeTokenPolicy();
    Require(futurePolicy.RequireOfficialProvider, "The future runtime restriction switch cannot be activated without rebuilding.");
    checks.Add("future runtime policy switch");

    var report = new
    {
        status = "passed",
        version = "0.8.3.3",
        checks,
        simulatedGuilds = load.Guilds.Count,
        roles = analysis.SourceSnapshot.Roles.Count,
        channels = analysis.SourceSnapshot.Channels.Count,
        emojis = analysis.SourceSnapshot.Emojis.Count,
        progressEvents = progressEvents.Count
    };

    var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    Console.WriteLine(json);
    if (outputPath is not null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, json);
    }
}
finally
{
    Environment.SetEnvironmentVariable("GUILDSYNC_FORCE_SIMULATION", null);
    Environment.SetEnvironmentVariable("GUILDSYNC_REQUIRE_OFFICIAL_PROVIDER", null);
    store.Clear();
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class InlineProgress<T>(Action<T> callback) : IProgress<T>
{
    public void Report(T value) => callback(value);
}

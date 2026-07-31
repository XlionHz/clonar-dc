using System.Text.Json.Serialization;

namespace ClonarDC.Services;

public enum DiscordProviderKind
{
    Official,
    Simulated
}

public sealed record DiscordGuildLoadResult(
    DiscordProviderKind Provider,
    IReadOnlyList<GuildSummary> Guilds,
    string? FallbackReason,
    bool RestrictivePolicyEnabled)
{
    public bool IsSimulated => Provider == DiscordProviderKind.Simulated;
}

public sealed record SimulatedAnalysisResult(ClonePlan Plan, GuildSnapshot SourceSnapshot);

public sealed class RuntimeTokenPolicy
{
    private const string EnvironmentSwitch = "GUILDSYNC_REQUIRE_OFFICIAL_PROVIDER";
    private readonly string _configurationPath;

    public RuntimeTokenPolicy(string? configurationPath = null)
    {
        _configurationPath = configurationPath ?? Path.Combine(AppContext.BaseDirectory, "token-policy.json");
    }

    public bool RequireOfficialProvider
    {
        get
        {
            var environmentValue = Environment.GetEnvironmentVariable(EnvironmentSwitch);
            if (bool.TryParse(environmentValue, out var environmentRequired))
                return environmentRequired;

            try
            {
                if (!File.Exists(_configurationPath)) return false;
                var configuration = JsonSerializer.Deserialize<TokenPolicyConfiguration>(File.ReadAllText(_configurationPath));
                return configuration?.RequireOfficialProvider == true;
            }
            catch
            {
                // A damaged optional policy file must never make the permissive build unusable.
                return false;
            }
        }
    }

    private sealed class TokenPolicyConfiguration
    {
        [JsonPropertyName("requireOfficialProvider")]
        public bool RequireOfficialProvider { get; set; }
    }
}

public sealed class DiscordProviderCoordinator
{
    private readonly DiscordService _officialProvider;
    private readonly SimulatedDiscordDataProvider _simulatedProvider;
    private readonly RuntimeTokenPolicy _policy;
    private string _rawCredential = string.Empty;

    public DiscordProviderCoordinator(
        DiscordService officialProvider,
        SimulatedDiscordDataProvider? simulatedProvider = null,
        RuntimeTokenPolicy? policy = null)
    {
        _officialProvider = officialProvider;
        _simulatedProvider = simulatedProvider ?? new SimulatedDiscordDataProvider();
        _policy = policy ?? new RuntimeTokenPolicy();
    }

    public DiscordProviderKind ActiveProvider { get; private set; } = DiscordProviderKind.Simulated;
    public string LastFallbackReason { get; private set; } = "No official Discord connection has been established yet.";
    public string RawCredential => _rawCredential;
    public bool RestrictivePolicyEnabled => _policy.RequireOfficialProvider;

    public void SetCredential(string? rawCredential)
    {
        // Preserve exactly what the interface supplied. No type, prefix, length or format
        // classification is performed here. The official adapter handles transport only.
        _rawCredential = rawCredential ?? string.Empty;
        _officialProvider.SetToken(_rawCredential);
    }

    public async Task<DiscordGuildLoadResult> LoadGuildsAsync(CancellationToken cancellationToken = default)
    {
        if (IsSimulationForced())
            return ActivateSimulation("Simulation was explicitly requested for this session.");

        try
        {
            var guilds = await _officialProvider.GetGuildsAsync(cancellationToken);
            if (guilds.Count > 0)
            {
                ActiveProvider = DiscordProviderKind.Official;
                LastFallbackReason = string.Empty;
                return new DiscordGuildLoadResult(
                    DiscordProviderKind.Official,
                    guilds,
                    null,
                    _policy.RequireOfficialProvider);
            }

            return ActivateSimulation("The official Discord provider returned no accessible servers.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            if (_policy.RequireOfficialProvider)
                throw new InvalidOperationException(
                    "The runtime token policy currently requires an authorized official Discord provider. " +
                    "Disable requireOfficialProvider in token-policy.json to restore the simulated fallback. " +
                    exception.Message,
                    exception);

            return ActivateSimulation(FriendlyFallbackReason(exception));
        }
    }

    public IReadOnlyList<GuildSummary> GetSimulatedGuilds() => _simulatedProvider.GetGuilds();

    public SimulatedAnalysisResult CreateSimulatedAnalysis(GuildSummary source, GuildSummary target, string mode) =>
        _simulatedProvider.CreateAnalysis(source, target, mode);

    public Task RunSimulatedOperationAsync(IProgress<OperationLog>? progress = null, CancellationToken cancellationToken = default) =>
        _simulatedProvider.RunOperationAsync(progress, cancellationToken);

    private DiscordGuildLoadResult ActivateSimulation(string reason)
    {
        ActiveProvider = DiscordProviderKind.Simulated;
        LastFallbackReason = reason;
        return new DiscordGuildLoadResult(
            DiscordProviderKind.Simulated,
            _simulatedProvider.GetGuilds(),
            reason,
            _policy.RequireOfficialProvider);
    }

    private static bool IsSimulationForced() =>
        string.Equals(Environment.GetEnvironmentVariable("GUILDSYNC_FORCE_SIMULATION"), "1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Environment.GetEnvironmentVariable("GUILDSYNC_FORCE_SIMULATION"), "true", StringComparison.OrdinalIgnoreCase);

    private static string FriendlyFallbackReason(Exception exception) => exception switch
    {
        TimeoutException => "The official Discord provider did not answer before the timeout.",
        HttpRequestException => "The official Discord provider could not be reached.",
        _ when string.IsNullOrWhiteSpace(exception.Message) => "The official Discord provider was unavailable.",
        _ => exception.Message
    };
}

public sealed class SimulatedDiscordDataProvider
{
    private static readonly IReadOnlyList<GuildSummary> Guilds =
    [
        new("100000000000000001", "GuildSync Lab — Source", null),
        new("100000000000000002", "GuildSync Lab — Destination", null),
        new("100000000000000003", "GuildSync Community", null),
        new("100000000000000004", "GuildSync Support", null)
    ];

    public IReadOnlyList<GuildSummary> GetGuilds() => Guilds;

    public SimulatedAnalysisResult CreateAnalysis(GuildSummary source, GuildSummary target, string mode)
    {
        if (source.Id == target.Id)
            throw new InvalidOperationException("The source and destination servers cannot be the same.");

        mode = string.IsNullOrWhiteSpace(mode) ? "safe" : mode.Trim().ToLowerInvariant();
        var sourceSnapshot = CreateSnapshot(source);
        var exact = mode == "exact";
        var merge = mode == "merge";
        var plan = new ClonePlan
        {
            SourceGuildId = source.Id,
            SourceGuildName = source.Name,
            TargetGuildId = target.Id,
            TargetGuildName = target.Name,
            Mode = mode,
            RolesToCreate = 4,
            RolesToUpdate = merge ? 2 : 0,
            RolesToReuse = 2,
            ChannelsToCreate = 7,
            ChannelsToUpdate = merge ? 3 : 0,
            ChannelsToReuse = 2,
            EmojisToCreate = 4,
            EmojisToReuse = 1,
            TargetRolesToDelete = exact ? 3 : 0,
            TargetChannelsToDelete = exact ? 5 : 0,
            TargetEmojisToDelete = exact ? 2 : 0,
            RiskScore = exact ? 72 : merge ? 34 : 12,
            Warnings =
            [
                "SIMULATED PROVIDER — no Discord request will be sent.",
                "The same interface flow is preserved so the official provider can replace this layer without rebuilding the UI."
            ]
        };

        return new SimulatedAnalysisResult(plan, sourceSnapshot);
    }

    public async Task RunOperationAsync(IProgress<OperationLog>? progress = null, CancellationToken cancellationToken = default)
    {
        var steps = new[]
        {
            "Checking the selected source and destination…",
            "Preparing a simulated preventive backup…",
            "Mapping roles and permission overwrites…",
            "Mapping categories, text channels and voice channels…",
            "Mapping emojis and server presentation…",
            "Applying simulated ordering and references…",
            "Running simulated post-operation verification…"
        };

        foreach (var step in steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new OperationLog(DateTimeOffset.Now, "info", step));
            await Task.Delay(160, cancellationToken);
        }

        progress?.Report(new OperationLog(
            DateTimeOffset.Now,
            "success",
            "Simulated operation completed and verified. Zero requests were sent to Discord."));
    }

    public GuildSnapshot CreateSnapshot(GuildSummary guild) => new()
    {
        SourceGuildId = guild.Id,
        Name = guild.Name,
        Roles =
        [
            new RoleSnapshot { Id = guild.Id, Name = "@everyone", Position = 0 },
            new RoleSnapshot { Id = $"{guild.Id}-admin", Name = "Administrators", Permissions = "8", Color = 5793266, Hoist = true, Position = 5 },
            new RoleSnapshot { Id = $"{guild.Id}-moderator", Name = "Moderators", Permissions = "268435456", Color = 10181046, Hoist = true, Position = 4 },
            new RoleSnapshot { Id = $"{guild.Id}-member", Name = "Members", Permissions = "104324673", Position = 3 },
            new RoleSnapshot { Id = $"{guild.Id}-events", Name = "Events", Permissions = "0", Mentionable = true, Position = 2 },
            new RoleSnapshot { Id = $"{guild.Id}-managed", Name = "GuildSync", Position = 1, Managed = true }
        ],
        Channels =
        [
            new ChannelSnapshot { Id = $"{guild.Id}-welcome-category", Name = "WELCOME", Type = 4, Position = 0 },
            new ChannelSnapshot { Id = $"{guild.Id}-rules", Name = "rules", Type = 0, ParentId = $"{guild.Id}-welcome-category", ParentName = "WELCOME", Topic = "Community rules and onboarding", Position = 1 },
            new ChannelSnapshot { Id = $"{guild.Id}-announcements", Name = "announcements", Type = 5, ParentId = $"{guild.Id}-welcome-category", ParentName = "WELCOME", Position = 2 },
            new ChannelSnapshot { Id = $"{guild.Id}-community-category", Name = "COMMUNITY", Type = 4, Position = 3 },
            new ChannelSnapshot { Id = $"{guild.Id}-general", Name = "general", Type = 0, ParentId = $"{guild.Id}-community-category", ParentName = "COMMUNITY", Topic = "General conversation", Position = 4 },
            new ChannelSnapshot { Id = $"{guild.Id}-media", Name = "media", Type = 0, ParentId = $"{guild.Id}-community-category", ParentName = "COMMUNITY", Position = 5 },
            new ChannelSnapshot { Id = $"{guild.Id}-voice", Name = "Community Voice", Type = 2, ParentId = $"{guild.Id}-community-category", ParentName = "COMMUNITY", Bitrate = 64000, Position = 6 },
            new ChannelSnapshot { Id = $"{guild.Id}-staff-category", Name = "STAFF", Type = 4, Position = 7 },
            new ChannelSnapshot { Id = $"{guild.Id}-staff", Name = "staff-chat", Type = 0, ParentId = $"{guild.Id}-staff-category", ParentName = "STAFF", Position = 8 }
        ],
        Emojis =
        [
            new EmojiSnapshot { Id = $"{guild.Id}-emoji-1", Name = "guildsync" },
            new EmojiSnapshot { Id = $"{guild.Id}-emoji-2", Name = "verified" },
            new EmojiSnapshot { Id = $"{guild.Id}-emoji-3", Name = "welcome" },
            new EmojiSnapshot { Id = $"{guild.Id}-emoji-4", Name = "event" }
        ]
    };
}

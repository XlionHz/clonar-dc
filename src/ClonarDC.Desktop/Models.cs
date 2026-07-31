using System.Text.Json.Serialization;

namespace ClonarDC;

public sealed record AppSession(string Email, string DisplayName, string Role, string AccessToken, LicenseInfo License)
{
    public bool IsAdmin => string.Equals(Role, "admin", StringComparison.OrdinalIgnoreCase);
}

public sealed record LicenseInfo(string Status, DateTimeOffset? ExpiresAt, int DeviceLimit)
{
    public static LicenseInfo Local => new("development", null, 1);
}

public sealed record GuildSummary(string Id, string Name, string? Icon)
{
    public override string ToString() => $"{Name}  •  {Id}";
}

public sealed class GuildSnapshot
{
    public int SchemaVersion { get; set; } = 2;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string SourceGuildId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? IconData { get; set; }
    public List<RoleSnapshot> Roles { get; set; } = [];
    public List<ChannelSnapshot> Channels { get; set; } = [];
    public List<EmojiSnapshot> Emojis { get; set; } = [];
}

public sealed class RoleSnapshot
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Permissions { get; set; } = "0";
    public int Color { get; set; }
    public bool Hoist { get; set; }
    public bool Mentionable { get; set; }
    public bool Managed { get; set; }
    public int Position { get; set; }
}

public sealed class ChannelSnapshot
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Type { get; set; }
    public string? ParentId { get; set; }
    public string? ParentName { get; set; }
    public int Position { get; set; }
    public string? Topic { get; set; }
    public bool Nsfw { get; set; }
    public int? Bitrate { get; set; }
    public int? UserLimit { get; set; }
    public int? RateLimitPerUser { get; set; }
    public List<PermissionOverwriteSnapshot> PermissionOverwrites { get; set; } = [];
}

public sealed class PermissionOverwriteSnapshot
{
    public string Id { get; set; } = string.Empty;
    public int Type { get; set; }
    public string Allow { get; set; } = "0";
    public string Deny { get; set; } = "0";
}

public sealed class EmojiSnapshot
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Animated { get; set; }
    public string? ImageData { get; set; }
}

public sealed class ClonePlan
{
    public string Id { get; set; } = "plan_" + Guid.NewGuid().ToString("N");
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string SourceGuildId { get; set; } = string.Empty;
    public string SourceGuildName { get; set; } = string.Empty;
    public string TargetGuildId { get; set; } = string.Empty;
    public string TargetGuildName { get; set; } = string.Empty;
    public string Mode { get; set; } = "safe";
    public int RolesToCreate { get; set; }
    public int RolesToUpdate { get; set; }
    public int RolesToReuse { get; set; }
    public int ChannelsToCreate { get; set; }
    public int ChannelsToUpdate { get; set; }
    public int ChannelsToReuse { get; set; }
    public int EmojisToCreate { get; set; }
    public int EmojisToReuse { get; set; }
    public int TargetRolesToDelete { get; set; }
    public int TargetChannelsToDelete { get; set; }
    public int TargetEmojisToDelete { get; set; }
    public int UnsupportedMemberOverwrites { get; set; }
    public int RiskScore { get; set; }
    public List<string> BlockingIssues { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public bool IsDestructive => TargetRolesToDelete > 0 || TargetChannelsToDelete > 0 || TargetEmojisToDelete > 0;
    public bool CanExecute => BlockingIssues.Count == 0;
}

public sealed record OperationLog(DateTimeOffset Time, string Level, string Message);

public sealed class CloneExecutionOptions
{
    public string Mode { get; set; } = "safe";
    public bool ContinueOnError { get; set; } = true;
    public bool VerifyAfterExecution { get; set; } = true;
    public bool ReorderResources { get; set; } = true;
}

public sealed class CloneOperationStep
{
    public string Key { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public string? SourceId { get; set; }
    public string? TargetId { get; set; }
    public string? Message { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class CloneVerificationReport
{
    public bool Passed { get; set; }
    public int MissingRoles { get; set; }
    public int MissingChannels { get; set; }
    public int MissingEmojis { get; set; }
    public List<string> Differences { get; set; } = [];
}

public sealed class CloneExecutionReport
{
    public int FormatVersion { get; set; } = 1;
    public string Id { get; set; } = "op_" + Guid.NewGuid().ToString("N");
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public string SourceGuildId { get; set; } = string.Empty;
    public string SourceGuildName { get; set; } = string.Empty;
    public string TargetGuildId { get; set; } = string.Empty;
    public string TargetGuildName { get; set; } = string.Empty;
    public string Mode { get; set; } = "safe";
    public string Status { get; set; } = "running";
    public List<CloneOperationStep> Steps { get; set; } = [];
    public Dictionary<string, string> RoleMap { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> ChannelMap { get; set; } = new(StringComparer.Ordinal);
    public List<string> Warnings { get; set; } = [];
    public List<string> Errors { get; set; } = [];
    public CloneVerificationReport? Verification { get; set; }

    [JsonIgnore]
    public int CompletedSteps => Steps.Count(step => step.Status is "completed" or "reused" or "skipped");

    [JsonIgnore]
    public int FailedSteps => Steps.Count(step => step.Status == "failed");

    [JsonIgnore]
    public bool CanResume => Status is "failed" or "cancelled" or "completed-with-errors";
}

public sealed class BackupEnvelope
{
    public int FormatVersion { get; set; } = 2;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string AppVersion { get; set; } = "0.8.3.3";
    public string Name { get; set; } = "Backup";
    public string? Description { get; set; }
    public List<string> Tags { get; set; } = [];
    public string SnapshotEntry { get; set; } = "snapshot.json";
    public string PayloadSha256 { get; set; } = string.Empty;
    public long PayloadBytes { get; set; }
    public string Protection { get; set; } = "integrity-only";
    public GuildSnapshot Snapshot { get; set; } = new();
}

public sealed record AdminUserDto(string Id, string Email, string Name, string Status, string License, DateTimeOffset? ExpiresAt, DateTimeOffset? LastAccess, long UsageSeconds, int Devices);
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
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string SourceGuildId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? IconData { get; set; }
    public List<RoleSnapshot> Roles { get; set; } = [];
    public List<ChannelSnapshot> Channels { get; set; } = [];
    public List<EmojiSnapshot> Emojis { get; set; } = [];
}

public sealed class RoleSnapshot
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Permissions { get; set; } = "0";
    public int Color { get; set; }
    public bool Hoist { get; set; }
    public bool Mentionable { get; set; }
    public bool Managed { get; set; }
    public int Position { get; set; }
}

public sealed class ChannelSnapshot
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Type { get; set; }
    public string? ParentId { get; set; }
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
    public string Id { get; set; } = "";
    public int Type { get; set; }
    public string Allow { get; set; } = "0";
    public string Deny { get; set; } = "0";
}

public sealed class EmojiSnapshot
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Animated { get; set; }
    public string? ImageData { get; set; }
}

public sealed class ClonePlan
{
    public string SourceGuildId { get; set; } = "";
    public string TargetGuildId { get; set; } = "";
    public string Mode { get; set; } = "safe";
    public int RolesToCreate { get; set; }
    public int ChannelsToCreate { get; set; }
    public int EmojisToCreate { get; set; }
    public int TargetRolesToDelete { get; set; }
    public int TargetChannelsToDelete { get; set; }
    public List<string> Warnings { get; set; } = [];
    public bool IsDestructive => TargetRolesToDelete > 0 || TargetChannelsToDelete > 0;
}

public sealed record OperationLog(DateTimeOffset Time, string Level, string Message);

public sealed class BackupEnvelope
{
    public int FormatVersion { get; set; } = 1;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Backup";
    public string? Description { get; set; }
    public List<string> Tags { get; set; } = [];
    public string PayloadSha256 { get; set; } = "";
    public GuildSnapshot Snapshot { get; set; } = new();
}

public sealed record AdminUserDto(string Id, string Email, string Name, string Status, string License, DateTimeOffset? ExpiresAt, DateTimeOffset? LastAccess, long UsageSeconds, int Devices);

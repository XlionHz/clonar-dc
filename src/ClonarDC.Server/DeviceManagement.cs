using System.Security.Cryptography;
using System.Text;

static class DeviceManagementEndpoints
{
    public static void MapDeviceManagementEndpoints(this WebApplication app, JsonStore store)
    {
        app.MapPost("/devices/claim", async (DeviceClaimRequest request, HttpContext context) =>
        {
            var user = await AuthenticateAsync(context, store);
            if (user is null) return Results.Unauthorized();
            var result = await store.ClaimDeviceAsync(user.Id, request.DeviceId, request.DeviceName);
            return result.Ok
                ? Results.Ok(new
                {
                    deviceId = result.Record!.Id,
                    name = result.Record.Name,
                    activeDevices = result.ActiveDevices,
                    deviceLimit = result.DeviceLimit,
                    registeredAt = result.Record.FirstSeenAt,
                    lastSeenAt = result.Record.LastSeenAt
                })
                : Results.Json(new { error = result.Error }, statusCode: StatusCodes.Status409Conflict);
        });

        app.MapGet("/devices", async (HttpContext context) =>
        {
            var user = await AuthenticateAsync(context, store);
            if (user is null) return Results.Unauthorized();
            var devices = await store.ListDevicesAsync(user.Id);
            return Results.Ok(devices.Select(device => new
            {
                id = device.Id,
                name = device.Name,
                firstSeenAt = device.FirstSeenAt,
                lastSeenAt = device.LastSeenAt,
                revokedAt = device.RevokedAt,
                active = device.RevokedAt is null
            }));
        });

        app.MapDelete("/devices/{deviceId}", async (string deviceId, HttpContext context) =>
        {
            var user = await AuthenticateAsync(context, store);
            if (user is null) return Results.Unauthorized();
            var result = await store.RevokeDeviceAsync(user.Id, deviceId, user.Id);
            return result.Ok ? Results.Ok(new { revoked = true }) : Results.NotFound(new { error = result.Error });
        });

        app.MapPost("/admin/users/{userId}/devices/reset", async (string userId, HttpContext context) =>
        {
            var admin = await AuthenticateAsync(context, store);
            if (admin is null || !string.Equals(admin.Role, "admin", StringComparison.OrdinalIgnoreCase))
                return Results.Unauthorized();
            var result = await store.ResetDevicesAsync(userId, admin.Id);
            return result.Ok ? Results.Ok(new { reset = true }) : Results.NotFound(new { error = result.Error });
        });
    }

    private static async Task<UserRecord?> AuthenticateAsync(HttpContext context, JsonStore store)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        var token = header[7..].Trim();
        return token.Length == 96 ? await store.FindBySessionAsync(token) : null;
    }
}

record DeviceClaimRequest(string? DeviceId, string? DeviceName);
record DeviceClaimResult(bool Ok, string? Error, DeviceRecord? Record, int ActiveDevices, int DeviceLimit);

sealed class DeviceRecord
{
    public string Id { get; set; } = "dev_" + Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string DeviceIdHash { get; set; } = string.Empty;
    public string Name { get; set; } = "Windows device";
    public DateTimeOffset FirstSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevokedAt { get; set; }
}

sealed partial class Database
{
    public List<DeviceRecord> Devices { get; set; } = [];
}

sealed partial class JsonStore
{
    public async Task<DeviceClaimResult> ClaimDeviceAsync(string userId, string? rawDeviceId, string? deviceName)
    {
        await _gate.WaitAsync();
        try
        {
            _db.Devices ??= [];
            var user = _db.Users.FirstOrDefault(item => item.Id == userId);
            if (user is null) return new(false, "Conta GuildSync não encontrada.", null, 0, 0);
            var result = ClaimDeviceUnsafe(user, rawDeviceId, deviceName, DateTimeOffset.UtcNow);
            if (result.Ok) await SaveUnsafeAsync();
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<List<DeviceRecord>> ListDevicesAsync(string userId)
    {
        await _gate.WaitAsync();
        try
        {
            _db.Devices ??= [];
            return _db.Devices
                .Where(item => item.UserId == userId)
                .OrderByDescending(item => item.LastSeenAt)
                .Select(CloneDevice)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OpResult> RevokeDeviceAsync(string userId, string deviceRecordId, string actorId)
    {
        await _gate.WaitAsync();
        try
        {
            _db.Devices ??= [];
            var device = _db.Devices.FirstOrDefault(item => item.UserId == userId && item.Id == deviceRecordId && item.RevokedAt is null);
            if (device is null) return new(false, "Dispositivo ativo não encontrado.");
            device.RevokedAt = DateTimeOffset.UtcNow;
            _db.Sessions.RemoveAll(session => session.UserId == userId && session.DeviceIdHash == device.DeviceIdHash);
            UpdateDeviceCountUnsafe(userId);
            _db.Audit.Add(new(DateTimeOffset.UtcNow, actorId, "device-revoked", userId, device.Id));
            await SaveUnsafeAsync();
            return new(true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OpResult> ResetDevicesAsync(string userId, string actorId)
    {
        await _gate.WaitAsync();
        try
        {
            _db.Devices ??= [];
            var user = _db.Users.FirstOrDefault(item => item.Id == userId);
            if (user is null) return new(false, "Usuário não encontrado.");
            var now = DateTimeOffset.UtcNow;
            foreach (var device in _db.Devices.Where(item => item.UserId == userId && item.RevokedAt is null))
                device.RevokedAt = now;
            _db.Sessions.RemoveAll(session => session.UserId == userId);
            user.DeviceCount = 0;
            _db.Audit.Add(new(now, actorId, "devices-reset", userId, string.Empty));
            await SaveUnsafeAsync();
            return new(true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private DeviceClaimResult ClaimDeviceUnsafe(UserRecord user, string? rawDeviceId, string? deviceName, DateTimeOffset now)
    {
        _db.Devices ??= [];
        var normalizedId = NormalizeDeviceId(rawDeviceId);
        if (normalizedId is null)
            return new(false, "A identidade deste dispositivo é inválida. Reinicie o GuildSync e tente novamente.", null, ActiveDeviceCountUnsafe(user.Id), user.DeviceLimit);

        var hash = HashDeviceId(normalizedId);
        var existing = _db.Devices.FirstOrDefault(item => item.UserId == user.Id && FixedTimeDeviceHashEquals(item.DeviceIdHash, hash));
        var activeCount = ActiveDeviceCountUnsafe(user.Id);
        if (existing is not null && existing.RevokedAt is null)
        {
            existing.Name = NormalizeDeviceName(deviceName);
            existing.LastSeenAt = now;
            user.DeviceCount = activeCount;
            return new(true, null, CloneDevice(existing), activeCount, user.DeviceLimit);
        }

        if (activeCount >= Math.Max(1, user.DeviceLimit))
            return new(false, $"O limite de {Math.Max(1, user.DeviceLimit)} dispositivo(s) desta licença foi atingido. Remova um dispositivo antigo ou contacte o suporte.", null, activeCount, user.DeviceLimit);

        var record = existing ?? new DeviceRecord
        {
            UserId = user.Id,
            DeviceIdHash = hash,
            FirstSeenAt = now
        };
        record.Name = NormalizeDeviceName(deviceName);
        record.LastSeenAt = now;
        record.RevokedAt = null;
        if (existing is null) _db.Devices.Add(record);
        activeCount++;
        user.DeviceCount = activeCount;
        _db.Audit.Add(new(now, user.Id, "device-claimed", user.Id, record.Id));
        return new(true, null, CloneDevice(record), activeCount, user.DeviceLimit);
    }

    private void TouchDeviceUnsafe(string userId, string? deviceIdHash, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(deviceIdHash)) return;
        _db.Devices ??= [];
        var device = _db.Devices.FirstOrDefault(item => item.UserId == userId && item.RevokedAt is null && FixedTimeDeviceHashEquals(item.DeviceIdHash, deviceIdHash));
        if (device is not null) device.LastSeenAt = now;
    }

    private void ResetDevicesUnsafe(string userId)
    {
        _db.Devices ??= [];
        var now = DateTimeOffset.UtcNow;
        foreach (var device in _db.Devices.Where(item => item.UserId == userId && item.RevokedAt is null))
            device.RevokedAt = now;
        _db.Sessions.RemoveAll(session => session.UserId == userId);
        UpdateDeviceCountUnsafe(userId);
    }

    private int ActiveDeviceCountUnsafe(string userId) =>
        _db.Devices.Count(item => item.UserId == userId && item.RevokedAt is null);

    private void UpdateDeviceCountUnsafe(string userId)
    {
        var user = _db.Users.FirstOrDefault(item => item.Id == userId);
        if (user is not null) user.DeviceCount = ActiveDeviceCountUnsafe(userId);
    }

    private static string? NormalizeDeviceId(string? value)
    {
        value = value?.Trim().ToUpperInvariant();
        return value is { Length: 64 } && value.All(char.IsAsciiHexDigit) ? value : null;
    }

    private static string NormalizeDeviceName(string? value)
    {
        var normalized = new string((value ?? string.Empty).Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(normalized)) normalized = "Windows device";
        return normalized[..Math.Min(normalized.Length, 100)];
    }

    private static string HashDeviceId(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool FixedTimeDeviceHashEquals(string left, string right)
    {
        var leftBytes = Encoding.ASCII.GetBytes(left);
        var rightBytes = Encoding.ASCII.GetBytes(right);
        return leftBytes.Length == rightBytes.Length && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static DeviceRecord CloneDevice(DeviceRecord device) =>
        JsonSerializer.Deserialize<DeviceRecord>(JsonSerializer.Serialize(device))!;
}
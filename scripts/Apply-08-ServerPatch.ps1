$ErrorActionPreference = 'Stop'
$path = Join-Path $PSScriptRoot '../src/ClonarDC.Server/Program.cs'
$content = Get-Content $path -Raw

function Replace-Required([string]$old, [string]$new) {
    if ($script:content.Contains($new)) { return }
    if (-not $script:content.Contains($old)) {
        throw "Required Program.cs block was not found:`n$old"
    }
    $script:content = $script:content.Replace($old, $new)
}

Replace-Required 'const string DefaultVersion = "0.7.1";' 'const string DefaultVersion = "0.8.0";'

Replace-Required @'
    discordIntegrationConfigured = !string.IsNullOrWhiteSpace(discordBotApiKey)
'@ @'
    discordIntegrationConfigured = !string.IsNullOrWhiteSpace(discordBotApiKey),
    deviceIdentityRequired = DevicePolicy.RequireIdentity
'@

Replace-Required @'
    var result = await store.LoginAsync(normalizedEmail, request.Password ?? string.Empty);
'@ @'
    var result = await store.LoginAsync(
        normalizedEmail,
        request.Password ?? string.Empty,
        request.DeviceId,
        request.DeviceName);
'@

Replace-Required @'
            deviceLimit = result.User.DeviceLimit
'@ @'
            deviceLimit = result.User.DeviceLimit,
            deviceCount = result.User.DeviceCount
'@

Replace-Required @'
        deviceLimit = user.DeviceLimit
'@ @'
        deviceLimit = user.DeviceLimit,
        deviceCount = user.DeviceCount
'@

Replace-Required @'
app.MapMercadoPagoEndpoints(store, mercadoPago, paymentOptions);
'@ @'
app.MapDeviceManagementEndpoints(store);
app.MapMercadoPagoEndpoints(store, mercadoPago, paymentOptions);
'@

Replace-Required 'record LoginRequest(string? Email, string? Password);' 'record LoginRequest(string? Email, string? Password, string? DeviceId, string? DeviceName);'

Replace-Required @'
    public string UserId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
'@ @'
    public string UserId { get; set; } = string.Empty;
    public string DeviceIdHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
'@

Replace-Required @'
            _db.Audit ??= [];
            _db.Payments ??= [];
'@ @'
            _db.Audit ??= [];
            _db.Devices ??= [];
            _db.Payments ??= [];
'@

Replace-Required @'
    public async Task<LoginResult> LoginAsync(string email, string password)
'@ @'
    public async Task<LoginResult> LoginAsync(string email, string password, string? deviceId, string? deviceName)
'@

Replace-Required @'
            user.LastAccess = now;
            _db.Sessions.RemoveAll(session => session.UserId == user.Id && session.ExpiresAt <= now);
'@ @'
            DeviceClaimResult? deviceResult = null;
            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                deviceResult = ClaimDeviceUnsafe(user, deviceId, deviceName, now);
                if (!deviceResult.Ok)
                    return new(false, deviceResult.Error, null, null);
            }
            else if (DevicePolicy.RequireIdentity)
            {
                return new(false, "Atualize o GuildSync para registrar este dispositivo.", null, null);
            }

            user.LastAccess = now;
            _db.Sessions.RemoveAll(session => session.UserId == user.Id && session.ExpiresAt <= now);
'@

Replace-Required @'
                UserId = user.Id,
                TokenHash = HashToken(token),
'@ @'
                UserId = user.Id,
                DeviceIdHash = deviceResult?.Record?.DeviceIdHash ?? string.Empty,
                TokenHash = HashToken(token),
'@

Replace-Required @'
            session.LastSeenAt = now;
            return CloneUser(user);
'@ @'
            session.LastSeenAt = now;
            TouchDeviceUnsafe(user.Id, session.DeviceIdHash, now);
            return CloneUser(user);
'@

Replace-Required @'
                case "reset-devices":
                    user.DeviceCount = 0;
                    break;
'@ @'
                case "reset-devices":
                    ResetDevicesUnsafe(user.Id);
                    break;
'@

Set-Content $path $content -Encoding UTF8
Write-Host 'GuildSync 0.8 server integration patch applied.'
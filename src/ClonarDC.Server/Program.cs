using System.Collections.Concurrent;
using System.Net.Mail;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

const string ProductName = "GuildSync API";
const string DefaultVersion = "0.8.0";

var builder = WebApplication.CreateBuilder(args);
var environmentName = Environment.GetEnvironmentVariable("GUILDSYNC_ENV")
                      ?? Environment.GetEnvironmentVariable("CLONARDC_ENV")
                      ?? builder.Environment.EnvironmentName;
var isProduction = string.Equals(environmentName, "production", StringComparison.OrdinalIgnoreCase);

var listenAddress = Environment.GetEnvironmentVariable("GUILDSYNC_LISTEN")
                    ?? Environment.GetEnvironmentVariable("CLONARDC_LISTEN");
if (string.IsNullOrWhiteSpace(listenAddress))
{
    var port = Environment.GetEnvironmentVariable("PORT");
    listenAddress = string.IsNullOrWhiteSpace(port)
        ? "http://127.0.0.1:8787"
        : $"http://0.0.0.0:{port}";
}

builder.WebHost.UseUrls(listenAddress);
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 1_048_576;
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
    options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(75);
});

var app = builder.Build();

var dataRoot = Environment.GetEnvironmentVariable("GUILDSYNC_DATA")
               ?? Environment.GetEnvironmentVariable("CLONARDC_DATA")
               ?? Path.Combine(AppContext.BaseDirectory, "data");
var persistence = StatePersistenceFactory.Create(dataRoot);
var store = new JsonStore(persistence);
await store.InitializeAsync();
await store.EnsureBootstrapAdminAsync(
    Environment.GetEnvironmentVariable("GUILDSYNC_ADMIN_EMAIL")
    ?? Environment.GetEnvironmentVariable("CLONARDC_ADMIN_EMAIL"),
    Environment.GetEnvironmentVariable("GUILDSYNC_ADMIN_PASSWORD")
    ?? Environment.GetEnvironmentVariable("CLONARDC_ADMIN_PASSWORD"));

var paymentOptions = MercadoPagoOptions.FromEnvironment();
var mercadoPago = new MercadoPagoClient(paymentOptions);
var discordBotApiKey = Environment.GetEnvironmentVariable("GUILDSYNC_BOT_API_KEY")?.Trim() ?? string.Empty;
var loginAttempts = new SlidingWindowLimiter(12, TimeSpan.FromMinutes(10));
var registrationAttempts = new SlidingWindowLimiter(6, TimeSpan.FromMinutes(30));

app.Use(async (context, next) =>
{
    var requestId = context.TraceIdentifier;
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["X-Permitted-Cross-Domain-Policies"] = "none";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; frame-ancestors 'none'; base-uri 'none'; form-action 'self' https://www.mercadopago.com https://www.mercadopago.com.br";
    context.Response.Headers["Cache-Control"] = "no-store, max-age=0";
    context.Response.Headers["Pragma"] = "no-cache";
    context.Response.Headers["X-Request-Id"] = requestId;

    try
    {
        await next();
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
        // The client disconnected. Do not turn this into an application error.
    }
    catch (Exception exception)
    {
        app.Logger.LogError(exception, "Unhandled request failure. RequestId={RequestId}", requestId);
        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "GuildSync could not complete the request.",
                requestId
            });
        }
    }
});

if (isProduction)
    app.UseHsts();

var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
var version = assemblyVersion is null
    ? DefaultVersion
    : $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{Math.Max(assemblyVersion.Build, 0)}";

app.MapGet("/", () => Results.Text("GuildSync API online", "text/plain; charset=utf-8"));
app.MapGet("/status", () => Results.Ok(new
{
    service = ProductName,
    version,
    environment = environmentName,
    utc = DateTimeOffset.UtcNow,
    storage = persistence.Kind,
    paymentsConfigured = paymentOptions.IsCheckoutConfigured,
    webhookConfigured = paymentOptions.IsWebhookConfigured,
    discordIntegrationConfigured = !string.IsNullOrWhiteSpace(discordBotApiKey),
    deviceIdentityRequired = DevicePolicy.RequireIdentity
}));

app.MapPost("/auth/register", async (RegisterRequest request, HttpContext context) =>
{
    var rateKey = $"register:{ClientKey(context)}";
    if (!registrationAttempts.TryAcquire(rateKey))
        return Results.Json(new { error = "Muitas tentativas de cadastro. Aguarde e tente novamente." }, statusCode: StatusCodes.Status429TooManyRequests);

    var validationError = InputValidation.ValidateRegistration(request);
    if (validationError is not null)
        return Results.BadRequest(new { error = validationError });

    var result = await store.RegisterAsync(request.Name!.Trim(), request.Email!.Trim(), request.Password!);
    return result.Ok
        ? Results.Ok(new
        {
            status = "pending",
            message = "Conta GuildSync criada. Escolha uma licença para ativar os recursos protegidos."
        })
        : Results.Conflict(new { error = result.Error });
});

app.MapPost("/auth/login", async (LoginRequest request, HttpContext context) =>
{
    var normalizedEmail = JsonStore.NormalizeEmail(request.Email ?? string.Empty);
    var rateKey = $"login:{ClientKey(context)}:{normalizedEmail}";
    if (!loginAttempts.TryAcquire(rateKey))
        return Results.Json(new { error = "Muitas tentativas. Aguarde alguns minutos." }, statusCode: StatusCodes.Status429TooManyRequests);

    var result = await store.LoginAsync(
        normalizedEmail,
        request.Password ?? string.Empty,
        request.DeviceId,
        request.DeviceName);
    if (!result.Ok)
        return Results.Json(new { error = result.Error }, statusCode: StatusCodes.Status401Unauthorized);

    loginAttempts.Reset(rateKey);
    return Results.Ok(new
    {
        accessToken = result.Token,
        user = new
        {
            id = result.User!.Id,
            email = result.User.Email,
            name = result.User.Name,
            role = result.User.Role
        },
        license = new
        {
            status = result.User.Status,
            expiresAt = result.User.ExpiresAt,
            deviceLimit = result.User.DeviceLimit,
            deviceCount = result.User.DeviceCount
        }
    });
});

app.MapPost("/auth/logout", async (HttpContext context) =>
{
    var token = ReadBearerToken(context);
    if (token is null) return Results.Unauthorized();
    await store.RevokeSessionAsync(token);
    return Results.Ok(new { loggedOut = true });
});

app.MapPost("/auth/logout-all", async (HttpContext context) =>
{
    var user = await AuthenticateAsync(context, store);
    if (user is null) return Results.Unauthorized();
    await store.RevokeAllSessionsAsync(user.Id);
    return Results.Ok(new { loggedOut = true, allSessions = true });
});

app.MapGet("/me", async (HttpContext context) =>
{
    var user = await AuthenticateAsync(context, store);
    if (user is null) return Results.Unauthorized();
    return Results.Ok(new
    {
        id = user.Id,
        email = user.Email,
        name = user.Name,
        role = user.Role,
        status = user.Status,
        expiresAt = user.ExpiresAt,
        deviceLimit = user.DeviceLimit,
        deviceCount = user.DeviceCount
    });
});

app.MapGet("/admin/users", async (HttpContext context) =>
{
    var admin = await RequireAdminAsync(context, store);
    if (admin is null) return Results.Unauthorized();
    var users = await store.ListUsersAsync();
    return Results.Ok(users.Select(user => new
    {
        id = user.Id,
        email = user.Email,
        name = user.Name,
        status = user.Status,
        license = user.LicenseLabel,
        expiresAt = user.ExpiresAt,
        lastAccess = user.LastAccess,
        usageSeconds = user.UsageSeconds,
        devices = user.DeviceCount,
        deviceLimit = user.DeviceLimit,
        deviceCount = user.DeviceCount,
        createdAt = user.CreatedAt
    }));
});

app.MapPost("/admin/users/{id}/{action}", async (string id, string action, AdminActionRequest? request, HttpContext context) =>
{
    var admin = await RequireAdminAsync(context, store);
    if (admin is null) return Results.Unauthorized();
    var result = await store.AdminActionAsync(admin.Id, id, action, request?.License);
    return result.Ok ? Results.Ok(new { ok = true }) : Results.BadRequest(new { error = result.Error });
});

app.MapGet("/admin/audit", async (HttpContext context) =>
{
    var admin = await RequireAdminAsync(context, store);
    return admin is null ? Results.Unauthorized() : Results.Ok(await store.ListAuditAsync());
});

app.MapDeviceManagementEndpoints(store);
app.MapMercadoPagoEndpoints(store, mercadoPago, paymentOptions);
app.MapDiscordIntegrationEndpoints(store, mercadoPago, paymentOptions, discordBotApiKey);
app.Run();

static string ClientKey(HttpContext context)
{
    var forwarded = context.Request.Headers["X-Forwarded-For"].ToString().Split(',').FirstOrDefault()?.Trim();
    var value = !string.IsNullOrWhiteSpace(forwarded)
        ? forwarded
        : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24];
}

static string? ReadBearerToken(HttpContext context)
{
    var header = context.Request.Headers.Authorization.ToString();
    if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
    var token = header[7..].Trim();
    return token.Length == 96 ? token : null;
}

static async Task<UserRecord?> AuthenticateAsync(HttpContext context, JsonStore store)
{
    var token = ReadBearerToken(context);
    return token is null ? null : await store.FindBySessionAsync(token);
}

static async Task<UserRecord?> RequireAdminAsync(HttpContext context, JsonStore store)
{
    var user = await AuthenticateAsync(context, store);
    return user is not null && string.Equals(user.Role, "admin", StringComparison.OrdinalIgnoreCase) ? user : null;
}

record RegisterRequest(string? Name, string? Email, string? Password);
record LoginRequest(string? Email, string? Password, string? DeviceId, string? DeviceName);
record AdminActionRequest(string? License);
record OpResult(bool Ok, string? Error = null);
record LoginResult(bool Ok, string? Error, string? Token, UserRecord? User);

sealed partial class Database
{
    public List<UserRecord> Users { get; set; } = [];
    public List<SessionRecord> Sessions { get; set; } = [];
    public List<AuditRecord> Audit { get; set; } = [];
}

sealed class UserRecord
{
    public string Id { get; set; } = "usr_" + Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = "user";
    public string Status { get; set; } = "pending";
    public string LicenseLabel { get; set; } = "Pending";
    public DateTimeOffset? ExpiresAt { get; set; }
    public int DeviceLimit { get; set; } = 1;
    public int DeviceCount { get; set; }
    public long UsageSeconds { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastAccess { get; set; }
}

sealed class SessionRecord
{
    public string TokenHash { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string DeviceIdHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddHours(12);
}

sealed record AuditRecord(DateTimeOffset At, string ActorId, string Action, string TargetId, string Detail);

sealed partial class JsonStore
{
    private static readonly TimeSpan SessionLifetime = ReadSessionLifetime();
    private readonly IStatePersistence _persistence;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
    private Database _db = new();

    public JsonStore(IStatePersistence persistence) => _persistence = persistence;

    public async Task InitializeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var existing = await _persistence.LoadAsync(_json);
            _db = existing ?? new Database();
            _db.Users ??= [];
            _db.Sessions ??= [];
            _db.Audit ??= [];
            _db.Devices ??= [];
            _db.Payments ??= [];
            _db.DiscordLinks ??= [];
            _db.DiscordLinkCodes ??= [];
            CleanupUnsafe(DateTimeOffset.UtcNow);
            await SaveUnsafeAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task EnsureBootstrapAdminAsync(string? email, string? password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return;
        if (!InputValidation.IsValidEmail(email.Trim()) || password.Length < 14)
            throw new InvalidOperationException("The bootstrap administrator requires a valid email and a password with at least 14 characters.");

        await _gate.WaitAsync();
        try
        {
            if (_db.Users.Any(user => string.Equals(user.Role, "admin", StringComparison.OrdinalIgnoreCase))) return;
            var (salt, hash) = Passwords.Hash(password);
            _db.Users.Add(new UserRecord
            {
                Name = "Administrator",
                Email = NormalizeEmail(email),
                PasswordSalt = salt,
                PasswordHash = hash,
                Role = "admin",
                Status = "active",
                LicenseLabel = "Permanent",
                DeviceLimit = 5
            });
            _db.Audit.Add(new(DateTimeOffset.UtcNow, "system", "bootstrap-admin-created", "admin", string.Empty));
            await SaveUnsafeAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OpResult> RegisterAsync(string name, string email, string password)
    {
        email = NormalizeEmail(email);
        await _gate.WaitAsync();
        try
        {
            if (_db.Users.Any(user => string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase)))
                return new(false, "Já existe uma conta com este e-mail.");

            var (salt, hash) = Passwords.Hash(password);
            var user = new UserRecord
            {
                Name = name,
                Email = email,
                PasswordSalt = salt,
                PasswordHash = hash
            };
            _db.Users.Add(user);
            _db.Audit.Add(new(DateTimeOffset.UtcNow, user.Id, "account-created", user.Id, string.Empty));
            await SaveUnsafeAsync();
            return new(true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<LoginResult> LoginAsync(string email, string password, string? deviceId, string? deviceName)
    {
        email = NormalizeEmail(email);
        await _gate.WaitAsync();
        try
        {
            var now = DateTimeOffset.UtcNow;
            CleanupUnsafe(now);
            var user = _db.Users.FirstOrDefault(item => string.Equals(item.Email, email, StringComparison.OrdinalIgnoreCase));
            if (user is null || !Passwords.Verify(password, user.PasswordSalt, user.PasswordHash))
                return new(false, "E-mail ou senha incorretos.", null, null);

            if (string.Equals(user.Status, "suspended", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(user.Status, "revoked", StringComparison.OrdinalIgnoreCase))
                return new(false, "Esta conta não está autorizada a entrar.", null, null);

            if (string.Equals(user.Status, "active", StringComparison.OrdinalIgnoreCase) &&
                user.ExpiresAt is not null && user.ExpiresAt <= now)
            {
                user.Status = "expired";
                user.LicenseLabel = "Expired";
            }

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
            var activeSessions = _db.Sessions
                .Where(session => session.UserId == user.Id)
                .OrderByDescending(session => session.CreatedAt)
                .ToList();
            foreach (var stale in activeSessions.Skip(9))
                _db.Sessions.Remove(stale);

            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(48));
            _db.Sessions.Add(new SessionRecord
            {
                UserId = user.Id,
                DeviceIdHash = deviceResult?.Record?.DeviceIdHash ?? string.Empty,
                TokenHash = HashToken(token),
                CreatedAt = now,
                LastSeenAt = now,
                ExpiresAt = now.Add(SessionLifetime)
            });
            _db.Audit.Add(new(now, user.Id, "login", user.Id, string.Empty));
            await SaveUnsafeAsync();
            return new(true, null, token, CloneUser(user));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<UserRecord?> FindBySessionAsync(string token)
    {
        var hash = HashToken(token);
        await _gate.WaitAsync();
        try
        {
            var now = DateTimeOffset.UtcNow;
            CleanupUnsafe(now);
            var session = _db.Sessions.FirstOrDefault(item =>
                FixedTimeTextEquals(item.TokenHash, hash) && item.ExpiresAt > now);
            if (session is null) return null;

            var user = _db.Users.FirstOrDefault(item => item.Id == session.UserId);
            if (user is null ||
                string.Equals(user.Status, "suspended", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(user.Status, "revoked", StringComparison.OrdinalIgnoreCase))
            {
                _db.Sessions.RemoveAll(item => item.UserId == session.UserId);
                await SaveUnsafeAsync();
                return null;
            }

            session.LastSeenAt = now;
            TouchDeviceUnsafe(user.Id, session.DeviceIdHash, now);
            return CloneUser(user);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RevokeSessionAsync(string token)
    {
        var hash = HashToken(token);
        await _gate.WaitAsync();
        try
        {
            _db.Sessions.RemoveAll(item => FixedTimeTextEquals(item.TokenHash, hash));
            await SaveUnsafeAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RevokeAllSessionsAsync(string userId)
    {
        await _gate.WaitAsync();
        try
        {
            _db.Sessions.RemoveAll(item => item.UserId == userId);
            _db.Audit.Add(new(DateTimeOffset.UtcNow, userId, "logout-all", userId, string.Empty));
            await SaveUnsafeAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<List<UserRecord>> ListUsersAsync()
    {
        await _gate.WaitAsync();
        try
        {
            return _db.Users.OrderByDescending(user => user.CreatedAt).Select(CloneUser).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<List<AuditRecord>> ListAuditAsync()
    {
        await _gate.WaitAsync();
        try
        {
            return _db.Audit.OrderByDescending(item => item.At).Take(1000).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OpResult> AdminActionAsync(string actorId, string targetId, string action, string? license)
    {
        await _gate.WaitAsync();
        try
        {
            var user = _db.Users.FirstOrDefault(item => item.Id == targetId);
            if (user is null) return new(false, "Usuário não encontrado.");

            var normalizedAction = action.Trim().ToLowerInvariant();
            if (actorId == targetId && normalizedAction is "suspend" or "revoke")
                return new(false, "Um administrador não pode bloquear a própria sessão por esta ação.");

            switch (normalizedAction)
            {
                case "approve":
                case "renew":
                    ApplyLicense(user, license ?? "1m");
                    user.Status = "active";
                    break;
                case "suspend":
                    user.Status = "suspended";
                    _db.Sessions.RemoveAll(session => session.UserId == user.Id);
                    break;
                case "reactivate":
                    user.Status = user.ExpiresAt is not null && user.ExpiresAt <= DateTimeOffset.UtcNow
                        ? "expired"
                        : "active";
                    break;
                case "revoke":
                    user.Status = "revoked";
                    _db.Sessions.RemoveAll(session => session.UserId == user.Id);
                    break;
                case "reset-devices":
                    ResetDevicesUnsafe(user.Id);
                    break;
                default:
                    return new(false, "Ação administrativa desconhecida.");
            }

            _db.Audit.Add(new(DateTimeOffset.UtcNow, actorId, normalizedAction, targetId, license ?? string.Empty));
            await SaveUnsafeAsync();
            return new(true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void ApplyLicense(UserRecord user, string code)
    {
        var now = user.ExpiresAt is not null && user.ExpiresAt > DateTimeOffset.UtcNow
            ? user.ExpiresAt.Value
            : DateTimeOffset.UtcNow;

        switch (code.Trim().ToLowerInvariant())
        {
            case "permanent":
                user.ExpiresAt = null;
                user.LicenseLabel = "Permanent";
                break;
            case "3m":
                user.ExpiresAt = now.AddMonths(3);
                user.LicenseLabel = "3 months";
                break;
            case "6m":
                user.ExpiresAt = now.AddMonths(6);
                user.LicenseLabel = "6 months";
                break;
            case "12m":
                user.ExpiresAt = now.AddMonths(12);
                user.LicenseLabel = "12 months";
                break;
            default:
                user.ExpiresAt = now.AddMonths(1);
                user.LicenseLabel = "1 month";
                break;
        }
    }

    private void CleanupUnsafe(DateTimeOffset now)
    {
        _db.Sessions.RemoveAll(session => session.ExpiresAt <= now);
        _db.DiscordLinkCodes.RemoveAll(code => code.ExpiresAt <= now);
        foreach (var user in _db.Users.Where(item =>
                     string.Equals(item.Status, "active", StringComparison.OrdinalIgnoreCase) &&
                     item.ExpiresAt is not null && item.ExpiresAt <= now))
        {
            user.Status = "expired";
            user.LicenseLabel = "Expired";
        }
    }

    private Task SaveUnsafeAsync() => _persistence.SaveAsync(_db, _json);

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static bool FixedTimeTextEquals(string left, string right)
    {
        var leftBytes = Encoding.ASCII.GetBytes(left);
        var rightBytes = Encoding.ASCII.GetBytes(right);
        return leftBytes.Length == rightBytes.Length && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static TimeSpan ReadSessionLifetime()
    {
        var raw = Environment.GetEnvironmentVariable("GUILDSYNC_SESSION_HOURS");
        return int.TryParse(raw, out var hours) && hours is >= 1 and <= 168
            ? TimeSpan.FromHours(hours)
            : TimeSpan.FromHours(12);
    }

    private static UserRecord CloneUser(UserRecord user) =>
        JsonSerializer.Deserialize<UserRecord>(JsonSerializer.Serialize(user))!;
}

sealed class SlidingWindowLimiter
{
    private readonly int _limit;
    private readonly TimeSpan _window;
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _attempts = new(StringComparer.Ordinal);

    public SlidingWindowLimiter(int limit, TimeSpan window)
    {
        _limit = limit;
        _window = window;
    }

    public bool TryAcquire(string key)
    {
        var now = DateTimeOffset.UtcNow;
        var queue = _attempts.GetOrAdd(key, _ => new Queue<DateTimeOffset>());
        lock (queue)
        {
            while (queue.Count > 0 && now - queue.Peek() > _window)
                queue.Dequeue();
            if (queue.Count >= _limit) return false;
            queue.Enqueue(now);
            return true;
        }
    }

    public void Reset(string key) => _attempts.TryRemove(key, out _);
}

static class InputValidation
{
    public static string? ValidateRegistration(RegisterRequest request)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        var email = request.Email?.Trim() ?? string.Empty;
        var password = request.Password ?? string.Empty;

        if (name.Length is < 2 or > 80)
            return "O nome deve ter entre 2 e 80 caracteres.";
        if (email.Length is < 3 or > 254 || !IsValidEmail(email))
            return "Informe um endereço de e-mail válido.";
        if (password.Length is < 10 or > 256)
            return "A senha deve ter entre 10 e 256 caracteres.";
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            return "A senha deve conter letras e números.";
        return null;
    }

    public static bool IsValidEmail(string email)
    {
        try
        {
            var parsed = new MailAddress(email);
            return string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}

static class Passwords
{
    private const int Iterations = 210_000;

    public static (string Salt, string Hash) Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(24);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return (Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public static bool Verify(string password, string saltText, string hashText)
    {
        try
        {
            var salt = Convert.FromBase64String(saltText);
            var expected = Convert.FromBase64String(hashText);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }
}





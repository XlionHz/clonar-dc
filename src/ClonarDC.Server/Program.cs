using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var listenAddress = Environment.GetEnvironmentVariable("CLONARDC_LISTEN");
if (string.IsNullOrWhiteSpace(listenAddress))
{
    var port = Environment.GetEnvironmentVariable("PORT");
    listenAddress = string.IsNullOrWhiteSpace(port)
        ? "http://127.0.0.1:8787"
        : $"http://0.0.0.0:{port}";
}
builder.WebHost.UseUrls(listenAddress);
var app = builder.Build();

var dataRoot = Environment.GetEnvironmentVariable("CLONARDC_DATA") ?? Path.Combine(AppContext.BaseDirectory, "data");
var persistence = StatePersistenceFactory.Create(dataRoot);
var store = new JsonStore(persistence);
await store.InitializeAsync();
await store.EnsureBootstrapAdminAsync(
    Environment.GetEnvironmentVariable("CLONARDC_ADMIN_EMAIL"),
    Environment.GetEnvironmentVariable("CLONARDC_ADMIN_PASSWORD"));

var paymentOptions = MercadoPagoOptions.FromEnvironment();
var mercadoPago = new MercadoPagoClient(paymentOptions);
var attempts = new ConcurrentDictionary<string, Queue<DateTimeOffset>>(StringComparer.OrdinalIgnoreCase);

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    ctx.Response.Headers["Cache-Control"] = "no-store";
    await next();
});

app.MapGet("/", () => Results.Text("Clonar DC API online", "text/plain; charset=utf-8"));
app.MapGet("/status", () => Results.Ok(new
{
    service = "Clonar DC API",
    version = "0.4.0",
    utc = DateTimeOffset.UtcNow,
    storage = persistence.Kind,
    paymentsConfigured = paymentOptions.IsCheckoutConfigured,
    webhookConfigured = paymentOptions.IsWebhookConfigured
}));

app.MapPost("/auth/register", async (RegisterRequest req, HttpContext ctx) =>
{
    if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Email) || req.Password?.Length < 8)
        return Results.BadRequest(new { error = "Nome, e-mail e senha com pelo menos 8 caracteres são obrigatórios." });
    var result = await store.RegisterAsync(req.Name.Trim(), req.Email.Trim(), req.Password!);
    return result.Ok ? Results.Ok(new { status = "pending", message = "Esperando autorização" }) : Results.Conflict(new { error = result.Error });
});

app.MapPost("/auth/login", async (LoginRequest req, HttpContext ctx) =>
{
    var key = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    if (!AllowAttempt(attempts, key)) return Results.Json(new { error = "Muitas tentativas. Aguarde alguns minutos." }, statusCode: 429);
    var result = await store.LoginAsync(req.Email?.Trim() ?? "", req.Password ?? "");
    if (!result.Ok) return Results.Json(new { error = result.Error }, statusCode: 401);
    return Results.Ok(new
    {
        accessToken = result.Token,
        user = new { id = result.User!.Id, email = result.User.Email, name = result.User.Name, role = result.User.Role },
        license = new { status = result.User.Status, expiresAt = result.User.ExpiresAt, deviceLimit = result.User.DeviceLimit }
    });
});

app.MapGet("/me", async (HttpContext ctx) =>
{
    var auth = await AuthenticateAsync(ctx, store);
    if (auth is null) return Results.Unauthorized();
    return Results.Ok(new { id = auth.Id, email = auth.Email, name = auth.Name, role = auth.Role, status = auth.Status, expiresAt = auth.ExpiresAt, deviceLimit = auth.DeviceLimit });
});

app.MapGet("/admin/users", async (HttpContext ctx) =>
{
    var admin = await RequireAdminAsync(ctx, store);
    if (admin is null) return Results.Unauthorized();
    var users = await store.ListUsersAsync();
    return Results.Ok(users.Select(u => new
    {
        id = u.Id,
        email = u.Email,
        name = u.Name,
        status = u.Status,
        license = u.LicenseLabel,
        expiresAt = u.ExpiresAt,
        lastAccess = u.LastAccess,
        usageSeconds = u.UsageSeconds,
        devices = u.DeviceCount
    }));
});

app.MapPost("/admin/users/{id}/{action}", async (string id, string action, AdminActionRequest? req, HttpContext ctx) =>
{
    var admin = await RequireAdminAsync(ctx, store);
    if (admin is null) return Results.Unauthorized();
    var result = await store.AdminActionAsync(admin.Id, id, action, req?.License);
    return result.Ok ? Results.Ok(new { ok = true }) : Results.BadRequest(new { error = result.Error });
});

app.MapGet("/admin/audit", async (HttpContext ctx) =>
{
    var admin = await RequireAdminAsync(ctx, store);
    return admin is null ? Results.Unauthorized() : Results.Ok(await store.ListAuditAsync());
});

app.MapMercadoPagoEndpoints(store, mercadoPago, paymentOptions);
app.Run();

static bool AllowAttempt(ConcurrentDictionary<string, Queue<DateTimeOffset>> map, string key)
{
    var now = DateTimeOffset.UtcNow;
    var queue = map.GetOrAdd(key, _ => new Queue<DateTimeOffset>());
    lock (queue)
    {
        while (queue.Count > 0 && now - queue.Peek() > TimeSpan.FromMinutes(10)) queue.Dequeue();
        if (queue.Count >= 12) return false;
        queue.Enqueue(now);
        return true;
    }
}

static async Task<UserRecord?> AuthenticateAsync(HttpContext ctx, JsonStore store)
{
    var header = ctx.Request.Headers.Authorization.ToString();
    if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
    return await store.FindBySessionAsync(header[7..].Trim());
}

static async Task<UserRecord?> RequireAdminAsync(HttpContext ctx, JsonStore store)
{
    var user = await AuthenticateAsync(ctx, store);
    return user is not null && string.Equals(user.Role, "admin", StringComparison.OrdinalIgnoreCase) ? user : null;
}

record RegisterRequest(string? Name, string? Email, string? Password);
record LoginRequest(string? Email, string? Password);
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
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordSalt { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "user";
    public string Status { get; set; } = "pending";
    public string LicenseLabel { get; set; } = "Pendente";
    public DateTimeOffset? ExpiresAt { get; set; }
    public int DeviceLimit { get; set; } = 1;
    public int DeviceCount { get; set; }
    public long UsageSeconds { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastAccess { get; set; }
}

sealed class SessionRecord
{
    public string TokenHash { get; set; } = "";
    public string UserId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddHours(12);
}

sealed record AuditRecord(DateTimeOffset At, string ActorId, string Action, string TargetId, string Detail);

sealed partial class JsonStore
{
    private readonly IStatePersistence _persistence;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private Database _db = new();

    public JsonStore(IStatePersistence persistence) => _persistence = persistence;

    public async Task InitializeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var existing = await _persistence.LoadAsync(_json);
            if (existing is null)
            {
                _db = new Database();
                await SaveUnsafeAsync();
            }
            else
            {
                _db = existing;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task EnsureBootstrapAdminAsync(string? email, string? password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password) || password.Length < 12) return;
        await _gate.WaitAsync();
        try
        {
            if (_db.Users.Any(u => u.Role == "admin")) return;
            var (salt, hash) = Passwords.Hash(password);
            _db.Users.Add(new UserRecord
            {
                Name = "Administrador",
                Email = NormalizeEmail(email),
                PasswordSalt = salt,
                PasswordHash = hash,
                Role = "admin",
                Status = "active",
                LicenseLabel = "Permanente",
                DeviceLimit = 5
            });
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
            if (_db.Users.Any(u => u.Email == email)) return new(false, "Já existe uma conta com este e-mail.");
            var (salt, hash) = Passwords.Hash(password);
            _db.Users.Add(new UserRecord { Name = name, Email = email, PasswordSalt = salt, PasswordHash = hash });
            await SaveUnsafeAsync();
            return new(true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<LoginResult> LoginAsync(string email, string password)
    {
        email = NormalizeEmail(email);
        await _gate.WaitAsync();
        try
        {
            var user = _db.Users.FirstOrDefault(u => u.Email == email);
            if (user is null || !Passwords.Verify(password, user.PasswordSalt, user.PasswordHash))
                return new(false, "E-mail ou senha incorretos.", null, null);

            if (user.Status == "active" && user.ExpiresAt is not null && user.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                user.Status = "expired";
                user.LicenseLabel = "Expirada";
            }

            user.LastAccess = DateTimeOffset.UtcNow;
            _db.Sessions.RemoveAll(s => s.ExpiresAt <= DateTimeOffset.UtcNow);
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(48));
            _db.Sessions.Add(new SessionRecord { UserId = user.Id, TokenHash = HashToken(token) });
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
            var session = _db.Sessions.FirstOrDefault(s => s.TokenHash == hash && s.ExpiresAt > DateTimeOffset.UtcNow);
            if (session is null) return null;
            var user = _db.Users.FirstOrDefault(u => u.Id == session.UserId);
            return user is null ? null : CloneUser(user);
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
            return _db.Users.OrderByDescending(u => u.CreatedAt).Select(CloneUser).ToList();
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
            return _db.Audit.OrderByDescending(a => a.At).Take(1000).ToList();
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
            var user = _db.Users.FirstOrDefault(u => u.Id == targetId);
            if (user is null) return new(false, "Usuário não encontrado.");
            switch (action.ToLowerInvariant())
            {
                case "approve":
                case "renew":
                    ApplyLicense(user, license ?? "1m");
                    user.Status = "active";
                    break;
                case "suspend":
                    user.Status = "suspended";
                    break;
                case "reactivate":
                    user.Status = user.ExpiresAt is not null && user.ExpiresAt <= DateTimeOffset.UtcNow ? "expired" : "active";
                    break;
                case "revoke":
                    user.Status = "revoked";
                    _db.Sessions.RemoveAll(s => s.UserId == user.Id);
                    break;
                case "reset-devices":
                    user.DeviceCount = 0;
                    break;
                default:
                    return new(false, "Ação administrativa desconhecida.");
            }
            _db.Audit.Add(new(DateTimeOffset.UtcNow, actorId, action, targetId, license ?? ""));
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
        switch (code.ToLowerInvariant())
        {
            case "permanent":
                user.ExpiresAt = null;
                user.LicenseLabel = "Permanente";
                break;
            case "3m":
                user.ExpiresAt = now.AddMonths(3);
                user.LicenseLabel = "3 meses";
                break;
            case "6m":
                user.ExpiresAt = now.AddMonths(6);
                user.LicenseLabel = "6 meses";
                break;
            case "12m":
                user.ExpiresAt = now.AddMonths(12);
                user.LicenseLabel = "12 meses";
                break;
            default:
                user.ExpiresAt = now.AddMonths(1);
                user.LicenseLabel = "1 mês";
                break;
        }
    }

    private Task SaveUnsafeAsync() => _persistence.SaveAsync(_db, _json);
    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static UserRecord CloneUser(UserRecord user) => JsonSerializer.Deserialize<UserRecord>(JsonSerializer.Serialize(user))!;
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
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

static class DiscordIntegrationEndpoints
{
    private const string BotKeyHeader = "X-GuildSync-Bot-Key";

    public static void MapDiscordIntegrationEndpoints(
        this WebApplication app,
        JsonStore store,
        MercadoPagoClient mercadoPago,
        MercadoPagoOptions paymentOptions,
        string botApiKey)
    {
        app.MapPost("/discord/link/claim", async (DiscordLinkClaimRequest request, HttpContext context) =>
        {
            var user = await AuthenticateUserAsync(context, store);
            if (user is null) return Results.Unauthorized();

            var result = await store.ClaimDiscordLinkCodeAsync(user.Id, request.Code ?? string.Empty);
            return result.Ok
                ? Results.Ok(new { linked = true, discordUserId = result.DiscordUserId, linkedAt = result.LinkedAt })
                : Results.BadRequest(new { error = result.Error });
        });

        app.MapGet("/discord/link/status", async (HttpContext context) =>
        {
            var user = await AuthenticateUserAsync(context, store);
            if (user is null) return Results.Unauthorized();
            var link = await store.GetDiscordLinkByUserAsync(user.Id);
            return Results.Ok(link is null
                ? new { linked = false, discordUserId = (string?)null, linkedAt = (DateTimeOffset?)null }
                : new { linked = true, discordUserId = (string?)link.DiscordUserId, linkedAt = (DateTimeOffset?)link.LinkedAt });
        });

        app.MapDelete("/discord/link", async (HttpContext context) =>
        {
            var user = await AuthenticateUserAsync(context, store);
            if (user is null) return Results.Unauthorized();
            await store.UnlinkDiscordByUserAsync(user.Id, user.Id);
            return Results.Ok(new { linked = false });
        });

        app.MapPost("/discord/bot/link-code", async (DiscordBotUserRequest request, HttpContext context) =>
        {
            var authorization = AuthorizeBot(context, botApiKey);
            if (authorization is not null) return authorization;

            var result = await store.CreateDiscordLinkCodeAsync(request.DiscordUserId ?? string.Empty);
            return result.Ok
                ? Results.Ok(new { code = result.Code, expiresAt = result.ExpiresAt })
                : Results.BadRequest(new { error = result.Error });
        });

        app.MapGet("/discord/bot/license/{discordUserId}", async (string discordUserId, HttpContext context) =>
        {
            var authorization = AuthorizeBot(context, botApiKey);
            if (authorization is not null) return authorization;

            var account = await store.GetDiscordLinkedAccountAsync(discordUserId);
            return account is null
                ? Results.NotFound(new { error = "This Discord account is not linked to GuildSync." })
                : Results.Ok(new
                {
                    linked = true,
                    discordUserId,
                    accountName = account.User.Name,
                    status = account.User.Status,
                    license = account.User.LicenseLabel,
                    expiresAt = account.User.ExpiresAt,
                    deviceLimit = account.User.DeviceLimit,
                    linkedAt = account.Link.LinkedAt
                });
        });

        app.MapPost("/discord/bot/checkout", async (DiscordBotCheckoutRequest request, HttpContext context) =>
        {
            var authorization = AuthorizeBot(context, botApiKey);
            if (authorization is not null) return authorization;

            if (!paymentOptions.IsCheckoutConfigured)
                return Results.Json(new { error = "GuildSync payments are not configured." }, statusCode: 503);

            var account = await store.GetDiscordLinkedAccountAsync(request.DiscordUserId ?? string.Empty);
            if (account is null)
                return Results.NotFound(new { error = "Use /link first and connect the generated code inside the GuildSync app." });

            var planCode = request.Plan?.Trim().ToLowerInvariant() ?? string.Empty;
            if (!paymentOptions.Plans.TryGetValue(planCode, out var plan))
                return Results.BadRequest(new { error = "Invalid or unavailable GuildSync plan." });

            var order = await store.CreatePaymentOrderAsync(account.User.Id, plan);
            try
            {
                var preference = await mercadoPago.CreatePreferenceAsync(order, account.User, plan, context.RequestAborted);
                await store.AttachPaymentPreferenceAsync(order.Id, preference.PreferenceId, preference.CheckoutUrl);
                return Results.Ok(new
                {
                    orderId = order.Id,
                    checkoutUrl = preference.CheckoutUrl,
                    plan = plan.Code,
                    planName = plan.Name,
                    amount = plan.Price,
                    currency = plan.Currency,
                    environment = paymentOptions.UseSandbox ? "test" : "production"
                });
            }
            catch (Exception)
            {
                await store.MarkPaymentOrderFailedAsync(order.Id, "discord-checkout-error");
                return Results.Json(new { error = "The GuildSync checkout could not be created." }, statusCode: 502);
            }
        });

        app.MapDelete("/discord/bot/link/{discordUserId}", async (string discordUserId, HttpContext context) =>
        {
            var authorization = AuthorizeBot(context, botApiKey);
            if (authorization is not null) return authorization;
            var removed = await store.UnlinkDiscordByDiscordAsync(discordUserId, "discord:" + discordUserId);
            return removed
                ? Results.Ok(new { linked = false })
                : Results.NotFound(new { error = "This Discord account is not linked." });
        });
    }

    private static IResult? AuthorizeBot(HttpContext context, string expectedKey)
    {
        if (string.IsNullOrWhiteSpace(expectedKey))
            return Results.Json(new { error = "The Discord bot integration is not configured." }, statusCode: 503);

        var receivedKey = context.Request.Headers[BotKeyHeader].ToString();
        if (!FixedTimeSecretEquals(expectedKey, receivedKey)) return Results.Unauthorized();
        return null;
    }

    private static bool FixedTimeSecretEquals(string expected, string received)
    {
        if (string.IsNullOrWhiteSpace(received)) return false;
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var receivedHash = SHA256.HashData(Encoding.UTF8.GetBytes(received));
        return CryptographicOperations.FixedTimeEquals(expectedHash, receivedHash);
    }

    private static async Task<UserRecord?> AuthenticateUserAsync(HttpContext context, JsonStore store)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        return await store.FindBySessionAsync(header[7..].Trim());
    }
}

sealed record DiscordLinkClaimRequest(string? Code);
sealed record DiscordBotUserRequest(string? DiscordUserId);
sealed record DiscordBotCheckoutRequest(string? DiscordUserId, string? Plan);
sealed record DiscordLinkOperationResult(bool Ok, string? Error = null, string? DiscordUserId = null, DateTimeOffset? LinkedAt = null);
sealed record DiscordLinkCodeResult(bool Ok, string? Error = null, string? Code = null, DateTimeOffset? ExpiresAt = null);
sealed record DiscordLinkedAccount(UserRecord User, DiscordAccountLinkRecord Link);

sealed class DiscordAccountLinkRecord
{
    public string DiscordUserId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public DateTimeOffset LinkedAt { get; set; } = DateTimeOffset.UtcNow;
}

sealed class DiscordLinkCodeRecord
{
    public string CodeHash { get; set; } = string.Empty;
    public string DiscordUserId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddMinutes(10);
}

sealed partial class Database
{
    public List<DiscordAccountLinkRecord> DiscordLinks { get; set; } = [];
    public List<DiscordLinkCodeRecord> DiscordLinkCodes { get; set; } = [];
}

sealed partial class JsonStore
{
    private const string LinkCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public async Task<DiscordLinkCodeResult> CreateDiscordLinkCodeAsync(string discordUserId)
    {
        discordUserId = discordUserId.Trim();
        if (!ulong.TryParse(discordUserId, out var parsed) || parsed == 0)
            return new(false, "Invalid Discord user ID.");

        await _gate.WaitAsync();
        try
        {
            var now = DateTimeOffset.UtcNow;
            _db.DiscordLinkCodes.RemoveAll(code => code.ExpiresAt <= now || code.DiscordUserId == discordUserId);
            if (_db.DiscordLinks.Any(link => link.DiscordUserId == discordUserId))
                return new(false, "This Discord account is already linked to GuildSync. Use /unlink before linking another account.");

            var codeText = CreateOneTimeCode();
            var expiresAt = now.AddMinutes(10);
            _db.DiscordLinkCodes.Add(new DiscordLinkCodeRecord
            {
                DiscordUserId = discordUserId,
                CodeHash = HashLinkCode(codeText),
                CreatedAt = now,
                ExpiresAt = expiresAt
            });
            await SaveUnsafeAsync();
            return new(true, Code: codeText, ExpiresAt: expiresAt);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<DiscordLinkOperationResult> ClaimDiscordLinkCodeAsync(string userId, string code)
    {
        var normalizedCode = NormalizeLinkCode(code);
        if (normalizedCode.Length != 8)
            return new(false, "The Discord link code is invalid or incomplete.");

        await _gate.WaitAsync();
        try
        {
            var now = DateTimeOffset.UtcNow;
            _db.DiscordLinkCodes.RemoveAll(item => item.ExpiresAt <= now);
            var codeHash = HashLinkCode(normalizedCode);
            var pending = _db.DiscordLinkCodes.FirstOrDefault(item => item.CodeHash == codeHash);
            if (pending is null)
                return new(false, "The Discord link code is invalid, expired, or has already been used.");

            var user = _db.Users.FirstOrDefault(item => item.Id == userId);
            if (user is null) return new(false, "GuildSync account not found.");

            var discordOwner = _db.DiscordLinks.FirstOrDefault(item => item.DiscordUserId == pending.DiscordUserId);
            if (discordOwner is not null && discordOwner.UserId != userId)
                return new(false, "This Discord account is already linked to another GuildSync account.");

            _db.DiscordLinks.RemoveAll(item => item.UserId == userId || item.DiscordUserId == pending.DiscordUserId);
            var link = new DiscordAccountLinkRecord
            {
                DiscordUserId = pending.DiscordUserId,
                UserId = userId,
                LinkedAt = now
            };
            _db.DiscordLinks.Add(link);
            _db.DiscordLinkCodes.RemoveAll(item => item.DiscordUserId == pending.DiscordUserId);
            _db.Audit.Add(new(now, userId, "discord-linked", pending.DiscordUserId, string.Empty));
            await SaveUnsafeAsync();
            return new(true, DiscordUserId: link.DiscordUserId, LinkedAt: link.LinkedAt);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<DiscordAccountLinkRecord?> GetDiscordLinkByUserAsync(string userId)
    {
        await _gate.WaitAsync();
        try
        {
            var link = _db.DiscordLinks.FirstOrDefault(item => item.UserId == userId);
            return link is null ? null : CloneDiscordLink(link);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<DiscordLinkedAccount?> GetDiscordLinkedAccountAsync(string discordUserId)
    {
        discordUserId = discordUserId.Trim();
        await _gate.WaitAsync();
        try
        {
            var link = _db.DiscordLinks.FirstOrDefault(item => item.DiscordUserId == discordUserId);
            if (link is null) return null;
            var user = _db.Users.FirstOrDefault(item => item.Id == link.UserId);
            return user is null ? null : new DiscordLinkedAccount(CloneUser(user), CloneDiscordLink(link));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UnlinkDiscordByUserAsync(string userId, string actorId)
    {
        await _gate.WaitAsync();
        try
        {
            var links = _db.DiscordLinks.Where(item => item.UserId == userId).ToList();
            foreach (var link in links)
                _db.Audit.Add(new(DateTimeOffset.UtcNow, actorId, "discord-unlinked", link.DiscordUserId, string.Empty));
            _db.DiscordLinks.RemoveAll(item => item.UserId == userId);
            await SaveUnsafeAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> UnlinkDiscordByDiscordAsync(string discordUserId, string actorId)
    {
        discordUserId = discordUserId.Trim();
        await _gate.WaitAsync();
        try
        {
            var removed = _db.DiscordLinks.RemoveAll(item => item.DiscordUserId == discordUserId) > 0;
            _db.DiscordLinkCodes.RemoveAll(item => item.DiscordUserId == discordUserId);
            if (removed)
            {
                _db.Audit.Add(new(DateTimeOffset.UtcNow, actorId, "discord-unlinked", discordUserId, string.Empty));
                await SaveUnsafeAsync();
            }
            return removed;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string CreateOneTimeCode()
    {
        Span<byte> random = stackalloc byte[8];
        RandomNumberGenerator.Fill(random);
        Span<char> characters = stackalloc char[8];
        for (var index = 0; index < characters.Length; index++)
            characters[index] = LinkCodeAlphabet[random[index] % LinkCodeAlphabet.Length];
        return new string(characters);
    }

    private static string NormalizeLinkCode(string code) =>
        new(code.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static string HashLinkCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeLinkCode(code))));

    private static DiscordAccountLinkRecord CloneDiscordLink(DiscordAccountLinkRecord link) =>
        JsonSerializer.Deserialize<DiscordAccountLinkRecord>(JsonSerializer.Serialize(link))!;
}
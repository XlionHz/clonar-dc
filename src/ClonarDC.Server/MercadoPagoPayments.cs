using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

static class MercadoPagoEndpoints
{
    public static void MapMercadoPagoEndpoints(
        this WebApplication app,
        JsonStore store,
        MercadoPagoClient mercadoPago,
        MercadoPagoOptions options)
    {
        app.MapGet("/payments/plans", () => Results.Ok(new
        {
            checkoutConfigured = options.IsCheckoutConfigured,
            webhookConfigured = options.IsWebhookConfigured,
            environment = options.UseSandbox ? "test" : "production",
            plans = options.Plans.Values.Select(plan => new
            {
                code = plan.Code,
                name = plan.Name,
                price = plan.Price,
                currency = plan.Currency
            })
        }));

        app.MapPost("/payments/checkout", async (CheckoutRequest request, HttpContext context) =>
        {
            var user = await AuthenticatePaymentUserAsync(context, store);
            if (user is null) return Results.Unauthorized();

            if (!options.IsCheckoutConfigured)
                return Results.Json(new { error = "GuildSync payments are not configured yet." }, statusCode: 503);

            var planCode = request.Plan?.Trim().ToLowerInvariant() ?? string.Empty;
            if (!options.Plans.TryGetValue(planCode, out var plan))
                return Results.BadRequest(new { error = "Invalid or unavailable GuildSync plan." });

            var order = await store.CreatePaymentOrderAsync(user.Id, plan);
            try
            {
                var preference = await mercadoPago.CreatePreferenceAsync(order, user, plan, context.RequestAborted);
                await store.AttachPaymentPreferenceAsync(order.Id, preference.PreferenceId, preference.CheckoutUrl);
                return Results.Ok(new
                {
                    orderId = order.Id,
                    preferenceId = preference.PreferenceId,
                    checkoutUrl = preference.CheckoutUrl,
                    environment = options.UseSandbox ? "test" : "production"
                });
            }
            catch (Exception ex)
            {
                await store.MarkPaymentOrderFailedAsync(order.Id, ex.Message);
                return Results.Json(new { error = "GuildSync could not start the payment." }, statusCode: 502);
            }
        });

        app.MapGet("/payments/orders/{orderId}", async (string orderId, HttpContext context) =>
        {
            var user = await AuthenticatePaymentUserAsync(context, store);
            if (user is null) return Results.Unauthorized();
            var order = await store.GetPaymentOrderAsync(orderId, user.Id);
            return order is null
                ? Results.NotFound(new { error = "Payment order not found." })
                : Results.Ok(new
                {
                    order.Id,
                    order.PlanCode,
                    order.PlanName,
                    order.Amount,
                    order.Currency,
                    order.Status,
                    order.CreatedAt,
                    order.UpdatedAt
                });
        });

        app.MapPost("/webhooks/mercadopago", async (HttpContext context) =>
        {
            JsonNode? body = null;
            try
            {
                body = await JsonNode.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            }
            catch
            {
                // Mercado Pago can include the resource identifier in the query string.
            }

            var dataId = FirstNonEmpty(
                context.Request.Query["data.id"].FirstOrDefault(),
                context.Request.Query["id"].FirstOrDefault(),
                body?["data"]?["id"]?.ToString());
            var topic = FirstNonEmpty(
                context.Request.Query["type"].FirstOrDefault(),
                context.Request.Query["topic"].FirstOrDefault(),
                body?["type"]?.ToString());

            if (string.IsNullOrWhiteSpace(dataId))
                return Results.BadRequest(new { error = "Notification resource ID is missing." });

            if (!options.AllowUnsignedWebhooks)
            {
                if (!options.IsWebhookConfigured)
                    return Results.Json(new { error = "The Mercado Pago webhook signature is not configured." }, statusCode: 503);

                var signatureValid = MercadoPagoWebhookSignature.IsValid(
                    context.Request.Headers["x-signature"].ToString(),
                    context.Request.Headers["x-request-id"].ToString(),
                    dataId,
                    options.WebhookSecret);
                if (!signatureValid) return Results.Unauthorized();
            }

            if (!string.Equals(topic, "payment", StringComparison.OrdinalIgnoreCase))
                return Results.Ok(new { received = true, ignored = true, topic });

            try
            {
                var payment = await mercadoPago.GetPaymentAsync(dataId, context.RequestAborted);
                var applied = await store.ApplyMercadoPagoPaymentAsync(payment);
                return applied.Ok
                    ? Results.Ok(new { received = true, status = payment.Status })
                    : Results.BadRequest(new { error = applied.Error });
            }
            catch
            {
                return Results.Json(new { error = "GuildSync could not confirm the payment with Mercado Pago." }, statusCode: 502);
            }
        });

        app.MapGet("/payments/success", () => PaymentReturnPage(
            "Payment received",
            "GuildSync is confirming the payment with Mercado Pago. Your license will be activated automatically after the approved status reaches the server."));
        app.MapGet("/payments/pending", () => PaymentReturnPage(
            "Payment pending",
            "The payment is still processing. GuildSync will activate the license automatically after approval."));
        app.MapGet("/payments/failure", () => PaymentReturnPage(
            "Payment not completed",
            "The charge was not completed. You can close this page and try again from GuildSync."));
    }

    private static async Task<UserRecord?> AuthenticatePaymentUserAsync(HttpContext context, JsonStore store)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        return await store.FindBySessionAsync(header[7..].Trim());
    }

    private static IResult PaymentReturnPage(string title, string message) => Results.Content(
        $"""
        <!doctype html>
        <html lang="en">
        <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{title} — GuildSync</title></head>
        <body style="margin:0;background:#0d0f18;color:#f5f3ff;font-family:Segoe UI,Arial,sans-serif;display:grid;min-height:100vh;place-items:center">
          <main style="max-width:620px;margin:24px;padding:32px;border:1px solid #302c46;border-radius:18px;background:#151724;box-shadow:0 24px 80px #0007">
            <h1 style="margin-top:0">{title}</h1><p style="line-height:1.6;color:#c9c5dc">{message}</p><p style="color:#8b85a6">You can now return to GuildSync.</p>
          </main>
        </body>
        </html>
        """,
        "text/html; charset=utf-8");

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}

sealed record CheckoutRequest(string? Plan);
sealed record PaymentPlan(string Code, string Name, decimal Price, string Currency = "BRL");

sealed class MercadoPagoOptions
{
    public string AccessToken { get; init; } = string.Empty;
    public string WebhookSecret { get; init; } = string.Empty;
    public string PublicUrl { get; init; } = string.Empty;
    public bool UseSandbox { get; init; }
    public bool AllowUnsignedWebhooks { get; init; }
    public IReadOnlyDictionary<string, PaymentPlan> Plans { get; init; } = new Dictionary<string, PaymentPlan>();

    public bool IsCheckoutConfigured =>
        !string.IsNullOrWhiteSpace(AccessToken) &&
        !string.IsNullOrWhiteSpace(PublicUrl) &&
        Plans.Count > 0;

    public bool IsWebhookConfigured => !string.IsNullOrWhiteSpace(WebhookSecret);

    public static MercadoPagoOptions FromEnvironment()
    {
        var environment = EnvironmentValue("GUILDSYNC_ENV", "CLONARDC_ENV");
        var sandboxSetting = Environment.GetEnvironmentVariable("MERCADOPAGO_USE_SANDBOX")?.Trim();
        var useSandbox = bool.TryParse(sandboxSetting, out var explicitSandbox)
            ? explicitSandbox
            : !string.Equals(environment, "production", StringComparison.OrdinalIgnoreCase);

        var currency = EnvironmentValue("GUILDSYNC_PAYMENT_CURRENCY")?.ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(currency)) currency = "BRL";

        var plans = new Dictionary<string, PaymentPlan>(StringComparer.OrdinalIgnoreCase);
        AddPlan(plans, "1m", "GuildSync — 1 month", currency, "GUILDSYNC_PRICE_1M", "CLONARDC_PRICE_1M");
        AddPlan(plans, "3m", "GuildSync — 3 months", currency, "GUILDSYNC_PRICE_3M", "CLONARDC_PRICE_3M");
        AddPlan(plans, "6m", "GuildSync — 6 months", currency, "GUILDSYNC_PRICE_6M", "CLONARDC_PRICE_6M");
        AddPlan(plans, "12m", "GuildSync — 12 months", currency, "GUILDSYNC_PRICE_12M", "CLONARDC_PRICE_12M");
        AddPlan(plans, "permanent", "GuildSync — Permanent", currency, "GUILDSYNC_PRICE_PERMANENT", "CLONARDC_PRICE_PERMANENT");

        var publicUrl = FirstNonEmpty(
            EnvironmentValue("GUILDSYNC_PUBLIC_URL", "CLONARDC_PUBLIC_URL"),
            Environment.GetEnvironmentVariable("RENDER_EXTERNAL_URL"))?.TrimEnd('/') ?? string.Empty;

        return new MercadoPagoOptions
        {
            AccessToken = Environment.GetEnvironmentVariable("MERCADOPAGO_ACCESS_TOKEN")?.Trim() ?? string.Empty,
            WebhookSecret = Environment.GetEnvironmentVariable("MERCADOPAGO_WEBHOOK_SECRET")?.Trim() ?? string.Empty,
            PublicUrl = publicUrl,
            UseSandbox = useSandbox,
            AllowUnsignedWebhooks = string.Equals(
                Environment.GetEnvironmentVariable("MERCADOPAGO_ALLOW_UNSIGNED_WEBHOOKS"),
                "true",
                StringComparison.OrdinalIgnoreCase),
            Plans = plans
        };
    }

    private static void AddPlan(
        IDictionary<string, PaymentPlan> plans,
        string code,
        string name,
        string currency,
        params string[] environmentKeys)
    {
        var raw = EnvironmentValue(environmentKeys);
        if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var price) || price <= 0) return;
        plans[code] = new PaymentPlan(code, name, decimal.Round(price, 2), currency);
    }

    private static string? EnvironmentValue(params string[] keys) =>
        keys.Select(Environment.GetEnvironmentVariable)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?.Trim();

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}

sealed class MercadoPagoClient
{
    private readonly MercadoPagoOptions _options;
    private readonly HttpClient _http = new()
    {
        BaseAddress = new Uri("https://api.mercadopago.com/"),
        Timeout = TimeSpan.FromSeconds(20)
    };

    public MercadoPagoClient(MercadoPagoOptions options)
    {
        _options = options;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("GuildSync-Server/0.5");
    }

    public async Task<MercadoPagoPreference> CreatePreferenceAsync(
        PaymentOrderRecord order,
        UserRecord user,
        PaymentPlan plan,
        CancellationToken cancellationToken)
    {
        EnsureAccessToken();
        if (string.IsNullOrWhiteSpace(_options.PublicUrl))
            throw new InvalidOperationException("GUILDSYNC_PUBLIC_URL is not configured.");

        var payload = new
        {
            items = new[]
            {
                new
                {
                    id = plan.Code,
                    title = plan.Name,
                    description = "GuildSync digital software license",
                    quantity = 1,
                    currency_id = plan.Currency,
                    unit_price = plan.Price
                }
            },
            payer = new { email = user.Email },
            external_reference = order.Id,
            statement_descriptor = "GUILDSYNC",
            back_urls = new
            {
                success = _options.PublicUrl + "/payments/success",
                pending = _options.PublicUrl + "/payments/pending",
                failure = _options.PublicUrl + "/payments/failure"
            },
            auto_return = "approved",
            notification_url = _options.PublicUrl + "/webhooks/mercadopago",
            metadata = new
            {
                order_id = order.Id,
                user_id = user.Id,
                plan_code = plan.Code
            }
        };

        using var request = AuthorizedRequest(HttpMethod.Post, "checkout/preferences");
        request.Content = JsonContent.Create(payload);
        using var response = await _http.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(MercadoPagoError(response.StatusCode, raw));

        var node = JsonNode.Parse(raw) ?? throw new InvalidOperationException("Mercado Pago returned an empty response.");
        var preferenceId = node["id"]?.GetValue<string>()
                           ?? throw new InvalidOperationException("Mercado Pago did not return the preference ID.");
        var checkoutUrl = (_options.UseSandbox ? node["sandbox_init_point"] : node["init_point"])?.GetValue<string>()
                          ?? node["init_point"]?.GetValue<string>()
                          ?? node["sandbox_init_point"]?.GetValue<string>()
                          ?? throw new InvalidOperationException("Mercado Pago did not return the checkout URL.");
        return new MercadoPagoPreference(preferenceId, checkoutUrl);
    }

    public async Task<MercadoPagoPayment> GetPaymentAsync(string paymentId, CancellationToken cancellationToken)
    {
        EnsureAccessToken();
        using var request = AuthorizedRequest(HttpMethod.Get, "v1/payments/" + Uri.EscapeDataString(paymentId));
        using var response = await _http.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(MercadoPagoError(response.StatusCode, raw));

        var node = JsonNode.Parse(raw) ?? throw new InvalidOperationException("Mercado Pago returned an empty response.");
        return new MercadoPagoPayment(
            node["id"]?.ToString() ?? paymentId,
            node["status"]?.GetValue<string>() ?? "unknown",
            node["status_detail"]?.GetValue<string>(),
            node["external_reference"]?.GetValue<string>(),
            node["transaction_amount"]?.GetValue<decimal>() ?? 0m,
            node["currency_id"]?.GetValue<string>() ?? string.Empty);
    }

    private HttpRequestMessage AuthorizedRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private void EnsureAccessToken()
    {
        if (string.IsNullOrWhiteSpace(_options.AccessToken))
            throw new InvalidOperationException("MERCADOPAGO_ACCESS_TOKEN is not configured on the server.");
    }

    private static string MercadoPagoError(System.Net.HttpStatusCode statusCode, string raw)
    {
        try
        {
            var node = JsonNode.Parse(raw);
            var message = node?["message"]?.ToString() ?? node?["error"]?.ToString();
            return string.IsNullOrWhiteSpace(message)
                ? $"Mercado Pago returned HTTP {(int)statusCode}."
                : $"Mercado Pago returned HTTP {(int)statusCode}: {message}";
        }
        catch
        {
            return $"Mercado Pago returned HTTP {(int)statusCode}.";
        }
    }
}

static class MercadoPagoWebhookSignature
{
    public static bool IsValid(string xSignature, string xRequestId, string dataId, string secret)
    {
        if (string.IsNullOrWhiteSpace(xSignature) ||
            string.IsNullOrWhiteSpace(xRequestId) ||
            string.IsNullOrWhiteSpace(dataId) ||
            string.IsNullOrWhiteSpace(secret)) return false;

        string? timestamp = null;
        string? receivedHash = null;
        foreach (var component in xSignature.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = component.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length != 2) continue;
            if (pair[0].Equals("ts", StringComparison.OrdinalIgnoreCase)) timestamp = pair[1];
            if (pair[0].Equals("v1", StringComparison.OrdinalIgnoreCase)) receivedHash = pair[1];
        }

        if (string.IsNullOrWhiteSpace(timestamp) || string.IsNullOrWhiteSpace(receivedHash)) return false;
        var manifest = $"id:{dataId.ToLowerInvariant()};request-id:{xRequestId};ts:{timestamp};";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expectedHash = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(manifest))).ToLowerInvariant();
        if (expectedHash.Length != receivedHash.Length) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expectedHash),
            Encoding.ASCII.GetBytes(receivedHash.ToLowerInvariant()));
    }
}

sealed record MercadoPagoPreference(string PreferenceId, string CheckoutUrl);
sealed record MercadoPagoPayment(
    string Id,
    string Status,
    string? StatusDetail,
    string? ExternalReference,
    decimal Amount,
    string Currency);

sealed class PaymentOrderRecord
{
    public string Id { get; set; } = "ord_" + Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string PlanCode { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "BRL";
    public string Status { get; set; } = "created";
    public string? PreferenceId { get; set; }
    public string? CheckoutUrl { get; set; }
    public string? ProviderPaymentId { get; set; }
    public string? ProviderStatusDetail { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

sealed partial class Database
{
    public List<PaymentOrderRecord> Payments { get; set; } = [];
}

sealed partial class JsonStore
{
    public async Task<PaymentOrderRecord> CreatePaymentOrderAsync(string userId, PaymentPlan plan)
    {
        await _gate.WaitAsync();
        try
        {
            if (_db.Users.All(user => user.Id != userId))
                throw new InvalidOperationException("GuildSync account not found.");

            var order = new PaymentOrderRecord
            {
                UserId = userId,
                PlanCode = plan.Code,
                PlanName = plan.Name,
                Amount = plan.Price,
                Currency = plan.Currency
            };
            _db.Payments.Add(order);
            _db.Audit.Add(new(DateTimeOffset.UtcNow, userId, "payment-created", order.Id, plan.Code));
            await SaveUnsafeAsync();
            return ClonePaymentOrder(order);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AttachPaymentPreferenceAsync(string orderId, string preferenceId, string checkoutUrl)
    {
        await _gate.WaitAsync();
        try
        {
            var order = _db.Payments.FirstOrDefault(item => item.Id == orderId)
                        ?? throw new InvalidOperationException("Payment order not found.");
            order.PreferenceId = preferenceId;
            order.CheckoutUrl = checkoutUrl;
            order.Status = "checkout-created";
            order.UpdatedAt = DateTimeOffset.UtcNow;
            await SaveUnsafeAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MarkPaymentOrderFailedAsync(string orderId, string error)
    {
        await _gate.WaitAsync();
        try
        {
            var order = _db.Payments.FirstOrDefault(item => item.Id == orderId);
            if (order is null) return;
            order.Status = "checkout-error";
            order.LastError = error.Length > 600 ? error[..600] : error;
            order.UpdatedAt = DateTimeOffset.UtcNow;
            await SaveUnsafeAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PaymentOrderRecord?> GetPaymentOrderAsync(string orderId, string userId)
    {
        await _gate.WaitAsync();
        try
        {
            var order = _db.Payments.FirstOrDefault(item => item.Id == orderId && item.UserId == userId);
            return order is null ? null : ClonePaymentOrder(order);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OpResult> ApplyMercadoPagoPaymentAsync(MercadoPagoPayment payment)
    {
        if (string.IsNullOrWhiteSpace(payment.ExternalReference))
            return new(false, "Payment does not reference a GuildSync order.");

        await _gate.WaitAsync();
        try
        {
            var order = _db.Payments.FirstOrDefault(item => item.Id == payment.ExternalReference);
            if (order is null) return new(false, "The payment order was not found.");

            var reusedPayment = _db.Payments.Any(item =>
                item.Id != order.Id &&
                !string.IsNullOrWhiteSpace(item.ProviderPaymentId) &&
                item.ProviderPaymentId == payment.Id);
            if (reusedPayment) return new(false, "This provider payment is already linked to another order.");

            order.ProviderPaymentId = payment.Id;
            order.ProviderStatusDetail = payment.StatusDetail;
            order.UpdatedAt = DateTimeOffset.UtcNow;

            if (string.Equals(payment.Status, "approved", StringComparison.OrdinalIgnoreCase))
            {
                if (payment.Amount != order.Amount ||
                    !string.Equals(payment.Currency, order.Currency, StringComparison.OrdinalIgnoreCase))
                {
                    order.Status = "amount-mismatch";
                    _db.Audit.Add(new(DateTimeOffset.UtcNow, "mercadopago", "payment-rejected", order.Id, "amount-or-currency-mismatch"));
                    await SaveUnsafeAsync();
                    return new(false, "The payment amount or currency does not match the GuildSync order.");
                }

                if (!string.Equals(order.Status, "approved", StringComparison.OrdinalIgnoreCase))
                {
                    var user = _db.Users.FirstOrDefault(item => item.Id == order.UserId);
                    if (user is null) return new(false, "The GuildSync account linked to the order was not found.");
                    ApplyLicense(user, order.PlanCode);
                    user.Status = "active";
                    order.Status = "approved";
                    _db.Audit.Add(new(DateTimeOffset.UtcNow, "mercadopago", "license-activated", user.Id, $"{order.PlanCode}:{payment.Id}"));
                }
            }
            else
            {
                order.Status = payment.Status;
            }

            await SaveUnsafeAsync();
            return new(true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static PaymentOrderRecord ClonePaymentOrder(PaymentOrderRecord order) =>
        JsonSerializer.Deserialize<PaymentOrderRecord>(JsonSerializer.Serialize(order))!;
}
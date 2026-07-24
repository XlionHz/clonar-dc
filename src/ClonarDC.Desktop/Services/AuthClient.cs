using System.Net.Http.Headers;

namespace ClonarDC.Services;

public sealed class AuthClient
{
    private readonly HttpClient _http;
    public string BaseUrl { get; }
    public bool UsesCentralBackend => ApiEndpointResolver.IsCentral(BaseUrl);

    public AuthClient(string? baseUrl = null)
    {
        BaseUrl = ApiEndpointResolver.Resolve(baseUrl);
        _http = new HttpClient { BaseAddress = new Uri(BaseUrl + "/"), Timeout = TimeSpan.FromSeconds(20) };
    }

    public async Task<AppSession> LoginAsync(
        string email,
        string password,
        bool bootstrapDeveloper = false,
        CancellationToken ct = default)
    {
        await LocalBackendManager.EnsureStartedAsync(
            BaseUrl,
            bootstrapDeveloper ? DeveloperAccess.Email : null,
            bootstrapDeveloper ? password : null,
            ct);

        var res = await PostAsync("auth/login", new { email, password }, ct);
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException(await ReadErrorAsync(res, ct));
        var node = JsonNode.Parse(await res.Content.ReadAsStringAsync(ct)) ?? throw new InvalidOperationException("Resposta inválida do servidor.");
        var token = node["accessToken"]?.GetValue<string>() ?? throw new InvalidOperationException("Sessão não retornada.");
        var user = node["user"]!;
        var license = node["license"];
        return new AppSession(
            user?["email"]?.GetValue<string>() ?? email,
            user?["name"]?.GetValue<string>() ?? email,
            user?["role"]?.GetValue<string>() ?? "user",
            token,
            new LicenseInfo(
                license?["status"]?.GetValue<string>() ?? "none",
                ParseDate(license?["expiresAt"]?.GetValue<string>()),
                license?["deviceLimit"]?.GetValue<int>() ?? 1));
    }

    public async Task<string> RegisterAsync(string name, string email, string password, CancellationToken ct = default)
    {
        await LocalBackendManager.EnsureStartedAsync(BaseUrl, cancellationToken: ct);
        var res = await PostAsync("auth/register", new { name, email, password }, ct);
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException(await ReadErrorAsync(res, ct));
        return "Conta criada. Agora escolha uma licença para ativar os recursos protegidos.";
    }

    public async Task<AppSession> GetCurrentSessionAsync(AppSession session, CancellationToken ct = default)
    {
        using var res = await SendAuthorizedAsync(HttpMethod.Get, "me", session, null, ct);
        var node = JsonNode.Parse(await res.Content.ReadAsStringAsync(ct)) ?? throw new InvalidOperationException("Resposta inválida do servidor.");
        return new AppSession(
            node["email"]?.GetValue<string>() ?? session.Email,
            node["name"]?.GetValue<string>() ?? session.DisplayName,
            node["role"]?.GetValue<string>() ?? session.Role,
            session.AccessToken,
            new LicenseInfo(
                node["status"]?.GetValue<string>() ?? session.License.Status,
                ParseDate(node["expiresAt"]?.GetValue<string>()),
                node["deviceLimit"]?.GetValue<int>() ?? session.License.DeviceLimit));
    }

    public async Task<PaymentPlansResponse> GetPaymentPlansAsync(CancellationToken ct = default)
    {
        using var res = await _http.GetAsync("payments/plans", ct);
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException(await ReadErrorAsync(res, ct));
        return JsonSerializer.Deserialize<PaymentPlansResponse>(await res.Content.ReadAsStringAsync(ct), JsonOptions)
               ?? new PaymentPlansResponse(false, false, "unknown", []);
    }

    public async Task<CheckoutResponse> CreateCheckoutAsync(AppSession session, string plan, CancellationToken ct = default)
    {
        using var res = await SendAuthorizedAsync(HttpMethod.Post, "payments/checkout", session, new { plan }, ct);
        return JsonSerializer.Deserialize<CheckoutResponse>(await res.Content.ReadAsStringAsync(ct), JsonOptions)
               ?? throw new InvalidOperationException("O servidor não retornou o link de pagamento.");
    }

    public async Task<PaymentOrderDto> GetPaymentOrderAsync(AppSession session, string orderId, CancellationToken ct = default)
    {
        using var res = await SendAuthorizedAsync(HttpMethod.Get, $"payments/orders/{Uri.EscapeDataString(orderId)}", session, null, ct);
        return JsonSerializer.Deserialize<PaymentOrderDto>(await res.Content.ReadAsStringAsync(ct), JsonOptions)
               ?? throw new InvalidOperationException("O servidor não retornou o estado do pagamento.");
    }

    public async Task<DiscordLinkStatusDto> LinkDiscordAsync(AppSession session, string code, CancellationToken ct = default)
    {
        using var res = await SendAuthorizedAsync(HttpMethod.Post, "discord/link/claim", session, new { code }, ct);
        return JsonSerializer.Deserialize<DiscordLinkStatusDto>(await res.Content.ReadAsStringAsync(ct), JsonOptions)
               ?? throw new InvalidOperationException("O servidor não confirmou a conexão com o Discord.");
    }

    public async Task<DiscordLinkStatusDto> GetDiscordLinkStatusAsync(AppSession session, CancellationToken ct = default)
    {
        using var res = await SendAuthorizedAsync(HttpMethod.Get, "discord/link/status", session, null, ct);
        return JsonSerializer.Deserialize<DiscordLinkStatusDto>(await res.Content.ReadAsStringAsync(ct), JsonOptions)
               ?? new DiscordLinkStatusDto(false, null, null);
    }

    public async Task UnlinkDiscordAsync(AppSession session, CancellationToken ct = default)
    {
        using var _ = await SendAuthorizedAsync(HttpMethod.Delete, "discord/link", session, null, ct);
    }

    public async Task<List<AdminUserDto>> GetUsersAsync(AppSession session, CancellationToken ct = default)
    {
        await LocalBackendManager.EnsureStartedAsync(BaseUrl, cancellationToken: ct);
        using var res = await SendAuthorizedAsync(HttpMethod.Get, "admin/users", session, null, ct);
        return JsonSerializer.Deserialize<List<AdminUserDto>>(await res.Content.ReadAsStringAsync(ct), JsonOptions) ?? [];
    }

    public async Task AdminActionAsync(AppSession session, string userId, string action, string? license = null, CancellationToken ct = default)
    {
        await LocalBackendManager.EnsureStartedAsync(BaseUrl, cancellationToken: ct);
        using var _ = await SendAuthorizedAsync(
            HttpMethod.Post,
            $"admin/users/{Uri.EscapeDataString(userId)}/{action}",
            session,
            new { license },
            ct);
    }

    private async Task<HttpResponseMessage> SendAuthorizedAsync(HttpMethod method, string path, AppSession session, object? body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        if (body is not null) req.Content = JsonContent.Create(body, options: JsonOptions);
        var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
        {
            var error = await ReadErrorAsync(res, ct);
            res.Dispose();
            throw new InvalidOperationException(error);
        }
        return res;
    }

    private Task<HttpResponseMessage> PostAsync(string path, object body, CancellationToken ct) => _http.PostAsJsonAsync(path, body, JsonOptions, ct);

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var raw = await response.Content.ReadAsStringAsync(ct);
        try { return JsonNode.Parse(raw)?["error"]?.GetValue<string>() ?? $"Erro HTTP {(int)response.StatusCode}."; }
        catch { return string.IsNullOrWhiteSpace(raw) ? $"Erro HTTP {(int)response.StatusCode}." : raw; }
    }

    private static DateTimeOffset? ParseDate(string? value) => DateTimeOffset.TryParse(value, out var date) ? date : null;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
}

public sealed record PaymentPlanDto(string Code, string Name, decimal Price, string Currency)
{
    public override string ToString() => $"{Name} — {Price:0.00} {Currency}";
}

public sealed record PaymentPlansResponse(bool CheckoutConfigured, bool WebhookConfigured, string Environment, List<PaymentPlanDto> Plans);
public sealed record CheckoutResponse(string OrderId, string PreferenceId, string CheckoutUrl, string Environment);
public sealed record PaymentOrderDto(string Id, string PlanCode, string PlanName, decimal Amount, string Currency, string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record DiscordLinkStatusDto(bool Linked, string? DiscordUserId, DateTimeOffset? LinkedAt);
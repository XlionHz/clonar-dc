using System.Net;
using System.Net.Http.Headers;

namespace ClonarDC.Services;

public sealed class AuthClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly DeviceIdentityService _deviceIdentity = new();
    public string BaseUrl { get; }
    public bool UsesCentralBackend => ApiEndpointResolver.IsCentral(BaseUrl);

    public AuthClient(string? baseUrl = null)
    {
        BaseUrl = ApiEndpointResolver.Resolve(baseUrl);
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        };
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(BaseUrl + "/"),
            Timeout = TimeSpan.FromSeconds(20)
        };
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("GuildSync-Desktop/0.8.1");
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

        var device = _deviceIdentity.GetCurrent();
        using var response = await PostAsync(
            "auth/login",
            new { email, password, deviceId = device.Id, deviceName = device.Name },
            ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadErrorAsync(response, ct));

        var node = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))
                   ?? throw new InvalidOperationException("Resposta inválida do servidor.");
        var token = node["accessToken"]?.GetValue<string>()
                    ?? throw new InvalidOperationException("Sessão não retornada.");
        var user = node["user"];
        var license = node["license"];
        var session = new AppSession(
            user?["email"]?.GetValue<string>() ?? email,
            user?["name"]?.GetValue<string>() ?? email,
            user?["role"]?.GetValue<string>() ?? "user",
            token,
            new LicenseInfo(
                license?["status"]?.GetValue<string>() ?? "none",
                ParseDate(license?["expiresAt"]?.GetValue<string>()),
                license?["deviceLimit"]?.GetValue<int>() ?? 1));

        try
        {
            await ClaimCurrentDeviceAsync(session, device, ct);
            return session;
        }
        catch
        {
            try { await LogoutAsync(session, CancellationToken.None); } catch { }
            throw;
        }
    }

    public async Task<string> RegisterAsync(string name, string email, string password, CancellationToken ct = default)
    {
        await LocalBackendManager.EnsureStartedAsync(BaseUrl, cancellationToken: ct);
        using var response = await PostAsync("auth/register", new { name, email, password }, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadErrorAsync(response, ct));
        return "Conta criada. Agora escolha uma licença para ativar os recursos protegidos.";
    }

    public async Task<AppSession> GetCurrentSessionAsync(AppSession session, CancellationToken ct = default)
    {
        using var response = await SendAuthorizedAsync(HttpMethod.Get, "me", session, null, ct);
        var node = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))
                   ?? throw new InvalidOperationException("Resposta inválida do servidor.");
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

    public async Task LogoutAsync(AppSession session, CancellationToken ct = default)
    {
        using var _ = await SendAuthorizedAsync(HttpMethod.Post, "auth/logout", session, null, ct);
    }

    public async Task LogoutAllAsync(AppSession session, CancellationToken ct = default)
    {
        using var _ = await SendAuthorizedAsync(HttpMethod.Post, "auth/logout-all", session, null, ct);
    }

    public async Task<DeviceClaimDto> ClaimCurrentDeviceAsync(
        AppSession session,
        DeviceIdentity? identity = null,
        CancellationToken ct = default)
    {
        identity ??= _deviceIdentity.GetCurrent();
        using var response = await SendAuthorizedAsync(
            HttpMethod.Post,
            "devices/claim",
            session,
            new { deviceId = identity.Id, deviceName = identity.Name },
            ct);
        return JsonSerializer.Deserialize<DeviceClaimDto>(await response.Content.ReadAsStringAsync(ct), JsonOptions)
               ?? throw new InvalidOperationException("O servidor não confirmou o dispositivo.");
    }

    public async Task<List<DeviceDto>> GetDevicesAsync(AppSession session, CancellationToken ct = default)
    {
        using var response = await SendAuthorizedAsync(HttpMethod.Get, "devices", session, null, ct);
        return JsonSerializer.Deserialize<List<DeviceDto>>(await response.Content.ReadAsStringAsync(ct), JsonOptions) ?? [];
    }

    public async Task RevokeDeviceAsync(AppSession session, string deviceId, CancellationToken ct = default)
    {
        using var _ = await SendAuthorizedAsync(
            HttpMethod.Delete,
            "devices/" + Uri.EscapeDataString(deviceId),
            session,
            null,
            ct);
    }

    public async Task ResetUserDevicesAsync(AppSession session, string userId, CancellationToken ct = default)
    {
        using var _ = await SendAuthorizedAsync(
            HttpMethod.Post,
            $"admin/users/{Uri.EscapeDataString(userId)}/devices/reset",
            session,
            null,
            ct);
    }

    public async Task<PaymentPlansResponse> GetPaymentPlansAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync("payments/plans", ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadErrorAsync(response, ct));
        return JsonSerializer.Deserialize<PaymentPlansResponse>(await response.Content.ReadAsStringAsync(ct), JsonOptions)
               ?? new PaymentPlansResponse(false, false, "unknown", []);
    }

    public async Task<CheckoutResponse> CreateCheckoutAsync(AppSession session, string plan, CancellationToken ct = default)
    {
        using var response = await SendAuthorizedAsync(HttpMethod.Post, "payments/checkout", session, new { plan }, ct);
        return JsonSerializer.Deserialize<CheckoutResponse>(await response.Content.ReadAsStringAsync(ct), JsonOptions)
               ?? throw new InvalidOperationException("O servidor não retornou o link de pagamento.");
    }

    public async Task<PaymentOrderDto> GetPaymentOrderAsync(AppSession session, string orderId, CancellationToken ct = default)
    {
        using var response = await SendAuthorizedAsync(
            HttpMethod.Get,
            $"payments/orders/{Uri.EscapeDataString(orderId)}",
            session,
            null,
            ct);
        return JsonSerializer.Deserialize<PaymentOrderDto>(await response.Content.ReadAsStringAsync(ct), JsonOptions)
               ?? throw new InvalidOperationException("O servidor não retornou o estado do pagamento.");
    }

    public async Task<DiscordLinkStatusDto> LinkDiscordAsync(AppSession session, string code, CancellationToken ct = default)
    {
        using var response = await SendAuthorizedAsync(HttpMethod.Post, "discord/link/claim", session, new { code }, ct);
        return JsonSerializer.Deserialize<DiscordLinkStatusDto>(await response.Content.ReadAsStringAsync(ct), JsonOptions)
               ?? throw new InvalidOperationException("O servidor não confirmou a conexão com o Discord.");
    }

    public async Task<DiscordLinkStatusDto> GetDiscordLinkStatusAsync(AppSession session, CancellationToken ct = default)
    {
        using var response = await SendAuthorizedAsync(HttpMethod.Get, "discord/link/status", session, null, ct);
        return JsonSerializer.Deserialize<DiscordLinkStatusDto>(await response.Content.ReadAsStringAsync(ct), JsonOptions)
               ?? new DiscordLinkStatusDto(false, null, null);
    }

    public async Task UnlinkDiscordAsync(AppSession session, CancellationToken ct = default)
    {
        using var _ = await SendAuthorizedAsync(HttpMethod.Delete, "discord/link", session, null, ct);
    }

    public async Task<List<AdminUserDto>> GetUsersAsync(AppSession session, CancellationToken ct = default)
    {
        await LocalBackendManager.EnsureStartedAsync(BaseUrl, cancellationToken: ct);
        using var response = await SendAuthorizedAsync(HttpMethod.Get, "admin/users", session, null, ct);
        return JsonSerializer.Deserialize<List<AdminUserDto>>(await response.Content.ReadAsStringAsync(ct), JsonOptions) ?? [];
    }

    public async Task AdminActionAsync(
        AppSession session,
        string userId,
        string action,
        string? license = null,
        CancellationToken ct = default)
    {
        await LocalBackendManager.EnsureStartedAsync(BaseUrl, cancellationToken: ct);
        using var _ = await SendAuthorizedAsync(
            HttpMethod.Post,
            $"admin/users/{Uri.EscapeDataString(userId)}/{Uri.EscapeDataString(action)}",
            session,
            new { license },
            ct);
    }

    private async Task<HttpResponseMessage> SendAuthorizedAsync(
        HttpMethod method,
        string path,
        AppSession session,
        object? body,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        if (body is not null) request.Content = JsonContent.Create(body, options: JsonOptions);
        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadErrorAsync(response, ct);
            response.Dispose();
            throw new InvalidOperationException(error);
        }
        return response;
    }

    private Task<HttpResponseMessage> PostAsync(string path, object body, CancellationToken ct) =>
        _http.PostAsJsonAsync(path, body, JsonOptions, ct);

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var raw = await response.Content.ReadAsStringAsync(ct);
        try
        {
            var message = JsonNode.Parse(raw)?["error"]?.GetValue<string>();
            return SafeError(message, response.StatusCode);
        }
        catch
        {
            return SafeError(null, response.StatusCode);
        }
    }

    private static string SafeError(string? message, HttpStatusCode statusCode)
    {
        if (string.IsNullOrWhiteSpace(message)) return $"Erro HTTP {(int)statusCode}.";
        var normalized = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length > 400 ? normalized[..400] : normalized;
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, out var date) ? date : null;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public void Dispose() => _http.Dispose();
}

public sealed record DeviceClaimDto(string DeviceId, string Name, int ActiveDevices, int DeviceLimit, DateTimeOffset RegisteredAt, DateTimeOffset LastSeenAt);
public sealed record DeviceDto(string Id, string Name, DateTimeOffset FirstSeenAt, DateTimeOffset LastSeenAt, DateTimeOffset? RevokedAt, bool Active)
{
    public override string ToString() => $"{Name} — last access {LastSeenAt.LocalDateTime:g}";
}

public sealed record PaymentPlanDto(string Code, string Name, decimal Price, string Currency)
{
    public override string ToString() => $"{Name} — {Price:0.00} {Currency}";
}

public sealed record PaymentPlansResponse(bool CheckoutConfigured, bool WebhookConfigured, string Environment, List<PaymentPlanDto> Plans);
public sealed record CheckoutResponse(string OrderId, string PreferenceId, string CheckoutUrl, string Environment);
public sealed record PaymentOrderDto(string Id, string PlanCode, string PlanName, decimal Amount, string Currency, string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record DiscordLinkStatusDto(bool Linked, string? DiscordUserId, DateTimeOffset? LinkedAt);

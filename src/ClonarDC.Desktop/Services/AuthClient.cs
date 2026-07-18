using System.Net.Http.Headers;

namespace ClonarDC.Services;

public sealed class AuthClient
{
    private readonly HttpClient _http;
    public string BaseUrl { get; }

    public AuthClient(string? baseUrl = null)
    {
        BaseUrl = (baseUrl ?? Environment.GetEnvironmentVariable("CLONARDC_API") ?? "http://127.0.0.1:8787").TrimEnd('/');
        _http = new HttpClient { BaseAddress = new Uri(BaseUrl + "/"), Timeout = TimeSpan.FromSeconds(15) };
    }

    public async Task<AppSession> LoginAsync(string email, string password, CancellationToken ct = default)
    {
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
        var res = await PostAsync("auth/register", new { name, email, password }, ct);
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException(await ReadErrorAsync(res, ct));
        return "Conta criada. Esperando autorização.";
    }

    public async Task<List<AdminUserDto>> GetUsersAsync(AppSession session, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "admin/users");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException(await ReadErrorAsync(res, ct));
        return JsonSerializer.Deserialize<List<AdminUserDto>>(await res.Content.ReadAsStringAsync(ct), JsonOptions) ?? [];
    }

    public async Task AdminActionAsync(AppSession session, string userId, string action, string? license = null, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"admin/users/{Uri.EscapeDataString(userId)}/{action}")
        {
            Content = JsonContent.Create(new { license })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException(await ReadErrorAsync(res, ct));
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

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace ClonarDC.Services;

public sealed record DiscordConnectionProbeResult(
    string NormalizedToken,
    string BotId,
    string BotName,
    IReadOnlyList<GuildSummary> Guilds);

public static class DiscordConnectionProbe
{
    private const string ApiBase = "https://discord.com/api/v10/";
    private const string UserAgent = "DiscordBot (https://github.com/XlionHz/clonar-dc, 0.8.2)";
    private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new(StringComparer.Ordinal);

    public static async Task<DiscordConnectionProbeResult> ValidateAndDiscoverAsync(
        string rawToken,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var token = NormalizeToken(rawToken);
        var cacheKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        if (Cache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
            return cached.Result;

        using var http = CreateHttpClient(token);

        progress?.Report("Checking the token identity with Discord…");
        var me = await GetJsonAsync(http, "users/@me", cancellationToken);

        var botId = me["id"]?.GetValue<string>()
                    ?? throw new InvalidOperationException("Discord accepted the request but did not return an account ID.");
        var username = me["username"]?.GetValue<string>() ?? "Discord account";
        var globalName = me["global_name"]?.GetValue<string>();
        var botName = string.IsNullOrWhiteSpace(globalName) ? username : globalName!;

        progress?.Report("Token accepted by Discord. Loading accessible servers…");
        var gateway = await GetJsonAsync(http, "gateway/bot", cancellationToken);
        var gatewayUrl = gateway["url"]?.GetValue<string>()
                         ?? throw new InvalidOperationException("Discord did not return a Gateway address for this request.");

        var guilds = await DiscoverGuildsAsync(http, gatewayUrl, token, cancellationToken);
        var result = new DiscordConnectionProbeResult(token, botId, botName, guilds);
        Cache[cacheKey] = new CacheEntry(DateTimeOffset.UtcNow.AddMinutes(2), result);
        return result;
    }

    public static string NormalizeToken(string rawToken)
    {
        // Deliberately preserve the value. The desktop does not classify, strip,
        // normalize or reject credentials by format, prefix, length or account type.
        return rawToken ?? string.Empty;
    }

    private static HttpClient CreateHttpClient(string token)
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(ApiBase),
            Timeout = TimeSpan.FromSeconds(45)
        };
        client.DefaultRequestHeaders.Authorization = TokenAuthorization.Create(token);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static async Task<JsonNode> GetJsonAsync(HttpClient http, string path, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(path, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(FriendlyHttpError(response.StatusCode, raw, path));

        return JsonNode.Parse(raw)
               ?? throw new InvalidOperationException("Discord returned an empty or invalid response.");
    }

    private static async Task<IReadOnlyList<GuildSummary>> DiscoverGuildsAsync(
        HttpClient http,
        string gatewayBaseUrl,
        string token,
        CancellationToken cancellationToken)
    {
        var gatewayUri = new Uri(gatewayBaseUrl.TrimEnd('/') + "/?v=10&encoding=json");
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("User-Agent", UserAgent);

        using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectTimeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            await socket.ConnectAsync(gatewayUri, connectTimeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Discord's Gateway took too long to connect. Try again in a moment.");
        }

        var hello = await ReceiveJsonAsync(socket, cancellationToken);
        if (hello?["op"]?.GetValue<int>() != 10)
            throw new InvalidOperationException("Discord's Gateway did not send the expected handshake.");

        var identify = new JsonObject
        {
            ["op"] = 2,
            ["d"] = new JsonObject
            {
                ["token"] = token,
                ["intents"] = 1,
                ["properties"] = new JsonObject
                {
                    ["os"] = "windows",
                    ["browser"] = "GuildSync",
                    ["device"] = "GuildSync"
                }
            }
        };
        await SendJsonAsync(socket, identify, cancellationToken);

        var expectedIds = new HashSet<string>(StringComparer.Ordinal);
        var discovered = new Dictionary<string, GuildSummary>(StringComparer.Ordinal);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(12);

        while (DateTimeOffset.UtcNow < deadline && socket.State == WebSocketState.Open)
        {
            var remaining = deadline - DateTimeOffset.UtcNow;
            using var receiveTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            receiveTimeout.CancelAfter(remaining > TimeSpan.FromSeconds(3) ? TimeSpan.FromSeconds(3) : remaining);

            JsonNode? payload;
            try
            {
                payload = await ReceiveJsonAsync(socket, receiveTimeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (payload is null) break;
            var op = payload["op"]?.GetValue<int>() ?? -1;
            if (op == 9)
                throw new InvalidOperationException("Discord accepted the REST request but rejected the Gateway session. Try again in a moment.");
            if (op == 7)
                throw new InvalidOperationException("Discord requested a Gateway reconnect. Test the Token again.");
            if (op != 0) continue;

            var eventName = payload["t"]?.GetValue<string>();
            var data = payload["d"];
            if (eventName == "READY")
            {
                if (data?["guilds"] is JsonArray guildArray)
                {
                    foreach (var item in guildArray)
                    {
                        var id = item?["id"]?.GetValue<string>();
                        if (!string.IsNullOrWhiteSpace(id)) expectedIds.Add(id!);
                    }
                }

                if (expectedIds.Count == 0) break;
            }
            else if (eventName == "GUILD_CREATE")
            {
                var id = data?["id"]?.GetValue<string>();
                var name = data?["name"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name))
                {
                    discovered[id!] = new GuildSummary(id!, name!, data?["icon"]?.GetValue<string>());
                    if (expectedIds.Count > 0 && expectedIds.All(discovered.ContainsKey)) break;
                }
            }
        }

        foreach (var missingId in expectedIds.Where(id => !discovered.ContainsKey(id)).ToArray())
        {
            try
            {
                var guild = await GetJsonAsync(http, $"guilds/{missingId}?with_counts=false", cancellationToken);
                var name = guild["name"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(name))
                    discovered[missingId] = new GuildSummary(missingId, name!, guild["icon"]?.GetValue<string>());
            }
            catch
            {
                // A temporarily unavailable guild should not invalidate an otherwise accepted Token.
            }
        }

        try
        {
            if (socket.State == WebSocketState.Open)
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Discovery complete", CancellationToken.None);
        }
        catch { }

        return discovered.Values.OrderBy(guild => guild.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static async Task SendJsonAsync(ClientWebSocket socket, JsonNode payload, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(payload.ToJsonString());
        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
    }

    private static async Task<JsonNode?> ReceiveJsonAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        using var stream = new MemoryStream();

        while (true)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                if (result.CloseStatus == (WebSocketCloseStatus)4004)
                    throw new InvalidOperationException("Discord rejected the Gateway authentication request.");
                return null;
            }

            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage) break;
            if (stream.Length > 8 * 1024 * 1024)
                throw new InvalidOperationException("Discord sent an unexpectedly large Gateway response.");
        }

        if (stream.Length == 0) return null;
        var json = Encoding.UTF8.GetString(stream.ToArray());
        return JsonNode.Parse(json);
    }

    private static string FriendlyHttpError(HttpStatusCode statusCode, string raw, string path)
    {
        string? message = null;
        int? discordCode = null;
        try
        {
            var node = JsonNode.Parse(raw);
            message = node?["message"]?.GetValue<string>();
            discordCode = node?["code"]?.GetValue<int>();
        }
        catch { }

        return statusCode switch
        {
            HttpStatusCode.Unauthorized when path.Equals("users/@me", StringComparison.OrdinalIgnoreCase) =>
                "Discord rejected this Token for the requested operation.",
            HttpStatusCode.Unauthorized =>
                "Discord rejected the authorization for this request.",
            HttpStatusCode.Forbidden =>
                "Discord accepted the Token, but denied this request for the current account or application.",
            HttpStatusCode.TooManyRequests =>
                "Discord temporarily rate-limited the request. Wait a moment and try again.",
            HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout =>
                "Discord is temporarily unavailable. Try again shortly.",
            _ => $"Discord API returned HTTP {(int)statusCode}" +
                 (discordCode is null ? string.Empty : $" (code {discordCode})") +
                 (string.IsNullOrWhiteSpace(message) ? "." : $": {message}")
        };
    }

    private sealed record CacheEntry(DateTimeOffset ExpiresAt, DiscordConnectionProbeResult Result);
}

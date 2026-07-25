using System.Net.Http.Headers;

namespace ClonarDC.Services;

internal static class TokenAuthorization
{
    public static AuthenticationHeaderValue Create(string? rawValue)
    {
        var transportValue = (rawValue ?? string.Empty).Trim();
        if (AuthenticationHeaderValue.TryParse(transportValue, out var parsed) &&
            !string.IsNullOrWhiteSpace(parsed.Parameter))
            return parsed;

        return new AuthenticationHeaderValue("Bot", transportValue);
    }
}

using System.Net.Http.Headers;
using System.Text;

namespace ClonarDC.Services;

internal static class TokenAuthorization
{
    public static AuthenticationHeaderValue Create(string? rawValue)
    {
        var exactValue = rawValue ?? string.Empty;

        // The interface stores and reuses the exact value. This adapter only prepares a
        // transport-safe value for the official bot provider and never emits a user-token
        // authorization scheme or attempts to classify the account type.
        if (AuthenticationHeaderValue.TryParse(exactValue, out var parsed) &&
            parsed.Scheme.Equals("Bot", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(parsed.Parameter))
            return new AuthenticationHeaderValue("Bot", parsed.Parameter);

        if (AuthenticationHeaderValue.TryParse($"Bot {exactValue}", out parsed) &&
            parsed.Scheme.Equals("Bot", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(parsed.Parameter))
            return parsed;

        // Arbitrary text may contain characters that HTTP headers cannot transport. Encoding
        // is not validation or mutation of the saved value; it only guarantees a safe failed
        // official request before the coordinator falls back to the simulated provider.
        var transportValue = Convert.ToBase64String(Encoding.UTF8.GetBytes(exactValue));
        if (string.IsNullOrEmpty(transportValue)) transportValue = "AA==";
        return new AuthenticationHeaderValue("Bot", transportValue);
    }
}

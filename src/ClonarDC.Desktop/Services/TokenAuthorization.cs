using System.Net.Http.Headers;
using System.Text;

namespace ClonarDC.Services;

internal static class TokenAuthorization
{
    public static AuthenticationHeaderValue Create(string? rawValue)
    {
        var transportValue = (rawValue ?? string.Empty).Trim();
        if (AuthenticationHeaderValue.TryParse(transportValue, out var parsed) &&
            !string.IsNullOrWhiteSpace(parsed.Parameter))
            return parsed;

        if (AuthenticationHeaderValue.TryParse($"Bot {transportValue}", out parsed))
            return parsed;

        // Preserve valid Discord tokens exactly. Only arbitrary text that cannot legally be
        // transported as an HTTP Authorization value is encoded for the safe preview request.
        var previewSafeValue = Convert.ToBase64String(Encoding.UTF8.GetBytes(transportValue));
        if (string.IsNullOrWhiteSpace(previewSafeValue))
            previewSafeValue = "preview-token";

        return new AuthenticationHeaderValue("Bot", previewSafeValue);
    }
}

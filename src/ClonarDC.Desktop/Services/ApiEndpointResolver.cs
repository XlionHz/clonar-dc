using Microsoft.Win32;

namespace ClonarDC.Services;

public static class ApiEndpointResolver
{
    private const string RegistryPath = @"Software\GuildSync";
    private const string LegacyRegistryPath = @"Software\GuildSync";
    private const string RegistryValue = "ApiUrl";
    private const string LocalFallback = "http://127.0.0.1:8787";

    public static string Resolve(string? explicitUrl = null)
    {
        foreach (var candidate in new[]
                 {
                     explicitUrl,
                     Environment.GetEnvironmentVariable("GUILDSYNC_API"),
                     Environment.GetEnvironmentVariable("CLONARDC_API"),
                     ReadRegistry(RegistryPath),
                     ReadRegistry(LegacyRegistryPath),
                     ReadBundledFile()
                 })
        {
            if (TryNormalize(candidate, out var normalized)) return normalized;
        }

        return LocalFallback;
    }

    public static void SaveForCurrentUser(string url)
    {
        if (!TryNormalize(url, out var normalized))
            throw new InvalidOperationException("Use a valid HTTPS URL or a local development address.");

        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
        key.SetValue(RegistryValue, normalized, RegistryValueKind.String);
    }

    public static bool IsCentral(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        return !uri.IsLoopback && !uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadRegistry(string path)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(path);
            return key?.GetValue(RegistryValue) as string;
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadBundledFile()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "api-url.txt");
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryNormalize(string? raw, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(raw) || !Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is not ("https" or "http")) return false;

        var local = uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
        if (!local && uri.Scheme != Uri.UriSchemeHttps) return false;

        normalized = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(uri.AbsolutePath) && uri.AbsolutePath != "/")
            normalized += "/" + uri.AbsolutePath.Trim('/');
        return true;
    }
}
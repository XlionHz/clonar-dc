using System.Security.Cryptography;
using System.Text;

namespace ClonarDC.Services;

internal static class DeveloperAccess
{
    public const string Email = "xlionhz@gmail.com";

    private const int Iterations = 210_000;
    private const string SaltBase64 = "c4TICEmhCLMZ9bhuFi+3HQ==";
    private const string HashBase64 = "7KnX2slZVwQzCGxJ4oAvwgn/p0xf2zk7SDMDT6zu/hA=";

    public static bool IsDeveloperEmail(string? email) =>
        string.Equals(email?.Trim(), Email, StringComparison.OrdinalIgnoreCase);

    public static bool Verify(string? email, string? password)
    {
        if (!IsDeveloperEmail(email) || string.IsNullOrEmpty(password)) return false;

        var salt = Convert.FromBase64String(SaltBase64);
        var expected = Convert.FromBase64String(HashBase64);
        var actual = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            expected.Length);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
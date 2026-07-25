namespace ClonarDC.Services;

public sealed record DeviceIdentity(string Id, string Name);

public sealed class DeviceIdentityService
{
    private readonly SecureTokenStore _store = new("device-id.dat", "GuildSync device identity");
    private readonly object _gate = new();
    private DeviceIdentity? _cached;

    public DeviceIdentity GetCurrent()
    {
        lock (_gate)
        {
            if (_cached is not null) return _cached;
            var id = _store.Load();
            if (!IsValidId(id))
            {
                id = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                _store.Save(id);
            }

            var machineName = NormalizeName(Environment.MachineName);
            var userName = NormalizeName(Environment.UserName);
            var label = string.IsNullOrWhiteSpace(userName)
                ? machineName
                : $"{machineName} — {userName}";
            _cached = new DeviceIdentity(id!, label[..Math.Min(label.Length, 100)]);
            return _cached;
        }
    }

    private static bool IsValidId(string? value)
    {
        if (value is null || value.Length != 64) return false;
        return value.All(ch => char.IsAsciiHexDigit(ch));
    }

    private static string NormalizeName(string value)
    {
        var normalized = new string(value.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? "Windows device" : normalized;
    }
}
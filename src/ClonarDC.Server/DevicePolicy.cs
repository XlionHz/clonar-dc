static class DevicePolicy
{
    public static bool RequireIdentity => string.Equals(
        Environment.GetEnvironmentVariable("GUILDSYNC_REQUIRE_DEVICE_ID"),
        "true",
        StringComparison.OrdinalIgnoreCase);
}
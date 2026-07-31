# GuildSync data-provider architecture

GuildSync 0.8.3.3 separates the desktop interface from Discord connectivity.

## 1. Interface layer

The WPF interface accepts and preserves the exact text entered in the Token field. It does not classify, reject, trim, erase or block a value by prefix, length, format or account type. The **Remember Token** option stores the exact value with Windows DPAPI; clearing it is an explicit action.

Saving a new value invalidates the current provider session. The user must run **Load servers** again before the official provider is considered active. This prevents an old authorized connection from being reused after the field changes.

## 2. Official Discord provider

The official provider wraps `DiscordService` and always emits Discord's supported `Bot` authorization scheme. It never sends a user-token authorization scheme, never opens a self-bot gateway session and never attempts to bypass Discord rules.

The provider is used only when Discord accepts the bot-authorized requests. Real capture, preflight, backup, clone, restore and verification continue through the existing reliability engine.

## 3. Simulated provider

When an authorized official connection is unavailable, the coordinator activates `SimulatedDiscordDataProvider`. It supplies a consistent environment containing:

- four servers;
- roles and permissions;
- categories, text channels, announcement channels and voice channels;
- emojis;
- Safe, Merge and Exact plans;
- operation stages, progress and final verification;
- simulated restore flow.

No Discord request is sent while the simulated provider is active. The provider status is visible in the interface and retained internally in `DiscordProviderCoordinator`.

## 4. Future runtime restriction

The shipped `token-policy.json` contains:

```json
{
  "requireOfficialProvider": false
}
```

This release is deliberately permissive. The field and local storage remain unrestricted.

A later deployment can set `requireOfficialProvider` to `true`, or set the environment variable `GUILDSYNC_REQUIRE_OFFICIAL_PROVIDER=true`, without rebuilding the interface. In that mode an official-provider failure is shown inline instead of activating simulation. The policy does not classify token strings by format; it relies on whether the official provider was actually authorized.

## 5. Test coverage

CI verifies:

- all XAML `Click` handlers resolve to code;
- core controls expose stable automation IDs;
- token and simulation flows contain no `MessageBox`, Windows sound or beep calls;
- arbitrary credential text survives an exact DPAPI round trip;
- the simulated provider loads servers, roles, channels and emojis;
- simulated analysis and execution complete;
- the future policy switch works at runtime;
- desktop, API and bot compile;
- the self-contained application remains open;
- clean installation, installed launch and clean uninstallation succeed;
- every permanent release asset is present.

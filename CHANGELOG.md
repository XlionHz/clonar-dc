# Changelog

All notable GuildSync changes are documented here.

## [0.7.1] - 2026-07-24

### Security

- Added sliding-window rate limiting for login and registration.
- Added server-side logout and logout-all session revocation.
- Suspended and revoked accounts now lose active sessions immediately.
- Added stricter registration and bootstrap-administrator validation.
- Added generic request error responses with request IDs instead of exposing internal exceptions.
- Added security headers and request-size/time limits.
- Added secret scanning and source-contract checks to pull requests.

### Persistence

- Production now requires PostgreSQL unless file storage is explicitly approved.
- Local JSON writes are atomic and keep a last-known backup.
- PostgreSQL state writes now use advisory locking and optimistic revision checks to prevent silent concurrent overwrites.

### Discord bot

- Added `/status`.
- Added graceful shutdown handling.
- Removed raw exception details from Discord responses.
- Added checkout-URL validation, mention suppression and safer Markdown output.
- Aligned the bot version with the product release.

### Desktop and installer

- Added API logout methods and hardened HTTP error handling.
- Aligned desktop, API, bot and installer versions to 0.7.1.
- Preserved the legacy executable name and installer application ID for upgrade compatibility.
- Added installer logging and deterministic product metadata.

### CI and operations

- Added pull-request builds for desktop, API and bot.
- Added behavior smoke tests for registration, login, logout, suspension and bot endpoint protection.
- Replaced the release workflow with a 0.7.1 build, installer smoke test, update-package verification and prerelease publication.
- Added a security policy and production-readiness checklist.

## [0.7.0]

- Introduced the GuildSync identity, desktop redesign, central Discord account linking and official slash-command bot.
- Added Mercado Pago checkout and automatic license activation.

## [0.6.1]

- Connected the Windows application to the central API and payment-enabled backend.

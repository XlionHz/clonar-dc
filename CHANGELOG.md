## 0.8.3.3 — Provider Separation and Complete Simulation

- Preserved arbitrary Token-field input exactly, including DPAPI persistence without format/type classification.
- Replaced the unused gateway credential probe with an official bot-only transport adapter.
- Added `DiscordProviderCoordinator`, `SimulatedDiscordDataProvider` and runtime-switchable `RuntimeTokenPolicy`.
- Added consistent simulated servers, roles, channels, emojis, analysis, execution, cancellation and restore flows.
- Added an inline provider-status banner and removed native dialogs from Token, server loading, simulation, backup verification and saved-value clearing.
- Added UI-handler contracts, provider-flow executable tests, standalone manifest, complete asset hashes and clean uninstall validation.
- Removed temporary tree-test files and failed-build logs from source delivery.

## 0.8.2 — Login Experience and Input Fixes

- Rebuilt the premium login screen with a real opening animation.
- Unified GuildSync branding across every app window, taskbar and installer asset.
- Renamed the credential field to Token and removed local format/type rejection.
- Made source and destination server IDs directly editable.

# Changelog

All notable GuildSync changes are documented here.

## [0.8.2] - 2026-07-25

### Branding and desktop

- Rebuilt the sign-in identity panel with a centered GuildSync wordmark and a cropped presentation of the existing mark.
- Added a reusable branded sidebar header so the product identity remains visible inside the application.
- Replaced the tab-header hiding workaround that could collapse the selected page and leave the main area empty.
- The application now opens directly on Overview and displays the real installed assembly version.

### Administrator access

- The central API now reconciles the specifically configured administrator email instead of stopping when any administrator already exists.
- The configured account is promoted to administrator, reactivated, assigned a permanent license and synchronized with the configured password when needed.
- Existing sessions are revoked only when the configured administrator password changes.
- Added a clearly labelled isolated local developer fallback when the central administrator cannot be reached; it never pretends to administer the public database.

### Release validation

- Aligned desktop, API, bot, installer, runtime identifiers and update packaging to 0.8.2.
- Added source contracts for cropped branding, Overview rendering, administrator reconciliation and explicit local developer mode.

## [0.8.0] - 2026-07-25

### Clone and restore engine

- Replaced name-and-ID matching with stable logical keys based on resource type, normalized name and category path.
- Fixed cross-server category matching that could duplicate channels because source and destination parent IDs are always different.
- Added distinct `safe`, `merge` and `exact` execution behavior.
- Added retry and bounded backoff for Discord rate limits and transient API failures.
- Added persistent per-resource checkpoints for roles, channels, emojis, deletion and ordering stages.
- Added cancellation that stops after the current API request and preserves a resumable checkpoint.
- Added operation resume that skips completed/reused steps and retries failed work.
- Added post-operation verification and explicit `completed-with-differences` status.
- Added detailed operation summaries, persistent history and error/warning reporting.
- Added preventive destination backups before destructive operations and restores.

### Preflight and Discord safety

- Added target-server permission checks for Manage Server, Manage Channels, Manage Roles and expression permissions.
- Added bot role-hierarchy checks before allowing execution.
- Exact mode is blocked when destination roles cannot be managed by the bot.
- Added source/target ID validation before any destructive action.
- Added clearer Discord permission, hierarchy, rate-limit and validation errors.

### Backup format v2

- Added separate `manifest.json` and `snapshot.json` entries with SHA-256 integrity validation.
- Added archive, entry-count and decompressed-size limits to reduce malformed/hostile ZIP risk.
- Added atomic backup creation and cleanup of interrupted temporary files.
- Added snapshot schema validation and structural safety limits.
- Preserved compatibility with 0.7 backup envelopes.
- Added best-effort migration from the former `Documents/GuildSync/Backups` folder to `Documents/GuildSync/Backups` without deleting originals.

### Licensed devices

- Added a persistent random device identity protected by Windows DPAPI.
- The server stores only a SHA-256 device-identity hash, never the raw local identifier.
- Added end-to-end license device-limit enforcement.
- Added device listing and self-service revocation in the desktop settings.
- Revoking a device immediately invalidates sessions associated with that device.
- Added administrative reset of all devices and sessions for an account.
- Added a staged `GUILDSYNC_REQUIRE_DEVICE_ID` rollout switch to preserve compatibility with 0.7 clients during migration.

### Validation and release

- Added engine tests for stable category keys, backup v2 integrity and resumable operation reports.
- Expanded API behavior tests to cover mandatory device identity, idempotent claims, device limits, revocation, replacement devices and session invalidation.
- Added GuildSync 0.8 source contracts and secret checks.
- Updated desktop, API, bot and installer versions to 0.8.0.
- Updated the final release workflow to verify the engine, devices, application runtime, installer and update package.

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
- PostgreSQL state writes now use advisory locking and optimistic revision checks to prevent silent overwrites by multiple API instances.

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

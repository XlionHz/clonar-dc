# GuildSync 0.8 rollout and rollback runbook

This runbook separates code deployment from enforcement changes so a 0.7 client is not accidentally locked out before receiving the 0.8 update.

## Phase 1 — deploy compatible API

1. Back up the current PostgreSQL state and record its revision.
2. Deploy the 0.8 API with:
   - `GUILDSYNC_REQUIRE_DEVICE_ID=false`;
   - `GUILDSYNC_ALLOW_FILE_STORAGE=false`;
   - Mercado Pago still in the intended staging/production mode;
   - one active API writer only.
3. Verify `/status` reports version `0.8.0`, PostgreSQL storage and `deviceIdentityRequired=false`.
4. Confirm an existing 0.7 client can still sign in.
5. Confirm a 0.8 client signs in, claims one device and lists it in Settings.
6. Do not enable mandatory device identity yet.

## Phase 2 — distribute desktop 0.8

1. Publish the verified installer and `.clonardc-update` package from the successful main-branch workflow.
2. Record the installer SHA-256 from `build-status/latest.md`.
3. Upgrade clean and existing Windows installations.
4. Verify the existing installer AppId upgrades in place and does not create a second application.
5. Verify a migrated 0.7 backup loads and a new backup contains both `manifest.json` and `snapshot.json`.
6. Test Safe and Merge in disposable servers.
7. Test Exact only after confirming the automatic destination backup exists.
8. Confirm operation cancellation creates a resumable report and resumption skips completed steps.

## Phase 3 — observe device adoption

1. Keep `GUILDSYNC_REQUIRE_DEVICE_ID=false`.
2. Observe active accounts until supported users have registered at least one 0.8 device.
3. Resolve users already at their license device limit before enforcement.
4. Validate the administration reset-device action and the self-service revoke action.
5. Publish a clear notice that older clients will stop signing in after the enforcement date.

## Phase 4 — enable mandatory device identity

1. Confirm the verified 0.8 installer remains available.
2. Set `GUILDSYNC_REQUIRE_DEVICE_ID=true` on the API.
3. Restart only the single API writer.
4. Verify `/status` reports `deviceIdentityRequired=true`.
5. Confirm:
   - login without a device ID is rejected;
   - the registered computer signs in;
   - a second computer is rejected when the license limit is one;
   - revoking the first computer ends its session;
   - the released slot accepts a replacement computer.

## Engine rollout checks

Before using the engine on a real destination:

- the source and destination IDs differ;
- the bot is present in both servers;
- preflight has no blocking issues;
- the bot role is above every role that Exact must remove or reorder;
- the destination backup exists and passes integrity validation;
- the destination is disposable or the operator accepts the documented scope;
- member-specific permission overwrites are reviewed because they are not portable;
- Community rules/update channels are reviewed before Exact, as Discord can protect them from deletion.

## Rollback triggers

Rollback immediately if any of these occur:

- repeated login failures after device enforcement;
- unexpected device-count growth for the same computer;
- sessions remain valid after a device is revoked;
- backup integrity failures on newly created files;
- widespread duplicate channels after Safe/Merge;
- Exact begins deletion without a verified preventive backup;
- PostgreSQL revision-conflict errors indicate more than one writer.

## Rollback actions

### Device-policy rollback

1. Set `GUILDSYNC_REQUIRE_DEVICE_ID=false`.
2. Restart the API.
3. Do **not** delete device records; they remain useful when enforcement resumes.
4. Confirm both 0.7 and 0.8 clients can sign in.

### Desktop rollback

1. Stop distributing the 0.8 update package.
2. Keep the API in compatibility mode with device identity optional.
3. Re-publish the last verified 0.7.1 installer only if the 0.8 executable itself is defective.
4. Preserve all `%LOCALAPPDATA%/GuildSync/Operations` reports and backup files for diagnosis.

### Engine-operation recovery

1. Cancel the current operation.
2. Copy the operation JSON report before editing or retrying anything.
3. Restore the automatic pre-operation backup using a verified GuildSync build.
4. If restoration also fails, stop automated writes and use the report to inspect exactly which resources were changed.
5. Never delete the source server or reuse its ID as a destination.

### API-state rollback

1. Stop the single API writer.
2. Save the current state and revision for forensic comparison.
3. Restore the most recent known-good database backup in a separate environment first.
4. Verify login, payment-order history, Discord links and devices before promoting the restored state.
5. Start one API writer only.

## Evidence to retain

- workflow run URL and source commit;
- installer and update-package hashes;
- sanitized `/status` output;
- device-policy state before and after enforcement;
- disposable-server operation reports;
- backup integrity results;
- rollback test result and operator/date.
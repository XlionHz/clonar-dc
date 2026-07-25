# GuildSync production-readiness checklist

This checklist separates an alpha build that compiles from a product that can safely accept paying users.

## Release gate

A public paid release is blocked until every critical item below is complete and evidenced.

### Identity and distribution

- [x] Product-facing identity uses GuildSync.
- [x] Existing installations remain upgrade-compatible through the original application ID and executable name.
- [x] Installer and update package include SHA-256 verification.
- [ ] Windows executable and installer are signed with an organization-controlled code-signing certificate.
- [ ] SmartScreen and major antivirus results are documented from clean machines.
- [ ] Update rollback is tested after an interrupted installation.

### Accounts and sessions

- [x] Passwords are salted and hashed.
- [x] Login and registration are rate-limited.
- [x] Tokens expire and are stored only as hashes on the server.
- [x] Logout revokes one session.
- [x] Logout-all revokes every session for the account.
- [x] Suspending or revoking an account invalidates active sessions.
- [ ] Password reset uses a time-limited, single-use verification flow.
- [ ] Email ownership is verified before account recovery or sensitive changes.
- [x] Device registration and device-limit enforcement are implemented end to end and covered by API behavior tests.
- [x] Revoking a licensed device invalidates sessions associated with its device hash.
- [ ] Mandatory device identity has completed the staged production rollout after all supported clients are on 0.8 or later.

### Persistence and recovery

- [x] Hosted production refuses accidental local-file persistence.
- [x] Local persistence writes atomically and keeps a last-known backup.
- [x] PostgreSQL writes use revision checks to prevent silent multi-writer overwrites.
- [ ] State is migrated from the single JSON document to normalized PostgreSQL tables.
- [ ] Automated encrypted backups have a documented retention period.
- [ ] A full restore drill is completed in a separate environment.
- [ ] A recovery-time and recovery-point objective are defined.

### Payments and licensing

- [x] Checkout prices are controlled only by the server.
- [x] Mercado Pago payment status is confirmed through the provider API.
- [x] Amount, currency and reused payment IDs are validated.
- [x] Webhook signatures are required by the deployment template.
- [ ] Production credentials are configured in a dedicated Mercado Pago application.
- [ ] Approved, pending, rejected, refunded and charged-back payments are tested.
- [ ] Refund and chargeback events revoke or adjust licenses according to a written policy.
- [ ] Terms of sale, refund policy and customer-support contact are published.

### Discord integration

- [x] The bot uses slash commands and the `Guilds` intent only.
- [x] Bot-to-API requests use a separate secret.
- [x] Account linking uses expiring one-time codes.
- [x] The bot never asks for GuildSync or Discord passwords.
- [x] Clone preflight checks required target-server permissions and bot role hierarchy before execution.
- [ ] Link-code abuse limits are tested under sustained traffic.
- [ ] Global command registration is tested after removing the test-guild setting.
- [ ] Bot downtime and API downtime produce clear user-facing states in a hosted integration test.

### Backup, clone and restore engine

- [x] Every object supported by the 0.8 snapshot has a documented schema in `docs/backup-schema-v2.md`.
- [x] Backup v2 integrity, archive limits and legacy compatibility are implemented; format creation/loading is automated in CI.
- [ ] Restore is tested in disposable Discord servers with missing permissions, hierarchy conflicts and protected Community channels.
- [x] Partial failures produce persistent resumable reports instead of silently presenting full success.
- [x] Re-running a saved operation skips completed/reused steps and retries failed work.
- [x] Stable channel matching uses category paths rather than non-portable cross-server parent IDs.
- [x] Post-operation verification reports missing supported roles, channels and emojis.
- [ ] Rate-limit handling is tested with realistic large Discord servers rather than only bounded retry contracts.
- [ ] Backups are encrypted when they contain sensitive community configuration.
- [x] Original and destination server IDs are validated before destructive actions.
- [x] Exact mode creates a preventive backup before deletion and is blocked by failed permission/hierarchy preflight.

### Privacy and operations

- [ ] Privacy policy lists every stored field and retention period.
- [ ] Users can request export and deletion of their account data.
- [ ] Audit-log access is restricted and retention is documented.
- [ ] Production alerts cover API health, database errors, failed webhooks and bot disconnects.
- [ ] Secrets have an owner, rotation schedule and emergency revocation procedure.
- [ ] An external security review is completed before broad public distribution.

## Current architecture constraint

The central API currently stores its logical state as one versioned JSON document. Revision checks prevent silent overwrites, but this architecture intentionally supports only one active API writer. Horizontal scaling must wait for normalized database storage.

## 0.8 rollout constraint

`GUILDSYNC_REQUIRE_DEVICE_ID` must remain `false` while supported 0.7 clients can still connect. The 0.8 client already registers and claims devices. Enable the mandatory server policy only after the update has been distributed and rollback instructions in `docs/release-0.8-rollout.md` have been rehearsed.

## Evidence required

For each unchecked critical item, attach one of:

- an automated test result;
- a reproducible manual test record;
- a provider configuration screenshot with secrets removed;
- a signed operational decision identifying the remaining risk and owner.

An item is not complete merely because the code path exists.
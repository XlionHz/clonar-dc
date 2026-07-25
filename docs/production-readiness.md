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
- [ ] Device registration and device-limit enforcement are implemented end to end.

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
- [ ] Link-code abuse limits are tested under sustained traffic.
- [ ] Global command registration is tested after removing the test-guild setting.
- [ ] Bot downtime and API downtime produce clear user-facing states.

### Backup, clone and restore engine

- [ ] Every supported Discord object has a documented backup schema.
- [ ] Restore is tested in disposable servers with missing permissions and hierarchy conflicts.
- [ ] Partial failures produce a resumable report instead of silently continuing.
- [ ] Re-running an operation is idempotent or clearly warns about duplicates.
- [ ] Rate-limit handling is tested with realistic large servers.
- [ ] Backups are encrypted when they contain sensitive community configuration.
- [ ] Original and destination server IDs are validated before destructive actions.

### Privacy and operations

- [ ] Privacy policy lists every stored field and retention period.
- [ ] Users can request export and deletion of their account data.
- [ ] Audit-log access is restricted and retention is documented.
- [ ] Production alerts cover API health, database errors, failed webhooks and bot disconnects.
- [ ] Secrets have an owner, rotation schedule and emergency revocation procedure.
- [ ] An external security review is completed before broad public distribution.

## Current architecture constraint

The central API currently stores its logical state as one versioned JSON document. Revision checks prevent silent overwrites, but this architecture intentionally supports only one active API writer. Horizontal scaling must wait for normalized database storage.

## Evidence required

For each unchecked critical item, attach one of:

- an automated test result;
- a reproducible manual test record;
- a provider configuration screenshot with secrets removed;
- a signed operational decision identifying the remaining risk and owner.

An item is not complete merely because the code path exists.

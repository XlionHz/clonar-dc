# GuildSync Security Policy

## Supported versions

GuildSync is currently in alpha. Security fixes are applied only to the newest development release.

| Version | Supported |
|---|---|
| 0.7.1 | Yes |
| 0.7.0 and older | No |

## Reporting a vulnerability

Do not open a public issue containing credentials, exploit steps against a live service, personal data or a working proof of concept.

Report privately to the repository owner with:

- the affected component and version;
- the minimum steps needed to reproduce the problem;
- the security impact;
- whether real user data or credentials may be affected;
- suggested remediation, when known.

Remove Discord bot tokens, Mercado Pago credentials, passwords, session tokens and personal data from screenshots or logs.

## Secrets

The following values must exist only in the hosting provider's secret manager or the local operating-system credential store:

- `DISCORD_BOT_TOKEN`;
- `GUILDSYNC_BOT_API_KEY`;
- `MERCADOPAGO_ACCESS_TOKEN`;
- `MERCADOPAGO_WEBHOOK_SECRET`;
- `GUILDSYNC_ADMIN_PASSWORD`;
- production database credentials.

If any secret is exposed, revoke or rotate it immediately. Deleting it from the latest commit is not sufficient because Git history may retain it.

## Operational requirements

- Use HTTPS for every non-local API endpoint.
- Keep unsigned Mercado Pago webhooks disabled.
- Run one GuildSync API writer until persistence is migrated to normalized tables.
- Use PostgreSQL in hosted production.
- Keep the Discord bot and API keys separate.
- Test backup restoration only in disposable Discord servers.
- Sign public Windows installers before a paid launch.
- Retain audit logs and payment-provider evidence according to applicable privacy and accounting requirements.

## Scope limitations

GuildSync is designed for Discord communities that the operator owns or is authorized to administer. User tokens, self-bots, credential collection, message scraping and bypassing Discord permissions are outside the supported product scope.

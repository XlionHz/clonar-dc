# GuildSync

GuildSync is a Windows desktop application for backing up, cloning and synchronizing Discord server structures through the official Discord API.

> Current development release: **0.7.1**

## What it does

- creates structured backups of Discord communities;
- restores or clones supported server structures;
- handles roles, categories, channels, permissions and emojis;
- stores the bot token locally instead of sending it to the GuildSync API;
- supports central accounts, licenses and Mercado Pago checkout;
- links a Discord account through a short-lived one-time code;
- includes an administration panel, audit history and automatic update packages.

GuildSync is intended for communities that the operator owns or is authorized to manage. It does not use user tokens, self-bots or message-content access.

## Components

| Component | Technology | Responsibility |
|---|---|---|
| Desktop | .NET 10 / WPF | User interface, backup, clone, restore and local bot-token handling |
| Central API | ASP.NET Core / .NET 10 | Accounts, sessions, licenses, payments, audit and Discord linking |
| Discord bot | Discord.Net / .NET 10 | Slash commands for linking, license status and checkout |
| Persistence | PostgreSQL or protected local JSON | Central state and local-development fallback |
| Installer | Inno Setup | Per-user Windows installation and upgrades |

The executable remains internally named `ClonarDC.exe` so existing installations can upgrade without becoming a second application. Product-facing names, icons and installer entries use **GuildSync**.

## Security model

- passwords are stored as PBKDF2 hashes with individual random salts;
- access tokens are random, stored only as SHA-256 hashes and expire automatically;
- suspended or revoked accounts lose active sessions immediately;
- login and registration use sliding-window rate limits;
- bot-to-API requests require a separate secret with fixed-time comparison;
- payment webhooks are signed and confirmed through Mercado Pago before activating a license;
- production refuses accidental file storage unless it is explicitly enabled;
- PostgreSQL writes use optimistic revision checks to prevent silent overwrites by multiple API instances;
- secrets are scanned by the pull-request quality workflow.

See [SECURITY.md](SECURITY.md) and [docs/production-readiness.md](docs/production-readiness.md).

## Local build

Requirements:

- .NET SDK 10;
- Windows for building and running the WPF desktop;
- Inno Setup 6 for generating the installer.

```powershell
dotnet restore .\src\ClonarDC.Desktop\ClonarDC.Desktop.csproj
dotnet restore .\src\ClonarDC.Server\ClonarDC.Server.csproj
dotnet restore .\src\GuildSync.Bot\GuildSync.Bot.csproj

dotnet build .\src\ClonarDC.Desktop\ClonarDC.Desktop.csproj -c Release
dotnet build .\src\ClonarDC.Server\ClonarDC.Server.csproj -c Release
dotnet build .\src\GuildSync.Bot\GuildSync.Bot.csproj -c Release
```

Copy `.env.example` values into the hosting provider's secret manager. Never commit real tokens, passwords or webhook secrets.

## Deployment

The repository includes:

- `Dockerfile` for the central API;
- `Dockerfile.bot` for the Discord bot;
- `render.yaml` for the API and PostgreSQL staging environment;
- `docs/discord-bot-setup.md` for secure bot configuration;
- `scripts/Test-GuildSyncApi.ps1` for behavior smoke tests.

The public API URL currently remains `https://clonar-dc-api.onrender.com` for backward compatibility. A future custom domain can replace it without changing the desktop binary by updating `api-url.txt`, the current-user registry value or `GUILDSYNC_API`.

## Validation

Pull requests build all three projects and test:

- API startup and version;
- registration and login;
- logout token revocation;
- immediate revocation after administrative suspension;
- rejection of unauthenticated bot requests;
- source contracts and secret scanning.

The main-branch release workflow additionally builds and smoke-tests the Windows application, installer and update package.

## Status

GuildSync is still an alpha product. Before a public paid launch, complete the checklist in [docs/production-readiness.md](docs/production-readiness.md), especially code signing, external security review, restore tests and production payment verification.

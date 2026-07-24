# GuildSync Discord bot setup

The official bot source lives in `src/GuildSync.Bot`. It uses slash commands only and does not request message-content access.

## 1. Discord application

1. Open the Discord Developer Portal and create an application named **GuildSync**.
2. In **Bot**, create the bot user and upload the GuildSync symbol as its avatar.
3. Reset/copy the bot token once and store it only in the hosting provider as `DISCORD_BOT_TOKEN`.
4. Under OAuth2 installation, enable the `bot` and `applications.commands` scopes.
5. Invite the bot first to a private test server.
6. Do not enable privileged intents; the current bot needs only the `Guilds` gateway intent.

Never commit the bot token, API key, passwords, or webhook secrets to GitHub and never paste them into chat.

## 2. Shared bot API key

Create one random secret of at least 32 characters. Store the same value in two places:

- the central API service: `GUILDSYNC_BOT_API_KEY`;
- the bot service: `GUILDSYNC_BOT_API_KEY`.

The bot sends this key in the private `X-GuildSync-Bot-Key` request header. The central API compares it in fixed time. This key is separate from the Discord bot token.

## 3. Bot environment variables

- `DISCORD_BOT_TOKEN`: private token from the Discord Developer Portal. Legacy alias: `GUILDSYNC_DISCORD_BOT_TOKEN`.
- `GUILDSYNC_BOT_API_KEY`: private shared key used only between the bot and API.
- `GUILDSYNC_API_URL`: central HTTPS API URL. Current staging value: `https://clonar-dc-api.onrender.com`.
- `DISCORD_TEST_GUILD_ID`: optional private test-server ID. Legacy alias: `GUILDSYNC_DISCORD_TEST_GUILD_ID`.

When `DISCORD_TEST_GUILD_ID` is present, commands are registered only in that test server. Remove it only after testing, so the bot registers the commands globally.

## 4. Commands

- `/guildsync` and `/help`: command guide.
- `/plans`: reads currently enabled plans from the central API.
- `/link`: generates an eight-character one-time code that expires after ten minutes.
- `/license`: displays the license linked to the caller's Discord account.
- `/buy plan:`: creates a checkout for a linked GuildSync account.
- `/unlink`: disconnects the Discord account from GuildSync.

## 5. Secure linking flow

1. The user runs `/link` in Discord.
2. The bot requests a one-time code from the central API.
3. The user signs in to the GuildSync desktop app.
4. In **Settings → Connect Discord**, the user enters the code.
5. The API consumes the code once and links the Discord user ID to the authenticated GuildSync account.

The bot never asks for a GuildSync password, Discord password, user token, card number, or Mercado Pago credential.

## 6. Hosting

Build with `Dockerfile.bot`. The bot maintains a Discord Gateway connection, so deploy it as an always-running worker/container rather than a web service that sleeps when idle. The API remains a separate service and the bot never receives database credentials.

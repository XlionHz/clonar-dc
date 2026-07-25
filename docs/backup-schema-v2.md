# GuildSync backup schema v2

GuildSync backup files use the `.cdbak` extension for upgrade compatibility. The file is a ZIP archive with bounded entry counts and decompressed sizes.

## Archive entries

### `manifest.json`

Metadata used before the full snapshot is parsed:

| Field | Type | Purpose |
|---|---|---|
| `formatVersion` | integer | Must be `2` for this format. |
| `id` | string | Random backup identifier. |
| `createdAt` | ISO-8601 timestamp | UTC creation time. |
| `appVersion` | string | GuildSync version that created the file. |
| `name` | string | User-facing backup name. |
| `description` | nullable string | Optional backup context. |
| `tags` | string array | Optional local classification tags. |
| `snapshotEntry` | string | Must point to the snapshot entry, currently `snapshot.json`. |
| `payloadSha256` | 64-character hexadecimal string | SHA-256 of the uncompressed snapshot bytes. |
| `payloadBytes` | integer | Expected uncompressed snapshot size. |
| `protection` | string | Currently `integrity-only`; encryption is not claimed. |

### `snapshot.json`

The structural snapshot. SHA-256 is verified in fixed time before deserialization.

## Guild snapshot

| Field | Type | Notes |
|---|---|---|
| `schemaVersion` | integer | `2` for snapshots created by GuildSync 0.8. |
| `createdAt` | ISO-8601 timestamp | Snapshot capture time. |
| `sourceGuildId` | Discord snowflake string | Original server ID; used for validation and audit, never reused as destination. |
| `name` | string | Server name. |
| `iconData` | nullable data URI | Server icon when available and within capture limits. |
| `roles` | role array | Includes captured guild roles; managed roles are retained for analysis but not cloned. |
| `channels` | channel array | Guild channels returned by the official API; active threads are not included. |
| `emojis` | emoji array | Custom guild emojis and captured assets when available. |

## Role snapshot

| Field | Type | Notes |
|---|---|---|
| `id` | snowflake string | Source mapping key. |
| `name` | string | Logical matching key after normalization. |
| `permissions` | decimal string | Discord variable-length permission bitset. |
| `color` | integer | Legacy primary RGB color returned by Discord. |
| `hoist` | boolean | Display role separately. |
| `mentionable` | boolean | Role mentionability. |
| `managed` | boolean | Managed roles are not created, edited or deleted. |
| `position` | integer | Used for ordering below the bot's highest role. |

## Channel snapshot

| Field | Type | Notes |
|---|---|---|
| `id` | snowflake string | Source mapping key. |
| `name` | string | Channel/category name. |
| `type` | integer | Discord channel type. GuildSync 0.8 handles categories, text, announcement, voice, stage, forum and media properties supported by the create/modify endpoints. |
| `parentId` | nullable snowflake string | Source parent ID, used only inside the source snapshot mapping. |
| `parentName` | nullable string | Portable category path used by stable matching across servers. |
| `position` | integer | Relative ordering position. |
| `topic` | nullable string | Text/announcement/forum/media topic. |
| `nsfw` | boolean | Age-restricted channel flag where supported. |
| `bitrate` | nullable integer | Voice/stage bitrate. |
| `userLimit` | nullable integer | Voice/stage user limit. |
| `rateLimitPerUser` | nullable integer | Slowmode where supported. |
| `permissionOverwrites` | overwrite array | Role overwrites are remapped; member-specific overwrites are reported and skipped. |

Stable matching does **not** compare `parentId` between servers. The logical key is channel type + normalized `parentName` + normalized channel name.

## Permission overwrite snapshot

| Field | Type | Notes |
|---|---|---|
| `id` | snowflake string | Source role/member ID. |
| `type` | integer | `0` for role, `1` for member. |
| `allow` | decimal string | Allowed permission bitset. |
| `deny` | decimal string | Denied permission bitset. |

Only role overwrites can be portably applied because unrelated servers do not share member IDs.

## Emoji snapshot

| Field | Type | Notes |
|---|---|---|
| `id` | snowflake string | Source emoji ID. |
| `name` | string | Logical matching key. |
| `animated` | boolean | Determines the CDN asset format. |
| `imageData` | nullable data URI | Required to recreate the emoji. Missing assets are reported and skipped. |

## Compatibility and validation

- GuildSync 0.8 opens legacy 0.7 backups whose complete envelope was stored inside `manifest.json`.
- Legacy files are copied into the new GuildSync backup folder without deleting the originals.
- Unknown format or snapshot versions are rejected.
- Oversized archives, manifests, snapshots, excessive entry counts and invalid hashes are rejected before structural execution.
- A valid hash means the file is internally intact; it does not prove who created it. Code signing/encrypted backups remain separate future work.
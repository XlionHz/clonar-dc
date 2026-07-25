$ErrorActionPreference = 'Stop'

function Require-Text {
    param([string]$Path, [string[]]$Values)
    $content = Get-Content $Path -Raw
    foreach ($value in $Values) {
        if (-not $content.Contains($value)) {
            throw "$Path is missing required contract: $value"
        }
    }
}

Require-Text 'src/ClonarDC.Desktop/ClonarDC.Desktop.csproj' @('<Version>0.7.1</Version>', '<Product>GuildSync</Product>')
Require-Text 'src/ClonarDC.Server/ClonarDC.Server.csproj' @('<Version>0.7.1</Version>', '<Product>GuildSync API</Product>')
Require-Text 'src/GuildSync.Bot/GuildSync.Bot.csproj' @('<Version>0.7.1</Version>', '<Product>GuildSync Bot</Product>')
Require-Text 'installer/ClonarDC.iss' @('MyAppName "GuildSync"', 'MyAppVersion "0.7.1"', 'GuildSync-Setup', 'GuildSync.ico')
Require-Text 'src/ClonarDC.Server/Program.cs' @('/auth/logout', '/auth/logout-all', 'SlidingWindowLimiter', 'suspended', 'RevokeAllSessionsAsync', 'Content-Security-Policy')
Require-Text 'src/ClonarDC.Server/StatePersistence.cs' @('GUILDSYNC_ALLOW_FILE_STORAGE', 'pg_advisory_xact_lock', 'concurrent database writer', 'revision bigint')
Require-Text 'src/GuildSync.Bot/Program.cs' @('WithName("status")', 'GuildSync-Bot/0.7.1', 'AllowedMentions.None', 'Error ID')
Require-Text 'render.yaml' @('GUILDSYNC_ENV', 'GUILDSYNC_ALLOW_FILE_STORAGE', 'MERCADOPAGO_ALLOW_UNSIGNED_WEBHOOKS', 'GUILDSYNC_PRICE_1M')

$trackedText = git ls-files | Where-Object {
    $_ -notmatch '\.(png|ico|exe|zip|dll|pdb|jpeg|jpg|gif|webp)$'
}
foreach ($file in $trackedText) {
    if (-not (Test-Path $file -PathType Leaf)) { continue }
    $content = Get-Content $file -Raw -ErrorAction SilentlyContinue
    if ($null -eq $content) { continue }
    if ($content -match 'APP_USR-[A-Za-z0-9_-]{20,}') {
        throw "Possible Mercado Pago credential committed in $file"
    }
    if ($content -match '(?im)^DISCORD_BOT_TOKEN=\S{20,}$') {
        throw "Possible Discord bot token committed in $file"
    }
    if ($content -match '(?im)^MERCADOPAGO_ALLOW_UNSIGNED_WEBHOOKS=true$') {
        throw "Unsigned Mercado Pago webhooks enabled in $file"
    }
}

Write-Host 'GuildSync source contracts and secret scan passed.'

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

Require-Text 'src/ClonarDC.Desktop/ClonarDC.Desktop.csproj' @('<Version>0.8.1</Version>', '<Product>GuildSync</Product>')
Require-Text 'src/ClonarDC.Server/ClonarDC.Server.csproj' @('<Version>0.8.1</Version>', '<Product>GuildSync API</Product>')
Require-Text 'src/GuildSync.Bot/GuildSync.Bot.csproj' @('<Version>0.8.1</Version>', '<Product>GuildSync Bot</Product>')
Require-Text 'installer/ClonarDC.iss' @('MyAppName "GuildSync"', 'MyAppVersion "0.8.1"', 'GuildSync-Setup', 'GuildSync.ico')
Require-Text 'src/ClonarDC.Server/Program.cs' @('/auth/logout', '/auth/logout-all', 'SlidingWindowLimiter', 'suspended', 'RevokeAllSessionsAsync', 'Content-Security-Policy', 'DevicePolicy.RequireIdentity', 'deviceCount', 'bootstrap-admin-synchronized')
Require-Text 'src/ClonarDC.Server/DeviceManagement.cs' @('/devices/claim', '/devices/{deviceId}', 'DeviceIdHash', 'FixedTimeDeviceHashEquals', 'ResetDevicesAsync')
Require-Text 'src/ClonarDC.Server/StatePersistence.cs' @('GUILDSYNC_ALLOW_FILE_STORAGE', 'pg_advisory_xact_lock', 'concurrent database writer', 'revision bigint')
Require-Text 'src/ClonarDC.Desktop/BrandPresentation.cs' @('ShieldGeometry', 'LetterGeometry', 'CreateMark', 'CreateSidebarHeader')
Require-Text 'src/ClonarDC.Desktop/BrandIcon.cs' @('DrawingImage', 'Shield', 'Letter', 'LinearGradientBrush')
Require-Text 'src/ClonarDC.Desktop/LoginWindow.xaml' @('BrandMarkHost', 'LoginCard', 'GhostCardBack', 'Welcome back', 'BackendModeText', 'LoginIntroStoryboard')
Require-Text 'src/ClonarDC.Desktop/LoginWindow.xaml.cs' @('LOCAL DEVELOPER MODE', 'Central administrator access is unavailable', 'bootstrapDeveloper: true', 'LoginIntroStoryboard')
Require-Text 'src/ClonarDC.Desktop/AlphaFixes.cs' @('ContentSource="SelectedContent"', 'BrandPresentation.CreateSidebarHeader', 'Pages.SelectedIndex = 0', 'MaterializeTypedServer')
Require-Text 'src/ClonarDC.Desktop/MainWindow.xaml' @('Text="Token"', 'IsEditable="True"', 'SourceGuildBox', 'TargetGuildBox', 'SidebarBrandHost')
Require-Text 'src/ClonarDC.Desktop/MainWindow.TokenValidation.cs' @('does not classify, normalize or reject', '_discord.SetToken(rawValue)', 'accepted exactly as entered')
Require-Text 'src/ClonarDC.Desktop/MainWindow.ServerInputs.cs' @('MaterializeTypedServer', 'RequireEditableGuild', 'Enter or select the')
Require-Text 'src/ClonarDC.Desktop/RegisterWindow.xaml' @('RegisterBrandHost', 'Create GuildSync account')
Require-Text 'src/ClonarDC.Desktop/PublishUpdateWindow.xaml' @('PublishBrandHost', 'GuildSync installation')
Require-Text 'src/ClonarDC.Desktop/TextConfirmWindow.xaml' @('ConfirmBrandHost', 'GuildSync')
Require-Text 'src/ClonarDC.Desktop/SecondaryWindowBranding.cs' @('RegisterBrandHost', 'PublishBrandHost', 'ConfirmBrandHost')
Require-Text 'src/ClonarDC.Desktop/Services/GuildSyncEngine.cs' @('CloneExecutionReport', 'VerifySnapshot', 'ChannelKey', 'SendWithRetryAsync', 'completed-with-differences')
Require-Text 'src/ClonarDC.Desktop/Services/DiscordPreflightService.cs' @(
    'ManageChannels',
    'ManageRoles',
    'ManageGuildExpressions',
    'HighestRolePosition',
    'guilds/{guildId}/members/{botId}',
    'CreateGuildExpressions',
    'COMMUNITY',
    'sourceRolePermissions'
)
Require-Text 'src/ClonarDC.Desktop/Services/BackupService.cs' @('FormatVersion = 2', 'snapshot.json', 'MaximumArchiveBytes', 'LoadLegacyEnvelope', 'CryptographicOperations.FixedTimeEquals')
Require-Text 'src/ClonarDC.Desktop/Services/OperationReportService.cs' @('FindLatestResumableAsync', 'SaveAsync', 'ExportSummaryAsync')
Require-Text 'src/ClonarDC.Desktop/Services/DeviceIdentityService.cs' @('RandomNumberGenerator.GetBytes(32)', 'SecureTokenStore', 'DeviceIdentity')
Require-Text 'src/GuildSync.Bot/Program.cs' @('WithName("status")', 'AllowedMentions.None', 'Error ID')
Require-Text 'render.yaml' @('GUILDSYNC_ENV', 'GUILDSYNC_ALLOW_FILE_STORAGE', 'GUILDSYNC_REQUIRE_DEVICE_ID', 'MERCADOPAGO_ALLOW_UNSIGNED_WEBHOOKS', 'GUILDSYNC_PRICE_1M')
Require-Text '.github/workflows/final-release.yml' @("APP_VERSION: '0.8.1'", 'Test-EngineReliability.ps1', 'Run API and device behavior smoke test')

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
    if ($content -match '(?im)^GUILDSYNC_REQUIRE_DEVICE_ID=true$' -and $file -notmatch 'Test-GuildSyncApi\.ps1$') {
        throw "Mandatory device identity must not be enabled by default in $file during the staged 0.8 rollout."
    }
}

Write-Host 'GuildSync 0.8.1 source contracts and secret scan passed.'
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

function Forbid-Text {
    param([string]$Path, [string[]]$Values)
    $content = Get-Content $Path -Raw
    foreach ($value in $Values) {
        if ($content.Contains($value)) {
            throw "$Path contains forbidden regression: $value"
        }
    }
}

$version = '0.8.3.3'
$installerVersionContract = 'MyAppVersion "' + $version + '"'
Require-Text 'src/ClonarDC.Desktop/ClonarDC.Desktop.csproj' @("<Version>$version</Version>", '<Product>GuildSync</Product>', '<InformationalVersion>0.8.3.3-provider-separation</InformationalVersion>', '<Content Include="token-policy.json">')
Require-Text 'src/ClonarDC.Server/ClonarDC.Server.csproj' @("<Version>$version</Version>", '<Product>GuildSync API</Product>')
Require-Text 'src/GuildSync.Bot/GuildSync.Bot.csproj' @("<Version>$version</Version>", '<Product>GuildSync Bot</Product>')
Require-Text 'installer/ClonarDC.iss' @('MyAppName "GuildSync"', $installerVersionContract, 'GuildSync-Setup', 'GuildSync.ico')
Require-Text 'src/ClonarDC.Server/Program.cs' @('/auth/logout', '/auth/logout-all', 'SlidingWindowLimiter', 'RevokeAllSessionsAsync', 'DevicePolicy.RequireIdentity', 'bootstrap-admin-synchronized', 'assemblyVersion.Revision')
Require-Text 'src/ClonarDC.Server/DeviceManagement.cs' @('/devices/claim', '/devices/{deviceId}', 'DeviceIdHash', 'ResetDevicesAsync')
Require-Text 'src/ClonarDC.Server/StatePersistence.cs' @('GUILDSYNC_ALLOW_FILE_STORAGE', 'pg_advisory_xact_lock', 'concurrent database writer')

Require-Text 'src/ClonarDC.Desktop/BrandPresentation.cs' @('ShieldGeometry', 'LetterGeometry', 'CreateMark', 'CreateSidebarHeader')
Require-Text 'src/ClonarDC.Desktop/LoginWindow.xaml' @('LoginIntroStoryboard', 'BrandMarkHost', 'LoginCard', 'Welcome back')
Require-Text 'src/ClonarDC.Desktop/LoginWindow.xaml.cs' @('AnimateLoginCard(1.025', 'UsedLocalFallback', 'LOCAL PREVIEW MODE', 'CanUseLocalPreviewFallback')
Require-Text 'src/ClonarDC.Desktop/RegisterWindow.xaml.cs' @('RegisterWithRecoveryAsync', 'LocalPreviewUrl', 'UsedLocalFallback')
Require-Text 'src/ClonarDC.Desktop/MainWindow.xaml' @('Text="Token"', 'IsEditable="True"', 'ProviderStatusText', 'AutomationProperties.AutomationId="LoadServers"')
Require-Text 'src/ClonarDC.Desktop/MainWindow.TokenValidation.cs' @('_discordProviders.SetCredential(rawValue)', 'ActivatePreviewServers', 'GetSimulatedGuilds', 'Value accepted and saved without restrictive validation')
Forbid-Text 'src/ClonarDC.Desktop/MainWindow.TokenValidation.cs' @('MessageBox.Show', 'PreviewGuilds')
Require-Text 'src/ClonarDC.Desktop/MainWindow.Discord.cs' @('AnalyzeCompatible_Click', 'CloneCompatible_Click', 'CreateSimulatedAnalysis', 'RunSimulatedOperationAsync')
Forbid-Text 'src/ClonarDC.Desktop/MainWindow.Discord.cs' @('MessageBox.Show', 'CreatePreviewSnapshot')
Require-Text 'src/ClonarDC.Desktop/MainWindow.ServerInputs.cs' @('MaterializeTypedServer', 'RequireEditableGuild')
Require-Text 'src/ClonarDC.Desktop/AlphaFixes.cs' @('ContentSource="SelectedContent"', 'Pages.SelectedIndex = 0')

Require-Text 'src/ClonarDC.Desktop/Services/DiscordDataProviders.cs' @('DiscordProviderCoordinator', 'SimulatedDiscordDataProvider', 'RuntimeTokenPolicy', 'GUILDSYNC_REQUIRE_OFFICIAL_PROVIDER', 'GUILDSYNC_FORCE_SIMULATION', 'SIMULATED PROVIDER')
Require-Text 'src/ClonarDC.Desktop/Services/TokenAuthorization.cs' @('new AuthenticationHeaderValue("Bot"', 'never emits a user-token')
Require-Text 'src/ClonarDC.Desktop/Services/SecureTokenStore.cs' @('StorageMarker', 'Save the exact value', 'Backward compatibility')
Require-Text 'src/ClonarDC.Desktop/token-policy.json' @('"requireOfficialProvider": false')
if (Test-Path 'src/ClonarDC.Desktop/Services/DiscordConnectionProbe.cs') { throw 'Legacy gateway credential probe must not ship.' }

Require-Text 'src/ClonarDC.Desktop/Services/GuildSyncEngine.cs' @('CloneExecutionReport', 'VerifySnapshot', 'ChannelKey', 'SendWithRetryAsync', 'completed-with-differences')
Require-Text 'src/ClonarDC.Desktop/Services/DiscordPreflightService.cs' @('ManageChannels', 'ManageRoles', 'HighestRolePosition', 'guilds/{guildId}/members/{botId}', 'COMMUNITY')
Require-Text 'src/ClonarDC.Desktop/Services/BackupService.cs' @('FormatVersion = 2', 'snapshot.json', 'MaximumArchiveBytes', 'LoadLegacyEnvelope')
Require-Text 'src/ClonarDC.Desktop/Services/OperationReportService.cs' @('FindLatestResumableAsync', 'SaveAsync', 'ExportSummaryAsync')
Require-Text 'src/GuildSync.Bot/Program.cs' @('WithName("status")', 'AllowedMentions.None', 'Error ID')
Require-Text 'src/GuildSync.DesktopFlowTests/Program.cs' @('exact arbitrary credential persistence', 'complete simulated server loading', 'future runtime policy switch')
Require-Text '.github/workflows/final-release.yml' @("APP_VERSION: '$version'", 'Test-UiButtonContracts.ps1', 'GuildSync.DesktopFlowTests', 'Uninstall smoke test', 'manifest.json', 'Permanent release is missing asset')

foreach ($junk in 'TEST_TREE_PLACEHOLDER.txt','TREE_TEST_A.txt','TREE_TEST_B.txt','TREE_TEST_C.txt','TREE_TEST_D.txt','TREE_TEST_E.txt','TREE_TEST_F.txt','TREE_TEST_G.txt','build-status/compile-error.txt','build-status/backend-error.txt') {
    if (Test-Path $junk) { throw "Temporary or failed-build file is still tracked: $junk" }
}

$trackedText = git ls-files | Where-Object { $_ -notmatch '\.(png|ico|exe|zip|dll|pdb|jpeg|jpg|gif|webp)$' }
foreach ($file in $trackedText) {
    if (-not (Test-Path $file -PathType Leaf)) { continue }
    $content = Get-Content $file -Raw -ErrorAction SilentlyContinue
    if ($null -eq $content) { continue }
    if ($content -match 'APP_USR-[A-Za-z0-9_-]{20,}') { throw "Possible Mercado Pago credential committed in $file" }
    if ($content -match '(?im)^DISCORD_BOT_TOKEN=\S{20,}$') { throw "Possible Discord bot token committed in $file" }
    if ($content -match '(?im)^MERCADOPAGO_ALLOW_UNSIGNED_WEBHOOKS=true$') { throw "Unsigned Mercado Pago webhooks enabled in $file" }
    if ($content -match '(?im)^GUILDSYNC_REQUIRE_DEVICE_ID=true$' -and $file -notmatch 'Test-GuildSyncApi\.ps1$') {
        throw "Mandatory device identity must not be enabled by default in $file during the staged rollout."
    }
}

Write-Host "GuildSync $version provider-separation source contracts and secret scan passed."

param(
    [string]$DesktopRoot = "src/ClonarDC.Desktop"
)

$ErrorActionPreference = 'Stop'
$xamlFiles = Get-ChildItem $DesktopRoot -Filter '*.xaml' -File
$code = (Get-ChildItem $DesktopRoot -Filter '*.cs' -File | Get-Content -Raw) -join "`n"
$handlers = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)

foreach ($file in $xamlFiles) {
    $raw = Get-Content $file.FullName -Raw
    foreach ($match in [regex]::Matches($raw, 'Click="([A-Za-z_][A-Za-z0-9_]*)"')) {
        [void]$handlers.Add($match.Groups[1].Value)
    }
}

$missing = @()
foreach ($handler in $handlers) {
    if ($code -notmatch "\b$([regex]::Escape($handler))\s*\(") {
        $missing += $handler
    }
}
if ($missing.Count -gt 0) {
    throw "XAML button handlers are missing: $($missing -join ', ')"
}

$mainXaml = Get-Content "$DesktopRoot/MainWindow.xaml" -Raw
foreach ($automationId in 'TokenInput','SaveToken','LoadServers','Analyze','Start','Resume','Cancel') {
    $automationPattern = [regex]::Escape(('AutomationProperties.AutomationId="{0}"' -f $automationId))
    if ($mainXaml -notmatch $automationPattern) {
        throw "Required UI automation contract is missing: $automationId"
    }
}

foreach ($nonModalFile in "$DesktopRoot/MainWindow.TokenValidation.cs", "$DesktopRoot/MainWindow.Discord.cs") {
    $raw = Get-Content $nonModalFile -Raw
    if ($raw -match 'MessageBox\.Show|SystemSounds|Console\.Beep') {
        throw "Non-modal token/simulation flow contains a native alert: $nonModalFile"
    }
}

if (Test-Path "$DesktopRoot/Services/DiscordConnectionProbe.cs") {
    throw 'Legacy gateway credential probe is still present.'
}

$providerSource = Get-Content "$DesktopRoot/Services/DiscordDataProviders.cs" -Raw
foreach ($contract in 'DiscordProviderCoordinator','SimulatedDiscordDataProvider','RuntimeTokenPolicy','GUILDSYNC_REQUIRE_OFFICIAL_PROVIDER') {
    if ($providerSource -notmatch [regex]::Escape($contract)) {
        throw "Provider separation contract is missing: $contract"
    }
}

$policy = Get-Content "$DesktopRoot/token-policy.json" -Raw | ConvertFrom-Json
if ($policy.requireOfficialProvider -ne $false) {
    throw 'The shipped token policy must remain permissive in this release.'
}

"GuildSync UI contracts passed: $($handlers.Count) XAML click handlers resolved; provider separation and non-modal token flow verified."

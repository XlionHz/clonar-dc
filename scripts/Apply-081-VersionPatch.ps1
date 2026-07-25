$ErrorActionPreference = 'Stop'

function Replace-IfPresent {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Old,
        [Parameter(Mandatory = $true)][string]$New
    )

    $content = Get-Content $Path -Raw
    if (-not $content.Contains($Old)) {
        Write-Host "$Path has no remaining '$Old' value."
        return
    }

    Set-Content $Path ($content.Replace($Old, $New)) -Encoding UTF8
    Write-Host "$Path updated to $New"
}

$files = @(
    'src/ClonarDC.Desktop/ClonarDC.Desktop.csproj',
    'src/ClonarDC.Server/ClonarDC.Server.csproj',
    'src/GuildSync.Bot/GuildSync.Bot.csproj',
    'installer/ClonarDC.iss',
    'src/ClonarDC.Desktop/Services/AuthClient.cs',
    'src/ClonarDC.Desktop/Services/DiscordPreflightService.cs',
    'src/ClonarDC.Desktop/Services/GuildSyncEngine.cs',
    'src/ClonarDC.Desktop/Services/BackupService.cs',
    'src/ClonarDC.Desktop/Models.cs',
    'src/ClonarDC.Server/Program.cs',
    'src/GuildSync.Bot/Program.cs',
    'scripts/Test-SourceContracts.ps1',
    'scripts/Test-GuildSyncApi.ps1',
    '.github/workflows/final-release.yml',
    '.github/workflows/pull-request-quality.yml'
)

foreach ($file in $files) {
    Replace-IfPresent -Path $file -Old '0.8.0' -New '0.8.1'
}

Replace-IfPresent -Path 'README.md' -Old '> Current development release: **0.8.0 alpha**' -New '> Current development release: **0.8.1 alpha**'

Write-Host 'GuildSync runtime, installer and CI versions are aligned to 0.8.1.'

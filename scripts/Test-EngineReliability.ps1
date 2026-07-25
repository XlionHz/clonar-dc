param(
    [string]$DesktopAssembly = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($DesktopAssembly)) {
    $desktopBin = Join-Path $PSScriptRoot '../src/ClonarDC.Desktop/bin/Release'
    $candidate = Get-ChildItem $desktopBin -Recurse -Filter 'ClonarDC.dll' -File |
        Where-Object { $_.FullName -notmatch '\\ref\\' } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $candidate) {
        throw "ClonarDC.dll was not found under $desktopBin after the desktop build."
    }
    $DesktopAssembly = $candidate.FullName
}

$assemblyPath = (Resolve-Path $DesktopAssembly).Path
Write-Host "Testing desktop assembly: $assemblyPath"
$assembly = [System.Reflection.Assembly]::LoadFrom($assemblyPath)

function New-TypeInstance([string]$name) {
    $type = $assembly.GetType($name, $true)
    return [System.Activator]::CreateInstance($type)
}

$engineType = $assembly.GetType('ClonarDC.Services.GuildSyncEngine', $true)
$channelType = $assembly.GetType('ClonarDC.ChannelSnapshot', $true)
$channelKey = $engineType.GetMethod('ChannelKey', [System.Reflection.BindingFlags]'NonPublic,Static')
if ($null -eq $channelKey) { throw 'GuildSyncEngine.ChannelKey was not found.' }

$first = [System.Activator]::CreateInstance($channelType)
$first.Id = '100000000000000001'
$first.Name = 'general'
$first.Type = 0
$first.ParentId = '100000000000000010'
$first.ParentName = 'Community'

$second = [System.Activator]::CreateInstance($channelType)
$second.Id = '200000000000000001'
$second.Name = 'general'
$second.Type = 0
$second.ParentId = '200000000000000010'
$second.ParentName = 'Community'

$keyOne = $channelKey.Invoke($null, [object[]]@($first))
$keyTwo = $channelKey.Invoke($null, [object[]]@($second))
if ($keyOne -ne $keyTwo) {
    throw "Stable channel keys still depend on cross-server IDs: '$keyOne' vs '$keyTwo'."
}

$third = [System.Activator]::CreateInstance($channelType)
$third.Id = '300000000000000001'
$third.Name = 'general'
$third.Type = 0
$third.ParentName = 'Staff'
$keyThree = $channelKey.Invoke($null, [object[]]@($third))
if ($keyOne -eq $keyThree) { throw 'Channels in different category paths produced the same key.' }

$snapshot = New-TypeInstance 'ClonarDC.GuildSnapshot'
$snapshot.SourceGuildId = '123456789012345678'
$snapshot.Name = 'Reliability Test Guild'
$category = New-TypeInstance 'ClonarDC.ChannelSnapshot'
$category.Id = '123456789012345679'
$category.Name = 'Community'
$category.Type = 4
$category.Position = 0
$snapshot.Channels.Add($category)
$channel = New-TypeInstance 'ClonarDC.ChannelSnapshot'
$channel.Id = '123456789012345680'
$channel.Name = 'general'
$channel.Type = 0
$channel.ParentId = $category.Id
$channel.ParentName = $category.Name
$channel.Position = 1
$snapshot.Channels.Add($channel)

$backupService = New-TypeInstance 'ClonarDC.Services.BackupService'
$saveTask = $backupService.SaveAsync($snapshot, 'GuildSync CI backup', 'Engine reliability test', [string[]]@('ci','v2'), [System.Threading.CancellationToken]::None)
$backupPath = $saveTask.GetAwaiter().GetResult()
try {
    $archive = [System.IO.Compression.ZipFile]::OpenRead($backupPath)
    try {
        $names = @($archive.Entries | ForEach-Object FullName)
        if ($names -notcontains 'manifest.json' -or $names -notcontains 'snapshot.json') {
            throw 'Backup v2 does not contain manifest.json and snapshot.json.'
        }
    }
    finally { $archive.Dispose() }

    $loadTask = $backupService.LoadAsync($backupPath, [System.Threading.CancellationToken]::None)
    $loaded = $loadTask.GetAwaiter().GetResult()
    if ($loaded.FormatVersion -ne 2) { throw "Expected backup format 2, got $($loaded.FormatVersion)." }
    if ($loaded.Snapshot.Channels.Count -ne 2) { throw 'Backup v2 did not preserve the snapshot channels.' }
    if ([string]::IsNullOrWhiteSpace($loaded.PayloadSha256)) { throw 'Backup v2 did not preserve its integrity hash.' }
}
finally {
    Remove-Item $backupPath -Force -ErrorAction SilentlyContinue
}

$reportService = New-TypeInstance 'ClonarDC.Services.OperationReportService'
$report = New-TypeInstance 'ClonarDC.CloneExecutionReport'
$report.SourceGuildId = '123456789012345678'
$report.SourceGuildName = 'Source'
$report.TargetGuildId = '223456789012345678'
$report.TargetGuildName = 'Target'
$report.Mode = 'merge'
$report.Status = 'failed'
$step = New-TypeInstance 'ClonarDC.CloneOperationStep'
$step.Key = 'channel:123'
$step.Kind = 'channel'
$step.Name = 'general'
$step.Status = 'failed'
$report.Steps.Add($step)

$reportPath = $reportService.SaveAsync($report, [System.Threading.CancellationToken]::None).GetAwaiter().GetResult()
try {
    $loadedReport = $reportService.LoadAsync($reportPath, [System.Threading.CancellationToken]::None).GetAwaiter().GetResult()
    if (-not $loadedReport.CanResume) { throw 'Failed operation report is not resumable.' }
    if ($loadedReport.FailedSteps -ne 1) { throw 'Failed step count was not preserved.' }
    $found = $reportService.FindLatestResumableAsync(
        $report.SourceGuildId,
        $report.TargetGuildId,
        $report.Mode,
        [System.Threading.CancellationToken]::None).GetAwaiter().GetResult()
    if ($null -eq $found -or $found.Id -ne $report.Id) { throw 'Latest resumable checkpoint was not found.' }
}
finally {
    Remove-Item $reportPath -Force -ErrorAction SilentlyContinue
}

Write-Host "GuildSync engine reliability tests passed. Stable key: $keyOne"

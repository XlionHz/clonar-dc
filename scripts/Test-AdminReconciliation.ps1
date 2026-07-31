param(
    [Parameter(Mandatory = $true)]
    [string]$BackendPath,

    [string]$ExpectedVersion = '0.8.3.3'
)

$ErrorActionPreference = 'Stop'

function Assert-Equal {
    param($Actual, $Expected, [string]$Message)
    if ($Actual -ne $Expected) {
        throw "$Message Expected '$Expected', received '$Actual'."
    }
}

function Assert-HttpStatus {
    param([scriptblock]$Action, [int]$ExpectedStatus, [string]$Message)
    try {
        & $Action
        throw "$Message Expected HTTP $ExpectedStatus, but the request succeeded."
    }
    catch {
        $status = [int]$_.Exception.Response.StatusCode
        if ($status -ne $ExpectedStatus) { throw }
    }
}

function New-DeviceId {
    return -join ((1..64) | ForEach-Object { '0123456789ABCDEF'[(Get-Random -Maximum 16)] })
}

function Start-GuildSyncApi {
    if ([System.IO.Path]::GetExtension($BackendPath) -eq '.dll') {
        return Start-Process -FilePath 'dotnet' -ArgumentList @($BackendPath) -PassThru -WindowStyle Hidden
    }
    return Start-Process -FilePath $BackendPath -PassThru -WindowStyle Hidden
}

function Wait-GuildSyncApi {
    for ($i = 0; $i -lt 100; $i++) {
        try {
            $status = Invoke-RestMethod -Uri "$baseUrl/status" -TimeoutSec 1
            Assert-Equal $status.version $ExpectedVersion 'Unexpected API version during administrator reconciliation.'
            return
        }
        catch {
            Start-Sleep -Milliseconds 250
        }
    }
    throw 'GuildSync API did not start for the administrator reconciliation test.'
}

function Stop-GuildSyncApi {
    param($Process)
    if ($Process -and -not $Process.HasExited) {
        Stop-Process -Id $Process.Id -Force
        $Process.WaitForExit(5000) | Out-Null
    }
}

function Invoke-Login {
    param([string]$Email, [string]$Password, [string]$DeviceName)
    return Invoke-RestMethod -Method Post -Uri "$baseUrl/auth/login" -ContentType 'application/json' -Body (@{
        email = $Email
        password = $Password
        deviceId = New-DeviceId
        deviceName = $DeviceName
    } | ConvertTo-Json -Compress)
}

$testData = Join-Path $env:RUNNER_TEMP "guildsync-admin-reconcile-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Force -Path $testData | Out-Null
$port = 18788
$baseUrl = "http://127.0.0.1:$port"
$originalAdminEmail = 'existing-admin@example.invalid'
$originalAdminPassword = 'ExistingAdminPassword2026'
$ownerEmail = 'configured-owner@example.invalid'
$ownerOldPassword = 'OwnerOldPassword2026'
$ownerNewPassword = 'OwnerNewPassword2026'
$backend = $null

$env:GUILDSYNC_LISTEN = $baseUrl
$env:GUILDSYNC_DATA = $testData
$env:GUILDSYNC_ENV = 'testing'
$env:GUILDSYNC_SESSION_HOURS = '1'
$env:GUILDSYNC_ALLOW_FILE_STORAGE = 'true'
$env:GUILDSYNC_REQUIRE_DEVICE_ID = 'true'

try {
    # Phase one: the database already has a different administrator and the desired owner exists only as a pending user.
    $env:GUILDSYNC_ADMIN_EMAIL = $originalAdminEmail
    $env:GUILDSYNC_ADMIN_PASSWORD = $originalAdminPassword
    $backend = Start-GuildSyncApi
    Wait-GuildSyncApi

    $registration = Invoke-RestMethod -Method Post -Uri "$baseUrl/auth/register" -ContentType 'application/json' -Body (@{
        name = 'Configured Owner'
        email = $ownerEmail
        password = $ownerOldPassword
    } | ConvertTo-Json -Compress)
    Assert-Equal $registration.status 'pending' 'The configured-owner fixture was not created as a pending user.'
    Stop-GuildSyncApi $backend
    $backend = $null

    # Phase two: startup configuration now names the existing pending user as the real administrator.
    $env:GUILDSYNC_ADMIN_EMAIL = $ownerEmail
    $env:GUILDSYNC_ADMIN_PASSWORD = $ownerNewPassword
    $backend = Start-GuildSyncApi
    Wait-GuildSyncApi

    Assert-HttpStatus {
        Invoke-Login $ownerEmail $ownerOldPassword 'obsolete owner credentials'
    } 401 'The previous configured-owner password remained valid after reconciliation.'

    $ownerLogin = Invoke-Login $ownerEmail $ownerNewPassword 'configured owner workstation'
    Assert-Equal $ownerLogin.user.email $ownerEmail 'Reconciliation returned the wrong account.'
    Assert-Equal $ownerLogin.user.role 'admin' 'The configured owner was not promoted to administrator.'
    Assert-Equal $ownerLogin.license.status 'active' 'The configured owner was not activated.'
    if ([int]$ownerLogin.license.deviceLimit -lt 5) {
        throw "The configured owner device limit was not raised to at least five. Received '$($ownerLogin.license.deviceLimit)'."
    }

    $headers = @{ Authorization = "Bearer $($ownerLogin.accessToken)" }
    $users = @(Invoke-RestMethod -Uri "$baseUrl/admin/users" -Headers $headers)
    if (-not ($users | Where-Object { $_.email -eq $ownerEmail })) {
        throw 'The reconciled owner could not access the administrator user list.'
    }

    Write-Host 'GuildSync administrator reconciliation passed: an existing pending owner was promoted and its configured password was synchronized despite another administrator already existing.'
}
finally {
    Stop-GuildSyncApi $backend
    Remove-Item $testData -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item Env:GUILDSYNC_LISTEN,Env:GUILDSYNC_DATA,Env:GUILDSYNC_ENV,Env:GUILDSYNC_ADMIN_EMAIL,Env:GUILDSYNC_ADMIN_PASSWORD,Env:GUILDSYNC_SESSION_HOURS,Env:GUILDSYNC_ALLOW_FILE_STORAGE,Env:GUILDSYNC_REQUIRE_DEVICE_ID -ErrorAction SilentlyContinue
}

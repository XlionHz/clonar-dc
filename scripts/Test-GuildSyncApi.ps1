param(
    [Parameter(Mandatory = $true)]
    [string]$BackendPath,

    [string]$ExpectedVersion = '0.8.0'
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

function New-DeviceId { return -join ((1..64) | ForEach-Object { '0123456789ABCDEF'[(Get-Random -Maximum 16)] }) }

function Invoke-Login {
    param([string]$Email, [string]$Password, [string]$DeviceId, [string]$DeviceName)
    return Invoke-RestMethod -Method Post -Uri "$baseUrl/auth/login" -ContentType 'application/json' -Body (@{
        email = $Email
        password = $Password
        deviceId = $DeviceId
        deviceName = $DeviceName
    } | ConvertTo-Json -Compress)
}

function Get-DeviceList {
    param([hashtable]$Headers)
    $response = Invoke-RestMethod -Uri "$baseUrl/devices" -Headers $Headers
    foreach ($item in $response) { Write-Output $item }
}

$testData = Join-Path $env:RUNNER_TEMP "guildsync-api-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Force -Path $testData | Out-Null
$port = 18787
$baseUrl = "http://127.0.0.1:$port"
$adminEmail = 'build-admin@example.invalid'
$adminPassword = 'BuildOnlyAdmin2026'

$env:GUILDSYNC_LISTEN = $baseUrl
$env:GUILDSYNC_DATA = $testData
$env:GUILDSYNC_ENV = 'testing'
$env:GUILDSYNC_ADMIN_EMAIL = $adminEmail
$env:GUILDSYNC_ADMIN_PASSWORD = $adminPassword
$env:GUILDSYNC_SESSION_HOURS = '1'
$env:GUILDSYNC_ALLOW_FILE_STORAGE = 'true'
$env:GUILDSYNC_REQUIRE_DEVICE_ID = 'true'

$extension = [System.IO.Path]::GetExtension($BackendPath)
if ($extension -eq '.dll') {
    $backend = Start-Process -FilePath 'dotnet' -ArgumentList @($BackendPath) -PassThru -WindowStyle Hidden
}
else {
    $backend = Start-Process -FilePath $BackendPath -PassThru -WindowStyle Hidden
}

try {
    $status = $null
    for ($i = 0; $i -lt 100; $i++) {
        try {
            $status = Invoke-RestMethod -Uri "$baseUrl/status" -TimeoutSec 1
            break
        }
        catch {
            Start-Sleep -Milliseconds 250
        }
    }

    if ($null -eq $status) { throw 'GuildSync API did not start.' }
    Assert-Equal $status.service 'GuildSync API' 'Unexpected API identity.'
    Assert-Equal $status.version $ExpectedVersion 'Unexpected API version.'
    Assert-Equal $status.storage 'file' 'Unexpected test storage.'
    Assert-Equal $status.discordIntegrationConfigured $false 'Bot integration must be disabled without a key.'
    Assert-Equal $status.deviceIdentityRequired $true 'Device identity policy was not enabled.'
    Write-Host 'API status and mandatory-device policy passed.'

    $email = "user-$([Guid]::NewGuid().ToString('N'))@example.invalid"
    $password = 'BuildOnlyUser2026'
    $register = Invoke-RestMethod -Method Post -Uri "$baseUrl/auth/register" -ContentType 'application/json' -Body (@{
        name = 'Build User'
        email = $email
        password = $password
    } | ConvertTo-Json -Compress)
    Assert-Equal $register.status 'pending' 'Registration failed.'

    Assert-HttpStatus {
        Invoke-RestMethod -Method Post -Uri "$baseUrl/auth/login" -ContentType 'application/json' -Body (@{
            email = $email
            password = $password
        } | ConvertTo-Json -Compress)
    } 401 'Login without a device identity was accepted.'
    Write-Host 'Registration and rejection of identity-less login passed.'

    $deviceOne = New-DeviceId
    $login = Invoke-Login $email $password $deviceOne 'CI workstation one'
    if (-not $login.accessToken) { throw 'User login did not return a token.' }
    if (-not $login.user.id) { throw 'User login did not return an ID.' }
    Assert-Equal $login.license.deviceCount 1 'First login did not register exactly one device.'

    $headers = @{ Authorization = "Bearer $($login.accessToken)" }
    $me = Invoke-RestMethod -Uri "$baseUrl/me" -Headers $headers
    Assert-Equal $me.email $email 'Authenticated profile returned the wrong account.'
    Assert-Equal $me.deviceCount 1 'Profile did not report the active device.'

    $claimedAgain = Invoke-RestMethod -Method Post -Uri "$baseUrl/devices/claim" -Headers $headers -ContentType 'application/json' -Body (@{
        deviceId = $deviceOne
        deviceName = 'CI workstation one renamed'
    } | ConvertTo-Json -Compress)
    Assert-Equal $claimedAgain.activeDevices 1 'Claiming the same device created a duplicate.'

    $devices = @(Get-DeviceList -Headers $headers)
    Assert-Equal $devices.Count 1 'Device list did not contain exactly one active device.'
    Assert-Equal $devices[0].active $true 'Registered device was not active.'
    Write-Host 'First-device claim and idempotent re-claim passed.'

    $deviceTwo = New-DeviceId
    Assert-HttpStatus {
        Invoke-Login $email $password $deviceTwo 'CI workstation two'
    } 401 'A second device bypassed the one-device license limit.'
    Write-Host 'Second device was correctly blocked by the license limit.'

    Invoke-RestMethod -Method Delete -Uri "$baseUrl/devices/$($devices[0].id)" -Headers $headers | Out-Null
    Assert-HttpStatus { Invoke-RestMethod -Uri "$baseUrl/me" -Headers $headers } 401 'Revoking a device did not revoke its session.'

    $replacementLogin = Invoke-Login $email $password $deviceTwo 'CI workstation two'
    if (-not $replacementLogin.accessToken) { throw 'Replacement device could not use the released slot.' }
    Assert-Equal $replacementLogin.license.deviceCount 1 'Replacement login did not report one active device.'
    $replacementHeaders = @{ Authorization = "Bearer $($replacementLogin.accessToken)" }
    $replacementDevices = @(Get-DeviceList -Headers $replacementHeaders)
    $activeReplacementDevices = @($replacementDevices | Where-Object { $_.active -eq $true })
    Assert-Equal $replacementDevices.Count 2 'Device history did not preserve the revoked and replacement records.'
    Assert-Equal $activeReplacementDevices.Count 1 'Replacement login left an invalid active-device count.'
    Assert-Equal $activeReplacementDevices[0].name 'CI workstation two' 'Replacement device identity was not preserved.'
    Write-Host 'Device revocation, session invalidation and slot replacement passed.'

    Invoke-RestMethod -Method Post -Uri "$baseUrl/auth/logout" -Headers $replacementHeaders | Out-Null
    Assert-HttpStatus { Invoke-RestMethod -Uri "$baseUrl/me" -Headers $replacementHeaders } 401 'Logout did not revoke the session.'

    $userLogin = Invoke-Login $email $password $deviceTwo 'CI workstation two'
    $userHeaders = @{ Authorization = "Bearer $($userLogin.accessToken)" }
    $adminLogin = Invoke-Login $adminEmail $adminPassword (New-DeviceId) 'CI admin workstation'
    $adminHeaders = @{ Authorization = "Bearer $($adminLogin.accessToken)" }
    Invoke-RestMethod -Method Post -Uri "$baseUrl/admin/users/$($userLogin.user.id)/suspend" -Headers $adminHeaders -ContentType 'application/json' -Body '{}' | Out-Null
    Assert-HttpStatus { Invoke-RestMethod -Uri "$baseUrl/me" -Headers $userHeaders } 401 'Suspension did not revoke active sessions.'
    Write-Host 'Logout and administrative suspension revocation passed.'

    Assert-HttpStatus {
        Invoke-RestMethod -Method Post -Uri "$baseUrl/discord/bot/link-code" -ContentType 'application/json' -Body '{"discordUserId":"123456789012345678"}'
    } 503 'Bot endpoint accepted a request without bot configuration.'

    Write-Host 'GuildSync API smoke test passed: auth, mandatory device identity, device limit, device revocation, logout, suspension and bot protection.'
}
finally {
    if ($backend -and -not $backend.HasExited) {
        Stop-Process -Id $backend.Id -Force
    }
    Remove-Item $testData -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item Env:GUILDSYNC_LISTEN,Env:GUILDSYNC_DATA,Env:GUILDSYNC_ENV,Env:GUILDSYNC_ADMIN_EMAIL,Env:GUILDSYNC_ADMIN_PASSWORD,Env:GUILDSYNC_SESSION_HOURS,Env:GUILDSYNC_ALLOW_FILE_STORAGE,Env:GUILDSYNC_REQUIRE_DEVICE_ID -ErrorAction SilentlyContinue
}
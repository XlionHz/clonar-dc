param(
    [Parameter(Mandatory = $true)]
    [string]$BackendPath,

    [string]$ExpectedVersion = '0.7.1'
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
        $status = $_.Exception.Response.StatusCode.value__
        if ($status -ne $ExpectedStatus) { throw }
    }
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

$backend = Start-Process -FilePath $BackendPath -PassThru -WindowStyle Hidden
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

    $email = "user-$([Guid]::NewGuid().ToString('N'))@example.invalid"
    $password = 'BuildOnlyUser2026'
    $register = Invoke-RestMethod -Method Post -Uri "$baseUrl/auth/register" -ContentType 'application/json' -Body (@{
        name = 'Build User'
        email = $email
        password = $password
    } | ConvertTo-Json -Compress)
    Assert-Equal $register.status 'pending' 'Registration failed.'

    $login = Invoke-RestMethod -Method Post -Uri "$baseUrl/auth/login" -ContentType 'application/json' -Body (@{
        email = $email
        password = $password
    } | ConvertTo-Json -Compress)
    if (-not $login.accessToken) { throw 'User login did not return a token.' }
    if (-not $login.user.id) { throw 'User login did not return an ID.' }

    $headers = @{ Authorization = "Bearer $($login.accessToken)" }
    $me = Invoke-RestMethod -Uri "$baseUrl/me" -Headers $headers
    Assert-Equal $me.email $email 'Authenticated profile returned the wrong account.'

    Invoke-RestMethod -Method Post -Uri "$baseUrl/auth/logout" -Headers $headers | Out-Null
    Assert-HttpStatus { Invoke-RestMethod -Uri "$baseUrl/me" -Headers $headers } 401 'Logout did not revoke the session.'

    $userLogin = Invoke-RestMethod -Method Post -Uri "$baseUrl/auth/login" -ContentType 'application/json' -Body (@{
        email = $email
        password = $password
    } | ConvertTo-Json -Compress)
    $userHeaders = @{ Authorization = "Bearer $($userLogin.accessToken)" }

    $adminLogin = Invoke-RestMethod -Method Post -Uri "$baseUrl/auth/login" -ContentType 'application/json' -Body (@{
        email = $adminEmail
        password = $adminPassword
    } | ConvertTo-Json -Compress)
    $adminHeaders = @{ Authorization = "Bearer $($adminLogin.accessToken)" }
    Invoke-RestMethod -Method Post -Uri "$baseUrl/admin/users/$($userLogin.user.id)/suspend" -Headers $adminHeaders -ContentType 'application/json' -Body '{}' | Out-Null
    Assert-HttpStatus { Invoke-RestMethod -Uri "$baseUrl/me" -Headers $userHeaders } 401 'Suspension did not revoke active sessions.'

    Assert-HttpStatus {
        Invoke-RestMethod -Method Post -Uri "$baseUrl/discord/bot/link-code" -ContentType 'application/json' -Body '{"discordUserId":"123456789012345678"}'
    } 503 'Bot endpoint accepted a request without bot configuration.'

    Write-Host 'GuildSync API smoke test passed: status, register, login, logout, admin suspension and bot protection.'
}
finally {
    if ($backend -and -not $backend.HasExited) {
        Stop-Process -Id $backend.Id -Force
    }
    Remove-Item $testData -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item Env:GUILDSYNC_LISTEN,Env:GUILDSYNC_DATA,Env:GUILDSYNC_ENV,Env:GUILDSYNC_ADMIN_EMAIL,Env:GUILDSYNC_ADMIN_PASSWORD,Env:GUILDSYNC_SESSION_HOURS,Env:GUILDSYNC_ALLOW_FILE_STORAGE -ErrorAction SilentlyContinue
}

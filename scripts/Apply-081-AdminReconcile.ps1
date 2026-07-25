$ErrorActionPreference = 'Stop'

$path = Join-Path $PSScriptRoot '../src/ClonarDC.Server/Program.cs'
$content = Get-Content $path -Raw
$pattern = '(?s)    public async Task EnsureBootstrapAdminAsync\(string\? email, string\? password\)\s*\{.*?\n    \}\r?\n\r?\n    public async Task<OpResult> RegisterAsync'
$replacement = @'
    public async Task EnsureBootstrapAdminAsync(string? email, string? password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return;
        email = NormalizeEmail(email);
        if (!InputValidation.IsValidEmail(email) || password.Length < 14)
            throw new InvalidOperationException("The bootstrap administrator requires a valid email and a password with at least 14 characters.");

        await _gate.WaitAsync();
        try
        {
            var user = _db.Users.FirstOrDefault(item =>
                string.Equals(item.Email, email, StringComparison.OrdinalIgnoreCase));
            var created = user is null;
            user ??= new UserRecord
            {
                Name = "Administrator",
                Email = email,
                CreatedAt = DateTimeOffset.UtcNow
            };

            var changed = created;
            var passwordMatches = false;
            try
            {
                passwordMatches = !string.IsNullOrWhiteSpace(user.PasswordSalt) &&
                                  !string.IsNullOrWhiteSpace(user.PasswordHash) &&
                                  Passwords.Verify(password, user.PasswordSalt, user.PasswordHash);
            }
            catch
            {
                passwordMatches = false;
            }

            if (!passwordMatches)
            {
                var credentials = Passwords.Hash(password);
                user.PasswordSalt = credentials.Salt;
                user.PasswordHash = credentials.Hash;
                _db.Sessions.RemoveAll(session => session.UserId == user.Id);
                changed = true;
            }

            if (!string.Equals(user.Role, "admin", StringComparison.OrdinalIgnoreCase))
            {
                user.Role = "admin";
                changed = true;
            }
            if (!string.Equals(user.Status, "active", StringComparison.OrdinalIgnoreCase))
            {
                user.Status = "active";
                changed = true;
            }
            if (!string.Equals(user.LicenseLabel, "Permanent", StringComparison.Ordinal))
            {
                user.LicenseLabel = "Permanent";
                changed = true;
            }
            if (user.ExpiresAt is not null)
            {
                user.ExpiresAt = null;
                changed = true;
            }
            if (user.DeviceLimit < 5)
            {
                user.DeviceLimit = 5;
                changed = true;
            }

            if (created) _db.Users.Add(user);
            if (!changed) return;

            _db.Audit.Add(new(
                DateTimeOffset.UtcNow,
                "system",
                created ? "bootstrap-admin-created" : "bootstrap-admin-synchronized",
                user.Id,
                user.Email));
            await SaveUnsafeAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OpResult> RegisterAsync
'@

$updated = [regex]::Replace($content, $pattern, $replacement, 1)
if ($updated -eq $content) {
    if ($content.Contains('bootstrap-admin-synchronized')) {
        Write-Host 'Administrator reconciliation is already integrated.'
        exit 0
    }
    throw 'Could not locate EnsureBootstrapAdminAsync in Program.cs.'
}

Set-Content $path $updated -Encoding UTF8
Write-Host 'Integrated idempotent administrator reconciliation.'

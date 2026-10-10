param(
    [Parameter(Mandatory = $true)]
    [ValidateLength(1, 128)]
    [string] $Username
)

$ErrorActionPreference = 'Stop'
if ($Username -ne $Username.Trim() -or $Username -match '[\x00-\x1f\x7f]') {
    throw 'Username must not contain control characters or surrounding whitespace.'
}

# Works offline in Windows PowerShell 5.1 and PowerShell 7. Never accepts a password argument.
$securePassword = Read-Host 'Administrator password (16-256 characters; use a unique passphrase)' -AsSecureString
$passwordPointer = [IntPtr]::Zero
$characters = $null
$passwordBytes = $null
$generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$derivation = $null
try {
    if ($securePassword.Length -lt 16 -or $securePassword.Length -gt 256) {
        throw 'Choose a password containing 16-256 characters.'
    }
    $passwordPointer = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
    $characters = New-Object char[] $securePassword.Length
    for ($index = 0; $index -lt $characters.Length; $index++) {
        $characters[$index] = [char]([System.Runtime.InteropServices.Marshal]::ReadInt16($passwordPointer, $index * 2) -band 0xffff)
    }
    $passwordBytes = [System.Text.Encoding]::UTF8.GetBytes($characters)
    $salt = New-Object byte[] 16
    $secret = New-Object byte[] 32
    $generator.GetBytes($salt)
    $generator.GetBytes($secret)
    $derivation = [System.Security.Cryptography.Rfc2898DeriveBytes]::new(
        $passwordBytes, $salt, 600000, [System.Security.Cryptography.HashAlgorithmName]::SHA256)
    $hash = $derivation.GetBytes(32)
    $encoded = 'v1$pbkdf2-sha256$600000$' + [Convert]::ToBase64String($salt) + '$' + [Convert]::ToBase64String($hash)
    Write-Host 'Copy these values into private local.settings.json or Azure Function App settings. Do not commit them or capture this output in logs.'
    [ordered]@{
        ADMIN_USERNAME = $Username
        ADMIN_PASSWORD_HASH = $encoded
        ADMIN_SESSION_SECRET = [Convert]::ToBase64String($secret)
    } | ConvertTo-Json
}
finally {
    if ($null -ne $characters) { [Array]::Clear($characters, 0, $characters.Length) }
    if ($null -ne $passwordBytes) { [Array]::Clear($passwordBytes, 0, $passwordBytes.Length) }
    if ($passwordPointer -ne [IntPtr]::Zero) { [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer) }
    if ($null -ne $derivation) { $derivation.Dispose() }
    $generator.Dispose()
    $securePassword.Dispose()
}

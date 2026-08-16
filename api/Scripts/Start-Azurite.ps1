param(
    [Parameter(Mandatory = $true)]
    [string] $Workspace
)

$ErrorActionPreference = 'Stop'

function Test-LocalPort {
    param([int] $Port)

    $client = [System.Net.Sockets.TcpClient]::new()

    try {
        $connection = $client.ConnectAsync('127.0.0.1', $Port)
        return $connection.Wait(250) -and $client.Connected
    }
    catch {
        return $false
    }
    finally {
        $client.Dispose()
    }
}

$ports = @(10000, 10001, 10002)
$activePorts = @($ports | Where-Object { Test-LocalPort $_ })

if ($activePorts.Count -eq $ports.Count) {
    Write-Host 'Azurite is already listening on ports 10000-10002.'
    exit 0
}

if ($activePorts.Count -gt 0) {
    throw "Azurite cannot start because only these emulator ports are occupied: $($activePorts -join ', '). Stop the process using those ports and run the project again."
}

$repoRoot = [System.IO.Path]::GetFullPath(
    [System.IO.Path]::Combine($PSScriptRoot, '..', '..'))
$candidates = [System.Collections.Generic.List[string]]::new()

$localAzurite = Join-Path $repoRoot 'node_modules\.bin\azurite.cmd'
if (Test-Path -LiteralPath $localAzurite) {
    $candidates.Add($localAzurite)
}

foreach ($commandName in @('azurite.cmd', 'azurite.exe')) {
    $command = Get-Command $commandName -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        $candidates.Add($command.Source)
    }
}

$visualStudioRoot = Join-Path ${env:ProgramFiles} 'Microsoft Visual Studio'
foreach ($version in @('18', '2022')) {
    foreach ($edition in @('Community', 'Professional', 'Enterprise', 'Preview')) {
        $candidate = Join-Path $visualStudioRoot "$version\$edition\Common7\IDE\Extensions\Microsoft\Azure Storage Emulator\azurite.exe"
        if (Test-Path -LiteralPath $candidate) {
            $candidates.Add($candidate)
        }
    }
}

$azurite = $candidates | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($azurite)) {
    throw 'Azurite was not found. Install the Visual Studio Azure development workload, or run "npm.cmd install --global azurite" and try again.'
}

$resolvedWorkspace = [System.IO.Path]::GetFullPath($Workspace)
[System.IO.Directory]::CreateDirectory($resolvedWorkspace) | Out-Null

$argumentList = @(
    '--silent',
    '--disableTelemetry',
    '--skipApiVersionCheck',
    '--location',
    "`"$resolvedWorkspace`""
)

$process = Start-Process `
    -FilePath $azurite `
    -ArgumentList $argumentList `
    -WorkingDirectory $resolvedWorkspace `
    -WindowStyle Hidden `
    -PassThru

$deadline = [DateTime]::UtcNow.AddSeconds(10)
do {
    Start-Sleep -Milliseconds 100

    if ($process.HasExited) {
        throw "Azurite exited during startup with code $($process.ExitCode)."
    }

    $activePorts = @($ports | Where-Object { Test-LocalPort $_ })
} while ($activePorts.Count -ne $ports.Count -and [DateTime]::UtcNow -lt $deadline)

if ($activePorts.Count -ne $ports.Count) {
    throw 'Azurite did not begin listening on ports 10000-10002 within 10 seconds.'
}

Write-Host "Azurite started from '$azurite' using '$resolvedWorkspace'."

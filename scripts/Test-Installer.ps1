[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string] $Version,

    [string] $InstallerDirectory,

    [switch] $RequireAuthenticode
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($InstallerDirectory)) {
    $InstallerDirectory = Join-Path $scriptRoot '..\artifacts\installer'
}
$installerRoot = [IO.Path]::GetFullPath($InstallerDirectory)
$setupName = "TubeForge-$Version-win-x64-setup.exe"
$setupPath = Join-Path $installerRoot $setupName
$checksumPath = Join-Path $installerRoot 'SHA256SUMS.txt'

if (-not (Test-Path -LiteralPath $setupPath -PathType Leaf)) {
    throw "Installer is missing: $setupPath"
}
if (-not (Test-Path -LiteralPath $checksumPath -PathType Leaf)) {
    throw "Installer checksum manifest is missing: $checksumPath"
}

$line = (Get-Content -LiteralPath $checksumPath | Where-Object { $_ -match "  $([regex]::Escape($setupName))$" })
if (@($line).Count -ne 1 -or $line -notmatch '^(?<Hash>[0-9A-Fa-f]{64})  ') {
    throw 'Installer checksum manifest is malformed.'
}
$actualHash = (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash
if ($actualHash -cne $Matches.Hash.ToUpperInvariant()) {
    throw 'Installer checksum verification failed.'
}

if ($RequireAuthenticode) {
    $signature = Get-AuthenticodeSignature -LiteralPath $setupPath
    if ($signature.Status -ne [Management.Automation.SignatureStatus]::Valid) {
        throw "Installer signature is not valid: $($signature.Status)"
    }
}

$process = Start-Process -FilePath $setupPath -ArgumentList '/verify-payload' -PassThru -WindowStyle Hidden
try {
    if (-not $process.WaitForExit(120000)) {
        throw 'Installer payload verification did not finish within 120 seconds.'
    }
    if ($process.ExitCode -ne 0) {
        throw "Installer payload verification failed with exit code $($process.ExitCode)."
    }
}
finally {
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        [void]$process.WaitForExit(5000)
    }
    $process.Dispose()
}

Write-Output "Installer $Version passed checksum, embedded-payload, and optional signature verification."

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $DestinationDirectory,

    [string] $CacheDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$upstreamArchiveName = 'ffmpeg-n8.1.2-22-g94138f6973-win64-lgpl-8.1.zip'
$upstreamArchiveHash = '66fdaf7e314968332c4c3fffbe730fedce47f9ac456ae3a04f73cd531080f4b3'
$upstreamArchiveUrl = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-07-17-13-22/' + $upstreamArchiveName
$bootstrapArchiveName = 'TubeForge-1.2.5-win-x64-framework-dependent.zip'
$bootstrapArchiveHash = 'e1a43566a114a09a71d178608a1a21f1a996121475f1a9681e3b95ea0b639b82'
$bootstrapArchiveUrl = 'https://github.com/0langa/TubeForge/releases/download/v1.2.5/' + $bootstrapArchiveName
$ffmpegExecutableHash = 'c63b7c29e268acb70f058c2c1863fdeae16830d401b226a6c6d25a29c55a4702'
$ffmpegLicenseName = 'ffmpeg-license-94138f6973.txt'
$ffmpegLicenseHash = '246041b6ecf9bc32d718a62c57877c78b5eb397b6467e74ed7ae2626ab189c30'
$ffmpegLicenseUrl = 'https://raw.githubusercontent.com/FFmpeg/FFmpeg/94138f6973dd1ac6208ace92148ac0d172455d65/COPYING.LGPLv2.1'
$buildLicenseName = 'ffmpeg-builds-license-1f74efed.txt'
$buildLicenseHash = 'c1b3cc7eec42bd9c4f6247169bb887b4a9bc904abfd2a7f7f9231ed357844993'
$buildLicenseUrl = 'https://raw.githubusercontent.com/BtbN/FFmpeg-Builds/1f74efed63f467dbf0d1e5dd8548bf2188f4ad21/LICENSE'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = [IO.Path]::GetFullPath((Join-Path $scriptRoot '..'))

if ([string]::IsNullOrWhiteSpace($CacheDirectory)) {
    $CacheDirectory = Join-Path $env:LOCALAPPDATA 'TubeForgeBuildCache\ffmpeg'
}
$cacheRoot = [IO.Path]::GetFullPath($CacheDirectory)
$destinationRoot = [IO.Path]::GetFullPath($DestinationDirectory)
$ffmpegDirectory = Join-Path $destinationRoot 'ffmpeg'
[void](New-Item -ItemType Directory -Path $cacheRoot -Force)
[void](New-Item -ItemType Directory -Path $ffmpegDirectory -Force)

function Get-VerifiedDownload(
    [string] $Name,
    [string] $Uri,
    [string] $ExpectedHash
) {
    $path = Join-Path $cacheRoot $Name
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        if ($actual.Equals($ExpectedHash, [StringComparison]::OrdinalIgnoreCase)) {
            return $path
        }
        throw "Cached third-party file failed SHA-256 verification: $path"
    }

    $temporary = $path + '.' + [Guid]::NewGuid().ToString('N') + '.download'
    try {
        Invoke-WebRequest -UseBasicParsing -Uri $Uri -OutFile $temporary
        $actual = (Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash
        if (-not $actual.Equals($ExpectedHash, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Downloaded third-party file failed SHA-256 verification: $Name"
        }
        Move-Item -LiteralPath $temporary -Destination $path
        return $path
    }
    finally {
        Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
    }
}

$archivePath = Get-VerifiedDownload `
    -Name $bootstrapArchiveName `
    -Uri $bootstrapArchiveUrl `
    -ExpectedHash $bootstrapArchiveHash
$ffmpegLicense = Get-VerifiedDownload -Name $ffmpegLicenseName -Uri $ffmpegLicenseUrl -ExpectedHash $ffmpegLicenseHash
$buildLicense = Get-VerifiedDownload -Name $buildLicenseName -Uri $buildLicenseUrl -ExpectedHash $buildLicenseHash

Add-Type -AssemblyName System.IO.Compression
$archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    $matches = @($archive.Entries | Where-Object { $_.FullName -eq 'ffmpeg/ffmpeg.exe' })
    if ($matches.Count -ne 1 -or $matches[0].Length -le 0) {
        throw 'Pinned FFmpeg archive does not contain exactly one non-empty bin/ffmpeg.exe.'
    }
    [IO.Compression.ZipFileExtensions]::ExtractToFile(
        $matches[0],
        (Join-Path $ffmpegDirectory 'ffmpeg.exe'),
        $true)
}
finally {
    $archive.Dispose()
}

$extractedFfmpeg = Join-Path $ffmpegDirectory 'ffmpeg.exe'
$actualFfmpegHash = (Get-FileHash -LiteralPath $extractedFfmpeg -Algorithm SHA256).Hash
if (-not $actualFfmpegHash.Equals($ffmpegExecutableHash, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Bootstrapped FFmpeg executable failed SHA-256 verification.'
}

Copy-Item -LiteralPath $ffmpegLicense -Destination (Join-Path $ffmpegDirectory 'FFmpeg-LICENSE.txt') -Force
Copy-Item -LiteralPath $buildLicense -Destination (Join-Path $ffmpegDirectory 'FFmpeg-Builds-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'THIRD_PARTY_NOTICES.md') -Destination $destinationRoot -Force

$provenance = @"
FFmpeg 8.1.2-22-g94138f6973
Target: Windows x64
Variant: LGPL static command-line executable
Original build archive: $upstreamArchiveUrl
Original build archive SHA-256: $upstreamArchiveHash
Distribution bootstrap archive: $bootstrapArchiveUrl
Distribution bootstrap archive SHA-256: $bootstrapArchiveHash
FFmpeg executable SHA-256: $ffmpegExecutableHash
FFmpeg source: https://github.com/FFmpeg/FFmpeg/archive/94138f6973dd1ac6208ace92148ac0d172455d65.tar.gz
Build scripts: https://github.com/BtbN/FFmpeg-Builds/archive/1f74efed63f467dbf0d1e5dd8548bf2188f4ad21.tar.gz
"@
[IO.File]::WriteAllText(
    (Join-Path $ffmpegDirectory 'BUILD-PROVENANCE.txt'),
    $provenance,
    [Text.UTF8Encoding]::new($false))

Write-Output (Join-Path $ffmpegDirectory 'ffmpeg.exe')

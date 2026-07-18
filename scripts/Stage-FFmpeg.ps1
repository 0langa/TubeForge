[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $DestinationDirectory,

    [string] $CacheDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$archiveName = 'ffmpeg-n8.1.2-22-g94138f6973-win64-lgpl-8.1.zip'
$archiveHash = '66fdaf7e314968332c4c3fffbe730fedce47f9ac456ae3a04f73cd531080f4b3'
$archiveUrl = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-07-17-13-22/' + $archiveName
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

$archivePath = Get-VerifiedDownload -Name $archiveName -Uri $archiveUrl -ExpectedHash $archiveHash
$ffmpegLicense = Get-VerifiedDownload -Name $ffmpegLicenseName -Uri $ffmpegLicenseUrl -ExpectedHash $ffmpegLicenseHash
$buildLicense = Get-VerifiedDownload -Name $buildLicenseName -Uri $buildLicenseUrl -ExpectedHash $buildLicenseHash

Add-Type -AssemblyName System.IO.Compression
$archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    $matches = @($archive.Entries | Where-Object { $_.FullName -match '/bin/ffmpeg\.exe$' })
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

Copy-Item -LiteralPath $ffmpegLicense -Destination (Join-Path $ffmpegDirectory 'FFmpeg-LICENSE.txt') -Force
Copy-Item -LiteralPath $buildLicense -Destination (Join-Path $ffmpegDirectory 'FFmpeg-Builds-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'THIRD_PARTY_NOTICES.md') -Destination $destinationRoot -Force

$provenance = @"
FFmpeg 8.1.2-22-g94138f6973
Target: Windows x64
Variant: LGPL static command-line executable
Archive: $archiveUrl
Archive SHA-256: $archiveHash
FFmpeg source: https://github.com/FFmpeg/FFmpeg/archive/94138f6973dd1ac6208ace92148ac0d172455d65.tar.gz
Build scripts: https://github.com/BtbN/FFmpeg-Builds/archive/1f74efed63f467dbf0d1e5dd8548bf2188f4ad21.tar.gz
"@
[IO.File]::WriteAllText(
    (Join-Path $ffmpegDirectory 'BUILD-PROVENANCE.txt'),
    $provenance,
    [Text.UTF8Encoding]::new($false))

Write-Output (Join-Path $ffmpegDirectory 'ffmpeg.exe')

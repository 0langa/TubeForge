[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$ffmpeg = Join-Path $repoRoot "src\TubeForge.App\bin\$Configuration\net10.0-windows\ffmpeg\ffmpeg.exe"
if (-not (Test-Path -LiteralPath $ffmpeg -PathType Leaf)) {
    throw "Bundled FFmpeg was not found at $ffmpeg. Build TubeForge.App first."
}

$safeRoot = Join-Path ([IO.Path]::GetTempPath()) 'TubeForge.TimelineSmoke'
[IO.Directory]::CreateDirectory($safeRoot) | Out-Null
$workDirectory = Join-Path $safeRoot ([Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($workDirectory) | Out-Null

function Invoke-CheckedFfmpeg {
    param([string[]]$FfmpegArguments)

    & $ffmpeg @FfmpegArguments
    if ($LASTEXITCODE -ne 0) {
        throw "FFmpeg failed with exit code $LASTEXITCODE."
    }
}

try {
    $source = Join-Path $workDirectory 'source.mp4'
    $removed = Join-Path $workDirectory 'removed.mp4'
    $trimmed = Join-Path $workDirectory 'trimmed.mp4'

    Invoke-CheckedFfmpeg @(
        '-hide_banner', '-loglevel', 'error', '-xerror', '-nostdin',
        '-f', 'lavfi', '-i', 'testsrc2=size=320x180:rate=30:duration=8',
        '-f', 'lavfi', '-i', 'sine=frequency=440:sample_rate=48000:duration=8',
        '-map', '0:v:0', '-map', '1:a:0',
        '-c:v', 'libopenh264', '-b:v', '600k', '-pix_fmt', 'yuv420p',
        '-c:a', 'aac', '-b:a', '96k', '-shortest', '-movflags', '+faststart', $source
    )
    Invoke-CheckedFfmpeg @(
        '-hide_banner', '-loglevel', 'error', '-xerror', '-nostdin', '-i', $source,
        '-map', '0:v:0', '-map', '0:a:0',
        '-vf', 'select=not(between(t\,2\,4)),setpts=N/FRAME_RATE/TB',
        '-af', 'aselect=not(between(t\,2\,4)),asetpts=N/SR/TB',
        '-c:v', 'libopenh264', '-b:v', '600k', '-pix_fmt', 'yuv420p',
        '-c:a', 'aac', '-b:a', '96k', '-movflags', '+faststart', $removed
    )
    Invoke-CheckedFfmpeg @(
        '-hide_banner', '-loglevel', 'error', '-xerror', '-nostdin',
        '-ss', '1', '-i', $source, '-t', '5',
        '-map', '0:v?', '-map', '0:a?', '-c', 'copy',
        '-avoid_negative_ts', 'make_zero', '-map_metadata', '-1',
        '-movflags', '+faststart', $trimmed
    )
    Invoke-CheckedFfmpeg @(
        '-hide_banner', '-loglevel', 'error', '-xerror', '-nostdin',
        '-i', $removed, '-f', 'null', 'NUL'
    )
    Invoke-CheckedFfmpeg @(
        '-hide_banner', '-loglevel', 'error', '-xerror', '-nostdin',
        '-i', $trimmed, '-f', 'null', 'NUL'
    )

    [pscustomobject]@{
        Result = 'pass'
        SourceBytes = ([IO.FileInfo]$source).Length
        RemovedBytes = ([IO.FileInfo]$removed).Length
        TrimmedBytes = ([IO.FileInfo]$trimmed).Length
    }
}
finally {
    $resolvedWork = [IO.Path]::GetFullPath($workDirectory)
    $resolvedRoot = [IO.Path]::GetFullPath($safeRoot) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedWork.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to clean a timeline-smoke directory outside the safe temporary root.'
    }

    if ([IO.Directory]::Exists($resolvedWork)) {
        [IO.Directory]::Delete($resolvedWork, $true)
    }
}

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

$safeRoot = Join-Path ([IO.Path]::GetTempPath()) 'TubeForge.HlsSmoke'
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
    $playlist = Join-Path $workDirectory 'playlist.m3u8'
    $segmentPattern = Join-Path $workDirectory 'segment-%03d.ts'
    $captureSource = Join-Path $workDirectory 'capture.hls-source'
    $output = Join-Path $workDirectory 'capture.mkv'

    Invoke-CheckedFfmpeg @(
        '-hide_banner', '-loglevel', 'error', '-xerror', '-nostdin',
        '-f', 'lavfi', '-i', 'testsrc2=size=320x180:rate=30:duration=12',
        '-f', 'lavfi', '-i', 'sine=frequency=440:sample_rate=48000:duration=12',
        '-map', '0:v:0', '-map', '1:a:0',
        '-c:v', 'libopenh264', '-b:v', '600k', '-pix_fmt', 'yuv420p', '-g', '90',
        '-c:a', 'aac', '-b:a', '96k', '-shortest',
        '-f', 'hls', '-hls_time', '3', '-hls_list_size', '0',
        '-hls_segment_filename', $segmentPattern, $playlist
    )

    $segments = @(Get-ChildItem -LiteralPath $workDirectory -Filter 'segment-*.ts' -File | Sort-Object Name)
    if ($segments.Count -lt 2) {
        throw 'Synthetic HLS generation did not produce multiple media segments.'
    }

    $outputStream = [IO.File]::Open($captureSource, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        foreach ($segment in $segments) {
            $inputStream = [IO.File]::OpenRead($segment.FullName)
            try {
                $inputStream.CopyTo($outputStream)
            }
            finally {
                $inputStream.Dispose()
            }
        }
        $outputStream.Flush($true)
    }
    finally {
        $outputStream.Dispose()
    }

    Invoke-CheckedFfmpeg @(
        '-hide_banner', '-loglevel', 'error', '-xerror', '-nostdin',
        '-i', $captureSource, '-map', '0:v?', '-map', '0:a?',
        '-c', 'copy', '-map_metadata', '-1', '-f', 'matroska', $output
    )
    Invoke-CheckedFfmpeg @(
        '-hide_banner', '-loglevel', 'error', '-xerror', '-nostdin',
        '-i', $output, '-f', 'null', 'NUL'
    )

    [pscustomobject]@{
        Result = 'pass'
        SegmentCount = $segments.Count
        CapturedBytes = ([IO.FileInfo]$captureSource).Length
        MkvBytes = ([IO.FileInfo]$output).Length
    }
}
finally {
    $resolvedWork = [IO.Path]::GetFullPath($workDirectory)
    $resolvedRoot = [IO.Path]::GetFullPath($safeRoot) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedWork.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to clean an HLS-smoke directory outside the safe temporary root.'
    }

    if ([IO.Directory]::Exists($resolvedWork)) {
        [IO.Directory]::Delete($resolvedWork, $true)
    }
}

[CmdletBinding()]
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$ffmpegPath = Join-Path $repositoryRoot "src\TubeForge.App\bin\$Configuration\net10.0-windows\ffmpeg\ffmpeg.exe"
if (-not (Test-Path -LiteralPath $ffmpegPath -PathType Leaf)) {
    throw "Bundled FFmpeg was not found. Build TubeForge.App first."
}

$systemTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$probeRoot = [System.IO.Path]::GetFullPath((Join-Path $systemTemp ("TubeForge-ChapterProbe-" + [guid]::NewGuid().ToString("N"))))
if (-not $probeRoot.StartsWith($systemTemp, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Probe path escaped the system temp directory."
}

New-Item -ItemType Directory -Path $probeRoot | Out-Null
try {
    $chapterPath = Join-Path $probeRoot "chapters.ffmeta"
    $captionPath = Join-Path $probeRoot "caption.srt"
    Set-Content -LiteralPath $chapterPath -Encoding utf8NoBOM -Value @(
        ";FFMETADATA1"
        "[CHAPTER]"
        "TIMEBASE=1/1000"
        "START=0"
        "END=1000"
        "title=Intro"
        "[CHAPTER]"
        "TIMEBASE=1/1000"
        "START=1000"
        "END=2000"
        "title=Main"
    )
    Set-Content -LiteralPath $captionPath -Encoding utf8NoBOM -Value @(
        "1"
        "00:00:00,000 --> 00:00:01,000"
        "Caption"
    )

    $cases = @(
        @{ Name = "mp4"; Video = "mpeg4"; Audio = "aac"; Subtitle = "mov_text"; Format = "mp4" }
        @{ Name = "mkv"; Video = "mpeg4"; Audio = "aac"; Subtitle = "srt"; Format = "matroska" }
        @{ Name = "webm"; Video = "libvpx-vp9"; Audio = "libopus"; Subtitle = "webvtt"; Format = "webm" }
    )

    foreach ($case in $cases) {
        $sourcePath = Join-Path $probeRoot ("source." + $case.Name)
        $outputPath = Join-Path $probeRoot ("output." + $case.Name)
        $probePath = Join-Path $probeRoot ("probe-" + $case.Name + ".ffmeta")

        & $ffmpegPath -hide_banner -loglevel error -xerror -nostdin `
            -f lavfi -i "color=c=black:s=320x180:d=2" `
            -f lavfi -i "anullsrc=r=48000:cl=stereo:d=2" `
            -c:v $case.Video -c:a $case.Audio -shortest -f $case.Format $sourcePath
        if ($LASTEXITCODE -ne 0) {
            throw "Source generation failed for $($case.Name)."
        }

        & $ffmpegPath -hide_banner -loglevel error -xerror -nostdin `
            -i $sourcePath -i $captionPath -f ffmetadata -i $chapterPath `
            -map "0:v:0" -map "0:a?" -map "1:0" `
            -c:v copy -c:a copy -c:s $case.Subtitle `
            "-metadata:s:s:0" "language=en" "-disposition:s:0" "0" `
            -map_metadata 2 -map_chapters 2 -f $case.Format $outputPath
        if ($LASTEXITCODE -ne 0) {
            throw "Metadata embedding failed for $($case.Name)."
        }

        & $ffmpegPath -hide_banner -loglevel error -xerror -nostdin `
            -i $outputPath -f ffmetadata $probePath
        if ($LASTEXITCODE -ne 0) {
            throw "Chapter extraction failed for $($case.Name)."
        }

        $chapterCount = (Select-String -LiteralPath $probePath -Pattern '^\[CHAPTER\]$').Count
        & $ffmpegPath -hide_banner -loglevel error -xerror -nostdin `
            -i $outputPath -map "0:s:0" -c copy -f null -
        if ($LASTEXITCODE -ne 0 -or $chapterCount -ne 2) {
            throw "Validation failed for $($case.Name): chapters=$chapterCount."
        }

        $splitDirectory = Join-Path $probeRoot ("split-" + $case.Name)
        New-Item -ItemType Directory -Path $splitDirectory | Out-Null
        foreach ($chapter in @(
            @{ Index = 1; Start = "0" }
            @{ Index = 2; Start = "1" }
        )) {
            $splitPath = Join-Path $splitDirectory ("0" + $chapter.Index + "." + $case.Name)
            & $ffmpegPath -hide_banner -loglevel error -xerror -nostdin `
                -ss $chapter.Start -i $outputPath -t 1 `
                -map "0:v?" -map "0:a?" -map "0:s?" `
                -c copy -avoid_negative_ts make_zero -reset_timestamps 1 `
                -f $case.Format $splitPath
            if ($LASTEXITCODE -ne 0) {
                throw "Chapter split failed for $($case.Name) chapter $($chapter.Index)."
            }

            & $ffmpegPath -hide_banner -loglevel error -xerror -nostdin -i $splitPath -f null -
            if ($LASTEXITCODE -ne 0) {
                throw "Chapter split validation failed for $($case.Name) chapter $($chapter.Index)."
            }
        }

        $bytes = (Get-Item -LiteralPath $outputPath).Length
        Write-Output "PASS $($case.Name): chapters=$chapterCount subtitle=present split=2 bytes=$bytes"
    }
}
finally {
    if (Test-Path -LiteralPath $probeRoot) {
        $resolvedCleanup = [System.IO.Path]::GetFullPath($probeRoot)
        if (-not $resolvedCleanup.StartsWith($systemTemp, [System.StringComparison]::OrdinalIgnoreCase) -or
            -not ([System.IO.Path]::GetFileName($resolvedCleanup)).StartsWith("TubeForge-ChapterProbe-", [System.StringComparison]::Ordinal)) {
            throw "Refusing to clean an unexpected probe directory."
        }

        Remove-Item -LiteralPath $resolvedCleanup -Recurse -Force
    }
}

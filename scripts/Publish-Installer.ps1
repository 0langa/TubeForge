[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version,

    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\installer')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$staging = Join-Path $outputRoot ".staging-$Version"
$appDirectory = Join-Path $staging 'app'
$setupDirectory = Join-Path $staging 'setup'
$payloadPath = Join-Path $staging 'TubeForge.Payload.zip'
$utf8NoBom = [Text.UTF8Encoding]::new($false)
$dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
}

function Assert-ChildPath([string] $Path, [string] $Parent) {
    $resolved = [IO.Path]::GetFullPath($Path)
    $root = [IO.Path]::GetFullPath($Parent).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if (-not $resolved.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing operation outside installer output: $resolved"
    }
}

function Invoke-DotNet([string[]] $Arguments) {
    & $dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet failed with exit code $LASTEXITCODE."
    }
}

function New-PayloadZip([string] $SourceDirectory, [string] $ArchivePath) {
    Add-Type -AssemblyName System.IO.Compression
    $stream = [IO.File]::Open($ArchivePath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
    try {
        $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $false)
        try {
            $root = [IO.Path]::GetFullPath($SourceDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar)
            foreach ($file in (Get-ChildItem -LiteralPath $root -File -Recurse | Sort-Object FullName)) {
                $name = $file.FullName.Substring($root.Length + 1).Replace('\', '/')
                $entry = $archive.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
                $input = $file.OpenRead()
                $output = $entry.Open()
                try { $input.CopyTo($output) } finally { $output.Dispose(); $input.Dispose() }
            }
        }
        finally { $archive.Dispose() }
    }
    finally { $stream.Dispose() }
}

[void](New-Item -ItemType Directory -Path $outputRoot -Force)
Assert-ChildPath -Path $staging -Parent $outputRoot
if (Test-Path -LiteralPath $staging) {
    Remove-Item -LiteralPath $staging -Recurse -Force
}
[void](New-Item -ItemType Directory -Path $appDirectory -Force)
[void](New-Item -ItemType Directory -Path $setupDirectory -Force)

try {
    Invoke-DotNet @(
        'publish', (Join-Path $repoRoot 'src\TubeForge.App\TubeForge.App.csproj'),
        '--configuration', 'Release', '--runtime', 'win-x64', '--self-contained', 'true',
        '--source', 'https://api.nuget.org/v3/index.json',
        '--output', $appDirectory, "-p:Version=$Version", '-p:PublishSingleFile=false',
        '-p:PublishTrimmed=false', '-p:PublishReadyToRun=false'
    )

    $files = foreach ($file in (Get-ChildItem -LiteralPath $appDirectory -File -Recurse | Sort-Object FullName)) {
        [ordered]@{
            path = $file.FullName.Substring($appDirectory.Length + 1).Replace('\', '/')
            length = $file.Length
            sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    $manifest = [ordered]@{
        schemaVersion = 1
        product = 'TubeForge'
        version = $Version
        files = @($files)
    } | ConvertTo-Json -Depth 5
    [IO.File]::WriteAllText((Join-Path $appDirectory 'install-manifest.json'), $manifest + "`n", $utf8NoBom)
    New-PayloadZip -SourceDirectory $appDirectory -ArchivePath $payloadPath

    Invoke-DotNet @(
        'publish', (Join-Path $repoRoot 'src\TubeForge.Installer\TubeForge.Installer.csproj'),
        '--configuration', 'Release', '--runtime', 'win-x64', '--self-contained', 'true',
        '--source', 'https://api.nuget.org/v3/index.json',
        '--output', $setupDirectory, "-p:Version=$Version", "-p:TubeForgePayload=$payloadPath",
        '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:PublishTrimmed=false', '-p:PublishReadyToRun=false'
    )

    $setupName = "TubeForge-$Version-win-x64-setup.exe"
    $setupPath = Join-Path $outputRoot $setupName
    Copy-Item -LiteralPath (Join-Path $setupDirectory 'TubeForge.Setup.exe') -Destination $setupPath -Force
    $hash = (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText((Join-Path $outputRoot 'SHA256SUMS.txt'), "$hash  $setupName`n", $utf8NoBom)
    Write-Output $setupPath
}
finally {
    if (Test-Path -LiteralPath $staging) {
        Assert-ChildPath -Path $staging -Parent $outputRoot
        Remove-Item -LiteralPath $staging -Recurse -Force
    }
}

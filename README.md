# TubeForge

TubeForge is an experimental, ad-free Windows desktop downloader built from scratch for media you are authorized to save from YouTube. It does not use `yt-dlp`, FFmpeg, hosted conversion services, third-party NuGet packages, or bundled external executables.

> [!IMPORTANT]
> You are responsible for complying with YouTube's terms, copyright, privacy, and local law. Download only content you own or have permission to save. TubeForge is not intended to bypass DRM, payment, membership, or other access controls.

## Status

Early functional MVP; no stable release yet.

Working now:

- modern WPF analyze/download screen;
- strict YouTube URL parsing and live public-video metadata resolution;
- versioned Android player fallback for direct progressive, native-audio, and video-only streams;
- truthful format/container/codec labels and collision-safe filenames;
- resumable `.part` transfers, validators, bounded retries, progress, cancellation, and atomic finalization;
- dependency-free fixture/transfer test runner and sanitized live metadata smoke tool.

Not finished: persistent multi-item queue, adaptive audio/video muxing, current ES6 player decipher, throttling-parameter transforms, container validation, settings, packaging, and releases. See [DEVELOPMENT_PLAN.md](DEVELOPMENT_PLAN.md) for exact checklist state.

## Baseline

- Windows 10/11 x64
- .NET 10 WPF desktop application
- Public video metadata and direct progressive MP4 downloads
- Native audio-only downloads
- Resumable direct transfers; persistent queue and in-house media muxers remain planned
- No ads, telemetry, accounts, or paid features

## Build

Required toolchain: .NET 10 SDK on Windows.

```powershell
dotnet build TubeForge.slnx --configuration Release
dotnet run --project tests/TubeForge.Tests --configuration Release -- --all
```

Opt-in live metadata smoke test (prints no media URLs):

```powershell
dotnet run --project tools/TubeForge.Smoke -- analyze "https://www.youtube.com/watch?v=VIDEO_ID"
```

Run the desktop application:

```powershell
dotnet run --project src/TubeForge.App --configuration Release
```

## Security

Do not include media URLs, cookies, signatures, visitor data, private video data, or copyrighted media when reporting problems. See [SECURITY.md](SECURITY.md). Automated redacted diagnostic export remains planned.

## License

No open-source license has been selected. Public visibility does not grant permission to copy, modify, or redistribute the code. Licensing will be decided before outside contributions are accepted.

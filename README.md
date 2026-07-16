# TubeForge

TubeForge is an experimental, ad-free Windows desktop downloader built from scratch for media you are authorized to save from YouTube. It does not use `yt-dlp`, FFmpeg, hosted conversion services, third-party NuGet packages, or bundled external executables.

> [!IMPORTANT]
> You are responsible for complying with YouTube's terms, copyright, privacy, and local law. Download only content you own or have permission to save. TubeForge is not intended to bypass DRM, payment, membership, or other access controls.

## Status

Release candidate; no stable GitHub release published yet.

Working now:

- modern WPF analyze/download screen;
- strict YouTube URL parsing and live public-video metadata resolution;
- versioned Android player fallback for direct progressive, native-audio, and video-only streams;
- type-first stream selection with resolution, container, codec, FPS/HDR, bitrate, and exact-stream filters;
- highest-quality audio + video selection with separate resumable track downloads and in-house MP4/WebM muxing, preferring MP4 on equivalent-quality choices;
- caption-track metadata plus manual/auto language selection and atomic SRT/WebVTT sidecar saves;
- on-demand validated thumbnail saves and stable JSON metadata sidecars with chapters but without signed stream URLs;
- bounded playlist/channel enumeration with per-video selection, source ordering, indexed filenames, and batch queue preparation;
- shared per-provider request limits and bounded `Retry-After` backoff that stops persistent rate-limited bulk preparation;
- customizable token-based filenames plus a durable local Library used for exact-output and destination duplicate detection;
- Short and completed-live-replay classification, sidecar metadata, and normal highest-quality adaptive downloads; active/upcoming capture is explicitly unsupported;
- resumable `.part` transfers, bounded container validation, retries, progress, cancellation, and atomic finalization;
- opt-in segmented transfer for large files with validated parallel ranges, resumable segment state, and automatic direct-transfer fallback;
- preflight disk-space forecasting with adaptive-mux peak-space accounting and retryable low-space failures;
- queue screen with 1–4 transfer global concurrency, per-item progress/speed/ETA, pause/resume/cancel/retry/remove/reveal controls, and interrupted-download recovery;
- privacy-safe schema-versioned queue state that stores source identities instead of expiring media URLs;
- local settings, first-run responsible-use acknowledgement, and redacted runtime/extraction diagnostics pages;
- keyboard-focused navigation, screen-reader labels/live regions, DPI-aware layout rounding, and dark Windows title-bar integration;
- dependency-free fixture/transfer test runner with deterministic hostile-container mutation coverage, plus sanitized live smoke tools.
- isolated performance budgets for analysis latency, startup, CPU, memory, and UI frame pacing, with the deterministic core gate enforced in CI.
- bounded structural classic/ES6 signature and `n` throttling transforms without executing player JavaScript;
- reproducible portable framework-dependent and self-contained Windows x64 packaging with SHA-256 manifests.

Not included: active-live capture, audio transcoding/MP3, authenticated/access-controlled media, automatic updates, or an installer. The public v1.0 release and license decision are still pending. See [DEVELOPMENT_PLAN.md](DEVELOPMENT_PLAN.md) for exact checklist state.

## Baseline

- Windows 10/11 x64
- .NET 10 WPF desktop application
- Public video metadata plus progressive and highest-compatible adaptive downloads
- Native audio-only downloads
- In-house regular/fragmented MP4 and WebM video/audio muxing without re-encoding
- Resumable direct transfers, persisted queue recovery, and bounded concurrent queue processing
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

Opt-in bounded collection smoke test (prints aggregate counts, not item IDs or titles):

```powershell
dotnet run --project tools/TubeForge.Smoke -- collection "https://www.youtube.com/playlist?list=PLAYLIST_ID" 150
```

Run a bounded local canary set without printing URLs, IDs, titles, or channels. See [extractor playbook](docs/EXTRACTOR_PLAYBOOK.md).

```powershell
dotnet run --project tools/TubeForge.Smoke -- canary C:\private\tubeforge-canaries.txt
```

Opt-in live MP4 mux smoke test (downloads the smallest compatible H.264/AAC pair to a temporary directory, validates it with the Windows media stack, then deletes it):

```powershell
dotnet run --project tools/TubeForge.LiveMuxSmoke -- "https://www.youtube.com/watch?v=VIDEO_ID"
```

Run the isolated local [performance budget](docs/PERFORMANCE_BUDGET.md) probe:

```powershell
dotnet build TubeForge.slnx --configuration Release
dotnet run --project tools/TubeForge.Performance --configuration Release --no-build
```

Run the desktop application:

```powershell
dotnet run --project src/TubeForge.App --configuration Release
```

## Release packaging

Create framework-dependent and self-contained Windows x64 archives, a manifest, and SHA-256 checksums:

```powershell
.\scripts\Publish-Release.ps1 -Version 1.0.0
.\scripts\Test-Release.ps1 -Version 1.0.0
```

Authenticode signing is optional and fails closed when a requested certificate cannot produce a valid signature. See [installation and data retention](docs/INSTALLATION.md), [extraction compatibility](docs/EXTRACTION_COMPATIBILITY.md), and the [v1 support policy](docs/SUPPORT_POLICY.md).

## Security

Do not include media URLs, cookies, signatures, visitor data, private video data, or copyrighted media when reporting problems. Use Diagnostics → Export JSON, review it, then follow [SECURITY.md](SECURITY.md).

## License

No open-source license has been selected. Public visibility does not grant permission to copy, modify, or redistribute the code. Licensing will be decided before outside contributions are accepted.

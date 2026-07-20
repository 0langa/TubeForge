<p align="center"><img src="assets/TubeForge.svg" width="104" height="104" alt="TubeForge icon"></p>

# TubeForge

TubeForge is an experimental, ad-free Windows desktop downloader built from scratch for media you are authorized to save from YouTube. It does not use `yt-dlp`, hosted conversion services, or third-party NuGet packages. Releases bundle a pinned LGPL FFmpeg executable for reliable MP4, WebM, and MKV stream-copy finalization.

> [!IMPORTANT]
> You are responsible for complying with YouTube's terms, copyright, privacy, and local law. Download only content you own or have permission to save. TubeForge is not intended to bypass DRM, payment, membership, or other access controls.

## Status

TubeForge v1.2.3 is the current public stable release.

Working now:

- modern WPF analyze/download screen;
- strict YouTube URL parsing and live public-video metadata resolution;
- tail-verified no-token YouTube client chain for direct progressive, native-audio, and video-only streams;
- type-first stream selection with resolution, container, codec, FPS/HDR, bitrate, and exact-stream filters;
- highest-quality video + audio selection with separate resumable track downloads and FFmpeg stream-copy MP4, WebM, or MKV finalization;
- native M4A/WebM audio saves plus dependency-free Windows Media Foundation MP3 conversion at 128, 192, 256, or 320 kbps;
- caption-track metadata plus manual/auto language selection and atomic SRT/WebVTT sidecar saves;
- on-demand validated thumbnail saves and stable JSON metadata sidecars with chapters but without signed stream URLs;
- bounded playlist/channel enumeration with per-video selection, source ordering, indexed filenames, and batch queue preparation;
- shared per-provider request limits and bounded `Retry-After` backoff that stops persistent rate-limited bulk preparation;
- customizable token-based filenames plus a durable local Library used for exact-output and destination duplicate detection;
- searchable/sortable Library history with one-click cleanup for records whose files were moved or deleted;
- Short and completed-live-replay classification, sidecar metadata, and normal highest-quality adaptive downloads; active/upcoming capture is explicitly unsupported;
- resumable `.part` transfers, bounded container validation, retries, progress, cancellation, and atomic finalization;
- opt-in segmented transfer for large files with validated parallel ranges, resumable segment state, and automatic direct-transfer fallback;
- preflight disk-space forecasting with adaptive-mux peak-space accounting and retryable low-space failures;
- queue screen with 1–4 transfer global concurrency, per-item progress/speed/ETA, pause/resume/cancel/retry/remove/reveal controls, and interrupted-download recovery;
- privacy-safe schema-versioned queue state that stores source identities instead of expiring media URLs;
- local settings, first-run responsible-use acknowledgement, and redacted runtime/extraction diagnostics pages;
- keyboard-focused navigation, screen-reader labels/live regions, DPI-aware layout rounding, and dark Windows title-bar integration;
- package-free fixture/transfer test runner with deterministic hostile-container mutation coverage, plus sanitized live smoke tools.
- isolated performance budgets for analysis latency, startup, CPU, memory, and UI frame pacing, with the deterministic core gate enforced in CI.
- bounded structural classic/ES6 signature and `n` throttling transforms without executing player JavaScript;
- reproducible portable framework-dependent and self-contained Windows x64 packaging with SHA-256 manifests.
- branded per-user installer, Add/Remove Programs integration, clean uninstall, and opt-in verified updates from the official GitHub release.

Not included: active/upcoming live capture, authenticated/access-controlled media, or video re-encoding.

## Baseline

- Windows 10/11 x64
- .NET 10 WPF desktop application
- Public video metadata plus progressive and highest-compatible adaptive downloads
- Native audio-only downloads
- Indexed MP4 plus validated WebM/MKV finalization through pinned FFmpeg stream copy
- Resumable direct transfers, persisted queue recovery, and bounded concurrent queue processing
- No ads, telemetry, accounts, or paid features

## Build

Required source toolchain: .NET 10 SDK on Windows. Release packaging downloads one pinned, SHA-256-verified FFmpeg x64 archive.

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

Opt-in live MP4 mux smoke test (requires bundled FFmpeg or `ffmpeg.exe` on `PATH`; downloads the smallest compatible H.264/AAC pair, produces indexed MP4, validates it with Windows media stack, then deletes it):

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
.\scripts\Publish-Release.ps1 -Version 1.2.3
.\scripts\Test-Release.ps1 -Version 1.2.3
```

Create the self-contained per-user installer and checksum manifest:

```powershell
.\scripts\Publish-Installer.ps1 -Version 1.2.3
```

Authenticode signing is optional and fails closed when a requested certificate cannot produce a valid signature. See [installation and data retention](docs/INSTALLATION.md), [extraction compatibility](docs/EXTRACTION_COMPATIBILITY.md), and the [v1 support policy](docs/SUPPORT_POLICY.md).

The release workflow signs both `TubeForge.exe` and the setup executable when the protected PFX secrets and `TUBEFORGE_TIMESTAMP_SERVER` repository variable are configured. Every setup is checksum-checked and runs an embedded-payload verification probe before publication.

## Security

Do not include media URLs, cookies, signatures, visitor data, private video data, or copyrighted media when reporting problems. Use Diagnostics → Export JSON, review it, then follow [SECURITY.md](SECURITY.md).

## License

TubeForge is available under the [MIT License](LICENSE). Bundled FFmpeg licensing, exact source, build provenance, and notices are documented in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

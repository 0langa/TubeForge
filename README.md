# TubeForge

TubeForge is an experimental, ad-free Windows desktop downloader built from scratch for media you are authorized to save from YouTube. It does not use `yt-dlp`, FFmpeg, hosted conversion services, third-party NuGet packages, or bundled external executables.

> [!IMPORTANT]
> You are responsible for complying with YouTube's terms, copyright, privacy, and local law. Download only content you own or have permission to save. TubeForge is not intended to bypass DRM, payment, membership, or other access controls.

## Status

Early development. No stable release yet. See [DEVELOPMENT_PLAN.md](DEVELOPMENT_PLAN.md) for scope, architecture, milestones, and current progress.

## Planned baseline

- Windows 10/11 x64
- .NET 10 WPF desktop application
- Public video metadata and direct progressive MP4 downloads
- Native audio-only downloads
- Resumable queue and in-house media container support
- No ads, telemetry, accounts, or paid features

## Build

The source scaffold is added in milestone M0. Required toolchain: .NET 10 SDK on Windows.

## Security

Do not include media URLs, cookies, signatures, visitor data, private video data, or copyrighted media when reporting problems. A dedicated security policy and redacted diagnostics workflow are planned in M0.

## License

No open-source license has been selected. Public visibility does not grant permission to copy, modify, or redistribute the code. Licensing will be decided before outside contributions are accepted.

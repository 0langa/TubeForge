# TubeForge v1.2.2

TubeForge is an ad-free Windows desktop downloader for public media you own or are authorized to save. It has no `yt-dlp`, hosted converter, third-party managed package, telemetry, account, or paid feature. Releases bundle pinned LGPL FFmpeg for MP4, WebM, and MKV stream-copy finalization.

Choose the per-user Windows x64 installer for normal use or a portable archive when needed. Verify `SHA256SUMS.txt` before running or extracting an asset. GitHub Actions release artifacts also carry signed build-provenance attestations; the per-release manifest states whether the Windows executable additionally has an Authenticode signature.

Highlights:

- selects the highest-bitrate, highest-sample-rate compatible audio track across MP4 and WebM families;
- keeps native MP4/WebM output as the equal-quality tie-breaker, preserving MP4 preference without sacrificing better audio;
- uses lossless MKV stream-copy when the better audio track crosses container families;
- reports FFmpeg failures using the selected MP4, WebM, or MKV output container;
- keeps v1.2.1 reliable indexed MP4, validated WebM/MKV, full quality ladder, resumable transfers, MP3 conversion, installer, updater, queue, and Library behavior.

Known limitations:

- no active/upcoming live capture;
- no video re-encoding;
- no login, private, paid, membership, DRM, or access-control bypass;
- updates never install silently and require explicit confirmation;
- upstream YouTube changes can temporarily break public extraction.

Read [installation, upgrades, rollback, and data retention](INSTALLATION.md), [extraction compatibility](EXTRACTION_COMPATIBILITY.md), and the [support policy](SUPPORT_POLICY.md).

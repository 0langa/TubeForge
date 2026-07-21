# TubeForge v1.2.5

TubeForge is an ad-free Windows desktop downloader for public media you own or are authorized to save. It has no `yt-dlp`, hosted converter, third-party managed package, telemetry, account, or paid feature. Releases bundle pinned LGPL FFmpeg for MP4, WebM, and MKV stream-copy finalization plus MP3 audio conversion.

Choose the per-user Windows x64 installer for normal use or a portable archive when needed. Verify `SHA256SUMS.txt` before running or extracting an asset. GitHub Actions release artifacts also carry signed build-provenance attestations; the per-release manifest states whether the Windows executable additionally has an Authenticode signature.

Highlights:

- accelerates large Googlevideo transfers with up to four validated 8 MiB query-range workers and safe automatic fallback;
- resumes completed chunks after interruption instead of restarting successful ranges;
- enables acceleration automatically for new and upgraded settings while keeping an explicit user control;
- selects the largest trusted widescreen thumbnail, removing the black preview strip caused by square primary images;
- retains bundled-FFmpeg MP3 conversion, highest-quality companion audio, MP4 preference, lossless MKV fallback, installer, updater, queue, and Library behavior.

Known limitations:

- no active/upcoming live capture;
- no video re-encoding;
- no login, private, paid, membership, DRM, or access-control bypass;
- updates never install silently and require explicit confirmation;
- upstream YouTube changes can temporarily break public extraction.

Read [installation, upgrades, rollback, and data retention](INSTALLATION.md), [extraction compatibility](EXTRACTION_COMPATIBILITY.md), and the [support policy](SUPPORT_POLICY.md).

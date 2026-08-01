# TubeForge v2.1.0

TubeForge v2.1.0 expands local media processing, collection workflows, recovery, and download controls while keeping original-quality stream copy as default. It remains an ad-free Windows desktop app for public media you own or are authorized to save.

Choose the per-user Windows x64 installer for normal use or a portable archive when needed. Verify `SHA256SUMS.txt` before running or extracting an asset. GitHub Actions release artifacts carry build-provenance attestations; the release manifest states whether Windows executables also have an Authenticode signature.

Highlights:

- adds MP3, AAC/M4A, Opus/OGG, WAV, and FLAC audio outputs plus optional H.264/AAC MP4, H.265/AAC MP4, and VP9/Opus WebM conversion;
- adds Best original, Windows MP4, Small file, MP3 320, and Custom quick presets with optional advanced format controls;
- adds opt-in soft-subtitle embedding, chapter embedding/splitting, bounded trimming, and disabled-by-default SponsorBlock chapter/removal workflows;
- adds bounded public active/upcoming HLS capture with recoverable segment journals and validated MKV finalization;
- adds playlist/channel archive profiles, Library export/import/rescan tools, queue recovery, disk forecasting, and unified proxy/retry controls;
- accelerates validated large transfers while retaining safe sequential fallback and resumable state;
- removes redundant container names from filename stems and adds an opt-in quality suffix: audio bitrate/lossless or video resolution;
- migrates existing filename settings and archive profiles so new outputs use one real extension, for example `Song.mp3` or optionally `Song 320kbps.mp3`.

Security and support boundaries:

- no login, cookies, private, paid, membership, DRM, or access-control bypass;
- no encrypted HLS or generic non-YouTube M3U8 capture;
- updates never install silently and require explicit confirmation;
- upstream YouTube changes can temporarily break public extraction.

Read [installation, upgrades, rollback, and data retention](INSTALLATION.md), [extraction compatibility](EXTRACTION_COMPATIBILITY.md), and the [support policy](SUPPORT_POLICY.md).

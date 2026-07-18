# TubeForge v1.2.1

TubeForge is an ad-free Windows desktop downloader for public media you own or are authorized to save. It has no `yt-dlp`, hosted converter, third-party managed package, telemetry, account, or paid feature. This release bundles pinned LGPL FFmpeg for MP4, WebM, and MKV stream-copy finalization.

Choose the per-user Windows x64 installer for normal use or a portable archive when needed. Verify `SHA256SUMS.txt` before running or extracting an asset. GitHub Actions release artifacts also carry signed build-provenance attestations; the per-release manifest states whether the Windows executable additionally has an Authenticode signature.

Read [installation, upgrades, rollback, and data retention](INSTALLATION.md), [extraction compatibility](EXTRACTION_COMPATIBILITY.md), and the [support policy](SUPPORT_POLICY.md).

Highlights:

- fixes MP4 files that exposed audio-only playback, failed video playback, or severe lag in common Windows players;
- converts YouTube fragmented DASH MP4 into conventional indexed MP4 with `-c copy` and `+faststart`, preserving source video/audio quality;
- applies compatibility normalization to adaptive audio + video, progressive MP4, and video-only MP4 outputs;
- finalizes every watch-page quality through FFmpeg stream-copy — including 1440p, 4K, and 8K VP9 and AV1 — routing WebM outputs through FFmpeg instead of the in-house muxer for the same proven reliability as MP4;
- adds lossless Matroska (MKV) output as a cross-container fallback, so a video codec always pairs with the best available audio track even when no same-container audio family exists; native MP4/WebM output is unchanged for the common case;
- fails closed instead of publishing an MP4, WebM, or MKV when bundled FFmpeg is absent, fails, or leaves fragmented output;
- recovers valid MP4 output after a crash between atomic publication and queue checkpoint without redownloading tracks;
- pins FFmpeg 8.1.2 x64 LGPL archive, SHA-256, license, exact source, and build-script provenance;
- fixes source-dependent HTTP 403 failures affecting many medium, long, and high-quality adaptive downloads;
- prevents long multi-range transfers from failing when unchanged resume metadata is briefly locked;
- fixes default artifact-path resolution in the Windows PowerShell packaging scripts;
- rejects unusable client streams during analysis using bounded end-of-stream probes;
- preserves client-specific media request identity and uses resumable bounded Googlevideo ranges;
- highest-quality audio + video downloads use separate YouTube tracks and FFmpeg stream-copy finalization instead of falling back to 360p;
- MP4 is preferred when an equivalent-quality choice exists, while WebM remains available;
- native audio saves and Windows Media Foundation MP3 conversion at 128–320 kbps;
- branded per-user installer, Add/Remove Programs integration, uninstall data choice, and opt-in verified updates;
- durable queue recovery, accurate conversion progress, and completed-output recovery after restart.

Known limitations:

- no active/upcoming live capture;
- no video re-encoding;
- no login, private, paid, membership, DRM, or access-control bypass;
- updates never install silently and require explicit confirmation;
- upstream YouTube changes can temporarily break public extraction.


# TubeForge v1.1.7

TubeForge is an ad-free Windows desktop downloader for public media you own or are authorized to save. It has no `yt-dlp`, FFmpeg, hosted converter, third-party application package, telemetry, account, or paid feature.

Choose the per-user Windows x64 installer for normal use or a portable archive when needed. Verify `SHA256SUMS.txt` before running or extracting an asset. GitHub Actions release artifacts also carry signed build-provenance attestations; the per-release manifest states whether the Windows executable additionally has an Authenticode signature.

Read [installation, upgrades, rollback, and data retention](INSTALLATION.md), [extraction compatibility](EXTRACTION_COMPATIBILITY.md), and the [support policy](SUPPORT_POLICY.md).

Highlights:

- fixes source-dependent HTTP 403 failures affecting many medium, long, and high-quality adaptive downloads;
- prevents long multi-range transfers from failing when unchanged resume metadata is briefly locked;
- fixes default artifact-path resolution in the Windows PowerShell packaging scripts;
- rejects unusable client streams during analysis using bounded end-of-stream probes;
- preserves client-specific media request identity and uses resumable bounded Googlevideo ranges;
- highest-quality audio + video downloads use separate YouTube tracks and TubeForge's internal MP4/WebM muxers instead of falling back to 360p;
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


# TubeForge v2.2.3

TubeForge v2.2.3 fixes four defects found during installed v2.2.2 end-to-end testing: trim loss across Quick presets, misleading queue state during local conversion, cancelled work recovering as paused, and a modern public-playlist lockup shape rejected by the collection parser.

> [!IMPORTANT]
> TubeForge v2.1.0 can detect a newer release, but its installed binary cannot enable the update button and does not contain the startup prompt. It cannot repair itself. If you are running v2.1.0, download and run `TubeForge-2.2.3-win-x64-setup.exe` once from the official release. Updates after that can use the in-app flow.

Choose the per-user Windows x64 installer for normal use or a portable archive when needed. Verify `SHA256SUMS.txt` before running or extracting an asset. GitHub Actions release artifacts carry build-provenance attestations; the release manifest states whether Windows executables also have an Authenticode signature.

Highlights:

- keeps an enabled trim range when switching between compatible Quick presets;
- labels local audio/video conversion as `PROCESSING` and shows explicit FFmpeg phase detail instead of stale transfer ETA;
- persists and renders `Cancelled` before stopping active work, preventing cancelled conversion jobs from recovering as paused after restart;
- accepts modern playlist lockups with deeply nested watch commands under a bounded parser fallback;
- retains strict video-ID, title, traversal-depth, and collection-size limits;
- retains verified installer download, digest checks, quiet per-user update, wait-for-current-process, and relaunch behavior;
- preserves settings, queue state, Library history, and downloaded media through the update.

Security and support boundaries:

- no update installs without the user choosing `Update now`;
- no login, cookies, private, paid, membership, DRM, or access-control bypass;
- no encrypted HLS or generic non-YouTube M3U8 capture;
- upstream YouTube changes can temporarily break public extraction.

Read [installation, upgrades, rollback, and data retention](INSTALLATION.md), [extraction compatibility](EXTRACTION_COMPATIBILITY.md), and the [support policy](SUPPORT_POLICY.md).

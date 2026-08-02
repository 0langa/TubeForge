# TubeForge v2.2.4

TubeForge v2.2.4 fixes the remaining public-playlist failure found during installed v2.2.3 retesting. YouTube can return all current playlist items in initial HTML, then answer its continuation token with a valid context-only terminal response. TubeForge now ends enumeration cleanly for that narrowly validated response instead of reporting `Extractor.CollectionPageChanged`.

> [!IMPORTANT]
> TubeForge v2.1.0 can detect a newer release, but its installed binary cannot enable the update button and does not contain the startup prompt. It cannot repair itself. If you are running v2.1.0, download and run `TubeForge-2.2.4-win-x64-setup.exe` once from the official release. Updates after that can use the in-app flow.

Choose the per-user Windows x64 installer for normal use or a portable archive when needed. Verify `SHA256SUMS.txt` before running or extracting an asset. GitHub Actions release artifacts carry build-provenance attestations; the release manifest states whether Windows executables also have an Authenticode signature.

Highlights:

- retains v2.2.3 fixes for trim persistence, truthful local-processing state, durable cancellation, and bounded modern lockup parsing;
- accepts a context-only response as terminal only for continuation JSON with object `responseContext` and non-empty string `trackingParams` after no items or next token were found;
- keeps initial collection HTML and arbitrary empty or malformed continuation JSON fail-closed;
- retains strict video-ID, title, traversal-depth, collection-size, and configuration-token limits;
- retains verified installer download, digest checks, quiet per-user update, wait-for-current-process, and relaunch behavior;
- preserves settings, queue state, Library history, and downloaded media through the update.

Security and support boundaries:

- no update installs without the user choosing `Update now`;
- no login, cookies, private, paid, membership, DRM, or access-control bypass;
- no encrypted HLS or generic non-YouTube M3U8 capture;
- upstream YouTube changes can temporarily break public extraction.

Read [installation, upgrades, rollback, and data retention](INSTALLATION.md), [extraction compatibility](EXTRACTION_COMPATIBILITY.md), and the [support policy](SUPPORT_POLICY.md).

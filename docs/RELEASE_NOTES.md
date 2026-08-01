# TubeForge v2.2.2

TubeForge v2.2.2 hardens the verified one-click updater under supervised app launches. The staged setup now starts through the Windows graphical shell before TubeForge exits, while retaining the quiet install, wait-for-current-process, and relaunch contract.

> [!IMPORTANT]
> TubeForge v2.1.0 can detect a newer release, but its installed binary cannot enable the download button and does not contain the startup prompt. It cannot repair itself. If you are running v2.1.0, download and run `TubeForge-2.2.2-win-x64-setup.exe` once from the official release. Updates after that can use the in-app flow.

Choose the per-user Windows x64 installer for normal use or a portable archive when needed. Verify `SHA256SUMS.txt` before running or extracting an asset. GitHub Actions release artifacts carry build-provenance attestations; the release manifest states whether Windows executables also have an Authenticode signature.

Highlights:

- starts the verified installer through the Windows graphical shell to reduce direct child-process coupling;
- safely reports Windows shell launch failures instead of allowing an unhandled updater exception;
- retains the startup prompt with `Later` and `Update now` actions;
- retains verified download progress and prevents prompt dismissal while the installer downloads;
- validates both GitHub and manifest SHA-256 records, then re-hashes the staged installer before execution;
- waits for the current TubeForge process to close, installs for the current Windows user, and relaunches TubeForge;
- preserves settings, queue state, Library history, and downloaded media through the update.

Security and support boundaries:

- no update installs without the user choosing `Update now`;
- no login, cookies, private, paid, membership, DRM, or access-control bypass;
- no encrypted HLS or generic non-YouTube M3U8 capture;
- upstream YouTube changes can temporarily break public extraction.

Read [installation, upgrades, rollback, and data retention](INSTALLATION.md), [extraction compatibility](EXTRACTION_COMPATIBILITY.md), and the [support policy](SUPPORT_POLICY.md).

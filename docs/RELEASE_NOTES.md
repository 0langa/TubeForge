# TubeForge v2.2.0

TubeForge v2.2.0 fixes the disabled update action and adds an explicit one-click update flow. When a stable release is detected, TubeForge can now download and verify the official installer, close the running app, install the update, and launch the updated version.

Choose the per-user Windows x64 installer for normal use or a portable archive when needed. Verify `SHA256SUMS.txt` before running or extracting an asset. GitHub Actions release artifacts carry build-provenance attestations; the release manifest states whether Windows executables also have an Authenticode signature.

Highlights:

- fixes the Settings update button remaining disabled after a successful release check;
- adds an update-available prompt with `Later` and `Update now` actions;
- shows verified download progress and prevents prompt dismissal while the installer downloads;
- makes `Update now` download and validate both GitHub and manifest SHA-256 records, then re-hash the staged installer before execution;
- starts the installer in unattended update mode, waits for the current TubeForge process to close, installs for the current Windows user, and relaunches TubeForge;
- preserves settings, queue state, Library history, and downloaded media through the update.

Security and support boundaries:

- no update installs without the user choosing `Update now`;
- no login, cookies, private, paid, membership, DRM, or access-control bypass;
- no encrypted HLS or generic non-YouTube M3U8 capture;
- upstream YouTube changes can temporarily break public extraction.

Read [installation, upgrades, rollback, and data retention](INSTALLATION.md), [extraction compatibility](EXTRACTION_COMPATIBILITY.md), and the [support policy](SUPPORT_POLICY.md).

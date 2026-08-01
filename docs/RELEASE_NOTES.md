# TubeForge v2.2.1

TubeForge v2.2.1 is a recovery-verification release for the updater introduced in v2.2.0. It adds an end-to-end startup regression proving a detected stable release raises the prompt and immediately enables `Update now`.

> [!IMPORTANT]
> TubeForge v2.1.0 can detect a newer release, but its installed binary cannot enable the download button and does not contain the startup prompt. It cannot repair itself. If you are running v2.1.0, download and run `TubeForge-2.2.1-win-x64-setup.exe` once from the official release. Updates after that can use the in-app flow.

Choose the per-user Windows x64 installer for normal use or a portable archive when needed. Verify `SHA256SUMS.txt` before running or extracting an asset. GitHub Actions release artifacts carry build-provenance attestations; the release manifest states whether Windows executables also have an Authenticode signature.

Highlights:

- carries forward the v2.2.0 fix that enables the Settings update button after a successful release check;
- carries forward the startup prompt with `Later` and `Update now` actions;
- adds regression coverage for the complete automatic-check, prompt-event, and command-enable path;
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

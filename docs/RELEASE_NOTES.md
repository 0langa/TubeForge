# TubeForge v2.4.0

TubeForge v2.4.0 stops downloads and updates from hanging on a connection that goes silent, cleans up after failed and cancelled downloads when you remove them, and lets a signed build pin its updates to its own certificate.

> [!IMPORTANT]
> TubeForge v2.1.0 can detect a newer release, but its installed binary cannot enable the update button and does not contain the startup prompt. It cannot repair itself. If you are running v2.1.0, download and run `TubeForge-2.4.0-win-x64-setup.exe` once from the official release. Updates after that can use the in-app flow.

Choose the per-user Windows x64 installer for normal use or a portable archive when needed. Verify `SHA256SUMS.txt` before running or extracting an asset. GitHub Actions release artifacts carry build-provenance attestations; the release manifest states whether Windows executables also have an Authenticode signature.

## What changed

### Silent connections

A connection that stops delivering data, such as dropped Wi-Fi, an expired NAT mapping, or a server that goes quiet, used to leave a download at "Downloading NN%" with its queue slot held until you paused it. Downloads, segmented ranges, live captures, and caption fetches now fail over after 60 seconds without data, and the normal retry resumes from what is already on disk. A connection that times out while connecting is retried instead of pausing the job.

**Update now**, the update check, **Save thumbnail**, and playlist loading fail safely the same way instead of hanging. A slow SponsorBlock lookup no longer marks a job as paused.

### Removing failed and cancelled downloads

**Remove** on a Failed or Cancelled item now also deletes what that download left beside its destination: the partial file and its resume state, intermediate audio and video tracks, pre-processing sources, a live capture's segment folder, and FFmpeg scratch files. These could run to gigabytes, and the leftover partial file pushed the next download of the same video to "Title (1)".

Only files named after that item's destination with a suffix TubeForge itself writes are deleted. The destination file, your other files, links, folders holding anything unexpected, and anything another queue item still uses are left alone. Paused items keep their resume data when removed.

### Update signing

When the running TubeForge is Authenticode-signed, the in-app updater now also requires the downloaded installer to be signed by the same certificate before it launches it.

**TubeForge releases are not signed today, and this one is not either. The certificate check therefore does not run: updates are still verified only by the SHA-256 checks against the release API digest and `SHA256SUMS.txt`, exactly as in v2.3.2.** The check takes effect once releases are signed.

### Smaller fixes

- Output names that Windows 10 opens as devices, such as `COM0`, `LPT0`, `CONIN$`, `CONOUT$`, `COM¹`, or `NUL .mp4`, get a leading `_`.
- Chapter and SponsorBlock-chapter embedding works for long titles. An internal probe file name went over 255 characters.
- A thumbnail save whose connection dropped is reported as a network failure, not as a failure to save the file.
- FFmpeg's diagnostic output is capped at its last 64K characters.
- Workflow actions are pinned to full commit SHAs, and Dependabot proposes their updates once a month.

## Known limitations

- While releases stay unsigned, the certificate check above never runs. Once signing is enabled, a certificate change such as a renewal needs one manual install.
- A stalled transfer uses one of a download's three attempts, so an outage longer than about a minute still ends as Failed. **Retry** resumes from the partial file.
- Removing a Paused item keeps its partial files.

## Verification

323 deterministic tests, a zero-warning Release build, the core parser performance budget, and a parse check of every release script. New coverage uses stub handlers and temporary folders only: silent transfers and update downloads, connect timeouts, owned leftover names against look-alikes that must survive, and the signer policy against real signed, tampered, and unsigned files. Published artifacts are rebuilt from the immutable release tag by GitHub Actions, which verifies archive checksums, the FFmpeg payload and the single-bundle layout, and the installer checksum and embedded payload, before it publishes them.

Everything in [v2.3.0](https://github.com/0langa/TubeForge/releases/tag/v2.3.0), [v2.3.1](https://github.com/0langa/TubeForge/releases/tag/v2.3.1), and [v2.3.2](https://github.com/0langa/TubeForge/releases/tag/v2.3.2) carries forward unchanged.

Security and support boundaries:

- no update installs without the user choosing `Update now`;
- no login, cookies, private, paid, membership, DRM, or access-control bypass;
- no encrypted HLS or generic non-YouTube M3U8 capture;
- upstream YouTube changes can temporarily break public extraction.

Read [installation, upgrades, rollback, and data retention](INSTALLATION.md), [extraction compatibility](EXTRACTION_COMPATIBILITY.md), and the [support policy](SUPPORT_POLICY.md).

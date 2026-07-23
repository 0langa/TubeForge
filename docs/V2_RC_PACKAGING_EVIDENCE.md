# TubeForge v2 Release-Candidate Packaging Evidence

Evidence date: 2026-07-23.

Candidate source: commit `6fa9289` on `cdx/v2-phase1-formats`.

This file records sanitized aggregate evidence only. It contains no media URLs, video identifiers, titles, channel names, signed stream URLs, credentials, user-data contents, or private local paths.

## Source gates

- Release build: passed with 0 warnings and 0 errors.
- Full deterministic suite: 238/238 passed.
- Formatter verification: passed.
- Exact candidate CI: passed in GitHub Actions run `29975627857`.
- Isolated performance rerun: passed; core parser p95 0.2214 ms, startup 1,743.7 ms, idle CPU 0%, working set 146.76 MiB, and UI frame p95 39.927 ms.
- One earlier combined performance sample exceeded the 4,000 ms startup budget at 4,491 ms. The isolated rerun passed; cold-start variance remains a release-monitoring risk.

## Portable packages

`Publish-Release.ps1` produced version 2.0.0 framework-dependent and self-contained Windows x64 archives plus a release manifest and SHA-256 manifest.

`Test-Release.ps1` passed:

- checksum verification;
- safe archive extraction and dependency-layout checks;
- pinned bundled FFmpeg version verification;
- self-contained application launch probe.

Candidate hashes:

- framework-dependent ZIP: `896839E71F4BF05C77F6C5DA9017BB256741D276AE65436850ECAC9698E68867`;
- self-contained ZIP: `A5E4A4C56B2D8B8D0AA381C3AE96CB951132B2354AAC431E70A7835AE31C2EC9`;
- release manifest: `EF9A16CA450069702AC4D4C57A48AB4F3C18270A0247610F6384BD8A0F916FD9`.

## Installer package

`Publish-Installer.ps1` produced the version 2.0.0 per-user Windows x64 setup executable and SHA-256 manifest.

`Test-Installer.ps1` passed checksum, embedded-payload, and signature-state verification.

Candidate installer hash:

- setup executable: `15BA4BB2983BE54209F60A74E382D8F13B779D97E4B304A5B09766F64A9B47AA`.

## Installed candidate system proof

The final rebuilt setup was exercised on the current Windows workstation with the existing v1.2.5 user data protected by an immutable hash snapshot:

- v1.2.5 to v2.0.0 update passed, changed the application executable, and preserved all five user-data files byte-for-byte;
- keep-data uninstall removed the v2 program directory and uninstall registration while preserving all five user-data files byte-for-byte;
- a clean-state v2 install passed after the existing application data was isolated outside the active profile;
- the installed application process launched and stayed running, but no main-window handle became ready during the bounded probe under severe host memory pressure, so this is not recorded as a packaged UI-readiness pass;
- quiet remove-data uninstall removed the program directory, uninstall registration, and a fresh application-data sentinel;
- every transaction restored v1.2.5, its exact executable hash, all five original user-data files, and the pre-test absence of a rollback directory.

The remove-data relocation path is covered by a deterministic regression test so `/uninstall /quiet /remove-data` cannot silently lose the removal intent.

## Installed UI and accessibility probe

The final installed candidate later produced a ready main window on the current workstation. Severe host memory pressure made readiness take 37,160 ms, so this observation proves window creation but does not satisfy the 4,000 ms desktop performance budget; the isolated desktop performance run remains the valid budget evidence.

A policy-controlled Windows accessibility inspection confirmed:

- named Download, Queue, Library, Settings, and Diagnostics navigation buttons;
- a named URL input and analysis action;
- named settings inputs for folder, filename template, preset, concurrency, proxy, retries, updates, and save/check actions;
- named Diagnostics copy/export actions and runtime state;
- keyboard focus reached the default-download-folder editor from the selected Settings navigation control.

The installed Diagnostics copy action produced valid JSON. Aggregate inspection found no user name, user-profile path, URL, or sensitive media identity in ordinary string values; sensitive vocabulary appeared only in the report's explicit exclusion declarations.

This was not a Narrator pass, high contrast was not enabled, and Windows scaling was not changed. Narrator, high-contrast, and the full 100/125/150/200-percent DPI matrix remain manual release gates.

## Trust state

The candidate is intentionally recorded as unsigned. No Authenticode certificate was supplied, the release manifest reports `authenticodeSigned: false`, and the setup executable has no signer certificate. SHA-256 manifests are present, but they do not replace Authenticode reputation or GitHub build-provenance attestations from the final release workflow.

## Open release gates

- authorized current-upstream public live and output-matrix canaries;
- installed-app playback, quality, duration, and size measurements;
- independent clean-Windows repetition with a ready packaged UI window;
- packaged Narrator, high-contrast, and 100/125/150/200 percent DPI checks;
- final diagnostics redaction pass after live failure scenarios;
- final public documentation and release-note sync;
- GitHub release publication and post-release package-manager manifests.

No tag or GitHub release was created from this evidence run.

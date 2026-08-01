# TubeForge v2 Release-Candidate Packaging Evidence

Evidence date: 2026-07-27.

Candidate source: commit `2aead44` on `cdx/v2-phase1-formats`.

This file records sanitized aggregate evidence only. It contains no media URLs, video identifiers, titles, channel names, signed stream URLs, credentials, user-data contents, or private local paths.

## Source gates

- Release build: passed with 0 warnings and 0 errors.
- Full deterministic suite: 239/239 passed.
- Formatter verification: passed.
- Exact candidate CI: passed in GitHub Actions run `30225244992`.
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

- framework-dependent ZIP: `44BB5B5CD1A8F406F29840AF1A61CFF099AA652BC4E97AFA79ADC6F81307A9E8`;
- self-contained ZIP: `4D92B2BC628351E3B2AE671625E2AB06DBC99D3E32E7563220505DA515E0BE26`;
- release manifest: `C9FA56F9736989E09551B05083B8FE064376B7B98C1F401C3842734AC923C891`.

## Installer package

`Publish-Installer.ps1` produced the version 2.0.0 per-user Windows x64 setup executable and SHA-256 manifest.

`Test-Installer.ps1` passed checksum, embedded-payload, and signature-state verification.

Candidate installer hash:

- setup executable: `B54AC9C10098EA71F57877003BF0ABE81C8AD57CCD0B1ECAE6EC96913503AD9D`.

## Installed candidate system proof

The exact `2aead44` setup was exercised on the current Windows workstation with the existing v1.2.5 user data protected by an immutable hash snapshot:

- v1.2.5 to v2.0.0 update passed and activated product version `2.0.0+2aead449543e626f0dd8a7c5ea19adfe0f5df957`;
- the installed application produced a ready main window and resolved an authorized public video through the default `System` proxy mode with 27 formats and 23 matching outputs;
- the exact installed candidate downloaded and converted a 213.04-second MP3 output at the 320 kbps preset; the 8,523,885-byte file passed a full bundled-FFmpeg decode;
- keep-data uninstall removed the v2 program directory and uninstall registration while preserving application data;
- v1.2.5 was restored with product version `1.2.5+d3826d100976fc2ba61c07bd1ca63789399e2815`, no running process, and all five original user-data files matching the protected snapshot byte-for-byte.

An earlier installed candidate exposed a Windows direct-access defect in the custom system-proxy wrapper: the operating-system proxy returned `null`, but TubeForge replaced it with the destination URI and produced `Network.RequestFailed`. Commit `2aead44` preserves the operating-system result, adds deterministic regression coverage, and routes the live smoke tool through the same configurable system-proxy path. The sanitized system-proxy canary set passed 2/2, and the exact packaged candidate then passed the installed `System`-mode analysis above.

Supplemental installed-media evidence from the immediately preceding candidate, whose media stack is unchanged by the network-only fix, produced a 97,747,368-byte 1280x720 HEVC/AAC MP4 with two ordered soft-subtitle streams. Its 213.04-second duration and complete decode passed. This supplements but does not replace the remaining exact-candidate output matrix.

The remove-data relocation path is covered by a deterministic regression test so `/uninstall /quiet /remove-data` cannot silently lose the removal intent.

## Installed UI and accessibility probe

The exact installed candidate produced a ready main window on the current workstation. This observation proves packaged window creation but is not a controlled startup-performance sample; the isolated desktop performance run remains the valid budget evidence.

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

- authorized current-upstream public-live HLS canary;
- remaining exact-candidate output matrix, including H.264/AAC, VP9/Opus, AAC, Opus, WAV, and FLAC profiles plus Windows playback where codecs are available;
- independent clean-Windows repetition with a ready packaged UI window;
- packaged Narrator, high-contrast, and 100/125/150/200 percent DPI checks;
- final diagnostics redaction pass after live failure scenarios;
- final public documentation and release-note sync;
- GitHub release publication and post-release package-manager manifests.

No tag or GitHub release was created from this evidence run.

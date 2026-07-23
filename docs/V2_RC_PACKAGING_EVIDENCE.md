# TubeForge v2 Release-Candidate Packaging Evidence

Evidence date: 2026-07-23.

Candidate source: commit `f41481d` on `cdx/v2-phase1-formats`.

This file records sanitized aggregate evidence only. It contains no media URLs, video identifiers, titles, channel names, signed stream URLs, credentials, user-data contents, or private local paths.

## Source gates

- Release build: passed with 0 warnings and 0 errors.
- Full deterministic suite: 233/233 passed.
- Formatter verification: passed.
- Exact candidate CI: passed in GitHub Actions run `29970204407`.
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

- framework-dependent ZIP: `17993B44B782B99C5FAE48AA00AB6D00AF81DC023DBA34481BDF6287081CE2A3`;
- self-contained ZIP: `39C3511D81B52E182D35DDB99363EF3CB6E429DC2477308A3EDE4B5EA98F54D9`;
- release manifest: `322CBFDED81EF8A290394FC6BAF70B5E35E5AADAF94D2B232B2E0B3A6F2390BD`.

## Installer package

`Publish-Installer.ps1` produced the version 2.0.0 per-user Windows x64 setup executable and SHA-256 manifest.

`Test-Installer.ps1` passed checksum, embedded-payload, and signature-state verification.

Candidate installer hash:

- setup executable: `2E1C04D43CC8B1CC33FB352365F42037F33E4F9E21BC794D1F1672C2CD168155`.

## Trust state

The candidate is intentionally recorded as unsigned. No Authenticode certificate was supplied, the release manifest reports `authenticodeSigned: false`, and the setup executable has no signer certificate. SHA-256 manifests are present, but they do not replace Authenticode reputation or GitHub build-provenance attestations from the final release workflow.

## Open release gates

- authorized current-upstream public live and output-matrix canaries;
- installed-app playback, quality, duration, and size measurements;
- fresh install, update from v1.2.5, and both uninstall-retention modes on the final candidate;
- packaged Narrator, high-contrast, and 100/125/150/200 percent DPI checks;
- final diagnostics redaction pass after live failure scenarios;
- final public documentation and release-note sync;
- GitHub release publication and post-release package-manager manifests.

No tag or GitHub release was created from this evidence run.

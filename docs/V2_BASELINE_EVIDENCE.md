# TubeForge v2 Baseline Evidence

Baseline date: 2026-07-22.

Baseline source: TubeForge v1.2.5, commit `d3826d1`.

This file records sanitized aggregate evidence only. It contains no media URLs, video identifiers, titles, channel names, signed stream URLs, credentials, user-data contents, or private local paths.

## Deterministic gates

- Release build: passed with 0 warnings and 0 errors.
- Full deterministic suite: 177/177 passed.
- Core parser probe: passed; p95 0.362 ms against 25 ms budget.

## Desktop performance

One initial cold-machine sample reported startup at 4,338.5 ms and failed the 4,000 ms hard budget. Ten subsequent clean launches all passed:

- startup p50: 2,419.5 ms;
- startup p95/max: 2,787.9 ms;
- hard-budget passes: 10/10;
- UI frame, idle CPU, and working-set budgets: passed in all ten runs.

Cold-machine variance above the hard limit remains a release-monitoring risk. It is not hidden by the passing repeat set.

## Live extraction

- Sanitized public canary: 1/1 passed.
- Resolved formats: 27 total; 1 progressive, 4 audio-only, 22 video-only.
- Highest-quality selection used adaptive video and audio with MKV mux fallback.

## Release and installer

- Fresh framework-dependent and self-contained portable artifacts built.
- Portable checksums, archive layout, bundled dependency layout, and launch probes passed.
- Fresh installer built.
- Installer checksum and embedded-payload verification passed.
- Quiet install passed.
- Quiet uninstall removed program files while preserving all existing application-data files byte-for-byte.
- Quiet reinstall passed and restored v1.2.5 from commit `d3826d1`.
- Destructive local-data removal was not run against the user profile. Isolated deterministic installer tests cover that code path.

## Baseline conclusion

Phase 0 baseline is healthy enough for v2 development. Fresh live playback/download matrices, updater migration, signed-release trust, long-run queue soak, accessibility, and destructive uninstall validation remain v2 release-candidate gates.

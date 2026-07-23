# TubeForge v2.0 Development Plan

Target: TubeForge 2.0 should be release-grade, user-friendly, and functionally competitive with popular YouTube downloaders while preserving TubeForge's strongest differentiators: local-only operation, no ads, no telemetry, no hosted conversion, strict redaction, and safer bounded extraction.

## Implementation Status

Status date: 2026-07-22.

- Phase 0 complete. Sanitized baseline evidence: [`docs/V2_BASELINE_EVIDENCE.md`](docs/V2_BASELINE_EVIDENCE.md).
- Phase 1 core implementation complete in working tree: expanded audio outputs plus resolution-aware H.264/AAC MP4, H.265/AAC MP4, and VP9/Opus WebM profiles.
- General output profiles persist through queue restart, validate before publication/recovery, clean failed temporary files, and use allowlisted FFmpeg arguments from the pinned LGPL build.
- Verification: Release build 0 warnings/errors; 233/233 deterministic tests; isolated core parser p95 0.2214 ms against 25 ms and desktop budgets passed; bundled FFmpeg encode/decode smoke passed for all four new audio outputs, all three video profiles, MP4/MKV/WebM soft-subtitle/chapter embed/split workflows, trim, SponsorBlock removal, and synthetic HLS-to-MKV capture.
- Phase 1 release proof remains open: installed-app live media matrix and measured quality/time/file-size evidence. Broader custom/device presets remain deferred to UX work.
- Preset-first UX is implemented for Best original, Windows MP4, Small file, MP3 320, and Custom; deterministic tests cover applied state and manual override behavior.
- Phase 2 subtitle slice implemented: selected manual/auto track can be embedded as a soft subtitle in a single-video MP4, MKV, or WebM download. Queue identity persists language/type without storing caption URLs; FFmpeg validates a subtitle stream before publication or recovery.
- Phase 2 chapter workflows implemented: embed/split intent survives queue restart; embedding combines with a selected soft subtitle in one atomic FFmpeg pass; splitting keeps the full file and atomically publishes a sibling folder using sanitized `{chapterIndex}` / `{chapterTitle}` names. Both paths validate outputs before publication or recovery.
- Phase 2 timeline editing implemented: bounded start/end trim persists through queue restart, uses keyframe-aligned stream copy for original outputs, applies precise trim during a selected transcode, and rebases embedded captions and chapters.
- Phase 2 SponsorBlock integration implemented as a disabled-by-default third-party opt-in. A four-character SHA-256 video-ID prefix is sent to the official API, candidates are matched locally, selected categories can become chapter markers or be removed during an explicit audio/video transcode, and neither IDs nor response payloads enter diagnostics or queue state.
- Phase 5 implemented: Settings exposes system/manual/off proxy policy, bounded metadata timeout/media retry/per-host concurrency controls, and applies one credential-free proxy object to metadata, collections, captions, thumbnails, media, and updates. Schema migration is safe, diagnostics emit mode only, and loopback tests prove metadata and media proxy paths.
- Phase 3 decision complete: v2.0 follows Option A and remains public-only. Cookie import and OAuth are deferred; login-required, private, membership, paid, and other access-controlled media fail with a stable typed error and no credential collection.
- Phase 4 public-live implementation complete in the working tree: active record-from-now and upcoming wait modes use bounded unencrypted HLS, duration/size/wait limits, trusted-host redirect checks, retrying segment downloads, recoverable hash-only journals, queue pause/resume, and atomic MKV stream-copy finalization. A real authorized public-live canary remains a release gate.
- Phase 6 implemented: schema-versioned Library transfer/repair plus persistent playlist/channel archive profiles. Profiles retain destination/template/output/caption/chapter preferences and bounded checked-item sets; user-initiated checks queue only new items, while Select missing identifies current collection gaps across Queue and Library.
- Phase 7 implementation complete: preset-first simple mode hides detailed format controls until requested, Custom reveals them, schema-v5 settings persist a default preset and disclosure choice, first run captures folder/preset/update preferences, vector navigation replaces text glyphs, and error recovery links expose destination, Settings, Diagnostics, and redacted-report actions. Manual Narrator/high-contrast/DPI release passes remain open.

## v2 Product Definition

v2.0 should mean:

- Best-effort parity with mainstream YouTube downloader GUIs for public and user-authorized YouTube content.
- Strong Windows desktop experience with installer, updater, signed release, clear defaults, queue recovery, Library, diagnostics, and polished UX.
- Broad practical output support through FFmpeg, without pretending every conversion is lossless.
- Clear legal/security boundary: no DRM bypass, no payment/membership bypass, no hidden credential collection, no silent updates, no telemetry.

Recommended v2 stance:

- Primary scope: YouTube-first Windows downloader.
- Secondary scope: architecture-ready for more providers later.
- Do not add broad multi-site support before v2.0 unless user explicitly chooses "yt-dlp-backed generic mode" as product direction. That would be a larger pivot and would weaken current clean-room/no-third-party posture.

## Release Criteria

v2.0 release cannot ship until all are true:

- Release build: 0 warnings, 0 errors.
- Full deterministic test suite passes.
- Core and desktop performance budgets pass.
- Public live canary matrix passes on current YouTube behavior.
- Installer packaging and verification pass.
- Fresh install, uninstall, and update from v1.2.5 pass.
- SHA-256 manifests, release manifest, third-party notices, and optional Authenticode state are correct.
- Diagnostics redaction passes after normal use and failure scenarios.
- README, support policy, release notes, and in-app text match actual feature surface.
- No v2 feature is claimed without deterministic tests plus at least one release-smoke proof where network/upstream behavior matters.

## Phase 0: Freeze Current Baseline

Goal: establish trustworthy v1.2.5 baseline before adding v2 surface.

Tasks:

- [x] Record current `main` commit, version, and git clean state.
- [x] Run:

```powershell
dotnet build TubeForge.slnx --configuration Release
dotnet run --project tests\TubeForge.Tests --configuration Release --no-build -- --all
dotnet run --project tools\TubeForge.Performance --configuration Release --no-build
```

- [x] Run sanitized canary set and save aggregate results only.
- [x] Run release and installer scripts for current version in a disposable output directory.
- [x] Install current release locally, then uninstall, confirming app data retention/removal behavior.

Exit gate:

- Baseline evidence file exists under `docs/` or release notes, with no secrets, URLs, local private paths, or media.

## Phase 1: Output Formats And Conversion

Goal: satisfy "many different audio and video formats" without compromising reliability.

### 1.1 Output Model

Add explicit output categories:

- Original/stream-copy outputs: MP4, WebM, MKV, M4A, WebM audio.
- Audio transcode outputs: MP3, AAC/M4A, Opus/OGG, WAV, FLAC.
- Video transcode outputs: H.264/AAC MP4, H.265/AAC MP4, VP9/Opus WebM.
- Compatibility presets: Best quality, Windows compatible MP4, Small file, Audio only, Archive original, Custom.

Implementation notes:

- Use the general persisted `OutputProfile` model across native, audio-transcode, and video-transcode paths.
- Keep stream-copy default.
- Reuse `FfmpegAudioTranscoder` patterns for cancellation, temp paths, validation, and recovery.
- Add `FfmpegVideoTranscoder` with safe allowlisted FFmpeg arguments only.
- Validate output container and codec family after FFmpeg completes.

Tests:

- Argument construction for every preset.
- Atomic publication and cleanup on failure/cancel.
- Recovery after output is published but queue checkpoint is stale.
- Oversized/low-disk forecast for transcode temp files.
- UI exact-output selection tests for every new profile.

Completed audio slice:

- [x] AAC/M4A 256 kbps through FFmpeg native AAC encoder.
- [x] Opus/OGG 160 kbps through bundled `libopus`.
- [x] WAV PCM 16-bit and FLAC lossless outputs.
- [x] Atomic publication, cancellation/failure cleanup, validated-output recovery, queue identity persistence, filename extension/quality, and UI combination tests.
- [x] Real bundled-FFmpeg encode/decode smoke using synthetic audio only.
- [x] Generalize audio-only profile model for video transcode presets.
- [x] Add H.264/AAC MP4, H.265/AAC MP4, and VP9/Opus WebM video transcode paths.
- [x] Add transcode-specific disk forecast tests.
- [x] Run synthetic source encode/decode smoke through every pinned video encoder.
- [ ] Run installed-app live media proof and record measured quality/time/file-size evidence.

Exit gate:

- All new output formats are available in UI and queue, documented, and tested.

## Phase 2: Captions, Chapters, Trim, And SponsorBlock

Goal: match modern YouTube downloader workflows around subtitles and timeline editing.

### 2.1 Subtitle Modes

Add:

- [x] Save sidecar: current behavior.
- [x] Embed one selected soft-subtitle track in single-video MP4/MKV/WebM outputs.
- [ ] Multi-language caption selection for single videos and batch queue.
- [ ] Keep burn-in subtitle support out of v2 unless video transcode lands cleanly first.

### 2.2 Chapter Workflows

Add:

- [x] Embed chapters in output when container supports it.
- [x] Split by chapters into separate files while retaining the full media output.
- [x] Filename template tokens for chapter index/title.

### 2.3 Trim/Cut

Add:

- [x] Start/end trim controls.
- [x] FFmpeg stream-copy trim when keyframe-compatible.
- [x] Precise trim during re-encoding only when the user selects a transcode profile.
- [x] Rebase and clip embedded captions and chapters to the selected interval.

### 2.4 SponsorBlock

Add as opt-in:

- [x] Fetch SponsorBlock segments only after user enables the feature.
- [x] Categories selectable: sponsor, intro, outro, self-promo, interaction, preview, filler.
- [x] Modes: write chapters, remove segments, or ignore.
- [x] Keep removal explicit and safe: it requires an audio/video transcode profile and cannot be combined with embedded timed metadata.
- [x] Diagnostics and persisted queue state omit video IDs and response payloads.

Exit gate:

- [x] Timeline edits preserve sync across video, audio, captions, and chapters for supported combinations.
- [x] User-facing labels distinguish keyframe-aligned copy cuts from precise re-encoded cuts.

## Phase 3: Authenticated User-Owned Content Strategy

Goal: support private/unlisted/user-accessible content only if security posture remains strong.

Decision recorded for v2.0:

- [x] Option A: keep all authenticated content unsupported for v2.0.
- Option B: add local cookie import for user-owned/access-granted non-DRM content.
- Option C: add OAuth/browser login. This is highest risk and should not be first v2 path.

Current v2 policy:

- [x] Do not collect, import, store, or transmit account credentials or cookies.
- [x] Reject login-required, private, membership, paid, purchase, DRM, and other access-controlled media with a stable typed error.
- [x] Keep authentication disabled by default and absent from settings, queue state, Library, diagnostics, and logs.

Tasks if Option B chosen:

- Threat model credential lifecycle.
- Store credential material via Windows DPAPI or Credential Manager.
- Add "Authenticated access" settings section with import/delete/test buttons.
- Route metadata/media/caption/thumbnail requests through credential-aware HTTP handler.
- Add redaction tests for diagnostics, queue, settings, Library, logs, and crash paths.

Exit gate:

- [x] Option A requires no credential-bearing live smoke; a deterministic login-required fixture proves the public-only typed failure.
- [x] Auth-disabled path remains the only path and stores no credential material.

## Phase 4: Live, HLS/M3U8, And Long-Running Downloads

Goal: support common public live/HLS workflows without pretending DRM capture is supported.

Tasks:

- [x] Add HLS playlist parser with bounds on playlist size, segment count, redirect count, and segment bytes.
- [x] Add live capture modes: record from now, wait for upcoming stream, stop after duration/size.
- [x] Persist segment journal for resume and recovery without manifest or segment URLs.
- [x] Finalize to validated MKV through bundled FFmpeg stream copy.
- [x] Add recoverable queue state for long-running live captures.
- [x] Add clear unsupported errors for DRM/encrypted HLS, login-required, missing segments, and expired manifests.

Tests:

- [x] Synthetic HLS fixtures, discontinuities, retries, duplicate segments, malformed playlists, oversized playlists.
- [x] Cancellation and restart.
- [x] Disk forecast uses the configured maximum size for unknown final output.
- [ ] Live capture smoke using a public authorized source before release.

Exit gate:

- [x] Deterministic interruption/cancellation tests preserve a validated resumable journal and publish no corrupt output.
- [ ] Authorized public-live canary proves upstream behavior before release.

## Phase 5: Proxy And Network Controls

Goal: expose network features users expect from downloaders.

Tasks:

- [x] Add Settings -> Network:
  - [x] System proxy.
  - [x] Manual HTTP/HTTPS proxy.
  - [x] No proxy.
  - [x] Reject proxy credentials because no secure credential store is implemented.
- [x] Apply the same network policy to metadata, collections, captions, thumbnails, media, updates, and SponsorBlock.
- [x] Add bounded metadata-timeout and media-retry settings for power users.
- [x] Add per-host concurrency explanation and controls.

Tests:

- [x] Loopback proxy for metadata and media.
- [x] Auth proxy omitted; credential-bearing endpoints are rejected.
- [x] Diagnostics redaction: mode only, never endpoint/user data.
- [x] Bad proxy failure UX uses a typed settings validation message.

Exit gate:

- [x] Proxy behavior works end-to-end, not only in direct download engine tests.

## Phase 6: Playlist, Channel, And Library Management

Goal: move from one-off downloads to archive workflows.

Tasks:

- [x] Add collection profiles:
  - [x] Source URL.
  - [x] Destination.
  - [x] Filename template.
  - [x] Output preset.
  - [x] Caption/chapter preferences.
  - [x] Last checked item set.
- [x] Add "download new items" for playlists/channels.
- [x] Add Library export/import.
- [x] Add "rescan files" and "repair missing records".
- [x] Add "download missing from this collection" workflow.

Tests:

- [x] Large playlist continuation and rate limit handling.
- [x] Duplicate source/profile validation and existing Queue/Library duplicate detection across profile checks.
- [x] Filename collision handling with index/chapter tokens remains shared with manual collection preparation.
- [x] Import/export schema migration.

Exit gate:

- [x] User can maintain a channel/playlist archive without manually selecting every old item each time.

## Phase 7: UX Polish

Goal: make v2 feel like a finished app, not engineering demo.

Tasks:

- [x] Add simple/advanced mode.
- [x] Add preset-first download UI:
  - [x] Windows-compatible MP4.
  - [x] Best original.
  - [x] MP3 320.
  - [x] Small file.
  - [x] Custom.
- [x] Replace improvised nav glyphs with consistent vector icon resources.
- [x] Add clearer progress states: analyzing, queued, downloading video, downloading audio, muxing, transcoding, writing captions, complete.
- [x] Add recovery actions for common failures:
  - [x] Change destination.
  - [x] Free disk space/change destination and retry through Queue.
  - [x] Retry after rate-limit through existing failed/deferred Queue controls.
  - [x] Open diagnostics.
  - [x] Copy report.
- [x] Add first-run quick settings: default folder, preset, update checks.

Accessibility:

- [x] Keyboard focus and automation labels cover analyze, presets, queue controls, diagnostics, first-run, and recovery actions.
- [ ] Narrator smoke pass on packaged v2 build.
- [ ] Windows high-contrast check on packaged v2 build.
- [ ] DPI scaling check at 100, 125, 150, 200 percent on packaged v2 build.

Exit gate:

- Normal user can paste URL, choose preset, download, find file, retry failure, and update app without reading docs.

## Phase 8: Distribution And Trust

Goal: ship v2 as something normal Windows users can install and update.

Tasks:

- Finalize versioning to 2.0.0.
- Acquire/use Authenticode certificate or clearly document unsigned status.
- Publish installer and portable ZIPs.
- Publish checksums and release manifest.
- Publish GitHub release.
- Verify update from v1.2.5 to v2.0.0.
- Add winget manifest after release.
- Add Scoop manifest if desired.
- Prepare false-positive security response template.
- Update `README.md`, `docs/SUPPORT_POLICY.md`, `docs/RELEASE_NOTES.md`, `docs/INSTALLATION.md`, and `THIRD_PARTY_NOTICES.md`.

Exit gate:

- Fresh install, update install, uninstall, and portable launch are verified on clean Windows environment.

## Phase 9: v2 Release Validation Matrix

Use a user-selected secondary drive for heavy media when system-disk pressure is an issue.

Test media classes:

- Short public video under 2 minutes.
- Normal public video 5-20 minutes.
- Long public video over 1 hour.
- 4K video.
- 8K video if available and authorized.
- Shorts.
- Completed live replay.
- Public playlist over 100 items.
- Public channel.
- Manual captions.
- Auto captions.
- Chapters.
- HDR/high-FPS if available.
- Age/private/authenticated case according to final v2 policy.
- Rate-limited or retry scenario if reproducible safely.

Output matrix:

- MP4 stream-copy.
- WebM stream-copy.
- MKV stream-copy.
- MP3 128/192/256/320.
- M4A.
- OGG/Opus if implemented.
- WAV/FLAC if implemented.
- H.264/AAC MP4 transcode if implemented.
- Captions sidecar.
- Embedded subtitles if implemented.
- Chapter split if implemented.
- Trim if implemented.

System matrix:

- Fresh install.
- Update from v1.2.5.
- Portable ZIP.
- Uninstall keep data.
- Uninstall remove data.
- Queue recovery after app kill.
- Network interruption.
- Low disk.
- Destination missing/disconnected.
- Proxy if implemented.

Release evidence rules:

- Never commit media, signed URLs, cookies, credentials, private IDs, local private paths, or raw logs.
- Record only aggregate counts, typed failures, timings, output extensions, hashes of release artifacts, and sanitized screenshots if needed.

## Proposed v2.0 Scope Cut

Must ship in v2.0:

- Strong public YouTube flow.
- Installer and verified updater.
- MP3 plus expanded audio formats.
- Optional video transcode profiles.
- Preset-first UX.
- Captions embed/sidecar improvements.
- Chapter embed/split.
- Proxy UI.
- Live release canary proof.
- Fresh install/update/uninstall proof.

Should ship if time allows:

- SponsorBlock opt-in.
- Trim/cut.
- Playlist/channel monitoring.
- Library export/import.
- Winget/Scoop manifests.

Defer unless explicitly chosen:

- Full multi-site support.
- yt-dlp generic mode.
- OAuth browser login.
- DRM/encrypted stream support.
- Silent background updater.
- Cloud sync, telemetry, accounts, hosted conversion.

## Implementation Order

1. Baseline proof and v2 issue list.
2. Output profile model and audio/video transcode.
3. Preset-first UI.
4. Captions/chapters.
5. Proxy/network settings.
6. Optional SponsorBlock/trim.
7. Optional authenticated private user-owned content.
8. Optional HLS/live capture.
9. Playlist/channel archive profiles.
10. Distribution trust and release validation.

## Source Notes

Competitor feature references used for this plan:

- [yt-dlp GitHub](https://github.com/yt-dlp/yt-dlp)
- [4K Video Downloader Plus](https://www.4kdownload.com/products/videodownloader-42)
- [4K playlist/private playlist documentation](https://www.4kdownload.com/howto/howto-download-private-youtube-playlists/3)
- [Stacher](https://stacher.io/)
- [YTSage GitHub](https://github.com/oop7/YTSage)
- [VideoProc](https://www.videoproc.com/)
- [SnapDownloader](https://snapdownloader.com/)
- [MediaHuman YouTube Downloader](https://www.mediahuman.com/youtube-video-downloader/32/)
- [Arroxy GitHub](https://github.com/antonio-orionus/Arroxy/blob/main/README.md)

# TubeForge Development Plan

## 1. Product vision

TubeForge is a free, ad-free, local-first Windows desktop application for downloading YouTube media that the user has the right to save. It must provide a polished interface, resilient downloads, clear format choices, and useful diagnostics without invoking `yt-dlp`, browser extensions, hosted conversion services, or third-party managed application libraries. A pinned x64 LGPL FFmpeg executable is bundled only for reliable MP4, WebM, and MKV stream-copy muxing and normalization.

The application is built from source in this repository. Network access is limited to fetching YouTube pages, API responses, player scripts, thumbnails, captions, and media selected by the user. No analytics, ads, account system, telemetry, or remote control plane.

## 2. Scope and hard constraints

### Supported baseline

- Windows 10/11 x64 desktop.
- C# and .NET 10.
- WPF UI using the Windows Desktop runtime shipped with .NET.
- Standard-library and Windows APIs plus one pinned FFmpeg command-line executable at runtime.
- Public YouTube videos first.
- Audio + video defaults to the highest compatible adaptive video and audio tracks; progressive media is fallback only.
- Audio-only downloads in the native container (`m4a`/MP4 or WebM), with optional Windows Media Foundation MP3 transcoding.
- FFmpeg produces conventional indexed MP4 and validated WebM/MKV outputs by stream copy; in-house MP4/WebM muxers remain fallback and test surfaces.
- Resumable, bounded-concurrency downloads with atomic finalization.

### Forbidden dependencies

- No `yt-dlp`, `youtube-dl`, aria2, VLC, browser automation, or hosted downloader API.
- No third-party NuGet or npm packages.
- No copied extractor, decipher, muxer, codec, or downloader implementation.
- No unpinned or unverified external executable. Bundled FFmpeg provenance, hash, license, and exact source revision must ship with every x64 release.

### Explicit non-goals for early releases

- DRM-protected, paid, members-only, or rental content.
- Circumventing access controls.
- General-purpose codec packs or silent system-wide dependency installation.
- Mobile, macOS, or Linux UI before the Windows release is stable.
- Guaranteed support for every YouTube experiment at all times.

### Responsible-use boundary

TubeForge must display a first-run notice: users are responsible for YouTube's terms, copyright, privacy, and local law. The product should download only user-owned, public-domain, permissively licensed, or otherwise authorized content. It must not market itself as an access-control bypass.

## 3. Definition of success

### User experience

- Paste a supported URL, inspect title/thumbnail/duration, select a format, choose a destination, and start in under 15 seconds.
- Useful labels such as `1080p · MP4 · video only · 124 MB`, not raw format IDs.
- Queue remains responsive during parsing, downloading, pausing, canceling, and retrying.
- Interrupted downloads resume when the remote stream permits byte ranges.
- Completed files never appear with partial/corrupt contents.
- Errors explain next action and expose redacted diagnostic details.

### Reliability targets

- At least 95% successful metadata resolution across a maintained public-video fixture set.
- At least 98% byte-correct completion for resolved direct streams under normal network conditions.
- No UI-thread network or disk I/O.
- Bounded memory use independent of media size.
- Zero unhandled exceptions in a 24-hour queue soak test.

### Privacy and security

- No telemetry or background network requests unrelated to explicit user actions.
- URLs and titles stay local unless required in requests to YouTube.
- Logs redact query-string tokens, cookies, signatures, and visitor data.
- Filenames are sanitized; output paths cannot escape the chosen directory.
- Media is written to a sibling temporary file and atomically renamed only after validation.

## 4. Architecture

```text
TubeForge.App (WPF, composition root)
    |
    +-- TubeForge.Core
    |     URLs, result types, models, policies, diagnostics contracts
    |
    +-- TubeForge.YouTube
    |     HTTP session, page/API clients, response parsing, player decipher
    |
    +-- TubeForge.Downloads
    |     queue, range transfer, resume state, retries, integrity, filenames
    |
    +-- TubeForge.Media
          stream probing, container validation, safe FFmpeg process boundary,
          internal MP4/WebM fallback muxing

TubeForge.Tests (dependency-free console test runner)
    references all production projects except UI where avoidable
```

### Project boundaries

- `TubeForge.Core`: no WPF and no YouTube-specific HTTP behavior.
- `TubeForge.YouTube`: converts YouTube inputs into stable domain models. Player changes stay here.
- `TubeForge.Downloads`: accepts resolved stream URLs; does not know how extraction works.
- `TubeForge.Media`: bounded binary/container work plus safe argument-list-only FFmpeg invocation for MP4/WebM/MKV finalization.
- `TubeForge.App`: view models, UI state, settings, composition. Minimal code-behind.
- `TubeForge.Tests`: custom attributes/assertions/runner; no external test framework.

### Core patterns

- `CancellationToken` on every network, disk, parsing, and queue operation.
- Immutable records at boundaries.
- Typed failure codes plus human-readable messages.
- Dependency injection through explicit constructors, without a DI container.
- `HttpClient` instances owned by long-lived services.
- Stream data to disk; never buffer complete media.
- Defensive JSON parsing with `System.Text.Json`.
- Small interfaces only at volatile or I/O boundaries.

## 5. Extraction strategy

YouTube delivery changes frequently. Extraction must be a replaceable subsystem with captured, sanitized fixtures and deterministic tests.

### Resolution pipeline

1. Normalize and validate supported URL forms.
2. Extract the 11-character video ID.
3. Fetch the watch page using a coherent browser-like HTTP session.
4. Parse embedded player response and player JavaScript URL.
5. If needed, call a documented internal client profile through YouTube's own player endpoint using keys/config found in the fetched page, not hard-coded secrets.
6. Convert streaming formats into domain models.
7. Resolve direct URLs:
   - use an existing URL when already signed;
   - parse `signatureCipher`/`cipher` query data;
   - locate signature transform operations in the current player script;
   - apply the operations in a constrained interpreter;
   - transform throttling parameter `n` when required.
8. Validate candidates with a range probe before presenting them as downloadable.

### Decipher design

- Do not execute arbitrary JavaScript.
- Tokenize only the required JavaScript subset.
- Build a small syntax tree for function declarations, calls, arrays, property access, arithmetic, assignment, and control flow actually observed in player scripts.
- Extract the called transform function and its helper object.
- Evaluate with instruction, recursion, allocation, and wall-clock limits.
- Cache compiled transform plans by player-script hash.
- Retain only hashes and sanitized failure structure in normal logs.

### Fallback profiles

- Start with WEB and ANDROID-style public client profiles derived from current page configuration.
- Treat client profiles as versioned data with health metrics, not scattered constants.
- Try fallbacks only for classified failures.
- Avoid retry storms by capping attempts and honoring server responses.

## 6. Download and media strategy

### Transfer engine

- Probe content length, content type, ETag, Last-Modified, and range support.
- Store resumable state beside a `.part` file using a versioned JSON schema.
- Resume only when URL identity and remote validators are compatible.
- Use sequential transfer first; add segmented ranges only after correctness benchmarks.
- Exponential backoff with jitter for transient failures; no retry for clear permanent failures.
- Periodic progress using monotonic byte counts and smoothed throughput.
- Flush state at bounded intervals and on pause/shutdown.
- Validate final byte length and optional container structure before atomic rename.

### Container support

- Phase 1: preserve progressive MP4 exactly as served.
- Phase 2: preserve standalone audio in M4A/MP4 or WebM.
- Phase 3: parse ISO BMFF boxes with size/overflow/depth limits.
- Phase 4: mux compatible fragmented MP4 audio/video without re-encoding.
- Phase 5: parse and mux compatible WebM/Matroska tracks.
- Phase 6: metadata tagging, chapters, thumbnails, and captions where the container permits.
- Phase 7: decode and re-encode audio through Windows Media Foundation platform codecs, with explicit format/bitrate choices and typed unsupported-codec failures.

Muxing combines existing encoded tracks without quality loss. Transcoding decodes and re-encodes through an explicitly selected Windows platform codec. UI must distinguish native output from lossy conversion and show selected bitrate.

## 7. Persistence and settings

- `%LocalAppData%/TubeForge/settings.json`: user preferences.
- `%LocalAppData%/TubeForge/queue.json`: non-sensitive queue state.
- `%LocalAppData%/TubeForge/logs/`: planned bounded rolling diagnostic logs; not created yet.
- Downloads remain in user-selected folders.
- Writes use temporary files plus replace/rename.
- Settings and queue schemas are versioned, migrate published v1 state forward, and fail closed on unsupported future versions.
- No authentication cookies in the first public release.
- If cookie support is later approved, use Windows DPAPI and explicit import/clear controls; never log cookie contents.

## 8. UI plan

### Main shell

- Left navigation: Download, Queue, Library, Settings, Diagnostics.
- Top paste field with clipboard detection and Analyze action.
- Video card with thumbnail, title, channel, duration, availability, and warning state.
- Format filters: Recommended, Video, Audio, Resolution, Container, codec, FPS, HDR.
- Queue cards with progress, speed, ETA, pause/resume/cancel/retry/reveal actions.
- Bottom status region for network/extractor health.

### Accessibility and polish

- Keyboard-first navigation and visible focus.
- UI Automation names for actionable controls.
- Minimum 4.5:1 text contrast.
- 100–200% scaling support.
- Light, dark, and system themes.
- Reduced-motion option.
- Never encode state with color alone.
- Localizable strings from the first UI milestone.

## 9. Observability and failure taxonomy

Failure codes:

- `Input.InvalidUrl`
- `Input.UnsupportedUrl`
- `Video.Unavailable`
- `Video.LoginRequired`
- `Video.AgeRestricted`
- `Extractor.PageChanged`
- `Extractor.PlayerChanged`
- `Extractor.NoStreams`
- `Network.Timeout`
- `Network.RateLimited`
- `Network.Forbidden`
- `Download.RemoteChanged`
- `Download.DiskFull`
- `Download.WriteFailed`
- `Media.UnsupportedContainer`
- `Media.InvalidStructure`
- `Operation.Cancelled`

Diagnostic bundles must include app/build version, OS/runtime version, failure codes, sanitized event trail, extractor strategy IDs, and player-script hash. Exclude media URLs, query tokens, cookies, local usernames, and full output paths by default.

## 10. Verification strategy

### Test layers

- Unit: URL parsing, filename safety, format ranking, retry policy, progress math, JSON mapping, binary primitives.
- Fixture: sanitized watch pages, player responses, player scripts, MP4/WebM headers.
- Contract: local HTTP server for redirects, ranges, disconnects, malformed lengths, retry codes, and resume validators.
- Integration: opt-in live public videos with stable, permissive fixtures; never run on every commit.
- UI: view-model state tests first; manual accessibility and scaling checklist until a dependency-free automation harness exists.
- Soak/fault: large sparse files, low disk, dropped connections, process restart, cancellation races, 24-hour queue.

### Required gates

- `dotnet build TubeForge.slnx -warnaserror`
- `dotnet run --project tests/TubeForge.Tests -- --all`
- `dotnet format --verify-no-changes` when available without adding packages.
- Release build and self-contained publish smoke test.
- Zero known critical/high security findings from manual review and platform analyzers.

### Fixture policy

- Store minimal, sanitized excerpts needed by each test.
- Remove titles, channel names, visitor data, signatures, cookies, tracking values, and full media URLs.
- Record source date, expected parser behavior, and reason fixture is legally safe to retain.
- No downloaded copyrighted media in Git.

## 11. Delivery roadmap

Checkboxes track repository state. Each milestone ends with passing gates, updated docs, and a runnable artifact.

### M0 — Foundation and public project

- [x] Define product boundaries, architecture, risks, and milestones.
- [x] Initialize Git repository with `main` branch.
- [x] Create public GitHub repository and push initial commit.
- [x] Initialize local RECALL project memory; keep `.recall/` untracked.
- [x] Add README, responsible-use notice, contribution rules, and security policy.
- [x] Scaffold solution and dependency-free test runner.
- [x] Add deterministic build properties and warning policy.
- [x] Add GitHub Actions build/test workflow using only official actions.

Exit: fresh clone builds and tests on Windows with the documented .NET SDK.

### M1 — Input and domain foundation

- [x] Parse `youtube.com/watch`, `youtu.be`, `/shorts/`, `/live/`, and `/embed/` URLs.
- [x] Validate video IDs without accepting arbitrary hostnames.
- [x] Define video, format, codec, container, availability, and failure models.
- [x] Implement filename sanitization and collision policy.
- [x] Implement format classification, display labels, and deterministic ranking.
- [x] Cover edge cases and malicious inputs in unit tests.

Exit: URL-to-video-ID and domain decisions pass exhaustive local tests.

### M2 — Metadata resolver

- [x] Build coherent YouTube HTTP session and request headers.
- [x] Fetch watch page with timeouts, cancellation, compression, and bounded redirects.
- [x] Extract embedded player response and player-script URL using balanced structural scanning.
- [x] Map metadata, thumbnails, duration, playability, and streaming formats.
- [x] Map caption tracks and language metadata.
- [x] Implement internal Android player request using page-derived public configuration.
- [x] Add fallback strategy orchestration and typed failure classification.
- [x] Add sanitized fixture tests and opt-in live smoke command.

Exit: metadata and unsigned stream URLs resolve for maintained public fixtures and live smoke set.

### M3 — Direct-stream downloader MVP

- [x] Validate the response endpoint and create a safe collision-free target name.
- [x] Stream a direct URL to `.part` with cancellation and progress.
- [x] Validate remote/final length and atomically finalize.
- [x] Implement retry classification and bounded backoff.
- [x] Persist privacy-safe resume state with schema versioning.
- [x] Persist the multi-item queue with schema versioning.
- [x] Implement byte-range resume, cancel, and retry in the transfer engine.
- [x] Implement active-download pause/resume controls.
- [x] Implement application-shutdown queue recovery.
- [x] Add deterministic HTTP handler contract tests for ranges, validators, truncation, retries, and cancellation.
- [x] Add a loopback HTTP fault server for socket-level integration tests.

Exit: dependency-free CLI/harness downloads a progressive MP4 reliably and resumes after forced interruption.

### M4 — Modern desktop MVP

- [x] Create styled WPF shell, theme resources, typography, and icons drawn as vectors.
- [x] Implement paste/analyze workflow and metadata card.
- [x] Implement recommended and detailed format list.
- [x] Add advanced resolution/container/codec/FPS/HDR filters.
- [x] Implement destination picker and automatic collision-safe naming.
- [x] Implement queue cards and global concurrency control.
- [x] Add settings, first-run responsible-use notice, and diagnostics view.
- [x] Complete keyboard, scaling, screen-reader, dark-mode, and cancellation review.

Exit: normal user can analyze and download a progressive MP4 entirely through the GUI.

### M5 — Signature and throttling decipher

- [x] Capture synthetic player-script shapes and expected transform plans.
- [x] Build tokenizer for the required JavaScript subset.
- [x] Build constrained classic signature transform planner/evaluator with strict size and operation limits.
- [x] Locate signature and throttling functions structurally.
- [x] Cache transform plans by script hash.
- [x] Resolve supported classic `signatureCipher` transform shapes without executing JavaScript.
- [x] Resolve the current ES6 signature shape and `n` transformations.
- [x] Add mutation/fuzz tests for malformed scripts and unsupported syntax.
- [x] Add sanitized extraction-stage health reporting and Android client fallback.

Exit: signed public formats resolve without executing arbitrary JavaScript.

### M6 — Native audio downloads

- [x] Identify audio-only streams, codec, bitrate, sample rate, and container.
- [x] Download M4A/MP4 and WebM audio without re-encoding through the direct transfer engine.
- [x] Use the correct `.m4a` or `.webm` extension and map declared MIME/container metadata.
- [x] Validate the downloaded container structure before finalization.
- [x] Add metadata display and recommended-audio ranking.
- [x] Document why MP3 conversion is unavailable without transcoding.

Exit: user can save best/native audio stream with truthful format labeling.

### M7 — Internal MP4 muxer prototype (superseded for release output by M15)

- [x] Implement bounded ISO BMFF box reader/writer.
- [x] Parse init segments and fragmented media metadata.
- [x] Validate codec/container compatibility.
- [x] Build a seekable regular-MP4 output with merged tracks and rewritten 64-bit chunk offsets.
- [x] Interleave/copy samples using bounded buffers.
- [x] Preserve source sample tables, timestamps, sync samples, rotation, color, and audio parameters.
- [x] Validate output structure and playback against Windows media stack.
- [x] Fuzz box sizes, nesting, integer overflow, truncation, and hostile input.

Exit at completion: compatible adaptive MP4 tracks combined without re-encoding and passed structural/open checks. Later real-player stress tests exposed compatibility failures; M15 replaces this release path with FFmpeg stream-copy normalization.

### M8 — Advanced content features

- [x] Playlist/channel URL parsing and bounded paged enumeration.
- [x] Per-item selection, indexed naming, and in-queue duplicate suppression.
- [x] Custom naming templates, persistent archive/history, and full duplicate detection.
- [x] Captions: language selection, manual/automatic distinction, SRT/VTT conversion.
- [x] Thumbnails and optional metadata sidecars.
- [x] Chapters: bounded watch-page extraction and metadata sidecar export.
- [x] Playlist indexing and ordering metadata.
- [x] Shorts/live metadata; completed live streams before active-live capture.
- [x] Rate-limit-aware bulk scheduling and per-host concurrency.

Exit: robust batch workflow with user-controlled content selection and bounded load.

### M9 — WebM muxing and extended media

- [x] Implement bounded EBML reader/writer.
- [x] Parse WebM tracks, clusters, timecodes, SimpleBlocks, and BlockGroups while preserving laced payloads.
- [x] Mux compatible Opus/Vorbis audio and VP9/AV1 video without re-encoding.
- [x] Interleave clusters by timecode, remap audio track identity, and generate new seek cues.
- [x] Add hostile-container fixtures and fuzz coverage.

Exit: best compatible WebM adaptive formats combine into seekable files.

### M10 — Reliability hardening

- [x] Maintain extraction canary set and documented update playbook.
- [x] Run a bounded weekly GitHub Actions canary from an operator-owned secret URL list without logging media identifiers.
- [x] Add segmented transfer behind a feature flag and prove integrity/performance.
- [x] Add network-change, sleep/resume, proxy, IPv4/IPv6, and slow-disk tests.
- [x] Add disk-space forecasting and low-space recovery.
- [x] Add queue soak tests and crash-consistent persistence.
- [x] Add redacted diagnostic export and issue template.
- [x] Performance budget: startup, analysis latency, CPU, memory, UI frame time.

Exit: release-candidate reliability targets met on supported Windows versions.

### M11 — Packaging and v1.0

- [x] Choose project license before accepting outside contributions.
- [x] Produce framework-dependent and self-contained x64 builds.
- [x] Add reproducible release script, checksums, and signed artifacts when certificate exists.
- [x] Verify the final setup executable's embedded payload and optional Authenticode signature before publishing.
- [x] Add upgrade/uninstall behavior and data-retention documentation.
- [x] Add versioned extraction compatibility notes.
- [x] Complete privacy, security, responsible-use, accessibility, and threat-model reviews.
- [x] Publish v1.0 with limitations and support policy.

Exit: clean-machine installation, download smoke test, uninstall, checksum verification, and rollback pass.

### M12 — TubeForge product identity

- [x] Replace generic play-button branding with a scalable TubeForge forge/play mark.
- [x] Apply icon to window, executable, installer, release artifacts, and repository surfaces.
- [x] Audit product names, descriptions, package IDs, paths, and URLs for one TubeForge identity.
- [x] Rename the public GitHub repository to `TubeForge` after code and release references migrate.

Exit: app, artifacts, documentation, and public repository use one recognizable TubeForge identity.

### M13 — Native Windows audio transcoding

- [x] Add dependency-free Windows Media Foundation source-reader/sink-writer bridge.
- [x] Expose MP3 output at 128, 192, 256, and 320 kbps for audio-only downloads.
- [x] Preserve native M4A/AAC and WebM/Opus as lossless-copy choices.
- [x] Stage source audio, transcode to a sibling temporary file, validate output, then publish atomically.
- [x] Persist output profile across queue restart and include it in duplicate identity.
- [x] Add deterministic validation, unsupported-codec, cancellation, and cleanup tests.

Exit: supported Windows codecs convert selected public audio to playable MP3 without FFmpeg or bundled codec binaries.

### M14 — Installer, updater, and v1.1

- [x] Build a per-user, unelevated installer with Start Menu entry and Add/Remove Programs registration.
- [x] Preserve user data by default and offer explicit removal during uninstall.
- [x] Check GitHub stable releases on startup only when update checks are enabled.
- [x] Download the matching installer with strict repository, asset-name, size, SHA-256 digest, and version validation.
- [x] Require explicit user confirmation before applying an update; never elevate or silently execute remote content.
- [x] Add atomic staging, rollback, stale-file cleanup, and update/installer failure recovery.
- [x] Extend threat model, packaging tests, installation docs, release workflow, and support policy.
- [x] Migrate published settings and queue schema v1 state to schema v2 without storing expiring media URLs.
- [x] Add Library search, sort, and missing-file cleanup while preserving durable duplicate history.
- [x] Publish v1.1 with installer, updater, branding, and MP3 limitations documented.

Exit: clean-machine install, in-app update, rollback, uninstall, MP3 conversion, checksum, and provenance smoke tests pass.

### M15 — Reliable indexed MP4 finalization

- [x] Reproduce Windows playback failures on valid YouTube H.264/AAC fragmented MP4 outputs.
- [x] Prove source tracks decode without errors and isolate fragmented DASH layout as compatibility cause.
- [x] Replace release-path MP4 muxing with FFmpeg `-c copy` and `+faststart`; retain source quality.
- [x] Normalize direct progressive and video-only MP4 downloads, not only adaptive audio + video outputs.
- [x] Fail closed when FFmpeg is absent, exits non-zero, or leaves fragmented/non-indexed output.
- [x] Pin exact Windows x64 LGPL FFmpeg archive, SHA-256, licenses, source revision, and build scripts.
- [x] Add deterministic process-boundary tests plus live real-file packet/decode/Windows-media verification.

Exit: adaptive and video-only H.264 MP4 outputs become conventional indexed files, decode through start/middle/end, and open through Windows media stack.

## 12. Current maintenance state

- [x] Build v1.2.1 x64 archives and installer with pinned FFmpeg payload.
- [x] Run adaptive MP4, video-only MP4, audio-only, MP3, WebM, MKV, queue, Library, diagnostics, and updater smoke tests.
- [x] Confirm indexed MP4 samples in Windows Media Player and VLC.
- [x] Publish checksummed v1.2.1 artifacts after release gates pass.
- [x] Rank companion audio globally by bitrate and sample rate; prefer native MP4/WebM only when audio quality is equal.
- [x] Synchronize current docs and container-specific FFmpeg failure wording.
- [x] Prepare v1.2.2 x64 release source, tests, notes, installer copy, and update metadata.

No unfinished implementation item remains in this plan. New product work requires a new milestone with explicit exit criteria.

## 13. Risk register

| Risk | Impact | Mitigation | Trigger/action |
|---|---|---|---|
| YouTube page/player changes | Core resolution breaks | Isolate extractor, fixtures, structural parsing, canaries | Classified extractor errors spike; capture sanitized new shape and patch adapter |
| Platform codec availability differs | MP3 conversion may reject some source codecs | Use Windows Media Foundation capability negotiation and typed failures | Preserve native output path; never claim unsupported conversion |
| FFmpeg binary supply-chain or license drift | Release compromise or incomplete notices | Pin exact archive/hash/source/build revisions; ship notices/licenses; fail release verification on drift | Review and deliberately repin before any FFmpeg update |
| Updater supply-chain compromise | Remote code executes as user | Pin repository/asset policy; verify API digest, checksum, version, and bounded payload | Fail closed; keep manual download path; update threat model before release |
| Arbitrary player JavaScript | Security and reliability risk | Constrained subset interpreter with hard limits | Unsupported syntax fails closed and records sanitized structure |
| Signed URL expiry | Resume fails | Store source identity; re-resolve and compare format/validators | Refresh URL, then resume only if remote identity matches |
| Rate limiting | Temporary failures/bans | Bounded concurrency, jitter, Retry-After, no aggressive probing | Pause host queue and show retry time |
| Malformed remote binary data | Crash, memory blowup, file corruption | Checked arithmetic, size/depth limits, streaming parsers, fuzzing | Reject with `Media.InvalidStructure` |
| Public repository misuse expectations | Legal/reputation risk | Responsible-use language, no bypass claims/features | Reject access-control circumvention scope |
| Trademark/project naming | Distribution risk | Use TubeForge product name; describe YouTube compatibility factually | Review branding before v1.0 |
| One-maintainer extractor burden | Slow recovery | Small modules, update playbook, diagnostics, fixtures | Publish compatibility status and prioritize resolver fixes |

## 14. Decision log

- 2026-07-16: Greenfield repository targets Windows using .NET 10 and WPF because the active development environment contains the full Windows Desktop runtime and this avoids third-party UI dependencies.
- 2026-07-17: `TubeForge` is the product, namespace, artifact, installer, updater-policy, and public repository identity.
- 2026-07-17: Supersede zero-executable invariant after real 720p/1080p H.264 outputs decoded cleanly but failed or lagged in common players because TubeForge preserved YouTube's fragmented DASH layout. Bundle one pinned x64 LGPL FFmpeg executable and use stream-copy-only MP4 mux/remux with conventional indexed output; keep zero third-party managed packages and no `yt-dlp`.
- 2026-07-16: Progressive and native-container output precede muxing; transcoding is not conflated with downloading.
- 2026-07-16: RECALL state is local development context and must remain outside Git.
- 2026-07-16: Use a versioned Android player profile as the primary direct-format fallback when the watch page exposes only ciphered media. Current WEB/MWEB/TV profiles returned no usable streams during live verification; the tested Android profile returned progressive, audio-only, and video-only URLs.
- 2026-07-16: Keep the constrained classic signature planner as a guarded fallback. The current ES6 player no longer exposes the traditional split/reverse/splice shape, so current-player decipher and `n` transformation remain unfinished M5 work.
- 2026-07-16: Use type-first stream selection. Combined audio/video, native audio, and video-only modes expose only relevant filters and report per-mode limits. Audio + video presents muxable adaptive outputs as complete files, while Video only remains an explicit track-only choice.
- 2026-07-16: Highest-quality audio + video is an MVP release gate. Audio + video selects the highest compatible adaptive tracks, downloads both resumably, and muxes them internally; low-resolution progressive media is fallback only.
- 2026-07-16: Internal MP4 and WebM muxers established container parsing and hostile-input safety coverage. Internal MP4 output later proved insufficiently compatible for release playback; see 2026-07-17 FFmpeg decision. Internal WebM cluster interleaving/cue generation remains active.
- 2026-07-16: Prefer MP4 when video quality characteristics are equivalent, while keeping quality as the primary rank and retaining higher-quality WebM options.
- 2026-07-16: Queue downloads through a tested global 1–4 transfer dispatcher. Persist only validated video/format identities and local destinations; re-resolve fresh media URLs when resuming recovered work.
- 2026-07-16: Persist bounded local settings atomically. Gate first use on a locally stored responsible-use acknowledgement and keep diagnostics redacted to runtime, counts, stages, and local storage paths.
- 2026-07-16: Keep first-run acknowledgement keyboard-modal, expose explicit analysis/download cancellation, label live status/progress for assistive technology, use DPI layout rounding, and request a dark DWM title bar with the Windows 10 fallback attribute.
- 2026-07-16: Keep deterministic MP4/WebM mutation suites and hostile size/truncation/offset fixtures in normal CI. Readers must return typed failures; muxers must never publish output or leave `.muxing` files after rejected input.
- 2026-07-16: Forecast destination space before each queue run. Direct transfers reserve remaining source bytes plus headroom; adaptive muxes also reserve a full output estimate. Insufficient-space failures stay retryable after cleanup.
- 2026-07-16: Keep canary URLs in an operator-owned local file, never Git. Canary output is ordinal and aggregate-only; maintenance playbook forbids URLs, IDs, titles, channels, headers, scripts, and media in reports/fixtures.
- 2026-07-16: Diagnostic export uses a whitelist-only JSON schema and deliberately omits all content/source/local-path fields. GitHub extractor reports require this redacted report plus explicit safety acknowledgement.
- 2026-07-16: Queue commits flush a same-directory pending file before atomic replacement and retain the prior committed snapshot as recovery backup. Startup accepts only a fully validated primary, pending, or backup snapshot and pauses interrupted active items.
- 2026-07-16: Deterministic transfer reliability coverage uses real loopback sockets for connection loss, explicit proxies, and IPv4/IPv6, plus gated streams for sleep-like transport stalls and slow-destination backpressure without timing sleeps.
- 2026-07-16: Segmented transfer is an opt-in setting for large known-length streams. Up to four concurrent ranges must agree on bounds, total length, and validators; completed-range state is resumable and servers that ignore ranges fall back to the normal direct engine.
- 2026-07-16: Enforce deterministic fixture-analysis latency in CI and measure desktop startup, isolated idle CPU, working set, and WPF frame cadence with a local-only probe. Keep 2 s startup and 34 ms frames as targets; use 4 s and 50 ms as hard cold/30 Hz environment ceilings.
- 2026-07-16: Caption downloads are bounded to the trusted YouTube timed-text endpoint, requested as WebVTT, validated before publication, and atomically saved as normalized VTT or safely converted SRT. Manual and auto-generated tracks remain explicit in the UI and filename.
- 2026-07-16: Thumbnail sidecars accept only bounded JPEG, PNG, or WebP bytes from trusted YouTube image hosts and publish atomically. JSON sidecars contain stable metadata and format summaries while excluding ephemeral signed media and caption URLs.
- 2026-07-16: Chapter metadata comes only from bounded description-chapter marker structures, is sorted and deduplicated by start time, and appears in stable JSON sidecars without affecting download eligibility.
- 2026-07-16: Enumerate public playlists and channel video tabs from bounded first-party page data and first-party continuation requests without API keys. Accept legacy renderer and current lockup shapes, deduplicate by video ID, preserve playlist indexes, cap UI analysis at 1,000 items, and treat consent preference as a fixed non-authentication request header.
- 2026-07-16: Coordinate metadata and media requests by provider host-group with a two-request cap, bounded three-attempt rate-limit retries, shared backoff, and clamped `Retry-After` handling. Persistent bulk 429 responses defer untouched items instead of continuing request pressure; queued bulk downloads re-resolve expiring stream URLs at execution time.
- 2026-07-16: Render filenames from a bounded token template before Windows filename sanitization, retaining indexed default collection names. Persist completed-output history atomically with recovery candidates and no media URLs; exact source-output and destination matches across queue/history are explicit duplicates, while different output selections for the same video remain allowed.
- 2026-07-16: Classify Shorts and completed live replays in stable metadata and sidecars, preserving live start/end timestamps when present. Completed replays use ordinary adaptive selection and internal muxing; active, upcoming, and offline live capture fail with explicit typed errors until a dedicated segmented-live design exists. Always try the versioned direct-format client when a watch page has zero streams, even when it also has zero ciphers.
- 2026-07-17: TubeForge becomes the public repository and artifact identity. Use a vector-first forge/play mark and derive Windows icon sizes from the same geometry.
- 2026-07-17: MP3 conversion continues using Windows Media Foundation. FFmpeg handles MP4 stream-copy finalization only; native M4A/WebM remains available and no video/audio re-encoding occurs during MP4 mux/remux.
- 2026-07-17: Installer and updater remain per-user and unelevated. Update checks are configurable; applying an update requires explicit confirmation plus strict GitHub repository, version, asset, size, and SHA-256 validation.
- 2026-07-18: Route WebM adaptive muxing (VP9/AV1 + Opus/Vorbis) through the bundled FFmpeg `-c copy` path, not only MP4. The highest ladder qualities (1440p/4K/8K) are WebM-only and previously depended on the internal 717-line EBML muxer — the same class of hand-rolled muxer M15 replaced for MP4 after real-player failures. `FfmpegMediaProcessor` is now container-parameterized (MP4 `+faststart`/indexed validation, WebM EBML-structural validation); `AdaptiveDownloadEngine` uses FFmpeg for both containers when present and keeps the internal Mp4/WebM muxers as the no-FFmpeg fallback. Verified live: 480p VP9+Opus stream-copy produced structurally valid WebM at full throughput; 144p H264+AAC still yields an indexed MP4 that opens in the Windows media stack.
- 2026-07-18: Confirmed by live probe (Big Buck Bunny, ANDROID_VR client) that the direct-client path already returns the complete watch-page ladder (144p–2160p60 across H264/VP9/AV1, AAC+Opus) with `ciphered=0` and no throttling `n` parameter, downloading at full speed. Wiring the signature/`n` decipher engine into the direct-client path is therefore not required for coverage or download reliability; keep it a watch-page-fallback concern. Revisit only if canary/live evidence shows a client returning ciphered or throttled URLs.
- 2026-07-18: Add Matroska (`MediaContainer.Mkv`) as a lossless cross-container fallback so every watch-page quality is selectable even when a video codec has no same-container audio family (e.g. a WebM-only VP9/AV1 ladder paired with AAC-only audio). `AdaptiveFormatSelector.ResolveOutputContainer` returns the native shared container when the pair is natively muxable and Matroska otherwise; `SelectCompanionAudio` prefers a native companion and falls back to the best cross-codec companion. FFmpeg (`-f matroska -c copy`) performs the mux; MKV output fails closed when FFmpeg is absent (internal muxers cannot produce Matroska). Native MP4/WebM output is unchanged for the common case where both audio families exist. Verified live: 480p VP9 (WebM) + AAC (MP4) stream-copied into a structurally valid MKV.
- 2026-07-20: Supersede native-first companion-audio ranking. Rank all losslessly muxable audio by bitrate and sample rate, using native MP4/WebM compatibility only as an equal-quality tie-breaker. Higher-quality cross-container audio produces MKV so Audio + video does not sacrifice available audio quality.

## 15. Plan maintenance

- Update checkboxes only with code/docs evidence in the repository.
- Add material architecture choices to the decision log with dates.
- Split milestones when an exit criterion becomes too broad; do not weaken it silently.
- Move deferred work explicitly; do not let TODOs disappear during refactors.
- Every release notes known extractor limitations and last successful live-smoke date.

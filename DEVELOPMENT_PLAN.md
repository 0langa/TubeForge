# TubeForge Development Plan

## 1. Product vision

TubeForge is a free, ad-free, local-first Windows desktop application for downloading YouTube media that the user has the right to save. It must provide a polished interface, resilient downloads, clear format choices, and useful diagnostics without invoking `yt-dlp`, FFmpeg, browser extensions, hosted conversion services, or third-party application libraries.

The application is built from source in this repository. Network access is limited to fetching YouTube pages, API responses, player scripts, thumbnails, captions, and media selected by the user. No analytics, ads, account system, telemetry, or remote control plane.

## 2. Scope and hard constraints

### Supported baseline

- Windows 10/11 x64 desktop.
- C# and .NET 10.
- WPF UI using the Windows Desktop runtime shipped with .NET.
- Standard-library and Windows APIs only at runtime.
- Public YouTube videos first.
- Audio + video defaults to the highest compatible adaptive video and audio tracks; progressive media is fallback only.
- Audio-only downloads in the stream's native container (`m4a`/MP4 or WebM) before transcoding support.
- In-house ISO BMFF/MP4 and EBML/WebM muxing combines encoded tracks without re-encoding.
- Resumable, bounded-concurrency downloads with atomic finalization.

### Forbidden dependencies

- No `yt-dlp`, `youtube-dl`, FFmpeg, aria2, VLC, browser automation, or hosted downloader API.
- No third-party NuGet or npm packages.
- No copied extractor, decipher, muxer, codec, or downloader implementation.
- No silent installation or execution of external binaries.

### Explicit non-goals for early releases

- DRM-protected, paid, members-only, or rental content.
- Circumventing access controls.
- MP3/AAC/Opus transcoding before an independently implemented and legally reviewed codec path exists.
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
          stream probing, MP4/WebM metadata, muxing, tagging

TubeForge.Tests (dependency-free console test runner)
    references all production projects except UI where avoidable
```

### Project boundaries

- `TubeForge.Core`: no WPF and no YouTube-specific HTTP behavior.
- `TubeForge.YouTube`: converts YouTube inputs into stable domain models. Player changes stay here.
- `TubeForge.Downloads`: accepts resolved stream URLs; does not know how extraction works.
- `TubeForge.Media`: pure binary/container work with streaming I/O and strict bounds checks.
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

Muxing means combining existing encoded tracks. Transcoding means decoding and re-encoding, and is a separate, much larger codec project. UI must never label native audio extraction as MP3 conversion.

## 7. Persistence and settings

- `%LocalAppData%/TubeForge/settings.json`: user preferences.
- `%LocalAppData%/TubeForge/queue.json`: non-sensitive queue state.
- `%LocalAppData%/TubeForge/logs/`: planned bounded rolling diagnostic logs; not created yet.
- Downloads remain in user-selected folders.
- Writes use temporary files plus replace/rename.
- Settings and queue schemas are versioned and fail closed on unsupported versions; migrations remain future work.
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
- [ ] Build tokenizer for the required JavaScript subset.
- [x] Build constrained classic signature transform planner/evaluator with strict size and operation limits.
- [ ] Locate signature and throttling functions structurally.
- [ ] Cache transform plans by script hash.
- [x] Resolve supported classic `signatureCipher` transform shapes without executing JavaScript.
- [ ] Resolve the current ES6 signature shape and `n` transformations.
- [ ] Add mutation/fuzz tests for malformed scripts and unsupported syntax.
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

### M7 — In-house MP4 muxer

- [x] Implement bounded ISO BMFF box reader/writer.
- [x] Parse init segments and fragmented media metadata.
- [x] Validate codec/container compatibility.
- [x] Build a seekable regular-MP4 output with merged tracks and rewritten 64-bit chunk offsets.
- [x] Interleave/copy samples using bounded buffers.
- [x] Preserve source sample tables, timestamps, sync samples, rotation, color, and audio parameters.
- [x] Validate output structure and playback against Windows media stack.
- [ ] Fuzz box sizes, nesting, integer overflow, truncation, and hostile input.

Exit: compatible adaptive MP4 video and audio combine into a seekable file without re-encoding.

### M8 — Advanced content features

- [ ] Playlist/channel URL parsing and paged enumeration.
- [ ] Per-item selection, naming templates, archive/history, and duplicate detection.
- [ ] Captions: language selection, manual/automatic distinction, SRT/VTT conversion.
- [ ] Thumbnails and optional metadata sidecars.
- [ ] Chapters and playlist indexing.
- [ ] Shorts/live metadata; completed live streams before active-live capture.
- [ ] Rate-limit-aware bulk scheduling and per-host concurrency.

Exit: robust batch workflow with user-controlled content selection and bounded load.

### M9 — WebM muxing and extended media

- [x] Implement bounded EBML reader/writer.
- [x] Parse WebM tracks, clusters, timecodes, SimpleBlocks, and BlockGroups while preserving laced payloads.
- [x] Mux compatible Opus/Vorbis audio and VP9/AV1 video without re-encoding.
- [x] Interleave clusters by timecode, remap audio track identity, and generate new seek cues.
- [ ] Add hostile-container fixtures and fuzz coverage.

Exit: best compatible WebM adaptive formats combine into seekable files.

### M10 — Reliability hardening

- [ ] Maintain extraction canary set and documented update playbook.
- [ ] Add segmented transfer behind a feature flag and prove integrity/performance.
- [ ] Add network-change, sleep/resume, proxy, IPv4/IPv6, and slow-disk tests.
- [ ] Add disk-space forecasting and low-space recovery.
- [ ] Add queue soak tests and crash-consistent persistence.
- [ ] Add redacted diagnostic export and issue template.
- [ ] Performance budget: startup, analysis latency, CPU, memory, UI frame time.

Exit: release-candidate reliability targets met on supported Windows versions.

### M11 — Packaging and v1.0

- [ ] Choose project license before accepting outside contributions.
- [ ] Produce framework-dependent and self-contained x64 builds.
- [ ] Add reproducible release script, checksums, and signed artifacts when certificate exists.
- [ ] Add upgrade/uninstall behavior and data-retention documentation.
- [ ] Add versioned extraction compatibility notes.
- [ ] Complete privacy, security, responsible-use, accessibility, and threat-model reviews.
- [ ] Publish v1.0 with limitations and support policy.

Exit: clean-machine installation, download smoke test, uninstall, checksum verification, and rollback pass.

## 12. Immediate implementation backlog

Order for current work:

1. Repository, GitHub, RECALL, policies, solution scaffold, CI.
2. URL parser and tests.
3. Domain media models and format ranking.
4. Filename/path safety.
5. Direct-stream transfer engine plus local fault server.
6. Watch-page HTTP client and embedded JSON extraction.
7. Metadata/format mapping fixtures.
8. Minimal WPF analyze/download vertical slice.
9. Player decipher research and constrained interpreter.
10. Native audio and MP4 muxing.

## 13. Risk register

| Risk | Impact | Mitigation | Trigger/action |
|---|---|---|---|
| YouTube page/player changes | Core resolution breaks | Isolate extractor, fixtures, structural parsing, canaries | Classified extractor errors spike; capture sanitized new shape and patch adapter |
| No external muxer/codecs | Advanced formats arrive later | Progressive/native formats first; implement containers separately | Never mislabel muxing/transcoding; keep UI capability-driven |
| Arbitrary player JavaScript | Security and reliability risk | Constrained subset interpreter with hard limits | Unsupported syntax fails closed and records sanitized structure |
| Signed URL expiry | Resume fails | Store source identity; re-resolve and compare format/validators | Refresh URL, then resume only if remote identity matches |
| Rate limiting | Temporary failures/bans | Bounded concurrency, jitter, Retry-After, no aggressive probing | Pause host queue and show retry time |
| Malformed remote binary data | Crash, memory blowup, file corruption | Checked arithmetic, size/depth limits, streaming parsers, fuzzing | Reject with `Media.InvalidStructure` |
| Public repository misuse expectations | Legal/reputation risk | Responsible-use language, no bypass claims/features | Reject access-control circumvention scope |
| Trademark/project naming | Distribution risk | Use TubeForge product name; describe YouTube compatibility factually | Review branding before v1.0 |
| One-maintainer extractor burden | Slow recovery | Small modules, update playbook, diagnostics, fixtures | Publish compatibility status and prioritize resolver fixes |

## 14. Decision log

- 2026-07-16: Greenfield repository targets Windows using .NET 10 and WPF because the active development environment contains the full Windows Desktop runtime and this avoids third-party UI dependencies.
- 2026-07-16: `TubeForge` is the working product/namespace name; repository may retain the descriptive `youtube-downloader` name.
- 2026-07-16: Zero third-party application dependencies is an architectural invariant, not merely a packaging goal.
- 2026-07-16: Progressive and native-container output precede muxing; transcoding is not conflated with downloading.
- 2026-07-16: RECALL state is local development context and must remain outside Git.
- 2026-07-16: Use a versioned Android player profile as the primary direct-format fallback when the watch page exposes only ciphered media. Current WEB/MWEB/TV profiles returned no usable streams during live verification; the tested Android profile returned progressive, audio-only, and video-only URLs.
- 2026-07-16: Keep the constrained classic signature planner as a guarded fallback. The current ES6 player no longer exposes the traditional split/reverse/splice shape, so current-player decipher and `n` transformation remain unfinished M5 work.
- 2026-07-16: Use type-first stream selection. Combined audio/video, native audio, and video-only modes expose only relevant filters and report per-mode limits. Audio + video presents muxable adaptive outputs as complete files, while Video only remains an explicit track-only choice.
- 2026-07-16: Highest-quality audio + video is an MVP release gate. Audio + video selects the highest compatible adaptive tracks, downloads both resumably, and muxes them internally; low-resolution progressive media is fallback only.
- 2026-07-16: Support regular MP4 chunk-offset rewriting, fragmented MP4 track/fragment remapping and interleaving, and WebM cluster interleaving/cue generation. A live H.264/AAC fragmented MP4 mux passed structural validation and opened with both tracks in the Windows media stack; AV1 playback still depends on the system codec installation.
- 2026-07-16: Prefer MP4 when video quality characteristics are equivalent, while keeping quality as the primary rank and retaining higher-quality WebM options.
- 2026-07-16: Queue downloads through a tested global 1–4 transfer dispatcher. Persist only validated video/format identities and local destinations; re-resolve fresh media URLs when resuming recovered work.
- 2026-07-16: Persist bounded local settings atomically. Gate first use on a locally stored responsible-use acknowledgement and keep diagnostics redacted to runtime, counts, stages, and local storage paths.
- 2026-07-16: Keep first-run acknowledgement keyboard-modal, expose explicit analysis/download cancellation, label live status/progress for assistive technology, use DPI layout rounding, and request a dark DWM title bar with the Windows 10 fallback attribute.

## 15. Plan maintenance

- Update checkboxes only with code/docs evidence in the repository.
- Add material architecture choices to the decision log with dates.
- Split milestones when an exit criterion becomes too broad; do not weaken it silently.
- Move deferred work explicitly; do not let TODOs disappear during refactors.
- Every release notes known extractor limitations and last successful live-smoke date.

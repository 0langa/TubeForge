# TubeForge Audit

Audit date: 2026-07-22.

Scope: v1.2.5 `main` baseline plus the current v2 implementation branch, deterministic local build/test/performance gates, release docs, WPF UI surface, downloader/extractor/media/update/installer code shape, and current public feature surface of popular YouTube downloaders.

## Current State

TubeForge is no longer a thin prototype. Current `main` builds cleanly and already includes many items that were earlier v2 targets:

- WPF desktop app with Download, Queue, Library, Settings, Diagnostics, responsible-use acknowledgement, branding, installer, and update UI.
- Strict YouTube URL and collection parsing for public videos, playlists, channels, Shorts, and completed live replays.
- Public YouTube metadata extraction with direct client fallback, constrained signature and `n` throttling transforms, and bounded parser/resource limits.
- Progressive, native audio-only, video-only, and adaptive video+audio download paths.
- MP4, WebM, MKV finalization through pinned bundled FFmpeg stream copy.
- MP3, AAC/M4A, Opus/OGG, WAV, and FLAC audio conversion through bundled FFmpeg, with fail-closed output validation and queue recovery.
- Optional resolution-aware H.264/AAC MP4, H.265/AAC MP4, and VP9/Opus WebM conversion presets, with native stream copy remaining default.
- Preset-first setup for Best original, Windows MP4, Small file, MP3 320, and fully custom selection.
- Caption SRT/WebVTT sidecars plus opt-in embedding of up to eight ordered MP4/MKV/WebM soft-subtitle tracks for single videos and collection/archive queue items, chapter embed/split, bounded trim, disabled-by-default SponsorBlock chapter/removal modes, thumbnails, JSON sidecars, filename templates, duplicate detection, resumable `.part` files, segmented transfer, disk forecasting, queue recovery, unified system/manual/off proxy controls, installer, release scripts, optional Authenticode signing, GitHub release update checks, and redacted diagnostics.

Verified during this audit:

```powershell
dotnet build TubeForge.slnx --configuration Release
```

Result: passed, 0 warnings, 0 errors.

```powershell
dotnet run --project tests\TubeForge.Tests --configuration Release --no-build -- --all
```

Result after multi-language caption selection: 237/237 passed.

```powershell
dotnet run --project tools\TubeForge.Performance --configuration Release --no-build -- --core-only
```

Latest isolated release-candidate rerun: passed. Core parser p95 0.2214 ms against the 25 ms budget; startup 1,743.7 ms, idle CPU 0%, working set 146.76 MiB, and UI frame p95 39.927 ms also passed. One earlier combined sample exceeded the startup budget at 4,491 ms, so cold-start variance remains a release-monitoring risk.

Locally verified after the initial audit: version 2.0.0 portable and installer candidates, checksums, release manifest, dependency layout, pinned FFmpeg, embedded installer payload, and portable launch probe. The candidate is explicitly unsigned.

Not verified in this audit: current live YouTube canary downloads, installer execution, clean update flow from v1.2.5, code signing, VirusTotal/SmartScreen reputation, store/winget distribution, long-run queue soak, or accessibility with Narrator/NVDA.

## Market Baseline

Popular tools define user expectations beyond "download a public YouTube video":

| Product | Current public positioning | Relevance for TubeForge |
|---|---|---|
| yt-dlp | Open-source CLI, thousands of supported sites, subtitles, playlists, format selection, metadata, post-processing, SponsorBlock/options, cookies, proxies, plugins. Source: [yt-dlp GitHub](https://github.com/yt-dlp/yt-dlp). | Functionality ceiling. TubeForge is safer and simpler for Windows users, but far narrower. |
| 4K Video Downloader Plus | Cross-platform app for YouTube and other sites; claims playlists, channels, Shorts, subtitles, private accessible playlists, up to 8K, MP4/MKV/M4A/MP3/OGG, Smart Mode, update-like playlist behavior. Sources: [product page](https://www.4kdownload.com/products/videodownloader-42), [playlist page](https://playlist.4kdownload.com/), [private playlist guide](https://www.4kdownload.com/howto/howto-download-private-youtube-playlists/3). | Closest mainstream GUI competitor. TubeForge matches privacy/ad-free posture better, but lacks account/private workflows, saved Smart Mode defaults, cross-platform packaging, and mature support docs. |
| Stacher | GUI for yt-dlp. Source: [Stacher](https://stacher.io/). | Competes by wrapping yt-dlp breadth. TubeForge must either stay YouTube-specialist or introduce a provider/plugin strategy. |
| YTSage | Open-source GUI; one-click video/audio/subtitles, playlist selection, SponsorBlock, subtitle merging, generic yt-dlp mode, cross-platform. Source: [YTSage GitHub](https://github.com/oop7/YTSage). | Modern open-source GUI baseline. SponsorBlock, subtitle merging, and generic mode are key gaps. |
| VideoProc Converter AI | Paid media suite; downloader covers many sites, videos, audio, playlists, channels, M3U8/live streams, conversion, compression, recording, proxy, device formats. Source: [VideoProc](https://www.videoproc.com/). | TubeForge should not chase full media-suite scope for v2, but proxy, live/M3U8 policy, and presets affect parity perception. |
| SnapDownloader | Windows/macOS app; 900+ sites, up to 8K, playlists/channels, VR/360, audio extraction, proxy, trimming, private-download guidance. Sources: [SnapDownloader](https://snapdownloader.com/), [YouTube downloader offer](https://snapdownloader.com/offers/youtube-downloader?ref=guru99). | Highlights missing proxy, VR/360 treatment, trimming, and multi-site support. |
| MediaHuman YouTube Downloader | Windows/macOS/Ubuntu; playlists/channels, 4K/8K with audio, simultaneous downloads, MP3 extraction, iTunes/Music export, channel/playlist monitoring. Source: [MediaHuman](https://www.mediahuman.com/youtube-video-downloader/32/). | Channel monitoring and music-library export are strong v2 differentiators for normal users. |
| Arroxy | Free/open-source GUI using yt-dlp; YouTube plus 2000+ sites, playlists, subtitles, no ads/limits, cross-platform packaging through winget/scoop/homebrew/flatpak/appimage. Source: [Arroxy GitHub](https://github.com/antonio-orionus/Arroxy/blob/main/README.md). | Distribution and broad extractor coverage set expectations for open-source GUI projects. |

## Feature Parity Matrix

| Capability | TubeForge v1.2.5 | Market expectation | Gap |
|---|---|---|---|
| Public YouTube video downloads | Strong | Required | Low |
| Highest quality adaptive video+audio | Strong for supported public YouTube streams | Required | Low |
| 4K/8K support | Supported when public streams resolve and local codecs handle playback | Expected claim in most competitors | Needs live proof per release |
| MP4/WebM/MKV | Present | Expected | Low |
| MP3 | Present | Expected | Low |
| M4A/WebM audio | Present | Expected | Low |
| OGG/Opus output | Present through bundled FFmpeg | Common in 4K/yt-dlp workflows | Low, needs release live smoke |
| AAC/WAV/FLAC device/audio conversion | Present through bundled FFmpeg | Common in paid tools | Low, needs release live smoke |
| Video re-encoding/transcoding | H.264/AAC MP4, H.265/AAC MP4, VP9/Opus WebM presets present in the current v2 branch | Common in paid tools and yt-dlp+FFmpeg | Medium, needs installed-app live proof |
| Presets/device profiles | Best original, Windows MP4, Small file, MP3 320, Custom, plus exact processing choices present in the current v2 branch | Common in mainstream apps | Medium, saved defaults and broader device presets remain |
| Playlists/channels | Present with bounded selection | Required | Medium, needs monitoring/sync |
| Auto-download new playlist/channel items | Not present | Present in 4K/MediaHuman/Tartube-like workflows | High |
| Private/account videos | Explicitly unsupported by recorded v2.0 public-only policy, with a stable typed failure | Many mainstream tools support authenticated accessible content | Deliberate strategic gap |
| Cookies/browser login import | Intentionally absent from v2.0; no credential material is collected or stored | Common for yt-dlp/Stacher/YTSage power users | Deferred due privacy/security tradeoff |
| Active live capture | Public record-now plus bounded upcoming wait mode on current v2 branch | Present in VideoProc/yt-dlp-style workflows | Medium, authorized live canary and soak remain |
| M3U8/HLS | Trusted YouTube/googlevideo, unencrypted HLS with recoverable journal and MKV finalization on current v2 branch | Common in broader downloaders | Medium, generic/encrypted streams intentionally unsupported |
| Multi-site support | YouTube-only | Major competitors support hundreds/thousands of sites | Strategic gap |
| SponsorBlock | Disabled-by-default category selection with chapter markers or transcode removal on current v2 branch | Common in yt-dlp GUIs | Medium, needs installed-app/live third-party proof |
| Trim/cut/split | Bounded start/end trim plus chapter split on current v2 branch; copy cuts are keyframe-aligned and selected transcodes are precise | Common in SnapDownloader/VideoProc, chapter split in MediaHuman | Low, needs installed-app proof |
| Captions/subtitles | Sidecar SRT/WebVTT plus up to eight ordered soft-subtitle tracks in single-video and collection/archive MP4/MKV/WebM outputs on current v2 branch | Expected | Low, burn-in is deferred and installed-app proof remains |
| Chapter handling | Opt-in embed and lossless split on current v2 branch | Embed chapters or split by chapters common | Low, batch controls and installed-app proof remain |
| Thumbnails/metadata sidecars | Present | Expected | Low |
| Queue/resume | Strong | Required | Low |
| Speed acceleration | Present, bounded to 4 workers | Expected | Low, but needs live throughput benchmarks |
| Proxy | System/manual/off UI on current v2 branch; shared policy, safe bounds, redacted diagnostics, and metadata/media loopback proof | Expected in SnapDownloader/VideoProc/yt-dlp | Low, installed-app live proof remains |
| Rate-limit handling | Present bounded backoff | Required | Low |
| Installer | Present | Expected | Low, but production trust proof missing |
| Auto-updater | Present as opt-in verified GitHub release flow | Expected | Medium, needs live signed update proof |
| Distribution | GitHub release scripts only | Winget/Scoop/store/direct installer expected | Medium |
| Cross-platform | Windows x64 only | Many competitors are cross-platform | Medium, but could remain deliberate |
| CLI/automation | Smoke tools only, no user CLI | yt-dlp/Arroxy-style users expect automation | Medium |
| Diagnostics/privacy | Strong redaction posture | Better than many competitors | Low |
| Telemetry-free/ad-free | Strong | Desirable differentiator | Low |

## Findings

### P0: No Release Blockers Found In Deterministic Local Gates

Build, full deterministic tests, and core parser performance passed. Current source quality gate is healthy.

Residual risk: these gates do not prove live extraction against current YouTube, Windows playback of real outputs, installer runtime behavior, or update flow.

### P1: v2 Positioning Is Undefined: YouTube Specialist vs General Downloader

README says TubeForge is built for media users are authorized to save from YouTube and does not use `yt-dlp`. The user goal says "pretty much anything from youtube in many different audio and video formats". Market leaders compete on either massive extractor breadth through yt-dlp or broad media-suite conversion.

Wrong/optimizable:

- Product scope does not define whether v2 stays YouTube-only, adds selected provider modules, or adds optional yt-dlp bridge/generic mode.
- Current architecture is YouTube-specific (`TubeForge.YouTube`, strict parsers, YouTube host policy), which is good for safety but limits parity with Stacher/YTSage/Arroxy.
- Docs compare only to current v1 support policy, not external expectations.

Recommendation:

- Keep v2 core as "best Windows YouTube downloader" unless user explicitly chooses multi-site.
- Add provider abstraction only after v2 release if "pretty much anything" means sites beyond YouTube.
- For YouTube parity, prioritize authenticated-access policy, live/M3U8, SponsorBlock, chapter workflows, presets, and production distribution.

### P1: Authenticated/Private Content Strategy Missing

TubeForge explicitly rejects private, membership, paid, DRM, and access-control content. Competitors often support private media that the user can access after login or cookie import. The v2.0 decision is Option A: remain public-only and collect no authentication material.

Wrong/optimizable:

- Current product cannot download user's own private YouTube videos/playlists.
- No account login, browser-cookie import, OAuth, or credential store exists by design for v2.0.
- Login-required responses map to a stable `Video.AuthenticationUnsupported` failure without echoing provider details.
- The public-only policy is safer, but not parity with premium tools.

Security constraints:

- Do not add broad credential persistence casually.
- Do not support DRM/payment/membership bypass.
- Reconsider authenticated support only in a later design milestone with explicit approval, a credential-lifecycle threat model, local encrypted storage, diagnostics redaction, and one-click deletion.

### P1: Live, M3U8, And Upcoming Stream Capture Missing

TubeForge supports completed live replays and the current v2 branch adds public active/upcoming HLS capture. Active streams record from now; upcoming streams can wait locally for a bounded time. Capture stops at the configured duration or size, journals segments for restart, and finalizes to validated MKV stream copy.

Wrong/optimizable:

- Public-live behavior still needs an authorized upstream canary and long-run soak before release.
- Only trusted YouTube/googlevideo public HLS is supported; generic M3U8, authenticated streams, encryption, and DRM remain intentionally unsupported.
- HLS variants with separate alternate audio renditions need explicit canary coverage before broader compatibility is claimed.

Recommendation:

- Keep the implementation aligned with [RFC 8216](https://www.rfc-editor.org/info/rfc8216/) parsing bounds while rejecting every encryption method except `NONE`.
- Run an authorized public active and upcoming canary, interrupt/restart it, and verify MKV playback/decode before v2 release.

### P1: Video Re-Encoding Implemented; Release-Grade Live Proof Missing

The current v2 branch exposes original stream copy plus three bounded video conversion profiles. Market tools still expose broader device, compression, trimming, and custom controls.

Wrong/optimizable:

- H.264/AAC MP4, H.265/AAC MP4, and VP9/Opus WebM conversion now use allowlisted encoders from the pinned LGPL FFmpeg build.
- Target video bitrate follows a bounded resolution ladder and is persisted in queue identity; H.265 pads dimensions by at most seven pixels per axis when required by `libkvazaar`.
- MOV/AVI workflows, user-defined encoder controls, and named device profiles remain absent.

Recommendation:

- Keep stream-copy default and conversion explicitly opt-in.
- Run installed-app live source-to-output proof for every video profile before release.
- Add custom/device profiles only after measured quality, time, and file-size evidence.

### P1: Release-Grade Live Evidence Not Current

Repository contains strong release automation and docs. Local version 2.0.0 archive and installer packaging now pass their checksum, layout, dependency, payload, and portable launch probes, but this audit did not run live canaries or execute the installer.

Wrong/optimizable:

- `docs/EXTRACTION_COMPATIBILITY.md` last live validation is 2026-07-20/2026-07-21 era, while audit date is 2026-07-22.
- Live YouTube behavior changes frequently. Deterministic fixtures cannot prove current upstream compatibility.
- `docs/PERFORMANCE_BUDGET.md` requires ten desktop runs, canary set, active direct download, and adaptive mux run before release; those were not run in this audit.
- Fresh local proof from `Publish-Release.ps1`, `Test-Release.ps1`, `Publish-Installer.ps1`, and `Test-Installer.ps1` now passes; actual install/update/uninstall and current-upstream media proof remain open.

Recommendation:

- Treat deterministic gates as necessary but insufficient.
- Block v2 release until live canary, installer, update, playback, and long-run queue evidence are captured.

### P2: Proxy Capability Productized; Installed-App Proof Remains

The current v2 branch exposes system/manual/off proxy selection plus bounded metadata timeout, media retries, and per-host concurrency. One credential-free proxy policy is shared by metadata, collections, captions, thumbnails, media, and updates. Loopback tests prove both metadata and media paths.

Wrong/optimizable:

- Installed-app live proxy behavior still needs release evidence.
- Proxy authentication remains intentionally unsupported until a secure credential store exists.

Recommendation:

- Keep proxy authentication fail-closed unless secure storage is deliberately implemented.
- Capture sanitized installed-app evidence for metadata and media through an operator-controlled proxy before v2 release.
- Continue emitting only proxy mode in diagnostics; never endpoint or credential data.

### P2: SponsorBlock And Trim Implemented; Release Proof Remains

yt-dlp GUIs increasingly expose SponsorBlock removal/chapter marking, and premium apps expose trimming. The current v2 branch adds both while preserving fail-closed publication and explicit user choice.

Wrong/optimizable:

- SponsorBlock is a third-party network dependency and still needs sanitized live/installed-app proof before release.
- Original-output trim is keyframe-aligned stream copy; frame-accurate trim intentionally requires a selected transcode profile.
- SponsorBlock removal requires an explicit transcode profile and is mutually exclusive with embedded captions/chapters so timed metadata cannot silently drift.
- Chapter embed/split UI currently targets single-video outputs; batch controls remain open.

Recommendation:

- Keep SponsorBlock disabled by default with the current privacy notice and four-character hash-prefix request described by the [official API](https://wiki.sponsor.ajay.app/w/API_Docs); match returned candidates locally and never persist payloads.
- Preserve the current trim distinction in UI: keyframe-aligned copy for original outputs and precise cut during a selected re-encode.
- Capture sanitized installed-app evidence for trim, SponsorBlock chapter marking, and SponsorBlock removal before v2 release.

### P2: Multi-Language Caption Embed Implemented; Burn-In Deferred

TubeForge can save SRT/WebVTT sidecars and opt in up to eight ordered manual/auto tracks as soft subtitles in single-video and collection/archive MP4, MKV, or WebM outputs. Queue identity stores only bounded language/type selections, and FFmpeg validates every expected subtitle stream. Permanent burn-in remains outside v2.

Wrong/optimizable:

- Collection/archive preferences embed all eligible manual tracks or a deterministic bounded manual/auto set; single-video UI exposes individual track checkboxes.
- No burn-in option by design; soft subtitles stay reversible and do not alter video pixels.
- Bundled-FFmpeg synthetic smoke verifies MP4 `mov_text`, MKV `srt`, and WebM `webvtt`; installed-app live media proof remains open.

Recommendation:

- Keep the eight-track bound, canonical queue identity, per-track FFmpeg mapping, and recovery validation covered by deterministic tests.
- Reconsider burn-in only as a separate post-v2 feature because it requires video transcode and permanent image changes.

### P2: Queue UX Still Looks Workmanlike Compared With Premium Apps

Current WPF UI is functional and keyboard-aware, but uses text/symbol button content in nav buttons rather than a polished icon system. Premium apps emphasize simple paste/analyze/download flows, smart defaults, and minimal technical jargon.

Wrong/optimizable:

- Quick presets cover the primary download flows and a non-Custom default now persists across app restarts.
- Detailed format filters are hidden behind an explicit advanced disclosure by default; Custom reveals them automatically.
- Sidebar navigation uses consistent vector resources that inherit button foreground color.
- The main error surface now links directly to destination selection, Settings, Diagnostics, and a redacted report; Queue retains retry/resume controls for failed or rate-limited work.

Recommendation:

- Keep the implemented simple/advanced preference and default-preset schema migration covered by deterministic tests.
- Keep vector navigation resources and recovery actions in the static XAML/accessibility gate.
- Complete Narrator, Windows high-contrast, and 100/125/150/200-percent DPI release passes before v2.0 publication.

### P2: Library Needs Management Features

Library has search/sort and cleanup for moved/deleted files. Competitors with channel monitoring and media managers expose more archive controls.

Wrong/optimizable:

- Re-checking is explicit rather than a background subscription service, so users control every network request and queue mutation.
- Persistent channel/playlist archive profiles, bounded new-item checkpoints, and download-missing selection are implemented on the current v2 branch.
- Library export/import and bounded moved-file repair are implemented on the current v2 branch.

Recommendation:

- Keep archive checks user-initiated for v2.0; evaluate scheduled monitoring only with explicit background-work and notification design.
- Keep the implemented schema-versioned Library export/import and bounded rescan paths covered by migration and ambiguity tests.
- Keep archive profile folder/template/output/caption/chapter defaults covered by persistence, duplicate, and rate-limit tests.

### P2: Distribution Trust Still Needs Production Hardening

Release workflow supports signing and attestations, but user trust for a Windows downloader depends on smooth install and reputation.

Wrong/optimizable:

- No winget/Scoop manifests.
- Current release-candidate evidence documents that no signing certificate was supplied and the candidate is unsigned; a production signing decision remains open.
- A false-positive response template now covers checksum, provenance, signature-state, and vendor-submission handling; SmartScreen reputation remains unproven.
- No rollback/update integration test against published release assets in this audit.

Recommendation:

- Add v2 release checklist for signing, GitHub release, install/uninstall, update from v1.2.5 to v2.0.0, winget/Scoop submission, checksum verification, and AV false-positive handling.

### P2: CI Good, But Release Gates Need Broader Automated Coverage

CI builds/tests/perf-gates on Windows and validates PowerShell syntax. Release workflow also publishes archives/installer and verifies installer.

Wrong/optimizable:

- No scheduled upstream canary in CI is visible beyond `.github/workflows/canary.yml`; release plan must define result handling.
- No UI screenshot/visual regression gate.
- No installer E2E in CI that installs/uninstalls into temp profile, only payload verification from script.
- No fuzz/property test framework beyond deterministic hostile tests.

Recommendation:

- Add nightly sanitized canary with private canary IDs in secrets or local-only manual path.
- Add Playwright/WinAppDriver/FlaUI-style smoke if feasible, or a small WPF automation probe.
- Add full installer install/uninstall test in isolated temp dirs where safe.

### P3: Documentation Needs v2 Product Promise Cleanup

Docs are clear for v1, but v2 plan should avoid contradictions.

Wrong/optimizable:

- `README.md` says v1.2.5 current public stable; v2 work needs a separate target doc.
- `docs/SUPPORT_POLICY.md` is v1-specific and excludes capabilities that v2 may add.
- Marketing phrase "many different audio and video formats" is not yet accurate for video transcode formats.

Recommendation:

- Add v2 support policy before release.
- Keep legality/responsible-use language.
- Split "supported", "unsupported", and "deliberately not supported" cleanly.

## Architecture Risk Areas

High-value files/modules for future review:

- `src/TubeForge.YouTube/YouTubeMetadataResolver.cs`: upstream coupling, client profiles, player transforms, bounded network reads.
- `src/TubeForge.YouTube/Extraction/YouTubeWatchPageParser.cs`: fragile YouTube JSON shape parsing.
- `src/TubeForge.YouTube/Collections/*`: playlist/channel continuation parsing and rate-limit behavior.
- `src/TubeForge.Downloads/DirectDownloadEngine.cs`: range logic, resume correctness, proxy path, validation, host allowlists.
- `src/TubeForge.Downloads/SegmentedDownloadEngine.cs`: concurrent range correctness, crash recovery.
- `src/TubeForge.Downloads/AdaptiveDownloadEngine.cs`: multi-track temp state, mux recovery.
- `src/TubeForge.Media/FfmpegMediaProcessor.cs`: FFmpeg invocation, output validation, temp cleanup.
- `src/TubeForge.Transcoding/FfmpegAudioTranscoder.cs`: current audio transcode pattern reusable for video transcode.
- `src/TubeForge.App/ViewModels/MainViewModel.cs`: large UI/workflow coordinator, risk of becoming unmaintainable as v2 features grow.
- `src/TubeForge.Updates/*`: release trust boundary, checksum policy, update-download validation.
- `src/TubeForge.Installation/*`: payload extraction, rollback, uninstall, path safety.

## Recommended Audit Actions Before v2 Release

1. Run deterministic gates on clean checkout.
2. Run full desktop performance probe ten times.
3. Run sanitized live canary set with public short, long, 4K/8K, Shorts, completed live replay, playlist, channel, captions, thumbnail, MP3, WebM, MKV, and low-space scenarios.
4. Run `Publish-Release.ps1`, `Test-Release.ps1`, `Publish-Installer.ps1`, and `Test-Installer.ps1` for v2.0.0.
5. Install v1.2.5, update to v2.0.0 through app UI, verify settings/queue/Library survive.
6. Install fresh v2.0.0, download canary matrix, uninstall with and without app data removal.
7. Verify Windows playback for MP4, MP3, M4A, and a compatible WebM/MKV path; verify FFmpeg decode for all outputs.
8. Run diagnostics export after failures and confirm no URL, signed stream, cookie, credential, local private path, or media bytes leak.
9. Review release asset names, checksums, signatures, attestations, third-party notices, and support policy.
10. Confirm v2 feature claims match implemented and tested behavior.

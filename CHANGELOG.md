# Changelog

## Unreleased

- Generalize persisted output profiles across native, audio-conversion, and video-conversion paths.
- Add resolution-aware H.264/AAC MP4, H.265/AAC MP4, and VP9/Opus WebM presets using encoders present in the pinned LGPL FFmpeg build.
- Preserve original-quality stream copy as default; stage native source media before optional video conversion and recover validated outputs after an interrupted queue checkpoint.
- Add fail-closed video output validation, cancellation/temporary-file cleanup, bounded disk forecasts, queue identity round-trips, UI selection coverage, and real synthetic encode/decode smoke proof.
- Add a preset-first download selector for Best original, Windows MP4, Small file, MP3 320, and Custom; detailed manual changes return the selector to Custom.
- Add opt-in single-video soft-subtitle embedding for MP4, MKV, and WebM with queue-safe language identity, recoverable intermediates, atomic publication, and FFmpeg subtitle-stream validation.

## 1.2.5 - 2026-07-21

- Replace latency-bound 1 MiB sequential Googlevideo reads with validated 8 MiB query ranges and up to four bounded workers, while retaining automatic sequential fallback for incompatible servers.
- Persist per-chunk completion for efficient retry/resume, migrate acceleration on by default, and preserve existing partial transfers when settings change.
- Select the largest trusted widescreen YouTube thumbnail instead of a smaller square primary image, removing the persistent black preview strip.

## 1.2.4 - 2026-07-21

- Replace Windows Media Foundation MP3 conversion with bundled FFmpeg `libmp3lame`, fixing high-bitrate AAC/M4A and Opus/WebM inputs that could fail before publication.
- Fail closed on missing FFmpeg, conversion failure, cancellation, or invalid MP3 output; retain validated-output queue recovery and clean temporary files.
- Add exhaustive dependent-filter coverage across download modes, containers, codecs, resolutions, frame rates, dynamic ranges, audio bitrates, processing profiles, and exact-output selection.

## 1.2.3 - 2026-07-20

- Preserve provider user agents that contain comma-separated product comments across metadata probes, direct downloads, and segmented downloads without relaxing header-injection bounds.
- Accept whitespace-only payload lines emitted by YouTube auto-caption WebVTT while continuing to reject malformed cue controls and timing.
- Complete a fresh installed-app E2E matrix covering MP4, WebM, MKV, M4A, MP3, captions, sidecars, 4K, a one-hour output, public channel selection, queue recovery, drive disconnect/reconnect, Windows playback, and FFmpeg decode validation.

## 1.2.2 - 2026-07-20

- Select the highest-bitrate, highest-sample-rate compatible audio track across MP4 and WebM families; use native MP4/WebM only as an equal-quality tie-breaker and MKV when the better audio track crosses container families.
- Report FFmpeg finalization failures using the selected MP4, WebM, or MKV output container.
- Synchronize current architecture and release-status documentation with the FFmpeg-backed WebM/MKV pipeline.

## 1.2.1 - 2026-07-18

- Bundle pinned x64 LGPL FFmpeg (8.1.2, verified SHA-256, license, source, and build provenance) and finalize MP4 outputs with `-c copy` and `+faststart`, converting YouTube fragmented DASH MP4 into conventional indexed MP4 without re-encoding.
- Route WebM adaptive muxing (VP9/AV1 + Opus/Vorbis) through FFmpeg stream-copy as well, giving every 1440p/4K/8K quality the same proven reliability as MP4; keep the in-house Mp4/WebM muxers as the FFmpeg-absent fallback.
- Add lossless Matroska (MKV) output as a cross-container fallback so any video codec can pair with the best available audio track when no same-container audio family exists; native MP4/WebM output is unchanged when both audio families are present.
- Stop silently dropping video qualities that lack a same-container audio companion; pair them across containers and mux into MKV instead.
- Fail closed instead of publishing MP4, WebM, or MKV output when bundled FFmpeg is absent, exits non-zero, or leaves fragmented/non-indexed structure.
- Recover validated MP4/WebM/MKV output after a crash between atomic publication and queue checkpoint without redownloading tracks.
- Add deterministic process-boundary tests plus live packet/decode/Windows-media verification for the FFmpeg finalization paths.

## 1.1.7 - 2026-07-17

- Stop rewriting unchanged resume metadata after every bounded media range.
- Treat cleanup of a temporarily locked, nonessential resume record as best-effort after media finalization.
- Add regression coverage for resume-record lock contention across multi-range downloads.
- Resolve release-script default output paths after script initialization so installer and archive packaging work under Windows PowerShell.

## 1.1.6 - 2026-07-17

- Prefer tail-verified no-token YouTube client profiles so advertised adaptive streams remain downloadable beyond initial probe bytes.
- Preserve client-specific user agents on media transfers and use bounded player-style Googlevideo range queries.
- Add regression coverage for large sequential ranges, Googlevideo query ranges, client identity, and end-of-stream verification.
- Keep signed media URLs, proof material, cookies, and account access out of persisted state.

## 1.0.0 - 2026-07-16

- Modern WPF download, queue, Library, settings, and diagnostics UI.
- Highest-compatible separate video/audio downloads with in-house MP4 and WebM muxing; MP4 preferred at equivalent quality.
- Native audio-only and video-only modes with truthful container/codec filtering.
- Public playlists, channels, Shorts, completed live replays, captions, thumbnails, chapters, and JSON sidecars.
- Resumable direct and segmented transfers, queue recovery, rate-limit handling, disk forecasting, and duplicate detection.
- Constrained classic/ES6 signature and throttling transforms without arbitrary JavaScript execution, plus Android client fallback.
- Framework-dependent and self-contained Windows x64 portable release artifacts with deterministic ZIP metadata and SHA-256 manifests.

Known limitations and support terms are documented in [docs/SUPPORT_POLICY.md](docs/SUPPORT_POLICY.md).

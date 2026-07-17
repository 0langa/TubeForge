# Changelog

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

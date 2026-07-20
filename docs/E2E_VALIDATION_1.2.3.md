# TubeForge 1.2.3 E2E validation

Validation date: 2026-07-20. Platform: Windows 11 x64. Application: installed self-contained x64 build controlled through the real desktop UI. Output root: `E:\TubeForgeE2E\v1.2.2-2026-07-20`.

## Result

Pass. The final diagnostics report recorded TubeForge 1.2.3, 11 completed outputs, and zero active, waiting, paused, failed, or cancelled items. The output directory contained 14 final files totaling 3,484,293,412 bytes and no `.part` or temporary media files.

## Media matrix

- Audio + video: H.264/AAC MP4, AV1/AAC MP4, VP9/AAC MKV, VP9/Opus WebM, and H.264/Opus MKV.
- Video only: H.264 MP4 and VP9 WebM.
- Audio only: AAC/M4A, Opus/WebM, and MP3 conversion.
- Quality and duration: 1080p, 2160p60/4K, short media, normal media, and a 60-minute 2.47 GB output.
- Sidecars: JPEG thumbnail, stable JSON metadata, and auto-English SRT captions.
- Collection: resolved a public channel with 60 videos across two pages, selected one item, queued it, downloaded it, and recorded it in Library.

## Integrity and playback

- FFmpeg decoded the nine initial short/normal media outputs at start, midpoint, and near end with zero decode errors.
- FFmpeg decoded the full 50-second collection MKV with exit code 0; it contained H.264 High 1920x1080 video and 48 kHz stereo Opus audio.
- The one-hour H.264/Opus MKV decoded at start, 30:00, and near end with exit codes 0/0/0.
- Windows Media Player displayed video and audio for H.264/AAC MP4, AV1/AAC MP4, VP9/Opus WebM, and the one-hour MKV. The MKV prompt was Windows' missing default-app association; after selecting Media Player, playback and seeking worked.
- The one-hour file played continuously beyond 40 seconds and sought successfully to 30:00 and 59:24.

## Application surfaces

- Download analysis, mode/quality/container/codec controls, destination browsing, and queue submission.
- Queue concurrency, progress, pause/resume, app termination/restart recovery, retry-safe finalization, and completed-item controls.
- Library search, sorting, missing-file state, reconnect recovery, and folder reveal.
- Settings persistence, segmented-transfer option, updater check surface, Diagnostics copy/export, and responsible-use gate.
- Strict URL rejection for a lookalike `youtube.com.evil.example` host.

## Resilience

- The E: destination was disconnected during validation. TubeForge remained usable, Library reflected missing media, and all 14 final files returned intact when the drive was reconnected.
- Library immediately reported the restored files as available. No partial files remained.
- Reinstalling the exact 1.2.3 installer preserved both the saved settings file and a disposable application-data marker byte-for-byte, confirming that normal install and upgrade paths retain `%LOCALAPPDATA%\TubeForge` data.

## Automated gates

- Release build: zero warnings and zero errors.
- Tests: 166/166 passed.
- Core performance: 0.5457 ms p95 against a 25 ms budget.
- GitHub Actions completed the clean v1.2.3 release workflow and published the installer, portable archives, manifest, and checksums.
- The public installer matched the published SHA-256 record, installed with exit code 0, and reported product version `1.2.3+9adeedda408e8a24888092183710cdb43037243c`.
- The installed app's updater reported TubeForge 1.2.3 as current after an explicit update check.
- Final redacted diagnostics: `Evidence\diagnostics-final.json` under the E2E root.

This matrix is broad release evidence, not a guarantee against every future upstream YouTube response change, network failure, regional policy, or unsupported access-controlled input.

# Extraction compatibility

YouTube is an upstream service outside TubeForge's control. Compatibility is versioned by TubeForge release and verified with synthetic fixtures plus bounded public canaries; it is not a permanent guarantee.

## v1.0.0 compatibility baseline

Validated on 2026-07-16:

- public standard videos and Shorts;
- completed live replays that expose normal downloadable streams;
- public playlist and channel enumeration with bounded continuation pages;
- progressive, native audio-only, and video-only MP4/WebM streams;
- highest-compatible separate video + audio selection and in-house MP4/WebM muxing;
- classic and ES6 `signatureCipher` transform shapes without JavaScript execution;
- structurally located `n` throttling transforms from the constrained supported operation set;
- versioned Android client fallback profile `ANDROID 20.10.38` when the watch page has no direct formats;
- captions, thumbnails, chapters, and metadata sidecars exposed by the supported public responses.

The live 4K canary resolved 27 formats and selected 2160p MP4 video plus AAC audio at this baseline. Canary identifiers and signed media URLs are intentionally not committed.

## Explicitly unsupported

- account login, cookies, private videos, memberships, purchases, or DRM;
- bypassing age, region, payment, or access controls;
- active or upcoming live-stream capture;
- MP3 or other transcoding/re-encoding;
- arbitrary JavaScript execution or general-purpose JavaScript evaluation;
- formats whose container/codec combination the in-house muxers cannot represent safely.

Malformed, oversized, or unsupported player scripts fail closed. When public extraction changes, follow the [extractor maintenance playbook](EXTRACTOR_PLAYBOOK.md) and add a sanitized synthetic regression before changing a client profile or transform rule.


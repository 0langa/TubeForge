# Support policy

TubeForge is a small, best-effort personal desktop project. There is no paid support, service-level agreement, hosted service, or guarantee that an upstream YouTube change can be fixed immediately.

## Supported environment

- Latest stable TubeForge release.
- Windows 10/11 x64.
- Self-contained build, or framework-dependent build with the matching .NET 10 Windows Desktop Runtime.
- Public media that exposes a supported non-DRM stream shape.

Security fixes and extractor compatibility fixes target the latest stable release and `main`. Older binaries may be asked to upgrade before a report is investigated.

## Reports

Use GitHub issues for sanitized functional reports and the process in [SECURITY.md](../SECURITY.md) for vulnerabilities. Never attach signed URLs, cookies, credentials, private/unlisted metadata, local paths, or downloaded media.

Include the TubeForge version, Windows version, typed failure code, extraction stage, and a reproduction using public media you are authorized to test. Upstream outages, geo/account restrictions, and unsupported access controls are not TubeForge defects.

## Current limitations

- Active/upcoming capture supports bounded public unencrypted YouTube HLS only, not generic M3U8 input.
- Audio/video conversion depends on bundled FFmpeg and remains explicitly opt-in; original-quality stream copy stays default.
- No authenticated, private, paid, membership, DRM, or access-control content.
- No encrypted HLS.
- No silent update, telemetry, or crash-upload service. Update checks are optional and installation always requires confirmation.
- Installer and portable distributions are Windows x64 only.
- Public extraction can break when YouTube changes response or player structure.


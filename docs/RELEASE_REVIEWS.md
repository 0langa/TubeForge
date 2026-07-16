# v1.0 release reviews

Review date: 2026-07-16. Scope: desktop runtime, local persistence, downloader/extractor, packaging, and public release process.

## Privacy

Status: pass with documented local retention.

- No telemetry, advertising, accounts, analytics SDK, or hosted conversion service exists.
- Network traffic is limited to public YouTube pages/API responses and trusted media/sidecar hosts. YouTube necessarily receives the user's IP address and requested public content identifier.
- Queue and Library persistence contains video identifiers, display titles, destination paths, format identities, sizes, and timestamps. It excludes signed media URLs, cookies, and credentials.
- Diagnostic export is explicit and whitelist-based; tests verify exclusion of URLs, IDs, titles, channels, local paths, headers, cookies, signatures, visitor data, and media.
- Retention and removal behavior is documented in [INSTALLATION.md](INSTALLATION.md).

## Security

Status: code/control review pass; threat-model context validation pending before final sign-off.

- Runtime downloads accept HTTPS `googlevideo.com` hosts only and re-check redirect destinations.
- YouTube/player responses, JavaScript tokens, JSON depth, captions, thumbnails, and media containers have explicit size/structure bounds.
- Player JavaScript is tokenized and mapped to a constrained operation plan; it is never executed.
- Final outputs use collision checks and atomic move/replace patterns; partial media cannot overwrite an existing completed destination.
- Queue/settings/history persistence is schema-validated with pending/backup crash recovery.
- The build rejects all `PackageReference` dependencies and enables latest .NET analyzers with warnings as errors.
- Remaining high-value review surfaces are the player parser, URL allowlists/redirect handling, container parsers/muxers, destination path handling, and release artifact integrity.

The available generic security-review skill has no C#/WPF-specific reference pack, so this review is grounded in repository controls, analyzers, tests, and the project threat model rather than a language checklist.

## Responsible use

Status: pass.

- First run blocks analysis/download until the responsible-use acknowledgement is accepted.
- UI and documentation limit use to media the user owns or is authorized to save.
- DRM, payment, membership, private-video, and access-control bypass are out of scope.
- No login/cookie import is implemented.

## Accessibility

Status: implementation review pass; no claim of third-party certification.

- Primary navigation, download controls, filter combo boxes, queue/history lists, and acknowledgement dialog expose automation names.
- Error/progress status uses assertive or polite automation live regions.
- Keyboard focus visuals, cycling dialog navigation, DPI layout rounding, and screen-reader labels are present in the WPF resources.
- The resource contract test prevents unresolved XAML resources. Release smoke tests cover application startup, not exhaustive NVDA/Narrator behavior.

## Release decision gates

- Clean Release build and formatter gate.
- Full dependency-free test suite.
- Core and desktop performance budgets.
- Framework-dependent and self-contained artifact verification.
- SHA-256 manifest verification.
- Public canary and highest-quality adaptive selection smoke.
- Threat model signed off after deployment/usage assumptions are confirmed.
- License choice recorded before outside contributions are accepted.


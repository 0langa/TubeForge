# Security policy

## Supported versions

Security fixes target the latest stable TubeForge release and `main`.

| Version | Supported |
|---|---|
| Latest stable (2.2.x) | Yes |
| `main` development snapshots | Best effort |
| Older releases | Upgrade first |

## Report a vulnerability privately

Use [GitHub private vulnerability reporting](https://github.com/0langa/TubeForge/security/advisories/new). Do not open a public issue containing exploit details, credentials, private media information, or other sensitive data.

Never include:

- cookies, account credentials, tokens, keys, or passwords;
- signed media URLs or query strings;
- visitor or session identifiers;
- private or unlisted video details;
- local usernames, full paths, or other personal information;
- downloaded media.

Useful safe details include the TubeForge version or commit, typed failure code, affected subsystem, sanitized reproduction structure, and whether the issue reproduces with synthetic/local test data.

Use the in-app Diagnostics export when possible. Its whitelist-only JSON excludes URLs, video IDs, titles, channels, local paths, headers, cookies, signatures, visitor data, and media. Review exported data before sharing it.

## Security boundaries

- Player JavaScript is tokenized into a constrained operation plan and never executed directly.
- Remote HTML, JSON, JavaScript, captions, thumbnails, HLS playlists, segments, and media containers are untrusted input.
- Downloads and generated sidecars use bounded parsing, temporary files, output validation, and atomic publication.
- Manual proxy mode is user-controlled; proxy credentials are rejected and proxy endpoints are excluded from diagnostics.
- Update installation requires explicit user confirmation and accepts only policy-matching assets from the official repository with matching GitHub and manifest SHA-256 records.
- TubeForge does not attempt to bypass DRM, payment, membership, login, or other access controls.

See the repository-grounded [threat model](TUBEFORGE_THREAT_MODEL.md) and [security false-positive response guide](docs/SECURITY_FALSE_POSITIVE_RESPONSE.md).

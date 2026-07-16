# Security Policy

## Supported versions

TubeForge has no supported release yet. Security fixes target the `main` branch during development.

## Reporting a vulnerability

Use GitHub's private vulnerability reporting feature when enabled. If unavailable, open a minimal issue asking the maintainer to establish a private channel; do not publish exploit details or secrets.

Never include:

- cookies or account credentials;
- signed media URLs or their query strings;
- visitor/session identifiers;
- private or unlisted video details;
- tokens, keys, passwords, or local personal information;
- downloaded media.

Useful safe details include the commit/version, failure code, affected subsystem, sanitized reproduction structure, and whether the issue works with synthetic/local test data.

Use the in-app Diagnostics export when possible. Its JSON schema is whitelist-only and excludes URLs, video IDs, titles, channels, local paths, headers, cookies, signatures, visitor data, and media. Review exported data before sharing it.

## Security boundaries

- Player JavaScript must never be executed directly.
- Remote JSON and media containers are untrusted input.
- Download output must remain under the user-selected directory.
- Partial files must not replace completed files.
- Logs and diagnostics must redact URL query strings and sensitive headers.
- TubeForge does not attempt to bypass DRM, payment, membership, or access controls.

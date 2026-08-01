# Security False-Positive Response

Use this template when Windows SmartScreen or an antivirus product reports a TubeForge release asset. Do not tell users to disable security software or add broad exclusions.

## Maintainer response template

Thanks for reporting this warning. Please do not run or whitelist the file until its origin and checksum are verified.

Provide only:

- TubeForge version and exact asset filename;
- download source page;
- SHA-256 hash;
- security product, engine/signature version, detection name, and detection time;
- whether Windows reports a digital-signature signer.

Do not provide media URLs, downloaded media, cookies, account data, signed stream URLs, user profile paths, or raw TubeForge logs.

Maintainer checks:

1. Compare the reported hash with the official release `SHA256SUMS.txt` and release manifest.
2. Verify GitHub build provenance with `gh attestation verify <asset> -R 0langa/TubeForge` when the release includes an attestation.
3. Compare the manifest `authenticodeSigned` field with the file's actual signature state.
4. Reproduce the scan against the exact official asset. Never substitute a locally rebuilt binary.
5. If the hash or origin differs, advise deletion and stop treating the report as an official-release false positive.
6. If the official hash matches, submit the exact asset and hash to the detecting vendor's false-positive process and track the vendor case without publishing private reporter data.
7. If independent evidence indicates malicious behavior or release compromise, stop distribution, remove affected assets, rotate release credentials, and follow `SECURITY.md`.

## User verification reply

The official asset hash is `<SHA256>`. Compare it with the release checksum before running the file. The release manifest says Authenticode is `<true-or-false>`; an unsigned build may trigger SmartScreen even when its checksum and GitHub provenance are valid. If your hash differs, delete the file and download only from the official GitHub release page.

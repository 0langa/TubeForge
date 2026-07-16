# Installation, upgrades, and removal

TubeForge v1 uses portable ZIP distributions. It does not write an installer registry entry, add a Windows service, or modify `PATH`.

## Choose a build

- `self-contained`: recommended. Includes the .NET 10 Windows Desktop runtime and does not require a separate runtime installation.
- `framework-dependent`: smaller. Requires the x64 .NET 10 Windows Desktop Runtime already installed.

Both builds target Windows 10/11 x64. Extract the whole archive; do not run `TubeForge.exe` from inside the ZIP.

The self-contained release build restores only the exact Microsoft .NET runtime packs selected by the pinned SDK from the official NuGet feed. Application projects still reject every `PackageReference`; no third-party application package is introduced.

## Verify and install

Keep the downloaded ZIP and `SHA256SUMS.txt` in the same directory. In PowerShell:

```powershell
$version = '1.0.0'
$name = "TubeForge-$version-win-x64-self-contained.zip"
$expected = (Get-Content .\SHA256SUMS.txt | Where-Object { $_ -match "  $([regex]::Escape($name))$" }).Split(' ')[0]
$actual = (Get-FileHash -LiteralPath ".\$name" -Algorithm SHA256).Hash
if ($actual -cne $expected) { throw 'TubeForge checksum mismatch.' }
```

Extract to a versioned directory such as:

```text
%LOCALAPPDATA%\Programs\TubeForge\1.0.0\
```

Then run `TubeForge.exe`. Windows may warn for an unsigned build. Check the release manifest field `authenticodeSigned`; do not assume an unsigned artifact is signed.

GitHub-hosted release artifacts also have signed build-provenance attestations. Online verification requires GitHub CLI:

```powershell
gh attestation verify ".\$name" -R 0langa/youtube-downloader
```

## Upgrade and rollback

1. Let active downloads finish or pause them, then close TubeForge.
2. Verify the new archive checksum.
3. Extract the new version into a new sibling directory. Never overwrite a running installation in place.
4. Start the new version. Existing local settings, queue, and Library are reused from `%LOCALAPPDATA%\TubeForge`.
5. Keep the prior application directory until the new version has completed one analyze/download smoke test.

Rollback by closing TubeForge and starting `TubeForge.exe` from the previous version directory. A release that changes a persistence schema must document downgrade compatibility in its release notes. v1.0 uses schema version 1 for settings, queue, and Library history.

## Local data and retention

TubeForge stores application state in `%LOCALAPPDATA%\TubeForge`:

- `settings.json`: download directory, filename template, concurrency, segmented-transfer preference, responsible-use acknowledgement;
- `queue.json`: video IDs, display titles, format identities, destination paths, byte counts, timestamps, and failure codes;
- `history.json`: completed video IDs, display titles, format identities, destination paths, sizes, and timestamps;
- `.bak` and `.pending` siblings: crash-recovery copies of those stores.

Signed media URLs, cookies, credentials, and downloaded media are not stored in these application-state files. Downloads, captions, thumbnails, metadata sidecars, and partial transfer files remain in the destination selected by the user. A diagnostic JSON exists only when the user explicitly exports it.

## Uninstall

First close TubeForge. Remove the versioned application directories to uninstall the program. This leaves local state and downloaded files intact.

Inspect retained state before deleting it:

```powershell
Get-ChildItem -LiteralPath "$env:LOCALAPPDATA\TubeForge" -Force
```

To reset TubeForge state, delete `%LOCALAPPDATA%\TubeForge` after reviewing the path. This does not delete downloaded media stored elsewhere. Remove downloaded media separately only if that is your intent.

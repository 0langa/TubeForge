# Installation, upgrades, and removal

TubeForge v1.1 provides a recommended per-user installer and portable ZIP distributions. Neither option adds a Windows service or modifies `PATH`.

## Choose a build

- `setup.exe`: recommended. Installs the self-contained app for the current user, adds a Start Menu shortcut, and registers TubeForge in Add/Remove Programs without elevation.
- `self-contained`: recommended portable option. Includes the .NET 10 Windows Desktop runtime and does not require a separate runtime installation.
- `framework-dependent`: smaller. Requires the x64 .NET 10 Windows Desktop Runtime already installed.

Both builds target Windows 10/11 x64. Extract the whole archive; do not run `TubeForge.exe` from inside the ZIP.

The self-contained release build restores only the exact Microsoft .NET runtime packs selected by the pinned SDK from the official NuGet feed. Application projects still reject every `PackageReference`; no third-party application package is introduced.

## Verify and install

Keep the downloaded installer or ZIP and `SHA256SUMS.txt` in the same directory. In PowerShell:

```powershell
$version = '1.1.5'
$name = "TubeForge-$version-win-x64-setup.exe"
$expected = (Get-Content .\SHA256SUMS.txt | Where-Object { $_ -match "  $([regex]::Escape($name))$" }).Split(' ')[0]
$actual = (Get-FileHash -LiteralPath ".\$name" -Algorithm SHA256).Hash
if ($actual -cne $expected) { throw 'TubeForge checksum mismatch.' }
```

Run the verified setup executable. The default per-user installation directory is:

```text
%LOCALAPPDATA%\Programs\TubeForge\
```

Portable users can instead extract a complete ZIP to a versioned directory and run `TubeForge.exe`. Windows may warn for an unsigned build. Check the release manifest field `authenticodeSigned`; do not assume an unsigned artifact is signed.

GitHub-hosted release artifacts also have signed build-provenance attestations. Online verification requires GitHub CLI:

```powershell
gh attestation verify ".\$name" -R 0langa/TubeForge
```

## Upgrade and rollback

1. Let active downloads finish or pause them, then close TubeForge.
2. Use Settings to check for an update, or download the new installer from the official release.
3. TubeForge verifies the repository, version, asset name, size, GitHub digest, and matching SHA-256 manifest before offering to run an update.
4. Confirm installation explicitly. Existing local settings, queue, and Library are reused from `%LOCALAPPDATA%\TubeForge`.

Portable users should verify and extract the new archive to a sibling directory. Keep the prior portable directory until the new version has completed an analyze/download smoke test.

Portable rollback uses the previous version directory. Installer rollback requires reinstalling a previously verified setup asset. A release that changes a persistence schema must document downgrade compatibility in its release notes. v1.1 uses schema version 1 for settings, queue, and Library history.

## Local data and retention

TubeForge stores application state in `%LOCALAPPDATA%\TubeForge`:

- `settings.json`: download directory, filename template, concurrency, segmented-transfer preference, responsible-use acknowledgement;
- `queue.json`: video IDs, display titles, format identities, destination paths, byte counts, timestamps, and failure codes;
- `history.json`: completed video IDs, display titles, format identities, destination paths, sizes, and timestamps;
- `.bak` and `.pending` siblings: crash-recovery copies of those stores.

Signed media URLs, cookies, credentials, and downloaded media are not stored in these application-state files. Downloads, captions, thumbnails, metadata sidecars, and partial transfer files remain in the destination selected by the user. A diagnostic JSON exists only when the user explicitly exports it.

## Uninstall

First close TubeForge. Use Add/Remove Programs or the TubeForge uninstaller in the installation directory. User data and downloaded files are preserved by default; the uninstaller offers an explicit local-data removal choice. Portable users can remove their extracted application directory.

Inspect retained state before deleting it:

```powershell
Get-ChildItem -LiteralPath "$env:LOCALAPPDATA\TubeForge" -Force
```

To reset TubeForge state, delete `%LOCALAPPDATA%\TubeForge` after reviewing the path. This does not delete downloaded media stored elsewhere. Remove downloaded media separately only if that is your intent.

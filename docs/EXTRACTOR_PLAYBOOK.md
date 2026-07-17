# Extractor maintenance playbook

Use this playbook when live analysis starts returning `Extractor.*` failures or format counts change unexpectedly. Never commit live URLs, player scripts, signed media URLs, cookies, visitor data, or private-video metadata.

## Canary set

Maintain a local text file outside Git with 3–10 public videos you are authorized to probe. Prefer stable, short videos covering:

- progressive plus adaptive MP4;
- adaptive WebM;
- captions;
- different durations and channels;
- one video known to use signature-ciphered formats when available.

One URL per line. Blank lines and lines starting with `#` are ignored. Maximum: 25 URLs and 64 KiB.

```powershell
dotnet run --project tools/TubeForge.Smoke --configuration Release -- canary C:\private\tubeforge-canaries.txt
```

Output contains only ordinal, typed failure/stage, aggregate format counts, and selected output container. It does not print input URLs, video IDs, titles, channels, media URLs, or error technical details.

## Triage order

1. Run full local test suite and Release build. Fixture failure means repository regression; live-only failure suggests upstream change.
2. Run canary set once. Do not loop aggressively; avoid triggering rate limits.
3. Classify failure:
   - `Network.*` or rate-limit response: wait and retry later.
   - `Extractor.PageChanged`: inspect bounded watch-page structure.
   - `Extractor.PlayerChanged`: inspect player-script structure; never execute script.
   - `Extractor.NoStreams`: compare watch-page and tail-verified client fallback stages.
   - `Network.HttpError` with HTTP 403 after successful analysis: confirm selected client passed end-of-stream probes and preserve its user agent on media requests.
   - supported metadata but missing high-quality output: inspect format classification and mux compatibility.
4. Reproduce with smallest legally safe synthetic fixture. Record source date, expected parser behavior, and why fixture contains no copyrighted media or secrets.
5. Patch narrow extractor stage. Unsupported syntax must fail closed.
6. Add regression fixture/test before updating live profile constants.
7. Run gates below and one non-looping canary pass.

## Required gates

```powershell
dotnet run --project tests/TubeForge.Tests --configuration Release -- --all
dotnet build TubeForge.slnx --configuration Release --no-restore
dotnet format TubeForge.slnx --verify-no-changes --no-restore
```

For formatter host mismatch on this development machine only, select system host without changing global configuration:

```powershell
$env:DOTNET_ROOT='C:\Program Files\dotnet'
$env:PATH='C:\Program Files\dotnet;' + $env:PATH
& 'C:\Program Files\dotnet\dotnet.exe' format TubeForge.slnx --verify-no-changes --no-restore
```

## Safe issue data

Include commit, UTC test time, canary ordinal, failure code, extractor stage, aggregate p/a/v counts, and synthetic test name. Exclude URLs, IDs, titles, channels, query strings, headers, and downloaded media.

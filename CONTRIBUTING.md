# Contributing

TubeForge is in early development and does not yet accept outside code contributions. Issue reports and design feedback are welcome.

## Before reporting a problem

- Confirm the content is public and you are authorized to save it.
- Remove cookies, signatures, visitor data, media URLs, private titles, local usernames, and full output paths.
- Never attach downloaded copyrighted media.
- Include the TubeForge version, Windows version, failure code, and minimal reproduction steps.

## Engineering rules

- No third-party NuGet/npm packages, external executables, hosted downloader services, or copied extractor code.
- Keep YouTube-specific behavior inside `TubeForge.YouTube`.
- Add focused dependency-free tests for behavior changes.
- Stream media; never buffer a complete download in memory.
- Fail closed on malformed player scripts, JSON, URLs, paths, or container data.
- Preserve cancellation across all network and disk operations.

## Local checks

```powershell
dotnet build TubeForge.slnx --configuration Release
dotnet run --project tests/TubeForge.Tests --configuration Release -- --all
```

No license has been selected. Do not submit code copied from projects whose terms are incompatible or unknown.

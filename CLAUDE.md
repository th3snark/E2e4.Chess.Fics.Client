# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

`E2e4.Chess.Fics.Client`: a cross-platform (`net10.0`) FICS (Free Internet Chess Server) client published as a NuGet package. It was
split out of <https://github.com/th3snark/E2e4.Chess> (MIT), which is its only known consumer. MIT licence.

## Commands

```
dotnet test E2e4.Chess.Fics.Client.slnx                                    # build + all tests (CI uses --configuration Release)
dotnet test tests/E2e4.Chess.Fics.Client.Test --filter "FullyQualifiedName~FicsFrameTokenizer"
dotnet pack src/E2e4.Chess.Fics.Client/E2e4.Chess.Fics.Client.csproj --configuration Release
dotnet format whitespace E2e4.Chess.Fics.Client.slnx --verify-no-changes   # CRLF line endings; CI runs it on Windows
```

Releases: push a `vX.Y.Z` tag; `publish.yml` tests, packs with `-p:Version=<tag minus v>` and pushes to nuget.org with Trusted
Publishing. The csproj intentionally has no fixed `Version`. Do not tag or publish without the owner's say-so.

## Architecture

- `System.IO.Pipelines` framing (`Protocol/FicsFrameTokenizer`), a `Channel<T>` message stream, a typed message parser, style-12 boards,
  and an `IFicsTransport` seam over TCP (`Transport/`). Applications program against `IFicsClient` (implemented by `FicsClient`) and can test with a fake.
- Layout for a later protocol/client split: protocol types (`Protocol/`, namespaces `E2e4.Chess.Fics.Protocol[.Messages]`) never
  reference client types; client types (`FicsClient`, options, `Transport/`, `ITimesealCodec`) are in `E2e4.Chess.Fics.Client[.Transport]`. Keep that direction.
- The message parser has no catch-all by design: malformed input becomes `UnknownLineMessage` through `TryParse` paths. `Style12` and the messages are immutable (`init`).
- `FicsClientOptions` is settable (for `IOptions` binding) and carries an optional `ILogger` (never raw lines or passwords) and a `TimeProvider`.
- DI: `AddFicsClient(...)` registers `IFicsClientFactory` (singleton), `IFicsClient` (transient) and a shared `IFicsTransport`.
- Tests: xunit; `Support/FakeFicsServer.cs` is an in-memory server with failure injection; `Fixtures/` are synthetic transcripts.

## Licensing and privacy rules

- **Never reference `E2e4.Chess.OpenSeal` (GPL v2+) from this repository.** Timeseal comes in through `ITimesealCodec`; `LicenceBoundaryUnitTest` and a CI step enforce it.
- Fixtures must stay synthetic: no banner art, help text, real player handles, addresses or other content from a real server. Do not commit raw captures, passwords or tokens.
- Keep the outbound line guard (`FicsLineEncoder.ThrowIfNotSingleLine`) and the parser length caps; their tests are security tests.

## Build constraints

- `assets/icon.png` (and its source `assets/icon.svg`) is generated, not hand-drawn: it is the E2e4.Chess base icon plus this package's badge, built by `Branding/Build-Icons.ps1` in the E2e4.Chess repository. Change the icon there and copy the files from `Branding/dist`.
- `Directory.Build.props`: `Nullable`, `AnalysisLevel latest-recommended`, `GenerateDocumentationFile`, `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`. The tree builds with 0 warnings. Versions are central in `Directory.Packages.props`.
- Line endings are CRLF (`.editorconfig`). Write/Edit tools and `sed -i` can leave LF; fix before committing. Do not change `.gitattributes` or git config.
- File-scoped namespaces everywhere (`IDE0161` is a build error).

## Consumer: E2e4.Chess

`E2e4.Chess.Fics.Client.Demo` (WPF) takes this package; it supplies the timeseal codec from the OpenSeal package (`OpenSealCodec`), so only that built demo is GPL.
After a release, E2e4.Chess bumps the version in its `Directory.Packages.props`. E2e4.Chess's own tests use a fake client, so a break here will not show up there.

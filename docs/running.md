# Running the desktop foundation

Install the .NET 10 SDK on Windows or macOS, then from the repository root:

```sh
dotnet run --project src/Baccarat.Desktop
dotnet run --project tests/Baccarat.Core.Checks
```

On this Windows workspace, a downloaded SDK may be used via `.tools/sdk/dotnet.exe` instead of `dotnet`.

The application supports manual Player/Banker/Tie records, counts, undo, confirmed clearing and CSV export. The Strategies tab imports the supplied JSON-shaped `.txt` definitions, calculates verified progression rules, accepts observed table outcomes, tracks profit and limits, and exports its own CSV history. Opaque protected rules require a manual side or stake rather than being guessed. Records remain in memory, so export before closing.

The Viewer tab receives live versioned snapshots whenever the manual session or strategy state changes. It shows the active protocol and document versions, communication status and ordered history. The current communication transport is in-process and offline: it neither connects to the original server nor sends credentials or game data over a network.

The Automation tab exercises the complete controlled workflow against a local simulation. Connect the simulation, prepare and approve the initial recommendation, then enable auto-bet if the next strategy recommendation should be prepared automatically after each observed round. Every simulated placement still requires approval. Stake, cumulative-session and chip-click limits are enforced; connection recovery returns paused; an uncertain placement is never retried.

The Account tab exercises validation and explicit offline state through a replaceable authentication boundary; it does not send credentials. There is no live authentication, real-money betting, outcome prediction or connection to the original service.

## Self-contained publish

```sh
dotnet publish src/Baccarat.Desktop -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64
dotnet publish src/Baccarat.Desktop -c Release -r osx-arm64 --self-contained true -o artifacts/publish/osx-arm64
dotnet publish src/Baccarat.Desktop -c Release -r osx-x64 --self-contained true -o artifacts/publish/osx-x64
```

Run Baccarat.Desktop.exe on Windows or the Baccarat.Desktop executable on a matching Mac. macOS app-bundle packaging, signing and notarization are pending; the publish commands alone do not create a signed installer. Source code is shared across these targets, but macOS runtime behavior must be tested on a Mac.

The GitHub Actions workflow builds on Windows and macOS for pushes and pull requests.

## Validation record

The .NET 10.0.401 SDK is installed locally. The Release desktop build, core checks and headless Avalonia checks passed on Windows on 2026-10-13. Avalonia is pinned to 11.3.22, and the dependency vulnerability check reported no known vulnerable packages. No Mac execution has been performed.

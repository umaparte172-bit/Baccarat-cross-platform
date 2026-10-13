# Running the desktop foundation

Install the .NET 10 SDK on Windows or macOS, then from the repository root:

```sh
dotnet run --project src/Baccarat.Desktop
dotnet run --project tests/Baccarat.Core.Checks
```

On this Windows workspace, a downloaded SDK may be used via `.tools/sdk/dotnet.exe` instead of `dotnet`.

The application currently supports manual Player/Banker/Tie records, counts, undo, confirmed clearing and CSV export. Records are held in memory: export before closing. The Account tab exercises validation and explicit offline state through a replaceable authentication boundary; it does not send credentials. There is no live authentication, automated betting, outcome prediction or connection to the original service. This is the foundation build, not feature parity with the original executable.

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

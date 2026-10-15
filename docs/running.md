# Running the desktop foundation

Install the .NET 10 SDK on Windows or macOS, then from the repository root:

```sh
dotnet run --project src/Baccarat.Desktop
dotnet run --project tests/Baccarat.Core.Checks
```

On this Windows workspace, the isolated SDK is available via `.tools/dotnet/dotnet.exe` instead of `dotnet`.

The application supports manual Player/Banker/Tie records, counts, undo, confirmed clearing and CSV export. The Strategies workspace imports the supplied JSON-shaped `.txt` definitions, calculates supported progression rules, accepts observed table outcomes, tracks profit and limits, and exports its own CSV history. Opaque protected rules require a manual side or stake rather than being guessed. Records remain in memory, so export before closing.

The Viewer tab receives live versioned snapshots whenever the manual session or strategy state changes. It shows the active protocol and document versions, communication status and ordered history. The current communication transport is in-process and offline: it neither connects to the original server nor sends credentials or game data over a network.

The Automation tab exercises the complete controlled workflow against a local simulation. Connect the simulation, prepare and approve the initial recommendation, then enable auto-bet if the next strategy recommendation should be prepared automatically after each observed round. Every simulated placement still requires approval. Stake, cumulative-session and chip-click limits are enforced; connection recovery returns paused; an uncertain placement is never retried.

Version 1 starts directly at Home. No account, password, registration, license, activation or online authentication is required. Choose Session, Strategies, Automation or Viewer; **Back to home** is at the upper-left of each workspace. There is no account tab or Sign Out button. Existing demo credentials and remembered sessions are not read. Closing cancels pending simulation approval and disables auto-bet. There is no real-money betting, outcome prediction or connection to the original service.

PATTERN is the priority strategy, separate from IHG. Loading its supplied definition asks you to choose the initial side with stake 100. After a win or loss, choose a side and enter the next stake manually. Its automatic selection and amount algorithms remain unverified. The current replacement applies standard Player/Banker settlement and tie pushes, not a verified original PATTERN tie rule. See [the workflow acceptance record](v1-workflow-acceptance.md).

## Self-contained publish

```sh
dotnet publish src/Baccarat.Desktop -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64
dotnet publish src/Baccarat.Desktop -c Release -r osx-arm64 --self-contained true -o artifacts/publish/osx-arm64
dotnet publish src/Baccarat.Desktop -c Release -r osx-x64 --self-contained true -o artifacts/publish/osx-x64
```

Run `Baccarat.Desktop.exe` on Windows. The raw macOS publish commands are useful for diagnostics, but the packaging workflow below should be used to create a normal application bundle.

## macOS app bundle

On a Mac with the .NET 10 SDK, choose the runtime matching the target machine. The standard package supports macOS 14 and newer:

```sh
bash tools/package-macos.sh osx-arm64 Release artifacts/macos/osx-arm64
bash tools/package-macos.sh osx-x64 Release artifacts/macos/osx-x64
```

Each command creates and validates `Baccarat Workspace.app` for macOS 14 or newer. The output directory must not already contain that bundle, which prevents stale files from leaking into a package. CI runs both commands and publishes separate unsigned ZIP artifacts from the `Desktop build` workflow. Those artifacts are appropriate for development verification only; they are not signed public releases.

For macOS 11–13, create the isolated legacy package instead:

```sh
bash tools/package-macos-legacy.sh osx-arm64 Release artifacts/macos-legacy/osx-arm64
bash tools/package-macos-legacy.sh osx-x64 Release artifacts/macos-legacy/osx-x64
```

The legacy package uses the final .NET 6 runtime because current supported .NET releases do not support macOS 11. It is built and checked separately, is never substituted for the modern package, and must not be represented as a currently supported or security-maintained runtime. Use `osx-arm64` for Apple Silicon and `osx-x64` for Intel. See [the compatibility guide](macos-11-compatibility.md).

For distribution, first install a Developer ID Application certificate in the login Keychain and create a `notarytool` profile interactively:

```sh
xcrun notarytool store-credentials Baccarat-notary
export APPLE_SIGNING_IDENTITY="Developer ID Application: Organization (TEAMID)"
export APPLE_NOTARY_PROFILE="Baccarat-notary"
bash tools/sign-notarize-macos.sh \
  "artifacts/macos/osx-arm64/Baccarat Workspace.app" \
  osx-arm64 \
  "artifacts/macos/osx-arm64/Baccarat-Workspace-osx-arm64.zip"
```

The signing script stops if either configuration value is missing, signs nested Mach-O files before the bundle, enables hardened runtime and a secure timestamp, submits with `notarytool`, staples the ticket, then runs signature, stapler and Gatekeeper validation. It does not accept or store an Apple password. Repeat with `osx-x64` for an Intel package.

Historical authentication and protected-store adapters remain in source for reference and isolated tests, but V1 does not construct or use them. Existing entries under `com.baccarat.crossplatform` or `com.baccarat.crossplatform.demo` are not read or modified by V1 startup.

The GitHub Actions workflow builds on Windows and macOS for pushes and pull requests and creates both unsigned macOS architecture artifacts.

## Windows package

On Windows with the .NET 10 SDK on `PATH`:

```powershell
./tools/package-windows.ps1 `
  -Runtime win-x64 `
  -Configuration Release `
  -OutputDirectory artifacts/windows/win-x64
```

This creates a self-contained application directory, an unsigned development ZIP and its SHA-256 sidecar. CI publishes the same files as `baccarat-workspace-win-x64-unsigned`.

For public distribution, install a code-signing certificate in the current user's certificate store and make `signtool.exe` available, then run:

```powershell
$env:WINDOWS_SIGNING_CERTIFICATE_THUMBPRINT = 'CERTIFICATE_THUMBPRINT'
./tools/sign-windows.ps1 `
  -ApplicationDirectory 'artifacts/windows/win-x64/Baccarat Workspace' `
  -OutputArchive 'artifacts/windows/win-x64/Baccarat-Workspace-win-x64-signed.zip'
```

The signing hook signs and timestamp-verifies only the replacement application's executable and assemblies. It does not import, export or store a private key.

## Verify downloaded artifacts

Keep the ZIP and `.sha256` file in the same directory. On macOS:

```sh
shasum -a 256 -c Baccarat-Workspace-osx-arm64-unsigned.zip.sha256
```

On Windows, calculate the ZIP hash and compare it with the lowercase value in the sidecar:

```powershell
(Get-FileHash Baccarat-Workspace-win-x64-unsigned.zip -Algorithm SHA256).Hash.ToLowerInvariant()
Get-Content Baccarat-Workspace-win-x64-unsigned.zip.sha256
```

A checksum detects accidental or unauthenticated modification after CI produced an artifact; it does not replace Authenticode, Developer ID signing or notarization for a public release.

## Validation record

The .NET 10.0.401 SDK is installed locally. The Release desktop build, core checks and headless Avalonia checks passed on Windows on 2026-10-14. The same build and checks passed on a GitHub-hosted macOS runner, including a Keychain save/read/delete round trip. CI creates and validates modern and macOS 11 compatibility bundles for `osx-arm64` and `osx-x64`. Avalonia is pinned to 11.3.22, and the dependency vulnerability check reported no known vulnerable packages. Interactive installation on macOS 11, Developer ID signing/notarization and live browser integration still require owner testing.

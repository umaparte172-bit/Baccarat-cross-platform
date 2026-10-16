# Running the desktop foundation

Install the .NET 10 SDK on Windows or macOS, then from the repository root:

```sh
dotnet run --project src/Baccarat.Desktop
dotnet run --project tests/Baccarat.Core.Checks
```

On this Windows workspace, the isolated SDK is available via `.tools/dotnet/dotnet.exe` instead of `dotnet`.

## Default dashboard (video-led Phases 2–3)

The app restores the local workspace, or opens **No Session** on first use, without login or server access. Choose
**New Session**, record Banker/Player/Tie, optionally select pair flags, then use
**Undo** or **Export CSV**. The sidebar switches independent sessions; its toggle
hides it on smaller screens. Up to 100 sessions and 10,000 hands per session are
retained locally, with a 32 MiB workspace-file limit. Export includes all recorded
results, pairs, settlements and shoe numbers, but not settings. Autosave runs after
750 ms without edits and flushes on normal close. **Tools → Save workspace now**
saves immediately. The footer shows the save status and full file location.

The file is `BaccaratWorkspace/workspace-v1.json` under the runtime's local application
data folder (`%LOCALAPPDATA%` on Windows). Its `.bak` file is the previous successful
save. If the primary is invalid, a validated backup is opened without automatically
overwriting anything: use **Tools → Save recovered workspace** to confirm recovery.
The rejected original is retained beside it with a unique `.rejected-...` suffix.
If both copies are invalid, inaccessible, or another instance holds the file,
the app warns that the workspace is unsaved. Do not discard that warning.
A failed save prevents silent closing; return to fix/retry or explicitly close without saving.

Select one of eight strategies and edit its fields. **Apply settings** or Enter
validates and applies the draft; changes remain separate between sessions and
strategy choices. Use a dot decimal separator, not commas, for single amounts.
These draft fields do **not** override imported rules. Open **Tools → Strategies**
and load an existing strategy to use its supported calculations/manual resolution.
The dashboard then shows its next recommendation and calculated balance/P&L;
result entry and Undo on either screen affect the same session. **New Shoe** resets
shoe counts/streaks, retaining session balance and strategy progression. Undo on an
empty new shoe removes that boundary; subsequent Undo removes the previous hand.
New shoes cancel pending simulation and require review before resuming.
All five roadmaps update from the current shoe. Scroll a road horizontally to
review earlier entries. Ask shows the derived marks that Banker or Player would
produce next, without recording a hand. Undo and New Shoe update the roads too.
See [Phase 5](video-phase-5-roadmaps.md).

## Retained tools and account screens

**Tools** opens the previous features inside the current application window,
without sign-in. Choose Strategies, Automation, Viewer or Account; Back to
dashboard returns without losing state. All tools belong to the selected
dashboard session. Switching sessions cancels approvals and disables auto-bet.
Tools also offers confirmed session clearing. Strategies imports the supplied
JSON-shaped `.txt` definitions and reuses the prior rules and manual fallbacks.
Records, strategy definitions, manual decisions, drafts and Undo state persist locally;
simulation always restarts disconnected, without approvals. See [shared-state semantics](integrated-tools.md).

The Viewer tab receives live versioned snapshots whenever the manual session or strategy state changes. It shows the active protocol and document versions, communication status and ordered history. The current communication transport is in-process and offline: it neither connects to the original server nor sends credentials or game data over a network.

The Automation tab exercises the complete controlled workflow against a local simulation. Connect the simulation, prepare and approve the initial recommendation, then enable auto-bet if the next strategy recommendation should be prepared automatically after each observed round. Every simulated placement still requires approval. Stake, cumulative-session and chip-click limits are enforced; connection recovery returns paused; an uncertain placement is never retried.

Inside **Tools → Account**, the retained Sign In and Sign Up screens are available.
Successful authentication with an injected test service returns to Tools;
Account retains Sign Out. The shipped original-service adapter remains
unavailable; it does not accept demo credentials or create fake accounts.
Old Wetalkbet integration is no longer part of the new client plan.

**Remember Me** is enabled and saves a successful session, not your password, in Windows Credential Manager or macOS Keychain. An unchecked session is not persisted; Sign Out removes the saved session, and expired sessions are rejected. The current unavailable/offline service cannot create or restore a real server login. Its checkbox does not bypass that limitation. Saving/restoring is covered with injected test sessions; live Wetalkbet verification remains pending.

**OPEN OFFLINE WORKSPACE** on Account returns to the dashboard without authenticating.
Back to dashboard and Back to Tools provide in-window navigation. Sign-out and
closing cancel pending simulation approvals and disable auto-bet. There is no
real-money betting or connection to the original service.

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

The account UI uses the authentication controller and an unavailable/offline service by default. Session storage uses the isolated service `com.baccarat.crossplatform.wetalkbet`; old entries under `com.baccarat.crossplatform` or `com.baccarat.crossplatform.demo` are not reused. Injected demo/test services do not receive this production store unless explicitly configured. Offline mode skips session restoration instead of treating an old token as a verified login.

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

# Stage 4 architecture and cumulative audit report

Audit date: 2026-10-13. The original application was not executed. All verification applies to the reconstructed project and static-analysis tools.

## Stage 4 outcome

The project now has explicit Core, Infrastructure and Desktop boundaries, a single solution entry point, centrally managed dependencies, deterministic warning-free builds, validated disconnected/network configuration, a secret-storage abstraction, redacted diagnostics and a composition root.

## Project structure

| Component | Responsibility | Dependency rule | Verification |
|---|---|---|---|
| `Baccarat.Core` | Domain rules, authentication state, configuration validation and interfaces | No UI or infrastructure dependency | Compiled; core checks passed |
| `Baccarat.Infrastructure` | Offline authentication, redacted diagnostics and volatile secret adapter | References Core only | Compiled; adapter checks passed |
| `Baccarat.Desktop` | Avalonia views, navigation and service composition | References Core and Infrastructure | Compiled; headless UI checks passed |
| `Baccarat.Core.Checks` | Deterministic application and adapter checks | No external service calls | Passed |
| `Baccarat.Desktop.Checks` | Headless startup and UI behavior | No original application or backend | Passed |

`Baccarat.slnx` is the canonical solution. `global.json` selects .NET SDK 10.0.401 with latest-patch roll-forward. `Directory.Packages.props` centrally pins Avalonia 11.3.22. `NuGet.Config` clears machine-specific feeds and uses the official NuGet v3 source. `Directory.Build.props` enables nullable analysis, latest analysis rules, deterministic output and warnings-as-errors.

## Architecture behaviors verified

- Disconnected configuration is valid and performs no network operation.
- Partial endpoint configuration is rejected.
- HTTP and WebSocket endpoints must use HTTPS and WSS.
- Request timeout is restricted to 1 second through 2 minutes.
- Invalid authentication input never calls a service.
- Rejected, empty, malformed and exceptional authentication outcomes leave no session.
- Successful test authentication creates a session.
- Sign-out clears the local session even if remote confirmation fails.
- Password input is cleared after an attempt and is not persisted.
- The current secret adapter supports save/read/delete only in process memory.
- Diagnostic output redacts email addresses, passwords, bearer tokens, API keys, secrets and token assignments.
- UI code has no direct HTTP or WebSocket dependency.

## Cumulative completion audit

### Stage 1 — evidence baseline

**Complete.** Forty-nine supplied files were inventoried with path, size, SHA-256, signature and version metadata. Original material is excluded from Git. No supplied executable was launched.

### Stage 2 — static decompilation

**Complete to the protected-image boundary.** Forms, controls, method tokens, startup branch, workflow state-machine names, configuration decoder inputs and five encrypted resources were documented. Runtime-restored method bodies and plaintext contracts remain unavailable statically.

### Stage 3 — application foundation

**Complete.** The replacement starts headlessly, records and exports manual rounds, handles account states through a replaceable service, remains useful offline, and has controlled validation/error behavior.

### Stage 4 — application architecture

**Complete.** Project/layer boundaries, dependency policy, configuration validation, diagnostics, secret-storage boundary, composition root, centralized packages and solution-wide build policy are implemented and tested.

## Verification record

Commands executed against the reconstructed source:

- Release build of `Baccarat.slnx`: **passed**, zero warnings and zero errors.
- Core checks: **passed**.
- Headless Avalonia checks: **passed**.
- Static-analysis Python tests: **passed**, 5 tests.
- NuGet direct/transitive vulnerability audit: **passed**, no known vulnerable packages reported.
- GitHub Actions run `38328513478`: **passed** on `macos-latest` and `windows-latest`; both jobs built the solution and passed the Core and headless Desktop checks.

## Known limits and next inputs

- Exact authentication and WebSocket contracts remain blocked by protected runtime-restored code.
- No endpoint, request field or response schema has been guessed.
- The current secret store is intentionally volatile. Windows Credential Manager and macOS Keychain adapters are future delivery work.
- macOS runtime testing requires a Mac.
- Stage 5 network implementation requires sanitized evidence described in `docs/network-evidence-capture.md`.


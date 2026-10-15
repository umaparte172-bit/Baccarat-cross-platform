# Application architecture

The replacement is organized around verified behavior and replaceable external adapters. Core logic does not depend on Avalonia, Windows APIs, browser drivers or an assumed server schema.

## Version 1 composition (2026-10-15)

The latest client scope supersedes the historical authentication design below.
`ApplicationServices` now composes only simulation, loopback communication,
redacted diagnostics and disconnected network options. Startup opens Home directly;
it does not construct an authentication service, account view or secret store.
Authentication components remain isolated historical/reference code, not a V1 gate.
Closing the workspace cancels pending simulation approvals and disables auto-bet.

## Projects

### `Baccarat.Core`

Platform-independent application rules:

- `Session` records Player, Banker and Tie outcomes and produces CSV.
- `StrategyDefinition` and its validator represent the recovered declarative strategy schema without depending on JSON or the desktop.
- `StrategySessionEngine` produces deterministic recommendations, settles observed outcomes, applies progression and limits, and supports exact-state undo/reset/export.
- `AuthenticationContracts` defines sign-in and registration input, normalized outcomes, authenticated sessions and the session-store boundary.
- `AuthenticationController` validates input, serializes authentication state transitions, persists valid sessions, rejects expired/malformed sessions and converts service failures into controlled results.
- `ViewerDocumentContract` defines the version 1.0 envelope and independently versioned session, strategy and communication-status documents.
- `ViewerDocumentStore` validates documents, accepts only increasing sequences and retains a bounded immutable snapshot of viewer history.
- `AutomationController` serializes initial-bet, auto-bet, approval, chip selection, placement, reconnect and recovery while enforcing exposure limits and no-replay handling for indeterminate outcomes.
- `ChipSelection` creates an exact, minimum-click chip combination within bounded stake and action limits.
- `IDiagnosticSink` and `ISecretStore` define logging and secret-storage boundaries without platform dependencies.
- `NetworkClientOptions` validates disconnected mode and requires HTTPS/WSS when endpoints are eventually supplied.

### `Baccarat.Infrastructure`

Replaceable technical adapters:

- `OfflineAuthenticationService` is the safe default until the original protocol is evidenced.
- `HttpAuthenticationService` provides a TLS-only, same-origin, no-redirect transport with bounded timeouts and normalized failures.
- `IAuthenticationHttpProtocol` isolates the still-unknown legacy routes, headers, encodings and response schema from the transport.
- `SecretAuthenticationSessionStore` serializes session state exclusively through `ISecretStore`; it never stores the user's password.
- `RedactingDiagnosticSink` removes email addresses, passwords, API keys, secrets and bearer/token values before output.
- `VolatileSecretStore` keeps temporary values in memory and never persists credentials to disk.
- `StrategyFileLoader` performs bounded, strict JSON parsing and maps recovered strategy files into validated Core definitions.
- `VersionedViewerDocumentCodec` strictly encodes and decodes viewer JSON, rejecting unknown fields, duplicate properties, unsupported versions, excessive depth and documents larger than 256 KiB.
- `ViewerCommunicationModule` assigns ordered sequence numbers, serializes concurrent publication and receipt, and routes documents through `IViewerMessageTransport` without depending on the desktop UI.
- `LoopbackViewerMessageTransport` is the safe offline transport used today. A future WebSocket transport can implement the same boundary after its legacy contract is evidenced.
- `SimulatedAutomationTableAdapter` is the only enabled automation target. It verifies selected chips, Player/Banker requests and idempotent operation IDs without touching a browser or external account.

Future WebSocket adapters belong here. Windows Credential Manager and macOS Keychain already implement the secret-storage boundary. The recovered HTTP protocol codec will implement the existing protocol boundary without changing session logic or UI state handling.

### `Baccarat.Desktop`

Cross-platform Avalonia presentation:

- `MainWindow` owns top-level navigation.
- `AccountView` owns account input and displays normalized authentication state.
- `StrategyView` imports strategy files, displays recommendations, accepts observed outcomes and exposes manual resolution, undo, reset and export.
- `ViewerView` displays the active protocol/document versions, communication status, current session and strategy snapshots, and bounded document history. Transport callbacks are marshalled onto the Avalonia UI thread.
- `AutomationView` exposes simulation connection, initial-bet preparation, approval, auto-bet control, pause and recovery. Live automation is visibly unavailable.
- The Session tab remains available without a server.
- Password text is cleared after every sign-in or registration attempt and is not persisted.

### Checks

- `Baccarat.Core.Checks` exercises session rules and authentication outcomes without network access.
- `Baccarat.Core.Checks` also exercises viewer round-trips, incompatible-version rejection, duplicate-property rejection, ordered delivery and subscriber isolation.
- `Baccarat.Desktop.Checks` starts Avalonia headlessly and checks navigation, essential controls and live viewer updates.
- Python tests cover static resource extraction and Phase 2 analysis helpers.

## Dependency direction

```text
Baccarat.Desktop  ->  Baccarat.Infrastructure  ->  Baccarat.Core
        |                                                ^
        +------------------------------------------------+

Core defines interfaces; Infrastructure implements them; Desktop composes them.
```

UI code cannot access HTTP or WebSocket APIs directly. Network code cannot update controls directly. This keeps the recovered protocol isolated and testable when Stage 5 evidence becomes available.

## Authentication outcomes

The core represents authenticated, expired, validation-failed, rejected, empty-response, malformed-response, unavailable and unexpected-error states. These are internal application outcomes, not claims about the original server's JSON schema.

The controller guarantees:

- invalid input never reaches a transport;
- unsuccessful results leave no authenticated session;
- null and exceptional service behavior becomes a controlled error;
- cancellation remains cancellation;
- local session state is removed even when remote sign-out fails.
- only non-empty user IDs and access tokens can establish a session;
- expired sessions are rejected and removed from storage;
- passwords are never written to the session store.

## Dependency security

Avalonia packages are pinned to `11.3.22`. That release resolves `Tmds.DBus.Protocol 0.21.3`, which contains the backported fix for CVE-2026-39959. The verified dependency audit reported no known vulnerable direct or transitive packages on 2026-10-13.


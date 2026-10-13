# Application architecture

The replacement is organized around verified behavior and replaceable external adapters. Core logic does not depend on Avalonia, Windows APIs, browser drivers or an assumed server schema.

## Projects

### `Baccarat.Core`

Platform-independent application rules:

- `Session` records Player, Banker and Tie outcomes and produces CSV.
- `AuthenticationContracts` defines sign-in input, normalized outcomes and authenticated session state.
- `AuthenticationController` validates input, serializes sign-in state transitions, clears failed sessions and converts service failures into controlled results.
- `OfflineAuthenticationService` is the safe default until the original protocol is evidenced.

The future HTTP/WebSocket implementation will implement the existing service boundary. It will not require changes to session logic or UI state handling.

### `Baccarat.Desktop`

Cross-platform Avalonia presentation:

- `MainWindow` owns top-level navigation.
- `AccountView` owns account input and displays normalized authentication state.
- The Session tab remains available without a server.
- Password text is cleared after every sign-in attempt and is not persisted.

### Checks

- `Baccarat.Core.Checks` exercises session rules and authentication outcomes without network access.
- `Baccarat.Desktop.Checks` starts Avalonia headlessly and checks navigation and essential controls.
- Python tests cover static resource extraction and Phase 2 analysis helpers.

## Dependency direction

```text
Baccarat.Desktop  ->  Baccarat.Core  ->  IAuthenticationService
                                              ^
                                              |
                         offline adapter now / recovered network adapter later
```

UI code cannot access HTTP or WebSocket APIs directly. Network code cannot update controls directly. This keeps the recovered protocol isolated and testable when Stage 3 evidence becomes available.

## Authentication outcomes

The core represents authenticated, validation-failed, rejected, empty-response, malformed-response, unavailable and unexpected-error states. These are internal application outcomes, not claims about the original server's JSON schema.

The controller guarantees:

- invalid input never reaches a transport;
- unsuccessful results leave no authenticated session;
- null and exceptional service behavior becomes a controlled error;
- cancellation remains cancellation;
- local session state is removed even when remote sign-out fails.

## Dependency security

Avalonia packages are pinned to `11.3.22`. That release resolves `Tmds.DBus.Protocol 0.21.3`, which contains the backported fix for CVE-2026-39959. The verified dependency audit reported no known vulnerable direct or transitive packages on 2026-10-13.


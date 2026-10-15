# Migration to-do list

Work proceeds in dependency order. Completion means the acceptance criteria have been checked, not merely that a file exists.

## Current client scope and strategy evidence (2026-10-15)

Latest clarification: original Wetalkbet server accounts are required, not local
accounts. This supersedes the no-login scope recorded below. Actual server
authentication is blocked on the decrypted contract and a working service; see
[the integration checkpoint](docs/original-server-auth-checkpoint.md). The current
build starts at the restored account screens, with explicit offline workspace
access. Do not mark real signup/login or all original features complete.

- [ ] Recover actual signup/sign-in requests and response fixtures.
- [x] Restore Sign In/Sign Up entry, top-right Sign Out after authentication and explicit offline access; verify UI with injected success/failure fixtures.
- [ ] Implement and bind the verified original-service protocol; restore the authentication gate with integration tests.
- [ ] Validate real startup token checks, expiry, sign-out and WebSocket authentication.

The earlier no-login V1 scope superseded the original authentication requirements. PATTERN is
the highest-priority strategy; IHG is supporting description and is separate.
See the [PATTERN evidence review](docs/pattern-strategy-evidence.md) for its exact
missing rules and the [IHG reference review](docs/strategy-9-ihg-evidence.md) for
the separately audited document. IHG questions do not block PATTERN recovery.

- [x] Compare the eight resupplied TXT files with prior client files (all identical).
- [x] Confirm with the user that Strategy 9 is IHG.
- [x] Extract and audit 315 IHG worked hands; preserve the source's pause conflict.
- [x] Record the subsequent clarification: prioritize PATTERN separately from IHG.
- [ ] Recover PATTERN side selection and the meaning of its Pattern amount value 2.
- [ ] Implement and verify PATTERN first, using original-app reference results.
- [ ] Confirm numbered Strategies 1-6 against the named strategy files.
- [ ] Obtain IHG tie and simultaneous-limit rules from the client.
- [ ] Resolve exactly-45 and nonpositive-profit consecutive-win boundaries.
- [ ] Integrate IHG only if separately included in the product scope, after its rules are resolved.
- [ ] Recover remaining named selector and amount algorithms; handle installed Ladder.
- [x] Remove the demo authentication gate so V1 opens directly into the workspace, without accessing stored credentials.
- [x] Exercise PATTERN manual resolution through simulation, viewer, undo/reset and exact CSV values; fix premature auto-bet round consumption.
- [ ] Compare priority strategies with original-app demonstrations and client results.

The historical stage checklist below records earlier work; checked manual
fallback support does not mean all original strategy algorithms are recovered.

## 1. Repository and evidence baseline

- [x] Create local Git repository and exclude the original executable, caches and generated binary resources.
- [x] Preserve initial analysis scripts and reports locally.
- [x] Document confirmed findings and correct unsupported conclusions.
- [x] Select .NET 10 LTS + Avalonia as the intended desktop stack.
- [x] Add a reproducible read-only evidence inventory and evidence-to-finding index.

## 2. Static recovery — completed to the protected-image boundary

- [x] Implement bounded embedded-resource extraction with content hashes.
- [x] Run extraction and record results (five resources; CLR flags 0x20003).
- [x] Classify version 2 resources as protected opaque blobs; no ordinary embedded assemblies were demonstrated.
- [x] Trace the version 2 string decoder call sites and startup protection routines using static IL.
- [x] Recover configuration field assignments and decoder inputs; plaintext endpoint values remain protected.
- [x] Inventory supplied installer, installed files, per-user settings and strategy files.
- [x] Recover and document the standalone updater workflow.
- [x] Inventory all form fields, controls and methods; record that event wiring is absent from protected disk stubs.
- [x] Reproduce the evidence-backed SignUp palette, field layout, brand header and actions with native Avalonia controls.
- [x] Reproduce the screenshot-backed SignIn fields, checkboxes, buttons, and SignIn/SignUp navigation.
- [ ] Reproduce the exact protected Main form after sanitized full-window screenshots are supplied from VMware.
- [x] Document recovered behavior with method/token evidence and confidence levels.

Acceptance: reproducible inventory, known limits, and sufficient evidence for each implemented feature.

## 3. Application foundation

- [x] Install an isolated .NET 10.0.401 SDK and create an Avalonia application.
- [x] Add a core project separate from UI.
- [x] Implement offline application navigation and explicit disconnected/error states.
- [x] Implement manual round recording, counts, undo and CSV export.
- [x] Compile and run core checks for records, undo, counts, CSV and invalid input.
- [x] Add mocked authentication covering validation, empty, malformed, unsuccessful and exceptional responses.
- [x] Separate account UI, authentication state and replaceable service boundaries.
- [x] Build and test the initial application, including headless desktop checks.
- [x] Update Avalonia and verify no known vulnerable direct or transitive packages.

## 4. Application architecture

- [x] Separate Core, Infrastructure and Desktop projects with one-way dependencies.
- [x] Add a canonical solution, SDK selection and centralized package versions.
- [x] Enable deterministic builds, nullable analysis and warnings-as-errors.
- [x] Add validated disconnected/HTTPS/WSS network configuration.
- [x] Add secret-storage and diagnostic abstractions.
- [x] Implement volatile secret storage and redacted diagnostic adapters.
- [x] Add a desktop composition root and verify all projects together.
- [x] Audit and document completion of Stages 1–4.

## 5. Verified behavior and network integration

- [x] Define the sanitized VMware network-evidence package required for exact contract recovery.
- [ ] Specify authentication and WebSocket contracts from evidence.
- [x] Implement sign-in and sign-up validation behind a replaceable service boundary.
- [x] Implement authenticated session validation, protected-store persistence, restoration, expiry and sign-out cleanup.
- [x] Implement a TLS-only, same-origin HTTP authentication transport with bounded timeouts and normalized failures.
- [ ] Bind the HTTP transport to the recovered legacy authentication codec and endpoint.
- [ ] Implement WebSocket authentication and reconnection after its contract is recovered.
- [ ] Implement browser workflows once selectors and intended actions are established.
- [x] Recover and implement the supplied strategy workflows, preserving protected algorithms as manual decisions.
- [ ] Recover remaining settings workflows when evidence establishes their behavior.
- [x] Implement redacted diagnostic logging.
- [x] Implement Windows Credential Manager and macOS Keychain storage.

If the old backend is unavailable, record that dependency explicitly. Rebuilding a UI cannot restore an unavailable service.

## 6. Strategy and session engine

- [x] Recover and document the supplied strategy JSON schema.
- [x] Strictly import bounded strategy files and reject unknown or invalid fields.
- [x] Validate all eight supplied client strategy files without modifying or executing the original application.
- [x] Implement deterministic recommendations, Player/Banker settlement, Banker commission and tie pushes.
- [x] Implement set, reset amount, multiply amount, reset sequence and next sequence progression.
- [x] Implement stop-profit, stop-loss, consecutive-loss stop and pause controls.
- [x] Preserve opaque selectors and Grinding/Pattern amount rules through explicit manual resolution instead of guessed algorithms.
- [x] Implement exact-state undo, reset and CSV export.
- [x] Add an interactive, offline Strategies workspace and headless UI checks.

Exact automation for protected selector and amount algorithms remains dependent on additional evidence. Manual resolution keeps those definitions operational without making unsupported compatibility claims.

## 7. Viewer and communication integration

- [x] Define a replacement viewer contract with independently versioned envelopes and payload documents.
- [x] Implement strict JSON encoding/decoding, validation, size/depth limits and duplicate-field rejection.
- [x] Implement ordered delivery, bounded history and duplicate/out-of-order suppression.
- [x] Connect manual session events, strategy state and communication status to the viewer through a replaceable transport.
- [x] Add a live Viewer workspace and verify it headlessly.
- [x] Keep the default transport local and offline so the application performs no unsupported external action.
- [x] Serialize concurrent publications, serialize receive processing and marshal Viewer updates to the UI thread.
- [ ] Bind a WebSocket transport only after the legacy endpoint, framing, authentication and reconnection contract is recovered from sanitized evidence.

The replacement document contract is complete at version 1.0. It is deliberately not presented as the original server protocol. Unknown versions and document types are rejected rather than guessed.

## 8. Controlled automation workflows

- [x] Map recovered `InitialBet`, `AutoBet`, `PlaceBet`, `DummyBet` and `GetChips` workflow names to replacement boundaries without inventing protected algorithms.
- [x] Implement a serialized automation state machine with explicit disconnected, approval, chip-selection, placement, recovery, paused, stopped and faulted states.
- [x] Implement exact bounded chip composition and Player/Banker placement requests.
- [x] Implement separately controlled initial-bet and auto-bet workflows.
- [x] Require explicit approval and enforce per-bet, cumulative-session and chip-action limits.
- [x] Implement bounded reconnect with exponential backoff and recovery into a paused review state.
- [x] Treat lost or missing placement acknowledgements as indeterminate and never replay those bets.
- [x] Cancel stale approvals when strategy state changes and safely handle strategy undo/reset.
- [x] Add an idempotent local simulation adapter and a complete Automation workspace.
- [x] Publish versioned automation state to the Viewer and verify the complete headless UI flow.
- [ ] Implement a live browser/table adapter only after verified selectors, chip denominations, readiness checks and placement acknowledgements are supplied.

The controlled automation workflow is feature-complete in local simulation. No real browser, account or monetary action is enabled by default.

## 9. macOS delivery

- [x] Build and validate self-contained Apple Silicon and Intel artifacts on macOS CI.
- [x] Verify the headless UI workflow and default Keychain read path on a macOS runner.
- [x] Package standard `.app` bundles and preserve their executable permissions in ZIP artifacts.
- [x] Provide fail-closed Developer ID signing, hardened-runtime, notarization and stapling tooling.
- [x] Publish local packaging, signing and validation instructions.
- [ ] Execute signing/notarization and validate an interactive installation when Apple Developer credentials and a test Mac are available.
- [ ] Verify live browser/table integration after the evidence-gated adapter exists.

The unsigned `osx-arm64` and `osx-x64` app bundles are produced by CI. They are development artifacts, not public distribution packages. Actual signing and notarization deliberately require a configured Developer ID identity and `notarytool` Keychain profile; no credentials are stored in the repository. Real service contracts and the live browser/table adapter remain unknown.

## 10. Final production readiness

- [x] Protect Windows sessions with the current user's Credential Manager.
- [x] Reject and remove corrupt persisted authentication sessions.
- [x] Exercise protected-store save, read, UTF-8 round-trip and cleanup on Windows and macOS CI.
- [x] Produce validated Windows x64, macOS Apple Silicon and macOS Intel development packages.
- [x] Publish a SHA-256 sidecar with every package.
- [x] Provide fail-closed Windows Authenticode and macOS Developer ID/notarization hooks.
- [x] Run builds, formatting, core checks, headless UI checks and static-analysis tests across the supported platforms.
- [x] Audit direct and transitive dependencies for known vulnerabilities and deprecations.
- [x] Document the release procedure, security policy, verified capabilities and remaining evidence boundaries.
- [ ] Sign public artifacts and complete interactive acceptance on physical Windows and macOS machines when owner credentials and hardware are available.
- [ ] Enable legacy-service and live-table adapters only after sanitized evidence supplies the missing contracts.

Phase 10 completes the replacement application's evidence-backed implementation and automated release-readiness work. The two open items require external credentials, physical acceptance environments or protocol evidence and are not substituted with guessed behavior.

## macOS 11 compatibility extension

- [x] Preserve the supported .NET 10 packages for macOS 14 and newer.
- [x] Add an isolated .NET 6 compatibility target for macOS 11–13.
- [x] Remove newer framework-only API dependencies without weakening strict JSON or diagnostic protections.
- [x] Build and validate Intel and Apple Silicon legacy app bundles independently.
- [x] Run core, headless UI and Keychain checks against the self-contained legacy runtime in CI.
- [x] Publish separate architecture-specific ZIP files and SHA-256 sidecars.
- [ ] Complete interactive acceptance on the client's macOS 11.7.8 computer.

The legacy runtime is end-of-life and receives no Microsoft security servicing. It is an explicit compatibility exception, not the default package for newer Macs.

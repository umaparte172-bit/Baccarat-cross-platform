# Migration to-do list

Work proceeds in dependency order. Completion means the acceptance criteria have been checked, not merely that a file exists.

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
- [ ] Recover and implement the required strategy/settings workflows.
- [ ] Implement Keychain storage and redacted diagnostic logging.

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

## 7. Viewer/service integration

- [ ] Confirm the Stage 7 integration target and its input/output contract.
- [ ] Connect verified authentication, strategy recommendations and session events through adapters.
- [ ] Keep all external actions disabled until their contracts and safety behavior are tested.

## 8. macOS delivery

- [ ] Build for Apple Silicon and Intel as appropriate.
- [ ] Verify UI, browser integration and Keychain on macOS.
- [ ] Package an app bundle.
- [ ] Sign and notarize when an Apple Developer identity is available.
- [ ] Validate installation and publish usage instructions.

Current constraints: this workspace is Windows; a local .NET SDK is installed under .tools. No macOS runtime testing has occurred. Real service contracts remain unknown. Desktop validation is tracked separately from the passing core checks.

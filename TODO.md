# Migration to-do list

Work proceeds in dependency order. Completion means the acceptance criteria have been checked, not merely that a file exists.

## 1. Repository and evidence baseline

- [x] Create local Git repository and exclude the original executable, caches and generated binary resources.
- [x] Preserve initial analysis scripts and reports locally.
- [x] Document confirmed findings and correct unsupported conclusions.
- [x] Select .NET 10 LTS + Avalonia as the intended desktop stack.

## 2. Static recovery — in progress

- [x] Implement bounded embedded-resource extraction with content hashes.
- [x] Run extraction and record results (five resources; CLR flags 0x20003).
- [ ] Identify resource formats; determine whether dependencies are embedded.
- [ ] Trace the string decoder and startup protection routines using static IL.
- [ ] Recover endpoint configuration where possible; do not invent routes or response schemas.
- [ ] Inventory forms, controls and settings.
- [ ] Document recovered behavior with method/token evidence and confidence levels.

Acceptance: reproducible inventory, known limits, and sufficient evidence for each implemented feature.

## 3. Application foundation

- [x] Install an isolated .NET 10.0.401 SDK and create an Avalonia application.
- [x] Add a core project separate from UI.
- [x] Implement offline application navigation and explicit disconnected/error states.
- [x] Implement manual round recording, counts, undo and CSV export.
- [x] Compile and run core checks for records, undo, counts, CSV and invalid input.
- [ ] Add mocked authentication covering empty, malformed and unsuccessful responses.
- [ ] Build and test the initial application.

## 4. Verified behavior

- [ ] Specify authentication and WebSocket contracts from evidence.
- [ ] Implement authenticated sessions, expiry and reconnection.
- [ ] Implement browser workflows once selectors and intended actions are established.
- [ ] Recover and implement the required strategy/settings workflows.
- [ ] Implement Keychain storage and redacted diagnostic logging.

If the old backend is unavailable, record that dependency explicitly. Rebuilding a UI cannot restore an unavailable service.

## 5. macOS delivery

- [ ] Build for Apple Silicon and Intel as appropriate.
- [ ] Verify UI, browser integration and Keychain on macOS.
- [ ] Package an app bundle.
- [ ] Sign and notarize when an Apple Developer identity is available.
- [ ] Validate installation and publish usage instructions.

Current constraints: this workspace is Windows; a local .NET SDK is installed under .tools. No macOS runtime testing has occurred. Real service contracts remain unknown. Desktop validation is tracked separately from the passing core checks.

# Stage 9 macOS delivery report

Date: 2026-10-13

## Outcome

The replacement application now has a verified macOS delivery path for Apple Silicon and Intel. GitHub-hosted macOS jobs compile and exercise the desktop application, create a standard self-contained `.app` for each architecture, validate its metadata and native executable, preserve bundle permissions in ZIP artifacts, and make those unsigned artifacts available from the workflow run.

This stage does not claim that an unsigned CI artifact is a public release. Developer ID signing, Apple notarization, stapling and Gatekeeper checks are fully scripted but cannot be executed without the owner's Apple Developer certificate and notarization profile. Interactive installation also still requires a test Mac.

## Implemented components

### Keychain-backed sessions

- `MacOsKeychainSecretStore` implements the existing `ISecretStore` boundary with macOS Security framework `SecItemAdd`, `SecItemCopyMatching`, `SecItemUpdate` and `SecItemDelete` calls.
- Items use the generic-password class, application service `com.baccarat.crossplatform`, and the logical secret key as the account.
- Access is serialized, key and value sizes are bounded, UTF-8 decoding is strict, and temporary byte arrays are cleared.
- Missing items return `null`; other OSStatus values fail explicitly rather than silently falling back to insecure storage.
- `SecretStoreFactory` selects Keychain only on macOS. Windows remains on the volatile development store.

### Application packaging

- `tools/package-macos.sh` accepts only `osx-arm64` or `osx-x64`, refuses unsafe or pre-existing bundle outputs, performs a self-contained deterministic Release publish, and creates `Baccarat Workspace.app`.
- The checked-in `Info.plist` defines the executable, bundle identifier, version and minimum macOS version.
- `tools/validate-macos-app.sh` checks plist syntax, identifier, executable permission, expected Mach-O architecture and absence of Windows `.exe` files.
- CI packages the validated bundle with `ditto`, preserving macOS metadata and executable permissions, and uploads separate architecture artifacts.

### Distribution security hook

- `tools/sign-notarize-macos.sh` requires an explicit `APPLE_SIGNING_IDENTITY` and `APPLE_NOTARY_PROFILE`.
- It verifies the identity exists, signs nested Mach-O files inside-out, applies hardened runtime and secure timestamps, and verifies the resulting code signature.
- It submits the ZIP with `xcrun notarytool`, waits for the result, staples and validates the ticket, then runs a Gatekeeper assessment.
- The notarization credential remains in the user's Keychain profile; no certificate, password, token or private key is committed.

## Verification evidence

Local Windows verification after the implementation:

- Release solution build: passed with zero warnings and zero errors.
- Core checks: passed, including platform secret-store selection.
- Shell parser checks for all three macOS scripts: passed.
- Git whitespace checks: passed.

GitHub Actions run `38415039298` on commit `6cdfd05` passed:

- Windows Release build, formatting, core checks and headless desktop checks.
- macOS Release build, formatting, core checks and headless desktop checks.
- `osx-arm64` app creation, validation, ZIP creation and artifact upload.
- `osx-x64` app creation, validation, ZIP creation and artifact upload.
- Python static-analysis tests.

The macOS desktop check creates the default application services and attaches the Account view, so the native Keychain missing-item read path ran successfully in addition to the explicit platform-selection assertion.

## Remaining boundary

The following work is deliberately open:

- Run the signing/notarization script with the owner's Apple Developer identity and inspect Apple's result.
- Install the stapled package on physical Apple Silicon and Intel Macs and perform an interactive UI acceptance pass.
- Validate real browser/table behavior only after the live adapter's selectors, chip values, readiness signals and acknowledgement contract are recovered from sanitized evidence.

No unsupported legacy endpoint, selector or protocol was invented during this stage.

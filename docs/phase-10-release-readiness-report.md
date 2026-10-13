# Phase 10 release-readiness report

Date: 2026-10-14

## Scope and outcome

Phase 10 is the final implementation and automated acceptance stage for the evidence-backed replacement. It closes the remaining Windows secret-storage and packaging gaps, adds native protected-store acceptance coverage, creates verifiable development artifacts for all supported architectures, and documents the controlled release boundary.

It does not invent the protected legacy protocols or enable real-money browser automation. Public signing and interactive installation require the owner's certificates and physical acceptance environments.

## Security completion

- Windows now stores authenticated sessions as generic credentials scoped to the current user. Temporary UTF-8 buffers are cleared, access is serialized, values are bounded to the platform limit, and native errors fail closed.
- macOS continues to use generic-password Keychain items through modern `SecItem` APIs.
- Corrupt stored-session JSON is rejected and deleted.
- CI performs an isolated Unicode save/read/delete round trip against the native protected store on both desktop platforms. A unique key and `finally` cleanup prevent test credentials from persisting.
- The repository security policy defines private reporting, evidence sanitization, credential handling and distribution rules.

## Release completion

- Windows x64, macOS Apple Silicon and macOS Intel are published self-contained.
- Packaging scripts refuse stale output, verify required executables and produce architecture-specific archives.
- Every CI package includes a SHA-256 sidecar suitable for verification after download.
- Windows Authenticode and macOS Developer ID/notarization hooks require credentials already installed in the platform's protected store and fail when configuration is absent.
- Unsigned CI files are explicitly labelled development artifacts rather than releases.

## Automated acceptance matrix

| Gate | Windows | macOS | Linux/static |
|---|---:|---:|---:|
| Release build and formatting | Required | Required | — |
| Core behavior and failure paths | Required | Required | — |
| Headless Avalonia workflow | Required | Required | — |
| Native protected-store round trip | Credential Manager | Keychain | — |
| Native package | `win-x64` | `osx-arm64`, `osx-x64` | — |
| Static-analysis regression tests | — | — | Required |
| SHA-256 artifact sidecar | Required | Required | — |

## Evidence boundary after Phase 10

The replacement is complete for offline session recording, supplied strategy import, deterministic recovered strategy rules, manual decisions for protected algorithms, versioned local viewer communication, controlled simulated automation, protected session persistence and cross-platform development packaging.

The following remain external acceptance gates rather than unfinished guessed code:

1. Legacy HTTP/WebSocket request and response codecs require sanitized captures containing the complete streams and redacted application payloads.
2. A live table adapter requires verified selectors, denominations, readiness signals and placement acknowledgements.
3. Public Windows and macOS artifacts require the owner's code-signing identities; macOS additionally requires Apple notarization.
4. Interactive installation and UI acceptance must be performed on representative physical Windows, Apple Silicon and Intel systems.

No original executable was launched during this work, and no backend, endpoint or protected selector was fabricated.

# macOS 11 compatibility

## Supported package selection

The application has two isolated macOS delivery tracks:

| macOS version | Package track | Runtime | Architectures |
|---|---|---|---|
| 11–13 | Legacy compatibility | Self-contained .NET 6 | Apple Silicon and Intel |
| 14 and newer | Modern | Self-contained .NET 10 | Apple Silicon and Intel |

Use `osx-arm64` on Apple Silicon and `osx-x64` on Intel. The application is self-contained, so the user does not install .NET separately.

## Why two packages are required

Current supported .NET releases do not support macOS 11. Lowering only `LSMinimumSystemVersion` would misrepresent compatibility and still leave an incompatible runtime. The legacy build therefore compiles the shared source for .NET 6 and embeds its final runtime, while the modern build remains on supported .NET 10.

The legacy target keeps the same strict strategy and Viewer JSON checks, authentication boundaries, Keychain adapter, offline Viewer, controlled simulation, and headless checks. Framework APIs introduced after .NET 6 are replaced with local equivalents shared by both targets.

## Security boundary

.NET 6 reached end of support on November 12, 2024. Microsoft no longer provides security fixes for that runtime. The macOS 11 package is therefore a compatibility artifact, not a security-equivalent replacement for the modern package. Upgrade macOS and use the .NET 10 package whenever the hardware permits.

The default application remains offline: legacy service authentication, WebSocket communication and live browser/table automation are not enabled. That boundary must remain in place during compatibility testing.

## Acceptance boundary

CI builds both architectures on a current macOS runner, validates the bundle identifier, declared minimum version, Mach-O deployment target, architecture and executable permissions, and runs the core, headless UI and Keychain checks against a self-contained .NET 6 runtime.

CI cannot reproduce the client's physical macOS 11.7.8 machine. Final acceptance requires launching the matching unsigned development package there and exercising navigation, session recording, strategy import, Viewer updates, simulated automation and Keychain session cleanup. Public delivery still requires Developer ID signing and Apple notarization.

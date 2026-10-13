# Security policy

## Supported code

Security fixes are applied to the current `main` branch. The supplied legacy binaries are analysis inputs, not supported releases, and must never be committed or redistributed from this repository.

## Reporting a vulnerability

Use GitHub's private vulnerability-reporting or security-advisory feature for this repository. Do not open a public issue containing credentials, session tokens, raw packet captures, private endpoints, certificate material or personal data.

Include the affected commit, platform, impact, minimal reproduction and any relevant redacted diagnostics. Replace tokens, cookies, email addresses, phone numbers and account identifiers before attaching evidence.

## Operational boundaries

- The application starts offline and currently enables only the local simulation adapter.
- Legacy authentication, WebSocket and live-table adapters remain disabled until their contracts are established by sanitized evidence.
- Never use a development build with real funds or production credentials.
- Windows sessions use Credential Manager and macOS sessions use Keychain. Storage failures are surfaced; the application does not silently write secrets to ordinary settings.
- CI artifacts are unsigned development packages. Verify their SHA-256 sidecars, but publish only artifacts signed with the platform owner's certificate. macOS releases must also be notarized and stapled.
- The intentionally vulnerable legacy updater is not reproduced. Releases must come from the repository's controlled packaging and signing process.

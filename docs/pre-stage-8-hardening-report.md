# Pre-Stage 8 security and readiness report

Review date: 2026-10-13.

## Outcome

The implementation through Stage 7 was re-audited and hardened before controlled automation work. The original executable was not run. The replacement remains offline by default and contains no recovered or guessed legacy endpoint.

## Remediated findings

- Static resource decompression is limited to 8 MiB. Oversized expansion is rejected and recorded instead of being materialized in memory.
- The evidence inventory no longer depends on `Path.GetRelativePath` and works with Windows PowerShell 5.1 and PowerShell 7.
- Strategy JSON is limited to 64 KiB and 16 levels, rejects unknown and duplicate properties, and validates all supported values.
- Strategy validation safely handles malformed null members and bounds stop-profit, stop-loss and stake values.
- Authentication fields are bounded in both the controller and desktop controls before any future protocol adapter can receive them.
- Viewer documents reject oversized, deeply nested, duplicate, unknown-version and unknown-type input.
- Viewer publication and receive processing are serialized; callbacks are marshalled to the Avalonia UI thread.
- Viewer history capacity and concurrent ordering now have direct regression tests.
- CI verifies formatting and runs the static-analysis security tests in addition to Windows/macOS builds and application checks.

## Verified state

- Release build completes with warnings treated as errors.
- Core and headless desktop checks pass.
- Static-analysis tests pass, including an oversized compressed-payload fixture.
- All eight supplied strategy files pass strict import.
- Current direct and transitive packages report no known vulnerabilities or deprecations.
- Evidence files and generated artifacts remain excluded from Git.

## Stage 8 entry boundary

The codebase is ready to implement controlled automation behind a simulation-first adapter. Recovered workflow names establish feature presence, but exact browser selectors and placement algorithms remain protected.

Stage 8 must require explicit approval, enforce bounded exposure, use idempotent operation identifiers, and never retry a placement whose outcome is unknown. A live adapter must remain absent until verified table-state and acknowledgement evidence is supplied.

Legacy server integration remains a separate evidence-gated task. It still requires sanitized fixtures for endpoints, authentication, WebSocket framing, acknowledgements, heartbeats and reconnection behavior. Stage 8 must not invent these values or enable external actions.

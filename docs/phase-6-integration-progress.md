# Phase 6 integration progress — 2026-10-16

## Implemented in this pass

- Tools opens as a toggleable right-side panel without replacing the current workspace.
  Its selected-session description explains the shared strategy, automation and viewer state.
  Feature selection stays in the same window; closing the panel preserves the current view.
- Martingale exposes Starting stake (units) and Multiply after a loss, defaulting to 1
  and 2. Starting units are multiplied by the base unit; losses use the configured
  multiplier and wins reset to the starting stake. Validation rejects invalid values
  before replacing the active engine. These controls do not invent PATTERN rules.
- Existing saved Martingale drafts containing only the first-side field are migrated
  in memory to the previous default behavior. New settings use the existing archive.
- Rolling back strategy history disarms auto-bet and requires explicit re-enabling.
  Disconnected controllers remain disconnected. Disposed automation views ignore late
  successful/cancelled UI completions.
- Preserved the pre-existing account-menu label edits in the working tree.

## Verification

Release build: zero warnings/errors. Core behavioral checks and desktop headless
checks pass, including persistence, shared Tools state, configurable progression,
sidebar positioning/toggling and explicit re-enabling after history replacement.

## Live delivery remains incomplete

Live operation is a requirement, not replaced by an offline-only scope. The UI
explicitly distinguishes the available simulation from a live connection.
See [recovered network evidence](static-network-recovery-2026-10-16.md): the recovered
account base returned a domain-parking page, not the account service. No credentials
were sent to it. Client code cannot restore its database or operator service.

Historical casino/browser addresses and selector strings are recovered, but their
action mapping, table events, chip values and authoritative placement acknowledgements
remain unverified. No production bet adapter is enabled. Protected method-body recovery
or authorized traffic from a functioning reference installation is still required,
followed by controlled integration tests against a verified service. No original
executable was run on this host in this pass.

Phase 6 is partial. In-flight adapter invalidation and atomic viewer state clearing
also remain review items before enabling a real asynchronous betting adapter.

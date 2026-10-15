# Version 1 workflow ownership and acceptance

Updated 2026-10-15. Latest scope: open and run with no registration, login,
license, activation or commercial features. PATTERN is the highest priority;
IHG is separate supporting material. Earlier authentication stage reports are
historical, not current V1 requirements.

## Working method

For each workflow, inspect supplied evidence and implementation, identify the
normal path and failure boundaries, reproduce issues with a test, fix confirmed
defects, and rerun affected and broader checks. Investigate ordinary technical
choices independently. Keep inferred hypotheses separate from observed original
behavior. Ask only for a specific observation that the available evidence cannot
establish; do not require the client to write a new technical specification.

No original executable was launched on the host. Tests below execute only the
replacement application, headlessly, using local data and simulated adapters.

## Changes and checks in this pass

| Workflow | Implementation / check | Evidence boundary |
| --- | --- | --- |
| Entry and relaunch | Home opens directly; account screens and Sign Out removed; startup does not construct authentication or secret stores | Deliberate client-requested difference from the original |
| Navigation | All four workspaces accessible; no tab bar; centered headings and Back to home retained | Not a claim of exact original Main-form layout |
| Session | Player/Banker/Tie recording, counts, undo, empty-state actions and viewer propagation | In-memory records; native export picker still needs interactive acceptance |
| Strategy | Initial recommendation, loss progression, undo and export availability | Unsupported selectors remain manual |
| PATTERN | Load the supplied configuration fixture; choose initial side at 100; resolve next side/stake; reject negative stake; tie push, Banker settlement, undo, reset and CSV values | Manual fallback only; payouts/ties are replacement defaults, not verified PATTERN rules |
| Simulation | Initial/next placement requires approval; incomplete manual decisions cannot prepare a bet | No browser or real-money placement |
| Viewer | Local versioned session, strategy and automation updates including reset | Replacement protocol, not recovered legacy server messages |
| Close | Pending simulation approval cancelled, auto-bet disabled; new launch starts without credentials | Checked with synchronous local simulation, not a live asynchronous table adapter |

The expanded headless check first reproduced a real defect: auto-bet marked a
round as handled while its PATTERN recommendation was still awaiting manual
input. Entering the side and stake later did not trigger approval. The fix only
marks a round for automatic preparation once the recommendation is Ready.
The same regression check passed after the fix.

Historical authentication components and their core checks remain in source for
reference. They are no longer constructed by V1 startup. Desktop tests now assert
direct entry rather than requiring successful demo registration.

## Verification on this Windows workspace

- Release solution build: passed, zero warnings/errors.
- Core behavioral checks: passed, including imports, settlement, limits,
  communication validation, simulation recovery and historical auth boundaries.
- Headless desktop checks: passed, including the new PATTERN regression.
- Formatting verification: passed.
- Python static-analysis/reference checks: 11 passed.
- Git whitespace validation: passed.

Dependency restore used the existing local package cache. Online NuGet access was
blocked; no fresh vulnerability audit was performed. This pass does not establish
macOS interactive acceptance, native file-dialog behavior, signing/notarization,
exact original strategy parity or live-server compatibility. No new Mac package
or remote CI result is claimed by these checks.

## Next PATTERN investigation, prepared without guessing rules

The TXT fixes the initial amount at 100 and names PATTERN/Pattern with value 2,
but contains no side-selection or amount algorithm. Protected on-disk method
bodies do not resolve those rules. Do not substitute IHG or assume doubling.

When a usable original demonstration or isolated VMware observation is available,
record the next side and stake **before** each result, plus the state immediately
after it. Use these short reset-separated cases to discriminate interpretations:

| Observation case | What it distinguishes |
| --- | --- |
| Fresh start, then P; repeat fresh start, then B | Fixed initial side versus history-dependent selection |
| PP, BB, PPPP, BBBB | Repeated outcomes, progression and repeat-length dependence |
| PB, BP, PBPB, BPBP | Alternating outcomes versus repeating patterns |
| PPBBPP and BBPPBB | Whether a two-by-two pattern has any special meaning |
| Insert T before and after a non-tie result | Whether ties preserve or advance selection, stake and counters |
| Repeat a captured sequence after reset or a new shoe | Reset versus cross-shoe state retention |
| If settings allow it, repeat with amount value 1, 2, 3 | What the Pattern amount parameter actually controls |

These are planned observations, not instructions to place real wagers and not
already executed original-app tests. If original manual replay is unavailable,
annotate naturally observed recording sequences instead. Preserve screenshots or
timestamps and configuration identity with each observation. Convert established
transitions into regression fixtures; keep unresolved branches explicitly manual.
No client decision is needed for the completed no-login and manual-flow fixes.

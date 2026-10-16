# Phase 7 — packaging and acceptance

Phase 7 is the last planned delivery phase, but not evidence that unfinished
Phase 4 strategy recovery or Phase 6 live integration has been completed.

## Delivery implemented

- Tools is a right-side toggle on dashboard, Strategies, Automation, Viewer,
  account and clear-confirmation pages. The current workspace remains underneath.
- CI creates self-contained Apple Silicon and Intel bundles for the modern and
  macOS 11 compatibility tracks, plus a Windows package. Package jobs now depend
  on successful behavioral/static checks; legacy packages also require legacy checks.
- Bundle validation checks every Mach-O dependency's architecture and deployment
  target, the bundle metadata and executable permissions.
- The signing script submits a temporary ZIP, staples and verifies the app, then
  creates the final distribution ZIP and SHA-256 checksum. The final ZIP therefore
  includes the stapled ticket instead of an earlier copy of the app.

## Package selection

Apple Silicon uses `osx-arm64`; Intel uses `osx-x64`. Modern bundles declare macOS
14.0 minimum. The macOS 11 compatibility bundle uses the end-of-support .NET 6
runtime and is not approved here for production live betting. Prefer the modern
track when the machine supports it. See [compatibility details](macos-11-compatibility.md).

GitHub Actions uploads ZIPs with matching `.sha256` files after the package jobs
finish. These CI artifacts are unsigned development builds, not notarized releases.
Do not disable Gatekeeper globally. Verify the checksum and provenance before testing.

## Native release steps still required

On a Mac with the appropriate Developer ID identity and Keychain notary profile:

```bash
bash tools/package-macos.sh osx-arm64 Release artifacts/release/osx-arm64
export APPLE_SIGNING_IDENTITY='Developer ID Application: YOUR ORGANIZATION (TEAMID)'
export APPLE_NOTARY_PROFILE='YOUR_KEYCHAIN_PROFILE'
bash tools/sign-notarize-macos.sh \
  'artifacts/release/osx-arm64/Baccarat Workspace.app' osx-arm64 \
  'artifacts/release/Baccarat-Workspace-osx-arm64.zip'
```

Keep signing keys and account secrets out of source control and chat. The signing
script requires credentials already configured on the Mac; it does not provision them.

## Client Mac acceptance checklist

Record OS version, architecture, commit, ZIP checksum and result for each check:

1. Download the signed ZIP, verify SHA-256, extract with Archive Utility and launch
   from Finder. Verify Gatekeeper accepts the app without security bypasses.
2. Create two sessions. Open/close Tools on every page; switch sessions and ensure
   strategy state, history and Viewer belong to the selected session.
3. Record Player, Banker, Tie and pair flags; verify all roads, Ask previews, Undo
   and New Shoe. Export CSV and compare counts and settlement totals.
4. Apply Martingale start 3, multiplier 2.5, base unit 1: a loss gives 7.5 and a
   following win resets to 3. Restart and confirm saved settings/history persist.
5. Run simulation approval/recovery checks. Undo must disarm auto-bet. This is
   simulation validation, not proof of any live server connection.
6. Exercise import, invalid-file errors, close/reopen, backups, account failure
   messages and Keychain cleanup. Check display scaling and narrow-window layout.
7. Test real account/table connectivity only after a verified service and adapter
   exist. Require confirmed result IDs, deduplication, bet acceptance/rejection,
   disconnect handling and no replay of uncertain placements before live acceptance.

## Outstanding gates

No physical Mac test, Developer ID signing or notarization was performed on the
Windows development host. CI success does not validate macOS 11 hardware or real
casino interaction. Phase 7 remains pending those acceptance results.

Live blockers are documented in [network evidence](static-network-recovery-2026-10-16.md):
the recovered account base returned a parking page, while protected method bodies
leave request/response details and live-table action mapping incomplete. A URL and
selectors are not sufficient to safely submit or reconcile a real bet.

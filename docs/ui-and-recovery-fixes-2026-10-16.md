# UI, strategy review and recovery checkpoint

- GitHub run 38869882198 failed because `lipo -verify_arch ARCH FILE` treated FILE
  as another architecture argument. Fixed to `lipo FILE -verify_arch ARCH`.
  The latest preceding run 38870524993 was cancelled, not a successful build.
- Clear Session now returns to the cleared dashboard and discards that session's
  old Tools/controller context. Cancel preserves data and returns to the dashboard
  with Tools open. Regression checks cover selected-session-only clearing.
- Settings labels/inputs now have bounded widths; Apply/return-to-drafts share a
  row where space permits. The pending-draft message correctly says that the
  current engine still uses previously applied settings. Applying settings refreshes
  the controls as well as the recommendation.
- Reused the existing product logo as the window icon; Mac packaging creates an
  ICNS with Apple tooling and declares it in Info.plist. This does not add a Windows
  Explorer EXE resource icon. Mac icon generation needs CI/native validation.
- Rechecked video frame 00:65: Martingale T,B,T,B produces balance 1000.90 with
  base unit/start 1, multiplier 2, initial Player. The existing M2 behavioral fixture
  covers this. A different starting side/stake or an earlier applied run cannot be
  compared using only the final balance. Full strategy parity is still incomplete:
  PATTERN transitions and the other unfinished modes were not invented in this pass.
- Deeper static recovery now passes the former local-address boundary and stops
  at a native allocation wrapper after 94 steps. See the detailed recovery report.

No original executable was run and no production betting connection was enabled.

# Baccarat macOS migration

## Current direction — 2026-10-16

The application is modeled on the supplied friend's-app video. The latest follow-up
also requests account and live-table connectivity; those remain under recovery.
The decoded old API currently serves a domain-parking page. See the
[new static recovery evidence](docs/static-network-recovery-2026-10-16.md). The new
[Phase 1 video specification](docs/video-reference-phase-1.md) contains the screen
inventory, 21 recovered written cases, 10 observed histories, contradictions and
the Phase 2 dashboard handoff. This is a new seven-phase plan; earlier stage
reports below describe the existing foundation, not completion of this new target.
The [Phase 2 dashboard](docs/video-dashboard-phase-2.md) is now the default entry:
create independent local sessions, record results/pairs, undo, export CSV and
edit strategy settings. **Tools** now navigates inside the same window to the
existing strategy, automation, viewer and account features. The dashboard and
tools share the selected session and its existing strategy engine; the separate
legacy-window approach has been removed. See [the integration correction](docs/integrated-tools.md).
[Phase 3](docs/video-phase-3-persistence.md) adds shoe boundaries, ledger statistics
and local autosave/backup recovery, preserving imported strategies and manual decisions
across restarts. [Phase 5](docs/video-phase-5-roadmaps.md) implements all five
current-shoe roadmaps and Ask previews. Unrecovered strategy algorithms remain pending.

## Existing build and historical migration records

Local project for investigating the supplied Windows executable and rebuilding verified behavior for Windows and macOS. No original source code is available. The first desktop foundation is implemented; original-service integration is pending. See [running instructions](docs/running.md).

The retained account workflow starts at Sign In, with Sign Up available there. Successful
authentication opens the workspace with Sign Out at the upper-right. Original
Wetalkbet server integration is still unavailable; an explicit Open Offline
Workspace action preserves access to local tools without pretending to authenticate.
See [the integration checkpoint](docs/original-server-auth-checkpoint.md).
See [the workflow acceptance record](docs/v1-workflow-acceptance.md) for current coverage and evidence gaps.

Start with [TODO.md](TODO.md) and [analysis findings](docs/findings.md).

The completed evidence baseline is documented in [the Phase 1 evidence index](docs/phase-1-evidence-index.md). Regenerate its ignored file manifest with `tools/inventory-original.ps1`; the tool hashes and inspects files without launching supplied programs.

[The Phase 2 static recovery report](docs/phase-2-static-recovery.md) records the recovered form structure, startup branch, protected method tokens, configuration-decoder inputs and resource classification.

Exact Stage 3 network compatibility requires the sanitized evidence described in [the VMware network-capture guide](docs/network-evidence-capture.md). Raw captures and credentials must not be committed.

The verified project boundaries and dependency direction are described in [the application architecture](docs/architecture.md).

The cumulative implementation status through Stage 4 is recorded in [the architecture and audit report](docs/stage-4-audit-report.md).

Stage 5 authentication progress and the exact remaining evidence boundary are recorded in [the authentication implementation report](docs/stage-5-authentication-report.md).

The completed offline strategy/session engine and its protected-algorithm boundary are documented in [the Stage 6 strategy report](docs/stage-6-strategy-session-report.md).

The earlier account-screen clarification preserved those screens and prioritized
[PATTERN](docs/pattern-strategy-evidence.md). IHG is supporting description and is
separate from PATTERN. The [IHG reference review](docs/strategy-9-ihg-evidence.md)
records its 315 worked hands and unresolved document questions. The current build
restores the account screens and retains manual fallbacks for PATTERN; automatic PATTERN parity
has not yet been established.

The live versioned viewer, communication boundary and compatibility rules are documented in [the Stage 7 integration report](docs/stage-7-viewer-communication-report.md).

The security hardening performed before controlled automation is recorded in [the pre-Stage 8 readiness report](docs/pre-stage-8-hardening-report.md).

The feature-complete simulated automation workflow and live-adapter evidence boundary are documented in [the Stage 8 automation report](docs/stage-8-automation-report.md).

The macOS Keychain implementation, dual-architecture app packaging, validation evidence and remaining distribution boundary are documented in [the Stage 9 delivery report](docs/stage-9-macos-delivery-report.md).

The final cross-platform security, packaging and acceptance status is documented in [the Phase 10 release-readiness report](docs/phase-10-release-readiness-report.md). See [the security policy](SECURITY.md) before supplying captures or publishing builds.

macOS 11–13 use a separately isolated legacy package, while macOS 14 and newer use the supported .NET 10 package. The compatibility and security boundaries are documented in [the macOS 11 compatibility guide](docs/macos-11-compatibility.md).

The desktop now uses the recovered navy/gold WETALKBET visual language and an evidence-matched registration form. Exact Main-form parity remains dependent on sanitized screenshots from the isolated original environment; see [the UI recovery record](docs/legacy-ui-recovery.md).

## Intended stack

.NET 10 LTS and Avalonia for the desktop UI; Playwright for browser interactions; HttpClient, ClientWebSocket and System.Text.Json for verified service contracts. Secrets belong in Windows Credential Manager or macOS Keychain. Choose exact package versions when creating the application scaffold.

.NET 10 replaces the earlier .NET 8 recommendation because .NET 8 support ends in November 2026. See https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core.

## Static analysis

Requires Python 3.12+ and uv. Put the original executable in this directory; it is intentionally excluded from Git along with generated binary artifacts.

```powershell
uv run --with dnfile==0.18.0 --with pefile==2024.8.26 python analyze_exe.py 'Baccarat (Evolution).exe'
uv run --with dnfile==0.18.0 --with pefile==2024.8.26 --with dncil==1.0.2 python dump_il.py 'Baccarat (Evolution).exe'
uv run --with dnfile==0.18.0 --with pefile==2024.8.26 python tools/extract_resources.py 'Baccarat (Evolution).exe'
```

Resource extraction only reads PE bytes. It never loads the assembly into the CLR. Resources are saved under artifacts/resources using numbered filenames and recorded with hashes in a manifest. Extraction refuses to overwrite an existing output directory.

The previous raw reports are retained locally. They are preliminary and may include parser errors or protected placeholder methods. They are not a recovered source tree or verified protocol specification.

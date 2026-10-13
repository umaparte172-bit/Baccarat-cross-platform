# Baccarat macOS migration

Local project for investigating the supplied Windows executable and rebuilding verified behavior for Windows and macOS. No original source code is available. The first desktop foundation is implemented; original-service integration is pending. See [running instructions](docs/running.md).

Start with [TODO.md](TODO.md) and [analysis findings](docs/findings.md).

The completed evidence baseline is documented in [the Phase 1 evidence index](docs/phase-1-evidence-index.md). Regenerate its ignored file manifest with `tools/inventory-original.ps1`; the tool hashes and inspects files without launching supplied programs.

[The Phase 2 static recovery report](docs/phase-2-static-recovery.md) records the recovered form structure, startup branch, protected method tokens, configuration-decoder inputs and resource classification.

Exact Stage 3 network compatibility requires the sanitized evidence described in [the VMware network-capture guide](docs/network-evidence-capture.md). Raw captures and credentials must not be committed.

The verified project boundaries and dependency direction are described in [the application architecture](docs/architecture.md).

The cumulative implementation status through Stage 4 is recorded in [the architecture and audit report](docs/stage-4-audit-report.md).

Stage 5 authentication progress and the exact remaining evidence boundary are recorded in [the authentication implementation report](docs/stage-5-authentication-report.md).

The completed offline strategy/session engine and its protected-algorithm boundary are documented in [the Stage 6 strategy report](docs/stage-6-strategy-session-report.md).

The live versioned viewer, communication boundary and compatibility rules are documented in [the Stage 7 integration report](docs/stage-7-viewer-communication-report.md).

The security hardening performed before controlled automation is recorded in [the pre-Stage 8 readiness report](docs/pre-stage-8-hardening-report.md).

The feature-complete simulated automation workflow and live-adapter evidence boundary are documented in [the Stage 8 automation report](docs/stage-8-automation-report.md).

The macOS Keychain implementation, dual-architecture app packaging, validation evidence and remaining distribution boundary are documented in [the Stage 9 delivery report](docs/stage-9-macos-delivery-report.md).

## Intended stack

.NET 10 LTS and Avalonia for the desktop UI; Playwright for browser interactions; HttpClient, ClientWebSocket and System.Text.Json for verified service contracts. Secrets belong in macOS Keychain. Choose exact package versions when creating the application scaffold.

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

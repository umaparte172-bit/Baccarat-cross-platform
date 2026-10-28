# Phase 1 evidence index

Phase completed: 2026-10-13. This phase was static and read-only. No supplied executable, installer, uninstaller, updater, ChromeDriver or batch file was launched.

## Evidence boundaries

The evidence root is the ignored `Original Application/` directory. It contains four user-supplied groups:

1. `Baccarat(Evolution).exe` — version 2 setup package.
2. `Wetalkbet (CProgram Files (x86))/` — installed program directory.
3. `Wetalkbet (CUsersElioAppDataLocal)/` — per-user configuration snapshots.
4. `strategies/` — client-provided strategy variants.

The reproducible inventory contains **49 files** totaling **54,540,580 bytes**. Run `analysis/tools/inventory-original.ps1` from Windows PowerShell 5.1 or PowerShell 7 to regenerate the ignored `artifacts/phase1-evidence-inventory.json` manifest. The manifest records normalized relative paths, sizes, SHA-256 hashes, extensions, Authenticode status and available version-resource metadata.

## Primary artifacts

| Artifact | SHA-256 | Role | Trust observation |
|---|---|---|---|
| Setup `Baccarat(Evolution).exe` | `5DF5A1AC8AEEAAAD4D683ABA9D0CF90050CF347615842D349BCFCCD357EA7824` | Inno Setup package | Unsigned |
| Installed `Baccarat (Evolution).exe` | `B051ADE2E179154614BD566ADC35C5C3672A04F899B891FEBAFBF0CD4045ECB1` | Main version 2 client | Unsigned; protected .NET Framework application |
| `Updater.exe` | `B922D8740243F80A7E66C3D1116203C6D4765863A4E488A6BAF5CCCC7203278F` | Standalone updater | Unsigned |
| `unins000.exe` | `9CDA256A755F970AD4BE914B5A6AD24E882EE174FB5411EDB56DA3BF543E194A` | Inno Setup uninstaller | Unsigned |
| Root `chromedriver.exe` | `37C78987CD4BD112F15A22E22CF42ECF315DC4179B4AC8332E3B0A9D167A4D91` | Browser driver | Unsigned |
| `Drivers/chromedriver.exe` | `0111588C7828F874B2E20AC0C835860BD7809AF83F55B84704CF7386F07D278E` | Newer browser driver | Unsigned |

## Evidence-to-finding map

| Finding | Direct evidence | Confidence |
|---|---|---|
| Main client uses .NET Framework 4.7.2 and requires a 32-bit CLR | Main executable configuration, CLR header flags `0x20003`, assembly references | Confirmed |
| Client integrates with HTTP and WebSocket services | `Baccarat.Authentication`, `Baccarat.WS_Authentication`, HttpClient/WebSocket4Net references and configuration fields | Confirmed |
| Client performs browser automation | Selenium, two ChromeDrivers, WindowsInput, batch files and recovered browser/element method names | Confirmed |
| Strategies are declarative JSON data | Seventeen strategy text files and their parsed property structure | Confirmed |
| Updater downloads and executes replacement application files | Recovered updater IL for `version.txt`, `package.zip`, extraction, copy and relaunch | Confirmed |
| Updater lacks a recovered package signature or hash check | Full recovered updater method bodies | High confidence |
| Exact service hosts and message schemas are known | Protected configuration and method bodies prevent recovery in Phase 1 | **Not established** |
| Supplied program is malware | Static evidence is insufficient for that conclusion | **Not established** |

## Generated analysis artifacts

Generated files are intentionally ignored by Git because they can be large or derived from proprietary binaries:

- `artifacts/phase1-evidence-inventory.json` — current reproducible file manifest.
- `artifacts/full-analysis-main-v2.json` — main-client metadata report.
- `artifacts/full-analysis-updater.json` and `full-analysis-updater-il.txt` — updater metadata and IL.
- `artifacts/resources/manifest.json` and numbered resource blobs — bounded resource extraction.
- `analysis/docs/original-application-analysis.md` — consolidated interpretation and security assessment.

## Acceptance record

- Every supplied file is represented by path, size and SHA-256: **passed**.
- Primary executable identity and signature state are recorded: **passed**.
- Installed, per-user and client-strategy evidence are separated: **passed**.
- Findings distinguish confirmed facts from hypotheses and unknowns: **passed**.
- Original binaries are excluded from Git and were not modified: **passed**.
- Inventory can be regenerated without executing supplied code: **passed**.

Phase 1 is complete. Phase 2 begins with resource-format identification, protected string/configuration recovery, and a complete form/control/event inventory.


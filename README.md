# TRADE247-V1 workspace

The product is in **[`trade247/`](trade247/README.md)**: a local baccarat strategy bot with the client's page as its
interface, shipped as a desktop app (Electron) and as a single-file program for Windows and Apple-silicon macOS.
Start there: how to run it, build it, and what it does and does not do.

| Where | What |
|---|---|
| [`trade247/`](trade247/README.md) | The program: engine, strategies, risk layer, live table, betting, reports, Telegram, backtest, packaging. |
| [`docs/trade247-v1-plan.md`](docs/trade247-v1-plan.md) | The delivery plan and its progress table. |
| [`docs/client-questions.md`](docs/client-questions.md) | What is still needed from the client. |
| [`docs/original-app-reverse-engineering.md`](docs/original-app-reverse-engineering.md) | What the original Windows application (Baccarat (Evolution) 2.0) tells us, from static analysis only. |
| `docs/` (the rest) | Evidence records: the original application's analysis, the recovered strings and network findings, the IHG and PATTERN evidence, the video reference cases. |
| `tools/`, `tests/`, `analyze_exe.py`, `dump_il.py` | Python tools that analyse the supplied original executable **without running it**, and their tests. They need the original files, which are not in the repository. |

## History

The first attempt was a native C# / .NET (Avalonia) application. Its logic (the strategies, the chip selection, the live
browser adapter) was ported into `trade247/`, and the project was removed. It remains in git history in the tag
**`legacy-csharp`**:

```bash
git worktree add ../legacy legacy-csharp
```

Not carried over, because the client's page and PRD do not use them: the old dashboard with sessions and undo, the road maps
(bead plate, big road, ...), the account and licence screens, and the import of the original `.txt` strategy files.

## Not in the repository

The supplied original application, the video and generated analysis output are local only (`Original Application/`,
`artifacts/`, `.tools/`) and are ignored by git. Never commit them, or any token, password or capture.

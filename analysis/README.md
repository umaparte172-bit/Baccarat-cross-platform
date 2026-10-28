# Analysis of the original application

Python tools that study the supplied original Windows application (Baccarat (Evolution) 2.0) **without running it**,
their tests, and the records of what they found. None of this is part of the product: the product is in
[`../trade247/`](../trade247/README.md). It is kept as the evidence behind the strategy rules and the questions to the client.

| Where | What |
|---|---|
| `tools/` | Extractors and auditors: static strings, network contract, resources, metadata, the IHG document, the video reference. |
| `tests/` | Unit tests of those tools. Tests that need the original files skip themselves when `../Original Application/` is missing. |
| `docs/` | The findings: application analysis, recovered strings and network evidence, IHG and PATTERN evidence, video reference cases. |
| `analyze_exe.py`, `dump_il.py` | Small helpers that print the structure and the IL of the executable. |
| `requirements-*.txt` | Python packages the tools need. |

The summary written for the project is [`../docs/original-app-reverse-engineering.md`](../docs/original-app-reverse-engineering.md).

## Running

Run everything from this folder (the tests import `tools`):

```bash
pip install -r requirements-analysis.txt
python -m unittest discover -s tests
```

The original executable, its output (`../artifacts/`) and the toolchain (`../.tools/`) are local only and ignored by git.

"""Extract and audit IHG's worked examples without running a betting application.

Requires pypdf in the analysis environment. Full extracted rows are written only
when an explicit --output path is supplied; keep that output outside tracked files.
This audits the supplied document, not the original executable or profitability.
"""

import argparse
import hashlib
import json
import re
from collections import Counter
from decimal import Decimal, ROUND_CEILING
from pathlib import Path


ROW = re.compile(
    r"^(\d+)\s+(\d+)\s+([PB])\s+(Follow|Streak|Chop|2-2)\s+"
    r"([PB])\s+([WL])\s+(\d+)\s+(\d+)\s+"
    r"(-?\d+(?:\.\d+)?)\s+(-?\d+(?:\.\d+)?)\s+([01])\s+"
    r"(-?\d+(?:\.\d+)?)$"
)


def extract_rows(pages):
    rows = []
    example = None
    for page_number, text in enumerate(pages, 1):
        for line in text.splitlines():
            heading = re.fullmatch(r"Example\s+([1-4]):", line.strip())
            if heading:
                example = int(heading[1])
            match = ROW.fullmatch(line.strip())
            if not match:
                if re.match(r"^\d+\s+\d+\s+[PBT]\b", line.strip()):
                    raise ValueError(f"Unrecognized reference row on page {page_number}")
                continue
            if example is None:
                raise ValueError("Reference row appears before an example heading")
            values = match.groups()
            rows.append(dict(zip(
                ("example", "page", "shoe", "hand", "outcome", "pattern", "side",
                 "result", "single_wins", "stake", "cash", "run_net", "closed", "net"),
                (example, page_number, int(values[0]), int(values[1]), *values[2:6],
                 int(values[6]), int(values[7]), values[8], values[9], int(values[10]), values[11]),
            )))
    keys = [(r["example"], r["shoe"], r["hand"]) for r in rows]
    if len(keys) != len(set(keys)):
        raise ValueError("Duplicate reference row")
    return rows


def location(row):
    return f"Example {row['example']}, shoe {row['shoe']}, hand {row['hand']} (page {row['page']})"


def audit(rows):
    arithmetic = []
    progression = []
    selection = []
    real_after_seventh = []
    threshold_bets = []
    previous = None
    history = []
    for row in rows:
        where = location(row)
        cash, run_net, net = (Decimal(row[key]) for key in ("cash", "run_net", "net"))
        won = row["outcome"] == row["side"]
        if won != (row["result"] == "W"):
            arithmetic.append(f"{where}: side/outcome disagree with W/L")
        expected_cash = Decimal(row["stake"]) * (
            (Decimal("0.95") if row["side"] == "B" else Decimal(1)) if won else Decimal(-1)
        )
        if cash != expected_cash:
            arithmetic.append(f"{where}: cash {cash}, calculated {expected_cash}")

        if previous is not None and previous["example"] == row["example"]:
            if net != Decimal(previous["net"]) + cash:
                arithmetic.append(f"{where}: cumulative net does not reconcile")
            opening = Decimal(0) if previous["closed"] else Decimal(previous["run_net"])
            if run_net != opening + cash:
                arithmetic.append(f"{where}: run net does not reconcile")

            # Check next-stake rules only where the table records a real bet.
            if row["stake"] > 0:
                expected_stake = 1
                if previous["stake"] > 0 and not previous["closed"] and previous["result"] == "W":
                    debt = max(Decimal(0), -Decimal(previous["run_net"]))
                    expected_stake = int(debt.to_integral_value(rounding=ROUND_CEILING)) + 1
                if row["stake"] != expected_stake:
                    progression.append(f"{where}: stake {row['stake']}, calculated {expected_stake}")
                if previous["single_wins"] >= 7:
                    real_after_seventh.append(where)

            # Pattern is recorded AFTER the outcome in the PDF. The first rows of
            # later excerpts omit prior history; skip checks until two are known.
            if len(history) >= 2:
                if previous["pattern"] == "2-2":
                    expected_side = ("B" if history[-1] == "P" else "P") if history[-1] == history[-2] else history[-1]
                else:
                    expected_side = history[-2]
                if row["side"] != expected_side:
                    selection.append(f"{where}: side {row['side']}, calculated {expected_side}")
        else:
            history = []

        if row["stake"] >= 45:
            threshold_bets.append({"location": where, "stake": row["stake"], "closed": bool(row["closed"])})
        history.append(row["outcome"])
        previous = row

    return {
        "row_count": len(rows),
        "rows_per_example": dict(Counter(row["example"] for row in rows)),
        "arithmetic_issues": arithmetic,
        "adjacent_stake_rule_issues": progression,
        "adjacent_selection_rule_issues": selection,
        "real_bets_after_previous_row_reached_seven_single_wins": real_after_seventh,
        "bets_at_or_above_45": threshold_bets,
        "tie_rows": sum(row["outcome"] == "T" for row in rows),
        "boundary_45_rows": sum(row["stake"] == 45 for row in rows),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("pdf", type=Path)
    parser.add_argument("--output", type=Path, help="Local JSON evidence output, such as artifacts/ihg-review/reference.json")
    args = parser.parse_args()
    from pypdf import PdfReader

    reader = PdfReader(args.pdf)
    rows = extract_rows([page.extract_text() or "" for page in reader.pages])
    if not rows:
        raise ValueError("No IHG reference rows found; inspect the PDF visually")
    summary = {
        "source_sha256": hashlib.sha256(args.pdf.read_bytes()).hexdigest(),
        "pages": len(reader.pages),
        **audit(rows),
    }
    print(json.dumps(summary, indent=2))
    if args.output:
        if args.output.resolve() == args.pdf.resolve():
            raise ValueError("Output must not overwrite the source PDF")
        with args.output.open("x", encoding="utf-8") as output:
            json.dump({"summary": summary, "rows": rows}, output, indent=2)
            output.write("\n")


if __name__ == "__main__":
    main()

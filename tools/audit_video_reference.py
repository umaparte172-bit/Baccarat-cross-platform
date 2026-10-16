"""Check transcribed video ledger arithmetic, NOT strategy implementation parity."""

import json
from decimal import Decimal
from pathlib import Path
import re


def settle(outcome, bet):
    if outcome not in "PBT" or len(outcome) != 1:
        raise ValueError("Invalid outcome")
    if bet == "N":
        return Decimal(0)
    if not isinstance(bet, str) or not re.fullmatch(r"[PB][0-9]+(?:\.[0-9]+)?", bet):
        raise ValueError("Invalid bet")
    stake = Decimal(bet[1:])
    if stake <= 0:
        raise ValueError("Stake must be positive")
    if outcome == "T":
        return Decimal(0)
    if outcome != bet[0]:
        return -stake
    return stake * (Decimal("0.95") if outcome == "B" else Decimal(1))


def audit(data):
    start = Decimal(data["defaults"]["starting_bankroll"])
    result = {"document_consistent": [], "document_conflicts": [],
              "incomplete": [], "observed_consistent": [], "observed_conflicts": []}
    ids = set()
    for case in data["document_cases"] + data["observed_histories"]:
        if case["id"] in ids:
            raise ValueError("Duplicate case id")
        ids.add(case["id"])
    for case in data["document_cases"]:
        if case["initial"] is None or case["next"] is None:
            result["incomplete"].append(case["id"])
            continue
        if len(case["outcomes"]) != len(case["next"]):
            raise ValueError("Outcome/recommendation length mismatch")
        # Validate final recommendation too, although it has not been wagered.
        settle("T", case["next"][-1])
        bets = [case["initial"]] + case["next"][:-1]
        balance = start + sum((settle(o, b) for o, b in zip(case["outcomes"], bets)), Decimal(0))
        if balance == Decimal(case["balance"]):
            result["document_consistent"].append(case["id"])
        else:
            result["document_conflicts"].append({"id": case["id"], "computed": str(balance), "stated": case["balance"]})
    for case in data["observed_histories"]:
        if not len(case["outcomes"]) == len(case["bets"]) == len(case["balances"]):
            raise ValueError("Observed history length mismatch")
        settle("T", case["next"])
        balance = start
        issues = []
        for hand, (outcome, bet, expected) in enumerate(zip(case["outcomes"], case["bets"], case["balances"]), 1):
            balance += settle(outcome, bet)
            if balance != Decimal(expected):
                issues.append({"hand": hand, "computed": str(balance), "stated": expected})
        if issues:
            result["observed_conflicts"].append({"id": case["id"], "issues": issues})
        else:
            result["observed_consistent"].append(case["id"])
    return result


if __name__ == "__main__":
    source = Path(__file__).resolve().parents[1] / "docs" / "video-reference-cases.json"
    print(json.dumps(audit(json.loads(source.read_text(encoding="utf-8"))), indent=2))

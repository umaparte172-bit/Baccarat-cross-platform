import copy
import json
from pathlib import Path
import unittest

from tools.audit_video_reference import audit, settle


class VideoReferenceTests(unittest.TestCase):
    def setUp(self):
        self.data = json.loads((Path(__file__).resolve().parents[1] / "docs" /
                                "video-reference-cases.json").read_text(encoding="utf-8"))

    def test_evidence_baseline_preserves_known_conflict(self):
        result = audit(self.data)
        self.assertEqual(len(self.data["document_cases"]), 21)
        self.assertEqual(len(result["document_consistent"]), 19)
        self.assertEqual(result["document_conflicts"],
                         [{"id": "S1", "computed": "998.95", "stated": "994.95"}])
        self.assertEqual(result["incomplete"], ["P1"])
        self.assertEqual(len(result["observed_consistent"]), 10)
        self.assertEqual(result["observed_conflicts"], [])

    def test_wrong_intermediate_balance_is_detected(self):
        self.data["observed_histories"][0]["balances"][0] = "1001"
        self.assertEqual(audit(self.data)["observed_conflicts"][0]["issues"][0]["hand"], 1)

    def test_duplicate_ids_and_truncated_rows_are_rejected(self):
        duplicate = copy.deepcopy(self.data)
        duplicate["document_cases"].append(duplicate["document_cases"][0])
        with self.assertRaises(ValueError):
            audit(duplicate)
        self.data["observed_histories"][0]["bets"].pop()
        with self.assertRaises(ValueError):
            audit(self.data)

    def test_tie_no_bet_and_invalid_inputs(self):
        self.assertEqual(settle("T", "B2"), 0)
        self.assertEqual(settle("B", "N"), 0)
        for outcome, bet in [("X", "P1"), ("P", "P0"), ("B", "BNaN")]:
            with self.assertRaises(ValueError):
                settle(outcome, bet)


if __name__ == "__main__":
    unittest.main()

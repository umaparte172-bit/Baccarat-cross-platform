import unittest

from tools.audit_ihg_reference import audit, extract_rows


class IhgReferenceTests(unittest.TestCase):
    def test_rounding_uses_decimal_deficit_before_adding_unit(self):
        rows = extract_rows([
            "Example 1:\n1 1 B Streak B W 0 1 0.95 -1.05 0 10.95",
            "1 2 B Streak B W 0 3 2.85 1.80 1 13.80",
        ])
        self.assertEqual(rows[1]["page"], 2)
        result = audit(rows)
        self.assertEqual(result["arithmetic_issues"], [])
        self.assertEqual(result["adjacent_stake_rule_issues"], [])
        rows[1]["stake"] = 2
        result = audit(rows)
        self.assertEqual(len(result["arithmetic_issues"]), 1)
        self.assertEqual(len(result["adjacent_stake_rule_issues"]), 1)

    def test_positive_run_closure_resets_run_accounting(self):
        rows = extract_rows(["Example 1:\n1 1 P Follow P W 0 1 1 1 1 1\n1 2 P Streak P W 0 1 1 1 1 2"])
        self.assertEqual(audit(rows)["arithmetic_issues"], [])
        rows[1]["run_net"] = "2"
        rows[1]["net"] = "3"
        self.assertEqual(len(audit(rows)["arithmetic_issues"]), 2)

    def test_boundary_conflict_is_reported_without_correcting_rows(self):
        rows = extract_rows(["Example 3:\n2 1 B Streak P L 7 81 -81 -160 1 -100\n2 2 B Streak B W 7 1 0.95 0.95 1 -99.05"])
        result = audit(rows)
        self.assertEqual(result["bets_at_or_above_45"][0]["stake"], 81)
        self.assertEqual(len(result["real_bets_after_previous_row_reached_seven_single_wins"]), 1)
        self.assertEqual(rows[1]["stake"], 1)

    def test_pattern_labels_describe_state_after_current_outcome(self):
        rows = extract_rows(["Example 1:\n1 1 P Follow P W 0 1 1 1 1 1\n1 2 B Chop P L 0 1 -1 -1 0 0\n1 3 P Chop P W 0 1 1 0 0 1"])
        self.assertEqual(audit(rows)["adjacent_selection_rule_issues"], [])
        rows[2]["side"] = "B"
        self.assertEqual(len(audit(rows)["adjacent_selection_rule_issues"]), 1)

    def test_duplicate_and_unparsed_candidate_rows_are_rejected(self):
        row = "1 1 P Follow P W 0 1 1 1 1 1"
        with self.assertRaises(ValueError):
            extract_rows([f"Example 1:\n{row}\n{row}"])
        with self.assertRaises(ValueError):
            extract_rows(["Example 1:\n1 1 T Follow P UNKNOWN"])


if __name__ == "__main__":
    unittest.main()

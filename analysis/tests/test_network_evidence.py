import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools"))
from recover_network_evidence import recover, InitializerProbe
from recover_static_strings import LocalAddress

SAMPLE = Path(__file__).resolve().parents[2] / "Original Application" / "Wetalkbet (CProgram Files (x86))" / "Baccarat(Evolution)" / "Baccarat (Evolution).exe"


class NetworkEvidenceChecks(unittest.TestCase):
    def test_symbolic_pointer_guards(self):
        probe = InitializerProbe.__new__(InitializerProbe)
        address = LocalAddress({16: 0}, 16)
        stack = [address, 0]
        probe.call("audited.NGAUGjTPlQcjxmgmGw", stack)
        self.assertEqual(stack, [0])
        for stack in ([1234, 0], [address, 8], [LocalAddress({16: 5}, 16), 0]):
            with self.assertRaises(ValueError):
                probe.call("audited.NGAUGjTPlQcjxmgmGw", list(stack))
        with self.assertRaises(ValueError):
            LocalAddress({}, 16)

    @unittest.skipUnless(SAMPLE.exists(), "Private original sample is not included in source control")
    def test_pinned_sample_stops_before_pointer_operations(self):
        report = recover(SAMPLE)
        probe = report["initializer_probe"]
        self.assertEqual(probe["steps"], 94)
        self.assertIn("Unsupported call: dg3ypDAonQcOidMs0w.WP6RZJql8gZrNhVA9v.oK7ThRlrRgiEaXxw3Y", probe["boundary"])
        self.assertEqual(probe["pointer_helper_calls"], ["System.Runtime.InteropServices.Marshal.ReadIntPtr"])
        members = {(r["owner"], r["member"]) for r in report["network_member_references"]}
        self.assertIn(("System.Net.Http.HttpMethod", "get_Post"), members)
        self.assertIn(("OpenQA.Selenium.IWebElement", "Click"), members)
        place = next(r for r in report["placement_state_machines"] if "<PlaceBet>" in r["type"])
        self.assertIn({"name": "_amount", "type": "System.Int32"}, place["fields"])


if __name__ == "__main__":
    unittest.main()

import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
from recover_static_strings import Decoder, signed

SOURCE = ROOT / "Original Application/Wetalkbet (CProgram Files (x86))/Baccarat(Evolution)/Baccarat (Evolution).exe"


class StaticStringChecks(unittest.TestCase):
    def test_integer_wrapping(self):
        self.assertEqual(signed(0xffffffff), -1)
        self.assertEqual(signed(0x100000001), 1)
        self.assertEqual(signed(-2147483649), 2147483647)
        self.assertEqual(signed(1.5), 1.5)

    @unittest.skipUnless(SOURCE.exists(), "Original proprietary sample is intentionally not committed")
    def test_audited_sample_data_recovery(self):
        decoder = Decoder(SOURCE)
        strings, boundary = decoder.recover()
        self.assertEqual(len(strings), 582)
        self.assertEqual(strings[18936], "https://buyer-api.otbx.com/api/")
        self.assertEqual(strings[19002], "wss://buyer-api.otbx.com/ws/products")
        self.assertEqual(strings[274], "sign-in")
        self.assertEqual(strings[450], "sign-up")
        self.assertEqual(strings[22290], "https://www.roobet.com/")
        self.assertEqual(strings[21386], "ws://127.0.0.1:8080")
        self.assertIn("m5rkLafepqV0jHLI0E", boundary)
        self.assertLess(decoder.steps, 10000000)


if __name__ == "__main__":
    unittest.main()

import gzip
import tempfile
import unittest
from pathlib import Path

from tools.classify_resources import MAX_DECOMPRESSED_BYTES, classify, entropy
from tools.phase2_metadata import compressed_uint


class Phase2ToolTests(unittest.TestCase):
    def test_compressed_unsigned_integer_forms(self):
        self.assertEqual(compressed_uint(bytes([0x7F]), 0), (0x7F, 1))
        self.assertEqual(compressed_uint(bytes([0x80, 0xF9]), 0), (0xF9, 2))
        self.assertEqual(compressed_uint(bytes([0xC0, 0x00, 0x40, 0x00]), 0), (0x4000, 4))

    def test_entropy_extremes(self):
        self.assertEqual(entropy(b"\0" * 128), 0.0)
        self.assertGreater(entropy(bytes(range(256))), 7.9)

    def test_gzip_classification_is_bounded(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "sample.bin"
            path.write_bytes(gzip.compress(b"static test payload"))
            result = classify(path)
            self.assertIn("gzip", result["recognized_magic"])
            self.assertEqual(result["standard_decompression"][0]["size"], 19)

    def test_oversized_gzip_is_rejected_without_materializing_output(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "oversized.bin"
            path.write_bytes(gzip.compress(b"A" * (MAX_DECOMPRESSED_BYTES + 1)))
            result = classify(path)
            self.assertEqual(result["standard_decompression"], [])
            self.assertEqual(result["rejected_decompression"][0]["format"], "gzip")
            self.assertEqual(
                result["rejected_decompression"][0]["limit"],
                MAX_DECOMPRESSED_BYTES,
            )


if __name__ == "__main__":
    unittest.main()


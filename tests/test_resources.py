"""Integration checks against the user's local analysis input."""
import hashlib
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('extract_resources', ROOT / 'tools/extract_resources.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
SOURCE = ROOT / 'Baccarat (Evolution).exe'
if not SOURCE.exists():
    SOURCE = ROOT / 'Original Application' / 'Wetalkbet (CProgram Files (x86))' / 'Baccarat(Evolution)' / 'Baccarat (Evolution).exe'


@unittest.skipUnless(SOURCE.exists(), 'Local executable is not distributed with repository')
class ResourceExtractionTests(unittest.TestCase):
    def test_extraction_preserves_input_and_verifies_payloads(self):
        before = hashlib.sha256(SOURCE.read_bytes()).hexdigest()
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / 'resources'
            result = module.extract(SOURCE, output)
            self.assertEqual(result['source_sha256'], before)
            self.assertEqual(len(result['resources']), 5)
            self.assertEqual(json.loads((output / 'manifest.json').read_text()), result)
            for entry in result['resources']:
                blob = (output / entry['file']).read_bytes()
                self.assertEqual(len(blob), entry['size'])
                self.assertEqual(hashlib.sha256(blob).hexdigest(), entry['sha256'])
            sentinel = output / 'sentinel.txt'
            sentinel.write_text('preserve')
            with self.assertRaises(FileExistsError):
                module.extract(SOURCE, output)
            self.assertEqual(sentinel.read_text(), 'preserve')
        self.assertEqual(hashlib.sha256(SOURCE.read_bytes()).hexdigest(), before)

    def test_invalid_input_creates_no_output(self):
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / 'invalid.bin'
            source.write_bytes(b'not a PE file')
            output = Path(directory) / 'output'
            with self.assertRaises(Exception):
                module.extract(source, output)
            self.assertFalse(output.exists())


if __name__ == '__main__':
    unittest.main()

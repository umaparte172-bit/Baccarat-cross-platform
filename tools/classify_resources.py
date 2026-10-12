"""Classify extracted CLR resource blobs without loading or executing them."""
import argparse
import bz2
import gzip
import hashlib
import json
import lzma
import math
import zlib
from collections import Counter
from pathlib import Path


MAGIC = {
    b"MZ": "Windows PE",
    b"PK\x03\x04": "ZIP",
    b"\x1f\x8b": "gzip",
    b"\x89PNG\r\n\x1a\n": "PNG",
    b"{": "possible JSON",
    b"[": "possible JSON",
    b"<?xml": "XML",
    b"\xd0\xcf\x11\xe0": "OLE compound document",
}


def entropy(data: bytes) -> float:
    if not data:
        return 0.0
    counts = Counter(data)
    size = len(data)
    return -sum((count / size) * math.log2(count / size) for count in counts.values())


def classify(path: Path) -> dict:
    data = path.read_bytes()
    detected = [label for prefix, label in MAGIC.items() if data.startswith(prefix)]
    printable = sum(byte in b"\t\n\r" or 32 <= byte <= 126 for byte in data)
    decompression = []
    attempts = {
        "zlib": zlib.decompress,
        "gzip": gzip.decompress,
        "bzip2": bz2.decompress,
        "lzma/xz": lzma.decompress,
    }
    for name, operation in attempts.items():
        try:
            decoded = operation(data)
            decompression.append({
                "format": name,
                "size": len(decoded),
                "sha256": hashlib.sha256(decoded).hexdigest(),
                "prefix_hex": decoded[:16].hex(),
            })
        except (OSError, EOFError, ValueError, zlib.error, lzma.LZMAError):
            pass
    return {
        "file": path.name,
        "size": len(data),
        "sha256": hashlib.sha256(data).hexdigest(),
        "prefix_hex": data[:32].hex(),
        "entropy": round(entropy(data), 4),
        "printable_ratio": round(printable / len(data), 4) if data else 0.0,
        "recognized_magic": detected,
        "standard_decompression": decompression,
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    blobs = sorted(args.directory.glob("*.bin"))
    rendered = json.dumps({"resources": [classify(path) for path in blobs]}, indent=2)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(rendered + "\n", encoding="utf-8")
    else:
        print(rendered)


if __name__ == "__main__":
    main()


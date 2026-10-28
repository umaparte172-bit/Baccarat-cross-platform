"""Classify extracted CLR resource blobs without loading or executing them."""
import argparse
import bz2
import gzip
import hashlib
import io
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

MAX_DECOMPRESSED_BYTES = 8 * 1024 * 1024


class DecompressionLimitExceeded(ValueError):
    """Raised when a resource expands beyond the analysis safety limit."""


def _read_bounded(stream) -> bytes:
    decoded = stream.read(MAX_DECOMPRESSED_BYTES + 1)
    if len(decoded) > MAX_DECOMPRESSED_BYTES:
        raise DecompressionLimitExceeded(
            f"decoded resource exceeds {MAX_DECOMPRESSED_BYTES} bytes"
        )
    return decoded


def _decompress_zlib(data: bytes) -> bytes:
    decoder = zlib.decompressobj()
    decoded = decoder.decompress(data, MAX_DECOMPRESSED_BYTES + 1)
    if len(decoded) > MAX_DECOMPRESSED_BYTES or not decoder.eof:
        raise DecompressionLimitExceeded(
            f"decoded resource exceeds {MAX_DECOMPRESSED_BYTES} bytes or is incomplete"
        )
    return decoded


def _decompress_stream(data: bytes, opener) -> bytes:
    with opener(io.BytesIO(data)) as stream:
        return _read_bounded(stream)


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
    rejected_decompression = []
    attempts = {
        "zlib": _decompress_zlib,
        "gzip": lambda value: _decompress_stream(
            value, lambda stream: gzip.GzipFile(fileobj=stream, mode="rb")
        ),
        "bzip2": lambda value: _decompress_stream(
            value, lambda stream: bz2.BZ2File(stream, mode="rb")
        ),
        "lzma/xz": lambda value: _decompress_stream(
            value, lambda stream: lzma.LZMAFile(stream, mode="rb")
        ),
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
        except DecompressionLimitExceeded as exception:
            rejected_decompression.append({
                "format": name,
                "reason": str(exception),
                "limit": MAX_DECOMPRESSED_BYTES,
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
        "rejected_decompression": rejected_decompression,
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


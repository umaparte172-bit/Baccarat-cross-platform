"""Extract timestamped evidence frames without executing the recorded application."""

import argparse
import hashlib
import json
from pathlib import Path
import subprocess


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("video", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--ffmpeg", type=Path, required=True)
    parser.add_argument("--seconds", type=float, nargs="+", required=True)
    args = parser.parse_args()
    video = args.video.resolve(strict=True)
    ffmpeg = args.ffmpeg.resolve(strict=True)
    if any(not 0 <= second < 86400 for second in args.seconds):
        parser.error("Timestamps must be finite seconds between 0 and 86400.")
    times = sorted(set(args.seconds))
    args.output.mkdir(parents=True, exist_ok=False)
    frames = []
    for second in times:
        name = f"frame-{round(second * 1000):09d}ms.png"
        target = args.output / name
        subprocess.run([
            str(ffmpeg), "-hide_banner", "-loglevel", "error", "-nostdin", "-n",
            "-ss", str(second), "-i", str(video), "-frames:v", "1", str(target),
        ], check=True, timeout=60)
        if not target.is_file():
            raise ValueError(f"No frame at {second} seconds; timestamp may exceed duration")
        frames.append({"requested_seconds": second, "file": name,
                       "sha256": hashlib.sha256(target.read_bytes()).hexdigest()})
    # ffmpeg seeks to the first decoded frame at/after the requested position.
    digest = hashlib.sha256()
    with video.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    with (args.output / "manifest.json").open("x", encoding="utf-8") as stream:
        json.dump({"source_name": video.name, "source_bytes": video.stat().st_size,
                   "source_sha256": digest.hexdigest(), "frames": frames}, stream, indent=2)
        stream.write("\n")
    print(f"Extracted {len(frames)} frames to {args.output}")


if __name__ == "__main__":
    main()

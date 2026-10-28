"""Rebuilds the application icon (.ico) of a Windows executable from its resources, without running it.

    python analysis/tools/extract_icon.py "<path to the .exe>" <output.ico>
"""
import struct
import sys
from pathlib import Path

import pefile

RT_ICON, RT_GROUP_ICON = 3, 14


def resource_data(pe, type_id, name_id=None, lang_id=None):
    for t in pe.DIRECTORY_ENTRY_RESOURCE.entries:
        if t.id != type_id:
            continue
        for n in t.directory.entries:
            if name_id is not None and n.id != name_id:
                continue
            for lang in n.directory.entries:
                if lang_id is not None and lang.id != lang_id:
                    continue
                d = lang.data.struct
                return n.id, pe.get_data(d.OffsetToData, d.Size)
    return None, None


def extract(exe: Path) -> bytes:
    pe = pefile.PE(str(exe), fast_load=False)
    if not hasattr(pe, "DIRECTORY_ENTRY_RESOURCE"):
        raise SystemExit("The file has no resources")
    _, group = resource_data(pe, RT_GROUP_ICON)
    if group is None:
        raise SystemExit("The file has no icon")
    _, count = struct.unpack_from("<HH", group, 2)
    entries, images = [], []
    for i in range(count):
        w, h, colors, _res, planes, bits, size, icon_id = struct.unpack_from("<BBBBHHIH", group, 6 + i * 14)
        _, data = resource_data(pe, RT_ICON, icon_id)
        if data is None:
            continue
        entries.append((w, h, colors, planes, bits, len(data)))
        images.append(data)
    out = bytearray(struct.pack("<HHH", 0, 1, len(images)))
    offset = 6 + 16 * len(images)
    for (w, h, colors, planes, bits, size) in entries:
        out += struct.pack("<BBBBHHII", w, h, colors, 0, planes, bits, size, offset)
        offset += size
    for data in images:
        out += data
    return bytes(out)


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    ico = extract(Path(sys.argv[1]))
    Path(sys.argv[2]).parent.mkdir(parents=True, exist_ok=True)
    Path(sys.argv[2]).write_bytes(ico)
    count = struct.unpack_from("<H", ico, 4)[0]
    sizes = [f"{ico[6 + i * 16] or 256}x{ico[7 + i * 16] or 256}" for i in range(count)]
    print(f"{len(ico)} bytes, {count} images: {', '.join(sizes)}")

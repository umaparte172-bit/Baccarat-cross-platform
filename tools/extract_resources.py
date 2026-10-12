"""Extract length-prefixed CLR resources without executing managed code."""
import argparse
import hashlib
import json
import struct
from pathlib import Path

import dnfile


def extract(source, output):
    data = source.read_bytes()
    pe = dnfile.dnPE(data=data)
    if not pe.net:
        raise ValueError('Input has no CLR metadata')
    rva = pe.net.struct.ResourcesRva
    size = pe.net.struct.ResourcesSize
    if not rva or not size:
        raise ValueError('Input has no embedded CLR resource area')
    area = pe.get_data(rva, size)
    if len(area) != size:
        raise ValueError('Truncated CLR resource area')
    entries = []
    payloads = []
    table = pe.net.mdtables.ManifestResource
    for index, row in enumerate(table.rows if table else []):
        entry = {'name': str(row.Name), 'offset': row.Offset}
        if row.Implementation and row.Implementation.row_index:
            entry['status'] = 'external reference; not extracted'
        else:
            offset = row.Offset
            if offset < 0 or offset + 4 > len(area):
                raise ValueError(f'Resource {index}: invalid header offset')
            length = struct.unpack_from('<I', area, offset)[0]
            end = offset + 4 + length
            if end > len(area):
                raise ValueError(f'Resource {index}: length exceeds CLR resource area')
            blob = area[offset + 4:end]
            filename = f'{index:03d}.bin'
            entry.update(status='extracted', file=filename, size=length,
                         sha256=hashlib.sha256(blob).hexdigest(),
                         prefix_hex=blob[:16].hex())
            payloads.append((filename, blob))
        entries.append(entry)
    manifest = {'source_sha256': hashlib.sha256(data).hexdigest(),
                'clr_flags': hex(pe.net.struct.Flags), 'resources': entries}
    output.mkdir(parents=True, exist_ok=False)
    for filename, blob in payloads:
        (output / filename).write_bytes(blob)
    (output / 'manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    return manifest


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('--output', type=Path, default=Path('artifacts/resources'))
    args = parser.parse_args()
    print(json.dumps(extract(args.source, args.output), indent=2))

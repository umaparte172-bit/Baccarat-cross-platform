import hashlib
import json
import re
import sys
from pathlib import Path

import dnfile
import pefile


def text(value):
    try:
        return str(value)
    except Exception:
        return "<unprintable>"


path = Path(sys.argv[1])
data = path.read_bytes()
pe = pefile.PE(data=data, fast_load=False)
dn = dnfile.dnPE(data=data)

report = {
    "file": str(path),
    "size": len(data),
    "sha256": hashlib.sha256(data).hexdigest(),
    "pe": {
        "machine": hex(pe.FILE_HEADER.Machine),
        "timestamp": pe.FILE_HEADER.TimeDateStamp,
        "characteristics": hex(pe.FILE_HEADER.Characteristics),
        "subsystem": pe.OPTIONAL_HEADER.Subsystem,
        "image_base": hex(pe.OPTIONAL_HEADER.ImageBase),
        "entry_point_rva": hex(pe.OPTIONAL_HEADER.AddressOfEntryPoint),
        "sections": [],
        "imports": {},
    },
    "dotnet": {"assembly_refs": [], "type_refs": [], "types": [], "resources": []},
    "indicators": {},
}

for sec in pe.sections:
    report["pe"]["sections"].append({
        "name": sec.Name.rstrip(b"\0").decode("ascii", "replace"),
        "virtual_size": sec.Misc_VirtualSize,
        "raw_size": sec.SizeOfRawData,
        "entropy": round(sec.get_entropy(), 3),
        "characteristics": hex(sec.Characteristics),
    })

if hasattr(pe, "DIRECTORY_ENTRY_IMPORT"):
    for entry in pe.DIRECTORY_ENTRY_IMPORT:
        dll = entry.dll.decode("ascii", "replace")
        report["pe"]["imports"][dll] = [
            imp.name.decode("ascii", "replace") if imp.name else f"ordinal:{imp.ordinal}"
            for imp in entry.imports
        ]

tables = getattr(getattr(dn, "net", None), "mdtables", None)
if tables:
    if getattr(tables, "AssemblyRef", None):
        for row in tables.AssemblyRef.rows:
            report["dotnet"]["assembly_refs"].append({
                "name": text(row.Name),
                "version": f"{row.MajorVersion}.{row.MinorVersion}.{row.BuildNumber}.{row.RevisionNumber}",
            })
    if getattr(tables, "TypeRef", None):
        for row in tables.TypeRef.rows:
            ns = text(row.TypeNamespace)
            name = text(row.TypeName)
            report["dotnet"]["type_refs"].append(f"{ns}.{name}".strip("."))
    if getattr(tables, "TypeDef", None):
        for row in tables.TypeDef.rows:
            ns = text(row.TypeNamespace)
            name = text(row.TypeName)
            methods = []
            for idx in getattr(row, "MethodList", []) or []:
                target = getattr(idx, "row", None)
                if target is not None:
                    methods.append(text(target.Name))
            report["dotnet"]["types"].append({
                "name": f"{ns}.{name}".strip("."),
                "methods": methods,
            })
    if getattr(tables, "ManifestResource", None):
        for row in tables.ManifestResource.rows:
            report["dotnet"]["resources"].append({
                "name": text(row.Name),
                "offset": row.Offset,
                "flags": text(row.Flags),
            })

ascii_strings = [m.group().decode("ascii", "replace") for m in re.finditer(rb"[ -~]{5,}", data)]
utf16_strings = [m.group().decode("utf-16le", "replace") for m in re.finditer(rb"(?:[ -~]\x00){4,}", data)]
all_strings = sorted(set(ascii_strings + utf16_strings))
patterns = {
    "urls": re.compile(r"(?i)https?://[^\s\"'<>]+"),
    "domains": re.compile(r"(?i)(?:[a-z0-9-]+\.)+(?:com|net|org|io|gg|app|dev|co|me|xyz)\b"),
    "files": re.compile(r"(?i)\b[^\s\"'<>]{1,100}\.(?:dll|exe|json|xml|config|txt|log|db|sqlite|zip|png|ico|dat)\b"),
    "registry": re.compile(r"(?i)(?:HKEY_|SOFTWARE\\|CurrentVersion\\)[^\s\"']*"),
}
for key, rx in patterns.items():
    found = []
    for s in all_strings:
        found.extend(m.group(0) for m in rx.finditer(s))
    report["indicators"][key] = sorted(set(found))[:500]

keywords = re.compile(
    r"(?i)selenium|webdriver|chrome|firefox|edge|websocket|httpclient|authorization|bearer|"
    r"api[_ -]?key|token|licen[cs]e|signin|login|username|password|process\.start|"
    r"cmd\.exe|powershell|download|upload|encrypt|decrypt|random\.org|evolution"
)
report["indicators"]["keyword_strings"] = [s for s in all_strings if keywords.search(s)][:1000]

print(json.dumps(report, indent=2, ensure_ascii=False))

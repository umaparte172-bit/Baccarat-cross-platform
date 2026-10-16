"""Read protected client metadata and literal URL hosts without loading its code."""
import argparse
import hashlib
import json
import re
from pathlib import Path
from urllib.parse import urlsplit

from phase2_metadata import Inspector


def audit(source: Path) -> dict:
    inspector = Inspector(source)
    metadata = inspector.inspect()
    methods = []
    for method in metadata["key_methods"]:
        if method["declaring_type"] in ("Baccarat.Authentication", "Baccarat.WS_Authentication"):
            methods.append({key: method[key] for key in ("declaring_type", "name", "token", "body", "instruction_count")})
    live_methods = []
    decoders = []
    for rid, method in enumerate(inspector.tables.MethodDef.rows, 1):
        _, owner = inspector.method_owner[id(method)]
        owner_name = inspector.full_type_name(owner)
        name = str(method.Name)
        if name == "NASP55YLMI":
            record = inspector.method_record(rid, method)
            decoders.append({key: record[key] for key in ("name", "token", "body", "instruction_count", "calls")})
        if (owner_name == "Baccarat.Global" and any(word in name for word in ("Chrome", "Selenium"))) or (
            any(word in owner_name for word in ("<InitialBet>", "<AutoBet>", "<PlaceBet>", "<UpdateSessionInfo>")) and name == "MoveNext"):
            record = inspector.method_record(rid, method)
            live_methods.append({"type": owner_name, **{key: record[key] for key in ("name", "token", "body", "instruction_count")}})
    hosts = set()
    # Hostnames only: do not print embedded credentials, queries or tokens.
    for encoding in ("latin-1", "utf-16-le"):
        text = inspector.data.decode(encoding, errors="ignore")
        for value in re.findall(r"(?:https?|wss?)://[^\s\x00\"'<>]+", text):
            try:
                host = urlsplit(value).hostname
                if host and re.fullmatch(r"[A-Za-z0-9.-]+", host):
                    hosts.add(host)
            except ValueError:
                pass
    return {"source": str(source), "sha256": hashlib.sha256(inspector.data).hexdigest(),
            "literal_url_hosts": sorted(hosts), "authentication_methods": methods, "live_table_methods": live_methods,
            "configuration_decoders": decoders,
            "scope": "Metadata and URL literals only; absence does not rule out encrypted endpoints. No code executed."}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    args = parser.parse_args()
    print(json.dumps(audit(args.source), indent=2))

"""Read-only network/protection evidence. Never load the sample into a runtime.

Only metadata names and counts are reported; no string-table values, user settings,
embedded credentials, native calls, reflection invocation or network requests.
"""
import argparse
import json
from pathlib import Path

from dncil.cil.body.reader import read_method_body_from_bytes
from recover_static_strings import Decoder, LocalAddress


class InitializerProbe(Decoder):
    """Follow only the audited pure-data prefix; unknown instructions fail closed."""

    def __init__(self, source):
        super().__init__(source)  # Includes the pinned SHA-256 sample check.
        method = self.inspector.tables.MethodDef.rows[0x22b - 1]
        self.instructions = read_method_body_from_bytes(
            self.inspector.pe.get_data(method.Rva, 65536)).instructions
        self.indices = {item.offset: n for n, item in enumerate(self.instructions)}
        # Verified .cctor IL_00B5: ldc.i4.0; stsfld MdswMdu9Vk.
        self.fields["Field:MdswMdu9Vk"] = False
        self.fields["System.IntPtr.Zero"] = 0

    def call(self, full_name, stack):
        # Audited helper bodies are ldnull/ldnull/ceq/ret and ldnull/ret.
        if full_name.endswith(".GZIH0c7xUSHQ3Xq2gN"):
            stack.append(True)
        elif full_name.endswith(".S4oC76rewyHqbQvv5W"):
            stack.append(None)
        elif full_name == "System.IntPtr..ctor":
            if not isinstance(stack[-1], LocalAddress):
                raise ValueError("Non-local pointer construction refused")
        elif full_name.endswith((".tox8LQ33xKEILiFoQX", ".ifwNiDabwmSPtfIQMp", ".svGsOqcmgnaMPk895L")):
            value, offset, address = stack.pop(), stack.pop(), stack.pop()
            if value != 0 or offset != 0 or not isinstance(address, LocalAddress):
                raise ValueError("Native or nonzero pointer write refused")
            address.values[address.index] = 0
        elif full_name.endswith((".NGAUGjTPlQcjxmgmGw", ".tPHFt6vU19Fjsu4up9", ".FdPQHhMJMkIhneGSbe")):
            offset, address = stack.pop(), stack.pop()
            if offset != 0 or not isinstance(address, LocalAddress):
                raise ValueError("Native pointer read refused")
            value = address.values[address.index]
            if not isinstance(value, int) or value != 0:
                raise ValueError("Only audited zero-valued local pointer reads are supported")
            stack.append(value)
        else:
            super().call(full_name, stack)


def recover(source):
    probe = InitializerProbe(source)
    inspector = probe.inspector
    references = []
    for rid, row in enumerate(inspector.tables.MemberRef.rows, 1):
        owner = inspector.coded_type_name(row.Class)
        if any(part in owner for part in ("System.Net.Http", "WebSocket4Net", "OpenQA.Selenium", "Newtonsoft.Json")):
            references.append({"token": f"0x{0x0a000000 + rid:08X}",
                               "owner": owner, "member": str(row.Name)})
    state_machines = []
    for row in inspector.tables.TypeDef.rows:
        name = inspector.full_type_name(row)
        if any(part in name for part in ("<PlaceBet>", "<InitialBet>", "<AutoBet>", "<UpdateSessionInfo>")):
            state_machines.append({"type": name, "fields": [
                {"name": str(field.row.Name), "type": inspector.field_type(field.row)}
                for field in row.FieldList]})
    try:
        probe.run()
        boundary = "completed"
    except (ValueError, KeyError, IndexError, TypeError) as exc:
        boundary = f"{type(exc).__name__}: {exc}"
    # Structural evidence beyond the stop, inspected but not interpreted.
    following = []
    for instruction in probe.instructions:
        if 14703 <= instruction.offset <= 14717:
            following.append({"offset": instruction.offset, "opcode": instruction.opcode.name,
                              "operand": inspector.resolve_token(instruction.operand)})
    helper = inspector.method_record(0x287, inspector.tables.MethodDef.rows[0x287 - 1])
    return {"scope": "Pinned v2 sample, static data only; references are NOT a verified endpoint/action mapping.",
            "network_member_references": references, "placement_state_machines": state_machines,
            "initializer_probe": {"method_token": "0x0600022B", "steps": probe.steps,
                                  "boundary": boundary, "following_instructions": following,
                                  "pointer_helper_calls": helper["calls"]},
            "resources": [{"name": str(r.name), "bytes": len(r.data)} for r in inspector.pe.net.resources],
            "native_imports": [{"library": str(r.ImportScope.row.Name), "name": str(r.ImportName)}
                               for r in inspector.tables.ImplMap.rows]}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    args = parser.parse_args()
    print(json.dumps(recover(args.source), indent=2))

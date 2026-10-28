"""Create a static form/control/method inventory for the protected .NET client."""
import argparse
import hashlib
import json
from pathlib import Path

import dnfile
from dncil.cil.body.reader import read_method_body_from_bytes
from dncil.clr.token import StringToken, Token


ELEMENT_TYPES = {
    0x01: "System.Void", 0x02: "System.Boolean", 0x03: "System.Char",
    0x04: "System.SByte", 0x05: "System.Byte", 0x06: "System.Int16",
    0x07: "System.UInt16", 0x08: "System.Int32", 0x09: "System.UInt32",
    0x0A: "System.Int64", 0x0B: "System.UInt64", 0x0C: "System.Single",
    0x0D: "System.Double", 0x0E: "System.String", 0x18: "System.IntPtr",
    0x19: "System.UIntPtr", 0x1C: "System.Object",
}


def compressed_uint(data: bytes, offset: int) -> tuple[int, int]:
    first = data[offset]
    if first & 0x80 == 0:
        return first, offset + 1
    if first & 0xC0 == 0x80:
        return ((first & 0x3F) << 8) | data[offset + 1], offset + 2
    if first & 0xE0 == 0xC0:
        return ((first & 0x1F) << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3], offset + 4
    raise ValueError("invalid compressed integer")


class Inspector:
    def __init__(self, source: Path):
        self.source = source
        self.data = source.read_bytes()
        self.pe = dnfile.dnPE(data=self.data)
        self.tables = self.pe.net.mdtables
        self.method_owner = {}
        for type_rid, type_row in enumerate(self.tables.TypeDef.rows, 1):
            for method_index in type_row.MethodList:
                self.method_owner[id(method_index.row)] = (type_rid, type_row)

    @staticmethod
    def full_type_name(row) -> str:
        namespace = str(getattr(row, "TypeNamespace", ""))
        name = str(getattr(row, "TypeName", getattr(row, "Name", "<?>")))
        return f"{namespace}.{name}".strip(".")

    def coded_type_name(self, coded) -> str:
        row = getattr(coded, "row", None)
        if row is None:
            return "<?>"
        return self.full_type_name(row)

    def type_from_encoded(self, encoded: int) -> str:
        tag = encoded & 3
        rid = encoded >> 2
        tables = {0: self.tables.TypeDef, 1: self.tables.TypeRef, 2: self.tables.TypeSpec}
        table = tables.get(tag)
        if table is None or rid < 1 or rid > len(table.rows):
            return f"TypeDefOrRef({encoded})"
        return self.full_type_name(table.rows[rid - 1])

    def signature_type(self, data: bytes, offset: int = 1) -> tuple[str, int]:
        element = data[offset]
        offset += 1
        if element in ELEMENT_TYPES:
            return ELEMENT_TYPES[element], offset
        if element in (0x11, 0x12):
            encoded, offset = compressed_uint(data, offset)
            return self.type_from_encoded(encoded), offset
        if element == 0x1D:
            inner, offset = self.signature_type(data, offset)
            return inner + "[]", offset
        if element == 0x0F:
            inner, offset = self.signature_type(data, offset)
            return inner + "*", offset
        return f"element(0x{element:02X})", offset

    def field_type(self, field) -> str:
        try:
            return self.signature_type(field.Signature.value)[0]
        except Exception as exc:
            return f"<signature error: {exc}>"

    def resolve_token(self, operand) -> str:
        if isinstance(operand, StringToken):
            try:
                return repr(str(self.pe.net.user_strings.get(operand.rid)))
            except Exception:
                return str(operand)
        if not isinstance(operand, Token):
            return str(operand)
        try:
            table = self.pe.net.mdtables.tables[operand.table]
            row = table.rows[operand.rid - 1]
            if table.name == "MethodDef":
                owner = self.method_owner.get(id(row))
                prefix = self.full_type_name(owner[1]) + "." if owner else ""
                return prefix + str(row.Name)
            if table.name == "MemberRef":
                owner = self.coded_type_name(row.Class)
                return owner + "." + str(row.Name)
            if table.name in ("TypeDef", "TypeRef"):
                return self.full_type_name(row)
            return table.name + ":" + str(getattr(row, "Name", operand))
        except Exception:
            return str(operand)

    def method_record(self, rid: int, method) -> dict:
        record = {
            "token": f"0x060{rid:05X}",
            "name": str(method.Name),
            "rva": f"0x{method.Rva:X}",
            "body": "none" if not method.Rva else "unparsed",
            "instruction_count": 0,
            "strings": [],
            "calls": [],
            "event_adders": [],
        }
        if not method.Rva:
            return record
        try:
            body = read_method_body_from_bytes(self.pe.get_data(method.Rva, 0x10000))
            record["instruction_count"] = len(body.instructions)
            record["body"] = "parsed"
            opcodes = [ins.opcode.name for ins in body.instructions]
            meaningful = [op for op in opcodes if op != "nop"]
            if len(meaningful) <= 3 and meaningful[-1:] == ["ret"]:
                record["body"] = "stub-like"
            for ins in body.instructions:
                if isinstance(ins.operand, StringToken):
                    value = self.resolve_token(ins.operand)
                    if value not in record["strings"]:
                        record["strings"].append(value)
                if ins.opcode.name in ("call", "callvirt", "newobj") and isinstance(ins.operand, Token):
                    target = self.resolve_token(ins.operand)
                    if target not in record["calls"]:
                        record["calls"].append(target)
                    if ".add_" in target and target not in record["event_adders"]:
                        record["event_adders"].append(target)
        except Exception as exc:
            record["body"] = "parse-error"
            record["error"] = repr(exc)
        return record

    def inspect(self) -> dict:
        forms = []
        baccarat_types = []
        key_methods = []
        state_machines = []
        key_type_names = {
            "Baccarat.Authentication", "Baccarat.Global", "Baccarat.Program",
            "Baccarat.WS_Authentication",
        }
        method_rid_by_id = {id(row): rid for rid, row in enumerate(self.tables.MethodDef.rows, 1)}
        for type_rid, type_row in enumerate(self.tables.TypeDef.rows, 1):
            full_name = self.full_type_name(type_row)
            base = self.coded_type_name(type_row.Extends)
            type_record = {
                "token": f"0x020{type_rid:05X}",
                "name": full_name,
                "base": base,
                "field_count": len(type_row.FieldList),
                "method_count": len(type_row.MethodList),
            }
            if str(type_row.TypeName).startswith("<"):
                state_machines.append(type_record)
            if not full_name.startswith("Baccarat."):
                continue
            baccarat_types.append(type_record)
            if full_name in key_type_names:
                for index in type_row.MethodList:
                    row = index.row
                    key_methods.append({
                        "declaring_type": full_name,
                        **self.method_record(method_rid_by_id[id(row)], row),
                    })
            if base != "System.Windows.Forms.Form":
                continue
            fields = []
            for index in type_row.FieldList:
                row = index.row
                fields.append({"name": str(row.Name), "type": self.field_type(row)})
            methods = []
            for index in type_row.MethodList:
                row = index.row
                methods.append(self.method_record(method_rid_by_id[id(row)], row))
            forms.append({**type_record, "fields": fields, "methods": methods})
        return {
            "source": str(self.source),
            "source_sha256": hashlib.sha256(self.data).hexdigest(),
            "warning": "Static metadata only. Protected runtime-restored behavior is not represented by stub-like on-disk bodies.",
            "baccarat_types": baccarat_types,
            "key_methods": key_methods,
            "state_machines": state_machines,
            "forms": forms,
        }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    rendered = json.dumps(Inspector(args.source).inspect(), indent=2)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(rendered + "\n", encoding="utf-8")
    else:
        print(rendered)


if __name__ == "__main__":
    main()


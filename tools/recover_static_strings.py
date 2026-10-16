"""Bounded, allowlisted IL data interpreter for the supplied v2 string resource.

Never loads the executable into the CLR, runs initializers, invokes native code,
or calls arbitrary methods. Unsupported operations stop recovery. Output excludes
credentials and non-URL strings. This is a data-recovery experiment, not a server adapter.
"""
import argparse
import io
import json
import re
import struct
from pathlib import Path
from urllib.parse import urlsplit

from dncil.cil.body.reader import read_method_body_from_bytes
from phase2_metadata import Inspector


def signed(value):
    if isinstance(value, float):
        return value
    value = int(value) & 0xffffffff
    return value if value < 0x80000000 else value - 0x100000000


class Decoder:
    def __init__(self, source):
        self.inspector = Inspector(source)
        # Match the audited file: token assignments are version-specific.
        import hashlib
        if hashlib.sha256(self.inspector.data).hexdigest() != "b051ade2e179154614bd566adc35c5c3672a04f899b891febafbf0cd4045ecb1":
            raise ValueError("Unsupported file hash")
        method = self.inspector.tables.MethodDef.rows[0x225 - 1]
        self.instructions = read_method_body_from_bytes(self.inspector.pe.get_data(method.Rva, 65536)).instructions
        self.indices = {item.offset: i for i, item in enumerate(self.instructions)}
        self.resources = {str(r.name): bytes(r.data) for r in self.inspector.pe.net.resources}
        self.fields = {"Field:Va6wT53QoJ": bytearray(), "Field:Ln9wPYCemc": "assembly"}
        self.steps = 0

    def run(self, argument=18936):
        stack, locals_ = [], {}
        pc = 0
        while self.steps < 10000000:
            self.steps += 1
            ins = self.instructions[pc]
            op, operand = ins.opcode.name, ins.operand
            pc += 1
            if op.startswith("ldc.i4"):
                suffix = op.removeprefix("ldc.i4")
                stack.append(operand if suffix in ("", ".s") else -1 if suffix == ".m1" else int(suffix[1:]))
            elif op == "ldc.r8": stack.append(operand)
            elif op in ("ldloc", "ldloc.s"): stack.append(locals_[operand.index])
            elif op in ("stloc", "stloc.s"): locals_[operand.index] = stack.pop()
            elif op == "ldarg.0": stack.append(argument)
            elif op == "ldsfld": stack.append(self.fields[self.inspector.resolve_token(operand)])
            elif op == "stsfld": self.fields[self.inspector.resolve_token(operand)] = stack.pop()
            elif op == "ldstr": stack.append(self.inspector.pe.net.user_strings.get(operand.value & 0xffffff).value)
            elif op == "ldlen": stack.append(len(stack.pop()))
            elif op == "newarr":
                count = stack.pop()
                if not 0 <= count <= 1048576: raise ValueError("Array bound")
                stack.append(bytearray(count))
            elif op == "stelem.i1":
                value, index, array = stack.pop(), stack.pop(), stack.pop()
                if not 0 <= index < len(array): raise ValueError("Array write bound")
                array[index] = value & 255
            elif op == "ldelem.u1":
                index, array = stack.pop(), stack.pop()
                if not 0 <= index < len(array): raise ValueError("Array read bound")
                stack.append(array[index])
            elif op in ("add", "sub", "mul", "div", "rem", "and", "or", "xor", "shl", "shr.un"):
                right, left = stack.pop(), stack.pop()
                if op == "add": result = left + right
                elif op == "sub": result = left - right
                elif op == "mul": result = left * right
                elif op == "div": result = int(left / right)
                elif op == "rem": result = left - int(left / right) * right
                elif op == "and": result = left & right
                elif op == "or": result = left | right
                elif op == "xor": result = left ^ right
                elif op == "shl": result = left << (right & 31)
                else: result = (left & 0xffffffff) >> (right & 31)
                stack.append(signed(result))
            elif op.startswith("conv."):
                value = stack.pop()
                if op == "conv.u1": value = int(value) & 255
                elif op in ("conv.u", "conv.u4"): value = int(value) & 0xffffffff
                elif op == "conv.i4": value = signed(int(value))
                elif op == "conv.i8": value = int(value)
                elif op == "conv.r.un": value = float(value & 0xffffffff)
                elif op == "conv.r8": value = float(value)
                else: raise ValueError("Unsupported conversion " + op)
                stack.append(value)
            elif op == "pop": stack.pop()
            elif op == "nop": pass
            elif op in ("br", "leave"):
                if op == "leave": stack.clear()
                pc = self.indices[operand]
            elif op in ("brtrue", "brfalse"):
                value = stack.pop()
                if bool(value) == (op == "brtrue"): pc = self.indices[operand]
            elif op in ("ble", "blt", "bne.un"):
                right, left = stack.pop(), stack.pop()
                branch = left <= right if op == "ble" else left < right if op == "blt" else left != right
                if branch: pc = self.indices[operand]
            elif op == "switch":
                index = stack.pop()
                if 0 <= index < len(operand): pc = self.indices[operand[index]]
            elif op in ("call", "newobj"):
                name = self.inspector.resolve_token(operand)
                self.call(name, stack)
            elif op == "ret": return stack.pop() if stack else None
            else: raise ValueError(f"Unsupported opcode {op} at {ins.offset}")
        raise ValueError("Instruction bound reached")

    def call(self, full_name, stack):
        name = full_name.rsplit(".", 1)[-1]
        if name == "WNBoWWoAuFWcbVa5Ov": stack.append(True)
        elif name == "dUfN3C5b4rCA1uFpJN": stack.append(None)
        elif name == "tZurFvCHUl9GkeRN8L":
            resource = stack.pop()
            stack.pop()  # assembly placeholder
            stack.append(io.BytesIO(self.resources[resource]))
        elif full_name == "VtNVUKGulysZw81C3E..ctor": pass  # BinaryReader over the existing stream
        elif name == "yHVeQLM03CfWVWrA0j": pass  # BinaryReader.BaseStream
        elif name == "Hlq6UH0oOV7dJxV6he":
            position, stream = stack.pop(), stack.pop()
            if not 0 <= position <= len(stream.getvalue()): raise ValueError("Stream position bound")
            stream.seek(position)
        elif name == "camYPjiff0M5mclxIp": stack.append(len(stack.pop().getvalue()))
        elif name == "behyWNnD3VHlTLujZ2":
            count, stream = stack.pop(), stack.pop()
            if not 0 <= count <= 1048576: raise ValueError("Stream read bound")
            stack.append(bytearray(stream.read(count)))
        elif name in ("cw3Km02QxiTmTxI1j3", "gHKS9tTUaHjcXdpohB"): stack.pop()
        elif name == "XminNWZv8eQk8OR1He": stack.pop().reverse()
        elif name == "Xlq8mEd0yYp0CBO0FG": pass  # AssemblyName placeholder
        elif name == "EFaWtGLluZ4nH1QDwb":
            stack.pop()
            if self.inspector.tables.Assembly.rows[0].PublicKey.value: raise ValueError("Unexpected signed assembly")
            stack.append(bytearray())
        elif name == "mMKNbX4a5Y7BtoUNVN":
            index, data = stack.pop(), stack.pop()
            if not 0 <= index <= len(data) - 4: raise ValueError("Int32 read bound")
            stack.append(struct.unpack_from("<i", data, index)[0])
        elif name == "UWgrdpatQgUgAZ70Mo": stack.append("utf-16-le")
        elif name == "AQO4P2bTBZ9VwmYeNy":
            count, index, data, encoding = stack.pop(), stack.pop(), stack.pop(), stack.pop()
            if not 0 <= index <= index + count <= len(data): raise ValueError("String read bound")
            stack.append(bytes(data[index:index + count]).decode(encoding))
        else: raise ValueError("Unsupported call: " + full_name)

    def recover(self):
        stop = None
        try:
            self.run()
        except ValueError as exc:
            # The audited void helper occurs after the table has been initialized.
            # Do not execute or emulate that unknown method; validate the data already
            # produced, and report the precise boundary.
            if str(exc) != "Unsupported call: dg3ypDAonQcOidMs0w.WP6RZJql8gZrNhVA9v.m5rkLafepqV0jHLI0E":
                raise
            stop = str(exc)
        table = self.fields["Field:Va6wT53QoJ"]
        strings = {}
        offset = 0
        while offset < len(table):
            if offset + 4 > len(table): raise ValueError("Truncated string header")
            count = struct.unpack_from("<i", table, offset)[0]
            if count < 0 or count % 2 or offset + 4 + count > len(table): raise ValueError("Invalid string table")
            strings[offset] = bytes(table[offset + 4:offset + 4 + count]).decode("utf-16-le")
            offset += 4 + count
        return strings, stop


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    args = parser.parse_args()
    decoder = Decoder(args.source)
    try:
        strings, stop = decoder.recover()
        result = {"steps": decoder.steps, "table_bytes": len(decoder.fields["Field:Va6wT53QoJ"]),
                  "validated_strings": len(strings), "execution_boundary": stop, "endpoints": {}}
        for name, offset in (("http", 18936), ("websocket", 19002), ("bucket", 19078)):
            value = strings[offset]
            parsed = urlsplit(value)
            result["endpoints"][name] = {"scheme": parsed.scheme, "host": parsed.hostname, "path": parsed.path}
        result["route_candidates"] = [{"offset": offset, "text": value} for offset, value in strings.items()
            if offset not in (19200, 19252, 19304, 19356) and re.fullmatch(r"[A-Za-z/_-]{2,100}", value)
            and re.search(r"sign|login|register|auth|token|cookie|service|product", value, re.I)]
        result["url_candidates"] = [{"offset": offset, "scheme": urlsplit(value).scheme,
            "host": urlsplit(value).hostname, "path": urlsplit(value).path} for offset, value in strings.items()
            if value.startswith(("https://", "http://", "wss://", "ws://"))]
        print(json.dumps(result, indent=2))
    except (ValueError, KeyError, IndexError, TypeError) as exc:
        print(json.dumps({"status": "incomplete", "steps": decoder.steps, "reason": str(exc)}))
        raise SystemExit(1)

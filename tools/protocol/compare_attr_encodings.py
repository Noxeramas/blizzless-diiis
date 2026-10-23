"""Compare how each attribute's value is encoded in 2.8.0 (from a snapshot) and in DiIiS.

Both sides are reduced to the same form: U32 (32 raw bits, int or float), F16, FVAR (Float16Or32), or
INT[min,max]/bits. Usage: python -I compare_attr_encodings.py <snapshot-dir> <GameAttribute.List.cs>
"""
import json
import os
import re
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from snap import Snapshot

s = Snapshot(sys.argv[1])
info = json.load(open(os.path.join(sys.argv[1], "snapshot.json")))
enc = info["attribute_encoders"]
table = s.base + enc["table_rva"]


def bounded(lo, hi):
    return f"INT[{lo},{hi}]/{max(1, (hi - lo).bit_length())}"


client = {}
for i in range(enc["count"]):
    rec = table + i * 0x40
    obj = enc["objects"].get(str(s.qword(rec + 0x18)))
    if not obj or "class" not in obj:
        client[i] = (s.cstring(s.qword(rec + 0x10)), f"NO-ENCODER({s.qword(rec + 0x18):#x})")
        continue
    cls = obj["class"]
    dw = struct.unpack("<16i", bytes.fromhex(obj["bytes"]))
    if "Uncompressed" in cls:
        form = "U32"
    elif "Float16" in cls:
        form = "F16"
    elif "FloatVariable" in cls:
        form = "FVAR"
    else:
        form = bounded(dw[6], dw[7])
    client[i] = (s.cstring(s.qword(rec + 0x10)), form)

src = open(sys.argv[2], encoding="utf-8-sig").read()
diiis = {}
for kind, ident, args in re.findall(r"GameAttribute([IFB])\s+(\w+)\s*=\s*new\((.*?)\);", src):
    m = re.match(r"\s*(\d+)\s*,\s*\"(\w+)\"\s*(.*)", args, re.S)
    if not m:
        continue
    aid, rest = int(m.group(1)), m.group(3)

    def named(key, default=None):
        mm = re.search(rf"\b{key}\s*:\s*(-?[\w.]+)", rest)
        return mm.group(1) if mm else default

    em = re.search(r"GameAttributeEncoding\.(\w+)", rest)
    encoding = em.group(1) if em else {"I": "Int", "F": "Float32", "B": "IntMinMax"}[kind]
    bits = int(named("bitCount", "1" if kind == "B" else "32"))
    lo = int(float(named("min", "0").rstrip("f")))
    hi = int(float(named("max", "1" if kind == "B" else "0").rstrip("f")))
    # Mirrors the server: a sub-32-bit Int is sent as a bounded int, and a storesRawValue one is already
    # shifted by -min in code, which is the same bits on the wire.
    if encoding == "Int" and bits < 32:
        encoding = "IntMinMax"
    if encoding in ("Int", "Float32"):
        form = "U32" if bits == 32 else f"RAW/{bits}"
    elif encoding == "Float16":
        form = "F16"
    elif encoding == "Float16Or32":
        form = "FVAR"
    else:
        form = bounded(lo, hi)
        if not form.endswith(f"/{bits}"):
            form += f" (DiIiS writes {bits} bits)"
    diiis[aid] = (ident, form)

mismatch = [(i, diiis[i][0], diiis[i][1], client[i][1]) for i in sorted(diiis)
            if i in client and diiis[i][1] != client[i][1]]
print(f"compared {len(diiis)} DiIiS attributes with {len(client)} 2.8.0 attributes: {len(mismatch)} encode differently")
for i, name, d, c in mismatch:
    print(f"  {i:>4} {name:<45} DiIiS {d:<32} 2.8.0 {c}")

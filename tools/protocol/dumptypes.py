"""Recursively dump Diablo III type descriptors from a snapshot in a compact, diffable form.

Field record (0xC0 bytes): +0x08 type, +0x10 offset, +0x20 min (i64), +0x28 max (i64), +0x30 flags,
+0x38 element type, +0x44 array/string length, +0x48 extra, +0x4C bit count (arrays: count bits << 16).
Usage: python -I dumptypes.py <snapshot-dir> <TypeName> [...]   (or --all to dump every type)
"""
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from snap import Snapshot

STRIDE = 0xC0
s = Snapshot(sys.argv[1])
TYPE_VTABLE = None


def i32(va):
    return struct.unpack("<i", s.bytes(va, 4))[0]


def i64(va):
    return struct.unpack("<q", s.bytes(va, 8))[0]


def tname(td):
    return (s.cstring(s.qword(td + 8) or 0) or f"?{td - s.base:#x}") if td else "-"


def find_type(name):
    for sva in s.find_cstring(name):
        for ref in s.find_qword(sva):
            if s.section(ref) == ".data":
                return ref - 8
    return None


def fields_of(td):
    fields, count = s.qword(td + 0x18), s.qword(td + 0x20)
    if not fields or not count or count > 4096 or s.section(fields) is None:
        return []
    out = []
    for i in range(count):
        f = fields + i * STRIDE
        ftype = s.qword(f + 8)
        if tname(ftype) == "DT_NULL":
            out.append({"end": i32(f + 0x10)})
            break
        out.append({
            "type": ftype, "offset": i32(f + 0x10), "min": i64(f + 0x20), "max": i64(f + 0x28),
            "flags": i32(f + 0x30), "elem": s.qword(f + 0x38), "len": i32(f + 0x44), "extra": i32(f + 0x48),
            "bits": i32(f + 0x4C),
        })
    return out


def describe(field):
    t = tname(field["type"])
    parts = [f"@{field['offset']}", t]
    bits = field["bits"]
    if t in ("DT_FIXEDARRAY", "DT_VARIABLEARRAY"):
        parts.append(f"of {tname(field['elem'])} x{field['len']} countbits={bits >> 16}")
    elif t == "DT_CHARARRAY":
        parts.append(f"len={field['len']}")
    elif bits:
        parts.append(f"bits={bits}")
    if field["flags"] & 0x10:
        parts.append(f"range=[{field['min']},{field['max']}]")
    if field["flags"] not in (1, 17, 0):
        parts.append(f"flags={field['flags']}")
    return " ".join(parts)


def dump(name_or_td, seen, out):
    td = find_type(name_or_td) if isinstance(name_or_td, str) else name_or_td
    if td is None or td in seen:
        return
    seen.add(td)
    fields = fields_of(td)
    if not fields:
        return
    size = fields[-1].get("end", "?")
    out.append(f"{tname(td)} (size {size})")
    nested = []
    for i, f in enumerate(fields):
        if "end" in f:
            continue
        out.append(f"  {i:>2}: {describe(f)}")
        for sub in (f["type"], f["elem"]):
            if sub and not tname(sub).startswith("DT_"):
                nested.append(sub)
    for sub in nested:
        dump(sub, seen, out)


out, seen = [], set()
for name in sys.argv[2:]:
    dump(name, seen, out)
print("\n".join(out))

"""Dump the types of specific fields of a named type, choosing the descriptor with the most fields.

Usage: python -I dump_field_type.py <snapshot-dir> <TypeName> <field-index> [...]
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dumptypes as d  # noqa: E402

name = sys.argv[2]
start, data, _ = next(r for r in d.s.regions if r[2] == ".data")
vtable = d.s.qword(d.find_type("NewPlayerMessage"))
candidates = []
for off in range(0, len(data) - 16, 8):
    if int.from_bytes(data[off:off + 8], "little") == vtable:
        if d.s.cstring(int.from_bytes(data[off + 8:off + 16], "little")) == name:
            candidates.append(start + off)
td = max(candidates, key=lambda t: len(d.fields_of(t)))
fields = d.fields_of(td)
print(f"{name}: {len(candidates)} descriptors with this name; using rva {td - d.s.base:#x} with {len(fields)} fields")
for idx in map(int, sys.argv[3:]):
    out = []
    d.dump(fields[idx]["type"], set(), out)
    print(f"--- field {idx}:")
    print("\n".join(out))

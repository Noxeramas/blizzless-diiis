"""Dump the 2.8.0 game-attribute table (0x40-byte records, name at +0x10) and compare id/name with DiIiS.

Usage: python -I compare_attrs.py <snapshot-dir> <GameAttribute.List.cs>
Prints the raw record dwords for the first few, then every id where the names differ.
"""
import os
import re
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from snap import Snapshot

STRIDE = 0x40
s = Snapshot(sys.argv[1])
name_ref = next(r for r in s.find_qword(s.find_cstring("Axe_Bad_Data")[0]) if s.section(r) == ".data")
table = name_ref - 0x10

attrs = []
while True:
    rec = table + len(attrs) * STRIDE
    name = s.cstring(s.qword(rec + 0x10) or 0)
    if not name:
        break
    attrs.append((name, [struct.unpack("<i", s.bytes(rec + o, 4))[0] for o in range(0, STRIDE, 4)]))
print(f"2.8.0 attribute table: {len(attrs)} attributes at rva {table - s.base:#x}")
for i in (0, 1, 2, 115, 124):
    name, dw = attrs[i]
    print(f"  [{i}] {name:<40} dwords: {dw}")

src = open(sys.argv[2], encoding="utf-8-sig").read()
diiis = {int(i): n for i, n in re.findall(r"new\(\s*(\d+)\s*,\s*\"(\w+)\"", src)}
print(f"DiIiS defines {len(diiis)} attributes, highest id {max(diiis)}")
diff = [(i, diiis.get(i), attrs[i][0] if i < len(attrs) else None)
        for i in range(max(len(attrs), max(diiis) + 1))
        if diiis.get(i) != (attrs[i][0] if i < len(attrs) else None)]
print(f"{len(diff)} ids differ (id: DiIiS -> 2.8.0):")
for i, a, b in diff[:60]:
    print(f"  {i:>4}: {a} -> {b}")

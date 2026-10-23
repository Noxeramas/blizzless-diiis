"""Map each 2.7.4 item field offset to its 2.8.0 offset by matching values for the same items.

Usage: python -I map_item_fields.py <dir with Base_GameBalance_<X>.gam and mpq_<X>.gam> <table name>...
For each 4-byte field in the 2.7.4 record, every candidate 2.8.0 offset is scored by how often it holds the
same value for the same item; fields with too few distinct values are reported as ambiguous.
"""
import os
import struct
import sys
from collections import Counter

NEW, OLD = 1408, 1304


def items(path, rec):
    d = open(path, "rb").read()
    off, size = struct.unpack_from("<ii", d, 56)
    base = off + 16
    return {d[base + k * rec:base + k * rec + 256].split(b"\0", 1)[0]: d[base + k * rec:base + (k + 1) * rec]
            for k in range(size // rec)}


new, old = {}, {}
for name in sys.argv[2:]:
    new.update(items(os.path.join(sys.argv[1], f"Base_GameBalance_{name}.gam"), NEW))
    old.update(items(os.path.join(sys.argv[1], f"mpq_{name}.gam"), OLD))
common = [n for n in old if n in new]
print(f"{len(common)} items in both versions")


def col(recs, off):
    return [struct.unpack_from("<i", recs[n], off)[0] for n in common]


new_cols = {p: col(new, p) for p in range(256, NEW, 4)}
for o in range(256, OLD, 4):
    oc = col(old, o)
    distinct = len(set(oc))
    scores = Counter()
    for p, nc in new_cols.items():
        scores[p] = sum(a == b for a, b in zip(oc, nc))
    best, hits = scores.most_common(1)[0]
    ties = [p for p, h in scores.items() if h == hits]
    tag = "" if distinct >= 3 and len(ties) == 1 else f"  (ambiguous: {distinct} distinct values, {len(ties)} equal candidates)"
    if o != best or tag:
        print(f"@{o:<5} -> @{best:<5} shift {best - o:+4d}  {hits}/{len(common)} match{tag}")

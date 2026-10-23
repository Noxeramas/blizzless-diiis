"""Compare DiIiS's Dicts.cs (2.7.4 name -> SNO id per group) with the 2.8.0 CoreTOC.dat.

Usage: python -I compare_toc.py <CoreTOC.dat> <Dicts.cs> <SNOGroup enum .cs>
"""
import re
import struct
import sys
from collections import Counter, defaultdict

toc = open(sys.argv[1], "rb").read()
N = 70
counts = struct.unpack_from(f"<{N}I", toc, 0)
offsets = struct.unpack_from(f"<{N}I", toc, 4 * N)
hdr = 4 * N * 3 + 4
toc_by = {}
for g in range(N):
    base = hdr + offsets[g]
    names = base + 12 * counts[g]
    for k in range(counts[g]):
        grp, sno, noff = struct.unpack_from("<III", toc, base + 12 * k)
        s = names + noff
        toc_by[(grp, toc[s:toc.index(b"\0", s)].decode(errors="replace"))] = sno
print(f"2.8.0 CoreTOC: {len(toc_by)} entries")

enum_src = open(sys.argv[3], encoding="utf-8-sig").read()
enum_src = enum_src[enum_src.find("enum SNOGroup"):]
groups = {n: int(v) for n, v in re.findall(r"^\s*(\w+)\s*=\s*(-?\d+)", enum_src[:enum_src.find("}")], re.M)}

dicts = open(sys.argv[2], encoding="utf-8-sig").read()
# Map dictionary name to SNOGroup via Data.cs's LoadSNODict(DictSNOX, SNOGroup.Y) calls.
data_cs = open(sys.argv[2].replace("Dicts.cs", "Data.cs"), encoding="utf-8-sig").read()
dict_group = dict(re.findall(r"LoadSNODict\((DictSNO\w+),\s*SNOGroup\.(\w+)\)", data_cs))

stats = Counter()
mismatch = defaultdict(list)
for dname, name, sno in re.findall(r"(DictSNO\w+)\.Add\(\"([^\"]+)\",\s*(-?\d+)\)", dicts):
    group = dict_group.get(dname)
    if group is None:
        stats["dict not loaded"] += 1
        continue
    new = toc_by.get((groups[group], name))
    if new is None:
        stats["missing in 2.8.0"] += 1
        mismatch[group].append((name, int(sno), None))
    elif new != int(sno):
        stats["id changed"] += 1
        mismatch[group].append((name, int(sno), new))
    else:
        stats["same"] += 1
print(dict(stats))
for group, rows in sorted(mismatch.items(), key=lambda kv: -len(kv[1])):
    print(f"  {group}: {len(rows)} differ, e.g. " + "; ".join(f"{n} {o}->{nw}" for n, o, nw in rows[:4]))
new_per_group = Counter()
diiis_names = set(re.findall(r"\.Add\(\"([^\"]+)\"", dicts))
for (g, name) in toc_by:
    if name not in diiis_names:
        new_per_group[g] += 1
inv = {v: k for k, v in groups.items()}
print("2.8.0 assets DiIiS doesn't list, by group:", {inv.get(g, g): c for g, c in new_per_group.most_common(12)})

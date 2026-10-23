"""For same-size files that differ between 2.7.4 and 2.8.0, count which 4-byte-aligned offsets differ.

Usage: python -I diff_offsets.py <fmt-dir> <Group> [top]
Offsets are file offsets; the struct starts at 16 (after the file header). Shows how many files differ at each
offset and a few old -> new values.
"""
import os
import struct
import sys
from collections import Counter, defaultdict

root, group = sys.argv[1], sys.argv[2]
top = int(sys.argv[3]) if len(sys.argv) > 3 else 25
cdir, mdir = os.path.join(root, "casc", group), os.path.join(root, "mpq", group)
counts, examples, files = Counter(), defaultdict(list), 0
for name in os.listdir(cdir):
    mp = os.path.join(mdir, name)
    if not os.path.exists(mp):
        continue
    c, m = open(os.path.join(cdir, name), "rb").read(), open(mp, "rb").read()
    if c == m or len(c) != len(m):
        continue
    files += 1
    for off in range(0, len(c) - 3, 4):
        if c[off:off + 4] != m[off:off + 4]:
            counts[off] += 1
            if len(examples[off]) < 3:
                examples[off].append((struct.unpack_from("<i", m, off)[0], struct.unpack_from("<i", c, off)[0], name))
print(f"{group}: {files} same-size files differ; {len(counts)} distinct offsets")
for off, n in counts.most_common(top):
    ex = "; ".join(f"{o}->{nw} ({nm[:28]})" for o, nw, nm in examples[off])
    print(f"  file @{off:<6} struct @{off - 16:<6} {n:>4} files  {ex}")

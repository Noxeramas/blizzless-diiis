"""Compare sampled game files between 2.7.4 (mpq) and 2.8.0 (casc) per SNO group.

Usage: python -I cmp_formats.py <fmt-dir>
For each group: files in both, byte-identical, same size, and the most common size changes. A group whose files
are mostly identical or differ only in content (same sizes) kept its layout; consistent size changes mean the
record layout changed.
"""
import os
import sys
from collections import Counter

root = sys.argv[1]
print(f"{'group':<14} {'both':>5} {'same':>5} {'sameSz':>6}  common size deltas (delta x count)")
for group in sorted(os.listdir(os.path.join(root, "casc"))):
    cdir, mdir = os.path.join(root, "casc", group), os.path.join(root, "mpq", group)
    if not os.path.isdir(mdir):
        print(f"{group:<14} no 2.7.4 files")
        continue
    both = same = same_size = 0
    deltas = Counter()
    for name in os.listdir(cdir):
        mp = os.path.join(mdir, name)
        if not os.path.exists(mp):
            continue
        c, m = open(os.path.join(cdir, name), "rb").read(), open(mp, "rb").read()
        both += 1
        if c == m:
            same += 1
        elif len(c) == len(m):
            same_size += 1
        else:
            deltas[len(c) - len(m)] += 1
    common = ", ".join(f"{d:+d}x{n}" for d, n in deltas.most_common(6))
    print(f"{group:<14} {both:>5} {same:>5} {same_size:>6}  {common}")

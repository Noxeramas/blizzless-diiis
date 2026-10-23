"""Find large MSVC switch jump tables: runs of 32-bit RVAs pointing into .text, stored in .text/.rdata.

Usage: python -I find_jumptables.py <exe> [min-entries]
"""
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pe import PE  # noqa: E402

pe = PE(sys.argv[1])
min_entries = int(sys.argv[2]) if len(sys.argv) > 2 else 400
_, t_rva, t_vsize, _, _ = next(x for x in pe.sections if x[0] == ".text")
for name, rva, vsize, raw, rawsize in pe.sections:
    if name not in (".text", ".rdata"):
        continue
    sec = pe.data[raw:raw + rawsize]
    n = len(sec) // 4
    vals = struct.unpack_from(f"<{n}I", sec, 0)
    run_start, run, lo, hi = 0, 0, 0, 0
    for k, v in enumerate(vals + (0,)):
        ok = t_rva <= v < t_rva + t_vsize
        if ok and run and (max(hi, v) - min(lo, v)) < 0x40000:
            run += 1
            lo, hi = min(lo, v), max(hi, v)
            continue
        if run >= min_entries:
            targets = vals[run_start:run_start + run]
            print(f"{name}: jump table at rva {rva + run_start * 4:#x}, {run} entries, {len(set(targets))} distinct targets, "
                  f"targets {lo:#x}..{hi:#x}")
        run_start, run, lo, hi = k, (1 if ok else 0), v, v

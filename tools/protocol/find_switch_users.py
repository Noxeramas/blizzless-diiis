"""Find instructions in .text that reference a jump table RVA as a disp32, and dump the code before them.

Usage: python -I find_switch_users.py <exe> <table-rva> [search-slack-bytes]
"""
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pe import PE  # noqa: E402

pe = PE(sys.argv[1])
table = int(sys.argv[2], 0)
slack = int(sys.argv[3]) if len(sys.argv) > 3 else 64
_, t_rva, _, t_raw, t_rawsize = next(x for x in pe.sections if x[0] == ".text")
text = pe.data[t_raw:t_raw + t_rawsize]
for delta in range(-slack, slack + 1, 4):
    needle = struct.pack("<I", table + delta)
    i = text.find(needle)
    while i != -1:
        rva = t_rva + i
        if not (table - 8192 <= rva <= table + 8192 * 4):  # skip hits inside the table itself
            print(f"disp32 {table + delta:#x} (table{delta:+d}) used at rva {rva:#x}")
            ctx = text[i - 48:i + 12]
            print("   " + " ".join(f"{b:02x}" for b in ctx))
        i = text.find(needle, i + 1)

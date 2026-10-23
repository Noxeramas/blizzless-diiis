"""Find where 2.8.0's message numbering shifted relative to DiIiS.

2.8.0: the client's receive dispatcher is a switch over opcode 2..559 (jump table, index = opcode - 2);
opcodes that jump to the default target are ones the client never receives (client -> server only).
DiIiS: messages with a Consumer in their [Message(...)] attribute are client -> server.
For each candidate shift d, count how well "client-only at opcode o in DiIiS" matches "default at o + d in
2.8.0", over sliding windows, to see where the alignment changes.
Usage: python -I align_opcodes.py <exe> <MessageSystem dir> <table-rva> <first-opcode> <count> <default-rva>
"""
import os
import re
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pe import PE  # noqa: E402

exe, msgdir = sys.argv[1], sys.argv[2]
table_rva, first, count, default = int(sys.argv[3], 0), int(sys.argv[4]), int(sys.argv[5]), int(sys.argv[6], 0)
pe = PE(exe)
off = pe.va_to_off(pe.image_base + table_rva)
targets = struct.unpack_from(f"<{count}I", pe.data, off)
client_default = {first + k for k, t in enumerate(targets) if t == default}
handled = {first + k for k, t in enumerate(targets) if t != default}
print(f"2.8.0: opcodes {first}..{first + count - 1}: {len(handled)} handled by the client, {len(client_default)} default")

src = open(os.path.join(msgdir, "Opcodes.cs"), encoding="utf-8-sig").read()
enum = src[src.find("enum Opcodes"):]
enum = enum[:enum.find("}")]
opnum = {n: int(v) for n, v in re.findall(r"^\s*(\w+)\s*=\s*(\d+)", enum, re.M)}
to_server, to_client = set(), set()
for root, _, files in os.walk(msgdir):
    for fn in files:
        if fn.endswith(".cs"):
            text = open(os.path.join(root, fn), encoding="utf-8-sig", errors="replace").read()
            for attr in re.findall(r"\[Message\((.*?)\)\]", text, re.S):
                ops = [opnum[n] for n in re.findall(r"Opcodes\.(\w+)", attr) if n in opnum]
                (to_server if "Consumers." in attr else to_client).update(ops)
print(f"DiIiS: {len(to_server)} client->server opcodes, {len(to_client)} server->client opcodes")

# Per-opcode agreement for shifts -8..+8: DiIiS client->server opcode o should be "default" at o+d,
# DiIiS server->client opcode o should be "handled" at o+d.
known = sorted(to_server | to_client)
for d in range(-2, 9):
    agree = [((o in to_server) == ((o + d) in client_default)) for o in known if first <= o + d < first + count]
    print(f"  shift {d:+d}: {sum(agree)}/{len(agree)} agree")

# Local alignment: for each DiIiS opcode, which shift (0..8) is consistent within a +-12 window?
print("\nlocal best shift by opcode range (window of known opcodes):")
prev = None
for idx, o in enumerate(known):
    win = known[max(0, idx - 8):idx + 9]
    scores = {}
    for d in range(0, 9):
        sc = [((x in to_server) == ((x + d) in client_default)) for x in win if first <= x + d < first + count]
        scores[d] = sum(sc) / max(1, len(sc))
    best = max(scores, key=lambda d: (scores[d], -d))
    if best != prev:
        print(f"  from DiIiS opcode {o}: best shift {best:+d} (score {scores[best]:.2f}, shift0 {scores[0]:.2f})")
        prev = best

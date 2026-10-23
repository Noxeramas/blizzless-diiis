"""Compare 2.8.0 message sizes (from a snapshot) with the //SIZE comments in DiIiS's Opcodes.cs.

Also writes every 2.8.0 type layout to types-2.8.txt next to the snapshot, for field-level comparison.
Usage: python -I compare_sizes.py <snapshot-dir> <Opcodes.cs>
"""
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dumptypes  # noqa: E402  (module-level code expects argv[1] = snapshot dir; it dumps nothing without names)

s = dumptypes.s
start, data, _ = next(r for r in s.regions if r[2] == ".data")
vtable = s.qword(dumptypes.find_type("NewPlayerMessage"))

types = {}
for off in range(0, len(data) - 16, 8):
    if int.from_bytes(data[off:off + 8], "little") == vtable:
        td = start + off
        name = s.cstring(int.from_bytes(data[off + 8:off + 16], "little"))
        if name:
            fields = dumptypes.fields_of(td)
            size = fields[-1].get("end") if fields and "end" in fields[-1] else None
            types[name] = (td, size)

out, seen = [], set()
for name in sorted(types):
    dumptypes.dump(types[name][0], seen, out)
open(os.path.join(sys.argv[1], "types-2.8.txt"), "w").write("\n".join(out) + "\n")

enum_text = open(sys.argv[2], encoding="utf-8-sig").read()
enum_text = enum_text[enum_text.find("enum Opcodes"):]
enum_text = enum_text[:enum_text.find("}")]
rows = re.findall(r"^\s*(\w+)\s*=\s*(\d+)\s*,\s*//\s*SIZE\s*(\d+)", enum_text, re.M)

changed, missing, same = [], [], 0
for name, opcode, size in rows:
    base = re.sub(r"\d+$", "", name)
    entry = types.get(name) or types.get(base)
    if not entry:
        missing.append((int(opcode), name))
    elif entry[1] != int(size):
        changed.append((int(opcode), name, int(size), entry[1]))
    else:
        same += 1

print(f"{len(rows)} DiIiS opcodes with sizes: {same} same size, {len(changed)} changed, {len(missing)} not matched by name")
print("\nChanged size (opcode, DiIiS name, DiIiS size -> 2.8.0 size):")
for row in sorted(changed):
    print(f"  {row[0]:>3} {row[1]:<45} {row[2]:>6} -> {row[3]}")
print("\nNo 2.8.0 type with that name:")
print("  " + ", ".join(f"{op}:{n}" for op, n in sorted(missing)))

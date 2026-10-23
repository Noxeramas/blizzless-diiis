"""List GameBalance's tables in file (offset) order with their element type and element size in 2.8.0."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dumptypes as d  # noqa: E402

start, data, _ = next(r for r in d.s.regions if r[2] == ".data")
vtable = d.s.qword(d.find_type("NewPlayerMessage"))
gb = max((start + off for off in range(0, len(data) - 16, 8)
          if int.from_bytes(data[off:off + 8], "little") == vtable
          and d.s.cstring(int.from_bytes(data[off + 8:off + 16], "little")) == "GameBalance"),
         key=lambda t: len(d.fields_of(t)))
for f in sorted((f for f in d.fields_of(gb) if "end" not in f), key=lambda f: f["offset"]):
    table = f["type"]
    if d.tname(table).startswith("DT_"):
        print(f"@{f['offset']:<4} {d.tname(table)}")
        continue
    arr = next((x for x in d.fields_of(table) if "end" not in x and d.tname(x["type"]) == "DT_VARIABLEARRAY"), None)
    elem = arr["elem"] if arr else None
    efields = d.fields_of(elem) if elem else []
    size = efields[-1]["end"] if efields and "end" in efields[-1] else "?"
    print(f"@{f['offset']:<4} {d.tname(table):<44} element {d.tname(elem) if elem else '-':<44} size {size}")

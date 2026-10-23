"""Compare the order and width of narrow (< 32-bit) fields between 2.8.0 types and DiIiS encoders.

Same-size layout changes, such as a 4-bit field becoming 5 bits, don't show up in a size comparison. This
reduces each side to the sequence of bit widths below 32 written directly by a type (count prefixes,
bools, optional-presence bits, ranged ints), ignoring nested types, and reports types whose sequences
differ. Usage: python -I compare_bitwidths.py <snapshot-dir> <DiIiS D3-GameServer dir>
"""
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dumptypes  # noqa: E402

s = dumptypes.s
start, data, _ = next(r for r in s.regions if r[2] == ".data")
vtable = s.qword(dumptypes.find_type("NewPlayerMessage"))


def client_tokens(td):
    tokens = []
    for f in dumptypes.fields_of(td):
        if "end" in f:
            continue
        t = dumptypes.tname(f["type"])
        bits = f["bits"]
        if t == "DT_OPTIONAL":
            tokens.append(1)
            if 0 < bits < 32:
                tokens.append(bits)
        elif t in ("DT_FIXEDARRAY", "DT_VARIABLEARRAY"):
            if f["flags"] & 8 and bits >> 16:
                tokens.append(bits >> 16)
        elif t in ("DT_INT", "DT_BYTE", "DT_ENUM", "DT_WORD") and 0 < bits < 32:
            tokens.append(bits)
    return tokens


client = {}
for off in range(0, len(data) - 16, 8):
    if int.from_bytes(data[off:off + 8], "little") == vtable:
        name = s.cstring(int.from_bytes(data[off + 8:off + 16], "little"))
        if name:
            client[name] = client_tokens(start + off)

WRITE = re.compile(r"buffer\.Write(Int|UInt|Bool|Float16|Float32|Int64|CharArray|Bytes|Blob)\s*\(\s*(\d+)?")


def encode_body(text, start_idx):
    depth, i = 0, text.index("{", start_idx)
    for j in range(i, len(text)):
        depth += {"{": 1, "}": -1}.get(text[j], 0)
        if depth == 0:
            return text[i + 1:j]
    return ""


diiis = {}
for root, _, files in os.walk(sys.argv[2]):
    for fn in files:
        if not fn.endswith(".cs"):
            continue
        text = open(os.path.join(root, fn), encoding="utf-8-sig", errors="replace").read()
        for m in re.finditer(r"(?:class|struct)\s+(\w+)", text):
            cls = m.group(1)
            enc = re.search(r"void\s+Encode\s*\(\s*GameBitBuffer\s+\w+\s*\)", text[m.end():])
            if not enc:
                continue
            body = encode_body(text, m.end() + enc.end())
            body = re.sub(r"//.*", "", body)
            tokens = []
            for w in WRITE.finditer(body):
                kind, bits = w.group(1), w.group(2)
                if kind == "Bool":
                    tokens.append(1)
                elif kind in ("Int", "UInt") and bits and int(bits) < 32:
                    tokens.append(int(bits))
            diiis.setdefault(cls, tokens)

diffs = [(n, diiis[n], client[n]) for n in sorted(set(diiis) & set(client)) if diiis[n] != client[n]]
print(f"{len(set(diiis) & set(client))} types compared, {len(diffs)} differ in narrow-field widths")
for name, d, c in diffs:
    print(f"  {name:<42} DiIiS {d}\n  {'':<42} 2.8.0 {c}")

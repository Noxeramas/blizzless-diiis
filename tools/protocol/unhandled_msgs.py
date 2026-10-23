"""List client-to-server game messages DiIiS ignores.

Usage: python -I unhandled_msgs.py <Diablo III64.exe> <DiIiS-NA dir>
Client-to-server opcodes are the ones the 2.8.0 client's receive dispatcher sends to its default case (it never
receives them). An opcode counts as handled when its Opcodes name is used outside Opcodes.cs and [Message]
attributes, or when its message class (registered for that opcode alone) is matched in code (`is X`, `case X`).
"""
import os
import re
import struct
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__))))
from pe import PE

exe, root = sys.argv[1], sys.argv[2]
p = PE(exe)
table = p.va_to_off(0x140000000 + 0x5ba910)
default = 0x5ba5e1
client_sent = {i + 2 for i in range(558) if struct.unpack_from("<I", p.data, table + 4 * i)[0] == default}

opcodes_cs = open(os.path.join(root, "D3-GameServer", "MessageSystem", "Opcodes.cs"), encoding="utf-8-sig").read()
region = opcodes_cs[opcodes_cs.index("#region 2.7.4"):]
names = {}
for m in re.finditer(r"^\s*(\w+)\s*=\s*(\d+)\s*,", region, re.M):
    names.setdefault(int(m.group(2)), []).append(m.group(1))

sources = {}
for dirpath, _, files in os.walk(os.path.join(root, "D3-GameServer")):
    for f in files:
        if f.endswith(".cs") and f != "Opcodes.cs":
            sources[os.path.join(dirpath, f)] = open(os.path.join(dirpath, f), encoding="utf-8-sig", errors="replace").read()

# message classes and the opcodes they register for
classes = {}
for path, text in sources.items():
    for m in re.finditer(r"\[Message\(", text):
        depth, i = 1, m.end()
        while depth and i < len(text):  # the attribute may contain new[] { ... }
            depth += {"(": 1, ")": -1}.get(text[i], 0)
            i += 1
        args = text[m.end():i - 1]
        cls = re.match(r"\]\s*(?:\[[^\]]*\]\s*)*(?:(?:public|internal|sealed|partial)\s+)*class\s+(\w+)", text[i:], re.S)
        if not cls:
            continue
        consumer = re.search(r"Consumers\.(\w+)", args)
        classes[cls.group(1)] = (re.findall(r"Opcodes\.(\w+)", args), consumer.group(1) if consumer else "None", path)

def used(name):
    pat = re.compile(r"Opcodes\." + name + r"\b")
    for text in sources.values():
        for line in text.splitlines():
            if pat.search(line) and "[Message(" not in line and not line.strip().startswith(("Opcodes.", "//")):
                return True
    return False

def class_matched(cls):
    pat = re.compile(r"(\bis\s+" + cls + r"\b|\bcase\s+" + cls + r"\b|<" + cls + r">|\(" + cls + r"\))")
    if any(pat.search(t) for t in sources.values()):
        return True
    # a self-handling message with a non-empty Handle()
    body = re.search(r"void\s+Handle\s*\(\s*GameClient\s+\w+\s*\)\s*\{(.*?)\n\s{8}\}", sources[classes[cls][2]], re.S)
    return bool(body and ";" in body.group(1))

for op in sorted(client_sent):
    for name in names.get(op, ["(no DiIiS name)"]):
        owners = [c for c, (ops, _, _) in classes.items() if name in ops]
        handled = used(name) or any(os.sep + "Base" + os.sep not in classes[c][2] and class_matched(c) for c in owners)
        if not handled:
            print(f"{op:4} {name:<45} {', '.join(owners) or '-'}")

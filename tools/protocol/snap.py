"""Read a snapshot made by snapshot.py: runtime-VA access to .data/.rdata, string lookup and dumps."""
import json
import os
import struct


class Snapshot:
    def __init__(self, folder):
        info = json.load(open(os.path.join(folder, "snapshot.json")))
        self.base = info["runtime_base"]
        self.regions = []
        for name, meta in info["sections"].items():
            data = open(os.path.join(folder, name.strip(".") + ".bin"), "rb").read()
            self.regions.append((self.base + meta["rva"], data, name))

    def region(self, va):
        for start, data, name in self.regions:
            if start <= va < start + len(data):
                return start, data, name
        return None

    def bytes(self, va, size):
        r = self.region(va)
        if not r:
            return None
        start, data, _ = r
        return data[va - start:va - start + size]

    def qword(self, va):
        b = self.bytes(va, 8)
        return struct.unpack("<Q", b)[0] if b and len(b) == 8 else None

    def dword(self, va):
        b = self.bytes(va, 4)
        return struct.unpack("<I", b)[0] if b and len(b) == 4 else None

    def cstring(self, va, limit=200):
        r = self.region(va) if isinstance(va, int) else None
        if not r:
            return None
        start, data, _ = r
        off = va - start
        end = data.find(b"\0", off, off + limit)
        if end <= off:
            return None
        raw = data[off:end]
        if any(b < 0x20 or b > 0x7E for b in raw):
            return None
        return raw.decode()

    def section(self, va):
        r = self.region(va)
        return r[2] if r else None

    def find_cstring(self, text):
        needle = b"\0" + text.encode() + b"\0"
        out = []
        for start, data, name in self.regions:
            i = data.find(needle)
            while i != -1:
                out.append(start + i + 1)
                i = data.find(needle, i + 1)
        return out

    def find_qword(self, value):
        needle = struct.pack("<Q", value)
        out = []
        for start, data, name in self.regions:
            i = data.find(needle)
            while i != -1:
                if i % 8 == 0:
                    out.append(start + i)
                i = data.find(needle, i + 1)
        return out

    def describe(self, q):
        if q is None:
            return ""
        s = self.cstring(q)
        if s:
            return f"-> '{s}'"
        sec = self.section(q)
        if sec:
            return f"-> {sec}+{q - self.base:#x}"
        return ""

    def dump(self, va, count, label=""):
        print(f"--- {label} @ rva {va - self.base:#x}")
        for k in range(count):
            a = va + k * 8
            q = self.qword(a)
            if q is None:
                break
            lo, hi = struct.unpack("<ii", struct.pack("<Q", q))
            print(f"  +{k*8:#05x}: {q:#018x} ({lo:>11d},{hi:>11d}) {self.describe(q)}")

"""Minimal PE helpers: section table, VA <-> file offset, string and pointer search."""
import struct


class PE:
    def __init__(self, path):
        self.data = open(path, "rb").read()
        d = self.data
        pe = struct.unpack_from("<I", d, 0x3C)[0]
        assert d[pe:pe + 4] == b"PE\0\0"
        nsec = struct.unpack_from("<H", d, pe + 6)[0]
        opt_size = struct.unpack_from("<H", d, pe + 20)[0]
        opt = pe + 24
        assert struct.unpack_from("<H", d, opt)[0] == 0x20B, "expected PE32+"
        self.image_base = struct.unpack_from("<Q", d, opt + 24)[0]
        self.sections = []
        sec = opt + opt_size
        for i in range(nsec):
            name = d[sec:sec + 8].rstrip(b"\0").decode()
            vsize, rva, rawsize, rawptr = struct.unpack_from("<IIII", d, sec + 8)
            self.sections.append((name, rva, vsize, rawptr, rawsize))
            sec += 40

    def off_to_va(self, off):
        for name, rva, vsize, rawptr, rawsize in self.sections:
            if rawptr <= off < rawptr + rawsize:
                return self.image_base + rva + (off - rawptr)
        return None

    def va_to_off(self, va):
        rva = va - self.image_base
        for name, srva, vsize, rawptr, rawsize in self.sections:
            if srva <= rva < srva + max(vsize, rawsize):
                if rva - srva < rawsize:
                    return rawptr + (rva - srva)
        return None

    def section_of_va(self, va):
        rva = va - self.image_base
        for name, srva, vsize, rawptr, rawsize in self.sections:
            if srva <= rva < srva + max(vsize, rawsize):
                return name
        return None

    def cstring_at_va(self, va, limit=200):
        off = self.va_to_off(va)
        if off is None:
            return None
        end = self.data.find(b"\0", off, off + limit)
        if end == -1:
            return None
        raw = self.data[off:end]
        if not raw or any(b < 0x20 or b > 0x7E for b in raw):
            return None
        return raw.decode()

    def find_cstring_vas(self, text):
        needle = b"\0" + text.encode() + b"\0"
        out, i = [], self.data.find(needle)
        while i != -1:
            out.append(self.off_to_va(i + 1))
            i = self.data.find(needle, i + 1)
        return out

    def find_pointer_refs(self, va):
        needle = struct.pack("<Q", va)
        out, i = [], self.data.find(needle)
        while i != -1:
            out.append(i)
            i = self.data.find(needle, i + 1)
        return out

    def qword(self, off):
        return struct.unpack_from("<Q", self.data, off)[0]

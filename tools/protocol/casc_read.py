"""Read files from the Diablo III CASC storage with CascLib; also parse CoreTOC.dat.

Usage: python -I casc_read.py <CascLib.dll> <game dir> <out-dir> <name> [name ...]
Writes each file to <out-dir>, prints its first bytes, and prints CoreTOC entries for the names' plain names.
"""
import ctypes
import os
import struct
import sys

dll = ctypes.WinDLL(sys.argv[1])
dll.CascOpenStorage.argtypes = [ctypes.c_char_p, ctypes.c_uint32, ctypes.POINTER(ctypes.c_void_p)]
dll.CascOpenStorage.restype = ctypes.c_bool
dll.CascOpenFile.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.POINTER(ctypes.c_void_p)]
dll.CascOpenFile.restype = ctypes.c_bool
dll.CascGetFileSize64.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_uint64)]
dll.CascGetFileSize64.restype = ctypes.c_bool
dll.CascReadFile.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_uint32, ctypes.POINTER(ctypes.c_uint32)]
dll.CascReadFile.restype = ctypes.c_bool
dll.CascCloseFile.argtypes = [ctypes.c_void_p]
dll.GetCascError.restype = ctypes.c_uint32

storage = ctypes.c_void_p()
assert dll.CascOpenStorage(sys.argv[2].encode(), 0xFFFFFFFF, ctypes.byref(storage)), dll.GetCascError()


def read(name):
    f = ctypes.c_void_p()
    if not dll.CascOpenFile(storage, name.encode(), 0, 0, ctypes.byref(f)):
        raise OSError(f"open {name}: {dll.GetCascError()}")
    size = ctypes.c_uint64()
    dll.CascGetFileSize64(f, ctypes.byref(size))
    buf = ctypes.create_string_buffer(size.value)
    got = ctypes.c_uint32()
    dll.CascReadFile(f, buf, size.value, ctypes.byref(got))
    dll.CascCloseFile(f)
    return buf.raw[:got.value]


out = sys.argv[3]
os.makedirs(out, exist_ok=True)
toc = read("Base\\CoreTOC.dat")
N = 70
counts = struct.unpack_from(f"<{N}I", toc, 0)
offsets = struct.unpack_from(f"<{N}I", toc, 4 * N)
hdr = 4 * N * 3 + 4
by_name = {}
for g in range(N):
    for k in range(counts[g]):
        grp, sno, noff = struct.unpack_from("<III", toc, hdr + offsets[g] + 12 * k)
        nm_start = hdr + offsets[g] + 12 * counts[g] + noff  # names follow each group's entries
        name = toc[nm_start:toc.index(b"\0", nm_start)].decode(errors="replace")
        by_name.setdefault(name, []).append((grp, sno))
print(f"CoreTOC: {sum(counts)} entries, groups with entries: {sum(1 for c in counts if c)}")
for name in sys.argv[4:]:
    data = read(name)
    open(os.path.join(out, name.replace("\\", "_")), "wb").write(data)
    plain = name.split("\\")[-1].rsplit(".", 1)[0]
    print(f"{name}: {len(data)} bytes, first 32: {data[:32].hex()}  TOC: {by_name.get(plain)}")

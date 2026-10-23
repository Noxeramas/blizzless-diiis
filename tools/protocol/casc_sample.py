"""Export an even sample of files per SNO group from the installed game's CASC storage.

Usage: python -I casc_sample.py <CascLib.dll> <game dir> <out-dir> <per-group> <Group>...
Writes <out-dir>/casc/<Group>/<file> and <out-dir>/names.txt (one "Group\\file" per line, without "Base\\").
"""
import ctypes
import os
import sys

dll = ctypes.WinDLL(sys.argv[1])
dll.CascOpenStorage.argtypes = [ctypes.c_char_p, ctypes.c_uint32, ctypes.POINTER(ctypes.c_void_p)]
dll.CascOpenStorage.restype = ctypes.c_bool
dll.CascOpenFile.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.POINTER(ctypes.c_void_p)]
dll.CascOpenFile.restype = ctypes.c_bool
dll.CascGetFileSize64.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_uint64)]
dll.CascReadFile.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_uint32, ctypes.POINTER(ctypes.c_uint32)]
dll.CascCloseFile.argtypes = [ctypes.c_void_p]
dll.CascFindFirstFile.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_void_p, ctypes.c_char_p]
dll.CascFindFirstFile.restype = ctypes.c_void_p
dll.CascFindNextFile.argtypes = [ctypes.c_void_p, ctypes.c_void_p]
dll.CascFindNextFile.restype = ctypes.c_bool

out, per_group, groups = sys.argv[3], int(sys.argv[4]), sys.argv[5:]
storage = ctypes.c_void_p()
assert dll.CascOpenStorage(sys.argv[2].encode(), 0xFFFFFFFF, ctypes.byref(storage))
buf = ctypes.create_string_buffer(4096)
names_out = []
for group in groups:
    names = []
    h = dll.CascFindFirstFile(storage, f"Base\\{group}\\*".encode(), buf, None)
    while h:
        name = buf.raw[:260].split(b"\0", 1)[0].decode(errors="replace")
        if name.lower().startswith(f"base\\{group.lower()}\\"):
            names.append(name)
        if not dll.CascFindNextFile(h, buf):
            break
    names.sort()
    step = max(1, len(names) // per_group)
    picked = names[::step][:per_group]
    os.makedirs(os.path.join(out, "casc", group), exist_ok=True)
    for name in picked:
        f = ctypes.c_void_p()
        if not dll.CascOpenFile(storage, name.encode(), 0, 0, ctypes.byref(f)):
            continue
        size = ctypes.c_uint64()
        dll.CascGetFileSize64(f, ctypes.byref(size))
        data = ctypes.create_string_buffer(max(1, size.value))
        got = ctypes.c_uint32()
        dll.CascReadFile(f, data, size.value, ctypes.byref(got))
        dll.CascCloseFile(f)
        rel = name[len("Base\\"):]
        open(os.path.join(out, "casc", group, rel.split("\\", 1)[1]), "wb").write(data.raw[:got.value])
        names_out.append(rel)
    print(f"{group}: {len(names)} files, sampled {len(picked)}")
open(os.path.join(out, "names.txt"), "w", encoding="utf-8").write("\n".join(names_out))

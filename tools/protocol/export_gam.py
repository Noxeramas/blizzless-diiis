"""Export every GameBalance .gam file from the installed game's CASC storage as Base_GameBalance_<name>.gam.

Usage: python -I export_gam.py <CascLib.dll> <game dir> <out-dir>
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

storage = ctypes.c_void_p()
assert dll.CascOpenStorage(sys.argv[2].encode(), 0xFFFFFFFF, ctypes.byref(storage))
buf = ctypes.create_string_buffer(4096)
names = []
h = dll.CascFindFirstFile(storage, b"Base\\GameBalance\\*", buf, None)
while h:
    names.append(buf.raw[:260].split(b"\0", 1)[0].decode())
    if not dll.CascFindNextFile(h, buf):
        break
os.makedirs(sys.argv[3], exist_ok=True)
for name in names:
    f = ctypes.c_void_p()
    if not dll.CascOpenFile(storage, name.encode(), 0, 0, ctypes.byref(f)):
        continue
    size = ctypes.c_uint64()
    dll.CascGetFileSize64(f, ctypes.byref(size))
    data = ctypes.create_string_buffer(size.value)
    got = ctypes.c_uint32()
    dll.CascReadFile(f, data, size.value, ctypes.byref(got))
    dll.CascCloseFile(f)
    open(os.path.join(sys.argv[3], name.replace("\\", "_")), "wb").write(data.raw[:got.value])
print(f"exported {len(names)} GameBalance files")

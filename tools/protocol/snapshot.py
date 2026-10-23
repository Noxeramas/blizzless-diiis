"""Start Diablo III, wait for it to initialise, and save its .data/.rdata sections plus the module base.

The game builds its message descriptors in .data at startup, so they can only be read from the
running process. Usage: python -I snapshot.py <exe> <out-dir> [wait-seconds]
"""
import ctypes
import ctypes.wintypes as wt
import json
import os
import struct
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pe import PE

PROCESS_QUERY_INFORMATION, PROCESS_VM_READ = 0x0400, 0x0010
k32 = ctypes.WinDLL("kernel32", use_last_error=True)
psapi = ctypes.WinDLL("psapi", use_last_error=True)
k32.OpenProcess.restype = wt.HANDLE
k32.ReadProcessMemory.argtypes = [wt.HANDLE, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t,
                                  ctypes.POINTER(ctypes.c_size_t)]


def module_base(handle):
    mods = (wt.HMODULE * 1024)()
    needed = wt.DWORD()
    if not psapi.EnumProcessModulesEx(handle, mods, ctypes.sizeof(mods), ctypes.byref(needed), 3):
        raise OSError(ctypes.get_last_error(), "EnumProcessModulesEx failed")
    return mods[0]


def read(handle, address, size):
    buf = ctypes.create_string_buffer(size)
    got = ctypes.c_size_t()
    if not k32.ReadProcessMemory(handle, ctypes.c_void_p(address), buf, size, ctypes.byref(got)):
        raise OSError(ctypes.get_last_error(), f"ReadProcessMemory failed at {address:#x}")
    return buf.raw[:got.value]


def rtti_class_name(handle, base, obj):
    """MSVC x64 RTTI: vtable[-1] -> CompleteObjectLocator {.., +0x0C TypeDescriptor rva}; name at TD+0x10."""
    vtable = struct.unpack("<Q", read(handle, obj, 8))[0]
    col = struct.unpack("<Q", read(handle, vtable - 8, 8))[0]
    td_rva = struct.unpack("<I", read(handle, col + 0x0C, 4))[0]
    raw = read(handle, base + td_rva + 0x10, 128)
    return raw.split(b"\0", 1)[0].decode(errors="replace"), vtable - base


def read_attribute_encoders(handle, base, pe, out):
    """The attribute table (0x40-byte records, name at +0x10, encoder pointer at +0x18) lives in .data;
    each encoder is a heap object. Save every encoder's class name and first 0x40 bytes."""
    data = open(os.path.join(out, "data.bin"), "rb").read()
    rdata = open(os.path.join(out, "rdata.bin"), "rb").read()
    rdata_rva = next(s[1] for s in pe.sections if s[0] == ".rdata")
    data_rva = next(s[1] for s in pe.sections if s[0] == ".data")
    name_va = base + rdata_rva + rdata.index(b"\0Axe_Bad_Data\0") + 1
    table_off = data.index(struct.pack("<Q", name_va)) - 0x10
    encoders = {}
    for i in range(4096):
        rec = table_off + i * 0x40
        name_ptr = struct.unpack_from("<Q", data, rec + 0x10)[0]
        if not (base + rdata_rva <= name_ptr < base + rdata_rva + len(rdata)):
            break
        enc = struct.unpack_from("<Q", data, rec + 0x18)[0]
        if enc and enc not in encoders:
            try:
                cls, vt = rtti_class_name(handle, base, enc)
                encoders[enc] = {"class": cls, "vtable_rva": vt, "bytes": read(handle, enc, 0x40).hex()}
            except OSError as e:
                encoders[enc] = {"error": str(e)}
    return {"table_rva": data_rva + table_off, "count": i, "objects": {str(k): v for k, v in encoders.items()}}


def main():
    exe, out = sys.argv[1], sys.argv[2]
    wait = int(sys.argv[3]) if len(sys.argv) > 3 else 25
    os.makedirs(out, exist_ok=True)
    pe = PE(exe)
    game = subprocess.Popen([exe, "-launch"], cwd=os.path.dirname(exe))
    try:
        time.sleep(wait)
        handle = k32.OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, False, game.pid)
        if not handle:
            raise OSError(ctypes.get_last_error(), "OpenProcess failed")
        base = module_base(handle)
        info = {"runtime_base": base, "image_base": pe.image_base, "sections": {}}
        for name, rva, vsize, rawptr, rawsize in pe.sections:
            if name in (".data", ".rdata"):
                data = read(handle, base + rva, vsize)
                open(os.path.join(out, name.strip(".") + ".bin"), "wb").write(data)
                info["sections"][name] = {"rva": rva, "size": len(data)}
        info["attribute_encoders"] = read_attribute_encoders(handle, base, pe, out)
        json.dump(info, open(os.path.join(out, "snapshot.json"), "w"), indent=1)
        print(json.dumps(info))
    finally:
        game.terminate()


if __name__ == "__main__":
    main()

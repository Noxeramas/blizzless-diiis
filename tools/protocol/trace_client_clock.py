"""Read the 2.8.0.99920 client's received/playback ticks and pending message count.

Usage: python -I trace_client_clock.py <output.jsonl> [--seconds 120]
Start while Diablo III is running, then reproduce the lag. This never launches,
pauses, patches, or terminates the game. Samples are approximate, not atomic.
See docs/combat-lag-investigation.md for the native address evidence.
"""
import argparse
import ctypes
from ctypes import wintypes as wt
from datetime import datetime
import hashlib
import json
from pathlib import Path
import struct
import sys
import time

sys.path.insert(0, str(Path(__file__).resolve().parent))
from snapshot import k32, psapi, module_base, read

CLIENT_SHA256 = "753d5f79d12b035b7268fdb255b5b2502ec858d149b6ebb62969a6e92dd23cc4"


class ProcessEntry(ctypes.Structure):
    _fields_ = [("size", wt.DWORD), ("usage", wt.DWORD), ("pid", wt.DWORD),
                ("heap", ctypes.c_size_t), ("module", wt.DWORD), ("threads", wt.DWORD),
                ("parent", wt.DWORD), ("priority", wt.LONG), ("flags", wt.DWORD),
                ("exe", wt.WCHAR * 260)]


k32.CreateToolhelp32Snapshot.argtypes = [wt.DWORD, wt.DWORD]
k32.CreateToolhelp32Snapshot.restype = wt.HANDLE
k32.Process32FirstW.argtypes = [wt.HANDLE, ctypes.POINTER(ProcessEntry)]
k32.Process32NextW.argtypes = [wt.HANDLE, ctypes.POINTER(ProcessEntry)]
k32.OpenProcess.argtypes = [wt.DWORD, wt.BOOL, wt.DWORD]
k32.CloseHandle.argtypes = [wt.HANDLE]
k32.QueryFullProcessImageNameW.argtypes = [wt.HANDLE, wt.DWORD, wt.LPWSTR, ctypes.POINTER(wt.DWORD)]
k32.GetExitCodeProcess.argtypes = [wt.HANDLE, ctypes.POINTER(wt.DWORD)]
psapi.EnumProcessModulesEx.argtypes = [wt.HANDLE, ctypes.POINTER(wt.HMODULE), wt.DWORD,
                                      ctypes.POINTER(wt.DWORD), wt.DWORD]


def find_client():
    snapshot = k32.CreateToolhelp32Snapshot(2, 0)
    if snapshot == ctypes.c_void_p(-1).value:
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        entry = ProcessEntry()
        entry.size = ctypes.sizeof(entry)
        found = []
        more = k32.Process32FirstW(snapshot, ctypes.byref(entry))
        while more:
            if entry.exe.lower() == "diablo iii64.exe":
                found.append(entry.pid)
            more = k32.Process32NextW(snapshot, ctypes.byref(entry))
        if len(found) != 1:
            raise RuntimeError(f"Expected one running Diablo III64.exe, found {len(found)}. Start the game first.")
        return found[0]
    finally:
        k32.CloseHandle(snapshot)


def sample(handle, base):
    pointer = lambda address: struct.unpack("<Q", read(handle, address, 8))[0]
    integer = lambda address: struct.unpack("<i", read(handle, address, 4))[0]
    context = pointer(base + 0x16F5250)
    network = pointer(base + 0x16C6410)
    if not context or not network:
        return {"state": "no_game_context"}
    clock = pointer(context + 0x7B0)
    if not clock:
        return {"state": "no_game_clock"}
    return {"state": "sample", "received_tick": integer(clock + 4),
            "playback_tick": integer(context + 0x54), "queued_messages": integer(network + 0xC8)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.add_argument("--seconds", type=int, default=120)
    args = parser.parse_args()
    if not 1 <= args.seconds <= 600:
        parser.error("--seconds must be between 1 and 600")
    pid = find_client()
    handle = k32.OpenProcess(0x0400 | 0x0010, False, pid)  # query + read only
    if not handle:
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        name = ctypes.create_unicode_buffer(32768)
        length = wt.DWORD(len(name))
        if not k32.QueryFullProcessImageNameW(handle, 0, name, ctypes.byref(length)):
            raise ctypes.WinError(ctypes.get_last_error())
        digest = hashlib.sha256(Path(name.value).read_bytes()).hexdigest()
        if digest != CLIENT_SHA256:
            raise RuntimeError("Client executable differs from the inspected 2.8.0.99920 build; offsets must be reverified.")
        base = module_base(handle)
        with args.output.open("x", encoding="utf-8") as output:
            output.write(json.dumps({"client_sha256": digest, "pid": pid, "interval_ms": 100}) + "\n")
            print(f"Capturing client clock for {args.seconds} seconds to {args.output}", flush=True)
            started = time.monotonic()
            while time.monotonic() - started < args.seconds:
                status = wt.DWORD()
                if not k32.GetExitCodeProcess(handle, ctypes.byref(status)):
                    raise ctypes.WinError(ctypes.get_last_error())
                if status.value != 259:  # STILL_ACTIVE
                    break
                row = {"time": datetime.now().astimezone().isoformat(timespec="milliseconds")}
                try:
                    row.update(sample(handle, base))
                except OSError:
                    # Reads can fail if the client exits between the liveness check and sample.
                    if not k32.GetExitCodeProcess(handle, ctypes.byref(status)) or status.value == 259:
                        raise
                    break
                output.write(json.dumps(row) + "\n")
                output.flush()
                time.sleep(0.1)
    finally:
        k32.CloseHandle(handle)


if __name__ == "__main__":
    main()

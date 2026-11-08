"""Observe only Esc through XWayland, without grabbing or consuming any input.

The caller only arms this for an identified XWayland target. No key data is
persisted; stdout is the bounded READY/ESC protocol. This separate process keeps
Xlib transport failures outside the companion runtime.
"""
import ctypes
import os
import signal
import sys
import time


def escape_down(keymap: bytes, keycode: int) -> bool:
    if len(keymap) != 32 or not 8 <= keycode <= 255:
        raise ValueError("Invalid X11 Escape-key map")
    return bool(keymap[keycode >> 3] & (1 << (keycode & 7)))


def main() -> int:
    if not os.environ.get("DISPLAY"):
        return 2
    parent = os.getppid()
    signal.alarm(2)
    lib = ctypes.CDLL("libX11.so.6")
    lib.XOpenDisplay.argtypes = [ctypes.c_char_p]
    lib.XOpenDisplay.restype = ctypes.c_void_p
    lib.XKeysymToKeycode.argtypes = [ctypes.c_void_p, ctypes.c_ulong]
    lib.XKeysymToKeycode.restype = ctypes.c_ubyte
    lib.XQueryKeymap.argtypes = [ctypes.c_void_p, ctypes.c_void_p]
    lib.XQueryKeymap.restype = ctypes.c_int
    lib.XCloseDisplay.argtypes = [ctypes.c_void_p]
    display = lib.XOpenDisplay(None)
    if not display:
        return 2
    try:
        keycode = lib.XKeysymToKeycode(display, 0xFF1B)
        if not 8 <= keycode <= 255:
            return 2
        data = ctypes.create_string_buffer(32)
        print("READY", flush=True)
        while os.getppid() == parent:
            signal.alarm(2)
            if not lib.XQueryKeymap(display, data):
                return 3
            if escape_down(data.raw, keycode):
                print("ESC", flush=True)
                return 0
            signal.alarm(0)
            time.sleep(0.02)
        return 0
    finally:
        signal.alarm(2)
        lib.XCloseDisplay(display)
        signal.alarm(0)


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError) as error:
        print(type(error).__name__, file=sys.stderr)
        raise SystemExit(2)

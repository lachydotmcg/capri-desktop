#!/usr/bin/env python3
"""Wait for the private test desktop's window manager and compositor."""

import ctypes
import ctypes.util
import time

x11 = ctypes.CDLL(ctypes.util.find_library("X11"))
x11.XOpenDisplay.argtypes = [ctypes.c_char_p]
x11.XOpenDisplay.restype = ctypes.c_void_p
x11.XInternAtom.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_int]
x11.XInternAtom.restype = ctypes.c_ulong
x11.XGetSelectionOwner.argtypes = [ctypes.c_void_p, ctypes.c_ulong]
x11.XGetSelectionOwner.restype = ctypes.c_ulong
x11.XCloseDisplay.argtypes = [ctypes.c_void_p]
display = x11.XOpenDisplay(None)
if not display:
    raise SystemExit("The private X11 display is unavailable")
try:
    atoms = [x11.XInternAtom(display, name, 0) for name in (b"WM_S0", b"_NET_WM_CM_S0")]
    deadline = time.monotonic() + 20
    while not all(x11.XGetSelectionOwner(display, atom) for atom in atoms):
        if time.monotonic() >= deadline:
            raise SystemExit("Private window manager/compositor readiness timed out")
        time.sleep(0.05)
finally:
    x11.XCloseDisplay(display)

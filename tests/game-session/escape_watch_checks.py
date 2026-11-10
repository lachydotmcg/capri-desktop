"""Pure Escape bit-map checks: no DISPLAY, input injection or desktop capture."""
import importlib.util
from pathlib import Path

source = Path(__file__).resolve().parents[2] / "cross/bridge/escape_watch.py"
spec = importlib.util.spec_from_file_location("escape_watch", source)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

count = 0
for keycode in (8, 9, 63, 64, 127, 128, 255):
    bits = bytearray(32)
    assert not module.escape_down(bits, keycode)
    bits[keycode >> 3] = 1 << (keycode & 7)
    assert module.escape_down(bits, keycode)
    for other in (8, 9, 63, 64, 127, 128, 255):
        assert module.escape_down(bits, other) == (keycode == other)
    count += 1
for bits, keycode in ((bytes(31), 9), (bytes(33), 9), (bytes(32), 7), (bytes(32), 256)):
    try:
        module.escape_down(bits, keycode)
    except ValueError:
        count += 1
    else:
        raise AssertionError("Invalid key-map boundary was admitted")
print(f"PASS Escape-only bit-map checks={count}; native focused-window observation UNVERIFIED")

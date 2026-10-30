"""Validate helper payloads without a compositor connection or input injection."""
import os
from pathlib import Path
import subprocess
import sys
import tempfile

helper = Path(sys.argv[1]).resolve()


def validate(payload, code, label):
    result = subprocess.run(
        [str(helper), "--validate-text"],
        input=str(len(payload)).encode() + b"\n" + payload,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        timeout=3,
        check=False,
    )
    assert result.returncode == code, (label, result.returncode, result.stderr)
    if code == 0:
        assert result.stdout == b"KEYMAP_VALID\n", (label, result.stdout)
    print("PASS", label)


validate("Capri  Ελληνικά 日本語 😀".encode(), 0, "UTF-8 produces a native XKB keymap")
validate(b"\xf0\x80\x80\x80", 7, "overlong UTF-8 is rejected")
validate(b"\xed\xa0\x80", 7, "surrogate UTF-8 is rejected")
validate(b"\xf4\x90\x80\x80", 7, "out-of-range Unicode is rejected")
validate(b"a" * 513, 7, "more than 512 keystrokes is rejected")
validate("".join(chr(0x1000 + index) for index in range(241)).encode(), 7,
         "more than 240 distinct key symbols is rejected")

with tempfile.TemporaryDirectory(prefix="capri-no-wayland-") as runtime:
    environment = dict(os.environ, XDG_RUNTIME_DIR=runtime,
                       WAYLAND_DISPLAY="absent-test-socket")
    result = subprocess.run([str(helper), "--probe"], env=environment,
                            capture_output=True, timeout=3, check=False)
    assert result.returncode == 3, result.returncode
    print("PASS unavailable Wayland session fails without attempting another backend")

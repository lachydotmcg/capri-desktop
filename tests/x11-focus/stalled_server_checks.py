"""Exercise real native connection stalls using a private loopback socket only."""
import os
from pathlib import Path
import socket
import struct
import signal
import subprocess
import sys
import tempfile
import time

program = Path(sys.argv[1]).resolve()


def read_exact(connection, size):
    result = b""
    while len(result) < size:
        part = connection.recv(size - len(result))
        assert part, "native observer disconnected before its read-only request"
        result += part
    return result


for mode, stage in (("cancel", "connect"), ("deadline", "connect"),
                    ("cancel", "reply"), ("deadline", "reply"),
                    ("focused", "reply"), ("proxy", "reply"), ("unfocused", "reply")):
    with socket.socket() as server, tempfile.TemporaryDirectory(prefix="capri-x11-focus-") as private:
        server.bind(("127.0.0.1", 0))
        server.listen(4)
        server.settimeout(3)
        port = server.getsockname()[1]
        assert port > 6000
        environment = dict(os.environ, DISPLAY=f"127.0.0.1:{port - 6000}",
                           XAUTHORITY=str(Path(private) / "absent-authority"),
                           WAYLAND_DISPLAY="absent-owned-focus-fixture")
        process = subprocess.Popen(["dotnet", str(program), mode], env=environment,
                                   stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        child = None
        child_handle = None
        try:
            with server.accept()[0] as connection:
                # Accept the X11 connection but never answer its setup request.
                connection.settimeout(1)
                setup = read_exact(connection, 12)
                assert setup[0] == ord("l"), "fixture expects host little endian protocol"
                name, data = struct.unpack_from("<HH", setup, 6)
                read_exact(connection, (name + 3) // 4 * 4 + (data + 3) // 4 * 4)
                children = Path(f"/proc/{process.pid}/task/{process.pid}/children").read_text().split()
                assert len(children) == 1, ("one observer child despite concurrent requests", children)
                child = int(children[0])
                child_handle = os.pidfd_open(child)
                if stage == "reply":
                    # A minimal successful X11 setup, then either delay the
                    # actual focus reply or answer only the read-only requests.
                    connection.sendall(struct.pack("<BBHHHIIIIHHBBBBBBBB4x", 1, 0, 11, 0, 8,
                                                   1, 0x200000, 0x1fffff, 0, 0, 65535,
                                                   0, 0, 0, 0, 32, 32, 8, 255))
                    request = read_exact(connection, 4)
                    assert request == bytes([43, 0, 1, 0]), request
                    if mode in ("focused", "proxy", "unfocused"):
                        target = {"focused": 123, "proxy": 456, "unfocused": 0}[mode]
                        connection.sendall(struct.pack("<BBHII20x", 1, 0, 1, 0, target))
                        if mode == "proxy":
                            request = read_exact(connection, 8)
                            assert request == struct.pack("<BBHI", 15, 0, 2, 456), request
                            connection.sendall(struct.pack("<BBHIIIH14x", 1, 0, 2, 0, 1, 123, 0))
                start = time.monotonic()
                output, error = process.communicate(b"cancel\n" if mode == "cancel" else None, timeout=3)
                assert process.returncode == 0, (output, error)
                assert time.monotonic() - start < 1.8
                assert not Path(f"/proc/{child}").exists(), "observer child was not killed and reaped"
                server.settimeout(0.05)
                try:
                    extra, _ = server.accept()
                    extra.close()
                    raise AssertionError("repeated Open started an extra native observer")
                except TimeoutError:
                    pass
                print(output.decode().strip(), "at", stage)
        finally:
            if process.poll() is None:
                process.kill()
                process.wait(timeout=3)
            if child_handle is not None:
                # The retained pidfd cannot refer to a recycled process ID.
                try:
                    signal.pidfd_send_signal(child_handle, signal.SIGKILL)
                except ProcessLookupError:
                    pass
                finally:
                    os.close(child_handle)

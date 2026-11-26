import hashlib
import http.client
import json
import os
from pathlib import Path
import stat
import subprocess
import sys
from contextlib import contextmanager
from tempfile import TemporaryDirectory
import time
import unittest

exe = Path(sys.argv.pop(1)).resolve()
state = {"kind": "state"}


@contextmanager
def bridge():
    with TemporaryDirectory() as name:
        root = Path(name)
        path = root / "config/Capri/companion-listener.json"
        path.parent.mkdir(parents=True)
        path.write_text("{}")
        path.chmod(0o644)
        inode = path.stat().st_ino
        run = root / "run"
        run.mkdir(mode=0o700)
        env = dict(os.environ, HOME=name, XDG_CONFIG_HOME=str(path.parent.parent),
                   XDG_RUNTIME_DIR=str(run), XDG_DATA_HOME=str(root / "data"),
                   XDG_CACHE_HOME=str(root / "cache"))
        for key in ("DBUS_SESSION_BUS_ADDRESS", "WAYLAND_DISPLAY", "HYPRLAND_INSTANCE_SIGNATURE"):
            env.pop(key, None)
        with subprocess.Popen([exe, "--companion-bridge", "--no-mods"], env=env) as app:
            try:
                end = time.monotonic() + 15
                while True:
                    try:
                        route = json.loads(path.read_bytes())
                        if "token" in route:
                            break
                    except (FileNotFoundError, json.JSONDecodeError):
                        pass
                    if app.poll() is not None or time.monotonic() >= end:
                        raise AssertionError("Capri did not publish companion discovery")
                    time.sleep(.05)
                digest = hashlib.sha256(path.read_bytes()).digest()

                def send(body, status=200, error=None):
                    time.sleep(.005)
                    conn = http.client.HTTPConnection("127.0.0.1", route["port"], timeout=4)
                    try:
                        conn.request("POST", "/event", json.dumps(body) if isinstance(body, dict) else body,
                                     {"X-Capri-Token": route["token"]})
                        response = conn.getresponse()
                        actual = response.read()
                        assert response.status == status, (response.status, actual)
                    finally:
                        conn.close()
                    if error is not None:
                        assert actual == f'{{"ok":false,"error":"{error}"}}'.encode(), actual
                        assert hashlib.sha256(path.read_bytes()).digest() == digest
                    else:
                        assert json.loads(actual)["ok"] is True, actual
                    return json.loads(actual)

                yield send, env, path, inode
            finally:
                if app.poll() is None:
                    app.terminate()
                    try:
                        app.wait(5)
                    except subprocess.TimeoutExpired:
                        app.kill()


class CompanionChecks(unittest.TestCase):
    def test_rewrite_mode(self):
        with bridge() as (_, _, path, inode):
            self.assertEqual(path.stat().st_ino, inode)
            self.assertEqual(stat.S_IMODE(path.stat().st_mode), 0o600)

    def test_quota_excludes_refusals(self):
        with bridge() as (send, _, _, _):
            send(b" " * 8193, 413, "too_large")
            send(b"", 400, "bad_json")
            send({"kind": "guard", "name": "call", "on": "on"}, 200, "bad_guard")
            for _ in range(60):
                send(state)
            send(state, 429, "rate_limited")

    def test_allowlists_and_dispatcher_expiry(self):
        with bridge() as (send, _, _, _):
            for payload, error in [
                ({"kind": "react", "name": "invalid"}, "unknown_react"),
                ({"kind": "guard", "name": "call"}, "bad_guard"),
                ({"kind": "ambient", "event": "invalid"}, "unknown_event"),
                ({"kind": "show", "target": "invalid"}, "bad_target"),
                ({"kind": "quiet", "on": "false"}, "bad_quiet"),
                ({"kind": "say", "text": " "}, "bad_text"),
                ({"kind": "say", "text": "a" * 281}, "bad_text"),
                ({"kind": "invalid"}, "unknown_kind"),
            ]:
                send(payload, 200, error)
            send({"kind": "quiet", "on": False})
            send({"kind": "guard", "name": "call", "on": True})
            send({"kind": "say", "text": "a" * 280, "source": "ambient"})
            send({"kind": "react", "name": "perch"})
            send({"kind": "show", "target": "pet"})
            self.assertIn("call", send(state)["guards"])
            time.sleep(19)
            send({"kind": "ambient", "event": "idle"})
            time.sleep(17)
            self.assertIn("call", send(state)["guards"])
            time.sleep(36)
            expired = send(state)
            self.assertEqual((expired["guards"], expired["quiet"], expired["userQuiet"]),
                             ([], False, False))


if __name__ == "__main__":
    unittest.main()

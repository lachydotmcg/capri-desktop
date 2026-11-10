"""Open only Capri's official auth URL and await the actual portal result."""
import secrets
import os
import ctypes
import signal
import sys
from urllib.parse import urlparse
import dbus
from dbus.mainloop.glib import DBusGMainLoop
from gi.repository import GLib

owner=os.getppid()
if owner<=1 or ctypes.CDLL(None,use_errno=True).prctl(1,signal.SIGTERM,0,0,0)!=0 or os.getppid()!=owner:
    raise SystemExit(125)
url = sys.stdin.buffer.read(16000).decode()
p = urlparse(url)
allowed=(p.netloc=='auth.openai.com' and p.path=='/api/accounts/authorize')
if p.scheme != 'https' or not allowed or p.username or p.password:
    raise SystemExit(2)
DBusGMainLoop(set_as_default=True)
bus = dbus.SessionBus()
loop = GLib.MainLoop()
result = {"response": None}

def response(code, values):
    result["response"] = int(code)
    loop.quit()

def timeout():
    result["response"] = 3
    loop.quit()
    return False

token = "capri_" + secrets.token_hex(12)
sender = bus.get_unique_name().lstrip(":").replace(".", "_")
handle = "/org/freedesktop/portal/desktop/request/" + sender + "/" + token
bus.add_signal_receiver(response, signal_name="Response", dbus_interface="org.freedesktop.portal.Request", path=handle)
portal = bus.get_object("org.freedesktop.portal.Desktop", "/org/freedesktop/portal/desktop")
actual = str(portal.OpenURI("", url, dbus.Dictionary({"handle_token": token, "ask": dbus.Boolean(False)}, signature="sv"), dbus_interface="org.freedesktop.portal.OpenURI"))
if actual != handle:
    bus.add_signal_receiver(response, signal_name="Response", dbus_interface="org.freedesktop.portal.Request", path=actual)
GLib.timeout_add_seconds(40, timeout)
if result["response"] is None:
    loop.run()
# Returning a request handle does not mean a browser opened. Keep the bus alive
# until Response; never print the credential-bearing authorization URL.
raise SystemExit(0 if result["response"] == 0 else result["response"] or 2)

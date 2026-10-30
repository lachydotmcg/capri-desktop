The `capri-input` helper uses the compositor's virtual pointer and keyboard protocols as the current user. Build with `make`; install the executable in `native/` beside Capri. It needs Wayland, xkbcommon and libxcb headers at build time and their shared libraries at runtime. It needs no root, setuid bit, input-device permissions, daemon or autostart entry.

The bounded input protocol and generated declarations were adapted from Mani's earlier Capri desktop-control implementation, with permission from their owner. The generated Wayland declarations retain their original MIT notices (virtual pointer: Josef Gajdusek; virtual keyboard: the contributors named in its files). The helper changes are contributed under the project's license. No legacy application, assets or runtime are required.

Input payloads use stdin, never argv. The helper reports READY and waits for the parent to revalidate the target before input. A drag requires a fresh authorization byte for every segment. Closing stdin, timeout, SIGTERM and disconnect release held input. Native input delivery is not proof that an app accepted the action; Capri always takes a new frame and asks its vision model to assess the effect. Pointer interference is detected; global physical keyboard activity is not observed.

`--x11-focus DECIMAL_XID` is a separate read-only observation mode used by Typer.
It reports `FOCUSED` when the keyboard recipient is that window or its child focus
proxy, and `UNFOCUSED` otherwise. It neither connects to Wayland nor injects input.
Capri permits one observer child at a time, bounds its async read to 750 ms, and
kills and reaps that exact child on timeout/cancellation; the native mode also
has a two-second hard limit. Stalled-server tests use private loopback sockets,
without connecting to the desktop: build `tests/x11-focus/X11FocusChecks.csproj`
then run `tests/x11-focus/stalled_server_checks.py` with its built DLL path.

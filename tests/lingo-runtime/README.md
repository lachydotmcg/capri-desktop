# Lingo runtime probe

Drives a built Capri cross app through its own language paths on a disposable profile: her spoken lines and sys labels
through `Said` and the cross reply seam, the tray, the Memory and Mail sheets, a live language switch, and a tooltip
whose body has no spaces.

Run it from the repository root, on a machine with Xvfb (CI uses the same setup as `ci/check-linux.sh`):

```bash
dotnet build cross/Capri.Cross.csproj -c Release -o out/cross
dotnet build tests/lingo-runtime/LingoProbe.csproj -c Release
env -u WAYLAND_DISPLAY -u HYPRLAND_INSTANCE_SIGNATURE -u DISPLAY -u DBUS_SESSION_BUS_ADDRESS \
    xvfb-run -a -s '-screen 0 1920x1080x24' \
    tests/lingo-runtime/bin/Release/net10.0/LingoProbe out/cross/Capri.dll
```

The probe makes its own private profile directory with a random name under the system temp, prints it as an `INFO`
line, and removes it when it is done. It takes no profile path and never opens a directory it did not create, so a
predictable temp path cannot be pointed at anyone's files. Each check prints `PASS ...` with the value it saw, and the
program exits 0 only when all of them pass. The server-less fake brain sits behind the same `Said.Ask` seam the shipped
app installs, so the probe covers the reply cleaning and the bare-label ask it adds, and one check calls the delegate
`Pet.StartSaid` installed itself with a canned brain, so a missing production call cannot hide behind the fake.

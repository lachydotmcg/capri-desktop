# Cross Lite mode integration

Loads a built `Capri.dll` by reflection and checks Lite mode's Watch state machine and her brain-rest retry on a Mac or Linux build. It is an independent verifier, so it takes the app assembly instead of linking it.

```
dotnet build cross/Capri.Cross.csproj -c Release -o out/cross
env -u WAYLAND_DISPLAY -u HYPRLAND_INSTANCE_SIGNATURE -u DISPLAY -u DBUS_SESSION_BUS_ADDRESS \
    xvfb-run -a -s "-screen 0 1920x1080x24" bash -c '
        openbox & sleep 2; xcompmgr & sleep 1
        HOME="$(mktemp -d)" dotnet run --project tests/cross-lite-integration/ -c Release -- \
            watch "$PWD/out/cross/Capri.dll" "$(mktemp -d)"'
```

The first argument is the mode: `watch` for the Watch my screen state machine through Lite mode, including the setting tool's deferred ask, and `retry` for the brain-rest retry, including a real `Engine` that is busy or still loading. Both print one `PASS` line per check, `passes=N failures=0` and exit 0 when they hold. `retry-base` is the same retry check against a build from before it waited for a late load, so it expects that build's failure; it is for comparing a base build, not a gate on this one.

The UI needs a display: run it under `xvfb-run` as above on a headless Linux box, with `HOME` at a scratch folder, so it never reads or writes a real Capri profile. The probe sets its own scratch settings folder from the last argument.

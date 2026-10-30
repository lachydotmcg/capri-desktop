# Mainframe checks

Run this fixture only on a private X11 display with isolated XDG directories.
It creates settings, opens native controls, and can inject pointer/keyboard input
when `CAPRI_PRIVATE_UI_TEST=1`. It must never be pointed at the owner's display.
Use a high explicitly reserved Xvfb display (191 or above), with both socket and
lock-file collision checks. Do not use `Xvfb -displayfd`: a missing filesystem
alias for a live Xwayland server can make display 0 appear available.

```sh
dotnet build tests/mainframe/MainframeChecks.csproj -c Release
python3 tests/mainframe/run-private.py /absolute/new-evidence-directory
```

The runner requires `Xvfb`, `openbox`, `ffmpeg`, .NET 10 and `libXtst`. It records
an uninterrupted private display capture and runs the actual Avalonia renderer.
The UI fixture deliberately uses `BareDesk`; it does not qualify the desktop
adapter, current Codex account or speech hardware. Captures and `checks.txt` are
written beneath the requested evidence directory. No model inference or
microphone capture is initiated by the fixture.

Focus checks inject a fake compositor into actual Avalonia settings windows.
They verify that pending, locked and unapplied placement never activates a window,
then that successful placement reaches both observations without an error.
The actual focus grant is reported separately: Openbox may reject programmatic
activation, including after the private Shift-key control. That remains
`UNVERIFIED` until actual host qualification. `CAPRI_PRIVATE_FOCUS_TEST_INPUT=1`
enables only that Shift-key control when `CAPRI_PRIVATE_UI_TEST` is off.

Set `CAPRI_PRIVATE_CLIPBOARD_TEST=1` to run the separate clipboard check instead
of keyboard text entry. Private X11 runs currently fail that check because the
clipboard reads empty after `SetTextAsync`. Preserve that negative evidence;
the main suite passing does not qualify clipboard behavior on Hyprland.

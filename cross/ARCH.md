# Arch Linux build and native desktop adapter

Capri keeps the .NET 10/Avalonia application, shared behavior and original sprite
and font assets. Avalonia draws through X11 or XWayland. On Hyprland, desktop
inventory, window operations, pointer coordinates and screenshot jobs use native
compositor interfaces. Other X11 desktops retain the existing adapter; other
Wayland compositors do not gain native computer input from this port.

`Desk.Make()` selects the adapter. `HyprlandDesktop` binds windows to the current
session and process identity; `HyprlandGeometry` maps output and image coordinates.
`ComputerJobs` preserves the existing permission and per-action confirmation
flow. The small `native/capri-input` helper uses advertised virtual pointer and
keyboard protocols, without a daemon or elevated permissions. `grim` captures
the selected visible window/output. Jobs stop on uncertainty rather than retrying
an action whose effect is unknown. Stop, quiet, hide and sleep revoke active jobs.

Native input requires both compositor protocols and the packaged helper. Screen
content goes to the selected vision model only for a requested screen action or
the separate opt-in Watch setting. An installed local text model does not prove
vision support. Check actual model compatibility before relying on computer jobs.

## Build and check

Build dependencies are .NET 10 SDK, make, GCC, pkgconf and Python, plus the runtime
libraries listed in `packaging/arch/PKGBUILD.in`. The app is published with the
.NET runtime; end users do not need the SDK. GIO launches installed desktop
entries, including Steam entries. No coding project directory is required.
The `ttf-font` dependency supplies Avalonia's system fallback font; Capri still
uses its embedded Share Tech Mono font. The .NET 10.0.13 runtime's optional native
trace provider expects `liblttng-ust.so.0`, while Arch's lttng-ust 2.16 provides
`.so.1`. That optional tracing path is unqualified; do not substitute a symlink
between incompatible ABIs. Ordinary application startup does not load it.

From a checkout with its NuGet lock files:

```sh
bash ci/check-linux.sh
bash ci/build-linux.sh /absolute/path/to/new-portable-directory
```

Both commands use locked NuGet restore. The portable directory contains `Capri`,
its managed and native runtime libraries, `native/capri-input` and licenses;
keep those files together. Run `./Capri`
manually from that directory. The build command refuses an existing output
directory. `DEPENDENCIES.json` records the published package versions, metadata
and retained notices, including the .NET runtime's third-party notices.
Speech models are separate optional downloads; see [SPEECH.md](SPEECH.md)
for setup, pinned bytes, terms and the limits of HD engine availability.

For an isolated UI smoke test, install Xvfb, xauth, Openbox, xcompmgr, Python and
D-Bus, then run:

```sh
bash ci/smoke-linux.sh /absolute/path/to/Capri /absolute/path/to/new-evidence
```

This uses a private X11 display, D-Bus session and XDG profile. It does not qualify
native Hyprland input, physical microphones, output audibility or display motion.
The deterministic desktop/computer tests include transformed layouts and stale
targets; simulated mixed-DPI layouts are not physical mixed-DPI qualification.
Physical keyboard interference detection is not provided. Live computer tests
must use an explicitly authorized disposable target, as described in
[`tests/computer/README.md`](../tests/computer/README.md).

## Arch package

Export a committed revision to a new directory, then run `makepkg` as a normal
user in that directory:

```sh
python3 ci/export-arch.py /absolute/path/to/new-package-source --revision HEAD
cd /absolute/path/to/new-package-source
makepkg --noconfirm
```

The exporter uses `git archive`, deterministic gzip metadata and the archive's
actual SHA-256. `SOURCE.json` binds the recipe to its commit and tree; working
directory edits are excluded. The package version uses the commit's own Unix
timestamp and SHA, so a shallow checkout exports the same recipe as a full one.
The resulting PKGBUILD contains a literal source
checksum rather than a moving branch URL. NuGet dependencies are locked. The
clean Arch CI image has a pinned base digest, but package updates resolve from
the current Arch repositories; its `/toolchain-packages.txt` records that exact
environment. This is a source-bound recipe, not a claim of bit-identical output
across different SDK or distribution package versions.

The package installs the app in `/usr/lib/capri`, the manual command in
`/usr/bin/capri`, and a desktop entry. It installs no service or autostart hook.
Install/remove using the normal package manager after reviewing the package.
Removing the package removes program files only; profiles, downloaded models,
keys and histories remain in the user's data directories.

For a staged manual run, set `XDG_CONFIG_HOME`, `XDG_DATA_HOME`, `XDG_CACHE_HOME`
and `XDG_STATE_HOME` to separate directories before launching the portable app.
Keep the real session's `XDG_RUNTIME_DIR` for native compositor/audio access.
No existing profile or account import is automatic. To roll back a staged run,
close that candidate and use the previous installation and its untouched profile.
Installing the package can replace an existing `/usr/bin/capri`; keep that as an
explicit activation step, separate from building and testing.

## Source and asset provenance

The port is based on `lachydotmcg/capri-desktop`'s `mac-linux` application.
`app/Brain.cs`, `Hands.cs`, `Voice.cs`, `VoicePack.cs` and `Ears.cs` remain shared
behavior; Linux boundaries live in `cross/`. All original sprite strips, font,
sequence timing and creator notices are retained. No generated art or alternate
frontend is introduced. The native helper's protocol licenses are in
`cross/native/LICENSE`; speech attribution and native/model provenance are in
`SPEECH.md` and the installed model notices. Tests and a build are technical
evidence, not human visual or voice acceptance.

## Pet movement on Hyprland

The pet uses compositor logical coordinates for its own window movement. A held
left-button drag reads the compositor pointer asynchronously and keeps one
body-local grab point; the character's original flail, click, drop and Shift-drop
animations are unchanged. XWayland is used only for the button mask during this
native drag, because its root pointer can disagree with the visible pointer while
the compositor interpolates an application-requested window position.

On each map, only the current process's `Capri` pet surface receives the floating,
transparent, undecorated, no-position-animation policy. Mainframe and other app
windows retain their separate focus and presentation behavior. No global
Hyprland settings are written. Saved placement records the output name and the
full sprite envelope's normalized position in its work area. Bring uses the
currently focused output; cross-output moves use that output's active workspace
without switching the user's focus. The pet is not pinned to every workspace.

If Avalonia cannot identify a scaled output, the pet can still recover onto the
focused output using its actual compositor window size and work area. This
visibility-only fallback does not invent a display/input mapping: dragging and
mapped ledges stay unavailable, with the reason shown in motion status. Placement
retries at most once a second while visible, and normal movement resumes when a
verified output mapping returns. Bring also unhides and recovers an unmapped pet.

Mainframe and its settings sheets retain the requested viewport when their first
compositor configure is tiled. Their separate owned-window placement floats,
resizes and centers the window on the focused output without applying pet rules.

If output names are unavailable, a unique exact rectangle is accepted; otherwise
viewport fitting uses the observed window-to-DIP size ratio without creating an
input mapping. The original content zoom and scrolling remain unchanged. Passive
viewport changes do not activate the window; an explicit Bring does so only after
the compositor reports the full window inside the visible work area.

Chat placement applies `no_focus` only to this process's exact Bubble/Pill roles,
reapplying it after remap. Typer opens without an early activation request. After
observed placement, an explicit Type action may issue one address-scoped native
focus operation if its original input owner has not changed. This uses normal
Hyprland focus behavior, including its cursor movement; no global setting is
changed. Success requires the same visible owned surface, session/layout,
unlocked desktop, compositor active address and X11 keyboard recipient (including
Avalonia's child focus proxy). The textbox is focused afterward. Native focus is
not rejected merely because Avalonia's activation event arrives late. Hide,
close, a newer open or another selected window cancel that request. Mainframe's
separate, observed-placement activation path is unchanged.
`CAPRI_MOTION_TRACE=/absolute/new-file.jsonl` enables a bounded local diagnostic
trace of pet geometry, capture state and pointer timing. It never logs other
window titles or desktop pixels and refuses to overwrite an existing file.
Leave it unset for normal use. Real multi-output motion still requires native
host review; synthetic geometry tests do not prove mixed-DPI or hotplug behavior.

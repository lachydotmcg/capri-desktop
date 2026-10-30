# Native Mainframe

Settings in the context menu and tray open a native Avalonia Mainframe. Closing
Mainframe or an owned sheet leaves the companion running. The existing settings
shortcuts remain available and share the same `Pet` setters and `Saved` object;
Mainframe does not maintain a second writable settings store.

The visual reference is upstream main
`eb1aecd4831829285044b87111497668024a0695`, `app/Mainframe.cs`, `app/Neon.cs`
and `app/Notice.cs`. This port retains their wine/near-black ground, exact color
tokens, rose labels, cyan values, teal section rules, clipped corners and red
slider handles. The upstream README screenshot depicts the older interface,
so it is not a rendered reference for this version. An exact Windows rendering
comparison has not been performed.

The controls are native buttons, toggle buttons, text boxes and sliders with
keyboard focus and automation names. Arrow pickers also open native menus.
Voice sliders apply settings while moving and request a preview on release.
Mainframe zoom is persisted on release, combines with OS scaling once, and
offers Auto/100/125/150/175/200%. Auto uses 100% where physical panel scaling
cannot be established. Large pages scroll in both directions within the current
work area. Initial loading does not write settings or change permissions.

On Hyprland, windows are placed asynchronously using the active output's mapping
and current workspace. Only the exact owned Mainframe/sheet title is targeted;
the pet's no-focus/window-decoration policy is not applied to settings.

Brain controls use the selected provider's existing connection/model boundary.
Codex / ChatGPT plan is separate from API-key setup. Local Ollama and the existing
optional API providers remain available. Local voice setup supports existing
prebuilt packs, owner-selected model files/reference clips, and loopback voice
servers. The existing ElevenLabs option remains optional. Microphone tests own
and dispose their capture independently and do not start on opening settings.

## Typography and provenance

Bahnschrift is not bundled by upstream and is not redistributed here. Mainframe
uses Barlow Semi Condensed Regular/SemiBold and Barlow Condensed SemiBold from
Jeremy Tribby's official Barlow repository, pinned to
`dc2940e2e04ef4ec96c07e23e0f02aefbddd343b`. The unmodified files, SHA-256 values,
and SIL Open Font License are in `assets/fonts/Barlow-SOURCE.json` and
`assets/fonts/Barlow-OFL.txt`. Actual resolution to the bundled Barlow family is
checked by the UI fixture. This is a lawful condensed fallback, not a claim of
identical Bahnschrift text metrics or Windows rasterization. Character art and
the existing bubble/TALK presentation are unchanged.

## Platform parity

`mainframe-controls.csv` records all requested controls and their native binding
or explicit capability gap. Global hotkey registration, custom cursor behavior,
HD voice GPU rest during games and interpolated perch frame rates are not
implemented by the existing Linux backend. Their absence is described in the
interface; there are no fake toggles. The newer mini-player is outside this port.

The focused fixture in `tests/mainframe` checks real rendering, native pointer
and keyboard controls, settings persistence, zoom/scroll
reachability and child-window lifecycle on an isolated X11 display. Actual
Hyprland placement, current-account inference, physical microphone quality and
human appearance acceptance are separate host checks.

Clipboard paste has an opt-in fixture check. The private X11 environment returned
empty clipboard content immediately after setting it; this remains a failed
check pending comparison with the actual desktop. Keyboard text entry is tested
separately and is not evidence that clipboard paste works.

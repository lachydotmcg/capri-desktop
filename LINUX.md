# Linux native candidate

This port integrates [Lachy’s Capri v0.8.6.1](https://github.com/lachydotmcg/capri-desktop/tree/87d5952305a14dea9defb0e9901b61911fe2c554), retaining its Avalonia Mainframe, artwork, fonts, shared behavior and credits. The original [mochiiwuff source](https://github.com/mochiiwuff/capri-desktop) remains credited. See LICENSE.md, THIRD-PARTY-NOTICES.md and REUSE_LINUX_WORKERS.json. Existing licenses and artwork credits remain unchanged.

UPSTREAM_SOURCE.json records the immutable 617-file release snapshot and its 53-path integration from v0.8.5. UPSTREAM_MAIN_SOURCE.json separately records public main at9ca691b145b2f9396863442d0f91474202600ada and preserves its newer upstream work; the release catalog remains a historical source pin rather than a claim that later upstream files were replaced. Native package/assembly identity is v0.8.6.1. Windows Parakeet/KWS packs are separate from the retained Linux Moonshine and owned local voice runtimes.

The native White-look menu correction supplies the pinned Fluent menu-item, state and submenu resources from the existing palette. It preserves typography, geometry, native host/input handling and renderer. See [menu-theme.md](docs/menu-theme.md) for the reproducible regression and inspected framework source.

## Build and run

Use .NET 10 and the pinned Avalonia 11.3.22 dependencies. On Arch install `dotnet-sdk base-devel python wayland libxkbcommon libxcb fontconfig freetype2 libx11 libice libsm libxrandr libxext icu openssl glib2 libpulse libarchive bubblewrap pipewire ffmpeg python-dbus python-gobject`. A functioning desktop/audio session is needed for native interaction. Hyprland capture uses `grim`; input uses the bundled compositor-protocol helper, not a new root service or udev rule.

`bash ci/check-linux.sh` runs focused, disposable checks. `bash ci/build-linux.sh /absolute/new/output` produces a self-contained directory with helper, bridges and notices. Run its `Capri` executable. No autostart entry is installed. The separate upstream `linux/build.sh` installs an application-menu launcher; use an isolated `XDG_DATA_HOME` if testing it alongside an existing installation. Use a separate `XDG_CONFIG_HOME` when reviewing alongside an existing Capri; each profile allows one native writer.

`python3 ci/export-arch.py /absolute/new/package-source` exports the committed root tree with its checksum-bound PKGBUILD. Build with `makepkg`, and qualify install/remove only in a disposable Arch environment. Installing on the owner host would replace its `capri` command and needs a separate activation decision.

## Existing local voice and ChatGPT account

`tools/migrate-linux-profile.py --help` documents the explicit private export inputs. It reads a consistent backup of the old companion SQLite store, exports completed conversation context, preserves all original records and touched flags in a private versioned archive, and writes native settings without modifying the source. Runtime references are absolute and explicit; the account is referenced, never copied. Keep exported profiles out of Git and review archives, including public-fixture profiles: their recovery archive still contains private source data.

For a new isolated ChatGPT runtime, create a Python venv and install `tools/plan-requirements.lock` with `pip install --require-hashes --only-binary=:all: -r tools/plan-requirements.lock`; retain the wheels and environment. Set the explicit `PlanPython` reference in a stopped/private native profile.

The ChatGPT-plan adapter is a separate native brain provider. It uses the retained OAuth Responses client and the account's selected chat/vision roles. It is not an API-key field or Code/Husk. Imported shared accounts remain read-only: the existing account owner alone refreshes or changes them. The ChatGPT plan button (or explicit `--connect-chatgpt` launch option) opens official browser sign-in for a separate private native-profile account store. Its credential component alone signs in and refreshes under an exclusive writer lock. No other account credentials are copied or changed. Stop or switching providers revokes a pending connection. The UI reports progress and errors; there is no silent provider fallback.

The retained voice adapter reads the existing immutable local profile and its downloaded model/effects. Volume preserves the old cubic curve. Tone and layers remain owned by that profile; native tone controls are disabled for this provider to prevent applying the effects twice. Other voice providers retain upstream controls. TYPE and TALK share the same final transcript route. TALK selects an enumerated physical microphone explicitly, captures one utterance and stops; there is no wake listening. Lock/session/account changes and Stop revoke capture. ASR and speech run in separate, network-isolated bubblewrap children with only their exact model/runtime paths; the decoder does not open an audio device.

For an explicit new download or recovery, `tools/voice_pack_setup.py` retains the original checksum-bound download/stage/profile commands and `tools/kokoro-runtime.lock` pins the Python 3.14 Linux wheels. `tools/asr_setup.py --directory NEW --lock tools/asr-models.lock.json` uses the retained Python 3.11 `tools/asr-requirements.lock` environment and verifies artifact hashes. The voice profile command requires an existing verified runtime profile; it does not invent new effects, reference audio or a voice. Speech never downloads during normal operation. Retain model files, runtime environments, manifests and applicable licenses for offline recovery. The native app requires no old Qt frontend or broad companion service for speech.

## Native actions and limitations

Hyprland inventory covers native Wayland and XWayland windows; focus/close/explicit force-close bind to fresh compositor and process identities. User service actions accept literal user-unit names only; power operations use one logind request. No model-generated shell is executed. Native jobs use fresh foreground pixels, a bounded native-density crop, one action then observation, account/session/focus/target revalidation and held-input cleanup. Stop cancels child work. Owner-pointer and focus changes yield; global physical keyboard interruption is not fully qualified. A whole-monitor request currently uses a bounded upper-left tile; it is not proof that a large desktop was all observed. Browser close-tab remains unavailable until tab-count and after-state verification are reliable.

The pet now uses the native Hyprland output adapter in production. Visual pointer samples carry session/layout identity and ordered publication; computer actions retain their separate fresh, locked-session checks. One asynchronous coalescing writer moves each owned surface in compositor coordinates, switching its output/workspace before positioning. This matters when XRandR and Wayland enumerate displays in different orders. The original sprites, body anchor and character behavior remain in use. Press acquisition captures the event point and observed origin once, fences older samples and pauses autonomous movement below threshold. During a hold, fresh compositor samples schedule only drag work, with one queued UI dispatch and one pending native position. Release settles accepted writes before physics continues; stale/locked/hidden results are discarded. Sprite timing and background habits retain their own cadence. This removes the fixed 33 ms drag scheduler; a configured sample interval is not a claim of delivered display refresh.

Each native window receives a process-local unique WM_CLASS through Avalonia’s public X11 API; PID, class and title bind its compositor address, including duplicate-title sheets. This per-window class can separate settings sheets in taskbar grouping. Owned pet/bubble/pill surfaces use nonfocusable X11 utility windows with override-redirect and an exact process-generation input rule. They accept pointer events without transferring keyboard focus or dispatching a focus restore. Native attributes are queued on Avalonia's own X11 connection before map, without a synchronous round-trip. Normal shutdown disables the process rules; a crash can leave inert, nonce-specific rules until compositor reload. Other applications never match them. Decorative cursor/control-glow classes are excluded from the interactive rule and get scoped no-focus policy so they cannot intercept clicks. Native jobs balance the released control indicator on success/cancel/error; capture suppresses mapped and pending indicators, then restores them. Owned surfaces disable compositor blur, shadow, border, rounding and movement animation, preserving the artwork and authored plate edges. Properties are checked after map and configuration reload. This integration uses the Lua dispatcher API present in the tested Hyprland 0.56.2 build; unsupported compositors report the failure rather than changing global settings.

TYPE uses a visible-session-only X11 pointer grab on Avalonia's own connection plus compositor `stay_focused` routing. This combination handles both native Wayland and XWayland outside clicks, without pointer confinement or click replay. Escape preserves ordinary drafts; secret entry restores the separate ordinary draft. The Avalonia 11.3.22 raw-input and X11 connection access is isolated in `ComposerInput.cs` and `NativeX11.cs`, with explicit compatibility checks and bounded asynchronous remap retries. Hide, close, acquisition errors and unavailable/locked desktop observation release the resources. Linux Mainframe X closes its sheets and timers while the pet continues; fresh ordinary Linux startup no longer opens settings implicitly. Explicit account setup remains available.

The actual context-menu root is prepared as a managed native popup before its first map, providing keyboard focus without a warp-capable activation request. Root and submenu hosts follow the owner's topmost setting. A menu-only pointer grab preserves same-client submenu routing and consumes outside press/release pairs. Menu and composer grabs share a lease so disposing an older session cannot release a newer grab. No extra menu window or global raise loop is used.

Native fixture sources are in `tests/native-ui`; they use the production factory/focus path and a disposable profile, not Smoke mode. The desktop suite includes delayed-pointer, layout and lock-publication regressions. Recordings and the review matrix distinguish technical interaction results from human motion approval. Negative-origin, mixed-scale/rotated physical displays, live hotplug/lock, physical microphone and long-run resource qualification remain conditional or deferred; passing synthetic fixtures or Xvfb does not qualify them.

Implementation references: [Avalonia 11.3.22 X11 window/input ownership](https://github.com/AvaloniaUI/Avalonia/blob/11.3.22/src/Avalonia.X11/X11Window.cs), [its popup input observer](https://github.com/AvaloniaUI/Avalonia/blob/11.3.22/src/Avalonia.Controls/Primitives/Popup.cs), and [Hyprland 0.56.2 input routing](https://github.com/hyprwm/Hyprland/blob/v0.56.2/src/managers/input/InputManager.cpp). These APIs were inspected; framework source and upstream artwork were not replaced.

## Recovery

Exit only the isolated native candidate, keep its profile and new drafts/history, and launch the unchanged normal installation. Do not copy an old SQLite backup over current user data. Migration never overwrites an existing destination. Re-export into a new private destination if needed. Uninstalling the candidate package removes application files, not profile records or retained model/runtime directories.

Computer actions use the documented Responses `text.format` JSON schema, then independent native validation; refusals, incomplete or malformed results never authorize input. API reference: https://developers.openai.com/api/reference/resources/responses/methods/create .

### Native terminal jobs and reply layout

Linux exposes `linux_system`, `run_terminal` and `terminal_job` in the actual tool
catalog. Commands run as the desktop user in the requested absolute directory
(default: the user's home), without a coding-project root. The configured
`TERMINAL` executable may be Kitty, Foot, Alacritty or xterm; Kitty is the default.
A visible command opens its terminal directly. `SHELL` selects the shell, with
`/bin/sh` as fallback. Launcher arguments are separate argv values; only the
explicit command is interpreted by that shell. The screenshot worker still
cannot type shell commands.

Jobs return an ID, observed state, cwd, output cursor and exit code when known.
`terminal_job` reads or stops that exact ID. Reads return a bounded current tail
when the byte watermark advances; tails may overlap and report truncation.
Output is capped at 256 KiB raw and 8,000 UTF-16 units per returned grapheme-safe
tail. ANSI controls are removed from the readable view. At most eight jobs run
concurrently; completed receipts are bounded to 32 retained jobs per app process.
Private job directories are mode 0700. Visible jobs have an owned PTY, support
native keyboard input and leave normal OS authentication in the terminal.
Background jobs have no input relay; stop a command waiting for input and rerun
it visibly. No hidden elevation or network control listener is installed.

Stop sends TERM to the job's terminal session using revalidated PID descriptors;
it covers job-control process groups and preserves unrelated terminals. A job
that ignores termination remains `stopping`; it is never automatically force
killed. Deliberately daemonized processes that create a new session are not
contained. These are owner-authorized commands, not an untrusted-code sandbox.
Explanations and conditional/negative requests do not authorize execution;
privileged/destructive command primitives require their exact command in the
owner's request. This intent check is defense in depth, not a general shell
safety parser. Existing Security toggles still apply. Explicit Stop, account or
session changes, permission changes and shutdown cancel active jobs; asking
about a running job does not cancel it. The supervisor also checks its owner's
PID/start identity and requests termination after one hour.

Bubble layout measures the original mono font, preserves explicit/blank lines,
expands tabs and reveals complete grapheme clusters. The same layout owns panel
height, row positions and caret; readable text is separate from decorative
entry/exit effects. Mainframe buttons retain their appearance but accept input
through their entire bounds, including the transparent area around the X.

The Mainframe header remains fixed above the scrolling settings body, including
at enlarged app scales. Shutdown retires private job receipts after cancellation;
ordinary completed receipts remain available while this app process is running.

The Hyprland reply surface also carries its measured size through the existing
identity-bound compositor writer. Native short-to-long transitions previously
retained the old compositor height and vertically scaled the larger buffer.
Size and position now share the existing layout/lifecycle fences; the change
does not resize the pet, composer, menus or Mainframe.

## Requested screen vision

`look_at_screen` carries the owner's actual visual question and optional `screen`
through the native handler. Omitted selection keeps the recent visible app crop.
On Hyprland, connected connector names select one display; numbers/ordinals use
physical left-to-right, then top-to-bottom order rather than IPC enumeration.
`left`/`right` select physical edges; `left of here`/`right of here`, `up`/`down`
and `next`/`previous` are relative to the pointer display. `here`, `focused`,
`portrait` and `other` are accepted when unique. Unknown or ambiguous selections
return connected choices. This compositor exposes no primary-display identity,
so `main`/`primary` asks for a position or connector name. Other desktop backends
retain their existing screenshot support and report unsupported explicit selection.

Fresh read-only observation ignores unrelated focus/title/window activity while
preserving session, lock, layout, output and cancellation checks. Window crops
also retain exact window/process/geometry identity. At most three fresh captures
retry typed observation revocations; input actions and inference never replay.
The complete selected area is scaled to a maximum 2048-pixel edge, preserving its
logical mapping. The actual task reaches the selected vision model with room for
analysis. Chess advice is based on the captured board: metadata equality cannot
prove unchanged application pixels, and this repair adds no autonomous chess play.
Strict computer input guards remain separate and unchanged.

Set `CAPRI_VISION_TRACE` to a new absolute file for optional local diagnosis.
The file is created privately, never replaces an existing path, and records at
most 128 metadata records/256KiB. Request text is hashed; images, credentials and
window titles are excluded. Traces distinguish selection/capture/retry/model
stages, epochs, frame dimensions, dispatch count and elapsed time. Public-fixture
helpers separately retain their known public prompts and image hashes.

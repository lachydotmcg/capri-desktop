# Capri Enhanced development

Branch: feature/capri-enhanced. Origin: Shaken89/capri-desktop.
Changes are confined to the development fork and branch; no upstream modification or release publication is performed.

## Build and test

Install .NET SDK 8 and the .NET Framework 4.8 targeting pack on Windows.
From the repository run `./scripts/build.ps1` in PowerShell, or:

```
dotnet build app/Capri.csproj -c Release -o out/app
dotnet run --project tests/PolicyTests.csproj -c Release
```

Output: out/app/Capri.exe. All original sprite strips, icon and font are embedded.
The application continues targeting .NET Framework 4.8/WinForms; the core test
runner targets .NET 8 so it can run on Linux. No UI framework migration occurred.
Build uses NuGet's Microsoft .NET Framework reference assemblies on Linux.

Run only the newly compiled executable on an interactive Windows desktop:

```powershell
$p = Start-Process ./out/app/Capri.exe -ArgumentList '--smoke-test', "$PWD/out/smoke" -Wait -PassThru
if ($p.ExitCode -ne 0) { throw 'Smoke test failed; inspect out/smoke' }
```

GitHub Actions builds the checked-out event revision on windows-latest and runs
core tests. A seven-day downloadable development ZIP is uploaded for successful builds of
feature/capri-enhanced in Shaken89/capri-desktop. This is the explicitly requested
test artifact, not a release. Review the Actions result for the exact commit;
public release distribution still requires rights clarification.

## Available behavior

Right-click Capri to enable Explore window edges. It is off by default and
requires Idle motion and Sit on windows. Every 25 seconds, an eligible seated
character takes a four-second walk using the existing perched sprite fallback.
If not seated, she tries to drop to a reachable surface. User interaction, chat,
sleep, dragging, cursor riding and menus interrupt exploration. There is a
20-second cooldown after interruption. Movement reverses at an edge and respects
screen bounds. No new sprite artwork or walk cycle is claimed.

Hop on this edge initiates a gravity-based hop on the current support. The lightweight update adds a ballistic jump to a reachable nearby window on the same monitor, falling back to a vertical hop. Existing drop/landing logic is
retained. A disappearing/minimized support now initiates falling, and moving
landing targets are revalidated. Fractional motion is retained between frames,
avoiding integer rounding bias. Cursor riding now uses elapsed-time easing.

Tray settings persist autonomous behavior, exploration speed (25/50/90 px/s),
gravity (6/11/16 times character size), fullscreen pause, and Eco (15 FPS).
Balanced remains 30 FPS; Smooth motion provides up to 60 FPS. Eco takes precedence.
Fullscreen pause is opt-in, hides companion overlays and suspends render updates;
a low-rate foreground check restores them when fullscreen ends. User-hidden
state is preserved. Detection reads window geometry, not application contents.

## Service boundaries

AnimationState/AnimationPolicy retain the previous animation transition extraction.
MotionPhysics contains portable gravity, acceleration/friction, crossing and
fullscreen geometry functions. DesktopBehavior owns decision cooldowns, movement
state and fractional horizontal displacement. Companion integrates these with
existing Ledges window filtering, surfaces, support tracking and layered rendering.
Ledges continues its original polling implementation; WinEvent-based invalidation
and more complete locomotion/social hierarchy remain future work.

The earlier CompanionServices interfaces remain extension contracts, not new AI
providers. Existing Brain/Agent/Voice chat features are preserved. This pass does
not claim Milestones B–D: streaming Ollama settings, local voice, persistent memory,
vision and permission-controlled desktop tools remain to implement and validate.
No new screen capture, microphone recording, command execution or telemetry is added.

## Validation and remaining acceptance work

Linux: core tests execute animation timing, exponential easing, exact gravity at
different frame rates, ballistic hop, acceleration, collision direction,
negative-monitor fullscreen geometry, cooldowns, interruption and default-denied
permissions. The actual application cross-compiles against net48 references.
Deserialization DTOs now use properties and compile without CS0649 warnings.
Compilation is not proof of Windows runtime compatibility.

Windows still required: original smoke suite; move/resize/minimize/close support
windows during walking, hopping and falling; negative-coordinate monitors; mixed
DPI; monitor removal; fullscreen entry/exit; input hit testing; serialized setting
round trips; existing voice/chat behavior; CPU/memory measurements at each FPS.
No original installer or downloaded Capri binary is executed.

This is an incremental Milestone A implementation, not complete Enhanced 2.0.
Remaining A work includes broader jump planning, event-driven window caches,
direction-aware artwork, transition blending and a complete hierarchical state
machine. Complete Windows acceptance before building later milestones on top.

## Sprite pipeline and rights

Keep PNG strips in assets/sprites with existing frame order and dimensions.
Clip defines frame count, FPS and height; resource names are Capri.Sprite.<name>.png.
The SDK project embeds strips automatically. New animation identities must be
registered in AnimationState and mapped explicitly in Clip, with policy tests for
looping and completion. Reuse a documented existing animation when artwork is absent.

No repository-wide license was found. Font assets include an OFL notice; this does
not grant redistribution rights to application source or Capri artwork. Preserve
notices and obtain applicable permission before public distribution.


## Lightweight chatbot update (8 GB laptop target)

The latest requested direction replaces the proposed Ollama milestone with a
built-in rule engine. No Ollama client or AI HTTP provider was added. No model,
Python, Docker, API key or GPU is needed. Original model/voice implementation files
remain for compatibility, but automatic model discovery is removed and normal
Mainframe model setup/refresh actions open built-in chat instead. Legacy voice
controls remain explicitly user-operated; the new chat never activates them.

Click Capri, use the typing pill, or right-click and select Open chat. The WinForms
window offers message bubbles, Enter to send (Shift+Enter for newline), a brief
cancellable typewriter effect, bounded smooth scrolling, fade transitions, New,
Clear, Delete all, a session selector, Private session and Settings. Timers stop
when transitions end; there are no chat worker processes or idle inference tasks.

Try `hello`, `how are you`, `help`, `sit`, `jump`, `sleep`, `wake up`, `follow cursor`,
`stop following`; Russian equivalents include `привет`, `как дела`, `помощь`, `сядь`,
`прыгни`, `спи`, `проснись`, `следуй за курсором`, `остановись`. Commands match complete
normalized phrases; neither response templates nor unknown input execute commands.
Unsupported questions receive an honest rule-engine fallback. Greetings account for
local time; repetition, compliments and character activity have specific responses.
Settings allow English/Russian greeting and fallback variants separated by `|`.

Architecture: IChatProvider / BuiltInDialogue in LocalChat.cs owns dialogue rules;
ChatWindow owns native UI and cancellable typing; ChatStorage owns validated JSON,
atomic replacement and retention. Companion validates typed command enums and
performs the existing character actions on the WinForms UI thread. There is no
network implementation behind the provider interface. AI integration is future work.

History persistence defaults OFF. When enabled, JSON is stored next to existing
Capri settings in the user's LocalApplicationData/Capri directory. It is local
plaintext, not encrypted. Limit: 20 sessions, configurable 2–200 messages per
session, and an 8 MiB file limit. Oldest entries are removed at the limit. Private
sessions are never written. Turning persistence off retains previously saved data;
Clear removes the current saved session and Delete all erases the archive. Settings
are stored separately in chat-settings.json. No conversation content is logged.

Nearby-window jumps use an analytical ballistic reachability check; moving target
geometry is checked during the short flight and descending surface crossings decide
landing. Unreachable targets are rejected. This is one-way ledge physics, not full
body collision with every window. Newly opened obstacles during flight and rapid
window changes still need Windows acceptance testing. No new walking/jumping artwork
is claimed; existing perch/dangle/glitch sprites provide the fallbacks.

Validation added: 22 dialogue/storage/settings checks and 16 physics/animation/
permission test groups execute on Linux. The complete net48 application cross-builds
with zero warnings and errors. The Windows workflow now runs Windows-specific regressions and the original
native smoke suite before packaging. Check its actual result; these executables
have only been compiled, not executed, in the Linux workspace. UI fades, cancellation during close, saved-session restoration,
command execution, actual jumps/landings, DPI and fullscreen behavior need Windows
runtime testing. Low CPU/RAM is a design goal, not a measured claim: profile idle,
chat, window exploration and 15/30/60 FPS on the intended 8 GB Intel laptop.

Build with `./scripts/build.ps1`; run `out/app/Capri.exe` on Windows. No downloaded
original executable is used. Public redistribution remains blocked pending code
and artwork rights clarification.


## First development package and stability pass

See [Windows acceptance checklist](docs/WINDOWS-SMOKE-TEST.md),
[package instructions](docs/README-FIRST.md), and [known issues](docs/KNOWN-ISSUES.md).
`./scripts/build.ps1` selects SDK 8 via global.json, builds with warnings as errors,
runs portable tests and validates embedded resources. On Windows it additionally
runs the WinForms regression executable and original native smoke suite. Successful
runs create a revision-tagged ZIP and SHA-256 checksum in out/artifacts. No installer,
user data, original downloaded binary or credentials enter the package.

Current Linux evidence: 22 chat checks, 19 stability checks and 16 physics/animation
test groups passed; both application and Windows regression runner compiled with
zero warnings/errors. The package utility verifies ZIP contents byte-for-byte.
Interactive Windows behavior and laptop resource measurements remain unverified.

Stability fixes include chat fade/reopen cancellation, immediate shutdown, preserved
corrupt history, defaults for partial settings, airborne/rest animation priority,
reduced hidden/fullscreen polling and resetting trajectories on display changes.
Diagnostics is optional (context menu or --diagnostics), samples once per second
only while open, and shows CPU, private/working-set memory, rendered FPS and state.

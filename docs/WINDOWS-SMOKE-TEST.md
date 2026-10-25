# Windows laptop acceptance checklist

Record Windows version, CPU, RAM, display layout/scales, commit from BUILD-INFO.json,
and results below. Do not mark an item passed without observing it on Windows.
The Linux cross-build does not establish that these checks pass.

## Automated checks

`./scripts/build.ps1` uses .NET SDK 8, compiles net48 with warnings as errors, runs
portable regression tests and verifies the executable's embedded sprite/icon/font
resources. On Windows it also runs the WinForms regression executable and the
original native smoke suite with fresh result files and 180-second process limits.
Any failed command blocks ZIP creation and Actions binary upload. Portable tests
cover chat rules, privacy, retention, invalid/corrupt data, settings defaults,
physics, negative monitor coordinates, animation priority and diagnostic arithmetic.

The Windows regression runner exercises real sprite decoding, repeated chat
open/close, reopening during fade, typing cancellation, character sleep/wake
integration, hide/restore, diagnostics disposal and application shutdown.
This is handler-driven testing, not verification of physical input or all desktops.

## Launch, rendering and shutdown

- [ ] Extract into a clean user folder; launch without elevation or installed SDK.
- [ ] Character is transparent with no opaque rectangle, missing strip, flicker or
      missing icon/font. Check idle, blink, grin/glitch, dangle, sleep/wake and perch.
- [ ] Launch a second instance: it restores the first rather than duplicating it.
- [ ] Try Small, Medium and Large. Character remains visible and clickable.
- [ ] Quit while idle, typing, following cursor and airborne. Process exits; no tray
      ghost, chat, diagnostics or child model process remains.
- [ ] Repeat launch/quit ten times; no error dialog or growing resource count.

## Mouse and chat

- [ ] Single click opens chat; double click still toggles cursor perch; drag still
      works. Transparent character regions pass clicks to the underlying app.
- [ ] Type `hello`, `Привет!`, `как дела`, `thanks`, `help`, repeat a message and ask an
      unsupported question. Responses correctly describe the rule-engine limit.
- [ ] Enter sends; Shift+Enter inserts a newline; Unicode/emoji render correctly.
- [ ] Stop during typing; close and immediately reopen chat 20 times, including
      during fade. No disposed-control exception, frozen input or duplicate reply.
- [ ] Resize chat and change font/opacity. Controls remain reachable at each DPI.
- [ ] Test New/session selection/Clear/Delete all. Clear removes the selected saved
      session; Delete all erases saved conversation content.
- [ ] Enable persistence, send messages, quit/restart and restore them. Check the
      configured retention limit. Settings survive restart.
- [ ] Record the archive hash, use Private session, then quit/restart: private text
      is absent and the saved archive is unchanged. Persistence-off retains existing
      history but writes no new conversations.
- [ ] In a backed-up test profile, replace conversations.json with malformed JSON:
      chat opens with an error, does not silently replace it, and explicit Delete all
      recovers. Restore backups after testing. Repeat with invalid chat settings.

## Character commands and physics

- [ ] `sit` / `сядь` reports truthfully whether an edge is reachable.
- [ ] `jump` / `прыгни` from a suitable window targets a reachable nearby edge, or
      hops vertically when none is available. No teleport to a distant window.
- [ ] Move, resize, cover, minimize and close source/target windows during motion.
      Capri follows valid support, falls or recovers when support disappears, and
      never gets permanently stuck outside the visible desktop.
- [ ] New windows appearing midflight do not cause a crash; report collision gaps.
- [ ] `sleep`, open chat, then `wake up`: chat greeting must not override sleep.
- [ ] `follow cursor`, move cursor, then `stop following`: cursor input remains usable.
- [ ] Chat during a jump must not switch the airborne sprite to idle midflight.
- [ ] Disable Idle motion while falling: physics must finish rather than freeze.
- [ ] Disable window sitting/exploration and confirm that movement respects settings.

## Monitors, DPI and fullscreen

- [ ] Test 100%, 125%, 150% and 200% scaling, including two monitors with different
      scales. Drag between monitors; check character hit testing and chat text.
- [ ] Place a monitor left of/above the primary screen (negative coordinates).
- [ ] Disconnect the current monitor while standing, seated and jumping. Character
      and open chat recover onto an available screen. Reconnect and change primary.
- [ ] Test taskbars on different edges and auto-hide. No permanent off-screen placement.
- [ ] Enable fullscreen suspension; enter/exit a fullscreen video/game. Render FPS
      becomes zero while paused and resumes on exit without a physics leap.
- [ ] Hide manually, enter/exit fullscreen: Capri must remain manually hidden.
- [ ] Restore through tray or second launch while paused; no unexpected overlay over
      fullscreen content. Check again after leaving fullscreen.

## Performance and resources (8 GB / integrated Intel graphics)

Use Diagnostics and Windows Task Manager. CPU is normalized over all logical cores;
FPS counts completed character renders, not monitor refreshes. Record measured data:

| Scenario (2 minutes each) | CPU typical/peak | Private MiB | Working set MiB | FPS | Notes |
|---|---|---|---|---|---|
| Balanced visible idle | | | | | |
| Eco visible idle | | | | | |
| Smooth visible idle | | | | | |
| Hidden | | | | | |
| Fullscreen paused | | | | | |
| Chat/typing | | | | | |
| Exploring/jumping | | | | | |

- [ ] Hidden/paused character FPS is zero and CPU falls toward process baseline.
- [ ] Close diagnostics: its one-second sampling stops.
- [ ] Open/close chat 50 times and change character size 30 times. After settling,
      private memory and Task Manager GDI/USER object counts plateau rather than
      increase without bound. Record start/end values and any exceptions.
- [ ] Run 30 minutes with exploration enabled. No steadily rising CPU/RAM/handles.
- [ ] Verify no Python, Docker, model server, Chromium or GPU inference process starts.

A Windows runner pass plus a completed checklist is required before calling this a
stable laptop build. A ZIP artifact alone is not that evidence.

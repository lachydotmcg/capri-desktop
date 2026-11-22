---
name: capri-parity
description: "How to contribute Capri parity work: port Windows app/ capabilities to cross/ on macOS or Linux, Mainframe, Wayland, smoke checks, commit voice, repo patterns. Load this before touching cross/ or filing a parity proposal."
metadata:
  author: "danielxxomg"
  version: "1.2"
---

# Capri parity skill

Capri ships two implementations. `app/` is the Windows app (.NET Framework 4.8,
C# 5, WinForms/GDI+/WinRT). `cross/` is the Mac and Linux app (.NET 10,
Avalonia), and shares files with `app/` through `cross/Capri.Cross.csproj`.
Parity means the same capability, rebuilt natively on each platform - never
move the Windows implementation across.

## Hard rules

- **Port the capability, not the implementation.** No WinForms, GDI+, UIAutomation,
  WinRT or `user32` code in shared files. Rebuild the behavior in Avalonia.
- **The shared surface is what the csproj links.** Derive it, never trust prose:
  `grep '<Compile Include="../app/' cross/Capri.Cross.csproj`
- **A linked `app/` file must compile as C# 5 and as net10.** No `$"..."` strings,
  `?.`, `nameof` or `=>` members in those files. Cross-only files use modern C#.
- **No code comments.** Match the code around you: plain names, same layout as
  the file you are in.
- **Add a smoke check** for anything new she does. New feature classes register
  their own static `Checks(List<string> checks)` section (see `NiriDesktop.Checks`,
  `MacFingers`) instead of inline blocks.
- **No new network calls, tracking or endpoints.** Keys stay on the user's PC.
- **Her look is strict.** Art, faces and animations need the maintainer's OK
  first, always in their own slice.
- **Commits say what she now does:** lowercase, no prefix, no period - usually
  `she ...`, sometimes `her ...` or `the ...` for docs and repo chores.

## Known impossibles

Do not schedule these as parity work; document them instead.

- **Wayland without a compositor-specific backend:** no foreign-window geometry,
  no global overlays, no global hotkeys. Hyprland, KWin and niri have backends
  (`cross/NiriDesktop.cs` is real and smoke-covered); GNOME falls back to screen
  edges, and its mouse/keyboard jobs work through uinput (`cross/Fingers.cs`)
  when the user's udev permissions allow it - a setup issue, not an
  architectural impossible.
- **A virtual microphone driver:** VB-CABLE is Windows-only. macOS needs
  BlackHole, Linux needs a PipeWire sink; both are user-installed.
- **macOS behavior claims without a tester** who can grant Accessibility and
  Screen Recording.

## Repo patterns

- **New settings need BOTH `[DataContract]` classes** - `Settings` and
  `NewSettings` in `cross/Pet.cs`, with copy-forward in `Newest` and the old-copy
  recovery in `Kept` - because an older copy may rewrite `cross.json` from its
  own shape; the second file `cross-new.json` is what survives that.
- **Off-compositor determinism:** strip the session environment before running
  tests off a live desktop - `unset WAYLAND_DISPLAY HYPRLAND_INSTANCE_SIGNATURE
  DISPLAY DBUS_SESSION_BUS_ADDRESS` under `xvfb-run` (see `ci/check-linux.sh`).
- **Test projects live at `tests/<name>/`** with source-linked files and a local
  `Stubs.cs`; some stay C# 5 and package-free on purpose.
- **Tests reach internals through access-bridge files** (like
  `cross/BrainProviderAccess.cs`), not `InternalsVisibleTo`.
- **Off-Hyprland desktops answer logind:** `Limbs.SessionLocked()` reads
  `LockedHint` (never `IdleHint` - an idle user keeps her microphone), and an
  answer she cannot read never bars her.

## Contribution gates

- Push to your own fork; merging upstream is the maintainer's call. Inspect
  `git remote -v` instead of assuming remote names.
- Big changes need a written maintainer go in an issue or Discord, quoted in
  the issue. No approval label exists; do not invent one.
- One behavior per commit, one capability per pull request. Report
  `git diff --stat`; merged PRs here range from hundreds to thousands of lines,
  so justify size instead of hiding it.
- PR body: What / Testing / Reviewers should know. `Refs #N` per slice,
  `Closes #N` only on the last one.
- CI is flaky and has merged red before. Quote PASS lines and name red jobs;
  never claim green.

## Public reference boundary

- Keep external projects' issue trackers, internal tooling and review processes
  out of capri-desktop issues, PRs and comments - and Capri project work out of
  other repositories' issues. Cross-project backlinks tie unrelated trackers
  together.
- Same-repository cross-references are fine: `#N` between capri-desktop issues
  and PRs, citing an upstream commit, or crediting a merged PR's author.

## Decision gates

| Situation | Action |
|---|---|
| Capability exists only as WinForms/WinRT/GDI+ | Rebuild it in Avalonia; share no widget |
| The change touches a linked shared file | Satisfy C# 5 and net10; do not add or remove comments |
| It needs a Win32/WinRT API | Put it behind a runtime OS check in `cross/Native.cs`, or declare Windows-only |
| It needs new art, animation or a face change | Separate issue, maintainer OK, separate PR |
| It needs a macOS observation | Seek a tester; report unverified behavior or gate it |
| The maintainer says not yet | Record it in the issue and stop; no half-port |
| An issue or PR already covers it | Reuse and link it; never duplicate |
| A Discord thread is the only evidence | Cite the thread id in the issue |

## Slicing and evidence

Slice order: shared core, then one surface, then polish - each slice leaves the
app working. Bigger changes need the written maintainer go first; small fixes
can go straight to a pull request from your own fork.

For an implementation, report the issue link, slices, files, `git diff --stat`,
build result, quoted `PASS` lines, per-OS evidence, and what was not seen. Name
the tester and environment for every manual claim; CI is automated evidence,
not proof of real Windows, macOS or desktop behavior. A real-macOS claim
without a tester is a defect.

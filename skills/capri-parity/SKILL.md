---
name: capri-parity
description: "How to contribute Capri parity work: port Windows app/ capabilities to cross/ on macOS or Linux, Mainframe, Wayland, smoke checks, commit voice. Load this before touching cross/ or filing a parity proposal."
metadata:
  author: "danielxxomg"
  version: "1.0"
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
- **No code comments.** Plain names, same layout as the file you are in.
- **Add a smoke check** for anything new she does: `checks.Add("PASS ...")` in
  `app/Capri.cs` or `cross/Smoke.cs`.
- **No new network calls, tracking or endpoints.** Keys stay on the user's PC.
- **Her look is strict.** Art, faces and animations need the maintainer's OK
  first, always in their own slice.
- **Commits say what she now does:** lowercase `she ...`, no prefix, no period.

## Known impossibles

Do not schedule these as parity work; document them instead.

- **Wayland without a compositor-specific backend:** no foreign-window geometry,
  no global overlays, no global hotkeys. Hyprland and KWin have partial backends;
  GNOME falls back to screen edges.
- **A virtual microphone driver:** VB-CABLE is Windows-only. macOS needs
  BlackHole, Linux needs a PipeWire sink; both are user-installed.
- **macOS behavior claims without a tester** who can grant Accessibility and
  Screen Recording.

## Slicing

One behavior per commit, one capability per pull request. Slice order: shared
core first, then one surface, then polish - each slice leaves the app working.
Bigger changes need a written maintainer go in an issue or Discord first, quoted
in the issue; small fixes can go straight to a pull request from your own fork.

## Evidence contract

For an implementation, report the issue link, slices, files, `git diff --stat`,
build result, quoted `PASS` lines, per-OS evidence, and what was not seen.
Name the tester and environment for every manual claim; CI is automated
evidence, not proof of real Windows, macOS or desktop behavior. A real-macOS
claim without a tester is a defect.

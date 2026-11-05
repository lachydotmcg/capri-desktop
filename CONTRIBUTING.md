# Contributing to Capri

Thanks for helping her out, choom.

## Before you start

- **Small fixes:** open a pull request.
- **New features or big changes:** ask first in the [Discord](https://discord.gg/capridesktop) or open an issue, so nobody builds something that won't be merged.
- **Licence:** Capri is under the [PolyForm Noncommercial License](LICENSE.md). By opening a pull request you agree your changes are shared under it too.

## Where things live

| Folder | What's in it |
|---|---|
| `app/` | The Windows app (C# 5, .NET Framework 4.8, WinForms) |
| `cross/` | The Mac and Linux app (.NET 10, Avalonia). Shares `Brain.cs`, `Engine.cs`, `Hands.cs` and `Voice.cs` with `app/` |
| `assets/sprites/` | Her animation strips |
| `site/` | The website |
| `ci/` | Build scripts the CI uses |

## Build and test

**Windows** (needs Windows 10 or 11 and the Windows 10/11 SDK):

```powershell
./ci/build-windows.ps1 -SmokeTest
```

This builds `out/windows/Capri.exe` and `Capri-Setup.exe`, then runs her smoke test. Every line should say `PASS`.

The smoke output folder also contains `smoke-timings.txt`, a tab-separated diagnostic for completed sections. `inclusive_ms` includes child sections; `own_ms` excludes their measured time, `depth` shows nesting, and `parent` names the enclosing section, so recovery credits only its own completed children. Only sections that ran and reached their exit have rows; interrupted sections and timer steps are not measured here. A new full run clears the file when writable, while worker recovery keeps completed rows and counts a resumed parent's kept children in its inclusive time, so nesting still reads top-down. This optional diagnostic does not change checks, timeouts, or normal app behavior; an unavailable timing file does not fail the checks. Do not compare runs using a timing file that could not be reset.

The section recorder can be tested without Windows using the .NET 10 SDK:

```bash
dotnet run --project ci/smoke-timings/SmokeTimingsTest.csproj -c Release -- /path/to/empty-temp-folder
```

This source-linked test uses C# 5 and no packages. It checks timing rows, nested and filtered sections, empty, interrupted or parent-resumed timing-file recovery, and unchanged check output. It does not run Windows APIs or replace the Windows smoke test.

Package managers run her setup with no window. `Capri-Setup.exe --silent` installs to `%LOCALAPPDATA%\Capri` and saves her Start menu shortcut, and `Uninstall.exe --uninstall --silent` removes her the same way. Each exits 0 when it worked, 1 when it failed and 2 for a command it doesn't know, with the reason on stderr. `--silent --package <name>` also writes `Capri.package` next to `Capri.exe` with the manager's name (`winget`, `scoop`, `chocolatey`, `pacman`); her updater reads it and says to update through that manager instead of updating herself. A plain `--silent` and a manual install mark it `manual`, so her updater updates herself again, and removing her takes the marker with her. The silent install skips the local speech model; get it later in her Mainframe.

**Mac and Linux** (needs the [.NET 10 SDK](https://dotnet.microsoft.com/download)):

```bash
dotnet build cross/Capri.Cross.csproj -c Release -o out/cross
```

Then run her smoke test, and check every line says `PASS`:

```bash
out/cross/Capri --smoke out/cross-smoke
```

On Windows, use `out\cross\Capri.exe` instead. CI runs all three on every pull request.

## House rules

- **Match the code around you.** No code comments, plain names, and the same layout as the file you're in.
- **`app/` is C# 5.** It's built with the `csc.exe` that ships with Windows, so no `$"..."` strings, `?.`, `nameof` or `=>` members.
- **Add a smoke check** for anything new she does. Look for `checks.Add("PASS ...")` in `app/Capri.cs` or `cross/Smoke.cs`.
- **Her look is strict.** Changes to her art or face need Lachy's OK first.
- **No new network calls or tracking** without asking. API keys stay encrypted on the user's PC.
- **Ask before risky actions.** Anything that sends, posts, buys or deletes must go through her ask step, unless the user asked for that exact thing.
- **Commit messages** say what she does now, in plain words. For example: `She dances when music plays`.

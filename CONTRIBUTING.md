# Contributing to Capri

Thanks for helping her out, choom.

## Before you start

- **Small fixes:** open a pull request.
- **New features or big changes:** ask first in the [Discord](https://discord.gg/F4fDHfGtFk) or open an issue, so nobody builds something that won't be merged.
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

**Mac and Linux** (needs the [.NET 10 SDK](https://dotnet.microsoft.com/download)):

```bash
dotnet build cross/Capri.Cross.csproj -c Release -o out/cross
```

Then run her smoke test, and check every line says `PASS`:

```bash
out/cross/Capri --smoke out/cross-smoke
```

On Windows, use `out\cross\Capri.exe` instead. CI runs all three on every pull request.

For the native Hyprland adapter, Linux checks, portable build and Arch package,
see [the Arch build guide](cross/ARCH.md). Local speech setup is documented in
[Local speech on Linux](cross/SPEECH.md).

## House rules

- **Match the code around you.** No code comments, plain names, and the same layout as the file you're in.
- **`app/` is C# 5.** It's built with the `csc.exe` that ships with Windows, so no `$"..."` strings, `?.`, `nameof` or `=>` members.
- **Add a smoke check** for anything new she does. Look for `checks.Add("PASS ...")` in `app/Capri.cs` or `cross/Smoke.cs`.
- **Her look is strict.** Changes to her art or face need Lachy's OK first.
- **No new network calls or tracking** without asking. API keys stay encrypted on the user's PC.
- **Ask before risky actions.** Anything that sends, posts, buys or deletes must go through her ask step, unless the user asked for that exact thing.
- **Commit messages** say what she does now, in plain words. For example: `She dances when music plays`.

# Capri Enhanced — Windows development build

This is a development test build, not a stable release or an installer.

1. Extract the entire ZIP into a new folder in your user profile.
2. Windows 10/11 with .NET Framework 4.8 is required. SDK 8 is needed only to build,
   not to run. There is no model server, Python, Docker, GPU runtime or API key requirement.
3. Compare the ZIP checksum with its adjacent `.sha256` file (`Get-FileHash file.zip
   -Algorithm SHA256`). `SHA256SUMS.txt` records checksums of the extracted files.
4. Run the newly built `Capri.exe`. Do not run it as administrator. This build is
   unsigned; do not disable Defender or antivirus protections to test it.
5. Click Capri or choose **Open chat** in her context menu. Type `help` or `помощь`.
6. Right-click Capri to choose movement, gravity, fullscreen pause and performance
   settings. Eco is 15 FPS, Balanced approximately 30 FPS, Smooth up to 60 FPS.
   Actual achieved FPS depends on Windows timer scheduling and hardware.
7. Use **Diagnostics**, or launch `Capri.exe --diagnostics`, to view process CPU,
   private/working-set memory, actual render FPS and animation state. Closing this
   window stops diagnostic sampling; it writes no log and sends no telemetry.
8. Quit through Capri's tray/context menu. Closing chat does not quit the companion.

Chat is a built-in English/Russian rule engine, not a language model. History
persistence is OFF by default. Settings and optional plaintext history reside in
`%LOCALAPPDATA%\Capri`. Private sessions are not persisted. Turning persistence off
retains previously saved conversations; use Clear or Delete all to erase them.
Do not put secrets into chat. No screen capture or microphone is enabled by chat.

For an isolated native smoke run, quit any test instance and use:

```powershell
$folder = Join-Path $env:TEMP ('capri-smoke-' + [Guid]::NewGuid().ToString('N'))
$p = Start-Process .\Capri.exe -ArgumentList '--smoke-test', ('"' + $folder + '"') -Wait -PassThru
$p.ExitCode
Get-Content (Join-Path $folder 'smoke-test.txt')
```

This runner uses app-owned test windows, fake provider responses and isolated data.
It creates diagnostic images of its own test backdrop. Use a non-sensitive test
Windows session. A nonzero exit or missing fresh result file means failure.

Read WINDOWS-SMOKE-TEST.md and KNOWN-ISSUES.md before judging stability. BUILD-INFO.json
identifies the exact commit; a `-dirty` suffix means local uncommitted changes were
included. Preserve that ID when reporting a bug. There is no auto-update to an
upstream executable in this development build. Legacy optional voice controls are
retained, but the basic companion/chat needs no external service.

Redistribution rights for the original code and artwork remain unresolved; see
NOTICE.md. This package is for requested development testing, not a public release.

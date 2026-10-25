## Capri Enhanced — first Windows development prerelease

A lightweight Windows Forms desktop companion with original Capri artwork,
English/Russian rule-based chat, optional local history/private sessions,
window-edge exploration, ballistic jumps and 15/30/60 FPS modes.

### Download and run

Download the attached development ZIP and its `.sha256` checksum, extract the ZIP,
and run `Capri.exe` on Windows 10/11 with .NET Framework 4.8. SDK 8 is needed only
for source builds. No Python, Docker, local language model, GPU inference or API
key is required for the companion and built-in chat.

Use **Open chat** or click Capri; type `help` or `помощь`. **Diagnostics** shows
process CPU, memory, rendered FPS and animation state. The ZIP includes the full
Windows smoke-test checklist, known issues, checksums and exact source revision.

### Validation and limitations

The publication job runs only after the Windows build, portable regressions,
WinForms lifecycle tests, original native smoke suite and package validation pass.
Check the associated Actions run for the exact results. Real laptop testing,
mixed-DPI setups, rapid window changes and measured resource use remain manual
acceptance work. This is an unsigned development prerelease, not a stable release.

History is local plaintext and persistence defaults off. Chat does not activate
microphones, screen capture or external AI providers. Corrupt history is preserved
until explicit deletion. Do not disable Windows security protections to test.

Original application/artwork redistribution rights remain unresolved. Publication
of this requested test prerelease does not grant rights to those works. Preserve
ownership notices and review `NOTICE.md` before further redistribution.

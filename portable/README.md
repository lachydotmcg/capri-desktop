# CAPRI Portable USB (Windows)

CAPRI Portable runs the desktop companion from a USB drive. On first launch it downloads the official standalone Ollama Windows CLI/runtime into the USB data folder, downloads a free local vision-capable model into the same drive, and keeps CAPRI's own settings, conversations, memory and logs on the USB.

## Requirements

- Windows 10 22H2 or Windows 11, 64-bit.
- Internet access for the first runtime/model downloads. After these downloads, local chat can work offline.
- A USB 3.x flash drive or external SSD. 16 GB free is a practical minimum; 32 GB or more is recommended. Faster storage improves model startup and inference.
- 8 GB system RAM minimum; 16 GB or more is recommended for the larger starter model.
- Windows GPU drivers are used when compatible. CPU inference remains available, but can be slower.

## Quick start

1. Build/download the CAPRI-Portable.zip artifact from the capri-portable-usb-windows GitHub Actions run.
2. Extract the ZIP directly to the root of your USB drive. Capri.exe and Start-Capri-Portable.cmd must sit next to each other.
3. Double-click Start-Capri-Portable.cmd.
4. On first launch, the script downloads the standalone Ollama runtime into CAPRI-Data\Runtime\Ollama and a model into CAPRI-Data\Models. It chooses gemma3:1b below 16 GB RAM and gemma3:4b at 16 GB RAM or above. Both are free local vision-capable models.
5. When CAPRI opens, right-click her and open Capri Mainframe. Under BRAIN, select the local Ollama option and the model downloaded by the launcher if it is not already selected. You do not need a paid API key for local inference.
6. Keep the USB connected while CAPRI is running. Close CAPRI normally before ejecting the drive.

## USB folder layout

- Capri.exe — desktop companion.
- Start-Capri-Portable.cmd — double-click launcher.
- portable\Start-Capri-Portable.ps1 — runtime/model setup and launch logic.
- CAPRI-Data\AppData — CAPRI settings, conversations, memory and app logs.
- CAPRI-Data\Models — local Ollama model blobs.
- CAPRI-Data\Runtime\Ollama — standalone Ollama runtime and GPU libraries.
- CAPRI-Data\Logs — Ollama stdout/stderr logs.
- CAPRI-Data\Downloads — reserved for downloaded files.

## Useful options

Open PowerShell in the extracted USB folder and run:

- .\portable\Start-Capri-Portable.ps1 — automatic model choice.
- .\portable\Start-Capri-Portable.ps1 -Model gemma3:1b — choose the smaller model.
- .\portable\Start-Capri-Portable.ps1 -Model gemma3:4b — choose the larger model.
- .\portable\Start-Capri-Portable.ps1 -SkipModelDownload — start without pulling a model (only useful if a model is already on the drive).

The launcher refuses to attach to another process already listening on port 11434, because that server might be storing models on the PC rather than the USB. Close any other Ollama instance and relaunch from the USB.

## Important limitations

- This portable package targets Windows. It does not make Windows desktop automation portable to macOS or Linux.
- The Ollama standalone runtime is stored on the USB, but still depends on the host PC's Windows version, drivers and hardware. GPU acceleration varies by machine; CPU mode is the fallback.
- Model downloads are large and first startup can take time. Keep the USB connected and do not remove it during downloads or while CAPRI is running.
- Local model quality and speed depend on RAM, GPU and USB speed. A 1B model is lighter but less capable; 4B is more capable but needs more memory.
- CAPRI's existing security controls remain in place. Do not enable its broad permission-bypass mode unless you fully understand the consequences.
- The launcher does not delete existing files on the USB. It only creates/updates its own CAPRI-Data folder.

## Troubleshooting

- **PowerShell does not open:** double-click Start-Capri-Portable.cmd; it launches Windows PowerShell itself.
- **Not enough storage:** use a larger/faster drive and leave at least 8 GB free before first setup.
- **Ollama fails to start:** inspect CAPRI-Data\Logs\ollama.stderr.log; make sure port 11434 is not in use.
- **CAPRI says its brain is offline:** open Capri Mainframe, select the local Ollama brain and choose the model shown in the local model list.
- **Slow answers:** use a faster USB 3.x drive, close other apps, or choose a smaller model with -Model gemma3:1b.

# CAPRI Portable USB (Windows)

This package runs the CAPRI desktop executable from the USB drive and configures Ollama to store downloaded model files on that same drive.

## Quick start

1. Extract the complete CAPRI-Portable package to the root of a USB drive.
2. Install the free Ollama Windows runtime on a PC once. The runtime is a Windows prerequisite; CAPRI's model data is stored on the USB.
3. Close any already-running Ollama instance from the Windows system tray.
4. Double-click Start-Capri-Portable.cmd.
5. The launcher detects system RAM, creates the USB data folders, starts a private Ollama server with OLLAMA_MODELS pointing at the USB, downloads a suitable free model if missing, and launches Capri.exe.

Optional: run portable\Start-Capri-Portable.ps1 -Model gemma3:4b to select a model explicitly, or add -SkipModelDownload to skip the automatic pull.

## USB layout

- Capri.exe — CAPRI desktop application.
- CAPRI-Data\Models — Ollama model blobs downloaded to this drive.
- CAPRI-Data\Downloads — reserved for portable downloads.
- CAPRI-Data\Memory and CAPRI-Data\Settings — portable data locations reserved for future native CAPRI path integration.
- CAPRI-Data\Logs — launcher and runtime logs.

## Important limitations

- The current CAPRI application stores some of its own settings and conversation memory using its existing Windows profile paths. This launcher does not silently redirect or migrate those existing files. The USB folders for memory/settings are prepared for a future native application change.
- Ollama's Windows runtime must be installed on each PC. The models are downloaded to the USB drive. This is not a fully self-contained runtime yet.
- CAPRI's existing Windows desktop automation and computer-vision features remain the application-provided features; this package does not bypass Windows permissions or security prompts.
- Keep the USB connected while CAPRI or the Ollama server is running. Close CAPRI before ejecting the drive.
- Model downloads are large. Use a USB 3.x drive with at least 16 GB free; more is recommended.
- The launcher refuses to reuse an already-running Ollama service because its model storage path may point to the PC instead of the USB.

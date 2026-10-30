# Local speech on Linux x64

Settings > Voice > **Set up her free voice and ears** installs the same Kokoro
voice pack and English Parakeet model as the Windows app. TALK starts capture
only when clicked. The microphone button mutes it; TYPE ends capture. **Stop**
cancels the reply, playback and active speech HTTP requests. Quiet, hide and quit
also release capture. A microphone disconnect ends the session; select a device
and press TALK again. Capture is bound with libpulse's `PA_STREAM_DONT_MOVE`, so
a disconnected source cannot silently switch to another microphone.

Local TALK is half duplex, matching the upstream local conversation: microphone
samples are held while thinking, synthesizing, transcribing or playing speech.
Typed interruption and Stop remain available then. It does not offer acoustic
echo cancellation or full-duplex voice barge-in. English is the only local ASR
language in this model. Native ASR decoding already in progress finishes on its
worker, but cancelled/session-stale results cannot submit another request.

The four upstream free styles, volume (100% default), pitch, speed, robot,
glitch and layers use the shared Windows implementation. ElevenLabs remains
optional. **Any voice model** also accepts a sherpa model folder/archive, a
local OpenAI-compatible server or local Fish Speech. Server keys stay in a
0600 profile file. No bundled HD reference clip is provided or restored.

The shared HD/Qwen client and Linux executable path are available for a matching
manually supplied engine, model, codec and owner-picked reference. At the source
pin used for this port, upstream labels its engine and talker URLs unpublished
placeholders; automated Linux HD setup is explicitly unavailable. Do not use a
different model or engine and call it equivalent. Model-file families other than
the included Kokoro and an actual HD server need separate runtime qualification.

## Headless setup

```
./Capri --setup-speech /absolute/path/to/profile --ears
```

This uses the same checksum-verified installers and exits without opening the
UI, playback or microphone. The normal profile directory is
`${XDG_CONFIG_HOME:-$HOME/.config}/Capri`; choose a separate directory when
staging or testing. `--ears` is optional. Model downloads need about 800 MB of
network transfer and additional temporary extraction space. The setup command
never imports existing keys or history. Keep the profile's `voice` and `ears`
directories and notices to run offline or restore them later.

Runtime dependencies on Arch: `libpulse` (`libpulse.so.0`, `pactl`, `pacat`), a
PulseAudio-compatible user audio server (PipeWire-Pulse or PulseAudio),
`libarchive` (`bsdtar`), `glibc`, and `gcc-libs`. `ffmpeg` is optional for decoding
owner-picked compressed reference clips and compressed custom server responses.
No audio service is installed, enabled or automatically started by Capri.

## Pinned artifacts and terms

| Artifact | SHA-256 | Terms |
| --- | --- | --- |
| [Upstream capri-voice-1.zip](https://github.com/lachydotmcg/capri-desktop/releases/download/voice-pack-1/capri-voice-1.zip), 316041808 bytes | `9fae085696d68a580d6b1a953d7b253129bfe506a2c016421b306b7237d3214c` | Kokoro Apache-2.0; upstream blended voice table and lexicons; notices retained |
| [sherpa-onnx 1.13.8 Linux x64 shared library bundle](https://github.com/k2-fsa/sherpa-onnx/releases/download/v1.13.8/sherpa-onnx-v1.13.8-linux-x64-shared-lib.tar.bz2), 9816899 bytes | `3892d184be41027e18165e67f549cd4e4cdd8dcd73ac5579e97afd55e14e30b6` | sherpa Apache-2.0, ONNX Runtime MIT, bundled espeak-ng GPL-3.0; notices retained from voice pack |
| [Parakeet TDT 0.6B v2 int8](https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8.tar.bz2), 482468385 bytes | `157c157bc51155e03e37d2466522a3a737dd9c72bb25f36eb18912964161e1ad` | [NVIDIA CC-BY-4.0 model](https://huggingface.co/nvidia/parakeet-tdt-0.6b-v2); attribution/license written during setup |

The Linux bundle reports sherpa `1.13.8` (source `11afbd00`) and ONNX Runtime
`1.28.2`. The native loader checks both Linux libraries' exact hashes and sherpa version
before use and resolves the C API from the versioned profile bundle, never the
working directory. The TTS structs and ASR offsets match the
[1.13.8 C header](https://github.com/k2-fsa/sherpa-onnx/blob/v1.13.8/sherpa-onnx/c-api/c-api.h).
The upstream portable model bytes and DSP defaults are unchanged. DLLs are
omitted from the Linux installation.

## Verification

`dotnet run --project tests/voice/VoiceChecks.csproj` exercises endpointing and
ABI assertions without opening audio. Add an explicit prepared profile path to
synthesize a fresh fixture, cancel native synthesis and transcribe the fixture
locally. It can run with networking disabled. The audio test mode requires a
private software-sink fixture; it must not run on the default owner devices.
Software-sink checks do not establish physical microphone quality, audibility,
voice likeness, exact mouth/audio alignment, or human acceptance.

# Native Listen for Capri

This opt-in feature uses the retained, pinned Moonshine tiny-streaming English
recognizer as a local name detector. It recognizes the word **Capri** in a final
utterance; it does not invoke or relabel the dormant Hey Jarvis keyword model.
The selected physical microphone, local ASR runtime and local voice remain in use.
If no microphone is selected, enabling Listen for Capri offers a physical microphone
chooser. No new weights are downloaded when the switch is enabled.

Before the name, decoded partials and unrelated final text stay inside the
network-isolated microphone worker and are discarded. Hearing Capri accepts that
utterance and opens a 20-second follow-up window. A line whose acoustic start is
inside the window still counts if its final arrives after the deadline. An
accepted follow-up restarts the quiet interval. Thinking and speaking extend an
already-open conversation; they cannot open an expired one. Jobs can require the
name again. An ambient unprefixed `stop` is not a global hotword: it must belong
to a conversation that actually opened, or the owner can say `Capri stop`.

The native controller pauses capture and resets decoder history around Capri's
own playback. Its local authority is revoked before playback starts, so a late
old decoder result cannot be submitted while the subprocess handles the pause.
The playback reservation and 600 ms output tail must remain part of the host
integration. This is half-duplex gating, not acoustic echo cancellation or speaker
authentication. Physical false-trigger, reverberation, audibility and full-duplex
qualification must be reported separately from deterministic tests.

Mute, sleep, Stop, account/model/session and selected-device changes close the
conversation and revoke pending results. The selected node/fingerprint is
verified on startup, before every accepted final, and periodically during the
session. Disconnection or decoder failure stops the worker; there is no fallback
to a monitor/loopback or another default microphone. The host must apply its Stop
latch rather than automatically reopening a stopped microphone every UI tick.

TALK stops name listening and waits for its worker to exit before opening the
default microphone, so pending name transcripts cannot submit the same words.
Re-enable Listen for Capri afterward. Lines Capri says during TALK wait until
the turn finishes. TALK setup is in [README: Linux TALK setup](../README.md#linux-talk-setup)
and [help: TALK on Linux](../site/help/knowledge.md#talk-on-linux).

## Retained setup and recovery

The native profile's `OwnedAsrPython`, `OwnedAsrManifest`, `MicrophoneNode` and
`MicrophoneFingerprint` select the existing private setup. The manifest pins the
model and executable package payloads; `tools/asr-requirements.lock`,
`tools/asr-models.lock.json` and `tools/asr_setup.py` describe reproducible setup.
The retained model is `tiny-streaming-en/quantized_26_08_21`, source revision
`234f60faa0eb388b01cdf7e60aca232af37aefda`, under the existing recorded Moonshine
terms. Keep the runtime, model files and artifact lock together for offline
recovery. Do not edit the reviewed manifest to conceal changed weights or delete
its retained unused wake artifacts. A missing/mismatched setup reports an error
and does not enable working listening by appearance alone.

Recognition and name gating use the existing `ears` sandbox with network access
unshared. Only the explicitly selected PipeWire node is recorded. Accepted
requests then use the owner's existing chat/task provider, which can be remote;
the entire companion is not thereby claimed to run offline. The output voice is
unchanged. Continuous ASR has a greater CPU cost than a small dedicated hotword
model, remains bounded to the existing two-CPU decoder policy, and stays disabled
until the user enables it.

Linux TALK uses the upstream English or European Parakeet pack, by her language, and sherpa-onnx. Listen for
Capri uses its separate pinned Moonshine setup; the TALK pack does not replace it.

## Mac

Thanks to Yoplaxs, Listen for Hey Capri also runs on Apple Silicon Macs (Moonshine
ships no Intel Mac wheel). It uses the same pinned decoder, name gate and 20-second
follow-ups as Linux, with these Mac differences:

- **Microphone.** There is no PipeWire node to pick, so turning the switch on asks
  `capri-mic --identity` for the current macOS default input and names it. Typing YES
  stores `mac-default` and a SHA-256 of the device's unique ID. The listener checks
  that ID on startup, every two seconds and before every accepted final; if the
  default input changes, listening stops until the switch is turned on again.
- **Isolation.** The listener controller (`name_listen.py`, standard library only)
  runs unsandboxed so `capri-mic` keeps Capri's microphone permission. The decoder
  (`asr_worker.py`, which loads Moonshine and ONNX Runtime) runs under Seatbelt via
  `mac_sandbox.decoder_command`: no network, no writes, and reads limited to the
  interpreter, the speech bridge and the paths in the private runtime manifest.
  The controller is bound to Capri with a kqueue parent watch, and `capri-mic` and
  the decoder end when their pipes close.
- **Her own voice.** Mac playback goes through the native helper, not PipeWire, so
  every line, typing sound and hush closes capture first and keeps it closed for
  the clip plus 700 ms.
- **Screen lock.** On Linux she pauses on a real lock report: Hyprland's own session
  snapshot while it has a fresh one, or logind's `LockedHint` when the snapshot is
  missing or stale. A desktop that answers nothing keeps her listening, and she may
  keep listening while the screen is locked, since the hint depends on the desktop
  updating it. Mac has no lock check yet; mute or quiet her before stepping away.

Mac setup, Apple Silicon, Python 3.11 (Homebrew `python@3.11` or python.org):

```sh
python3.11 -m venv ~/CapriEars/venv
~/CapriEars/venv/bin/python -m pip install --require-hashes --only-binary=:all: -r tools/asr-requirements.lock
~/CapriEars/venv/bin/python tools/asr_setup.py --directory ~/CapriEars/asr --lock tools/asr-models.lock.json
```

Quit Capri, then set `OwnedAsrPython` to the full path of `~/CapriEars/venv/bin/python`
and `OwnedAsrManifest` to the full path of `~/CapriEars/asr/runtime-manifest.json` in
her `cross.json`. `tflite-runtime` is Linux-only in the lock; Mac uses ONNX Runtime.
The `Mac ears` workflow installs this runtime on a macos-15 runner and checks that
the sandboxed decoder hears a spoken line, cannot reach the network and cannot read
the home folder.

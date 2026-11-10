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

Linux TALK uses the upstream English Parakeet pack and sherpa-onnx. Listen for
Capri uses its separate pinned Moonshine setup; the TALK pack does not replace it.

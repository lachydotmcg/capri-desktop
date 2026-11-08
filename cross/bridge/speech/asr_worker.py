#!/usr/bin/env python3
"""Persistent CPU Moonshine + openWakeWord worker; never opens an audio device."""
from __future__ import annotations

import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import sys
import time

MAX_MESSAGE = 65536


def emit(event, **fields):
    print(json.dumps({"event": event, "timestamp_ns": time.monotonic_ns(), **fields}), flush=True)


def verify_manifest(path):
    manifest = json.loads(Path(path).read_text())
    for item in manifest["files"]:
        source = Path(item["path"])
        if not source.is_file():
            raise ValueError("Pinned ASR runtime/model file is missing")
        with source.open("rb") as stream:
            digest = hashlib.file_digest(stream, "sha256").hexdigest()
        if digest != item["sha256"]:
            raise ValueError("Pinned ASR runtime/model hash mismatch")
    return manifest


class Decoder:
    def __init__(self, manifest):
        # Affinity bounds native ORT threads as well as Python work; no CUDA
        # execution provider is selected. Children inherit the same CPU set.
        available = sorted(os.sched_getaffinity(0))
        os.sched_setaffinity(0, available[:2])
        self.allowed_cpus = set(available[:2])
        # ORT's default pool explicitly reassigns child affinity beyond the
        # caller's mask. Use Moonshine's supported sequential-session option;
        # OMP_NUM_THREADS or pinning only the main thread does not bound it.
        os.environ["MOONSHINE_ORT_SINGLE_THREAD"] = "1"
        import numpy as np
        from moonshine_voice import Transcriber
        from moonshine_voice.moonshine_api import ModelArch
        self.np = np
        self.manifest = manifest
        self.transcriber = Transcriber(
            manifest["moonshine"]["model_dir"], ModelArch.TINY_STREAMING,
            update_interval=0.16,
            options={"ort_providers": "cpu", "vad_window_duration": 0.35,
                     "vad_max_segment_duration": 15.0, "log_output_text": False,
                     "keyterms": "Capri,terminal,fastfetch,YouTube", "keyterm_boost": 2.0})
        self.stream = None
        self.session_id = ""
        self.serial = 0
        self.stream_start_sample = 0
        self.revisions = {}
        self.completed = set()
        self.wake = None
        self.wake_active = False
        self.wake_hits = 0
        self.wake_frames = bytearray()
        self.closed_gate = False
        self.total_samples = 0
        self.last_wake_sample = -32000
        self.verify_thread_affinity()

    def verify_thread_affinity(self):
        tasks = os.listdir("/proc/self/task")
        union = set()
        for task in tasks:
            try:
                affinity = os.sched_getaffinity(int(task))
            except ProcessLookupError:
                continue
            if not affinity <= self.allowed_cpus:
                raise RuntimeError("Native ASR thread escaped the selected CPU affinity")
            union.update(affinity)
        return {"threads": len(tasks), "allowed_cpu_union": sorted(union)}

    def make_stream(self, *, discard=True):
        if self.stream:
            if discard:
                self.stream.remove_all_listeners()
            self.stream.close()
        self.serial += 1
        self.stream_start_sample = self.total_samples
        self.revisions = {}
        self.completed = set()
        self.stream = self.transcriber.create_stream(update_interval=0.16)
        self.stream.add_listener(self.on_transcript)
        self.stream.start()

    def on_transcript(self, event):
        from moonshine_voice.transcriber import LineStarted, LineTextChanged, LineCompleted, Error
        if isinstance(event, Error):
            emit("error", message="Moonshine incremental decoder failed")
            return
        if not isinstance(event, (LineStarted, LineTextChanged, LineCompleted)):
            return
        line = event.line
        identity = f"{self.session_id}:{self.serial}:{line.line_id}"
        if isinstance(event, LineStarted):
            emit("speech_started", session_id=self.session_id, utterance_id=identity,
                 speech_start_sample=self.stream_start_sample + round(line.start_time * 16000))
        final = isinstance(event, LineCompleted)
        if not final and not isinstance(event, LineTextChanged):
            return
        if identity in self.completed:
            return
        revision = self.revisions.get(identity, 0) + 1
        self.revisions[identity] = revision
        if final:
            self.completed.add(identity)
        emit("asr_final" if final else "asr_partial", session_id=self.session_id,
             utterance_id=identity, revision=revision, text=line.text,
             speech_start_sample=self.stream_start_sample + round(line.start_time * 16000),
             speech_end_sample=round((line.start_time + line.duration) * 16000),
             decoder_latency_ms=line.last_transcription_latency_ms)

    def start(self, session_id, wake):
        self.session_id = session_id
        self.total_samples = 0
        self.closed_gate = False
        self.wake_active = bool(wake)
        if wake and self.wake is None:
            from openwakeword.model import Model
            config = self.manifest["wake"]
            self.wake = Model(wakeword_models=[config["model"]], inference_framework="onnx",
                              melspec_model_path=config["melspectrogram"],
                              embedding_model_path=config["embedding"], ncpu=2)
        if self.wake:
            self.wake.reset()
        self.make_stream()
        self.verify_thread_affinity()

    def audio(self, pcm):
        if not pcm or len(pcm) % 2 or len(pcm) > 5120:
            raise ValueError("Invalid PCM16 streaming frame")
        self.total_samples += len(pcm) // 2
        if self.closed_gate:
            return
        if self.wake_active:
            self.wake_frames.extend(pcm)
            while len(self.wake_frames) >= 2560:
                frame = bytes(self.wake_frames[:2560])
                del self.wake_frames[:2560]
                scores = self.wake.predict(self.np.frombuffer(frame, dtype="<i2"))
                score = float(scores["hey_jarvis_v0.1"])
                self.wake_hits = self.wake_hits + 1 if score >= 0.6 else 0
                if self.wake_hits >= 2 and self.total_samples - self.last_wake_sample >= 32000:
                    self.last_wake_sample = self.total_samples
                    self.wake_hits = 0
                    # Two-step activation: discard the wake phrase's acoustic
                    # history. The next utterance supplies the command.
                    self.make_stream()
                    emit("wake", session_id=self.session_id, model="hey_jarvis_v0.1", score=score)
                    return
        samples = self.np.frombuffer(pcm, dtype="<i2").astype(self.np.float32) / 32768.0
        self.stream.add_audio(samples.tolist(), 16000)
        self.verify_thread_affinity()

    def gate(self, closed):
        self.closed_gate = bool(closed)
        self.wake_frames.clear()
        self.wake_hits = 0
        if self.wake:
            self.wake.reset()
        self.make_stream()

    def finish(self):
        self.stream.stop()
        self.make_stream()

    def close(self):
        if self.stream:
            self.stream.remove_all_listeners()
            self.stream.close()
        self.transcriber.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", required=True)
    args = parser.parse_args()
    decoder = None
    try:
        manifest = verify_manifest(args.manifest)
        decoder = Decoder(manifest)
        emit("ready", backend="moonshine-tiny-streaming-en", cpu_affinity=sorted(os.sched_getaffinity(0)),
             code_revision=manifest["moonshine"]["source_revision"],
             weight_revision=manifest["moonshine"]["weight_revision"], wake_phrase="Hey Jarvis",
             keyterms=["Capri", "terminal", "fastfetch", "YouTube"], keyterm_boost=2.0,
             native_ort_mode="single-thread sequential", thread_audit=decoder.verify_thread_affinity())
        while True:
            raw = sys.stdin.buffer.readline(MAX_MESSAGE + 1)
            if not raw:
                break
            if len(raw) > MAX_MESSAGE:
                raise ValueError("Streaming message exceeds limit")
            request = json.loads(raw)
            command = request["command"]
            if command == "start":
                decoder.start(request["session_id"], request.get("wake", False))
            elif command == "audio":
                decoder.audio(base64.b64decode(request["pcm"], validate=True))
            elif command == "finish":
                decoder.finish()
            elif command == "reset":
                decoder.gate(decoder.closed_gate)
            elif command == "gate":
                decoder.gate(request["closed"])
            else:
                raise ValueError("Unknown ASR worker command")
            emit("ack", sequence=request["sequence"], samples=decoder.total_samples)
    except Exception as error:
        # Fixed failure category avoids exporting private audio/transcripts or
        # paths in GUI errors; stderr preserves the technical cause locally.
        print(f"{type(error).__name__}: {error}", file=sys.stderr, flush=True)
        emit("error", message="Local ASR worker failed; verify its pinned setup")
        return 1
    finally:
        if decoder:
            decoder.close()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

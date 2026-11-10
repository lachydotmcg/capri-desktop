"""Private offline CPU preset worker. Stdio carries bounded JSON, never PCM.

This process has no playback, microphone, provider or desktop capabilities.
Cancel terminates the entire worker; model inference cannot outlive its owner.
"""
from __future__ import annotations

import argparse
from array import array
import contextlib
import ctypes
import fcntl
import hashlib
import importlib.metadata
import importlib.util
import json
import math
import os
from pathlib import Path
import re
import resource
import signal
import sys
import time
import wave


MAX_MESSAGE = 16_384


class WorkerError(ValueError):
    """Static diagnostic codes safe for the app; never prompt/path contents."""



def file_hash(path):
    with Path(path).open("rb") as source:
        return hashlib.file_digest(source, "sha256").hexdigest()


def effects_module():
    spec = importlib.util.spec_from_file_location("capri_voice_effects", Path(__file__).with_name("voice_effects.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def load_effect_profile(profile):
    required = {"schema", "backend", "kind", "variant", "label", "base_profile", "base_profile_sha256", "effects", "processor"}
    effects = effects_module()
    if (set(profile) != required or type(profile['schema']) is not int or profile['schema']not in(4,5)
            or profile['kind'] != 'preset' or profile['variant'] != ('capri-local-fx-v1'if profile['schema']==4 else'capri-local-phrase-v1')
            or profile['effects'] not in effects.MODES
            or not isinstance(profile['label'], str) or not 1 <= len(profile['label']) <= 120):
        raise WorkerError('effects-profile')
    path = Path(profile['base_profile'])
    if not path.is_absolute() or path.is_symlink() or path.stat().st_size > 128000 or file_hash(path) != profile['base_profile_sha256']:
        raise WorkerError('effects-base-pin')
    base = json.loads(path.read_text())
    if base.get('schema') != 3:raise WorkerError('effects-base-schema')
    base = load_voice_pack_profile(base)
    try:effects.validate_processor(profile['processor'])
    except (ValueError, OSError) as error:raise WorkerError('effects-processor-unavailable') from error
    return {**base, 'schema': profile['schema'], 'variant': profile['variant'],
            'label': profile['label'], 'effects': profile['effects'], 'processor': profile['processor']}


def delivery_module():
    spec=importlib.util.spec_from_file_location("capri_speech_delivery",Path(__file__).with_name("speech_delivery.py"))
    module=importlib.util.module_from_spec(spec)
    sys.modules[spec.name]=module
    spec.loader.exec_module(module)
    return module


def load_profile(path):
    path = Path(path)
    if path.stat().st_size > 128_000:
        raise WorkerError("profile-size")
    profile = json.loads(path.read_text(encoding="utf-8"))
    if profile.get("schema") in (4, 5) and profile.get("backend") == "kokoro-onnx":
        return load_effect_profile(profile)
    if profile.get("schema") == 3 and profile.get("backend") == "kokoro-onnx":
        return load_voice_pack_profile(profile)
    if profile.get("schema") == 2 and profile.get("backend") == "kokoro-onnx":
        return load_kokoro_profile(profile)
    raise WorkerError("profile-backend")


# These names/IDs belong to the pinned official v1.0 voice table, not a performer.
KOKORO_VOICES = {"af_bella": 2, "af_heart": 3, "af_sky": 10}
VOICE_PACK_VARIANT = "lachy-capri-voice-1"
VOICE_PACK_LOCK_SHA256 = "c3d2ee84f7120251c9869e2c618b3370650f44f6f02d5d4c01806fb4702d5178"
VOICE_PACK_ROW_SHA256 = "ac707ce8b934e0da17a1ac4311093a348603b8d803e3065a82f50ece15427ac7"


def voice_pack_lock():
    path = Path(__file__).resolve().parents[1] / "voice/lachy-capri-voice-1.lock.json"
    if path.is_symlink() or path.stat().st_size > 128_000 or file_hash(path) != VOICE_PACK_LOCK_SHA256:
        raise WorkerError("voice-pack-lock")
    return json.loads(path.read_text())


def load_voice_pack_profile(profile):
    """Admit only the exact published blend, independently of caller file pins.

    Row53 is em_santa in the ordinary table. A speaker ID alone cannot identify
    this voice, and schema2 presets must retain their original table/language.
    """
    lock = voice_pack_lock()
    required = {"kind": "preset", "variant": VOICE_PACK_VARIANT,
        "voice": "lachy-capri-voice-1-her-own", "speaker_id": 53, "speed": 1.10,
        "language": "en-us", "model_filename": "model.onnx",
        "lexicon_filename": "lexicon-us-en.txt", "sample_rate": 24000,
        "alignment_kind": "approximate-duration", "streaming_kind": "whole-clause",
        "runtime_version": "1.13.8", "lock_sha256": VOICE_PACK_LOCK_SHA256,
        "voice_row_sha256": VOICE_PACK_ROW_SHA256}
    if (any(profile.get(k) != v for k, v in required.items())
            or type(profile.get("schema")) is not int or profile["schema"] != 3
            or type(profile.get("speaker_id")) is not int
            or type(profile.get("sample_rate")) is not int):
        raise WorkerError("voice-pack-selection")
    if (profile.get("archive") != lock["archive"]
            or profile.get("model_revision") != lock["release_commit"]
            or profile.get("dependencies") != {"sherpa-onnx": "1.13.8", "sherpa-onnx-core": "1.13.8", "numpy": "2.4.2"}):
        raise WorkerError("voice-pack-provenance")
    if (type(profile.get("threads")) is not int or not 1 <= profile["threads"] <= 4
            or not isinstance(profile.get("label"), str) or not 1 <= len(profile["label"]) <= 120):
        raise WorkerError("preset-settings")
    for key in ("model_dir", "cache_dir"):
        if not isinstance(profile.get(key), str) or not Path(profile[key]).is_absolute():
            raise WorkerError("absolute-path-required")
    affinity = profile.get("cpu_affinity")
    if (not isinstance(affinity, list) or not 1 <= len(affinity) <= 4
            or any(type(cpu) is not int or cpu not in os.sched_getaffinity(0) for cpu in affinity)
            or len(set(affinity)) != len(affinity)):
        raise WorkerError("cpu-affinity")
    selected = set(lock["linux_data_paths"])
    expected = {r["path"]: r["sha256"] for r in lock["archive_members"] if r["path"] in selected}
    if profile.get("model_files") != expected:
        raise WorkerError("voice-pack-resource-pins")
    root = Path(profile["model_dir"])
    if root.is_symlink() or not root.is_dir():
        raise WorkerError("voice-pack-directory")
    actual = set()
    for path in root.rglob("*"):
        if path.is_symlink() or (not path.is_dir() and not path.is_file()):
            raise WorkerError("voice-pack-link")
        if path.is_file():
            actual.add(path.relative_to(root).as_posix())
    if actual != selected:
        raise WorkerError("voice-pack-file-set")
    _pinned_files(root, expected)
    row_bytes = 510 * 256 * 4
    table = root / "voices.bin"
    if table.stat().st_size != 54 * row_bytes:
        raise WorkerError("voice-pack-table-shape")
    with table.open("rb") as stream:
        stream.seek(53 * row_bytes)
        raw = stream.read(row_bytes)
    if hashlib.sha256(raw).hexdigest() != VOICE_PACK_ROW_SHA256:
        raise WorkerError("voice-pack-row")
    values = array("f"); values.frombytes(raw)
    if sys.byteorder != "little":
        values.byteswap()
    if not any(values) or not all(math.isfinite(value) for value in values):
        raise WorkerError("voice-pack-row-values")
    return profile


def default_cpu_affinity():
    """At most four distinct physical cores, preferring faster Linux cores.

    Taking the first four logical IDs can accidentally select only two cores
    on an SMT desktop. This selects only this worker's permitted CPU set.
    """
    cores = {}
    for cpu in sorted(os.sched_getaffinity(0)):
        root = Path(f"/sys/devices/system/cpu/cpu{cpu}")
        try:
            core = ((root / "topology/physical_package_id").read_text().strip(),
                    (root / "topology/core_id").read_text().strip())
            frequency = int((root / "cpufreq/cpuinfo_max_freq").read_text())
        except (OSError, ValueError):
            core, frequency = ("unknown", cpu), 0
        cores.setdefault(core, (frequency, cpu))
    return [cpu for _, cpu in sorted(cores.values(), key=lambda item: (-item[0], item[1]))[:4]]


def _pinned_files(root, files):
    if not isinstance(files, dict) or not files:
        raise WorkerError("missing-file-pins")
    for name, digest in files.items():
        relative = Path(name)
        if (relative.is_absolute() or ".." in relative.parts
                or not re.fullmatch(r"[0-9a-f]{64}", digest)):
            raise WorkerError("file-pin")
        if file_hash(Path(root) / relative) != digest:
            raise WorkerError("file-hash")


def load_kokoro_profile(profile):
    if profile.get("kind") != "preset":
        raise WorkerError("preset-kind")
    if (type(profile.get("threads")) is not int or not 1 <= profile["threads"] <= 4
            or profile.get("sample_rate") != 24000
            or profile.get("language") != "en-us"
            or not isinstance(profile.get("label"), str) or not 1 <= len(profile["label"]) <= 120):
        raise WorkerError("preset-settings")
    if (profile.get("voice") not in KOKORO_VOICES
            or profile.get("speaker_id") != KOKORO_VOICES[profile["voice"]]
            or type(profile.get("speed")) not in (int, float)
            or not math.isfinite(profile["speed"]) or not 0.8 <= profile["speed"] <= 1.2):
        raise WorkerError("preset-voice")
    for name in ("model_dir", "cache_dir"):
        if not Path(profile[name]).is_absolute():
            raise WorkerError("absolute-path-required")
    affinity = profile.get("cpu_affinity")
    if (not isinstance(affinity, list) or not 1 <= len(affinity) <= 4
            or any(type(cpu) is not int or cpu not in os.sched_getaffinity(0) for cpu in affinity)):
        raise WorkerError("cpu-affinity")
    required = {"model.int8.onnx", "voices.bin", "tokens.txt", "lexicon-us-en.txt", "LICENSE"}
    files = profile.get("model_files")
    if not isinstance(files, dict) or not required.issubset(files) or not any(name.startswith("espeak-ng-data/") for name in files):
        raise WorkerError("model-resource-pins")
    # Pin the complete G2P tree: extra unpinned data must not alter pronunciation.
    root = Path(profile["model_dir"])
    expected_g2p = {name for name in files if name.startswith(("espeak-ng-data/", "dict/"))}
    actual_g2p = {str(p.relative_to(root)) for d in ("espeak-ng-data", "dict") for p in (root / d).rglob("*") if p.is_file()}
    if expected_g2p != actual_g2p:
        raise WorkerError("g2p-file-set")
    _pinned_files(root, files)
    return profile


def verify_kokoro_runtime(profile):
    dependencies = profile.get("dependencies", {})
    if set(dependencies) != {"sherpa-onnx", "sherpa-onnx-core", "numpy"}:
        raise WorkerError("dependency-pins")
    for name, version in dependencies.items():
        if importlib.metadata.version(name) != version:
            raise WorkerError("dependency-version")
    trees = profile.get("runtime_trees", {})
    if set(trees) != set(dependencies):
        raise WorkerError("runtime-pins")
    for name in dependencies:
        if runtime_tree(name) != trees[name]:
            raise WorkerError("runtime-file-hash")


def runtime_tree(name):
    dist = importlib.metadata.distribution(name)
    files = sorted(str(p) for p in dist.files or [] if not str(p).endswith((".pyc", "/RECORD"))
                   and ".dist-info/" not in str(p) and ".data/" not in str(p)
                   and not str(p).startswith("../"))
    records = {p: file_hash(dist.locate_file(p)) for p in files}
    digest = hashlib.sha256(json.dumps(records, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
    return {"sha256": digest, "file_count": len(records)}


class VoiceWorker:
    def __init__(self, profile_path):
        self.profile_path = profile_path
        self.model = None
        self.profile = None
        self.load_metrics = {}

    def warm(self):
        if self.model is not None:
            return {**self.load_metrics, "already_warm": True}
        started = time.monotonic_ns()
        profile = load_profile(self.profile_path)
        verify_kokoro_runtime(profile)
        return self._warm_kokoro(profile, started)

    def _warm_kokoro(self, profile, started):
        # One lock shared by all candidate presets; no concurrent model copies.
        cache = Path(profile["cache_dir"])
        cache.mkdir(mode=0o700, parents=True, exist_ok=True)
        self._profile_lock = (cache / "kokoro-worker.lock").open("a")
        fcntl.flock(self._profile_lock.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        os.sched_setaffinity(0, profile["cpu_affinity"])
        os.environ.update(OMP_NUM_THREADS=str(profile["threads"]), OPENBLAS_NUM_THREADS="1")
        import sherpa_onnx
        imported = time.monotonic_ns()
        root = Path(profile["model_dir"])
        is_pack = profile.get("schema") in (3, 4, 5)
        model_filename = profile["model_filename"] if is_pack else "model.int8.onnx"
        language = profile["language"] if is_pack else "en"
        config = sherpa_onnx.OfflineTtsConfig(model=sherpa_onnx.OfflineTtsModelConfig(
            kokoro=sherpa_onnx.OfflineTtsKokoroModelConfig(
                model=str(root / model_filename), voices=str(root / "voices.bin"),
                tokens=str(root / "tokens.txt"), data_dir=str(root / "espeak-ng-data"),
                lexicon=str(root / "lexicon-us-en.txt"), lang=language),
            num_threads=profile["threads"], provider="cpu", debug=False), max_num_sentences=1)
        if not config.validate():
            raise WorkerError("kokoro-config")
        self.model = sherpa_onnx.OfflineTts(config)
        if self.model.sample_rate != profile["sample_rate"]:
            raise WorkerError("sample-rate")
        loaded = time.monotonic_ns()
        self.profile = profile
        self.load_metrics = {"validation_import_ms": (imported-started)/1e6,
            "model_load_ms": (loaded-imported)/1e6,
            "warm_total_ms": (loaded-started)/1e6,
            "alignment_kind": "approximate-duration", "device": "cpu", "sample_rate": 24000,
            "streaming_kind": "whole-clause", "worker_pid": os.getpid(),
            "voice_kind": "preset", "voice": profile["voice"], "speed": profile["speed"],
            "variant": profile.get("variant", "official-v1.0-int8"),
            "speaker_id": profile["speaker_id"], "language": language,
            "model_filename": model_filename, "lexicon_filename": "lexicon-us-en.txt",
            "cpu_affinity": sorted(os.sched_getaffinity(0)), "gpu_provider": False}
        return self.load_metrics

    def _synthesize_kokoro(self, text, output, cold, warm_metrics, delivery=None):
        import numpy as np
        import sherpa_onnx
        started = time.monotonic_ns()
        config = sherpa_onnx.GenerationConfig()
        options=delivery_module().decode(delivery)
        if delivery is not None and self.profile.get("schema")!=5:raise WorkerError("phrase-profile-required")
        config.sid, config.speed = self.profile["speaker_id"], self.profile["speed"]*options.rate
        audio = self.model.generate(text, config)
        generated = time.monotonic_ns()
        samples = np.asarray(audio.samples, dtype=np.float32)
        effect_metrics = {}
        if self.profile.get('schema') in (4,5):
            samples, effect_metrics = effects_module().process(samples, self.profile, text, options if self.profile.get("schema")==5 else None)
            samples = np.asarray(samples, dtype=np.float32)
        duration = samples.size / audio.sample_rate
        if (samples.ndim != 1 or audio.sample_rate != 24000 or not 0 < duration <= 45
                or not np.isfinite(samples).all()):
            raise WorkerError("invalid-generated-audio")
        pcm = (np.clip(samples, -1, 1) * 32767).astype("<i2").tobytes()
        with output.open("xb") as destination:
            os.chmod(output, 0o600)
            with wave.open(destination, "wb") as wav:
                wav.setnchannels(1); wav.setsampwidth(2); wav.setframerate(audio.sample_rate)
                wav.writeframes(pcm)
        ready = time.monotonic_ns()
        return {**warm_metrics, **effect_metrics, "cold_request": cold, "tts_start_ns": started,
            "pcm_first_ready_ns": ready, "synthesis_ms": (generated-started)/1e6,
            "write_ms": (ready-generated)/1e6, "duration_ms": duration*1000,
            "rtf": (ready-started)/1e9/duration, "watermark": "none-backend-native",
            "rss_hwm_bytes": resource.getrusage(resource.RUSAGE_SELF).ru_maxrss * 1024}

    def synthesize(self, text, output, delivery=None):
        if not isinstance(text, str) or not 0 < len(text) <= 384:
            raise WorkerError("text-bound")
        output = Path(output)
        if not output.is_absolute() or output.exists() or not output.parent.is_dir():
            raise WorkerError("new-output-required")
        cold = self.model is None
        warm_metrics = self.warm()
        return self._synthesize_kokoro(text, output, cold, warm_metrics, delivery)


def serve(profile_path, input_stream, output_stream):
    worker = VoiceWorker(profile_path)
    while True:
        raw = input_stream.readline(MAX_MESSAGE + 1)
        if not raw:
            return
        request_id = None
        try:
            if len(raw) > MAX_MESSAGE or not raw.endswith("\n"):
                raise WorkerError("message-bound")
            request = json.loads(raw)
            request_id = request["id"]
            if type(request_id) is not int:
                raise WorkerError("request-id")
            # Third-party progress/warnings must never corrupt JSON framing.
            with contextlib.redirect_stdout(sys.stderr):
                if request.get("op") == "warm":
                    metrics = worker.warm()
                elif request.get("op") == "synthesize":
                    metrics = worker.synthesize(request["text"], request["output"], request.get("delivery"))
                else:
                    raise WorkerError("unknown-operation")
            result = {"id": request_id, "ok": True, "metrics": metrics}
        except Exception as error:
            # Class-only failure keeps prompts/audio paths out of ordinary logs.
            code = type(error).__name__ + (":" + str(error) if isinstance(error, WorkerError) else "")
            result = {"id": request_id, "ok": False, "error": code}
        output_stream.write(json.dumps(result) + "\n")
        output_stream.flush()


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--profile", required=True)
    args = parser.parse_args()
    os.umask(0o077)
    # Linux parent death must stop inference even before the next stdin read.
    parent = os.getppid()
    if ctypes.CDLL(None, use_errno=True).prctl(1, signal.SIGTERM, 0, 0, 0) != 0:
        raise OSError(ctypes.get_errno(), "Cannot bind worker lifetime")
    if os.getppid() != parent:
        raise SystemExit(1)
    # CPU trial isolation: one failed allocation must not consume host headroom.
    resource.setrlimit(resource.RLIMIT_AS, (16 * 1024**3, 16 * 1024**3))
    resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
    serve(args.profile, sys.stdin, sys.stdout)

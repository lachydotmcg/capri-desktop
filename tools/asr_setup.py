#!/usr/bin/env python3
"""Explicit, private ASR artifact setup. No model downloads occur at runtime."""
from __future__ import annotations

import argparse
import hashlib
import importlib.metadata
import json
from pathlib import Path
import urllib.request

MOONSHINE_REVISION = "234f60faa0eb388b01cdf7e60aca232af37aefda"
WAKE_REVISION = "c8ef6912c5feccf1037b852d9bc6c7ed644135ba"


def digest(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory", type=Path, required=True)
    parser.add_argument("--lock", type=Path, required=True)
    parser.add_argument("--resolve-lock", action="store_true", help="maintainer-only: record newly resolved weight hashes")
    args = parser.parse_args()
    root = args.directory.resolve()
    root.mkdir(mode=0o700, parents=True, exist_ok=True)
    import moonshine_voice
    from moonshine_voice.download import get_model_for_language, _stt_dependency_manifest
    from moonshine_voice.moonshine_api import ModelArch
    if importlib.metadata.version("moonshine-voice") != "0.1.5" or importlib.metadata.version("openwakeword") != "0.6.0":
        raise ValueError("Install the pinned private ASR requirements before setup")
    model_path, arch = get_model_for_language("en", ModelArch.TINY_STREAMING, cache_root=root / "moonshine")
    native = _stt_dependency_manifest("en", arch)
    artifacts = []
    runtime_files = []
    for group in native["groups"]:
        for item in group["files"]:
            path = Path(model_path) / item["name"]
            artifacts.append({"name": item["name"], "url": item["url"], "sha256": digest(path),
                              "license": "MIT", "size": path.stat().st_size})
            runtime_files.append({"path": str(path), "sha256": digest(path)})
    wake_dir = root / "wake"
    wake_dir.mkdir(mode=0o700, exist_ok=True)
    wake = {}
    for key, filename in (("model", "hey_jarvis_v0.1.onnx"), ("melspectrogram", "melspectrogram.onnx"),
                          ("embedding", "embedding_model.onnx")):
        url = f"https://github.com/dscripka/openWakeWord/releases/download/v0.5.1/{filename}"
        path = wake_dir / filename
        if not path.exists():
            temporary = path.with_suffix(".download")
            with urllib.request.urlopen(url, timeout=60) as response, temporary.open("xb") as output:
                while block := response.read(1024 * 1024):
                    output.write(block)
            temporary.replace(path)
        wake[key] = str(path)
        artifacts.append({"name": filename, "url": url, "sha256": digest(path),
                          "license": "CC-BY-NC-SA-4.0 (upstream bundled model terms)", "size": path.stat().st_size})
        runtime_files.append({"path": str(path), "sha256": digest(path)})
    lock = {"moonshine_source_revision": MOONSHINE_REVISION, "wake_source_revision": WAKE_REVISION,
            "moonshine_weight_revision": "tiny-streaming-en/quantized_26_08_21",
            "wake_weight_revision": "openWakeWord/v0.5.1/hey_jarvis_v0.1",
            "artifacts": artifacts}
    if args.resolve_lock:
        # Exclusive creation preserves any already reviewed lock/provenance.
        with args.lock.open("x") as output:
            json.dump(lock, output, indent=2)
            output.write("\n")
    elif json.loads(args.lock.read_text()) != lock:
        raise ValueError("Downloaded ASR model files do not match the reviewed artifact lock")
    # Pin executable package payloads too, including native libmoonshine.so.
    for package in ("moonshine-voice", "openwakeword", "onnxruntime"):
        distribution = importlib.metadata.distribution(package)
        for relative in distribution.files or []:
            if str(relative).endswith((".py", ".so")):
                path = distribution.locate_file(relative).resolve()
                runtime_files.append({"path": str(path), "sha256": digest(path)})
    manifest = {"moonshine": {"source_revision": MOONSHINE_REVISION,
                              "weight_revision": lock["moonshine_weight_revision"], "model_dir": str(model_path)},
                "wake": {**wake, "source_revision": WAKE_REVISION, "phrase": "Hey Jarvis",
                         "speaker_verifier": "not enrolled; wake phrase is not speaker identity"},
                "files": runtime_files, "artifact_lock_sha256": digest(args.lock)}
    manifest_path = root / "runtime-manifest.json"
    with manifest_path.open("x") as output:
        json.dump(manifest, output, indent=2)
        output.write("\n")
    manifest_path.chmod(0o600)
    print(json.dumps({"manifest": str(manifest_path), "sha256": digest(manifest_path),
                      "lock": str(args.lock), "artifact_count": len(artifacts)}))


if __name__ == "__main__":
    main()

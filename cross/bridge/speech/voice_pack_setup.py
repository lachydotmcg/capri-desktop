"""Explicit setup for the pinned published voice pack; never called by speech.

Reuses the supplied offline staging procedure with bounded HTTPS download,
complete member validation and create-only publication. No inference/player.
"""
import ctypes
import errno
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import shutil
import stat
import tempfile
import urllib.request
import zipfile

from .voice_worker import (VOICE_PACK_VARIANT, VOICE_PACK_LOCK_SHA256,
    VOICE_PACK_ROW_SHA256, file_hash, load_profile, verify_runtime, voice_pack_lock)


def publish_directory(source, destination):
    """Linux no-replace rename closes the final destination-exists race."""
    libc = ctypes.CDLL(None, use_errno=True)
    rename = getattr(libc, "renameat2", None)
    if rename is None:
        raise OSError(errno.ENOSYS, "Atomic no-replace rename is unavailable")
    rename.argtypes = [ctypes.c_int, ctypes.c_char_p, ctypes.c_int, ctypes.c_char_p, ctypes.c_uint]
    rename.restype = ctypes.c_int
    if rename(-100, os.fsencode(source), -100, os.fsencode(destination), 1):
        code = ctypes.get_errno()
        raise OSError(code, os.strerror(code))


def verify_archive(archive, lock):
    if (archive.is_symlink() or not archive.is_file()
            or archive.stat().st_size != lock["archive"]["bytes"]
            or file_hash(archive) != lock["archive"]["sha256"]):
        raise ValueError("Voice archive size or SHA-256 mismatch")


def download_archive(directory):
    """Fetch only the source-pinned public HTTPS asset, outside model execution."""
    lock = voice_pack_lock()
    directory = Path(directory)
    if not directory.is_absolute():
        raise ValueError("Choose an absolute external download directory")
    directory.mkdir(mode=0o700, parents=True, exist_ok=True)
    archive = directory / "capri-voice-1.zip"
    if archive.exists() or archive.is_symlink():
        verify_archive(archive, lock)
        return archive
    fd, temporary = tempfile.mkstemp(prefix=".capri-voice-1-download-", dir=directory)
    temporary = Path(temporary)
    try:
        with os.fdopen(fd, "wb") as output, urllib.request.urlopen(lock["archive"]["url"], timeout=30) as source:
            if not source.geturl().startswith("https://"):
                raise ValueError("Voice download redirected outside HTTPS")
            while chunk := source.read(1024 * 1024):
                if output.tell() + len(chunk) > lock["archive"]["bytes"]:
                    raise ValueError("Voice archive exceeds pinned size")
                output.write(chunk)
            output.flush(); os.fsync(output.fileno())
        verify_archive(temporary, lock)
        os.link(temporary, archive)  # An existing archive is never replaced.
        return archive
    finally:
        temporary.unlink(missing_ok=True)


def stage_archive(archive, output):
    lock = voice_pack_lock()
    archive, output = Path(archive), Path(output)
    if not output.is_absolute() or output.exists() or output.is_symlink():
        raise ValueError("Choose a new absolute data directory")
    verify_archive(archive, lock)
    expected = {r["path"]: r for r in lock["archive_members"]}
    selected = set(lock["linux_data_paths"])
    allowed_directories = {p.as_posix() + "/" for name in expected
                           for p in PurePosixPath(name).parents if p != PurePosixPath(".")}
    with zipfile.ZipFile(archive) as source:
        infos = source.infolist()
        if len(infos) != len({i.filename for i in infos}):
            raise ValueError("Duplicate archive names")
        if {i.filename for i in infos if not i.is_dir()} != set(expected):
            raise ValueError("Unexpected or missing archive member")
        if sum(i.file_size for i in infos) != lock["uncompressed_bytes"]:
            raise ValueError("Uncompressed byte limit mismatch")
        for info in infos:
            name = PurePosixPath(info.filename)
            mode = stat.S_IFMT(info.external_attr >> 16)
            canonical = name.as_posix() + ("/" if info.is_dir() else "")
            if (name.is_absolute() or ".." in name.parts or "\\" in info.filename
                    or canonical != info.filename or any(ord(c) < 32 for c in info.filename)
                    or mode not in (0, stat.S_IFREG, stat.S_IFDIR) or info.flag_bits & 1
                    or (info.is_dir() and info.filename not in allowed_directories)):
                raise ValueError("Unsafe archive path, link or encryption")
        output.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
        temporary = Path(tempfile.mkdtemp(prefix=".capri-voice-1-stage-", dir=output.parent))
        try:
            for info in infos:
                if info.is_dir():
                    continue
                record = expected[info.filename]
                if info.file_size != record["bytes"]:
                    raise ValueError("Voice member size mismatch")
                target = temporary / info.filename if info.filename in selected else None
                if target:
                    target.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
                digest, count = hashlib.sha256(), 0
                destination = target.open("xb") if target else None
                try:
                    with source.open(info) as stream:
                        while chunk := stream.read(1024 * 1024):
                            count += len(chunk)
                            if count > record["bytes"]:
                                raise ValueError("Voice member exceeds pinned size")
                            digest.update(chunk)
                            if destination:
                                destination.write(chunk)
                    if destination:
                        destination.flush(); os.fsync(destination.fileno())
                finally:
                    if destination:
                        destination.close()
                if count != record["bytes"] or digest.hexdigest() != record["sha256"]:
                    raise ValueError("Voice member SHA-256 mismatch")
                if target:
                    target.chmod(0o600)
            actual = {p.relative_to(temporary).as_posix() for p in temporary.rglob("*") if p.is_file()}
            if actual != selected:
                raise ValueError("Selected voice data set mismatch")
            publish_directory(temporary, output)
        finally:
            if temporary.exists():
                shutil.rmtree(temporary)  # Only our unpublished staging directory.
    return {"status": "STAGED_NOT_SELECTED", "data_files": len(selected),
            "output": str(output), "archive_sha256": lock["archive"]["sha256"]}


def create_profile(model_dir, runtime_profile, output):
    """Validate a new immutable profile using the existing private runtime.

    Selection by the manual launcher is a separate reversible operation. The
    shared cache keeps the old and new presets on the same single-worker lock.
    """
    runtime_profile, model_dir, output = map(Path, (runtime_profile, model_dir, output))
    if not output.is_absolute() or output.exists() or output.is_symlink():
        raise ValueError("Choose a new absolute profile path")
    old = load_profile(runtime_profile)
    if old.get("backend") != "kokoro-onnx":
        raise ValueError("Reuse the verified existing Kokoro runtime profile")
    verify_runtime(old)
    lock = voice_pack_lock()
    selected = set(lock["linux_data_paths"])
    profile = {"schema": 3, "kind": "preset", "backend": "kokoro-onnx",
        "variant": VOICE_PACK_VARIANT, "voice": "lachy-capri-voice-1-her-own",
        "model_dir": str(model_dir), "cache_dir": old["cache_dir"],
        "model_revision": lock["release_commit"], "archive": lock["archive"],
        "lock_sha256": VOICE_PACK_LOCK_SHA256, "voice_row_sha256": VOICE_PACK_ROW_SHA256,
        "speaker_id": 53, "speed": 1.10, "language": "en-us", "sample_rate": 24000,
        "model_filename": "model.onnx", "lexicon_filename": "lexicon-us-en.txt",
        "alignment_kind": "approximate-duration", "streaming_kind": "whole-clause",
        "runtime_version": "1.13.8", "threads": old["threads"], "cpu_affinity": old["cpu_affinity"],
        "dependencies": old["dependencies"], "runtime_trees": old["runtime_trees"],
        "model_files": {r["path"]: r["sha256"] for r in lock["archive_members"] if r["path"] in selected},
        "label": "Capri · Lachy voice pack 1",
        "provenance": {"identity": "Published Kokoro preset blend, not an anime-trained model",
            "blend": lock["selection"]["blend"], "table_row_name": "em_santa overwritten by this pack",
            "required_notice": "Copyright 2026 Lachy (https://github.com/lachydotmcg)"}}
    output.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
    fd, temporary = tempfile.mkstemp(prefix=".voice-profile-", dir=output.parent)
    temporary = Path(temporary)
    try:
        with os.fdopen(fd, "w") as stream:
            json.dump(profile, stream, indent=2); stream.write("\n")
            stream.flush(); os.fsync(stream.fileno())
        load_profile(temporary); verify_runtime(profile)
        os.link(temporary, output)
    finally:
        temporary.unlink(missing_ok=True)
    return profile

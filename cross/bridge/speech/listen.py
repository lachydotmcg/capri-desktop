"""Shared process and physical microphone checks for local name listening."""
import asyncio
import ctypes
import json
import signal
from sources import physical_sources


def parent_guard():
    ctypes.CDLL(None).prctl(1, signal.SIGTERM, 0, 0, 0)


async def message(reader):
    line = await asyncio.wait_for(reader.readline(), 25)
    if not line or len(line) > 65536:
        raise ValueError('Invalid local decoder message')
    return json.loads(line)


async def verified_microphone(source, fingerprint):
    """Verify the selected physical node before name-listen capture."""
    if not isinstance(fingerprint, str) or not 1 <= len(fingerprint) <= 512:
        raise ValueError('Select an explicit near-field microphone')
    if not isinstance(source, str) or not 1 <= len(source) <= 256 or source.startswith('-') or any(ord(c) < 32 for c in source) or any(v in source.lower() for v in ('monitor', 'loopback', 'virtual', 'echo-cancel')):
        raise ValueError('Select an explicit near-field microphone')
    inventory = await asyncio.create_subprocess_exec('pw-dump', stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.DEVNULL, preexec_fn=parent_guard)
    try:
        raw = bytearray()
        async with asyncio.timeout(5):
            while True:
                chunk = await inventory.stdout.read(65536)
                if not chunk: break
                raw.extend(chunk)
                if len(raw) > 4 * 1024 * 1024:
                    raise ValueError('Microphone inventory exceeded its limit')
            await inventory.wait()
    finally:
        if inventory.returncode is None:
            inventory.kill()
            await inventory.wait()
    if len(raw) > 4 * 1024 * 1024 or inventory.returncode != 0:
        raise ValueError('Microphone inventory unavailable')
    eligible = physical_sources(json.loads(raw))
    if not any(row['node'] == source and row['fingerprint'] == fingerprint for row in eligible):
        raise ValueError('Selected physical microphone is no longer available')

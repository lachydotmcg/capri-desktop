"""Shared process and physical microphone checks for local name listening."""
import asyncio
import ctypes
import json
import signal
import sys
import os
from pathlib import Path
import hashlib
from sources import physical_sources


def parent_guard():
    if sys.platform == 'linux':
        ctypes.CDLL(None).prctl(1, signal.SIGTERM, 0, 0, 0)


async def message(reader):
    line = await asyncio.wait_for(reader.readline(), 25)
    if not line or len(line) > 65536:
        raise ValueError('Invalid local decoder message')
    return json.loads(line)


async def verified_microphone(source, fingerprint):
    """Verify that the selected audio input has not changed."""
    if sys.platform == 'darwin':
        if source != 'mac-default' or not isinstance(fingerprint, str) or len(fingerprint) != 64:
            raise ValueError('Approve the current macOS microphone first')
        helper = Path(__file__).resolve().parents[2] / 'capri-mic'
        process = await asyncio.create_subprocess_exec(str(helper), '--identity',
            stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.DEVNULL)
        try:
            raw = await asyncio.wait_for(process.stdout.read(8193), 5)
            await asyncio.wait_for(process.wait(), 5)
            if process.returncode != 0 or len(raw) > 8192:
                raise ValueError('The macOS microphone is unavailable')
            uid = json.loads(raw)['uid']
            if not isinstance(uid, str) or hashlib.sha256(uid.encode()).hexdigest() != fingerprint:
                raise ValueError('Your macOS default microphone changed. Re-enable name listening.')
        finally:
            if process.returncode is None:
                process.kill()
                await process.wait()
        return
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

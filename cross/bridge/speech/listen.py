"""One explicit microphone utterance; only an accepted final leaves this process."""
import asyncio
import base64
import ctypes
import json
import os
from pathlib import Path
import signal
import sys
import uuid
from sources import physical_sources


def parent_guard():
    ctypes.CDLL(None).prctl(1, signal.SIGTERM, 0, 0, 0)


async def message(reader):
    line = await asyncio.wait_for(reader.readline(), 25)
    if not line or len(line) > 65536:
        raise ValueError('Invalid local decoder message')
    return json.loads(line)


async def verified_microphone(source, fingerprint):
    """Verify the selected physical node before a one-shot or name-listen capture."""
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


async def main():
    parent = os.getppid()
    parent_guard()
    if os.getppid() != parent:
        return 1
    config = json.loads(sys.stdin.buffer.readline(65537))
    if set(config) != {'source', 'fingerprint', 'manifest'}:
        raise ValueError('Invalid recognition configuration')
    source = config['source']
    await verified_microphone(source, config['fingerprint'])
    worker = capture = None
    session = uuid.uuid4().hex
    sequence = 0
    async def send(command, **fields):
        nonlocal sequence
        sequence += 1
        worker.stdin.write((json.dumps({'command': command, 'sequence': sequence, **fields}) + '\n').encode())
        await worker.stdin.drain()
        final = None
        while True:
            event = await message(worker.stdout)
            if event['event'] == 'error':
                raise ValueError('Local recognition failed')
            if event['event'] == 'asr_final' and event.get('session_id') == session:
                text = event.get('text', '').strip()
                if not isinstance(text, str) or not 1 <= len(text) <= 4096:
                    raise ValueError('Invalid final transcript')
                final = {'event': 'final', 'session': session, 'utterance': event['utterance_id'], 'text': text}
            if event['event'] == 'ack':
                if event.get('sequence') != sequence:
                    raise ValueError('Recognition acknowledgement mismatch')
                return final
    try:
        worker = await asyncio.create_subprocess_exec(sys.executable, '-u', str(Path(__file__).with_name('asr_worker.py')), '--manifest', config['manifest'], stdin=asyncio.subprocess.PIPE, stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.DEVNULL, preexec_fn=parent_guard)
        if (await message(worker.stdout))['event'] != 'ready':
            raise ValueError('Local recognition failed to warm')
        await send('start', session_id=session, wake=False)
        capture = await asyncio.create_subprocess_exec('pw-record', '--target', source, '--rate', '16000', '--channels', '1', '--format', 's16', '--raw', '-', stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.DEVNULL, preexec_fn=parent_guard)
        print(json.dumps({'event': 'listening', 'session': session}), flush=True)
        async with asyncio.timeout(45):
            while True:
                pcm = await capture.stdout.readexactly(2560)
                final = await send('audio', pcm=base64.b64encode(pcm).decode())
                if final:
                    print(json.dumps(final), flush=True)
                    return 0
    finally:
        for process in (capture, worker):
            if process and process.returncode is None:
                process.terminate()
                try:
                    await asyncio.wait_for(process.wait(), 2)
                except asyncio.TimeoutError:
                    process.kill()
                    await process.wait()


if __name__ == '__main__':
    try:
        raise SystemExit(asyncio.run(main()))
    except Exception:
        print(json.dumps({'event': 'error', 'message': 'Local listening stopped. Check the selected microphone and pinned decoder setup.'}), flush=True)
        raise SystemExit(1)

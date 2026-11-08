"""Opt-in local Capri-name listening with the retained pinned Moonshine decoder.

Only name-gated accepted finals leave this network-isolated worker. This is an
ASR name detector, not the unrelated dormant Hey Jarvis keyword model.
"""
from __future__ import annotations

import asyncio
import base64
import contextlib
import json
import os
from pathlib import Path
import sys
import time
import uuid

from listen import message, parent_guard, verified_microphone
from name_chat import NameChat


async def stdin_reader():
    reader = asyncio.StreamReader(limit=65536)
    await asyncio.get_running_loop().connect_read_pipe(lambda: asyncio.StreamReaderProtocol(reader), sys.stdin.buffer)
    return reader


def context(value):
    if not isinstance(value, dict) or set(value) != {'barred', 'speaking', 'busy', 'name_only'} or any(type(v) is not bool for v in value.values()):
        raise ValueError('Invalid local listening context')
    return value


def revision(value):
    if type(value) is not int or not 1 <= value <= 2**53:
        raise ValueError('Invalid local listening generation')
    return value


class LocalDecoder:
    def __init__(self, process, session):
        self.process, self.session = process, session
        self.sequence = 0
        self.samples = 0
        self.lock = asyncio.Lock()

    async def send(self, command, **fields):
        async with self.lock:
            self.sequence += 1
            self.process.stdin.write((json.dumps({'command': command, 'sequence': self.sequence, **fields}) + '\n').encode())
            await self.process.stdin.drain()
            events = []
            while True:
                event = await message(self.process.stdout)
                if not isinstance(event, dict) or event.get('event') == 'error':
                    raise ValueError('Local name decoder failed')
                if event.get('event') == 'ack':
                    if event.get('sequence') != self.sequence or type(event.get('samples')) is not int:
                        raise ValueError('Local name decoder acknowledgement mismatch')
                    self.samples = event['samples']
                    return events
                if event.get('session_id') != self.session:
                    raise ValueError('Local name decoder session changed')
                if len(events) >= 64:
                    raise ValueError('Local name decoder event limit exceeded')
                events.append(event)


class NameListener:
    def __init__(self, config, reader, *, clock=time.monotonic, write=None, microphone_check=None):
        if not isinstance(config, dict) or set(config) != {'source', 'fingerprint', 'manifest', 'generation', 'context'}:
            raise ValueError('Invalid name-listening configuration')
        self.source, self.fingerprint, self.manifest = config['source'], config['fingerprint'], config['manifest']
        self.authority = revision(config['generation'])
        self.reader, self.clock = reader, clock
        self.verify_microphone = microphone_check or verified_microphone
        self.policy = NameChat(clock)
        self.policy.control(**context(config['context']))
        self.write = write or (lambda obj: print(json.dumps(obj), flush=True))
        self.session = uuid.uuid4().hex
        self.worker = self.decoder = self.capture = None
        self.capture_ready = asyncio.Event()
        self.changed = asyncio.Event()
        self.shutdown = asyncio.Event()
        self.capture_lock = asyncio.Lock()
        self.capture_start = 0.0
        self.capture_sample = 0
        self.last_state = None
        self.advertised = False

    def emit(self, kind, **data):
        self.write({'event': kind, 'session': self.session, 'generation': self.authority, **data})

    def publish_state(self):
        if not self.advertised:
            return
        value = (self.authority, self.policy.mode)
        if value != self.last_state:
            self.last_state = value
            self.emit('state', mode=self.policy.mode)

    async def stop_capture(self):
        self.capture_ready.clear()
        async with self.capture_lock:
            process, self.capture = self.capture, None
            self.capture_ready.clear()
            if process is not None:
                await self.stop_process(process)

    @staticmethod
    async def stop_process(process):
        if process.returncode is not None:
            await process.wait()
            return
        with contextlib.suppress(ProcessLookupError):
            process.terminate()
        try:
            await asyncio.wait_for(process.wait(), 2)
        except asyncio.TimeoutError:
            with contextlib.suppress(ProcessLookupError):
                process.kill()
            await process.wait()

    async def commands(self):
        while not self.shutdown.is_set():
            raw = await self.reader.readline()
            if not raw:
                self.shutdown.set()
                return
            if len(raw) > 65536:
                raise ValueError('Name-listener control exceeded its limit')
            value = json.loads(raw)
            if not isinstance(value, dict) or value.get('command') != 'state' or set(value) != {'command', 'generation', 'context'}:
                raise ValueError('Invalid name-listener command')
            admitted = revision(value['generation'])
            if admitted < self.authority:
                continue
            update = context(value['context'])
            safety_changed = (update['barred'], update['speaking'], update['name_only']) != (self.policy.barred, self.policy.speaking, self.policy.name_only)
            if safety_changed and admitted == self.authority:
                raise ValueError('Name-listener authority did not change with its gate')
            reset = admitted != self.authority
            self.authority = admitted
            self.policy.control(**update)
            # Drop queued/in-flight decoder text as soon as control changes, not
            # after an acknowledgement or another UI tick.
            if reset:
                await self.stop_capture()
                self.changed.set()
            self.publish_state()

    async def configure_capture(self):
        while not self.shutdown.is_set():
            await self.changed.wait()
            self.changed.clear()
            admitted = self.authority
            await self.stop_capture()
            await self.decoder.send('gate', closed=self.policy.blocked)
            if self.shutdown.is_set() or admitted != self.authority or self.policy.blocked:
                continue
            await self.verify_microphone(self.source, self.fingerprint)
            if self.shutdown.is_set() or admitted != self.authority or self.policy.blocked:
                continue
            process = await asyncio.create_subprocess_exec('pw-record', '--target', self.source,
                '--rate', '16000', '--channels', '1', '--format', 's16', '--raw', '-',
                stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.DEVNULL, preexec_fn=parent_guard)
            async with self.capture_lock:
                if self.shutdown.is_set() or admitted != self.authority or self.policy.blocked:
                    await self.stop_process(process)
                    continue
                self.capture = process
                self.capture_start, self.capture_sample = self.clock(), self.decoder.samples
                self.capture_ready.set()
            self.publish_state()

    def began(self, event, base_time, base_sample):
        sample = event.get('speech_start_sample')
        if type(sample) is not int or sample < base_sample or sample > self.decoder.samples:
            raise ValueError('Local decoder omitted its utterance boundary')
        return min(self.clock(), base_time + (sample - base_sample) / 16000.0)

    async def audio(self):
        while not self.shutdown.is_set():
            await self.capture_ready.wait()
            process = self.capture
            if process is None:
                continue
            admitted = self.authority
            base_time, base_sample = self.capture_start, self.capture_sample
            try:
                pcm = await process.stdout.readexactly(2560)
            except asyncio.IncompleteReadError:
                if self.shutdown.is_set() or process != self.capture or self.policy.blocked or admitted != self.authority:
                    continue
                raise ValueError('The selected microphone stopped')
            if process != self.capture or admitted != self.authority or self.policy.blocked:
                continue
            events = await self.decoder.send('audio', pcm=base64.b64encode(pcm).decode())
            if process != self.capture or admitted != self.authority or self.policy.blocked:
                continue
            for event in events:
                kind = event.get('event')
                if kind not in ('speech_started', 'asr_final'):
                    continue  # In particular, never export pre-name partial text.
                utterance = event.get('utterance_id')
                if kind == 'speech_started':
                    self.policy.speech_started(utterance, self.began(event, base_time, base_sample))
                else:
                    if utterance not in self.policy.started:
                        self.policy.speech_started(utterance, self.began(event, base_time, base_sample))
                    accepted = self.policy.final(utterance, event.get('text'))
                    if accepted is not None:
                        # Do not submit an utterance after a hot-swapped node has
                        # reused the selected name. Periodic inventory alone has
                        # a race; acceptance gets its own fresh fingerprint check.
                        await self.verify_microphone(self.source, self.fingerprint)
                        if process == self.capture and admitted == self.authority and not self.policy.blocked:
                            self.emit('final', utterance=accepted.utterance, text=accepted.text)
                self.publish_state()

    async def timers(self):
        while not self.shutdown.is_set():
            self.policy.tick()
            self.publish_state()
            await asyncio.sleep(.05)

    async def devices(self):
        while not self.shutdown.is_set():
            await asyncio.sleep(2)
            await self.verify_microphone(self.source, self.fingerprint)

    async def run(self):
        await self.verify_microphone(self.source, self.fingerprint)
        tasks = []
        try:
            # Start the command reader before warming the decoder. A mute/Stop or
            # playback that happens during warmup must not open the microphone.
            tasks.append(asyncio.create_task(self.commands()))
            self.worker = await asyncio.create_subprocess_exec(sys.executable, '-u',
                str(Path(__file__).with_name('asr_worker.py')), '--manifest', self.manifest,
                stdin=asyncio.subprocess.PIPE, stdout=asyncio.subprocess.PIPE,
                stderr=asyncio.subprocess.DEVNULL, preexec_fn=parent_guard)
            ready = await message(self.worker.stdout)
            if not isinstance(ready, dict) or ready.get('event') != 'ready':
                raise ValueError('The pinned local name decoder did not warm')
            self.decoder = LocalDecoder(self.worker, self.session)
            await self.decoder.send('start', session_id=self.session, wake=False)
            if self.shutdown.is_set():
                return
            for task in tasks:
                if task.done():
                    task.result()
            self.advertised = True
            self.emit('listening', detector='moonshine-final-capri-word-boundary-v1')
            self.publish_state()
            self.changed.set()
            tasks.extend(asyncio.create_task(fn()) for fn in (self.configure_capture, self.audio, self.timers, self.devices))
            tasks.append(asyncio.create_task(self.shutdown.wait()))
            done, pending = await asyncio.wait(tasks, return_when=asyncio.FIRST_COMPLETED)
            for task in done:
                task.result()
        finally:
            self.shutdown.set()
            self.policy.close()
            for task in tasks:
                task.cancel()
            if tasks:
                await asyncio.gather(*tasks, return_exceptions=True)
            await self.stop_capture()
            if self.worker is not None:
                await self.stop_process(self.worker)


async def main():
    parent = os.getppid()
    parent_guard()
    if os.getppid() != parent:
        return 1
    reader = await stdin_reader()
    raw = await asyncio.wait_for(reader.readline(), 5)
    if not raw or len(raw) > 65536:
        raise ValueError('Invalid name-listening configuration')
    await NameListener(json.loads(raw), reader).run()
    return 0


if __name__ == '__main__':
    try:
        raise SystemExit(asyncio.run(main()))
    except Exception as error:
        print(type(error).__name__ + ': ' + str(error), file=sys.stderr, flush=True)
        print(json.dumps({'event': 'error', 'message': 'Local name listening stopped. Check the selected microphone and pinned decoder setup.'}), flush=True)
        raise SystemExit(1)

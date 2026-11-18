#!/usr/bin/env python3
"""Real Mac check: the pinned Moonshine decoder hears speech under Seatbelt with no network or home access."""
import argparse
import base64
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import wave

BRIDGE = Path(__file__).resolve().parents[2] / 'cross' / 'bridge'
sys.path.insert(0, str(BRIDGE))
import mac_sandbox


def probe(command, environment, code):
    result = subprocess.run(command[:8] + ['-c', code], env=environment, capture_output=True, text=True, timeout=60)
    return result.returncode, (result.stdout + result.stderr).strip()


def confined(command, environment):
    reach = "import socket; socket.create_connection(('1.1.1.1', 443), 5); print('reached')"
    status, output = probe(command, environment, reach)
    if status == 0 or 'reached' in output:
        raise SystemExit('FAIL the decoder sandbox reached the network: ' + output)
    print('PASS no network inside the decoder sandbox: ' + output.splitlines()[-1])
    online = [command[0], command[1], command[2].replace('(allow sysctl-read)', '(allow sysctl-read)\n(allow network*)'), *command[3:]]
    status, output = probe(online, environment, reach)
    print(('PASS' if status == 0 else 'INFO') + ' control profile with network allowed: ' + (output.splitlines() or ['no output'])[-1])
    with tempfile.NamedTemporaryFile(dir=Path.home(), prefix='capri-ears-', suffix='.txt') as secret:
        secret.write(b'private')
        secret.flush()
        status, output = probe(command, environment, 'print(open(%r).read())' % secret.name)
    if status == 0 or 'private' in output:
        raise SystemExit('FAIL the decoder sandbox read a file in your home folder')
    print('PASS no home folder reads inside the decoder sandbox')


def frames(path):
    with wave.open(str(path)) as audio:
        if audio.getframerate() != 16000 or audio.getnchannels() != 1 or audio.getsampwidth() != 2:
            raise SystemExit('FAIL test audio must be 16 kHz mono PCM16')
        pcm = audio.readframes(audio.getnframes())
    pcm = bytes(16000) + pcm + bytes(64000)
    return [pcm[index:index + 3200] for index in range(0, len(pcm), 3200)]


def hear(command, environment, audio):
    process = subprocess.Popen(command, env=environment, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                               stderr=subprocess.PIPE, text=True)
    finals, sequence = [], 0

    def read_until_ack():
        while True:
            line = process.stdout.readline()
            if not line:
                raise SystemExit('FAIL the sandboxed decoder ended: ' + process.stderr.read()[-4000:])
            event = json.loads(line)
            if event.get('event') == 'error':
                raise SystemExit('FAIL the sandboxed decoder failed: ' + process.stderr.read()[-4000:])
            if event.get('event') == 'asr_final':
                finals.append(event['text'])
            if event.get('event') == 'ack':
                return

    def send(command_name, **fields):
        nonlocal sequence
        sequence += 1
        process.stdin.write(json.dumps({'command': command_name, 'sequence': sequence, **fields}) + '\n')
        process.stdin.flush()
        read_until_ack()

    try:
        ready = json.loads(process.stdout.readline() or '{}')
        if ready.get('event') != 'ready':
            raise SystemExit('FAIL the sandboxed decoder did not warm: ' + process.stderr.read()[-4000:])
        print('PASS the decoder warmed under Seatbelt: ' + ready.get('backend', ''))
        send('start', session_id='mac-ears-check', wake=False)
        for chunk in frames(audio):
            send('audio', pcm=base64.b64encode(chunk).decode())
        send('finish')
    finally:
        process.stdin.close()
        try:
            process.wait(10)
        except subprocess.TimeoutExpired:
            process.kill()
    heard = ' '.join(finals).strip()
    if not heard:
        raise SystemExit('FAIL the sandboxed decoder heard nothing')
    if 'capri' not in heard.lower() and 'time' not in heard.lower():
        raise SystemExit('FAIL the sandboxed decoder heard something else: ' + heard)
    print('PASS the sandboxed decoder heard: ' + heard)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--manifest', required=True)
    parser.add_argument('--audio', required=True)
    args = parser.parse_args()
    if sys.platform != 'darwin':
        raise SystemExit('This check runs on macOS')
    command, environment = mac_sandbox.decoder_command(BRIDGE / 'speech' / 'asr_worker.py', args.manifest)
    confined(command, environment)
    hear(command, environment, args.audio)


if __name__ == '__main__':
    main()

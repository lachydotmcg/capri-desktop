"""Offline Mac process fixtures, invoked only by Capri --smoke."""
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import time

sys.dont_write_bytecode = True
os.environ['PYTHONDONTWRITEBYTECODE'] = '1'

import mac_process


def until(predicate, seconds=3):
    end = time.monotonic() + seconds
    while time.monotonic() < end:
        value = predicate()
        if value:
            return value
        time.sleep(.02)
    raise AssertionError('Mac fixture observation timed out')


def status(folder):
    try:
        return json.loads((folder / 'status.json').read_text())
    except FileNotFoundError:
        return {}


def terminal(root, command, stop=False, cancelled=False, expired=False):
    folder = root / ('expired' if expired else 'cancelled' if cancelled else 'stop' if stop else 'output')
    folder.mkdir(mode=0o700)
    request = dict(job_id=folder.name, command=command, cwd=str(folder), shell='/bin/sh', visible=False,
                   owner_pid=os.getpid(), owner_start=mac_process.identity(os.getpid()), timeout=5, launch_deadline=time.time() + (-1 if expired else 5))
    (folder / 'request.json').write_text(json.dumps(request))
    if cancelled:
        (folder / 'stop').write_text('stop')
    process = subprocess.Popen([sys.executable, str(Path(__file__).with_name('terminal_job.py')), str(folder)],
                               stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
    child = None
    try:
        if stop:
            value = until(lambda: status(folder) if status(folder).get('output', '').startswith('ready') else None)
            child = value['pid']
            os.kill(child, signal.SIGSTOP)
            time.sleep(.2)
            assert status(folder)['state'] == 'running' and status(folder)['exit_code'] is None, 'A stopped child was treated as an exit'
            os.kill(child, signal.SIGCONT)
            (folder / 'stop').write_text('stop')
        process.wait(timeout=4)
        assert process.returncode == 0, process.stderr.read().decode()
        value = status(folder)
        if cancelled or expired:
            assert value['state'] == 'stopped' and value['exit_code'] is None, value
            assert not (folder / 'ran').exists(), 'Cancelled admission still ran its command'
        elif stop:
            assert value['state'] == 'stopped' and value['stop_reason'] == 'requested', value
            assert not mac_process.session_members(child), 'A stopped job left terminal processes'
        else:
            assert value['state'] == 'exited' and value['exit_code'] == 7, value
            assert 'quoted Ω' in value['output'] and str(folder) in value['output'], value
            assert value['total_bytes'] >= len(value['output'].encode('utf-8')), value
    finally:
        if process.poll() is None:
            if child:
                os.killpg(child, signal.SIGCONT)
                mac_process.terminate_session(child)
            process.kill()
            process.wait()
        process.stderr.close()


def parent_lifetime():
    bridge = str(Path(__file__).parent)
    guarded = "import sys,time; sys.path.insert(0," + repr(bridge) + "); from parent_watch import bind_parent; bind_parent(); print('ready',flush=True); time.sleep(30)"
    launcher = "import os,subprocess,sys; child=subprocess.Popen([sys.executable,'-u','-c'," + repr(guarded) + "]); print('child:'+str(child.pid),flush=True); sys.stdin.buffer.read(1); os._exit(0)"
    process = subprocess.Popen([sys.executable, '-u', '-c', launcher], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    pid = start = None
    try:
        for _ in range(2):
            line = process.stdout.readline().decode().strip()
            if line.startswith('child:'):
                pid = int(line[6:])
            else:
                assert line == 'ready', line
        assert pid is not None
        start = mac_process.identity(pid)
        process.stdin.close()
        process.wait(timeout=2)
        until(lambda: mac_process.identity(pid) != start)
    finally:
        if pid and start and mac_process.identity(pid) == start:
            os.kill(pid, signal.SIGTERM)
        if process.poll() is None:
            process.kill(); process.wait()
        process.stdout.close(); process.stderr.close()


def sandbox(root):
    directory, account = root / 'sandbox-worker', root / 'sandbox-account'
    directory.mkdir(mode=0o700); account.mkdir(mode=0o700)
    (account / 'native-owner-v1').write_text('Mac fixture account; no credentials')
    (account / 'fixture.txt').write_text('allowed')
    forbidden = root / 'ungranted.txt'
    forbidden.write_text('private fixture')
    (directory / 'parent_watch.py').write_bytes(Path(__file__).with_name('parent_watch.py').read_bytes())
    worker = directory / 'worker.py'
    for kind in ('plan', 'account'):
        worker.write_text("from pathlib import Path\nfrom parent_watch import bind_parent\nbind_parent()\n"
            + 'account=Path(' + repr(str(account)) + ')\n'
            + "assert (account/'fixture.txt').read_text() == 'allowed'\n"
            + "try:\n    Path(" + repr(str(forbidden)) + ").read_text()\n    raise AssertionError('ungranted read allowed')\nexcept PermissionError: pass\n"
            + "try:\n    (account/'written.txt').write_text('fixture')\n    written=True\nexcept PermissionError: written=False\n"
            + 'assert written == ' + repr(kind == 'account') + '\n'
            + "import os\nassert 'CAPRI_MAC_SMOKE_SENTINEL' not in os.environ\nprint('confined',flush=True)\n")
        environment = dict(os.environ, CAPRI_MAC_SMOKE_SENTINEL='fixture')
        result = subprocess.run([sys.executable, str(Path(__file__).with_name('sandbox.py')),
            '--kind', kind, '--python', sys.executable, '--worker', str(worker), '--resource', str(account)],
            input=b'', stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=3, env=environment)
        assert result.returncode == 0 and result.stdout.strip() == b'confined', (kind, result.stdout, result.stderr)


def main():
    assert sys.platform == 'darwin'
    root = Path(sys.argv[1]).absolute()
    root.mkdir(mode=0o700, parents=True, exist_ok=True)
    assert mac_process.identity(os.getppid()) == sys.argv[2], 'C# and Python disagree about the owner process identity'
    terminal(root, "printf 'quoted Ω\\n'; pwd; exit 7")
    terminal(root, "printf ready; sleep 30 & wait", stop=True)
    terminal(root, 'touch ran', cancelled=True)
    terminal(root, 'touch ran', expired=True)
    parent_lifetime()
    sandbox(root)
    print('Mac process fixtures passed')


if __name__ == '__main__':
    main()

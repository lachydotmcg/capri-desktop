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


def kill_child(process):
    if process.poll() is not None:
        return
    value = mac_process.info(process.pid)
    if value is None:
        if process.poll() is not None:
            return
        raise AssertionError('Cannot inspect the owned fixture child')
    mac_process.signal_process(process.pid, value[0], signal.SIGKILL)
    process.wait(timeout=2)


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
    child = child_start = None
    try:
        if stop:
            value = until(lambda: status(folder) if status(folder).get('output', '').startswith('ready') else None)
            child = value['pid']
            child_start = mac_process.identity(child)
            assert child_start is not None, 'The fixture child exited before it could be stopped'
            mac_process.signal_process(child, child_start, signal.SIGSTOP)
            time.sleep(.2)
            assert status(folder)['state'] == 'running' and status(folder)['exit_code'] is None, 'A stopped child was treated as an exit'
            mac_process.signal_process(child, child_start, signal.SIGCONT)
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
            value = mac_process.info(child) if child else None
            if child_start and value is not None and value[0] == child_start:
                mac_process.signal_group(child, signal.SIGCONT)
                mac_process.terminate_session(child)
            kill_child(process)
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
        if pid and start:
            mac_process.signal_process(pid, start, signal.SIGTERM)
        if process.poll() is None:
            kill_child(process)
        process.stdout.close(); process.stderr.close()


def sandbox_denials():
    try:
        result = subprocess.run(['/usr/bin/log', 'show', '--last', '2m', '--style', 'compact', '--predicate',
            'sender == "Sandbox" OR eventMessage CONTAINS "deny"'],
            stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, timeout=2)
        lines = result.stdout.decode('utf-8', errors='replace').splitlines()
        return '\n'.join(line[-1000:] for line in lines[-8:]) or 'No sandbox denials were available.'
    except (OSError, subprocess.TimeoutExpired) as error:
        return 'Sandbox denials unavailable: ' + type(error).__name__


def sandbox_failure(kind, resolved, returncode, stdout=b'', stderr=b''):
    return (f'Confined {kind} launch failed: returncode={returncode}; interpreter={resolved["executable"]}; '
            f'sys.executable={resolved["launcher"]}; base_prefix={resolved["base_prefix"]}; '
            f'stdout={stdout!r}; stderr={stderr!r}\nLast sandbox denials:\n{sandbox_denials()}')


def sandbox(root):
    from mac_sandbox import runtime
    resolved = runtime()
    directory, account = root / 'sandbox-worker', root / 'sandbox-account'
    directory.mkdir(mode=0o700); account.mkdir(mode=0o700)
    (account / 'native-owner-v1').write_text('Mac fixture account; no credentials')
    (account / 'fixture.txt').write_text('allowed')
    forbidden = root / 'ungranted.txt'
    forbidden.write_text('private fixture')
    (directory / 'parent_watch.py').write_bytes(Path(__file__).with_name('parent_watch.py').read_bytes())
    worker = directory / 'worker.py'

    def check(kind, python, extra=''):
        worker.write_text("import asyncio, ctypes, select, ssl\nfrom pathlib import Path\nfrom parent_watch import bind_parent\nbind_parent()\n"
            + extra
            + 'account=Path(' + repr(str(account)) + ')\n'
            + "assert (account/'fixture.txt').read_text() == 'allowed'\n"
            + "try:\n    Path(" + repr(str(forbidden)) + ").read_text()\n    raise AssertionError('ungranted read allowed')\nexcept PermissionError: pass\n"
            + "try:\n    (account/'written.txt').write_text('fixture')\n    written=True\nexcept PermissionError: written=False\n"
            + 'assert written == ' + repr(kind == 'account') + '\n'
            + "import os\nassert 'CAPRI_MAC_SMOKE_SENTINEL' not in os.environ\nprint('confined',flush=True)\n")
        environment = dict(os.environ, CAPRI_MAC_SMOKE_SENTINEL='fixture')
        interpreter = dict(resolved, launcher=str(python))
        try:
            result = subprocess.run([str(python), '-B', '-E', '-S', str(Path(__file__).with_name('sandbox.py')),
                '--kind', kind, '--python', str(python), '--worker', str(worker), '--resource', str(account)],
                input=b'', stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=3, env=environment)
        except subprocess.TimeoutExpired as error:
            raise AssertionError(sandbox_failure(kind, interpreter, 'timeout', error.stdout, error.stderr)) from error
        except OSError as error:
            raise AssertionError(sandbox_failure(kind, interpreter, 'not started', stderr=str(error))) from error
        if result.returncode != 0 or result.stdout.strip() != b'confined':
            raise AssertionError(sandbox_failure(kind, interpreter, result.returncode, result.stdout, result.stderr))

    for kind in ('plan', 'account'):
        check(kind, sys.executable)

    import sysconfig
    import venv
    virtual = root / 'sandbox-venv'
    venv.EnvBuilder(with_pip=False, symlinks=True).create(virtual)
    packages = Path(sysconfig.get_path('purelib', scheme='posix_prefix', vars={'base': str(virtual)}))
    packages.mkdir(parents=True, exist_ok=True)
    early_site = root / 'unconfined-site-hook.txt'
    (packages / 'capri_sandbox_fixture.py').write_text('from pathlib import Path\ntry:\n    Path(' + repr(str(forbidden)) + ').read_text()\n'
        + 'except PermissionError:\n    confined=True\nelse:\n    confined=False\n    Path(' + repr(str(early_site)) + ").write_text('unconfined site hook')\n")
    (packages / 'capri_sandbox_fixture.pth').write_text('import capri_sandbox_fixture\n')
    check('plan', virtual / 'bin/python3', 'import capri_sandbox_fixture\nassert capri_sandbox_fixture.confined\n'
          + 'assert not Path(' + repr(str(early_site)) + ').exists()\n')


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

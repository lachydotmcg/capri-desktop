"""Offline Mac process fixtures, invoked only by Capri --smoke."""
import json
import itertools
import os
from pathlib import Path
import signal
import subprocess
import sys
import time

sys.dont_write_bytecode = True
os.environ['PYTHONDONTWRITEBYTECODE'] = '1'

import mac_process


STARTED = b'capri-sandbox-started'
STARTUP_GROUPS = (
    ('mach-lookups', '(allow mach-lookup)'),
    ('posix-shm', '(allow ipc-posix-shm-read*)'),
    ('sysctl', '(allow sysctl-read)'),
    ('file-read-metadata', '(allow file-read-metadata)'),
    ('file-read*', '(allow file-read*)'),
    ('process-info', '(allow process-info*)'),
    ('ioctl/dev', '(allow file-ioctl)\n(allow file-read* (subpath "/dev"))'),
)
CAPTURE_LAUNCH = '''import json, os, runpy, sys
record, script = sys.argv[1:3]
sys.path.insert(0, os.path.dirname(script))
sys.argv = sys.argv[2:]
execute = os.execve
def capture(path, arguments, environment):
    with open(record, 'w', encoding='utf-8') as stream:
        json.dump(dict(path=path, arguments=arguments, environment=environment, cwd=os.getcwd()), stream)
    execute(path, arguments, environment)
os.execve = capture
runpy.run_path(script, run_name='__main__')
'''


def clipped(value, limit):
    value = str(value)
    return value if len(value) <= limit else value[:limit - 3] + '...'


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


def sandbox_denials(executable):
    name = Path(executable).name
    predicate = ('(process == ' + json.dumps(name) + ' OR eventMessage CONTAINS ' + json.dumps(name + '(')
        + ' OR eventMessage CONTAINS ' + json.dumps(name + '[') + ') AND eventMessage CONTAINS[c] "deny"')
    try:
        result = subprocess.run(['/usr/bin/log', 'show', '--last', '30s', '--style', 'compact', '--info', '--debug',
            '--predicate', predicate], stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=20)
        lines = result.stdout.decode('utf-8', errors='replace').splitlines()
        tail = '\n'.join(line[-300:] for line in lines[-3:])
        if result.returncode:
            tail = f'Log query exited {result.returncode}: ' + clipped(result.stderr.decode('utf-8', errors='replace'), 180) + '\n' + tail
        return clipped(tail, 900) or 'No matching sandbox denials in the last 30 s.'
    except (OSError, subprocess.TimeoutExpired) as error:
        partial = getattr(error, 'stdout', None) or b''
        return clipped('Sandbox denials unavailable: ' + type(error).__name__ + '\n' + partial.decode('utf-8', errors='replace')[-700:], 900)


def sandbox_bisect(record, stdout=b''):
    if STARTED in (stdout or b'').splitlines():
        return 'Bisection: Python reached the startup marker; the fixture failed after startup.'
    try:
        launch = json.loads(record.read_text(encoding='utf-8'))
        arguments = launch['arguments']
        if launch['path'] != '/usr/bin/sandbox-exec' or arguments[:2] != ['sandbox-exec', '-p']:
            raise ValueError('Not a captured Seatbelt launch')
        base = arguments[2]
        environment, cwd = launch['environment'], launch['cwd']
    except (OSError, ValueError, KeyError, TypeError, IndexError) as error:
        return 'Bisection unavailable: ' + type(error).__name__ + ' (no confined launch captured).'

    deadline = time.monotonic() + 30
    outcomes, singles = {}, []

    def attempt(indices):
        if indices in outcomes:
            return outcomes[indices]
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            return None
        command = [launch['path'], *arguments[1:]]
        command[2] = base + '\n' + '\n'.join(STARTUP_GROUPS[index][1] for index in indices)
        try:
            result = subprocess.run(command, input=b'', stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                timeout=min(2, remaining), env=environment, cwd=cwd)
            output, state = result.stdout, 'exit=' + str(result.returncode)
        except subprocess.TimeoutExpired as error:
            output, state = error.stdout or b'', 'timeout'
        except OSError as error:
            output, state = b'', type(error).__name__
        started = STARTED in output.splitlines()
        outcomes[indices] = (started, ('started/' if started else '') + state)
        return outcomes[indices]

    original = attempt(())
    if original and original[0]:
        return 'Bisection: unmodified replay started; no missing allowance isolated.'

    def report(winner=None, proven=False, reason=''):
        summary = 'Bisection: baseline=' + (original[1] if original else 'not retried') + '; ' + ', '.join(singles)
        summary += f'; tries={len(outcomes)}. '
        if winner is None:
            return summary + reason
        label = 'Smallest startup allowance' if proven else 'Working startup allowances (smallest unresolved: 30 s budget)'
        names = ' + '.join(STARTUP_GROUPS[index][0] for index in winner)
        rules = '\n'.join(STARTUP_GROUPS[index][1] for index in winner)
        return summary + label + ': ' + names + '\n' + rules

    for index, (name, _) in enumerate(STARTUP_GROUPS):
        outcome = attempt((index,))
        if outcome is None:
            return report(reason='30 s budget exhausted; no working allowance isolated.')
        singles.append(name + '=' + outcome[1])
        if outcome[0]:
            return report((index,), proven=True)

    winner = tuple(range(len(STARTUP_GROUPS)))
    together = attempt(winner)
    if together is None or not together[0]:
        return report(reason='All groups: ' + (together[1] if together else '30 s budget exhausted') + '; no working allowance isolated.')
    for index in range(len(STARTUP_GROUPS)):
        candidate = tuple(value for value in winner if value != index)
        outcome = attempt(candidate)
        if outcome is None:
            return report(winner)
        if outcome[0]:
            winner = candidate
    for size in range(2, len(winner)):
        for candidate in itertools.combinations(range(len(STARTUP_GROUPS)), size):
            outcome = attempt(candidate)
            if outcome is None:
                return report(winner)
            if outcome[0]:
                return report(candidate, proven=True)
    return report(winner, proven=True)


def sandbox_failure(kind, resolved, returncode, stdout=b'', stderr=b'', record=None):
    denials = sandbox_denials(resolved['executable'])
    bisection = sandbox_bisect(record, stdout) if record is not None else 'Bisection unavailable: no launch captured.'
    return clipped((f'Confined {kind} launch failed: returncode={returncode}; interpreter={clipped(resolved["executable"], 240)}; '
        f'sys.executable={clipped(resolved["launcher"], 160)}; base_prefix={clipped(resolved["base_prefix"], 120)}; '
        f'stdout={clipped(repr(stdout), 160)}; stderr={clipped(repr(stderr), 240)}\n'
        + clipped(bisection, 1100) + '\nLast sandbox denials:\n' + denials), 3000)


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
        worker.write_text("print('capri-sandbox-started',flush=True)\nimport asyncio, ctypes, select, ssl\nfrom pathlib import Path\nfrom parent_watch import bind_parent\nbind_parent()\n"
            + extra
            + 'account=Path(' + repr(str(account)) + ')\n'
            + "assert (account/'fixture.txt').read_text() == 'allowed'\n"
            + "try:\n    Path(" + repr(str(forbidden)) + ").read_text()\n    raise AssertionError('ungranted read allowed')\nexcept PermissionError: pass\n"
            + "try:\n    Path(" + repr(str(forbidden)) + ").write_text('changed')\n    raise AssertionError('ungranted write allowed')\nexcept PermissionError: pass\n"
            + "try:\n    (account/'written.txt').write_text('fixture')\n    written=True\nexcept PermissionError: written=False\n"
            + 'assert written == ' + repr(kind == 'account') + '\n'
            + "import os\nassert 'CAPRI_MAC_SMOKE_SENTINEL' not in os.environ\nprint('confined',flush=True)\n")
        environment = dict(os.environ, CAPRI_MAC_SMOKE_SENTINEL='fixture')
        interpreter = dict(resolved, launcher=str(python))
        record = root / 'sandbox-launch.json'
        record.unlink(missing_ok=True)
        try:
            result = subprocess.run([str(python), '-B', '-E', '-S', '-c', CAPTURE_LAUNCH,
                str(record), str(Path(__file__).with_name('sandbox.py')),
                '--kind', kind, '--python', str(python), '--worker', str(worker), '--resource', str(account)],
                input=b'', stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=3, env=environment)
        except subprocess.TimeoutExpired as error:
            raise AssertionError(sandbox_failure(kind, interpreter, 'timeout', error.stdout, error.stderr, record)) from error
        except OSError as error:
            raise AssertionError(sandbox_failure(kind, interpreter, 'not started', stderr=str(error), record=record)) from error
        if result.returncode != 0 or result.stdout.splitlines() != [STARTED, b'confined']:
            raise AssertionError(sandbox_failure(kind, interpreter, result.returncode, result.stdout, result.stderr, record))

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

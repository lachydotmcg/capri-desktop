"""Offline Mac process fixtures, invoked only by Capri --smoke."""
import json
import itertools
import os
from pathlib import Path
import re
import signal
import subprocess
import sys
import tempfile
import time

sys.dont_write_bytecode = True
os.environ['PYTHONDONTWRITEBYTECODE'] = '1'

import mac_process
from mac_sandbox import ancestor_directories


STARTED = b'capri-sandbox-started'
DIAGNOSTIC_SECONDS = 120
SANDBOX_REPORTS = 'sender == "Sandbox" OR subsystem == "com.apple.sandbox.reporting" OR eventMessage CONTAINS "deny("'
STARTUP_GROUPS = (
    ('mach-lookups', '(allow mach-lookup)'),
    ('posix-shm', '(allow ipc-posix-shm-read*)'),
    ('sysctl', '(allow sysctl-read)'),
    ('file-read-metadata', '(allow file-read-metadata)'),
    ('file-read*', '(allow file-read*)'),
    ('process-info', '(allow process-info*)'),
    ('ioctl/dev', '(allow file-ioctl)\n(allow file-read* (subpath "/dev"))'),
)
CAPTURE_LAUNCH = '''import json, os, runpy, sys, sysconfig, time
record, script = sys.argv[1:3]
sys.path.insert(0, os.path.dirname(script))
sys.argv = sys.argv[2:]
execute = os.execve
def capture(path, arguments, environment):
    started = time.strftime('%Y-%m-%d %H:%M:%S')
    with open(record, 'w', encoding='utf-8') as stream:
        json.dump(dict(path=path, arguments=arguments, environment=environment, cwd=os.getcwd(),
            pid=os.getpid(), started=started, stdlib=sysconfig.get_path('stdlib'),
            home=os.path.expanduser('~'), temporary=os.environ.get('TMPDIR', '')), stream)
    execute(path, arguments, environment)
os.execve = capture
runpy.run_path(script, run_name='__main__')
'''


def clipped(value, limit):
    value = str(value)
    if limit <= 3:
        return value[:max(0, limit)]
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


def captured_launch(record):
    launch = json.loads(record.read_text(encoding='utf-8'))
    arguments = launch['arguments']
    if launch['path'] != '/usr/bin/sandbox-exec' or arguments[:2] != ['sandbox-exec', '-p'] or len(arguments) < 4:
        raise ValueError('Not a captured Seatbelt launch')
    if not isinstance(launch['environment'], dict) or not isinstance(launch['cwd'], str):
        raise ValueError('Missing captured environment or cwd')
    return launch


def denied_operations(output, pids=(), name='Python'):
    events = []
    identities = [re.compile(r'(?:\(|\[|\bpid[\s:=]+)' + re.escape(str(pid)) + r'(?!\d)', re.IGNORECASE) for pid in pids if pid]
    for line in output.decode('utf-8', errors='replace').splitlines():
        if not (name + '(' in line or any(identity.search(line) for identity in identities)):
            continue
        denied = re.search(r'\bdeny\([^)]*\)\s+(\S+)(?:\s+(.*))?', line)
        if denied:
            events.append((denied[1], (denied[2] or '').strip()))
    return list(dict.fromkeys(events))


def denial_report(control, events, errors=(), limit=900):
    lines = [control, *errors]
    events = list(dict.fromkeys(events))
    for index, (operation, path) in enumerate(events):
        line = operation + (' ' + path if path else '')
        remaining = limit - len('\n'.join(lines)) - 1
        if len(line) > remaining - 45:
            lines.append(clipped(line, max(0, remaining - 45)))
            lines.append(f'... {len(events) - index} distinct denials truncated/omitted.')
            break
        lines.append(line)
    if not events:
        lines.append('No matching child sandbox denials.')
    return clipped('\n'.join(lines), limit)


def bounded_launch(command, **kwargs):
    with subprocess.Popen(command, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.PIPE, **kwargs) as process:
        try:
            process.communicate(timeout=2)
            state = 'exit=' + str(process.returncode)
        except subprocess.TimeoutExpired:
            process.kill()
            process.communicate(timeout=2)
            state = 'timeout'
        return process.pid, state


def sandbox_denials(executable, record=None, started=None):
    launch, errors, events = None, [], []
    if record is not None:
        try:
            launch = captured_launch(record)
        except (OSError, ValueError, KeyError, TypeError, IndexError):
            pass
    started = (launch or {}).get('started') or started or time.strftime('%Y-%m-%d %H:%M:%S')
    pids = [(launch or {}).get('pid')]
    name = Path(executable).name
    parent = record.parent if record is not None else None
    try:
        with tempfile.TemporaryDirectory(prefix='capri-denial-', dir=parent) as directory:
            target = Path(directory).resolve() / 'denied.txt'
            target.write_text('sandbox denial control', encoding='utf-8')
            profile = '(version 1)\n(allow default)\n(deny file-read-data (literal ' + json.dumps(str(target), ensure_ascii=False) + '))'
            command = ['/usr/bin/sandbox-exec', '-p', profile, '/bin/cat', str(target)]

            def control():
                try:
                    return bounded_launch(command)
                except OSError as error:
                    errors.append('Control launch: ' + type(error).__name__)
                    return None, 'not started'

            def observed(output, pid):
                return any(operation == 'file-read-data' and str(target) == path.strip('"')
                    for operation, path in denied_operations(output, (pid,), 'cat'))

            pid, state = control()
            time.sleep(2)
            output = b''
            try:
                result = subprocess.run(['/usr/bin/log', 'show', '--start', started, '--style', 'compact', '--info', '--debug',
                    '--predicate', SANDBOX_REPORTS], stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=20)
                output = result.stdout
                if result.returncode:
                    errors.append(f'Log query exited {result.returncode}: ' + clipped(result.stderr.decode('utf-8', errors='replace'), 150))
            except (OSError, subprocess.TimeoutExpired) as error:
                output = getattr(error, 'stdout', None) or b''
                errors.append('Log show: ' + type(error).__name__)
            events.extend(denied_operations(output, pids, name))
            if observed(output, pid):
                return denial_report('Denial control: seen via log show (' + state + ').', events, errors)

            with tempfile.TemporaryFile() as stream_output, tempfile.TemporaryFile() as stream_error:
                stream = None
                stream_seen = False
                try:
                    stream = subprocess.Popen(['/usr/bin/log', 'stream', '--style', 'compact', '--info', '--debug',
                        '--predicate', SANDBOX_REPORTS], stdin=subprocess.DEVNULL, stdout=stream_output, stderr=stream_error)
                    time.sleep(.5)
                    pid, state = control()
                    if launch is not None:
                        try:
                            replay_pid, _ = bounded_launch([launch['path'], *launch['arguments'][1:]],
                                env=launch['environment'], cwd=launch['cwd'])
                            pids.append(replay_pid)
                        except OSError as error:
                            errors.append('Stream replay: ' + type(error).__name__)
                    time.sleep(2)
                except OSError as error:
                    errors.append('Log stream: ' + type(error).__name__)
                finally:
                    if stream is not None:
                        if stream.poll() is None:
                            stream.terminate()
                        try:
                            stream.wait(timeout=2)
                        except subprocess.TimeoutExpired:
                            stream.kill()
                            stream.wait(timeout=2)
                        stream_output.seek(0)
                        output = stream_output.read()
                        events.extend(denied_operations(output, pids, name))
                        stream_seen = observed(output, pid)
                        if stream.returncode not in (0, -signal.SIGTERM):
                            stream_error.seek(0)
                            errors.append('Log stream exited ' + str(stream.returncode) + ': '
                                + clipped(stream_error.read().decode('utf-8', errors='replace'), 150))
                control_status = 'seen via log stream (log show unseen; ' + state + ')' if stream_seen else 'not seen via log show or log stream (' + state + ')'
                return denial_report('Denial control: ' + control_status + '.', events, errors)
    except (OSError, subprocess.TimeoutExpired) as error:
        errors.append('Sandbox denials unavailable: ' + type(error).__name__)
        return denial_report('Denial control: not seen; capture failed.', events, errors)


def startup_ancestors(launch):
    environment = launch['environment']
    paths = ancestor_directories((launch['arguments'][3], environment.get('__PYVENV_LAUNCHER__')),
        (launch.get('stdlib'), launch['cwd'], launch.get('home', Path.home()), launch.get('temporary', os.environ.get('TMPDIR')),
         environment.get('HOME'), environment.get('TMPDIR')))
    return tuple(('literal', path) for path in sorted(paths))


def startup_paths():
    home = Path.home()
    paths = [('literal', str(home / '.CFUserTextEncoding')), ('subpath', str(home))]
    temporary = os.environ.get('TMPDIR', '')
    if temporary.startswith('/'):
        paths.append(('subpath', temporary.rstrip('/')))
    paths.extend(('subpath', path) for path in ('/private/var/folders', '/private/tmp', '/private/var', '/private/etc',
        '/Library', '/System', '/usr', '/opt/homebrew', '/usr/local', '/Applications', '/dev', '/Volumes'))
    return tuple(dict.fromkeys(paths))


def path_rule(path):
    kind, value = path
    if kind not in ('literal', 'subpath') or any(ord(c) < 32 or ord(c) == 127 for c in value):
        raise ValueError('Invalid diagnostic read path')
    return '(allow file-read* (' + kind + ' ' + json.dumps(value, ensure_ascii=False) + '))'


def path_children(path):
    children = [('literal', path)]
    with os.scandir(path) as entries:
        for entry in entries:
            if entry.is_symlink():
                children.append(('literal', entry.path))
                target = Path(entry.path).resolve()
                children.append(('subpath' if target.is_dir() else 'literal', str(target)))
            else:
                children.append(('subpath' if entry.is_dir(follow_symlinks=False) else 'literal', entry.path))
            if len(children) > 2048:
                raise ValueError('Directory has too many children')
    children = tuple(sorted(set(children)))
    if sum(len(path_rule(child)) for child in children) > 128000:
        raise ValueError('Directory paths exceed the probe limit')
    return children


def sandbox_path_bisect(attempt, deadline, ancestors=()):
    winner = (('subpath', '/'),)
    history, singles, early, limits = [], [], [], set()

    def probe(paths):
        return attempt(tuple(path_rule(path) for path in paths))

    def remember(paths):
        nonlocal winner
        winner = tuple(paths)
        history.append(winner)

    def report():
        if not history:
            return 'Path bisection: no working path set isolated within the 120 s budget.'
        shown = next((paths for paths in reversed(history) if sum(len(path_rule(path)) + 1 for path in paths) <= 1200), history[0])
        if shown != winner:
            limits.add('report size')
        label = 'Working startup path set (unresolved: ' + ', '.join(sorted(limits)) + ')' if limits else 'Smallest tested startup path set'
        summary = 'Path bisection: ' + clipped(', '.join(singles), 180) + '\n'
        if early:
            summary += '; '.join(early) + '\n'
        return summary + label + ':\n' + '\n'.join(path_rule(path) for path in shown)

    original = probe(winner)
    if original is None or not original[0]:
        return 'Path bisection: root replay ' + (original[1] if original else 'exceeded the 120 s budget') + '; no stable path result.'
    remember(winner)
    roots = startup_paths()
    combined = tuple(dict.fromkeys(roots + ancestors))
    ancestor_result = probe(combined)
    early.append('listed roots+ancestors=' + (ancestor_result[1] if ancestor_result else 'budget'))
    if ancestor_result and ancestor_result[0]:
        remember(combined)
    for operation in ('file-read-metadata', 'file-read-xattr'):
        outcome = attempt(tuple(path_rule(path) for path in roots) + ('(allow ' + operation + ')',))
        early.append('roots+' + operation + '=' + (outcome[1] if outcome else 'budget'))
    for path in roots:
        outcome = probe((path,))
        if outcome is None:
            limits.add('120 s budget')
            return report()
        singles.append(path[1] + '=' + outcome[1])
        if outcome[0]:
            remember((path,))
            break
    else:
        outcome = probe(roots)
        if outcome is None:
            limits.add('120 s budget')
            return report()
        if outcome[0]:
            remember(roots)
        elif ancestor_result and ancestor_result[0]:
            remember(combined)
        else:
            early.append('listed roots failed; splitting /')

    def reduce(paths, fixed=()):
        paths, count = tuple(paths), 2
        while len(paths) > 1:
            chunks = [paths[index * len(paths) // count:(index + 1) * len(paths) // count] for index in range(count)]
            candidates = chunks + [tuple(path for path in paths if path not in chunk) for chunk in chunks]
            for candidate in dict.fromkeys(candidates):
                outcome = probe(fixed + candidate)
                if outcome is None:
                    limits.add('120 s budget')
                    return paths
                if outcome[0]:
                    paths, count = candidate, 2
                    remember(fixed + paths)
                    break
            else:
                if count == len(paths):
                    break
                count = min(len(paths), count * 2)
        return paths

    reduce(winner)
    visited = set()
    while True:
        if time.monotonic() >= deadline:
            limits.add('120 s budget')
            return report()
        pending = next((path for path in winner if path[0] == 'subpath' and path not in visited), None)
        if pending is None:
            return report()
        visited.add(pending)
        fixed = tuple(path for path in winner if path != pending)
        literal = ('literal', pending[1])
        outcome = probe(fixed + (literal,))
        if outcome is None:
            limits.add('120 s budget')
            return report()
        if outcome[0]:
            remember(fixed + (literal,))
            continue
        try:
            children = path_children(pending[1])
        except (OSError, ValueError, RuntimeError):
            limits.add('unreadable or oversized directory')
            continue
        outcome = probe(fixed + children)
        if outcome is None:
            limits.add('120 s budget')
            return report()
        if not outcome[0]:
            limits.add('directory split did not reproduce startup')
            continue
        remember(fixed + children)
        reduce(children, fixed)


def sandbox_bisect(record, stdout=b''):
    if STARTED in (stdout or b'').splitlines():
        return 'Bisection: Python reached the startup marker; the fixture failed after startup.'
    try:
        launch = captured_launch(record)
        arguments = launch['arguments']
        base = arguments[2]
        environment, cwd = launch['environment'], launch['cwd']
    except (OSError, ValueError, KeyError, TypeError, IndexError) as error:
        return 'Bisection unavailable: ' + type(error).__name__ + ' (no confined launch captured).'

    deadline = time.monotonic() + DIAGNOSTIC_SECONDS
    context = '; cwd=' + clipped(json.dumps(cwd, ensure_ascii=False), 160)
    outcomes, singles = {}, []

    def replay(rules):
        if rules in outcomes:
            return outcomes[rules]
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            return None
        command = [launch['path'], *arguments[1:]]
        command[2] = base + '\n' + '\n'.join(rules)
        try:
            result = subprocess.run(command, input=b'', stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                timeout=min(2, remaining), env=environment, cwd=cwd)
            output, state = result.stdout, 'exit=' + str(result.returncode)
        except subprocess.TimeoutExpired as error:
            output, state = error.stdout or b'', 'timeout'
        except OSError as error:
            output, state = b'', type(error).__name__
        started = STARTED in output.splitlines()
        outcomes[rules] = (started, ('started/' if started else '') + state)
        return outcomes[rules]

    def attempt(indices):
        return replay(tuple(STARTUP_GROUPS[index][1] for index in indices))

    original = attempt(())
    if original and original[0]:
        return 'Bisection: unmodified replay started' + context + '; no missing allowance isolated.'

    def report(winner=None, proven=False, reason=''):
        summary = 'Bisection: baseline=' + (original[1] if original else 'not retried') + '; ' + ', '.join(singles)
        summary += f'; tries={len(outcomes)}' + context + '. '
        if winner is None:
            return summary + reason
        label = 'Smallest startup allowance' if proven else 'Working startup allowances (smallest unresolved: 120 s budget)'
        names = ' + '.join(STARTUP_GROUPS[index][0] for index in winner)
        rules = '\n'.join(STARTUP_GROUPS[index][1] for index in winner)
        return summary + label + ': ' + names + '\n' + rules

    for index, (name, _) in enumerate(STARTUP_GROUPS):
        outcome = attempt((index,))
        if outcome is None:
            return report(reason='120 s budget exhausted; no working allowance isolated.')
        singles.append(name + '=' + outcome[1])
        if outcome[0]:
            if name == 'file-read*':
                paths = sandbox_path_bisect(replay, deadline, startup_ancestors(launch))
                return 'Bisection: baseline=' + original[1] + '; file-read*=' + outcome[1] + f'; tries={len(outcomes)}' + context + '.\n' + paths
            return report((index,), proven=True)

    winner = tuple(range(len(STARTUP_GROUPS)))
    together = attempt(winner)
    if together is None or not together[0]:
        return report(reason='All groups: ' + (together[1] if together else '120 s budget exhausted') + '; no working allowance isolated.')
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


def sandbox_failure(kind, resolved, returncode, stdout=b'', stderr=b'', record=None, started=None):
    denials = sandbox_denials(resolved['executable'], record, started)
    try:
        bisection = sandbox_bisect(record, stdout) if record is not None else 'Bisection unavailable: no launch captured.'
    except (OSError, ValueError, TypeError, RuntimeError) as error:
        bisection = 'Bisection unavailable: ' + type(error).__name__ + '; original confined launch failure retained.'
    header = (f'Confined {kind} launch failed: returncode={returncode}; interpreter={clipped(resolved["executable"], 200)}; '
        f'sys.executable={clipped(resolved["launcher"], 100)}; base_prefix={clipped(resolved["base_prefix"], 100)}; '
        f'stdout={clipped(repr(stdout), 100)}; stderr={clipped(repr(stderr), 140)}\n')
    return header + bisection + '\nLast sandbox denials:\n' + clipped(denials, min(900, 2975 - len(header) - len(bisection)))


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
        started = time.strftime('%Y-%m-%d %H:%M:%S')
        try:
            result = subprocess.run([str(python), '-B', '-E', '-S', '-c', CAPTURE_LAUNCH,
                str(record), str(Path(__file__).with_name('sandbox.py')),
                '--kind', kind, '--python', str(python), '--worker', str(worker), '--resource', str(account)],
                input=b'', stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=3, env=environment)
        except subprocess.TimeoutExpired as error:
            raise AssertionError(sandbox_failure(kind, interpreter, 'timeout', error.stdout, error.stderr, record, started)) from error
        except OSError as error:
            raise AssertionError(sandbox_failure(kind, interpreter, 'not started', stderr=str(error), record=record, started=started)) from error
        if result.returncode != 0 or result.stdout.splitlines() != [STARTED, b'confined']:
            raise AssertionError(sandbox_failure(kind, interpreter, result.returncode, result.stdout, result.stderr, record, started))

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

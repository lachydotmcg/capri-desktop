"""Mac interpreter selection, Seatbelt grants and smoke diagnostics with fake Darwin APIs."""
import ctypes
import importlib.util
import json
import os
from pathlib import Path
import ssl
import subprocess
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch


BRIDGE = Path(__file__).resolve().parents[2] / 'cross/bridge'


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


sandbox = load('mac_sandbox', BRIDGE / 'mac_sandbox.py')
with patch.dict(sys.modules, mac_process=Mock()):
    smoke = load('mac_sandbox_smoke_checks', BRIDGE / 'mac_smoke.py')


class MacSandboxChecks(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name).resolve()
        self.prefix = self.root / 'python-runtime'
        self.executable = self.prefix / 'Resources/Python.app/Contents/MacOS/Python'
        self.executable.parent.mkdir(parents=True)
        self.executable.touch()
        self.launcher = self.root / 'venv/bin/python3'
        self.launcher.parent.mkdir(parents=True)
        self.launcher.touch()
        self.library = self.root / 'openssl/lib/libssl.dylib'
        self.library.parent.mkdir(parents=True)
        self.library.touch()
        self.resolved = dict(executable=str(self.executable), launcher=str(self.launcher), base_prefix=str(self.prefix),
                             prefixes={str(self.prefix)}, libraries={str(self.executable), str(self.library)})

    def measure(self, prefix=None):
        images = [os.fsencode(self.executable), os.fsencode(self.library)]
        dyld = Mock()
        dyld._dyld_image_count.return_value = len(images)
        dyld._dyld_get_image_name.side_effect = images.__getitem__
        prefix = str(prefix or self.prefix)
        with patch.object(ctypes, 'CDLL', return_value=dyld), patch.multiple(sys,
                executable=str(self.launcher), base_prefix=prefix, base_exec_prefix=prefix, prefix=prefix, exec_prefix=prefix):
            return sandbox.runtime()

    def test_running_macho_replaces_a_launcher_without_granting_its_parent(self):
        resolved = self.measure()
        self.assertEqual(resolved['executable'], str(self.executable))
        self.assertEqual(resolved['launcher'], str(self.launcher))
        self.assertEqual(resolved['prefixes'], {str(self.prefix)})
        self.assertEqual(resolved['libraries'], {str(self.executable), str(self.library)})
        self.assertNotIn(str(self.library.parent), resolved['prefixes'])
        self.assertNotIn(str(self.launcher.parent.parent), resolved['prefixes'])

    def test_no_site_bootstrap_preserves_the_configured_virtual_environment(self):
        (self.launcher.parent.parent / 'pyvenv.cfg').write_text('include-system-site-packages = false\n')
        self.assertEqual(self.measure()['prefixes'], {str(self.prefix), str(self.launcher.parent.parent)})

    def test_broad_runtime_prefixes_fail_closed_after_resolution(self):
        for prefix in ('/', '/usr', '/usr/local', '/opt/homebrew', str(Path.home())):
            with self.subTest(prefix=prefix), self.assertRaises(ValueError):
                self.measure(prefix)

    def test_mapping_and_execution_are_limited_to_runtime_code(self):
        text = sandbox.profile({'/worker', '/account'}, set(), self.resolved)
        self.assertIn('(deny default)', text)
        self.assertIn('(allow process-exec (literal ' + sandbox.quoted(self.executable) + '))', text)
        self.assertIn('(allow file-read* file-map-executable (literal ' + sandbox.quoted(self.library) + '))', text)
        self.assertNotIn('(subpath ' + sandbox.quoted(self.library.parent) + ')', text)
        self.assertNotIn('(allow process-exec process-fork)', text)
        self.assertNotIn('file-map-executable (subpath "/account")', text)
        self.assertNotIn('file-map-executable (subpath "/worker")', text)
        self.assertNotIn('(subpath "/usr/local")', text)
        self.assertNotIn('(subpath "/opt/homebrew")', text)
        self.assertNotIn('(subpath "/System")', text)
        self.assertIn('file-map-executable (subpath "/System/Volumes/Preboot/Cryptexes/OS")', text)
        self.assertNotIn('(allow file-write*', text)
        writer = sandbox.profile({'/worker'}, {'/account'}, self.resolved)
        self.assertEqual([line for line in writer.splitlines() if line.startswith('(allow file-write*')],
                         ['(allow file-write* (subpath "/account"))'])

    def test_startup_services_shared_memory_and_devices_keep_narrow_boundaries(self):
        text = sandbox.profile({'/worker', '/account'}, set(), self.resolved)
        for name in ('com.apple.system.notification_center', 'com.apple.cfprefsd.daemon',
                     'com.apple.cfprefsd.agent', 'com.apple.diagnosticd'):
            self.assertIn('(global-name "' + name + '")', text)
        self.assertIn('(ipc-posix-name "apple.shm.notification_center")', text)
        self.assertIn('(ipc-posix-name-regex #"^apple[.]cfprefs[.]")', text)
        self.assertNotIn('ipc-posix-shm-write', text)
        writes = [line for line in text.splitlines() if 'file-write' in line]
        self.assertEqual(len(writes), 2)
        for line in writes:
            self.assertIn('(vnode-type CHARACTER-DEVICE)', line)
            self.assertNotIn('(subpath ', line)
        self.assertIn('(literal "/dev/dtracehelper")', writes[1])
        self.assertIn('file-ioctl', writes[1])
        self.assertIn('(target self)', text)
        for rule in ('(allow mach-lookup)', '(allow ipc-posix-shm-read*)', '(allow file-read*)', '(allow file-ioctl)'):
            self.assertNotIn(rule, text)
        self.assertEqual(text.count('(allow network*)'), 1)
        self.assertIn('(subpath ' + sandbox.quoted(self.prefix) + ')', text)

    def launch(self, kind, fail=False):
        worker = self.root / 'worker/worker.py'
        worker.parent.mkdir(exist_ok=True)
        worker.touch()
        account = self.root / 'account'
        account.mkdir(exist_ok=True)
        (account / 'native-owner-v1').touch()
        arguments = SimpleNamespace(kind=kind, cache=None, python=str(self.launcher), worker=str(worker), resource=str(account))
        stat = Path.stat

        def private_stat(path, *args, **kwargs):
            value = stat(path, *args, **kwargs)
            if path == account:
                fields = list(value)
                fields[0] &= ~0o077
                value = os.stat_result(fields)
            return value

        with patch.object(sandbox, 'runtime', return_value=self.resolved), patch.object(Path, 'stat', private_stat), \
                patch.object(os, 'getuid', return_value=account.stat().st_uid, create=True), \
                patch.object(os, 'chdir') as chdir, patch.object(os, 'execve', side_effect=OSError('fixture exec failure') if fail else None) as execute:
            if fail:
                with self.assertRaises(OSError):
                    sandbox.launch(arguments)
            else:
                sandbox.launch(arguments)
            chdir.assert_called_once_with('/var/empty')
        execute.assert_called_once()
        return execute.call_args.args, account

    def test_launch_uses_real_interpreter_with_site_inside_the_sandbox(self):
        for kind in ('plan', 'account'):
            with self.subTest(kind=kind):
                (command, arguments, environment), account = self.launch(kind)
                self.assertEqual(command, '/usr/bin/sandbox-exec')
                self.assertEqual(arguments[3:8], [str(self.executable), '-B', '-E', '-s', '-u'])
                self.assertNotIn('-S', arguments[3:])
                self.assertEqual(environment['__PYVENV_LAUNCHER__'], str(self.launcher))
                self.assertEqual(set(environment), {'PATH', 'HOME', 'TMPDIR', 'LANG', '__PYVENV_LAUNCHER__'})
                self.assertEqual('(allow file-write* (subpath ' + sandbox.quoted(account) + '))' in arguments[2], kind == 'account')

    def test_failed_sandbox_exec_has_no_unsandboxed_retry(self):
        (command, _, _), _ = self.launch('plan', fail=True)
        self.assertEqual(command, '/usr/bin/sandbox-exec')

    def test_unqualified_worker_kinds_are_still_rejected(self):
        for kind in ('voice', 'ears', 'decoder'):
            with self.subTest(kind=kind), patch.object(os, 'execve') as execute, self.assertRaises(ValueError):
                sandbox.launch(SimpleNamespace(kind=kind, cache=None))
            execute.assert_not_called()

    def test_denial_diagnostics_filter_the_interpreter_and_last_30_seconds(self):
        output = '\n'.join('Sandbox deny ' + str(index) for index in range(12)).encode()
        with patch.object(subprocess, 'run', return_value=SimpleNamespace(returncode=0, stdout=output, stderr=b'')) as run:
            text = smoke.sandbox_failure('plan', self.resolved, -9)
        self.assertIn('returncode=-9', text)
        self.assertIn('interpreter=' + str(self.executable), text)
        self.assertIn('sys.executable=' + str(self.launcher), text)
        self.assertIn('base_prefix=' + str(self.prefix), text)
        self.assertNotIn('Sandbox deny 3\n', text)
        self.assertIn('Sandbox deny 11', text)
        self.assertEqual(run.call_args.kwargs['timeout'], 20)
        command = run.call_args.args[0]
        self.assertEqual(command[command.index('--last') + 1], '30s')
        predicate = command[command.index('--predicate') + 1]
        self.assertIn('process == "Python"', predicate)
        self.assertIn('eventMessage CONTAINS "Python("', predicate)
        self.assertIn('AND eventMessage CONTAINS[c] "deny"', predicate)

    def test_diagnostic_errors_do_not_hide_the_confined_launch_failure(self):
        for error in (OSError('fixture log denied'), subprocess.TimeoutExpired('log', 2)):
            with self.subTest(error=error), patch.object(subprocess, 'run', side_effect=error):
                text = smoke.sandbox_failure('account', self.resolved, 71, b'', b'fixture error')
                self.assertIn('returncode=71', text)
                self.assertIn('fixture error', text)
                self.assertIn(type(error).__name__, text)

    def test_smoke_reports_silent_exit_timeouts_and_spawn_errors(self):
        for outcome, code in ((SimpleNamespace(returncode=-9, stdout=b'', stderr=b''), '-9'),
                              (subprocess.TimeoutExpired('python', 3), 'timeout'), (OSError('fixture spawn failed'), 'not started')):
            with self.subTest(code=code), tempfile.TemporaryDirectory(dir=self.root) as temporary, \
                    patch.dict(sys.modules, mac_sandbox=sandbox), patch.object(sandbox, 'runtime', return_value=self.resolved), \
                    patch.object(smoke, 'sandbox_denials', return_value='Sandbox deny fixture'), \
                    patch.object(subprocess, 'run', side_effect=outcome if isinstance(outcome, Exception) else None,
                                 return_value=outcome) as run:
                with self.assertRaises(AssertionError) as raised:
                    smoke.sandbox(Path(temporary))
                self.assertIn('returncode=' + code, str(raised.exception))
                self.assertIn(str(self.executable), str(raised.exception))
                self.assertIn('Sandbox deny fixture', str(raised.exception))
                run.assert_called_once()
                self.assertEqual(run.call_args.args[0][1:4], ['-B', '-E', '-S'])

    def captured_launch(self, record=None):
        record = record or self.root / 'sandbox-launch.json'
        launch = dict(path='/usr/bin/sandbox-exec', arguments=['sandbox-exec', '-p', '(version 1)\n(deny default)',
            str(self.executable), '-B', '-E', '-s', '-u', '/fixture/worker.py'],
            environment={'HOME': '/var/empty', '__PYVENV_LAUNCHER__': str(self.launcher)}, cwd='/var/empty')
        record.write_text(json.dumps(launch), encoding='utf-8')
        return record, launch

    def probe(self, launch, succeeds):
        def run(command, **kwargs):
            self.assertEqual(command[:2], ['/usr/bin/sandbox-exec', '-p'])
            self.assertEqual(command[3:], launch['arguments'][3:])
            self.assertTrue(command[2].startswith(launch['arguments'][2] + '\n'))
            self.assertEqual(kwargs['env'], launch['environment'])
            self.assertEqual(kwargs['cwd'], launch['cwd'])
            self.assertEqual(kwargs['input'], b'')
            self.assertGreater(kwargs['timeout'], 0)
            self.assertLessEqual(kwargs['timeout'], 2)
            extra = command[2][len(launch['arguments'][2]):]
            indices = {index for index, (_, rule) in enumerate(smoke.STARTUP_GROUPS) if rule in extra}
            started = succeeds(indices)
            return SimpleNamespace(returncode=1 if started else -6, stdout=smoke.STARTED + b'\n' if started else b'', stderr=b'')
        return run

    def test_capture_records_the_actual_profile_argv_environment_and_cwd_before_exec(self):
        import runpy
        record, launch = self.captured_launch()
        record.unlink()
        script = str(BRIDGE / 'sandbox.py')

        def main(path, run_name):
            self.assertEqual((path, run_name), (script, '__main__'))
            self.assertEqual(sys.argv, [script, '--kind', 'plan'])
            os.execve(launch['path'], launch['arguments'], launch['environment'])

        with patch.object(sys, 'argv', ['-c', str(record), script, '--kind', 'plan']), \
                patch.object(sys, 'path', list(sys.path)), patch.object(os, 'getcwd', return_value='/var/empty'), \
                patch.object(os, 'execve', side_effect=SystemExit(0)) as execute, patch.object(runpy, 'run_path', side_effect=main):
            with self.assertRaises(SystemExit):
                exec(smoke.CAPTURE_LAUNCH, {})
        execute.assert_called_once_with(launch['path'], launch['arguments'], launch['environment'])
        self.assertEqual(json.loads(record.read_text(encoding='utf-8')), launch)

    def test_bisection_identifies_each_individual_allowance_without_relaxing_other_rules(self):
        record, launch = self.captured_launch()
        for index, (name, rule) in enumerate(smoke.STARTUP_GROUPS):
            with self.subTest(name=name), patch.object(subprocess, 'run', side_effect=self.probe(launch, lambda indices: index in indices)) as run:
                text = smoke.sandbox_bisect(record)
            self.assertIn('Smallest startup allowance: ' + name + '\n' + rule, text)
            self.assertIn('baseline=exit=-6', text)
            self.assertEqual(run.call_count, index + 2)

    def test_bisection_finds_the_smallest_combination_even_with_a_larger_alternative(self):
        record, launch = self.captured_launch()
        succeeds = lambda indices: {1, 2, 3} <= indices or {0, 6} <= indices
        with patch.object(subprocess, 'run', side_effect=self.probe(launch, succeeds)):
            text = smoke.sandbox_bisect(record)
        self.assertIn('Smallest startup allowance: mach-lookups + ioctl/dev', text)
        self.assertIn('(allow mach-lookup)\n(allow file-ioctl)', text)

    def test_bisection_does_not_attribute_a_transient_failure_to_an_allowance(self):
        record, launch = self.captured_launch()
        with patch.object(subprocess, 'run', side_effect=self.probe(launch, lambda indices: True)) as run:
            text = smoke.sandbox_bisect(record)
        run.assert_called_once()
        self.assertIn('unmodified replay started', text)
        self.assertIn('no missing allowance isolated', text)

    def test_bisection_reports_when_all_groups_still_abort(self):
        record, launch = self.captured_launch()
        with patch.object(subprocess, 'run', side_effect=self.probe(launch, lambda indices: False)) as run:
            text = smoke.sandbox_bisect(record)
        self.assertEqual(run.call_count, 9)
        self.assertIn('All groups: exit=-6', text)
        self.assertIn('no working allowance isolated', text)

    def test_bisection_bounds_timeout_and_spawn_failures(self):
        record, _ = self.captured_launch()
        for error, status in ((subprocess.TimeoutExpired('sandbox-exec', 2), 'timeout'), (OSError('fixture spawn error'), 'OSError')):
            with self.subTest(status=status), patch.object(subprocess, 'run', side_effect=error) as run:
                text = smoke.sandbox_bisect(record)
            self.assertIn('baseline=' + status, text)
            self.assertIn('All groups: ' + status, text)
            self.assertTrue(all(call.kwargs['timeout'] <= 2 for call in run.call_args_list))

    def test_bisection_keeps_partial_startup_output_on_timeout(self):
        record, _ = self.captured_launch()
        with patch.object(subprocess, 'run', side_effect=[SimpleNamespace(returncode=-6, stdout=b''),
                subprocess.TimeoutExpired('sandbox-exec', 2, output=smoke.STARTED + b'\n')]):
            text = smoke.sandbox_bisect(record)
        self.assertIn('mach-lookups=started/timeout', text)
        self.assertIn('Smallest startup allowance: mach-lookups', text)

    def test_bisection_has_a_total_deadline_and_does_not_claim_an_unproven_minimum(self):
        record, launch = self.captured_launch()
        for all_work in (False, True):
            now = [0]
            probe = self.probe(launch, lambda indices: len(indices) == len(smoke.STARTUP_GROUPS))

            def run(command, **kwargs):
                result = probe(command, **kwargs)
                if not all_work or result.stdout:
                    now[0] = 31
                return result

            with self.subTest(all_work=all_work), patch.object(smoke.time, 'monotonic', side_effect=lambda: now[0]), \
                    patch.object(subprocess, 'run', side_effect=run) as execute:
                text = smoke.sandbox_bisect(record)
            self.assertIn('30 s budget', text)
            self.assertNotIn('Smallest startup allowance:', text)
            self.assertEqual(execute.call_count, 9 if all_work else 1)
            if all_work:
                self.assertIn('Working startup allowances', text)

    def test_missing_or_invalid_capture_never_uses_an_unsandboxed_fallback(self):
        record, launch = self.captured_launch()
        for invalid in (None, {}, dict(launch, path='/bin/sh'), dict(launch, arguments=['sandbox-exec', '-p'])):
            if invalid is None:
                record.unlink(missing_ok=True)
            else:
                record.write_text(json.dumps(invalid), encoding='utf-8')
            with self.subTest(invalid=invalid), patch.object(subprocess, 'run') as run:
                self.assertIn('Bisection unavailable:', smoke.sandbox_bisect(record))
            run.assert_not_called()

    def test_fixture_failure_after_startup_does_not_trigger_broader_permissions(self):
        record, _ = self.captured_launch()
        with patch.object(subprocess, 'run') as run:
            text = smoke.sandbox_bisect(record, smoke.STARTED + b'\n')
        self.assertIn('fixture failed after startup', text)
        run.assert_not_called()

    def test_failure_report_is_bounded_and_keeps_log_errors_and_original_status(self):
        resolved = {name: 'x' * 4000 for name in ('executable', 'launcher', 'base_prefix')}
        result = SimpleNamespace(returncode=71, stdout=b'Sandbox deny file-read-data /fixture\n' * 200,
                                 stderr=b'fixture log access denied')
        with patch.object(subprocess, 'run', return_value=result):
            text = smoke.sandbox_failure('plan', resolved, -6, b'x' * 20000, b'y' * 20000)
        self.assertLessEqual(len(text), 3000)
        self.assertIn('returncode=-6', text)
        self.assertIn('Log query exited 71', text)
        self.assertIn('fixture log access denied', text)
        self.assertIn('Sandbox deny file-read-data /fixture', text)

    def test_successful_bisection_preserves_the_original_smoke_failure(self):
        with tempfile.TemporaryDirectory(dir=self.root) as temporary:
            root = Path(temporary)
            _, launch = self.captured_launch(root / 'expected.json')
            probe = self.probe(launch, lambda indices: 1 in indices)

            def run(command, **kwargs):
                if command[0] != '/usr/bin/sandbox-exec':
                    record = Path(command[6])
                    record.write_text(json.dumps(launch), encoding='utf-8')
                    return SimpleNamespace(returncode=-6, stdout=b'', stderr=b'')
                return probe(command, **kwargs)

            with patch.dict(sys.modules, mac_sandbox=sandbox), patch.object(sandbox, 'runtime', return_value=self.resolved), \
                    patch.object(smoke, 'sandbox_denials', return_value='Sandbox deny ipc-posix-shm-read-data'), \
                    patch.object(subprocess, 'run', side_effect=run):
                with self.assertRaises(AssertionError) as raised:
                    smoke.sandbox(root)
        self.assertIn('returncode=-6', str(raised.exception))
        self.assertIn('Smallest startup allowance: posix-shm', str(raised.exception))

    def test_successful_smoke_runs_never_bisect_or_query_logs(self):
        import sysconfig
        import venv
        with tempfile.TemporaryDirectory(dir=self.root) as temporary:
            root = Path(temporary)
            result = SimpleNamespace(returncode=0, stdout=smoke.STARTED + b'\nconfined\n', stderr=b'')
            with patch.dict(sys.modules, mac_sandbox=sandbox), patch.object(sandbox, 'runtime', return_value=self.resolved), \
                    patch.object(subprocess, 'run', return_value=result) as run, patch.object(venv, 'EnvBuilder'), \
                    patch.object(sysconfig, 'get_path', return_value=str(root / 'sandbox-venv/site-packages')), \
                    patch.object(smoke, 'sandbox_bisect', side_effect=AssertionError('Unexpected bisection')), \
                    patch.object(smoke, 'sandbox_denials', side_effect=AssertionError('Unexpected log query')):
                smoke.sandbox(root)
            self.assertEqual(run.call_count, 3)


if __name__ == '__main__':
    unittest.main(testRunner=unittest.TextTestRunner(stream=sys.stdout, verbosity=2))

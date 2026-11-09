"""Mac interpreter selection, Seatbelt grants and smoke diagnostics with fake Darwin APIs."""
import ctypes
import importlib.util
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

    def test_denial_diagnostics_keep_a_bounded_tail_with_a_short_timeout(self):
        output = '\n'.join('Sandbox deny ' + str(index) for index in range(12)).encode()
        with patch.object(subprocess, 'run', return_value=SimpleNamespace(stdout=output)) as run:
            text = smoke.sandbox_failure('plan', self.resolved, -9)
        self.assertIn('returncode=-9', text)
        self.assertIn('interpreter=' + str(self.executable), text)
        self.assertIn('sys.executable=' + str(self.launcher), text)
        self.assertIn('base_prefix=' + str(self.prefix), text)
        self.assertNotIn('Sandbox deny 3\n', text)
        self.assertIn('Sandbox deny 11', text)
        self.assertEqual(run.call_args.kwargs['timeout'], 2)
        self.assertIn('sender == "Sandbox" OR eventMessage CONTAINS "deny"', run.call_args.args[0])

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


if __name__ == '__main__':
    unittest.main(testRunner=unittest.TextTestRunner(stream=sys.stdout, verbosity=2))

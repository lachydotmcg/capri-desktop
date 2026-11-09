"""Mac interpreter selection, Seatbelt grants and smoke diagnostics with fake Darwin APIs."""
import ctypes
import importlib.util
import json
import os
from pathlib import Path, PurePosixPath
import re
import ssl
import struct
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

    def macho(self, *commands, magic=0xfeedfacf):
        data = b''
        for kind, name in commands:
            start = 12 if kind == 0x8000001c else 24
            value = os.fsencode(name) + b'\0'
            length = (start + len(value) + 7) // 8 * 8
            data += struct.pack('=3I', kind, length, start) + b'\0' * (start - 12) + value + b'\0' * (length - start - len(value))
        header = struct.pack('=7I', magic, 0, 0, 6, len(commands), len(data), 0)
        return ctypes.create_string_buffer(header + (b'\0' * 4 if magic == 0xfeedfacf else b'') + data)

    def measure(self, prefix=None):
        images = [os.fsencode(self.executable), os.fsencode(self.library)]
        dyld = Mock()
        dyld._dyld_image_count.return_value = len(images)
        dyld._dyld_get_image_name.side_effect = images.__getitem__
        header = self.macho()
        dyld._dyld_get_image_header.return_value = ctypes.addressof(header)
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

    def test_loaded_install_names_dependencies_and_rpaths_are_read_from_macho_memory(self):
        entries = ((0xd, '/opt/homebrew/opt/openssl@3/lib/libssl.3.dylib'),
                   (0xc, '@rpath/libcrypto.3.dylib'), (0x80000018, '/usr/lib/libSystem.B.dylib'),
                   (0x8000001f, '@loader_path/reexport.dylib'), (0x8000001c, '@executable_path/../lib'))
        for magic in (0xfeedface, 0xfeedfacf):
            with self.subTest(magic=magic):
                header = self.macho(*entries, magic=magic)
                names, rpaths = sandbox.library_commands(ctypes.addressof(header))
                self.assertEqual(names, [name for _, name in entries[:-1]])
                self.assertEqual(rpaths, [entries[-1][1]])

    def test_malformed_macho_headers_and_command_bounds_fail_closed(self):
        good = bytes(self.macho((0xd, '/library.dylib')))
        invalid = [b'\0' * 32, good[:16] + struct.pack('=2I', 1, 2 * 1024 * 1024) + good[24:],
                   good[:36] + struct.pack('=I', 4) + good[40:],
                   good[:40] + struct.pack('=I', 8) + good[44:], good[:56] + b'x' * (len(good) - 56)]
        for data in invalid:
            header = ctypes.create_string_buffer(data)
            with self.subTest(data=data), self.assertRaises(ValueError):
                sandbox.library_commands(ctypes.addressof(header))
        with self.assertRaises(ValueError):
            sandbox.library_commands(None)

    def test_every_symlink_hop_keeps_the_alias_full_path_and_exact_link(self):
        alias = self.root / 'lib/libssl.dylib'
        opt = self.root / 'opt/openssl@3'
        link = self.root / 'Cellar/openssl@3/3.6/lib/libssl.dylib'
        real = link.with_name('libssl.3.dylib')
        links = {alias: Path('../opt/openssl@3/lib/libssl.dylib'), opt: Path('../Cellar/openssl@3/3.6'),
                 link: Path('libssl.3.dylib')}
        with patch.object(Path, 'is_symlink', lambda path: path in links), \
                patch.object(Path, 'readlink', lambda path: links[path]):
            paths = sandbox.path_hops(alias)
        for path in (alias, opt, link, real):
            self.assertIn(str(path), paths)
        text = sandbox.profile(set(), set(), dict(self.resolved, libraries=paths))
        for path in (alias, opt, link, real):
            self.assertIn('(literal ' + sandbox.quoted(path) + ')', text)
            self.assertNotIn('(subpath ' + sandbox.quoted(path) + ')', text)
        with patch.object(Path, 'is_symlink', return_value=True), patch.object(Path, 'readlink', return_value=Path('loop')):
            with self.assertRaises(ValueError):
                sandbox.path_hops(alias)

    def test_install_name_aliases_are_granted_only_when_they_resolve_to_loaded_images(self):
        alias = self.root / 'opt/openssl@3/lib/libssl.dylib'
        sibling = alias.with_name('not-loaded.dylib')
        resolve = Path.resolve

        def resolved(path, *args, **kwargs):
            return self.library if path == alias else resolve(path, *args, **kwargs)

        commands = [([str(alias), str(sibling), '@rpath/libssl.dylib'], [str(alias.parent)]),
                    (['@loader_path/libssl.dylib', '@executable_path/unused.dylib'], [])]
        with patch.object(Path, 'resolve', resolved):
            paths = sandbox.library_paths([self.executable, self.library], commands)
        self.assertIn(str(alias), paths)
        self.assertNotIn(str(sibling), paths)
        self.assertNotIn(str(alias.parent), paths)

    def test_homebrew_opt_and_lib_aliases_are_measured_for_both_prefixes(self):
        for root in ('/opt/homebrew', '/usr/local'):
            image = Path(root).absolute() / 'Cellar/openssl@3/3.6/lib/libssl.3.dylib'
            opt = Path(root).absolute() / 'opt/openssl@3/lib/libssl.3.dylib'
            lib = Path(root).absolute() / 'lib/libssl.3.dylib'
            resolve = Path.resolve

            def resolved(path, *args, **kwargs):
                return image if path in (image, opt, lib) else resolve(path, *args, **kwargs)

            with self.subTest(root=root), patch.object(Path, 'resolve', resolved):
                paths = sandbox.library_paths([image], [([], [])])
            self.assertTrue({str(image), str(opt), str(lib)} <= paths)

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

    def test_startup_encoding_and_logging_reads_do_not_grant_the_home_or_preferences_tree(self):
        with patch.object(Path, 'home', return_value=self.root):
            text = sandbox.profile(set(), set(), self.resolved)
        self.assertIn('(allow file-read* (literal ' + sandbox.quoted(self.root / '.CFUserTextEncoding') + '))', text)
        self.assertNotIn('(subpath ' + sandbox.quoted(self.root) + ')', text)
        self.assertIn('(allow file-read* (subpath "/Library/Preferences/Logging"))', text)
        self.assertIn('(allow file-read* (subpath "/System/Library/Preferences/Logging"))', text)
        self.assertNotIn('(subpath "/Library/Preferences")', text)
        self.assertNotIn('(allow file-write*', text)

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
                for path in ('/usr/share/locale', '/usr/share/zoneinfo', '/private/var/db/timezone'):
                    self.assertIn('(allow file-read* (subpath ' + sandbox.quoted(Path(path)) + '))', arguments[2])

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
            with self.subTest(name=name), patch.object(smoke, 'sandbox_path_bisect', return_value='path fixture') as paths, \
                    patch.object(subprocess, 'run', side_effect=self.probe(launch, lambda indices: index in indices)) as run:
                text = smoke.sandbox_bisect(record)
            if name == 'file-read*':
                self.assertIn('path fixture', text)
                paths.assert_called_once()
            else:
                self.assertIn('Smallest startup allowance: ' + name + '\n' + rule, text)
                paths.assert_not_called()
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

    def path_probe(self, launch, required):
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
            paths = [(kind, json.loads(value)) for kind, value in re.findall(r'\(allow file-read\* \((literal|subpath) (.+)\)\)', extra)]
            starts = '(allow file-read*)' in extra or all(any(target == path or
                kind == 'subpath' and target.startswith(path.rstrip('/') + '/') for kind, path in paths) for target in required)
            return SimpleNamespace(returncode=1 if starts else -6, stdout=smoke.STARTED + b'\n' if starts else b'', stderr=b'')
        return run

    def test_path_candidates_include_the_real_home_tmpdir_and_every_requested_root(self):
        temporary = '/private/var/folders/ab/runner/T'
        with patch.object(Path, 'home', return_value=PurePosixPath('/Users/runner')), \
                patch.dict(os.environ, TMPDIR=temporary):
            paths = smoke.startup_paths()
        self.assertEqual(paths[:2], (('literal', '/Users/runner/.CFUserTextEncoding'), ('subpath', '/Users/runner')))
        for path in (temporary, '/private/var/folders', '/private/tmp', '/private/var', '/private/etc', '/Library',
                     '/System', '/usr', '/opt/homebrew', '/usr/local', '/Applications', '/dev', '/Volumes'):
            self.assertIn(('subpath', path), paths)

    def test_path_bisection_is_triggered_by_file_reads_and_finds_the_encoding_literal(self):
        record, launch = self.captured_launch()
        required = '/Users/runner/.CFUserTextEncoding'
        with patch.object(Path, 'home', return_value=PurePosixPath('/Users/runner')), \
                patch.object(smoke, 'path_children', side_effect=AssertionError('No subtree split needed')), \
                patch.object(subprocess, 'run', side_effect=self.path_probe(launch, [required])) as run:
            text = smoke.sandbox_bisect(record)
        self.assertIn('Smallest tested startup path set:\n(allow file-read* (literal "' + required + '"))', text)
        self.assertNotIn('(subpath "/Users/runner")', text)
        self.assertEqual(run.call_count, 8)

    def test_path_bisection_splits_a_working_subtree_and_keeps_multiple_required_files(self):
        record, launch = self.captured_launch()
        required = ['/Library/Preferences/Logging/first.plist', '/Library/Preferences/Logging/second.plist']
        tree = {'/Library': (('literal', '/Library'), ('subpath', '/Library/Caches'), ('subpath', '/Library/Preferences')),
                '/Library/Preferences': (('literal', '/Library/Preferences'), ('subpath', '/Library/Preferences/Logging'),
                                         ('literal', '/Library/Preferences/unused.plist')),
                '/Library/Preferences/Logging': tuple(('literal', path) for path in required + ['/Library/Preferences/Logging/unused.plist'])}
        with patch.object(smoke, 'startup_paths', return_value=(('subpath', '/Library'),)), \
                patch.object(smoke, 'path_children', side_effect=tree.__getitem__) as children, \
                patch.object(subprocess, 'run', side_effect=self.path_probe(launch, required)):
            text = smoke.sandbox_bisect(record)
        for path in required:
            self.assertIn(smoke.path_rule(('literal', path)), text)
        self.assertNotIn('unused.plist"))', text)
        self.assertNotIn('(subpath "/Library")', text)
        self.assertEqual([call.args[0] for call in children.call_args_list], list(tree))
        self.assertIn('Smallest tested startup path set:', text)

    def test_path_bisection_can_require_two_roots_and_refines_them_with_the_other_fixed(self):
        record, launch = self.captured_launch()
        required = ['/Library/first.plist', '/usr/second.dat']
        tree = {root: (('literal', root), ('literal', target), ('literal', root + '/unused'))
                for root, target in zip(('/Library', '/usr'), required)}
        with patch.object(smoke, 'startup_paths', return_value=(('subpath', '/Library'), ('subpath', '/usr'), ('subpath', '/dev'))), \
                patch.object(smoke, 'path_children', side_effect=tree.__getitem__), \
                patch.object(subprocess, 'run', side_effect=self.path_probe(launch, required)):
            text = smoke.sandbox_bisect(record)
        for path in required:
            self.assertIn(smoke.path_rule(('literal', path)), text)
        self.assertNotIn('(subpath ', text)
        self.assertNotIn('/unused"))', text)

    def test_path_bisection_uses_the_same_total_deadline_and_keeps_the_last_working_rules(self):
        record, launch = self.captured_launch()
        now = [0]
        probe = self.path_probe(launch, ['/Library/needed'])

        def run(command, **kwargs):
            result = probe(command, **kwargs)
            if '(subpath "/Library")' in command[2]:
                now[0] = 31
            return result

        with patch.object(smoke, 'startup_paths', return_value=(('subpath', '/Library'),)), \
                patch.object(smoke.time, 'monotonic', side_effect=lambda: now[0]), \
                patch.object(subprocess, 'run', side_effect=run) as execute:
            text = smoke.sandbox_bisect(record)
        self.assertIn('Working startup path set (unresolved: 30 s budget)', text)
        self.assertIn('(allow file-read* (subpath "/Library"))', text)
        self.assertNotIn('Smallest tested', text)
        self.assertEqual(execute.call_count, 8)

    def test_path_bisection_handles_split_errors_without_losing_the_working_parent(self):
        record, launch = self.captured_launch()
        for error in (OSError('fixture directory unreadable'), ValueError('fixture too wide')):
            with self.subTest(error=error), patch.object(smoke, 'startup_paths', return_value=(('subpath', '/Library'),)), \
                    patch.object(smoke, 'path_children', side_effect=error), \
                    patch.object(subprocess, 'run', side_effect=self.path_probe(launch, ['/Library/needed'])):
                text = smoke.sandbox_bisect(record)
            self.assertIn('unreadable or oversized directory', text)
            self.assertIn('(allow file-read* (subpath "/Library"))', text)

    def test_path_replay_keeps_partial_startup_output_from_a_timeout(self):
        record, launch = self.captured_launch()
        required = '/Library/needed'
        probe = self.path_probe(launch, [required])

        def run(command, **kwargs):
            if smoke.path_rule(('literal', required)) in command[2]:
                raise subprocess.TimeoutExpired(command, kwargs['timeout'], output=smoke.STARTED + b'\n')
            return probe(command, **kwargs)

        with patch.object(smoke, 'startup_paths', return_value=(('literal', required),)), \
                patch.object(subprocess, 'run', side_effect=run):
            text = smoke.sandbox_bisect(record)
        self.assertIn(required + '=started/timeout', text)
        self.assertIn('Smallest tested startup path set:\n' + smoke.path_rule(('literal', required)), text)

    def test_failed_root_replay_does_not_claim_that_a_path_set_worked(self):
        record, launch = self.captured_launch()
        for error, state in ((OSError('fixture spawn error'), 'OSError'), (subprocess.TimeoutExpired('sandbox-exec', 2), 'timeout')):
            probe = self.path_probe(launch, ['/Library/needed'])

            def run(command, **kwargs):
                if '(subpath "/")' in command[2]:
                    raise error
                return probe(command, **kwargs)

            with self.subTest(state=state), patch.object(subprocess, 'run', side_effect=run):
                text = smoke.sandbox_bisect(record)
            self.assertIn('Path bisection: root replay ' + state + '; no stable path result.', text)
            self.assertNotIn('Smallest tested', text)

    def test_changed_directory_split_preserves_the_last_working_subtree(self):
        record, launch = self.captured_launch()
        with patch.object(smoke, 'startup_paths', return_value=(('subpath', '/Library'),)), \
                patch.object(smoke, 'path_children', return_value=(('literal', '/Library/unrelated'),)), \
                patch.object(subprocess, 'run', side_effect=self.path_probe(launch, ['/Library/needed'])):
            text = smoke.sandbox_bisect(record)
        self.assertIn('directory split did not reproduce startup', text)
        self.assertIn('(allow file-read* (subpath "/Library"))', text)

    def test_path_bisection_reports_when_listed_roots_do_not_cover_the_missing_read(self):
        record, launch = self.captured_launch()
        with patch.object(smoke, 'startup_paths', return_value=(('subpath', '/Library'), ('subpath', '/usr'))), \
                patch.object(subprocess, 'run', side_effect=self.path_probe(launch, ['/unlisted/needed'])):
            text = smoke.sandbox_bisect(record)
        self.assertIn('listed roots did not reproduce startup', text)
        self.assertIn('(allow file-read* (subpath "/"))', text)
        self.assertNotIn('Smallest tested', text)

    def test_path_bisection_retains_a_proven_set_when_narrow_rules_exceed_the_report_limit(self):
        record, launch = self.captured_launch()
        required = '/Library/' + 'x' * 1500
        with patch.object(smoke, 'startup_paths', return_value=(('literal', required),)), \
                patch.object(subprocess, 'run', side_effect=self.path_probe(launch, [required])):
            text = smoke.sandbox_bisect(record)
        self.assertIn('unresolved: report size', text)
        self.assertTrue(text.endswith('(allow file-read* (subpath "/"))'))
        self.assertLess(len(text), 1800)

    def test_path_split_lists_only_direct_children_and_bounds_oversized_directories(self):
        directory = self.root / 'paths'
        directory.mkdir()
        (directory / 'data').touch()
        (directory / 'nested').mkdir()
        children = smoke.path_children(str(directory))
        self.assertEqual(set(children), {('literal', str(directory)), ('literal', str(directory / 'data')),
                                        ('subpath', str(directory / 'nested'))})
        entries = Mock()
        entries.__enter__ = Mock(return_value=[SimpleNamespace(path='x', is_dir=lambda: False)] * 2048)
        entries.__exit__ = Mock(return_value=False)
        with patch.object(os, 'scandir', return_value=entries), self.assertRaises(ValueError):
            smoke.path_children('/wide')

    def test_path_split_supports_large_homebrew_library_directories(self):
        entries = Mock()
        entries.__enter__ = Mock(return_value=[SimpleNamespace(path='/opt/homebrew/lib/library' + str(index) + '.dylib',
            is_dir=lambda: False) for index in range(512)])
        entries.__exit__ = Mock(return_value=False)
        with patch.object(os, 'scandir', return_value=entries):
            children = smoke.path_children('/opt/homebrew/lib')
        self.assertEqual(len(children), 513)
        self.assertIn(('literal', '/opt/homebrew/lib/library511.dylib'), children)

    def test_diagnostic_read_rules_escape_paths_without_accepting_control_characters(self):
        value = '/a/\\" ) (allow default)'
        rule = smoke.path_rule(('literal', value))
        self.assertEqual(json.loads(rule[len('(allow file-read* (literal '):-2]), value)
        for path in (('literal', '/a\n(allow default)'), ('anything', '/a')):
            with self.subTest(path=path), self.assertRaises(ValueError):
                smoke.path_rule(path)

    def test_failure_report_preserves_long_complete_path_rules_and_original_failure(self):
        record, launch = self.captured_launch()
        required = '/Library/' + 'x' * 950
        resolved = {name: 'x' * 4000 for name in ('executable', 'launcher', 'base_prefix')}
        with patch.object(smoke, 'startup_paths', return_value=(('literal', required),)), \
                patch.object(smoke, 'sandbox_denials', return_value='Sandbox deny fixture\n' * 1000), \
                patch.object(subprocess, 'run', side_effect=self.path_probe(launch, [required])):
            text = smoke.sandbox_failure('plan', resolved, -6, b'x' * 20000, b'y' * 20000, record)
        self.assertLessEqual(len(text), 3000)
        self.assertIn('returncode=-6', text)
        self.assertIn(smoke.path_rule(('literal', required)), text)
        self.assertIn('Last sandbox denials:', text)

    def test_invalid_path_diagnostics_preserve_the_original_failure_and_log_tail(self):
        record, launch = self.captured_launch()
        with patch.object(smoke, 'startup_paths', return_value=(('subpath', '/invalid\npath'),)), \
                patch.object(smoke, 'sandbox_denials', return_value='Sandbox deny fixture'), \
                patch.object(subprocess, 'run', side_effect=self.path_probe(launch, ['/Library/needed'])):
            text = smoke.sandbox_failure('plan', self.resolved, -6, b'', b'original stderr', record)
        self.assertIn('returncode=-6', text)
        self.assertIn('original stderr', text)
        self.assertIn('Bisection unavailable: ValueError', text)
        self.assertIn('Sandbox deny fixture', text)

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

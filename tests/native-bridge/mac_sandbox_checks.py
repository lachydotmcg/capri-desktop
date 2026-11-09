"""Mac interpreter selection, Seatbelt grants and smoke diagnostics with fake Darwin APIs."""
import ctypes
import importlib.util
import json
import os
from pathlib import Path, PurePosixPath
import re
import signal
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
with patch.dict(sys.modules, mac_process=Mock(), mac_sandbox=sandbox):
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
                             stdlib=str(self.prefix / 'lib/python3.15'), prefixes={str(self.prefix)},
                             libraries={str(self.executable), str(self.library)})

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

    def test_ancestor_directories_include_roots_and_resolved_aliases_without_file_leaves(self):
        alias_file = self.root / 'aliases/bin/python3'
        alias_directory = self.root / 'aliases/stdlib'
        directory = self.prefix / 'lib/python3.15'
        resolve = Path.resolve

        def resolved(path, *args, **kwargs):
            return {alias_file: self.executable, alias_directory: directory}.get(path, resolve(path, *args, **kwargs))

        with patch.object(Path, 'resolve', resolved):
            ancestors = sandbox.ancestor_directories((alias_file,), (alias_directory,))
        for parent in (self.executable.parent, alias_file.parent, alias_directory, directory, self.root, Path(self.root.anchor)):
            self.assertIn(str(parent), ancestors)
        self.assertNotIn(str(self.executable), ancestors)
        self.assertNotIn(str(alias_file), ancestors)
        self.assertNotIn(str(directory / 'site-packages'), ancestors)

    def test_production_ancestor_grants_are_read_only_directory_literals(self):
        cwd, home, temporary = (self.root / name for name in ('cwd', 'home', 'temporary'))
        with patch.object(Path, 'cwd', return_value=cwd), patch.object(Path, 'home', return_value=home), \
                patch.dict(os.environ, TMPDIR=str(temporary)):
            expected = sandbox.ancestor_directories((self.executable, self.launcher),
                (self.resolved['stdlib'], cwd, home, temporary))
            text = sandbox.profile({'/worker', '/account'}, set(), self.resolved)
        grants = [line for line in text.splitlines() if line.startswith('(allow file-read-metadata file-read-data ')]
        self.assertEqual(grants, ['(allow file-read-metadata file-read-data (literal ' + sandbox.quoted(path) + '))'
                                 for path in sorted(expected)])
        self.assertIn(str(Path(self.root.anchor)), expected)
        for path in (cwd, home, temporary, self.executable.parent, Path(self.resolved['stdlib'])):
            self.assertIn(str(path), expected)
        for grant in grants:
            self.assertNotIn('subpath', grant)
            self.assertNotIn('file-write', grant)
            self.assertNotIn('file-map-executable', grant)
        self.assertNotIn('(allow file-read*)', text)

    def test_runtime_keeps_measured_ancestors_when_the_confined_cwd_changes(self):
        cwd, home, temporary = (self.root / name for name in ('bootstrap', 'home', 'temporary'))
        with patch.object(Path, 'cwd', return_value=cwd), patch.object(Path, 'home', return_value=home), \
                patch.dict(os.environ, TMPDIR=str(temporary)), patch('sysconfig.get_path', return_value=self.resolved['stdlib']):
            resolved = self.measure()
        self.assertEqual(resolved['stdlib'], self.resolved['stdlib'])
        for path in (cwd, home, temporary, self.executable.parent, Path(self.resolved['stdlib'])):
            self.assertIn(str(path), resolved['ancestors'])
        confined = self.root / 'empty'
        with patch.object(Path, 'cwd', return_value=confined):
            text = sandbox.profile(set(), set(), resolved)
        for path in (cwd, home, temporary, confined):
            self.assertIn('(allow file-read-metadata file-read-data (literal ' + sandbox.quoted(path) + '))', text)

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

    def denial_capture(self, show_control=True, stream_control=False, show_error=None, stream_error=None, query_exit=0,
                       extra=b'', stream_extra=b''):
        record, launch = self.captured_launch()
        calls, controls = [], []

        def bounded(command, **kwargs):
            calls.append(('launch', command, kwargs))
            if command[-2] == '/bin/cat':
                controls.append(command)
                self.assertTrue(Path(command[-1]).is_file())
                self.assertEqual(command[:2], ['/usr/bin/sandbox-exec', '-p'])
                self.assertEqual(command[2], '(version 1)\n(allow default)\n(deny file-read-data (literal '
                                 + json.dumps(command[-1]) + '))')
                return 400 + len(controls), 'exit=1'
            self.assertEqual(command, [launch['path'], *launch['arguments'][1:]])
            self.assertEqual(kwargs, dict(env=launch['environment'], cwd=launch['cwd']))
            return 512, 'exit=-6'

        def line(pid):
            return ('Sandbox: cat(' + str(pid) + ') deny(1) file-read-data ' + controls[0][-1] + '\n').encode()

        def show(command, **kwargs):
            calls.append(('show', command, kwargs))
            if show_error:
                raise show_error
            output = b'Timestamp Ty Process[PID:TID]\n' + extra
            if show_control:
                output += line(401)
            return SimpleNamespace(returncode=query_exit, stdout=output, stderr=b'fixture log access denied')

        def stream(command, **kwargs):
            calls.append(('stream', command, kwargs))
            if stream_error:
                raise stream_error
            process = Mock(returncode=-signal.SIGTERM)
            process.poll.return_value = None

            def terminate():
                output = stream_extra + (line(402) if stream_control else b'')
                kwargs['stdout'].write(output)
            process.terminate.side_effect = terminate
            return process

        with patch.object(smoke, 'bounded_launch', side_effect=bounded) as bounded_mock, \
                patch.object(subprocess, 'run', side_effect=show) as run, \
                patch.object(subprocess, 'Popen', side_effect=stream) as popen, \
                patch.object(smoke.time, 'sleep', side_effect=lambda seconds: calls.append(('sleep', seconds))):
            text = smoke.sandbox_denials(self.resolved['executable'], record, 'fallback timestamp')
        return text, calls, run, popen, bounded_mock

    def test_denial_capture_uses_prelaunch_timestamp_and_proves_log_show_with_a_control(self):
        text, calls, run, popen, _ = self.denial_capture(extra=b'Sandbox: Python(321) deny(1) file-read-data /outside/file\n')
        self.assertIn('Denial control: seen via log show (exit=1)', text)
        self.assertIn('file-read-data /outside/file', text)
        popen.assert_not_called()
        command = run.call_args.args[0]
        self.assertEqual(command[command.index('--start') + 1], '2026-11-10 12:34:56')
        self.assertNotIn('--last', command)
        self.assertEqual(run.call_args.kwargs['timeout'], 20)
        predicate = command[command.index('--predicate') + 1]
        for expected in ('sender == "Sandbox"', 'subsystem == "com.apple.sandbox.reporting"', 'eventMessage CONTAINS "deny("'):
            self.assertIn(expected, predicate)
        self.assertNotIn('process ==', predicate)
        self.assertEqual([call[0] for call in calls], ['launch', 'sleep', 'show'])
        self.assertEqual(calls[1], ('sleep', 2))

    def test_missing_show_control_starts_stream_before_the_control_and_confined_replay(self):
        text, calls, run, popen, bounded = self.denial_capture(show_control=False, stream_control=True,
            stream_extra=b'Sandbox: renamed(512) deny(1) file-read-xattr /root/alias\n')
        self.assertIn('seen via log stream (log show unseen; exit=1)', text)
        self.assertIn('file-read-xattr /root/alias', text)
        self.assertEqual(bounded.call_count, 3)
        popen.assert_called_once()
        self.assertEqual([call[0] for call in calls], ['launch', 'sleep', 'show', 'stream', 'sleep', 'launch', 'launch', 'sleep'])
        self.assertEqual(calls[-1], ('sleep', 2))
        self.assertEqual(run.call_args.args[0][-1], popen.call_args.args[0][-1])

    def test_unobservable_control_is_explicit_even_if_python_denials_are_present(self):
        text, _, _, _, _ = self.denial_capture(show_control=False, stream_control=False,
            extra=b'Sandbox: Python(321) deny(1) file-read-data /missing\n')
        self.assertIn('not seen via log show or log stream', text)
        self.assertIn('file-read-data /missing', text)

    def test_denied_operations_deduplicate_every_operation_and_path_and_filter_child_identity(self):
        output = ('Sandbox: Python(999) deny(1) file-read-data /first\n' * 5
            + 'Sandbox: shim(321) deny(2) file-read-xattr /first\n'
            + 'Sandbox: shim(321) deny(1) file-read-data /second\n'
            + 'Sandbox: other(3210) deny(1) file-read-data /unrelated\n'
            + '12:34:56.321 Sandbox: other(999) deny(1) file-read-data /unrelated-time\n'
            + 'Sandbox: Python(321) allowed /ignored\n').encode()
        self.assertEqual(smoke.denied_operations(output, (321,)),
            [('file-read-data', '/first'), ('file-read-xattr', '/first'), ('file-read-data', '/second')])
        text, _, _, _, _ = self.denial_capture(extra=output, stream_extra=output)
        self.assertEqual(text.count('file-read-data /first'), 1)
        self.assertIn('file-read-xattr /first', text)
        self.assertIn('file-read-data /second', text)
        self.assertNotIn('/unrelated', text)

    def test_denial_limits_preserve_control_status_and_report_distinct_omissions(self):
        output = '\n'.join(f'Sandbox: Python(321) deny(1) file-read-data /file{index}' for index in range(100)).encode()
        text, _, _, _, _ = self.denial_capture(extra=output)
        self.assertLessEqual(len(text), 900)
        self.assertIn('seen via log show', text)
        self.assertIn('distinct denials truncated/omitted', text)
        self.assertIn('file-read-data /file0', text)

    def test_log_errors_and_partial_timeout_denials_survive_a_failed_stream(self):
        for error in (OSError('fixture log denied'), subprocess.TimeoutExpired('log', 20,
                output=b'Sandbox: Python(321) deny(1) file-read-data /partial\n')):
            with self.subTest(error=error):
                text, _, _, _, _ = self.denial_capture(show_error=error, stream_error=OSError('no log stream'))
            self.assertIn('not seen via log show or log stream', text)
            self.assertIn('Log show: ' + type(error).__name__, text)
            self.assertIn('Log stream: OSError', text)
            if isinstance(error, subprocess.TimeoutExpired):
                self.assertIn('file-read-data /partial', text)

    def test_denial_query_nonzero_status_is_retained_with_its_events(self):
        text, _, _, _, _ = self.denial_capture(query_exit=71,
            extra=b'Sandbox: Python(321) deny(1) file-read-data /fixture\n')
        self.assertIn('Log query exited 71: fixture log access denied', text)
        self.assertIn('file-read-data /fixture', text)

    def test_control_and_stream_replays_have_a_two_second_process_bound(self):
        for timeout in (False, True):
            process = Mock(pid=123, returncode=1)
            process.__enter__ = Mock(return_value=process)
            process.__exit__ = Mock(return_value=False)
            process.communicate.side_effect = [subprocess.TimeoutExpired('cat', 2), (b'', b'')] if timeout else None
            with self.subTest(timeout=timeout), patch.object(subprocess, 'Popen', return_value=process):
                pid, state = smoke.bounded_launch(['/bin/cat', '/fixture'])
            self.assertEqual(pid, 123)
            self.assertEqual(state, 'timeout' if timeout else 'exit=1')
            self.assertTrue(all(call.kwargs['timeout'] == 2 for call in process.communicate.call_args_list))
            self.assertEqual(process.kill.call_count, int(timeout))

    def test_diagnostic_errors_do_not_hide_the_confined_launch_failure(self):
        for error in (OSError('fixture log denied'), subprocess.TimeoutExpired('log', 2)):
            with self.subTest(error=error), patch.object(subprocess, 'run', side_effect=error), \
                    patch.object(subprocess, 'Popen', side_effect=error), patch.object(smoke.time, 'sleep'):
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
            environment={'HOME': '/var/empty', '__PYVENV_LAUNCHER__': str(self.launcher)}, cwd='/var/empty',
            pid=321, started='2026-11-10 12:34:56', stdlib=self.resolved['stdlib'],
            home=str(self.root / 'home'), temporary=str(self.root / 'temporary'))
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
                patch.object(os, 'getpid', return_value=321), patch.object(smoke.time, 'strftime', return_value=launch['started']), \
                patch('sysconfig.get_path', return_value=launch['stdlib']), patch.object(os.path, 'expanduser', return_value=launch['home']), \
                patch.dict(os.environ, TMPDIR=launch['temporary']), \
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
                    now[0] = 121
                return result

            with self.subTest(all_work=all_work), patch.object(smoke.time, 'monotonic', side_effect=lambda: now[0]), \
                    patch.object(subprocess, 'run', side_effect=run) as execute:
                text = smoke.sandbox_bisect(record)
            self.assertIn('120 s budget', text)
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
        self.assertEqual(run.call_count, 11)

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
                now[0] = 121
            return result

        with patch.object(smoke, 'startup_paths', return_value=(('subpath', '/Library'),)), \
                patch.object(smoke, 'startup_ancestors', return_value=(('literal', '/'),)), \
                patch.object(smoke.time, 'monotonic', side_effect=lambda: now[0]), \
                patch.object(subprocess, 'run', side_effect=run) as execute:
            text = smoke.sandbox_bisect(record)
        self.assertIn('Working startup path set (unresolved: 120 s budget)', text)
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
                patch.object(smoke, 'path_children', side_effect=OSError('root unreadable')), \
                patch.object(subprocess, 'run', side_effect=self.path_probe(launch, ['/unlisted/needed'])):
            text = smoke.sandbox_bisect(record)
        self.assertIn('listed roots failed; splitting /', text)
        self.assertIn('unreadable or oversized directory', text)
        self.assertIn('(allow file-read* (subpath "/"))', text)
        self.assertNotIn('Smallest tested', text)

    def test_root_split_reduces_unlisted_roots_and_descends_with_other_paths_fixed(self):
        record, launch = self.captured_launch()
        required = ['/', '/bin/needed', '/.vol/required']
        tree = {'/': (('literal', '/'), ('subpath', '/bin'), ('subpath', '/.vol'), ('literal', '/.file'),
                      ('literal', '/.nofollow'), ('literal', '/.resolve'), ('subpath', '/sbin'),
                      ('subpath', '/cores'), ('literal', '/home'), ('subpath', '/Network'), ('subpath', '/AppleInternal')),
                '/bin': (('literal', '/bin'), ('literal', '/bin/needed'), ('literal', '/bin/unused')),
                '/.vol': (('literal', '/.vol'), ('literal', '/.vol/required'), ('literal', '/.vol/unused'))}
        with patch.object(smoke, 'startup_paths', return_value=(('subpath', '/Library'), ('subpath', '/usr'))), \
                patch.object(smoke, 'startup_ancestors', return_value=(('literal', '/'),)), \
                patch.object(smoke, 'path_children', side_effect=tree.__getitem__) as children, \
                patch.object(subprocess, 'run', side_effect=self.path_probe(launch, required)):
            text = smoke.sandbox_bisect(record)
        self.assertIn('listed roots failed; splitting /', text)
        self.assertIn('Smallest tested startup path set:', text)
        self.assertIn('cwd="/var/empty"', text)
        for path in required:
            self.assertIn(smoke.path_rule(('literal', path)), text)
        self.assertNotIn('(subpath ', text)
        self.assertEqual({call.args[0] for call in children.call_args_list}, set(tree))

    def test_early_ancestor_union_reduces_missing_parents_to_literals(self):
        record, launch = self.captured_launch()
        required = ['/opt', '/Users', '/private']
        with patch.object(smoke, 'startup_paths', return_value=(('subpath', '/Library'), ('subpath', '/usr'))), \
                patch.object(smoke, 'startup_ancestors', return_value=tuple(('literal', path) for path in ['/', *required])), \
                patch.object(smoke, 'path_children', side_effect=AssertionError('Only parent literals are needed')), \
                patch.object(subprocess, 'run', side_effect=self.path_probe(launch, required)) as run:
            text = smoke.sandbox_bisect(record)
        self.assertIn('listed roots+ancestors=started/exit=1', text)
        self.assertIn('Smallest tested startup path set:', text)
        for path in required:
            self.assertIn(smoke.path_rule(('literal', path)), text)
        self.assertNotIn('(subpath ', text)
        for operation in ('file-read-metadata', 'file-read-xattr'):
            probe = next(call.args[0][2] for call in run.call_args_list
                         if call.args[0][2].endswith('(allow ' + operation + ')') and '(subpath "/Library")' in call.args[0][2])
            self.assertIn('(subpath "/usr")', probe)
            self.assertNotIn('(literal "/opt")', probe)

    def test_early_global_xattr_probe_reports_success_and_root_search_still_isolates_the_path(self):
        record, launch = self.captured_launch()
        probe = self.path_probe(launch, ['/unlisted/required'])

        def run(command, **kwargs):
            if '(allow file-read-xattr)' in command[2]:
                return SimpleNamespace(returncode=1, stdout=smoke.STARTED + b'\n', stderr=b'')
            return probe(command, **kwargs)

        tree = {'/': (('literal', '/'), ('subpath', '/unlisted')),
                '/unlisted': (('literal', '/unlisted'), ('literal', '/unlisted/required'))}
        with patch.object(smoke, 'startup_paths', return_value=(('subpath', '/Library'),)), \
                patch.object(smoke, 'startup_ancestors', return_value=(('literal', '/'),)), \
                patch.object(smoke, 'path_children', side_effect=tree.__getitem__), \
                patch.object(subprocess, 'run', side_effect=run):
            text = smoke.sandbox_bisect(record)
        self.assertIn('roots+file-read-xattr=started/exit=1', text)
        self.assertIn('roots+file-read-metadata=exit=-6', text)
        self.assertIn('Smallest tested startup path set:\n' + smoke.path_rule(('literal', '/unlisted/required')), text)

    def test_replay_ancestors_use_the_captured_interpreter_stdlib_cwd_home_and_tmpdir(self):
        _, launch = self.captured_launch()
        expected = sandbox.ancestor_directories((launch['arguments'][3], launch['environment']['__PYVENV_LAUNCHER__']),
            (launch['stdlib'], launch['cwd'], launch['home'], launch['temporary'], launch['environment']['HOME']))
        with patch.object(Path, 'home', return_value=self.root / 'different-user'), \
                patch.dict(os.environ, TMPDIR=str(self.root / 'different-temp')):
            paths = smoke.startup_ancestors(launch)
        self.assertEqual(paths, tuple(('literal', path) for path in sorted(expected)))

    def test_root_split_includes_hidden_files_symlinks_and_resolved_targets_using_only_names_and_types(self):
        entries = Mock()
        entries.__enter__ = Mock(return_value=[
            SimpleNamespace(path='/bin', is_dir=lambda **kwargs: True, is_symlink=lambda: False),
            SimpleNamespace(path='/.vol', is_dir=lambda **kwargs: True, is_symlink=lambda: False),
            SimpleNamespace(path='/.resolve', is_dir=lambda **kwargs: False, is_symlink=lambda: False),
            SimpleNamespace(path='/etc', is_symlink=lambda: True),
            SimpleNamespace(path='/tmp', is_symlink=lambda: True),
            SimpleNamespace(path='/var', is_symlink=lambda: True),
        ])
        entries.__exit__ = Mock(return_value=False)
        targets = {'/etc': '/private/etc', '/tmp': '/private/tmp', '/var': '/private/var'}

        class Target:
            def __init__(self, value):
                self.value = value

            def resolve(self):
                return self

            def is_dir(self):
                return True

            def __str__(self):
                return targets[self.value]

        with patch.object(os, 'scandir', return_value=entries), patch.object(smoke, 'Path', Target):
            children = smoke.path_children('/')
        expected = {('literal', '/'), ('subpath', '/bin'), ('subpath', '/.vol'), ('literal', '/.resolve')}
        expected.update(('literal', alias) for alias in targets)
        expected.update(('subpath', target) for target in targets.values())
        self.assertEqual(set(children), expected)
        for alias in targets:
            self.assertNotIn(('subpath', alias), children)

    def test_shared_search_budget_and_outer_mac_smoke_timeouts_allow_the_new_diagnostic(self):
        self.assertEqual(smoke.DIAGNOSTIC_SECONDS, 120)
        root = BRIDGE.parents[1]
        self.assertIn('process.WaitForExit(240000)', (root / 'cross/Smoke.cs').read_text(encoding='utf-8'))
        for name, count in (('mac-linux.yml', 2), ('ci.yml', 1)):
            workflow = (root / '.github/workflows' / name).read_text(encoding='utf-8')
            self.assertEqual(workflow.count('timeout-minutes: 8'), count)

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
        entries.__enter__ = Mock(return_value=[SimpleNamespace(path='x', is_dir=lambda **kwargs: False, is_symlink=lambda: False)] * 2048)
        entries.__exit__ = Mock(return_value=False)
        with patch.object(os, 'scandir', return_value=entries), self.assertRaises(ValueError):
            smoke.path_children('/wide')

    def test_path_split_supports_large_homebrew_library_directories(self):
        entries = Mock()
        entries.__enter__ = Mock(return_value=[SimpleNamespace(path='/opt/homebrew/lib/library' + str(index) + '.dylib',
            is_dir=lambda **kwargs: False, is_symlink=lambda: False) for index in range(512)])
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
        denials, _, _, _, _ = self.denial_capture(query_exit=71,
            extra=b'Sandbox: Python(321) deny(1) file-read-data /fixture\n' * 200)
        with patch.object(smoke, 'sandbox_denials', return_value=denials):
            text = smoke.sandbox_failure('plan', resolved, -6, b'x' * 20000, b'y' * 20000)
        self.assertLessEqual(len(text), 3000)
        self.assertIn('returncode=-6', text)
        self.assertIn('Log query exited 71', text)
        self.assertIn('fixture log access denied', text)
        self.assertIn('file-read-data /fixture', text)

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

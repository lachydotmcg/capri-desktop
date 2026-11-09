"""Darwin signal races using fake libproc records; safe to run on any host."""
import ctypes
import errno
import importlib.util
from pathlib import Path
import struct
import sys
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch


spec = importlib.util.spec_from_file_location('mac_process', Path(__file__).resolve().parents[2] / 'cross/bridge/mac_process.py')
mac = importlib.util.module_from_spec(spec)
with patch.object(ctypes, 'CDLL', return_value=Mock()):
    spec.loader.exec_module(mac)
smoke_spec = importlib.util.spec_from_file_location('mac_smoke', Path(spec.origin).with_name('mac_smoke.py'))
smoke = importlib.util.module_from_spec(smoke_spec)
with patch.dict(sys.modules, mac_process=mac):
    smoke_spec.loader.exec_module(smoke)


class MacProcessChecks(unittest.TestCase):
    def setUp(self):
        self.records = {321: ('12:34', 5), 322: ('12:35', 5), 999: ('98:76', 2)}
        self.groups = {321: 321, 322: 321, 999: 999}
        self.selector = (2, 321)

        def inventory(kind, value, buffer, size):
            self.assertEqual((kind, value), self.selector)
            members = [pid for pid, group in self.groups.items() if kind == 1 or group == value]
            data = struct.pack('=' + 'i' * len(members), *members)
            if buffer is not None:
                ctypes.memmove(buffer, data, len(data))
                return len(data)
            return (len(self.groups) + 20) * 4

        def group(pid):
            if pid not in self.groups:
                raise ProcessLookupError(errno.ESRCH, 'fixture exited')
            return self.groups[pid]

        self.patches = [
            patch.object(mac.lib, 'proc_listpids', side_effect=inventory),
            patch.object(mac, 'signal', SimpleNamespace(SIGTERM=15, SIGCONT=19)),
            patch.object(mac, 'info', side_effect=lambda pid: self.records.get(pid)),
            patch.object(mac.os, 'getpgid', create=True, side_effect=group),
            patch.object(mac.os, 'killpg', create=True),
            patch.object(mac.os, 'kill'),
        ]
        active = []
        for item in self.patches:
            active.append(item.start())
            self.addCleanup(item.stop)
        self.killpg, self.kill = active[-2:]

    def test_eperm_on_zombie_group_is_finished(self):
        self.killpg.side_effect = PermissionError(errno.EPERM, 'fixture zombie group')
        mac.terminate_session(321)
        self.killpg.assert_called_once_with(321, mac.signal.SIGTERM)
        self.kill.assert_not_called()

    def test_eperm_on_gone_group_is_finished(self):
        self.groups = {999: 999}
        self.killpg.side_effect = PermissionError(errno.EPERM, 'fixture gone group')
        mac.terminate_session(321)

    def test_eperm_with_live_or_unknown_group_member_is_an_error(self):
        for record in (('12:35', 2), ('12:35', 4), None):
            with self.subTest(record=record):
                self.records[322] = record
                self.killpg.side_effect = PermissionError(errno.EPERM, 'fixture denied live group')
                with self.assertRaises(PermissionError):
                    mac.terminate_session(321)
        self.kill.assert_not_called()

    def test_eperm_after_member_disappears_is_finished(self):
        self.records[322] = None
        with patch.object(mac.os, 'getpgid', side_effect=ProcessLookupError(errno.ESRCH, 'fixture exit')):
            self.killpg.side_effect = PermissionError(errno.EPERM, 'fixture exit race')
            mac.terminate_session(321)

    def test_other_live_process_groups_do_not_block_zombie_cleanup(self):
        self.groups[999] = 322
        with patch.object(mac.os, 'getsid', create=True, return_value=321):
            self.killpg.side_effect = PermissionError(errno.EPERM, 'fixture zombie group')
            mac.terminate_session(321)

    def test_inventory_and_membership_failures_are_not_success(self):
        self.killpg.side_effect = PermissionError(errno.EPERM, 'fixture denied group')
        with patch.object(mac.lib, 'proc_listpids', side_effect=OSError(errno.EACCES, 'fixture inventory denied')):
            with self.assertRaises(OSError):
                mac.terminate_session(321)
        self.records[321] = None
        with patch.object(mac.os, 'getpgid', side_effect=PermissionError(errno.EPERM, 'fixture membership denied')):
            with self.assertRaises(PermissionError):
                mac.terminate_session(321)

    def test_group_success_and_esrch_need_no_inventory(self):
        with patch.object(mac, '_pids', side_effect=AssertionError('Unneeded inventory')):
            mac.terminate_session(321)
            self.killpg.side_effect = ProcessLookupError(errno.ESRCH, 'fixture gone group')
            mac.terminate_session(321)

    def test_failed_zero_length_inventory_is_not_an_empty_group(self):
        self.killpg.side_effect = PermissionError(errno.EPERM, 'fixture denied group')
        with patch.object(mac.lib, 'proc_listpids', side_effect=[120, 0]), patch.object(mac.ctypes, 'get_errno', return_value=errno.EPERM):
            with self.assertRaises(PermissionError):
                mac.terminate_session(321)

    def test_session_inventory_still_excludes_zombies_and_other_sessions(self):
        self.selector = (1, 0)
        self.records[322] = ('12:35', 2)
        with patch.object(mac.os, 'getsid', create=True, side_effect=lambda pid: 321 if pid in (321, 322) else 999):
            self.assertEqual(mac.session_members(321), [(322, '12:35')])

    def test_other_signal_errors_are_preserved(self):
        for error in (PermissionError(errno.EACCES, 'fixture denied'), OSError(errno.EINVAL, 'fixture invalid')):
            with self.subTest(error=error):
                self.killpg.side_effect = error
                with self.assertRaises(type(error)):
                    mac.terminate_session(321)

    def test_resume_handles_a_zombie_group_too(self):
        self.killpg.side_effect = PermissionError(errno.EPERM, 'fixture zombie group')
        mac.signal_group(321, mac.signal.SIGCONT)
        self.killpg.assert_called_once_with(321, mac.signal.SIGCONT)

    def test_process_signals_require_the_original_live_identity(self):
        for record in (None, ('reused', 2), ('12:34', 5)):
            with self.subTest(record=record):
                self.records[321] = record
                mac.signal_process(321, '12:34', mac.signal.SIGTERM)
        self.records[321] = ('12:34', 2)
        mac.signal_process(321, None, mac.signal.SIGTERM)
        self.kill.assert_not_called()
        mac.signal_process(321, '12:34', mac.signal.SIGTERM)
        self.kill.assert_called_once_with(321, mac.signal.SIGTERM)

    def test_process_eperm_checks_exit_and_birth_again(self):
        for record, present, denied in ((('12:34', 5), True, False), (None, False, False),
                                       (('reused', 2), True, False), (('12:34', 2), True, True), (None, True, True)):
            with self.subTest(record=record, present=present):
                self.records[321] = ('12:34', 2)
                self.groups[321] = 321

                def signal(pid, signum):
                    self.records[pid] = record
                    if not present:
                        del self.groups[pid]
                    raise PermissionError(errno.EPERM, 'fixture signal race')

                self.kill.side_effect = signal
                if denied:
                    with self.assertRaises(PermissionError):
                        mac.signal_process(321, '12:34', mac.signal.SIGTERM)
                else:
                    mac.signal_process(321, '12:34', mac.signal.SIGTERM)

    def test_fixture_cleanup_reaps_a_child_that_turns_zombie_during_signal(self):
        self.records[321] = ('12:34', 2)
        process = Mock(pid=321)
        process.poll.return_value = None

        def signal(pid, signum):
            self.records[pid] = ('12:34', 5)
            raise PermissionError(errno.EPERM, 'fixture exit race')

        self.kill.side_effect = signal
        with patch.object(smoke, 'signal', SimpleNamespace(SIGKILL=9)):
            smoke.kill_child(process)
        self.kill.assert_called_once_with(321, 9)
        process.wait.assert_called_once_with(timeout=2)

    def test_fixture_cleanup_does_not_signal_a_zombie_or_reaped_child(self):
        process = Mock(pid=321)
        process.poll.return_value = None
        with patch.object(smoke, 'signal', SimpleNamespace(SIGKILL=9)):
            smoke.kill_child(process)
        process.wait.assert_called_once_with(timeout=2)
        process.poll.return_value = 0
        smoke.kill_child(process)
        self.kill.assert_not_called()

    def test_fixture_cleanup_refuses_an_uninspectable_child(self):
        self.records[321] = None
        process = Mock(pid=321)
        process.poll.return_value = None
        with self.assertRaises(AssertionError):
            smoke.kill_child(process)
        self.kill.assert_not_called()
        process.wait.assert_not_called()

    def test_fixture_cleanup_preserves_a_live_child_permission_error(self):
        self.records[321] = ('12:34', 2)
        self.kill.side_effect = PermissionError(errno.EPERM, 'fixture live child denied')
        process = Mock(pid=321)
        process.poll.return_value = None
        with patch.object(smoke, 'signal', SimpleNamespace(SIGKILL=9)), self.assertRaises(PermissionError):
            smoke.kill_child(process)
        process.wait.assert_not_called()


if __name__ == '__main__':
    unittest.main(testRunner=unittest.TextTestRunner(stream=sys.stdout, verbosity=2))

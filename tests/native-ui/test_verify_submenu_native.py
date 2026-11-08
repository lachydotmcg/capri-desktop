#!/usr/bin/env python3
"""Synthetic regression fixtures for the offline checker; no native/GUI acceptance claims."""
import copy
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

CHECKER = Path(__file__).with_name('verify-submenu-native.py')
spec = importlib.util.spec_from_file_location('submenu_checker', CHECKER)
checker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(checker)
PID = 1234
MENU_CLASS = 'Capri.Passive.1234.' + 'a' * 32 + 'Menu'
IDENTITY = {'pid': PID, 'nativeMenuClass': MENU_CLASS}
TRACE = [
    {**IDENTITY, 'stage': 'submenu-opened', 'data': {'header': 'Size', 'host': {'xid': 100}}},
    {**IDENTITY, 'stage': 'native-attributes', 'data': {
        'parent': {'xid': 100, 'found': True, 'mapState': 2, 'overrideRedirect': False},
        'renderChild': {'xid': 101, 'found': True, 'mapState': 2}}}]
CAPTURE = {'state': {'pid': PID}, 'clients': [
    {'pid': PID, 'class': MENU_CLASS, 'mapped': True, 'pinned': True, 'floating': True},
    {'pid': PID, 'class': MENU_CLASS, 'mapped': True, 'pinned': True, 'floating': True}]}


class SubmenuEvidenceChecks(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix='capri-submenu-checks-')
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)

    def files(self, trace=None, capture=None):
        path, snapshot = self.root / 'trace.jsonl', self.root / 'capture.json'
        path.write_text(''.join(json.dumps(row) + '\n' for row in (TRACE if trace is None else trace)))
        snapshot.write_text(json.dumps(CAPTURE if capture is None else capture))
        return path, snapshot

    def verify(self, trace=None, capture=None):
        path, snapshot = self.files(trace, capture)
        return checker.verify(path, ['Size'], [snapshot])

    def test_accepts_matching_process_nonce_and_managed_hosts(self):
        result = self.verify()
        self.assertEqual(result['status'], 'PASS_NATIVE_METADATA')
        self.assertEqual(result['pid'], PID)
        self.assertEqual(result['checks'][-1]['mapped_menus'], 2)

    def test_rejects_capture_from_another_process(self):
        capture = copy.deepcopy(CAPTURE); capture['state']['pid'] += 1
        with self.assertRaisesRegex(ValueError, 'capture process'):
            self.verify(capture=capture)

    def test_rejects_unrelated_menu_clients_that_used_to_pass(self):
        capture = copy.deepcopy(CAPTURE)
        for index, client in enumerate(capture['clients']):
            client.update(pid=99990 + index, **{'class': 'UnrelatedMenu'})
        with self.assertRaisesRegex(ValueError, 'exact trace process/menu class'):
            self.verify(capture=capture)

    def test_rejects_another_nonce_in_the_same_process(self):
        capture = copy.deepcopy(CAPTURE)
        for client in capture['clients']:
            client['class'] = MENU_CLASS.replace('a' * 32, 'b' * 32)
        with self.assertRaisesRegex(ValueError, 'exact trace process/menu class'):
            self.verify(capture=capture)

    def test_rejects_missing_identity_on_any_trace_row(self):
        for key in ['pid', 'nativeMenuClass']:
            with self.subTest(key=key):
                trace = copy.deepcopy(TRACE); del trace[1][key]
                with self.assertRaises(ValueError):
                    self.verify(trace=trace)

    def test_rejects_changed_trace_identity(self):
        trace = copy.deepcopy(TRACE); trace[1]['nativeMenuClass'] = MENU_CLASS.replace('a' * 32, 'b' * 32)
        with self.assertRaisesRegex(ValueError, 'identity changed'):
            self.verify(trace=trace)

    def test_rejects_nonpositive_or_boolean_trace_pid(self):
        for pid in [0, -1, True]:
            with self.subTest(pid=pid):
                trace = copy.deepcopy(TRACE); trace[0]['pid'] = pid
                with self.assertRaisesRegex(ValueError, 'process identity'):
                    self.verify(trace=trace)

    def test_rejects_class_for_a_different_pid(self):
        trace = copy.deepcopy(TRACE); trace[0]['nativeMenuClass'] = MENU_CLASS.replace('1234', '1235')
        with self.assertRaisesRegex(ValueError, 'process-specific menu class'):
            self.verify(trace=trace)

    def test_rejects_missing_capture_identity(self):
        capture = copy.deepcopy(CAPTURE); capture['state'] = None
        with self.assertRaisesRegex(ValueError, 'capture process'):
            self.verify(capture=capture)

    def test_rejects_unpinned_or_nonfloating_own_hosts(self):
        for property_name in ['pinned', 'floating']:
            with self.subTest(property_name=property_name):
                capture = copy.deepcopy(CAPTURE); capture['clients'][1][property_name] = False
                with self.assertRaisesRegex(ValueError, 'popup rules'):
                    self.verify(capture=capture)

    def test_rejects_override_redirect_submenu(self):
        trace = copy.deepcopy(TRACE); trace[1]['data']['parent']['overrideRedirect'] = True
        with self.assertRaisesRegex(ValueError, 'managed mapped submenu'):
            self.verify(trace=trace)

    def test_rejects_unmapped_parent_or_render_child(self):
        for target in ['parent', 'renderChild']:
            with self.subTest(target=target):
                trace = copy.deepcopy(TRACE); trace[1]['data'][target]['mapState'] = 0
                with self.assertRaises(ValueError):
                    self.verify(trace=trace)

    def test_rejects_missing_required_open(self):
        trace = copy.deepcopy(TRACE); trace[0]['data']['header'] = 'Help'
        with self.assertRaisesRegex(ValueError, 'Missing actual submenu opens'):
            self.verify(trace=trace)

    def test_cli_reports_success_and_rejects_bad_capture(self):
        path, capture = self.files()
        command = [sys.executable, str(CHECKER), str(path), '--header', 'Size', '--capture', str(capture)]
        result = subprocess.run(command, capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(json.loads(result.stdout)['pid'], PID)
        capture.write_text(json.dumps({'state': {'pid': PID + 1}, 'clients': CAPTURE['clients']}))
        result = subprocess.run(command, capture_output=True, text=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('capture process', result.stderr)


if __name__ == '__main__':
    unittest.main()

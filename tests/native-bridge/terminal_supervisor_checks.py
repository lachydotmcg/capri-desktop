"""Real isolated PTY lifecycle regressions; no accounts, desktop, network or owner processes."""
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import time
import unittest

WORKER = Path(__file__).resolve().parents[2] / 'cross/bridge/terminal_job.py'


def live(pid):
    try:
        return Path(f'/proc/{pid}/stat').read_text().rsplit(')', 1)[1].split()[0] != 'Z'
    except FileNotFoundError:
        return False


class SupervisorTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='capri-terminal-test-')
        self.root = Path(self.temporary.name)
        self.processes = []
        self.children = set()

    def launch(self, command, **overrides):
        folder = self.root / str(len(self.processes))
        folder.mkdir(mode=0o700)
        request = dict(job_id=folder.name, command=command, cwd=str(folder), shell='/bin/bash', visible=False,
                       owner_pid=os.getpid(), owner_start=Path('/proc/self/stat').read_text().rsplit(')', 1)[1].split()[19], timeout=8)
        request.update(overrides)
        (folder/'request.json').write_text(json.dumps(request))
        process = subprocess.Popen([sys.executable, str(WORKER), str(folder)], stdin=subprocess.DEVNULL,
                                   stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
        self.processes.append((folder, process))
        return folder, process

    def state(self, folder):
        try:
            return json.loads((folder/'status.json').read_text())
        except FileNotFoundError:
            return {}

    def until(self, predicate, timeout=5):
        end = time.monotonic()+timeout
        while time.monotonic()<end:
            value = predicate()
            if value:
                return value
            time.sleep(.02)
        self.fail('Bounded observation timed out')

    def finish(self, folder, process):
        process.wait(timeout=5)
        self.assertEqual(process.returncode, 0, process.stderr.read().decode())
        return self.state(folder)

    def tearDown(self):
        # Explicit force cleanup is only for these disposable test-owned TERM-ignore fixtures.
        for folder, process in self.processes:
            (folder/'stop').write_text('stop')
            state = self.state(folder)
            if process.poll() is None and state.get('pid'):
                try:
                    os.killpg(state['pid'], signal.SIGKILL)
                except ProcessLookupError:
                    pass
            try:
                process.wait(timeout=3)
            except subprocess.TimeoutExpired:
                process.kill(); process.wait()
            process.stderr.close()
        for pid in self.children:
            if live(pid):
                os.kill(pid, signal.SIGKILL)
        self.temporary.cleanup()

    def test_crlf_and_ansi_have_readable_lines(self):
        folder, process = self.launch("printf '\\033[31ma\\033[0m\\r\\nb\\r\\n'")
        self.assertEqual(self.finish(folder, process)['output'], 'a\nb\n')

    def test_large_unicode_output_is_bounded_without_broken_scalars(self):
        folder, process = self.launch("python3 -c 'print(\"雪\"*200000,end=\"XX\")'")
        state = self.finish(folder, process)
        self.assertEqual(state['total_bytes'], 600002)
        self.assertTrue(state['truncated'])
        self.assertLessEqual((folder/'transcript.bin').stat().st_size, 262144)
        self.assertNotIn('\ufffd', state['output'])

    def test_stop_covers_another_process_group_in_owned_session(self):
        command = "python3 -c 'import os,time; p=os.fork();\nif p==0: os.setpgrp();open(\"child.pid\",\"w\").write(str(os.getpid()));time.sleep(30);os._exit(0)\nelse: os.waitpid(p,0)'"
        folder, process = self.launch(command)
        self.until(lambda: (folder/'child.pid').exists())
        child = int((folder/'child.pid').read_text()); self.children.add(child)
        (folder/'stop').write_text('stop')
        self.assertEqual(self.finish(folder, process)['state'], 'stopped')
        self.assertFalse(live(child))

    def test_eof_before_shell_exit_still_cleans_redirected_children(self):
        command = "trap '' HUP; sleep 30 >/dev/null 2>&1 & echo $! > child.pid; exec </dev/null >/dev/null 2>&1; sleep 0.3; exit 0"
        folder, process = self.launch(command)
        self.until(lambda: (folder/'child.pid').exists())
        child = int((folder/'child.pid').read_text()); self.children.add(child)
        state = self.finish(folder, process)
        self.assertEqual(state['exit_code'], 0)
        self.assertFalse(live(child))

    def test_term_ignored_is_stopping_not_fake_success_or_force_kill(self):
        folder, process = self.launch("trap '' TERM; echo READY; while :; do sleep 1; done")
        self.until(lambda: 'READY' in self.state(folder).get('output', ''))
        (folder/'stop').write_text('stop')
        self.until(lambda: self.state(folder).get('state') == 'stopping')
        self.assertIsNone(self.state(folder)['exit_code'])
        self.assertIsNone(process.poll())

    def test_missing_command_reports_actual_exit(self):
        folder, process = self.launch('capri_nonexistent_executable_7391')
        self.assertEqual(self.finish(folder, process)['exit_code'], 127)


if __name__ == '__main__':
    unittest.main()

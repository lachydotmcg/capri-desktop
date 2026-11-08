"""Owned native terminal supervisor. No network, elevation or shell launcher interpolation."""
import errno
import json
import os
from pathlib import Path
import pty
import re
import select
import signal
import sys
import termios
import time
import tty

CAP = 262144
ANSI = re.compile(r'\x1b\][^\x07\x1b]*(?:\x07|\x1b\\)|\x1b\[[0-?]*[ -/]*[@-~]|\x1b[@-_]')


def identity(pid):
    try:
        return Path(f'/proc/{pid}/stat').read_text().rsplit(')', 1)[1].split()[19]
    except (OSError, IndexError):
        return None


def session_members(session):
    """Bind processes in this still-owned terminal session; unrelated terminals are excluded."""
    members = []
    for entry in Path('/proc').iterdir():
        if not entry.name.isdigit():
            continue
        try:
            fields = (entry / 'stat').read_text().rsplit(')', 1)[1].split()
            if int(fields[3]) == session and fields[0] != 'Z':
                members.append((int(entry.name), fields[19]))
        except (OSError, ValueError, IndexError):
            continue
    return members


def terminate_session(session):
    # Interactive shells create several process groups in one session. PID descriptors and
    # start-time revalidation prevent a reused PID from receiving an old job's cancellation.
    for pid, start in session_members(session):
        try:
            fd = os.pidfd_open(pid)
            try:
                if identity(pid) == start:
                    signal.pidfd_send_signal(fd, signal.SIGTERM)
            finally:
                os.close(fd)
        except ProcessLookupError:
            pass


def clean(data):
    # A bounded raw tail may begin inside a UTF-8 scalar. Skip only leading continuation bytes.
    start = 0
    while start < len(data) and data[start] & 0xc0 == 0x80:
        start += 1
    text = ANSI.sub('', data[start:].decode('utf-8', errors='replace'))
    text = re.sub(r'\r+\n', '\n', text).replace('\r', '\n')
    return ''.join(c for c in text if c in '\n\t' or ord(c) >= 32 and ord(c) != 127)


def supervise(directory):
    directory = Path(directory)
    request = json.loads((directory / 'request.json').read_text())
    visible = request['visible']
    original = termios.tcgetattr(0) if visible and os.isatty(0) else None
    child = None
    master = None
    output = bytearray()
    total = 0
    stopped = False
    exit_code = None
    last_write = 0
    started = time.monotonic()
    reason = None

    def save(state, error=None):
        payload = dict(job_id=request['job_id'], state=state, cwd=request['cwd'], exit_code=exit_code,
                       pid=child, output=clean(output), total_bytes=total, retained_bytes=len(output),
                       truncated=total > len(output), error=error, stop_reason=reason,
                       interactive=bool(original), updated=time.time())
        temporary = directory / 'status.tmp'
        temporary.write_text(json.dumps(payload, ensure_ascii=False))
        temporary.replace(directory / 'status.json')
        (directory / 'transcript.bin').write_bytes(output)

    def stop(signum=None, frame=None):
        nonlocal stopped, reason
        if not stopped and child:
            stopped = True
            reason = reason or 'requested'
            # The child is deliberately not reaped until cleanup: its PID/group cannot be reused.
            terminate_session(child)

    try:
        os.umask(0o077)
        signal.signal(signal.SIGTERM, stop)
        signal.signal(signal.SIGHUP, stop)
        signal.signal(signal.SIGINT, stop)
        child, master = pty.fork()
        if child == 0:
            os.chdir(request['cwd'])
            os.execv(request['shell'], [request['shell'], '-c', request['command']])
        if original:
            # The child PTY owns /dev/tty and its foreground process group. Relay native keys,
            # including Ctrl-C and authentication input, without recording stdin.
            termios.tcsetwinsize(master, termios.tcgetwinsize(0))
            tty.setraw(0)
        os.set_blocking(master, False)
        save('running')
        eof = False
        input_open = bool(original)
        while True:
            now = time.monotonic()
            owner_alive = identity(request['owner_pid']) == request['owner_start']
            if not owner_alive or (directory / 'stop').exists() or now - started > request.get('timeout', 3600):
                reason = 'owner exited' if not owner_alive else 'timeout' if now - started > request.get('timeout', 3600) else 'requested'
                stop()
            done = os.waitid(os.P_PID, child, os.WEXITED | os.WNOHANG | os.WNOWAIT)
            if done is not None:
                exit_code = done.si_status if done.si_code == os.CLD_EXITED else 128 + done.si_status
                # EOF may precede shell exit when it redirects its own descriptors.
                # Session cleanup must still include detached-from-PTY job-control groups.
                terminate_session(child)
            readers = ([] if eof else [master]) + ([0] if input_open else [])
            ready, _, _ = select.select(readers, [], [], .05)
            if 0 in ready:
                data = os.read(0, 4096)
                if data and not eof:
                    try:
                        os.write(master, data)
                    except OSError:
                        input_open = False
                else:
                    input_open = False
            if master in ready:
                try:
                    data = os.read(master, 32768)
                except OSError as error:
                    if error.errno != errno.EIO:
                        raise
                    data = b''
                if data:
                    total += len(data)
                    output.extend(data)
                    del output[:-CAP]
                    if visible:
                        try:
                            os.write(1, data)
                        except OSError:
                            stop()
                else:
                    eof = True
            if now - last_write > .15:
                if stopped:
                    terminate_session(child)
                save('stopping' if stopped else 'running')
                last_write = now
            if done is not None and eof and not session_members(child):
                os.waitpid(child, 0)
                save('stopped' if stopped else 'exited')
                break
    except BaseException as error:
        stop()
        save('failed', type(error).__name__ + ': ' + str(error))
        return 1
    finally:
        if original:
            termios.tcsetattr(0, termios.TCSADRAIN, original)
        if master is not None:
            os.close(master)
        if (directory / 'retire').exists() or identity(request['owner_pid']) != request['owner_start']:
            for name in ('request.json', 'status.json', 'status.tmp', 'transcript.bin', 'stop', 'retire'):
                (directory / name).unlink(missing_ok=True)
            directory.rmdir()
            try:
                directory.parent.rmdir()
            except OSError as error:
                if error.errno != errno.ENOTEMPTY:
                    raise
    return 0


if __name__ == '__main__':
    sys.exit(supervise(sys.argv[1]))

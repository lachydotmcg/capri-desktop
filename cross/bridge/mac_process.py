"""Darwin process identities and terminal-session supervision."""
import ctypes
import errno
import os
import signal
import struct
from types import SimpleNamespace


lib = ctypes.CDLL('/usr/lib/libproc.dylib', use_errno=True)
lib.proc_pidinfo.argtypes = [ctypes.c_int, ctypes.c_int, ctypes.c_uint64, ctypes.c_void_p, ctypes.c_int]
lib.proc_pidinfo.restype = ctypes.c_int
lib.proc_listpids.argtypes = [ctypes.c_uint32, ctypes.c_uint32, ctypes.c_void_p, ctypes.c_int]
lib.proc_listpids.restype = ctypes.c_int
system = ctypes.CDLL('/usr/lib/libSystem.B.dylib', use_errno=True)
system.waitid.argtypes = [ctypes.c_int, ctypes.c_uint32, ctypes.c_void_p, ctypes.c_int]
system.waitid.restype = ctypes.c_int


def info(pid):
    data = ctypes.create_string_buffer(136)
    if lib.proc_pidinfo(pid, 3, 0, data, len(data)) != len(data):
        return None
    seconds, micros = struct.unpack_from('=QQ', data, 120)
    status = struct.unpack_from('=I', data, 4)[0]
    return f'{seconds}:{micros}', status


def identity(pid):
    value = info(pid)
    return value[0] if value is not None and value[1] != 5 else None


def _pids(group=None):
    kind, value = (1, 0) if group is None else (2, group)
    size = lib.proc_listpids(kind, value, None, 0)
    if size <= 0:
        raise OSError(ctypes.get_errno(), 'Cannot enumerate terminal processes')
    for _ in range(4):
        buffer = ctypes.create_string_buffer(size + 4096)
        ctypes.set_errno(0)
        used = lib.proc_listpids(kind, value, buffer, len(buffer))
        if used < 0 or used == 0 and ctypes.get_errno():
            raise OSError(ctypes.get_errno(), 'Cannot enumerate terminal processes')
        if used < len(buffer):
            break
        size = len(buffer) * 2
    else:
        raise OSError('Terminal process inventory kept growing')
    return [pid for (pid,) in struct.iter_unpack('=i', buffer.raw[:used]) if pid > 0]


def session_members(session):
    members = []
    for pid in _pids():
        start = identity(pid)
        try:
            if start is not None and os.getsid(pid) == session:
                members.append((pid, start))
        except ProcessLookupError:
            pass
    return members


def _exited(pid, start=None):
    value = info(pid)
    if value is not None:
        return value[1] == 5 or start is not None and value[0] != start
    try:
        os.getpgid(pid)
    except ProcessLookupError:
        return True
    return False


def _group_exited(group):
    return all(_exited(pid) for pid in _pids(group))


def signal_group(group, signum):
    # The caller keeps the session leader unreaped so this group ID cannot be reused.
    try:
        os.killpg(group, signum)
    except ProcessLookupError:
        pass
    except PermissionError as error:
        # Darwin may report EPERM for a group containing only unreaped zombies.
        if error.errno != errno.EPERM or not _group_exited(group):
            raise


def terminate_session(session):
    signal_group(session, signal.SIGTERM)


def signal_process(pid, start, signum):
    value = info(pid)
    if start is None or value is None or value[0] != start or value[1] == 5:
        return
    try:
        os.kill(pid, signum)
    except ProcessLookupError:
        pass
    except PermissionError as error:
        if error.errno != errno.EPERM or not _exited(pid, start):
            raise


def wait_child(pid):
    # Darwin's waitid leaves the leader waitable until every owned session member exits.
    data = ctypes.create_string_buffer(104)
    if system.waitid(1, pid, data, 0x01 | 0x04 | 0x20) < 0:
        code = ctypes.get_errno()
        if code == errno.EINTR:
            return None
        raise OSError(code, 'Cannot observe terminal child')
    _, _, kind, child, _, status = struct.unpack_from('=6i', data)
    return SimpleNamespace(si_status=status, si_code=kind) if child and kind in (1, 2, 3) else None

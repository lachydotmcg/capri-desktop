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


def session_members(session):
    size = lib.proc_listpids(1, 0, None, 0)
    if size <= 0:
        raise OSError(ctypes.get_errno(), 'Cannot enumerate terminal processes')
    for _ in range(4):
        buffer = ctypes.create_string_buffer(size + 4096)
        used = lib.proc_listpids(1, 0, buffer, len(buffer))
        if used <= 0:
            raise OSError(ctypes.get_errno(), 'Cannot enumerate terminal processes')
        if used < len(buffer):
            break
        size = len(buffer) * 2
    else:
        raise OSError('Terminal process inventory kept growing')
    members = []
    for (pid,) in struct.iter_unpack('=i', buffer.raw[:used]):
        if pid <= 0:
            continue
        start = identity(pid)
        try:
            if start is not None and os.getsid(pid) == session:
                members.append((pid, start))
        except ProcessLookupError:
            pass
    return members


def terminate_session(session):
    # The unreaped session leader reserves this group ID; descendant PIDs can be reused.
    try:
        os.killpg(session, signal.SIGTERM)
    except ProcessLookupError:
        pass


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

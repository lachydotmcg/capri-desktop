"""Bind a Darwin worker's lifetime to the process that launched it."""
import os
import select
import threading


def bind_parent():
    parent = os.getppid()
    if parent <= 1:
        raise SystemExit(1)
    queue = select.kqueue()
    try:
        queue.control([select.kevent(parent, filter=select.KQ_FILTER_PROC,
            flags=select.KQ_EV_ADD | select.KQ_EV_ONESHOT, fflags=select.KQ_NOTE_EXIT)], 0, 0)
        if os.getppid() != parent:
            raise SystemExit(1)
    except BaseException:
        queue.close()
        raise

    def watch():
        try:
            queue.control(None, 1)
        finally:
            os._exit(1)

    threading.Thread(target=watch, name='capri-parent', daemon=True).start()

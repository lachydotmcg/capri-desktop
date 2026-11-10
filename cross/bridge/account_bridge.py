"""Single credential writer for an explicitly linked native-only account store."""
import asyncio
import ctypes
import fcntl
import json
import os
from pathlib import Path
import sys
from plan.provider import PlanProvider
from plan.broker import Denied


def emit(value):
    print(json.dumps(value, ensure_ascii=True), flush=True)


class NativeAccount(PlanProvider):
    async def open_browser(self, url):
        # Parent opens this only after validating the official auth origin. Never log it.
        emit({'event': 'browser', 'url': url})
        answer = await asyncio.wait_for(asyncio.to_thread(sys.stdin.buffer.readline, 1024), 50)
        if answer != b'{"opened":true}\n':
            raise Denied('The browser could not open the official sign-in page.')


async def main():
    if sys.platform == 'darwin':
        from parent_watch import bind_parent
        bind_parent()
    else:
        ctypes.CDLL(None).prctl(1, 15, 0, 0, 0)
    os.umask(0o077)
    raw = sys.stdin.buffer.readline(16385)
    if len(raw) > 16384: raise Denied('Account request too large')
    request = json.loads(raw)
    if set(request) != {'directory', 'operation', 'model'} or request['operation'] not in ('connect', 'refresh'):
        raise Denied('Invalid account operation')
    directory = Path(request['directory'])
    if directory.is_symlink() or not (directory/'native-owner-v1').is_file():
        raise Denied('This account belongs to a different credential owner')
    descriptor = os.open(directory/'writer.lock', os.O_RDWR|os.O_CREAT|os.O_NOFOLLOW, 0o600)
    with os.fdopen(descriptor,'w') as lock:
        fcntl.flock(lock,fcntl.LOCK_EX|fcntl.LOCK_NB)
        account = NativeAccount(directory)
        async def notify(message): emit({'event':'status','message':message})
        try:
            try:
                if not account.record: raise Denied('No linked account')
                await account.access_token()
            except Denied:
                if request['operation'] != 'connect': raise
                await account.sign_in(notify)
            models = await account.models()
            model = request['model']
            if model and any(row['id']==model for row in models): await account.choose_model(model)
            emit({'event':'done','models':[row['id'] for row in models], 'model':account.model})
        finally:
            if account.http: await account.http.aclose()
    return 0


if __name__ == '__main__':
    try: raise SystemExit(asyncio.run(main()))
    except Exception:
        emit({'event':'error','message':'ChatGPT could not connect. Check the browser sign-in and try ChatGPT plan again. Your other Capri account was not changed.'})
        raise SystemExit(1)

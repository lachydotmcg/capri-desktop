"""Read-only adapter to the owner's retained PlanProvider account.

The original credential component alone owns login, refresh and logout. This
process accepts one bounded inference, streams text, and never executes tools.
"""
import asyncio
import ctypes
import json
import os
from pathlib import Path
import sys
import time
from types import SimpleNamespace

from plan.provider import PlanProvider, private_json, API
from plan.broker import Denied

MAX_REQUEST = 24 * 1024 * 1024


class Account(PlanProvider):
    def __init__(self, directory, expected):
        self.directory = Path(directory)
        self.lock = asyncio.Lock()
        self.http = None
        self.listener = self.pending = None
        self.catalog = []
        self.calls = 0
        self.host = private_json(self.directory / 'host.json')['id']
        self.expected = expected
        self.reload()
        self.model = self.record.get('selected_model')

    def reload(self):
        account = private_json(self.directory / 'active.json')['account']
        if account != self.expected or len(account) != 64 or any(c not in '0123456789abcdef' for c in account):
            raise Denied('The connected account changed; restart this request.')
        self.record = private_json(self.directory / (account + '.json'))
        if self.partition != account:
            raise Denied('Account identity mismatch.')

    def current(self):
        if private_json(self.directory / 'active.json')['account'] != self.expected:
            raise Denied('The connected account changed; the response was cancelled.')

    async def access_token(self, min_validity=90):
        self.reload()
        if self.record['expires_at'] < time.time() + min_validity:
            raise Denied('The shared ChatGPT session needs renewal in its owning Capri account. No credentials were changed.')
        if 'chatgpt.tokens.use.direct' not in self.record['scopes']:
            raise Denied('ChatGPT plan permission is unavailable.')
        return self.record['access_token']

    async def models(self):
        token = await self.access_token()
        value = await self.request('GET', API + '/models', headers={'Authorization': 'Bearer ' + token})
        self.catalog = [{'id': row['slug'], 'name': row['display_name']} for row in value.get('models', []) if row.get('visibility') == 'list']
        if not self.catalog:
            raise Denied('The account returned no available models.')
        return self.catalog


def emit(value):
    print(json.dumps(value, ensure_ascii=True, separators=(',', ':')), flush=True)


async def main():
    parent = os.getppid()
    ctypes.CDLL(None).prctl(1, 15, 0, 0, 0)
    if os.getppid() != parent:
        return 1
    raw = sys.stdin.buffer.readline(MAX_REQUEST + 1)
    if len(raw) > MAX_REQUEST:
        raise Denied('Inference request exceeded its limit.')
    request = json.loads(raw)
    if set(request) != {'account_directory', 'account', 'model', 'effort', 'limit', 'messages', 'instructions', 'format'}:
        raise Denied('Invalid inference request fields.')
    model = request['model']
    if not isinstance(model, str) or not 1 <= len(model) <= 128 or not isinstance(request['messages'], list) or len(request['messages']) > 256:
        raise Denied('Invalid model or conversation bounds.')
    effort = request['effort']
    if effort not in (None, 'none', 'low', 'medium', 'high', 'xhigh'):
        raise Denied('Invalid reasoning effort.')
    fields = {'max_output_tokens': max(32, min(8192, int(request['limit'])))}
    if effort is not None:
        fields['reasoning'] = {'effort': effort}
    if request['format'] not in (None, 'computer_action'):
        raise Denied('Invalid structured response format')
    if request['format'] == 'computer_action':
        fields['text'] = {'format': json.loads(Path(__file__).with_name('computer-action-format.json').read_text())}
    account = Account(request['account_directory'], request['account'])
    binding = SimpleNamespace(provider='chatgpt_plan', partition=account.partition, model=model, fields=lambda: fields)
    async def delta(text):
        emit({'event': 'delta', 'text': text})
    try:
        text = await account.respond(request['instructions'], request['messages'], delta, authorize=account.current, binding=binding)
        emit({'event': 'done', 'text': text})
    finally:
        if account.http:
            await account.http.aclose()
    return 0


if __name__ == '__main__':
    try:
        raise SystemExit(asyncio.run(main()))
    except (Denied, ValueError, KeyError, OSError):
        emit({'event': 'error', 'message': 'The retained ChatGPT account is unavailable or its shared session needs renewal. No fallback or credential change was made.'})
        raise SystemExit(1)

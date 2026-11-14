// A stand-in for her brain's providers, for phone-only mode's page test (site/remote/native.js). It answers as Anthropic
// under /anthropic and as OpenRouter (OpenAI's chat format) under /openrouter, with CORS like the real ones, and keeps
// every request it got. /google answers as Google AI's OpenAI-style endpoint. /free stands in for the worker's free phone
// brain. state.fail makes the next chat calls answer with that status, body and headers, the way a real service refuses.
// No real key or provider is ever involved.

import { createServer } from 'node:http';

export const GOOD = { anthropic: 'sk-ant-test-good-0000000000000000', openrouter: 'sk-or-test-good-0000000000000000', google: 'AIzaTestGoodKey000000000000000000000' };
export const BAD = 'sk-ant-test-bad-00000000000000000';

const cors = (request, response) => {
  response.setHeader('access-control-allow-origin', request.headers.origin || '*');
  response.setHeader('access-control-allow-methods', 'GET, POST, OPTIONS');
  response.setHeader('access-control-allow-headers', request.headers['access-control-request-headers'] || 'content-type');
  response.setHeader('access-control-max-age', '600');
};

const json = (response, status, body, headers = {}) => {
  response.writeHead(status, { 'content-type': 'application/json', ...headers });
  response.end(JSON.stringify(body));
};

const lastUser = body => {
  const turns = Array.isArray(body && body.messages) ? body.messages.filter(turn => turn.role === 'user') : [];
  const content = turns.length ? turns[turns.length - 1].content : '';
  return Array.isArray(content) ? content.filter(block => block.type === 'text').map(block => block.text).join(' ') : String(content);
};

// Her reply in a few parts, a little apart, so the page has to put a stream together.
const stream = async (response, parts, frame) => {
  response.writeHead(200, { 'content-type': 'text/event-stream', 'cache-control': 'no-cache' });
  for (const line of frame.start) response.write(line);
  for (const part of parts) {
    await new Promise(resolve => setTimeout(resolve, 40));
    response.write(frame.part(part));
  }
  for (const line of frame.end) response.write(line);
  response.end();
};

const anthropicFrame = {
  start: ['event: message_start\ndata: {"type":"message_start","message":{"id":"msg_test","type":"message","role":"assistant","content":[]}}\n\n',
    'event: content_block_start\ndata: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}\n\n'],
  part: text => 'event: content_block_delta\ndata: ' + JSON.stringify({ type: 'content_block_delta', index: 0, delta: { type: 'text_delta', text } }) + '\n\n',
  end: ['event: content_block_stop\ndata: {"type":"content_block_stop","index":0}\n\n',
    'event: message_delta\ndata: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":9}}\n\n',
    'event: message_stop\ndata: {"type":"message_stop"}\n\n']
};

const chatFrame = {
  start: [],
  part: text => 'data: ' + JSON.stringify({ id: 'chatcmpl-test', object: 'chat.completion.chunk', choices: [{ index: 0, delta: { content: text } }] }) + '\n\n',
  end: ['data: ' + JSON.stringify({ id: 'chatcmpl-test', object: 'chat.completion.chunk', choices: [{ index: 0, delta: {}, finish_reason: 'stop' }] }) + '\n\n', 'data: [DONE]\n\n']
};

export const startProvider = port =>
  new Promise((resolve, reject) => {
    const got = [];
    const state = { count: 0, free: { left: 15, each: 15, resets: new Date((Math.floor(Date.now() / 86400000) + 1) * 86400000).toISOString(), open: true, wait: 0 } };
    const server = createServer((request, response) => {
      cors(request, response);
      if (request.method === 'OPTIONS') { response.writeHead(204); response.end(); return; }
      let raw = '';
      request.on('data', chunk => { raw += chunk; });
      request.on('end', async () => {
        let body = null;
        try { body = raw ? JSON.parse(raw) : null; } catch { body = raw; }
        const url = new URL(request.url, 'http://127.0.0.1');
        got.push({ method: request.method, path: url.pathname + url.search, headers: request.headers, body });
        const heard = lastUser(body);
        // The worker's daily phone count (?count= on the page): 'drop' hangs up, a number answers with that status.
        if (url.pathname === '/count/phone-day') {
          if (state.count === 'drop') { request.socket.destroy(); return; }
          return json(response, state.count || 200, state.count ? { error: 'down' } : { ok: true });
        }
        // The free allowance has no key, cookie or page id. A short wait lets the page test see her typing.
        if (url.pathname === '/free') {
          const { left, each, resets, open } = state.free;
          if (request.method === 'GET') return json(response, 200, { left, each, resets, open });
          if (request.method === 'POST') {
            if (!left) return json(response, 429, { error: 'used', left, each, resets });
            if (!open) return json(response, 429, { error: 'pool', left, each, resets });
            state.free.left--;
            if (state.free.wait) await new Promise(done => setTimeout(done, state.free.wait));
            return json(response, 200, { reply: 'Free says: ' + heard, left: state.free.left, each, resets });
          }
        }
        // A refusal set by the test, for any chat call.
        if (state.fail && /\/(messages|chat\/completions)$/.test(url.pathname) && request.method === 'POST') return json(response, state.fail.status, state.fail.body, state.fail.headers);
        if (url.pathname.startsWith('/google/')) {
          if (request.headers.authorization !== 'Bearer ' + GOOD.google) return json(response, 401, [{ error: { code: 401, message: 'API key not valid.', status: 'UNAUTHENTICATED' } }]);
          if (url.pathname === '/google/models') return json(response, 200, { object: 'list', data: [
            { id: 'models/gemini-3.8-flash' }, { id: 'models/gemini-3.5-flash-lite' }, { id: 'models/gemini-3.6-flash-lite-preview' },
            { id: 'models/gemini-2.5-flash-lite' }, { id: 'models/gemini-3.1-pro-preview' }, { id: 'models/gemini-embedding-2' }
          ] });
          if (url.pathname === '/google/chat/completions' && request.method === 'POST') return stream(response, ['Gemini', ' says: ', heard], chatFrame);
        }
        if (url.pathname.startsWith('/anthropic/')) {
          // The real API turns a browser away without this header.
          if (request.headers['anthropic-dangerous-direct-browser-access'] !== 'true') return json(response, 401, { type: 'error', error: { type: 'authentication_error', message: 'CORS requests must set the anthropic-dangerous-direct-browser-access header' } });
          if (request.headers['x-api-key'] !== GOOD.anthropic) return json(response, 401, { type: 'error', error: { type: 'authentication_error', message: 'invalid x-api-key' } });
          if (url.pathname === '/anthropic/models') return json(response, 200, { data: [{ id: 'claude-sonnet-5-5', type: 'model' }, { id: 'claude-haiku-5-5', type: 'model' }], has_more: false });
          if (url.pathname === '/anthropic/messages' && request.method === 'POST') return stream(response, ['Got it', ': ', heard], anthropicFrame);
        }
        if (url.pathname.startsWith('/openrouter/')) {
          if (request.headers.authorization !== 'Bearer ' + GOOD.openrouter) return json(response, 401, { error: { message: 'No auth credentials found', code: 401 } });
          if (url.pathname === '/openrouter/models') return json(response, 200, { data: [
            { id: 'paid/big-model', architecture: { input_modalities: ['text', 'image'], output_modalities: ['text'] }, supported_parameters: ['tools'] },
            { id: 'free/text-only:free', architecture: { input_modalities: ['text'], output_modalities: ['text'] }, supported_parameters: [] },
            { id: 'free/sees-and-acts:free', architecture: { input_modalities: ['text', 'image'], output_modalities: ['text'] }, supported_parameters: ['tools'] },
            { id: 'image/maker', architecture: { input_modalities: ['text'], output_modalities: ['image'] }, supported_parameters: [] }
          ] });
          if (url.pathname === '/openrouter/chat/completions' && request.method === 'POST') {
            if (/make it fail/.test(heard)) return json(response, 429, { error: { message: 'Rate limit exceeded', code: 429 } });
            return stream(response, ['Router', ' says: ', heard], chatFrame);
          }
        }
        json(response, 404, { error: { message: 'not here' } });
      });
    });
    server.on('error', reject);
    server.listen(port, '127.0.0.1', () => resolve({ got, state, close: () => new Promise(done => { server.close(done); server.closeAllConnections(); }) }));
  });

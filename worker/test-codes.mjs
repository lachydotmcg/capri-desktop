import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { copyFileSync, mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

// The worker imports the Anthropic SDK for /help. A stub stands in for it here, so this runs with no packages installed.
const room = mkdtempSync(join(tmpdir(), 'capri-codes-'));
const sdk = join(room, 'node_modules', '@anthropic-ai', 'sdk');
mkdirSync(sdk, { recursive: true });
writeFileSync(join(sdk, 'package.json'), JSON.stringify({ name: '@anthropic-ai/sdk', type: 'module', main: 'index.js' }));
writeFileSync(join(sdk, 'index.js'), 'export default class Anthropic {}');
writeFileSync(join(room, 'package.json'), JSON.stringify({ type: 'module' }));
copyFileSync(join(dirname(fileURLToPath(import.meta.url)), 'src', 'index.js'), join(room, 'index.js'));
const { default: worker } = await import(pathToFileURL(join(room, 'index.js')).href);
process.on('exit', () => rmSync(room, { recursive: true, force: true }));

class Database {
  rows = [];
  updates = [];
  prepare(sql) {
    const db = this;
    let values = [];
    return {
      bind(...sent) { values = sent; return this; },
      async first() {
        if (sql === 'INSERT INTO codes (hash, name, budget, expires, created) VALUES (?, ?, ?, ?, ?) RETURNING id') {
          const [hash, name, budget, expires, created] = values;
          assert.match(hash, /^[0-9a-f]{64}$/);
          assert.ok(!db.rows.some(row => row.hash === hash));
          const row = { id: db.rows.length + 1, hash, name, budget, expires, created, spent: 0, off: 0, used: 0, requests: 0 };
          db.rows.push(row);
          return { id: row.id };
        }
        assert.equal(sql, 'SELECT id, budget, spent, expires, off FROM codes WHERE hash = ?');
        const row = db.rows.find(row => row.hash === values[0]);
        return row ? { ...row } : null;
      },
      async all() {
        assert.equal(sql, 'SELECT id, name, budget, spent, expires, off, requests, used FROM codes ORDER BY id DESC');
        return { results: db.rows.map(row => ({ ...row })).reverse() };
      },
      async run() {
        if (sql === 'UPDATE codes SET off = ? WHERE id = ?') {
          const row = db.rows.find(row => row.id === values[1]);
          if (row) row.off = values[0];
          return { meta: { changes: row ? 1 : 0 } };
        }
        assert.equal(sql, 'UPDATE codes SET spent = spent + ?, requests = requests + 1, used = ? WHERE id = ?');
        const [cost, used, id] = values;
        const row = db.rows.find(row => row.id === id);
        assert.ok(row);
        assert.ok(Number.isSafeInteger(cost) && cost >= 0);
        row.spent += cost;
        row.used = used;
        row.requests++;
        db.updates.push({ cost, used, id });
        return { meta: { changes: 1 } };
      }
    };
  }
}

const env = { DB: new Database(), SALT: 'local-salt', CODES_SECRET: 'local-admin-secret', ANTHROPIC_API_KEY: 'local-upstream-key' };
const pending = [];
const ctx = { waitUntil(promise) { pending.push(promise); } };
const settle = async () => { await Promise.all(pending.splice(0)); };
const originalFetch = globalThis.fetch;
const originalError = console.error;
const originalLog = console.log;
let forwarded = [], logs = [];
let upstream = async () => Response.json({ usage: { input_tokens: 1000000, output_tokens: 1000000 }, content: [] });
globalThis.fetch = async (url, options) => {
  assert.equal(url, 'https://api.anthropic.com/v1/messages');
  assert.equal(options.method, 'POST');
  assert.equal(options.redirect, 'manual');
  assert.equal(options.headers.get('x-api-key'), env.ANTHROPIC_API_KEY);
  assert.equal(options.headers.get('authorization'), null);
  forwarded.push({ headers: options.headers, body: JSON.parse(options.body) });
  return upstream();
};
console.log = (...values) => logs.push(values.join(' '));
console.error = (...values) => logs.push(values.join(' '));
const call = (path, method = 'GET', body, headers = {}, config = env) => worker.fetch(new Request('https://local.test' + path, { method, headers: { ...(body === undefined ? {} : { 'Content-Type': 'application/json' }), ...headers }, ...(body === undefined ? {} : { body: JSON.stringify(body) }) }), config, ctx);
const admin = { Authorization: 'Bearer ' + env.CODES_SECRET };
let code;
const brain = (body = { messages: [], max_tokens: 100 }, headers = {}, path = '/brain/v1/messages') => call(path, path.endsWith('/models') ? 'GET' : 'POST', path.endsWith('/models') ? undefined : body, { 'x-api-key': code, 'anthropic-version': '2026-10-21', 'anthropic-beta': 'thinking-binding-controls-2026-08-01', ...headers });
const noCors = response => { for (const name of response.headers.keys()) assert.ok(!name.startsWith('access-control-')); };

try {
  for (const path of ['/codes', '/codes/off', '/codes/on']) {
    assert.equal((await call(path, 'POST', {}, admin, { ...env, CODES_SECRET: '' })).status, 503);
    assert.equal((await call(path, 'POST', {})).status, 401);
    assert.equal((await call(path, 'POST', {}, { Authorization: 'Bearer wrong' })).status, 401);
  }
  assert.equal((await call('/codes', 'GET', undefined, { Authorization: 'Bearer ' + env.CODES_SECRET + 'x' })).status, 401);
  assert.equal((await call('/codes', 'POST', { name: 'Fadie', dollars: -1 }, admin)).status, 400);
  assert.equal((await call('/codes', 'POST', { name: 'Fadie', dollars: 1, days: -1 }, admin)).status, 400);
  assert.equal((await call('/codes', 'POST', { name: '', dollars: 1 }, admin)).status, 400);
  const madeAt = Date.now();
  const made = await call('/codes', 'POST', { name: ' Fadie ', dollars: 5, days: 30 }, admin);
  noCors(made);
  assert.equal(made.status, 200);
  const created = await made.json();
  code = created.code;
  assert.match(code, /^CAPRI(-[0-9A-HJKMNP-TV-Z]{4}){4}$/);
  assert.equal(created.name, 'Fadie');
  assert.equal(created.dollars, 5);
  assert.ok(created.expires >= madeAt + 30 * 86400000 && created.expires <= Date.now() + 30 * 86400000);
  const row = env.DB.rows[0];
  assert.equal(row.hash, createHash('sha256').update(code + ':' + env.SALT).digest('hex'));
  assert.ok(!JSON.stringify(env.DB.rows).includes(code));
  const never = await (await call('/codes', 'POST', { name: 'Never', dollars: 1 }, admin)).json();
  const zero = await (await call('/codes', 'POST', { name: 'Zero', dollars: 1, days: 0 }, admin)).json();
  assert.equal(never.expires, 0);
  assert.equal(zero.expires, 0);
  assert.notEqual(never.code, code);
  assert.notEqual(zero.code, code);
  const list = await (await call('/codes', 'GET', undefined, admin)).json();
  assert.equal(list.codes.length, 3);
  assert.deepEqual(list.codes[2], { id: row.id, name: 'Fadie', dollars: 5, spent: 0, left: 5, expires: row.expires, off: 0, requests: 0, used: 0 });
  assert.ok(!JSON.stringify(list).includes(code));
  assert.ok(!JSON.stringify(list).includes(row.hash));

  const refusal = async (status, type, message, headers = {}) => {
    const response = await brain(undefined, headers);
    assert.equal(response.status, status);
    assert.deepEqual(await response.json(), { type: 'error', error: { type, message } });
    assert.ok(response.headers.has('capri-code-left'));
    assert.ok(response.headers.has('capri-code-expires'));
    noCors(response);
  };
  await refusal(401, 'authentication_error', "That Capri code isn't right.", { 'x-api-key': 'CAPRI-0000-0000-0000-0000' });
  await refusal(401, 'authentication_error', "That Capri code isn't right.", { 'x-api-key': '' });
  assert.equal((await call('/codes/off', 'POST', { id: row.id }, admin)).status, 200);
  await refusal(403, 'permission_error', 'That Capri code was switched off.');
  assert.equal((await call('/codes/on', 'POST', { id: row.id }, admin)).status, 200);
  assert.equal(row.off, 0);
  assert.equal((await call('/codes/off', 'POST', { id: 9999 }, admin)).status, 404);
  assert.equal((await call('/codes/on', 'POST', { id: '1' }, admin)).status, 400);
  const ends = row.expires;
  row.expires = Date.now() - 1;
  await refusal(403, 'permission_error', 'That Capri code has expired.');
  row.expires = ends;
  row.spent = row.budget;
  await refusal(403, 'permission_error', 'That Capri code has used up its budget.');
  assert.equal(forwarded.length, 0);
  row.spent = 0;
  const modelResponse = await brain(undefined, { 'x-api-key': '  ' + code.toLowerCase() + '  ' }, '/brain/v1/models');
  assert.equal(modelResponse.status, 200);
  const models = await modelResponse.json();
  assert.deepEqual(models.data.map(model => model.id), ['claude-haiku-5-5']);
  assert.equal(models.has_more, false);
  assert.equal(forwarded.length, 0);
  assert.equal(row.requests, 0);
  const preflight = await call('/brain/v1/messages', 'OPTIONS', undefined, { 'x-api-key': code });
  noCors(preflight);
  assert.equal(preflight.status, 404);

  const tools = [{ name: 'local_tool', input_schema: { type: 'object' } }];
  const messages = [{ role: 'user', content: 'Keep this text.' }];
  const response = await brain({ model: 'claude-sonnet-5-5', max_tokens: 9999999, tools, messages, temperature: 0.7, top_p: 0.9, top_k: 20, fallbacks: 'default', thinking: { type: 'enabled', budget_tokens: 1000 }, output_config: { effort: 'max' } }, { 'x-api-key': '  ' + code.toLowerCase() + '  ' });
  assert.equal(response.status, 200);
  assert.equal(response.headers.get('capri-code-left'), '5.00');
  assert.equal(response.headers.get('capri-code-expires'), new Date(ends).toISOString());
  noCors(response);
  const sent = forwarded.at(-1);
  assert.equal(sent.body.model, 'claude-haiku-5-5');
  assert.equal(sent.body.max_tokens, 9999999);
  assert.deepEqual(sent.body.messages, messages);
  assert.deepEqual(sent.body.tools, tools);
  assert.deepEqual(sent.body.thinking, { type: 'adaptive' });
  assert.equal(sent.body.output_config.effort, 'max');
  for (const name of ['temperature', 'top_p', 'top_k', 'fallbacks']) assert.ok(!(name in sent.body));
  assert.equal(sent.headers.get('anthropic-version'), '2026-10-21');
  assert.equal(sent.headers.get('anthropic-beta'), 'thinking-binding-controls-2026-08-01');
  assert.ok(!(await response.text()).includes(env.ANTHROPIC_API_KEY));
  await settle();
  assert.equal(row.spent, 600000);
  assert.equal(row.requests, 1);
  assert.ok(row.used >= madeAt);
  assert.equal(env.DB.updates.length, 1);
  for (const effort of ['xhigh', 'max', 'low']) {
    upstream = async () => Response.json({ usage: { input_tokens: 1, output_tokens: 1 }, content: [] });
    await (await brain({ messages, tools, max_tokens: 100, thinking: { type: 'disabled' }, output_config: { effort } })).text();
    assert.equal(forwarded.at(-1).body.output_config.effort, effort === 'low' ? 'low' : 'high');
    assert.deepEqual(forwarded.at(-1).body.thinking, { type: 'disabled' });
    await settle();
    assert.equal(env.DB.updates.at(-1).cost, 1);
  }
  upstream = async () => Response.json({ usage: { cache_creation_input_tokens: 8, output_tokens: 2 }, content: [] });
  await (await brain()).text();
  await settle();
  assert.equal(env.DB.updates.at(-1).cost, 2);

  const first = 'event: message_start\r\ndata: {"type":"message_start","message":{"usage":{"input_tokens":1000,"output_tokens":2,"cache_read_input_tokens":1000,"cache_creation_input_tokens":1000,"cache_creation":{"ephemeral_5m_input_tokens":600,"ephemeral_1h_input_tokens":400}}}}\r\n\r\n';
  const rest = ': keepalive\r\n\r\nevent: content_block_delta\ndata: {"type":"content_block_delta","delta":{"text":"Hi é."}}\n\nevent: message_delta\r\ndata: {"type":"message_delta",\r\ndata: "usage":{"output_tokens":1000}}\r\n\r\ndata: {"type":"message_delta","usage":{"output_tokens":1000}}\n\ndata: {"type":"message_stop"}';
  let release;
  const held = new Promise(resolve => { release = resolve; });
  upstream = async () => new Response(new ReadableStream({
    async start(controller) {
      controller.enqueue(new TextEncoder().encode(first));
      await held;
      for (const byte of new TextEncoder().encode(rest)) controller.enqueue(new Uint8Array([byte]));
      controller.close();
    }
  }), { headers: { 'Content-Type': 'text/event-stream', 'access-control-allow-origin': '*', 'request-id': 'local-request' } });
  const was = row.spent, updatesBefore = env.DB.updates.length;
  const stream = await brain({ messages, max_tokens: 100, stream: true });
  const reader = stream.body.getReader();
  const chunks = [];
  const arrived = await reader.read();
  assert.equal(new TextDecoder().decode(arrived.value), first);
  assert.equal(row.spent, was);
  assert.equal(env.DB.updates.length, updatesBefore);
  chunks.push(arrived.value);
  release();
  while (true) {
    const next = await reader.read();
    if (next.done) break;
    chunks.push(next.value);
  }
  assert.equal(Buffer.concat(chunks).toString(), first + rest);
  assert.equal(stream.headers.get('request-id'), 'local-request');
  noCors(stream);
  await settle();
  assert.equal(row.spent, was + 765);
  assert.equal(env.DB.updates.length, updatesBefore + 1);
  assert.equal(env.DB.updates.at(-1).cost, 765);

  upstream = async () => new Response(new ReadableStream({
    start(controller) { controller.enqueue(new TextEncoder().encode(first)); },
    pull() { throw new Error('Local stream ended early.'); }
  }), { headers: { 'Content-Type': 'text/event-stream' } });
  const broken = await brain({ messages, max_tokens: 100, stream: true });
  await assert.rejects(broken.text());
  await settle();
  assert.equal(env.DB.updates.at(-1).cost, 266);

  const errorBody = '{ "type": "error", "error": { "type": "overloaded_error", "message": "Try later." } }';
  upstream = async () => new Response(errorBody, { status: 529, headers: { 'Content-Type': 'application/json', 'request-id': 'error-request' } });
  const beforeError = row.spent, requestsBeforeError = row.requests;
  const error = await brain();
  assert.equal(error.status, 529);
  assert.equal(await error.text(), errorBody);
  assert.equal(error.headers.get('request-id'), 'error-request');
  assert.equal(error.headers.get('capri-code-left'), (Math.max(0, row.budget - beforeError) / 1000000).toFixed(2));
  await settle();
  assert.equal(row.spent, beforeError);
  assert.equal(row.requests, requestsBeforeError + 1);

  row.spent = row.budget - 1;
  upstream = async () => Response.json({ usage: { input_tokens: 1000, output_tokens: 1000 } });
  await (await brain()).text();
  await settle();
  assert.equal(row.spent, row.budget + 599);
  await refusal(403, 'permission_error', 'That Capri code has used up its budget.');
  const finalList = await (await call('/codes', 'GET', undefined, admin)).json();
  assert.equal(finalList.codes.find(entry => entry.id === row.id).left, 0);
  const neverResponse = await brain(undefined, { 'x-api-key': never.code });
  assert.equal(neverResponse.headers.get('capri-code-expires'), '');
  await neverResponse.text();
  await settle();
  assert.ok(!JSON.stringify(finalList).includes(env.ANTHROPIC_API_KEY));
  assert.ok(!logs.join('\n').includes(code));
  assert.ok(!logs.join('\n').includes(env.ANTHROPIC_API_KEY));
  assert.ok(!logs.join('\n').includes(env.CODES_SECRET));
} finally {
  globalThis.fetch = originalFetch;
  console.error = originalError;
  console.log = originalLog;
}

console.log('PASS code admin, generation, hashing, refusals, models, Haiku parameters, headers, streaming, cache costs and budget accounting');

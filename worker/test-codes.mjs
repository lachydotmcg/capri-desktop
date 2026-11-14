import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { copyFileSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { DatabaseSync } from 'node:sqlite';
import { fileURLToPath, pathToFileURL } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const room = mkdtempSync(join(here, 'codes-'));
const sdk = join(room, 'node_modules', '@anthropic-ai', 'sdk');
mkdirSync(sdk, { recursive: true });
writeFileSync(join(sdk, 'package.json'), JSON.stringify({ name: '@anthropic-ai/sdk', type: 'module', main: 'index.js' }));
writeFileSync(join(sdk, 'index.js'), 'export default class Anthropic {}');
writeFileSync(join(room, 'package.json'), JSON.stringify({ type: 'module' }));
writeFileSync(join(room, 'index.js'), readFileSync(join(here, 'src', 'index.js'), 'utf8') + '\nexport { codeGuess };\n');
const { default: worker, codeGuess } = await import(pathToFileURL(join(room, 'index.js')).href);
process.on('exit', () => rmSync(room, { recursive: true, force: true }));

class Database {
  sqlite = new DatabaseSync(':memory:');
  updates = [];
  statements = [];
  batches = [];
  constructor(files = ['0007_codes.sql', '0012_code_kinds.sql']) {
    for (const file of files) this.sqlite.exec(readFileSync(join(here, 'migrations', file), 'utf8'));
  }
  get rows() { return this.sqlite.prepare('SELECT * FROM codes ORDER BY id').all().map(row => ({ ...row })); }
  get flights() { return this.sqlite.prepare('SELECT * FROM code_flights').all().map(row => ({ ...row })); }
  row(id) { return { ...this.sqlite.prepare('SELECT * FROM codes WHERE id = ?').get(id) }; }
  set(id, fields) { this.sqlite.prepare('UPDATE codes SET ' + Object.keys(fields).map(name => name + ' = ?').join(', ') + ' WHERE id = ?').run(...Object.values(fields), id); }
  prepare(sql) {
    const db = this;
    let values = [];
    const execute = method => {
      db.statements.push({ sql, values: [...values], method });
      const query = db.sqlite.prepare(sql);
      if (method === 'first') return query.get(...values) ?? null;
      if (method === 'all') return { results: query.all(...values) };
      const result = query.run(...values);
      if (sql.startsWith('UPDATE codes SET spent =') && sql.includes('used = MAX(used, ?) WHERE id = ?')) {
        const cost = values[0], [used, id] = values.slice(-2);
        assert.ok(Number.isSafeInteger(cost) && cost >= 0);
        db.updates.push({ cost, used, id });
      }
      return { meta: { changes: Number(result.changes) } };
    };
    return {
      sql,
      bind(...sent) { values = sent; return this; },
      async first() { return execute('first'); },
      async all() { return execute('all'); },
      async run() { return execute('run'); },
      execute() { return execute(sql.startsWith('SELECT ') ? 'all' : 'run'); }
    };
  }
  async batch(statements) {
    this.batches.push(statements);
    this.sqlite.exec('BEGIN');
    try {
      const results = statements.map(statement => statement.execute());
      this.sqlite.exec('COMMIT');
      return results;
    } catch (error) { this.sqlite.exec('ROLLBACK'); throw error; }
  }
}

const env = { DB: new Database(), SALT: 'local-salt', CODES_SECRET: 'local-admin-secret', ANTHROPIC_API_KEY: 'local-upstream-key', CODES_KEY_STAFF: 'local-staff-key', CODES_KEY_GIVEAWAY: 'local-giveaway-key' };
const pending = [];
const ctx = { waitUntil(promise) { pending.push(promise); } };
const settle = async () => { await Promise.all(pending.splice(0)); };
const deferred = () => {
  let resolve, reject;
  const promise = new Promise((yes, no) => { resolve = yes; reject = no; });
  return { promise, resolve, reject };
};
const originalFetch = globalThis.fetch;
const originalError = console.error;
const originalLog = console.log;
const originalNow = Date.now;
let now = Date.now();
const flightAge = 900000;
const guess = codeGuess(100);
Date.now = () => now;
const forwarded = [], logs = [], issued = [];
let upstream = async () => Response.json({ usage: { input_tokens: 1000000, output_tokens: 100 }, content: [] });
globalThis.fetch = async (url, options) => {
  assert.equal(url, 'https://api.anthropic.com/v1/messages');
  assert.equal(options.method, 'POST');
  assert.equal(options.redirect, 'manual');
  assert.ok(options.signal instanceof AbortSignal);
  assert.equal(options.headers.get('authorization'), null);
  forwarded.push({ headers: new Headers(options.headers), body: JSON.parse(options.body), wire: options.body });
  return upstream();
};
console.log = (...values) => logs.push(values.join(' '));
console.error = (...values) => logs.push(values.join(' '));
const call = (path, method = 'GET', body, headers = {}, config = env, context = ctx) => worker.fetch(new Request('https://local.test' + path, { method, headers: { ...(body === undefined ? {} : { 'Content-Type': 'application/json' }), ...headers }, ...(body === undefined ? {} : { body: JSON.stringify(body) }) }), config, context);
const admin = { Authorization: 'Bearer ' + env.CODES_SECRET };
let code;
const brain = (body = { messages: [], max_tokens: 100 }, headers = {}, path = '/brain/v1/messages', config = env, context = ctx) => call(path, path.endsWith('/models') ? 'GET' : 'POST', path.endsWith('/models') ? undefined : body, { 'x-api-key': code, 'anthropic-version': '2026-10-21', 'anthropic-beta': 'thinking-binding-controls-2026-08-01', ...headers }, config, context);
const noCors = response => { for (const name of response.headers.keys()) assert.ok(!name.startsWith('access-control-')); };
const error = async (response, status, body) => {
  assert.equal(response.status, status);
  assert.deepEqual(await response.json(), body);
  assert.equal(response.headers.get('cache-control'), 'no-store');
  noCors(response);
};
const refusal = async (status, type, message, headers = {}, body, config = env) => {
  const before = [forwarded.length, env.DB.updates.length, env.DB.flights.length];
  const response = await brain(body, headers, '/brain/v1/messages', config);
  await error(response, status, { type: 'error', error: { type, message } });
  assert.ok(response.headers.has('capri-code-left'));
  assert.ok(response.headers.has('capri-code-expires'));
  assert.deepEqual([forwarded.length, env.DB.updates.length, env.DB.flights.length], before);
};
const make = async (body, config = env) => {
  const response = await call('/codes', 'POST', body, admin, config);
  assert.equal(response.status, 200);
  noCors(response);
  const made = await response.json();
  assert.match(made.code, /^CAPRI(-[0-9A-HJKMNP-TV-Z]{4}){4}$/);
  issued.push(made.code);
  if (made.named) issued.push(made.named);
  assert.equal(config.DB.row(made.id).hash, createHash('sha256').update(made.code + ':' + config.SALT).digest('hex'));
  for (const spelling of issued) assert.ok(!JSON.stringify(config.DB.rows).includes(spelling));
  return made;
};

try {
  assert.equal(codeGuess(100), 1000250);
  assert.equal(codeGuess(128000), 1320000);
  const guessColumn = env.DB.sqlite.prepare('PRAGMA table_info(code_flights)').all().find(column => column.name === 'guess');
  assert.equal(guessColumn.type, 'INTEGER');
  assert.equal(guessColumn.notnull, 1);
  assert.equal(guessColumn.dflt_value, '0');

  for (const path of ['/codes', '/codes/off', '/codes/on', '/codes/limit', '/codes/days', '/codes/unknown']) {
    for (const [config, headers, status, message] of [
      [{ ...env, CODES_SECRET: '' }, admin, 503, 'Codes secret is not set.'],
      [env, {}, 401, 'Not authorised.'],
      [env, { Authorization: 'Bearer wrong' }, 401, 'Not authorised.'],
      [env, { Authorization: 'Bearer ' + env.CODES_SECRET + 'x' }, 401, 'Not authorised.'],
      [{ ...env, SALT: '' }, admin, 503, "Capri codes aren't ready."]
    ]) await error(await call(path, 'POST', {}, headers, config), status, { error: message });
  }
  for (const kind of [null, 'STAFF', 'person', 0, {}, []]) await error(await call('/codes', 'POST', { name: 'Fadie', dollars: 5, kind }, admin), 400, { error: 'Give a code kind of giveaway or staff.' });
  for (const tag of [null, '', 'A', 'a-b', ' abc ', '1234567890123', 'Løst', 'ß', 'ſx', 'AB\n', 'AB\r', 'AB\u2028', 123, {}]) await error(await call('/codes', 'POST', { name: 'Fadie', dollars: 5, tag }, admin), 400, { error: 'Give a tag of 2 to 12 letters or numbers.' });
  for (const kind of ['giveaway', 'staff']) {
    for (const dollars of [null, '5', true, {}, [], -1, -0.0000001, 0.00000049, Number.MAX_VALUE, Number.MAX_SAFE_INTEGER / 1000000 + 1, ...(kind === 'giveaway' ? [undefined, 0] : [])]) await error(await call('/codes', 'POST', { name: 'Fadie', kind, dollars }, admin), 400, { error: 'Give a positive dollar limit.' });
  }
  for (const body of [{ name: '', dollars: 1 }, { name: 'x'.repeat(81), dollars: 1 }, { name: 'Fadie', dollars: 1, days: -1 }, { name: 'Fadie', dollars: 1, days: null }]) assert.equal((await call('/codes', 'POST', body, admin)).status, 400);
  const made = await make({ name: ' Fadie ', dollars: 5, days: 30, tag: 'yoplaxs' });
  code = made.code;
  const id = made.id;
  assert.equal(made.name, 'Fadie');
  assert.equal(made.kind, 'giveaway');
  assert.equal(made.tag, 'YOPLAXS');
  assert.equal(made.named, 'CAPRI-YOPLAXS' + code.slice(5));
  assert.equal(made.expires, now + 30 * 86400000);
  const never = await make({ name: 'Never', dollars: 1 });
  const zero = await make({ name: 'Zero', dollars: 1, days: 0 });
  assert.equal(never.expires, 0);
  assert.equal(zero.expires, 0);
  assert.notEqual(never.code, code);
  for (const [name, tag] of [['Løst', 'LST'], ['cilly_boy', 'CILLYBOY'], ['maxi || MODERATOR & SUPPORT', 'MAXIMODERATO'], ['ø !a', ''], ['ø', '']]) {
    const made = await make({ name, dollars: 1 });
    assert.equal(made.tag, tag);
    assert.equal(env.DB.row(made.id).tag, tag);
    assert.equal(made.named, tag ? 'CAPRI-' + tag + made.code.slice(5) : undefined);
  }
  const tagless = await make({ name: '!', dollars: 1 });
  const staff = await make({ name: 'Staff', kind: 'staff' });
  const staffZero = await make({ name: 'Staff Zero', kind: 'staff', dollars: 0 });
  const staffLimit = await make({ name: 'Staff Limit', kind: 'staff', dollars: 20 });
  assert.equal(env.DB.row(staff.id).budget, 0);
  assert.equal(staffZero.dollars, 0);
  assert.equal(staffLimit.dollars, 20);
  const list = await (await call('/codes', 'GET', undefined, admin)).json();
  assert.deepEqual(list.codes.find(row => row.id === id), { id, name: 'Fadie', kind: 'giveaway', tag: 'YOPLAXS', dollars: 5, spent: 0, left: 5, expires: made.expires, off: 0, requests: 0, used: 0 });
  assert.equal(list.codes.find(row => row.id === staff.id).left, null);
  assert.equal(list.codes.find(row => row.id === staff.id).dollars, 0);
  for (const row of env.DB.rows) assert.ok(!JSON.stringify(list).includes(row.hash));

  const beforeEdits = JSON.stringify(env.DB.rows);
  for (const [path, field, bad, message] of [
    ['/codes/limit', 'dollars', [undefined, null, '10', true, {}, [], 0, -1, -0.0000001, 0.00000049, Number.MAX_VALUE, Number.MAX_SAFE_INTEGER / 1000000 + 1], 'Give a positive dollar limit.'],
    ['/codes/days', 'days', [undefined, null, '30', true, {}, [], 0, -1, Number.MAX_VALUE, 100000001], 'Give days above zero, or never.']
  ]) {
    for (const value of bad) await error(await call(path, 'POST', { id, [field]: value }, admin), 400, { error: message });
    for (const value of [undefined, null, '1', true, 0, -1, 1.5, Number.MAX_SAFE_INTEGER + 1]) await error(await call(path, 'POST', { id: value, [field]: 1 }, admin), 400, { error: 'Give a code id.' });
    for (const body of [null, [], 'bad']) await error(await call(path, 'POST', body, admin), 400, { error: 'Bad request' });
    await error(await worker.fetch(new Request('https://local.test' + path, { method: 'POST', headers: admin, body: '{' }), env, ctx), 400, { error: 'Bad request' });
    await error(await call(path, 'POST', { id: 9999, [field]: 1 }, admin), 404, { error: 'No such code.' });
    for (const method of ['GET', 'PUT', 'DELETE']) await error(await call(path, method, undefined, admin), 404, { error: 'Not found' });
  }
  for (const never of [false, 'true', 1, null]) await error(await call('/codes/days', 'POST', { id, never }, admin), 400, { error: 'Give days above zero, or never.' });
  for (const days of [0, 30]) await error(await call('/codes/days', 'POST', { id, days, never: true }, admin), 400, { error: 'Give days above zero, or never.' });
  assert.equal(JSON.stringify(env.DB.rows), beforeEdits);

  for (const spelling of ['CAPRI-0000-0000-0000-0000', '', made.named.replace('YOPLAXS', 'WRONG'), 'CAPRI-XX' + tagless.code.slice(5), 'CAPRI-YOPLAXS-0000-0000-0000-0000', 'CAPRI-Y-0000-0000-0000-0000']) await refusal(401, 'authentication_error', "That Capri code isn't right.", { 'x-api-key': spelling });
  await refusal(503, 'api_error', "Capri codes aren't ready.", {}, undefined, { ...env, SALT: '' });
  assert.equal((await call('/codes/off', 'POST', { id }, admin)).status, 200);
  await refusal(403, 'permission_error', 'That Capri code was switched off.');
  assert.equal((await call('/codes/on', 'POST', { id }, admin)).status, 200);
  assert.equal((await call('/codes/off', 'POST', { id: 9999 }, admin)).status, 404);
  assert.equal((await call('/codes/on', 'POST', { id: '1' }, admin)).status, 400);
  env.DB.set(id, { expires: now - 1 });
  await refusal(403, 'permission_error', 'That Capri code has expired.');
  env.DB.set(id, { expires: made.expires, spent: 5000000 });
  await refusal(403, 'permission_error', 'That Capri code has used up its budget.');
  env.DB.set(id, { spent: 0 });
  for (const spelling of [code, '  ' + code.toLowerCase() + '  ', made.named, '  ' + made.named.toLowerCase() + '  ']) {
    const response = await brain(undefined, { 'x-api-key': spelling }, '/brain/v1/models');
    assert.equal(response.status, 200);
    assert.deepEqual(await response.json(), { data: [{ type: 'model', id: 'claude-haiku-5-5', display_name: 'Claude Haiku 5.5' }], has_more: false, first_id: 'claude-haiku-5-5', last_id: 'claude-haiku-5-5' });
  }
  assert.equal(forwarded.length, 0);
  assert.equal(env.DB.row(id).requests, 0);
  assert.equal((await call('/brain/v1/messages', 'OPTIONS', undefined, { 'x-api-key': code })).status, 404);
  await refusal(503, 'api_error', "Capri codes aren't ready.", {}, undefined, { ...env, CODES_KEY_STAFF: '' });
  for (const body of [null, [], 'bad']) await refusal(400, 'invalid_request_error', 'Bad request', {}, body);
  await refusal(400, 'invalid_request_error', 'Capri codes answer in one piece. Leave stream off.', {}, { messages: [], max_tokens: 100, stream: true });

  const messages = [{ role: 'user', content: 'Keep this private text.' }];
  for (const max_tokens of [null, 0, -1, 1.5, '100', 128001])
    await refusal(400, 'invalid_request_error', 'Give a valid max_tokens value.', {}, { messages, max_tokens });
  for (const block of [
    { type: 'image', source: { type: 'url', url: 'https://local.test/private' } },
    { type: 'image', source: { type: 'file', file_id: 'local-file' } },
    { type: 'document', source: { type: 'base64', data: 'AAAA', media_type: 'application/pdf' } }
  ]) {
    await refusal(400, 'invalid_request_error', 'Capri codes accept text and images sent directly.', {}, {
      max_tokens: 100, messages: [{ role: 'user', content: [{ type: 'tool_result', tool_use_id: 'local', content: [block] }] }]
    });
  }
  const tools = [{ name: 'local_tool', description: 'A local tool.', input_schema: { type: 'object' } }, { type: 'custom', name: 'another_tool', input_schema: {} }, { type: 'computer_toolset_20260801' }];
  const allowed = { model: 'claude-haiku-5-5', max_tokens: 128000, system: [{ type: 'text', text: 'Keep this system.', cache_control: { type: 'ephemeral' } }], messages, stop_sequences: ['Done.'], tool_choice: { type: 'auto' }, tools, thinking: { type: 'adaptive', display: 'summarized', block_binding: { prefix_mismatch_behavior: 'drop_block' } }, output_config: { effort: 'max' } };
  const extras = { temperature: 0.7, top_p: 0.9, top_k: 20, fallbacks: ['a different model'], speed: 'fast', inference_geo: 'us', service_tier: 'priority', metadata: { user_id: 'private-id' }, container: { skills: [{ type: 'custom', skill_id: 'private-skill' }] }, context_management: { edits: [{ type: 'compact_20260112' }] }, compaction: { prompt: 'private-prompt' }, mcp_servers: [{ url: 'https://private.test' }], thread: 'private-thread', diagnostics: true, cache_control: {}, unknown_field: 'private-value' };
  const badTools = [{ type: 'advisor_20260301', name: 'advisor', model: 'a different model' }, { type: 'web_search_20260209', name: 'web_search' }, { type: 'code_execution_20260120', name: 'code_execution' }, { type: 'mcp_toolset', mcp_server_name: 'private-server' }, { type: 'web_fetch_20260209' }, { type: 'tool_search_tool_regex_20251119' }, { type: 'computer_toolset_2026080' }, { type: 'computer_toolset_202608010' }, { name: 'missing_schema' }, { name: 'array_schema', input_schema: [] }, null];
  const beforeLogs = logs.length;
  const response = await brain({ ...allowed, ...extras, model: 'a different model', tools: [...tools, ...badTools], thinking: { ...allowed.thinking, type: 'enabled', budget_tokens: 1000, extra: 'private-thinking', block_binding: { ...allowed.thinking.block_binding, extra: 'private-binding' } }, output_config: { effort: 'max', format: { type: 'json_schema' }, extra: 'private-output' } }, { 'x-api-key': '  ' + made.named.toLowerCase() + '  ', 'anthropic-beta': 'advisor-tool-2026-03-01, thinking-binding-controls-2026-08-01, compact-2026-09-04, mcp-client-2025-11-20, second-beta-2026-01-01', Authorization: 'Bearer private-header', Cookie: 'private-cookie', 'anthropic-extra': 'private-extra' });
  assert.equal(response.status, 200);
  assert.equal(response.headers.get('capri-code-left'), '5.00');
  assert.equal(response.headers.get('capri-code-expires'), new Date(made.expires).toISOString());
  noCors(response);
  assert.deepEqual(forwarded.at(-1).body, allowed);
  assert.deepEqual(Object.fromEntries(forwarded.at(-1).headers), { 'anthropic-beta': 'thinking-binding-controls-2026-08-01', 'anthropic-version': '2026-10-21', 'content-type': 'application/json', 'x-api-key': env.CODES_KEY_GIVEAWAY });
  assert.equal(logs.length, beforeLogs + 1);
  const names = new Set(logs.at(-1).split(', '));
  for (const name of [...Object.keys(extras), ...badTools.filter(tool => tool && tool.type).map(tool => tool.type), 'thinking.budget_tokens', 'thinking.extra', 'thinking.block_binding.extra', 'output_config.format', 'output_config.extra', 'advisor-tool-2026-03-01', 'compact-2026-09-04', 'mcp-client-2025-11-20', 'second-beta-2026-01-01']) assert.ok(names.has(name), name);
  assert.ok(!logs.at(-1).includes('private'));
  await settle();
  assert.equal(env.DB.row(id).spent, 500250);
  assert.equal(env.DB.row(id).requests, 1);
  assert.equal(env.DB.row(id).used, now);
  assert.equal(env.DB.flights.length, 0);
  env.DB.set(id, { budget: 100000000 });
  upstream = async () => Response.json({ usage: { input_tokens: 1, output_tokens: 1 }, content: [] });
  for (const system of ['Keep this system.', allowed.system]) {
    const capri = { model: 'claude-haiku-5-5', max_tokens: 16000, system, messages, tools, thinking: { type: 'adaptive', block_binding: { prefix_mismatch_behavior: 'drop_block' } }, output_config: { effort: 'high' } };
    const before = logs.length;
    await brain(capri);
    await settle();
    assert.deepEqual(forwarded.at(-1).body, capri);
    assert.equal(logs.length, before);
  }
  for (const effort of ['xhigh', 'max', 'high', 'medium', 'low']) {
    const capri = { messages, tools, max_tokens: 100, thinking: { type: 'disabled' }, output_config: { effort } };
    await brain(capri);
    await settle();
    assert.deepEqual(forwarded.at(-1).body, { model: 'claude-haiku-5-5', ...capri, output_config: { effort: ['xhigh', 'max'].includes(effort) ? 'high' : effort } });
    assert.equal(env.DB.updates.at(-1).cost, 1);
  }
  for (const stream of [false, null, 'true', 1, {}]) {
    await brain({ messages, max_tokens: 100, stream });
    await settle();
    assert.deepEqual(forwarded.at(-1).body, { model: 'claude-haiku-5-5', messages, max_tokens: 100 });
    assert.equal(logs.at(-1), 'stream');
  }
  for (const tools of [[], badTools, null, {}]) {
    await brain({ messages, max_tokens: 100, tools });
    await settle();
    assert.ok(!('tools' in forwarded.at(-1).body));
  }
  for (const type of ['computer_toolset_20260801\n', 'computer_toolset_20260801\r', 'computer_toolset_20260801\u2028']) {
    await brain({ messages, max_tokens: 100, tools: [{ type }] });
    await settle();
    assert.ok(!('tools' in forwarded.at(-1).body));
  }
  await brain({ messages, max_tokens: 100, thinking: { type: 'enabled', display: 1, block_binding: { prefix_mismatch_behavior: 1 } }, output_config: { extra: true } });
  await settle();
  assert.deepEqual(forwarded.at(-1).body, { model: 'claude-haiku-5-5', messages, max_tokens: 100, thinking: { type: 'adaptive' } });
  for (const version of ['', 'not-a-date', '2026-1-1', '2026-10-21 extra', '2026-10-21']) {
    await brain(undefined, { 'anthropic-version': version, 'anthropic-beta': 'advisor-tool-2026-03-01, compact-2026-09-04' });
    await settle();
    assert.equal(forwarded.at(-1).headers.get('anthropic-version'), version === '2026-10-21' ? version : '2023-06-01');
    assert.equal(forwarded.at(-1).headers.get('anthropic-beta'), null);
  }
  await call('/brain/v1/messages', 'POST', { messages, max_tokens: 100 }, { 'x-api-key': code });
  await settle();
  assert.equal(forwarded.at(-1).headers.get('anthropic-version'), '2023-06-01');
  assert.equal(forwarded.at(-1).headers.get('anthropic-beta'), null);
  const hidden = [code.toLowerCase(), made.named.toLowerCase(), env.ANTHROPIC_API_KEY, env.CODES_SECRET, env.SALT, 'sk-ant-unrelated-key'];
  const beforeHidden = logs.length;
  await brain({ messages, max_tokens: 100, ...Object.fromEntries(hidden.map(name => [name, 'private-value'])), tools: hidden.map(type => ({ type })), thinking: { type: 'adaptive', ...Object.fromEntries(hidden.map(name => [name, 'private-value'])) }, output_config: { ...Object.fromEntries(hidden.map(name => [name, 'private-value'])) } }, { 'anthropic-beta': hidden.join(', ') });
  await settle();
  assert.equal(logs.length, beforeHidden + 1);
  for (const name of hidden) assert.ok(!logs.at(-1).includes(name));
  const beforePlain = env.DB.row(id).requests;
  for (const spelling of [code, made.named, '  ' + code.toLowerCase() + '  ', '  ' + made.named.toLowerCase() + '  ']) assert.equal((await brain(undefined, { 'x-api-key': spelling })).status, 200);
  await settle();
  assert.equal(env.DB.row(id).requests, beforePlain + 4);

  const small = { input_tokens: 40000, cache_read_input_tokens: 30000, cache_creation_input_tokens: 30000, cache_creation: { ephemeral_5m_input_tokens: 20000, ephemeral_1h_input_tokens: 10000 }, output_tokens: 1000 };
  const flat = { input_tokens: 40000, cache_read_input_tokens: 30000, cache_creation_input_tokens: 30000, output_tokens: 1000 };
  const iterations = [{ type: 'message', model: 'another-model', input_tokens: 1000000, output_tokens: 1000000 }, { type: 'compaction', input_tokens: 100, cache_read_input_tokens: 50, cache_creation_input_tokens: 40, cache_creation: { ephemeral_5m_input_tokens: 10, ephemeral_1h_input_tokens: 30 }, output_tokens: 20 }, { type: 'advisor', input_tokens: 200, cache_read_input_tokens: 60, cache_creation_input_tokens: 60, cache_creation: { ephemeral_5m_input_tokens: 20, ephemeral_1h_input_tokens: 70 }, output_tokens: 30 }, null];
  for (const [usage, cost] of [
    [small, 9300], [{ ...small, input_tokens: 40001 }, 46501], [flat, 8550], [{ ...flat, input_tokens: 40001 }, 42751],
    [{ ...small, cache_creation: {} }, 8550], [{ ...small, cache_creation: { ephemeral_1h_input_tokens: 10000 } }, 9300],
    [{ cache_creation_input_tokens: 8, output_tokens: 2 }, 2],
    [{ input_tokens: 10, output_tokens: 4, server_tool_use: { web_search_requests: 3 } }, 30003],
    [{ input_tokens: 10, output_tokens: 4, iterations }, 7903],
    [{ input_tokens: 10, output_tokens: 4, iterations: [{ type: 'message', ...small }] }, 3],
    [{ iterations: [{ type: 'new_type', cache_creation: { ephemeral_5m_input_tokens: 2, ephemeral_1h_input_tokens: 3 }, cache_read_input_tokens: 4, input_tokens: 5, output_tokens: 6 }] }, 440],
    [{ iterations: {} }, 0]
  ]) {
    upstream = async () => Response.json({ usage: { input_tokens: 0, output_tokens: 0, ...usage }, content: [] });
    assert.equal((await brain()).status, 200);
    await settle();
    assert.equal(env.DB.updates.at(-1).cost, cost);
  }

  const keyEnv = { ...env, CODES_KEY_STAFF: 'local-staff-key', CODES_KEY_GIVEAWAY: 'local-giveaway-key' };
  const refusedReplies = [
    [401, 'private authentication reply'], [402, { error: { message: 'private billing reply' } }], [403, { error: { message: 'private permission reply' } }],
    [400, { type: 'error', error: { type: 'invalid_request_error', message: 'You have reached your specified workspace API usage limits. Private workspace limit.' } }],
    [400, { type: 'error', error: { type: 'invalid_request_error', message: 'You have reached your specified API usage limits. Private organization limit.' } }],
    [400, { type: 'error', error: { type: 'invalid_request_error', message: 'Your credit balance is too low. Private balance.' } }],
    [429, { type: 'error', error: { type: 'rate_limit_error', message: 'private spend cap', details: { error_code: 'enforced_spend_limit_reached' } } }]
  ];
  for (const [target, kind] of [[staffLimit, 'staff'], [made, 'giveaway']]) {
    for (const [status, refusedBody] of refusedReplies) {
      const before = env.DB.row(target.id), beforeFetch = forwarded.length, beforeLogs = logs.length, beforeBatches = env.DB.batches.length, beforeUpdates = env.DB.updates.length;
      const answer = { usage: { input_tokens: 1000, output_tokens: 1000 }, content: [{ type: 'text', text: 'Local answer.' }] };
      let calls = 0;
      upstream = async () => calls++ === 0 ? new Response(typeof refusedBody === 'string' ? refusedBody : JSON.stringify(refusedBody), { status }) : Response.json(answer);
      const response = await brain(undefined, { 'x-api-key': target.code }, '/brain/v1/messages', keyEnv);
      assert.equal(response.status, 200);
      assert.deepEqual(await response.json(), answer);
      await settle();
      const asked = forwarded.slice(beforeFetch), expected = kind === 'staff' ? [keyEnv.CODES_KEY_STAFF, keyEnv.CODES_KEY_GIVEAWAY] : [keyEnv.CODES_KEY_GIVEAWAY, keyEnv.CODES_KEY_STAFF];
      assert.deepEqual(asked.map(sent => sent.headers.get('x-api-key')), expected);
      assert.equal(asked[0].wire, asked[1].wire);
      assert.deepEqual(asked[0].body, asked[1].body);
      assert.deepEqual(Object.fromEntries(asked[0].headers), { ...Object.fromEntries(asked[1].headers), 'x-api-key': expected[0] });
      assert.equal(env.DB.row(target.id).spent, before.spent + 600);
      assert.equal(env.DB.row(target.id).requests, before.requests + 1);
      assert.equal(env.DB.flights.length, 0);
      assert.equal(env.DB.batches.length, beforeBatches + 2);
      assert.equal(env.DB.updates.length, beforeUpdates + 1);
      assert.deepEqual(logs.slice(beforeLogs), ['codes ' + kind + ' ' + status]);
    }
  }
  env.DB.set(id, { budget: 100000000 });
  for (const [status, reply] of [
    [429, { error: { message: 'private rate limit', details: { error_code: 'rate_limit_reached' } } }],
    [429, { error: { message: 'You have reached your specified workspace API usage limits.' } }],
    [429, 'private non-JSON rate limit'], [429, { details: { error_code: 'enforced_spend_limit_reached' } }], [429, { error: null }],
    [400, { error: { message: 'private invalid request' } }], [400, 'private non-JSON bad request'],
    [400, { message: 'You have reached your specified API usage limits.' }], [400, { error: null }], [400, { error: [] }], [400, { error: { message: 1 } }],
    [400, { type: 'error', error: { type: 'invalid_request_error', message: 'tools.0.name: invalid value Your credit balance is too low' } }],
    [404, { error: { message: 'private not found' } }], [413, 'private too large'], [500, 'private server error'], [503, 'private unavailable'], [529, 'private overloaded']
  ]) {
    const wire = typeof reply === 'string' ? reply : JSON.stringify(reply), before = env.DB.row(id), beforeFetch = forwarded.length, beforeLogs = logs.length;
    upstream = async () => new Response(wire, { status, headers: { 'request-id': 'local-error' } });
    const response = await brain(undefined, {}, '/brain/v1/messages', keyEnv);
    assert.equal(response.status, status);
    assert.equal(await response.text(), wire);
    assert.equal(response.headers.get('request-id'), 'local-error');
    await settle();
    assert.equal(forwarded.length, beforeFetch + 1);
    assert.equal(forwarded.at(-1).headers.get('x-api-key'), keyEnv.CODES_KEY_GIVEAWAY);
    assert.equal(env.DB.row(id).spent, before.spent + (status === 413 ? 0 : guess));
    assert.equal(env.DB.row(id).requests, before.requests + 1);
    assert.equal(env.DB.flights.length, 0);
    assert.equal(logs.length, beforeLogs);
  }
  for (const status of [401, 402, 403]) {
    const before = env.DB.row(id), beforeFetch = forwarded.length;
    let calls = 0;
    upstream = async () => calls++ === 0 ? new Response(new ReadableStream({ start(controller) { controller.error(new Error('private refused body error')); } }), { status }) : Response.json({ usage: { input_tokens: 1, output_tokens: 0 } });
    assert.equal((await brain(undefined, {}, '/brain/v1/messages', keyEnv)).status, 200);
    await settle();
    assert.equal(forwarded.length, beforeFetch + 2);
    assert.equal(env.DB.row(id).spent, before.spent + 1);
    assert.equal(env.DB.row(id).requests, before.requests + 1);
    assert.equal(env.DB.flights.length, 0);
  }
  for (const [target, kind] of [[staffLimit, 'staff'], [made, 'giveaway']]) {
    const before = env.DB.row(target.id), beforeFetch = forwarded.length, beforeLogs = logs.length;
    let calls = 0;
    upstream = async () => calls++ === 0 ? new Response('private rejected key', { status: 401 }) : Response.json(refusedReplies[3][1], { status: 400 });
    const response = await brain(undefined, { 'x-api-key': target.code }, '/brain/v1/messages', keyEnv);
    await error(response, 402, { type: 'error', error: { type: 'billing_error', message: 'Capri codes have run out for now.' } });
    await settle();
    assert.deepEqual(forwarded.slice(beforeFetch).map(asked => asked.headers.get('x-api-key')), kind === 'staff' ? [keyEnv.CODES_KEY_STAFF, keyEnv.CODES_KEY_GIVEAWAY] : [keyEnv.CODES_KEY_GIVEAWAY, keyEnv.CODES_KEY_STAFF]);
    assert.equal(env.DB.row(target.id).spent, before.spent);
    assert.equal(env.DB.row(target.id).requests, before.requests + 1);
    assert.equal(env.DB.flights.length, 0);
    assert.deepEqual(logs.slice(beforeLogs), ['codes ' + kind + ' 401', 'codes ' + (kind === 'staff' ? 'giveaway' : 'staff') + ' 400', 'codes all refused']);
  }
  {
    const started = deferred(), reply = deferred();
    const before = env.DB.row(staff.id), beforeFetch = forwarded.length;
    upstream = async () => { started.resolve(); return reply.promise; };
    const running = brain(undefined, { 'x-api-key': staff.code });
    await started.promise;
    assert.equal((await call('/codes/off', 'POST', { id: staff.id }, admin)).status, 200);
    reply.resolve(Response.json(refusedReplies[3][1], { status: 400 }));
    await error(await running, 403, { type: 'error', error: { type: 'permission_error', message: 'That Capri code can no longer start this request.' } });
    await settle();
    assert.equal(forwarded.length, beforeFetch + 1);
    assert.equal(env.DB.row(staff.id).spent, before.spent);
    assert.equal(env.DB.row(staff.id).requests, before.requests + 1);
    assert.equal((await call('/codes/on', 'POST', { id: staff.id }, admin)).status, 200);
  }
  for (const [fields, message] of [
    [{ off: 1 }, 'That Capri code was switched off.'],
    [{ expires: now - 1 }, 'That Capri code has expired.'],
    [{ budget: 1, spent: 1 }, 'That Capri code has used up its budget.'],
    [{ budget: 1 }, 'That Capri code has too little budget left for this request.']
  ]) {
    const fresh = await make({ name: 'Paused staff', kind: 'staff' });
    const reading = deferred(), release = deferred(), before = forwarded.length;
    const heldBody = new Request('https://local.test/brain/v1/messages', { method: 'POST', headers: { 'x-api-key': fresh.code } });
    heldBody.json = async () => { reading.resolve(); await release.promise; return { messages, max_tokens: 100 }; };
    const running = worker.fetch(heldBody, env, ctx);
    await reading.promise;
    env.DB.set(fresh.id, fields);
    release.resolve();
    await error(await running, 403, { type: 'error', error: { type: 'permission_error', message } });
    await settle();
    assert.equal(forwarded.length, before);
    assert.equal(env.DB.row(fresh.id).requests, 0);
  }
  {
    const budget = guess + 10;
    const fresh = await make({ name: 'One reservation', dollars: budget / 1000000 });
    const arrived = deferred(), reply = deferred(), before = forwarded.length;
    upstream = async () => { arrived.resolve(); return reply.promise; };
    const headers = { 'x-api-key': fresh.named };
    const running = brain(undefined, headers);
    await arrived.promise;
    const blocked = Array.from({ length: 4 }, () => brain(undefined, headers));
    for (const response of await Promise.all(blocked))
      await error(response, 403, { type: 'error', error: { type: 'permission_error', message: 'That Capri code has too little budget left for this request.' } });
    assert.equal(forwarded.length, before + 1);
    assert.equal(env.DB.row(fresh.id).spent, 0);
    assert.equal(env.DB.flights.find(flight => flight.code === fresh.id).guess, guess);
    reply.resolve(Response.json({ usage: { input_tokens: 5, output_tokens: 1 } }));
    assert.equal((await running).status, 200);
    await settle();
    assert.equal(env.DB.row(fresh.id).spent, 1);
    assert.equal(env.DB.flights.length, 0);
    upstream = async () => Response.json({ usage: { input_tokens: 1, output_tokens: 0 } });
    assert.equal((await brain(undefined, headers)).status, 200);
    await settle();
    assert.equal(env.DB.row(fresh.id).spent, 2);
  }
  for (const refused of [false, true]) {
    const fetchStarted = deferred(), fetchReady = deferred(), bodyReady = deferred();
    const kept = [];
    let calls = 0;
    upstream = async () => {
      if (refused && calls++ === 0) return Response.json(refusedReplies[3][1], { status: 400 });
      fetchStarted.resolve();
      await fetchReady.promise;
      return new Response(new ReadableStream({ async start(controller) {
        controller.enqueue(new TextEncoder().encode('{"usage":'));
        await bodyReady.promise;
        controller.enqueue(new TextEncoder().encode('{"input_tokens":1000,"output_tokens":1000}}'));
        controller.close();
      } }));
    };
    const beforeHangup = env.DB.row(id), updatesBeforeHangup = env.DB.updates.length, beforeFetch = forwarded.length;
    const unread = brain(undefined, {}, '/brain/v1/messages', keyEnv, { waitUntil(promise) { kept.push(promise); } });
    await fetchStarted.promise;
    assert.equal(kept.length, 1);
    assert.equal(env.DB.updates.length, updatesBeforeHangup);
    assert.equal(env.DB.flights.length, 1);
    assert.equal(env.DB.flights[0].guess, guess);
    assert.deepEqual(forwarded.slice(beforeFetch).map(asked => asked.headers.get('x-api-key')), refused ? [keyEnv.CODES_KEY_GIVEAWAY, keyEnv.CODES_KEY_STAFF] : [keyEnv.CODES_KEY_GIVEAWAY]);
    fetchReady.resolve();
    bodyReady.resolve();
    await Promise.all(kept);
    assert.equal(env.DB.row(id).spent, beforeHangup.spent + 600);
    assert.equal(env.DB.row(id).requests, beforeHangup.requests + 1);
    assert.equal(env.DB.flights.length, 0);
    assert.equal((await unread).bodyUsed, false);
  }

  let held = [], target = 4, arrived = deferred();
  upstream = async () => {
    const next = deferred();
    held.push(next);
    if (held.length === target) arrived.resolve();
    return next.promise;
  };
  const beforeBusy = [forwarded.length, env.DB.updates.length];
  const running = Array.from({ length: 4 }, () => brain());
  await arrived.promise;
  assert.equal(env.DB.flights.length, 4);
  assert.equal(forwarded.length, beforeBusy[0] + 4);
  await refusal(429, 'rate_limit_error', 'That Capri code is busy. Try again in a moment.');
  assert.equal(env.DB.updates.length, beforeBusy[1]);
  held[0].resolve(Response.json({ usage: { input_tokens: 1, output_tokens: 0 } }));
  assert.equal((await running[0]).status, 200);
  assert.equal(env.DB.flights.length, 3);
  target = 5;
  arrived = deferred();
  running.push(brain());
  await arrived.promise;
  assert.equal(env.DB.flights.length, 4);
  for (const next of held.slice(1)) next.resolve(Response.json({ usage: { input_tokens: 1, output_tokens: 0 } }));
  assert.ok((await Promise.all(running)).every(response => response.status === 200));
  await settle();
  assert.equal(env.DB.flights.length, 0);
  assert.equal(env.DB.updates.length, beforeBusy[1] + 5);
  const stale = batch => batch[0].sql.startsWith('UPDATE codes SET spent = spent + (SELECT COALESCE(SUM(guess)') && batch[1].sql === 'DELETE FROM code_flights WHERE started <= ?';
  assert.ok(env.DB.batches.every(batch => batch.length === 4
    ? stale(batch) && batch[2].sql.startsWith('INSERT INTO code_flights')
    : batch.length === 2 && (stale(batch) || (batch[0].sql.startsWith('UPDATE codes SET spent = MAX(0,') && batch[1].sql === 'DELETE FROM code_flights WHERE id = ?'))));

  for (const age of [flightAge, flightAge + 1]) {
    for (let i = 0; i < 4; i++) env.DB.sqlite.prepare('INSERT INTO code_flights (id, code, started) VALUES (?, ?, ?)').run('old-' + i, id, i === 0 ? now - age : now);
    assert.ok(env.DB.flights.every(flight => flight.guess === 0));
    const before = env.DB.row(id);
    upstream = async () => Response.json({ usage: { input_tokens: 0, output_tokens: 0 } });
    assert.equal((await brain()).status, 200);
    await settle();
    assert.equal(env.DB.flights.length, 3);
    assert.ok(!env.DB.flights.some(row => row.id === 'old-0'));
    assert.equal(env.DB.row(id).spent, before.spent);
    assert.equal(env.DB.row(id).requests, before.requests + 2);
    env.DB.sqlite.exec('DELETE FROM code_flights');
  }
  {
    const lost = await make({ name: 'Lost', dollars: 5 }), other = await make({ name: 'Lost Staff', kind: 'staff' }), reaper = await make({ name: 'Reaper', dollars: 5 });
    const started = deferred(), kept = [], savedNow = now;
    let calls = 0;
    upstream = async () => {
      if (++calls === 3) started.resolve();
      return new Promise(() => {});
    };
    const abandoned = [lost.code, lost.code, other.code].map(spelling => brain(undefined, { 'x-api-key': spelling }, '/brain/v1/messages', env, { waitUntil(promise) { kept.push(promise); } }));
    await started.promise;
    assert.equal(abandoned.length, 3);
    assert.equal(kept.length, 3);
    assert.deepEqual(env.DB.flights.map(flight => flight.guess), [guess, guess, guess]);
    upstream = async () => Response.json({ usage: { input_tokens: 1, output_tokens: 0 } });
    now += flightAge - 1;
    assert.equal((await brain(undefined, { 'x-api-key': reaper.code })).status, 200);
    await settle();
    for (const row of [lost, other]) {
      assert.equal(env.DB.row(row.id).spent, 0);
      assert.equal(env.DB.row(row.id).requests, 0);
    }
    assert.equal(env.DB.flights.length, 3);
    now++;
    assert.ok((await Promise.all([brain(undefined, { 'x-api-key': reaper.code }), brain(undefined, { 'x-api-key': staffLimit.code })])).every(response => response.status === 200));
    await settle();
    assert.equal(env.DB.row(lost.id).spent, 2 * guess);
    assert.equal(env.DB.row(lost.id).requests, 2);
    assert.equal(env.DB.row(other.id).spent, guess);
    assert.equal(env.DB.row(other.id).requests, 1);
    assert.equal(env.DB.flights.length, 0);
    assert.equal((await brain(undefined, { 'x-api-key': reaper.code })).status, 200);
    await settle();
    const listed = (await (await call('/codes', 'GET', undefined, admin)).json()).codes;
    assert.equal(listed.find(row => row.id === lost.id).spent, 2 * guess / 1000000);
    assert.equal(listed.find(row => row.id === lost.id).requests, 2);
    assert.equal(listed.find(row => row.id === other.id).spent, guess / 1000000);
    assert.equal(listed.find(row => row.id === other.id).requests, 1);
    now = savedNow;
  }
  {
    const unread = await make({ name: 'Unread', dollars: 5 }), started = deferred(), kept = [], savedNow = now;
    upstream = async () => { started.resolve(); return new Promise(() => {}); };
    brain(undefined, { 'x-api-key': unread.code }, '/brain/v1/messages', env, { waitUntil(promise) { kept.push(promise); } });
    await started.promise;
    const read = async () => (await (await call('/codes', 'GET', undefined, admin)).json()).codes.find(row => row.id === unread.id);
    now += flightAge - 1;
    assert.equal((await read()).spent, 0);
    assert.equal(env.DB.flights.length, 1);
    now++;
    const listed = await read();
    assert.equal(listed.spent, guess / 1000000);
    assert.equal(listed.left, (5000000 - guess) / 1000000);
    assert.equal(listed.requests, 1);
    assert.equal(env.DB.flights.length, 0);
    assert.equal(kept.length, 1);
    now = savedNow;
  }
  for (const [status, usage, cost] of [[200, { input_tokens: 1, output_tokens: 0 }, 1], [200, { input_tokens: 1000, output_tokens: 1000 }, 600], [500, { input_tokens: 1000000 }, guess], [502, null, guess]]) {
    const late = await make({ name: 'Late', dollars: 5 }), started = deferred(), ready = deferred(), kept = [], savedNow = now;
    upstream = async () => { started.resolve(); return ready.promise; };
    const finishing = brain(undefined, { 'x-api-key': late.code }, '/brain/v1/messages', env, { waitUntil(promise) { kept.push(promise); } });
    await started.promise;
    now += flightAge + 1;
    upstream = async () => Response.json({ usage: { input_tokens: 0, output_tokens: 0 } });
    assert.equal((await brain()).status, 200);
    await settle();
    assert.equal(env.DB.row(late.id).spent, guess);
    assert.equal(env.DB.row(late.id).requests, 1);
    assert.equal(env.DB.flights.length, 0);
    if (status === 502) {
      ready.reject(new Error('private-late-fetch-error'));
    }
    else ready.resolve(Response.json({ usage }, { status }));
    assert.equal((await finishing).status, status);
    await Promise.all(kept);
    assert.equal(env.DB.row(late.id).spent, cost);
    assert.equal(env.DB.row(late.id).requests, 1);
    assert.equal(env.DB.row(late.id).used, savedNow);
    assert.equal(env.DB.flights.length, 0);
    now = savedNow;
  }
  {
    const late = await make({ name: 'Late Pair', dollars: 5 }), started = deferred(), ready = [deferred(), deferred()], kept = [], savedNow = now;
    let calls = 0;
    upstream = async () => {
      const next = ready[calls++];
      if (calls === 2) started.resolve();
      return next.promise;
    };
    const finishing = Array.from({ length: 2 }, () => brain(undefined, { 'x-api-key': late.code }, '/brain/v1/messages', env, { waitUntil(promise) { kept.push(promise); } }));
    await started.promise;
    now += flightAge;
    upstream = async () => Response.json({ usage: { input_tokens: 0, output_tokens: 0 } });
    await Promise.all([brain(), brain(undefined, { 'x-api-key': staffLimit.code })]);
    await settle();
    assert.equal(env.DB.row(late.id).spent, 2 * guess);
    assert.equal(env.DB.row(late.id).requests, 2);
    ready[0].resolve(Response.json({ usage: { input_tokens: 1, output_tokens: 0 } }));
    ready[1].resolve(Response.json({ usage: { input_tokens: 1000, output_tokens: 1000 } }));
    assert.ok((await Promise.all(finishing)).every(response => response.status === 200));
    await Promise.all(kept);
    assert.equal(env.DB.row(late.id).spent, 601);
    assert.equal(env.DB.row(late.id).requests, 2);
    assert.equal(env.DB.flights.length, 0);
    now = savedNow;
  }
  for (const status of [200, 400, 429, 529, 302]) {
    const errorBody = '{ "usage": {"input_tokens": 1000000}, "error": "Try later." }';
    upstream = async () => new Response(errorBody, { status, statusText: 'Local upstream', headers: { 'Content-Type': 'application/json', 'request-id': 'error-request', 'retry-after': '9', 'access-control-allow-origin': '*', 'content-length': '1', 'content-encoding': 'gzip', 'transfer-encoding': 'chunked', 'anthropic-organization-id': 'private-organization', 'anthropic-workspace-id': 'private-workspace', 'anthropic-ratelimit-requests-limit': '100', 'anthropic-ratelimit-tokens-remaining': '999', 'set-cookie': 'private-cookie', server: 'private-server', 'cache-control': 'public', 'capri-code-left': 'private-left', 'capri-code-expires': 'private-expires' } });
    const before = env.DB.row(id);
    const response = await brain();
    assert.equal(response.status, status);
    assert.equal(response.statusText, 'Local upstream');
    assert.equal(await response.text(), errorBody);
    assert.deepEqual(Object.fromEntries(response.headers), { 'cache-control': 'no-store', 'capri-code-expires': new Date(before.expires).toISOString(), 'capri-code-left': ((before.budget - before.spent) / 1000000).toFixed(2), 'content-type': 'application/json', 'request-id': 'error-request', 'retry-after': '9' });
    noCors(response);
    await settle();
    assert.equal(env.DB.row(id).spent, before.spent + guess);
    assert.equal(env.DB.row(id).requests, before.requests + 1);
    assert.equal(env.DB.flights.length, 0);
  }
  for (const status of [201, 204, 205, 304]) {
    const bytes = status === 201 ? new TextEncoder().encode('{"usage":{"input_tokens":1,"output_tokens":0}}') : null;
    upstream = async () => new Response(bytes, { status, headers: { server: 'private-server' } });
    const before = env.DB.row(id), response = await brain();
    assert.equal(response.status, status);
    assert.deepEqual(Object.fromEntries(response.headers), { 'cache-control': 'no-store', 'capri-code-expires': new Date(before.expires).toISOString(), 'capri-code-left': ((before.budget - before.spent) / 1000000).toFixed(2) });
    assert.equal(await response.text(), status === 201 ? '{"usage":{"input_tokens":1,"output_tokens":0}}' : '');
    await settle();
    assert.equal(env.DB.row(id).spent, before.spent + (status === 201 ? 1 : guess));
    assert.equal(env.DB.row(id).requests, before.requests + 1);
    assert.equal(env.DB.flights.length, 0);
  }
  for (const failure of ['fetch', 'body']) {
    upstream = async () => {
      if (failure === 'fetch') throw new Error('private-fetch-error');
      return new Response(new ReadableStream({ pull(controller) { controller.error(new Error('private-body-error')); } }));
    };
    const before = env.DB.row(id), beforeFetch = forwarded.length, beforeLogs = logs.length;
    const response = await brain(undefined, {}, '/brain/v1/messages', keyEnv);
    await error(response, 502, { type: 'error', error: { type: 'api_error', message: "Couldn't reach Claude. Try again." } });
    await settle();
    assert.equal(env.DB.row(id).spent, before.spent + guess);
    assert.equal(env.DB.row(id).requests, before.requests + 1);
    assert.equal(env.DB.flights.length, 0);
    assert.equal(forwarded.length, beforeFetch + 1);
    assert.equal(logs.length, beforeLogs);
  }

  env.DB.set(staff.id, { spent: 500000000 });
  held = [];
  target = 6;
  arrived = deferred();
  upstream = async () => {
    const next = deferred();
    held.push(next);
    if (held.length === target) arrived.resolve();
    return next.promise;
  };
  for (let i = 0; i < 5; i++) env.DB.sqlite.prepare('INSERT INTO code_flights (id, code, started) VALUES (?, ?, ?)').run('staff-' + i, staff.id, now);
  const staffRunning = Array.from({ length: 6 }, () => brain(undefined, { 'x-api-key': staff.code }));
  await arrived.promise;
  assert.equal(env.DB.flights.length, 11);
  for (const next of held) next.resolve(Response.json({ usage: { input_tokens: 10, output_tokens: 4 } }));
  for (const response of await Promise.all(staffRunning)) {
    assert.equal(response.status, 200);
    assert.equal(response.headers.get('capri-code-left'), '');
  }
  await settle();
  assert.equal(env.DB.row(staff.id).spent, 500000018);
  const staffList = await (await call('/codes', 'GET', undefined, admin)).json();
  assert.equal(staffList.codes.find(row => row.id === staff.id).left, null);
  env.DB.sqlite.exec('DELETE FROM code_flights');
  env.DB.set(staffLimit.id, { spent: 20000000 });
  await refusal(403, 'permission_error', 'That Capri code has used up its budget.', { 'x-api-key': staffLimit.code });
  env.DB.set(staffLimit.id, { spent: 0 });
  for (let i = 0; i < 4; i++) env.DB.sqlite.prepare('INSERT INTO code_flights (id, code, started) VALUES (?, ?, ?)').run('limited-staff-' + i, staffLimit.id, now);
  await refusal(429, 'rate_limit_error', 'That Capri code is busy. Try again in a moment.', { 'x-api-key': staffLimit.code });
  env.DB.sqlite.exec('DELETE FROM code_flights');

  for (const keys of [{ CODES_KEY_STAFF: '' }, { CODES_KEY_GIVEAWAY: '' }, { CODES_KEY_STAFF: env.CODES_KEY_GIVEAWAY }])
    for (const target of [made, staff])
      await refusal(503, 'api_error', "Capri codes aren't ready.", { 'x-api-key': target.code }, undefined, { ...env, ...keys });

  env.DB.set(id, { budget: 5000000, spent: 4999999 });
  await refusal(403, 'permission_error', 'That Capri code has too little budget left for this request.');
  env.DB.set(id, { spent: 5000000 });
  await refusal(403, 'permission_error', 'That Capri code has used up its budget.');
  upstream = async () => Response.json({ usage: { input_tokens: 1000, output_tokens: 1000 } });
  for (const dollars of [10, 5.000599, 1, 0.0000005, 10.0000006]) {
    const before = env.DB.row(id), budget = Math.round(dollars * 1000000);
    const response = await call('/codes/limit', 'POST', { id, dollars }, admin);
    assert.equal(response.status, 200);
    assert.deepEqual(await response.json(), { id, dollars: budget / 1000000, spent: before.spent / 1000000, left: Math.max(0, budget - before.spent) / 1000000 });
    assert.deepEqual(env.DB.row(id), { ...before, budget });
    if (budget <= before.spent) await refusal(403, 'permission_error', 'That Capri code has used up its budget.');
  }
  assert.equal((await brain()).status, 200);
  await settle();
  for (const dollars of [10, 0, 5, 0]) {
    const before = env.DB.row(staff.id), budget = dollars * 1000000;
    const response = await call('/codes/limit', 'POST', { id: staff.id, dollars }, admin);
    assert.equal(response.status, 200);
    assert.deepEqual(await response.json(), { id: staff.id, dollars, spent: before.spent / 1000000, left: dollars ? 0 : null });
    assert.deepEqual(env.DB.row(staff.id), { ...before, budget });
    if (dollars) await refusal(403, 'permission_error', 'That Capri code has used up its budget.', { 'x-api-key': staff.code });
    else assert.equal((await brain(undefined, { 'x-api-key': staff.named })).headers.get('capri-code-left'), '');
    await settle();
  }
  env.DB.set(id, { expires: now - 1 });
  await refusal(403, 'permission_error', 'That Capri code has expired.');
  for (const body of [{ days: 30 }, { never: true }, { days: 0.5 }]) {
    const before = env.DB.row(id), expires = body.never ? 0 : now + body.days * 86400000;
    const response = await call('/codes/days', 'POST', { id, ...body }, admin);
    assert.equal(response.status, 200);
    assert.deepEqual(await response.json(), { id, expires });
    assert.deepEqual(env.DB.row(id), { ...before, expires });
    assert.equal((await brain()).headers.get('capri-code-expires'), expires ? new Date(expires).toISOString() : '');
    await settle();
  }
  await call('/codes/off', 'POST', { id }, admin);
  assert.equal((await call('/codes/limit', 'POST', { id, dollars: 11 }, admin)).status, 200);
  assert.equal((await call('/codes/days', 'POST', { id, never: true }, admin)).status, 200);
  await refusal(403, 'permission_error', 'That Capri code was switched off.');
  await call('/codes/on', 'POST', { id }, admin);

  for (const [usage, cost] of [
    [undefined, guess], [null, guess],
    [{ input_tokens: null, output_tokens: null }, guess],
    [{ input_tokens: -10, output_tokens: '20' }, guess],
    [{ input_tokens: 0.1, output_tokens: 0.1 }, guess],
    [{ input_tokens: Number.MAX_VALUE, cache_read_input_tokens: Number.MAX_VALUE, cache_creation: { ephemeral_5m_input_tokens: Number.MAX_VALUE, ephemeral_1h_input_tokens: Number.MAX_VALUE }, output_tokens: Number.MAX_VALUE }, guess],
    [{ server_tool_use: { web_search_requests: Number.MAX_VALUE } }, guess],
    [{ iterations: [{ type: 'advisor', input_tokens: Number.MAX_VALUE, output_tokens: Number.MAX_VALUE }] }, guess]
  ]) {
    env.DB.set(staffZero.id, { spent: 0 });
    upstream = async () => Response.json({ usage, content: [] });
    assert.equal((await brain(undefined, { 'x-api-key': staffZero.code })).status, 200);
    await settle();
    assert.equal(env.DB.updates.at(-1).cost, cost);
    assert.ok(Number.isSafeInteger(env.DB.row(staffZero.id).spent));
  }
  env.DB.set(staffZero.id, { spent: 0 });
  upstream = async () => new Response('{"usage":{"input_tokens":1e309,"cache_creation_input_tokens":1e309,"cache_creation":{"ephemeral_5m_input_tokens":1e309,"ephemeral_1h_input_tokens":-1e309},"output_tokens":-1e309,"server_tool_use":{"web_search_requests":1e309}}}');
  await brain(undefined, { 'x-api-key': staffZero.code });
  await settle();
  assert.equal(env.DB.updates.at(-1).cost, guess);

  const full = { ...env, DB: new Database(readdirSync(join(here, 'migrations')).filter(name => name.endsWith('.sql')).sort()) };
  const fullCode = await make({ name: 'Full migrations', dollars: 5 }, full);
  assert.equal((await brain(undefined, { 'x-api-key': fullCode.named }, '/brain/v1/messages', full)).status, 200);
  await settle();
  assert.equal(full.DB.row(fullCode.id).requests, 1);
  assert.equal(full.DB.flights.length, 0);
  full.DB.sqlite.close();
  const finalList = await (await call('/codes', 'GET', undefined, admin)).json();
  for (const spelling of issued) {
    assert.ok(!JSON.stringify(finalList).includes(spelling));
    assert.ok(!JSON.stringify(env.DB.rows).includes(spelling));
    assert.ok(!logs.join('\n').toUpperCase().includes(spelling));
  }
  for (const secret of [env.SALT, env.CODES_SECRET, env.ANTHROPIC_API_KEY, 'local-staff-key', 'local-giveaway-key', 'private']) assert.ok(!logs.join('\n').includes(secret));
  for (const asked of forwarded) assert.ok(!logs.join('\n').includes(asked.headers.get('x-api-key')));
  for (const message of ['You have reached your specified', 'credit balance', 'enforced_spend_limit_reached']) assert.ok(!logs.join('\n').includes(message));
  for (const line of logs) assert.ok(!/[\r\n\u2028\u2029]/.test(line));

  if (process.platform === 'win32') {
    copyFileSync(join(here, 'codes.ps1'), join(room, 'codes.ps1'));
    writeFileSync(join(room, 'check-cli.ps1'), `
$ErrorActionPreference = 'Stop'
$env:CAPRI_CODES_SECRET = 'local-cli-secret'
$global:capriTestSent = $null
$global:capriTestHits = 0
$script:output = ''
function Invoke-RestMethod {
    param($Uri, $Headers, $Method, $ContentType, $Body)
    if ($Headers.Authorization -ne 'Bearer local-cli-secret') { throw 'Bad mock authorisation.' }
    $global:capriTestHits++
    if ($Body -is [byte[]]) { $Body = [Text.Encoding]::UTF8.GetString($Body) }
    if ($Body) { $global:capriTestSent = $Body | ConvertFrom-Json }
    if ($Method -eq 'Get') { return @{ codes = @(@{ id = 1; name = 'Person'; kind = 'staff'; tag = 'PERSON'; dollars = 0; spent = 3; left = $null; expires = 0; used = 0; off = 0; requests = 1 }) } }
    if ($Uri.EndsWith('/limit')) { return @{ id = 1; dollars = $global:capriTestSent.dollars; spent = 3; left = 7 } }
    if ($Uri.EndsWith('/days')) { return @{ id = 1; expires = 0 } }
    return @{ id = 1; code = 'CAPRI-0000-0000-0000-0000'; named = 'CAPRI-PERSON-0000-0000-0000-0000'; name = $global:capriTestSent.name; kind = $global:capriTestSent.kind; dollars = $global:capriTestSent.dollars; expires = 0 }
}
$tool = Join-Path $PSScriptRoot 'codes.ps1'
$out = & $tool new Person -Staff | Out-String
$script:output += $out
if ($global:capriTestSent.kind -ne 'staff' -or $global:capriTestSent.dollars -ne 0 -or $out -notmatch 'staff.*no limit') { throw 'Staff default failed.' }
if ($out.IndexOf('CAPRI-0000-0000-0000-0000') -gt $out.IndexOf('CAPRI-PERSON-0000-0000-0000-0000')) { throw 'Code order failed.' }
$out = & $tool new Person -Staff -NoLimit -Tag person | Out-String
$script:output += $out
if ($global:capriTestSent.dollars -ne 0 -or $global:capriTestSent.tag -ne 'person') { throw 'Staff flags failed.' }
$out = & $tool new Person -Staff -Dollars 10 | Out-String
$script:output += $out
if ($global:capriTestSent.dollars -ne 10 -or $out -notmatch 'staff.*10') { throw 'Staff budget failed.' }
$out = & $tool new Person -Staff -Dollars 0 | Out-String
$script:output += $out
if ($global:capriTestSent.dollars -ne 0 -or $out -notmatch 'no limit') { throw 'Staff zero failed.' }
$out = & $tool new Person | Out-String
$script:output += $out
if ($global:capriTestSent.kind -ne 'giveaway' -or $global:capriTestSent.dollars -ne 5 -or $out -notmatch 'giveaway') { throw 'Giveaway default failed.' }
$out = & $tool list | Out-String -Width 240
$script:output += $out
if ($out -notmatch 'kind' -or $out -notmatch 'tag' -or $out -notmatch 'no limit') { throw 'List failed.' }
$out = & $tool limit 1 -Dollars 10 | Out-String
$script:output += $out
if ($global:capriTestSent.dollars -ne 10 -or $out -notmatch '10.00 limit') { throw 'Limit failed.' }
$out = & $tool limit 1 -NoLimit | Out-String
$script:output += $out
if ($global:capriTestSent.dollars -ne 0 -or $out -notmatch 'no limit') { throw 'NoLimit failed.' }
$out = & $tool days 1 -Days 30 | Out-String
$script:output += $out
if ($global:capriTestSent.days -ne 30) { throw 'Days failed.' }
$out = & $tool days 1 -Never | Out-String
$script:output += $out
if ($global:capriTestSent.never -ne $true -or $global:capriTestSent.PSObject.Properties.Name -contains 'days' -or $out -notmatch 'never ends') { throw 'Never failed.' }
foreach ($run in @({ & $tool new Person -NoLimit }, { & $tool new Person -Dollars 0 }, { & $tool new Person -Tag bad-tag }, { & $tool new Person -Tag ('AB' + [char]10) }, { & $tool days 1 -Days 0 }, { & $tool days 1 }, { & $tool days 1 -Days 0 -Never }, { & $tool limit 1 -Dollars 0 }, { & $tool limit 1 -NoLimit -Dollars 5 })) {
    $before = $global:capriTestHits
    $failed = $false
    try { & $run | Out-Null } catch { $failed = $true; if ($_.ToString().Contains('local-cli-secret')) { throw 'Secret printed.' } }
    if (-not $failed -or $global:capriTestHits -ne $before) { throw 'Bad input reached the mock.' }
}
if ($script:output.Contains('local-cli-secret')) { throw 'Secret printed.' }
`);
    assert.equal(execFileSync('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', join(room, 'check-cli.ps1')], { encoding: 'utf8', timeout: 30000 }).trim(), '');
  }

} finally {
  globalThis.fetch = originalFetch;
  console.error = originalError;
  console.log = originalLog;
  Date.now = originalNow;
  env.DB.sqlite.close();
}

console.log('PASS code admin, tags, staff limits, key failover, request allowlists, safe costs, stale guesses, late charges, disconnect accounting, atomic flight limits, reply headers, migrations and CLI');

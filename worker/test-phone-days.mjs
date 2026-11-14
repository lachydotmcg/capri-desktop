import assert from 'node:assert/strict';
import { copyFileSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { DatabaseSync } from 'node:sqlite';
import { fileURLToPath, pathToFileURL } from 'node:url';

// /phone-day, the daily count from Capri Remote on a phone alone. Real SQLite runs every migration, so a bad query fails here.
const here = dirname(fileURLToPath(import.meta.url));
const room = mkdtempSync(join(tmpdir(), 'capri-phone-days-'));
const sdk = join(room, 'node_modules', '@anthropic-ai', 'sdk');
mkdirSync(sdk, { recursive: true });
writeFileSync(join(sdk, 'package.json'), JSON.stringify({ name: '@anthropic-ai/sdk', type: 'module', main: 'index.js' }));
writeFileSync(join(sdk, 'index.js'), 'export default class Anthropic {}');
writeFileSync(join(room, 'package.json'), JSON.stringify({ type: 'module' }));
copyFileSync(join(here, 'src', 'index.js'), join(room, 'index.js'));
const worker = (await import(pathToFileURL(join(room, 'index.js')).href)).default;
process.on('exit', () => rmSync(room, { recursive: true, force: true }));

const sqlite = new DatabaseSync(':memory:');
for (const file of readdirSync(join(here, 'migrations')).filter(name => name.endsWith('.sql')).sort()) sqlite.exec(readFileSync(join(here, 'migrations', file), 'utf8'));
let broken = false;
const statement = (sql, values = []) => ({
  bind: (...sent) => statement(sql, sent),
  first: async () => sqlite.prepare(sql).get(...values) ?? null,
  all: async () => ({ results: sqlite.prepare(sql).all(...values) }),
  run: async () => { if (broken) throw new Error('D1 is down'); return { meta: { changes: Number(sqlite.prepare(sql).run(...values).changes) } }; }
});
const env = { DB: { prepare: sql => statement(sql) }, SALT: 'salt' };

const send = async (method, body, type = 'text/plain') => {
  const answer = await worker.fetch(new Request('https://worker.test/phone-day', {
    method,
    headers: { 'CF-Connecting-IP': '203.0.113.9', 'User-Agent': 'phone test', 'Content-Type': type, Origin: 'https://capriai.app' },
    body: body === undefined ? undefined : typeof body === 'string' ? body : JSON.stringify(body)
  }), env, { waitUntil: () => {} });
  const text = await answer.text();
  return { status: answer.status, headers: answer.headers, body: text ? JSON.parse(text) : null };
};
const rows = () => sqlite.prepare('SELECT * FROM phone_days ORDER BY day').all().map(row => ({ ...row }));
const today = new Date().toISOString().slice(0, 10);

// The phone's own preflight-free POST, and a browser's preflight for a JSON one, both get the site's CORS.
const preflight = await worker.fetch(new Request('https://worker.test/phone-day', { method: 'OPTIONS', headers: { Origin: 'https://capriai.app', 'Access-Control-Request-Method': 'POST' } }), env, {});
assert.equal(preflight.status, 204);
assert.equal(preflight.headers.get('Access-Control-Allow-Origin'), '*');

// Three phones today: a new one alone, a returning one alone, and a returning one that's also paired with a PC.
let answer = await send('POST', { first: true, paired: false });
assert.equal(answer.status, 200);
assert.deepEqual(answer.body, { ok: true });
assert.equal(answer.headers.get('Access-Control-Allow-Origin'), '*');
assert.equal(answer.headers.get('Cache-Control'), 'no-store');
assert.equal((await send('POST', { first: false, paired: false })).status, 200);
assert.equal((await send('POST', { first: false, paired: true }, 'application/json')).status, 200);
assert.deepEqual(rows(), [{ day: today, phones: 3, first_time: 1, also_paired: 1 }]);

// Bad input changes nothing and gets a 400.
for (const bad of ['', 'not json', '[]', 'null', '7', '{"first":true}', '{"paired":false}', '{"first":"yes","paired":false}', '{"first":1,"paired":0}', '{"first":true,"paired":null}', JSON.stringify({ first: true, paired: false, pad: 'x'.repeat(300) })]) {
  const sent = await send('POST', bad);
  assert.equal(sent.status, 400, 'bad input: ' + bad.slice(0, 40));
  assert.deepEqual(sent.body, { error: 'Bad request' });
  assert.equal(sent.headers.get('Access-Control-Allow-Origin'), '*');
}
assert.deepEqual(rows(), [{ day: today, phones: 3, first_time: 1, also_paired: 1 }]);

// Only POST adds: nothing reads the numbers back over the web.
for (const method of ['GET', 'PUT', 'DELETE']) assert.equal((await send(method, method === 'GET' ? undefined : { first: true, paired: true })).status, 404, method);
assert.deepEqual(rows(), [{ day: today, phones: 3, first_time: 1, also_paired: 1 }]);

// Nothing about the sender is kept: the table has only the day and its three counters.
assert.deepEqual(sqlite.prepare('PRAGMA table_info(phone_days)').all().map(column => column.name), ['day', 'phones', 'first_time', 'also_paired']);
assert.ok(!JSON.stringify(rows()).includes('203.0.113.9'));

// A database that fails says so plainly, with CORS, and the phone never shows it.
broken = true;
answer = await send('POST', { first: true, paired: false });
assert.equal(answer.status, 503);
assert.deepEqual(answer.body, { error: "Couldn't count that." });
assert.equal(answer.headers.get('Access-Control-Allow-Origin'), '*');
broken = false;

// A new UTC day gets its own row.
const RealDate = Date;
const later = RealDate.now() + 86400000;
globalThis.Date = class extends RealDate { constructor(...args) { super(...(args.length ? args : [later])); } static now() { return later; } };
const tomorrow = new RealDate(later).toISOString().slice(0, 10);
assert.equal((await send('POST', { first: false, paired: true })).status, 200);
globalThis.Date = RealDate;
assert.deepEqual(rows(), [{ day: today, phones: 3, first_time: 1, also_paired: 1 }, { day: tomorrow, phones: 1, first_time: 0, also_paired: 1 }]);

console.log('PASS the phone count adds to today only, keeps nothing about the sender, turns away bad input and other methods, says when D1 fails, and starts a new row each UTC day');

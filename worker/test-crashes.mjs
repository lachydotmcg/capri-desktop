import assert from 'node:assert/strict';
import { copyFileSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { DatabaseSync } from 'node:sqlite';
import { fileURLToPath, pathToFileURL } from 'node:url';

// POST /crash, the opt-in crash report from Capri desktop, and the crash blocks in /stats. Real SQLite runs every migration, so a bad query fails here.
const here = dirname(fileURLToPath(import.meta.url));
const room = mkdtempSync(join(tmpdir(), 'capri-crashes-'));
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
  sql,
  bind: (...sent) => statement(sql, sent),
  first: async () => sqlite.prepare(sql).get(...values) ?? null,
  all: async () => ({ results: sqlite.prepare(sql).all(...values) }),
  run: async () => { if (broken) throw new Error('D1 is down'); return { meta: { changes: Number(sqlite.prepare(sql).run(...values).changes) } }; }
});
const env = {
  DB: {
    prepare: sql => statement(sql),
    batch: async list => Promise.all(list.map(item => /^\s*SELECT/i.test(item.sql) ? item.all() : item.run()))
  },
  SALT: 'salt',
  STATS_KEY: 'stats-secret'
};
const waits = [];
const ctx = { waitUntil: promise => { waits.push(promise); } };

const send = async (body, ip = '203.0.113.9', method = 'POST') => {
  const answer = await worker.fetch(new Request('https://worker.test/crash', {
    method,
    headers: { 'CF-Connecting-IP': ip, 'Content-Type': 'text/plain', Origin: 'https://capriai.app' },
    body: method !== 'POST' || body === undefined ? undefined : typeof body === 'string' ? body : JSON.stringify(body)
  }), env, ctx);
  const text = await answer.text();
  return { status: answer.status, headers: answer.headers, body: text ? JSON.parse(text) : null };
};
const stats = async query => {
  const answer = await worker.fetch(new Request('https://worker.test/stats' + query, { headers: { Authorization: 'Bearer stats-secret' } }), env, ctx);
  return { status: answer.status, body: await answer.json() };
};
const rows = () => sqlite.prepare('SELECT day, version, platform, os, store, sig, lines FROM crashes ORDER BY id').all().map(row => ({ ...row }));
const crashNumber = Math.floor(Date.now() / 86400000);
const today = new Date(crashNumber * 86400000).toISOString().slice(0, 10);

const report = {
  version: '0.9.4',
  platform: 'windows',
  os: 'Windows 10.0.26100',
  store: false,
  lines: ['2026-11-19 10:00:00 | crash | -1 | NullReferenceException: Object reference at 0xbeef', '2026-11-19 10:00:01 | info | 0 | started']
};
const sig = '| crash | | NullReferenceException: Object reference at';

sqlite.exec(`INSERT INTO crashes (day, version, platform, os, store, sig, lines) VALUES ('2020-01-01', '0.1.0', 'mac', 'macOS', 0, 'old', 'old')`);
sqlite.exec(`INSERT INTO crash_taps (key, day, count) VALUES ('old-tap', ${crashNumber - 2}, 1)`);

// A good report is stored with its signature, and the old rows are cleared.
let answer = await send(report);
assert.equal(answer.status, 200);
assert.deepEqual(answer.body, { ok: true });
assert.equal(answer.headers.get('Access-Control-Allow-Origin'), '*');
assert.equal(answer.headers.get('Cache-Control'), 'no-store');
assert.deepEqual(rows(), [{ day: today, version: '0.9.4', platform: 'windows', os: 'Windows 10.0.26100', store: 0, sig, lines: report.lines.join('\n') }]);
await Promise.all(waits.splice(0));
assert.equal(sqlite.prepare('SELECT COUNT(*) AS n FROM crash_taps WHERE day < ?').get(crashNumber).n, 0, 'old tap rows are cleared');

// Each bad field gets a 400 and stores nothing.
const bad = [
  '', 'not json', '[]', 'null', '7',
  JSON.stringify({ ...report, version: 'v0.9.4' }),
  JSON.stringify({ ...report, version: '1.1.1.1111111111111111111' }),
  JSON.stringify({ ...report, version: undefined }),
  JSON.stringify({ ...report, platform: 'android' }),
  JSON.stringify({ ...report, platform: 'Windows' }),
  JSON.stringify({ ...report, os: 'x'.repeat(61) }),
  JSON.stringify({ ...report, os: 7 }),
  JSON.stringify({ ...report, store: 'no' }),
  JSON.stringify({ ...report, store: undefined }),
  JSON.stringify({ ...report, lines: [] }),
  JSON.stringify({ ...report, lines: 'text' }),
  JSON.stringify({ ...report, lines: Array(31).fill('line') }),
  JSON.stringify({ ...report, lines: ['x'.repeat(401)] }),
  JSON.stringify({ ...report, lines: [5] }),
  JSON.stringify({ ...report, pad: 'x'.repeat(8000) })
];
for (const body of bad) {
  const sent = await send(body);
  assert.equal(sent.status, 400, 'bad input: ' + body.slice(0, 60));
  assert.deepEqual(sent.body, { error: 'Bad request' });
  assert.equal(sent.headers.get('Access-Control-Allow-Origin'), '*');
}
assert.equal(rows().length, 1);

// Only POST stores: other methods are 404 and store nothing.
for (const method of ['GET', 'PUT', 'DELETE']) assert.equal((await send(undefined, '203.0.113.9', method)).status, 404, method);
assert.equal(rows().length, 1);

// Five a day per address, the sixth is refused, and a different address still gets through. Bad input never counted.
for (let i = 0; i < 4; i++) assert.equal((await send(report)).status, 200);
answer = await send(report);
assert.equal(answer.status, 429);
assert.deepEqual(answer.body, { error: 'Too many reports today.' });
assert.equal(rows().length, 5);
assert.equal((await send({ ...report, version: '0.9.3', platform: 'linux', store: true, lines: ['2026-11-19 12:00:00 | closed | 4 | Unexpected exit'] }, '203.0.113.10')).status, 200);
assert.equal(rows().length, 6);
assert.ok(!JSON.stringify(sqlite.prepare('SELECT * FROM crash_taps').all()).includes('203.0.113'), 'no address is kept');

// A database that fails says so plainly, with CORS, and stores nothing.
broken = true;
answer = await send(report, '203.0.113.11');
assert.equal(answer.status, 503);
assert.deepEqual(answer.body, { error: "Couldn't save that." });
assert.equal(answer.headers.get('Access-Control-Allow-Origin'), '*');
broken = false;
assert.equal(rows().length, 6);

// Stats with the stats key shows the crash blocks, and keeps the old fields.
const shown = await stats('?days=30');
assert.equal(shown.status, 200);
assert.deepEqual(shown.body.crashes.days, [
  { day: today, version: '0.9.3', platform: 'linux', count: 1 },
  { day: today, version: '0.9.4', platform: 'windows', count: 5 }
]);
assert.deepEqual(shown.body.crashes.top, [
  { sig, version: '0.9.4', count: 5, last_day: today },
  { sig: '| closed | | Unexpected exit', version: '0.9.3', count: 1, last_day: today }
]);
assert.equal(shown.body.crashes.latest.length, 6);
assert.deepEqual(shown.body.crashes.latest[0], { day: today, version: '0.9.3', platform: 'linux', os: 'Windows 10.0.26100', store: true, lines: ['2026-11-19 12:00:00 | closed | 4 | Unexpected exit'] });
assert.equal(shown.body.crashes.latest[5].store, false);
assert.deepEqual(shown.body.crashes.latest[5].lines, report.lines);
assert.ok('phone_days' in shown.body && 'codes' in shown.body && 'counters' in shown.body);
assert.equal((await worker.fetch(new Request('https://worker.test/stats?days=30', { headers: { Authorization: 'Bearer nope' } }), env, ctx)).status, 401);
console.log('crashes ok');

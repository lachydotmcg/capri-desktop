import assert from 'node:assert/strict';
import { copyFileSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { DatabaseSync } from 'node:sqlite';
import { fileURLToPath, pathToFileURL } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const room = mkdtempSync(join(tmpdir(), 'capri-stats-'));
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
const written = [];
const statement = (sql, values = []) => ({
  sql,
  bind: (...sent) => statement(sql, sent),
  first: async () => sqlite.prepare(sql).get(...values) ?? null,
  all: async () => ({ results: sqlite.prepare(sql).all(...values) }),
  run: async () => { written.push(sql); return { meta: { changes: Number(sqlite.prepare(sql).run(...values).changes) } }; }
});
const env = {
  DB: {
    prepare: sql => statement(sql),
    batch: async list => Promise.all(list.map(item => /^\s*SELECT/i.test(item.sql) ? item.all() : (written.push(item.sql), item.run())))
  },
  SALT: 'salt',
  CODES_SECRET: 'admin'
};
const call = (path, method = 'GET', headers = {}, body) => worker.fetch(new Request('https://local.test' + path, { method, headers, ...(body === undefined ? {} : { body: JSON.stringify(body) }) }), env, { waitUntil() {} });

const today = Math.floor(Date.now() / 86400000);
const day = n => new Date((today - n) * 86400000).toISOString().slice(0, 10);
sqlite.exec(`INSERT INTO codes (hash, name, kind, tag, budget, spent, expires, created, requests, used) VALUES ('secret-hash-1', 'Sam', 'staff', 'SAM', 0, 2500000, 0, 1, 12, 1700000000000), ('secret-hash-2', 'Winner', 'giveaway', '', 5000000, 1000000, 0, 1, 3, 0)`);
sqlite.exec(`UPDATE downloads SET count = 42; UPDATE stars SET count = 7`);
sqlite.exec(`INSERT INTO phone_days VALUES ('${day(0)}', 5, 2, 1), ('${day(100)}', 9, 9, 9)`);
sqlite.exec(`INSERT INTO help_days VALUES (${today}, 4), (${-today}, 6)`);
sqlite.exec(`INSERT INTO free_days (day, key, count, neurons, held) VALUES (${today}, 'pool', 0, 10, 0), (${today}, 'a', 3, 0, 0), (${today}, 'b', 2, 0, 0)`);
sqlite.exec(`INSERT INTO mod_downloads VALUES ('cat-ears', 11)`);
sqlite.exec(`INSERT INTO mod_ratings VALUES ('cat-ears', 'v1', 4), ('cat-ears', 'v2', 5)`);

assert.equal((await call('/stats')).status, 401, 'no key is set yet');
assert.equal((await call('/stats', 'GET', { Authorization: 'Bearer anything' })).status, 401);
assert.equal((await call('/codes/stats-key', 'POST', { Authorization: 'Bearer wrong' }, {})).status, 401, 'only the admin secret mints');

const minted = await (await call('/codes/stats-key', 'POST', { Authorization: 'Bearer admin' }, {})).json();
assert.match(minted.key, /^CAPRI-STATS-[0-9a-f]{48}$/);
assert.ok(!JSON.stringify(sqlite.prepare('SELECT * FROM stats_key').all()).includes(minted.key), 'only a hash is kept');
const key = { Authorization: 'Bearer ' + minted.key };

assert.equal((await call('/stats', 'GET', { Authorization: 'Bearer admin' })).status, 401, 'the admin secret is not a stats key');
assert.equal((await call('/stats', 'POST', key)).status, 404);
assert.equal((await call('/codes', 'GET', key)).status, 401, 'the stats key cannot list or change codes');
assert.equal((await call('/codes/off', 'POST', key, { id: 1 })).status, 401);

written.length = 0;
const answer = await call('/stats?days=30', 'GET', key);
assert.equal(answer.status, 200);
assert.equal(answer.headers.get('Cache-Control'), 'no-store');
const body = await answer.json();
assert.deepEqual(written, [], 'reading stats writes nothing');
assert.ok(!JSON.stringify(body).includes('secret-hash'), 'no code hash leaves');
assert.equal(body.days, 30);
assert.deepEqual(body.counters, { site_downloads: 42, site_stars: 7 });
assert.deepEqual(body.phone_days, [{ day: day(0), phones: 5, first_time: 2, also_paired: 1 }]);
assert.deepEqual(body.help_days.map(row => [row.place, row.count]).sort(), [['discord', 6], ['site', 4]]);
assert.deepEqual(body.free_days, [{ day: day(0), phones: 2, messages: 5 }]);
assert.deepEqual(body.mods, [{ folder: 'cat-ears', installs: 11, rating: 4.5, votes: 2 }]);
const sam = body.codes.find(row => row.name === 'Sam');
assert.deepEqual({ spent: sam.spent, left: sam.left, requests: sam.requests, kind: sam.kind }, { spent: 2.5, left: null, requests: 12, kind: 'staff' });
assert.equal(body.codes.find(row => row.name === 'Winner').left, 4);
assert.equal((await call('/stats?days=9999', 'GET', key).then(r => r.json())).days, 60);

const rotated = await (await call('/codes/stats-key', 'POST', { Authorization: 'Bearer admin' }, {})).json();
assert.notEqual(rotated.key, minted.key);
assert.equal((await call('/stats', 'GET', key)).status, 401, 'an old key stops working once rotated');
assert.equal((await call('/stats', 'GET', { Authorization: 'Bearer ' + rotated.key })).status, 200);
assert.deepEqual(await (await call('/codes/stats-key', 'POST', { Authorization: 'Bearer admin' }, { off: true })).json(), { off: true });
assert.equal((await call('/stats', 'GET', { Authorization: 'Bearer ' + rotated.key })).status, 401, 'off removes the key');
console.log('stats ok');

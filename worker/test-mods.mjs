import assert from 'node:assert/strict';
import { copyFileSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { DatabaseSync } from 'node:sqlite';
import { fileURLToPath, pathToFileURL } from 'node:url';

// The mods list and how much of D1 it costs. Real SQLite runs the real migrations here, so a bad query fails this.
const here = dirname(fileURLToPath(import.meta.url));
const room = mkdtempSync(join(tmpdir(), 'capri-mods-'));
const sdk = join(room, 'node_modules', '@anthropic-ai', 'sdk');
mkdirSync(sdk, { recursive: true });
writeFileSync(join(sdk, 'package.json'), JSON.stringify({ name: '@anthropic-ai/sdk', type: 'module', main: 'index.js' }));
writeFileSync(join(sdk, 'index.js'), 'export default class Anthropic {}');
writeFileSync(join(room, 'package.json'), JSON.stringify({ type: 'module' }));
copyFileSync(join(here, 'src', 'index.js'), join(room, 'index.js'));
const load = async name => (await import(pathToFileURL(join(room, 'index.js')).href + '?' + name)).default;
const worker = await load('first');
process.on('exit', () => rmSync(room, { recursive: true, force: true }));

let indexUp = true;
const asked = [];
globalThis.fetch = async url => {
  asked.push(String(url));
  return indexUp ? Response.json({ format: 1, mods: [{ folder: 'far-names', kind: 'settings' }, { folder: 'other', kind: 'settings' }, { folder: 'btd6-strategy', kind: 'skill' }, { folder: 'brand-new', kind: 'code' }] }) : new Response('slow down', { status: 429 });
};

const database = migrations => {
  const sqlite = new DatabaseSync(':memory:');
  for (const file of migrations) sqlite.exec(readFileSync(join(here, 'migrations', file), 'utf8'));
  const ran = [];
  const statement = (sql, values = []) => ({
    bind: (...sent) => statement(sql, sent),
    first: async () => { ran.push(sql); return sqlite.prepare(sql).get(...values) ?? null; },
    all: async () => { ran.push(sql); return { results: sqlite.prepare(sql).all(...values) }; },
    run: async () => { ran.push(sql); return { meta: { changes: Number(sqlite.prepare(sql).run(...values).changes) } }; }
  });
  return { DB: { prepare: sql => statement(sql) }, SALT: 'salt', ran, sqlite };
};

const send = async (env, method, path, body, visitor = '203.0.113.7', target = worker) => {
  const waits = [];
  const answer = await target.fetch(new Request('https://worker.test' + path, {
    method,
    headers: { 'CF-Connecting-IP': visitor, 'Content-Type': 'application/json' },
    body: body ? JSON.stringify(body) : undefined
  }), env, { waitUntil: work => waits.push(work) });
  await Promise.all(waits);
  return { status: answer.status, body: await answer.json() };
};

const fullRuns = env => env.ran.filter(sql => sql.startsWith('SELECT f.folder') && !sql.includes('WHERE')).length;

const all = ['0001_reactions.sql', '0006_mods.sql', '0008_mod_stats.sql'];
const env = database(all);
env.sqlite.exec("INSERT INTO mod_downloads VALUES ('far-names', 7); INSERT INTO mod_ratings VALUES ('far-names', 'a', 5), ('far-names', 'b', 3), ('other', 'a', 4)");

// A thousand opens of the Mods window run the full query once, and everyone gets the same numbers.
const first = await send(env, 'GET', '/mods');
assert.equal(first.status, 200);
assert.deepEqual(first.body.mods.find(mod => mod.folder === 'far-names'), { folder: 'far-names', downloads: 7, rating: 4, votes: 2 });
assert.deepEqual(first.body.mods.find(mod => mod.folder === 'other'), { folder: 'other', downloads: 0, rating: 4, votes: 1 });
for (let n = 0; n < 1000; n++) assert.deepEqual((await send(env, 'GET', '/mods')).body, first.body);
assert.equal(fullRuns(env), 1, 'Opens ran the full query instead of reading the stored copy.');
assert.equal(env.ran.filter(sql => sql.startsWith('SELECT at, body')).length, 1001);

// An install counts once a day per person and reads nothing back.
env.ran.length = 0;
const install = await send(env, 'POST', '/mods/download', { folder: 'far-names' });
assert.deepEqual(install, { status: 200, body: { ok: true } });
assert.deepEqual(env.ran.filter(sql => sql.startsWith('SELECT')), ['SELECT 1 FROM mod_downloads WHERE folder = ?'], 'An install read more than its one lookup.');
await send(env, 'POST', '/mods/download', { folder: 'far-names' });
assert.equal(env.sqlite.prepare("SELECT count FROM mod_downloads WHERE folder = 'far-names'").get().count, 8);
await send(env, 'POST', '/mods/download', { folder: 'far-names' }, '203.0.113.8');
assert.equal(env.sqlite.prepare("SELECT count FROM mod_downloads WHERE folder = 'far-names'").get().count, 9);

// A rating answers with that mod's fresh numbers, since the app shows them straight away.
const rated = await send(env, 'POST', '/mods/rate', { folder: 'far-names', stars: 2 }, '203.0.113.9');
assert.deepEqual(rated, { status: 200, body: { folder: 'far-names', downloads: 9, rating: 10 / 3, votes: 3 } });

// Skill mods can be rated and counted like the other kinds.
assert.equal((await send(env, 'POST', '/mods/rate', { folder: 'btd6-strategy', stars: 5 })).status, 200);
assert.equal((await send(env, 'POST', '/mods/download', { folder: 'btd6-strategy' })).status, 200);

// A stale copy refreshes once, and the list catches up.
env.ran.length = 0;
env.sqlite.prepare('UPDATE mod_stats SET at = ?').run(Date.now() - 600001);
const later = await send(env, 'GET', '/mods');
assert.deepEqual(later.body.mods.find(mod => mod.folder === 'far-names'), { folder: 'far-names', downloads: 9, rating: 10 / 3, votes: 3 });
await send(env, 'GET', '/mods');
assert.equal(fullRuns(env), 1, 'A stale copy was not refreshed exactly once.');

// Before the migration runs, the list still works the old way.
const old = database(['0001_reactions.sql', '0006_mods.sql']);
old.sqlite.exec("INSERT INTO mod_downloads VALUES ('far-names', 2)");
assert.deepEqual((await send(old, 'GET', '/mods')).body, { mods: [{ folder: 'far-names', downloads: 2, rating: 0, votes: 0 }] });

// Mods on record never need GitHub, so a fresh worker counts them and takes their ratings while the index is down.
asked.length = 0;
indexUp = false;
const fresh = await load('index-down');
assert.equal((await send(env, 'POST', '/mods/download', { folder: 'far-names' }, '203.0.113.20', fresh)).status, 200);
assert.equal((await send(env, 'POST', '/mods/rate', { folder: 'far-names', stars: 3 }, '203.0.113.20', fresh)).status, 200);
assert.deepEqual(asked, [], 'A mod on record asked GitHub.');

// A mod with nothing on record has to wait for the index, then counts.
assert.equal((await send(env, 'POST', '/mods/download', { folder: 'brand-new' }, '203.0.113.20', fresh)).status, 503);
assert.deepEqual(asked, ['https://raw.githubusercontent.com/lachydotmcg/capri-mods/HEAD/index.json']);
indexUp = true;
assert.equal((await send(env, 'POST', '/mods/download', { folder: 'brand-new' }, '203.0.113.20', fresh)).status, 200);
assert.equal(env.sqlite.prepare("SELECT count FROM mod_downloads WHERE folder = 'brand-new'").get().count, 1);

// A worker that already has the index keeps using it when a refresh fails.
const clock = Date.now;
Date.now = () => clock() + 301000;
indexUp = false;
assert.equal((await send(env, 'POST', '/mods/rate', { folder: 'other', stars: 4 }, '203.0.113.21', fresh)).status, 200);
assert.equal((await send(env, 'POST', '/mods/download', { folder: 'never-heard-of-it' }, '203.0.113.21', fresh)).status, 404);
Date.now = clock;
indexUp = true;

// Refusals don't change.
assert.equal((await send(env, 'POST', '/mods/download', { folder: 'not-listed' })).status, 404);
assert.equal((await send(env, 'POST', '/mods/rate', { folder: 'far-names', stars: 9 })).status, 400);
assert.equal((await send(env, 'POST', '/mods')).status, 404);

console.log('PASS mods list reads one stored row, refreshes when stale, works before the migration, installs read nothing back, ratings answer fresh, skills count, and GitHub trouble costs only brand-new mods');

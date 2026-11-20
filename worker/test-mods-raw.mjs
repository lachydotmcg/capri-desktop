import assert from 'node:assert/strict';
import { copyFileSync, mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

// The app's copy of capri-mods, for networks where raw.githubusercontent.com is blocked or rate limited.
const here = dirname(fileURLToPath(import.meta.url));
const room = mkdtempSync(join(tmpdir(), 'capri-mods-raw-'));
const sdk = join(room, 'node_modules', '@anthropic-ai', 'sdk');
mkdirSync(sdk, { recursive: true });
writeFileSync(join(sdk, 'package.json'), JSON.stringify({ name: '@anthropic-ai/sdk', type: 'module', main: 'index.js' }));
writeFileSync(join(sdk, 'index.js'), 'export default class Anthropic {}');
writeFileSync(join(room, 'package.json'), JSON.stringify({ type: 'module' }));
copyFileSync(join(here, 'src', 'index.js'), join(room, 'index.js'));
const worker = (await import(pathToFileURL(join(room, 'index.js')).href)).default;
process.on('exit', () => rmSync(room, { recursive: true, force: true }));

const commit = 'c'.repeat(40);
const files = {
  [`HEAD/tree.json`]: '{"format":1,"commit":"' + commit + '","tree":[]}',
  [`HEAD/index.json`]: '{"format":1,"mods":[]}',
  [`${commit}/mods/far-names/preview.png`]: 'png bytes'
};
let up = true, status = 503;
const asked = [];
globalThis.fetch = async (url, options) => {
  asked.push({ url: String(url), cf: options && options.cf });
  const key = String(url).replace('https://raw.githubusercontent.com/lachydotmcg/capri-mods/', '');
  if (!up) return new Response('busy', { status });
  return key in files ? new Response(files[key], { headers: { 'Content-Type': key.endsWith('.png') ? 'image/png' : 'text/plain; charset=utf-8' } }) : new Response('404: Not Found', { status: 404 });
};
const get = async (path, method = 'GET') => {
  const answer = await worker.fetch(new Request('https://worker.test' + path, { method }), {}, { waitUntil() {} });
  return { status: answer.status, body: await answer.text(), type: answer.headers.get('Content-Type'), cache: answer.headers.get('Cache-Control'), cors: answer.headers.get('Access-Control-Allow-Origin') };
};

// tree.json and index.json come through as GitHub has them, and a minute's opens ask GitHub once.
const tree = await get('/mods/raw/HEAD/tree.json');
assert.deepEqual([tree.status, tree.body, tree.cors], [200, files['HEAD/tree.json'], '*']);
for (let n = 0; n < 50; n++) assert.equal((await get('/mods/raw/HEAD/tree.json')).body, files['HEAD/tree.json']);
assert.equal(asked.filter(each => each.url.endsWith('/HEAD/tree.json')).length, 1, 'Opens within a minute asked GitHub again.');
assert.equal((await get('/mods/raw/HEAD/index.json')).body, files['HEAD/index.json']);

// A file at a commit never changes, so Cloudflare keeps it a day.
asked.length = 0;
const picture = await get(`/mods/raw/${commit}/mods/far-names/preview.png`);
assert.deepEqual([picture.status, picture.body, picture.type, picture.cache], [200, 'png bytes', 'image/png', 'public, max-age=86400, immutable']);
assert.deepEqual(asked, [{ url: `https://raw.githubusercontent.com/lachydotmcg/capri-mods/${commit}/mods/far-names/preview.png`, cf: { cacheTtl: 86400, cacheEverything: true } }]);

// While GitHub is down or rate limiting, the last good tree.json still answers; a file it never had says why.
const clock = Date.now;
Date.now = () => clock() + 61000;
up = false;
assert.equal((await get('/mods/raw/HEAD/tree.json')).body, files['HEAD/tree.json']);
const down = await get(`/mods/raw/${commit}/mods/far-names/README.md`);
assert.deepEqual([down.status, JSON.parse(down.body).error], [502, 'GitHub said 503']);
up = true;
assert.equal((await get(`/mods/raw/${commit}/mods/far-names/README.md`)).status, 404);
Date.now = clock;

// Only the mods repo's own listing and mod folders come through: no other paths, short commits, escapes, deep folders or POSTs.
asked.length = 0;
for (const path of ['/mods/raw/HEAD/README.md', '/mods/raw/main/tree.json', '/mods/raw/abc123/tree.json', '/mods/raw/HEAD/mods/../tools/make-tree.py', '/mods/raw/HEAD/mods/a/b/c.png',
  '/mods/raw/HEAD/mods/..x/a.png', '/mods/raw/HEAD/.github/workflows/mods-tree.yml', '/mods/raw/' + commit + '/tools/make-tree.py', '/mods/raw/HEAD/mods/-a/b.png'])
  assert.equal((await get(path)).status, 404, path);
assert.equal((await get('/mods/raw/HEAD/tree.json', 'POST')).status, 404);
assert.deepEqual(asked, [], 'A refused path still asked GitHub.');

console.log('PASS mods raw copy: tree.json and index.json pass through and are kept a minute, commit files are cached a day, the last tree.json answers while GitHub is down, and only listing and mod-folder paths come through');

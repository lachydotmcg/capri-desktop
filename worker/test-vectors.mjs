import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { DatabaseSync } from 'node:sqlite';
import { fileURLToPath, pathToFileURL } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const room = mkdtempSync(join(here, 'vectors-'));
assert.equal(dirname(room), here);
process.on('exit', () => rmSync(room, { recursive: true, force: true }));
const sdk = join(room, 'node_modules', '@anthropic-ai', 'sdk');
mkdirSync(sdk, { recursive: true });
writeFileSync(join(sdk, 'package.json'), JSON.stringify({ name: '@anthropic-ai/sdk', type: 'module', main: 'index.js' }));
writeFileSync(join(sdk, 'index.js'), 'export default class Anthropic { constructor() { this.messages = { create: body => globalThis.claudeCall(body) }; } }');
writeFileSync(join(room, 'package.json'), JSON.stringify({ type: 'module' }));
const source = readFileSync(join(here, 'src', 'index.js'), 'utf8');
writeFileSync(join(room, 'index.js'), source + '\nexport { parse, pick, search, fuse, oddPc, knowledge, vectorTexts, vectorCache };\n');
let serial = 0;
const load = () => import(pathToFileURL(join(room, 'index.js')).href + '?isolate=' + (++serial));
const sources = new Map();
globalThis.fetch = async url => {
  assert.ok(sources.has(String(url)), 'No network in this test: ' + url);
  if (sources.get(String(url)) instanceof Error) throw sources.get(String(url));
  return new Response(sources.get(String(url)));
};
const prompts = [];
globalThis.claudeCall = async body => {
  prompts.push(body.system);
  return { stop_reason: 'end_turn', content: [{ type: 'text', text: 'Stand-in reply.' }] };
};
const logs = [];
const realLog = console.log;
console.log = (...parts) => { if (String(parts[0]).startsWith('Help vectors:')) logs.push(parts.join(' ')); else realLog(...parts); };
const realDate = Date;
let clock = realDate.parse('2026-11-13T01:00:00Z');
globalThis.Date = class extends realDate { constructor(...args) { super(...(args.length ? args : [clock])); } static now() { return clock; } };

const small = '## 11. Her voice\n### Voice choice\nChoose a voice in Mainframe > VOICE. Pick a speaker from the list.\n\n' +
  '## 2. Install on Windows\n### Steps\nDownload the installer. Run setup and follow the steps.\n\n' +
  "### Antivirus\nAn antivirus might quarantine the installer. Check the download source before allowing a flagged file.\n\n" +
  '## 18. Remove Capri\n### Uninstall\nRemove the app from Installed apps.\n\n' +
  Array.from({ length: 43 }, (_, index) => '## ' + (30 + index) + '. Topic ' + index + '\n### Detail ' + index + '\nA separate reference entry numbered ' + index + '.').join('\n\n');
const realNotes = readFileSync(join(here, '..', 'site', 'help', 'knowledge.md'), 'utf8');
const concepts = text => {
  const tests = [/voice|\bvoz\b|brzmienie|timbre/i, /install|setup|download/i, /antivirus|virus|trojan|quarantine/i, /mouse|hacked|black screen|by itself|on its own/i, /warn|smartscreen|publisher/i, /resize|snap|windows/i, /sleep|bedtime/i, /microphone|\bmic\b/i];
  const vector = tests.map(test => test.test(text) ? 1 : 0);
  return [...vector, vector.some(Boolean) ? 0.02 : 1];
};
const databases = [];
const make = async (text = small, table = true) => {
  const mod = await load();
  const sqlite = new DatabaseSync(':memory:');
  databases.push(sqlite);
  for (const file of readdirSync(join(here, 'migrations')).filter(name => name.endsWith('.sql') && (table || name !== '0011_help_vectors.sql')).sort()) sqlite.exec(readFileSync(join(here, 'migrations', file), 'utf8'));
  const state = {}, calls = [], queries = [];
  const statement = (sql, values = []) => {
    const bound = values.map(value => value instanceof ArrayBuffer ? new Uint8Array(value) : value);
    const check = async () => {
      queries.push(sql);
      assert.ok(values.length <= 100, 'D1 allows at most 100 bound values');
      if (sql.includes('help_vectors') && state.dbFail) throw new Error('D1 is down');
      if (sql.startsWith('SELECT hash, vector') && state.readGate) await state.readGate;
    };
    const blob = value => {
      if (!(value instanceof Uint8Array)) return value;
      if (state.blob === 'buffer') return value.buffer.slice(value.byteOffset, value.byteOffset + value.byteLength);
      if (state.blob === 'view') {
        const bytes = new Uint8Array(value.length + 4);
        bytes.set(value, 2);
        return new Int8Array(bytes.buffer, 2, value.length);
      }
      if (state.blob === 'data') return new DataView(value.buffer, value.byteOffset, value.byteLength);
      return Array.from(value);
    };
    const rows = () => sqlite.prepare(sql).all(...bound).map(row => Object.fromEntries(Object.entries(row).map(([key, value]) => [key, blob(value)])));
    return {
      bind: (...sent) => statement(sql, sent),
      first: async () => { await check(); return sqlite.prepare(sql).get(...bound) ?? null; },
      all: async () => { await check(); return { results: sql.startsWith('SELECT hash, vector') && state.vectorRows ? JSON.parse(state.vectorRows) : rows() }; },
      run: async () => { await check(); return { meta: { changes: Number(sqlite.prepare(sql).run(...bound).changes) } }; },
      execute: () => ({ meta: { changes: Number(sqlite.prepare(sql).run(...bound).changes) } })
    };
  };
  const DB = { prepare: sql => statement(sql), batch: async statements => {
    if (state.dbFail) throw new Error('D1 is down');
    if (state.writeFail) throw new Error('D1 write failed');
    if (state.loseClaim) sqlite.exec('UPDATE help_vectors SET pending = 0 WHERE vector IS NULL');
    sqlite.exec('BEGIN');
    try { const result = statements.map(query => query.execute()); sqlite.exec('COMMIT'); return result; }
    catch (error) { sqlite.exec('ROLLBACK'); throw error; }
  } };
  const env = { DB, SALT: 'stand-in salt', ANTHROPIC_API_KEY: 'stand-in', DISCORD_RELAY_SECRET: 'stand-in relay', KNOWLEDGE_URL: 'https://notes.test/' + serial, AI: { run: async (model, body) => {
    assert.equal(model, '@cf/baai/bge-m3');
    assert.deepEqual(Object.keys(body), ['text']);
    calls.push({ text: body.text });
    if (Array.isArray(body.text)) {
      assert.ok(body.text.length > 0 && body.text.length <= 20);
      if (state.noteGate) await state.noteGate;
    } else if (state.queryGate) await state.queryGate;
    if (state.fail) throw state.fail;
    if (state.output) return state.output(body.text);
    const texts = Array.isArray(body.text) ? body.text : [body.text];
    return { data: texts.map(text => !Array.isArray(body.text) && state.queryVector || concepts(text)) };
  } } };
  sources.set(env.KNOWLEDGE_URL, text);
  return { mod, sqlite, state, env, calls, queries };
};
const send = async (test, question, route = '/help', extra = {}, mod = test.mod) => {
  const pending = [];
  const ctx = { waitUntil: work => pending.push(work) };
  const response = await mod.default.fetch(new Request('https://worker.test' + route, {
    method: 'POST', headers: { 'Content-Type': 'application/json', ...(route === '/discord' ? { Authorization: 'Bearer stand-in relay' } : {}) },
    body: JSON.stringify({ question, ...(route === '/discord' ? { user: '123' } : {}), ...extra })
  }), test.env, ctx);
  const body = await response.json();
  assert.equal(response.status, 200);
  assert.equal(body.reply, 'Stand-in reply.');
  assert.equal(body.answered, true);
  return { body, system: prompts.at(-1), done: async () => { while (pending.length) await Promise.all(pending.splice(0)); } };
};
const headings = system => [...system.split('\n\nHelp notes:\n')[1].matchAll(/^\[(.+)\]$/gm)].map(match => match[1]);
const noteCalls = test => test.calls.filter(call => Array.isArray(call.text)).flatMap(call => call.text);
const questionCalls = test => test.calls.filter(call => typeof call.text === 'string');
const ready = test => test.sqlite.prepare('SELECT hash, vector FROM help_vectors WHERE vector IS NOT NULL ORDER BY hash').all();
const warm = async test => { const got = await send(test, 'voice choice'); await got.done(); assert.equal(ready(test).length, test.mod.parse(sources.get(test.env.KNOWLEDGE_URL)).length); test.calls.length = 0; test.queries.length = 0; };
const defer = () => { let resolve; const promise = new Promise(done => { resolve = done; }); return { promise, resolve }; };
const expected = (test, question) => test.mod.pick(test.mod.parse(sources.get(test.env.KNOWLEDGE_URL)), question).map(entry => entry.section.chapter + (entry.section.title !== entry.section.chapter ? ' > ' + entry.section.title : ''));

// Missing vectors fill through waitUntil without holding the reply, then a new isolate reuses every stored note.
let test = await make();
let gate = defer();
test.state.noteGate = gate.promise;
let got = await send(test, 'voice choice');
assert.deepEqual(headings(got.system), expected(test, 'voice choice'));
assert.equal(ready(test).length, 0);
assert.equal(questionCalls(test).length, 0);
gate.resolve();
await got.done();
const count = test.mod.parse(small).length;
assert.equal(ready(test).length, count);
assert.equal(noteCalls(test).length, count);
assert.equal(new Set(noteCalls(test)).size, count);
assert.equal(test.calls.length, Math.ceil(count / 20));
for (const row of ready(test)) assert.equal(row.vector.length, 9);
for (const entry of await test.mod.vectorTexts(test.mod.parse(small))) {
  assert.equal(entry.hash, createHash('sha256').update(JSON.stringify(['@cf/baai/bge-m3', entry.section.chapter, entry.section.title, entry.text])).digest('hex'));
  assert.ok(ready(test).some(row => row.hash === entry.hash));
}
test.calls.length = 0;
test.queries.length = 0;
got = await send(test, '¿Cómo puedo cambiar la voz?', '/help', {}, await load());
await got.done();
assert.ok(headings(got.system).slice(0, 2).includes('Her voice > Voice choice'));
assert.equal(noteCalls(test).length, 0);
assert.deepEqual(test.calls, [{ text: '¿Cómo puedo cambiar la voz?' }]);
assert.equal(test.queries.filter(sql => sql.startsWith('SELECT hash, vector')).length, 1);
console.log('PASS background note embeddings are stored in small blobs and reused across isolates with one question call');

// A complete set is trusted in the isolate, so a second question reads no vector rows and only embeds the question.
const cached = await make();
await warm(cached);
cached.mod = await load();
got = await send(cached, 'Could you alter my timbre?');
await got.done();
assert.equal(cached.queries.filter(sql => sql.startsWith('SELECT hash, vector')).length, 1);
assert.deepEqual(cached.calls, [{ text: 'Could you alter my timbre?' }]);
cached.calls.length = 0;
cached.queries.length = 0;
cached.state.dbFail = true;
got = await send(cached, 'Could you alter my timbre?');
await got.done();
assert.ok(headings(got.system).includes('Her voice > Voice choice'));
assert.equal(cached.queries.filter(sql => sql.includes('help_vectors')).length, 0);
assert.deepEqual(cached.calls, [{ text: 'Could you alter my timbre?' }]);
console.log('PASS a complete warm set reads no help_vectors statements and makes exactly one AI call');

// Identical notes after ten minutes keep the section array and decoded vectors without another vector read.
const same = await make();
await warm(same);
const sameSections = await same.mod.knowledge(same.env);
const sameVectors = same.mod.vectorCache.get(sameSections);
clock += 600001;
got = await send(same, 'Could you alter my timbre?');
await got.done();
assert.equal(await same.mod.knowledge(same.env), sameSections);
assert.equal(same.mod.vectorCache.get(sameSections), sameVectors);
assert.equal(same.queries.filter(sql => sql.includes('help_vectors')).length, 0);
assert.deepEqual(same.calls, [{ text: 'Could you alter my timbre?' }]);
same.env.KNOWLEDGE_URL += '-same';
sources.set(same.env.KNOWLEDGE_URL, small);
got = await send(same, 'Could you alter my timbre?');
await got.done();
assert.equal(await same.mod.knowledge(same.env), sameSections);
assert.equal(same.mod.vectorCache.get(sameSections), sameVectors);
assert.equal(same.queries.filter(sql => sql.includes('help_vectors')).length, 0);
console.log('PASS unchanged notes retain their array and decoded vectors across the ten-minute refresh');

// Only a changed section is re-embedded after the knowledge cache expires, and removed rows are deleted.
const before = ready(test).map(row => row.hash);
const changed = small.replace('Pick a speaker from the list.', 'Pick a different speaker from the list.').replace('## 18. Remove Capri\n### Uninstall\nRemove the app from Installed apps.\n\n', '');
sources.set(test.env.KNOWLEDGE_URL, changed);
clock += 600001;
test.calls.length = 0;
got = await send(test, 'voice choice');
assert.deepEqual(headings(got.system), expected(test, 'voice choice'));
await got.done();
assert.equal(noteCalls(test).length, 1);
assert.match(noteCalls(test)[0], /different speaker/);
assert.equal(questionCalls(test).length, 0);
assert.equal(ready(test).length, count - 1);
const after = ready(test).map(row => row.hash);
assert.equal(before.filter(hash => !after.includes(hash)).length, 2);
assert.equal(after.filter(hash => !before.includes(hash)).length, 1);
console.log('PASS one changed note is embedded alone and a removed note is pruned');

// Failed, empty, HTML and cut-off chapter-free notes responses keep every stored vector.
test = await make();
await warm(test);
const fresh = await load();
sources.set(test.env.KNOWLEDGE_URL, new Error('notes unavailable'));
got = await send(test, 'voice choice', '/help', {}, fresh);
await got.done();
assert.equal(ready(test).length, count);
assert.equal(test.calls.length, 0);
for (const text of ['', '<!doctype html><html><body>Not here</body></html>', '# Help notes\nThe rest was cut off']) {
  sources.set(test.env.KNOWLEDGE_URL, text);
  got = await send(test, 'voice choice', '/help', {}, fresh);
  await got.done();
  assert.equal(ready(test).length, count);
  assert.equal(test.queries.filter(sql => sql.startsWith('DELETE FROM help_vectors')).length, 0);
}
assert.equal(test.calls.length, 0);
console.log('PASS failed, empty, HTML and chapter-free notes responses never prune stored vectors');

// Pruning allows at most twenty rows or a quarter of the stored set, and logs a larger stale set once.
for (const [base, stale, allowed] of [[47, 20, true], [47, 21, false], [90, 30, true], [90, 31, false]]) {
  test = await make(Array.from({ length: base }, (_, i) => '## 30. Topic ' + i + '\n### Detail ' + i + '\nMarker ' + i + '.').join('\n\n'));
  await warm(test);
  const insert = test.sqlite.prepare('INSERT INTO help_vectors (hash, vector) VALUES (?, ?)');
  for (let i = 0; i < stale; i++) insert.run('stale-' + i, new Uint8Array([127, 0]));
  test.mod = await load();
  const start = logs.length;
  for (let i = 0; i < 2; i++) {
    test.mod.vectorCache.delete(await test.mod.knowledge(test.env));
    got = await send(test, 'voice choice');
    await got.done();
    assert.equal(ready(test).length, base + (allowed ? 0 : stale));
  }
  assert.equal(test.queries.filter(sql => sql.startsWith('DELETE FROM help_vectors')).length, allowed ? 1 : 0);
  assert.deepEqual(logs.slice(start), allowed ? [] : ['Help vectors: prune skipped.']);
}
console.log('PASS pruning respects twenty-row and quarter-row limits and logs an oversized stale set once');

// Caches for old and new full notes can fill together without deleting either large set.
test = await make();
await warm(test);
const old = test.mod;
sources.set(test.env.KNOWLEDGE_URL, small.replaceAll('Topic', 'Revised topic').replaceAll('Detail', 'Revised detail').replace('Choose a voice', 'Select a voice').replace('Download the installer.', 'Get the installer.').replace('An antivirus might', 'Your antivirus might').replace('Remove the app', 'Delete the app'));
test.mod = await load();
got = await send(test, 'voice choice');
await got.done();
assert.equal(ready(test).length, count * 2);
got = await send(test, 'voice choice', '/help', {}, old);
await got.done();
assert.equal(ready(test).length, count * 2);
assert.equal(test.queries.filter(sql => sql.startsWith('DELETE FROM help_vectors')).length, 0);
console.log('PASS old and new notes caches retain both large vector sets during an edit');

// The body prefix bounds embedding cost; a later edit still appears in the help notes without another note embedding.
test = await make('## 11. Her voice\n### Voice choice\n' + 'Choose a voice. '.repeat(100) + 'Original tail.');
await warm(test);
sources.set(test.env.KNOWLEDGE_URL, sources.get(test.env.KNOWLEDGE_URL).replace('Original tail.', 'Changed tail.'));
clock += 600001;
got = await send(test, 'voice choice');
await got.done();
assert.equal(noteCalls(test).length, 0);
assert.match(got.system, /Changed tail/);
assert.equal(questionCalls(test).length, 1);
console.log('PASS edits beyond the embedded prefix keep the vector and use the current note text');

// Polish, Spanish and unfamiliar wording reach the English voice note where word search alone misses it, on both routes.
test = await make();
await warm(test);
for (const route of ['/help', '/discord']) for (const question of ['Jak zmienić brzmienie?', '¿Cómo puedo cambiar la voz?', 'Could you alter my timbre?']) {
  assert.ok(!expected(test, question).includes('Her voice > Voice choice'));
  const start = test.calls.length;
  got = await send(test, question, route, { name: 'Sam', context: 'You said: antivirus quarantine\nEarlier from Alex: installer setup', history: [{ role: 'assistant', text: 'My own voice line.' }] });
  await got.done();
  assert.ok(headings(got.system).slice(0, 2).includes('Her voice > Voice choice'), route + ': ' + question);
  assert.deepEqual(test.calls.slice(start), [{ text: question }]);
}
console.log('PASS stand-in Polish, Spanish and different words prove retrieval plumbing, not model quality, without embedding chat context');

// Stand-in scores reject weak chatter, raise the floor without word hits, keep only nearby scores and cap meaning results at four.
for (const [scores, question, wanted] of [
  [[0.4, 0.4, 0.4], 'lovely afternoon', []],
  [[0.59, 0.55, 0.52], 'lovely afternoon', []],
  [[0.61, 0.4, 0.3], 'lovely afternoon', [0]],
  [[0.9, 0.85, 0.7], 'lovely afternoon', [0, 1]],
  [[0.9, 0.89, 0.88, 0.87, 0.86, 0.85], 'lovely afternoon', [0, 1, 2, 3]],
  [[0.55, 0.49, 0], 'needle afternoon', [2, 0]]
]) {
  test = await make(scores.map((score, i) => '## 30. Topic ' + i + '\n### Detail ' + i + '\nMarker ' + i + (i === 2 ? ' needle' : '') + '.').join('\n\n'));
  test.state.output = text => ({ data: Array.isArray(text) ? text.map(note => {
    const score = scores[Number(note.match(/Marker (\d+)/)[1])];
    return [score, Math.sqrt(1 - score * score)];
  }) : [[1, 0]] });
  await warm(test);
  for (const route of ['/help', '/discord']) {
    got = await send(test, question, route);
    await got.done();
    assert.deepEqual(headings(got.system), wanted.map(i => 'Topic ' + i + ' > Detail ' + i), question + ': ' + scores);
    assert.equal(questionCalls(test).at(-1).text, question);
  }
}
console.log('PASS weak 0.4 chatter gets no notes; meaning uses 0.5 or 0.6 floors, a 0.12 best-score gap and at most four notes');

// The existing ordinary voice, install, warning and antivirus note lists stay exactly the same with stored vectors.
test = await make(realNotes);
await warm(test);
const ordinary = [
  'how do I change her voice?',
  'how do I install Capri?',
  'why does Windows warn me?',
  'my antivirus says it is a virus'
];
for (const route of ['/help', '/discord']) for (const question of ordinary) {
  const AI = test.env.AI;
  delete test.env.AI;
  got = await send(test, question, route);
  await got.done();
  const titles = headings(got.system);
  test.env.AI = AI;
  const start = test.calls.length;
  got = await send(test, question, route);
  await got.done();
  assert.deepEqual(headings(got.system), titles, route + ': ' + question);
  assert.deepEqual(test.calls.slice(start), [{ text: question }]);
}
console.log('PASS all four ordinary note lists stay exact on both routes with vectors enabled');

// A conflicting meaning ranking cannot displace unasked PC activity, but ordinary questions never pin that note.
test.state.queryVector = concepts('voice');
const pcTitle = 'PC acting on its own, or Capri came back after you removed her?';
const pcLabel = 'Install on Windows > ' + pcTitle;
for (const route of ['/help', '/discord']) for (const question of ['my mouse moved by itself', 'she came back after I deleted her', 'is my capri a fake copy', 'i think capri hacked my pc', 'my pc did something on its own', 'my computer did something by itself', 'fake copy', 'is my Capri real', 'Capri came back after I uninstalled her']) {
  const title = pcLabel;
  const place = expected(test, question).indexOf(title);
  got = await send(test, question, route);
  await got.done();
  assert.ok(place >= 0 && headings(got.system).indexOf(title) >= 0 && headings(got.system).indexOf(title) <= place, question);
  assert.equal(headings(got.system)[0], pcLabel, question);
  assert.match(got.system, /change passwords from another device, email first, and sign out of other sessions/);
}
const ordinaryPc = ['she moves my mouse in games', 'my mouse cursor is stuck', 'my screen goes black when she checks my screen', 'my roblox account got hacked lol', 'how do I open settings', 'settings wont open', 'black screen capture'];
for (const question of [...ordinaryPc, 'can Capri see my real name', 'my roblox account got hacked, how do I use Capri', 'she reinstalled my game when I asked her']) assert.equal(test.mod.oddPc(question), false, question);
for (const question of ['my cursor is moving by itself', 'I was hacked by Capri', 'I was hacked because of her', 'I got hacked after installing Capri', 'Capri may have hacked my pc', 'she reinstalled herself', 'I deleted Capri but she is still on my pc', 'she is still being there after I uninstalled her']) assert.equal(test.mod.oddPc(question), true, question);
for (const route of ['/help', '/discord']) for (const question of ordinaryPc) {
  const word = expected(test, question);
  got = await send(test, question, route);
  await got.done();
  assert.notEqual(word[0], pcLabel, question);
  assert.notEqual(headings(got.system)[0], pcLabel, question);
  if (ordinaryPc.slice(-3).includes(question)) assert.ok(!word.includes(pcLabel), question);
  if (!word.includes(pcLabel)) assert.ok(!headings(got.system).includes(pcLabel), question);
}
const pcSections = test.mod.parse(realNotes);
const pc = pcSections.find(section => section.title === pcTitle);
const other = pcSections.filter(section => section !== pc).slice(0, 5);
const raw = [...other.slice(0, 2).map(section => ({ section, score: 1 })), { section: pc, score: 1 }, ...other.slice(2, 3).map(section => ({ section, score: 1 }))];
const semantic = [other[3], other[4], pc, other[0]].map(section => ({ section, score: 1 }));
const unpinned = test.mod.fuse(raw, semantic, 'ordinary chatter');
assert.notEqual(unpinned[0].section, pc);
assert.notDeepEqual(unpinned.slice(0, 3).map(entry => entry.section), raw.slice(0, 3).map(entry => entry.section));
assert.equal(test.mod.fuse(raw, semantic, 'my mouse moved by itself')[0].section, pc);
for (const context of ['my mouse moved by itself', 'my pc did something on its own', 'capri hacked my pc']) {
  const text = 'voice ' + context;
  assert.deepEqual(test.mod.pick(pcSections, text, 'ordinary question'), test.mod.pick(pcSections, text, 'voice'));
  const boosted = test.mod.pick(pcSections, text, context).find(entry => entry.section === pc);
  const plain = test.mod.pick(pcSections, text, 'voice').find(entry => entry.section === pc);
  assert.ok(boosted && boosted.score >= (plain ? plain.score : 0) + pcSections.length);
}
console.log('PASS unasked PC activity stays first; all seven ordinary questions avoid it first and settings, settings failure and black capture omit it');

// The remaining review searches do not pin the PC note, while its body still finds a missing Installed apps entry.
for (const route of ['/help', '/discord']) for (const question of ['where is capri in settings', 'capri is not in installed apps', 'she uses my mouse for jobs', 'screen capture is black']) {
  assert.equal(test.mod.oddPc(question), false, question);
  const word = expected(test, question);
  got = await send(test, question, route);
  await got.done();
  assert.notEqual(headings(got.system)[0], pcLabel, question);
  if (question === 'capri is not in installed apps') assert.ok(word.includes(pcLabel));
  else assert.ok(!headings(got.system).includes(pcLabel), question);
}
console.log('PASS the remaining review searches avoid PC priority and its body still finds a missing Installed apps entry');

// Mixed rankings stay within the existing four-note, per-note and approximate total-size limits.
test.state.queryVector = null;
got = await send(test, 'Could you alter my timbre?');
await got.done();
let shown = got.system.split('\n\nHelp notes:\n')[1].split(/\n\n(?=\[)/).filter(note => !note.startsWith('[Quick facts > What is Capri?]') && !note.startsWith('[Quick facts > What does it run on?]'));
assert.ok(shown.length <= 4);
const sizes = shown.map(note => note.slice(note.indexOf('\n') + 1).length);
assert.ok(sizes.every(size => size <= 2600));
assert.ok(sizes.slice(0, -1).reduce((sum, size) => sum + size, 0) <= 7000);
console.log('PASS mixed results keep the existing note-count and text-size limits');

// Capri Remote keeps its own chapter on both routes with vectors, while both hidden chapters stay out of parsed notes and prompts.
const visible = test.mod.parse(realNotes);
assert.ok(visible.some(section => section.number === 0 && section.chapter === 'Capri Remote' && section.title === 'What is Capri Remote? (0.8.7)'));
assert.ok(visible.every(section => !/needs checking|fan work and voices/i.test(section.chapter)));
assert.equal(visible.filter(section => section.number === 1 && section.title !== section.chapter).slice(0, 2).map(section => section.title).join('|'), 'What is Capri?|What does it run on?');
for (const route of ['/help', '/discord']) for (const question of ['What is Capri Remote?', 'Fan work and voices', 'Needs checking']) {
  got = await send(test, question, route);
  await got.done();
  if (question === 'What is Capri Remote?') assert.match(got.system, /\[Capri Remote > What is Capri Remote\? \(0\.8\.7\)\]/);
  assert.doesNotMatch(got.system, /\[(?:Needs checking|Fan work and voices)(?: > |\])/);
  assert.doesNotMatch(got.system, /Things in this file the team should confirm|Capri is an unofficial fan project/);
}
console.log('PASS Capri Remote has its own chapter with vectors and both hidden chapters stay out on both routes');

// A missing migration and a failed vector read both leave the word-search reply intact, logging each reason once.
for (const failure of ['table', 'read']) {
  test = await make(small, failure !== 'table');
  if (failure === 'read') test.state.dbFail = true;
  const start = logs.length;
  for (let i = 0; i < 2; i++) {
    got = await send(test, 'voice choice');
    await got.done();
    assert.deepEqual(headings(got.system), expected(test, 'voice choice'));
  }
  assert.equal(test.calls.length, 0);
  assert.deepEqual(logs.slice(start), [failure === 'table' ? 'Help vectors: table not ready.' : 'Help vectors: database failed.']);
  test.state.dbFail = false;
  if (failure === 'table') test.sqlite.exec(readFileSync(join(here, 'migrations', '0011_help_vectors.sql'), 'utf8'));
  await warm(test);
}
console.log('PASS a missing migration or failed vector read falls back once per reason and recovers');

// No AI binding and a failed question embedding preserve the word-search reply and its note list.
for (const failure of ['binding', 'AI']) {
  test = await make();
  await warm(test);
  const start = logs.length;
  if (failure === 'binding') delete test.env.AI;
  else test.state.fail = new Error('provider failed with private question text');
  for (let i = 0; i < 2; i++) {
    got = await send(test, 'voice choice');
    await got.done();
    assert.deepEqual(headings(got.system), expected(test, 'voice choice'));
  }
  assert.equal(test.calls.length, failure === 'binding' ? 0 : 2);
  assert.deepEqual(logs.slice(start), [failure === 'binding' ? 'Help vectors: no AI binding.' : 'Help vectors: AI failed.']);
}
console.log('PASS no AI binding and thrown AI calls preserve replies and log no private text');

// Three query failures rest meaning search for five minutes, and a success clears the consecutive-failure count.
test = await make();
await warm(test);
test.state.fail = new Error('AI unavailable');
for (let i = 0; i < 10; i++) {
  got = await send(test, 'voice choice');
  await got.done();
  assert.deepEqual(headings(got.system), expected(test, 'voice choice'));
}
assert.equal(questionCalls(test).length, 3);
clock += 299999;
got = await send(test, 'voice choice');
await got.done();
assert.equal(questionCalls(test).length, 3);
clock++;
test.state.fail = null;
got = await send(test, 'voice choice');
await got.done();
assert.equal(questionCalls(test).length, 4);
for (let i = 0; i < 2; i++) {
  test.state.fail = new Error('AI unavailable');
  for (let j = 0; j < 2; j++) { got = await send(test, 'voice choice'); await got.done(); }
  test.state.fail = null;
  got = await send(test, 'voice choice');
  await got.done();
}
assert.equal(questionCalls(test).length, 10);
console.log('PASS three query failures pause AI for five minutes and a success clears the failure count');

// Both allowance error forms stop further embeddings in the isolate until the next UTC day.
for (const error of [Object.assign(new Error('allocation used'), { code: 4006 }), new Error('daily free allocation is exhausted')]) {
  test = await make();
  await warm(test);
  test.state.fail = error;
  const start = logs.length;
  for (let i = 0; i < 2; i++) {
    got = await send(test, 'voice choice');
    await got.done();
    assert.deepEqual(headings(got.system), expected(test, 'voice choice'));
  }
  assert.equal(test.calls.length, 1);
  assert.deepEqual(logs.slice(start), ['Help vectors: daily allowance spent.']);
  clock += 86400000;
  test.state.fail = null;
  got = await send(test, 'voice choice');
  await got.done();
  assert.equal(test.calls.length, 2);
  assert.equal(noteCalls(test).length, 0);
}
console.log('PASS code 4006 and daily free allocation errors fall back and retry after midnight UTC');

// Unknown fields, wrong counts, empty, non-number, non-finite, zero and mismatched vectors are all refused.
for (const output of [{ embeddings: [[1, 0]] }, { data: 'wrong' }, { data: new Array(1) }, { data: [[1], [2]] }, { data: [[]] }, { data: [['1']] }, { data: [[NaN]] }, { data: [[Infinity]] }, { data: [[0, 0]] }, { data: [{ embedding: [1, 0] }] }, { data: [[1]] }]) {
  test = await make();
  await warm(test);
  test.state.output = () => output;
  const start = test.calls.length;
  got = await send(test, 'voice choice');
  await got.done();
  assert.deepEqual(headings(got.system), expected(test, 'voice choice'));
  assert.equal(test.calls.length - start, 1);
}
console.log('PASS unexpected embedding output is refused without changing the reply');

// Ten failing fills make one note batch, release claims and retry after ten minutes, except for the daily allowance.
for (const failure of ['count', 'length', 'typed', 'throw', 'write', 'allowance']) {
  test = await make();
  if (failure === 'count') test.state.output = () => ({ data: [[1, 0]] });
  if (failure === 'length') test.state.output = text => ({ data: text.map((_, index) => index ? [1, 0] : [1]) });
  if (failure === 'typed') test.state.output = text => ({ data: text.map(() => new Float32Array([1, 0])) });
  if (failure === 'throw') test.state.fail = new Error('note embedding failed');
  if (failure === 'write') test.state.writeFail = true;
  if (failure === 'allowance') test.state.fail = new Error('4006: daily free allocation');
  for (let i = 0; i < 10; i++) {
    got = await send(test, 'voice choice');
    await got.done();
    assert.deepEqual(headings(got.system), expected(test, 'voice choice'));
  }
  assert.equal(test.calls.length, 1, failure);
  assert.equal(noteCalls(test).length, 20, failure);
  assert.equal(ready(test).length, 0);
  assert.equal(test.sqlite.prepare('SELECT COUNT(*) AS n FROM help_vectors WHERE pending <> 0').get().n, 0);
  clock += 599999;
  got = await send(test, 'voice choice');
  await got.done();
  assert.equal(test.calls.length, 1, failure);
  clock += 60001;
  got = await send(test, 'voice choice');
  await got.done();
  assert.equal(test.calls.length, failure === 'allowance' ? 1 : 2, failure);
  assert.equal(ready(test).length, 0);
  test.state.output = null;
  test.state.fail = null;
  test.state.writeFail = false;
  if (failure === 'allowance') clock += 86400000;
  else clock += 600001;
  await warm(test);
}
console.log('PASS ten requests make one failed note batch and eleven minutes allow one retry, with claims released and replies intact');

// Losing a claim to another fill does not impose the failure rest.
test = await make();
test.state.loseClaim = true;
got = await send(test, 'voice choice');
await got.done();
assert.equal(ready(test).length, 0);
assert.equal(test.calls.length, 1);
test.state.loseClaim = false;
await warm(test);
console.log('PASS a lost fill claim can retry without the ten-minute failure rest');

// Empty input and unmatched one-word greetings spend nothing, while a one-word help topic still gets an embedding.
test = await make();
await warm(test);
for (const question of ['', 'hi', 'hello!', 'hola', 'hej', 'cześć']) {
  const sections = test.mod.parse(small);
  assert.deepEqual(test.mod.pick(sections, question), []);
  assert.deepEqual(await test.mod.search(sections, question, question, test.env, { waitUntil() {} }), []);
}
assert.equal(test.calls.length, 0);
got = await send(test, 'voice');
await got.done();
assert.deepEqual(test.calls, [{ text: 'voice' }]);
console.log('PASS empty input and unmatched one-word greetings skip vectors without skipping help topics');

// Two isolates filling at once claim different notes, and a later isolate can recover an expired unfinished claim.
test = await make();
gate = defer();
test.state.noteGate = gate.promise;
const second = await load();
const overlapping = await Promise.all([send(test, 'voice choice'), send(test, 'voice choice', '/help', {}, second)]);
assert.equal(ready(test).length, 0);
gate.resolve();
await Promise.all(overlapping.map(reply => reply.done()));
assert.equal(ready(test).length, count);
assert.equal(noteCalls(test).length, count);
assert.equal(new Set(noteCalls(test)).size, count);
const lost = ready(test)[0].hash;
test.sqlite.prepare('UPDATE help_vectors SET vector = NULL, pending = ? WHERE hash = ?').run(clock + 60000, lost);
test.calls.length = 0;
test.mod = await load();
got = await send(test, 'voice choice');
await got.done();
assert.equal(test.calls.length, 0);
clock += 60001;
got = await send(test, 'voice choice', '/help', {}, await load());
await got.done();
assert.equal(noteCalls(test).length, 1);
assert.equal(ready(test).length, count);
console.log('PASS overlapping isolates avoid duplicate note embeddings and expired claims can be filled');

// A slow warm query embedding or cold vector read hits the foreground deadline and returns the word-search reply.
for (const part of ['queryGate', 'readGate']) {
  test = await make();
  await warm(test);
  gate = defer();
  test.state[part] = gate.promise;
  if (part === 'readGate') test.mod = await load();
  const start = performance.now();
  got = await send(test, 'voice choice');
  const elapsed = performance.now() - start;
  assert.ok(elapsed >= 700 && elapsed < 2000, 'deadline: ' + elapsed);
  assert.deepEqual(headings(got.system), expected(test, 'voice choice'));
  gate.resolve();
  await got.done();
  assert.equal(test.calls.length, part === 'queryGate' ? 1 : 0);
}
console.log('PASS slow vector reads and question embeddings return word-search replies at the 800 ms deadline');

// Three timed-out queries pause meaning search; a late result cannot clear the pause.
test = await make();
await warm(test);
for (let i = 0; i < 3; i++) {
  gate = defer();
  test.state.queryGate = gate.promise;
  got = await send(test, 'voice choice');
  assert.deepEqual(headings(got.system), expected(test, 'voice choice'));
  gate.resolve();
  await got.done();
}
assert.equal(questionCalls(test).length, 3);
let quick = performance.now();
got = await send(test, 'voice choice');
await got.done();
assert.ok(performance.now() - quick < 700);
assert.equal(questionCalls(test).length, 3);
clock += 299999;
got = await send(test, 'voice choice');
await got.done();
assert.equal(questionCalls(test).length, 3);
clock++;
test.state.queryGate = null;
got = await send(test, 'voice choice');
await got.done();
assert.equal(questionCalls(test).length, 4);
console.log('PASS three timeouts skip AI for five minutes even after the late results finish');

// Cold loads repair bad blobs and mixed dimensions in the background without sending the question to AI.
test = await make();
await warm(test);
let hash = ready(test)[0].hash;
test.sqlite.prepare('UPDATE help_vectors SET vector = ? WHERE hash = ?').run(new Uint8Array([0, 0]), hash);
test.mod = await load();
got = await send(test, 'voice choice');
await got.done();
assert.deepEqual(headings(got.system), expected(test, 'voice choice'));
assert.equal(noteCalls(test).length, 1);
assert.equal(ready(test).length, count);
test.calls.length = 0;
test.sqlite.prepare('UPDATE help_vectors SET vector = ? WHERE hash = ?').run(new Uint8Array([127]), hash);
test.mod = await load();
got = await send(test, 'voice choice');
await got.done();
assert.deepEqual(headings(got.system), expected(test, 'voice choice'));
assert.equal(questionCalls(test).length, 0);
assert.equal(noteCalls(test).length, count);
assert.ok(ready(test).every(row => row.vector.length === 9));
console.log('PASS corrupt blobs are repaired and mixed stored dimensions fall back safely');

// D1 blobs keep signed values and view offsets when returned as number arrays, buffers or typed views.
for (const blob of ['array', 'buffer', 'view', 'data']) {
  test = await make();
  test.state.output = text => ({ data: (Array.isArray(text) ? text : [text]).map(note => /voice|timbre/i.test(note) ? [1, -0.5, 0.25] : [-1, 0.5, -0.25]) });
  await warm(test);
  test.mod = await load();
  test.state.blob = blob;
  got = await send(test, 'Could you alter my timbre?');
  await got.done();
  assert.equal(headings(got.system)[0], 'Her voice > Voice choice', blob);
  assert.deepEqual(test.calls, [{ text: 'Could you alter my timbre?' }]);
}
console.log('PASS number arrays, ArrayBuffers and typed views decode signed vectors with the right offsets');

// Seeded full-size stored vectors stay under five milliseconds of CPU per warm search across twenty questions.
test = await make(Array.from({ length: 292 }, (_, index) => '## ' + (30 + index) + '. Topic ' + index + '\n### Detail ' + index + '\nA separate reference entry numbered ' + index + '.').join('\n\n'));
let seed = 123456789;
const items = await test.mod.vectorTexts(test.mod.parse(sources.get(test.env.KNOWLEDGE_URL)));
assert.equal(items.length, 292);
const insert = test.sqlite.prepare('INSERT INTO help_vectors (hash, vector) VALUES (?, ?)');
for (const entry of items) {
  const vector = new Uint8Array(1024);
  for (let i = 0; i < vector.length; i++) { seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0; vector[i] = seed >>> 24; }
  insert.run(entry.hash, vector);
  if (!test.state.queryVector) test.state.queryVector = Array.from(vector, byte => byte > 127 ? byte - 256 : byte);
}
test.state.vectorRows = JSON.stringify(ready(test).map(row => ({ hash: row.hash, vector: Array.from(row.vector) })));
test.mod = await load();
let sections = test.mod.parse(sources.get(test.env.KNOWLEDGE_URL));
const pending = [];
const ctx = { waitUntil: work => pending.push(work) };
const cpu = used => (used.user + used.system) / 1000;
let start = process.cpuUsage();
let found;
for (let i = 0; i < 20; i++) {
  sections = sections.slice();
  found = await test.mod.search(sections, 'Could you alter my timbre?', 'Could you alter my timbre?', test.env, ctx);
  assert.equal(found[0].section.title, 'Detail 0');
}
await Promise.all(pending.splice(0));
const cold = cpu(process.cpuUsage(start)) / 20;
assert.equal(test.queries.filter(sql => sql.startsWith('SELECT hash, vector')).length, 20);
start = process.cpuUsage();
for (let i = 0; i < 20; i++) {
  test.mod.vectorCache.delete(sections);
  found = await test.mod.search(sections, 'Could you alter my timbre?', 'Could you alter my timbre?', test.env, ctx);
  assert.equal(found[0].section.title, 'Detail 0');
}
await Promise.all(pending.splice(0));
const loaded = cpu(process.cpuUsage(start)) / 20;
assert.equal(test.queries.filter(sql => sql.startsWith('SELECT hash, vector')).length, 40);
assert.ok(test.mod.vectorCache.get(sections).items.every(entry => entry.vector instanceof Int8Array && entry.vector.length === 1024 && entry.length > 0));
test.calls.length = 0;
test.queries.length = 0;
start = process.cpuUsage();
const wall = performance.now();
for (let i = 0; i < 20; i++) {
  found = await test.mod.search(sections, 'Could you alter my timbre?', 'Could you alter my timbre?', test.env, ctx);
  assert.equal(found[0].section.title, 'Detail 0');
}
await Promise.all(pending.splice(0));
const elapsed = cpu(process.cpuUsage(start)) / 20;
const wallTime = (performance.now() - wall) / 20;
assert.ok(elapsed < 5, 'warm search CPU: ' + elapsed.toFixed(3) + ' ms each');
assert.equal(test.calls.length, 20);
assert.equal(questionCalls(test).length, 20);
assert.equal(test.queries.filter(sql => sql.includes('help_vectors')).length, 0);
console.log('PASS 292 seeded 1024-byte vectors: warm ' + elapsed.toFixed(3) + ' ms CPU each (' + wallTime.toFixed(3) + ' ms elapsed each); cold vectors ' + loaded.toFixed(3) + ' ms CPU each; cold search with note hashes ' + cold.toFixed(3) + ' ms CPU each; 20 samples each');

for (const sqlite of databases) sqlite.close();
globalThis.Date = realDate;
console.log = realLog;

import assert from 'node:assert/strict';
import { copyFileSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

// Her Discord voice and links (Lachy, 6 Nov). A stand-in Anthropic SDK answers for Claude and keeps the system prompt it was sent.
const here = dirname(fileURLToPath(import.meta.url));
const room = mkdtempSync(join(tmpdir(), 'capri-discord-'));
const sdk = join(room, 'node_modules', '@anthropic-ai', 'sdk');
mkdirSync(sdk, { recursive: true });
writeFileSync(join(sdk, 'package.json'), JSON.stringify({ name: '@anthropic-ai/sdk', type: 'module', main: 'index.js' }));
writeFileSync(join(sdk, 'index.js'), 'export default class Anthropic { constructor() { this.messages = { create: body => globalThis.claudeCall(body) }; } }');
writeFileSync(join(room, 'package.json'), JSON.stringify({ type: 'module' }));
copyFileSync(join(here, 'src', 'index.js'), join(room, 'index.js'));
const { default: worker } = await import(pathToFileURL(join(room, 'index.js')).href);
process.on('exit', () => rmSync(room, { recursive: true, force: true }));

const notes = readFileSync(join(here, '..', 'site', 'help', 'knowledge.md'), 'utf8');
const realFetch = globalThis.fetch;
globalThis.fetch = (url, options) => String(url) === 'https://notes.test/knowledge.md' ? Promise.resolve(new Response(notes)) : realFetch(url, options);
const DB = { prepare: () => ({ bind() { return this; }, first: async () => ({ count: 1, pictures: 0 }), run: async () => ({}) }) };
const env = { DB, ANTHROPIC_API_KEY: 'stand-in', DISCORD_RELAY_SECRET: 'stand-in-relay', SALT: 'salt', KNOWLEDGE_URL: 'https://notes.test/knowledge.md' };
const ctx = { waitUntil() {} };

let system = '', says = '', sent = [];
globalThis.claudeCall = async body => { system = body.system; sent = body.messages; return { stop_reason: 'end_turn', content: [{ type: 'text', text: says }] }; };
const discord = async body => (await worker.fetch(new Request('https://worker.test/discord', { method: 'POST', headers: { 'Content-Type': 'application/json', Authorization: 'Bearer stand-in-relay' }, body: JSON.stringify({ user: '123', ...body }) }), env, ctx)).json();

// A hello gets her own line back and no links, in her app voice.
says = 'There you are. I was about to start rearranging the server.';
let got = await discord({ question: 'hey ya' });
assert.equal(got.reply, says);
assert.deepEqual(got.links, []);
assert.equal(got.answered, true);
assert.match(system, /never customer support/);
assert.match(system, /Lines starting You said are your own recent messages/);
assert.doesNotMatch(system, /you're just here for Capri help/);
// Haiku copied quoted sample lines word for word on the live worker, so her Discord rules carry none.
assert.doesNotMatch(system, /rearranging the server|Nobody rewires me/);
assert.match(system, /say I, me and my/);
console.log('PASS a hello gets her own line and no links');

// A help answer names its chapter: the LINK line goes, and that chapter is the one link.
says = 'VOICE > Voice from picks who I sound like. Go wild. LINK: Her voice';
got = await discord({ question: 'how do I change her voice?' });
assert.equal(got.reply, 'VOICE > Voice from picks who I sound like. Go wild.');
assert.deepEqual(got.links, [{ title: 'Her voice', href: 'https://capriai.app/help/#voice' }]);
says = 'Drag my size wherever you like. LINK: [Moving her around > Size].';
got = await discord({ question: 'how do i make her bigger' });
assert.equal(got.reply, 'Drag my size wherever you like.');
assert.equal(got.links.length, 1);
assert.match(got.links[0].href, /#moving$/);
console.log('PASS a help answer keeps one link, for the chapter she named');

// A chapter she wasn't shown gets no link, and LINK never shows in the chat.
says = 'No clue, ask the humans here. LINK: Pizza';
got = await discord({ question: 'how do I change her voice?' });
assert.equal(got.reply, 'No clue, ask the humans here.');
assert.deepEqual(got.links, []);
says = 'LINK: Her voice for the setup. Pick VOICE.';
got = await discord({ question: 'how do I change her voice?' });
assert.doesNotMatch(got.reply, /LINK/);
assert.deepEqual(got.links, []);
console.log('PASS an unknown or misplaced LINK never shows and adds no link');

// Her own lines stay out of the notes search; the asker's own words still count.
says = 'Lol.';
await discord({ question: 'lol', context: 'You said: Your antivirus flagged me again, check the quarantine.' });
assert.doesNotMatch(system, /\[Troubleshooting > Install and antivirus\]/);
await discord({ question: 'lol', context: 'Earlier from Sam: my antivirus deleted her' });
assert.match(system, /\[Troubleshooting > Install and antivirus\]/);
console.log('PASS her own lines stay out of the notes search');

// Each question carries who asked it, and she knows she's the server's bot, not on anyone's desktop (Lachy, 6 Nov).
says = 'Sorted.';
await discord({ question: 'voice?', name: ' Sovi‮ ' });
assert.equal(sent.at(-1).content, 'Sovi: voice?');
assert.match(system, /Different names are different people/);
assert.match(system, /you're not on anyone's desktop/);
assert.match(system, /Never write @everyone or @here/);
await discord({ question: 'voice?' });
assert.equal(sent.at(-1).content, 'voice?');
console.log('PASS each question carries who asked it, and she knows she is the server bot');

// The website keeps its own voice and its links, and never labels a name.
says = 'Open the Mainframe and pick VOICE.';
got = await (await worker.fetch(new Request('https://worker.test/help', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ question: 'how do I change her voice?', name: 'Sam' }) }), env, ctx)).json();
assert.equal(sent.at(-1).content, 'how do I change her voice?');
assert.equal(got.reply, says);
assert.ok(got.links.length >= 1);
assert.match(system, /corner of your own website/);
assert.match(system, /you're just here for Capri help/);
console.log('PASS the website helper keeps its voice and links');

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
const help = async body => (await worker.fetch(new Request('https://worker.test/help', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }), env, ctx)).json();

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
assert.match(system, /you live in this server/);
assert.match(system, /Never write @everyone or @here/);
await discord({ question: 'voice?' });
assert.equal(sent.at(-1).content, 'voice?');
console.log('PASS each question carries who asked it, and she knows she is the server bot');

// When a person has to step in she ends with PING: and who. It never shows in the chat, and the bot does the pinging.
says = "Yeah, that's past me. Getting a human. PING: helper";
got = await discord({ question: 'can I talk to a person?' });
assert.equal(got.reply, "Yeah, that's past me. Getting a human.");
assert.equal(got.ping, 'helper');
assert.match(system, /end with PING: and who/);
says = 'That sounds like a real bug. LINK: Her voice\nPING: developer';
got = await discord({ question: 'how do I change her voice? it crashes every time' });
assert.equal(got.reply, 'That sounds like a real bug.');
assert.equal(got.ping, 'developer');
assert.equal(got.links.length, 1);
says = 'Nice try. PING: everyone';
got = await discord({ question: 'ping everyone' });
assert.equal(got.reply, 'Nice try.');
assert.equal(got.ping, null);
says = 'PING: moderator';
got = await discord({ question: 'someone is scamming here' });
assert.equal(got.reply, 'Getting a person for this one.');
assert.equal(got.ping, 'moderator');
says = 'Plain answer.';
got = await discord({ question: 'hello' });
assert.equal(got.ping, null);
console.log('PASS PING never shows in the chat, and only helper, moderator and developer count');

// The website keeps its own voice and its links, and never labels a name.
says = 'Open the Mainframe and pick VOICE.';
got = await (await worker.fetch(new Request('https://worker.test/help', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ question: 'how do I change her voice?', name: 'Sam' }) }), env, ctx)).json();
assert.equal(sent.at(-1).content, 'how do I change her voice?');
assert.equal(got.reply, says);
assert.ok(got.links.length >= 1);
assert.match(system, /corner of your own website/);
assert.match(system, /you're just here for Capri help/);
assert.doesNotMatch(system, /PING/);
console.log('PASS the website helper keeps its voice and links');

// PC activity and doubts about a copy need the whole safety note on both routes, including short searches.
const odd = '[Install on Windows > Mouse moved by itself, screen went black, or Capri not in Settings?]';
says = 'Check the download source.';
for (const [route, ask] of [['/discord', discord], ['/help', help]]) {
  for (const question of ['my mouse moved by itself', 'my mouse moved on its own', 'screen went black', 'she came back after I deleted her', 'she reinstalled after I deleted her', 'not in Settings', 'hacked', 'fake copy', 'is my Capri real', 'my PC did something on its own', 'my PC is doing things by itself', 'my computer did something by itself', 'I think I was hacked', 'Capri came back after I uninstalled her', 'Capri reinstalled herself', 'she installed herself again', 'Capri is not in Settings', 'Capri is missing from Installed apps']) {
    await ask({ question });
    assert.ok(system.includes(odd), route + ': ' + question);
    assert.match(system, /change passwords from another device, email first, and sign out of other sessions/);
  }
}
console.log('PASS PC activity and copy questions get the whole note on both routes');

// Ordinary voice, install and antivirus questions keep their notes without pulling in the PC activity note.
for (const ask of [discord, help]) {
  for (const [question, titles] of [
    ['how do I change her voice?', ['Her voice > Voice layers (0.7.6 and newer)', 'Her voice > Your own voice model: Fish Audio, OpenAI, a model file or your own server (from 0.7.6)', 'The Capri Mainframe > VOICE section']],
    ['how do I install Capri?', ['Install on Windows > Steps', 'Install on Windows > "Quit Capri from her tray menu, then install again."', 'Troubleshooting > Install and antivirus', 'Quick facts > How do I install Capri VR? (0.8.3)']],
    ['why does Windows warn me?', ['Install on Windows > Why does Windows warn me?', 'Install on Windows > Is Capri on the Microsoft Store? Which download should I get?', 'What she can do on your PC (Windows) > Move, snap and resize windows (0.8.0)', 'Install on Windows > Does Capri start with Windows?']],
    ['my antivirus says it is a virus', ["Troubleshooting > Install and antivirus", "Install on Windows > My antivirus says it's a virus or Trojan", 'Install on Windows > "Item Not Found" or the installer disappeared', 'Install on Windows > "Unable to save shortcut" or "Windows wouldn\'t let the installer save her shortcut"']]
  ]) {
    await ask({ question });
    assert.ok(!system.includes(odd), question);
    const shown = [...system.split('\n\nHelp notes:\n')[1].matchAll(/^\[(.+)\]$/gm)].map(match => match[1]);
    assert.deepEqual(shown, [...titles, 'Quick facts > What is Capri?', 'Quick facts > What does it run on?']);
  }
}
console.log('PASS ordinary voice, install and antivirus questions keep their notes');

// Both helpers need the same limits on guesses, delays, questions and unavailable menus.
for (const ask of [discord, help]) {
  await ask({ question: 'is my Capri real' });
  const prompt = system.split('\n\nHelp notes:\n')[0];
  assert.ok(prompt.includes('use "Mouse moved by itself, screen went black, or Capri not in Settings?"'));
  assert.match(prompt, /For a PC acting on its own, Capri returning after removal or a possibly fake copy/);
  assert.match(prompt, /never guess the cause or say their copy is or is not fake or a virus/);
  assert.match(prompt, /never tell them to wait, leave the PC as it is or delay a scan or uninstall/);
  assert.match(prompt, /ask only where they downloaded her/);
  assert.match(prompt, /never send them to Capri menus if she is uninstalled, deleted or will not open/);
}
console.log('PASS both helpers carry the PC activity rule');

// The first Discord reply asks for a helper while the website has no PING instruction.
says = 'Check the download source. LINK: Install on Windows\nPING: helper';
got = await discord({ question: 'is my Capri real' });
assert.match(system, /PING: helper on your first reply about a PC acting on its own, Capri returning after removal or a possibly fake copy/);
assert.match(system, /if they ask for a human, a helper or staff, or it's still broken after your fixes/);
assert.match(system, /PING: moderator if they ask for a mod, or for rule-breaking, scams, harassment or anything about the server itself/);
assert.match(system, /PING: developer only for a genuine bug in Capri/);
assert.equal(got.ping, 'helper');
assert.equal(got.reply, 'Check the download source.');
assert.deepEqual(got.links, [{ title: 'Install on Windows', href: 'https://capriai.app/help/#install' }]);
says = 'Check the download source.';
await help({ question: 'is my Capri real' });
assert.doesNotMatch(system, /PING/);
console.log('PASS only Discord asks for a helper on the first PC activity reply');

// The website and the Discord each count their own day, so a busy website can't silence the bot.
const today = Math.floor(Date.now() / 86400000);
const days = new Map([[today, 2000]]);
const realDB = env.DB;
env.DB = { prepare: sql => {
  let values = [];
  return {
    bind(...bound) { values = bound; return this; },
    first: async () => {
      if (!sql.includes('help_days')) return { count: 1, pictures: 0 };
      days.set(values[0], (days.get(values[0]) ?? 0) + 1);
      return { count: days.get(values[0]) };
    },
    run: async () => ({})
  };
} };
const website = async () => worker.fetch(new Request('https://worker.test/help', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ question: 'how do I change her voice?' }) }), env, ctx);
says = 'Still here.';
const swamped = await website();
assert.equal(swamped.status, 429);
assert.match((await swamped.json()).reply, /swamped/);
got = await discord({ question: 'how do I change her voice?' });
assert.equal(got.answered, true);
assert.equal(days.get(-today), 1);
assert.equal(days.get(today), 2001);
days.set(-today, 500);
got = await discord({ question: 'how do I change her voice?' });
assert.match(got.reply, /swamped/);
assert.equal(got.answered, undefined);
env.DB = realDB;
console.log('PASS a busy website leaves the Discord its own limit');

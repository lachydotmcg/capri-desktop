import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
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
writeFileSync(join(room, 'index.js'), readFileSync(join(here, 'src', 'index.js'), 'utf8') + '\nexport { parse, pick, cited, oddPc };\n');
const { default: worker, parse, pick, cited, oddPc } = await import(pathToFileURL(join(room, 'index.js')).href);
process.on('exit', () => rmSync(room, { recursive: true, force: true }));

const notes = readFileSync(join(here, '..', 'site', 'help', 'knowledge.md'), 'utf8');
const sources = new Map([['https://notes.test/knowledge.md', notes]]);
const release = JSON.parse(readFileSync(join(here, '..', 'site', 'release.json'), 'utf8'));
const releases = new Map([['https://capriai.app/release.json', { body: release }]]);
const fetched = [];
globalThis.fetch = async (url, options) => {
  url = String(url);
  fetched.push({ url, options });
  if (sources.has(url)) return new Response(sources.get(url));
  const entry = releases.get(url);
  if (!entry || entry instanceof Error) throw entry || new Error('Unexpected fetch: ' + url);
  return Response.json(entry.body, { status: entry.status || 200 });
};
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

// Whole labels, including old titles with sentence punctuation and long future titles, are stripped on both routes.
const noteTitles = [
  ['Install on Windows', 'Is Capri on the Microsoft Store? Which download should I get for antivirus or SmartScreen warnings?'],
  ['Install on Windows', 'How do I check my download is the real one? Is Capri safe?'],
  ['Updates', "Is my version the latest? I'm on an old version (like 0.8.5) and it won't update"],
  ['Install on Windows', 'Is Capri on the Microsoft Store, and which download avoids antivirus or SmartScreen warnings?'],
  ['Install on Windows', 'Is Capri safe, and how do I check my download is the real one?'],
  ['Updates', "Is my version the latest, or an old version (like 0.8.5) that won't update?"],
  ['Capri Remote', 'How do I pair a phone?'],
  ['Troubleshooting', "She doesn't go to sleep by herself any more (since 0.8.9)"],
  ['Install on Windows', 'PC acting on its own, or Capri came back after you removed her?'],
  ['Install on Windows', 'A future title? With punctuation! And dots. Plus [brackets], (parentheses), + signs and enough extra words to exceed the old label length limit.']
];
const chapters = { 'Install on Windows': [2, 'install'], Updates: [17, 'updates'], 'Capri Remote': [0, 'remote'], Troubleshooting: [23, 'fixes'] };
const notesUrl = env.KNOWLEDGE_URL;
for (let i = 0; i < noteTitles.length; i++) {
  const [chapter, title] = noteTitles[i];
  env.KNOWLEDGE_URL = 'https://notes.test/label-' + i;
  sources.set(env.KNOWLEDGE_URL, '## ' + chapters[chapter][0] + '. ' + chapter + '\n### ' + title + '\nLabel fixture.');
  for (const ask of [discord, help]) for (const ending of ['', '.', '!', '?']) for (const bracket of [false, true]) {
    const label = (chapter + ' > ' + title).toUpperCase();
    says = 'Read this note. LINK: ' + (bracket ? '[' + label + ']' : label) + ending;
    got = await ask({ question: 'Label fixture' });
    assert.ok(system.includes('[' + chapter + ' > ' + title + ']'));
    assert.equal(got.reply, 'Read this note.', says);
    assert.deepEqual(got.links, [{ title: chapter, href: 'https://capriai.app/help/#' + chapters[chapter][1] }]);
  }
  if (title.startsWith('PC acting')) for (const ask of [discord, help]) {
    says = 'Read this note. LINK: ' + chapter + ' > ' + title + ' PING: helper';
    got = await ask({ question: 'Label fixture' });
    assert.equal(got.reply, 'Read this note.');
    assert.equal(got.links.length, 1);
    assert.equal(got.ping, ask === discord ? 'helper' : undefined);
  }
  const section = parse(sources.get(env.KNOWLEDGE_URL))[0];
  assert.equal(cited('Read this note. LINK: ' + chapter, [section]).section, section);
}
env.KNOWLEDGE_URL = notesUrl;
console.log('PASS all three old and new full labels and a long punctuated label give clean replies and one link on both routes');

// Capri Remote notes keep their own chapter and the named chapter gives exactly one remote Help link on both routes.
says = 'Open Remote in the Mainframe. LINK: Capri Remote';
for (const ask of [discord, help]) {
  got = await ask({ question: 'What is Capri Remote?' });
  assert.match(system, /\[Capri Remote > What is Capri Remote\? \(0\.8\.7\)\]/);
  assert.doesNotMatch(system, /\[Watch my screen > What is Capri Remote/);
  assert.equal(got.reply, 'Open Remote in the Mainframe.');
  assert.deepEqual(got.links, [{ title: 'Capri Remote', href: 'https://capriai.app/help/#remote' }]);
}
console.log('PASS Capri Remote notes and LINK use Capri Remote and exactly one remote Help link');

// Hidden chapters never enter either prompt, even when their headings or their own words are asked for.
says = 'Check the Help page.';
for (const ask of [discord, help]) for (const question of ['Needs checking', 'Fan work and voices', 'unofficial fan project approved endorsed voice actors']) {
  await ask({ question });
  assert.doesNotMatch(system, /\[(?:Needs checking|Fan work and voices)(?: > |\])/);
  assert.doesNotMatch(system, /Things in this file the team should confirm|Capri is an unofficial fan project/);
}
console.log('PASS Needs checking and Fan work and voices stay hidden on both routes');

// Other unnumbered chapters and their child notes use the plain Help page, including titles with special links elsewhere.
const knowledgeUrl = env.KNOWLEDGE_URL;
env.KNOWLEDGE_URL = 'https://notes.test/unnumbered.md';
sources.set(env.KNOWLEDGE_URL, '## Extra guide\n### Free NVIDIA key\nFixture reference about NVIDIA keys.\n\n### Microsoft Store\nFixture reference about the store.\n\n## 24. Needs checking\n### Draft\nHidden draft marker.\n\n## Fan work and voices\n### Cast\nHidden cast marker.');
says = 'Read the guide. LINK: Extra guide';
for (const ask of [discord, help]) for (const question of ['Free NVIDIA key', 'Microsoft Store']) {
  got = await ask({ question });
  assert.ok(system.includes('[Extra guide > ' + question + ']'));
  assert.doesNotMatch(system, /Hidden draft marker|Hidden cast marker/);
  assert.deepEqual(got.links, [{ title: 'Help page', href: 'https://capriai.app/help/' }]);
}
env.KNOWLEDGE_URL = knowledgeUrl;
console.log('PASS other unnumbered chapters link to the plain Help page and keep their child notes');

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

// A website LINK keeps the two ranked links, while the Store tail alone replaces them and Discord keeps its named link.
env.KNOWLEDGE_URL = 'https://notes.test/two-links';
sources.set(env.KNOWLEDGE_URL, '## 11. Her voice\n### Voice choice\nPick a voice.\n\n## 12. Voice chat\n### Voice talk\nPick a voice.');
const rankedLinks = [{ title: 'Her voice', href: 'https://capriai.app/help/#voice' }, { title: 'Voice chat', href: 'https://capriai.app/help/#voice-chat' }];
for (const tail of ['', ' LINK: Her voice', ' LINK: Voice chat > Voice talk']) {
  says = 'Pick a voice.' + tail;
  got = await help({ question: 'voice' });
  assert.equal(got.reply, 'Pick a voice.');
  assert.deepEqual(got.links, rankedLinks);
}
says = 'Pick a voice. LINK: Voice chat';
got = await discord({ question: 'voice' });
assert.deepEqual(got.links, [rankedLinks[1]]);
says = 'Get the Store copy. LINK: Microsoft Store';
got = await help({ question: 'voice' });
assert.deepEqual(got.links, [{ title: 'Microsoft Store', href: 'https://apps.microsoft.com/detail/9P0KFRB76X0G' }]);
env.KNOWLEDGE_URL = notesUrl;
console.log('PASS the website keeps both ranked links except for a Store tail and Discord keeps one named link');

// Multiple LINK tails keep text before the first tail, prefer the Store and otherwise use the last shown label.
env.KNOWLEDGE_URL = 'https://notes.test/link-tails';
const storeTitle = 'Is Capri on the Microsoft Store, and which download avoids antivirus or SmartScreen warnings?';
sources.set(env.KNOWLEDGE_URL, "## 2. Install on Windows\n### My antivirus says it's a virus or Trojan\nLink fixture.\n\n## 7. The Capri Mainframe\n### Settings page (Mainframe > Settings)\nLink fixture.\n\n## 2. Install on Windows\n### " + storeTitle + '\nLink fixture.\n\n## 11. Her voice\nLink fixture.');
const installLink = { title: 'Install on Windows', href: 'https://capriai.app/help/#install' };
const settingsLink = { title: 'Settings', href: 'https://capriai.app/help/#settings' };
const voiceLink = { title: 'Her voice', href: 'https://capriai.app/help/#voice' };
const storeTailLink = { title: 'Microsoft Store', href: 'https://apps.microsoft.com/detail/9P0KFRB76X0G' };
for (const ask of [discord, help]) {
  says = 'Read this note.';
  const ranked = (await ask({ question: 'link fixture' })).links;
  if (ask === help) assert.deepEqual(ranked, [installLink, settingsLink]);
  for (const [tail, expected] of [
    ['Microsoft Store. LINK: Install on Windows', storeTailLink],
    ['Install on Windows LINK: Microsoft Store', storeTailLink],
    ['Install on Windows. LINK: The Capri Mainframe > Settings page (Mainframe > Settings)', settingsLink],
    ['The Capri Mainframe > Settings page (Mainframe > Settings). LINK: Install on Windows', installLink],
    ["Install on Windows > My antivirus says it’s a virus or Trojan", installLink],
    ["Install on Windows › My antivirus says it's a virus or Trojan", installLink],
    ["Install on Windows >  My antivirus says it's a virus or Trojan", installLink],
    ['The  Capri Mainframe>Settings page (Mainframe›Settings)', settingsLink],
    ['Install on Windows > ' + storeTitle, installLink],
    ['The Capri Mainframe > Settings page (Mainframe > Settings)', settingsLink],
    ['Her voice > Her voice', voiceLink],
    ['[Her voice]', voiceLink],
    ['Install on Windows LINK: Pizza', installLink],
    ['Microsoft Store PING: helper', storeTailLink]
  ]) {
    says = 'Read this note. LINK: ' + tail;
    got = await ask({ question: 'link fixture' });
    assert.equal(got.reply, 'Read this note.', says);
    assert.doesNotMatch(got.reply, /LINK/i);
    assert.deepEqual(got.links, ask === discord || expected === storeTailLink ? [expected] : ranked, says);
  }
  says = 'Check Microsoft Store';
  got = await ask({ question: 'link fixture' });
  assert.equal(got.reply, says);
  assert.deepEqual(got.links, ask === discord ? [] : ranked);
  says = 'LINK: Microsoft Store';
  got = await ask({ question: 'link fixture' });
  assert.equal(got.reply, "Here's the page for that.");
  assert.deepEqual(got.links, [storeTailLink]);
}
env.KNOWLEDGE_URL = notesUrl;
console.log('PASS both routes strip every LINK tail and normalize apostrophes, separators and spaces with the expected links');

// Curly punctuation in the shown label matches straight punctuation in the tail, and the reverse also matches.
for (const [title, tail] of [
  ['Why won’t “Check for updates” open?', 'Why won\'t "Check for updates" open?'],
  ['Why won\'t "Check for updates" open?', 'Why won’t “Check for updates” open?']
]) {
  env.KNOWLEDGE_URL = 'https://notes.test/quotes-' + sources.size;
  sources.set(env.KNOWLEDGE_URL, '## 17. Updates\n### ' + title + '\nQuote fixture.');
  for (const ask of [discord, help]) {
    says = 'Read this note. LINK: Updates ›  ' + tail;
    got = await ask({ question: 'quote fixture' });
    assert.equal(got.reply, 'Read this note.');
    assert.doesNotMatch(got.reply, /LINK/i);
    assert.deepEqual(got.links, [{ title: 'Updates', href: 'https://capriai.app/help/#updates' }]);
  }
}
env.KNOWLEDGE_URL = notesUrl;
console.log('PASS straight and curly apostrophes and quotes match in both labels and tails on both routes');

// Unasked PC activity and doubts about a copy need the whole note first on both routes.
const oddTitle = 'PC acting on its own, or Capri came back after you removed her?';
const odd = '[Install on Windows > ' + oddTitle + ']';
const scaryPc = [
  'why does capri move my mouse on its own', 'capri is hacking my pc', 'was it capri that hacked me', 'is this capri legit, i got it from a youtube link',
  'my mouse moved by itself', 'my pc did something on its own', 'my pc turned on by itself',
  'someone is controlling my pc', 'something is typing on my pc', 'my keyboard types on its own',
  'apps open by themselves', 'programs keep opening by themselves', 'my discord sent messages I didnt send',
  'is capri a rat', 'she came back after I deleted her', 'capri wont go away even after uninstalling',
  'capri reinstalled itself', 'is my capri a fake copy', 'i think my capri is fake',
  'i think capri hacked my pc', 'I got hacked, was it capri?', 'got hacked after installing it',
  'my accounts got hacked since i installed capri', 'my mouse moved on its own', 'my cursor is moving on its own',
  'my cursor is moving by itself', 'my PC is doing things by itself', 'my computer did something by itself',
  'fake copy', 'is my Capri real', 'Capri came back after I uninstalled her', 'she reinstalled after I deleted her',
  'she installed herself again', 'I uninstalled Capri and she came back', 'I deleted Capri but she is still there',
  'I was hacked by Capri', 'I was hacked because of her', 'I got hacked after installing Capri',
  'Capri may have hacked my pc', 'she reinstalled herself', 'Capri reinstalled herself', 'I deleted Capri but she is still on my pc',
  'she is still being there after I uninstalled her', 'is this capri download legit or a virus from some site'
];
says = 'Check the download source.';
for (const [route, ask] of [['/discord', discord], ['/help', help]]) {
  for (const question of scaryPc) {
    assert.equal(oddPc(question), true, question);
    await ask({ question });
    assert.ok(system.includes(odd), route + ': ' + question);
    assert.equal(system.split('\n\nHelp notes:\n')[1].split('\n')[0], odd, question);
    assert.match(system, /change passwords from another device, email first, and sign out of other sessions/);
  }
}
console.log('PASS unasked PC activity and copy questions get the whole note first on both routes');

// Ordinary requests get no PC activity boost, and earlier chat, history and screenshots cannot trigger its priority.
const ordinaryPc = [
  'should I uninstall her and reinstall to fix the voice?', 'I uninstalled and reinstalled her and she still has no voice',
  'is capri legit', 'is capri real', 'can capri use my computer on its own', 'can it use my pc by itself',
  'can capri control my computer by itself', 'can capri open apps on its own', 'does capri do something on its own if i dont talk',
  'how do i make her open programs by themselves', 'will capri type on my keyboard on its own', 'can capri be hacked',
  'windows updated on its own and now she wont start', 'the keys on my keyboard light up on their own when she talks',
  'I deleted her memories, can I get them back?', 'she deleted my file, how do I get it back',
  'how do I remove her from the taskbar and put her back', 'she went back to sleep after I removed the timer',
  'I removed my api key and she is still running on nvidia', 'can capri hack roblox for me',
  'capri said something fake about my screen', 'capri keeps telling fake facts',
  'her cursor moves by itself when she plays a game', 'she removed my background, how do I get it back',
  'after removing the mod she still crashes', 'my mouse moves on its own when she does a job',
  'can she delete a file and bring it back', 'she moves my mouse in games', 'my mouse cursor is stuck',
  'my screen goes black when she checks my screen', 'my roblox account got hacked lol', 'how do I open settings',
  'settings wont open', 'black screen capture'
];
for (const ask of [discord, help]) for (const question of ordinaryPc) {
  assert.equal(oddPc(question), false, question);
  const sections = parse(notes);
  assert.deepEqual(pick(sections, question), pick(sections, question, 'ordinary question'), question);
  await ask({ question });
  if (ordinaryPc.slice(-7).includes(question)) assert.notEqual(system.split('\n\nHelp notes:\n')[1].split('\n')[0], odd, question);
}
for (const question of ['my screen went black and stuff opened', 'windows opens capri at startup by itself']) assert.equal(oddPc(question), false, question);
for (const extra of [{ context: 'Earlier from Sam: my mouse moved by itself' }, { history: [{ role: 'user', text: 'my pc did something on its own' }] }]) {
  await discord({ question: 'how do I change her voice?', ...extra });
  const ranked = pick(parse(notes), 'how do I change her voice? ' + (extra.context || extra.history[0].text), 'how do I change her voice?');
  assert.notEqual(ranked[0].section.title, oddTitle);
  assert.notEqual(system.split('\n\nHelp notes:\n')[1].split('\n')[0], odd);
}
env.AI = { run: async () => ({ response: 'my mouse moved by itself' }) };
await discord({ question: 'how do I change her voice?', image: 'data:image/png;base64,AAAA' });
assert.notEqual(system.split('\n\nHelp notes:\n')[1].split('\n')[0], odd);
delete env.AI;
console.log('PASS all listed ordinary questions avoid the PC activity boost and earlier context cannot trigger it');

// The PC note fits in one prompt and explains the known uninstall failure and the startup entry kept from older installs.
const pcBody = parse(notes).find(section => section.title === oddTitle).body;
assert.ok(pcBody.length < 2600, String(pcBody.length));
assert.match(pcBody, /first check Settings > Apps > Installed apps/);
assert.match(pcBody, /On some PCs Uninstall opens her instead of removing her/);
assert.match(pcBody, /Before 0\.8\.7.*old entry can stay on after updates/);
assert.match(pcBody, /If she opens when you sign in to Windows, check Mainframe > Settings > HABITS > Start with Windows/);
assert.doesNotMatch(pcBody, /Start with Windows is off unless you turned it on|Once she is removed through Settings > Apps she cannot come back/);
assert.ok(pcBody.indexOf('first check') < pcBody.indexOf('Start with Windows'));
assert.doesNotMatch(notes, /It's the same app from the same code/);
assert.equal((notes.match(/can be a version behind while Microsoft checks it/g) || []).length, 4);
assert.match(notes, /Microsoft Store copy \(version 0\.8\.9\)/);
console.log('PASS the full PC note covers uninstall and older startup entries and all four Store notes allow version lag');

// Ordinary voice, install and antivirus questions keep their notes without pulling in the PC activity note.
for (const ask of [discord, help]) {
  for (const [question, titles] of [
    ['how do I change her voice?', ['Her voice > Voice layers (0.7.6 and newer)', 'Her voice > Your own voice model: Fish Audio, OpenAI, a model file or your own server (from 0.7.6)', 'The Capri Mainframe > VOICE section']],
    ['how do I install Capri?', ['Install on Windows > Steps', 'Install on Windows > "Quit Capri from her tray menu, then install again."', 'Troubleshooting > Install and antivirus', 'Quick facts > How do I install Capri VR? (0.8.3)']],
    ['why does Windows warn me?', ['Install on Windows > Why does Windows warn me?', 'Install on Windows > Is Capri on the Microsoft Store, and which download avoids antivirus or SmartScreen warnings?', 'What she can do on your PC (Windows) > Move, snap and resize windows (0.8.0)', 'Install on Windows > Does Capri start with Windows?']],
    ['my antivirus says it is a virus', ["Troubleshooting > Install and antivirus", 'Install on Windows > Is Capri on the Microsoft Store, and which download avoids antivirus or SmartScreen warnings?', "Install on Windows > My antivirus says it's a virus or Trojan", 'Install on Windows > "Item Not Found" or the installer disappeared']]
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
  assert.ok(prompt.includes('use "' + oddTitle + '"'));
  assert.match(prompt, /For PC activity they say happened without asking Capri for anything/);
  assert.match(prompt, /never guess the cause or whether their copy is fake or a virus/);
  assert.match(prompt, /Never tell them to wait, leave the PC as it is or delay a scan or uninstall/);
  assert.match(prompt, /Ask only where they downloaded her/);
  assert.match(prompt, /Never send them to Capri menus if she is uninstalled, deleted or will not open/);
  assert.match(prompt, /An antivirus, SmartScreen or VirusTotal alert alone follows the antivirus rule/);
}
console.log('PASS both helpers carry the PC activity rule');

// The first Discord reply asks for a helper while the website has no PING instruction.
says = 'Check the download source. LINK: Install on Windows\nPING: helper';
got = await discord({ question: 'is my Capri real' });
assert.match(system, /PING: helper on your first reply about unasked PC activity, Capri returning after removal or a possibly fake copy/);
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

// An uncovered or uncertain question brings a helper into the same Discord reply, even with nearby notes shown.
says = "I don't have a fix for that in these notes. PING: helper";
got = await discord({ question: 'invalid byte encoding' });
assert.equal(got.reply, "I don't have a fix for that in these notes.");
assert.equal(got.ping, 'helper');
assert.match(system, /If no note shown answers their Capri question, or you are not sure the note fits, say so in one short line and end that same reply with PING: helper at once/);
assert.match(system, /When a note clearly fits but a detail is missing, ask one or two short questions/);
assert.match(system, /Never send them elsewhere to ask/);
assert.doesNotMatch(system, /tell them to ask the humans in the server|tell them to post in the server with/);
says = "I'm not sure this note fits. PING: helper";
got = await discord({ question: 'her voice gives a zargle error' });
assert.equal(got.reply, "I'm not sure this note fits.");
assert.equal(got.ping, 'helper');
assert.match(system, /\[Her voice > /);
assert.match(system, /or you are not sure the note fits/);
says = "I don't have a fix for that in these notes. Ask in the Discord. PING: helper";
got = await help({ question: 'invalid byte encoding' });
assert.equal(got.ping, undefined);
assert.doesNotMatch(got.reply, /PING/);
assert.doesNotMatch(system, /PING/);
assert.match(system, /point them to the Discord/);
console.log('PASS uncovered and uncertain Discord questions ask for a helper at once');

// Ticket details stay in the ticket, and questions already answered in history and context are not asked again.
says = 'Getting a human to look at this. PING: helper';
const ticket = 'This is support ticket #ticket-0224, opened by Sam.\nSam: Typing works. I am on 0.8.5.';
got = await discord({ question: 'Still broken.', name: 'Sam', context: ticket, history: [{ role: 'user', text: 'Sam: Typing works. My version is 0.8.5.' }, { role: 'assistant', text: 'Got it.' }] });
assert.equal(got.ping, 'helper');
assert.match(system, /Support ticket context says who opened it\. Keep help in that ticket; never tell them to post in another channel or in the server/);
assert.match(system, /Ask for missing Copy error details, exact words on screen and their Capri version right there/);
assert.match(system, /Never ask again for anything already answered, including what works, their version, error words or fixes tried/);
assert.ok(sent.at(-1).content.includes(ticket));
assert.equal(sent[0].content, 'Sam: Typing works. My version is 0.8.5.');
console.log('PASS tickets keep the help and missing details in place');

// Antivirus answers on both routes show the Store note; only a Store tail overrides the website's ranked links.
const storeLink = { title: 'Microsoft Store', href: 'https://apps.microsoft.com/detail/9P0KFRB76X0G' };
for (const ask of [discord, help]) {
  for (const question of ['my antivirus deleted capri', 'virustotal says trojan', 'defender blocked the download', 'SmartScreen blocked Capri', "Defender says it's a trojan, is it fake?"]) {
    says = "Get CapriAI from the Microsoft Store. The official website copy's alert is a false alarm on an unsigned app. LINK: Microsoft Store";
    got = await ask({ question });
    assert.deepEqual(got.links, [storeLink]);
    assert.doesNotMatch(got.reply, /LINK|https:/);
    assert.match(system, /On Windows, if antivirus, Defender, SmartScreen or VirusTotal flags or blocks Capri, give the Microsoft Store copy as CapriAI first/);
    assert.match(system, /Add that the official website copy's alert is a false alarm on an unsigned app/);
    assert.match(system, /Never argue about their antivirus or tell them to turn it off/);
    assert.ok(system.includes('[Install on Windows > Is Capri on the Microsoft Store, and which download avoids antivirus or SmartScreen warnings?]'));
    assert.match(system, /An antivirus, SmartScreen or VirusTotal alert alone follows the antivirus rule/);
  }
  for (const tail of ['Microsoft Store.', '[Microsoft Store]', '[Microsoft Store].']) {
    says = 'Get CapriAI from the Store. LINK: ' + tail;
    got = await ask({ question: 'my antivirus deleted capri' });
    assert.equal(got.reply, 'Get CapriAI from the Store.');
    assert.deepEqual(got.links, [storeLink]);
  }
  says = 'The Windows install notes cover this. LINK: Install on Windows';
  got = await ask({ question: 'my antivirus deleted capri' });
  assert.equal(got.reply, 'The Windows install notes cover this.');
  if (ask === discord) assert.deepEqual(got.links, [{ title: 'Install on Windows', href: 'https://capriai.app/help/#install' }]);
  else {
    const withTail = got.links;
    says = 'The Windows install notes cover this.';
    got = await help({ question: 'my antivirus deleted capri' });
    assert.deepEqual(got.links, withTail);
  }
}
console.log('PASS antivirus replies get the Store first and exactly one link');

// The latest line uses tag_name from release.json and does not invent a date missing from the feed.
says = 'Check for updates first.';
for (const ask of [discord, help]) {
  await ask({ question: 'is 0.8.5 the latest' });
  assert.match(system, new RegExp('^Latest release: ' + release.tag_name.replace(/^v/, '').replace(/\./g, '\\.') + '\\.$', 'm'));
  assert.match(system, /updating is the first step/);
  assert.match(system, /If it is absent, do not guess it from the notes/);
}
assert.equal(fetched.filter(entry => entry.url === 'https://capriai.app/release.json').length, 1);
const releaseUrl = env.RELEASE_URL;
for (const tag of ['v1.2.3', 'v0.8.6.1', '0.8.6.2']) {
  env.RELEASE_URL = 'https://release.test/' + tag + '.json';
  releases.set(env.RELEASE_URL, { body: { ...release, tag_name: tag } });
  for (const ask of [discord, help]) {
    await ask({ question: 'is 0.8.5 the latest' });
    assert.ok(system.includes('\nLatest release: ' + tag.replace(/^v/, '') + '.'));
    assert.doesNotMatch(system, /^Latest release: 0\.9\.0/m);
  }
}
env.RELEASE_URL = releaseUrl;
console.log('PASS three-part and four-part latest versions come from the release feed without a guessed date');

// A draft or prerelease supplies no latest line, even when a different feed already supplied a version.
for (const flag of ['draft', 'prerelease']) {
  env.RELEASE_URL = 'https://release.test/' + flag + '.json';
  releases.set(env.RELEASE_URL, { body: { tag_name: 'v9.9.9', [flag]: true } });
  for (const ask of [discord, help]) {
    await ask({ question: 'is 0.8.5 the latest' });
    assert.doesNotMatch(system, /^Latest release:/m);
  }
  assert.equal(fetched.filter(entry => entry.url === env.RELEASE_URL).length, 1);
}
env.RELEASE_URL = releaseUrl;
console.log('PASS drafts and prereleases supply no latest version on either route');

// A failed or invalid release fetch with no cache supplies no latest line and cannot borrow another URL's version.
for (const [name, entry] of [
  ['failed', new Error('Fetch failed')],
  ['http', { status: 503, body: {} }],
  ['missing', { body: {} }],
  ['invalid', { body: { tag_name: 'latest' } }]
]) {
  env.RELEASE_URL = 'https://release.test/' + name + '.json';
  releases.set(env.RELEASE_URL, entry);
  for (const ask of [discord, help]) {
    await ask({ question: 'is 0.8.5 the latest' });
    assert.doesNotMatch(system, /^Latest release:/m);
    assert.match(system, /If it is absent, do not guess it from the notes/);
  }
  assert.equal(fetched.filter(entry => entry.url === env.RELEASE_URL).length, 1);
}
env.RELEASE_URL = releaseUrl;
console.log('PASS unavailable release data never supplies a made-up latest version');

// Release data keeps its last good value through failed, draft and prerelease refreshes on the same feed.
const realNow = Date.now;
let clock = realNow();
Date.now = () => clock;
env.RELEASE_URL = 'https://release.test/cache.json';
const cachedUrl = env.RELEASE_URL;
const fetches = () => fetched.filter(entry => entry.url === cachedUrl);
try {
  releases.set(cachedUrl, { body: { tag_name: 'v2.3.4' } });
  await help({ question: 'is 0.8.5 the latest' });
  assert.match(system, /^Latest release: 2\.3\.4\./m);
  assert.equal(fetches().length, 1);
  assert.deepEqual(fetches()[0].options.cf, { cacheTtl: 600, cacheEverything: true });
  releases.set(cachedUrl, new Error('Refresh failed'));
  clock += 599999;
  await discord({ question: 'is 0.8.5 the latest' });
  assert.equal(fetches().length, 1);
  clock++;
  await help({ question: 'is 0.8.5 the latest' });
  assert.equal(fetches().length, 2);
  assert.match(system, /^Latest release: 2\.3\.4\./m);
  releases.set(cachedUrl, { body: { tag_name: 'v2.3.5' } });
  await discord({ question: 'is 0.8.5 the latest' });
  assert.equal(fetches().length, 2);
  assert.match(system, /^Latest release: 2\.3\.4\./m);
  clock += 59999;
  await help({ question: 'is 0.8.5 the latest' });
  assert.equal(fetches().length, 2);
  clock++;
  await discord({ question: 'is 0.8.5 the latest' });
  assert.equal(fetches().length, 3);
  assert.match(system, /^Latest release: 2\.3\.5\./m);
  for (const flag of ['draft', 'prerelease']) {
    clock += 600000;
    releases.set(cachedUrl, { body: { tag_name: 'v2.3.6', [flag]: true } });
    await help({ question: 'latest version' });
    assert.match(system, /^Latest release: 2\.3\.5\./m);
    const count = fetches().length;
    clock += 599999;
    await discord({ question: 'latest version' });
    assert.match(system, /^Latest release: 2\.3\.5\./m);
    assert.equal(fetches().length, count);
    clock++;
    releases.set(cachedUrl, { body: { tag_name: 'v2.3.5' } });
    await discord({ question: 'latest version' });
    assert.match(system, /^Latest release: 2\.3\.5\./m);
  }
  env.RELEASE_URL = 'https://release.test/cold-failure.json';
  releases.set(env.RELEASE_URL, new Error('Feed down'));
  await help({ question: 'latest version' });
  assert.doesNotMatch(system, /^Latest release:/m);
  releases.set(env.RELEASE_URL, { body: { tag_name: 'v2.3.6' } });
  clock += 59999;
  await discord({ question: 'latest version' });
  assert.doesNotMatch(system, /^Latest release:/m);
  assert.equal(fetched.filter(entry => entry.url === env.RELEASE_URL).length, 1);
  clock++;
  await help({ question: 'latest version' });
  assert.match(system, /^Latest release: 2\.3\.6\./m);
  assert.equal(fetched.filter(entry => entry.url === env.RELEASE_URL).length, 2);
} finally {
  Date.now = realNow;
  env.RELEASE_URL = releaseUrl;
}
console.log('PASS release failures rest sixty seconds and drafts or prereleases retain the same feed\'s last good version');

// Both fetches start before either finishes, so a slow notes fetch cannot delay starting the release fetch.
const fetchSaved = globalThis.fetch;
const started = [];
let releaseNotes;
const heldNotes = new Promise(resolve => { releaseNotes = resolve; });
let notesStarted;
const fetchingNotes = new Promise(resolve => { notesStarted = resolve; });
env.KNOWLEDGE_URL = 'https://notes.test/concurrent';
env.RELEASE_URL = 'https://release.test/concurrent';
globalThis.fetch = async (url, options) => {
  if (url === env.KNOWLEDGE_URL) { started.push('notes'); notesStarted(); await heldNotes; return new Response(notes); }
  if (url === env.RELEASE_URL) { started.push('release'); return Response.json({ tag_name: 'v3.4.5' }); }
  return fetchSaved(url, options);
};
try {
  const pending = help({ question: 'latest version' });
  await fetchingNotes;
  assert.deepEqual(started, ['notes', 'release']);
  releaseNotes();
  await pending;
  assert.match(system, /^Latest release: 3\.4\.5\./m);
} finally {
  releaseNotes();
  globalThis.fetch = fetchSaved;
  env.KNOWLEDGE_URL = notesUrl;
  env.RELEASE_URL = releaseUrl;
}
console.log('PASS notes and release fetches start together');

// Each tracked problem has its own note, shown for its symptom with its workaround, without another developer ping.
says = 'That is a known problem being worked on. LINK: Troubleshooting';
const tracked = [
  ["she won't open any app", 255], ['she says she opened an app and nothing happens', 255],
  ["she doesn't go to sleep any more", 249], ['she stopped going to sleep since 0.8.9', 249],
  ["I can't uninstall her", 248], ['clicking Uninstall opens her again', 248],
  ['on calls she takes clear speech for static', 172], ['diagrams stay on Waiting for NVIDIA', 166],
  ['she is invisible in Ghostrunner 2 on DirectX 11', 159], ['she floats above the taskbar when I come back', 156],
  ['she teleports into the air and sits on nothing', 155], ['Valorant crashes when her settings open', 110],
  ['she clicks the wrong button in game menus', 108], ["This app can't run on your PC", 104],
  ["she gets stuck and won't quit", 103], ['Mac keeps asking to see the screen', 105],
  ['Mac updates do not install themselves yet', 236], ['mouse and keyboard setup fails on Ubuntu GNOME Wayland', 250],
  ['she is not drawn on niri', 252]
];
for (const ask of [discord, help]) {
  for (const [question, issue] of tracked) {
    got = await ask({ question });
    const known = [...system.matchAll(/\[Troubleshooting > [^\n]*\]\n(Known problem\. [^\n]*)/g)].map(found => found[1]);
    assert.ok(known.some(body => body.endsWith('https://github.com/lachydotmcg/capri-desktop/issues/' + issue)), question);
    assert.match(system, /say it is known and being worked on and give only its noted workaround/);
    assert.equal(got.ping, ask === discord ? null : undefined);
  }
}
await discord({ question: 'she says she opened an app and nothing happens' });
assert.match(system, /Until it's fixed, try asking again in text/);
await discord({ question: 'she stopped going to sleep since 0.8.9' });
assert.match(system, /Move > Go to sleep puts her to sleep/);
await discord({ question: 'Mac updates do not install themselves yet' });
assert.match(system, /quit Capri and replace the old app with the new download/);
await discord({ question: 'Valorant crashes when her settings open' });
assert.match(system, /Never use PING: developer for a Known problem note/);
await help({ question: 'Valorant crashes when her settings open' });
assert.doesNotMatch(system, /PING/);
console.log('PASS each tracked problem shows its own note without asking for another developer ping');

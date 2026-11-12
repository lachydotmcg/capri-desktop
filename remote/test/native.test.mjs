// Phone-only mode (site/remote/native.js) in a real browser: headless Chrome opens the phone page from a local copy of the
// site, a stand-in provider answers (lib/fake-provider.mjs), and the test sets her up with a key, chats, reloads, changes
// brain, hits an error and switches between a PC (the demo's) and this phone. It also starts free, checks the daily
// allowance and its two limit replies, and switches to a key. Her persona is checked word for word
// against app/Brain.cs. Ports 8792 to 8794. Skipped where Chrome isn't installed.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { chromePath, launch } from './lib/cdp.mjs';
import { BAD, GOOD, startProvider } from './lib/fake-provider.mjs';
import { ROOT, SITE, earlyAccess, startSite } from './lib/site.mjs';

const SITE_PORT = 8792, PROVIDER_PORT = 8793, CHROME_PORT = 8794;
const SHOTS = process.env.CAPRI_PHONE_SHOTS || '';

// Her persona's parts as Brain.cs writes them: each const is C# string literals joined with +, in JSON's escapes.
const brainConst = name => {
  const source = readFileSync(join(ROOT, 'app', 'Brain.cs'), 'utf8');
  const start = source.indexOf('internal const string ' + name + ' =');
  assert.ok(start >= 0, name + ' is in Brain.cs');
  const end = source.indexOf('";', start) + 1;
  const literals = source.slice(start, end).match(/"((?:[^"\\]|\\.)*)"/g);
  return literals.map(literal => JSON.parse(literal)).join('');
};

test('phone-only mode: her own brain on the phone, with no PC', { skip: !chromePath() && 'Chrome is not installed', timeout: 120000 }, async () => {
  const provider = await startProvider(PROVIDER_PORT);
  const site = await startSite(SITE_PORT);
  const page = await launch({ port: CHROME_PORT });
  const seen = [];
  page.on((method, params) => {
    if (method === 'Network.requestWillBeSent') seen.push(params.request.url);
    if (method === 'Network.webSocketCreated') seen.push(params.url);
  });
  const shot = async name => { if (SHOTS) { mkdirSync(SHOTS, { recursive: true }); await page.shot(join(SHOTS, name + '.png')); } };
  const click = selector => page.run(`(() => { const node = document.querySelector(${JSON.stringify(selector)}); if (!node) throw new Error('no ${selector}'); node.click(); return true; })()`);
  const type = (selector, text) => page.run(`(() => { const node = document.querySelector(${JSON.stringify(selector)}); node.value = ${JSON.stringify(text)}; node.dispatchEvent(new Event('input', { bubbles: true })); return true; })()`);
  const text = selector => page.run(`(document.querySelector(${JSON.stringify(selector)}) || {}).textContent || ''`);
  const say = async words => {
    await type('#msg', words);
    await click('#send');
  };
  const lastReply = `(() => { const all = [...document.querySelectorAll('.msg.capri.done')]; const last = all[all.length - 1]; return last ? last.textContent : ''; })()`;
  try {
    // The gate is passed the way the page keeps it, with its own hash: the password never goes in a test.
    await page.send('Page.addScriptToEvaluateOnNewDocument', { source: earlyAccess() });
    await page.go(`http://127.0.0.1:${SITE_PORT}/?provider=http://127.0.0.1:${PROVIDER_PORT}&count=http://127.0.0.1:${PROVIDER_PORT}/count`);
    await page.until(`document.querySelector('#app').dataset.screen === 'pair'`, 'the pair screen');
    assert.equal(await text('#native-start'), 'Use her without a PC');
    await shot('phone-pair');

    // Its home screen button shows the guide here too, before SETTINGS has ever been pressed.
    await click('#native-start');
    await page.until(`!document.querySelector('#brain').hidden`, 'SETTINGS from the pair screen');
    await click('#brain .native-home');
    await page.until(`!document.querySelector('#home').hidden && document.querySelector('#brain').hidden`, 'the home screen guide');
    await click('#home-done');
    await page.until(`document.querySelector('#home').hidden`, 'the guide closed');

    // A wrong key: her Mainframe's words, and nothing saved.
    await click('#native-start');
    await page.until(`!document.querySelector('#brain').hidden`, 'SETTINGS');
    assert.equal(await page.run(`document.querySelector('.native-modes .nbtn[aria-pressed="true"]').textContent`), 'This phone');
    await click('#brain .native-own-key');
    await type('#brain-key', BAD);
    await click('.native-row .nbtn.solid');
    await page.until(`/rejected the key/.test(document.querySelector('#brain .native-status').textContent)`, 'the bad key note');
    assert.equal(await text('#brain .native-status'), 'Anthropic rejected the key. Paste it again in SETTINGS.');
    assert.equal(await page.run(`localStorage.getItem('capri-remote-brain')`), null);

    // A good key: she runs here, on Claude Haiku 5.5, the desktop's pick for Anthropic.
    await type('#brain-key', GOOD.anthropic);
    await click('.native-row .nbtn.solid');
    await page.until(`document.querySelector('#app').dataset.link === 'connected' && document.querySelector('#app').dataset.screen === 'chat'`, 'the chat on this phone');
    assert.equal(await page.run(`document.querySelector('#brain').hidden`), true);
    assert.equal(await text('#native-mode'), 'This phone');
    assert.equal(JSON.parse(await page.run(`localStorage.getItem('capri-remote-brain')`)).model, 'claude-haiku-5-5');
    assert.equal(await text('.starters .nbtn'), 'rate my day so far');
    await shot('phone-chat-empty');

    // The daily count: nothing goes before her first reply that worked, and the first try gets a 503.
    const counts = () => provider.got.filter(item => item.path === '/count/phone-day');
    const until = async (check, what) => {
      for (const end = Date.now() + 8000; !check();) {
        if (Date.now() > end) throw new Error('Timed out waiting for ' + what);
        await new Promise(resolve => setTimeout(resolve, 60));
      }
    };
    const quiet = async () => {
      assert.equal(await page.run(`(() => { const toast = document.querySelector('.native-toast'); return !!toast && !toast.hidden; })()`), false, 'a toast showed');
      assert.equal(await page.run(`[...document.querySelectorAll('.msg.capri .cut')].filter(node => node.textContent).length`), 0, 'her reply was cut');
    };
    const counted = async () => JSON.parse(await page.run(`localStorage.getItem('capri-remote-counted')`));
    const day = Math.floor(Date.now() / 86400000);
    const newDay = sent => page.run(`localStorage.setItem('capri-remote-counted', ${JSON.stringify(JSON.stringify({ day: day - 1, sent }))})`);
    assert.equal(counts().length, 0);
    assert.equal(await counted(), null);
    provider.state.count = 503;
    await say('hello from the phone test');
    await page.until(`${lastReply} === 'Capri: Got it: hello from the phone test'`, 'her first streamed reply');
    let asked = provider.got.filter(item => item.path === '/anthropic/messages');
    assert.equal(asked.length, 1);
    const first = asked[0];
    assert.equal(first.headers['x-api-key'], GOOD.anthropic);
    assert.equal(first.headers['anthropic-version'], '2026-10-21');
    assert.equal(first.headers['anthropic-dangerous-direct-browser-access'], 'true');
    assert.equal(first.body.model, 'claude-haiku-5-5');
    assert.equal(first.body.stream, true);
    assert.deepEqual(first.body.output_config, { effort: 'low' });
    assert.equal(first.body.temperature, undefined);
    assert.deepEqual(first.body.messages, [{ role: 'user', content: 'hello from the phone test' }]);
    // Her persona, word for word from Brain.cs: personality, safety lines and machinery without its ElevenLabs line.
    const machinery = brainConst('Machinery').split('\n').slice(0, 2).join('\n');
    assert.ok(first.body.system.startsWith(brainConst('DefaultPersonality') + '\n' + brainConst('Safety') + '\n' + machinery + '\n'), 'her persona matches Brain.cs');
    assert.ok(!/ElevenLabs/.test(first.body.system));
    // The count carries its two flags and nothing else: no key, no chat, no cookie or referrer.
    await until(() => counts().length === 1, 'the first count');
    assert.equal(counts()[0].method, 'POST');
    assert.deepEqual(counts()[0].body, { first: true, paired: false });
    assert.match(counts()[0].headers['content-type'], /^text\/plain/);
    for (const name of ['x-api-key', 'authorization', 'cookie', 'referer']) assert.equal(counts()[0].headers[name], undefined, name);
    assert.ok(!JSON.stringify(counts()[0]).includes(GOOD.anthropic) && !JSON.stringify(counts()[0]).includes('hello'));
    await new Promise(resolve => setTimeout(resolve, 300));
    await quiet();
    assert.deepEqual(await counted(), { day, sent: false });

    // Her memory is the chat on screen.
    await say('and a second one');
    await page.until(`${lastReply} === 'Capri: Got it: and a second one'`, 'her second reply');
    asked = provider.got.filter(item => item.path === '/anthropic/messages');
    assert.deepEqual(asked[1].body.messages, [
      { role: 'user', content: 'hello from the phone test' },
      { role: 'assistant', content: 'Got it: hello from the phone test' },
      { role: 'user', content: 'and a second one' }
    ]);
    // One try a day: her second reply sends nothing.
    await new Promise(resolve => setTimeout(resolve, 300));
    assert.equal(counts().length, 1);
    await shot('phone-chat');

    // A reload keeps her on this phone with the chat (saved 200 ms after it changes).
    await new Promise(resolve => setTimeout(resolve, 500));
    await page.go(`http://127.0.0.1:${SITE_PORT}/?provider=http://127.0.0.1:${PROVIDER_PORT}&count=http://127.0.0.1:${PROVIDER_PORT}/count`);
    await page.until(`document.querySelector('#app').dataset.link === 'connected' && document.querySelectorAll('.msg.you').length === 2`, 'her chat after a reload');
    assert.equal(await text('#native-mode'), 'This phone');

    // A reload the same day sends nothing either.
    await say('after the reload');
    await page.until(`${lastReply} === 'Capri: Got it: after the reload'`, 'her reply after the reload');
    await new Promise(resolve => setTimeout(resolve, 300));
    assert.equal(counts().length, 1);
    // The next day the line drops (Chrome tries once more by itself): nothing shows, and the phone still hasn't told it once.
    await newDay(false);
    provider.state.count = 'drop';
    await say('a new day');
    await page.until(`${lastReply} === 'Capri: Got it: a new day'`, 'her reply on a new day');
    await until(() => counts().length >= 2, "the second day's count");
    await new Promise(resolve => setTimeout(resolve, 300));
    for (const sent of counts().slice(1)) assert.deepEqual(sent.body, { first: true, paired: false });
    const base = counts().length;
    await quiet();
    assert.deepEqual(await counted(), { day, sent: false });
    // The day after, it works, and this phone is no longer a first-timer.
    await newDay(false);
    provider.state.count = 0;
    await say('third time lucky');
    await page.until(`${lastReply} === 'Capri: Got it: third time lucky'`, 'her reply on a third day');
    await until(() => counts().length === base + 1, 'the third day\'s count');
    assert.deepEqual(counts()[base].body, { first: true, paired: false });
    await page.until(`JSON.parse(localStorage.getItem('capri-remote-counted')).sent === true`, 'the count remembered');
    assert.deepEqual(await counted(), { day, sent: true });

    // Another brain: an OpenRouter key picks its provider by itself, and the desktop's pick (free, sees, uses tools).
    await click('#f-settings');
    await page.until(`!document.querySelector('#brain').hidden`, 'SETTINGS again');
    await type('#brain-key', GOOD.openrouter);
    assert.equal(await page.run(`document.querySelector('#brain-provider').value`), 'openrouter');
    await shot('phone-settings');
    await click('.native-row .nbtn.solid');
    await page.until(`document.querySelector('#brain').hidden && JSON.parse(localStorage.getItem('capri-remote-brain')).provider === 'openrouter'`, 'the OpenRouter key');
    assert.equal(JSON.parse(await page.run(`localStorage.getItem('capri-remote-brain')`)).model, 'free/sees-and-acts:free');
    await say('hi router');
    await page.until(`${lastReply} === 'Capri: Router says: hi router'`, 'the OpenRouter reply');
    const routed = provider.got.filter(item => item.path === '/openrouter/chat/completions');
    assert.equal(routed.length, 1);
    assert.equal(routed[0].headers.authorization, 'Bearer ' + GOOD.openrouter);
    assert.equal(routed[0].headers['x-title'], 'Capri');
    assert.equal(routed[0].body.messages[0].role, 'system');
    assert.ok(routed[0].body.messages[0].content.includes(brainConst('Safety')));
    assert.equal(routed[0].body.max_tokens, 2000);
    assert.deepEqual(routed[0].body.messages.slice(-1), [{ role: 'user', content: 'hi router' }]);

    // A provider's refusal shows in the notice at the end of the chat, never as a reply from her.
    const capriBubbles = await page.run(`document.querySelectorAll('.msg.capri').length`);
    await say('make it fail');
    await page.until(`(() => { const node = document.querySelector('.native-notice'); return !!node && !node.hidden; })()`, 'the rate limit notice');
    assert.equal(await text('.native-notice-words'), "OpenRouter says that's too many messages for one minute. Give it a minute, then try again.");
    assert.equal(await page.run(`document.querySelectorAll('.msg.capri').length`), capriBubbles);
    assert.equal(await page.run(`[...document.querySelectorAll('.msg.capri .cut')].filter(node => node.textContent).length`), 0);
    // Seeing the screen needs her PC.
    await click('#f-screen');
    assert.equal(await text('.native-toast'), 'Seeing your screen needs Capri on your PC. Switch in SETTINGS.');

    // Back to a PC: with none paired, the pair screen, and the key stays on this phone. The demo stands in for a PC.
    await click('#f-settings');
    await page.until(`!document.querySelector('#brain').hidden`, 'SETTINGS for the PC');
    await click('.native-modes .nbtn:first-child');
    assert.equal(await text('#brain .native-use-pc'), 'Pair your PC');
    await click('#brain .native-use-pc');
    await page.until(`document.querySelector('#app').dataset.screen === 'pair'`, 'the pair screen again');
    assert.ok(await page.run(`!!localStorage.getItem('capri-remote-brain')`));
    await click('#try-demo');
    await page.until(`document.querySelector('#app').dataset.link === 'connected' && !document.querySelector('#native-mode').hidden`, 'the demo PC');
    assert.equal(await text('#native-mode'), 'Demo PC');
    assert.equal(await page.run(`CapriRemote.info.native`), false);
    // On a PC, the demo's here, nothing is counted, even on a new day.
    await newDay(true);
    const before = await page.run(`document.querySelectorAll('.msg.capri.done').length`);
    await say('hello demo');
    await page.until(`document.querySelectorAll('.msg.capri.done').length > ${before}`, 'the demo\'s reply');
    await new Promise(resolve => setTimeout(resolve, 500));
    assert.equal(counts().length, base + 1);
    await click('#f-settings');
    await page.until(`!document.querySelector('#brain').hidden`, 'SETTINGS on the PC');
    await click('.native-modes .nbtn:last-child');
    await click('.native-row .nbtn.solid');
    await page.until(`CapriRemote.info.native === true && document.querySelector('#native-mode').textContent === 'This phone'`, 'this phone again');
    // Back on this phone it counts, and the demo isn't a PC this phone is paired with.
    await say('back on the phone');
    await page.until(`${lastReply} === 'Capri: Router says: back on the phone'`, 'her reply back on the phone');
    await until(() => counts().length === base + 2, 'the count back on the phone');
    assert.deepEqual(counts()[base + 1].body, { first: false, paired: false });
    // A phone that's also paired with a real PC says so.
    await page.run(`localStorage.setItem('capri-remote-pairing', JSON.stringify({ demo: false, device: true, pc: 'Test PC', pairedAt: Date.now(), allowed: true }))`);
    await newDay(true);
    await say('paired too');
    await page.until(`${lastReply} === 'Capri: Router says: paired too'`, 'her reply when paired too');
    await until(() => counts().length === base + 3, 'the paired phone\'s count');
    assert.deepEqual(counts()[base + 2].body, { first: false, paired: true });

    // The key went to the provider only: never in an address, and nothing left for anywhere but these two ports.
    for (const url of seen.concat(site.asked)) {
      for (const key of [GOOD.anthropic, GOOD.openrouter, BAD]) assert.ok(!url.includes(key), 'a key in ' + url);
      assert.ok(/^(data:|about:|http:\/\/127\.0\.0\.1:(8792|8793)\/|\/)/.test(url), 'the page reached ' + url);
    }
  } catch (error) {
    // Where the page was, for the failure's message.
    let where = '';
    try { where = await page.run(`JSON.stringify({ link: document.querySelector('#app').dataset.link, screen: document.querySelector('#app').dataset.screen, mode: localStorage.getItem('capri-remote-mode'), you: document.querySelectorAll('.msg.you').length, chats: (localStorage.getItem('capri-remote-chats') || '').length, counted: localStorage.getItem('capri-remote-counted') })`); } catch {}
    error.message += ' | page: ' + where;
    throw error;
  } finally {
    await page.close();
    await site.close();
    await provider.close();
  }
});

test('phone-only mode: start free, see the allowance and switch to a key', { skip: !chromePath() && 'Chrome is not installed', timeout: 120000 }, async () => {
  const provider = await startProvider(PROVIDER_PORT);
  const site = await startSite(SITE_PORT);
  const page = await launch({ port: CHROME_PORT });
  const seen = [];
  page.on((method, params) => { if (method === 'Network.requestWillBeSent') seen.push(params.request.url); });
  const url = `http://127.0.0.1:${SITE_PORT}/?provider=http://127.0.0.1:${PROVIDER_PORT}&free=http://127.0.0.1:${PROVIDER_PORT}&count=http://127.0.0.1:${PROVIDER_PORT}/count`;
  const click = selector => page.run(`document.querySelector(${JSON.stringify(selector)}).click()`);
  const type = (selector, text) => page.run(`(() => { const node = document.querySelector(${JSON.stringify(selector)}); node.value = ${JSON.stringify(text)}; node.dispatchEvent(new Event('input', { bubbles: true })); })()`);
  const text = selector => page.run(`document.querySelector(${JSON.stringify(selector)}).textContent`);
  const say = async words => { await type('#msg', words); await click('#send'); };
  const lastReply = `(() => { const all = [...document.querySelectorAll('.msg.capri.done')]; return all.length ? all[all.length - 1].textContent : ''; })()`;
  const requests = method => provider.got.filter(item => item.path === '/free' && item.method === method);
  const counts = () => provider.got.filter(item => item.path === '/count/phone-day');
  const freeWords = "Capri's developers provide this AI usage for free, however it is limited because it costs real money.";
  const kofiWords = "If you'd like to help keep it free, you can support Capri on Ko-fi.";
  const shot = async name => { if (SHOTS) { mkdirSync(SHOTS, { recursive: true }); await page.shot(join(SHOTS, name + '.png')); } };
  const shown = selector => page.run(`(() => { const node = document.querySelector(${JSON.stringify(selector)}); return !!node && node.getClientRects().length > 0; })()`);
  try {
    await page.send('Page.addScriptToEvaluateOnNewDocument', { source: earlyAccess() });
    await page.send('Emulation.setTimezoneOverride', { timezoneId: 'Australia/Sydney' });
    await page.go(url);
    await page.until(`document.querySelector('#app').dataset.screen === 'pair'`, 'the pair screen');
    await click('#native-start');
    await page.until(`!document.querySelector('#brain').hidden`, 'the free choice');
    assert.equal(await text('#brain .native-free'), 'Start free');
    assert.equal(await text('#brain .native-free-words'), freeWords);
    assert.equal(await text('#brain .native-own-key'), 'or use your own key');
    assert.equal(await page.run(`document.querySelector('#native-key-part').hidden`), true);
    // The start screen has his words alone: no allowance and no Ko-fi line until the free brain is in use.
    assert.equal(await shown('#brain .native-free-kofi'), false);
    assert.equal(await shown('#brain .native-free-left'), false);
    assert.equal(provider.got.length, 0);
    await shot('free-1-start');
    await click('#brain .native-free');
    await page.until(`CapriRemote.info.native === true && document.querySelector('#app').dataset.screen === 'chat'`, 'free chat without a key');
    assert.equal(await page.run(`localStorage.getItem('capri-remote-free')`), '1');
    assert.equal(await page.run(`localStorage.getItem('capri-remote-brain')`), null);
    assert.equal(await text('#native-mode'), 'This phone');
    assert.equal(counts().length, 0);

    // A whole reply uses the same typing state, history and daily phone count as a keyed brain.
    provider.state.free.wait = 300;
    await say('hello free brain');
    await page.until(`!!document.querySelector('.msg.capri.thinking')`, 'her typing state');
    await page.until(`${lastReply} === 'Capri: Free says: hello free brain'`, 'her whole free reply');
    await page.until(`JSON.parse(localStorage.getItem('capri-remote-counted')).sent === true`, 'the free phone counted');
    assert.deepEqual(requests('POST')[0].body, { messages: [{ role: 'user', content: 'hello free brain' }] });
    // A plain-text POST, so no preflight goes before each message.
    assert.equal(requests('POST')[0].headers['content-type'], 'text/plain');
    assert.equal(provider.got.filter(item => item.path === '/free' && item.method === 'OPTIONS').length, 0);
    await shot('free-2-chat');
    for (const name of ['x-api-key', 'authorization', 'cookie', 'referer']) assert.equal(requests('POST')[0].headers[name], undefined, name);
    assert.equal(counts().length, 1);
    assert.deepEqual(counts()[0].body, { first: true, paired: false });

    // Opening SETTINGS reads the server, rather than just repeating the number from her last reply.
    provider.state.free.left = 7;
    await click('#f-settings');
    await page.until(`document.querySelector('.native-free-left').textContent === 'Free messages today: 7 of 15 left'`, 'the allowance read from GET');
    assert.equal(requests('GET').length, 1);
    assert.equal(await text('.native-free-words'), freeWords);
    // Midnight UTC in Sydney in November is 11:00 am daylight time, today or tomorrow there.
    const sydneyDay = date => new Intl.DateTimeFormat('en-CA', { timeZone: 'Australia/Sydney', year: 'numeric', month: '2-digit', day: '2-digit' }).format(date);
    const plain = words => words.replace(/\s/g, ' ');
    // The time is the phone's own, with no zone code: 11:00 or 11:00 AM, as this browser writes it.
    const elevenLocal = plain(await page.run(`new Date(${JSON.stringify(provider.state.free.resets)}).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })`));
    assert.match(elevenLocal, /^11:00( AM)?$/);
    const localReset = 'at ' + elevenLocal + ' ' + (sydneyDay(new Date(provider.state.free.resets)) === sydneyDay(new Date()) ? 'today' : 'tomorrow');
    assert.equal(plain(await text('.native-free-reset')), 'Resets ' + localReset + '.');
    // His words, then the Ko-fi line straight after them, under the allowance, with a plain link to his page.
    assert.equal(await text('.native-free-kofi'), kofiWords);
    assert.equal(await page.run(`document.querySelector('.native-free-words').nextElementSibling.className`), 'native-note native-free-kofi');
    assert.equal(await page.run(`!!(document.querySelector('.native-free-left').compareDocumentPosition(document.querySelector('.native-free-words')) & Node.DOCUMENT_POSITION_FOLLOWING)`), true);
    assert.deepEqual(await page.run(`(() => { const link = document.querySelector('.native-free-kofi a'); return [link.textContent, link.href, link.target, link.rel]; })()`), ['Ko-fi', 'https://ko-fi.com/lachysdev', '_blank', 'noopener noreferrer']);
    assert.equal(await shown('.native-free-kofi'), true);
    await shot('free-3-settings');
    // A reply updates the open sheet too, with no extra GET.
    await say('one more for free');
    await page.until(`${lastReply} === 'Capri: Free says: one more for free'`, 'another free reply');
    assert.equal(await text('.native-free-left'), 'Free messages today: 6 of 15 left');
    assert.equal(requests('GET').length, 1);
    assert.deepEqual(requests('POST')[1].body.messages, [
      { role: 'user', content: 'hello free brain' }, { role: 'assistant', content: 'Free says: hello free brain' },
      { role: 'user', content: 'one more for free' }
    ]);
    await click('#brain .native-done');
    await new Promise(resolve => setTimeout(resolve, 500));
    await page.go(url);
    await page.until(`CapriRemote.info.native === true && document.querySelectorAll('.msg.you').length === 2`, 'free chat kept after reload');

    // Limit lines show in the notice in her words, never as a saved reply sent back as chat, and never count as a
    // successful phone reply.
    await page.run(`localStorage.setItem('capri-remote-counted', JSON.stringify({ day: Math.floor(Date.now() / 86400000) - 1, sent: true }))`);
    const herReplies = await page.run(`document.querySelectorAll('.msg.capri').length`);
    provider.state.free.left = 0;
    await say('used up');
    await page.until(`(() => { const node = document.querySelector('.native-notice'); return !!node && !node.hidden && /That's all your free messages/.test(node.textContent); })()`, 'her used-up notice');
    assert.equal(plain(await text('.native-notice-words')), "That's all your free messages for today. They're back " + localReset + '. Add your own AI key in SETTINGS for no limit.');
    assert.equal(await page.run(`document.querySelector('.native-notice-free').hidden`), true);
    await shot('free-4-used');
    provider.state.free.left = 9;
    provider.state.free.open = false;
    await say('pool used up');
    await page.until(`/Everyone's free messages for today are gone\./.test(document.querySelector('.native-notice-words').textContent)`, 'her shared pool notice');
    assert.ok(plain(await text('.native-notice-words')).includes(localReset));
    assert.equal(await page.run(`document.querySelectorAll('.msg.capri').length`), herReplies);
    assert.deepEqual(requests('POST').at(-1).body.messages.slice(-1), [{ role: 'user', content: 'used up\n\npool used up' }]);
    assert.equal(counts().length, 1);
    assert.equal(await page.run(`[...document.querySelectorAll('.msg.capri .cut')].filter(node => node.textContent).length`), 0);
    assert.equal(await page.run(`!!document.querySelector('.native-toast:not([hidden])')`), false);

    // Switching to a key uses the existing check and model choice, and future chats go straight to that provider.
    await click('#f-settings');
    await page.until(`document.querySelector('.native-free-left').textContent === 'Free messages today: 9 of 15 left'`, 'the pool allowance');
    assert.equal(await page.run(`document.querySelector('.native-free-pool').hidden`), false);
    assert.equal(await text('.native-own-key'), 'Switch to your own key');
    await click('.native-own-key');
    assert.equal(await page.run(`document.querySelector('#native-key-part').hidden`), false);
    await type('#brain-key', GOOD.anthropic);
    await click('.native-row .nbtn.solid');
    await page.until(`document.querySelector('#brain').hidden && JSON.parse(localStorage.getItem('capri-remote-brain')).provider === 'anthropic'`, 'the key saved');
    assert.equal(JSON.parse(await page.run(`localStorage.getItem('capri-remote-brain')`)).model, 'claude-haiku-5-5');
    const freePosts = requests('POST').length;
    await say('back with a key');
    // The two messages the free brain turned away were never answered, so they go with this one.
    await page.until(`${lastReply} === 'Capri: Got it: used up\\n\\npool used up\\n\\nback with a key'`, 'her keyed reply');
    assert.equal(requests('POST').length, freePosts);
    const freeGets = requests('GET').length;
    await click('#f-settings');
    assert.equal(await page.run(`document.querySelector('.native-free-left').parentNode.hidden`), true);
    assert.equal(await shown('.native-free-kofi'), false);
    assert.equal(requests('GET').length, freeGets);
    // Back to free and to the key again: trying the free brain never throws the saved key away.
    assert.equal(await text('#brain .native-free'), 'Use free brain');
    await click('#brain .native-free');
    await page.until(`document.querySelector('#brain').hidden && localStorage.getItem('capri-remote-free') === '1'`, 'back on the free brain');
    assert.equal(JSON.parse(await page.run(`localStorage.getItem('capri-remote-brain')`)).key, GOOD.anthropic);
    await page.until(`CapriRemote.info.native === true && document.querySelector('#app').dataset.link === 'connected'`, 'free brain connected');
    provider.state.free.open = true;
    await say('free once more');
    await page.until(`${lastReply} === 'Capri: Free says: free once more'`, 'a free reply after the key');
    await click('#f-settings');
    await page.until(`!document.querySelector('#brain').hidden`, 'SETTINGS on free');
    await click('.native-own-key');
    assert.equal(await page.run(`document.querySelector('#brain-key').placeholder`), 'Key saved');
    assert.equal(await text('.native-row .nbtn.solid'), 'Use this phone');
    assert.equal(await shown('.native-row .nbtn.hot'), true);
    await click('.native-row .nbtn.solid');
    await page.until(`document.querySelector('#brain').hidden && localStorage.getItem('capri-remote-free') === null`, 'the saved key back in use');
    await say('the key again');
    await page.until(`${lastReply} === 'Capri: Got it: the key again'`, 'a keyed reply with no retyping');
    // A browser that saves nothing gets a plain line, not a silent trip back to the pair screen.
    await page.run(`Storage.prototype.setItem = () => { throw new Error('QuotaExceededError'); }`);
    await click('#f-settings');
    await page.until(`!document.querySelector('#brain').hidden`, 'SETTINGS with no storage');
    await click('#brain .native-free');
    await page.until(`(() => { const toast = document.querySelector('.native-toast'); return !!toast && !toast.hidden && /won't save settings/.test(toast.textContent); })()`, 'the no-storage line');
    assert.equal(await page.run(`localStorage.getItem('capri-remote-free')`), null);
    assert.equal(await page.run(`document.querySelector('#brain').hidden`), false);
    for (const request of requests('GET').concat(requests('POST'))) for (const name of ['x-api-key', 'authorization', 'cookie', 'referer']) assert.equal(request.headers[name], undefined, name);
    for (const address of seen.concat(site.asked)) assert.ok(/^(data:|about:|http:\/\/127\.0\.0\.1:(8792|8793)\/|\/)/.test(address), 'the page reached ' + address);
  } finally {
    await page.close();
    await site.close();
    await provider.close();
  }
});

// Real users on free keys got nothing but "[TOO MANY REQUESTS]" (12 Nov). Each kind of refusal now says what it is, in
// the notice, with the service's answer behind Details and no key in it; nothing is saved as her reply or sent back as
// chat; a wait the service asks for is kept with no call; nothing is sent again by itself; and the free brain is one tap.
test('phone-only mode: a refused key says which limit it hit, and offers free messages', { skip: !chromePath() && 'Chrome is not installed', timeout: 120000 }, async () => {
  const provider = await startProvider(PROVIDER_PORT);
  const site = await startSite(SITE_PORT);
  const page = await launch({ port: CHROME_PORT });
  const url = `http://127.0.0.1:${SITE_PORT}/?provider=http://127.0.0.1:${PROVIDER_PORT}&free=http://127.0.0.1:${PROVIDER_PORT}&count=http://127.0.0.1:${PROVIDER_PORT}/count`;
  const click = selector => page.run(`document.querySelector(${JSON.stringify(selector)}).click()`);
  const type = (selector, text) => page.run(`(() => { const node = document.querySelector(${JSON.stringify(selector)}); node.value = ${JSON.stringify(text)}; node.dispatchEvent(new Event('input', { bubbles: true })); })()`);
  const text = selector => page.run(`document.querySelector(${JSON.stringify(selector)}).textContent`);
  const say = async words => { await type('#msg', words); await click('#send'); };
  const shot = async name => { if (SHOTS) { mkdirSync(SHOTS, { recursive: true }); await page.shot(join(SHOTS, name + '.png')); } };
  const lastReply = `(() => { const all = [...document.querySelectorAll('.msg.capri.done')]; return all.length ? all[all.length - 1].textContent : ''; })()`;
  const chats = () => provider.got.filter(item => item.path === '/google/chat/completions');
  const shown = `(() => { const node = document.querySelector('.native-notice'); return !!node && !node.hidden; })()`;
  const noticeSays = async (pattern, what) => { await page.until(`${shown} && ${pattern}.test(document.querySelector('.native-notice-words').textContent)`, what); return text('.native-notice-words'); };
  const saved = () => page.run(`JSON.parse(localStorage.getItem('capri-remote-chats') || 'null')`);
  const google = (status, message, quotaId, more = {}) => ({ status, body: [{ error: { code: status, message, status: 'RESOURCE_EXHAUSTED', details: quotaId ? [{ '@type': 'type.googleapis.com/google.rpc.QuotaFailure', violations: [{ quotaMetric: 'generativelanguage.googleapis.com/generate_content_free_tier_requests', quotaId, quotaValue: more.value || '20' }] }] : [] } }], headers: more.headers || {} });
  try {
    await page.send('Page.addScriptToEvaluateOnNewDocument', { source: earlyAccess() });
    await page.send('Emulation.setTimezoneOverride', { timezoneId: 'Australia/Sydney' });
    await page.go(url);
    await page.until(`document.querySelector('#app').dataset.screen === 'pair'`, 'the pair screen');

    // New free keys start where the free allowance is: Gemini's newest stable Flash-Lite, OpenRouter's free router.
    assert.equal(await page.run(`CapriNative.pickModel('google', ['gemini-3.8-flash', 'gemini-3.5-flash-lite', 'gemini-3.6-flash-lite-preview', 'gemini-2.5-flash-lite', 'gemini-3.1-pro-preview'].map(id => ({ id })))`), 'gemini-3.5-flash-lite');
    assert.equal(await page.run(`CapriNative.pickModel('openrouter', [{ id: 'big/sees:free', images: true, tools: true }, { id: 'openrouter/free', images: true, tools: true }])`), 'openrouter/free');
    assert.equal(await page.run(`CapriNative.pickModel('openrouter', [{ id: 'paid/x', images: true, tools: true }, { id: 'big/sees:free', images: true, tools: true }])`), 'big/sees:free');
    await click('#native-start');
    await page.until(`!document.querySelector('#brain').hidden`, 'SETTINGS');
    await click('#brain .native-own-key');
    await type('#brain-key', GOOD.google);
    await click('.native-row .nbtn.solid');
    await page.until(`document.querySelector('#brain').hidden && JSON.parse(localStorage.getItem('capri-remote-brain') || '{}').provider === 'google'`, 'the Gemini key');
    assert.equal(JSON.parse(await page.run(`localStorage.getItem('capri-remote-brain')`)).model, 'gemini-3.5-flash-lite');
    await say('hello gemini');
    await page.until(`${lastReply} === 'Capri: Gemini says: hello gemini'`, 'a good reply');
    const herReplies = await page.run(`document.querySelectorAll('.msg.capri').length`);

    // Too many this minute, with the wait the service asked for: one call, then no call until the wait is over.
    provider.state.fail = google(429, 'Quota exceeded for metric: generativelanguage.googleapis.com/generate_content_free_tier_requests, limit: 5, model: gemini-3.5-flash-lite. Please retry in 2.4s.', 'GenerateRequestsPerMinutePerProjectPerModel-FreeTier', { value: '5' });
    await say('one');
    assert.equal(await noticeSays('/one minute/', 'the per-minute notice'), "Google AI says that's too many messages for one minute. Try again in a few seconds.");
    assert.equal(await page.run(`document.querySelector('.native-notice-free').hidden`), false);
    await shot('limit-minute');
    assert.equal(chats().length, 2);
    await say('two');
    await noticeSays('/Try again in a few seconds/', 'the wait kept');
    assert.equal(chats().length, 2, 'no call while the service asked her to wait');
    // Nothing went in the chat as hers, and nothing is sent again by itself.
    assert.equal(await page.run(`document.querySelectorAll('.msg.capri').length`), herReplies);
    await page.until(`!!localStorage.getItem('capri-remote-chats')`, 'the chat saved');
    await new Promise(resolve => setTimeout(resolve, 300));
    assert.ok(!(await saved()).chats.some(chat => chat.msgs.some(msg => msg.role === 'capri' && (msg.cut || !msg.text))));
    await new Promise(resolve => setTimeout(resolve, 3200));
    assert.equal(chats().length, 2, 'never retried by itself');
    provider.state.fail = null;
    await say('three');
    await page.until(`${lastReply} === 'Capri: Gemini says: one\\n\\ntwo\\n\\nthree'`, 'the next good reply');
    assert.equal(await page.run(shown), false, 'a good reply clears the notice');
    assert.deepEqual(chats().at(-1).body.messages.slice(1), [
      { role: 'user', content: 'hello gemini' }, { role: 'assistant', content: 'Gemini says: hello gemini' }, { role: 'user', content: 'one\n\ntwo\n\nthree' }
    ]);

    // Today's free messages on this key are gone: waiting a minute won't help, and it says so. The service's own answer
    // is behind Details, with the key taken out.
    provider.state.fail = google(429, `You exceeded your current quota. Quota exceeded for metric: generativelanguage.googleapis.com/generate_content_free_tier_requests, limit: 20, model: gemini-3.5-flash-lite. key=${GOOD.google} Please retry in 41.3s.`, 'GenerateRequestsPerDayPerProjectPerModel-FreeTier');
    await say('daily');
    // Google's day ends at midnight US Pacific: 7 pm in Sydney in November, today or tomorrow there.
    const sydneyDay = date => new Intl.DateTimeFormat('en-CA', { timeZone: 'Australia/Sydney' }).format(date);
    const pacificDay = date => new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Los_Angeles' }).format(date);
    let midnight = new Date(Math.ceil(Date.now() / 3600000) * 3600000);
    while (pacificDay(new Date(midnight - 1)) === pacificDay(midnight)) midnight = new Date(+midnight + 3600000);
    const sevenLocal = (await page.run(`new Date(${+midnight}).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })`)).replace(/\s/g, ' ');
    assert.match(sevenLocal, /^(7:00 PM|19:00)$/);
    const backAt = 'at ' + sevenLocal + ' ' + (sydneyDay(midnight) === sydneyDay(new Date()) ? 'today' : 'tomorrow');
    assert.equal((await noticeSays('/daily free limit/', 'the daily notice')).replace(/\s/g, ' '), "Your Google AI key has hit Google's daily free limit. It comes back " + backAt + ', or pick another model in SETTINGS.');
    await click('.native-notice-more');
    const raw = await text('.native-notice-raw');
    assert.match(raw, /^429 /);
    assert.match(raw, /GenerateRequestsPerDayPerProjectPerModel-FreeTier/);
    assert.ok(!raw.includes(GOOD.google), 'no key in Details');
    assert.equal(await page.run(`document.querySelector('.native-notice-more').getAttribute('aria-expanded')`), 'true');
    await shot('limit-daily');
    // A daily limit has no wait to keep: the next message is tried again, once.
    const beforeDaily = chats().length;
    await say('daily again');
    for (const end = Date.now() + 8000; chats().length === beforeDaily && Date.now() < end;) await new Promise(resolve => setTimeout(resolve, 60));
    await page.until(`document.querySelector('.native-notice-raw').hidden`, 'a fresh notice');
    assert.equal(chats().length, beforeDaily + 1);

    // A model with no free allowance at all.
    provider.state.fail = google(429, 'Quota exceeded for metric: generativelanguage.googleapis.com/generate_content_free_tier_requests, limit: 0, model: gemini-3.1-pro', 'GenerateRequestsPerDayPerProjectPerModel-FreeTier', { value: '0' });
    await say('pro please');
    assert.equal(await noticeSays('/for free/', 'the no-free notice'), "This Google AI key can't use that model for free. Pick another model in SETTINGS.");
    await shot('limit-unfree');

    // A free model full upstream for everyone, with a Retry-After the page may read.
    provider.state.fail = { status: 429, body: { error: { code: 429, message: 'free/model:free is temporarily rate-limited upstream. Please retry shortly.', metadata: { provider_name: 'Chutes', raw: 'busy' } } }, headers: { 'retry-after': '1', 'access-control-expose-headers': 'retry-after' } };
    await say('busy');
    assert.equal(await noticeSays('/full for everyone/', 'the busy notice'), "Google AI's free model is full for everyone right now. Try again in a moment, or pick another model in SETTINGS.");
    await shot('limit-busy');
    await new Promise(resolve => setTimeout(resolve, 1200));

    // OpenAI's insufficient_quota comes as a 429 but means no credits.
    provider.state.fail = { status: 429, body: { error: { message: 'You exceeded your current quota, please check your plan and billing details.', type: 'insufficient_quota', code: 'insufficient_quota' } } };
    await say('credits');
    assert.equal(await noticeSays('/No credits/', 'the credits notice'), "No credits on this key. Add credits on Google AI's site.");
    assert.equal(await page.run(`document.querySelectorAll('.msg.capri').length`), herReplies + 1);
    assert.equal(await page.run(`[...document.querySelectorAll('.msg.capri .cut')].filter(node => node.textContent).length`), 0);

    // One tap to the free brain for now. The key stays saved, and the next message goes to the free brain.
    await click('.native-notice-free');
    await page.until(`localStorage.getItem('capri-remote-free') === '1' && !${shown}`, 'free messages for now');
    assert.equal(JSON.parse(await page.run(`localStorage.getItem('capri-remote-brain')`)).key, GOOD.google);
    await page.until(`CapriRemote.info.native === true && document.querySelector('#app').dataset.link === 'connected'`, 'the free brain connected');
    const googleCalls = chats().length;
    await say('free now');
    await page.until(`${lastReply}.startsWith('Capri: Free says: ')`, 'a free reply');
    assert.equal(chats().length, googleCalls);
  } finally {
    await page.close();
    await site.close();
    await provider.close();
  }
});

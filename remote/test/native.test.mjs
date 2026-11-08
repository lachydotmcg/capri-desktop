// Phone-only mode (site/remote/native.js) in a real browser: headless Chrome opens the phone page from a local copy of the
// site, a stand-in provider answers (lib/fake-provider.mjs), and the test sets her up with a key, chats, reloads, changes
// brain, hits an error and switches between a PC (the demo's) and this phone. Her persona is checked word for word
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
    await page.go(`http://127.0.0.1:${SITE_PORT}/?provider=http://127.0.0.1:${PROVIDER_PORT}`);
    await page.until(`document.querySelector('#app').dataset.screen === 'pair'`, 'the pair screen');
    assert.equal(await text('#native-start'), 'Use her without a PC');
    await shot('phone-pair');

    // A wrong key: her Mainframe's words, and nothing saved.
    await click('#native-start');
    await page.until(`!document.querySelector('#brain').hidden`, 'SETTINGS');
    assert.equal(await page.run(`document.querySelector('.native-modes .nbtn[aria-pressed="true"]').textContent`), 'This phone');
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

    // Her memory is the chat on screen.
    await say('and a second one');
    await page.until(`${lastReply} === 'Capri: Got it: and a second one'`, 'her second reply');
    asked = provider.got.filter(item => item.path === '/anthropic/messages');
    assert.deepEqual(asked[1].body.messages, [
      { role: 'user', content: 'hello from the phone test' },
      { role: 'assistant', content: 'Got it: hello from the phone test' },
      { role: 'user', content: 'and a second one' }
    ]);
    await shot('phone-chat');

    // A reload keeps her on this phone with the chat (saved 200 ms after it changes).
    await new Promise(resolve => setTimeout(resolve, 500));
    await page.go(`http://127.0.0.1:${SITE_PORT}/?provider=http://127.0.0.1:${PROVIDER_PORT}`);
    await page.until(`document.querySelector('#app').dataset.link === 'connected' && document.querySelectorAll('.msg.you').length === 2`, 'her chat after a reload');
    assert.equal(await text('#native-mode'), 'This phone');

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

    // A provider's refusal shows on her reply, in her words.
    await say('make it fail');
    await page.until(`(() => { const cut = [...document.querySelectorAll('.msg.capri .cut')].pop(); return cut && /TOO MANY REQUESTS/.test(cut.textContent); })()`, 'the rate limit on her reply');
    assert.equal(await text('.native-toast'), 'Too many requests. Wait a minute, then try again.');
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
    await click('#f-settings');
    await page.until(`!document.querySelector('#brain').hidden`, 'SETTINGS on the PC');
    await click('.native-modes .nbtn:last-child');
    await click('.native-row .nbtn.solid');
    await page.until(`CapriRemote.info.native === true && document.querySelector('#native-mode').textContent === 'This phone'`, 'this phone again');

    // The key went to the provider only: never in an address, and nothing left for anywhere but these two ports.
    for (const url of seen.concat(site.asked)) {
      for (const key of [GOOD.anthropic, GOOD.openrouter, BAD]) assert.ok(!url.includes(key), 'a key in ' + url);
      assert.ok(/^(data:|about:|http:\/\/127\.0\.0\.1:(8792|8793)\/|\/)/.test(url), 'the page reached ' + url);
    }
  } catch (error) {
    // Where the page was, for the failure's message.
    let where = '';
    try { where = await page.run(`JSON.stringify({ link: document.querySelector('#app').dataset.link, screen: document.querySelector('#app').dataset.screen, mode: localStorage.getItem('capri-remote-mode'), you: document.querySelectorAll('.msg.you').length, chats: (localStorage.getItem('capri-remote-chats') || '').length })`); } catch {}
    error.message += ' | page: ' + where;
    throw error;
  } finally {
    await page.close();
    await site.close();
    await provider.close();
  }
});

// SETTINGS on a paired PC (site/remote/native.js, PROTOCOL.md section 8.1) in a real browser: headless Chrome pairs the
// phone page with a stand-in PC (lib/fakes.mjs) through the relay under wrangler dev, then picks her brain and sends a
// key. The stand-in checks what arrives; the page must show the PC's words and keep nothing of the key. Then SETTINGS must
// fit every iPhone, in Safari and from the home screen (lib/fit.mjs). Ports 8792 and 8794, and a free one for the relay.
// Skipped where Chrome isn't installed. CAPRI_PHONE_SHOTS=<folder> saves screenshots.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { chromePath, launch } from './lib/cdp.mjs';
import { FakePc } from './lib/fakes.mjs';
import { HOME_SCREEN, IPHONES, SAFARI, misfitsOf, scrollSheet, setPhone } from './lib/fit.mjs';
import * as R from './lib/capri-remote.mjs';
import { startRelay } from './lib/relay-dev.mjs';
import { earlyAccess, startSite } from './lib/site.mjs';

const SITE_PORT = 8792, CHROME_PORT = 8794;
const SHOTS = process.env.CAPRI_PHONE_SHOTS || '';
const KEY = 'sk-ant-test-pc-key-000000000000000';
const WRONG = 'sk-ant-test-pc-wrong-0000000000000';

test('SETTINGS on a paired PC: her brain picked from the phone, and a key that goes to the PC only', { skip: !chromePath() && 'Chrome is not installed', timeout: 180000 }, async () => {
  const relay = await startRelay();
  const site = await startSite(SITE_PORT);
  const pc = new FakePc(relay);
  let page = null;
  try {
    await pc.connect();
    const qr = await pc.showCode();
    page = await launch({ port: CHROME_PORT });
    const click = selector => page.run(`(() => { const node = document.querySelector(${JSON.stringify(selector)}); if (!node) throw new Error('no ${selector}'); node.click(); return true; })()`);
    const type = (selector, text) => page.run(`(() => { const node = document.querySelector(${JSON.stringify(selector)}); node.value = ${JSON.stringify(text)}; node.dispatchEvent(new Event('input', { bubbles: true })); return true; })()`);
    const text = selector => page.run(`(document.querySelector(${JSON.stringify(selector)}) || {}).textContent || ''`);
    const shot = async name => { if (SHOTS) { mkdirSync(SHOTS, { recursive: true }); await page.shot(join(SHOTS, name + '.png')); } };
    // The next message of a type from the phone, past the relay's notices and anything else it sends.
    const next = async wanted => {
      for (;;) {
        const item = await pc.conn.next(10000);
        if (item.text) continue;
        const { device: id, envelope } = R.splitPcFrame(item.data);
        const message = await pc.devices.get(id).session.open(envelope);
        if (message.type === wanted) return { message };
      }
    };
    await page.send('Page.addScriptToEvaluateOnNewDocument', { source: earlyAccess() });
    await page.go(`http://127.0.0.1:${SITE_PORT}/?relay=${relay.ws}${qr.slice(qr.indexOf('#'))}`);
    const device = await pc.allow(await pc.pairRequest());
    await pc.conn.json('joined', 10000);
    await pc.acceptHello(['chat', 'brains']);
    await page.until(`document.querySelector('#app').dataset.link === 'connected'`, 'the paired PC');
    assert.equal(await text('#native-mode'), 'Your PC');

    // SETTINGS asks the PC for her brains and shows the rung in use.
    await click('#f-settings');
    await next('brain.get');
    const none = [{ id: 'normal', key: false }, { id: 'medium', key: false }, { id: 'max', key: false }];
    await pc.send(device.id, { type: 'brain.state', use: 'normal', name: 'Built-in · qwen3-4b', note: 'Running on this PC', brains: none });
    await page.until(`!document.querySelector('.native-brains').hidden`, 'her brains in SETTINGS');
    assert.equal(await page.run(`document.querySelector('.native-rung[aria-pressed="true"]').dataset.brain`), 'normal');
    assert.equal(await text('.native-now'), 'Now: Built-in · qwen3-4b. Running on this PC');
    assert.equal(await page.run(`document.querySelector('#pc-key').closest('div').hidden`), true);

    // Tapping MAX INTELLIGENCE picks it on the PC, and its key box shows.
    await click('.native-rung[data-brain="max"]');
    assert.equal((await next('brain.use')).message.brain, 'max');
    await pc.send(device.id, { type: 'brain.state', use: 'max', name: 'No brain yet', note: 'Paste a brain API key', brains: none });
    await page.until(`!document.querySelector('#pc-key').closest('div').hidden`, 'the key box for MAX');
    assert.equal(await text('label[for="pc-key"]'), 'Key for MAX INTELLIGENCE');

    // A key goes to the PC, end to end; the page shows the PC's notes and keeps nothing of it.
    await type('#pc-key', KEY);
    await click('.native-send');
    const sent = (await next('brain.key')).message;
    assert.equal(sent.brain, 'max');
    assert.equal(sent.key, KEY);
    assert.match(sent.id, /^[A-Za-z0-9_-]{1,64}$/);
    await pc.send(device.id, { type: 'brain.note', re: sent.id, state: 'checking', text: 'Checking key...' });
    await page.until(`document.querySelector('.native-brains .card-note').textContent === 'Checking key...'`, 'the checking note');
    await pc.send(device.id, { type: 'brain.note', re: sent.id, state: 'saved', text: 'Key saved. She uses it now.' });
    await pc.send(device.id, { type: 'brain.state', use: 'max', name: 'Claude · claude-haiku-5-5', note: 'Using your Anthropic key', brains: [{ id: 'normal', key: false }, { id: 'medium', key: false }, { id: 'max', key: true }] });
    await page.until(`document.querySelector('.native-brains .card-note').textContent === 'Key saved. She uses it now.' && !document.querySelector('.native-rung[data-brain="max"] .native-saved').hidden`, 'the saved key');
    assert.equal(await page.run(`document.querySelector('#pc-key').value`), '');
    assert.equal(await text('.native-now'), 'Now: Claude · claude-haiku-5-5. Using your Anthropic key');
    await shot('pc-brains');
    const kept = await page.run(`JSON.stringify(Object.assign({}, localStorage))`);
    assert.ok(!kept.includes(KEY), 'the key stayed on the phone');

    // A key the PC turns down: its words, and the key box stays for another try.
    await type('#pc-key', WRONG);
    await click('.native-send');
    const wrong = (await next('brain.key')).message;
    await pc.send(device.id, { type: 'brain.note', re: wrong.id, state: 'failed', text: 'Still using your Anthropic key. Anthropic rejected the key for my brain.' });
    await page.until(`document.querySelector('.native-brains .card-note').textContent.startsWith('Still using your Anthropic key.')`, 'the failed note');
    assert.equal(await page.run(`document.querySelector('#pc-key').closest('.field').classList.contains('bad')`), true);
    assert.equal(await page.run(`document.querySelector('#pc-key').value`), WRONG);

    // SETTINGS at its longest (MAX's key box, a note, In use, the home screen part) fits every iPhone from the SE to the Pro
    // Max, in Safari and as a home screen app under the notch and the home bar: nothing past the right edge, and the frame
    // holds all of it, scrolling inside. A long model name wraps instead of pushing the page wide.
    await pc.send(device.id, { type: 'brain.state', use: 'max', name: 'OpenRouter · nvidia/llama-3.1-nemotron-ultra-253b-v1:free', note: 'Using your OpenRouter key', brains: [{ id: 'normal', key: false }, { id: 'medium', key: true }, { id: 'max', key: true }] });
    await page.until(`document.querySelector('.native-now').textContent.includes('nemotron')`, 'the long brain name');
    for (const mode of [SAFARI, HOME_SCREEN]) {
      for (const size of IPHONES) {
        await setPhone(page, size, mode);
        assert.deepEqual(await misfitsOf(page, '#brain'), [], `SETTINGS at ${size.join('x')} (${mode.name})`);
        await scrollSheet(page, '#brain', false);
        await shot(`settings-${mode.name}-${size.join('x')}`);
        await scrollSheet(page, '#brain', true);
        await shot(`settings-${mode.name}-${size.join('x')}-end`);
      }
    }
  } finally {
    if (page) await page.close();
    pc.close();
    await site.close();
    await relay.stop();
  }
});

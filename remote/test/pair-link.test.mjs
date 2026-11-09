// The home screen app's pairing link (PROTOCOL.md 5.1 and 8.3) in a real browser. An iPhone keeps a home screen app's
// storage apart from Safari's, so the app needs a pairing of its own. Headless Chrome acts out a paired Safari
// (?ios=safari) and a stand-in PC (lib/fakes.mjs) answers through the relay under wrangler dev: a PC from before pairing
// links (tester build 3) gets the old guide, and one that offers them makes a link that Safari copies in one tap. A second
// Chrome, with storage of its own like the app's, acts out the home screen app (?ios=app): its pair screen says where a
// link comes from, and Paste link pairs it with no Allow. Both pages get a stand-in clipboard, so the test never touches
// the PC's own. The guide and the app's pair screen must fit every iPhone (lib/fit.mjs). Ports 8795 to 8797, and a free
// one for the relay. Skipped where Chrome isn't installed. CAPRI_PHONE_SHOTS=<folder> saves screenshots.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { chromePath, launch } from './lib/cdp.mjs';
import { FakePc, FakePhone } from './lib/fakes.mjs';
import { HOME_SCREEN, IPHONES, SAFARI, misfitsOf, scrollSheet, setPhone } from './lib/fit.mjs';
import * as R from './lib/capri-remote.mjs';
import { startRelay } from './lib/relay-dev.mjs';
import { earlyAccess, startSite } from './lib/site.mjs';

const SITE_PORT = 8795, SAFARI_PORT = 8796, APP_PORT = 8797;
const SHOTS = process.env.CAPRI_PHONE_SHOTS || '';
const OLD_STEPS = ['Tap Share, then Add to Home Screen', 'On your PC: Pair a phone, scan, pick Home screen app', 'Open Capri from your home screen, tap Paste link'];
const LINK_STEPS = ['Tap Share, then Add to Home Screen', 'Open Capri from your home screen', 'Tap Paste link within 10 minutes'];
const COPIED = 'Link copied. It works once, for 10 minutes';
const wait = ms => new Promise(resolve => setTimeout(resolve, ms));

// The page's clipboard, kept in the page as window.__clip: what it writes, how, and what Paste link reads back.
const CLIPBOARD = `(() => {
  const clip = { text: '', writes: [] };
  const standIn = {
    standIn: true,
    writeText: text => { clip.text = String(text); clip.writes.push('writeText'); return Promise.resolve(); },
    readText: () => Promise.resolve(clip.text),
    write: items => Promise.all(items.map(item => item.getType('text/plain').then(blob => blob.text()))).then(texts => { clip.text = texts.join(''); clip.writes.push('write'); })
  };
  Object.defineProperty(navigator, 'clipboard', { configurable: true, get: () => standIn });
  window.__clip = clip;
})();`;

const tools = page => ({
  click: selector => page.run(`(() => { const node = document.querySelector(${JSON.stringify(selector)}); if (!node) throw new Error('no ${selector}'); node.click(); return true; })()`),
  text: selector => page.run(`(document.querySelector(${JSON.stringify(selector)}) || {}).textContent || ''`),
  hidden: selector => page.run(`(() => { const node = document.querySelector(${JSON.stringify(selector)}); return !node || node.hidden || !node.getClientRects().length; })()`),
  shot: async name => { if (SHOTS) { mkdirSync(SHOTS, { recursive: true }); await page.shot(join(SHOTS, name + '.png')); } }
});

// A phone page with the gate passed and the stand-in clipboard, which must be the one the page sees.
const openPhone = async (port, size, mode, url) => {
  const page = await launch({ port });
  await page.send('Page.addScriptToEvaluateOnNewDocument', { source: earlyAccess() });
  await page.send('Page.addScriptToEvaluateOnNewDocument', { source: CLIPBOARD });
  await setPhone(page, size, mode);
  await page.go(url);
  await page.until(`document.readyState === 'complete'`, 'the page');
  assert.equal(await page.run(`navigator.clipboard.standIn === true && Array.isArray(window.__clip.writes) && window.__clip.text === ''`), true, 'the stand-in clipboard is the page\'s');
  return page;
};

test('the home screen app pairs with a link a paired Safari asks its PC for', { skip: !chromePath() && 'Chrome is not installed', timeout: 180000 }, async () => {
  const relay = await startRelay();
  const site = await startSite(SITE_PORT);
  const pc = new FakePc(relay);
  let safari = null, app = null, late = null;
  try {
    await pc.connect();
    // The PC's next relay notice of a kind, past the phones' frames and its other notices.
    const notice = async t => {
      for (;;) {
        const item = await pc.conn.next(10000);
        if (item.text && item.data.t === t) return item.data;
      }
    };
    // The next message of a type from a phone, past the relay's notices and the phones' other messages.
    const heard = async type => {
      for (;;) {
        const item = await pc.conn.next(10000);
        if (item.text) continue;
        const { device: id, envelope } = R.splitPcFrame(item.data);
        const device = pc.devices.get(id);
        if (!device || !device.session) continue;
        let message;
        try { message = await device.session.open(envelope); } catch { continue; }
        if (message.type === type) return message;
      }
    };

    // Safari scans the PC's code and pairs here, with Allow, on a PC from before pairing links: build 3 offers no "pair".
    const qr = await pc.showCode();
    safari = await openPhone(SAFARI_PORT, [390, 844], SAFARI, `http://127.0.0.1:${SITE_PORT}/?ios=safari&relay=${relay.ws}${qr.slice(qr.indexOf('#'))}`);
    const s = tools(safari);
    const steps = () => safari.run(`[...document.querySelectorAll('#home-steps li > span:last-child')].map(node => node.textContent)`);
    const note = () => s.text('#home-link-note');
    await safari.until(`document.querySelector('#app').dataset.screen === 'where'`, 'Pair where?');
    await s.click('#where-here');
    const ask = await pc.pairRequest();
    assert.ok(!ask.request.name.endsWith(' home screen'), 'Safari is not the home screen app: ' + ask.request.name);
    const device = await pc.allow(ask);
    assert.equal((await notice('joined')).device, device.id);
    await pc.acceptHello(['chat', 'brains']);
    await safari.until(`document.querySelector('#app').dataset.link === 'connected'`, 'Safari paired');

    // SETTINGS keeps its old button there, and the guide says to scan a code from the PC.
    await s.click('#f-settings');
    await safari.until(`!document.querySelector('#brain').hidden`, 'SETTINGS');
    assert.equal(await s.text('#brain .native-home'), 'Add her to your home screen');
    await s.click('#brain .native-home');
    await safari.until(`!document.querySelector('#home').hidden && document.querySelector('#brain').hidden`, 'the home screen guide');
    assert.equal(await s.hidden('#home-link-part'), true);
    assert.deepEqual(await steps(), OLD_STEPS);
    await s.shot('home-guide-build3');
    await s.click('#home-done');
    await safari.until(`document.querySelector('#home').hidden`, 'the guide closed');

    // The PC is updated to one that offers pairing links, and the phone gets a fresh session.
    pc.conn.send({ t: 'kick', device: device.id });
    assert.equal((await notice('joined')).device, device.id);
    await pc.acceptHello(['chat', 'brains', 'pair']);
    await safari.until(`document.querySelector('#app').dataset.link === 'connected' && CapriRemote.info.features.includes('pair')`, 'the PC that makes pairing links');

    // One tap in SETTINGS opens the guide and asks the PC for a link. A PC showing its own code says busy.
    await s.click('#f-settings');
    await safari.until(`!document.querySelector('#brain').hidden`, 'SETTINGS');
    assert.equal(await s.text('#brain .native-home'), 'Set up the home screen app');
    await s.click('#brain .native-home');
    await safari.until(`!document.querySelector('#home').hidden && document.querySelector('#brain').hidden`, 'the guide');
    const first = await heard('pair.ask');
    assert.deepEqual(Object.keys(first).sort(), ['id', 'type']);
    assert.match(first.id, /^[A-Za-z0-9_-]{1,64}$/);
    assert.equal(await s.text('#home-link'), 'Asking your PC...');
    assert.equal(await safari.run(`document.querySelector('#home-link').disabled`), true);
    await pc.send(device.id, { type: 'error', code: 'busy', re: first.id });
    await safari.until(`document.querySelector('#home-link-note').textContent.startsWith('Your PC is showing')`, 'the busy words');
    assert.equal(await note(), 'Your PC is showing a pairing code. Close it, then try again');
    assert.equal(await s.text('#home-link'), 'Set up the home screen app');
    assert.equal(await safari.run(`document.querySelector('#home-link').disabled`), false);
    assert.equal(await safari.run(`window.__clip.text`), '', 'nothing copied for a busy PC');
    await s.shot('home-link-busy');

    // Again, and the PC makes a one-time link. Safari copies it in the same tap, on its own address, marked as a phone's.
    await s.click('#home-link');
    const second = await heard('pair.ask');
    assert.notEqual(second.id, first.id);
    const url = await pc.makeLink(device.id, second.id);
    const link = `http://127.0.0.1:${SITE_PORT}/${url.slice(url.indexOf('#'))}&via=phone`;
    await safari.until(`document.querySelector('#home-link-note').textContent.startsWith('Link copied')`, 'the copied link');
    assert.equal(await note(), COPIED);
    assert.equal(await safari.run(`window.__clip.text`), link);
    assert.deepEqual(await safari.run(`window.__clip.writes`), ['write'], 'copied with the write that starts in the tap');
    assert.equal(await s.text('#home-link'), 'Copy the link again');
    assert.deepEqual(await steps(), LINK_STEPS);
    const { code, key } = R.parseQr(url);
    const kept = await safari.run(`JSON.stringify(Object.assign({}, localStorage, sessionStorage)) + location.href`);
    assert.ok(!kept.includes(code) && !kept.includes(key), 'the link stays in memory');

    // The guide with the link part fits every iPhone in Safari.
    for (const size of IPHONES) {
      await setPhone(safari, size, SAFARI);
      assert.deepEqual(await misfitsOf(safari, '#home'), [], `the guide at ${size.join('x')} (safari)`);
      await scrollSheet(safari, '#home', false);
      await s.shot(`home-link-safari-${size.join('x')}`);
    }
    await setPhone(safari, [390, 844], SAFARI);

    // Copy the link again copies the same link, with no new ask.
    await s.click('#home-link');
    await safari.until(`window.__clip.writes.length === 2`, 'the link copied again');
    assert.deepEqual(await safari.run(`window.__clip.writes`), ['write', 'writeText']);
    assert.equal(await safari.run(`window.__clip.text`), link);
    assert.equal(await note(), COPIED);
    await wait(300);
    assert.ok(!pc.conn.inbox.some(item => !item.text), 'no second ask for a link still good');

    // The home screen app, with its own storage. Its pair screen says where a link comes from, with no steps for a scan.
    app = await openPhone(APP_PORT, [390, 844], HOME_SCREEN, `http://127.0.0.1:${SITE_PORT}/?ios=app&relay=${relay.ws}`);
    const a = tools(app);
    await app.until(`document.querySelector('#app').dataset.screen === 'pair'`, 'the app\'s pair screen');
    assert.equal(await a.text('#pair-title'), 'Pair this app');
    assert.equal(await a.text('#pair-card .card-lead'), 'It pairs with a link Safari copies');
    assert.equal(await a.hidden('#pair-steps'), true);
    assert.deepEqual(await app.run(`[...document.querySelectorAll('#pair-routes li')].map(item => [...item.children].map(node => node.textContent))`), [['Safari, paired', 'SETTINGS > Set up the home screen app'], ['Your PC', 'Pair a phone > scan > Home screen app']]);
    assert.equal(await a.hidden('#paste'), false);
    for (const size of IPHONES) {
      await setPhone(app, size, HOME_SCREEN);
      assert.deepEqual(await misfitsOf(app), [], `the app's pair screen at ${size.join('x')}`);
      await a.shot(`app-pair-${size.join('x')}`);
    }
    await setPhone(app, [390, 844], HOME_SCREEN);

    // Paste link with nothing copied yet says so.
    await a.click('#paste-go');
    await app.until(`document.querySelector('#pair-note').textContent === 'No link to paste yet'`, 'the empty paste');

    // The phone's one clipboard has Safari's link: Paste link pairs the app with no Allow, and says so while it waits.
    await app.run(`(window.__clip.text = ${JSON.stringify(link)}, true)`);
    await a.click('#paste-go');
    await app.until(`document.querySelector('#app').dataset.screen === 'wait'`, 'the app pairing');
    assert.equal(await a.text('#wait-title'), 'Pairing');
    assert.equal(await a.text('#wait-lead'), 'Safari asked your PC for this link');
    assert.equal(await a.hidden('#wait-check'), true);
    assert.equal(await a.hidden('#wait-code'), true);
    assert.equal(await a.text('#wait-words'), 'Pairing with your PC');
    assert.equal(await app.run(`location.hash`), '', 'the link left the address bar');
    await a.shot('app-pairing');
    const { device: home, request } = await pc.admitLinked();
    assert.match(request.name, / home screen$/);
    assert.equal(home.linkedBy, device.id);
    assert.equal((await notice('joined')).device, home.id);
    await pc.acceptHello(['chat', 'brains', 'pair']);
    await app.until(`document.querySelector('#app').dataset.link === 'connected'`, 'the app paired');
    assert.equal(await app.run(`window.__clip.text`), '', 'Paste link leaves the clipboard empty');
    assert.equal(await app.run(`window.__clip.writes.includes('writeText')`), true);
    assert.equal(await s.text('#home-link'), 'Copy the link again', 'Safari is still paired too');
    assert.equal(await safari.run(`document.querySelector('#app').dataset.link`), 'connected');
    await a.shot('app-paired');

    // The link works once.
    late = new FakePhone(relay);
    await late.scan(url);
    await late.conn.refused('code-used', 4410);
  } finally {
    if (late) late.close();
    if (app) await app.close();
    if (safari) await safari.close();
    pc.close();
    await site.close();
    await relay.stop();
  }
});

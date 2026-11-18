import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { chromePath, launch } from './lib/cdp.mjs';
import { FakePc } from './lib/fakes.mjs';
import { HOME_SCREEN, IPHONES, SAFARI, misfitsOf, setPhone } from './lib/fit.mjs';
import * as R from './lib/capri-remote.mjs';
import { startRelay } from './lib/relay-dev.mjs';
import { earlyAccess, startSite } from './lib/site.mjs';

const SITE_PORT = 8806, CHROME_PORT = 8808;
const SHOTS = process.env.CAPRI_PHONE_SHOTS || '';
const CARD = { mac: '0A:1B:2C:3D:4E:5F', broadcast: '192.168.1.255', name: 'Ethernet', wired: true, ready: 'on' };

test('Wake PC: keeps her PC\'s cards, shows them when it\'s off, and the Android app sends the packet', { skip: !chromePath() && 'Chrome is not installed', timeout: 180000 }, async () => {
  const relay = await startRelay();
  const site = await startSite(SITE_PORT);
  const pc = new FakePc(relay);
  let page = null;
  try {
    await pc.connect();
    const qr = await pc.showCode();
    page = await launch({ port: CHROME_PORT });
    const errors = [];
    page.on((method, data) => { if (method === 'Runtime.exceptionThrown') errors.push(data.exceptionDetails.text); });
    const click = selector => page.run(`document.querySelector(${JSON.stringify(selector)}).click()`);
    const text = selector => page.run(`document.querySelector(${JSON.stringify(selector)}).textContent`);
    const shot = async name => { if (SHOTS) { mkdirSync(SHOTS, { recursive: true }); await page.shot(join(SHOTS, name + '.png')); } };
    const next = async type => {
      for (;;) {
        const item = await pc.conn.next(10000);
        if (item.text) continue;
        const { device: from, envelope } = R.splitPcFrame(item.data);
        const message = await pc.devices.get(from).session.open(envelope);
        if (message.type === type) return message;
      }
    };
    await page.send('Page.addScriptToEvaluateOnNewDocument', { source: earlyAccess() });
    await page.go(`http://127.0.0.1:${SITE_PORT}/?relay=${relay.ws}${qr.slice(qr.indexOf('#'))}`);
    const device = await pc.allow(await pc.pairRequest());
    await pc.conn.json('joined', 10000);
    await pc.acceptHello(['chat', 'wake']);
    await page.until(`document.querySelector('#app').dataset.link === 'connected'`, 'the paired PC');
    assert.deepEqual(await next('wake.get'), { type: 'wake.get' });
    await pc.send(device.id, { type: 'wake.info', os: 'windows', cards: [CARD, { mac: 'not-a-mac', broadcast: '192.168.1.255' }, { ...CARD, broadcast: '300.1.1.1', mac: '0A:1B:2C:3D:4E:60' }] });
    await page.until(`CapriWake.saved() && CapriWake.saved().cards.length === 1`, 'her card kept on the phone');
    assert.deepEqual(await page.run(`CapriWake.saved().cards`), [CARD]);
    assert.equal(await page.run(`document.querySelector('#banner-wake').hidden`), true, 'no Wake PC while she\'s on');

    pc.close();
    await page.until(`document.querySelector('#app').dataset.link === 'offline'`, 'the PC going off');
    await page.until(`!document.querySelector('#banner-wake').hidden`, 'Wake PC on the banner');
    for (const mode of [SAFARI, HOME_SCREEN]) for (const size of IPHONES) {
      await setPhone(page, size, mode);
      assert.deepEqual(await misfitsOf(page), [], `the banner at ${size.join('x')} (${mode.name})`);
    }
    await setPhone(page, IPHONES[0], SAFARI);
    await shot('wake-banner');

    await click('#banner-wake');
    await page.until(`!document.querySelector('#wake').hidden`, 'the wake sheet in a browser');
    assert.match(await text('#wake'), /same Wi-Fi or network/);
    assert.match(await text('#wake-lead'), /can't send the wake signal/);
    assert.equal(await text('.wake-card .wake-value'), CARD.mac);
    assert.equal(await page.run(`document.querySelectorAll('.wake-card').length`), 1);
    for (const mode of [SAFARI, HOME_SCREEN]) for (const size of IPHONES) {
      await setPhone(page, size, mode);
      assert.deepEqual(await misfitsOf(page, '#wake'), [], `the wake sheet at ${size.join('x')} (${mode.name})`);
    }
    await setPhone(page, IPHONES[0], SAFARI);
    await shot('wake-sheet');
    await click('#wake-done');
    assert.equal(await page.run(`document.querySelector('#wake').hidden && document.querySelector('#scrim').hidden`), true);

    await page.run(`window.CapriAndroid = { wake: cards => { window.woke = JSON.parse(cards); return '{"sent":6}'; } }, true`);
    await click('#banner-wake');
    assert.deepEqual(await page.run(`window.woke`), [{ mac: CARD.mac, broadcast: CARD.broadcast }]);
    assert.equal(await page.run(`document.querySelector('#wake').hidden`), true, 'the app sends it without the sheet');
    assert.equal(await text('#banner-title'), 'Waking your PC');
    assert.match(await text('#banner-sub'), /same network/);
    await shot('wake-sent');
    await page.run(`window.CapriAndroid = { wake: () => '{"sent":0}' }, true`);
    await click('#banner-wake');
    await page.until(`!document.querySelector('#wake').hidden && !document.querySelector('#wake-note').hidden`, 'a wake that couldn\'t go out');
    await click('#wake-done');

    await page.run(`CapriRemote.unpair(), true`);
    await page.until(`!CapriWake.saved()`, 'unpairing forgets her cards');
    assert.deepEqual(errors, []);
  } finally {
    if (page) await page.close();
    pc.close();
    await site.close();
    await relay.stop();
  }
});

test('Wake PC stays away from a PC without the feature', { skip: !chromePath() && 'Chrome is not installed', timeout: 120000 }, async () => {
  const relay = await startRelay();
  const site = await startSite(SITE_PORT);
  const pc = new FakePc(relay);
  let page = null;
  try {
    await pc.connect();
    const qr = await pc.showCode();
    page = await launch({ port: CHROME_PORT });
    await page.send('Page.addScriptToEvaluateOnNewDocument', { source: earlyAccess() });
    await page.go(`http://127.0.0.1:${SITE_PORT}/?relay=${relay.ws}${qr.slice(qr.indexOf('#'))}`);
    await pc.allow(await pc.pairRequest());
    await pc.conn.json('joined', 10000);
    await pc.acceptHello(['chat']);
    await page.until(`document.querySelector('#app').dataset.link === 'connected'`, 'the paired PC');
    const seen = [];
    const until = Date.now() + 1500;
    while (Date.now() < until) {
      const item = await pc.conn.next(Math.max(1, until - Date.now())).catch(() => null);
      if (!item) break;
      if (item.text) continue;
      const { device: from, envelope } = R.splitPcFrame(item.data);
      seen.push((await pc.devices.get(from).session.open(envelope)).type);
    }
    assert.equal(seen.includes('wake.get'), false);
    pc.close();
    await page.until(`document.querySelector('#app').dataset.link === 'offline'`, 'the PC going off');
    assert.equal(await page.run(`document.querySelector('#banner-wake').hidden`), true);
  } finally {
    if (page) await page.close();
    pc.close();
    await site.close();
    await relay.stop();
  }
});

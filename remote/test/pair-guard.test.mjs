import { test } from 'node:test';
import assert from 'node:assert/strict';
import { webcrypto } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import vm from 'node:vm';
import { chromePath, launch } from './lib/cdp.mjs';
import { FakePc } from './lib/fakes.mjs';
import { HOME_SCREEN, IPHONES, misfitsOf, setPhone } from './lib/fit.mjs';
import { startRelay } from './lib/relay-dev.mjs';
import { earlyAccess, SITE, startSite } from './lib/site.mjs';

const MARKER = 'capri-remote-pairing';
const room = byte => Buffer.alloc(16, byte).toString('base64url');
const fragment = (id, via = '') => `#r=${id}&c=${room(3)}&k=${Buffer.alloc(32, 4).toString('base64url')}${via}`;
const tick = () => new Promise(resolve => setImmediate(resolve));
const cryptoSource = readFileSync(join(SITE, 'crypto.js'), 'utf8');
const remoteSource = readFileSync(join(SITE, 'remote.js'), 'utf8');

const harness = async ({ connected = true, paired = true, marker = true } = {}) => {
  const saved = { device: true, demo: false, pc: 'Home PC', pairedAt: 123, allowed: true };
  const values = new Map(paired && marker ? [[MARKER, JSON.stringify(saved)]] : []);
  const device = { room: room(1), device: 'original-device', token: 'original-token', key: {}, pc: saved.pc, pairedAt: saved.pairedAt };
  const result = { device: paired ? device : null, clears: 0, scans: [], states: [], active: null };
  const context = {
    crypto: webcrypto, TextEncoder, TextDecoder, Uint8Array, ArrayBuffer, atob, btoa, URLSearchParams, console,
    navigator: { userAgent: 'Chrome/1 Windows', maxTouchPoints: 0 },
    matchMedia: () => ({ matches: false }),
    location: { hostname: '127.0.0.1', pathname: '/', search: '', hash: '' },
    localStorage: { getItem: name => values.get(name) || null, setItem: (name, value) => values.set(name, value), removeItem: name => values.delete(name) },
    indexedDB: {
      open() {
        const request = { result: {
          close() {},
          transaction() {
            const tx = { objectStore: () => ({ get: () => ({ result: result.device }) }) };
            queueMicrotask(() => tx.oncomplete());
            return tx;
          }
        } };
        queueMicrotask(() => request.onsuccess());
        return request;
      }
    }
  };
  context.window = context;
  context.history = { replaceState: () => { context.location.hash = ''; } };
  context.CapriRelay = {
    clear: () => { result.clears++; result.device = null; },
    link: () => ({
      close() {},
      open(pairing, hooks) {
        result.active = { pairing, hooks };
        if (pairing.scan) { result.scans.push(pairing.scan.room); hooks.state('pairing', { code: '123 456' }); }
        else hooks.state('connected', { pc: result.device.pc });
      }
    })
  };
  vm.createContext(context);
  vm.runInContext(cryptoSource, context);
  vm.runInContext(remoteSource, context);
  result.remote = context.CapriRemote;
  result.values = values;
  result.context = context;
  result.marker = values.get(MARKER);
  result.remote.onState(state => result.states.push(state));
  if (connected) await result.remote.connect();
  return result;
};

const unchanged = h => {
  assert.equal(h.values.get(MARKER), h.marker);
  assert.equal(h.device.device, 'original-device');
  assert.equal(h.device.room, room(1));
  assert.equal(h.clears, 0);
};

test('a bad or partial fragment leaves the stored pairing and strips the address', async () => {
  for (const hash of ['#r=a', '#r=a&c=b&k=c']) {
    for (const options of [{}, { connected: false }, { connected: false, marker: false }]) {
      const h = await harness(options);
      h.context.location.hash = hash;
      await h.remote.connect();
      unchanged(h);
      assert.equal(h.context.location.hash, '');
      assert.equal(h.remote.state, 'connected');
      assert.equal(h.remote.info.pairReason, 'bad-link');
      assert.deepEqual(h.scans, []);
    }
  }
});

test('an unpaired browser keeps the pair screen for a bad link and honours via=phone for a good one', async () => {
  const h = await harness({ connected: false, paired: false });
  h.context.location.hash = '#r=a';
  assert.equal(await h.remote.connect(), false);
  assert.equal(h.context.location.hash, '');
  assert.equal(h.remote.state, 'unpaired');
  assert.equal(h.remote.info.pairReason, 'bad-link');
  assert.equal(h.clears, 0);
  assert.equal(await h.remote.connect({ fragment: fragment(room(2), '&via=phone') }), true);
  assert.equal(h.remote.state, 'pairing');
  assert.equal(h.remote.info.viaPhone, true);
  assert.ok(!h.states.includes('confirming'));
});

test('a foreign room waits for confirmation and Keep leaves the pairing', async () => {
  const h = await harness();
  const pending = h.remote.connect({ fragment: fragment(room(2)) });
  await tick();
  assert.equal(h.remote.state, 'confirming');
  assert.equal(h.remote.info.pc, 'Home PC');
  assert.deepEqual(h.scans, []);
  unchanged(h);
  h.remote.confirmPairing(false);
  assert.equal(await pending, false);
  assert.equal(h.remote.state, 'connected');
  unchanged(h);
});

test('a deny, timeout or failure after going ahead leaves the old pairing', async () => {
  for (const reason of ['denied', 'timeout', 'network', 'ended']) {
    const h = await harness();
    const pending = h.remote.connect({ fragment: fragment(room(2)) });
    await tick();
    h.remote.confirmPairing(true);
    assert.equal(await pending, true);
    unchanged(h);
    h.active.hooks.forget(reason);
    assert.equal(h.remote.state, 'connected');
    assert.equal(h.remote.info.pc, 'Home PC');
    assert.equal(h.remote.info.pairReason, reason);
    unchanged(h);
  }
});

test('a same-room fragment starts pairing without a replacement confirmation', async () => {
  const h = await harness();
  assert.equal(await h.remote.connect({ fragment: fragment(room(1)) }), true);
  assert.equal(h.remote.state, 'pairing');
  assert.ok(!h.states.includes('confirming'));
  assert.deepEqual(h.scans, [room(1)]);
  unchanged(h);
});

test('via=phone cannot hide the code for a stored pairing', async () => {
  const h = await harness();
  const pending = h.remote.connect({ fragment: fragment(room(2), '&via=phone') });
  await tick();
  assert.equal(h.remote.state, 'confirming');
  h.remote.confirmPairing(true);
  await pending;
  assert.equal(h.remote.state, 'pairing');
  assert.equal(h.remote.info.viaPhone, false);
  assert.equal(h.remote.info.code, '123 456');
  unchanged(h);
});

test('Allow replaces the marker only after the new device is stored', async () => {
  const h = await harness();
  const pending = h.remote.connect({ fragment: fragment(room(2)) });
  await tick();
  h.remote.confirmPairing(true);
  await pending;
  unchanged(h);
  h.device = { ...h.device, room: room(2), device: 'new-device', pc: 'New PC', pairedAt: 456 };
  h.active.hooks.paired('New PC');
  assert.equal(h.values.get(MARKER), h.marker);
  h.active.hooks.state('connected', { pc: 'New PC' });
  await tick();
  assert.equal(h.remote.state, 'connected');
  assert.equal(JSON.parse(h.values.get(MARKER)).pc, 'New PC');
  assert.equal(h.clears, 0);
});

test('a failed device write or Cancel keeps the old pairing', async () => {
  for (const cancel of [false, true]) {
    const h = await harness();
    const pending = h.remote.connect({ fragment: fragment(room(2)) });
    await tick();
    h.remote.confirmPairing(true);
    await pending;
    if (cancel) h.remote.cancelPairing();
    else { h.active.hooks.paired('New PC'); await tick(); }
    assert.equal(h.remote.state, 'connected');
    assert.equal(h.remote.info.pc, 'Home PC');
    unchanged(h);
  }
});

const CLIPBOARD = `(() => {
  const clip = { text: '', writes: [] };
  Object.defineProperty(navigator, 'clipboard', { configurable: true, value: {
    readText: () => Promise.resolve(clip.text),
    writeText: text => { clip.text = String(text); clip.writes.push(String(text)); return Promise.resolve(); }
  } });
  window.__clip = clip;
  window.__pairSockets = [];
  const Socket = WebSocket;
  window.WebSocket = class extends Socket {
    constructor(url, ...args) {
      if (!/^(127\\.0\\.0\\.1|localhost|\\[::1\\])$/.test(new URL(url).hostname)) throw new Error('non-local socket blocked');
      super(url, ...args);
      window.__pairSockets.push(String(url));
    }
  };
})();`;

const readSaved = () => new Promise((resolve, reject) => {
  const request = indexedDB.open('capri-remote', 1);
  request.onerror = () => reject(request.error);
  request.onsuccess = () => {
    const db = request.result;
    const tx = db.transaction('device', 'readonly');
    const item = tx.objectStore('device').get('current');
    tx.oncomplete = () => {
      const record = item.result;
      db.close();
      resolve({ marker: localStorage.getItem('capri-remote-pairing'), device: record ? {
        room: record.room, device: record.device, token: record.token, pc: record.pc, pairedAt: record.pairedAt,
        key: { type: record.key.type, extractable: record.key.extractable, usages: record.key.usages }
      } : null });
    };
    tx.onerror = tx.onabort = () => { db.close(); reject(tx.error); };
  };
});

test('the phone confirms replacement links and preserves the original PC in Chrome', { skip: !chromePath() && 'Chrome is not installed', timeout: 150000 }, async t => {
  let relay = null, site = null, home = null, other = null, page = null;
  const routeErrors = [];
  let stubsLive = false;
  try {
    console.log('Starting local headless Chrome');
    page = await launch({ port: 8799 });
    console.log('Chrome ready; starting the local relay');
    relay = await startRelay();
    site = await startSite(8798);
    home = new FakePc(relay);
    other = new FakePc(relay);
    await home.connect();
    await other.connect();
    for (const pc of [home, other]) pc.conn.json = async (type, timeout) => {
      for (;;) {
        const item = await pc.conn.next(timeout);
        assert.ok(item.text, 'expected a relay notice');
        if (item.data.t === type) return item.data;
        assert.ok(['joined', 'left'].includes(item.data.t), 'unexpected relay notice: ' + item.data.t);
      }
    };
    page.on((method, event) => {
      if (method !== 'Fetch.requestPaused') return;
      const url = new URL(event.request.url);
      const stub = /^\/(help|download)(\/|$)/.test(url.pathname);
      const action = stub ? page.send('Fetch.fulfillRequest', { requestId: event.requestId, responseCode: 200, body: Buffer.from('local stub').toString('base64') })
        : /^(127\.0\.0\.1|localhost|\[::1\])$/.test(url.hostname) ? page.send('Fetch.continueRequest', { requestId: event.requestId })
          : page.send('Fetch.failRequest', { requestId: event.requestId, errorReason: 'BlockedByClient' });
      action.catch(error => routeErrors.push(error.message));
    });
    await page.send('Fetch.enable', { patterns: [{ urlPattern: '*' }] });
    await page.send('Page.addScriptToEvaluateOnNewDocument', { source: earlyAccess() });
    await page.send('Page.addScriptToEvaluateOnNewDocument', { source: CLIPBOARD });
    await setPhone(page, [390, 844], HOME_SCREEN);
    const base = `http://127.0.0.1:8798/?ios=app&relay=${relay.ws}`;
    // A new query each time, so the page loads afresh rather than only changing its #fragment.
    let loads = 0;
    const open = async hash => {
      await page.go(base + '&load=' + ++loads + hash);
      await page.until(`document.readyState === 'complete'`, 'the page');
    };
    const click = async selector => {
      assert.equal(stubsLive, true, 'help and download stubs must be live before any click');
      assert.deepEqual(routeErrors, []);
      await page.run(`document.querySelector(${JSON.stringify(selector)}).click()`);
    };
    const saved = () => page.run(`(${readSaved.toString()})()`);
    const acceptHello = async pc => {
      for (;;) {
        const item = await pc.conn.next(10000);
        if (!item.text) { pc.conn.inbox.unshift(item); break; }
        assert.ok(['joined', 'left'].includes(item.data.t), 'unexpected relay notice: ' + item.data.t);
      }
      await pc.acceptHello();
      await page.until(`CapriRemote.state === 'connected'`, 'the PC connected');
    };
    const confirm = () => page.until(`document.querySelector('#app').dataset.screen === 'replace'`, 'the replacement confirmation');
    const waiting = () => page.until(`document.querySelector('#app').dataset.screen === 'wait'`, 'Allow with a code');
    const qr = await home.showCode();
    await open(qr.slice(qr.indexOf('#')));
    assert.deepEqual(await page.run(`Promise.all(['/help', '/download'].map(path => fetch(path).then(response => response.text())))`), ['local stub', 'local stub']);
    stubsLive = true;
    await waiting();
    await home.allow(await home.pairRequest());
    await acceptHello(home);
    let original = await saved();

    await t.test('a bad fragment keeps the PC and says the link did not read right', async () => {
      await open('#r=a');
      await acceptHello(home);
      assert.equal(await page.run('location.hash'), '');
      assert.equal(await page.run('CapriRemote.info.pairReason'), 'bad-link');
      assert.deepEqual(await saved(), original);
    });

    const foreign = await other.showCode();
    const foreignHash = foreign.slice(foreign.indexOf('#'));
    await t.test('Keep, Close and Back leave the pairing and never start a foreign connection', async () => {
      for (const action of ['#replace-keep', '#replace-close', 'back']) {
        await open(foreignHash);
        await confirm();
        assert.equal(await page.run(`document.querySelector('#replace-pc').textContent`), 'This phone is paired with Test PC. A new PC replaces it.');
        assert.equal(await page.run('document.activeElement.id'), 'replace-keep');
        assert.deepEqual(await page.run('window.__pairSockets'), []);
        assert.deepEqual(await saved(), original);
        for (const size of IPHONES) {
          await setPhone(page, size, HOME_SCREEN);
          assert.deepEqual(await misfitsOf(page), [], 'the confirmation at ' + size.join('x'));
        }
        if (action === 'back') await page.run('history.back()');
        else await click(action);
        await acceptHello(home);
        assert.deepEqual(await saved(), original);
      }
      await setPhone(page, [390, 844], HOME_SCREEN);
    });

    await t.test('going ahead and then a deny restores the old PC', async () => {
      await open(foreignHash);
      await confirm();
      await click('#replace-go');
      await waiting();
      const ask = await other.pairRequest();
      assert.deepEqual(await saved(), original);
      await other.deny(ask);
      await acceptHello(home);
      assert.deepEqual(await saved(), original);
    });

    await t.test('a pasted foreign via=phone link waits for consent and still shows the code', async () => {
      const url = await other.showCode();
      const pasted = base + url.slice(url.indexOf('#')) + '&via=phone';
      await page.run(`window.__clip.text = ${JSON.stringify(pasted)}`);
      await click('#paste-go');
      await confirm();
      assert.equal(await page.run('window.__clip.text'), pasted);
      await click('#replace-keep');
      await acceptHello(home);
      assert.equal(await page.run('window.__clip.text'), pasted);
      assert.deepEqual(await saved(), original);
      await click('#paste-go');
      await confirm();
      await click('#replace-go');
      await waiting();
      const ask = await other.pairRequest();
      assert.equal(await page.run(`document.querySelector('#wait-title').textContent`), 'Allow on your PC');
      assert.equal(await page.run(`document.querySelector('#wait-check').hidden || document.querySelector('#wait-code').hidden`), false);
      assert.equal(await page.run(`document.querySelector('#wait-code').textContent`), ask.confirm);
      await page.until(`window.__clip.text === ''`, 'the clipboard cleared after starting');
      assert.deepEqual(await saved(), original);
      await other.deny(ask);
      await acceptHello(home);
      assert.deepEqual(await saved(), original);
    });

    await t.test('a same-room fragment pairs without a replacement confirmation', async () => {
      const url = await home.showCode();
      await open(url.slice(url.indexOf('#')));
      await waiting();
      assert.equal(await page.run(`document.querySelector('#replace').hidden`), true);
      assert.deepEqual(await saved(), original);
      await home.allow(await home.pairRequest());
      await acceptHello(home);
      const next = await saved();
      assert.equal(next.device.room, original.device.room);
      assert.notEqual(next.device.device, original.device.device);
      original = next;
    });

    await t.test('an allowed foreign pairing replaces the stored device', async () => {
      const url = await other.showCode();
      await open(url.slice(url.indexOf('#')) + '&via=phone');
      await confirm();
      await click('#replace-go');
      await waiting();
      assert.equal(await page.run(`document.querySelector('#wait-code').hidden`), false);
      assert.deepEqual(await saved(), original);
      await other.allow(await other.pairRequest());
      await acceptHello(other);
      const next = await saved();
      assert.equal(next.device.room, other.room);
      assert.notEqual(next.device.device, original.device.device);
    });
    assert.deepEqual(routeErrors, []);
  } finally {
    if (page) await page.close();
    if (home) home.close();
    if (other) other.close();
    if (site) await site.close();
    if (relay) await relay.stop();
  }
});

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { chromePath, launch } from './lib/cdp.mjs';
import { FakePc } from './lib/fakes.mjs';
import * as R from './lib/capri-remote.mjs';
import { startRelay } from './lib/relay-dev.mjs';
import { earlyAccess, startSite } from './lib/site.mjs';

// Phone notifications (PROTOCOL.md 8.7) on a phone paired with a stand-in PC: the menu section only with the feature, the
// Android app's route, answers from the app, push.away, unpairing, and the web route up to the subscription (headless
// Chrome has no push service, so PushManager and the permission are stubbed).
const SITE_PORT = 8812, CHROME_PORT = 8814;
const KEY = R.b64u(Uint8Array.from({ length: 65 }, (_, at) => at === 0 ? 4 : at));

const androidStub = `(() => {
  const calls = [];
  let on = false;
  window.__calls = calls;
  window.CapriAndroid = {
    info: () => JSON.stringify({ bubble: false, overlay: true, ar: false }),
    bubble: () => '{}', room: () => false, said: () => {}, wake: () => '{"sent":0}',
    notifyInfo: () => JSON.stringify({ on, allowed: true }),
    notifySet: value => { on = value; calls.push(['notifySet', value]); return JSON.stringify({ on, allowed: true }); },
    notify: json => calls.push(['notify', json])
  };
})()`;

const webStub = `(() => {
  const log = [];
  window.__web = log;
  let sub = null;
  Object.defineProperty(Notification, 'permission', { get: () => 'granted' });
  Notification.requestPermission = async () => { log.push(['permission']); return 'granted'; };
  PushManager.prototype.getSubscription = async () => sub;
  PushManager.prototype.subscribe = async options => {
    log.push(['subscribe', options.userVisibleOnly, [...new Uint8Array(options.applicationServerKey)].length]);
    sub = {
      options,
      toJSON: () => ({ endpoint: 'https://fcm.googleapis.com/fcm/send/abc', keys: { p256dh: 'p', auth: 'a' } }),
      unsubscribe: async () => { log.push(['unsubscribe']); sub = null; return true; }
    };
    return sub;
  };
})()`;

const again = async (pc, features) => {
  for (let tries = 0; tries < 20; tries++) {
    try { return await pc.acceptHello(features); } catch (error) { if (!/envelope rejected|expected a binary frame/.test(error.message)) throw error; }
  }
  throw new Error('no hello came');
};

const rig = async (name, { features, init = [], search = '' }, body) => {
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
    await page.send('Page.addScriptToEvaluateOnNewDocument', { source: earlyAccess() });
    for (const source of init) await page.send('Page.addScriptToEvaluateOnNewDocument', { source });
    await page.go(`http://127.0.0.1:${SITE_PORT}/?relay=${relay.ws}${search}${qr.slice(qr.indexOf('#'))}`);
    const device = await pc.allow(await pc.pairRequest());
    await pc.conn.json('joined', 10000);
    await pc.acceptHello(features);
    await page.until(`document.querySelector('#app').dataset.link === 'connected'`, 'the paired PC');
    const next = async type => {
      for (;;) {
        const item = await pc.conn.next(10000);
        if (item.text) continue;
        const { device: from, envelope } = R.splitPcFrame(item.data);
        const message = await pc.devices.get(from).session.open(envelope);
        if (message.type === type) return message;
        if (message.type === 'job.list') await pc.jobsMessage(from, message);
      }
    };
    await body({ pc, page, device, relay, next, errors, hello: () => again(pc, features), reload: () => page.go(`http://127.0.0.1:${SITE_PORT}/?relay=${relay.ws}`) });
    assert.deepEqual(errors, []);
  } finally {
    if (page) await page.close();
    pc.close();
    await site.close();
    await relay.stop();
  }
};

const skip = !chromePath() && 'Chrome is not installed';

test('push: the menu section only shows when the PC offers push', { skip, timeout: 60000 }, async () => {
  await rig('without', { features: ['chat', 'jobs'] }, async ({ page }) => {
    assert.equal(await page.run(`document.querySelector('#d-push').hidden`), true);
  });
  await rig('with', { features: ['chat', 'jobs', 'push'], init: [webStub] }, async ({ page, next }) => {
    await page.until(`!document.querySelector('#d-push').hidden`, 'the Notifications section');
    assert.equal(await page.run(`document.querySelector('#d-push-head').textContent`), 'Notifications');
    assert.equal(await page.run(`document.querySelector('#d-push-on').textContent`), 'Notifications: off');
    assert.equal(await page.run(`document.querySelector('#d-push-on').getAttribute('aria-pressed')`), 'false');
    assert.equal(await page.run(`document.querySelector('#d-push-test').hidden`), true);
    assert.equal(await page.run(`document.querySelector('#d-push-note').getAttribute('role')`), 'status');
    assert.deepEqual(await next('push.get'), { type: 'push.get' });
  });
});

test('push: Android app route, notify, answers, away and unpair', { skip, timeout: 90000 }, async () => {
  await rig('android', { features: ['chat', 'jobs', 'push'], init: [androidStub] }, async ({ pc, page, device, next }) => {
    await page.until(`!document.querySelector('#d-push').hidden`, 'the Notifications section');
    assert.equal(await page.run(`window.CapriKeepAlive`), false);
    await next('push.get');
    await pc.send(device.id, { type: 'push.state', key: KEY, on: false, web: false, app: false });
    await page.run(`document.querySelector('#d-push-on').click()`);
    const sub = await next('push.sub');
    assert.equal(sub.on, true);
    assert.equal(sub.app, true);
    assert.equal(sub.web, undefined);
    assert.match(sub.id, /^[0-9a-f]{16}$/);
    assert.deepEqual(await page.run(`window.__calls`), [['notifySet', true]]);
    await pc.send(device.id, { type: 'push.result', re: sub.id, ok: true, code: 'saved' });
    await page.until(`document.querySelector('#d-push-on').textContent === 'Notifications: on'`, 'the switch on');
    assert.equal(await page.run(`localStorage.getItem('capri-remote-push')`), 'on');
    assert.equal(await page.run(`document.querySelector('#d-push-on').getAttribute('aria-pressed')`), 'true');
    assert.equal(await page.run(`document.querySelector('#d-push-test').hidden`), false);
    assert.equal(await page.run(`window.CapriKeepAlive`), true);

    await page.run(`document.querySelector('#d-push-test').click()`);
    const probe = await next('push.test');
    await pc.send(device.id, { type: 'push.result', re: probe.id, ok: true, code: 'sent' });
    await page.until(`/Sent/.test(document.querySelector('#d-push-note').textContent)`, 'the test result');

    const note = { type: 'notify', id: 'n1', kind: 'ask', title: 'Open Steam', body: 'Allow it?', at: Date.now(), job: 'job-1', ask_id: 'q1' };
    await pc.send(device.id, note);
    await page.until(`window.__calls.some(call => call[0] === 'notify')`, 'the notify forwarded');
    assert.deepEqual(JSON.parse(await page.run(`window.__calls.find(call => call[0] === 'notify')[1]`)), note);

    const job = await pc.jobState({ id: 'job-1', task: 'Open Steam', state: 'asking', ask: 'Allow it?', ask_id: 'q1' });
    await page.until(`CapriJobs.state('job-1') && CapriJobs.state('job-1').state === 'asking'`, 'the asking job');
    assert.deepEqual(await page.run(`CapriJobs.state('job-1')`), { state: 'asking', ask: 'Allow it?', ask_id: 'q1' });
    assert.equal(await page.run(`CapriAndroidAnswer(JSON.stringify({ job: 'job-1', ask_id: 'old', yes: true }))`), false);
    assert.equal(await page.run(`document.querySelector('#jobs').hidden`), false, 'a stale answer opens the job instead');
    assert.match(await page.run(`document.querySelector('.jobs-note').textContent`), /already answered/);
    await page.run(`CapriJobs.close()`);
    assert.equal(await page.run(`CapriAndroidAnswer(JSON.stringify({ job: 'job-1', ask_id: 'q1', yes: true }))`), true);
    const answer = await next('job.answer');
    assert.deepEqual(answer, { type: 'job.answer', id: job.id, yes: true, ask_id: 'q1' });
    await pc.jobsMessage(device.id, answer);
    await page.until(`CapriJobs.state('job-1').state === 'running'`, 'the job going on');
    assert.equal(await page.run(`CapriAndroidAnswer(JSON.stringify({ job: 'job-1', ask_id: 'q1', yes: true }))`), false, 'no second answer');
    await page.run(`CapriJobs.close()`);
    await page.run(`CapriAndroidOpen(JSON.stringify({ kind: 'ask', job: 'job-1', id: 'n1' }))`);
    assert.equal(await page.run(`document.querySelector('#jobs').hidden`), false);
    await page.run(`CapriAndroidOpen(JSON.stringify({ kind: 'reply', id: 'n2' }))`);
    assert.equal(await page.run(`document.querySelector('#jobs').hidden`), true);

    await page.run(`Object.defineProperty(document, 'hidden', { configurable: true, get: () => true }); document.dispatchEvent(new Event('visibilitychange'))`);
    assert.deepEqual(await next('push.away'), { type: 'push.away', away: true });
    await page.run(`Object.defineProperty(document, 'hidden', { configurable: true, get: () => false }); document.dispatchEvent(new Event('visibilitychange'))`);
    assert.deepEqual(await next('push.away'), { type: 'push.away', away: false });

    await page.run(`document.querySelector('#d-push-on').click()`);
    const off = await next('push.sub');
    assert.equal(off.on, false);
    assert.equal(await page.run(`localStorage.getItem('capri-remote-push')`), null);
    assert.deepEqual(await page.run(`window.__calls.filter(call => call[0] === 'notifySet')`), [['notifySet', true], ['notifySet', false]]);

    await pc.send(device.id, { type: 'push.state', key: KEY, on: true, web: false, app: true });
    await page.run(`localStorage.setItem('capri-remote-push', 'on'); CapriRemote.unpair()`);
    await page.until(`localStorage.getItem('capri-remote-push') === null && window.__calls.filter(call => call[0] === 'notifySet').length === 3`, 'unpair forgetting the wish');
    assert.equal(await page.run(`document.querySelector('#d-push').hidden`), true);
  });
});

test('push: a hidden page asks away at the start of a session, and a wish is renewed quietly', { skip, timeout: 90000 }, async () => {
  const hidden = `Object.defineProperty(document, 'hidden', { configurable: true, get: () => true });`;
  await rig('hidden', { features: ['chat', 'push'], init: [androidStub, hidden] }, async ({ pc, page, device, next, reload, hello }) => {
    await next('push.away');
    await page.run(`localStorage.setItem('capri-remote-push', 'on')`);
    await reload();
    await hello();
    assert.deepEqual(await next('push.get'), { type: 'push.get' });
    assert.deepEqual(await next('push.away'), { type: 'push.away', away: true });
    await pc.send(device.id, { type: 'push.state', key: KEY, on: false, web: false, app: false });
    const sub = await next('push.sub');
    assert.equal(sub.app, true);
    assert.equal(sub.on, true);
  });
});

test('push: web route asks permission, subscribes with the PC key and saves it (stubbed PushManager)', { skip, timeout: 90000 }, async () => {
  await rig('web', { features: ['chat', 'push'], init: [webStub] }, async ({ pc, page, device, next }) => {
    await next('push.get');
    await pc.send(device.id, { type: 'push.state', key: KEY, on: false, web: false, app: false });
    await page.run(`document.querySelector('#d-push-on').click()`);
    const sub = await next('push.sub');
    assert.equal(sub.on, true);
    assert.deepEqual(sub.web, { endpoint: 'https://fcm.googleapis.com/fcm/send/abc', p256dh: 'p', auth: 'a' });
    assert.equal(sub.app, undefined);
    assert.deepEqual(await page.run(`window.__web`), [['subscribe', true, 65]]);
    await pc.send(device.id, { type: 'push.result', re: sub.id, ok: true, code: 'saved' });
    await page.until(`document.querySelector('#d-push-on').textContent === 'Notifications: on'`, 'the switch on');
    await pc.send(device.id, { type: 'notify', id: 'n1', kind: 'reply', title: 'Capri', body: 'Hi', at: Date.now() });
    await page.run(`document.querySelector('#d-push-on').click()`);
    const off = await next('push.sub');
    assert.equal(off.on, false);
    await page.until(`window.__web.some(call => call[0] === 'unsubscribe')`, 'the browser subscription dropped');
  });
});

test('push: a web notification opens the Jobs view at its job', { skip, timeout: 90000 }, async () => {
  await rig('launch', { features: ['chat', 'jobs', 'push'], init: [webStub], search: '&job=job-9' }, async ({ pc, page, device, next }) => {
    await pc.jobState({ id: 'job-9', task: 'Open Discord', state: 'done', ended: Date.now() });
    await page.until(`document.querySelector('#jobs') && !document.querySelector('#jobs').hidden`, 'the Jobs view');
    assert.equal(await page.run(`new URLSearchParams(location.search).has('job')`), false);
  });
});

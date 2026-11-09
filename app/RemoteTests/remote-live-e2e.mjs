// Capri Remote's screen view and call end to end. RemoteTests.exe live runs RemoteLink with RemoteScreen and RemoteCall on
// a drawn two-monitor desktop with stand-in ears and a test tone for her voice, through the real relay under
// `wrangler dev --local`. Fake phones watch, ack, switch monitors, talk and listen. Nothing real is captured or recorded.
// At the end it prints what the relay carries a second for each quality and scene, and for a call.
//
//   node app/RemoteTests/remote-live-e2e.mjs <RemoteTests.exe> [--relay wss://<host>]
//
// With --relay, the same steps against that running relay: fresh rooms, and no keys, tokens or codes printed.

import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { FakePhone } from '../../remote/test/lib/fakes.mjs';
import * as L from '../../remote/test/lib/live.mjs';
import * as R from '../../remote/test/lib/capri-remote.mjs';
import { startRelay } from '../../remote/test/lib/relay-dev.mjs';

const USAGE = 'usage: node remote-live-e2e.mjs <RemoteTests.exe> [--relay wss://<host>]';
const argv = process.argv.slice(2);
const flag = argv.indexOf('--relay');
const live = flag < 0 ? null : liveRelay(argv.splice(flag, 2)[1]);
const exe = argv[0];
if (!exe) throw new Error(USAGE);

const BEAT = live ? 5000 : 1000;
const PHONE_BEAT = live ? 10000 : 500;
const MEASURE_MS = 4000;
const VIEW = { width: 1280, height: 720 };
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

function liveRelay(text) {
  if (!text) throw new Error(USAGE);
  const url = new URL(text);
  const loopback = /^(127\.0\.0\.1|localhost|\[::1\])$/.test(url.hostname);
  if (!(url.protocol === 'wss:' || (url.protocol === 'ws:' && loopback))) throw new Error('--relay takes wss://<host>, or ws:// on this machine');
  if (url.pathname !== '/' || url.search || url.hash || url.username || url.password) throw new Error('--relay takes a scheme, a host and a port, nothing more');
  return `${url.protocol}//${url.host}`;
}

const hide = (text) => String(text).replace(/[A-Za-z0-9_-]{20,}/g, '[hidden]').replace(/\b\d{3} \d{3}\b/g, '[code]');
const say = (text) => console.log(live ? hide(text) : String(text));

// Waits on a list of records for the first one not yet taken that matches.
function waitIn(owner, list, match, timeout, what) {
  return new Promise((resolve, reject) => {
    const look = () => {
      const found = list.find((item) => !item.taken && match(item));
      if (!found) return false;
      found.taken = true;
      owner.waiters.delete(look);
      clearTimeout(timer);
      resolve(found);
      return true;
    };
    const timer = setTimeout(() => {
      owner.waiters.delete(look);
      reject(new Error(`timed out waiting for ${what}. Last: ${JSON.stringify(list.slice(-8).map((item) => item.m ?? item), (key, value) => (key === 'data' || key === 'pcm' ? `[${value.length}]` : value))}`));
    }, timeout);
    if (!look()) owner.waiters.add(look);
  });
}

// ---------- The PC: RemoteTests.exe live, one JSON event per line ----------
class Pc {
  static async start(relayUrl, statePath) {
    const child = spawn(exe, ['live', relayUrl, statePath, String(BEAT)], { stdio: ['pipe', 'pipe', 'pipe'] });
    const pc = new Pc(child);
    await pc.wait((e) => e.e === 'status' && e.status === 'online', 30000, 'the PC to sign in');
    return pc;
  }

  constructor(child) {
    this.child = child;
    this.events = [];
    this.waiters = new Set();
    this.err = '';
    let rest = '';
    child.stdout.setEncoding('utf8');
    child.stdout.on('data', (chunk) => {
      rest += chunk;
      let at;
      while ((at = rest.indexOf('\n')) >= 0) {
        const line = rest.slice(0, at).trim();
        rest = rest.slice(at + 1);
        if (!line) continue;
        const event = JSON.parse(line);
        event.at = Date.now();
        if (event.e === 'unknown') throw new Error(`the PC doesn't know the command "${event.c}"`);
        this.events.push(event);
        for (const waiter of [...this.waiters]) waiter();
      }
    });
    child.stderr.setEncoding('utf8');
    child.stderr.on('data', (chunk) => (this.err += chunk));
    this.exited = new Promise((resolve) => child.on('exit', resolve));
  }

  wait(match, timeout = 8000, what = 'an event') {
    return waitIn(this, this.events, match, timeout, what);
  }

  async none(match, ms, what) {
    const since = Date.now();
    await sleep(ms);
    const event = this.events.find((e) => !e.taken && e.at >= since && match(e));
    assert.equal(event, undefined, `did not expect ${what}: ${JSON.stringify(event)}`);
  }

  send(command) {
    this.child.stdin.write(JSON.stringify(command) + '\n');
  }

  // Everything so far counts as seen: the next waits only match what comes after.
  skip() {
    for (const event of this.events) event.taken = true;
  }

  async stats() {
    this.send({ c: 'stats' });
    return this.wait((e) => e.e === 'stats', 5000, 'the stats');
  }

  // The badge's newest list of watchers or callers once it matches.
  async list(what, match, label) {
    return (await this.wait((e) => e.e === what && match(e.list), 6000, label)).list;
  }

  async quit() {
    this.send({ c: 'quit' });
    await this.wait((e) => e.e === 'bye', 15000, 'the PC to stop');
    await this.exited;
  }
}

// ---------- A phone with the screen view and the call ----------
// Every sealed message in is opened in order and kept with its size on the wire. Frames are put back together and
// acked, unless acking is off.
class Viewer {
  constructor(name) {
    this.name = name;
    this.fake = new FakePhone(relay, { name, heartbeat: PHONE_BEAT });
    this.got = [];
    this.frames = [];
    this.waiters = new Set();
    this.acking = true;
    this.acked = 0;
    this.sent = 0;
    viewers.push(this);
  }

  async pair(pc) {
    pc.send({ c: 'code' });
    assert.equal((await pc.wait((e) => e.e === 'code', 5000, 'the code answer')).ok, true);
    const url = (await pc.wait((e) => e.e === 'qr' && e.url, 5000, 'a QR')).url;
    await this.fake.scan(url);
    await this.fake.conn.json('waiting');
    const ask = await pc.wait((e) => e.e === 'ask', 8000, 'the Allow prompt');
    assert.equal(ask.code, this.fake.confirm);
    pc.send({ c: 'allow' });
    assert.equal((await pc.wait((e) => e.e === 'allow')).ok, true);
    const { message } = await this.fake.pairReply();
    assert.equal(message.type, 'pair-allow');
    this.device = message.device;
    await pc.wait((e) => e.e === 'paired' && e.about === this.device, 8000, 'the admit ok');
  }

  async session(pc) {
    const ready = await this.fake.connect();
    assert.equal(ready.pc, true);
    await pc.wait((e) => e.e === 'joined' && e.about === this.device, 8000, 'joined');
    const ok = await this.fake.hello();
    await pc.wait((e) => e.e === 'session' && e.about === this.device, 8000, 'the session');
    this.pump(this.fake.conn, this.fake.session, new L.FrameAssembler());
    return ok;
  }

  async pump(conn, session, frames) {
    for (;;) {
      let item;
      try {
        item = await conn.next(3600000);
      } catch {
        if (conn.closeInfo) return;
        continue;
      }
      if (item.text) {
        this.add({ relay: item.data, at: Date.now(), size: 0 });
        continue;
      }
      const message = await session.open(item.data);
      const got = { m: message, size: item.data.length, at: Date.now() };
      if (message.type === 'watch.frame' || message.type === 'call.voice') {
        assert.ok(item.data.length <= L.MAX_PHONE_ENVELOPE, `a ${message.type} of ${item.data.length} bytes`);
        assert.ok(JSON.stringify(message).length <= L.MAX_PLAINTEXT, `a ${message.type} over ${L.MAX_PLAINTEXT} characters`);
      }
      if (message.type === 'watch.frame') {
        const frame = frames.add(message);
        if (frame) {
          frame.at = got.at;
          this.frames.push(frame);
          if (this.acking) {
            this.acked = frame.n;
            this.send({ type: 'watch.ack', n: frame.n });
          }
        }
      }
      this.add(got);
    }
  }

  skip() {
    for (const item of this.got) item.taken = true;
  }

  add(record) {
    this.got.push(record);
    for (const waiter of [...this.waiters]) waiter();
  }

  send(message) {
    this.sent += L.envelopeBytes(JSON.stringify(message).length);
    return this.fake.send(message);
  }

  async sendAll(messages, gap = 0) {
    for (const message of messages) {
      await this.send(message);
      if (gap) await sleep(gap);
    }
  }

  wait(match, timeout = 6000, what = 'a message') {
    return waitIn(this, this.got, (item) => item.m && match(item.m), timeout, what);
  }

  async none(match, ms, what) {
    const since = Date.now();
    await sleep(ms);
    const found = this.got.find((item) => item.at >= since && item.m && match(item.m));
    assert.equal(found, undefined, `did not expect ${what}: ${JSON.stringify(found && found.m.type)}`);
  }

  framesFrom(since, until = Infinity) {
    return this.frames.filter((frame) => frame.at >= since && frame.at < until);
  }

  async frameAfter(since, match = () => true, timeout = 6000) {
    const deadline = Date.now() + timeout;
    for (;;) {
      const frame = this.frames.find((f) => f.at >= since && match(f));
      if (frame) return frame;
      if (Date.now() > deadline) throw new Error(`no frame came. Last: ${JSON.stringify(this.frames.slice(-3).map(({ jpeg, ...f }) => f))}`);
      await sleep(25);
    }
  }

  close() {
    this.fake.close();
  }
}

// ---------- Steps ----------
let passed = 0;
const viewers = [];
const fresh = () => {
  if (pc) pc.skip();
  for (const viewer of viewers) viewer.skip();
};
const step = async (name, run) => {
  const began = Date.now();
  fresh();
  await run();
  passed++;
  say(`PASS ${name} (${Date.now() - began} ms)`);
};
const plain = ({ m }) => {
  const { taken, ...rest } = m;
  return rest;
};

const work = mkdtempSync(join(tmpdir(), 'capri-remote-live-'));
const statePath = join(work, 'state.dpapi');
let relay, pc;
const rates = [];

let cleaned;
const cleanup = () =>
  (cleaned ??= (async () => {
    if (pc && pc.child.exitCode === null) pc.child.kill();
    if (relay) await relay.stop();
    rmSync(work, { recursive: true, force: true });
  })());
const fail = (error) => {
  say('FAIL ' + (error && error.stack ? error.stack : error));
  if (pc && pc.err) say('PC stderr:\n' + pc.err);
  process.exitCode = 1;
};
for (const what of ['uncaughtException', 'unhandledRejection'])
  process.on(what, (error) => {
    fail(error);
    cleanup().finally(() => process.exit(1));
  });

try {
  relay = live ? { ws: live, stop: async () => {} } : await startRelay();
  pc = await Pc.start(relay.ws, statePath);
  const a = new Viewer('Live Phone');

  await step('hello-ok offers the screen view and the call, then her brains', async () => {
    await a.pair(pc);
    const ok = await a.session(pc);
    assert.deepEqual(ok.features, ['chat', 'watch', 'call', 'brains', 'pair']);
  });

  await step('nothing is captured or sent until a phone asks', async () => {
    await sleep(1500);
    assert.equal(a.got.filter((item) => item.m && /^(watch|call)\./.test(item.m.type)).length, 0);
    const stats = await pc.stats();
    assert.equal(stats.captures, 0);
    assert.equal(stats.frames, 0);
  });

  await step('watch: on, the monitor list, the badge, frames in parts the relay takes at three a second', async () => {
    pc.send({ c: 'look', monitor: '1' });
    const since = Date.now();
    await a.send({ type: 'watch.start', monitor: 'auto', quality: 'balanced', ...VIEW });
    assert.deepEqual(plain(await a.wait((m) => m.type === 'watch.state')), { type: 'watch.state', on: true });
    const list = (await a.wait((m) => m.type === 'watch.monitors')).m;
    assert.deepEqual(list.list.map((m) => [m.id, m.name, m.w, m.h, m.primary]), [['1', 'Monitor 1', 1920, 1080, true], ['2', 'Monitor 2', 2560, 1440, false]]);
    assert.equal(list.monitor, '1');
    assert.equal(list.auto, true);
    assert.deepEqual(await pc.list('watchers', (l) => l.length === 1 && l[0].includes('@1@'), 'the badge'), [`${a.device}@1@Live Phone`]);
    await sleep(3000);
    const frames = a.framesFrom(since);
    assert.ok(frames.length >= 6 && frames.length <= 11, `${frames.length} frames in about 3 s`);
    for (const frame of frames) assert.deepEqual([frame.w, frame.h, frame.monitor], [960, 540, '1']);
    assert.deepEqual([frames[0].cx, frames[0].cy], [5000, 5000]);
    frames.forEach((frame, i) => i && assert.equal(frame.n, frames[i - 1].n + 1));
  });

  await step('flow control: never more than two frames ahead of the acks, and one more after 4 s in case an ack was lost', async () => {
    a.acking = false;
    const lastAcked = a.acked;
    await sleep(3000);
    assert.equal(a.frames.filter((f) => f.n > lastAcked).length, 2, 'two frames past the last ack');
    await sleep(3000);
    assert.equal(a.frames.filter((f) => f.n > lastAcked).length, 3, 'one more after the ack wait');
    a.acking = true;
    const since = Date.now();
    await a.send({ type: 'watch.ack', n: a.frames.at(-1).n });
    a.acked = a.frames.at(-1).n;
    await a.frameAfter(since, () => true, 2000);
    await sleep(1000);
    assert.ok(a.framesFrom(since).length >= 3, 'frames flow again');
  });

  await step('a screen that stays the same sends nothing; the mouse moving over it goes as watch.cursor', async () => {
    pc.send({ c: 'scene', name: 'still' });
    await sleep(1200);
    const since = Date.now();
    await sleep(2000);
    assert.equal(a.framesFrom(since).length, 0);
    pc.send({ c: 'mouse', x: 100, y: 100 });
    assert.deepEqual(plain(await a.wait((m) => m.type === 'watch.cursor' && m.cx === 520, 3000, 'the cursor')), { type: 'watch.cursor', cx: 520, cy: 925 });
    pc.send({ c: 'mouse', x: 2500, y: 500 });
    assert.deepEqual(plain(await a.wait((m) => m.type === 'watch.cursor' && m.cx === -1, 3000, 'the cursor leaving')), { type: 'watch.cursor', cx: -1, cy: -1 });
    assert.ok((await pc.stats()).unchanged > 0);
    pc.send({ c: 'mouse', x: 960, y: 540 });
    pc.send({ c: 'scene', name: 'typing' });
  });

  await step('monitors: a pick, Auto following her after a second, a glance that does not flick, an unplugged pick', async () => {
    let since = Date.now();
    await a.send({ type: 'watch.start', monitor: '2', quality: 'balanced', ...VIEW });
    assert.equal((await a.wait((m) => m.type === 'watch.monitors' && m.monitor === '2', 4000, 'monitor 2')).m.auto, false);
    const two = await a.frameAfter(since, (f) => f.monitor === '2');
    assert.deepEqual([two.w, two.h], [960, 540]);
    await pc.list('watchers', (l) => l.length === 1 && l[0].includes('@2@'), 'the badge on monitor 2');

    fresh();
    since = Date.now();
    await a.send({ type: 'watch.start', monitor: 'auto', quality: 'balanced', ...VIEW });
    const back = await a.wait((m) => m.type === 'watch.monitors' && m.monitor === '1' && m.auto, 5000, 'Auto to go to monitor 1');
    assert.ok(back.at - since >= 800, `Auto moved after ${back.at - since} ms`);

    const glance = Date.now();
    pc.send({ c: 'look', monitor: '2' });
    await sleep(500);
    pc.send({ c: 'look', monitor: '1' });
    await sleep(1800);
    assert.equal(a.got.filter((item) => item.at >= glance && item.m && item.m.type === 'watch.monitors' && item.m.monitor === '2').length, 0, 'a glance moved it');

    fresh();
    since = Date.now();
    pc.send({ c: 'look', monitor: '2' });
    const moved = await a.wait((m) => m.type === 'watch.monitors' && m.monitor === '2' && m.auto, 5000, 'Auto to follow her to monitor 2');
    assert.ok(moved.at - since >= 800, `Auto moved after ${moved.at - since} ms`);
    await a.frameAfter(moved.at - 50, (f) => f.monitor === '2');

    pc.send({ c: 'look', monitor: '1' });
    fresh();
    await a.send({ type: 'watch.start', monitor: '2', quality: 'balanced', ...VIEW });
    await a.wait((m) => m.type === 'watch.monitors' && m.monitor === '2' && !m.auto, 4000, 'the pick of 2');
    pc.send({ c: 'monitors', list: [{ id: '1', x: 0, y: 0, w: 1920, h: 1080, primary: true }] });
    const missing = (await a.wait((m) => m.type === 'watch.monitors' && m.missing === true, 5000, 'the missing pick')).m;
    assert.deepEqual([missing.monitor, missing.list.length], ['1', 1]);
    pc.send({ c: 'monitors', list: [{ id: '1', x: 0, y: 0, w: 1920, h: 1080, primary: true }, { id: '2', x: 1920, y: 0, w: 2560, h: 1440 }] });
    await a.wait((m) => m.type === 'watch.monitors' && m.monitor === '2' && !m.missing, 5000, 'monitor 2 back');
  });

  await step('a small view gets frames no bigger than it shows', async () => {
    const since = Date.now();
    await a.send({ type: 'watch.start', monitor: '1', quality: 'balanced', width: 400, height: 300 });
    const small = await a.frameAfter(since + 50, (f) => f.w !== 960);
    assert.deepEqual([small.w, small.h, small.monitor], [400, 225, '1']);
  });

  await step('qualities: saver, balanced and sharp sizes, and the bytes a second for each scene', async () => {
    for (const [quality, w, h, fps] of [['saver', 640, 360, 2], ['balanced', 960, 540, 3], ['sharp', 1280, 720, 4]]) {
      for (const scene of ['still', 'typing', 'scroll', 'video']) {
        pc.send({ c: 'scene', name: scene });
        await a.send({ type: 'watch.start', monitor: '1', quality, ...VIEW });
        await sleep(1500);
        const since = Date.now();
        await sleep(MEASURE_MS);
        const until = Date.now();
        const seconds = (until - since) / 1000;
        const frames = a.framesFrom(since, until);
        const wire = a.got.filter((item) => item.at >= since && item.at < until && item.m && item.m.type.startsWith('watch.')).reduce((sum, item) => sum + item.size, 0);
        for (const frame of frames) assert.deepEqual([frame.w, frame.h], [w, h], `${quality} frame size`);
        if (scene === 'still') assert.equal(frames.length, 0, 'a still screen sent frames');
        else if (!(frames.length >= fps * seconds * 0.6 && frames.length <= fps * seconds + 1.5)) {
          const stats = await pc.stats();
          const recent = a.got.slice(-6).map((item) => (item.m ? item.m.type : JSON.stringify(item.relay)));
          throw new Error(`${frames.length} ${quality} ${scene} frames in ${seconds} s. PC: ${JSON.stringify(stats)}. Phone's last: ${recent}`);
        }
        const jpeg = frames.reduce((sum, frame) => sum + frame.jpeg.length, 0);
        assert.equal((await pc.stats()).failed, 0, `${quality} ${scene} frames failed on the PC`);
        rates.push({ quality, scene, fps: frames.length / seconds, frame: frames.length ? jpeg / frames.length : 0, parts: Math.max(0, ...frames.map((f) => f.parts)), bytes: wire / seconds });
      }
    }
  });

  const b = new Viewer('Second Phone');
  await step('two phones: the badge lists both with their monitors; watch.stop and leaving take them off', async () => {
    pc.send({ c: 'scene', name: 'typing' });
    await a.send({ type: 'watch.start', monitor: 'auto', quality: 'balanced', ...VIEW });
    await b.pair(pc);
    await b.session(pc);
    await b.send({ type: 'watch.start', monitor: '2', quality: 'saver', width: 800, height: 450 });
    const both = await pc.list('watchers', (l) => l.length === 2 && l.some((x) => x.startsWith(`${b.device}@2@`)), 'both on the badge');
    assert.ok(both.includes(`${b.device}@2@Second Phone`));
    const first = await b.frameAfter(0);
    assert.deepEqual([first.w, first.h, first.monitor], [640, 360, '2']);
    fresh();
    await b.send({ type: 'watch.stop' });
    assert.deepEqual(await pc.list('watchers', (l) => l.length === 1, 'B off the badge'), [`${a.device}@1@Live Phone`]);
    await sleep(600);
    const since = Date.now();
    await sleep(1500);
    assert.equal(b.framesFrom(since).length, 0, 'frames after watch.stop');
    assert.ok(a.framesFrom(since).length >= 3, 'the other phone kept watching');
    fresh();
    a.close();
    await pc.wait((e) => e.e === 'left' && e.about === a.device, 8000, 'A to leave');
    await pc.list('watchers', (l) => l.length === 0, 'nobody on the badge');
    await a.session(pc);
  });

  await step('a phone the PC will not let watch or call is told so, and bad asks get bad-message', async () => {
    pc.send({ c: 'let', watch: false, call: false });
    const since = Date.now();
    await a.send({ type: 'watch.start', monitor: 'auto' });
    assert.deepEqual(plain(await a.wait((m) => m.type === 'watch.state')), { type: 'watch.state', on: false, reason: 'not-allowed' });
    await a.send({ type: 'call.start' });
    assert.deepEqual(plain(await a.wait((m) => m.type === 'call.state')), { type: 'call.state', on: false, reason: 'not-allowed' });
    await sleep(1000);
    assert.equal(a.framesFrom(since).length, 0);
    pc.send({ c: 'let' });
    for (const bad of [{ monitor: '../1' }, { monitor: 'monitor-two' }, { quality: 'ultra' }, { width: 10 }, { height: 9000 }]) {
      await a.send({ type: 'watch.start', monitor: 'auto', quality: 'balanced', width: 640, height: 360, ...bad });
      assert.deepEqual(plain(await a.wait((m) => m.type === 'error')), { type: 'error', code: 'bad-message' }, JSON.stringify(bad));
    }
    await a.none((m) => m.type === 'watch.frame', 800, 'frames from bad asks');
  });

  let talk = 0;
  await step('call: a push-to-talk line in parts reaches her ears whole, and her answer comes back as chat', async () => {
    await a.send({ type: 'call.start' });
    assert.deepEqual(plain(await a.wait((m) => m.type === 'call.state')), { type: 'call.state', on: true, voice: true, ears: true });
    assert.deepEqual(await pc.list('callers', (l) => l.length === 1, 'the call on the badge'), [`${a.device}@@Live Phone`]);
    const line = L.tone(16000, 1.5, 220);
    const parts = L.audioParts('u1', line, 320);
    assert.equal(parts.length, 5);
    await a.sendAll(parts, 60);
    talk = parts.reduce((sum, part) => sum + L.envelopeBytes(JSON.stringify(part).length), 0) / 1.5;
    const ears = await pc.wait((e) => e.e === 'ears', 5000, 'her ears');
    assert.deepEqual([ears.samples, ears.hash], [line.length, L.fnv(line)]);
    assert.deepEqual(plain(await a.wait((m) => m.type === 'call.heard')), { type: 'call.heard', id: 'u1', text: 'testing one two three' });
    const heard = await pc.wait((e) => e.e === 'heard', 5000, 'the words to her');
    assert.deepEqual([heard.device, heard.id, heard.text], [a.device, 'u1', 'testing one two three']);
    pc.send({ c: 'reply', device: a.device, re: 'u1', id: 'r1', parts: ['Testing ', 'received.'], gap: 0 });
    let text = '';
    for (;;) {
      const reply = (await a.wait((m) => m.type === 'chat.reply', 5000, 'her answer')).m;
      assert.equal(reply.re, 'u1');
      text += reply.text;
      if (reply.done) break;
    }
    assert.equal(text, 'Testing received.');
    await a.sendAll(L.audioParts('u2', new Int16Array(8000), 320));
    assert.deepEqual(plain(await a.wait((m) => m.type === 'call.heard')), { type: 'call.heard', id: 'u2', text: '' });
    await pc.none((e) => e.e === 'heard', 800, 'silence to reach her');
  });

  let voice24 = 0;
  await step('call: her voice comes in quarter-second parts at up to 24 kHz, in time, and stops at once on a hush', async () => {
    const voiceFrom = async (rate, samples, label) => {
      const parts = [];
      let count = 0;
      while (count < samples) {
        const part = await a.wait((m) => m.type === 'call.voice', 5000, label);
        parts.push(part);
        assert.equal(part.m.rate, rate);
        count += L.pcmSamples(R.fromB64u(part.m.pcm)).length;
      }
      assert.equal(count, samples);
      return parts;
    };
    pc.send({ c: 'voice', seconds: 1, rate: 24000 });
    const burst = await voiceFrom(24000, 24000, 'her voice at 24 kHz');
    burst.forEach((part, i) => assert.equal(part.m.n, i));
    assert.deepEqual(burst.map((part) => R.fromB64u(part.m.pcm).length), [12000, 12000, 12000, 12000]);
    assert.ok(L.furthest(L.pcmSamples(R.concat(...burst.map((part) => R.fromB64u(part.m.pcm)))), L.tone(24000, 1, 330)) <= 1);
    voice24 = burst.reduce((sum, part) => sum + part.size, 0);

    pc.skip();
    pc.send({ c: 'voice', seconds: 1, rate: 44100, paced: true });
    const paced = await voiceFrom(22050, 22050, 'her voice at 44.1 kHz, halved');
    paced.forEach((part, i) => assert.equal(part.m.n, burst.length + i));
    const voiced = await pc.wait((e) => e.e === 'voiced', 5000, 'the paced voice to end');
    assert.ok(paced[0].at < voiced.at - 300, 'her voice came as she spoke, not after');
    assert.ok(L.furthest(L.pcmSamples(R.concat(...paced.map((part) => R.fromB64u(part.m.pcm)))), L.halve(L.tone(44100, 1, 330))) <= 1);

    pc.send({ c: 'voice', seconds: 0.1, rate: 24000 });
    pc.send({ c: 'hush' });
    await a.wait((m) => m.type === 'call.hush', 3000, 'the hush');
    await a.none((m) => m.type === 'call.voice', 600, 'voice after the hush');

    fresh();
    await a.send({ type: 'call.stop' });
    await pc.list('callers', (l) => l.length === 0, 'the call off the badge');
    pc.send({ c: 'voice', seconds: 0.5, rate: 24000 });
    await a.none((m) => m.type === 'call.voice', 800, 'voice after the call ended');

    await a.send({ type: 'call.start' });
    await a.wait((m) => m.type === 'call.state' && m.on, 4000, 'the call again');
    fresh();
    pc.send({ c: 'hang-up', device: a.device });
    assert.deepEqual(plain(await a.wait((m) => m.type === 'call.state' && !m.on, 4000, 'the PC hanging up')), { type: 'call.state', on: false, reason: 'ended' });
    await pc.list('callers', (l) => l.length === 0, 'the hang-up off the badge');
  });

  await step('call errors: audio with no call, parts out of order or at the wrong rate, a cancelled line, a line over 30 s', async () => {
    const error = async (re) => plain(await a.wait((m) => m.type === 'error', 5000, `an error for ${re}`));
    await a.send(L.audioParts('e1', L.tone(16000, 0.1, 300))[0]);
    assert.deepEqual(await error('e1'), { type: 'error', code: 'not-allowed', re: 'e1' });
    await a.send({ type: 'call.start' });
    await a.wait((m) => m.type === 'call.state' && m.on, 4000, 'the call');
    const line = L.audioParts('e2', L.tone(16000, 1, 300));
    await a.send(line[1]);
    assert.deepEqual(await error('e2'), { type: 'error', code: 'bad-message', re: 'e2' });
    await a.send({ ...line[0], id: 'e3', rate: 8000 });
    assert.deepEqual(await error('e3'), { type: 'error', code: 'bad-message', re: 'e3' });
    await a.send({ ...line[0], id: 'e4', pcm: 'AA' });
    assert.deepEqual(await error('e4'), { type: 'error', code: 'bad-message', re: 'e4' });
    const cancelled = L.audioParts('e5', L.tone(16000, 1, 300));
    await a.sendAll(cancelled.slice(0, 2));
    await a.send({ type: 'call.cancel', id: 'e5' });
    await a.send(cancelled[2]);
    assert.deepEqual(await error('e5'), { type: 'error', code: 'bad-message', re: 'e5' });
    await pc.none((e) => e.e === 'ears', 300, 'a cancelled line to reach her ears');
    const long = L.audioParts('e6', new Int16Array(16000 * 31).fill(1000), 1250);
    assert.equal(long.length, 25);
    await a.sendAll(long, 100);
    assert.deepEqual(await error('e6'), { type: 'error', code: 'too-long', re: 'e6' });
    await pc.none((e) => e.e === 'ears', 500, 'a too-long line to reach her ears');
    await a.send({ type: 'call.stop' });
  });

  await step('a new session ends the watch and the call; the phone asks again in the new one', async () => {
    await a.send({ type: 'watch.start', monitor: 'auto', quality: 'saver', width: 640, height: 360 });
    await a.send({ type: 'call.start' });
    await pc.list('watchers', (l) => l.length === 1, 'the watch');
    await pc.list('callers', (l) => l.length === 1, 'the call');
    fresh();
    await a.session(pc);
    await pc.list('watchers', (l) => l.length === 0, 'the watch to end');
    await pc.list('callers', (l) => l.length === 0, 'the call to end');
    const since = Date.now();
    await sleep(1200);
    assert.equal(a.framesFrom(since).length, 0, 'frames into the new session unasked');
  });

  await step('quit: the PC stops cleanly while phones watch and call', async () => {
    const since = Date.now();
    await a.send({ type: 'watch.start', monitor: 'auto', quality: 'sharp', ...VIEW });
    await a.send({ type: 'call.start' });
    await a.frameAfter(since);
    pc.send({ c: 'voice', seconds: 0.3, rate: 24000 });
    await pc.quit();
  });

  say('');
  say('What the relay carries to the phone, per second (sealed messages; drawn 1920x1080 desktop, view 1280x720):');
  for (const r of rates)
    say(`RATE ${r.quality.padEnd(8)} ${r.scene.padEnd(6)} ${r.fps.toFixed(1)} frames/s, ${(r.frame / 1024).toFixed(1)} KB a frame in ${r.parts} part(s), ${(r.bytes / 1024).toFixed(1)} KB/s`);
  say(`RATE call     talking: ${(talk / 1024).toFixed(1)} KB/s phone to PC (16 kHz); her voice: ${(voice24 / 1024).toFixed(1)} KB/s PC to phone (24 kHz)`);
  say(`\nPASS ${passed} steps`);
} catch (error) {
  fail(error);
} finally {
  await cleanup();
}

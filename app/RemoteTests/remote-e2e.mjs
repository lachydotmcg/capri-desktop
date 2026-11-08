// Capri Remote, the PC side end to end: RemoteTests.exe runs the real RemoteLink (app/RemoteLink.cs) against the real
// relay, and the reference fake phone from remote/test/lib talks to it.
//
//   node app/RemoteTests/remote-e2e.mjs <path to RemoteTests.exe>                       every step, on wrangler dev --local
//   node app/RemoteTests/remote-e2e.mjs <path to RemoteTests.exe> --relay wss://<host>  the live steps, on a running relay
//
// Live mode starts no relay and runs only what is safe on a shared production relay: pair, session, chat, a repeated
// id, a tampered and a replayed frame, deny, revoke, a phone that asks to be forgotten, and the wire audit. It adds one
// PC that reaches the relay itself with no tap in between, which is the only place its own TLS gets used. Every run
// makes fresh rooms, revokes its test phones at the end, and prints no keys, tokens, ids or codes. --relay takes
// wss://host, or ws:// on this machine (a `wrangler dev` URL, to try live mode out locally).
//
// The PC reaches the relay through a small TCP tap that reads what the PC sends: its upgrade request (it must carry no
// Origin header), every heartbeat (it must be exactly {"t":"ping"}), every control, and the header of every envelope
// (sequence numbers must start at 0 with each session and go up by one). Against wss:// the tap speaks TLS to the relay
// and passes on the relay's host name. Nothing here deploys or signs in anywhere.

import assert from 'node:assert/strict';
import { spawn, spawnSync } from 'node:child_process';
import { copyFileSync, mkdtempSync, rmSync } from 'node:fs';
import { createServer, connect, isIP } from 'node:net';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { connect as tlsConnect } from 'node:tls';
import { FakePhone } from '../../remote/test/lib/fakes.mjs';
import * as R from '../../remote/test/lib/capri-remote.mjs';
import { startRelay } from '../../remote/test/lib/relay-dev.mjs';

const USAGE = 'usage: node remote-e2e.mjs <RemoteTests.exe> [--relay wss://<host>]';
const argv = process.argv.slice(2);
const flag = argv.indexOf('--relay');
const live = flag < 0 ? null : liveRelay(argv.splice(flag, 2)[1]);
const exe = argv[0];
if (!exe || argv.length !== 1) throw new Error(USAGE);
// The PC's heartbeat, sped up so a short run still sees some (the relay answers them itself), and the fake phone's.
const BEAT = live ? 5000 : 1000;
const PHONE_BEAT = live ? 10000 : 500;
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

// A running relay as scheme://host[:port]: wss, or ws on this machine only, as the PC itself allows.
function liveRelay(text) {
  if (!text) throw new Error(USAGE);
  const url = new URL(text);
  const loopback = /^(127\.0\.0\.1|localhost|\[::1\])$/.test(url.hostname);
  if (!(url.protocol === 'wss:' || (url.protocol === 'ws:' && loopback))) throw new Error('--relay takes wss://<host>, or ws:// on this machine');
  if (url.pathname !== '/' || url.search || url.hash || url.username || url.password) throw new Error('--relay takes a scheme, a host and a port, nothing more');
  return `${url.protocol}//${url.host}`;
}

// What a live run prints: ids, tokens, keys and one-time codes (base64url) and confirmation codes are hidden.
const hide = (text) => String(text).replace(/[A-Za-z0-9_-]{20,}/g, '[hidden]').replace(/\b\d{3} \d{3}\b/g, '[code]');
const say = (text) => console.log(live ? hide(text) : String(text));

// ---------- The tap ----------
// relayUrl is the relay's ws:// or wss:// base. The PC's upgrade request goes on with the relay's own host name in it,
// as a relay behind Cloudflare needs, and the tap reads the original.
function startTap(relayUrl) {
  const target = new URL(relayUrl);
  const secure = target.protocol === 'wss:';
  const host = target.hostname.replace(/^\[|\]$/g, '');
  const port = Number(target.port) || (secure ? 443 : 80);
  const tap = { upgrades: [], texts: [], binaries: [], problems: [], errors: [], sockets: new Set() };
  const server = createServer((client) => {
    const upstream = secure ? tlsConnect({ host, port, servername: isIP(host) ? undefined : host }) : connect(port, host);
    tap.sockets.add(client);
    tap.sockets.add(upstream);
    upstream.pipe(client);
    const end = () => {
      client.destroy();
      upstream.destroy();
    };
    client.on('error', end);
    // Kept for a failure report: a relay that can't be reached, or a TLS problem on the way to it, shows here.
    upstream.on('error', (error) => {
      tap.errors.push(error.message);
      end();
    });
    client.on('close', end);
    upstream.on('close', end);
    let held = Buffer.alloc(0);
    client.on('data', (chunk) => {
      if (!held) upstream.write(chunk);
      else {
        held = Buffer.concat([held, chunk]);
        const at = held.indexOf('\r\n\r\n');
        if (at >= 0) {
          upstream.write(Buffer.from(held.subarray(0, at).toString('latin1').replace(/^host:[^\r\n]*$/im, `Host: ${target.host}`), 'latin1'));
          upstream.write(held.subarray(at));
          held = null;
        }
      }
      read(chunk);
    });
    let buffer = Buffer.alloc(0);
    let head = true;
    const read = (chunk) => {
      buffer = Buffer.concat([buffer, chunk]);
      for (;;) {
        if (head) {
          const at = buffer.indexOf('\r\n\r\n');
          if (at < 0) return;
          tap.upgrades.push(buffer.subarray(0, at).toString('latin1'));
          buffer = buffer.subarray(at + 4);
          head = false;
        }
        if (buffer.length < 2) return;
        const opcode = buffer[0] & 0x0f;
        const fin = (buffer[0] & 0x80) !== 0;
        const masked = (buffer[1] & 0x80) !== 0;
        let length = buffer[1] & 0x7f;
        let at = 2;
        if (length === 126) {
          if (buffer.length < 4) return;
          length = buffer.readUInt16BE(2);
          at = 4;
        } else if (length === 127) {
          if (buffer.length < 10) return;
          length = Number(buffer.readBigUInt64BE(2));
          at = 10;
        }
        if (buffer.length < at + (masked ? 4 : 0) + length) return;
        const mask = masked ? buffer.subarray(at, at + 4) : null;
        if (masked) at += 4;
        const payload = Buffer.from(buffer.subarray(at, at + length));
        if (mask) for (let i = 0; i < payload.length; i++) payload[i] ^= mask[i & 3];
        buffer = buffer.subarray(at + length);
        if (!masked) tap.problems.push('a client frame went unmasked');
        if (!fin) tap.problems.push('the PC split a message into fragments');
        if (opcode === 1) tap.texts.push(payload.toString('utf8'));
        else if (opcode === 2) tap.binaries.push(new Uint8Array(payload));
      }
    };
  });
  return new Promise((resolve) => {
    server.listen(0, '127.0.0.1', () => {
      tap.port = server.address().port;
      tap.close = () => {
        for (const socket of tap.sockets) socket.destroy();
        server.close();
      };
      resolve(tap);
    });
  });
}

// ---------- The PC: RemoteTests.exe pc, one JSON event per line ----------
class Pc {
  // relayUrl: the tap's ws://127.0.0.1 address, or in live mode's direct step the relay itself.
  static async start(statePath, relayUrl) {
    const child = spawn(exe, ['pc', relayUrl, statePath, String(BEAT)], { stdio: ['pipe', 'pipe', 'pipe'] });
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
        if (line) this.add(JSON.parse(line));
      }
    });
    child.stderr.setEncoding('utf8');
    child.stderr.on('data', (chunk) => (this.err += chunk));
    this.exited = new Promise((resolve) => child.on('exit', resolve));
  }

  add(event) {
    this.events.push(event);
    for (const waiter of [...this.waiters]) waiter();
  }

  // The first event not yet taken that matches. Events can come in any order, so each wait searches all of them.
  wait(match, timeout = 8000, what = 'an event') {
    return new Promise((resolve, reject) => {
      const look = () => {
        const event = this.events.find((e) => !e.taken && match(e));
        if (!event) return false;
        event.taken = true;
        this.waiters.delete(look);
        clearTimeout(timer);
        resolve(event);
        return true;
      };
      const timer = setTimeout(() => {
        this.waiters.delete(look);
        reject(new Error(`timed out waiting for ${what}. Events: ${JSON.stringify(this.events.slice(-12))}\n${this.err}`));
      }, timeout);
      if (!look()) this.waiters.add(look);
    });
  }

  async none(match, ms, what) {
    await sleep(ms);
    const event = this.events.find((e) => !e.taken && match(e));
    assert.equal(event, undefined, `did not expect ${what}: ${JSON.stringify(event)}`);
  }

  send(command) {
    this.child.stdin.write(JSON.stringify(command) + '\n');
  }

  async state() {
    this.send({ c: 'state' });
    return this.wait((e) => e.e === 'state', 5000, 'the state');
  }

  async qr() {
    this.send({ c: 'code' });
    const code = await this.wait((e) => e.e === 'code', 5000, 'the code answer');
    assert.equal(code.ok, true, 'the PC would not open a code');
    return (await this.wait((e) => e.e === 'qr' && e.url, 5000, 'a QR')).url;
  }

  async quit() {
    this.send({ c: 'quit' });
    await this.wait((e) => e.e === 'bye', 10000, 'the PC to stop');
    await this.exited;
  }
}

// ---------- Steps ----------
let passed = 0;
// where: 'both', 'local' (needs wrangler dev: relay internals, fast timers or a second Capri on the room) or 'live'.
const step = async (name, run, where = 'both') => {
  if (where === (live ? 'local' : 'live')) return;
  const began = Date.now();
  await run();
  passed++;
  say(`PASS ${name} (${Date.now() - began} ms)`);
};
const localStep = (name, run) => step(name, run, 'local');
const phoneFor = (name) => new FakePhone(relay, { name, heartbeat: PHONE_BEAT });
const tapUrl = () => `ws://127.0.0.1:${tap.port}`;

// Pairs a new phone through the PC: QR, confirmation code, Allow, then a signed-in session.
async function pairPhone(pc, name) {
  const url = await pc.qr();
  const phone = phoneFor(name);
  await phone.scan(url);
  await phone.conn.json('waiting');
  const ask = await pc.wait((e) => e.e === 'ask', 8000, 'the Allow prompt');
  assert.equal(ask.code, phone.confirm, 'the PC and phone show different codes');
  pc.send({ c: 'allow' });
  assert.equal((await pc.wait((e) => e.e === 'allow')).ok, true);
  const { message, closed } = await phone.pairReply();
  assert.equal(message.type, 'pair-allow');
  assert.equal(closed.code, 4001);
  await pc.wait((e) => e.e === 'paired' && e.about === message.device, 8000, 'the admit ok');
  secrets.push(message.token, message.key, new URL(url).hash.split('k=')[1]);
  return { phone, device: message.device, ask, message, url };
}

async function session(pc, phone, device) {
  const ready = await phone.connect();
  assert.equal(ready.pc, true);
  await pc.wait((e) => e.e === 'joined' && e.about === device, 8000, 'joined');
  const ok = await phone.hello();
  await pc.wait((e) => e.e === 'session' && e.about === device, 8000, 'the session');
  return ok;
}

async function pingPong(phone, n) {
  await phone.send({ type: 'ping', n });
  assert.deepEqual(await phone.receive(), { type: 'pong', n });
}

// One whole reply, however its parts were batched.
async function readReply(phone) {
  let text = '';
  for (;;) {
    const part = await phone.receive();
    assert.equal(part.type, 'chat.reply');
    text += part.text;
    if (part.done) return { id: part.id, re: part.re, text };
  }
}

const secrets = [];
const work = mkdtempSync(join(tmpdir(), 'capri-remote-e2e-'));
const statePath = join(work, 'state.dpapi');
let relay, tap, pc, pc2, pcDirect;

// Whatever ends the run, the PCs stop, the tap closes and the local relay goes.
let cleaned;
const cleanup = () =>
  (cleaned ??= (async () => {
    for (const p of [pc, pc2, pcDirect]) if (p && p.child.exitCode === null) p.child.kill();
    if (tap) tap.close();
    if (relay) await relay.stop();
    rmSync(work, { recursive: true, force: true });
  })());
const fail = (error) => {
  say('FAIL ' + (error && error.stack ? error.stack : error));
  if (pc && pc.err) say('PC stderr:\n' + pc.err);
  if (pcDirect && pcDirect.err) say('Direct PC stderr:\n' + pcDirect.err);
  if (tap && tap.errors.length) say('Tap to relay:\n' + tap.errors.join('\n'));
  process.exitCode = 1;
};
for (const what of ['uncaughtException', 'unhandledRejection'])
  process.on(what, (error) => {
    fail(error);
    cleanup().finally(() => process.exit(1));
  });

try {
  relay = live ? { ws: live, stop: async () => {} } : await startRelay();
  tap = await startTap(relay.ws);
  pc = await Pc.start(statePath, tapUrl());

  let a;
  await step('pair: QR, matching confirmation code, cleaned phone name, Allow', async () => {
    a = await pairPhone(pc, 'Lachy\u202e  Phone\u0007\u200b');
    assert.match(a.url, /^https:\/\/capriai\.app\/remote\/#r=[\w-]{22}&c=[\w-]{22}&k=[\w-]{43}$/);
    assert.equal(a.ask.name, 'Lachy Phone');
    assert.equal(a.message.v, 1);
    assert.equal(a.message.name, 'Test PC');
    assert.equal(a.message.app, 'capri/test');
    assert.equal(R.fromB64u(a.message.device, 16).length, 16);
    assert.equal(R.fromB64u(a.message.token, 32).length, 32);
    assert.equal(R.fromB64u(a.message.key, 32).length, 32);
    assert.deepEqual((await pc.state()).devices, [a.device]);
  });

  await step('session: hello, hello-ok with echo and features, badge count', async () => {
    const ok = await session(pc, a.phone, a.device);
    assert.equal(ok.v, 1);
    assert.deepEqual(ok.features, ['chat']);
    assert.equal(ok.name, 'Test PC');
    const state = await pc.state();
    assert.equal(state.online, 1);
    assert.deepEqual(state.sessions, [a.device]);
  });

  await step('chat round trip: receipt, tool, streamed reply batched, done', async () => {
    await a.phone.send({ type: 'chat.send', id: 'm1', text: 'Hello Capri' });
    assert.deepEqual(await a.phone.receive(), { type: 'chat.typing', on: true, re: 'm1' });
    const said = await pc.wait((e) => e.e === 'said', 5000, 'chat.send at the PC');
    assert.deepEqual([said.device, said.id, said.text], [a.device, 'm1', 'Hello Capri']);
    pc.send({ c: 'tool', device: a.device, re: 'm1', id: 't1', name: 'web_search', summary: 'Searching the web', state: 'running' });
    assert.deepEqual(await a.phone.receive(), { type: 'chat.tool', id: 't1', name: 'web_search', summary: 'Searching the web', state: 'running', re: 'm1' });
    const words = 'Hi Lachy! It is sunny in Sydney today, about 24 degrees, with a light sea breeze after lunch.'.split(/(?<= )/);
    pc.send({ c: 'reply', device: a.device, re: 'm1', id: 'r1', parts: words, gap: 15 });
    let text = '';
    let frames = 0;
    for (;;) {
      const part = await a.phone.receive();
      frames++;
      assert.equal(part.type, 'chat.reply');
      assert.equal(part.id, 'r1');
      assert.equal(part.re, 'm1');
      text += part.text;
      if (part.done) break;
    }
    assert.equal(text, words.join(''));
    assert.ok(frames < words.length / 2, `${words.length} parts came as ${frames} frames; they should be batched`);
    pc.send({ c: 'tool', device: a.device, re: 'm1', id: 't1', name: 'web_search', summary: 'Searched the web', state: 'done' });
    assert.equal((await a.phone.receive()).state, 'done');
    say(`     ${words.length} streamed parts arrived as ${frames} chat.reply frames`);
  });

  await step('a chat.send id seen before is ignored; ping gets its pong', async () => {
    await a.phone.send({ type: 'chat.send', id: 'm1', text: 'Hello Capri' });
    await pingPong(a.phone, 42);
    await pc.none((e) => e.e === 'said', 300, 'a second said for m1');
  });

  await localStep('a bad chat.send gets error bad-message with its re', async () => {
    await a.phone.send({ type: 'chat.send', id: 'm2', text: '' });
    assert.deepEqual(await a.phone.receive(), { type: 'error', code: 'bad-message', re: 'm2' });
    await a.phone.send({ type: 'settings.set', key: 'x' });
    await a.phone.send({ type: 'something-new' });
    await pingPong(a.phone, 43);
  });

  await step('a tampered frame gets the phone kicked (4000), and it comes back with a fresh session', async () => {
    const envelope = await a.phone.session.seal({ type: 'chat.send', id: 'm3', text: 'tampered' });
    envelope[40] ^= 0x01;
    a.phone.conn.sendBytes(envelope);
    const closed = await a.phone.conn.closedWithin();
    assert.deepEqual([closed.code, closed.reason], [4000, 'kicked']);
    await pc.wait((e) => e.e === 'kicked' && e.about === a.device, 5000, 'kicked');
    await pc.none((e) => e.e === 'said', 200, 'the tampered message');
    await pc.wait((e) => e.e === 'left' && e.about === a.device, 5000, 'left');
    await session(pc, a.phone, a.device);
    await pingPong(a.phone, 44);
  });

  await step('a replayed frame gets the phone kicked', async () => {
    const envelope = await a.phone.session.seal({ type: 'ping', n: 45 });
    a.phone.conn.sendBytes(envelope);
    assert.deepEqual(await a.phone.receive(), { type: 'pong', n: 45 });
    a.phone.conn.sendBytes(envelope);
    const closed = await a.phone.conn.closedWithin();
    assert.equal(closed.code, 4000);
    await pc.wait((e) => e.e === 'kicked' && e.about === a.device, 5000, 'kicked');
    await pc.wait((e) => e.e === 'left' && e.about === a.device, 5000, 'left');
    await session(pc, a.phone, a.device);
  });

  await localStep('a reply made while the phone is away arrives after its next hello', async () => {
    await a.phone.send({ type: 'chat.send', id: 'm4', text: 'Back in a sec' });
    assert.equal((await a.phone.receive()).type, 'chat.typing');
    await pc.wait((e) => e.e === 'said' && e.id === 'm4');
    a.phone.close();
    await pc.wait((e) => e.e === 'left' && e.about === a.device, 5000, 'left');
    pc.send({ c: 'reply', device: a.device, re: 'm4', id: 'r4', parts: ['Welcome ', 'back.'], gap: 0 });
    await sleep(300);
    await session(pc, a.phone, a.device);
    assert.deepEqual(await a.phone.receive(), { type: 'chat.reply', id: 'r4', re: 'm4', text: 'Welcome back.', done: true });
    await pingPong(a.phone, 46);
  });

  await localStep(`heartbeat: the exact {"t":"ping"} every ${BEAT} ms keeps one connection up`, async () => {
    const texts = tap.texts.length;
    const upgrades = tap.upgrades.length;
    await sleep(BEAT * 3.5);
    const beats = tap.texts.slice(texts).filter((text) => JSON.parse(text).t === 'ping');
    assert.ok(beats.length >= 3, `${beats.length} heartbeats in ${BEAT * 3.5} ms`);
    for (const text of beats) assert.equal(text, '{"t":"ping"}');
    assert.equal(tap.upgrades.length, upgrades, 'the PC reconnected while idle');
    assert.equal((await pc.state()).status, 'online');
    await pingPong(a.phone, 49);
  });

  await localStep('restart: the PC signs back in to the same room from its DPAPI state; the phone says hello again', async () => {
    await pc.quit();
    assert.deepEqual(await a.phone.conn.json('presence'), { t: 'presence', pc: false });
    pc = await Pc.start(statePath, tapUrl());
    assert.deepEqual(await a.phone.conn.json('presence'), { t: 'presence', pc: true });
    await a.phone.hello();
    await pc.wait((e) => e.e === 'session' && e.about === a.device, 8000, 'the session');
    await pingPong(a.phone, 47);
    assert.deepEqual((await pc.state()).devices, [a.device]);
  });

  await step('deny: the phone gets pair-deny and 4003', async () => {
    const url = await pc.qr();
    const phone = phoneFor('Stranger');
    await phone.scan(url);
    await phone.conn.json('waiting');
    await pc.wait((e) => e.e === 'ask' && e.name === 'Stranger');
    pc.send({ c: 'deny' });
    assert.equal((await pc.wait((e) => e.e === 'deny')).ok, true);
    const { message, closed } = await phone.pairReply();
    assert.deepEqual(message, { type: 'pair-deny', v: 1, reason: 'denied' });
    assert.equal(closed.code, 4003);
    assert.deepEqual((await pc.state()).devices, [a.device]);
  });

  let b;
  await localStep('a pair-request that does not open: a plain no, and a new QR goes up that works', async () => {
    const url = await pc.qr();
    const wrongKey = url.replace(/k=[\w-]+$/, 'k=' + R.b64u(R.randomBytes(32)));
    const phone = phoneFor('Wrong key');
    await phone.scan(wrongKey);
    await phone.conn.json('waiting');
    const closed = await phone.conn.closedWithin();
    assert.equal(closed.code, 4003);
    assert.equal(phone.conn.inbox.length, 0, 'a phone whose request did not open got a pair-reply');
    const fresh = await pc.wait((e) => e.e === 'qr' && e.url && e.url !== url, 5000, 'a new QR');
    const next = phoneFor('Second phone');
    await next.scan(fresh.url);
    await next.conn.json('waiting');
    const ask = await pc.wait((e) => e.e === 'ask' && e.name === 'Second phone');
    assert.equal(ask.code, next.confirm);
    pc.send({ c: 'allow' });
    await pc.wait((e) => e.e === 'allow');
    const reply = await next.pairReply();
    assert.equal(reply.message.type, 'pair-allow');
    await pc.wait((e) => e.e === 'paired' && e.about === reply.message.device);
    secrets.push(reply.message.token, reply.message.key);
    b = { phone: next, device: reply.message.device };
    await session(pc, b.phone, b.device);
    await pingPong(b.phone, 1);
  });

  await localStep('closing the pairing dialog cancels the code on the relay (4410 code-unknown)', async () => {
    const url = await pc.qr();
    pc.send({ c: 'close-code' });
    await sleep(300);
    const phone = phoneFor('Late');
    await phone.scan(url);
    await phone.conn.refused('code-unknown', 4410);
  });

  await step('revoke from the PC: the phone gets a sealed revoke, then 4403, and its token stops working', async () => {
    if (!b) {
      const paired = await pairPhone(pc, 'Second phone');
      await session(pc, paired.phone, paired.device);
      b = { phone: paired.phone, device: paired.device };
    }
    pc.send({ c: 'revoke', device: b.device });
    assert.deepEqual(await b.phone.receive(), { type: 'revoke' });
    await b.phone.conn.refused('revoked', 4403);
    await pc.wait((e) => e.e === 'removed' && e.about === b.device);
    await b.phone.open();
    await b.phone.conn.refused('unknown-device', 4403);
    assert.deepEqual((await pc.state()).devices, [a.device]);
  });

  await step('a phone that asks to be forgotten is removed here and on the relay', async () => {
    const c = await pairPhone(pc, 'Forget me');
    await session(pc, c.phone, c.device);
    await c.phone.send({ type: 'revoke' });
    await pc.wait((e) => e.e === 'removed' && e.about === c.device, 5000, 'removed');
    await c.phone.conn.refused('revoked', 4403);
    assert.deepEqual((await pc.state()).devices, [a.device]);
  });

  await localStep('sign-in sync: a phone the PC no longer knows is revoked when the PC signs in', async () => {
    const d = await pairPhone(pc, 'Orphan');
    await session(pc, d.phone, d.device);
    await pc.quit();
    assert.deepEqual(await d.phone.conn.json('presence'), { t: 'presence', pc: false });
    assert.deepEqual(await a.phone.conn.json('presence'), { t: 'presence', pc: false });
    const forgot = spawnSync(exe, ['forget', statePath, d.device], { encoding: 'utf8' });
    assert.equal(forgot.status, 0, forgot.stderr);
    pc = await Pc.start(statePath, tapUrl());
    assert.deepEqual(await d.phone.conn.json('presence'), { t: 'presence', pc: true });
    await d.phone.conn.refused('revoked', 4403);
    assert.deepEqual(await a.phone.conn.json('presence'), { t: 'presence', pc: true });
    await a.phone.hello();
    await pc.wait((e) => e.e === 'session' && e.about === a.device);
    assert.deepEqual((await pc.state()).devices, [a.device]);
  });

  await localStep('another copy of Capri on the same room takes over (4409); the first one stops', async () => {
    const copy = join(work, 'copy.dpapi');
    copyFileSync(statePath, copy);
    pc2 = await Pc.start(copy, tapUrl());
    await pc.wait((e) => e.e === 'status' && e.status === 'replaced', 8000, 'replaced');
    assert.equal((await pc.state()).status, 'replaced');
    assert.deepEqual(await a.phone.conn.json('presence'), { t: 'presence', pc: true });
    await a.phone.hello();
    await pc2.wait((e) => e.e === 'session' && e.about === a.device);
    await pingPong(a.phone, 48);
    await pc2.quit();
    await pc.quit();
  });

  // Every other PC here goes through the tap, which speaks plain ws to it, so this one is the PC's own TLS to the relay.
  await step('direct: a second PC on its own connection to the relay, with no tap, pairs a phone and chats', async () => {
    pcDirect = await Pc.start(join(work, 'direct.dpapi'), live);
    const d = await pairPhone(pcDirect, 'Direct phone');
    await session(pcDirect, d.phone, d.device);
    await d.phone.send({ type: 'chat.send', id: 'd1', text: 'Straight through?' });
    assert.deepEqual(await d.phone.receive(), { type: 'chat.typing', on: true, re: 'd1' });
    const said = await pcDirect.wait((e) => e.e === 'said', 5000, 'chat.send at the direct PC');
    assert.deepEqual([said.device, said.id, said.text], [d.device, 'd1', 'Straight through?']);
    pcDirect.send({ c: 'reply', device: d.device, re: 'd1', id: 'rd1', parts: ['Straight ', 'through.'], gap: 0 });
    assert.deepEqual(await readReply(d.phone), { id: 'rd1', re: 'd1', text: 'Straight through.' });
    pcDirect.send({ c: 'revoke', device: d.device });
    assert.deepEqual(await d.phone.receive(), { type: 'revoke' });
    await d.phone.conn.refused('revoked', 4403);
    await pcDirect.wait((e) => e.e === 'removed' && e.about === d.device, 5000, 'removed');
    await pcDirect.quit();
  }, 'live');

  if (live) {
    // A shared relay keeps rooms for 30 days, so the first test phone goes too and the room ends with none. The PC
    // then stops, so its last frames are in the wire audit.
    pc.send({ c: 'revoke', device: a.device });
    await pc.wait((e) => e.e === 'removed' && e.about === a.device, 8000, 'the last test phone to go');
    assert.deepEqual((await pc.state()).devices, []);
    // At least one heartbeat for the audit to check: they come every BEAT.
    for (const until = Date.now() + BEAT * 2; !tap.texts.includes('{"t":"ping"}') && Date.now() < until; ) await sleep(100);
    await pc.quit();
  }

  await step('the wire: no Origin header, exact heartbeats, known controls, secrets stay sealed, sequence numbers in order', async () => {
    assert.deepEqual(tap.problems, []);
    assert.equal(tap.upgrades.length, live ? 1 : 4, `${tap.upgrades.length} PC connections seen`);
    for (const upgrade of tap.upgrades) {
      assert.match(upgrade, /^GET \/v1\/rooms\/[\w-]{22}\/pc HTTP\/1\.1\r\n/);
      assert.doesNotMatch(upgrade, /^origin:/im, 'the PC sent an Origin header');
    }
    const controls = new Set(['auth', 'code', 'code-cancel', 'admit', 'deny', 'revoke', 'kick', 'ping']);
    let pings = 0;
    for (const text of tap.texts) {
      const frame = JSON.parse(text);
      assert.ok(controls.has(frame.t), `unexpected control ${text}`);
      if (frame.t === 'ping') {
        assert.equal(text, '{"t":"ping"}');
        pings++;
      }
    }
    assert.ok(pings >= (live ? 1 : 3), `only ${pings} heartbeats`);
    const everything = [...tap.texts, ...tap.binaries.map((bytes) => Buffer.from(bytes).toString('latin1'))].join('\n');
    for (const secret of secrets) {
      assert.ok(!everything.includes(secret), 'a device token, device key or pairing key crossed the wire in the clear');
      assert.ok(!everything.includes(Buffer.from(R.fromB64u(secret)).toString('latin1')), 'raw key bytes crossed the wire');
    }
    const next = new Map();
    let sessions = 0;
    for (const frame of tap.binaries) {
      assert.ok(frame.length >= 16 + 72, 'a short frame');
      const device = R.b64u(frame.subarray(0, 16));
      const [version, kind, dir, reserved] = frame.subarray(16, 20);
      const seq = new DataView(frame.buffer, frame.byteOffset + 20, 4).getUint32(0, false);
      assert.deepEqual([version, dir, reserved], [1, 2, 0]);
      if (kind === 2) {
        assert.equal(seq, 0, 'hello-ok is always seq 0');
        next.set(device, 0);
        sessions++;
      } else {
        assert.equal(kind, 3);
        assert.equal(seq, next.get(device), `seq ${seq} for ${device}, expected ${next.get(device)}`);
        next.set(device, seq + 1);
      }
    }
    say(`     ${tap.upgrades.length} PC connections, ${pings} heartbeats, ${tap.texts.length - pings} controls, ${tap.binaries.length} envelopes in ${sessions} sessions`);
  });

  say(live ? `PASS live e2e: ${passed} steps against ${live}` : `PASS e2e: ${passed} steps against the relay under wrangler dev`);
} catch (error) {
  fail(error);
} finally {
  await cleanup();
}

// A fake PC and a fake phone that speak Capri Remote protocol 1 through a real relay.
// They use Node's built-in WebSocket client and the reference crypto in capri-remote.mjs.

import assert from 'node:assert/strict';
import * as R from './capri-remote.mjs';

export const PHONE_ORIGIN = 'https://capriai.app';
export const PING = '{"t":"ping"}';

// One WebSocket with an inbox. Pongs are counted and dropped; everything else is read in order.
export class Conn {
  static open(url, { origin, heartbeat = 0 } = {}) {
    return new Promise((resolve, reject) => {
      const ws = new WebSocket(url, origin ? { headers: { Origin: origin } } : undefined);
      ws.binaryType = 'arraybuffer';
      const opened = () => {
        ws.removeEventListener('error', failed);
        resolve(new Conn(ws, heartbeat));
      };
      const failed = () => {
        ws.removeEventListener('open', opened);
        reject(new Error('could not connect to ' + url));
      };
      ws.addEventListener('open', opened, { once: true });
      ws.addEventListener('error', failed, { once: true });
    });
  }

  constructor(ws, heartbeat) {
    this.ws = ws;
    this.inbox = [];
    this.waiters = [];
    this.pongs = 0;
    this.closeInfo = null;
    this.closed = new Promise((resolve) => {
      ws.addEventListener('close', (event) => {
        this.closeInfo = { code: event.code, reason: event.reason };
        clearInterval(this.timer);
        resolve(this.closeInfo);
        this.flush();
      });
    });
    ws.addEventListener('message', (event) => {
      if (typeof event.data === 'string') {
        const data = JSON.parse(event.data);
        if (data.t === 'pong') return void this.pongs++;
        this.inbox.push({ text: true, data });
      } else {
        this.inbox.push({ text: false, data: new Uint8Array(event.data) });
      }
      this.flush();
    });
    if (heartbeat) this.timer = setInterval(() => this.ping(), heartbeat);
  }

  flush() {
    while (this.waiters.length && this.inbox.length) this.waiters.shift().resolve(this.inbox.shift());
    if (this.closeInfo) {
      for (const waiter of this.waiters.splice(0)) waiter.reject(new Error(`socket closed (${this.closeInfo.code} ${this.closeInfo.reason}) while waiting`));
    }
  }

  next(timeout = 5000) {
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.waiters = this.waiters.filter((waiter) => waiter.resolve !== done);
        reject(new Error('timed out waiting for a frame'));
      }, timeout);
      const done = (item) => {
        clearTimeout(timer);
        resolve(item);
      };
      this.waiters.push({ resolve: done, reject: (error) => (clearTimeout(timer), reject(error)) });
      this.flush();
    });
  }

  async json(t, timeout) {
    const item = await this.next(timeout);
    assert.ok(item.text, 'expected a text frame, got binary');
    if (t) assert.equal(item.data.t, t, `expected "${t}", got ${JSON.stringify(item.data)}`);
    return item.data;
  }

  async bytes(timeout) {
    const item = await this.next(timeout);
    assert.ok(!item.text, `expected a binary frame, got ${JSON.stringify(item.data)}`);
    return item.data;
  }

  // Waits for a fatal {"t":"error"} (one without "op"). Like a real client it then closes its own side at
  // once (PROTOCOL.md 4.5), and checks that the relay's close code and reason still come through.
  async refused(code, closeCode, timeout = 5000) {
    const error = await this.json('error', timeout);
    assert.equal(error.code, code, `expected error ${code}, got ${JSON.stringify(error)}`);
    assert.equal(error.op, undefined, 'a fatal error has no op');
    this.close();
    const closed = await this.closedWithin(timeout);
    assert.equal(closed.code, closeCode);
    assert.equal(closed.reason, code);
    return error;
  }

  closedWithin(timeout = 5000) {
    return Promise.race([
      this.closed,
      new Promise((_, reject) => setTimeout(() => reject(new Error('socket did not close in time')), timeout)),
    ]);
  }

  ping() {
    if (this.ws.readyState === WebSocket.OPEN) this.ws.send(PING);
  }

  // Sends one heartbeat and waits for its pong.
  async heartbeat(timeout = 3000) {
    const before = this.pongs;
    this.ping();
    const deadline = Date.now() + timeout;
    while (this.pongs === before) {
      if (Date.now() > deadline) throw new Error('no pong');
      await new Promise((resolve) => setTimeout(resolve, 20));
    }
  }

  send(message) {
    this.ws.send(JSON.stringify(message));
  }

  sendText(text) {
    this.ws.send(text);
  }

  sendBytes(bytes) {
    this.ws.send(bytes);
  }

  close(code = 1000) {
    clearInterval(this.timer);
    if (this.ws.readyState <= WebSocket.OPEN) this.ws.close(code);
  }
}

export const roomUrl = (relay, room, role) => `${relay.ws}/v1/rooms/${room}/${role}`;

export class FakePc {
  constructor(relay, { room = R.b64u(R.randomBytes(16)), token = R.b64u(R.randomBytes(32)), heartbeat = 500 } = {}) {
    this.relay = relay;
    this.room = room;
    this.token = token;
    this.heartbeat = heartbeat;
    this.devices = new Map();
    this.pairing = null;
  }

  async open() {
    this.conn = await Conn.open(roomUrl(this.relay, this.room, 'pc'), { heartbeat: this.heartbeat });
    this.conn.send({ t: 'auth', token: this.token });
    return this.conn;
  }

  async connect() {
    await this.open();
    this.ready = await this.conn.json('ready');
    return this.ready;
  }

  // Opens a one-time code on the relay and returns the QR URL the Mainframe would show.
  async showCode(ttl) {
    const code = R.randomBytes(16);
    const key = R.randomBytes(32);
    this.conn.send({ t: 'code', hash: await R.hashB64u(code), ...(ttl ? { ttl } : {}) });
    const ok = await this.conn.json('ok');
    assert.equal(ok.op, 'code');
    this.pairing = { code: R.b64u(code), key };
    return R.qrUrl(this.room, R.b64u(code), R.b64u(key));
  }

  async pairRequest() {
    const pending = await this.conn.json('pending');
    const nonce = R.fromB64u(pending.nonce, 16);
    const channel = new R.Channel(await R.pairKeys(this.pairing.key, nonce), R.KIND.pair, 'pc');
    const request = await channel.open(R.fromB64u(pending.env));
    const confirm = await R.confirmCode(this.pairing.key, nonce);
    return { pending, request, confirm, channel };
  }

  async allow(ask) {
    const id = R.randomBytes(16);
    const token = R.randomBytes(32);
    const key = R.randomBytes(32);
    const env = await ask.channel.seal({ type: 'pair-allow', v: 1, device: R.b64u(id), token: R.b64u(token), key: R.b64u(key), name: 'Test PC', app: 'capri/test' });
    this.conn.send({ t: 'admit', pending: ask.pending.id, device: R.b64u(id), tokenHash: await R.hashB64u(token), env: R.b64u(env) });
    const ok = await this.conn.json('ok');
    assert.equal(ok.op, 'admit');
    const device = { id: R.b64u(id), idBytes: id, key, name: ask.request.name, session: null };
    this.devices.set(device.id, device);
    this.pairing = null;
    return device;
  }

  async deny(ask) {
    const env = await ask.channel.seal({ type: 'pair-deny', v: 1, reason: 'denied' });
    this.conn.send({ t: 'deny', pending: ask.pending.id, env: R.b64u(env) });
    const ok = await this.conn.json('ok');
    assert.equal(ok.op, 'deny');
    this.pairing = null;
    return ok;
  }

  // Answers the next hello with hello-ok, offering features, and starts that device's session.
  async acceptHello(features = ['chat']) {
    const { device: id, envelope } = R.splitPcFrame(await this.conn.bytes());
    const device = this.devices.get(id);
    assert.ok(device, 'hello from an unknown device');
    const channel = new R.Channel(await R.helloKeys(device.key, device.idBytes), R.KIND.hello, 'pc');
    const hello = await channel.open(envelope);
    assert.equal(hello.type, 'hello');
    assert.equal(hello.v, 1);
    const pcNonce = R.randomBytes(16);
    const reply = await channel.seal({ type: 'hello-ok', v: 1, nonce: R.b64u(pcNonce), echo: hello.nonce, name: 'Test PC', app: 'capri/test', features });
    this.conn.sendBytes(R.pcFrame(device.idBytes, reply));
    device.session = new R.Channel(await R.sessionKeys(device.key, R.fromB64u(hello.nonce, 16), pcNonce), R.KIND.session, 'pc');
    return { device, hello };
  }

  async send(deviceId, message) {
    const device = this.devices.get(deviceId);
    this.conn.sendBytes(R.pcFrame(device.idBytes, await device.session.seal(message)));
  }

  // Returns the next session message. Throws R.EnvelopeError when the frame does not open.
  async receive() {
    const { device: id, envelope } = R.splitPcFrame(await this.conn.bytes());
    const device = this.devices.get(id);
    return { device, message: await device.session.open(envelope) };
  }

  close() {
    this.conn?.close();
  }
}

export class FakePhone {
  constructor(relay, { origin = PHONE_ORIGIN, name = 'Test Phone', heartbeat = 500 } = {}) {
    this.relay = relay;
    this.origin = origin;
    this.name = name;
    this.heartbeat = heartbeat;
    this.creds = null;
  }

  // Scans the QR: derives the pairing keys and the confirmation code, then presents the one-time code.
  async scan(qr) {
    const { room, code, key } = R.parseQr(qr);
    const pairingKey = R.fromB64u(key, 32);
    const nonce = R.randomBytes(16);
    this.room = room;
    this.pairChannel = new R.Channel(await R.pairKeys(pairingKey, nonce), R.KIND.pair, 'phone');
    this.confirm = await R.confirmCode(pairingKey, nonce);
    const env = await this.pairChannel.seal({ type: 'pair-request', v: 1, name: this.name, app: 'remote-web/test' });
    this.conn = await Conn.open(roomUrl(this.relay, room, 'phone'), { origin: this.origin, heartbeat: this.heartbeat });
    this.conn.send({ t: 'pair', code, nonce: R.b64u(nonce), env: R.b64u(env) });
    return this.conn;
  }

  async pairReply() {
    const reply = await this.conn.json('pair-reply');
    const message = await this.pairChannel.open(R.fromB64u(reply.env));
    const closed = await this.conn.closedWithin();
    if (message.type === 'pair-allow') {
      this.creds = { room: this.room, device: message.device, token: message.token, key: R.fromB64u(message.key, 32) };
    }
    this.pairChannel = null;
    return { message, closed };
  }

  async open({ heartbeat = this.heartbeat, token = this.creds.token, device = this.creds.device } = {}) {
    this.conn = await Conn.open(roomUrl(this.relay, this.creds.room, 'phone'), { origin: this.origin, heartbeat });
    this.conn.send({ t: 'auth', device, token });
    return this.conn;
  }

  async connect(options) {
    await this.open(options);
    return this.conn.json('ready');
  }

  async hello() {
    const nonce = R.randomBytes(16);
    const channel = new R.Channel(await R.helloKeys(this.creds.key, R.fromB64u(this.creds.device, 16)), R.KIND.hello, 'phone');
    this.conn.sendBytes(await channel.seal({ type: 'hello', v: 1, nonce: R.b64u(nonce), name: this.name, app: 'remote-web/test' }));
    const ok = await channel.open(await this.conn.bytes());
    assert.equal(ok.type, 'hello-ok');
    assert.equal(ok.echo, R.b64u(nonce));
    this.session = new R.Channel(await R.sessionKeys(this.creds.key, nonce, R.fromB64u(ok.nonce, 16)), R.KIND.session, 'phone');
    return ok;
  }

  async send(message) {
    this.conn.sendBytes(await this.session.seal(message));
  }

  async receive() {
    return this.session.open(await this.conn.bytes());
  }

  close() {
    this.conn?.close();
  }
}

// Pairs a fresh phone with the PC and opens a session. Returns the phone and the PC's device record.
export async function pairAndConnect(relay, pc, options = {}) {
  const qr = await pc.showCode();
  const phone = new FakePhone(relay, options);
  await phone.scan(qr);
  await phone.conn.json('waiting');
  const ask = await pc.pairRequest();
  assert.equal(ask.confirm, phone.confirm);
  const device = await pc.allow(ask);
  const { message, closed } = await phone.pairReply();
  assert.equal(message.type, 'pair-allow');
  assert.equal(closed.code, 4001);
  const ready = await phone.connect();
  assert.equal(ready.pc, true);
  assert.deepEqual(await pc.conn.json('joined'), { t: 'joined', device: device.id });
  await Promise.all([phone.hello(), pc.acceptHello()]);
  return { phone, device };
}

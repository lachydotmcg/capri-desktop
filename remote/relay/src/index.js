// Capri Remote relay, protocol 1 (see remote/PROTOCOL.md).
//
// One Durable Object per room, where a room is one paired PC. The PC and its phones both connect out
// to it over WebSocket. It forwards opaque end-to-end encrypted frames between them and never holds
// keys or plaintext. It stores only SHA-256 hashes of the PC token, device tokens and the pairing
// code, writes storage only when that state changes (never per message), and never logs frames.
// It uses the WebSocket Hibernation API, so an idle room costs next to nothing.

import { DurableObject } from 'cloudflare:workers';

const PING = '{"t":"ping"}';
const PONG = '{"t":"pong"}';
const TEXT_MAX = 16384;
const MAX_SIGNING_IN = 8;
const MAX_PENDING = 2;
const ID_BYTES = 16;
const MIN_ENVELOPE = 72;
const DAY = 24 * 60 * 60 * 1000;
const LOCAL_ORIGIN = /^https?:\/\/(localhost|127\.0\.0\.1|\[::1\])(:\d{1,5})?$/;
const ROUTE = /^\/v1\/rooms\/([^/]+)\/(pc|phone)$/;

const b64u = (bytes) => {
  let binary = '';
  for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
};

// Strict base64url (no padding, canonical); returns null instead of throwing.
const fromB64u = (text, length) => {
  if (typeof text !== 'string' || text.length === 0 || text.length % 4 === 1 || !/^[A-Za-z0-9_-]+$/.test(text)) return null;
  let bytes;
  try {
    bytes = Uint8Array.from(atob(text.replace(/-/g, '+').replace(/_/g, '/') + '='.repeat((4 - (text.length % 4)) % 4)), (c) => c.charCodeAt(0));
  } catch {
    return null;
  }
  if (length !== undefined && bytes.length !== length) return null;
  return b64u(bytes) === text ? bytes : null;
};

const id16 = (value) => (fromB64u(value, ID_BYTES) ? value : null);
const hash32 = (value) => (fromB64u(value, 32) ? value : null);
const sha256 = async (bytes) => new Uint8Array(await crypto.subtle.digest('SHA-256', bytes));

const sameText = (a, b) => {
  if (typeof a !== 'string' || typeof b !== 'string' || a.length !== b.length) return false;
  let diff = 0;
  for (let i = 0; i < a.length; i++) diff |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return diff === 0;
};

const concat = (a, b) => {
  const out = new Uint8Array(a.length + b.length);
  out.set(a, 0);
  out.set(b, a.length);
  return out;
};

const send = (ws, message) => {
  try {
    ws.send(typeof message === 'string' || message instanceof Uint8Array ? message : JSON.stringify(message));
    return true;
  } catch {
    return false;
  }
};

const number = (value, fallback, min, max) => {
  const n = Number(value);
  return Number.isFinite(n) ? Math.min(max, Math.max(min, n)) : fallback;
};

const settings = (env) => ({
  maxFrame: number(env.MAX_FRAME_BYTES, 65536, 1024, 1048576),
  maxPcFrame: number(env.MAX_PC_FRAME_BYTES, 65536, 1024, 1048576),
  codeTtl: Math.round(number(env.CODE_TTL_SECONDS, 600, 1, 3600)),
  authTimeout: number(env.AUTH_TIMEOUT_MS, 10000, 200, 60000),
  pendingTimeout: number(env.PENDING_TIMEOUT_MS, 180000, 200, 600000),
  idleTimeout: number(env.IDLE_TIMEOUT_MS, 90000, 200, 3600000),
  roomTtl: number(env.ROOM_TTL_SECONDS, 2592000, 1, 31536000) * 1000,
  phoneRate: number(env.PHONE_RATE, 10, 0.1, 10000),
  phoneBurst: number(env.PHONE_BURST, 60, 1, 100000),
  pcRate: number(env.PC_RATE, 100, 0.1, 10000),
  pcBurst: number(env.PC_BURST, 300, 1, 100000),
  maxDevices: Math.round(number(env.MAX_DEVICES, 16, 1, 256)),
  maxPhones: Math.round(number(env.MAX_PHONES_ONLINE, 8, 1, 64)),
});

export const phoneOriginAllowed = (origin, env) => {
  if (!origin) return false;
  const listed = String(env.PHONE_ORIGINS || '').split(',').map((item) => item.trim()).filter(Boolean);
  if (listed.includes(origin)) return true;
  return String(env.ALLOW_LOCAL_ORIGINS).toLowerCase() === 'true' && LOCAL_ORIGIN.test(origin);
};

const problem = (status, error) => Response.json({ error }, { status, headers: { 'cache-control': 'no-store' } });

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (url.pathname === '/' && request.method === 'GET') {
      return new Response('Capri Remote relay, protocol 1\n', { headers: { 'content-type': 'text/plain; charset=utf-8', 'cache-control': 'no-store' } });
    }
    const route = ROUTE.exec(url.pathname);
    if (!route) return problem(404, 'not-found');
    const [, room, role] = route;
    if (!id16(room)) return problem(400, 'bad-room');
    if (request.method !== 'GET' || (request.headers.get('Upgrade') || '').toLowerCase() !== 'websocket') return problem(426, 'websocket-only');
    // Browsers always send Origin. The PC is not a browser, so a PC connection with an Origin is a web page trying to act as one.
    const origin = request.headers.get('Origin');
    if (role === 'pc' ? origin !== null : !phoneOriginAllowed(origin, env)) return problem(403, 'origin');
    return env.ROOMS.get(env.ROOMS.idFromName(room)).fetch(request);
  },
};

// Socket attachment: { id, role: 'pc'|'phone', state: 'auth'|'pc'|'phone'|'pending'|'closed', since, last, device?, pending? }
// Stored room record: { v: 1, pc: hash, created, pcSeen, code: { hash, exp, used } | null, devices: { [id]: { hash, added } } }
export class Room extends DurableObject {
  constructor(ctx, env) {
    super(ctx, env);
    this.cfg = settings(env);
    this.room = undefined; // undefined until loaded, then null (unclaimed) or the record
    this.alarmAt = undefined;
    this.pcGoneAt = 0;
    this.buckets = new Map(); // rate limits, memory only: a wake after hibernation starts them full
    this.queue = Promise.resolve();
    // Heartbeats are answered by the runtime without waking the object.
    ctx.setWebSocketAutoResponse(new WebSocketRequestResponsePair(PING, PONG));
  }

  // Every event runs to completion before the next starts, so checks and state changes never interleave.
  serial(task) {
    this.queue = this.queue.then(task).catch((error) => console.error('capri-remote relay:', error && error.stack ? error.stack : String(error)));
    return this.queue;
  }

  async fetch(request) {
    const role = new URL(request.url).pathname.endsWith('/pc') ? 'pc' : 'phone';
    const pair = new WebSocketPair();
    const client = pair[0];
    const server = pair[1];
    this.ctx.acceptWebSocket(server);
    const now = Date.now();
    server.serializeAttachment({ id: b64u(crypto.getRandomValues(new Uint8Array(9))), role, state: 'auth', since: now, last: now });
    await this.serial(async () => {
      // Too many sockets that have not signed in: drop the oldest, not the newest. A real client signs in
      // within one round trip, so someone holding sockets open cannot lock it out.
      const older = this.sockets().filter((s) => s.att.state === 'auth' && s.ws !== server).sort((a, b) => a.att.since - b.att.since);
      for (const s of older.slice(0, Math.max(0, older.length + 1 - MAX_SIGNING_IN))) {
        this.end(s, 4429, 'busy', 'Too many connections are signing in. Try again in a minute.');
      }
      await this.settle();
    });
    return new Response(null, { status: 101, webSocket: client });
  }

  webSocketMessage(ws, message) {
    return this.serial(() => this.onMessage(ws, message).catch((error) => {
      console.error('capri-remote relay:', error && error.stack ? error.stack : String(error));
      const s = this.socket(ws);
      if (s.att) this.end(s, 1011, 'internal', 'The relay hit an error. Reconnect in a moment.');
    }));
  }

  webSocketClose(ws) {
    return this.serial(() => this.onGone(ws));
  }

  webSocketError(ws) {
    return this.serial(() => this.onGone(ws));
  }

  // Rethrows after logging, so the runtime retries a failed alarm instead of dropping every later deadline.
  async alarm() {
    let failure = null;
    await this.serial(() =>
      this.onAlarm().catch((error) => {
        failure = error;
        throw error;
      }),
    );
    if (failure) throw failure;
  }

  socket(ws) {
    let att = null;
    try {
      att = ws.deserializeAttachment();
    } catch {}
    return { ws, att };
  }

  sockets() {
    const list = [];
    for (const ws of this.ctx.getWebSockets()) {
      const s = this.socket(ws);
      if (s.att && s.att.state !== 'closed') list.push(s);
    }
    return list;
  }

  pc() {
    return this.sockets().find((s) => s.att.state === 'pc') || null;
  }

  phones() {
    return this.sockets().filter((s) => s.att.state === 'phone');
  }

  // The PC's socket, unless it has been silent past the idle timeout; then it is closed as dead.
  livePc(now) {
    const pc = this.pc();
    if (pc && now - this.lastActive(pc) > this.cfg.idleTimeout) {
      this.end(pc, 4408, 'timeout', 'No heartbeat from Capri for too long.');
      return null;
    }
    return pc;
  }

  async load() {
    if (this.room === undefined) this.room = (await this.ctx.storage.get('room')) || null;
    return this.room;
  }

  // Persist first, then cache. Events run one at a time, so nothing reads the cache in between.
  async save(room) {
    await this.ctx.storage.put('room', room);
    this.room = room;
  }

  async onMessage(ws, message) {
    const s = this.socket(ws);
    if (!s.att || s.att.state === 'closed') return;
    const text = typeof message === 'string';
    const size = text ? message.length : message.byteLength;
    const cap = text ? TEXT_MAX : s.att.state === 'pc' ? this.cfg.maxPcFrame : this.cfg.maxFrame;
    if (size > cap) return this.end(s, 1009, 'too-big', `Frames may be at most ${cap} bytes.`);
    const now = Date.now();
    if (!this.take(s, now)) return this.end(s, 4429, 'rate-limited', 'Too many frames. Slow down.');
    if ((s.att.state === 'pc' || s.att.state === 'phone') && now - s.att.last >= 5000) {
      s.att.last = now;
      ws.serializeAttachment(s.att);
    }
    if (!text) return this.onBinary(s, new Uint8Array(message));
    let msg = null;
    try {
      msg = JSON.parse(message);
    } catch {}
    if (!msg || typeof msg !== 'object' || Array.isArray(msg) || typeof msg.t !== 'string') {
      return this.end(s, 4400, 'bad-request', 'Text frames are JSON objects with a "t" field.');
    }
    if (msg.t === 'ping') return void send(ws, PONG);
    if (s.att.state === 'auth') return s.att.role === 'pc' ? this.signInPc(s, msg) : this.signInPhone(s, msg);
    if (s.att.state === 'pc') return this.control(s, msg);
    return this.end(s, 4400, 'bad-request', s.att.state === 'pending' ? 'Wait for your PC to answer.' : 'Once signed in, phones send only binary frames and pings.');
  }

  take(s, now) {
    const pc = s.att.role === 'pc';
    const rate = pc ? this.cfg.pcRate : this.cfg.phoneRate;
    const burst = pc ? this.cfg.pcBurst : this.cfg.phoneBurst;
    let bucket = this.buckets.get(s.att.id);
    if (!bucket) this.buckets.set(s.att.id, (bucket = { tokens: burst, at: now }));
    bucket.tokens = Math.min(burst, bucket.tokens + ((now - bucket.at) * rate) / 1000);
    bucket.at = now;
    if (bucket.tokens < 1) return false;
    bucket.tokens -= 1;
    return true;
  }

  onBinary(s, bytes) {
    if (s.att.state === 'phone') {
      if (bytes.length < MIN_ENVELOPE) return this.end(s, 4400, 'bad-request', 'Binary frames are envelopes of at least 72 bytes.');
      const pc = this.pc();
      if (!pc) return void send(s.ws, { t: 'error', op: 'send', code: 'pc-offline', message: 'Your PC is offline.' });
      return void send(pc.ws, concat(fromB64u(s.att.device, ID_BYTES), bytes));
    }
    if (s.att.state === 'pc') {
      if (bytes.length < ID_BYTES + MIN_ENVELOPE) return this.end(s, 4400, 'bad-request', 'Binary frames are a 16-byte device id and then an envelope.');
      const device = b64u(bytes.subarray(0, ID_BYTES));
      const target = this.phones().find((p) => p.att.device === device);
      if (!target) return void send(s.ws, { t: 'error', op: 'send', code: 'no-device', device, message: 'That phone is not connected.' });
      return void send(target.ws, bytes.slice(ID_BYTES));
    }
    return this.end(s, 4400, 'bad-request', 'Sign in before sending binary frames.');
  }

  async signInPc(s, msg) {
    if (msg.t !== 'auth') return this.end(s, 4400, 'bad-request', 'Send auth first.');
    const token = fromB64u(msg.token, 32);
    if (!token) return this.end(s, 4400, 'bad-request', 'The PC token is 32 bytes in base64url.');
    const hash = b64u(await sha256(token));
    let room = await this.load();
    const now = Date.now();
    const claimed = !room;
    if (claimed) await this.save((room = { v: 1, pc: hash, created: now, pcSeen: now, code: null, devices: {} }));
    else if (!sameText(room.pc, hash)) return this.end(s, 4401, 'bad-token', 'This room belongs to a different PC token.');
    for (const old of this.sockets()) {
      if (old.att.state === 'pc' && old.ws !== s.ws) this.end(old, 4409, 'replaced', 'Capri connected to this room again from somewhere else.', { replaced: true });
    }
    s.att.state = 'pc';
    s.att.last = now;
    s.ws.serializeAttachment(s.att);
    if (!claimed && now - room.pcSeen > DAY) await this.save((room = { ...room, pcSeen: now }));
    const phones = this.phones();
    send(s.ws, { t: 'ready', claimed, devices: Object.keys(room.devices), online: phones.map((p) => p.att.device) });
    for (const p of phones) send(p.ws, { t: 'presence', pc: true });
    await this.settle();
  }

  async signInPhone(s, msg) {
    if (msg.t !== 'auth' && msg.t !== 'pair') return this.end(s, 4400, 'bad-request', 'Send auth or pair first.');
    const room = await this.load();
    if (!room) return this.end(s, 4404, 'no-room', 'There is no Capri at this address any more.');
    return msg.t === 'auth' ? this.signInDevice(s, msg, room) : this.startPairing(s, msg, room);
  }

  async signInDevice(s, msg, room) {
    const device = id16(msg.device);
    const token = fromB64u(msg.token, 32);
    if (!device || !token) return this.end(s, 4400, 'bad-request', 'auth needs a 16-byte device id and a 32-byte token.');
    const hash = b64u(await sha256(token));
    const record = Object.hasOwn(room.devices, device) ? room.devices[device] : null;
    if (!record) return this.end(s, 4403, 'unknown-device', 'This phone is not paired with this Capri. Scan a new QR code.');
    if (!sameText(record.hash, hash)) return this.end(s, 4401, 'bad-token', 'Wrong device token.');
    for (const old of this.phones()) {
      if (old.att.device === device) this.end(old, 4409, 'replaced', 'This phone connected again from another tab or window.');
    }
    if (this.phones().length >= this.cfg.maxPhones) return this.end(s, 4429, 'busy', 'Too many phones are connected to this Capri.');
    const now = Date.now();
    const pc = this.livePc(now);
    s.att.state = 'phone';
    s.att.device = device;
    s.att.last = now;
    s.ws.serializeAttachment(s.att);
    send(s.ws, { t: 'ready', pc: Boolean(pc) });
    if (pc) send(pc.ws, { t: 'joined', device });
    await this.settle();
  }

  async startPairing(s, msg, room) {
    const code = fromB64u(msg.code, ID_BYTES);
    const nonce = id16(msg.nonce);
    const env = typeof msg.env === 'string' && msg.env.length <= 8192 ? fromB64u(msg.env) : null;
    if (!code || !nonce || !env || env.length < MIN_ENVELOPE) return this.end(s, 4400, 'bad-request', 'pair needs a 16-byte code, a 16-byte nonce and an envelope.');
    const hash = b64u(await sha256(code));
    const now = Date.now();
    const pc = this.livePc(now);
    if (!pc) return this.end(s, 4503, 'pc-offline', 'Your PC is offline. Open Capri on it and try again.');
    const open = room.code;
    if (!open || !sameText(open.hash, hash)) return this.end(s, 4410, 'code-unknown', 'This QR code is not valid. Show a new one on your PC.');
    if (open.used) return this.end(s, 4410, 'code-used', 'This QR code was already used. Show a new one on your PC.');
    if (now >= open.exp) return this.end(s, 4410, 'code-expired', 'This QR code has expired. Show a new one on your PC.');
    if (this.sockets().filter((x) => x.att.state === 'pending').length >= MAX_PENDING) {
      return this.end(s, 4429, 'busy', 'Another phone is already waiting for an answer. Try again in a minute.');
    }
    await this.save({ ...room, code: { ...open, used: true } });
    const id = b64u(crypto.getRandomValues(new Uint8Array(ID_BYTES)));
    s.att.state = 'pending';
    s.att.pending = id;
    s.att.since = now;
    s.ws.serializeAttachment(s.att);
    send(s.ws, { t: 'waiting' });
    send(pc.ws, { t: 'pending', id, nonce: msg.nonce, env: msg.env });
    await this.settle();
  }

  async control(s, msg) {
    const room = await this.load();
    if (!room) return this.end(s, 4404, 'no-room', 'This room was removed.');
    const bad = (what) => this.end(s, 4400, 'bad-request', what);
    const fail = (op, code, message, extra = {}) => void send(s.ws, { t: 'error', op, code, message, ...extra });
    const now = Date.now();
    switch (msg.t) {
      case 'code': {
        const hash = hash32(msg.hash);
        const ttl = msg.ttl === undefined ? this.cfg.codeTtl : msg.ttl;
        if (!hash || !Number.isInteger(ttl) || ttl < 1 || ttl > this.cfg.codeTtl) return bad(`code needs a 32-byte hash and a ttl from 1 to ${this.cfg.codeTtl} seconds.`);
        const exp = now + ttl * 1000;
        await this.save({ ...room, code: { hash, exp, used: false } });
        return void send(s.ws, { t: 'ok', op: 'code', expires: exp });
      }
      case 'code-cancel': {
        if (room.code) await this.save({ ...room, code: null });
        return void send(s.ws, { t: 'ok', op: 'code-cancel' });
      }
      case 'admit': {
        const pending = id16(msg.pending);
        const device = id16(msg.device);
        const tokenHash = hash32(msg.tokenHash);
        const env = typeof msg.env === 'string' && msg.env.length <= 8192 ? fromB64u(msg.env) : null;
        if (!pending || !device || !tokenHash || !env || env.length < MIN_ENVELOPE) return bad('admit needs pending, device, tokenHash and env.');
        const waiting = this.sockets().find((x) => x.att.state === 'pending' && x.att.pending === pending);
        if (!waiting || waiting.ws.readyState !== WebSocket.OPEN) return fail('admit', 'no-pending', 'That phone is no longer waiting.', { pending });
        if (Object.hasOwn(room.devices, device)) return fail('admit', 'device-exists', 'That device id is already paired.', { pending, device });
        if (Object.keys(room.devices).length >= this.cfg.maxDevices) return fail('admit', 'too-many-devices', `A room holds at most ${this.cfg.maxDevices} phones.`, { pending });
        await this.save({ ...room, devices: { ...room.devices, [device]: { hash: tokenHash, added: now } } });
        send(waiting.ws, { t: 'pair-reply', env: msg.env });
        this.end(waiting, 4001, 'paired', null, { quiet: true });
        send(s.ws, { t: 'ok', op: 'admit', pending, device });
        return this.settle();
      }
      case 'deny': {
        const pending = id16(msg.pending);
        const env = msg.env === undefined ? null : typeof msg.env === 'string' && msg.env.length <= 8192 ? fromB64u(msg.env) : null;
        if (!pending || (msg.env !== undefined && (!env || env.length < MIN_ENVELOPE))) return bad('deny needs pending, and env if given must be an envelope.');
        const waiting = this.sockets().find((x) => x.att.state === 'pending' && x.att.pending === pending);
        if (!waiting) return fail('deny', 'no-pending', 'That phone is no longer waiting.', { pending });
        if (env) send(waiting.ws, { t: 'pair-reply', env: msg.env });
        this.end(waiting, 4003, 'denied', null, { quiet: true });
        send(s.ws, { t: 'ok', op: 'deny', pending });
        return this.settle();
      }
      case 'revoke': {
        const device = id16(msg.device);
        if (!device) return bad('revoke needs a device id.');
        if (Object.hasOwn(room.devices, device)) {
          const devices = { ...room.devices };
          delete devices[device];
          await this.save({ ...room, devices });
        }
        for (const p of this.phones()) if (p.att.device === device) this.end(p, 4403, 'revoked', 'This phone was removed from Capri.');
        send(s.ws, { t: 'ok', op: 'revoke', device });
        return this.settle();
      }
      case 'kick': {
        const device = id16(msg.device);
        if (!device) return bad('kick needs a device id.');
        for (const p of this.phones()) if (p.att.device === device) this.end(p, 4000, 'kicked', 'Capri restarted this connection. Connect again.');
        send(s.ws, { t: 'ok', op: 'kick', device });
        return this.settle();
      }
      default:
        return bad('Unknown control.');
    }
  }

  // Closes a socket the relay is done with. Fatal errors send {"t":"error"} first, then the close code.
  end(s, code, reason, message, { quiet = false, replaced = false } = {}) {
    const prev = s.att.state;
    if (prev === 'closed') return;
    s.att.state = 'closed';
    try {
      s.ws.serializeAttachment(s.att);
    } catch {}
    if (message) send(s.ws, { t: 'error', code: reason, message });
    try {
      s.ws.close(code, reason);
    } catch {}
    this.buckets.delete(s.att.id);
    if (!quiet) this.departed(prev, s.att, reason, replaced);
  }

  departed(prev, att, reason, replaced = false) {
    if (prev === 'phone') {
      const pc = this.pc();
      if (pc) send(pc.ws, { t: 'left', device: att.device });
    } else if (prev === 'pending') {
      const pc = this.pc();
      if (pc) send(pc.ws, { t: 'pending-left', id: att.pending, reason });
    } else if (prev === 'pc') {
      for (const p of this.sockets()) {
        if (p.att.state === 'pending') this.end(p, 4503, 'pc-offline', 'Your PC went offline before it answered. Try again.', { quiet: true });
      }
      if (!replaced) {
        this.pcGoneAt = Date.now();
        for (const p of this.phones()) send(p.ws, { t: 'presence', pc: false });
      }
    }
  }

  async onGone(ws) {
    const s = this.socket(ws);
    try {
      ws.close();
    } catch {}
    if (!s.att || s.att.state === 'closed') return;
    const prev = s.att.state;
    s.att.state = 'closed';
    try {
      ws.serializeAttachment(s.att);
    } catch {}
    this.buckets.delete(s.att.id);
    this.departed(prev, s.att, 'closed');
    await this.settle();
  }

  lastActive(s) {
    let at = s.att.last || s.att.since || 0;
    try {
      const auto = this.ctx.getWebSocketAutoResponseTimestamp(s.ws);
      if (auto) at = Math.max(at, auto.getTime());
    } catch {}
    return at;
  }

  async onAlarm() {
    this.alarmAt = null;
    const now = Date.now();
    for (const s of this.sockets()) {
      const a = s.att;
      if (a.state === 'auth' && now >= a.since + this.cfg.authTimeout) this.end(s, 4408, 'timeout', 'Signing in took too long.');
      else if (a.state === 'pending' && now >= a.since + this.cfg.pendingTimeout) this.end(s, 4408, 'timeout', 'Your PC did not answer in time.');
      else if (a.state === 'phone' && now >= this.lastActive(s) + this.cfg.idleTimeout) this.end(s, 4408, 'timeout', 'No heartbeat for too long.');
    }
    const pc = this.pc();
    if (pc && this.phones().length > 0 && now >= this.lastActive(pc) + this.cfg.idleTimeout) {
      this.end(pc, 4408, 'timeout', 'No heartbeat from Capri for too long.');
    }
    await this.flushPcSeen();
    const room = await this.load();
    if (room && now >= room.pcSeen + this.cfg.roomTtl) {
      if (!this.pc()) return this.forget();
      await this.save({ ...room, pcSeen: now });
    }
    await this.settle();
  }

  async flushPcSeen() {
    if (!this.pcGoneAt) return;
    const at = this.pcGoneAt;
    this.pcGoneAt = 0;
    const room = await this.load();
    if (room && !this.pc() && at > room.pcSeen) await this.save({ ...room, pcSeen: at });
  }

  // Schedules the one alarm for the earliest deadline: a socket that must sign in, a phone waiting for
  // Allow, an idle check while phones are connected, or the room's 30-day clean-up. Only writes when
  // the alarm has to move earlier, so ordinary messages never write.
  async settle() {
    await this.flushPcSeen();
    const room = await this.load();
    let next = Infinity;
    let phoneOnline = false;
    for (const s of this.sockets()) {
      const a = s.att;
      if (a.state === 'auth') next = Math.min(next, a.since + this.cfg.authTimeout);
      else if (a.state === 'pending') next = Math.min(next, a.since + this.cfg.pendingTimeout);
      else if (a.state === 'phone') {
        phoneOnline = true;
        next = Math.min(next, this.lastActive(s) + this.cfg.idleTimeout);
      }
    }
    const pc = this.pc();
    if (pc && phoneOnline) next = Math.min(next, this.lastActive(pc) + this.cfg.idleTimeout);
    if (room) next = Math.min(next, room.pcSeen + this.cfg.roomTtl);
    if (next === Infinity) return;
    next = Math.max(next, Date.now() + 100);
    if (this.alarmAt === undefined) this.alarmAt = await this.ctx.storage.getAlarm();
    if (this.alarmAt === null || next < this.alarmAt) {
      this.alarmAt = next;
      await this.ctx.storage.setAlarm(next);
    }
  }

  async forget() {
    for (const s of this.sockets()) this.end(s, 4404, 'no-room', 'This Capri room was removed because its PC stayed away too long.', { quiet: true });
    this.room = null;
    this.pcGoneAt = 0;
    this.alarmAt = null;
    await this.ctx.storage.deleteAlarm();
    await this.ctx.storage.deleteAll();
  }
}

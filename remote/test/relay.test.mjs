// End-to-end tests: a fake PC and fake phones talk through the real relay under `wrangler dev --local`.
// Timeouts are shortened with --var so the slow paths run in seconds. Run: npm --prefix remote/relay test

import assert from 'node:assert/strict';
import http from 'node:http';
import { after, before, describe, test } from 'node:test';
import * as R from './lib/capri-remote.mjs';
import { Conn, FakePc, FakePhone, PHONE_ORIGIN, pairAndConnect, roomUrl } from './lib/fakes.mjs';
import { startRelay } from './lib/relay-dev.mjs';

const VARS = {
  AUTH_TIMEOUT_MS: 1500,
  PENDING_TIMEOUT_MS: 2000,
  IDLE_TIMEOUT_MS: 3000,
  ROOM_TTL_SECONDS: 4,
  PHONE_RATE: 5,
  PHONE_BURST: 20,
};

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const newRoom = () => R.b64u(R.randomBytes(16));

// Sends a raw upgrade request so the HTTP status of a refused upgrade is visible.
const upgradeStatus = (relay, path, origin) =>
  new Promise((resolve, reject) => {
    const headers = { Connection: 'Upgrade', Upgrade: 'websocket', 'Sec-WebSocket-Version': '13', 'Sec-WebSocket-Key': 'dGVzdCB0ZXN0IHRlc3QgIQ==' };
    if (origin) headers.Origin = origin;
    const request = http.request({ host: '127.0.0.1', port: relay.port, path, headers });
    request.on('upgrade', (response, socket) => {
      socket.destroy();
      resolve(response.statusCode);
    });
    request.on('response', (response) => {
      response.resume();
      resolve(response.statusCode);
    });
    request.on('error', reject);
    request.end();
  });

let relay;
before(async () => {
  relay = await startRelay(VARS);
});
after(async () => {
  await relay?.stop();
});

describe('routes and origins', () => {
  test('the Worker answers only room WebSockets, and checks the Origin', async () => {
    const room = newRoom();
    assert.equal((await fetch(relay.base + '/')).status, 200);
    assert.equal((await fetch(relay.base + '/nope')).status, 404);
    assert.equal((await fetch(`${relay.base}/v1/rooms/${room}/pc`)).status, 426);
    assert.equal(await upgradeStatus(relay, '/v1/rooms/not-a-room/pc'), 400);
    assert.equal(await upgradeStatus(relay, `/v1/rooms/${room}/pc`), 101);
    assert.equal(await upgradeStatus(relay, `/v1/rooms/${room}/pc`, PHONE_ORIGIN), 403, 'a web page must not act as the PC');
    assert.equal(await upgradeStatus(relay, `/v1/rooms/${room}/phone`), 403, 'phones are browsers and must send an Origin');
    assert.equal(await upgradeStatus(relay, `/v1/rooms/${room}/phone`, 'https://evil.example'), 403);
    assert.equal(await upgradeStatus(relay, `/v1/rooms/${room}/phone`, 'https://capriai.app.evil.example'), 403);
    assert.equal(await upgradeStatus(relay, `/v1/rooms/${room}/phone`, PHONE_ORIGIN), 101);
    assert.equal(await upgradeStatus(relay, `/v1/rooms/${room}/phone`, 'http://localhost:5173'), 101);
    assert.equal(await upgradeStatus(relay, `/v1/rooms/${room}/phone`, 'http://127.0.0.1:8080'), 101);
  });
});

describe('pairing', () => {
  test('allow: both screens show the same code, the phone gets its keys, then a full chat round trip', async () => {
    const pc = new FakePc(relay);
    const ready = await pc.connect();
    assert.deepEqual(ready, { t: 'ready', claimed: true, devices: [], online: [] });

    const qr = await pc.showCode();
    assert.match(qr, /^https:\/\/capriai\.app\/remote\/#r=[\w-]{22}&c=[\w-]{22}&k=[\w-]{43}$/);
    const phone = new FakePhone(relay, { name: 'Lachy test phone' });
    await phone.scan(qr);
    assert.deepEqual(await phone.conn.json(), { t: 'waiting' });

    const ask = await pc.pairRequest();
    assert.equal(ask.request.type, 'pair-request');
    assert.equal(ask.request.name, 'Lachy test phone');
    assert.match(ask.confirm, /^\d{3} \d{3}$/);
    assert.equal(ask.confirm, phone.confirm, 'the Allow prompt and the phone show the same confirmation code');

    const device = await pc.allow(ask);
    const { message, closed } = await phone.pairReply();
    assert.equal(message.type, 'pair-allow');
    assert.equal(message.device, device.id);
    assert.equal(closed.code, 4001);

    assert.deepEqual(await phone.connect(), { t: 'ready', pc: true });
    assert.deepEqual(await pc.conn.json(), { t: 'joined', device: device.id });
    const [helloOk] = await Promise.all([phone.hello(), pc.acceptHello()]);
    assert.deepEqual(helloOk.features, ['chat']);

    await phone.send({ type: 'chat.send', id: 'm1', text: 'What is on my calendar?' });
    const sent = await pc.receive();
    assert.equal(sent.device.id, device.id);
    assert.deepEqual(sent.message, { type: 'chat.send', id: 'm1', text: 'What is on my calendar?' });

    await pc.send(device.id, { type: 'chat.typing', on: true, re: 'm1' });
    await pc.send(device.id, { type: 'chat.tool', id: 't1', re: 'r1', name: 'calendar', summary: 'Reading your calendar', state: 'running' });
    await pc.send(device.id, { type: 'chat.tool', id: 't1', re: 'r1', name: 'calendar', summary: 'Read your calendar', state: 'done' });
    for (const [text, done] of [['You have ', false], ['two meetings ', false], ['today.', true]]) {
      await pc.send(device.id, { type: 'chat.reply', id: 'r1', re: 'm1', text, done });
    }
    assert.deepEqual(await phone.receive(), { type: 'chat.typing', on: true, re: 'm1' });
    assert.equal((await phone.receive()).state, 'running');
    assert.equal((await phone.receive()).state, 'done');
    let reply = '';
    for (;;) {
      const part = await phone.receive();
      assert.equal(part.type, 'chat.reply');
      assert.equal(part.id, 'r1');
      reply += part.text;
      if (part.done) break;
    }
    assert.equal(reply, 'You have two meetings today.');

    await phone.send({ type: 'ping', n: 7 });
    assert.deepEqual((await pc.receive()).message, { type: 'ping', n: 7 });
    await pc.send(device.id, { type: 'pong', n: 7 });
    assert.deepEqual(await phone.receive(), { type: 'pong', n: 7 });

    // Heartbeats are answered by the relay itself (the auto-response), on both sockets.
    await phone.conn.heartbeat();
    await pc.conn.heartbeat();
    phone.close();
    assert.deepEqual(await pc.conn.json(), { t: 'left', device: device.id });
    pc.close();
  });

  test('deny: the phone hears no, and the used code cannot be tried again', async () => {
    const pc = new FakePc(relay);
    await pc.connect();
    const qr = await pc.showCode();
    const phone = new FakePhone(relay);
    await phone.scan(qr);
    await phone.conn.json('waiting');
    const ask = await pc.pairRequest();
    await pc.deny(ask);
    const { message, closed } = await phone.pairReply();
    assert.equal(message.type, 'pair-deny');
    assert.equal(closed.code, 4003);
    assert.equal(phone.creds, null);

    const again = new FakePhone(relay);
    await again.scan(qr);
    await again.conn.refused('code-used', 4410);
    pc.close();
  });

  test('an expired code is refused', async () => {
    const pc = new FakePc(relay);
    await pc.connect();
    const qr = await pc.showCode(1);
    await sleep(1300);
    const phone = new FakePhone(relay);
    await phone.scan(qr);
    await phone.conn.refused('code-expired', 4410);
    pc.close();
  });

  test('a reused code is refused, and so is a made-up one', async () => {
    const pc = new FakePc(relay);
    await pc.connect();
    const qr = await pc.showCode();
    const first = new FakePhone(relay);
    await first.scan(qr);
    await first.conn.json('waiting');
    const ask = await pc.pairRequest();
    await pc.allow(ask);
    await first.pairReply();

    const second = new FakePhone(relay);
    await second.scan(qr);
    await second.conn.refused('code-used', 4410);

    const { room, key } = R.parseQr(qr);
    const madeUp = new FakePhone(relay);
    await madeUp.scan(R.qrUrl(room, R.b64u(R.randomBytes(16)), key));
    await madeUp.conn.refused('code-unknown', 4410);

    // A cancelled code stops working at once.
    const qr2 = await pc.showCode();
    pc.conn.send({ t: 'code-cancel' });
    assert.equal((await pc.conn.json('ok')).op, 'code-cancel');
    const late = new FakePhone(relay);
    await late.scan(qr2);
    await late.conn.refused('code-unknown', 4410);
    pc.close();
  });

  test('a phone waiting for Allow is told when the PC goes away, and pairing needs the PC online', async () => {
    const pc = new FakePc(relay);
    await pc.connect();
    const qr = await pc.showCode();
    const phone = new FakePhone(relay);
    await phone.scan(qr);
    await phone.conn.json('waiting');
    await pc.conn.json('pending');
    pc.close();
    await phone.conn.refused('pc-offline', 4503);

    const pc2 = new FakePc(relay);
    await pc2.connect();
    const qr2 = await pc2.showCode();
    pc2.close();
    await sleep(300);
    const late = new FakePhone(relay);
    await late.scan(qr2);
    await late.conn.refused('pc-offline', 4503);
  });
});

describe('access', () => {
  test('wrong tokens are refused', async () => {
    const pc = new FakePc(relay);
    await pc.connect();
    const { phone, device } = await pairAndConnect(relay, pc);

    const thief = new FakePc(relay, { room: pc.room });
    await thief.open();
    await thief.conn.refused('bad-token', 4401);

    const wrongToken = new FakePhone(relay);
    wrongToken.creds = { ...phone.creds };
    await wrongToken.open({ token: R.b64u(R.randomBytes(32)) });
    await wrongToken.conn.refused('bad-token', 4401);

    const unknownDevice = new FakePhone(relay);
    unknownDevice.creds = { ...phone.creds };
    await unknownDevice.open({ device: R.b64u(R.randomBytes(16)) });
    await unknownDevice.conn.refused('unknown-device', 4403);

    const nowhere = new FakePhone(relay);
    nowhere.creds = { ...phone.creds, room: newRoom() };
    await nowhere.open();
    await nowhere.conn.refused('no-room', 4404);

    // The real phone was never disturbed.
    await phone.send({ type: 'chat.send', id: 'm2', text: 'still here' });
    assert.equal((await pc.receive()).message.text, 'still here');
    assert.equal(device.session.rxSeq, 1);
    phone.close();
    pc.close();
  });

  test('revoke drops the phone at once and its token stops working', async () => {
    const pc = new FakePc(relay);
    await pc.connect();
    const { phone, device } = await pairAndConnect(relay, pc);

    await pc.send(device.id, { type: 'revoke' });
    pc.conn.send({ t: 'revoke', device: device.id });
    assert.deepEqual(await phone.receive(), { type: 'revoke' });
    await phone.conn.refused('revoked', 4403);
    const seen = [await pc.conn.json(), await pc.conn.json()];
    assert.deepEqual(seen.find((m) => m.t === 'left'), { t: 'left', device: device.id });
    assert.deepEqual(seen.find((m) => m.t === 'ok'), { t: 'ok', op: 'revoke', device: device.id });

    await phone.open();
    await phone.conn.refused('unknown-device', 4403);
    pc.close();
    const back = new FakePc(relay, { room: pc.room, token: pc.token });
    assert.deepEqual((await back.connect()).devices, []);
    back.close();
  });

  test('a second connection replaces the first, for a phone and for the PC', async () => {
    const pc = new FakePc(relay);
    await pc.connect();
    const { phone, device } = await pairAndConnect(relay, pc);

    const tab = new FakePhone(relay);
    tab.creds = phone.creds;
    await tab.open();
    await phone.conn.refused('replaced', 4409);
    assert.deepEqual(await tab.conn.json(), { t: 'ready', pc: true });
    assert.deepEqual(await pc.conn.json(), { t: 'left', device: device.id });
    assert.deepEqual(await pc.conn.json(), { t: 'joined', device: device.id });

    const pc2 = new FakePc(relay, { room: pc.room, token: pc.token });
    const ready = await pc2.connect();
    assert.deepEqual(ready, { t: 'ready', claimed: false, devices: [device.id], online: [device.id] });
    await pc.conn.refused('replaced', 4409);
    assert.deepEqual(await tab.conn.json(), { t: 'presence', pc: true }, 'no offline blip when the PC is replaced');
    tab.close();
    pc2.close();
  });
});

describe('presence', () => {
  test('phones hear when the PC goes away and comes back, then start a new session', async () => {
    const pc = new FakePc(relay);
    await pc.connect();
    const { phone, device } = await pairAndConnect(relay, pc);

    pc.close();
    assert.deepEqual(await phone.conn.json(), { t: 'presence', pc: false });
    await phone.send({ type: 'chat.send', id: 'm3', text: 'anyone there?' });
    const offline = await phone.conn.json('error');
    assert.equal(offline.code, 'pc-offline');
    assert.equal(offline.op, 'send');

    const back = new FakePc(relay, { room: pc.room, token: pc.token });
    back.devices.set(device.id, { ...device, session: null });
    const ready = await back.connect();
    assert.deepEqual(ready, { t: 'ready', claimed: false, devices: [device.id], online: [device.id] });
    assert.deepEqual(await phone.conn.json(), { t: 'presence', pc: true });

    await Promise.all([phone.hello(), back.acceptHello()]);
    await phone.send({ type: 'chat.send', id: 'm4', text: 'back?' });
    assert.equal((await back.receive()).message.text, 'back?');

    // A PC frame for a phone that is not connected gets a non-fatal error.
    back.conn.sendBytes(R.pcFrame(R.randomBytes(16), new Uint8Array(80)));
    const missing = await back.conn.json('error');
    assert.equal(missing.code, 'no-device');
    phone.close();
    back.close();
  });
});

describe('limits', () => {
  test('oversized frames are refused', async () => {
    const pc = new FakePc(relay);
    await pc.connect();
    const { phone } = await pairAndConnect(relay, pc);

    phone.conn.sendBytes(new Uint8Array(65536));
    const forwarded = await pc.conn.bytes();
    assert.equal(forwarded.length, 16 + 65536, 'a frame at the cap goes through');

    phone.conn.sendBytes(new Uint8Array(65537));
    await phone.conn.refused('too-big', 1009);
    await pc.conn.json('left');

    pc.conn.sendBytes(new Uint8Array(65537));
    await pc.conn.refused('too-big', 1009);

    const chatty = new FakePc(relay);
    await chatty.connect();
    chatty.conn.sendText(JSON.stringify({ t: 'code', hash: 'x'.repeat(17000) }));
    await chatty.conn.refused('too-big', 1009);
  });

  test('a phone that floods the relay is cut off', async () => {
    const pc = new FakePc(relay);
    await pc.connect();
    const { phone } = await pairAndConnect(relay, pc);
    for (let i = 0; i < 40; i++) phone.conn.sendBytes(new Uint8Array(80));
    const closed = await phone.conn.closedWithin();
    assert.equal(closed.code, 4429);
    assert.equal(closed.reason, 'rate-limited');
    pc.close();
  });

  test('sockets that never sign in cannot lock a real client out', async () => {
    const room = newRoom();
    const idle = [];
    // Heartbeats only, never a sign-in. Each sends one heartbeat before the next opens: a socket that has
    // sent no frame at all still gets the error and the close frame at once, but local workerd holds its
    // TCP teardown until the client gives up, so the close event would not fire.
    for (let i = 0; i < 8; i++) {
      idle.push(await Conn.open(roomUrl(relay, room, 'pc'), { heartbeat: 200 }));
      await idle[i].heartbeat();
    }
    idle.push(await Conn.open(roomUrl(relay, room, 'pc'), { heartbeat: 200 }));
    await idle[0].refused('busy', 4429);
    const pc = new FakePc(relay, { room });
    assert.equal((await pc.connect()).claimed, true);
    await idle[1].refused('busy', 4429);
    for (const conn of idle) conn.close();
    pc.close();
  });

  test('malformed frames close the connection', async () => {
    const room = newRoom();
    const early = await Conn.open(roomUrl(relay, room, 'pc'));
    early.sendBytes(new Uint8Array(100));
    await early.refused('bad-request', 4400);

    const junk = await Conn.open(roomUrl(relay, room, 'phone'), { origin: PHONE_ORIGIN });
    junk.sendText('not json');
    await junk.refused('bad-request', 4400);

    const pc = new FakePc(relay);
    await pc.connect();
    pc.conn.send({ t: 'nonsense' });
    await pc.conn.refused('bad-request', 4400);
  });
});

describe('end-to-end integrity', () => {
  test('a frame tampered in transit fails the MAC, and the PC resets that phone', async () => {
    const pc = new FakePc(relay);
    await pc.connect();
    const { phone, device } = await pairAndConnect(relay, pc);

    const envelope = await phone.session.seal({ type: 'chat.send', id: 'm5', text: 'transfer nothing' });
    envelope[30] ^= 0x01;
    phone.conn.sendBytes(envelope);
    await assert.rejects(pc.receive(), (error) => error instanceof R.EnvelopeError && error.reason === 'mac');

    pc.conn.send({ t: 'kick', device: device.id });
    await phone.conn.refused('kicked', 4000);
    const seen = [await pc.conn.json(), await pc.conn.json()];
    assert.ok(seen.some((m) => m.t === 'left'));
    assert.ok(seen.some((m) => m.t === 'ok' && m.op === 'kick'));

    // A fresh connection and hello give a working session again.
    await phone.connect();
    await pc.conn.json('joined');
    await Promise.all([phone.hello(), pc.acceptHello()]);
    await phone.send({ type: 'chat.send', id: 'm6', text: 'clean' });
    assert.equal((await pc.receive()).message.text, 'clean');
    phone.close();
    pc.close();
  });

  test('replayed and reordered frames are refused', async () => {
    const pc = new FakePc(relay);
    await pc.connect();
    const { phone } = await pairAndConnect(relay, pc);

    const first = await phone.session.seal({ type: 'chat.send', id: 'a', text: 'one' });
    phone.conn.sendBytes(first);
    assert.equal((await pc.receive()).message.text, 'one');
    phone.conn.sendBytes(first);
    await assert.rejects(pc.receive(), (error) => error instanceof R.EnvelopeError && error.reason === 'seq');

    const pc2 = new FakePc(relay);
    await pc2.connect();
    const pair2 = await pairAndConnect(relay, pc2);
    const second = await pair2.phone.session.seal({ type: 'chat.send', id: 'b', text: 'two' });
    const third = await pair2.phone.session.seal({ type: 'chat.send', id: 'c', text: 'three' });
    pair2.phone.conn.sendBytes(third);
    pair2.phone.conn.sendBytes(second);
    await assert.rejects(pc2.receive(), (error) => error instanceof R.EnvelopeError && error.reason === 'seq');
    phone.close();
    pair2.phone.close();
    pc.close();
    pc2.close();
  });
});

describe('timeouts and clean-up', () => {
  test('a socket that never signs in is closed', async () => {
    const quiet = await Conn.open(roomUrl(relay, newRoom(), 'phone'), { origin: PHONE_ORIGIN, heartbeat: 300 });
    await quiet.refused('timeout', 4408, 6000);
  });

  test('a pairing the PC never answers times out on both sides', async () => {
    const pc = new FakePc(relay);
    await pc.connect();
    const qr = await pc.showCode();
    const phone = new FakePhone(relay);
    await phone.scan(qr);
    await phone.conn.json('waiting');
    const pending = await pc.conn.json('pending');
    await phone.conn.refused('timeout', 4408, 6000);
    assert.deepEqual(await pc.conn.json('pending-left', 6000), { t: 'pending-left', id: pending.id, reason: 'timeout' });
    pc.close();
  });

  test('a phone that stops sending heartbeats is dropped and the PC is told', async () => {
    const pc = new FakePc(relay);
    await pc.connect();
    const { phone, device } = await pairAndConnect(relay, pc);
    clearInterval(phone.conn.timer);
    await phone.conn.refused('timeout', 4408, 8000);
    assert.deepEqual(await pc.conn.json('left', 8000), { t: 'left', device: device.id });
    pc.close();
  });

  test('a room whose PC stays away is deleted', async () => {
    const pc = new FakePc(relay);
    await pc.connect();
    const { phone } = await pairAndConnect(relay, pc);
    phone.close();
    await pc.conn.json('left');
    pc.close();
    await sleep(VARS.ROOM_TTL_SECONDS * 1000 + 1500);

    const old = new FakePhone(relay);
    old.creds = phone.creds;
    await old.open();
    await old.conn.refused('no-room', 4404);

    const newcomer = new FakePc(relay, { room: pc.room });
    assert.equal((await newcomer.connect()).claimed, true, 'the room id is free to claim again');
    newcomer.close();
  });
});

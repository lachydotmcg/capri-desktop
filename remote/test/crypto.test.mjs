// Proves the Capri Remote test vectors. Two independent implementations must agree on every byte:
// the WebCrypto reference (lib/capri-remote.mjs, what the phone uses) and a hand-rolled one on node:crypto
// written the way the C# side has to (HKDF from HMAC, AES-256-CBC with PKCS#7, HMAC-SHA256).

import assert from 'node:assert/strict';
import { createCipheriv, createDecipheriv, createHash, createHmac, timingSafeEqual } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import * as R from './lib/capri-remote.mjs';
import { PROTOCOL_PATH, VECTORS_PATH, buildVectors, renderMarkdown, spliceProtocol } from './make-vectors.mjs';

const vectors = await buildVectors();
const H = R.fromHex;

const hmac = (key, ...parts) => {
  const mac = createHmac('sha256', key);
  for (const part of parts) mac.update(part);
  return new Uint8Array(mac.digest());
};

function handHkdf(ikm, salt, info, length) {
  const prk = hmac(salt, ikm);
  const blocks = [];
  let previous = new Uint8Array(0);
  for (let i = 1; blocks.length * 32 < length; i++) {
    previous = hmac(prk, previous, typeof info === 'string' ? Buffer.from(info, 'ascii') : info, Uint8Array.of(i));
    blocks.push(previous);
  }
  return R.concat(...blocks).slice(0, length);
}

function handSeal(key, kind, dir, seq, iv, plaintext) {
  const header = Buffer.alloc(8);
  header[0] = 1;
  header[1] = kind;
  header[2] = dir;
  header.writeUInt32BE(seq, 4);
  const cipher = createCipheriv('aes-256-cbc', key.enc, iv);
  const body = Buffer.concat([header, iv, cipher.update(plaintext), cipher.final()]);
  return R.concat(body, hmac(key.mac, body));
}

function handOpen(key, kind, dir, seq, envelope) {
  if (envelope.length < 72 || (envelope.length - 56) % 16 !== 0) return 'length';
  const body = envelope.subarray(0, envelope.length - 32);
  if (!timingSafeEqual(hmac(key.mac, body), envelope.subarray(envelope.length - 32))) return 'mac';
  if (body[0] !== 1 || body[1] !== kind || body[2] !== dir || body[3] !== 0) return 'context';
  if (Buffer.from(body).readUInt32BE(4) !== seq) return 'seq';
  const decipher = createDecipheriv('aes-256-cbc', key.enc, body.subarray(8, 24));
  return new Uint8Array(Buffer.concat([decipher.update(body.subarray(24)), decipher.final()]));
}

const keyFor = (name) => {
  const [set, way] = name.split('.');
  const keys = vectors[`${set}Keys`][way];
  return { enc: H(keys.enc), mac: H(keys.mac) };
};

test('vectors.json and the block in PROTOCOL.md match what make-vectors.mjs computes', () => {
  assert.deepEqual(JSON.parse(readFileSync(VECTORS_PATH, 'utf8')), vectors, 'run: npm --prefix remote/relay run vectors');
  const doc = readFileSync(PROTOCOL_PATH, 'utf8');
  assert.equal(spliceProtocol(doc, renderMarkdown(vectors)), doc, 'PROTOCOL.md vectors are stale: npm --prefix remote/relay run vectors');
});

test('HKDF: RFC 5869 test case 1, with WebCrypto and by hand', async () => {
  const v = vectors.hkdfRfc5869Case1;
  assert.equal(v.okm, '3cb25f25faacd57a90434f64d0362f2a2d2d0a90cf1a5a4c5db02d56ecc4c5bf34007208d5b887185865');
  assert.equal(R.hex(await R.hkdf(H(v.ikm), H(v.salt), H(v.info), v.length)), v.okm);
  assert.equal(R.hex(handHkdf(H(v.ikm), H(v.salt), H(v.info), v.length)), v.okm);
});

test('every labelled key derivation matches a hand-rolled HKDF, and splits in the fixed order', () => {
  const I = vectors.inputs;
  const cases = [
    ['pairKeys', H(I.pairingKey.hex), H(I.pairNonce.hex), 'capri-remote/1 pair'],
    ['helloKeys', H(I.deviceKey.hex), H(I.deviceId.hex), 'capri-remote/1 hello'],
    ['sessionKeys', H(I.deviceKey.hex), R.concat(H(I.phoneNonce.hex), H(I.pcNonce.hex)), 'capri-remote/1 session'],
  ];
  for (const [name, ikm, salt, info] of cases) {
    const okm = handHkdf(ikm, salt, info, 128);
    const v = vectors[name];
    assert.equal(R.hex(okm), v.okm, name);
    assert.equal(v.salt, R.hex(salt));
    assert.equal(v.phoneToPc.enc, R.hex(okm.subarray(0, 32)));
    assert.equal(v.phoneToPc.mac, R.hex(okm.subarray(32, 64)));
    assert.equal(v.pcToPhone.enc, R.hex(okm.subarray(64, 96)));
    assert.equal(v.pcToPhone.mac, R.hex(okm.subarray(96, 128)));
  }
});

test('the confirmation code is the first four HKDF bytes, big-endian, mod 1,000,000', async () => {
  const I = vectors.inputs;
  const okm = handHkdf(H(I.pairingKey.hex), H(I.pairNonce.hex), 'capri-remote/1 confirm', 4);
  assert.equal(R.hex(okm), vectors.confirm.okm);
  const digits = String(Buffer.from(okm).readUInt32BE(0) % 1000000).padStart(6, '0');
  assert.equal(vectors.confirm.code, `${digits.slice(0, 3)} ${digits.slice(3)}`);
  assert.equal(await R.confirmCode(H(I.pairingKey.hex), H(I.pairNonce.hex)), vectors.confirm.code);
  // Leading zeros are kept: find a nonce whose code starts with 0 and check the format.
  for (let i = 0; i < 64; i++) {
    const code = await R.confirmCode(H(I.pairingKey.hex), R.concat(H(I.pairNonce.hex).subarray(0, 15), Uint8Array.of(i)));
    assert.match(code, /^\d{3} \d{3}$/);
  }
});

test('every envelope seals to the exact bytes, both ways, and opens to the exact plaintext', async () => {
  for (const e of vectors.envelopes) {
    const key = keyFor(e.keys);
    const plaintext = H(e.plaintextHex);
    assert.equal(R.hex(plaintext), R.hex(new TextEncoder().encode(e.plaintext)), `${e.name}: plaintext is the UTF-8 of the text`);
    assert.equal(R.hex(await R.seal(key, e.kind, e.dir, e.seq, plaintext, H(e.iv))), e.envelope, `${e.name}: WebCrypto seal`);
    assert.equal(R.hex(handSeal(key, e.kind, e.dir, e.seq, H(e.iv), plaintext)), e.envelope, `${e.name}: hand seal`);
    assert.equal(R.hex(await R.open(key, e.kind, e.dir, e.seq, H(e.envelope))), e.plaintextHex, `${e.name}: WebCrypto open`);
    assert.equal(R.hex(handOpen(key, e.kind, e.dir, e.seq, H(e.envelope))), e.plaintextHex, `${e.name}: hand open`);
    const bytes = H(e.envelope);
    assert.equal(bytes[0], 1);
    assert.equal(bytes[1], e.kind);
    assert.equal(bytes[2], e.dir);
    assert.equal(Buffer.from(bytes).readUInt32BE(4), e.seq);
    assert.equal(R.hex(bytes.subarray(8, 24)), e.iv);
  }
  const typing = vectors.envelopes.find((e) => e.name.startsWith('chat.typing'));
  assert.equal(H(typing.plaintextHex).length, 32);
  assert.equal(H(typing.envelope).length, 8 + 16 + 48 + 32, 'a 32-byte plaintext gets a whole 16-byte block of padding');
});

test('every reject vector fails to open, for the stated reason, in both implementations', async () => {
  for (const r of vectors.rejects) {
    const key = keyFor(r.keys);
    await assert.rejects(R.open(key, r.kind, r.dir, r.seq, H(r.envelope)), (error) => error instanceof R.EnvelopeError && error.reason === r.reason, r.name);
    assert.equal(handOpen(key, r.kind, r.dir, r.seq, H(r.envelope)), r.reason, r.name);
  }
});

test('hashes, base64url and the QR URL', () => {
  const I = vectors.inputs;
  const b64 = (hex) => createHash('sha256').update(H(hex)).digest('base64url');
  assert.equal(vectors.hashes.codeHashB64u, b64(I.code.hex));
  assert.equal(vectors.hashes.pcTokenHashB64u, b64(I.pcToken.hex));
  assert.equal(vectors.hashes.deviceTokenHashB64u, b64(I.deviceToken.hex));
  for (const value of Object.values(I)) {
    assert.equal(value.b64u, Buffer.from(H(value.hex)).toString('base64url'));
    assert.equal(R.hex(R.fromB64u(value.b64u)), value.hex);
  }
  assert.deepEqual(R.parseQr(vectors.qrUrl), { room: I.room.b64u, code: I.code.b64u, key: I.pairingKey.b64u });
});

test('base64url decoding is strict', () => {
  assert.throws(() => R.fromB64u('EBESExQVFhcYGRobHB0eHw=='), /bad base64url/, 'padding');
  assert.throws(() => R.fromB64u('EBESExQVFhcYGRobHB0eH+'), /bad base64url/, 'standard alphabet');
  assert.throws(() => R.fromB64u('EBESExQVFhcYGRobHB0eHx'), /non-canonical/, 'trailing bits set');
  assert.throws(() => R.fromB64u('EBESExQVFhcYGRobHB0eHw', 32), /expected 32 bytes/);
  assert.throws(() => R.fromB64u('A'), /bad base64url/);
});

test('frames and text frames in the vectors are built from the same values', () => {
  const I = vectors.inputs;
  const chatSend = vectors.envelopes.find((e) => e.name === 'chat.send').envelope;
  assert.equal(vectors.frames.relayToPcChatSend, I.deviceId.hex + chatSend);
  const split = R.splitPcFrame(H(vectors.frames.pcToRelayChatTyping));
  assert.equal(split.device, I.deviceId.b64u);
  const admit = JSON.parse(vectors.controls.pcAdmit);
  assert.equal(admit.tokenHash, vectors.hashes.deviceTokenHashB64u);
  assert.equal(R.hex(R.fromB64u(admit.env)), vectors.envelopes.find((e) => e.name === 'pair-allow').envelope);
  const pair = JSON.parse(vectors.controls.phonePair);
  assert.equal(pair.code, I.code.b64u);
  assert.equal(pair.nonce, I.pairNonce.b64u);
  assert.equal(JSON.parse(vectors.controls.pcCode).hash, vectors.hashes.codeHashB64u);
});

test('a Channel counts sequence numbers per direction and refuses the other side\'s frames', async () => {
  const keys = R.splitKeys(R.randomBytes(128));
  const phone = new R.Channel(keys, R.KIND.session, 'phone');
  const pc = new R.Channel(keys, R.KIND.session, 'pc');
  const sent = await Promise.all([phone.seal({ type: 'ping', n: 1 }), phone.seal({ type: 'ping', n: 2 }), phone.seal({ type: 'ping', n: 3 })]);
  assert.deepEqual(sent.map((envelope) => Buffer.from(envelope).readUInt32BE(4)), [0, 1, 2]);
  for (const [i, envelope] of sent.entries()) assert.equal((await pc.open(envelope)).n, i + 1);
  await assert.rejects(phone.open(sent[0]), (error) => error.reason === 'mac', 'a phone cannot open its own frames');
  const back = await pc.seal({ type: 'pong', n: 1 });
  assert.equal((await phone.open(back)).type, 'pong');
  await assert.rejects(phone.open(back), (error) => error.reason === 'seq', 'a replay is refused');
});

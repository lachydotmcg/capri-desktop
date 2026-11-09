// The fake phone's side of the screen view and the call (PROTOCOL.md 8.2): watch.frame parts put back together with
// every JPEG checked, and the 16-bit PCM that call.audio and call.voice carry.

import assert from 'node:assert/strict';
import * as R from './capri-remote.mjs';

export const PART_BYTES = 44000; // JPEG bytes in one watch.frame part
export const MAX_PLAINTEXT = 60000;
export const MAX_PHONE_ENVELOPE = 65536 - R.ID_BYTES; // the relay's cap on a PC frame, less the device id in front
export const CALL_RATE = 16000;

// The bytes a sealed message of this many UTF-8 bytes takes: header, IV, padded ciphertext and tag (PROTOCOL.md 6.3).
export const envelopeBytes = (jsonBytes) => R.HEADER_BYTES + R.IV_BYTES + 16 * (Math.floor(jsonBytes / 16) + 1) + R.TAG_BYTES;

// The width and height a JPEG's start-of-frame segment gives, or null.
export function jpegSize(bytes) {
  let at = 2;
  while (at + 9 < bytes.length) {
    if (bytes[at] !== 0xff) return null;
    const marker = bytes[at + 1];
    if (marker === 0xff) {
      at++;
      continue;
    }
    const length = (bytes[at + 2] << 8) | bytes[at + 3];
    if (marker >= 0xc0 && marker <= 0xcf && marker !== 0xc4 && marker !== 0xc8 && marker !== 0xcc)
      return { w: (bytes[at + 7] << 8) | bytes[at + 8], h: (bytes[at + 5] << 8) | bytes[at + 6] };
    at += 2 + length;
  }
  return null;
}

// Gathers one phone's watch.frame parts, which come in order. add() gives back the whole frame with its last part.
export class FrameAssembler {
  constructor() {
    this.open = null;
  }

  add(message) {
    const { n, part, parts } = message;
    assert.ok(Number.isInteger(n) && n > 0, `frame number ${n}`);
    assert.ok(Number.isInteger(parts) && parts >= 1 && Number.isInteger(part) && part >= 0 && part < parts, `part ${part} of ${parts}`);
    if (part === 0) {
      for (const key of ['w', 'h', 'bytes', 'cx', 'cy']) assert.ok(Number.isInteger(message[key]), `the first part's ${key}`);
      assert.equal(typeof message.monitor, 'string');
      const { w, h, monitor, bytes, cx, cy } = message;
      this.open = { n, w, h, monitor, bytes, cx, cy, parts, pieces: [] };
    }
    const open = this.open;
    assert.ok(open && open.n === n && open.parts === parts && open.pieces.length === part, `frame ${n} part ${part} out of order`);
    const piece = R.fromB64u(message.data);
    assert.ok(part === parts - 1 ? piece.length >= 1 && piece.length <= PART_BYTES : piece.length === PART_BYTES, `part ${part} has ${piece.length} bytes`);
    open.pieces.push(piece);
    if (open.pieces.length < parts) return null;
    this.open = null;
    const jpeg = R.concat(...open.pieces);
    assert.equal(jpeg.length, open.bytes, 'the frame is as long as its first part said');
    assert.ok(jpeg[0] === 0xff && jpeg[1] === 0xd8 && jpeg[jpeg.length - 2] === 0xff && jpeg[jpeg.length - 1] === 0xd9, 'a whole JPEG');
    assert.deepEqual(jpegSize(jpeg), { w: open.w, h: open.h }, 'the JPEG is the size its first part said');
    const { pieces, ...frame } = open;
    return { ...frame, jpeg };
  }
}

// ---------- PCM ----------

export function pcmBytes(samples) {
  const bytes = new Uint8Array(samples.length * 2);
  const view = new DataView(bytes.buffer);
  samples.forEach((sample, i) => view.setInt16(i * 2, sample, true));
  return bytes;
}

export function pcmSamples(bytes) {
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const samples = new Int16Array(bytes.length >> 1);
  for (let i = 0; i < samples.length; i++) samples[i] = view.getInt16(i * 2, true);
  return samples;
}

// FNV-1a over the samples' bytes, low byte first, as the PC's stand-in ears print it.
export function fnv(samples) {
  let hash = 2166136261;
  for (const sample of samples) {
    hash = Math.imul(hash ^ (sample & 0xff), 16777619) >>> 0;
    hash = Math.imul(hash ^ ((sample >> 8) & 0xff), 16777619) >>> 0;
  }
  return hash.toString(16).padStart(8, '0');
}

// The PC's test tone: a sine at 9000 of 32767, cut toward zero as C# casts it.
export function tone(rate, seconds, hz) {
  const samples = new Int16Array(Math.floor(rate * seconds));
  for (let i = 0; i < samples.length; i++) samples[i] = Math.trunc(Math.sin((2 * Math.PI * hz * i) / rate) * 9000);
  return samples;
}

// Half the rate, each pair averaged, as RemoteCall.Halve does.
export function halve(samples) {
  const half = new Int16Array(samples.length >> 1);
  for (let i = 0; i < half.length; i++) half[i] = Math.trunc((samples[i * 2] + samples[i * 2 + 1]) / 2);
  return half;
}

// One push-to-talk line as call.audio parts of partMs each, the last with end.
export function audioParts(id, samples, partMs = 320) {
  const step = (CALL_RATE * partMs) / 1000;
  const parts = [];
  for (let at = 0, n = 0; at < samples.length || n === 0; at += step, n++) {
    const part = { type: 'call.audio', id, n, rate: CALL_RATE, pcm: R.b64u(pcmBytes(samples.subarray(at, at + step))) };
    if (at + step >= samples.length) part.end = true;
    parts.push(part);
  }
  return parts;
}

// The largest difference between two runs of samples of the same length.
export function furthest(a, b) {
  assert.equal(a.length, b.length, 'as many samples');
  let most = 0;
  for (let i = 0; i < a.length; i++) most = Math.max(most, Math.abs(a[i] - b[i]));
  return most;
}

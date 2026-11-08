/* Capri Remote protocol 1: the phone's crypto and framing (PROTOCOL.md section 6), ported from the reference in
   remote/test/lib/capri-remote.mjs and checked against remote/test/vectors.json. WebCrypto only, so it needs a secure
   context: https://capriai.app or localhost. */
(() => {
  'use strict';
  const webCrypto = window.crypto;
  const subtle = webCrypto && webCrypto.subtle;
  const utf8 = new TextEncoder();
  const strictUtf8 = new TextDecoder('utf-8', { fatal: true });

  const PROTOCOL = 1;
  const KIND = Object.freeze({ pair: 1, hello: 2, session: 3 });
  const DIR = Object.freeze({ phoneToPc: 1, pcToPhone: 2 });
  const LABELS = Object.freeze({
    pair: 'capri-remote/1 pair',
    confirm: 'capri-remote/1 confirm',
    hello: 'capri-remote/1 hello',
    session: 'capri-remote/1 session'
  });
  const HEADER_BYTES = 8, IV_BYTES = 16, TAG_BYTES = 32, MIN_ENVELOPE_BYTES = 72, MAX_SEQ = 0xffffffff;

  class EnvelopeError extends Error {
    constructor(reason) {
      super('envelope rejected: ' + reason);
      this.reason = reason;
    }
  }

  const ready = () => !!(subtle && window.isSecureContext !== false);
  const randomBytes = length => webCrypto.getRandomValues(new Uint8Array(length));
  const concat = (...parts) => {
    const out = new Uint8Array(parts.reduce((sum, part) => sum + part.length, 0));
    let at = 0;
    for (const part of parts) { out.set(part, at); at += part.length; }
    return out;
  };
  const hex = bytes => Array.from(bytes, byte => byte.toString(16).padStart(2, '0')).join('');
  const fromHex = text => {
    if (!/^(?:[0-9a-f]{2})*$/.test(text)) throw new Error('bad hex');
    return Uint8Array.from(text.match(/../g) || [], pair => parseInt(pair, 16));
  };

  // base64url without padding (RFC 4648 section 5).
  const b64u = bytes => {
    let binary = '';
    for (let i = 0; i < bytes.length; i += 0x8000) binary += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
    return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  };
  // Strict: no padding, no other alphabet, canonical trailing bits, and the right length when one is given.
  const fromB64u = (text, length) => {
    if (typeof text !== 'string' || !/^[A-Za-z0-9_-]*$/.test(text) || text.length % 4 === 1) throw new Error('bad base64url');
    const binary = atob(text.replace(/-/g, '+').replace(/_/g, '/') + '='.repeat((4 - (text.length % 4)) % 4));
    const bytes = Uint8Array.from(binary, char => char.charCodeAt(0));
    if (b64u(bytes) !== text) throw new Error('non-canonical base64url');
    if (length !== undefined && bytes.length !== length) throw new Error('expected ' + length + ' bytes, got ' + bytes.length);
    return bytes;
  };

  const sha256 = async bytes => new Uint8Array(await subtle.digest('SHA-256', bytes));
  const hashB64u = async bytes => b64u(await sha256(bytes));

  // HKDF-SHA256 (RFC 5869). ikm is bytes, or an HKDF CryptoKey: this phone keeps its device key as a non-extractable one.
  const isKey = value => typeof CryptoKey !== 'undefined' && value instanceof CryptoKey;
  const importDeviceKey = bytes => subtle.importKey('raw', bytes, 'HKDF', false, ['deriveBits']);
  const hkdf = async (ikm, salt, info, length) => {
    const key = isKey(ikm) ? ikm : await subtle.importKey('raw', ikm, 'HKDF', false, ['deriveBits']);
    const bits = await subtle.deriveBits({ name: 'HKDF', hash: 'SHA-256', salt, info: typeof info === 'string' ? utf8.encode(info) : info }, key, length * 8);
    return new Uint8Array(bits);
  };

  // 128 bytes of HKDF output split into each direction's keys: phone->PC AES, phone->PC HMAC, PC->phone AES, PC->phone HMAC.
  const splitKeys = okm => {
    if (okm.length !== 128) throw new Error('need 128 bytes');
    return {
      phoneToPc: { enc: okm.slice(0, 32), mac: okm.slice(32, 64) },
      pcToPhone: { enc: okm.slice(64, 96), mac: okm.slice(96, 128) }
    };
  };
  const pairKeys = async (pairingKey, pairNonce) => splitKeys(await hkdf(pairingKey, pairNonce, LABELS.pair, 128));
  const helloKeys = async (deviceKey, deviceId) => splitKeys(await hkdf(deviceKey, deviceId, LABELS.hello, 128));
  const sessionKeys = async (deviceKey, phoneNonce, pcNonce) => splitKeys(await hkdf(deviceKey, concat(phoneNonce, pcNonce), LABELS.session, 128));

  // The code both screens show while pairing, "ddd ddd": a big-endian uint32 of 4 HKDF bytes, mod 1,000,000.
  const confirmCode = async (pairingKey, pairNonce) => {
    const bytes = await hkdf(pairingKey, pairNonce, LABELS.confirm, 4);
    const value = new DataView(bytes.buffer, bytes.byteOffset, 4).getUint32(0, false);
    const digits = String(value % 1000000).padStart(6, '0');
    return digits.slice(0, 3) + ' ' + digits.slice(3);
  };

  const header = (kind, dir, seq) => {
    if (!Number.isInteger(seq) || seq < 0 || seq > MAX_SEQ) throw new Error('bad seq');
    const out = new Uint8Array(HEADER_BYTES);
    out[0] = PROTOCOL;
    out[1] = kind;
    out[2] = dir;
    out[3] = 0;
    new DataView(out.buffer).setUint32(4, seq, false);
    return out;
  };

  // envelope = header(8) || iv(16) || AES-256-CBC-PKCS7(enc, iv, plaintext) || HMAC-SHA256(mac, header || iv || ciphertext)
  const seal = async (key, kind, dir, seq, plaintext, iv = randomBytes(IV_BYTES)) => {
    if (iv.length !== IV_BYTES) throw new Error('iv must be 16 bytes');
    const aes = await subtle.importKey('raw', key.enc, { name: 'AES-CBC' }, false, ['encrypt']);
    const ciphertext = new Uint8Array(await subtle.encrypt({ name: 'AES-CBC', iv }, aes, plaintext));
    const body = concat(header(kind, dir, seq), iv, ciphertext);
    const hmac = await subtle.importKey('raw', key.mac, { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']);
    return concat(body, new Uint8Array(await subtle.sign('HMAC', hmac, body)));
  };

  // In this order: length, the MAC (WebCrypto's verify compares in constant time), the MAC'd header, then decrypt.
  const open = async (key, kind, dir, seq, envelope) => {
    if (!(envelope instanceof Uint8Array)) envelope = new Uint8Array(envelope);
    if (envelope.length < MIN_ENVELOPE_BYTES || (envelope.length - HEADER_BYTES - IV_BYTES - TAG_BYTES) % 16 !== 0) throw new EnvelopeError('length');
    const body = envelope.subarray(0, envelope.length - TAG_BYTES);
    const tag = envelope.subarray(envelope.length - TAG_BYTES);
    const hmac = await subtle.importKey('raw', key.mac, { name: 'HMAC', hash: 'SHA-256' }, false, ['verify']);
    if (!(await subtle.verify('HMAC', hmac, tag, body))) throw new EnvelopeError('mac');
    if (body[0] !== PROTOCOL) throw new EnvelopeError('version');
    if (body[1] !== kind || body[2] !== dir || body[3] !== 0) throw new EnvelopeError('context');
    if (new DataView(body.buffer, body.byteOffset, HEADER_BYTES).getUint32(4, false) !== seq) throw new EnvelopeError('seq');
    const aes = await subtle.importKey('raw', key.enc, { name: 'AES-CBC' }, false, ['decrypt']);
    try {
      return new Uint8Array(await subtle.decrypt({ name: 'AES-CBC', iv: body.subarray(8, 24) }, aes, body.subarray(24)));
    } catch (error) {
      throw new EnvelopeError('padding');
    }
  };

  const encodeMessage = message => utf8.encode(JSON.stringify(message));
  const decodeMessage = bytes => {
    let message;
    try { message = JSON.parse(strictUtf8.decode(bytes)); } catch (error) { throw new EnvelopeError('json'); }
    if (!message || typeof message !== 'object' || Array.isArray(message) || typeof message.type !== 'string') throw new EnvelopeError('json');
    return message;
  };

  // One side of a pair, hello or session context. Each direction counts from 0 and goes up by one. Seals resolve in the
  // order they were asked for, so sending each envelope as its promise resolves keeps them in sequence.
  class Channel {
    constructor(keys, kind, side) {
      if (side !== 'phone' && side !== 'pc') throw new Error('side is phone or pc');
      this.kind = kind;
      this.txDir = side === 'phone' ? DIR.phoneToPc : DIR.pcToPhone;
      this.rxDir = side === 'phone' ? DIR.pcToPhone : DIR.phoneToPc;
      this.tx = side === 'phone' ? keys.phoneToPc : keys.pcToPhone;
      this.rx = side === 'phone' ? keys.pcToPhone : keys.phoneToPc;
      this.txSeq = 0;
      this.rxSeq = 0;
      this.chain = Promise.resolve();
    }

    seal(message, iv) {
      const seq = this.txSeq++;
      const run = this.chain.then(() => seal(this.tx, this.kind, this.txDir, seq, encodeMessage(message), iv));
      this.chain = run.catch(() => {});
      return run;
    }

    async open(envelope) {
      const plaintext = await open(this.rx, this.kind, this.rxDir, this.rxSeq, envelope);
      this.rxSeq++;
      return decodeMessage(plaintext);
    }
  }

  // The QR code's fragment: #r=<room id>&c=<one-time code>&k=<pairing key>, each checked strictly.
  const parseFragment = hash => {
    const params = new URLSearchParams(String(hash || '').replace(/^#/, ''));
    const room = params.get('r'), code = params.get('c'), key = params.get('k');
    fromB64u(room, 16);
    fromB64u(code, 16);
    fromB64u(key, 32);
    return { room, code, key };
  };

  window.CapriCrypto = Object.freeze({
    PROTOCOL, KIND, DIR, LABELS, EnvelopeError, Channel,
    ready, randomBytes, concat, hex, fromHex, b64u, fromB64u, sha256, hashB64u, hkdf, importDeviceKey,
    splitKeys, pairKeys, helloKeys, sessionKeys, confirmCode, header, seal, open, encodeMessage, decodeMessage, parseFragment
  });
})();

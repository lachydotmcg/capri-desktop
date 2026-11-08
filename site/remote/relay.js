/* Capri Remote's link through the relay: the phone's side of PROTOCOL.md sections 3 to 9. One WebSocket out to the relay;
   pairing from the QR code; the PC's Allow; the device this phone was allowed on kept in IndexedDB; a fresh end-to-end
   session on every connection; heartbeats, close codes and reconnects. remote.js opens it and gets back link states and
   the PC's messages. */
(() => {
  'use strict';
  const C = window.CapriCrypto;
  const APP = 'remote-web/0.1.0';
  const PING = '{"t":"ping"}';
  const HEARTBEAT = 30000, PONG_WAIT = 10000, HELLO_WAIT = 10000;

  // Names from the other side are shown, never trusted: one line, no control or direction-flipping characters, 64 at most.
  const tidy = value => String(value == null ? '' : value).replace(/[\x00-\x1f\x7f\u200e\u200f\u202a-\u202e\u2066-\u2069]/g, ' ').replace(/\s+/g, ' ').trim().slice(0, 64);

  // ---------- The device this phone was allowed on: room, device id, device token and a non-extractable device key ----------
  const DB = 'capri-remote', STORE = 'device', SLOT = 'current';
  const base = () => new Promise((resolve, reject) => {
    const request = indexedDB.open(DB, 1);
    request.onupgradeneeded = () => request.result.createObjectStore(STORE);
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
  const store = (mode, act) => base().then(db => new Promise((resolve, reject) => {
    const tx = db.transaction(STORE, mode);
    const request = act(tx.objectStore(STORE));
    tx.oncomplete = () => { db.close(); resolve(request.result); };
    tx.onerror = tx.onabort = () => { db.close(); reject(tx.error); };
  }));
  const loadDevice = () => store('readonly', slots => slots.get(SLOT)).catch(() => null);
  const saveDevice = record => store('readwrite', slots => slots.put(record, SLOT));
  const clearDevice = () => store('readwrite', slots => slots.delete(SLOT)).catch(() => {});

  // What a fatal error's code means, as the close code that would carry it (PROTOCOL.md section 4.5).
  const closeFor = {
    'too-big': 1009, internal: 1011, kicked: 4000, 'bad-request': 4400, 'bad-token': 4401, 'unknown-device': 4403, revoked: 4403,
    'no-room': 4404, timeout: 4408, replaced: 4409, 'code-unknown': 4410, 'code-used': 4410, 'code-expired': 4410, busy: 4429,
    'rate-limited': 4429, 'pc-offline': 4503
  };

  const link = options => {
    const relay = String(options.relay || '').replace(/\/+$/, '');
    const name = tidy(options.name) || 'Phone';
    let on = null;
    let closed = false;
    let ws = null;
    let scan = null;        // the QR's room, code and pairing key: in memory only, dropped at pair-reply
    let pairChannel = null;
    let confirm = '';
    let device = null;      // { room, device, token, key, pc, idBytes }
    let hello = null;       // { channel, nonce, timer }
    let session = null;
    let pcName = '';
    let sending = Promise.resolve();
    let beat = 0, pongTimer = 0, retryTimer = 0, backoff = 1000;
    let queue = Promise.resolve();

    const set = (state, extra) => { if (!closed) on.state(state, Object.assign({ pc: pcName }, extra || {})); };
    // Socket events are handled one at a time, in order, even though opening an envelope is asynchronous.
    const later = fn => { queue = queue.then(fn).catch(error => console.error(error)); };
    const stopTimers = () => {
      clearInterval(beat);
      clearTimeout(pongTimer);
      clearTimeout(retryTimer);
      if (hello) clearTimeout(hello.timer);
    };
    // Closes this side without reacting to the close: used once a close or fatal error has been acted on.
    const drop = () => {
      stopTimers();
      hello = null;
      session = null;
      if (!ws) return;
      const old = ws;
      ws = null;
      old.onopen = old.onmessage = old.onclose = old.onerror = null;
      try { old.close(1000); } catch (error) {}
    };
    const finish = reason => {
      if (closed) return;
      drop();
      scan = pairChannel = null;
      on.forget(reason);
    };

    const ping = socket => {
      if (socket !== ws || socket.readyState !== WebSocket.OPEN) return;
      socket.send(PING);
      clearTimeout(pongTimer);
      pongTimer = setTimeout(() => { if (socket === ws) lost(); }, PONG_WAIT);
    };
    const openSocket = (room, first) => {
      drop();
      let socket;
      try { socket = new WebSocket(relay + '/v1/rooms/' + room + '/phone'); } catch (error) { ended(1006, ''); return; }
      socket.binaryType = 'arraybuffer';
      ws = socket;
      socket.onopen = () => later(() => {
        if (socket !== ws) return;
        first(socket);
        clearInterval(beat);
        beat = setInterval(() => ping(socket), HEARTBEAT);
      });
      socket.onmessage = event => later(() => socket === ws ? receive(socket, event.data) : null);
      socket.onclose = event => later(() => {
        if (socket !== ws) return;
        ws = null;
        stopTimers();
        ended(event.code, event.reason);
      });
      socket.onerror = () => {};
    };

    // ---------- Pairing: present the QR's one-time code, show the confirmation code, wait for the PC's Allow ----------
    const pair = async () => {
      set('connecting', { reason: '', code: '' });
      const pairingKey = C.fromB64u(scan.key, 32);
      const nonce = C.randomBytes(16);
      pairChannel = new C.Channel(await C.pairKeys(pairingKey, nonce), C.KIND.pair, 'phone');
      confirm = await C.confirmCode(pairingKey, nonce);
      const env = await pairChannel.seal({ type: 'pair-request', v: 1, name, app: APP });
      if (closed) return;
      const { room, code } = scan;
      openSocket(room, socket => socket.send(JSON.stringify({ t: 'pair', code, nonce: C.b64u(nonce), env: C.b64u(env) })));
    };
    const pairFrame = async frame => {
      if (frame.t === 'waiting') { set('pairing', { code: confirm, reason: '' }); return; }
      if (frame.t !== 'pair-reply') return;
      let message;
      try { message = await pairChannel.open(C.fromB64u(frame.env)); } catch (error) { finish('ended'); return; }
      // The pairing key is done with either way.
      const room = scan.room;
      scan = pairChannel = null;
      if (message.type === 'pair-deny') { finish(message.reason === 'busy' ? 'busy' : 'denied'); return; }
      if (message.type !== 'pair-allow') { finish('ended'); return; }
      let record;
      try {
        record = {
          room,
          device: message.device,
          token: message.token,
          idBytes: C.fromB64u(message.device, 16),
          key: await C.importDeviceKey(C.fromB64u(message.key, 32)),
          pc: tidy(message.name) || 'Your PC',
          pairedAt: Date.now()
        };
        C.fromB64u(message.token, 32);
      } catch (error) { finish('ended'); return; }
      device = record;
      pcName = record.pc;
      try { await saveDevice({ room: record.room, device: record.device, token: record.token, key: record.key, pc: record.pc, pairedAt: record.pairedAt }); } catch (error) {}
      if (!closed) on.paired(record.pc);
      // The relay closes this socket with 4001 next, and the phone signs in with its device token.
    };

    // ---------- Signing in as the allowed device, then a fresh session with every hello ----------
    const signIn = () => {
      set('connecting', { reason: '', code: '' });
      openSocket(device.room, socket => socket.send(JSON.stringify({ t: 'auth', device: device.device, token: device.token })));
    };
    const sayHello = async socket => {
      if (hello) clearTimeout(hello.timer);
      hello = null;
      session = null;
      set('connecting', { reason: '' });
      const nonce = C.randomBytes(16);
      const channel = new C.Channel(await C.helloKeys(device.key, device.idBytes), C.KIND.hello, 'phone');
      const env = await channel.seal({ type: 'hello', v: 1, nonce: C.b64u(nonce), name, app: APP });
      if (socket !== ws || socket.readyState !== WebSocket.OPEN) return;
      hello = { channel, nonce, timer: setTimeout(() => { if (socket === ws) lost(); }, HELLO_WAIT) };
      socket.send(env);
    };
    const helloReply = async (socket, bytes) => {
      const waiting = hello;
      let message;
      try { message = await waiting.channel.open(bytes); } catch (error) { broken(); return; }
      if (message.type === 'error') {
        clearTimeout(waiting.timer);
        hello = null;
        set('offline', { reason: message.code === 'version' ? 'version' : 'error' });
        return;
      }
      // A hello-ok that doesn't echo this hello's nonce is an old one played back: drop it (the wait runs out).
      if (message.type !== 'hello-ok' || message.echo !== C.b64u(waiting.nonce)) return;
      let pcNonce;
      try { pcNonce = C.fromB64u(message.nonce, 16); } catch (error) { broken(); return; }
      clearTimeout(waiting.timer);
      hello = null;
      const keys = await C.sessionKeys(device.key, waiting.nonce, pcNonce);
      if (socket !== ws) return;
      session = new C.Channel(keys, C.KIND.session, 'phone');
      pcName = tidy(message.name) || device.pc || pcName;
      backoff = 1000;
      set('connected', { reason: '', features: Array.isArray(message.features) ? message.features.filter(name => typeof name === 'string').slice(0, 16) : [] });
      on.message({ type: 'presence', pc: true });
    };
    const sessionFrame = async bytes => {
      let message;
      try { message = await session.open(bytes); } catch (error) { broken(); return; }
      // Her brains (PROTOCOL.md section 8.1): SETTINGS shows them.
      if (message.type === 'brain.state' || message.type === 'brain.note') { on.message(message); return; }
      switch (message.type) {
        case 'ping':
          if (Number.isInteger(message.n)) send({ type: 'pong', n: message.n });
          return;
        case 'revoke':
        case 'chat.reply':
        case 'chat.tool':
        case 'chat.typing':
        case 'error':
          on.message(message);
          return;
        default:
          // pong, and names reserved for later phases or unknown: ignored.
          return;
      }
    };

    const receive = async (socket, data) => {
      if (typeof data !== 'string') {
        const bytes = new Uint8Array(data);
        if (hello) await helloReply(socket, bytes);
        else if (session) await sessionFrame(bytes);
        return;
      }
      let frame;
      try { frame = JSON.parse(data); } catch (error) { return; }
      if (!frame || typeof frame.t !== 'string') return;
      if (frame.t === 'pong') { clearTimeout(pongTimer); return; }
      if (frame.t === 'error') { failed(frame); return; }
      if (scan) { await pairFrame(frame); return; }
      if (!device || (frame.t !== 'ready' && frame.t !== 'presence')) return;
      if (frame.t === 'ready') backoff = 1000;
      if (frame.pc === true) { await sayHello(socket); return; }
      if (hello) clearTimeout(hello.timer);
      hello = null;
      session = null;
      set('offline', { reason: '' });
      on.message({ type: 'presence', pc: false });
    };

    // ---------- Errors, closes and reconnects (PROTOCOL.md section 4.5) ----------
    const failed = frame => {
      if (frame.op) {
        // Not fatal. A frame dropped because the PC is offline: presence {pc: false} says the same.
        if (frame.op === 'send' && frame.code === 'pc-offline' && session) { session = null; set('offline', { reason: '' }); }
        return;
      }
      // Fatal: act on it now, stop sending and close this side. The close event that follows is ignored.
      const code = String(frame.code || '');
      drop();
      ended(closeFor[code] || 1011, code);
    };
    const ended = (code, reason) => {
      hello = null;
      session = null;
      if (closed) return;
      if (scan || pairChannel) {
        // 1006: no close frame at all, so the relay was never reached.
        finish(code === 4003 ? 'denied' : code === 4410 ? 'code' : code === 4503 ? 'pc-offline' : code === 4408 ? 'timeout' : code === 4429 ? 'busy' : code === 1006 ? 'network' : 'ended');
        return;
      }
      if (!device) { finish('ended'); return; }
      if (code === 4001) { signIn(); return; }
      if (code === 4400) console.warn('Capri Remote: the relay refused a frame (bad-request)');
      if (code === 4401 || code === 4403 || code === 4404) { finish(code === 4403 && reason !== 'unknown-device' ? 'revoked' : 'ended'); return; }
      if (code === 4409) { set('offline', { reason: 'replaced' }); return; }
      if (code === 4000) { retry(1000); return; }
      if (code === 4429) { retry(Math.max(10000, backoff)); return; }
      retry();
    };
    // Reconnect with backoff: 1 second doubling to 60, plus jitter.
    const retry = wait => {
      if (closed || !device) return;
      const delay = wait != null ? wait : backoff + Math.random() * backoff * 0.3;
      if (wait == null) backoff = Math.min(60000, backoff * 2);
      if (delay > 2500 || navigator.onLine === false) set('offline', { reason: 'network' });
      else set('connecting', { reason: '' });
      clearTimeout(retryTimer);
      retryTimer = setTimeout(() => { if (!closed && !ws) signIn(); }, delay);
    };
    // The socket or the session stopped answering.
    const lost = () => {
      if (scan) { finish('ended'); return; }
      drop();
      retry();
    };
    // An envelope that doesn't open ends the session: start a new connection, which starts a new session.
    const broken = () => {
      drop();
      retry(1000);
    };

    // Back from the background or back online: check the socket, and reconnect at once if it's gone.
    const wake = () => {
      if (closed || document.hidden || scan || !device) return;
      if (ws && ws.readyState === WebSocket.OPEN) { ping(ws); return; }
      if (!ws) { clearTimeout(retryTimer); backoff = 1000; signIn(); }
    };
    const offline = () => { if (!closed && device && !scan) set('offline', { reason: 'network' }); };
    document.addEventListener('visibilitychange', wake);
    addEventListener('online', wake);
    addEventListener('offline', offline);

    const send = message => {
      if (!session || !ws || ws.readyState !== WebSocket.OPEN) return false;
      const socket = ws, channel = session;
      const sealed = channel.seal(message);
      sending = sealed.then(env => {
        if (socket === ws && channel === session && socket.readyState === WebSocket.OPEN) socket.send(env);
      }, () => {});
      return true;
    };

    return {
      open(pairing, hooks) {
        on = hooks;
        // Promise.resolve: the link is set up before any state comes back.
        Promise.resolve().then(async () => {
          if (closed) return;
          if (!C || !C.ready() || !window.WebSocket) { finish('insecure'); return; }
          if (pairing.scan) {
            scan = pairing.scan;
            try { await pair(); } catch (error) { finish('bad-code'); }
            return;
          }
          const record = await loadDevice();
          if (closed) return;
          try {
            if (!record || !record.key || typeof record.token !== 'string' || typeof record.room !== 'string') throw new Error('no device');
            device = Object.assign({}, record, { idBytes: C.fromB64u(record.device, 16) });
          } catch (error) { finish('ended'); return; }
          pcName = record.pc || '';
          signIn();
        });
      },
      send,
      retry() {
        if (closed || !device || scan) return;
        clearTimeout(retryTimer);
        backoff = 1000;
        drop();
        signIn();
      },
      // Closing waits for anything already being sealed (an unpair's revoke) to go out first.
      close() {
        if (closed) return;
        closed = true;
        document.removeEventListener('visibilitychange', wake);
        removeEventListener('online', wake);
        removeEventListener('offline', offline);
        clearTimeout(retryTimer);
        const shut = () => drop();
        Promise.race([sending, new Promise(resolve => setTimeout(resolve, 1500))]).then(shut, shut);
      }
    };
  };

  window.CapriRelay = { link, clear: clearDevice, tidy };
})();

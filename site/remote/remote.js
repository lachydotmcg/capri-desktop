/* Capri Remote's link to her PC. The app only talks to the PC through this module, in the message names and fields of
   remote/PROTOCOL.md (section 8):
     CapriRemote.connect(options)   pair from the QR code's #fragment, or the PC this phone was allowed on, or the demo
     CapriRemote.send(type, body)   'chat.send' { id, text }; true if it went out
     CapriRemote.use(mode)          'phone' runs her on this phone with its own key (native.js), 'pc' goes back to the PC
     CapriRemote.onMessage(fn)      fn(message), where message.type is one of:
                                      'chat.typing'  { on, re }                        Capri is thinking (a receipt for re)
                                      'chat.tool'    { id, name, summary, state, re }  state 'running', 'done' or 'failed'
                                      'chat.reply'   { id, text, done, re }            one reply, streamed in parts
                                      'error'        { code, message, re }             'busy', 'bad-message', 'not-allowed'
                                      'presence'     { pc }                            the PC came online or went away
     CapriRemote.onState(fn)        fn(state, info), state is one of:
                                      'unpaired'   no PC: show how to scan the code (info.reason says why a pairing ended)
                                      'pairing'    waiting for "Allow this phone?" on the PC; info.code is the code to match
                                      'connecting' reaching the relay and the PC
                                      'offline'    paired, but Capri is closed or the PC is asleep (info.reason says more)
                                      'connected'  live
   The real link is relay.js (PROTOCOL.md sections 3 to 9, crypto in crypto.js); the demo stand-in is demo.js; her brain
   on this phone, with no PC, is native.js. A revoke from the PC forgets the pairing here. */
(() => {
  'use strict';
  const SAVED = 'capri-remote-pairing';
  const config = window.CAPRI_CONFIG || {};
  // The relay isn't deployed yet (PROTOCOL.md section 3); this is its planned host. On localhost, ?relay=ws://127.0.0.1:8787
  // points the app at a relay from `npm --prefix remote/relay run dev`.
  const RELAY = 'wss://capri-remote.lachydotmcg.workers.dev';
  const relayUrl = () => {
    const local = /^(localhost|127\.0\.0\.1|\[::1\])$/.test(location.hostname);
    const asked = local ? new URLSearchParams(location.search).get('relay') : '';
    if (asked && /^wss?:\/\/[A-Za-z0-9.:[\]-]+$/.test(asked)) return asked;
    return typeof config.relay === 'string' && /^wss:\/\//.test(config.relay) ? config.relay : RELAY;
  };
  // What the PC's "Allow this phone?" shows, and this phone's own label in the menu.
  const deviceName = (() => {
    const agent = navigator.userAgent;
    if (/iPhone/.test(agent)) return 'iPhone';
    if (/iPad/.test(agent) || (/Macintosh/.test(agent) && navigator.maxTouchPoints > 1)) return 'iPad';
    if (/Android/.test(agent)) return /Mobile/.test(agent) ? 'Android phone' : 'Android tablet';
    const browser = /Edg\//.test(agent) ? 'Edge' : /Firefox\//.test(agent) ? 'Firefox' : /Chrome\//.test(agent) ? 'Chrome' : /Safari\//.test(agent) ? 'Safari' : 'Browser';
    const system = /Windows/.test(agent) ? 'Windows' : /Mac OS X/.test(agent) ? 'Mac' : /Linux/.test(agent) ? 'Linux' : '';
    return system ? browser + ' on ' + system : browser;
  })();

  const hooks = { message: new Set(), state: new Set() };
  let state = 'unpaired';
  let info = { demo: false, native: false, pc: '', code: '', reason: '', pairedAt: 0 };
  let link = null;
  let pairing = null;

  const tell = (set, ...values) => {
    for (const fn of set) {
      try { fn(...values); } catch (error) { console.error(error); }
    }
  };
  const setState = (next, extra) => {
    state = next;
    info = Object.assign({}, info, extra || {});
    // The demo PC allowed this phone: keep that, so a reload goes straight back to the chat.
    if (next === 'connected' && pairing && pairing.demo && !pairing.kept) { pairing.allowed = pairing.kept = true; keep(pairing); }
    tell(hooks.state, state, info);
  };

  // localStorage keeps only which PC this phone is paired with, and since when. The device id, token and key are the relay
  // link's, in IndexedDB (PROTOCOL.md section 9). The QR's pairing key is only ever in memory. Storage can be blocked or
  // wiped (private windows, cleared site data), so every read and write is guarded.
  const read = () => {
    try {
      const saved = JSON.parse(localStorage.getItem(SAVED));
      return saved && typeof saved === 'object' && (saved.demo === true || saved.device === true) ? saved : null;
    } catch (error) { return null; }
  };
  const write = value => {
    try {
      if (value) localStorage.setItem(SAVED, JSON.stringify(value));
      else localStorage.removeItem(SAVED);
    } catch (error) {}
  };
  const keep = value => write({ demo: !!value.demo, device: !value.demo, pc: String(value.pc || '').slice(0, 64), pairedAt: value.pairedAt || Date.now(), allowed: !!value.allowed });

  // The QR code opens capriai.app/remote/#r=<room>&c=<one-time code>&k=<pairing key>. A #fragment never reaches a server.
  // It's read once and wiped from the address bar and history straight away.
  const fromFragment = () => {
    const hash = location.hash;
    if (!/[#&](r|c|k)=/.test(hash)) return null;
    try { history.replaceState(null, '', location.pathname + location.search); } catch (error) {}
    try {
      return { scan: window.CapriCrypto.parseFragment(hash), pc: '', pairedAt: Date.now(), allowed: false };
    } catch (error) { return { bad: true }; }
  };

  // Messages coming in. A revoke means the PC removed this phone: forget the pairing.
  const deliver = message => {
    if (!message || typeof message !== 'object' || typeof message.type !== 'string') return;
    if (message.type === 'revoke') { forget('revoked'); return; }
    tell(hooks.message, message);
  };
  const forget = reason => {
    if (link) { try { link.close(); } catch (error) {} link = null; }
    const wasDemo = pairing && pairing.demo;
    pairing = null;
    write(null);
    if (!wasDemo && window.CapriRelay) window.CapriRelay.clear();
    setState('unpaired', { demo: false, native: false, pc: '', code: '', reason: reason || '', pairedAt: 0 });
  };

  const connect = (options = {}) => {
    if (link) { try { link.close(); } catch (error) {} link = null; }
    const scanned = options.demo ? null : fromFragment();
    if (scanned && scanned.bad) { forget('bad-code'); return; }
    // A fresh scan replaces whatever this phone was paired with before.
    if (scanned && window.CapriRelay) window.CapriRelay.clear();
    // Phone-only mode (native.js): her brain on this phone with its own key, until a scan or the demo picks a PC again.
    const native = window.CapriNative;
    if (native && (scanned || options.demo)) native.use('pc');
    const saved = read();
    const own = native && native.on() && !scanned && !options.demo ? { native: true, pc: saved && saved.allowed ? saved.pc : '', pairedAt: saved && saved.allowed ? saved.pairedAt : 0 } : null;
    pairing = options.demo ? { demo: true, pc: 'Demo PC', pairedAt: Date.now(), allowed: !!options.instant } : scanned || own || saved;
    if (!pairing) { setState('unpaired', { demo: false, native: false, pc: '', code: '', reason: '', pairedAt: 0 }); return; }
    // A build without the relay link (the demo preview) can only run the demo.
    if (!pairing.demo && !pairing.native && !window.CapriRelay) { forget(''); return; }
    if (pairing.demo) keep(pairing);
    else if (scanned) write(null);
    info = Object.assign({}, info, { features: [], demo: !!pairing.demo, native: !!pairing.native, pc: pairing.pc || '', pairedAt: pairing.native ? pairing.pairedAt : pairing.pairedAt || Date.now(), code: '', reason: '' });
    link = pairing.native ? native.link() : pairing.demo && window.CapriDemo ? window.CapriDemo.link() : window.CapriRelay.link({ relay: relayUrl(), name: deviceName });
    const mine = link;
    link.open(pairing, {
      state: (next, extra) => { if (link === mine) setState(next, extra); },
      message: message => { if (link === mine) deliver(message); },
      forget: reason => { if (link === mine) forget(reason); },
      // The PC said Allow: remember which PC (its keys are the relay link's to keep). The pairing key is already gone.
      paired: pc => {
        if (link !== mine || !pairing) return;
        pairing.pc = pc || pairing.pc;
        pairing.allowed = true;
        pairing.pairedAt = Date.now();
        delete pairing.scan;
        keep(pairing);
        info = Object.assign({}, info, { pc: pairing.pc, pairedAt: pairing.pairedAt });
      }
    });
  };

  // Unpairing tells the PC to forget this phone too (a revoke from the phone), then forgets it here.
  const unpair = () => {
    // On this phone's own brain, Unpair forgets the PC it was paired with and she stays here.
    if (pairing && pairing.native) {
      const was = read();
      write(null);
      if (window.CapriRelay && !(was && was.demo)) window.CapriRelay.clear();
      pairing.pc = '';
      pairing.pairedAt = 0;
      setState(state, { pc: '', pairedAt: 0 });
      return;
    }
    if (link && state === 'connected') { try { link.send({ type: 'revoke' }); } catch (error) {} }
    forget('');
  };

  window.CapriRemote = {
    connect,
    unpair,
    send: (type, body) => state === 'connected' && link ? link.send(Object.assign({}, body, { type })) !== false : false,
    retry: () => { if (link && link.retry) link.retry(); else connect(); },
    // 'phone' runs her on this phone with its own key (native.js), 'pc' goes back to the PC this phone was paired with.
    use: mode => { if (window.CapriNative) window.CapriNative.use(mode); connect(); },
    onMessage: fn => { hooks.message.add(fn); return () => hooks.message.delete(fn); },
    onState: fn => { hooks.state.add(fn); fn(state, info); return () => hooks.state.delete(fn); },
    // Demo only: step the stand-in through each link state, so every screen can be seen without a PC, and tap its Allow.
    simulate: name => { if (link && link.simulate) link.simulate(name); },
    allow: () => { if (link && link.allow) link.allow(); },
    device: deviceName,
    get state() { return state; },
    get info() { return info; }
  };
})();

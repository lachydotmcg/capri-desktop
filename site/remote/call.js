/* Capri Remote's call (PROTOCOL.md 8.1): talk to her with your screen in the mini player over the chat. Push to talk:
   hold TALK while speaking, or tap it on and tap again to send. The phone records only then, sends the line as 16 kHz
   call.audio parts, and her own ears on the PC hear it as if it was said to her. Her answer comes back in the chat, and
   her real voice plays here as call.voice parts.
     CapriCall.supported()  this session's PC takes calls and this browser can record
     CapriCall.start()      ring her; false when it can't
     CapriCall.end()        hang up
   On localhost, ?fakecall swaps the mic for a test tone and mutes her voice, for the tests. */
(() => {
  'use strict';
  const RATE = 16000;
  const PART = 5120; // 320 ms
  const MOST = 29 * RATE; // a line stops itself just under the PC's 30 seconds
  const TAIL = 200; // keep listening this long after TALK lets go, for the end of the last word
  const TAP = 350; // a press shorter than this is a tap: talking stays on until the next tap
  const C = window.CapriCrypto;
  const local = /^(localhost|127\.0\.0\.1|\[::1\])$/.test(location.hostname);
  const fake = local && new URLSearchParams(location.search).has('fakecall');
  const remote = () => window.CapriRemote;
  const say = text => { if (window.CapriApp) window.CapriApp.say(text); };
  const newId = () => Date.now().toString(36) + Math.random().toString(36).slice(2, 10);
  const make = (tag, className, text) => {
    const el = document.createElement(tag);
    if (className) el.className = className;
    if (text != null) el.textContent = text;
    return el;
  };

  // ---------- The call bar ----------
  const bar = make('div', 'callbar');
  bar.hidden = true;
  bar.setAttribute('role', 'region');
  bar.setAttribute('aria-label', 'Call with Capri');
  const status = make('span', 'call-status', 'Calling');
  status.setAttribute('role', 'status');
  const talk = make('button', 'nbtn cyan talk', 'Hold to talk');
  talk.type = 'button';
  talk.setAttribute('aria-pressed', 'false');
  const hangUp = make('button', 'nbtn hot', 'End');
  hangUp.type = 'button';
  hangUp.setAttribute('aria-label', 'End the call');
  bar.append(status, talk, hangUp);

  let state = 'off'; // 'off', 'ringing' or 'on'
  let them = { voice: true, ears: true };
  let ctx = null;
  let out = null;
  let ring = 0;
  let line = null;
  let hearing = '';
  let playAt = 0;
  const playing = new Set();
  const stats = { lines: 0, parts: 0, audioBytes: 0, heard: 0, voiceParts: 0, voiceSamples: 0, hushes: 0 };

  const supported = () => {
    const r = remote();
    const recordable = fake || !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia);
    return !!(r && r.state === 'connected' && Array.isArray(r.info.features) && r.info.features.includes('call') && C && (window.AudioContext || window.webkitAudioContext) && recordable);
  };
  const label = () => {
    if (state === 'ringing') return 'Calling';
    if (line) return line.ending ? 'Sending' : line.latched ? 'Listening: tap to send' : 'Listening';
    if (hearing) return 'Hearing you';
    if (!them.ears) return 'Her ears are off on the PC';
    if (!them.voice) return 'On a call: her voice is off on the PC';
    return 'On a call';
  };
  const draw = () => {
    bar.hidden = state === 'off';
    document.getElementById('app').classList.toggle('on-call', state !== 'off');
    status.textContent = label();
    talk.textContent = line ? (line.ending ? 'Sending' : line.latched ? 'Tap to send' : 'Talking') : 'Hold to talk';
    talk.setAttribute('aria-pressed', line ? 'true' : 'false');
    talk.classList.toggle('live', !!line);
    talk.disabled = state !== 'on' || !them.ears;
  };

  // One AudioContext for the mic and her voice, made on the CALL tap, as iPhones need.
  const audio = () => {
    if (!ctx) {
      const Context = window.AudioContext || window.webkitAudioContext;
      ctx = new Context();
      out = ctx.createGain();
      out.gain.value = fake ? 0 : 1;
      out.connect(ctx.destination);
    }
    if (ctx.state === 'suspended') ctx.resume().catch(() => {});
    return ctx;
  };

  // ---------- His voice out ----------
  const pcm = samples => {
    const bytes = new Uint8Array(samples.length * 2);
    const view = new DataView(bytes.buffer);
    for (let i = 0; i < samples.length; i++) view.setInt16(i * 2, samples[i], true);
    return C.b64u(bytes);
  };
  const sendPart = (current, samples, end) => {
    const message = { id: current.id, n: current.n++, rate: RATE, pcm: pcm(samples) };
    if (end) message.end = true;
    stats.parts++;
    stats.audioBytes += samples.length * 2;
    return remote().send('call.audio', message);
  };
  // Box-filtered down to 16 kHz from the mic's own rate.
  const resampler = rate => {
    const step = rate / RATE;
    let carry = new Float32Array(0);
    let at = 0;
    return input => {
      const all = new Float32Array(carry.length + input.length);
      all.set(carry);
      all.set(input, carry.length);
      const result = [];
      while (at + step <= all.length) {
        const from = Math.floor(at), to = Math.max(from + 1, Math.floor(at + step));
        let sum = 0;
        for (let i = from; i < to; i++) sum += all[i];
        result.push(sum / (to - from));
        at += step;
      }
      const used = Math.min(all.length, Math.floor(at));
      carry = all.slice(used);
      at -= used;
      return result;
    };
  };
  const take = (current, floats) => {
    if (line !== current) return;
    for (const value of floats) {
      current.buffer[current.used++] = Math.max(-32768, Math.min(32767, Math.round(value * 32767)));
      current.total++;
      if (current.used === PART) {
        if (!sendPart(current, current.buffer, false)) { cancel('Not sent'); return; }
        current.buffer = new Int16Array(PART);
        current.used = 0;
      }
    }
    if (current.total >= MOST) finish();
  };

  // The mic, only while he talks. Its tracks stop as soon as the line ends, so her voice plays out of the loudspeaker.
  let worklet = null;
  const mic = async current => {
    if (fake) {
      // A test tone at 16 kHz in real time.
      let phase = 0;
      const timer = setInterval(() => {
        const block = new Float32Array(640);
        for (let i = 0; i < block.length; i++) block[i] = Math.sin((2 * Math.PI * 300 * phase++) / RATE) * 0.3;
        take(current, block);
      }, 40);
      return () => clearInterval(timer);
    }
    const context = audio();
    const stream = await navigator.mediaDevices.getUserMedia({ audio: { channelCount: 1, echoCancellation: true, noiseSuppression: true, autoGainControl: true } });
    const stopTracks = () => { for (const track of stream.getTracks()) track.stop(); };
    if (line !== current) { stopTracks(); return () => {}; }
    const source = context.createMediaStreamSource(stream);
    const down = resampler(context.sampleRate);
    let node;
    if (context.audioWorklet && window.AudioWorkletNode) {
      if (!worklet) worklet = context.audioWorklet.addModule('mic-worklet.js');
      await worklet;
      node = new AudioWorkletNode(context, 'capri-mic');
      node.port.onmessage = event => take(current, down(event.data));
    } else {
      node = context.createScriptProcessor(2048, 1, 1);
      node.onaudioprocess = event => take(current, down(event.inputBuffer.getChannelData(0)));
    }
    // A muted path to the speakers keeps the node running in every browser.
    const quiet = context.createGain();
    quiet.gain.value = 0;
    source.connect(node);
    node.connect(quiet);
    quiet.connect(context.destination);
    return () => {
      try { source.disconnect(); node.disconnect(); quiet.disconnect(); } catch (error) {}
      if (node.port) node.port.onmessage = null;
      stopTracks();
    };
  };

  const begin = async () => {
    if (state !== 'on' || line || !them.ears) return;
    hush();
    const current = { id: newId(), n: 0, buffer: new Int16Array(PART), used: 0, total: 0, latched: false, release: null, down: Date.now() };
    line = current;
    draw();
    try {
      const release = await mic(current);
      if (line === current) current.release = release;
      else release();
    } catch (error) {
      if (line === current) line = null;
      draw();
      say(error && error.name === 'NotAllowedError' ? 'Microphone blocked: allow it for this site' : 'No microphone');
    }
  };
  // The line goes, a moment after TALK lets go: what's left with end, then the PC's ears take it.
  const finish = () => {
    const current = line;
    if (!current || current.ending) return;
    current.ending = true;
    draw();
    const close = () => {
      if (current.cancelled) return;
      if (line === current) line = null;
      if (current.release) current.release();
      if (current.n === 0 && current.used < RATE / 4) { draw(); say('Too short: hold to talk'); return; }
      if (sendPart(current, current.buffer.subarray(0, current.used), true)) {
        stats.lines++;
        hearing = current.id;
      } else say('Not sent');
      draw();
    };
    if (current.release) setTimeout(close, TAIL);
    else close();
  };
  const cancel = text => {
    const current = line;
    if (!current) return;
    line = null;
    current.cancelled = true;
    if (current.release) current.release();
    if (current.n > 0) remote().send('call.cancel', { id: current.id });
    draw();
    if (text) say(text);
  };

  talk.addEventListener('pointerdown', event => {
    if (event.button !== 0 || talk.disabled) return;
    event.preventDefault();
    if (line && line.latched) { finish(); return; }
    try { talk.setPointerCapture(event.pointerId); } catch (error) {}
    begin();
  });
  const letGo = event => {
    if (!line || line.latched || line.ending) return;
    if (event.type === 'pointercancel') { cancel(''); return; }
    if (Date.now() - line.down < TAP) {
      line.latched = true;
      draw();
      return;
    }
    finish();
  };
  talk.addEventListener('pointerup', letGo);
  talk.addEventListener('pointercancel', letGo);
  talk.addEventListener('keydown', event => {
    if (event.key !== ' ' && event.key !== 'Enter') return;
    event.preventDefault();
    if (event.repeat) return;
    if (line) finish();
    else begin().then(() => { if (line) { line.latched = true; draw(); } });
  });
  hangUp.addEventListener('click', () => end());

  // ---------- Her voice in ----------
  const voice = message => {
    if (state !== 'on' || !ctx || typeof message.pcm !== 'string' || !Number.isInteger(message.rate) || message.rate < 8000 || message.rate > 48000) return;
    let bytes;
    try { bytes = C.fromB64u(message.pcm); } catch (error) { return; }
    const count = bytes.length >> 1;
    if (!count) return;
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    const sound = ctx.createBuffer(1, count, message.rate);
    const samples = sound.getChannelData(0);
    for (let i = 0; i < count; i++) samples[i] = view.getInt16(i * 2, true) / 32768;
    const source = ctx.createBufferSource();
    source.buffer = sound;
    source.connect(out);
    // A little slack ahead of the first part, then each part straight after the one before.
    const now = ctx.currentTime;
    if (playAt < now + 0.03) playAt = now + 0.12;
    source.start(playAt);
    playAt += sound.duration;
    playing.add(source);
    source.onended = () => playing.delete(source);
    stats.voiceParts++;
    stats.voiceSamples += count;
  };
  // She stopped mid-line on the PC, or he started talking over her: what hasn't played yet goes.
  const hush = () => {
    for (const source of playing) { try { source.stop(); } catch (error) {} }
    playing.clear();
    playAt = 0;
    stats.hushes++;
  };

  // ---------- The call ----------
  const reasons = { 'not-allowed': 'Calls are off on the PC', ended: 'Call ended on the PC', busy: 'Capri is busy' };
  const over = text => {
    clearTimeout(ring);
    cancel('');
    hush();
    hearing = '';
    state = 'off';
    draw();
    if (window.CapriScreen && window.CapriScreen.mode === 'mini' && window.CapriScreen.calling) window.CapriScreen.close();
    if (text) say(text);
  };
  const start = () => {
    if (state !== 'off') return true;
    if (!supported()) return false;
    audio();
    state = 'ringing';
    them = { voice: true, ears: true };
    draw();
    if (!remote().send('call.start', {})) { over('Not connected'); return true; }
    ring = setTimeout(() => { if (state === 'ringing') over('No answer from the PC'); }, 8000);
    // The screen in the mini player, when the PC shows it.
    if (window.CapriScreen && window.CapriScreen.mode === 'off' && window.CapriScreen.mini(true)) window.CapriScreen.calling = true;
    return true;
  };
  const end = () => {
    if (state === 'off') return;
    if (line && line.n > 0) remote().send('call.cancel', { id: line.id });
    remote().send('call.stop', {});
    over('');
  };

  const said = message => {
    if (message.type === 'call.state') {
      if (message.on === true) {
        if (state === 'off') return;
        clearTimeout(ring);
        state = 'on';
        them = { voice: message.voice !== false, ears: message.ears !== false };
        draw();
      } else if (state !== 'off') over(reasons[message.reason] || 'Call ended');
    } else if (message.type === 'call.heard') {
      if (typeof message.id !== 'string') return;
      stats.heard++;
      if (message.id === hearing) hearing = '';
      draw();
      const text = typeof message.text === 'string' ? message.text.trim() : '';
      if (!text) { say('Didn\'t catch that'); return; }
      if (window.CapriApp) window.CapriApp.heard(text, message.id);
    } else if (message.type === 'call.voice') voice(message);
    else if (message.type === 'call.hush') hush();
    else if (message.type === 'error' && typeof message.re === 'string' && (message.re === hearing || (line && message.re === line.id))) {
      if (message.re === hearing) hearing = '';
      if (line && message.re === line.id) cancel('');
      draw();
      say({ 'too-long': 'Too long: keep it under 30 seconds', busy: 'Capri is busy', 'not-allowed': 'Calls are off on the PC' }[message.code] || 'She didn\'t hear that');
    }
  };

  const mount = () => {
    const app = document.getElementById('app');
    const actions = document.getElementById('actions');
    if (!app || !actions) return;
    actions.before(bar);
    const r = remote();
    if (!r) return;
    r.onMessage(said);
    // A new session, or the link going down, ends the call: the PC already has.
    r.onState(next => { if (state !== 'off' && next !== 'connected') over('Signal lost'); else if (state !== 'off' && next === 'connected') over('Call dropped: call again'); });
  };
  // Switching away ends what he was saying, not the call.
  document.addEventListener('visibilitychange', () => { if (document.hidden) cancel(''); });
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', mount, { once: true });
  else mount();

  window.CapriCall = {
    supported,
    start,
    end,
    stats: () => Object.assign({}, stats, { state, playing: playing.size }),
    get state() { return state; }
  };
})();

/* Capri Remote's call (PROTOCOL.md 8.2), like a phone call. One tap on CALL rings her and opens the mic once for the
   whole call; then he just talks. The phone finds where each thing he says starts and ends and sends it as one line of
   16 kHz call.audio parts, so her own ears on the PC hear it as if it was said to her there. Her answer comes back in
   the chat, and her real voice plays here as call.voice parts. Mute is the only talk control.
     - His lines: a noise floor that follows the room, about 300 ms from before he started so the first sound isn't cut,
       and the end of his turn after 800 ms of quiet. Clicks and blips never go on their own.
     - Her voice never starts a line, but talking over her does: while it plays, the mic listens over a bar set by how
       loud she's just been, so the echo cancelling's leftovers of her stay under it, and nothing counts in the first
       moment after her voice starts. His voice over the bar for 300 ms stops her here and goes as his next line. With
       it off (SETTINGS), the mic starts nothing while she plays, then listens over a raised floor for a moment.
     - Interrupt, and talking over her, stop her answer on a PC that lists hush too (call.hush with the message it
       answers); on an older PC only here. Her voice waits while he's talking.
     - iPhones: the sound starts in the CALL tap and again once the mic is open, a suspended or interrupted sound is
       resumed, and a mic that gives nothing is fixed (a fresh sound, or the mic again) or says so plainly.
     CapriCall.supported()  this session's PC takes calls and this browser can record
     CapriCall.start()      ring her, from a tap; false when it can't
     CapriCall.end()        hang up
     CapriCall.overHer      talking over her stops her: true or false, kept on this phone */
(() => {
  'use strict';
  const RATE = 16000;
  const FRAME = 320; // 20 ms: the step his lines are found in
  const PART = 5120; // 320 ms: one call.audio part
  const MOST = 29 * RATE; // a line ends itself just under the PC's 30 seconds
  const PRE = 15; // frames from before he started that go with a line (300 ms)
  const KEPT = 60; // frames a dropped start stays in front of a line that starts soon after (1.2 s)
  const LEAST = 12; // frames of voice before a line goes at all (240 ms)
  const BLIP = 15; // frames of quiet that drop a start that never got going (300 ms)
  const HANG = 40; // frames of quiet that end his turn (800 ms)
  const TAIL = 15; // frames of that quiet kept on the end of a line (300 ms)
  const SETTLE = 15; // frames the noise floor learns from before any line can start (300 ms)
  const OVER = 10, STAY = 6; // dB over the noise floor to start a line, and to keep it going
  const LEAST_DB = -50, LEAST_STAY_DB = -55; // and never quieter than these
  const AFTER_HER = 0.35, RAISED = 1.2, RAISE = 10; // seconds after her voice that start nothing, then RAISE dB more
  // Talking over her: frames over the bar that stop her (300 ms), and frames under it that start the count again (120 ms).
  const OVER_HER = 15, OVER_GAP = 6;
  // The bar: OVER_START dB over the usual start, and never under her own loudness of the last OVER_HOLD frames (400 ms,
  // as her voice takes a moment to come back through the mic) less OVER_UNDER dB.
  const OVER_START = 8, OVER_UNDER = 15, OVER_HOLD = 20;
  const OVER_SETTLE = 0.3; // seconds after her voice starts (or starts again after a break) that count for nothing
  const SILENT_MIC = 2000; // a live mic that gives no samples this long gets fixed
  const C = window.CapriCrypto;
  const SAVED = 'capri-remote-call';
  const SCREEN_TOOLS = new Set([
    'use_computer', 'look_at_screen', 'play_music', 'open_app', 'open_website', 'app_window', 'shortcut', 'media',
    'draw', 'desktop_icons', 'clip', 'mini_game', 'fake_crash', 'power', 'look', 'screenshot', 'zoom',
    'left_click', 'right_click', 'middle_click', 'double_click', 'triple_click', 'left_click_drag', 'mouse_move',
    'left_mouse_down', 'left_mouse_up', 'mouse_hold', 'cursor_position', 'scroll', 'type', 'key', 'hold_key', 'hold_and_press',
    'click', 'press', 'walk', 'wait_for'
  ]);
  const SCREEN_LINGER = 4000;
  const SPRITES = (window.CAPRI_CONFIG && window.CAPRI_CONFIG.sprites) || '../assets/sprites/';
  const apple = /iP(hone|od|ad)/.test(navigator.userAgent) || (/Macintosh/.test(navigator.userAgent) && navigator.maxTouchPoints > 1);
  const reduced = matchMedia('(prefers-reduced-motion: reduce)');
  const remote = () => window.CapriRemote;
  const newId = () => Date.now().toString(36) + Math.random().toString(36).slice(2, 10);
  const make = (tag, className, text) => {
    const el = document.createElement(tag);
    if (className) el.className = className;
    if (text != null) el.textContent = text;
    return el;
  };
  const icon = (...paths) => {
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('viewBox', '0 0 24 24');
    svg.setAttribute('aria-hidden', 'true');
    for (const d of paths) {
      const line = document.createElementNS('http://www.w3.org/2000/svg', 'path');
      line.setAttribute('d', d);
      svg.appendChild(line);
    }
    return svg;
  };
  const HANDSET = 'M4.2 4.4L8.6 3l2.1 5-2.6 1.9c1.2 2.6 3.2 4.6 5.8 5.8l1.9-2.6 5 2.1-1.4 4.4-1.6 1.1C10.6 20.4 3.6 13.4 3.1 6z';
  const MIC = ['M12 3.5a3 3 0 0 0-3 3v5a3 3 0 0 0 6 0v-5a3 3 0 0 0-3-3z', 'M5.5 11a6.5 6.5 0 0 0 13 0M12 17.5V21'];
  const CONE = 'M3.5 9.25h3.75L12 5v14l-4.75-4.25H3.5z';

  // The screen pin and talking over her. The old screen setting opened every call, so only version 2 keeps a pin.
  const pick = (() => {
    let saved = {};
    try { saved = JSON.parse(localStorage.getItem(SAVED)) || {}; } catch (error) {}
    return { version: 2, screen: saved.version === 2 && saved.screen === true, overHer: saved.overHer !== false };
  })();
  const keep = () => { try { localStorage.setItem(SAVED, JSON.stringify(pick)); } catch (error) {} };

  // ---------- The call screen: her face and name, how the call is going, one caption and three keys ----------
  const room = make('section', 'call-room');
  room.hidden = true;
  room.tabIndex = -1;
  room.setAttribute('role', 'dialog');
  room.setAttribute('aria-label', 'Call with Capri');
  const roomTop = make('div', 'call-top');
  const screenKey = make('button', 'glyph call-screen');
  screenKey.type = 'button';
  screenKey.setAttribute('aria-label', 'Pin your PC screen');
  screenKey.setAttribute('aria-pressed', 'false');
  screenKey.append(icon('M3 4.5h18v12H3zM8.5 20h7M12 16.5V20'));
  const shrink = make('button', 'glyph call-shrink');
  shrink.type = 'button';
  shrink.setAttribute('aria-label', 'Back to the chat: the call stays on');
  shrink.append(icon('M14 4v6h6M10 20v-6H4M14 10l6.5-6.5M10 14l-6.5 6.5'));
  roomTop.append(screenKey, shrink);
  const who = make('div', 'call-who');
  const face = make('div', 'call-face');
  const art = make('span', 'call-art');
  face.append(art);
  const name = make('p', 'call-name', 'Capri');
  who.append(face, name);
  const middle = make('div', 'call-mid');
  const statusLine = make('div', 'call-status');
  statusLine.setAttribute('role', 'status');
  const word = make('span', 'call-word', 'Calling');
  const clock = make('span', 'call-clock', '00:00');
  const separator = make('span', 'call-sep', '\u00b7');
  separator.setAttribute('aria-hidden', 'true');
  statusLine.append(word, separator, clock);
  const note = make('p', 'call-note');
  note.hidden = true;
  const pill = make('button', 'call-pill');
  pill.type = 'button';
  pill.hidden = true;
  middle.append(statusLine, note, pill);
  // The middle of the screen, where the PC screen's mini player sits during a call.
  const stage = make('div', 'call-stage');
  const captions = make('p', 'call-caps');
  const captionWords = make('span', 'cap-words');
  captions.append(captionWords);
  const pip = make('div', 'call-pip');
  pip.hidden = true;
  pip.setAttribute('aria-hidden', 'true');
  const keys = make('div', 'call-keys');
  const key = (className, label, svg) => {
    const el = make('button', 'call-key ' + className);
    el.type = 'button';
    const pad = make('span', 'call-pad');
    pad.append(svg);
    const text = make('span', 'call-label', label);
    el.append(pad, text);
    keys.append(el);
    return { el, pad, text };
  };
  const speakerKey = key('speaker', 'Speaker', icon(CONE, 'M15.5 9.2a4 4 0 0 1 0 5.6M18.2 6.6a7.6 7.6 0 0 1 0 10.8'));
  speakerKey.pad.append(icon(CONE, 'M15.5 9.5l5 5M20.5 9.5l-5 5'));
  speakerKey.el.setAttribute('aria-pressed', 'true');
  const endKey = key('end', 'End', icon(HANDSET));
  endKey.pad.append(icon('M6.5 6.5l11 11M17.5 6.5l-11 11'));
  endKey.el.setAttribute('aria-label', 'End the call');
  const muteKey = key('mute', 'Mute', icon(...MIC));
  muteKey.pad.append(icon(...MIC, 'M4.5 4.5l15 15'));
  muteKey.el.setAttribute('aria-pressed', 'false');
  room.append(roomTop, who, middle, stage, captions, keys);

  // ---------- The call bar: the call shrunk to a strip over the chat (or over the PC screen's full view) ----------
  const bar = make('div', 'callbar');
  bar.hidden = true;
  bar.setAttribute('role', 'region');
  bar.setAttribute('aria-label', 'Call with Capri');
  const back = make('button', 'call-back');
  back.type = 'button';
  back.setAttribute('aria-label', 'Open the call');
  const barWord = make('span', 'call-back-word', 'Connecting');
  const barClock = make('span', 'call-back-clock', '00:00');
  back.append(make('span', 'call-dot'), barWord, barClock);
  const barMute = make('button', 'nbtn icon call-bar-mute');
  barMute.type = 'button';
  barMute.setAttribute('aria-label', 'Mute');
  barMute.setAttribute('aria-pressed', 'false');
  barMute.append(icon(...MIC), icon(...MIC, 'M4.5 4.5l15 15'));
  const barEnd = make('button', 'nbtn hot call-bar-end', 'End');
  barEnd.type = 'button';
  barEnd.setAttribute('aria-label', 'End the call');
  bar.append(back, barMute, barEnd);

  // ---------- The call's state ----------
  let state = 'off'; // 'off', 'ringing', 'on', or 'ended' while the call screen says so
  let shown = false; // the call screen is up, rather than shrunk to the call bar
  let them = { voice: true, ears: true };
  let sound = null; // her voice's way out and the mic's way in: one AudioContext a call
  let mic = null;
  let muted = false, speakerOn = true, calling = false;
  let since = 0, ended = null, micAsked = 0;
  let ring = 0, tick = 0, closeTimer = 0;
  let waiting = '', waitSince = 0, waitUntil = 0; // his last line, while she thinks about it
  let trouble = ''; // 'sound' (the phone paused it), 'mic' (no samples) or 'gone' (the mic stopped)
  let flashText = '', flashUntil = 0;
  let you = { text: '', faint: false };
  let her = { key: '', text: '' };
  let first = true;
  const screenTools = new Set(), closedTools = new Set();
  let screenTimer = 0, screenUntil = 0;
  // overHer: times talking over her stopped her, overMs how long the last one took from his first sound over the bar,
  // echoes starts over the bar that came to nothing, overPeak the most a frame got over the bar while she played (dB),
  // interrupts the call.hush messages sent.
  const stats = { lines: 0, parts: 0, audioBytes: 0, blips: 0, heard: 0, voiceParts: 0, voiceSamples: 0, held: 0, skipped: 0, hushes: 0, frames: 0, fixes: 0, overHer: 0, overMs: 0, echoes: 0, overPeak: -100, interrupts: 0, herDb: -100, barDb: -100 };

  // Finding his lines: 'idle' between them, 'maybe' when something started, 'talking' once a line goes. over: the start
  // came while her voice played, so it's held to the bar for talking over her; overAt: when.
  const gate = { mode: 'idle', floor: -70, frames: 0, level: -100, ring: [], keep: PRE, pending: [], voiced: 0, quiet: 0, tail: [], line: null, over: false, overAt: 0, stopped: false };

  // Her voice: what's playing, where the next part starts, parts held while he talks or until his last line is heard, and
  // when her voice last started after a break (in the sound's time).
  const playing = new Set();
  let playAt = 0, held = [], holdFor = '', holdUntil = 0, skipping = false, lastVoiceAt = 0, herStart = -10;
  // Her loudness at the speaker for each of the last OVER_HOLD mic frames.
  const herLoud = [];

  const supported = () => {
    const r = remote();
    return !!(r && r.state === 'connected' && Array.isArray(r.info.features) && r.info.features.includes('call') && C && (window.AudioContext || window.webkitAudioContext) && navigator.mediaDevices && navigator.mediaDevices.getUserMedia);
  };
  const live = () => state === 'ringing' || state === 'on';
  const fullScreen = () => live() && window.CapriScreen && window.CapriScreen.mode === 'full';
  const tell = text => {
    if (shown && state !== 'off') { flashText = text; flashUntil = performance.now() + 4000; draw(); }
    else if (window.CapriApp) window.CapriApp.say(text);
  };

  // ---------- Sound: made in the CALL tap, as iPhones need ----------
  const resume = context => {
    try {
      const resuming = context.resume();
      if (resuming && resuming.catch) resuming.catch(() => {});
    } catch (error) {}
  };
  const openSound = () => {
    const Context = window.AudioContext || window.webkitAudioContext;
    const context = new Context();
    resume(context);
    // Her voice: in, a meter for her mouth, a gentle leveller that brings her up to a phone call's loudness, the speaker.
    const voiceIn = context.createGain();
    const meter = context.createAnalyser();
    meter.fftSize = 512;
    const level = context.createDynamicsCompressor();
    level.threshold.value = -20;
    level.knee.value = 10;
    level.ratio.value = 4;
    level.attack.value = 0.003;
    level.release.value = 0.25;
    const speaker = context.createGain();
    speaker.gain.value = speakerOn ? 1 : 0;
    // How loud she is as she goes to the speaker, for the bar he talks over her by.
    const out = context.createAnalyser();
    out.fftSize = 1024;
    voiceIn.connect(meter);
    voiceIn.connect(level);
    level.connect(out);
    level.connect(speaker);
    speaker.connect(context.destination);
    sound = { context, voiceIn, meter, out, speaker };
    playAt = -10;
    context.onstatechange = () => soundState(context);
  };
  const closeSound = () => {
    const was = sound;
    sound = null;
    playing.clear();
    playAt = 0;
    if (!was) return;
    was.context.onstatechange = null;
    try {
      const closing = was.context.close();
      if (closing && closing.catch) closing.catch(() => {});
    } catch (error) {}
  };
  // A suspended sound, or Safari's 'interrupted' one (a phone call, Siri, the page in the background, the mic switching
  // the phone to call audio), is resumed at once; one that stays stopped says so, and a tap brings it back.
  const soundState = context => {
    if (!sound || sound.context !== context || !live()) return;
    if (context.state === 'running') {
      if (trouble === 'sound') trouble = '';
      draw();
      return;
    }
    if (context.state === 'closed') return;
    resume(context);
    setTimeout(() => {
      if (sound && sound.context === context && context.state !== 'running' && context.state !== 'closed' && live() && !document.hidden) {
        trouble = 'sound';
        draw();
      }
    }, 1200);
  };
  // On iPhones, call audio starts quiet until the sound is suspended and resumed once the mic is open (WebKit bug 219424).
  const bounce = async () => {
    const context = sound && sound.context;
    if (!context || !context.suspend) return;
    try { await context.suspend(); } catch (error) {}
    if (sound && sound.context === context) { try { await context.resume(); } catch (error) {} }
  };

  // ---------- The mic: one stream for the whole call ----------
  const modules = new WeakMap();
  const askMic = () => navigator.mediaDevices.getUserMedia({ audio: { channelCount: 1, echoCancellation: true, noiseSuppression: true, autoGainControl: true } });
  const openMic = () => {
    let asking;
    micAsked = performance.now();
    try { asking = askMic(); } catch (error) { micFailed(error); return; }
    asking.then(stream => {
      if (!live()) { for (const track of stream.getTracks()) track.stop(); return; }
      useStream(stream);
    }, micFailed);
  };
  const micFailed = error => {
    if (!live()) return;
    const kind = error && error.name;
    remote().send('call.stop', {});
    over(kind === 'NotAllowedError' || kind === 'SecurityError' ? 'Microphone blocked: allow it for this site, then call again'
      : kind === 'NotFoundError' || kind === 'OverconstrainedError' ? 'No microphone found on this phone' : 'The microphone didn\'t start', false);
  };
  const useStream = async stream => {
    closeMic(true);
    const track = stream.getAudioTracks()[0];
    if (!track) { micFailed(null); return; }
    // echo: whether the phone cancels her voice's echo in the mic. Talking over her needs it.
    let settings = {};
    try { settings = (track.getSettings && track.getSettings()) || {}; } catch (error) {}
    const current = { stream, track, wired: false, wiredAt: 0, last: 0, samples: 0, fixed: 0, paused: false, graph: null, echo: settings.echoCancellation !== false };
    mic = current;
    track.enabled = !muted;
    track.onended = () => { if (mic === current && live()) { trouble = 'gone'; draw(); } };
    track.onmute = () => { if (mic === current) { current.paused = true; draw(); } };
    track.onunmute = () => { if (mic === current) { current.paused = false; current.last = performance.now(); draw(); } };
    await wire(current);
    if (apple && mic === current) await bounce();
  };
  // Box-filtered down to 16 kHz from the sound's own rate.
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
  const wire = async current => {
    const here = sound;
    if (!here || mic !== current) return;
    const context = here.context;
    resume(context);
    const source = context.createMediaStreamSource(current.stream);
    const down = resampler(context.sampleRate);
    let node = null;
    if (context.audioWorklet && window.AudioWorkletNode) {
      if (!modules.has(context)) modules.set(context, context.audioWorklet.addModule('mic-worklet.js'));
      try {
        await modules.get(context);
        if (mic !== current || sound !== here) return;
        node = new AudioWorkletNode(context, 'capri-mic');
        node.port.onmessage = event => take(current, down(event.data));
      } catch (error) { node = null; }
    }
    if (mic !== current || sound !== here) return;
    if (!node) {
      node = context.createScriptProcessor(2048, 1, 1);
      node.onaudioprocess = event => take(current, down(event.inputBuffer.getChannelData(0)));
    }
    // A muted path to the speakers keeps the node running in every browser.
    const sink = context.createGain();
    sink.gain.value = 0;
    source.connect(node);
    node.connect(sink);
    sink.connect(context.destination);
    current.graph = { source, node, sink };
    current.wired = true;
    current.wiredAt = performance.now();
    draw();
  };
  const unwire = current => {
    const graph = current && current.graph;
    if (!graph) return;
    current.graph = null;
    current.wired = false;
    try { graph.source.disconnect(); graph.node.disconnect(); graph.sink.disconnect(); } catch (error) {}
    if (graph.node.port) graph.node.port.onmessage = null;
    graph.node.onaudioprocess = null;
  };
  const closeMic = stopTracks => {
    const current = mic;
    mic = null;
    frameUsed = 0;
    if (!current) return;
    unwire(current);
    current.track.onended = current.track.onmute = current.track.onunmute = null;
    if (stopTracks) for (const track of current.stream.getTracks()) track.stop();
  };
  // Fixing: a stopped mic opens again, a stopped sound resumes, and a running sound that hears nothing is made afresh
  // with the same mic. From a tap it all happens inside it.
  const fix = () => {
    stats.fixes++;
    trouble = '';
    if (!mic || mic.track.readyState !== 'live') { closeMic(true); openMic(); draw(); return; }
    mic.last = performance.now();
    if (sound && sound.context.state !== 'running') { resume(sound.context); draw(); return; }
    const stream = mic.stream;
    closeMic(false);
    hush();
    closeSound();
    openSound();
    useStream(stream);
    draw();
  };

  // ---------- His lines ----------
  const frameIn = new Float32Array(FRAME);
  let frameUsed = 0;
  const take = (current, floats) => {
    if (mic !== current) return;
    current.last = performance.now();
    current.samples += floats.length;
    if (trouble === 'mic') { trouble = ''; draw(); }
    for (let i = 0; i < floats.length; i++) {
      frameIn[frameUsed++] = floats[i];
      if (frameUsed === FRAME) {
        frameUsed = 0;
        const pcm = new Int16Array(FRAME);
        let sum = 0;
        for (let j = 0; j < FRAME; j++) {
          const value = Math.max(-1, Math.min(1, frameIn[j]));
          sum += value * value;
          pcm[j] = Math.round(value * 32767);
        }
        stats.frames++;
        step(pcm, 10 * Math.log10(sum / FRAME + 1e-12));
      }
    }
  };
  const hearing = () => state === 'on' && !!mic && mic.wired && !mic.paused && !muted && them.ears && !document.hidden && !trouble;
  // Where her voice is for the mic: 'on' while it plays from the speaker and a moment after, 'raised' a little after
  // that, '' otherwise. With her speaker off nothing of hers reaches the mic.
  const herGate = () => {
    if (!sound || !speakerOn) return '';
    if (playing.size) return 'on';
    const now = sound.context.currentTime;
    if (now < playAt + AFTER_HER) return 'on';
    if (now < playAt + AFTER_HER + RAISED) return 'raised';
    return '';
  };
  // How loud her voice is at the speaker just now, in dB, or -100 while she's quiet or the speaker is off.
  const loudness = new Float32Array(1024);
  const herOut = () => {
    if (!sound || !speakerOn || !playing.size || !sound.out.getFloatTimeDomainData) return -100;
    sound.out.getFloatTimeDomainData(loudness);
    let sum = 0;
    for (let i = 0; i < loudness.length; i++) sum += loudness[i] * loudness[i];
    return Math.max(-100, 10 * Math.log10(sum / loudness.length + 1e-12));
  };
  // Talking over her needs it on and the phone cancelling her voice's echo in the mic.
  const overOn = () => pick.overHer && !!mic && mic.echo;
  // The first moment after her voice starts, as it comes out of the speaker, while the echo cancelling catches up.
  const settlingNow = () => !!sound && sound.context.currentTime < herStart + OVER_SETTLE + (sound.context.outputLatency || sound.context.baseLatency || 0);
  const step = (pcm, db) => {
    gate.level = db;
    herLoud.push(herOut());
    while (herLoud.length > OVER_HOLD) herLoud.shift();
    if (!hearing()) {
      drop();
      gate.ring.length = 0;
      gate.frames = 0;
      return;
    }
    gate.frames++;
    const herNow = herGate();
    // The noise floor: learnt fast from the first frames, then it drops fast and climbs slowly, only from the room, not
    // from her. While he talks it still creeps up, so a steady noise ends a line rather than running on.
    let pull = 0;
    if (gate.frames <= SETTLE) pull = gate.frames === 1 ? 1 : 0.3;
    else if (gate.mode === 'idle' && !herNow) pull = db < gate.floor ? 0.25 : 0.015;
    else if (gate.mode !== 'idle' && db > gate.floor) pull = 0.002;
    gate.floor = Math.max(-90, Math.min(-25, gate.floor + (db - gate.floor) * pull));
    // A line that stopped her keeps the usual bar: she isn't playing any more.
    const raise = herNow === 'raised' && !gate.stopped ? RAISE : 0;
    const start = Math.max(gate.floor + OVER, LEAST_DB) + raise;
    const stay = Math.max(gate.floor + STAY, LEAST_STAY_DB) + raise;
    // While her voice plays, talking over her has to clear its own bar, and the first moment of her voice counts for nothing.
    const over = herNow === 'on' && overOn();
    const bar = over ? Math.max(start + OVER_START, Math.max(...herLoud) - OVER_UNDER) : start;
    const settling = over && settlingNow();
    stats.herDb = Math.round(Math.max(...herLoud));
    stats.barDb = Math.round(bar);
    if (over && !settling && gate.mode !== 'talking') stats.overPeak = Math.max(stats.overPeak, Math.round(db - bar));
    if (gate.mode === 'idle') {
      if ((herNow !== 'on' || over) && !settling && gate.frames > SETTLE && db > bar) {
        gate.mode = 'maybe';
        gate.over = over;
        gate.overAt = performance.now();
        gate.pending = gate.ring;
        gate.ring = [];
        gate.keep = PRE;
        gate.pending.push(pcm);
        gate.voiced = 1;
        gate.quiet = 0;
        draw();
        return;
      }
      gate.ring.push(pcm);
      while (gate.ring.length > Math.max(PRE, gate.keep)) gate.ring.shift();
      if (gate.keep > PRE) gate.keep--;
      return;
    }
    const voiced = db > stay;
    if (gate.mode === 'maybe') {
      gate.pending.push(pcm);
      // She stopped by herself while it started: from here it's an ordinary start.
      if (gate.over && !over) gate.over = false;
      if (gate.over) {
        if (!settling) {
          if (db > bar) { gate.voiced++; gate.quiet = 0; } else gate.quiet++;
        }
        if (gate.voiced >= OVER_HER) { commit(); return; }
        if (gate.quiet < OVER_GAP) return;
        // Her own voice back through the mic, or a sound under the bar: nothing goes, and she plays on.
        stats.echoes++;
      } else {
        if (voiced) { gate.voiced++; gate.quiet = 0; } else gate.quiet++;
        if (gate.voiced >= LEAST) { commit(); return; }
        if (gate.quiet < BLIP) return;
        // A click, a blip or one short sound: nothing goes, but it stays in front of a line that starts soon after.
        stats.blips++;
      }
      const kept = gate.pending.slice(-KEPT);
      idle();
      gate.ring = kept;
      gate.keep = kept.length;
      draw();
      return;
    }
    if (voiced) {
      for (const quiet of gate.tail) add(quiet);
      gate.tail = [];
      gate.quiet = 0;
      add(pcm);
      if (gate.line && gate.line.total >= MOST) finish(true);
      return;
    }
    gate.quiet++;
    gate.tail.push(pcm);
    if (gate.quiet >= HANG) {
      for (const quiet of gate.tail.slice(0, TAIL)) add(quiet);
      gate.tail = [];
      finish(false);
    }
  };
  const idle = () => {
    gate.mode = 'idle';
    gate.pending = [];
    gate.voiced = gate.quiet = 0;
    gate.tail = [];
    gate.over = gate.stopped = false;
    if (!holdFor && held.length) release();
  };
  // A line goes. Started over her voice, it stops her first, as does any line while she plays with her speaker off.
  const commit = () => {
    if (gate.over || (pick.overHer && speaking())) {
      stats.overHer++;
      stats.overMs = Math.round(performance.now() - gate.overAt);
      gate.stopped = true;
      interrupt();
    }
    gate.over = false;
    gate.line = { id: newId(), n: 0, buffer: new Int16Array(PART), used: 0, total: 0 };
    gate.mode = 'talking';
    gate.quiet = 0;
    gate.tail = [];
    const pending = gate.pending;
    gate.pending = [];
    for (const frame of pending) add(frame);
    draw();
  };
  const encode = samples => {
    const bytes = new Uint8Array(samples.length * 2);
    const view = new DataView(bytes.buffer);
    for (let i = 0; i < samples.length; i++) view.setInt16(i * 2, samples[i], true);
    return C.b64u(bytes);
  };
  const sendPart = (line, samples, end) => {
    const message = { id: line.id, n: line.n++, rate: RATE, pcm: encode(samples) };
    if (end) message.end = true;
    stats.parts++;
    stats.audioBytes += samples.length * 2;
    return remote().send('call.audio', message);
  };
  const add = pcm => {
    const line = gate.line;
    if (!line) return;
    let at = 0;
    while (at < pcm.length) {
      const count = Math.min(PART - line.used, pcm.length - at);
      line.buffer.set(pcm.subarray(at, at + count), line.used);
      line.used += count;
      line.total += count;
      at += count;
      if (line.used === PART) {
        // A part that can't go means the link went: the call ends with it.
        if (!sendPart(line, line.buffer, false)) { gate.line = null; idle(); return; }
        line.buffer = new Int16Array(PART);
        line.used = 0;
      }
    }
  };
  // His turn ended: the rest goes with end, and she thinks. A line that ran to the PC's limit goes on as a new one.
  const finish = more => {
    const line = gate.line;
    if (!line) return;
    gate.line = null;
    if (sendPart(line, line.buffer.subarray(0, line.used), true)) {
      stats.lines++;
      first = false;
      waiting = line.id;
      waitSince = performance.now();
      waitUntil = 0;
      holdFor = line.id;
      holdUntil = performance.now() + 4000;
    }
    if (more) {
      gate.line = { id: newId(), n: 0, buffer: new Int16Array(PART), used: 0, total: 0 };
      gate.quiet = 0;
      gate.tail = [];
    } else idle();
    draw();
  };
  // Muted, switched away or hung up mid-line: the PC drops what came of it.
  const drop = () => {
    const line = gate.line;
    gate.line = null;
    if (line && line.n > 0 && live()) remote().send('call.cancel', { id: line.id });
    if (gate.mode !== 'idle') { idle(); draw(); }
  };

  // ---------- Her voice ----------
  const voice = message => {
    if (state !== 'on' || !sound || typeof message.pcm !== 'string' || !Number.isInteger(message.rate) || message.rate < 8000 || message.rate > 48000) return;
    let bytes;
    try { bytes = C.fromB64u(message.pcm); } catch (error) { return; }
    const count = bytes.length >> 1;
    if (!count) return;
    stats.voiceParts++;
    stats.voiceSamples += count;
    // After Interrupt, the rest of what she was saying is dropped as it comes; what she says after a pause plays.
    const now = performance.now();
    const gap = now - lastVoiceAt;
    lastVoiceAt = now;
    if (skipping && gap < 1000) { stats.skipped++; return; }
    skipping = false;
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    const samples = new Float32Array(count);
    for (let i = 0; i < count; i++) samples[i] = view.getInt16(i * 2, true) / 32768;
    const part = { samples, rate: message.rate };
    // While he talks, or until the PC has heard his last line (which stops what she was saying), her voice waits.
    if (gate.mode !== 'idle' || holdFor) {
      held.push(part);
      stats.held++;
      draw();
      return;
    }
    play(part);
  };
  const play = part => {
    const context = sound.context;
    let buffer;
    try {
      buffer = context.createBuffer(1, part.samples.length, part.rate);
      buffer.getChannelData(0).set(part.samples);
    } catch (error) {
      // Older Safari makes no buffers under 22.05 kHz: her voice at the sound's own rate instead.
      const ratio = context.sampleRate / part.rate;
      buffer = context.createBuffer(1, Math.max(1, Math.floor(part.samples.length * ratio)), context.sampleRate);
      const out = buffer.getChannelData(0);
      for (let i = 0; i < out.length; i++) {
        const at = i / ratio, low = Math.floor(at), high = Math.min(part.samples.length - 1, low + 1);
        out[i] = part.samples[low] + (part.samples[high] - part.samples[low]) * (at - low);
      }
    }
    const source = context.createBufferSource();
    source.buffer = buffer;
    source.connect(sound.voiceIn);
    // A little slack ahead of the first part, then each part straight after the one before.
    const now = context.currentTime;
    if (playAt < now + 0.03) {
      playAt = now + 0.12;
      herStart = playAt;
    }
    source.start(playAt);
    playAt += buffer.duration;
    playing.add(source);
    source.onended = () => {
      playing.delete(source);
      if (!playing.size) draw();
    };
    waiting = '';
    draw();
  };
  const release = () => {
    const parts = held;
    held = [];
    if (state === 'on' && sound) for (const part of parts) play(part);
  };
  // She stopped mid-line on the PC, or he interrupted her: what hasn't played yet goes.
  const hush = () => {
    for (const source of playing) { try { source.stop(); } catch (error) {} }
    playing.clear();
    held = [];
    if (sound) playAt = sound.context.currentTime;
    stats.hushes++;
  };
  // Interrupt, or talking over her: what hasn't played goes here and the rest of what she was saying is dropped as it
  // comes. A PC that lists hush stops her answer there too (her.key is the message it answers), and its own call.hush
  // ends the dropping.
  const pcHushes = () => {
    const r = remote();
    return !!(r && r.info && Array.isArray(r.info.features) && r.info.features.includes('hush'));
  };
  const interrupt = () => {
    hush();
    skipping = true;
    lastVoiceAt = performance.now();
    if (pcHushes() && her.key && remote().send('call.hush', { re: her.key })) stats.interrupts++;
    draw();
  };
  const speaking = () => playing.size > 0;

  // ---------- Her face: her idle strip, cropped to her head, with her mouth moving to her real voice ----------
  const CELL_W = 320, CELL_H = 512, CROP = { x: 94, y: 82, size: 152 };
  const sheets = { idle: { file: 'idle.webp', count: 16 }, blink: { file: 'blink.webp', count: 4 }, mouth: { file: 'mouth.webp', count: 5 } };
  const loaded = new Set();
  let asked = false;
  const loadFace = () => {
    if (asked) return;
    asked = true;
    for (const [sheet, about] of Object.entries(sheets)) {
      const image = new Image();
      image.decoding = 'async';
      image.src = SPRITES + about.file;
      (image.decode ? image.decode() : new Promise((resolve, reject) => { image.onload = resolve; image.onerror = reject; }))
        .then(() => { loaded.add(sheet); about.image = image; painted = ''; }, () => {});
    }
  };
  const meterData = new Float32Array(512);
  let raf = 0, lastPaint = 0, painted = '', faceSize = 0, loudest = 0.05, mouthStep = 0, blinkRound = -1, blinking = false;
  const herLevel = () => {
    if (!sound || !playing.size) return 0;
    if (sound.meter.getFloatTimeDomainData) sound.meter.getFloatTimeDomainData(meterData);
    else return 0;
    let sum = 0;
    for (let i = 0; i < meterData.length; i++) sum += meterData[i] * meterData[i];
    return Math.sqrt(sum / meterData.length);
  };
  // Her mouth from how loud she is, as her desktop's speaker shapes it (Speaker.Write in app/Ears.cs).
  const mouthFor = level => {
    loudest = Math.max(loudest * 0.995, level);
    if (level <= Math.max(0.004, loudest * 0.08)) return -1;
    mouthStep++;
    if (level < loudest * 0.3) return mouthStep % 3 === 0 ? 0 : 1;
    if (level < loudest * 0.55) return mouthStep % 2 ? 2 : 1;
    return level < loudest * 0.85 ? 3 : 4;
  };
  const paintFace = time => {
    const level = herLevel();
    const glow = reduced.matches ? '0' : Math.min(1, level * 5).toFixed(3);
    room.style.setProperty('--her', glow);
    pip.style.setProperty('--her', glow);
    room.style.setProperty('--you', gate.mode === 'idle' ? '0' : Math.max(0, Math.min(1, (gate.level - gate.floor) / 30)).toFixed(3));
    if (!loaded.has('idle')) return;
    faceSize = art.clientWidth;
    if (!faceSize) return;
    const still = reduced.matches;
    const index = still ? 0 : Math.floor(time / 125);
    const cell = index % sheets.idle.count;
    const round = Math.floor(index / sheets.idle.count);
    if (round !== blinkRound) { blinkRound = round; blinking = !still && loaded.has('blink') && Math.random() < 0.55; }
    let sheet = 'idle', at = cell;
    if (blinking && cell >= 9 && cell < 13) { sheet = 'blink'; at = cell - 9; }
    const mouth = !still && level > 0 && loaded.has('mouth') ? mouthFor(level) : -1;
    const look = sheet + at + ':' + mouth + ':' + faceSize;
    if (look === painted) return;
    painted = look;
    const k = faceSize / CROP.size;
    const px = value => (value * k).toFixed(2) + 'px';
    const layer = (name, n) => ({ image: 'url("' + SPRITES + sheets[name].file + '")', size: px(sheets[name].count * CELL_W) + ' ' + px(CELL_H), spot: px(-(n * CELL_W + CROP.x)) + ' ' + px(-CROP.y) });
    const layers = mouth >= 0 ? [layer('mouth', mouth), layer(sheet, at)] : [layer(sheet, at)];
    art.style.backgroundImage = layers.map(one => one.image).join(', ');
    art.style.backgroundSize = layers.map(one => one.size).join(', ');
    art.style.backgroundPosition = layers.map(one => one.spot).join(', ');
  };
  const animate = () => {
    if (raf) return;
    const frame = time => {
      if ((!shown && !fullScreen()) || state === 'off') { raf = 0; return; }
      raf = requestAnimationFrame(frame);
      if (time - lastPaint < 60) return;
      lastPaint = time;
      paintFace(time);
    };
    raf = requestAnimationFrame(frame);
  };
  addEventListener('resize', () => { faceSize = 0; painted = ''; capText = ''; });

  // ---------- Drawing ----------
  const twoDigits = n => (n < 10 ? '0' : '') + n;
  const timeText = () => {
    const ms = state === 'ended' ? (ended ? ended.length : 0) : since ? Date.now() - since : 0;
    const total = Math.floor(ms / 1000), hours = Math.floor(total / 3600), minutes = Math.floor(total / 60) % 60, seconds = total % 60;
    return (hours ? hours + ':' : '') + twoDigits(minutes) + ':' + twoDigits(seconds);
  };
  const flashOr = text => (flashText && performance.now() < flashUntil ? flashText : text);
  // What the call screen says: its status, a short note under it, and the one button that helps right now.
  const status = () => {
    if (state === 'ended') {
      const failed = !ended.byHim && !ended.length;
      return { key: 'ended', word: 'Call ended', note: ended.why || (failed ? 'Call failed' : ''), pill: ended.byHim ? '' : 'Call again' };
    }
    if (state === 'ringing' || !mic || !mic.wired) {
      const asking = !mic && performance.now() - micAsked > 1500;
      return { key: 'connecting', word: 'Calling', note: asking ? 'Allow the microphone to talk to her' : state === 'ringing' ? 'Ringing your PC' : 'Opening your mic' };
    }
    if (trouble === 'gone') return { key: 'trouble', word: 'Mic stopped', note: 'Your phone stopped the mic', pill: 'Turn it back on' };
    if (trouble === 'sound') return { key: 'trouble', word: 'Sound paused', note: 'Your phone paused the call\'s sound', pill: 'Turn it back on' };
    if (trouble === 'mic') return { key: 'trouble', word: 'No sound from your mic', note: 'Tap Fix, or hang up and call again', pill: 'Fix' };
    if (mic.paused) return { key: 'muted', word: 'Mic paused', note: 'Your phone paused the mic' };
    if (muted) return { key: 'muted', word: 'Muted', note: flashOr('She can\'t hear you') };
    if (!them.ears) return { key: 'muted', word: 'Can\'t hear you', note: 'Her ears are off on your PC' };
    if (gate.mode === 'talking') return { key: 'hearing', word: 'Listening', note: flashOr('') };
    if (speaking()) return { key: 'talking', word: 'Speaking', note: flashOr(speakerOn ? '' : 'Your speaker is off: her words are below'), pill: speakerOn ? 'Interrupt' : '' };
    if (waiting) return { key: 'thinking', word: 'Thinking', note: flashOr('') };
    return { key: 'listening', word: 'Listening', note: flashOr(!them.voice ? 'Her voice is off on your PC: she answers in words' : first ? 'Just talk. She answers when you pause' : '') };
  };
  const put = (el, text) => { if (el.textContent !== text) el.textContent = text; };
  // The caption's one line: the whole text while it fits, else only its newest words, from the first word that lets them
  // fit, with the start faded out (call.css). The same text growing as her reply comes in only ever moves on from the
  // word it had got to, so it measures once or twice a part rather than searching again.
  let capText = '', capFrom = 0;
  const caption = text => {
    if (text === capText) return;
    if (!capText || !text.startsWith(capText)) capFrom = 0;
    capText = text;
    const words = text.split(' '), room = captions.clientWidth;
    const fits = from => {
      captionWords.textContent = words.slice(from).join(' ');
      return captionWords.scrollWidth <= room + 1;
    };
    if (!room || fits(capFrom)) {
      captions.classList.toggle('long', capFrom > 0);
      return;
    }
    let low = capFrom + 1, high = words.length - 1;
    while (low < high) {
      const middle = (low + high) >> 1;
      if (fits(middle)) high = middle;
      else low = middle + 1;
    }
    capFrom = low;
    fits(capFrom);
    captions.classList.add('long');
  };
  const draw = () => {
    const app = document.getElementById('app');
    const full = fullScreen();
    const up = shown && state !== 'off' && !full;
    if (app) {
      app.classList.toggle('on-call', live());
      app.classList.toggle('call-open', up);
    }
    room.hidden = !up;
    bar.hidden = !(live() && (!shown || full));
    pip.hidden = !full;
    if (face.parentElement !== (full ? pip : who)) {
      if (full) pip.append(face);
      else who.prepend(face);
      faceSize = 0;
      painted = '';
    }
    const now = status();
    room.dataset.status = bar.dataset.status = pip.dataset.status = now.key;
    put(word, now.word);
    put(barWord, now.word);
    const time = timeText();
    put(clock, time);
    put(barClock, time);
    put(note, now.note || '');
    note.hidden = !now.note;
    put(pill, now.pill || '');
    pill.hidden = !now.pill;
    // Her latest words, or his while she listens and thinks; a caret while her ears are still hearing him.
    const talking = gate.mode === 'talking';
    const yourTurn = talking || (!speaking() && ((!!waiting && her.key !== waiting) || !her.text));
    const text = state === 'ended' || state === 'ringing' || talking ? '' : yourTurn ? you.text : her.text;
    captions.classList.toggle('live', talking);
    captions.classList.toggle('you', yourTurn);
    captions.classList.toggle('faint', yourTurn && !talking && you.faint);
    caption(text);
    speakerKey.el.setAttribute('aria-pressed', String(speakerOn));
    speakerKey.el.setAttribute('aria-label', speakerOn ? 'Speaker on: tap to turn her voice off' : 'Speaker off: tap to hear her');
    muteKey.el.setAttribute('aria-pressed', String(muted));
    muteKey.el.setAttribute('aria-label', muted ? 'Muted: tap to talk again' : 'Mute your mic');
    barMute.setAttribute('aria-pressed', String(muted));
    barMute.setAttribute('aria-label', muted ? 'Unmute' : 'Mute');
    const done = state === 'ended';
    room.classList.toggle('over', done);
    put(endKey.text, done ? 'Close' : 'End');
    endKey.el.setAttribute('aria-label', done ? 'Close the call screen' : 'End the call');
    const S = window.CapriScreen;
    screenKey.hidden = !(S && live() && S.supported());
    screenKey.setAttribute('aria-pressed', String(pick.screen));
    screenKey.setAttribute('aria-label', pick.screen ? 'Unpin and hide your PC screen' : 'Pin your PC screen');
    room.classList.toggle('has-screen', !!(live() && S && S.mode !== 'off'));
  };

  // ---------- Opening, shrinking and ending ----------
  // The PC screen's mini player, as big as the call screen's middle allows, sits in it; whenever the middle changes
  // (a note or a button under the status comes or goes), it's placed again.
  const fitMini = () => {
    const S = window.CapriScreen, app = document.getElementById('app');
    if (!shown || state === 'off' || !S || !S.within || !app) return;
    const box = stage.getBoundingClientRect();
    if (box.width && box.height) app.style.setProperty('--call-mini', Math.floor(Math.max(0, Math.min(360, box.width - 16, (box.height - 16) * 16 / 9))) + 'px');
    S.within(stage);
  };
  if (window.ResizeObserver) new ResizeObserver(fitMini).observe(stage);
  const screenRunning = () => Array.from(screenTools).some(id => !closedTools.has(id));
  const clearScreenTimer = () => {
    clearTimeout(screenTimer);
    screenTimer = screenUntil = 0;
  };
  const syncScreen = () => {
    const S = window.CapriScreen;
    if (!live() || !S || !S.supported()) return;
    const wanted = pick.screen || screenRunning() || performance.now() < screenUntil;
    if (wanted && S.mode === 'off') {
      calling = true;
      if (!S.mini(true)) calling = false;
    } else if (!wanted && calling && S.mode === 'mini') {
      calling = false;
      S.close();
    }
    draw();
    fitMini();
  };
  const lingerScreen = () => {
    clearScreenTimer();
    screenUntil = performance.now() + SCREEN_LINGER;
    screenTimer = setTimeout(() => { screenTimer = screenUntil = 0; syncScreen(); }, SCREEN_LINGER);
  };
  const closeScreen = () => {
    pick.screen = false;
    for (const id of screenTools) closedTools.add(id);
    clearScreenTimer();
    calling = false;
    keep();
    if (window.CapriScreen) window.CapriScreen.close();
    draw();
  };
  const toolSaid = message => {
    const S = window.CapriScreen;
    if (!live() || !S || !S.supported() || typeof message.id !== 'string' || !message.id || message.id.length > 64) return;
    if (message.state === 'running' && SCREEN_TOOLS.has(message.name)) {
      screenTools.add(message.id);
      if (!closedTools.has(message.id)) clearScreenTimer();
    } else if ((message.state === 'done' || message.state === 'failed') && screenTools.delete(message.id)) {
      closedTools.delete(message.id);
      if (!screenRunning() && calling && !pick.screen) lingerScreen();
    } else return;
    syncScreen();
  };
  const screenChanged = (mode, was) => {
    if (!live()) return;
    if (mode === 'off' && calling) {
      if (window.CapriScreen.supported()) { closeScreen(); return; }
      calling = false;
    }
    if (mode === 'full') calling = true;
    if (mode === 'mini' && was === 'full') {
      if (!pick.screen && !screenRunning()) lingerScreen();
      show();
      return;
    }
    draw();
    if (mode === 'full') animate();
  };
  const show = () => {
    if (fullScreen()) { window.CapriScreen.minimise(); return; }
    shown = true;
    faceSize = 0;
    painted = '';
    draw();
    fitMini();
    try { room.focus({ preventScroll: true }); } catch (error) {}
    animate();
  };
  const hide = () => {
    shown = false;
    if (state === 'ended') state = 'off';
    draw();
    if (window.CapriScreen && window.CapriScreen.within) window.CapriScreen.within(null);
  };
  const reasons = { 'not-allowed': 'Calls are off on your PC', ended: 'Your PC ended the call', busy: 'Capri is busy' };
  const over = (why, byHim) => {
    clearTimeout(ring);
    drop();
    hush();
    holdFor = waiting = '';
    skipping = false;
    trouble = '';
    closeMic(true);
    closeSound();
    ended = { why: why || '', byHim: !!byHim, length: since ? Date.now() - since : 0 };
    if (fullScreen()) shown = true;
    state = 'ended';
    clearScreenTimer();
    screenTools.clear();
    closedTools.clear();
    if (calling && window.CapriScreen && window.CapriScreen.mode !== 'off') window.CapriScreen.close();
    calling = false;
    clearInterval(tick);
    clearTimeout(closeTimer);
    // Hung up here, it closes after a moment. Dropped or failed, the screen stays to say why, with Call again.
    if (!shown) {
      state = 'off';
      if (why) tell(why);
    } else if (byHim) closeTimer = setTimeout(() => { if (state === 'ended') hide(); }, 1200);
    draw();
  };
  const start = () => {
    if (live()) { show(); return true; }
    if (!supported()) return false;
    clearTimeout(closeTimer);
    try { openSound(); } catch (error) { return false; }
    state = 'ringing';
    them = { voice: true, ears: true };
    muted = false;
    speakerOn = true;
    since = 0;
    ended = null;
    trouble = '';
    waiting = holdFor = '';
    held = [];
    skipping = false;
    you = { text: '', faint: false };
    her = { key: '', text: '' };
    first = true;
    flashText = '';
    Object.assign(gate, { mode: 'idle', floor: -70, frames: 0, ring: [], keep: PRE, pending: [], voiced: 0, quiet: 0, tail: [], line: null, over: false, overAt: 0, stopped: false });
    herLoud.length = 0;
    herStart = -10;
    clearScreenTimer();
    screenTools.clear();
    closedTools.clear();
    calling = false;
    const S = window.CapriScreen;
    if (S && S.mode !== 'off') S.close();
    loadFace();
    show();
    // hush: this phone stops her with call.hush, so a PC can send her answers a sentence at a time.
    if (!remote().send('call.start', { hush: true })) { closeSound(); over('Not connected', false); return true; }
    ring = setTimeout(() => { if (state === 'ringing') { remote().send('call.stop', {}); over('No answer from your PC', false); } }, 8000);
    // The mic opens in this same tap, once for the whole call.
    openMic();
    syncScreen();
    clearInterval(tick);
    tick = setInterval(onTick, 250);
    draw();
    return true;
  };
  const end = () => {
    if (state === 'ended') { hide(); return; }
    if (!live()) return;
    remote().send('call.stop', {});
    over('', true);
  };
  const onTick = () => {
    if (!live()) { clearInterval(tick); return; }
    const now = performance.now();
    if (holdFor && now > holdUntil) { holdFor = ''; if (gate.mode === 'idle') release(); }
    if (waiting && ((waitUntil && now > waitUntil) || now - waitSince > 60000)) waiting = '';
    // A live mic that gives nothing: the sound is fixed once by itself, then he's told.
    if (state === 'on' && mic && mic.wired && !mic.paused && !muted && !trouble && !document.hidden && now - (mic.last || mic.wiredAt) > SILENT_MIC) {
      if (mic.fixed < 1) { mic.fixed++; mic.last = now; const tries = mic.fixed; fix(); if (mic) mic.fixed = tries; }
      else trouble = 'mic';
    }
    draw();
  };

  // ---------- Keys ----------
  speakerKey.el.addEventListener('click', () => {
    speakerOn = !speakerOn;
    if (sound) sound.speaker.gain.setTargetAtTime(speakerOn ? 1 : 0, sound.context.currentTime, 0.015);
    draw();
  });
  const toggleMute = () => {
    if (state === 'ended') return;
    muted = !muted;
    if (mic) mic.track.enabled = !muted;
    if (muted) drop();
    draw();
  };
  muteKey.el.addEventListener('click', toggleMute);
  barMute.addEventListener('click', toggleMute);
  endKey.el.addEventListener('click', end);
  barEnd.addEventListener('click', end);
  shrink.addEventListener('click', hide);
  back.addEventListener('click', show);
  pill.addEventListener('click', () => {
    if (state === 'ended') { start(); return; }
    if (trouble) { fix(); return; }
    if (speaking()) interrupt();
  });
  screenKey.addEventListener('click', () => {
    const S = window.CapriScreen;
    if (!live() || !S || !S.supported()) return;
    if (pick.screen) { closeScreen(); return; }
    pick.screen = true;
    keep();
    syncScreen();
  });
  room.addEventListener('keydown', event => { if (event.key === 'Escape') { event.preventDefault(); hide(); } });

  // ---------- The PC's side ----------
  const said = message => {
    const type = message.type;
    if (type === 'call.state') {
      if (message.on === true) {
        if (!live()) return;
        clearTimeout(ring);
        if (state === 'ringing') { state = 'on'; since = Date.now(); }
        them = { voice: message.voice !== false, ears: message.ears !== false };
        draw();
      } else if (live()) over(reasons[message.reason] || 'Call ended', false);
    } else if (type === 'chat.tool') toolSaid(message);
    else if (type === 'call.heard') {
      if (typeof message.id !== 'string' || state !== 'on') return;
      stats.heard++;
      const text = typeof message.text === 'string' ? message.text.trim() : '';
      // His last line was heard: words stop what she was saying on the PC (her hush follows), so held voice goes;
      // nothing heard leaves her talking, so it plays.
      if (message.id === holdFor) {
        holdFor = '';
        if (text) held = [];
        else if (gate.mode === 'idle') release();
      }
      if (!text) {
        if (message.id === waiting) waiting = '';
        you = { text: 'Didn\'t catch that', faint: true };
      } else {
        you = { text, faint: false };
        if (window.CapriApp) window.CapriApp.heard(text, message.id);
      }
      draw();
    } else if (type === 'call.voice') voice(message);
    else if (type === 'call.hush') {
      hush();
      skipping = false;
      draw();
    } else if (type === 'chat.reply') {
      if (state !== 'on') return;
      const replyTo = typeof message.re === 'string' ? message.re : '';
      const id = replyTo || (typeof message.id === 'string' ? message.id : '');
      if (id !== her.key) her = { key: id, text: '' };
      if (typeof message.text === 'string' && message.text) her.text = (her.text + message.text).slice(0, 4000);
      if (message.done && replyTo && replyTo === waiting) {
        // Her words are in; her voice follows, unless it's off on the PC or she had nothing to say.
        if (!her.text.trim() || !them.voice) waiting = '';
        else waitUntil = performance.now() + 8000;
      }
      draw();
    } else if (type === 'error' && typeof message.re === 'string' && state === 'on') {
      const line = gate.line && message.re === gate.line.id;
      if (!line && message.re !== waiting && message.re !== holdFor) return;
      if (line) { gate.line = null; idle(); }
      if (message.re === waiting) waiting = '';
      if (message.re === holdFor) { holdFor = ''; if (gate.mode === 'idle') release(); }
      tell({ 'too-long': 'Too long for her: keep it under 30 seconds', busy: 'Capri is busy', 'not-allowed': 'Calls are off on your PC' }[message.code] || 'She didn\'t hear that: say it again');
    }
  };

  const mount = () => {
    const app = document.getElementById('app');
    const actions = document.getElementById('actions');
    if (!app || !actions) return;
    actions.before(bar);
    app.append(room, pip);
    if (window.CapriScreen && window.CapriScreen.onMode) window.CapriScreen.onMode(screenChanged);
    const r = remote();
    if (!r) return;
    r.onMessage(said);
    // A new session, or the link going down, ends the call: the PC already has.
    r.onState(next => {
      if (!live()) return;
      over(next === 'connected' ? 'The call dropped: call again' : 'Signal lost', false);
    });
  };
  // Switching away ends what he was saying, not the call. Back again, the sound starts once more (iPhones pause it).
  document.addEventListener('visibilitychange', () => {
    if (!live()) return;
    if (document.hidden) { drop(); return; }
    if (sound) { if (apple) bounce(); else resume(sound.context); }
    if (mic) mic.last = performance.now();
    draw();
  });
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', mount, { once: true });
  else mount();

  window.CapriCall = {
    supported,
    start,
    end,
    get overHer() { return pick.overHer; },
    set overHer(on) {
      pick.overHer = !!on;
      keep();
    },
    stats: () => Object.assign({}, stats, {
      state,
      status: status().key,
      shown,
      muted,
      speaker: speakerOn,
      playing: playing.size,
      waiting: !!waiting,
      mode: gate.mode,
      floor: Math.round(gate.floor),
      level: Math.round(gate.level),
      micLive: !!(mic && mic.track.readyState === 'live'),
      echo: !!(mic && mic.echo),
      overOn: pick.overHer,
      sound: sound ? sound.context.state : 'none',
      trouble
    }),
    get state() { return state; }
  };
})();

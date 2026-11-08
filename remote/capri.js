/* Capri on the message bar.
   Her clips and how they play are the website's (site/home.js): the same strips, frame counts, rates, cells across, fits,
   blink swaps, glitch timing and decode-before-use. Where she sits, lies and how she flies come from the desktop app
   (app/Capri.cs): her hip on a ledge (SitX 0.30, SitY 0.79), her lying scale and hip (LieScale 0.85, LieRight), and her
   throw (gravity 11 sizes a second squared, air drag 0.6, walls 0.55, floor 0.42, floor grip 0.72, tumble and righting). */
(() => {
  'use strict';
  const clamp = (value, low, high) => Math.max(low, Math.min(high, value));
  const pick = list => list[Math.floor(Math.random() * list.length)];
  const config = window.CAPRI_CONFIG || {};
  const shared = config.sprites || '../assets/sprites/';
  const own = config.ownSprites || 'sprites/';

  // ---------- Her clips, from home.js's table: frames, fps, cells across, where the cell sits in her square, file type ----------
  // perch_talk is her artist's talking perch from the app's own strips, frame for frame with the plain perch.
  const table = {};
  for (const [name, count, fps, across = count, fit = 'stand', type = 'webp', from = shared] of [
    ['idle', 16, 8], ['blink', 4, 8], ['blink_glitch', 4, 8], ['grin', 16, 8], ['glitch', 10, 15], ['dangle', 24, 10], ['sleep', 16, 6], ['wake', 13, 15], ['mouth', 5, 12],
    ['perch', 60, 30, 10, 'perch'], ['perch_blink', 8, 30, 8, 'perch'], ['perch_blink_glitch', 8, 30, 8, 'perch'], ['perch_glitch', 10, 15, 10, 'perch'],
    ['perch_music', 60, 30, 60, 'music', 'png'], ['perch_talk', 60, 30, 60, 'music', 'png', own],
    ['laydown', 60, 30, 60, 'lie', 'png'], ['laydown_talk', 60, 30, 60, 'lie', 'png'],
    ['laydown_blink', 8, 30, 8, 'lie', 'png'], ['laydown_sleep', 60, 30, 60, 'lie', 'png'],
    ['laydown_glitch', 10, 15, 10, 'wide', 'png'], ['laydown_leave', 12, 15, 12, 'wide', 'png'],
    ['arrive', 14, 15, 14, 'stand', 'png'], ['leave', 12, 15, 12, 'stand', 'png'],
    ['perch_arrive', 14, 15, 14, 'square', 'png'], ['perch_leave', 12, 15, 12, 'square', 'png']
  ]) table[name] = { count, fps, across, rows: Math.ceil(count / across), fit, url: from + name + '.' + type };

  // ---------- Where she is in her square, as shares of her size (Capri.cs) ----------
  const SIT_X = 0.30, SIT_Y = 0.79, LIE_X = 0.5 + (0.806 - 0.5) * 0.85, LIE_BACK = 0.672, STAND_Y = 0.995, STAND_MID = 0.503;
  // Her dock: the stretch of a ledge she sits or lies on, from its left end (the bar) or its right end (the cards). Her
  // legs hang over the ledge's far side, out of sight, so nothing under her needs to make room for them.
  const DOCK = 0.74, PERCH_HIP = 0.302, LIE_HIP = 0.582, STAND_AT = 0.37;
  // Her throw, from the app, in sizes: gravity, air drag, bounces, and how fast counts as a throw on a phone.
  const GRAVITY = 11, AIR = 0.6, WALL = 0.55, FLOOR = 0.42, GRIP = 0.72, THROW_FROM = 380, THROW_MOST = 4200;
  // After this long with nothing going on she lies down on the bar, and later she sleeps there.
  const LIE_AFTER = 25000, SLEEP_AFTER = 85000;

  const lines = {
    throw: ['WHEEEE!', 'Put me DOWN!', "Choom, I'm not a frisbee!", 'AAAAAA!', 'Not the hair!'],
    warn: ['Okay, rude.', 'Again?! One more and we\'re fighting.', 'Do that again. I dare you.'],
    dizzy: ['Ugh. Which way is up?', "Everything's spinning, choom.", "My gyro's cooked."],
    landed: ['Stuck the landing.', 'Still in one piece.', 'Ten out of ten. No notes.'],
    poke: ['Hey. Felt that.', 'Poke received.', 'Something on my face?', 'You rang?'],
    annoyed: 'Okay. Hands to yourself.',
    woke: ['Mm. I was resting my eyes.', "I'm up. I'm up.", 'Five more minutes. Fine.'],
    nap: 'Night night.',
    still: 'Motion is off. Still here with you.'
  };

  const reduced = matchMedia('(prefers-reduced-motion: reduce)');
  let still = reduced.matches;

  // ---------- Strips decode before use, so she never blinks out while one loads (home.js) ----------
  const ready = new Set();
  const asked = new Map();
  const kept = [];
  const fetchStrips = names => Promise.all(names.map(name => {
    if (!table[name]) return Promise.resolve(false);
    if (asked.has(name)) return asked.get(name);
    const strip = new Image();
    strip.decoding = 'async';
    strip.src = table[name].url;
    const done = (strip.decode ? strip.decode() : new Promise((resolve, reject) => { strip.onload = resolve; strip.onerror = reject; }))
      .then(() => { ready.add(name); kept.push(strip); return true; }, () => { asked.delete(name); return false; });
    asked.set(name, done);
    return done;
  }));

  // ---------- Her actor: hold a loop, play a clip once, then back to her rest (home.js) ----------
  let time = 0;
  let lastGlitch = -1e9;
  const her = { name: 'perch', rest: 'perch', base: 0, loop: true, then: null, from: 0, round: -1, blink: '', shown: '', layers: '' };
  const hold = next => { her.name = her.rest = next; her.base = 0; her.loop = true; her.then = null; her.from = time; her.round = -1; her.blink = ''; };
  const once = (next, then) => {
    if (still || !ready.has(next)) { if (then) then(); return false; }
    if (/glitch$/.test(next) && !/blink/.test(next)) {
      const now = performance.now();
      if (now - lastGlitch < 2000) { if (then) then(); return false; }
      lastGlitch = now;
    }
    her.name = next; her.base = 0; her.loop = false; her.then = then || null; her.from = time;
    return true;
  };
  const finish = () => { const then = her.then; hold(her.rest); if (then) then(); };

  // ---------- Her state on the bar ----------
  let layer = null, el = null, paint = null, hit = null;
  let s = 104, x = 0, y = 0, pose = 'perch', seatEl = null, side = 'left', ledge = null;
  let area = { w: 0, h: 0, top: 0 };
  let shown = false, gone = false, leaving = false;
  let talking = false, music = false, asleep = false, resting = false;
  let lastActive = performance.now();
  let nextGlitch = performance.now() + 14000 + Math.random() * 14000;
  let press = null, drag = null, flight = null, dizzy = null;
  const swing = { a: 0, v: 0 };
  const throwsAt = [];
  let lastTap = 0, tapTimer = 0, wokeByTap = false, pokes = [];
  let onDock = null;

  const restClip = () => {
    if (pose === 'lie') return asleep && ready.has('laydown_sleep') ? 'laydown_sleep' : 'laydown';
    if (pose === 'perch') return music && ready.has('perch_music') ? 'perch_music' : 'perch';
    if (pose === 'stand') return 'idle';
    return ready.has('dangle') ? 'dangle' : 'perch';
  };
  const settle = () => hold(restClip());

  // ---------- Painting a frame: the strip as a background, its cell picked by position (home.js) ----------
  const show = (strip, cell, mouth) => {
    const key = strip + ':' + cell + ':' + mouth;
    if (key === her.shown) return;
    her.shown = key;
    const clip = table[strip];
    const layers = mouth >= 0 ? ['mouth', strip] : [strip];
    if (her.layers !== layers.join()) {
      her.layers = layers.join();
      paint.dataset.fit = clip.fit;
      paint.style.backgroundImage = layers.map(layerName => 'url("' + table[layerName].url + '")').join(', ');
      paint.style.backgroundSize = layers.map(layerName => table[layerName].across * 100 + '% ' + table[layerName].rows * 100 + '%').join(', ');
    }
    const column = cell % clip.across;
    const row = Math.floor(cell / clip.across);
    const at = (clip.across > 1 ? column / (clip.across - 1) * 100 : 0) + '% ' + (clip.rows > 1 ? row / (clip.rows - 1) * 100 : 0) + '%';
    paint.style.backgroundPosition = (mouth >= 0 ? mouth / 4 * 100 + '% 0, ' : '') + at;
  };

  // A new round of a loop: maybe a blink (her artist's, frames 29 to 36 of the perch), and now and then a glitch.
  const newRound = () => {
    const roll = Math.random();
    her.blink = '';
    if (her.name === 'perch') her.blink = roll < 0.1 ? 'perch_blink_glitch' : roll < 0.55 ? 'perch_blink' : '';
    else if (her.name === 'laydown') her.blink = roll < 0.55 ? 'laydown_blink' : '';
    else if (her.name === 'idle') her.blink = roll < 0.1 ? 'blink_glitch' : roll < 0.55 ? 'blink' : '';
    const now = performance.now();
    if (now >= nextGlitch && !talking && !drag && !flight && !asleep && (her.name === 'perch' || her.name === 'laydown')) {
      nextGlitch = now + 14000 + Math.random() * 14000;
      return once(her.name === 'perch' ? 'perch_glitch' : 'laydown_glitch');
    }
    return false;
  };

  let mouthText = '';
  const mouthFor = (letter, step) => {
    const lower = letter.toLowerCase();
    if ('aeo'.includes(lower)) return letter !== lower ? 4 : 3;
    if ('iuy'.includes(lower)) return 2;
    if ('mbp'.includes(lower) || !/\p{L}/u.test(letter)) return 0;
    return step % 2 ? 1 : 2;
  };

  const draw = () => {
    let clip = table[her.name];
    let index = her.base + Math.floor((time - her.from) / 1000 * clip.fps);
    if (!her.loop && index >= clip.count) {
      finish();
      clip = table[her.name];
      index = her.base + Math.floor((time - her.from) / 1000 * clip.fps);
    }
    if (her.loop) {
      const round = Math.floor(index / clip.count);
      if (round !== her.round) {
        her.round = round;
        if (newRound()) return;
      }
    }
    let strip = her.name;
    let cell = index % clip.count;
    let mouth = -1;
    if (strip === 'perch' && her.blink && ready.has(her.blink) && cell >= 28 && cell < 36) { strip = her.blink; cell -= 28; }
    else if (strip === 'laydown' && her.blink && ready.has(her.blink) && cell >= 45 && cell < 53) { strip = her.blink; cell -= 45; }
    else if (strip === 'idle' && her.blink && ready.has(her.blink) && cell >= 9 && cell < 13) { strip = her.blink; cell -= 9; }
    // Talking, she's in her artist's talking frames, frame for frame with the plain ones, as on her desktop.
    if (talking && !still) {
      if (her.name === 'perch' && ready.has('perch_talk')) { strip = 'perch_talk'; cell = index % 60; }
      else if (her.name === 'laydown' && ready.has('laydown_talk')) { strip = 'laydown_talk'; cell = index % 60; }
      else if (strip === 'idle' && ready.has('mouth') && mouthText) {
        const step = Math.floor(time / 1000 * 12);
        mouth = mouthFor(mouthText[step % mouthText.length], step);
      }
    }
    if (!ready.has(strip)) return;
    show(strip, cell, mouth);
  };

  // ---------- Her seat: the element she sits on, its top edge her ledge ----------
  const dockWidth = () => Math.round(DOCK * s);
  const readSeat = () => {
    if (!seatEl || !layer) return !!ledge;
    const box = layer.getBoundingClientRect();
    const seat = seatEl.getBoundingClientRect();
    area = { w: box.width, h: box.height, top: area.top };
    if (!seat.width || !seat.height) return !!ledge;
    const left = seat.left - box.left, right = seat.right - box.left;
    const dock = dockWidth();
    ledge = { left, right, y: seat.top - box.top, dockLeft: side === 'right' ? right - dock : left };
    return true;
  };
  const seatedAt = which => {
    const dockLeft = ledge.dockLeft;
    if (which === 'lie') {
      let hip = dockLeft + LIE_HIP * s - 4;
      // On the bar her head and hair reach back past its left end; keep them on the screen.
      if (side === 'left') hip = Math.max(hip, LIE_BACK * s + 1);
      return { x: hip - LIE_X * s, y: ledge.y - SIT_Y * s };
    }
    if (which === 'stand') return { x: dockLeft + STAND_AT * s - STAND_MID * s, y: ledge.y - STAND_Y * s };
    return { x: dockLeft + PERCH_HIP * s - SIT_X * s, y: ledge.y - SIT_Y * s };
  };

  // ---------- Resting and waking ----------
  const activity = () => { lastActive = performance.now(); };
  const lieDown = () => {
    if (pose !== 'perch' || drag || flight) return;
    if (!ready.has('laydown')) { fetchStrips(['laydown', 'laydown_blink', 'laydown_sleep', 'laydown_glitch']); return; }
    // once() runs its follow-on itself when the clip can't play, so each of these swaps happens exactly once.
    once('perch_glitch', () => { if (drag || flight) return; pose = 'lie'; settle(); });
  };
  const sitUp = () => {
    asleep = false;
    if (pose !== 'lie') { if (her.loop) settle(); return; }
    once('laydown_glitch', () => { if (drag || flight) return; pose = 'perch'; settle(); });
  };
  const fallAsleep = () => {
    if (!ready.has('laydown_sleep')) { fetchStrips(['laydown_sleep']); return; }
    asleep = true;
    if (pose === 'lie' && her.loop) settle();
  };
  const behave = now => {
    if (drag || flight || gone || !her.loop) return;
    if (resting) {
      if (pose === 'perch') lieDown();
      else if (pose === 'lie' && !asleep) fallAsleep();
      return;
    }
    if (talking || music) return;
    const idle = now - lastActive;
    if (pose === 'perch' && idle > LIE_AFTER) lieDown();
    else if (pose === 'lie' && !asleep && idle > SLEEP_AFTER) fallAsleep();
  };

  // ---------- Her quip: a small box like her texts (one cut corner, a thin edge), with a tail down to her ----------
  let bubble = null, bubbleWords = null, bubbleSvg = null, bubbleUntil = 0, bubbleW = 0, bubbleH = 0, bubbleTip = -1;
  const platePath = (left, top, width, height, cut, tipX, tipY, tail) => {
    const right = left + width, bottom = top + height;
    const near = Math.max(left + 2, tipX - tail * 0.8);
    const far = Math.min(right - cut - 2, tipX + tail * 0.2);
    const points = [[left, top], [right, top], [right, bottom - cut], [right - cut, bottom]];
    if (tipY > bottom && far > near) points.push([far, bottom], [tipX, tipY], [near, bottom]);
    points.push([left, bottom]);
    return 'M' + points.map(point => point[0].toFixed(1) + ' ' + point[1].toFixed(1)).join('L') + 'Z';
  };
  const quip = text => {
    if (!bubble || gone) return;
    bubbleWords.textContent = text;
    bubble.hidden = false;
    bubbleW = bubble.offsetWidth;
    bubbleH = bubble.offsetHeight;
    bubbleTip = -1;
    bubbleUntil = performance.now() + 2200 + text.length * 30;
  };
  const headAt = () => {
    if (pose === 'lie') return { x: x + 0.27 * s, y: y + 0.5 * s };
    if (pose === 'perch') return { x: x + 0.46 * s, y: y + 0.2 * s };
    return { x: x + 0.5 * s, y: y + 0.04 * s };
  };
  const placeBubble = now => {
    if (!bubble || bubble.hidden) return;
    if (now > bubbleUntil || gone) { bubble.hidden = true; return; }
    const head = headAt();
    const tail = 12;
    const left = clamp(head.x - bubbleW * 0.3, 8, Math.max(8, area.w - bubbleW - 8));
    const top = Math.max(area.top + 6, head.y - bubbleH - tail - 4);
    const tip = clamp(head.x - left, 14, bubbleW - 17);
    if (Math.abs(tip - bubbleTip) > 0.5) {
      bubbleTip = tip;
      const w = bubbleW + 10, h = bubbleH + tail + 10;
      bubbleSvg.setAttribute('width', w);
      bubbleSvg.setAttribute('height', h);
      bubbleSvg.setAttribute('viewBox', '0 0 ' + w + ' ' + h);
      // On half pixels, so the 1 px edge stays sharp.
      const path = platePath(5.5, 5.5, bubbleW - 1, bubbleH - 1, 8, 5.5 + tip, 4.5 + bubbleH + tail, 14);
      for (const part of bubbleSvg.querySelectorAll('path')) part.setAttribute('d', path);
    }
    bubble.style.transform = 'translate3d(' + left.toFixed(1) + 'px,' + top.toFixed(1) + 'px,0)';
  };

  // ---------- Poking, napping and the double-tap ----------
  // A poke while she sits plays her artist's glitchy blink, the way the app wakes her: the perch loop jumps to its blink.
  const glitchBlink = () => {
    if (still || !her.loop || her.name !== 'perch' || !ready.has('perch_blink_glitch')) return false;
    her.from = time - (28 + 0.5) / 30 * 1000;
    her.base = 0;
    her.round = 0;
    her.blink = 'perch_blink_glitch';
    return true;
  };
  const poke = () => {
    activity();
    const now = performance.now();
    pokes = pokes.filter(at => now - at < 3500);
    pokes.push(now);
    // Lying down, the laydown loop jumps to its blink instead.
    if (!glitchBlink() && !still && her.loop && her.name === 'laydown' && ready.has('laydown_blink')) {
      her.from = time - (45 + 0.5) / 30 * 1000;
      her.base = 0;
      her.round = 0;
      her.blink = 'laydown_blink';
    }
    return pokes.length >= 5 ? lines.annoyed : pick(lines.poke);
  };
  const nap = () => {
    activity();
    if (pose === 'perch') {
      if (!ready.has('laydown')) { fetchStrips(['laydown', 'laydown_sleep']); return; }
      once('perch_glitch', () => { if (drag || flight) return; pose = 'lie'; asleep = ready.has('laydown_sleep'); settle(); });
    } else if (pose === 'lie') fallAsleep();
    quip(lines.nap);
  };
  const tap = () => {
    const now = performance.now();
    const double = now - lastTap < 330;
    lastTap = now;
    clearTimeout(tapTimer);
    activity();
    if (double) {
      if (wokeByTap) { wokeByTap = false; return; }
      if (asleep) { sitUp(); quip(pick(lines.woke)); }
      else nap();
      return;
    }
    wokeByTap = false;
    if (asleep || pose === 'lie') {
      wokeByTap = asleep;
      const was = asleep;
      sitUp();
      if (was) tapTimer = setTimeout(() => quip(pick(lines.woke)), 260);
      return;
    }
    const line = poke();
    tapTimer = setTimeout(() => quip(still ? lines.still : line), 260);
  };

  // ---------- Dragging her: she hangs from the finger by her shoulders and swings with it ----------
  const startDrag = () => {
    resting = false;
    asleep = false;
    activity();
    if (bubble) bubble.hidden = true;
    const hx = clamp(press.fx - x, 0, s), hy = clamp(press.fy - y, 0, s);
    drag = { hx, hy, tx: clamp(hx, 0.38 * s, 0.62 * s), ty: clamp(hy, 0.1 * s, 0.34 * s), fx: press.fx, fy: press.fy, vx: 0 };
    swing.a = 0;
    swing.v = 0;
    flight = null;
    dizzy = null;
    pose = 'hang';
    if (ready.has('dangle')) hold('dangle');
  };
  const dragStep = dt => {
    const grip = drag;
    const ease = 1 - Math.exp(-dt * 14);
    grip.hx += (grip.tx - grip.hx) * ease;
    grip.hy += (grip.ty - grip.hy) * ease;
    const fx = press ? press.fx : grip.fx, fy = press ? press.fy : grip.fy;
    const vx = dt > 0 ? (fx - grip.fx) / dt : 0;
    const smooth = grip.vx + (vx - grip.vx) * Math.min(1, dt * 16);
    const ax = dt > 0 ? (smooth - grip.vx) / dt : 0;
    grip.vx = smooth;
    grip.fx = fx;
    grip.fy = fy;
    // A pendulum hung from the finger: her weight pulls her back under it, and the finger's push swings her the other way.
    const reach = 0.5 * s, pull = GRAVITY * s / reach, damp = 2 * 0.16 * Math.sqrt(pull);
    const push = clamp(ax, -14000, 14000) / reach * 57.2958;
    const steps = Math.max(1, Math.ceil(dt / 0.008)), h = dt / steps;
    for (let i = 0; i < steps; i++) {
      swing.v += (-pull * swing.a - damp * swing.v + push) * h;
      swing.a = clamp(swing.a + swing.v * h, -70, 70);
    }
    x = fx - grip.hx;
    y = fy - grip.hy;
  };
  // How fast the finger was going over the last 80 ms when it let go (the app's ThrowSpan).
  const trailSpeed = trail => {
    if (!trail || trail.length < 2) return { x: 0, y: 0 };
    const end = trail[trail.length - 1];
    if (performance.now() - end[0] > 110) return { x: 0, y: 0 };
    let i = trail.length - 1;
    while (i > 0 && end[0] - trail[i - 1][0] <= 80) i--;
    const start = trail[Math.max(0, i - 1)];
    const span = Math.max(0.016, (end[0] - start[0]) / 1000);
    return { x: (end[1] - start[1]) / span, y: (end[2] - start[2]) / span };
  };
  const release = speed => {
    const held = drag;
    drag = null;
    // Her pivot moves from the finger to her middle; keep her drawn exactly where she was.
    const turn = swing.a * Math.PI / 180, dx = s / 2 - held.hx, dy = s / 2 - held.hy;
    x += held.hx - s / 2 + Math.cos(turn) * dx - Math.sin(turn) * dy;
    y += held.hy - s / 2 + Math.sin(turn) * dx + Math.cos(turn) * dy;
    const fast = speed && Math.hypot(speed.x, speed.y) > THROW_FROM;
    launch(fast ? speed : { x: speed ? speed.x * 0.4 : 0, y: speed ? Math.max(0, speed.y) * 0.4 : 0 }, fast);
  };
  const launch = (speed, thrown) => {
    let vx = speed.x, vy = speed.y;
    const fast = Math.hypot(vx, vy);
    if (fast > THROW_MOST) { vx *= THROW_MOST / fast; vy *= THROW_MOST / fast; }
    pose = 'fly';
    flight = {
      vx, vy, floors: 0, hardest: 0, thrown,
      spin: swing.a,
      // She tumbles the way she's thrown, faster the harder it is (the app's spin rate).
      spinRate: thrown ? clamp(vx * 0.28 + (vx < 0 ? -1 : 1) * Math.abs(vy) * 0.1, -1080, 1080) : swing.v * 0.3
    };
    swing.a = swing.v = 0;
    if (ready.has('dangle')) hold('dangle');
    if (thrown) {
      const now = performance.now();
      throwsAt.push(now);
      while (throwsAt.length && now - throwsAt[0] > 120000) throwsAt.shift();
      const count = throwsAt.length;
      quip(count === 3 ? lines.warn[0] : count === 5 ? lines.warn[1] : count >= 7 && count % 2 ? lines.warn[2] : pick(lines.throw));
    }
  };

  // ---------- Her flight: gravity and air drag, bounces off the screen's sides and top, then hops home to her seat ----------
  const fly = dt => {
    const f = flight;
    const gravity = GRAVITY * s, settleAt = 2.2 * s, calm = 6 * s;
    const left = -0.25 * s, right = area.w - 0.76 * s, top = area.top;
    const floor = ledge.y - SIT_Y * s;
    const home = seatedAt('perch').x;
    const steps = Math.max(1, Math.ceil(dt / 0.004)), h = dt / steps;
    for (let i = 0; i < steps; i++) {
      f.vy += gravity * h;
      const keep = Math.exp(-AIR * h);
      f.vx *= keep;
      f.vy *= keep;
      x += f.vx * h;
      y += f.vy * h;
      // Her tumble slows in the air, and once she's slowed or bounced off the bar she rights herself.
      f.spin += f.spinRate * h;
      f.spinRate *= Math.exp(-0.6 * h);
      if (f.floors > 0 || f.vx * f.vx + f.vy * f.vy < calm * calm) {
        const off = f.spin - 360 * Math.round(f.spin / 360);
        f.spinRate += (-off * 60 - f.spinRate * 14) * h;
      }
      if (x < left) { x = Math.min(right, left + (left - x) * WALL); f.vx = Math.abs(f.vx) * WALL; f.spinRate *= -0.8; }
      else if (x > right) { x = Math.max(left, right - (x - right) * WALL); f.vx = -Math.abs(f.vx) * WALL; f.spinRate *= -0.8; }
      if (y < top) { y = Math.min(floor, top + (top - y) * WALL); f.vy = Math.abs(f.vy) * WALL; }
      if (y >= floor && f.vy > 0) {
        const impact = f.vy;
        y = floor;
        f.hardest = Math.max(f.hardest, impact);
        const toHome = home - x;
        if (impact > settleAt && f.floors < 6) {
          // A bounce off the bar, steered so each hop carries her back toward her seat.
          f.vy = -impact * FLOOR;
          f.floors++;
          f.spinRate *= 0.6;
          const airborne = 2 * -f.vy / gravity;
          f.vx = f.vx * GRIP * 0.35 + toHome / Math.max(0.12, airborne) * 0.65;
        } else if (Math.abs(toHome) <= 3 || f.floors > 10) { land(); return; }
        else {
          const airborne = clamp(0.24 + Math.abs(toHome) / 900, 0.24, 0.55);
          f.vy = -gravity * airborne / 2;
          f.vx = toHome / airborne;
          f.floors++;
        }
      }
    }
  };
  const land = () => {
    const f = flight;
    flight = null;
    pose = 'perch';
    settle();
    once('perch_glitch');
    activity();
    if (!f) return;
    if (!still && f.hardest > 12 * s) {
      dizzy = { from: performance.now(), until: performance.now() + 1800 };
      quip(pick(lines.dizzy));
    } else if (f.thrown && Math.random() < 0.5) quip(pick(lines.landed));
  };

  // ---------- Pointer: tap to poke, double-tap to nap or wake, drag to pick her up, fling to throw ----------
  const layerPoint = event => {
    const box = layer.getBoundingClientRect();
    return { x: event.clientX - box.left, y: event.clientY - box.top };
  };
  const onDown = event => {
    if (event.button > 0 || press || gone || leaving) return;
    event.preventDefault();
    try { hit.setPointerCapture(event.pointerId); } catch (error) {}
    const at = layerPoint(event);
    const now = performance.now();
    press = { id: event.pointerId, sx: event.clientX, sy: event.clientY, fx: at.x, fy: at.y, moved: false, trail: [[now, at.x, at.y]], caught: !!flight };
    if (flight) { swing.a = flight.spin; flight = null; }
    activity();
    fetchStrips(['dangle', 'perch_blink_glitch', 'laydown_blink']);
  };
  const onMove = event => {
    if (!press || event.pointerId !== press.id) return;
    const at = layerPoint(event);
    const now = performance.now();
    press.fx = at.x;
    press.fy = at.y;
    press.trail.push([now, at.x, at.y]);
    while (press.trail.length > 2 && press.trail[1][0] < now - 180) press.trail.shift();
    if (press.moved) return;
    const dx = event.clientX - press.sx, dy = event.clientY - press.sy;
    if (dx * dx + dy * dy < 49) return;
    press.moved = true;
    clearTimeout(tapTimer);
    if (still) { quip(lines.still); return; }
    startDrag();
  };
  const onUp = event => {
    if (!press || event.pointerId !== press.id) return;
    const done = press;
    if (drag) { const speed = trailSpeed(done.trail); press = null; release(speed); return; }
    press = null;
    if (done.caught) { launch({ x: 0, y: 0 }, false); return; }
    if (!done.moved) tap();
  };
  const onCancel = event => {
    if (!press || (event.pointerId !== undefined && event.pointerId !== press.id)) return;
    const done = press;
    press = null;
    if (drag) release(null);
    else if (done.caught) launch({ x: 0, y: 0 }, false);
  };

  // ---------- The clock: one requestAnimationFrame loop, transforms only, stopped while the page is hidden ----------
  let raf = 0, last = 0, origin = '', placed = '', drawnPose = '', cutAt = '';
  const render = now => {
    let turn = 0, ox = s / 2, oy = s / 2;
    if (drag) { turn = swing.a; ox = drag.hx; oy = drag.hy; }
    else if (flight) turn = flight.spin;
    else if (dizzy) {
      // A big landing leaves her dizzy for a beat: her head swims around her hip.
      const t = (now - dizzy.from) / 1000;
      if (now >= dizzy.until) dizzy = null;
      else { turn = Math.sin(t * Math.PI * 2 * 1.3) * 7 * (1 - t / 1.8); ox = SIT_X * s; oy = SIT_Y * s; }
    }
    const nextOrigin = ox.toFixed(1) + 'px ' + oy.toFixed(1) + 'px';
    if (nextOrigin !== origin) { el.style.transformOrigin = nextOrigin; origin = nextOrigin; }
    const transform = 'translate3d(' + x.toFixed(2) + 'px,' + y.toFixed(2) + 'px,0) rotate(' + turn.toFixed(2) + 'deg)';
    if (transform !== placed) { el.style.transform = transform; placed = transform; }
    const body = pose === 'perch' || pose === 'lie' ? pose : 'stand';
    if (body !== drawnPose) { el.dataset.pose = body; drawnPose = body; }
    // She sits on her seat's top edge with her legs over the far side: nothing of hers shows (or takes a touch) below
    // that edge, so she never covers the bar's words or buttons, or the card she sits on. In the hand she's in front.
    const cut = drag || !ledge ? 'none' : 'inset(0 0 ' + Math.max(0, area.h - ledge.y).toFixed(1) + 'px 0)';
    if (cut !== cutAt) { layer.style.clipPath = cut; layer.style.webkitClipPath = cut; cutAt = cut; }
  };
  const tick = now => {
    raf = requestAnimationFrame(tick);
    const step = Math.min(now - last, 250);
    last = now;
    if (!still) time += step;
    const dt = Math.min(step, 40) / 1000;
    if (!readSeat()) return;
    if (!shown) {
      if (!ready.has('perch')) return;
      shown = true;
      el.hidden = false;
      settle();
      once('perch_arrive');
    }
    if (drag) dragStep(dt);
    else if (flight) fly(dt);
    // Caught mid-flight she stays in the hand until it moves or lets go.
    else if (!(press && press.caught)) { const at = seatedAt(pose); x = at.x; y = at.y; }
    behave(now);
    render(now);
    draw();
    placeBubble(now);
  };
  const run = () => {
    if (raf || document.hidden || !layer) return;
    last = performance.now();
    raf = requestAnimationFrame(tick);
  };
  document.addEventListener('visibilitychange', () => {
    if (document.hidden) { cancelAnimationFrame(raf); raf = 0; if (press) onCancel({}); }
    else run();
  });
  reduced.addEventListener('change', () => {
    still = reduced.matches;
    if (!still) return;
    if (drag) { drag = null; press = null; }
    if (flight) { flight = null; pose = 'perch'; }
    dizzy = null;
    if (!her.loop) finish();
    settle();
  });

  // ---------- Building her ----------
  const mount = (into, options = {}) => {
    layer = into;
    el = document.createElement('div');
    el.className = 'her';
    el.hidden = true;
    el.dataset.pose = 'perch';
    paint = document.createElement('span');
    paint.className = 'sprite';
    paint.dataset.fit = 'perch';
    hit = document.createElement('button');
    hit.type = 'button';
    hit.className = 'hit';
    hit.setAttribute('aria-label', 'Capri. Tap to poke, double-tap to nap, drag to pick her up and fling to throw.');
    el.append(paint, hit);
    bubble = document.createElement('div');
    bubble.className = 'quip';
    bubble.hidden = true;
    bubble.setAttribute('aria-hidden', 'true');
    const ns = 'http://www.w3.org/2000/svg';
    bubbleSvg = document.createElementNS(ns, 'svg');
    bubbleSvg.setAttribute('class', 'quip-plate');
    for (const part of ['fill', 'edge']) {
      const path = document.createElementNS(ns, 'path');
      path.setAttribute('class', part);
      bubbleSvg.append(path);
    }
    // No name on it: the header already says who's talking.
    bubbleWords = document.createElement('p');
    bubbleWords.className = 'quip-words';
    bubble.append(bubbleSvg, bubbleWords);
    layer.append(el, bubble);
    hit.addEventListener('pointerdown', onDown);
    hit.addEventListener('pointermove', onMove);
    hit.addEventListener('pointerup', onUp);
    hit.addEventListener('pointercancel', onCancel);
    hit.addEventListener('lostpointercapture', onCancel);
    hit.addEventListener('contextmenu', event => event.preventDefault());
    hit.addEventListener('keydown', event => {
      if (event.key !== 'Enter' && event.key !== ' ') return;
      event.preventDefault();
      tap();
    });
    if (options.size) setSize(options.size);
    if (options.top !== undefined) area.top = options.top;
    fetchStrips(['perch', 'perch_blink', 'perch_blink_glitch', 'perch_glitch', 'perch_arrive', 'perch_leave']).then(() =>
      fetchStrips(['dangle', 'perch_talk', 'laydown', 'laydown_blink', 'laydown_glitch', 'laydown_talk', 'laydown_sleep', 'laydown_leave']));
    run();
  };
  const setSize = size => {
    const next = Math.round(size);
    if (next === s && layer && layer.style.getPropertyValue('--s')) return;
    s = next;
    if (layer) layer.style.setProperty('--s', s + 'px');
    if (onDock) onDock(dockWidth());
  };

  // She glitches off one seat and arrives on the next, as she hops between windows on the desktop.
  const seat = (target, where = 'left') => {
    if (target === seatEl && where === side) return;
    if (!seatEl || !shown || still || document.hidden) { seatEl = target; side = where; ledge = null; readSeat(); return; }
    leaving = true;
    const go = () => {
      leaving = false;
      gone = true;
      el.classList.add('gone');
      if (bubble) bubble.hidden = true;
      seatEl = target;
      side = where;
      ledge = null;
      setTimeout(() => {
        gone = false;
        el.classList.remove('gone');
        asleep = false;
        if (pose !== 'lie' || resting) { pose = 'perch'; settle(); once('perch_arrive'); }
        else settle();
      }, 240);
    };
    if (drag) { drag = null; press = null; }
    if (flight) flight = null;
    dizzy = null;
    if (pose === 'hang' || pose === 'fly' || pose === 'stand') { pose = 'perch'; settle(); }
    // The screen changed under her and took her old seat with it: nothing left to glitch off, so she just arrives.
    const old = seatEl.getBoundingClientRect();
    if (!old.width || !old.height) { go(); return; }
    once(pose === 'lie' ? 'laydown_leave' : 'perch_leave', go);
  };

  window.CapriBar = {
    mount,
    seat,
    setSize,
    activity,
    get size() { return s; },
    get dock() { return dockWidth(); },
    onDock: fn => { onDock = fn; fn(dockWidth()); },
    setTop: top => { area.top = top; },
    // She talks while a reply streams in: perch_talk on her perch, laydown_talk lying down. A reply wakes her.
    talk: (on, text) => {
      talking = !!on;
      if (text) mouthText = text.slice(-40);
      if (!on) return;
      activity();
      if (asleep && !resting) { asleep = false; if (her.loop) settle(); }
    },
    // When they send, she reacts where she sits with her glitchy blink. (Standing up to grin, at her size, would cover the
    // message she's about to answer.) Lying down she can't be bothered.
    grin: () => {
      activity();
      if (drag || flight || gone || pose !== 'perch') return;
      glitchBlink();
    },
    music: on => {
      music = !!on;
      if (music) fetchStrips(['perch_music']).then(() => { if (music && pose === 'perch' && her.loop) settle(); });
      if (pose === 'perch' && her.loop) settle();
    },
    // PC offline: she lies down on the bar and sleeps until it's back.
    rest: on => {
      resting = !!on;
      if (!resting && (asleep || pose === 'lie')) sitUp();
    },
    wake: () => sitUp(),
    quip
  };
})();

(() => {
  // The reveals only hide once this file runs, so the page stays readable if the script never loads.
  const root = document.documentElement;
  root.classList.replace('no-js', 'js');

  const $ = (query, from = document) => from.querySelector(query);
  const $$ = (query, from = document) => [...from.querySelectorAll(query)];
  const clamp = (value, low, high) => Math.max(low, Math.min(high, value));
  const wide = matchMedia('(min-width: 901px)');
  const reduced = matchMedia('(prefers-reduced-motion: reduce)');
  let still = reduced.matches;
  let time = 0;
  let lastGlitch = -1e9;

  // ---------- Her short lines ----------
  const lines = {
    help: 'Hey choom. Need a hand setting me up?',
    voice: 'Voice needs my chip, choom. You can type to me here.',
    hi: "Hey choom! Net's been quiet, too quiet. Time to stir up some trouble.",
    come: 'Teleporting to your mouse.',
    off: 'Hopped off your cursor.',
    sleep: 'Night night.',
    opened: 'Got the player open, choom.',
    paused: 'Music paused.',
    resumed: 'Coming right up, choom.',
    playing: "I'm In Love With Myself. Good pick, choom.",
    more: count => count === 1 ? "There's two of me now, choom. Hold Esc to clear her." : "There's " + count + ' more of me now, choom. Hold Esc to clear them.',
    multiply: "One of me wasn't enough, choom. We'll keep multiplying, so hold Esc when you've had enough."
  };

  // ---------- Her clips, from the app's Clip table: frames, fps, cells across, where the cell sits in her box, file type ----------
  const table = {};
  for (const [name, count, fps, across = count, fit = 'stand', type = 'webp'] of [
    ['idle', 16, 8], ['blink', 4, 8], ['blink_glitch', 4, 8], ['grin', 16, 8], ['glitch', 10, 15], ['dangle', 24, 10], ['sleep', 16, 6], ['wake', 13, 15], ['mouth', 5, 12],
    ['perch', 60, 30, 10, 'perch'], ['perch_music', 60, 30, 60, 'music', 'png'], ['perch_glitch', 10, 15, 10, 'perch'],
    ['laydown', 60, 30, 60, 'lie', 'png'], ['laydown_talk', 60, 30, 60, 'lie', 'png'],
    ['laydown_blink', 8, 30, 8, 'lie', 'png'], ['laydown_sleep', 60, 30, 60, 'lie', 'png'],
    ['laydown_glitch', 10, 15, 10, 'lie', 'png'], ['laydown_leave', 12, 15, 12, 'lie', 'png'],
    ['arrive', 14, 15, 14, 'stand', 'png'], ['leave', 12, 15, 12, 'stand', 'png'],
    ['perch_arrive', 14, 15, 14, 'square', 'png'], ['perch_leave', 12, 15, 12, 'square', 'png']
  ]) table[name] = { count, fps, across, rows: Math.ceil(count / across), fit, url: 'assets/sprites/' + name + '.' + type };

  // Strips decode before use, so she never blinks out while one loads.
  const ready = new Set(['idle', 'perch']);
  const asked = new Set();
  const strips = new Map();
  const fetchStrips = names => {
    for (const name of names) {
      if (asked.has(name)) continue;
      asked.add(name);
      const strip = new Image();
      strip.src = table[name].url;
      strip.decode().then(() => { strips.set(name, strip); ready.add(name); }).catch(() => {});
    }
  };

  const actors = [];
  const live = new Set();
  const actor = (el, name, options = {}) => {
    const paint = $('.sprite', el);
    const nodCanvas = document.createElement('canvas');
    nodCanvas.className = 'nod-layer';
    nodCanvas.width = nodCanvas.height = 768;
    nodCanvas.hidden = true;
    nodCanvas.setAttribute('aria-hidden', 'true');
    el.append(nodCanvas);
    const ink = nodCanvas.getContext('2d');
    const self = { el, name, rest: name, start: options.start || 0, base: options.start || 0, loop: true, then: null, from: 0, host: el.closest('.desk'), blinks: !!options.blinks, round: -1, blink: '', layers: '', shown: '', mouth: null, talkFrom: null, onRound: null };
    self.hold = next => { self.name = self.rest = next; self.base = next === self.el.dataset.clip ? self.start : 0; self.loop = true; self.then = null; self.from = time; };
    self.once = (next, then) => {
      if (still || !ready.has(next)) { if (then) then(); return; }
      if (next === 'glitch' || next === 'perch_glitch' || next === 'laydown_glitch') {
        const now = performance.now();
        if (now - lastGlitch < 2000) { if (then) then(); return; }
        lastGlitch = now;
      }
      self.name = next; self.base = 0; self.loop = false; self.then = then || null; self.from = time;
    };
    self.finish = () => { const then = self.then; self.hold(self.rest); if (then) then(); };
    self.show = (strip, cell, mouth) => {
      const key = strip + ':' + cell + ':' + mouth;
      if (key === self.shown) return;
      self.shown = key;
      const clip = table[strip];
      const layers = mouth >= 0 ? ['mouth', strip] : [strip];
      if (self.layers !== layers.join()) {
        self.layers = layers.join();
        paint.dataset.fit = clip.fit;
        paint.style.backgroundImage = layers.map(layer => 'url("' + table[layer].url + '")').join(', ');
        paint.style.backgroundSize = layers.map(layer => table[layer].across * 100 + '% ' + table[layer].rows * 100 + '%').join(', ');
      }
      const column = cell % clip.across;
      const row = Math.floor(cell / clip.across);
      const at = (clip.across > 1 ? column / (clip.across - 1) * 100 : 0) + '% ' + (clip.rows > 1 ? row / (clip.rows - 1) * 100 : 0) + '%';
      paint.style.backgroundPosition = (mouth >= 0 ? mouth / 4 * 100 + '% 0, ' : '') + at;
    };
    self.draw = now => {
      const clip = table[self.name];
      const index = still && self.name === 'perch_music' ? 0 : self.base + Math.floor((now - self.from) / 1000 * clip.fps);
      if (!self.loop && index >= clip.count) { self.finish(); return; }
      if (self.loop) {
        const round = Math.floor(index / clip.count);
        if (round !== self.round) {
          self.round = round;
          const roll = Math.random();
          self.blink = self.blinks && self.name === 'idle' ? (roll < 0.1 ? 'blink_glitch' : roll < 0.55 ? 'blink' : '') : '';
          if (self.blinks && self.name === 'laydown' && roll < 0.55) self.blink = 'laydown_blink';
          if (self.onRound) self.onRound(now);
        }
      }
      let strip = self.name;
      let cell = index % clip.count;
      if (strip === 'idle' && self.blink && ready.has(self.blink) && cell >= 9 && cell < 13) { strip = self.blink; cell -= 9; }
      if (strip === 'laydown') {
        if (self.talkFrom !== null && ready.has('laydown_talk')) { strip = 'laydown_talk'; cell = Math.floor((now - self.talkFrom) / 1000 * 30) % 60; }
        else if (self.blink && ready.has(self.blink) && cell >= 45 && cell < 53) { strip = self.blink; cell -= 45; }
      }
      const mouth = self.mouth ? self.mouth(now, strip) : -1;
      self.show(strip, cell, mouth);
      self.drawNod(strip, cell, mouth);
    };
    // App Nod: the chest and legs stay fixed. Only rows above the chest bend.
    self.drawNod = (name, cell, mouth) => {
      const source = strips.get(name);
      const active = ink && source && self.nod > 0 && ['idle', 'blink', 'blink_glitch', 'perch_music'].includes(name);
      nodCanvas.hidden = !active;
      paint.style.visibility = active ? 'hidden' : '';
      if (!active) return;
      ink.clearRect(0, 0, 768, 768);
      const sitting = table[name].fit === 'music';
      const fit = sitting ? [.1771, .0938, .5104, 1] : [.153, -.055, .694, 1.11];
      const draw = (image, clip, frame) => {
        const width = image.naturalWidth / clip.across;
        const height = image.naturalHeight / clip.rows;
        const sx = frame % clip.across * width;
        const sy = Math.floor(frame / clip.across) * height;
        const scale = fit[3] * 512 / height;
        const rowsDown = fit[1] * 512 / scale;
        const head = (sitting ? 196 : 138) - rowsDown;
        const chest = (sitting ? 232 : 176) - rowsDown;
        const neck = (sitting ? 214 : 152) - rowsDown;
        const at = row => {
          let weight = clamp((chest - row) / (chest - head), 0, 1);
          weight = weight * weight * (3 - 2 * weight);
          return { x: 128 + fit[0] * 512 + (neck - row) * self.nod * .22 * weight * scale,
            y: 128 + fit[1] * 512 + (row + self.nod * 10 * weight) * scale };
        };
        const piece = (top, bottom) => {
          const a = at(top), b = at(bottom);
          ink.setTransform(fit[2] * 512 / width, 0, (b.x - a.x) / (bottom - top), (b.y - a.y) / (bottom - top), a.x, a.y);
          ink.drawImage(image, sx, sy + top, width, bottom - top, 0, 0, width, bottom - top);
        };
        piece(chest, height);
        for (let row = chest; row > head; row -= 3) piece(Math.max(head, row - 3), row);
        piece(0, head);
        ink.resetTransform();
      };
      draw(source, table[name], cell);
      if (mouth >= 0 && strips.has('mouth')) draw(strips.get('mouth'), table.mouth, mouth);
    };
    actors.push(self);
    return self;
  };

  // ---------- Her plate: the cut-corner box with a tail ----------
  const plate = (left, top, width, height, cut, tipX, tipY, tail, both) => {
    const right = both ? cut : 0;
    const near = Math.max(left + cut + 2, tipX - tail * 0.8);
    const far = Math.min(left + width - right - 2, tipX + tail * 0.2);
    const bottom = top + height;
    const points = [[left + cut, top]];
    if (tipY < top) points.push([near, top], [tipX, tipY], [far, top]);
    points.push([left + width - right, top]);
    if (both) points.push([left + width, top + cut], [left + width, bottom - cut]);
    points.push([left + width - right, bottom]);
    if (tipY > bottom) points.push([far, bottom], [tipX, tipY], [near, bottom]);
    points.push([left + cut, bottom], [left, bottom - cut], [left, top + cut]);
    return 'M' + points.map(point => point[0].toFixed(1) + ' ' + point[1].toFixed(1)).join('L') + 'Z';
  };
  const drawn = new Map();
  let frames = 0;
  const frame = (svg, width, height, path, bar) => {
    const key = width + ' ' + height + ' ' + path;
    if (drawn.get(svg) === key) return;
    drawn.set(svg, key);
    const id = svg.dataset.id || (svg.dataset.id = 'scan' + frames++);
    svg.setAttribute('width', width);
    svg.setAttribute('height', height);
    svg.setAttribute('viewBox', '0 0 ' + width + ' ' + height);
    svg.innerHTML = '<defs><pattern id="' + id + '" width="4" height="3" patternUnits="userSpaceOnUse"><rect width="4" height="1" fill="#ffffff09"/></pattern></defs>' +
      '<path d="' + path + '" fill="none" stroke="#3ad6eb2e" stroke-width="7"/>' +
      '<path d="' + path + '" fill="#090b0fee"/>' +
      '<path d="' + path + '" fill="url(#' + id + ')"/>' +
      '<path d="' + path + '" fill="none" stroke="#3ad6eb" stroke-width="2"/>' +
      '<rect x="10" y="' + bar + '" width="3" height="' + Math.max(0, height - bar * 2) + '" fill="#e82440"/>';
  };
  const edge = box => {
    const width = box.offsetWidth;
    const height = box.offsetHeight;
    if (width) frame($('svg.frame', box), width, height, plate(1, 1, width - 2, height - 2, 10, -1, height / 2, 0, true), 10);
  };
  const placeBox = (box, tip, across, low) => {
    const width = box.offsetWidth;
    const height = box.offsetHeight;
    if (!width) return;
    if (!wide.matches && box.id === 'talkbox') {
      // A short tail stays inside the speech space, clear of her hair in every pose.
      box.style.left = '8px';
      box.style.top = '4px';
      frame($('svg.frame', box), width, height, plate(0, 0, width, height, 10, clamp(tip.x - 8, 32, width - 10), height + 6, 12, true), 13);
      return;
    }
    const left = clamp(tip.x - width * 0.7, 4, across - width - 4);
    const top = clamp(tip.y - 16 - height, 4, low - height);
    box.style.left = left + 'px';
    box.style.top = top + 'px';
    frame($('svg.frame', box), width, height, plate(0, 0, width, height, 10, clamp(tip.x - left, 32, width - 10), Math.max(tip.y - top, height + 1), 18, true), 13);
  };

  // ---------- The type borrows her glitch: two cut bands slip for a third of a second, at most once every 2 s ----------
  let lastCut = -1e9;
  const cut = () => {
    const now = performance.now();
    if (still || now - lastCut < 2000) return;
    lastCut = now;
    for (const mark of $$('.wordmark, .glitchable')) {
      const box = mark.getBoundingClientRect();
      if (box.bottom < 0 || box.top > innerHeight) continue;
      mark.classList.remove('glitching');
      void mark.offsetWidth;
      mark.classList.add('glitching');
    }
  };
  document.addEventListener('animationend', event => { if (event.animationName === 'cut-two') event.target.parentElement.classList.remove('glitching'); });

  // ---------- The stage ----------
  const stage = $('#stage');
  const status = $('#stage-status');
  const wins = { notes: $('#win-notes'), player: $('#win-player'), log: $('#win-log') };
  const tasks = { notes: $('#task-notes'), player: $('#task-music'), log: $('#task-log') };
  const pink = $('#her-cursor');
  // Fixed to the viewport so her jobs can reach real controls outside the stage.
  document.body.append(pink);
  let lastMouse = null;
  const mouseAt = () => lastMouse ? { x: clamp(lastMouse.x, 0, across), y: clamp(lastMouse.y, 0, tall) } : { x: across / 2, y: tall / 2 };
  const pill = $('#pill');
  const talkbox = $('#talkbox');
  const mini = $('#mini');
  const hint = $('#ride-hint');
  const playButton = $('#player-play');
  const her = actor($('#her'), 'perch', { blinks: true });
  const smalls = $$('.stage.small').map(el => ({ el, box: $('.talkbox', el), her: actor($('.her', el), 'idle', { blinks: true }) }));

  // One saved list feeds the visible log, answered help turns and shell recall.
  const logKey = 'capri-stage-log';
  const stageLog = [];
  let turnCount = 0;
  let recallAt = null;
  let recallDraft = '';
  const newTurn = () => Date.now() + '-' + ++turnCount;
  try {
    const saved = JSON.parse(localStorage.getItem(logKey));
    if (Array.isArray(saved)) {
      for (const line of saved.slice(-40)) {
        if (!line || !['user', 'assistant'].includes(line.role) || typeof line.text !== 'string' || typeof line.at !== 'string' || !Number.isFinite(Date.parse(line.at))) continue;
        stageLog.push({ role: line.role, text: line.text.slice(0, 1500), at: line.at, turn: typeof line.turn === 'string' ? line.turn : null, answered: line.answered === true, typed: line.typed === true && line.role === 'user' });
      }
    }
  } catch {}
  const renderLog = () => {
    const list = $('#chat-log');
    const rows = stageLog.map(line => {
      const row = document.createElement('li');
      const when = document.createElement('time');
      when.dateTime = line.at;
      when.title = new Date(line.at).toLocaleString();
      when.textContent = new Date(line.at).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' }) + ' ';
      const speaker = document.createElement('span');
      speaker.className = 'speaker';
      speaker.textContent = line.role === 'user' ? 'you: ' : 'Capri: ';
      const words = document.createElement('span');
      words.textContent = line.text;
      row.append(when, speaker, words);
      return row;
    });
    list.replaceChildren(...rows);
    list.scrollTop = list.scrollHeight;
  };
  const logLine = (role, text, turn = null, answered = false, typed = false) => {
    stageLog.push({ role, text, at: new Date().toISOString(), turn, answered, typed });
    if (stageLog.length > 40) stageLog.splice(0, stageLog.length - 40);
    recallAt = null;
    try { localStorage.setItem(logKey, JSON.stringify(stageLog)); } catch {}
    renderLog();
  };
  const earlierTurns = () => {
    const turns = [];
    stageLog.forEach((line, index) => {
      if (line.role !== 'assistant' || !line.answered || !line.turn) return;
      const question = stageLog.slice(0, index).find(entry => entry.role === 'user' && entry.turn === line.turn);
      if (question) turns.push({ role: 'user', text: question.text }, { role: 'assistant', text: line.text });
    });
    return turns.slice(-12);
  };
  $('#clear-log').addEventListener('click', () => {
    touched();
    stageLog.length = 0;
    recallAt = null;
    recallDraft = '';
    try { localStorage.removeItem(logKey); } catch {}
    renderLog();
    status.textContent = 'Conversation log and help history cleared.';
  });
  tasks.log.addEventListener('click', () => {
    touched();
    showWindow('log');
    renderLog();
    $('#chat-log').focus();
  });
  renderLog();

  // Each chapter's arrangement, as shares of the stage. A window is [left, top, width]; a missing window is closed.
  const scenes = {
    hero: { player: [0.47, 0.47, 0.46], notes: [0.05, 0.66, 0.36], seat: 'player', pose: 'lie', heard: 'hi', line: lines.hi },
    desktop: { player: [0.5, 0.72, 0.44], notes: [0.09, 0.5, 0.42], seat: 'notes' },
    talk: { player: [0.5, 0.07, 0.44], seat: null, at: 0.2, heard: 'hi capri', line: lines.help, pill: true },
    music: { player: [0.2, 0.56, 0.6], seat: 'player', heard: 'open spotify', line: lines.opened, playerJob: true }
  };
  const phone = { hero: { player: [0.05, 0.7, 0.9], seat: 'player', pose: 'lie', heard: 'hi', line: lines.hi } };
  const plans = () => wide.matches ? scenes : phone;
  // Where she sits along a window: the player leaves its left side for her, and me.md's text is on its left.
  const seats = { player: 0.13, notes: 0.56, log: 0.56 };
  const lieWidth = 372 / 384 * 0.85;
  const lieHeight = 250 / 384 * 0.85;

  let across = 0;
  let tall = 0;
  let floor = 0;
  let size = 300;
  let x = 0;
  let y = 0;
  let pose = 'perch';
  let seat = 'player';
  let scene = 'hero';
  let shownScene = 'hero';
  let hopping = false;
  let riding = false;
  let drag = null;
  let flight = null;
  let dirty = true;
  let staged = false;
  let placed = '';
  let holdUntil = 0;
  let waiting = 0;
  let pillUntil = 0;
  let lastInput = performance.now();
  let napUntil = 0;
  let asleep = false;
  let manualNap = false;
  let helpBusy = false;
  let nextGlitch = 12000 + Math.random() * 12000;

  const restClip = () => pose === 'lie' ? 'laydown' : pose === 'perch' ? 'perch' : pose === 'hang' ? 'dangle' : 'idle';
  const tipOf = () => {
    if (pose === 'lie') return { x: x + size * lieWidth * 0.22, y: y + size * lieHeight * 0.25 };
    return pose === 'perch' ? { x: x + size * 0.36, y: y + size * 0.13 } : { x: x + size * 0.52, y: y - 4 };
  };
  // Her back rests on the top edge and her far leg hangs off its right end, as on her real windows.
  const lyingAt = win => {
    if (!wide.matches) return { x: win.offsetLeft + 4, y: win.offsetTop - size * lieHeight * 0.6 };
    return { x: win.offsetLeft + win.offsetWidth - size * lieWidth * 0.84, y: win.offsetTop - size * lieHeight * 0.6 };
  };
  const lieFits = at => at.x >= 4 && at.y >= 4 && at.x + size * lieWidth <= across - 4 && at.y + size * lieHeight <= floor;
  const measure = () => {
    across = stage.clientWidth;
    tall = stage.clientHeight;
    floor = tall - 52;
    size = Math.round(clamp(tall * 0.44, 180, 440));
    if (!wide.matches) size = Math.round(clamp(tall * 0.42, 110, 170));
    stage.style.setProperty('--s', size + 'px');
  };
  const place = () => {
    if (seat) {
      const win = wins[seat];
      if (pose === 'lie' && !wide.matches && !lieFits(lyingAt(win))) {
        pose = 'perch';
        her.hold(asleep ? 'sleep' : restClip());
      }
      if (pose === 'lie') ({ x, y } = lyingAt(win));
      else {
        x = win.offsetLeft + win.offsetWidth * seats[seat] - size * 0.4;
        y = win.offsetTop - size * 0.73 - 4;
      }
    } else {
      x = clamp(x, -size * 0.2, across - size * 0.8);
      y = clamp(y, -size * 0.2, floor - size * 0.99);
      if (!wide.matches) {
        x = clamp(x, 4, across - size - 4);
        y = clamp(y, 100 + size * 0.055, floor - size * 1.094 - 4);
      }
    }
    her.el.dataset.pose = pose;
    const key = x + ',' + y + ',' + size + ',' + pose;
    if (key === placed) return;
    placed = key;
    her.el.style.transform = 'translate(' + x + 'px, ' + y + 'px)';
    dirty = true;
  };
  const placePill = () => {
    const width = pill.offsetWidth;
    const middle = pose === 'lie' ? x + size * lieWidth * 0.5 : x + size * 0.58 - 4;
    const bottom = y + size * (pose === 'lie' ? lieHeight : 0.99);
    pill.style.left = clamp(middle - width / 2, 4, across - width - 4) + 'px';
    pill.style.top = clamp(bottom + 4, 4, floor - pill.offsetHeight - 6) + 'px';
  };

  const arrange = name => {
    const plan = plans()[name] || plans().hero;
    for (const key of ['notes', 'player']) {
      const spec = plan[key];
      wins[key].classList.toggle('gone', !spec);
      wins[key].inert = !spec;
      if (!spec) continue;
      wins[key].style.left = spec[0] * across + 'px';
      wins[key].style.width = spec[2] * across + 'px';
      wins[key].style.top = clamp(spec[1] * tall, wide.matches ? 92 : 8, floor - wins[key].offsetHeight - 12) + 'px';
    }
    $('#task-notes').classList.toggle('on', !!plan.notes);
    $('#task-music').classList.toggle('on', !!plan.player);
    $('#task-music').classList.toggle('lit', !!plan.playerJob);
    seat = plan.seat;
    pose = seat ? plan.pose || 'perch' : 'stand';
    if (pose === 'lie' && !wide.matches && !lieFits(lyingAt(wins[seat]))) pose = 'perch';
    if (!seat) { x = plan.at * across; y = floor - size * 0.99; }
    wins.player.classList.toggle('free', seat !== 'player' || pose === 'lie');
    her.hold(restClip());
    dirty = true;
  };

  // She leaves on her own glitch frames and arrives the same way, and the chapter's windows glide while she's gone.
  const teleport = (move, then) => {
    if (still) { move(); if (then) then(); return; }
    her.once(pose === 'lie' ? 'laydown_leave' : pose === 'perch' ? 'perch_leave' : 'leave', () => {
      if (still) { move(); if (then) then(); return; }
      her.el.classList.add('gone');
      move();
      setTimeout(() => {
        her.el.classList.remove('gone');
        her.hold(restClip());
        her.once(pose === 'perch' ? 'perch_arrive' : 'arrive', then);
      }, still ? 0 : 280);
    });
  };
  const perform = name => {
    const plan = plans()[name] || plans().hero;
    staged = true;
    if (plan.line) speak(plan.heard, plan.line);
    else talkbox.classList.add('off');
    staged = false;
    if (plan.pill) pillUntil = Infinity;
    if (plan.playerJob) openPlayer(false);
  };
  const hop = () => {
    const to = scene;
    hopping = true;
    pillUntil = 0;
    pink.classList.remove('on');
    stopTalk();
    talkbox.classList.add('off');
    teleport(() => {
      arrange(to);
      shownScene = to;
      cut();
      if (!still) stage.classList.add('hop');
    }, () => {
      hopping = false;
      perform(to);
      if (scene !== to) go(scene);
    });
  };
  // Visitor input beats the script: anything they do holds the scenes off for 8 s.
  const go = name => {
    scene = name;
    if (hopping || shownScene === name) return;
    const wait = holdUntil - performance.now();
    if (wait > 0 || riding || drag || flight || helpBusy || manualNap || talking) {
      clearTimeout(waiting);
      waiting = setTimeout(() => go(scene), Math.max(wait, 400));
      return;
    }
    if (still) { arrange(name); shownScene = name; perform(name); return; }
    hop();
  };
  const touched = () => {
    holdUntil = performance.now() + 8000;
    $('#stage-tries').classList.add('used');
  };
  stage.addEventListener('animationend', event => { if (event.animationName === 'scan') stage.classList.remove('hop'); });

  const react = () => {
    if (hopping || still || drag || asleep || playing) return;
    her.once(pose === 'lie' ? 'laydown_glitch' : pose === 'perch' ? 'perch_glitch' : 'glitch');
    cut();
  };
  her.onRound = now => {
    if (now >= nextGlitch && !talking && !helpBusy && !hopping && !drag && !riding && !flight) { nextGlitch = now + 12000 + Math.random() * 12000; react(); }
    if (!playing && her.name === 'idle' && performance.now() - lastInput > 30000 && !talking && !helpBusy && !hopping && !riding && !flight) nap();
  };
  const nap = (manual = false) => {
    fetchStrips(['sleep', 'wake', 'laydown_sleep']);
    if (riding) setRide(false);
    flight = null;
    asleep = true;
    manualNap = manual;
    her.hold(pose === 'lie' ? 'laydown_sleep' : 'sleep');
    napUntil = performance.now() + 3500;
  };
  const wake = (force = false) => {
    lastInput = performance.now();
    if (!asleep || (!force && (manualNap || lastInput < napUntil))) return;
    asleep = manualNap = false;
    if (pose === 'lie') { her.hold(restClip()); return; }
    her.rest = restClip();
    her.once('wake');
    if (her.name === 'sleep') her.hold(restClip());
  };
  for (const name of ['pointermove', 'pointerdown', 'keydown', 'wheel', 'scroll']) addEventListener(name, () => wake(), { passive: true });

  const setRide = on => {
    if (on && still) { speak(null, 'Cursor ride is resting while motion is off, choom.'); return; }
    if (riding === on) return;
    flight = null;
    wake(true);
    riding = on;
    hint.hidden = !on;
    if (on) {
      seat = null;
      pose = 'perch';
      const mouse = mouseAt();
      x = mouse.x - size * .267 + 12;
      y = mouse.y - size * .73 - 12;
      wins.player.classList.add('free');
      her.hold('perch');
      her.once('perch_glitch');
      talkbox.classList.add('off');
      return;
    }
    pose = 'stand';
    her.hold('idle');
    her.once('grin');
    speak(null, lines.off);
    touched();
  };
  const followPointer = event => {
    const box = stage.getBoundingClientRect();
    const left = event.clientX - box.left;
    const top = event.clientY - box.top;
    if (event.pointerType === 'mouse') lastMouse = { x: left, y: top };
    if (left > x - 40 && left < x + size + 40 && top > y - 40 && top < y + size + 70 && pillUntil !== Infinity) pillUntil = performance.now() + 1500;
    if (riding && !still) { x = left - size * 0.267 + 12; y = top - size * 0.73 - 12; }
  };
  stage.addEventListener('pointermove', followPointer);

  // Dragging her, as on the live site. Dropped on a window's top edge she sits on it and rides along.
  const landOn = () => {
    const hip = { x: x + size * 0.5, y: y + size * 0.78 };
    return Object.keys(wins).find(key => {
      const win = wins[key];
      return !win.classList.contains('gone') && hip.x > win.offsetLeft - 20 && hip.x < win.offsetLeft + win.offsetWidth + 20 && Math.abs(hip.y - win.offsetTop) < size * 0.3;
    });
  };
  const drop = (event, velocity) => {
    her.el.classList.remove('dragging');
    if (still) { speak(null, 'Easy, choom. Motion is off.'); return; }
    pose = 'stand';
    if (event.shiftKey) { seat = landOn() || null; pose = 'perch'; }
    else if (event.ctrlKey) pose = 'hang';
    else if (velocity && Math.hypot(velocity.x, velocity.y) > 320) {
      flight = { vx: velocity.x, vy: velocity.y };
      her.hold('dangle');
      speak(null, 'Wheee. Catch me, choom.');
      return;
    }
    else {
      seat = landOn() || null;
      if (seat) pose = 'perch';
      else {
        her.el.classList.add('falling');
        y = floor - size * 0.99;
        setTimeout(() => her.el.classList.remove('falling'), 340);
      }
    }
    wins.player.classList.toggle('free', seat !== 'player' || pose === 'lie');
    her.hold(restClip());
    if (pose === 'stand') her.once('grin');
    speak(null, pose === 'perch' ? 'Good spot, choom.' : pose === 'hang' ? 'Just hanging out.' : 'Stuck the landing.');
  };
  const fly = dt => {
    if (!flight || still || !dt) return;
    const low = floor - size * 0.99;
    flight.vy += 1800 * dt;
    x += flight.vx * dt;
    y += flight.vy * dt;
    const left = -size * 0.2;
    const right = across - size * 0.8;
    if (x < left || x > right) { x = clamp(x, left, right); flight.vx *= -0.65; }
    if (y < -size * 0.2) { y = -size * 0.2; flight.vy = Math.abs(flight.vy) * 0.6; }
    flight.vx *= Math.pow(0.96, dt * 60);
    if (y >= low) {
      y = low;
      if (flight.vy > 180) { flight.vy *= -0.35; flight.vx *= 0.7; }
      else { flight = null; her.hold('idle'); her.once('grin'); speak(null, 'Still in one piece, choom.'); }
    }
  };
  let pokes = [];
  const poke = () => {
    touched();
    const now = performance.now();
    pokes = pokes.filter(at => now - at < 3500);
    pokes.push(now);
    const annoyed = pokes.length >= 5;
    fetchStrips(['grin', 'blink_glitch', 'dangle']);
    if (!asleep && !flight) her.once(pose === 'lie' ? 'laydown_blink' : ['grin', 'blink_glitch', 'dangle'][Math.floor(Math.random() * 3)]);
    return annoyed ? 'Okay choom. Hands to yourself.' : ['Hey. Felt that.', 'You rang, choom?', 'Poke received.', 'Something on my face?'][Math.floor(Math.random() * 4)];
  };
  let clickTimer = 0;
  her.el.addEventListener('pointerdown', event => {
    if (event.button || hopping) return;
    touched();
    if (flight) { flight = null; her.hold(restClip()); }
    fetchStrips(['dangle', 'grin', 'glitch', 'perch_glitch']);
    pillUntil = pillUntil === Infinity ? Infinity : performance.now() + 4000;
    drag = { id: event.pointerId, startX: event.clientX, startY: event.clientY, originX: x, originY: y, lastX: event.clientX, lastY: event.clientY, lastAt: performance.now(), vx: 0, vy: 0, moved: false };
    her.el.setPointerCapture(event.pointerId);
  });
  her.el.addEventListener('pointermove', event => {
    if (!drag || drag.id !== event.pointerId) return;
    const dx = event.clientX - drag.startX;
    const dy = event.clientY - drag.startY;
    const now = performance.now();
    const elapsed = Math.max(8, now - drag.lastAt) / 1000;
    drag.vx = clamp((event.clientX - drag.lastX) / elapsed, -1800, 1800);
    drag.vy = clamp((event.clientY - drag.lastY) / elapsed, -1600, 1600);
    drag.lastX = event.clientX;
    drag.lastY = event.clientY;
    drag.lastAt = now;
    if (!drag.moved && dx * dx + dy * dy > 25) {
      drag.moved = true;
      clearTimeout(clickTimer);
      if (still) return;
      wake(true);
      if (riding) setRide(false);
      seat = null;
      pose = 'stand';
      wins.player.classList.add('free');
      talkbox.classList.add('off');
      her.el.classList.add('dragging');
      her.hold('dangle');
    }
    if (!drag.moved || still) return;
    touched();
    x = drag.originX + dx;
    y = drag.originY + dy;
  });
  her.el.addEventListener('pointerup', event => {
    if (!drag || drag.id !== event.pointerId) return;
    const moved = drag.moved;
    const velocity = performance.now() - drag.lastAt < 120 ? { x: drag.vx, y: drag.vy } : null;
    drag = null;
    if (moved) { drop(event, velocity); return; }
    clearTimeout(clickTimer);
    const reply = poke();
    clickTimer = setTimeout(() => { if (!riding) speak(null, reply); }, 230);
  });
  const cancelDrag = () => {
    if (!drag) return;
    const moved = drag.moved;
    drag = null;
    her.el.classList.remove('dragging');
    if (moved && !still) drop({});
  };
  her.el.addEventListener('pointercancel', cancelDrag);
  her.el.addEventListener('lostpointercapture', cancelDrag);
  her.el.addEventListener('dblclick', event => {
    event.preventDefault();
    clearTimeout(clickTimer);
    if (hopping) return;
    touched();
    if (asleep) { wake(true); speak(null, 'Hey choom. Miss me?'); }
    else { nap(true); speak(null, lines.sleep); }
  });
  her.el.addEventListener('keydown', event => {
    const step = { ArrowLeft: [-16, 0], ArrowRight: [16, 0], ArrowUp: [0, -16], ArrowDown: [0, 16] }[event.key];
    if (step) {
      event.preventDefault();
      touched();
      if (still) { speak(null, 'Motion is off. Still here with you, choom.'); return; }
      flight = null;
      wake(true);
      if (riding) setRide(false);
      if (seat || pose !== 'stand') { seat = null; pose = 'stand'; wins.player.classList.add('free'); her.hold('idle'); }
      x += step[0];
      y += step[1];
    }
    if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); speak(null, poke()); }
  });
  her.el.addEventListener('focus', () => { pillUntil = pillUntil === Infinity ? Infinity : performance.now() + 4000; });
  document.addEventListener('keydown', event => {
    if (riding && event.ctrlKey && event.altKey && (event.code === 'KeyC' || event.key.toLowerCase() === 'c')) { event.preventDefault(); setRide(false); }
  });

  // Her windows drag too, and she rides along when one she's sitting on moves.
  const showWindow = key => {
    const win = wins[key];
    win.classList.remove('gone');
    win.inert = false;
    if (win.offsetWidth < 220) win.style.width = Math.min(across - 16, 260) + 'px';
    win.style.left = clamp(win.offsetLeft, 8, Math.max(8, across - win.offsetWidth - 8)) + 'px';
    win.style.top = clamp(win.offsetTop, 8, Math.max(8, floor - win.offsetHeight - 8)) + 'px';
    tasks[key].classList.add('on');
    if (key === 'log') tasks.log.setAttribute('aria-expanded', 'true');
  };
  for (const [key, win] of Object.entries(wins)) {
    let held = null;
    win.addEventListener('pointerdown', event => {
      if (event.button || !event.target.closest('.bar') || event.target.closest('button')) return;
      touched();
      if (still) { speak(null, 'Window stays put while motion is off, choom.'); return; }
      held = { id: event.pointerId, dx: event.clientX - win.offsetLeft, dy: event.clientY - win.offsetTop };
      win.setPointerCapture(event.pointerId);
      win.classList.add('held');
    });
    win.addEventListener('pointermove', event => {
      if (!held || held.id !== event.pointerId || still) return;
      touched();
      win.style.left = clamp(event.clientX - held.dx, 0, across - win.offsetWidth) + 'px';
      win.style.top = clamp(event.clientY - held.dy, 8, floor - win.offsetHeight) + 'px';
    });
    const letGo = () => { held = null; win.classList.remove('held'); };
    win.addEventListener('pointerup', letGo);
    win.addEventListener('pointercancel', letGo);
    win.addEventListener('lostpointercapture', letGo);
    $('.close', win).addEventListener('click', () => {
      touched();
      win.classList.add('gone');
      win.inert = true;
      tasks[key].classList.remove('on');
      if (key === 'log') tasks.log.setAttribute('aria-expanded', 'false');
      if (key === 'player') setPlaying(false);
      if (seat === key && !still) { seat = null; pose = 'stand'; y = floor - size * 0.99; her.hold('idle'); }
      speak(null, 'Closed. Taskbar brings it back, choom.');
    });
  }
  $('#task-notes').addEventListener('click', () => {
    touched();
    showWindow('notes');
    speak(null, 'Your notes, choom. Make yourself at home.');
    $('#me-notes').focus();
  });
  let visitor = 'choom';
  let nameTimer = 0;
  $('#me-notes').addEventListener('input', event => {
    touched();
    clearTimeout(nameTimer);
    const match = event.target.value.match(/^[ \t]*name:[ \t]*([^\r\n]+)$/im);
    const name = match ? match[1].trim().slice(0, 60) : '';
    if (!name || name === visitor) return;
    nameTimer = setTimeout(() => { visitor = name; speak(null, 'Got it, ' + visitor + '.'); }, 900);
  });

  $('#task-capri').addEventListener('click', () => {
    setRide(false);
    drag = null;
    flight = null;
    wake(true);
    holdUntil = 0;
    her.el.classList.remove('gone', 'dragging');
    shownScene = '';
    status.textContent = 'Capri is back.';
    if (still) speak(null, 'Right here, ' + visitor + '.');
    else go(scene);
  });

  // ---------- Her player ----------
  // The song plays from its official YouTube upload, and only once a visitor asks for it. YouTube's rules need the player on show while
  // it plays, at least 200px each way, with nothing in front of it, so it gets its own box above everything else. This object answers to
  // the same names an audio element does, so the rest of her music code stays the same.
  const songBox = $('#song-box');
  // On a desktop it sits under her music player, or beside it on a short screen, clear of the player and her taskbar. Phones keep the
  // spot in the stylesheet, under her stage.
  const placeSongBox = () => {
    if (innerWidth <= 900) { songBox.style.left = songBox.style.top = songBox.style.bottom = ''; return; }
    const plate = wins.player.getBoundingClientRect();
    const box = songBox.getBoundingClientRect();
    const below = plate.bottom + 24 + box.height <= innerHeight - 60;
    songBox.style.left = clamp(below ? plate.left : plate.left - box.width - 16, 16, innerWidth - box.width - 16) + 'px';
    songBox.style.top = clamp(below ? plate.bottom + 24 : plate.bottom - box.height, 16, innerHeight - box.height - 16) + 'px';
    songBox.style.bottom = 'auto';
  };
  window.addEventListener('resize', placeSongBox);
  const song = (() => {
    const events = new EventTarget();
    const length = 175;
    let player = null;
    let loading = null;
    let ready = false;
    let wanted = false;
    let going = false;
    let state = -1;
    let clock = 0;
    let clockAt = 0;
    let startAt = 0;
    let poll = 0;
    let stuck = 0;
    const tell = name => events.dispatchEvent(new Event(name));
    const read = () => {
      if (!ready) return;
      const seconds = player.getCurrentTime() || 0;
      if (seconds !== clock) { clock = seconds; clockAt = performance.now(); }
    };
    const halt = () => {
      wanted = false;
      going = false;
      clearInterval(poll);
      clearTimeout(stuck);
      read();
      songBox.classList.add('off');
    };
    const changed = next => {
      state = next;
      if (next === 1) {
        if (!wanted) { player.pauseVideo(); return; }
        clearTimeout(stuck);
        clearInterval(poll);
        placeSongBox();
        going = true;
        read();
        poll = setInterval(() => { read(); tell('timeupdate'); }, 250);
        tell('play');
        tell('playing');
      } else if (next === 3) tell('waiting');
      else if (next === 0) { halt(); tell('ended'); }
      // A seek can pass through paused, so only a pause that lasts counts. This also catches a tap on the video itself.
      else if (next === 2) setTimeout(() => { if (state === 2 && going) { halt(); tell('pause'); } }, 300);
    };
    const load = () => loading || (loading = new Promise((done, fail) => {
      const late = setTimeout(() => fail(new Error('YouTube took too long')), 9000);
      const make = () => {
        player = new YT.Player('song-video', {
          host: 'https://www.youtube-nocookie.com',
          videoId: 'gKoaAow1XLY',
          width: '100%',
          height: '100%',
          playerVars: { controls: 0, disablekb: 1, fs: 0, iv_load_policy: 3, playsinline: 1, rel: 0, origin: location.origin },
          events: {
            onReady: () => {
              clearTimeout(late);
              ready = true;
              player.getIframe().title = "I'm In Love With Myself on YouTube";
              if (startAt) player.seekTo(startAt, true);
              tell('durationchange');
              done();
            },
            onStateChange: event => changed(event.data),
            onError: () => { halt(); tell('error'); }
          }
        });
      };
      if (window.YT && YT.Player) { make(); return; }
      window.onYouTubeIframeAPIReady = make;
      const script = document.createElement('script');
      script.src = 'https://www.youtube.com/iframe_api';
      script.onerror = () => { clearTimeout(late); fail(new Error('YouTube was blocked')); };
      document.head.append(script);
    }).catch(error => { loading = null; throw error; }));
    return {
      addEventListener: (name, listener) => events.addEventListener(name, listener),
      get paused() { return !going; },
      get ended() { return state === 0; },
      get readyState() { return ready ? 4 : 0; },
      get duration() { return ready && player.getDuration() || length; },
      get currentTime() {
        read();
        return going && state === 1 ? clock + (performance.now() - clockAt) / 1000 : clock;
      },
      set currentTime(seconds) {
        clock = seconds;
        clockAt = performance.now();
        if (ready) player.seekTo(seconds, true);
        else startAt = seconds;
        tell('seeked');
      },
      play() {
        wanted = true;
        placeSongBox();
        songBox.classList.remove('off');
        clearTimeout(stuck);
        // Phones can refuse a scripted start, and an ad can run first. Either way the video in the box still answers a tap.
        stuck = setTimeout(() => { if (wanted && !going) speak(null, "If it doesn't start, tap the video, choom."); }, 4000);
        return load().then(() => { if (wanted) player.playVideo(); }).catch(error => { halt(); throw error; });
      },
      pause() {
        if (ready) player.pauseVideo();
        halt();
      }
    };
  })();
  const seek = $('#player-seek');
  let playing = false;
  let beats = [];
  let nextBeat = 0;
  let beatClock = 0;
  let beatAt = -1e9;
  let beatCount = 0;
  let beatLength = 500;
  let fallbackBeat = true;
  let musicReady = false;
  let audioWaiting = false;
  let cursorTimer = 0;
  let danceUntil = 0;
  let lyingMusic = null;
  let musicTransition = false;
  const musicRests = new Map();
  const musicNotes = new Map();
  const dancePoses = now => {
    const on = playing || now < danceUntil;
    const free = !asleep && !drag && !flight && !hopping;
    if (musicTransition && her.name !== 'laydown_leave') { musicTransition = false; lyingMusic = null; }
    if (on && free && pose === 'lie' && !musicTransition) {
      lyingMusic = { seat, scene: shownScene };
      musicTransition = true;
      her.once('laydown_leave', () => {
        musicTransition = false;
        if ((!playing && performance.now() >= danceUntil) || pose !== 'lie' || asleep || drag || flight) { lyingMusic = null; return; }
        pose = 'perch';
        wins.player.classList.toggle('free', seat !== 'player');
        her.hold(ready.has('perch_music') ? 'perch_music' : 'perch');
        place();
      });
    }
    if (!on && lyingMusic && !musicTransition) {
      const back = lyingMusic;
      lyingMusic = null;
      if (free && seat === back.seat && shownScene === back.scene && pose === 'perch') {
        pose = 'lie';
        wins.player.classList.add('free');
        her.hold('laydown');
        place();
      }
    }
    for (const one of actors) {
      const available = one !== her || (free && pose !== 'lie' && !musicTransition);
      if (on && available) {
        if (!musicRests.has(one)) musicRests.set(one, one.rest);
        const sitting = one === her ? pose === 'perch' : one.el.classList.contains('perch');
        const clip = sitting ? (ready.has('perch_music') ? 'perch_music' : 'perch') : 'idle';
        if (one.loop && one.name !== clip) one.hold(clip);
      } else if (musicRests.has(one)) {
        const previous = musicRests.get(one);
        musicRests.delete(one);
        if (one.loop && ['idle', 'perch', 'perch_music'].includes(one.name)) one.hold(one === her ? restClip() : previous);
      }
    }
  };
  const duration = () => Number.isFinite(song.duration) ? song.duration : 0;
  const stamp = seconds => Math.floor(seconds / 60) + ':' + String(Math.floor(seconds % 60)).padStart(2, '0');
  const paintSeek = () => {
    const length = duration();
    const seconds = song.currentTime || 0;
    const share = (length ? seconds / length * 100 : 0) + '%';
    wins.player.style.setProperty('--played', share);
    seek.style.setProperty('--played', share);
    seek.max = length || 100;
    seek.value = seconds;
    seek.disabled = !length;
    seek.setAttribute('aria-valuetext', stamp(seconds) + ' of ' + stamp(length));
    $('#player-time').textContent = stamp(seconds);
    $('#player-duration').textContent = stamp(length);
    for (const clock of $$('.player .time', swarm)) clock.textContent = stamp(seconds);
  };
  const clearBeat = () => {
    for (const one of actors) {
      one.nod = 0;
      one.drawNod('', 0, -1);
    }
    for (const notes of musicNotes.values()) for (const note of notes) note.el.remove();
    musicNotes.clear();
    beatAt = -1e9;
  };
  const addMusicNote = (one, now) => {
    const notes = musicNotes.get(one) || [];
    if (notes.length >= 5) return;
    const el = document.createElement('span');
    el.className = 'music-note';
    el.setAttribute('aria-hidden', 'true');
    el.textContent = Math.random() < .5 ? '\u266a' : '\u266b';
    one.el.append(el);
    notes.push({ el, born: now, side: beatCount % 4 < 2 ? 1 : -1, seed: Math.random() * Math.PI * 2 });
    musicNotes.set(one, notes);
  };
  const drawMusicNotes = now => {
    for (const [one, notes] of musicNotes) {
      const sitting = one.name === 'perch_music';
      for (const note of notes) {
        const t = (now - note.born) / 1500;
        if (t >= 1) { note.el.remove(); continue; }
        const pop = t < .08 ? t / .08 * 1.3 : t < .18 ? 1.3 - (t - .08) / .1 * .3 : 1;
        const left = 50 + note.side * ((sitting ? 33 : 36) + 5 * t + 3.5 * Math.sin(t * 5 + note.seed));
        const top = 50 - (sitting ? 10 : 25) - 28 * t;
        note.el.style.left = left + '%';
        note.el.style.top = top + '%';
        note.el.style.opacity = t < .65 ? 1 : (1 - t) / .35;
        note.el.style.transform = 'translate(-50%, -50%) rotate(' + note.side * (12 + 10 * Math.sin(note.seed + t * 4)) + 'deg) scale(' + pop + ')';
      }
      const remaining = notes.filter(note => now - note.born < 1500);
      if (remaining.length) musicNotes.set(one, remaining);
      else musicNotes.delete(one);
    }
  };
  const syncPlaying = () => {
    playing = !song.paused && !song.ended;
    wins.player.classList.toggle('paused', !playing);
    playButton.setAttribute('aria-label', playing ? 'Pause song' : 'Play song');
    if (!playing) {
      clearBeat();
    }
    dancePoses(performance.now());
    paintSeek();
  };
  // A page can't read the sound inside a YouTube player, so her bounce follows beats worked out from the track ahead of time.
  const prepareMusic = () => {
    if (musicReady) return;
    musicReady = true;
    fetch('assets/music/beats.json').then(answer => answer.json()).then(times => { beats = times; }).catch(() => { musicReady = false; });
  };
  const setPlaying = (on, user = false) => {
    if (!on) { danceUntil = 0; song.pause(); syncPlaying(); return; }
    if (!user) return;
    prepareMusic();
    fetchStrips(['idle', 'perch_music', 'laydown_leave']);
    if (song.ended) song.currentTime = 0;
    song.play().catch(() => {
      syncPlaying();
      speak(null, "Couldn't start the song, choom. Try play again.");
    });
  };
  const musicBeat = now => {
    dancePoses(now);
    const dancing = now < danceUntil;
    if (still || (!playing && !dancing) || (playing && audioWaiting) || document.hidden) { clearBeat(); return; }
    let beat = false;
    fallbackBeat = !playing || !beats.length;
    if (!fallbackBeat) {
      const at = song.currentTime;
      if (at < beatClock || at > beatClock + 2) {
        const next = beats.findIndex(time => time > at);
        nextBeat = next < 0 ? beats.length : next;
      }
      beatClock = at;
      while (nextBeat < beats.length && beats[nextBeat] <= at) { nextBeat++; beat = true; }
    }
    if (fallbackBeat) beat = now - beatAt >= 500;
    if (beat) {
      const gap = now - beatAt;
      beatLength = gap < 1500 ? clamp(gap, 280, 1200) : 500;
      beatAt = now;
      beatCount++;
      if (beatLength >= 550 || beatCount % 2 === 0) for (const one of actors) {
        if (live.has(one.host) && !one.el.classList.contains('gone') && ['idle', 'perch_music'].includes(one.name)) addMusicNote(one, now);
      }
    }
    const phase = clamp((now - beatAt) / beatLength, 0, 1);
    const rise = phase < .45 ? 1 - phase / .45 : 0;
    const fall = phase > .75 ? (phase - .75) / .25 : 0;
    for (const one of actors) one.nod = one === her && (asleep || drag || flight || hopping) ? 0 : .28 * (rise * rise + fall * fall);
    drawMusicNotes(now);
  };
  for (const name of ['loadedmetadata', 'durationchange', 'timeupdate', 'seeked']) song.addEventListener(name, paintSeek);
  for (const name of ['play', 'pause', 'ended']) song.addEventListener(name, syncPlaying);
  song.addEventListener('waiting', () => { audioWaiting = true; clearBeat(); });
  song.addEventListener('playing', () => { audioWaiting = false; });
  song.addEventListener('error', () => { song.pause(); syncPlaying(); speak(null, "Song's missing or won't load, choom."); });
  seek.addEventListener('input', () => { touched(); if (duration()) song.currentTime = +seek.value; paintSeek(); });
  seek.addEventListener('change', () => speak(null, 'Right there, choom.'));
  for (const button of [$('#player-prev'), $('#player-next')]) button.addEventListener('click', () => {
    touched();
    if (song.readyState) song.currentTime = 0;
    paintSeek();
    speak(null, 'From the top, choom.');
  });
  playButton.addEventListener('click', event => {
    touched();
    const on = song.paused || song.ended;
    setPlaying(on, event.isTrusted);
    speak(null, on ? lines.resumed : lines.paused);
  });
  $('#task-music').addEventListener('click', event => { touched(); openPlayer(event.isTrusted); speak(null, lines.opened); });
  // Only visitor actions start sound. Scene changes just reveal her player.
  const openPlayer = (user = false) => {
    showWindow('player');
    $('#task-music').classList.add('on', 'lit');
    if (user) setPlaying(true, true);
    playButton.classList.add('lit');
    if (user) cursorPress(playButton);
  };

  // One cursor job at a time. Its tip starts at her and lands on the actual control.
  let cursorJob = null;
  let cursorPressed = null;
  const finishCursor = (complete = false) => {
    clearTimeout(cursorTimer);
    cursorTimer = 0;
    if (cursorPressed) cursorPressed.classList.remove('capri-pressed');
    cursorPressed = null;
    pink.classList.remove('on', 'pressing');
    const job = cursorJob;
    cursorJob = null;
    if (job) (complete ? job.done : job.cancel)();
  };
  const cursorPress = (element, done = () => {}, cancel = () => {}) => {
    finishCursor();
    if (!element || still) { done(); return; }
    const box = element.getBoundingClientRect();
    if (!box.width || !box.height) { done(); return; }
    const origin = stage.getBoundingClientRect();
    const tip = tipOf();
    pink.style.transition = 'none';
    pink.style.transform = 'translate(' + clamp(origin.left + tip.x, 0, innerWidth - 26) + 'px, ' + clamp(origin.top + tip.y, 0, innerHeight - 34) + 'px)';
    pink.classList.add('on');
    void pink.getBoundingClientRect();
    pink.style.transition = '';
    cursorJob = { done, cancel };
    pink.style.transform = 'translate(' + clamp(box.left + box.width * .5, 0, innerWidth - 26) + 'px, ' + clamp(box.top + box.height * .5, 0, innerHeight - 34) + 'px)';
    cursorTimer = setTimeout(() => {
      cursorPressed = element;
      element.classList.add('capri-pressed');
      pink.classList.add('pressing');
      cursorTimer = setTimeout(() => finishCursor(true), 140);
    }, 650);
  };
  const pageTargets = {
    features: ['desktop', 'Features'], talk: ['talk', 'Talk'], 'your pc': ['hands', 'Your PC'],
    'chaos mode': ['chaos', 'Chaos mode'], numbers: ['proof', 'Numbers'], changelog: ['changelog', 'Changelog'],
    install: ['get', 'Install'], download: ['get', 'Install'], top: ['top', 'Top']
  };
  const pageColumn = $('.col');
  // Her cursor presses a spot on the page when no header link fits the job.
  const pageSpot = $('#page-spot');
  const visibleLink = selector => $$(selector).find(link => link.getClientRects().length && link.getBoundingClientRect().width);
  const revealNavLink = selector => {
    let link = visibleLink('.nav ' + selector);
    const menu = $('.nav .menu');
    if (!link && menu.getClientRects().length) {
      menu.open = true;
      link = visibleLink('.nav .menu ' + selector);
    }
    if (link && link.closest('.menu-links')) link.scrollIntoView({ block: 'nearest', inline: 'nearest', behavior: 'instant' });
    return link || pageSpot;
  };
  const goTo = destination => {
    const target = pageTargets[destination];
    if (!target) return null;
    const [id, label] = target;
    return { reply: id === 'get' ? 'Install is here, choom. You pick the download.' : label + '. On it, choom.', job: () => {
      touched();
      if (id === 'get') $('.os-win [data-os="windows"]').classList.add('capri-lit');
      const link = revealNavLink('a[href="#' + id + '"]');
      cursorPress(link, () => {
        $('.nav .menu').open = false;
        const section = $('#' + id);
        const clearance = wide.matches ? 104 : stage.getBoundingClientRect().height + 76;
        const top = id === 'top' ? 0 : scrollY + section.getBoundingClientRect().top - clearance;
        window.scrollTo({ top: Math.max(0, top), behavior: still ? 'instant' : 'smooth' });
      });
    } };
  };
  const scrollPage = direction => ({ reply: direction > 0 ? 'One screen down, choom.' : 'Back up, choom.', job: () => {
    cursorPress(pageSpot, () => window.scrollBy({ top: direction * innerHeight, behavior: still ? 'instant' : 'smooth' }));
  } });
  let pageMotion = null;
  const movePage = roll => ({ reply: roll ? 'One spin, choom.' : 'Little shake, choom.', job: () => {
    cursorPress(pageSpot, () => {
      if (pageMotion) pageMotion.cancel();
      if (still) return;
      // Rotate around the visible part of the column, even deep into the page.
      pageColumn.style.transformOrigin = '50% ' + (innerHeight / 2 - pageColumn.getBoundingClientRect().top) + 'px';
      pageMotion = pageColumn.animate(roll ? [{ transform: 'rotate(0deg)' }, { transform: 'rotate(360deg)' }] :
        [{ transform: 'translateX(0)' }, { transform: 'translateX(-7px)' }, { transform: 'translateX(7px)' }, { transform: 'translateX(-4px)' }, { transform: 'translateX(4px)' }, { transform: 'translateX(0)' }],
      { duration: roll ? 800 : 400, easing: 'ease-in-out' });
    });
  } });
  const openSite = name => {
    const urls = { help: 'https://capriai.app/help/', setup: 'https://capriai.app/setup/', discord: 'https://discord.gg/capridesktop', github: 'https://github.com/lachydotmcg/capri-desktop' };
    const url = urls[name];
    return { reply: 'Opening ' + (name === 'github' ? 'GitHub' : name === 'discord' ? 'Discord' : name) + ', choom.', job: () => {
      const link = revealNavLink('a[href="' + url + '"]');
      cursorPress(link, () => {
        $('.nav .menu').open = false;
        // Open after the visible press, while the typed gesture is still recent.
        const tab = window.open('about:blank', '_blank');
        if (tab) { tab.opener = null; tab.location.replace(url); }
        else location.assign(url);
      });
    } };
  };

  // ---------- Talking to her ----------
  const mouthFor = (letter, step) => {
    const lower = letter.toLowerCase();
    if ('aeo'.includes(lower)) return letter !== lower ? 4 : 3;
    if ('iuy'.includes(lower)) return 2;
    if ('mbp'.includes(lower) || !/\p{L}/u.test(letter)) return 0;
    return step % 2 ? 1 : 2;
  };
  let talking = false;
  let talkStart = 0;
  let answer = '';
  let typing = 0;
  let thinking = 0;
  let spokenBoxes = [];
  her.mouth = (now, strip) => {
    if (!talking || still || strip !== 'idle' || !ready.has('mouth')) return -1;
    const step = Math.floor((now - talkStart) / 1000 * 12);
    const letter = Math.floor(step / 12 * 28);
    return letter < answer.length ? mouthFor(answer[letter], step) : -1;
  };
  smalls[0].her.mouth = her.mouth;
  const stopTalk = () => { clearTimeout(thinking); clearInterval(typing); thinking = typing = 0; talking = false; her.talkFrom = null; };
  const speak = (heard, reply, extra = [], links = [], turn = null, answered = true) => {
    stopTalk();
    // The log is the conversation: the page's own scene lines and her asides stay out of it.
    if (!staged && (heard !== null || turn !== null)) {
      if (turn === null) { turn = newTurn(); logLine('user', heard, turn); }
      logLine('assistant', reply, turn, answered);
    }
    answer = reply;
    const boxes = [talkbox, $('.talkbox', mini), ...extra];
    spokenBoxes = boxes;
    talkbox.classList.remove('off');
    fetchStrips(['mouth', 'laydown_talk']);
    for (const box of boxes) {
      box.hidden = false;
      $('.heard', box).textContent = heard === null ? '' : '> you: ' + heard;
      $('.heard', box).hidden = heard === null;
      let buttons = $('.help-links', box);
      if (!buttons) { buttons = document.createElement('div'); buttons.className = 'help-links'; box.append(buttons); }
      buttons.replaceChildren();
      for (const item of links) {
        if (buttons.childElementCount >= 3) break;
        if (!item || typeof item.href !== 'string' || typeof item.title !== 'string') continue;
        try {
          const url = new URL(item.href, location.href);
          const localSection = url.origin === location.origin && url.pathname === location.pathname && url.hash;
          if ((!localSection && !['http:', 'https:'].includes(url.protocol)) || url.username || url.password) continue;
          const link = document.createElement('a');
          link.href = url.href;
          link.textContent = item.title.slice(0, 80);
          const destination = Object.keys(pageTargets).find(key => localSection && url.hash === '#' + pageTargets[key][0]);
          if (destination) link.addEventListener('click', event => { event.preventDefault(); respond('show me ' + destination, event.isTrusted); });
          else link.target = '_blank';
          link.rel = 'noopener noreferrer';
          buttons.append(link);
        } catch {}
      }
      box.style.minHeight = '';
      $('.words', box).textContent = reply;
      box.style.minHeight = box.offsetHeight + 'px';
      if (box.matches('.echo')) edge(box);
      if (!still) $('.words', box).textContent = '';
    }
    dirty = true;
    status.textContent = 'Capri says: ' + reply;
    layoutFrames();
    if (still) return;
    thinking = setTimeout(() => {
      thinking = 0;
      if (still) { for (const box of boxes) $('.words', box).textContent = reply; return; }
      talking = true;
      talkStart = time;
      her.talkFrom = time;
      let shown = 0;
      typing = setInterval(() => {
        shown = Math.min(reply.length, shown + 1);
        for (const box of boxes) $('.words', box).textContent = reply.slice(0, shown);
        if (shown === reply.length) { clearInterval(typing); typing = 0; talking = false; her.talkFrom = null; }
      }, 1000 / 28);
    }, 500);
  };
  const clockLine = () => {
    const now = new Date();
    return "It's " + now.toLocaleTimeString('en-US', { hour: 'numeric', minute: '2-digit' }) + ' on ' + now.toLocaleDateString('en-GB', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' }).replace(',', '') + '.';
  };
  const comeHere = () => {
    if (hopping || still) return;
    touched();
    flight = null;
    wake(true);
    const mouse = mouseAt();
    hopping = true;
    teleport(() => {
      seat = null;
      pose = 'stand';
      wins.player.classList.add('free');
      x = mouse.x - size * 0.5;
      y = mouse.y - size * 0.4;
    }, () => { hopping = false; });
  };
  // Local answers are kept small and drawn only from knowledge.md. No request is needed for these.
  const helpLinks = [
    { title: 'Help', href: 'https://capriai.app/help/' },
    { title: 'Discord', href: 'https://discord.gg/capridesktop' }
  ];
  const helpTable = [
    [/\b(mac|macos|apple|silicon|intel)\b/i, 'Mac needs macOS 15 or newer, choom. Grab the Apple Silicon or Intel zip. The Help page has the first-open steps.'],
    [/\b(linux|wayland|x11|appimage)\b/i, 'Linux is an early preview, choom. AppImage or tarball, with Ollama for my brain. On Wayland I sit at the bottom of your screen.'],
    [/\b(ram|memory requirements|requirements|specs|graphics|disk space)\b/i, 'Local brain: 8 GB RAM, 16 is better, and 3 to 5 GB space. With a brain key: 4 GB RAM and under 100 MB, choom.'],
    [/\b(install|download|windows|setup)\b/i, 'On Windows 10 or 11, 64-bit, grab Capri-Setup.exe and open it, choom. The installer has everything.'],
    [/\b(cost|price|free|pay|subscription)\b/i, "I'm free, choom. My built-in brain and free voice run on your PC. Some optional cloud keys need paid credits."],
    [/\b(offline|internet|connection)\b/i, 'My built-in brain and free voice run on your PC, choom. A brain from a key needs internet.'],
    [/\b(brain|model|ollama|nvidia)\b/i, 'Open Capri Mainframe, then BRAIN > Set up her brain, choom. On Mac and Linux my brain uses Ollama.'],
    [/\b(voice|microphone|mic|speak|talk out loud)\b/i, 'My free voice runs on your PC, choom. Get it in Mainframe under VOICE > Get free voice. The wave button starts voice chat.'],
    [/\b(startup|start with windows|auto.?start)\b/i, 'Mainframe > Settings > HABITS > Start with Windows, choom. Turn that off if you want me to wait.'],
    [/\b(stuck|cursor|hop off|gone|missing|bring.*back)\b/i, 'Stuck on your cursor? Ctrl + Alt + C, choom. If I went missing, double-click my tray icon or open Capri again.'],
    [/\b(type|typing|keyboard|chat)\b/i, 'On your PC, the pencil opens typing, choom. Ctrl + Alt + T does it too.'],
    [/\b(notes|me\.md|remember|name)\b/i, 'Mainframe > Settings > YOU > Open her notes edits me.md, choom. That keeps what you want me to know about you.'],
    [/\b(music|dance|spotify|player)\b/i, 'Turn on Dance to music in Settings, choom. The app needs to show up as music in Windows.'],
    [/\b(clones?|chaos)\b/i, 'Clones are a Windows beta, choom. Hold Esc to clear them.'],
    [/\b(mainframe|settings|menu)\b/i, 'Right-click me > Open Capri Mainframe, choom. My tray icon opens it too.']
  ];
  const offlineHelp = (text, earlier = []) => {
    // Skip earlier platform-only follow-ups when looking for their original topic.
    const platformOnly = /^(?:(?:and|what about|how about|also)\s+)?(?:on |for )?(mac|macos|apple|silicon|intel|linux|wayland|x11|appimage|windows)[?!.\s]*$/i;
    const followUp = platformOnly.test(text.trim());
    const previous = [...earlier].reverse().find(line => line.role === 'user' && !platformOnly.test(line.text.trim()) && helpTable.slice(2).some(([match]) => match.test(line.text)));
    const question = followUp && previous ? previous.text : text;
    const topic = helpTable.slice(2).find(([match]) => match.test(question));
    const platform = helpTable.slice(0, 2).find(([match]) => match.test(text));
    const found = topic || platform;
    let reply = found ? found[1] : 'Try the Help page or ask in Discord, choom.';
    let answered = !!found;
    if (platform && topic) {
      if (topic === helpTable[3]) reply = platform[1];
      else if (topic === helpTable[6]) reply = 'On Mac and Linux my brain uses Ollama, choom. Install it, then run ollama pull gemma4:e4b.';
      else if (topic === helpTable[7]) reply = 'On Mac and Linux, right-click me > Settings > Voice, choom. Pick a local speech server or a cloud voice.';
      else if ([helpTable[2], helpTable[8], helpTable[12]].includes(topic)) { reply = 'For that on ' + (platform === helpTable[0] ? 'Mac' : 'Linux') + ', check the Help page, choom. It has the preview steps.'; answered = false; }
      else reply += ' ' + platform[1];
    }
    const destination = topic === helpTable[3] || (!topic && platform) ? 'install' : null;
    return { reply, links: destination ? [{ title: 'Install', href: '#get' }, ...helpLinks] : helpLinks, answered, destination };
  };
  const helpAddress = () => {
    const query = new URLSearchParams(location.search);
    if (['localhost', '127.0.0.1', '[::1]'].includes(location.hostname) && query.has('helpapi')) {
      try {
        const url = new URL(query.get('helpapi'));
        return ['http:', 'https:'].includes(url.protocol) && !url.username && !url.password ? url.href : null;
      } catch { return null; }
    }
    return ['capriai.app', 'lachydotmcg.github.io'].includes(location.hostname) ? 'https://capri-reactions.lachydotmcg.workers.dev/help' : null;
  };
  const askHelp = async (text, earlier = earlierTurns(), turn = null) => {
    if (helpBusy) return null;
    helpBusy = true;
    her.el.classList.add('thinking');
    talkbox.setAttribute('aria-busy', 'true');
    $('#typer').setAttribute('aria-busy', 'true');
    speak(text, 'Lemme check, choom.', [], [], turn, false);
    let result = offlineHelp(text, earlier);
    const address = helpAddress();
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 12000);
    try {
      if (address) {
        const response = await fetch(address, {
          method: 'POST', headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ question: text, history: earlier }),
          credentials: 'omit', redirect: 'error', signal: controller.signal
        });
        if (!response.ok) throw new Error('Unavailable');
        const data = await response.json();
        if (!data || typeof data.reply !== 'string' || !data.reply.trim() || typeof data.answered !== 'boolean') throw new Error('No reply');
        result = { reply: data.reply.trim().replace(/\u2014/g, ',').slice(0, 1500), links: Array.isArray(data.links) ? data.links : [], answered: data.answered };
      }
    } catch { result = offlineHelp(text, earlier); }
    finally {
      clearTimeout(timeout);
      helpBusy = false;
      touched();
      her.el.classList.remove('thinking');
      talkbox.removeAttribute('aria-busy');
      $('#typer').removeAttribute('aria-busy');
    }
    return result;
  };
  // Scripted stage commands stay local. Other questions go to her support answers.
  const act = (said, user = false, earlier = earlierTurns(), turn = null, typed = false) => {
    const text = said.toLowerCase().replace(/[?!.]+$/, '').trim();
    const destination = text.match(/^(?:show me|take me to|go to)\s+(?:the\s+)?(features|talk|your pc|chaos(?: mode)?|numbers|changelog|install|download|top)$/);
    if (destination) return goTo(destination[1] === 'chaos' ? 'chaos mode' : destination[1]);
    if (/^(?:download|install)(?: capri)?$/.test(text)) return goTo('install');
    if (/^(?:scroll down|keep going)$/.test(text)) return scrollPage(1);
    if (/^(?:scroll up|go back up)$/.test(text)) return scrollPage(-1);
    if (/^shake the page$/.test(text)) return movePage(false);
    if (/^(?:flip the page|do a barrel roll)$/.test(text)) return movePage(true);
    const external = text.match(/^open (help|setup|discord|github)$/);
    if (external) return typed && user ? openSite(external[1]) : 'Type that ask, choom. Then I can open it.';
    const clones = text.match(/\bmake (\d+) clones?\b/);
    if (clones) { const count = clamp(+clones[1], 1, 7); spawn(count); return lines.more(count); }
    if (/\b(clone yourself|chaos mode)\b/.test(text)) { spawn('all'); return lines.multiply; }
    if (/\b(come here|teleport)\b/.test(text)) { comeHere(); return still ? 'Right here, ' + visitor + '.' : lines.come; }
    if (/\b(go to sleep|take a nap|night night|goodnight)\b/.test(text)) { touched(); nap(true); return lines.sleep; }
    if (/\b(wake up)\b/.test(text)) { touched(); wake(true); return 'Hey ' + visitor + '. Miss me?'; }
    if (/^poke\b/.test(text)) return poke();
    if (/^dance$/.test(text)) {
      touched(); wake(true); fetchStrips(['perch_music', 'idle', 'laydown_leave']);
      if (!still) { danceUntil = performance.now() + 3000; beatAt = -1e9; }
      return still ? 'Dancing in spirit, choom. Motion is off.' : 'Little dance break, choom. Play a song and keep it going.';
    }
    if (/^(who are you|what can you do)$/.test(text)) return { reply: 'Poke me. Play the song. Ask about setup. I can show sections, scroll, shake or spin the page. Type open help, setup, Discord or GitHub. Downloads are your call.', links: [{ title: 'Chaos mode', href: '#chaos' }, { title: 'Install', href: '#get' }, { title: 'Features', href: '#desktop' }] };
    if (/\b(ride (my|the|your) cursor|cursor ride)\b/.test(text)) { touched(); setRide(true); return still ? 'Cursor ride is resting while motion is off, choom.' : 'Your cursor, my ride. Ctrl + Alt + C to hop off.'; }
    if (/\b(hop off|stop riding)\b/.test(text)) { touched(); setRide(false); return lines.off; }
    if (/\b(open spotify|play (a|some|my|the) (song|music))\b/.test(text)) { touched(); openPlayer(user); return lines.opened; }
    if (/what'?s playing/.test(text)) return lines.playing;
    if (/^(pause|stop the music)\b/.test(text)) { touched(); setPlaying(false); return lines.paused; }
    if (/^(play|resume)\b/.test(text)) { touched(); setPlaying(true, user); return lines.resumed; }
    if (/\bwhat time\b/.test(text)) return clockLine();
    if (/^(hi|hey|hello|yo|sup)( capri)?$/.test(text)) return visitor === 'choom' ? lines.hi : 'Hey ' + visitor + '. Need a hand?';
    return askHelp(said, earlier, turn);
  };
  const respond = async (text, user, extra = [], typed = false) => {
    if (helpBusy) { status.textContent = 'Capri is thinking. One question at a time, choom.'; return; }
    touched();
    const earlier = earlierTurns();
    const turn = newTurn();
    logLine('user', text, turn, false, typed);
    const result = await act(text, user, earlier, turn, typed);
    if (!result) return;
    if (typeof result === 'string') speak(text, result, extra, [], turn);
    else {
      speak(text, result.reply, extra, result.links, turn, result.answered);
      if (result.job) result.job();
      else if (result.destination) goTo(result.destination).job();
    }
  };
  $('#pill-type').addEventListener('click', () => { touched(); $('#say').focus(); });
  $('#pill-voice').addEventListener('click', () => { touched(); speak('(voice)', lines.voice); });
  $('#typer').addEventListener('submit', event => {
    event.preventDefault();
    const input = $('#say');
    const text = input.value.replace(/\s+/g, ' ').trim().slice(0, 500);
    if (!text || helpBusy) { if (helpBusy) status.textContent = 'One question at a time, choom.'; return; }
    input.value = '';
    respond(text, event.isTrusted, [], true);
  });
  $('#say').addEventListener('input', () => { recallAt = null; });
  $('#say').addEventListener('keydown', event => {
    if (event.key !== 'ArrowUp' && event.key !== 'ArrowDown') return;
    const typed = stageLog.filter(line => line.role === 'user' && line.typed);
    if (!typed.length || (event.key === 'ArrowDown' && recallAt === null)) return;
    event.preventDefault();
    if (recallAt === null) { recallDraft = event.target.value; recallAt = typed.length; }
    recallAt = clamp(recallAt + (event.key === 'ArrowUp' ? -1 : 1), 0, typed.length);
    event.target.value = recallAt === typed.length ? recallDraft : typed[recallAt].text;
    event.target.setSelectionRange(event.target.value.length, event.target.value.length);
  });
  for (const button of $$('.say[data-say]')) button.addEventListener('click', event => {
    const echo = button.closest('.tries').nextElementSibling;
    const said = button.dataset.say;
    respond(said, event.isTrusted, echo && echo.matches('.echo') ? [echo] : []);
  });
  for (const button of $$('[data-try]')) button.addEventListener('click', event => {
    if (button.dataset.try === 'help') { touched(); $('#say').focus(); }
    else respond(button.dataset.try, event.isTrusted);
  });

  // ---------- Chaos: the clones, their notes and holding Esc ----------
  const swarm = $('#swarm');
  const crew = $$('.clone', swarm).map(el => actor(el, el.dataset.clip, { start: +el.dataset.start || 0 }));
  const swarmNotes = $$('.note', swarm);
  const swarmStatus = $('#swarm-status');
  const cloneCount = $('#clone-count');
  const esc = $('#esc');
  let clear = false;
  let spawnTimers = [];
  const shown = () => crew.filter(one => getComputedStyle(one.el).display !== 'none');
  const showCount = () => { cloneCount.textContent = '×' + shown().filter(one => !one.el.classList.contains('gone')).length; };
  const setPressed = which => { for (const button of $$('.say[data-clones]')) button.setAttribute('aria-pressed', String(button.dataset.clones === which)); };
  const spawn = wanted => {
    for (const timer of spawnTimers) clearTimeout(timer);
    spawnTimers = [];
    clear = false;
    swarm.classList.remove('calm');
    const pool = shown();
    const count = wanted === 'all' ? pool.length : Math.min(wanted, pool.length);
    pool.forEach((one, index) => {
      const on = index < count;
      if (!on) { one.el.classList.add('gone'); return; }
      spawnTimers.push(setTimeout(() => {
        one.el.classList.remove('gone');
        one.hold(one.rest);
        one.once(one.el.classList.contains('perch') ? 'perch_arrive' : 'arrive');
        showCount();
      }, still ? 0 : index * 130));
    });
    swarmNotes.forEach((note, index) => note.classList.toggle('gone', index >= (count > 3 ? 4 : 2)));
    setPressed(wanted === 'all' ? 'all' : String(wanted));
    swarmStatus.textContent = wanted === 'all' ? 'Every clone is back.' : count + ' clones on the desktop.';
    showCount();
    cut();
  };
  const clearSwarm = () => {
    for (const timer of spawnTimers) clearTimeout(timer);
    clear = true;
    swarm.classList.add('calm');
    for (const win of $$('.win', swarm)) win.style.translate = '';
    for (const note of swarmNotes) note.classList.add('gone');
    shown().forEach((one, index) => setTimeout(() => one.once(one.el.classList.contains('perch') ? 'perch_leave' : 'leave', () => one.el.classList.add('gone')), still ? 0 : index * 70));
    setTimeout(() => { for (const one of crew) one.el.classList.add('gone'); showCount(); }, still ? 0 : 1100);
    setPressed('');
    swarmStatus.textContent = 'Clones gone, every window back.';
  };
  let holding = 0;
  const holdEsc = () => {
    if (holding || clear) return;
    esc.classList.add('held');
    holding = setTimeout(() => { holding = 0; esc.classList.remove('held'); clearSwarm(); }, still ? 0 : 800);
  };
  const letEscGo = () => { clearTimeout(holding); holding = 0; esc.classList.remove('held'); };
  esc.addEventListener('pointerdown', holdEsc);
  for (const name of ['pointerup', 'pointercancel', 'pointerleave']) esc.addEventListener(name, letEscGo);
  esc.addEventListener('keydown', event => { if ((event.key === 'Enter' || event.key === ' ') && !event.repeat) { event.preventDefault(); holdEsc(); } });
  esc.addEventListener('keyup', letEscGo);
  esc.addEventListener('blur', letEscGo);
  document.addEventListener('keydown', event => { if (event.key === 'Escape' && !event.repeat && live.has(swarm) && !$('#heads-up').open) holdEsc(); });
  document.addEventListener('keyup', event => { if (event.key === 'Escape') letEscGo(); });
  for (const button of $$('.say[data-clones]')) button.addEventListener('click', () => {
    const wanted = button.dataset.clones === 'all' ? 'all' : +button.dataset.clones;
    touched();
    spawn(wanted);
    speak(null, wanted === 'all' ? lines.multiply : lines.more(wanted));
  });
  // One clone hops at a time and the windows get dragged a little, so the swarm never sits still while it's on screen.
  setInterval(() => {
    if (still || playing || performance.now() < danceUntil || clear || !live.has(swarm) || document.hidden) return;
    const walkers = shown().filter(one => one.loop && one.name === 'idle' && !one.el.classList.contains('gone'));
    const one = walkers[Math.floor(Math.random() * walkers.length)];
    if (one) one.once('leave', () => {
      one.el.style.left = 6 + Math.random() * 78 + 'cqw';
      one.el.style.top = 6 + Math.random() * 28 + 'cqh';
      one.once('arrive');
    });
    const win = $$('.win', swarm)[Math.floor(Math.random() * 2)];
    if (getComputedStyle(win).display !== 'none') win.style.translate = Math.round(Math.random() * 44 - 22) + 'px ' + Math.round(Math.random() * 30 - 15) + 'px';
  }, 2200);

  // ---------- The drawing loop: one clock, paused with the motion button, and stages off screen skip it ----------
  const watch = new IntersectionObserver(entries => {
    for (const entry of entries) {
      if (entry.isIntersecting) live.add(entry.target);
      else live.delete(entry.target);
      if (entry.target === swarm && entry.isIntersecting) fetchStrips(['dangle', 'glitch', 'leave', 'arrive', 'perch_leave', 'perch_arrive']);
    }
  });
  for (const desk of $$('.desk')) watch.observe(desk);
  let last = performance.now();
  const tick = now => {
    const dt = Math.min(now - last, 32) / 1000;
    if (!still) time += Math.min(now - last, 250);
    last = now;
    fly(dt);
    musicBeat(now);
    for (const one of actors) if (live.has(one.host)) one.draw(time);
    if (live.has(stage)) {
      place();
      const showPill = !riding && !(drag && drag.moved) && !hopping && (now < pillUntil || pill.contains(document.activeElement));
      if (showPill === pill.classList.contains('off')) { pill.classList.toggle('off', !showPill); dirty = true; }
      if (dirty) {
        dirty = false;
        placeBox(talkbox, tipOf(), across, floor - 4);
        if (showPill) placePill();
      }
    }
    requestAnimationFrame(tick);
  };

  // ---------- Layout, and everything that isn't her ----------
  const layoutFrames = () => {
    for (const box of $$('.say, .typer, .note')) edge(box);
    for (const box of $$('.echo:not([hidden])')) edge(box);
    dirty = true;
    for (const small of smalls) {
      if (!small.el.clientWidth) continue;
      const box = small.her.el;
      placeBox(small.box, { x: box.offsetLeft + box.offsetWidth * 0.52, y: box.offsetTop - 4 }, small.el.clientWidth, small.el.clientHeight - 56);
    }
  };
  const resized = () => {
    measure();
    arrange(shownScene || scene);
    if (asleep) her.hold(pose === 'lie' ? 'laydown_sleep' : 'sleep');
    if (!wins.log.classList.contains('gone')) showWindow('log');
    place();
    layoutFrames();
    showCount();
  };
  new ResizeObserver(resized).observe(stage);
  if (document.fonts) document.fonts.ready.then(layoutFrames);
  addEventListener('load', () => { layoutFrames(); fetchStrips(['blink', 'blink_glitch', 'glitch', 'perch_glitch', 'leave', 'arrive', 'perch_leave', 'perch_arrive']); });
  addEventListener('pointerdown', () => fetchStrips(['dangle', 'grin', 'sleep', 'wake', 'mouth']), { once: true, passive: true });
  addEventListener('keydown', () => fetchStrips(['dangle', 'grin', 'sleep', 'wake', 'mouth']), { once: true, passive: true });

  measure();
  arrange('hero');
  place();
  paintSeek();
  layoutFrames();
  fetchStrips(['idle', 'perch', 'perch_music', 'laydown', 'laydown_talk', 'laydown_blink', 'laydown_sleep', 'laydown_glitch', 'laydown_leave']);
  perform('hero');
  requestAnimationFrame(tick);

  if ('IntersectionObserver' in window) {
    const chapters = new IntersectionObserver(entries => {
      if (!wide.matches) return;
      for (const entry of entries) if (entry.isIntersecting) go(entry.target.dataset.scene);
    }, { rootMargin: '-45% 0px -45% 0px' });
    for (const part of $$('.col [data-scene]')) chapters.observe(part);
  }

  // Each chapter's parts rise in once, in order.
  const reveals = $$('.reveal');
  const revealAll = () => { for (const el of reveals) el.classList.add('in'); };
  if (!still && 'IntersectionObserver' in window) {
    const rise = new IntersectionObserver(entries => {
      for (const entry of entries) if (entry.isIntersecting) { entry.target.classList.add('in'); rise.unobserve(entry.target); }
    }, { threshold: 0.01, rootMargin: '0px 0px -6% 0px' });
    for (const el of reveals) rise.observe(el);
  } else revealAll();

  // The name settles a little as the first screen leaves.
  const mark = $('#wordmark');
  const hero = $('.hero');
  const weigh = () => {
    if (still || !wide.matches) { mark.style.transform = ''; return; }
    const box = hero.getBoundingClientRect();
    const gone = clamp(-box.top / Math.max(1, box.height), 0, 1);
    mark.style.transform = 'scale(' + (1 - 0.06 * gone).toFixed(4) + ')';
  };
  addEventListener('scroll', () => requestAnimationFrame(weigh), { passive: true });
  weigh();

  // The tray clock shows the visitor's own time.
  const clock = $('#clock');
  const showTime = () => { clock.textContent = new Date().toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' }); };
  showTime();
  setInterval(showTime, 15000);

  // The small-screen menu closes on a pick, a click elsewhere or Esc.
  const menu = $('.menu');
  document.addEventListener('click', event => { if (menu.open && (!menu.contains(event.target) || event.target.closest('a'))) menu.open = false; });
  document.addEventListener('keydown', event => { if (event.key === 'Escape' && menu.open) menu.open = false; });

  // ---------- Pause motion, also the state for prefers-reduced-motion: her frames hold and the animated pictures freeze ----------
  const moving = $$('img[src$=".gif"], img[src$=".webp"]');
  const freeze = image => {
    if (!image.complete || !image.naturalWidth) { image.addEventListener('load', () => { if (still) freeze(image); }, { once: true }); return; }
    if (image.dataset.live) return;
    try {
      const canvas = document.createElement('canvas');
      canvas.width = image.naturalWidth;
      canvas.height = image.naturalHeight;
      canvas.getContext('2d').drawImage(image, 0, 0);
      const frozen = canvas.toDataURL();
      image.dataset.live = image.src;
      image.src = frozen;
    } catch {}
  };
  const thaw = image => { if (image.dataset.live) { image.src = image.dataset.live; delete image.dataset.live; } };
  const motionButton = $('#motion');
  const setStill = on => {
    still = on;
    root.classList.toggle('still', on);
    motionButton.setAttribute('aria-pressed', String(on));
    motionButton.setAttribute('aria-label', on ? 'Play motion' : 'Pause motion');
    if (on) {
      drag = null;
      her.el.classList.remove('dragging', 'falling');
      flight = null;
      danceUntil = 0;
      riding = false;
      hint.hidden = true;
      clearBeat();
      finishCursor(true);
      if (pageMotion) pageMotion.cancel();
      for (const one of actors) if (!one.loop) one.finish();
      dancePoses(performance.now());
      revealAll();
      if (talking || typing || thinking) { stopTalk(); for (const box of spokenBoxes) $('.words', box).textContent = answer; }
      pink.classList.remove('on');
    }
    for (const image of moving) { if (on) freeze(image); else thaw(image); }
    weigh();
    startRoll();
  };
  motionButton.addEventListener('click', () => setStill(!still));
  reduced.addEventListener('change', event => setStill(event.matches));

  // ---------- Download links and the heads-up ----------
  const repo = 'lachydotmcg/capri-desktop';
  const install = $('#install');
  const files = { windows: 'Capri-Setup.exe', mac: 'Capri-mac-apple-silicon.zip', intel: 'Capri-mac-intel.zip', linux: 'Capri-linux-x64.tar.gz', appimage: 'Capri-linux-x64.AppImage', quest: 'Capri-VR-Quest.apk' };
  const agent = navigator.userAgent;
  const platform = (navigator.userAgentData && navigator.userAgentData.platform) || navigator.platform || agent;
  let system = /Android|iPhone|iPad|CrOS/.test(agent) ? 'windows' : /mac/i.test(platform) && navigator.maxTouchPoints < 2 ? 'mac' : /linux|x11/i.test(platform) ? 'linux' : 'windows';
  const urls = {};
  for (const link of $$('a[data-os]')) urls[link.dataset.os] = link.href;
  const point = () => {
    install.href = urls[system] || urls.windows;
    for (const link of $$('a[data-os]')) if (urls[link.dataset.os]) link.href = urls[link.dataset.os];
    const yours = $('[data-panel="' + (system === 'intel' ? 'mac' : system) + '"] .yours');
    if (yours) yours.hidden = false;
  };
  point();
  const intelGraphics = () => {
    try {
      const gl = document.createElement('canvas').getContext('webgl');
      const info = gl && gl.getExtension('WEBGL_debug_renderer_info');
      const name = gl ? String(gl.getParameter(info ? info.UNMASKED_RENDERER_WEBGL : gl.RENDERER)) : '';
      return /Intel|AMD|Radeon|NVIDIA|GeForce/i.test(name) && !/Apple (M\d|GPU)/i.test(name);
    } catch (error) { return false; }
  };
  if (system === 'mac' && navigator.userAgentData && navigator.userAgentData.getHighEntropyValues) {
    navigator.userAgentData.getHighEntropyValues(['architecture']).then(values => { if (values.architecture === 'x86') { system = 'intel'; point(); } }).catch(() => {});
  } else if (system === 'mac' && intelGraphics()) { system = 'intel'; point(); }
  const headsUp = $('#heads-up');
  const warn = os => {
    const showing = os === 'intel' ? 'mac' : os;
    for (const note of $$('p[data-os]', headsUp)) note.hidden = note.dataset.os !== showing;
    if (headsUp.showModal && !headsUp.open) headsUp.showModal();
  };
  document.addEventListener('click', event => {
    const link = event.target.closest('a[data-os], #install');
    if (!link) return;
    warn(link.dataset.os || system);
    readCount({ method: 'POST', keepalive: true }).catch(() => {});
  });
  headsUp.addEventListener('click', event => { if (event.target === headsUp) headsUp.close(); });

  // ---------- Real numbers. The page keeps its built-in snapshot if these can't be reached. ----------
  const compact = new Intl.NumberFormat('en', { notation: 'compact', maximumFractionDigits: 1 });
  const day = new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short' });
  const setAll = (query, text) => { for (const el of $$(query)) el.textContent = text; };
  const readJson = async url => {
    const response = await fetch(url, { signal: AbortSignal.timeout(6000) });
    if (!response.ok) throw new Error('Unavailable');
    return response.json();
  };
  const beforeCounter = 839;
  let downloads = null;
  const readCount = async options => {
    const response = await fetch(install.dataset.api, { ...options, signal: AbortSignal.timeout(6000) });
    if (!response.ok) throw new Error('Unavailable');
    downloads = Math.max((await response.json()).count + beforeCounter, downloads || 0);
    setAll('.download-count', compact.format(downloads));
  };
  const showRelease = latest => {
    if (!latest || !Array.isArray(latest.assets)) return;
    setAll('[data-version]', latest.tag_name);
    if (latest.published_at) setAll('[data-released]', day.format(new Date(latest.published_at)));
    for (const os of Object.keys(files)) {
      const asset = latest.assets.find(item => item.name === files[os] && item.size > 1024 && typeof item.browser_download_url === 'string' && item.browser_download_url.startsWith('https://github.com/' + repo + '/releases/download/'));
      if (!asset) continue;
      urls[os] = asset.browser_download_url;
      for (const size of $$('[data-size="' + os + '"]')) size.textContent = (asset.size / 1e6).toFixed(1) + ' MB';
    }
    point();
  };
  const readReleases = async () => {
    const all = await readJson('https://api.github.com/repos/' + repo + '/releases?per_page=100');
    const apps = all.filter(release => !release.draft && !release.prerelease && /^v\d/.test(release.tag_name)).sort((a, b) => new Date(b.published_at) - new Date(a.published_at));
    if (!apps.length) return;
    showRelease(apps[0]);
    setAll('[data-release-count]', apps.length);
    setAll('[data-first-release]', day.format(new Date(apps[apps.length - 1].published_at)));
  };

  // The change log rolls: hover pauses it, and a wheel, a touch, a press or a key stops it for good.
  const pane = $('.log-window');
  const track = $('.log-track');
  let rolling = false;
  let rollStopped = false;
  let hovering = false;
  let rolled = 0;
  let lastRoll = 0;
  const roll = now => {
    if (!rolling) return;
    if (lastRoll && !hovering && !still) {
      rolled += Math.min(now - lastRoll, 100) / 1000 * 26;
      const loop = track.scrollHeight / 2;
      if (rolled >= loop) rolled -= loop;
      pane.scrollTop = rolled;
    }
    lastRoll = now;
    requestAnimationFrame(roll);
  };
  const startRoll = () => {
    if (rolling || rollStopped || still || pane.scrollHeight <= pane.clientHeight) return;
    for (const row of $$('li', track)) {
      const copy = row.cloneNode(true);
      copy.setAttribute('aria-hidden', 'true');
      $('a', copy).tabIndex = -1;
      track.append(copy);
    }
    rolling = true;
    requestAnimationFrame(roll);
  };
  const stopRolling = () => {
    rollStopped = true;
    if (!rolling) return;
    rolling = false;
    const top = pane.scrollTop % (track.scrollHeight / 2);
    for (const copy of $$('li[aria-hidden="true"]', track)) copy.remove();
    pane.scrollTop = top;
  };
  for (const name of ['wheel', 'touchstart', 'pointerdown', 'keydown']) pane.addEventListener(name, stopRolling, { passive: true });
  pane.addEventListener('pointerenter', () => { hovering = true; });
  pane.addEventListener('pointerleave', () => { hovering = false; });
  const readChanges = async () => {
    const commits = (await readJson('https://api.github.com/repos/' + repo + '/commits?sha=main&per_page=20')).filter(item => item && item.commit && typeof item.html_url === 'string' && item.html_url.startsWith('https://github.com/' + repo + '/commit/') && !item.commit.message.startsWith('Merge '));
    if (!commits.length) return;
    const rows = commits.map(item => {
      const row = document.createElement('li');
      const link = document.createElement('a');
      link.href = item.html_url;
      const when = document.createElement('time');
      when.dateTime = item.commit.author.date;
      when.textContent = day.format(new Date(item.commit.author.date));
      const text = document.createElement('span');
      text.textContent = item.commit.message.split('\n')[0];
      link.append(when, text);
      row.append(link);
      return row;
    });
    const wasRolling = rolling;
    if (rolling) { rolling = false; for (const copy of $$('li[aria-hidden="true"]', track)) copy.remove(); }
    track.replaceChildren(...rows);
    if (wasRolling || !rollStopped) startRoll();
  };
  addEventListener('load', () => {
    startRoll();
    readCount().catch(() => {});
    readReleases().catch(() => readJson('release.json').then(showRelease)).catch(() => {});
    readChanges().catch(() => {});
  });

  if (still) setStill(true);
})();

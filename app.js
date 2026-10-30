(() => {
  const repo = 'lachydotmcg/capri-desktop';
  const install = document.querySelector('#install');
  const systems = document.querySelectorAll('.systems a[data-os]');
  const files = { windows: 'Capri-Setup.exe', mac: 'Capri-mac-apple-silicon.zip', intel: 'Capri-mac-intel.zip', linux: 'Capri-linux-x64.tar.gz' };
  const agent = navigator.userAgent;
  const platform = (navigator.userAgentData && navigator.userAgentData.platform) || navigator.platform || agent;
  let system = /Android|iPhone|iPad|CrOS/.test(agent) ? 'windows' : /mac/i.test(platform) && navigator.maxTouchPoints < 2 ? 'mac' : /linux|x11/i.test(platform) ? 'linux' : 'windows';
  const urls = {};
  systems.forEach(link => { urls[link.dataset.os] = link.href; });
  const point = () => {
    install.href = urls[system] || urls.windows;
    systems.forEach(link => {
      link.hidden = !urls[link.dataset.os];
      if (urls[link.dataset.os]) link.href = urls[link.dataset.os];
    });
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
    navigator.userAgentData.getHighEntropyValues(['architecture']).then(values => {
      if (values.architecture === 'x86') { system = 'intel'; point(); }
    }).catch(() => {});
  } else if (system === 'mac' && intelGraphics()) { system = 'intel'; point(); }
  const enableDownload = (release) => {
    if (!release || release.draft || release.prerelease || !Array.isArray(release.assets)) throw new Error('Release unavailable');
    const found = {};
    Object.keys(files).forEach(os => {
      const asset = release.assets.find(item => item.name === files[os] && item.size > 1024 && typeof item.browser_download_url === 'string' && item.browser_download_url.startsWith('https://github.com/' + repo + '/releases/download/'));
      if (asset) found[os] = asset.browser_download_url;
    });
    if (!found.windows) throw new Error('Installer unavailable');
    Object.keys(files).forEach(os => { urls[os] = found[os]; });
    point();
  };
  const readRelease = async (url) => {
    const response = await fetch(url, { signal: AbortSignal.timeout(6000) });
    if (!response.ok) throw new Error('Release unavailable');
    enableDownload(await response.json());
  };
  readRelease('https://api.github.com/repos/' + repo + '/releases/latest').catch(() => readRelease('release.json')).catch(() => {});
  const headsUp = document.querySelector('#heads-up');
  const notes = headsUp.querySelectorAll('p[data-os]');
  const warn = (os) => {
    const shown = os === 'intel' ? 'mac' : os;
    notes.forEach(note => { note.hidden = note.dataset.os !== shown; });
    if (headsUp.showModal && !headsUp.open) headsUp.showModal();
  };
  install.addEventListener('click', () => warn(urls[system] ? system : 'windows'));
  systems.forEach(link => link.addEventListener('click', () => warn(link.dataset.os)));
  headsUp.addEventListener('click', event => { if (event.target === headsUp) headsUp.close(); });

  const stage = document.querySelector('#demo');
  const capri = document.querySelector('#demo-capri');
  const sprite = capri.querySelector('.sprite');
  const cursor = document.querySelector('#player-cursor');
  const status = document.querySelector('#demo-status');
  const motion = !matchMedia('(prefers-reduced-motion: reduce)').matches;
  // Frames, frames a second, and for her artist's 60-frame perch, how many cells go across before the strip wraps to its next row.
  const clips = { idle: [16, 8], blink: [4, 8], blink_glitch: [4, 8], grin: [16, 8], glitch: [10, 15], dangle: [24, 10], sleep: [16, 6], wake: [13, 15], perch: [60, 30, 10], perch_blink: [8, 30], perch_blink_glitch: [8, 30], perch_glitch: [10, 15], mouth: [5, 12] };
  const across = name => clips[name][2] || clips[name][0];
  const down = name => Math.ceil(clips[name][0] / across(name));
  const cellAt = (name, cell) => {
    const columns = across(name), rows = down(name);
    return (columns > 1 ? cell % columns / (columns - 1) * 100 : 0) + '% ' + (rows > 1 ? Math.floor(cell / columns) / (rows - 1) * 100 : 0) + '%';
  };
  const loaded = new Set(['idle']);
  let clip = 'idle';
  let clipStart = performance.now();
  let cycle = 0;
  let blinkWith = '';
  let nextGlitch = clipStart + 12000 + Math.random() * 12000;
  let lastInput = clipStart;
  let shown = '';
  let layered = '';
  const play = name => { clip = loaded.has(name) ? name : riding && loaded.has('perch') ? 'perch' : 'idle'; clipStart = performance.now(); cycle = 0; blinkWith = ''; };
  const show = (name, cell, mouth) => {
    const layers = mouth >= 0 ? ['mouth', name] : [name];
    if (layered !== layers.join()) {
      layered = layers.join();
      sprite.style.backgroundImage = layers.map(layer => 'url("assets/sprites/' + layer + '.webp")').join(', ');
      sprite.style.backgroundSize = layers.map(layer => across(layer) * 100 + '% ' + down(layer) * 100 + '%').join(', ');
    }
    if (shown === name + ':' + cell + ':' + mouth) return;
    shown = name + ':' + cell + ':' + mouth;
    sprite.style.backgroundPosition = (mouth >= 0 ? mouth / 4 * 100 + '% 0, ' : '') + cellAt(name, cell);
  };
  const mouthFor = (letter, step) => {
    const lower = letter.toLowerCase();
    if ('aeo'.includes(lower)) return letter !== lower ? 4 : 3;
    if ('iuy'.includes(lower)) return 2;
    if ('mbp'.includes(lower) || !/\p{L}/u.test(letter)) return 0;
    return step % 2 ? 1 : 2;
  };
  const animate = now => {
    const seated = clip.startsWith('perch');
    if (riding && !seated && loaded.has('perch')) play('perch');
    if ((!riding && seated) || (clip === 'dangle' && !(drag && drag.moved))) play('idle');
    let index = Math.max(0, Math.floor((now - clipStart) / 1000 * clips[clip][1]));
    if ((clip === 'glitch' && index >= clips.glitch[0]) || (clip === 'wake' && index >= clips.wake[0]) || (clip === 'grin' && index >= clips.grin[0] * 2)) { play('idle'); index = 0; }
    if (clip === 'perch_glitch' && index >= clips.perch_glitch[0]) { play('perch'); index = 0; }
    if ((clip === 'idle' || clip === 'perch') && motion) {
      const current = Math.floor(index / clips[clip][0]);
      if (current !== cycle) {
        cycle = current;
        const roll = Math.random();
        const prefix = clip === 'perch' ? 'perch_' : '';
        blinkWith = roll < 0.1 ? prefix + 'blink_glitch' : roll < 0.55 ? prefix + 'blink' : '';
        if (now >= nextGlitch && talkState === 'off') { nextGlitch = now + 12000 + Math.random() * 12000; play(clip === 'perch' ? 'perch_glitch' : 'glitch'); index = 0; }
      }
      if (clip === 'idle' && now - lastInput > 30000 && !riding && !drag && talkState === 'off' && typer.hidden) play('sleep');
    }
    let name = clip;
    let cell = motion ? index % clips[clip][0] : 0;
    // A blink swaps in at cells 9 to 12 of her idle loop, and at 28 to 35 of the perch's: her artist's own blink, its frames 29 to 36.
    const blinkFrom = name === 'perch' ? 28 : 9;
    if ((name === 'idle' || name === 'perch') && blinkWith && loaded.has(blinkWith) && cell >= blinkFrom && cell < blinkFrom + clips[blinkWith][0]) { name = blinkWith; cell -= blinkFrom; }
    capri.classList.toggle('still', clip === 'sleep' || clip === 'dangle');
    let mouth = -1;
    if (talkState === 'talking' && motion && clip === 'idle' && name !== 'blink_glitch' && loaded.has('mouth')) {
      const step = Math.floor((now - replyStart) / 1000 * 12);
      const letter = Math.floor(step / 12 * 28);
      if (letter < answer.length) mouth = mouthFor(answer[letter], step);
    }
    show(name, cell, mouth);
    updateTalk(now);
    requestAnimationFrame(animate);
  };
  const wake = () => { lastInput = performance.now(); if (clip === 'sleep') play('wake'); };
  ['pointermove', 'pointerdown', 'keydown', 'wheel'].forEach(type => document.addEventListener(type, wake, { passive: true }));
  addEventListener('load', () => Object.keys(clips).forEach(name => {
    const strip = new Image();
    strip.src = 'assets/sprites/' + name + '.webp';
    strip.decode().then(() => loaded.add(name)).catch(() => {});
  }));
  requestAnimationFrame(animate);
  let x = 0;
  let y = 0;
  let size = 220;
  let drag = null;
  let initialized = false;
  let clickTimer;
  let riding = false;
  let dragged = false;
  const taskbar = document.querySelector('#taskbar');
  const floor = () => stage.clientHeight - taskbar.offsetHeight;
  const cancelClick = () => clearTimeout(clickTimer);
  const setRide = on => { cancelClick(); const was = riding; riding = on; if (on && !was) play('perch_glitch'); if (was && !on) play('grin'); capri.classList.toggle('riding', on); if (on) stage.focus({ preventScroll: true }); };
  const placeCursor = (left, top) => {
    left = Math.max(0, Math.min(left, stage.clientWidth - 26));
    top = Math.max(0, Math.min(top, stage.clientHeight - 32));
    cursor.style.transform = 'translate(' + left + 'px, ' + top + 'px)';
  };
  const followPointer = (event) => {
    const bounds = stage.getBoundingClientRect();
    placeCursor(event.clientX - bounds.left, event.clientY - bounds.top);
    if (riding) { x = event.clientX - bounds.left - size * 0.267 + 12; y = event.clientY - bounds.top - size * 0.73 - 12; place(); }
  };
  const place = () => {
    x = Math.max(0, Math.min(x, stage.clientWidth - size));
    y = Math.max(0, Math.min(y, floor() - size));
    capri.style.left = x + 'px';
    capri.style.top = y + 'px';
    capri.style.width = size + 'px';
    capri.style.height = size + 'px';
  };
  const updateMotion = () => {
    capri.classList.toggle('no-motion', !motion);
  };
  const reset = () => {
    size = stage.clientWidth < 550 ? 160 : 220;
    x = stage.clientWidth * (stage.clientWidth < 550 ? 0.46 : 0.62);
    y = floor() * 0.3;
    closeTalk();
    closeTyper();
    setRide(false);
    capri.hidden = false;
    play('grin');
    place();
    placeCursor(x + size * 0.6, y + size * 0.5);
    status.textContent = 'Capri is back.';
  };
  const react = () => { if (motion) play('glitch'); };
  const endDrag = () => { drag = null; capri.classList.remove('dragging'); cursor.classList.remove('pressed'); };
  stage.addEventListener('pointermove', followPointer);
  stage.addEventListener('pointerdown', event => { followPointer(event); cursor.classList.add('pressed'); });
  document.addEventListener('pointerup', () => cursor.classList.remove('pressed'));
  stage.addEventListener('pointercancel', endDrag);
  capri.addEventListener('pointerdown', event => {
    if (event.button !== 0) return;
    drag = { id: event.pointerId, startX: event.clientX, startY: event.clientY, originX: x, originY: y, moved: false };
    capri.setPointerCapture(event.pointerId);
  });
  capri.addEventListener('pointermove', event => {
    if (!drag || drag.id !== event.pointerId) return;
    const dx = event.clientX - drag.startX;
    const dy = event.clientY - drag.startY;
    if (dx * dx + dy * dy > 25 && !drag.moved) { drag.moved = true; cancelClick(); play('dangle'); }
    if (!drag.moved) return;
    capri.classList.add('dragging');
    x = drag.originX + dx;
    y = drag.originY + dy;
    place();
  });
  capri.addEventListener('pointerup', event => {
    if (!drag || drag.id !== event.pointerId) return;
    const moved = drag.moved;
    endDrag();
    if (capri.hasPointerCapture(event.pointerId)) capri.releasePointerCapture(event.pointerId);
    dragged = moved;
    clearTimeout(clickTimer);
    if (!moved) clickTimer = setTimeout(() => { if (!riding && !drag && !capri.hidden) react(); }, 500);
  });
  capri.addEventListener('dblclick', event => { cancelClick(); if (dragged || riding) return; setRide(true); followPointer(event); });
  capri.addEventListener('lostpointercapture', endDrag);
  stage.addEventListener('pointercancel', cancelClick);
  stage.addEventListener('pointerdown', event => { if (event.target === stage) stage.focus({ preventScroll: true }); });
  capri.addEventListener('keydown', event => {
    const movement = { ArrowLeft: [-12, 0], ArrowRight: [12, 0], ArrowUp: [0, -12], ArrowDown: [0, 12] }[event.key];
    if (movement) { event.preventDefault(); x += movement[0]; y += movement[1]; place(); placeCursor(x + size * 0.6, y + size * 0.5); }
    if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); react(); }
  });
  stage.addEventListener('keydown', event => { if (event.ctrlKey && event.altKey && (event.code === 'KeyC' || event.key.toLowerCase() === 'c') && riding) { event.preventDefault(); setRide(false); } });
  document.querySelector('#demo-restore').addEventListener('click', reset);
  new ResizeObserver(() => { if (!initialized) { initialized = true; reset(); } else { place(); placeCursor(x + size * 0.6, y + size * 0.5); } }).observe(stage);
  updateMotion();

  const answer = "I can't reply through here choom, you needa install my chip";
  const pill = document.querySelector('#talk-pill');
  const typer = document.querySelector('#talk-typer');
  const input = document.querySelector('#talk-input');
  const box = document.querySelector('#talk-box');
  const heardLine = box.querySelector('.talk-heard');
  const words = box.querySelector('.talk-words');
  const drawn = new Map();
  let talkState = 'off';
  let thinkUntil = 0;
  let replyStart = 0;
  let replyDone = 0;
  let leaving = 0;
  let pillUntil = 0;
  const clamp = (value, low, high) => Math.max(low, Math.min(high, value));
  const head = () => ({ x: x + size * 0.52, y: y - 4 });
  const feet = () => ({ x: x + size * 0.58, y: y + size * 0.99 });
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
  const frame = (svg, id, width, height, path, bar) => {
    const key = width + ' ' + height + ' ' + path;
    if (drawn.get(svg) === key) return;
    drawn.set(svg, key);
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
  const underPill = () => ({ x: feet().x, y: feet().y + 42 });
  let boxBelow = false;
  const placeBox = () => {
    const width = box.offsetWidth;
    const height = box.offsetHeight;
    const tail = 16;
    const low = floor() - 4;
    const above = head();
    const under = underPill();
    boxBelow = above.y - tail - height < 4 && (under.y + tail + height <= low || low - under.y > above.y);
    const tip = boxBelow ? under : above;
    const left = clamp(tip.x - width * 0.7, 4, stage.clientWidth - width - 4);
    const top = clamp(boxBelow ? tip.y + tail : tip.y - tail - height, 4, low - height);
    box.style.left = left + 'px';
    box.style.top = top + 'px';
    frame(box.querySelector('svg'), 'talk-scan-box', width, height, plate(0, 0, width, height, 10, clamp(tip.x - left, 32, width - 10), boxBelow ? Math.max(tip.y - top, -tail) : Math.min(tip.y - top, height + tail), 18, false), 13);
  };
  const placeTyper = () => {
    const width = typer.offsetWidth;
    const height = typer.offsetHeight;
    const low = floor() - 4;
    let left;
    let top;
    if (!box.hidden) {
      left = box.offsetLeft;
      const outward = boxBelow ? box.offsetTop + box.offsetHeight + 8 : box.offsetTop - height - 16;
      top = outward >= 4 && outward + height <= low ? outward : boxBelow ? box.offsetTop - height - 16 : box.offsetTop + box.offsetHeight + 8;
    } else {
      left = clamp(head().x - width * 0.7, 4, stage.clientWidth - width - 4);
      top = head().y - height - 18;
      if (top < 4) top = underPill().y;
    }
    top = clamp(top, 4, low - height);
    typer.style.left = left + 'px';
    typer.style.top = top + 'px';
    frame(typer.querySelector('svg'), 'talk-scan-typer', width, height, plate(1, 1, width - 2, height - 2, 10, -1, height / 2, 0, true), 10);
  };
  const placePill = () => {
    const width = pill.offsetWidth;
    const height = pill.offsetHeight;
    pill.style.left = clamp(feet().x - 4 - width / 2, 4, stage.clientWidth - width - 4) + 'px';
    pill.style.top = clamp(feet().y + 4, 4, stage.clientHeight - height - 2) + 'px';
  };
  const closeTalk = () => { talkState = 'off'; leaving = 0; box.hidden = true; box.classList.remove('glitch', 'leaving'); };
  const closeTyper = () => { if (typer.hidden) return; typer.hidden = true; if (talkState === 'online') replyDone = performance.now(); };
  const dismiss = () => { if (talkState !== 'off' && !leaving) { leaving = performance.now(); box.classList.add('leaving'); } };
  const hear = text => {
    text = text.trim().slice(0, 240);
    if (!text) return;
    lastInput = performance.now();
    heardLine.textContent = '> you: ' + text;
    words.textContent = '';
    talkState = 'thinking';
    thinkUntil = performance.now() + 700;
    leaving = 0;
    box.classList.remove('leaving');
    if (box.hidden) {
      box.hidden = false;
      if (motion) { box.classList.remove('glitch'); void box.offsetWidth; box.classList.add('glitch'); }
    }
    box.style.minHeight = '';
    words.textContent = answer;
    box.style.minHeight = box.offsetHeight + 'px';
    words.textContent = '';
  };
  const openTyper = () => {
    if (!typer.hidden) { closeTyper(); return; }
    typer.hidden = false;
    placeTyper();
    input.focus({ preventScroll: true });
    pillUntil = performance.now() + 1500;
  };
  const updateTalk = now => {
    const free = !capri.hidden && !riding && !(drag && drag.moved);
    const showPill = free && (now < pillUntil || !typer.hidden || talkState !== 'off' || pill.contains(document.activeElement));
    if (pill.hidden === showPill) pill.hidden = !showPill;
    if (showPill) placePill();
    if (talkState === 'thinking' && now >= thinkUntil) { talkState = 'talking'; replyStart = now; status.textContent = 'Capri says: ' + answer; }
    if (talkState === 'talking' && (!motion || (now - replyStart) / 1000 * 28 >= answer.length)) { talkState = 'online'; replyDone = now; }
    if (talkState === 'online' && typer.hidden && now > replyDone + Math.min(14, 4 + answer.length * 0.05) * 1000) dismiss();
    if (leaving && now - leaving > 250) closeTalk();
    if (!box.hidden) {
      const shownWords = talkState === 'thinking' ? '' : talkState === 'online' ? answer : answer.slice(0, Math.floor((now - replyStart) / 1000 * 28));
      if (words.textContent !== shownWords) words.textContent = shownWords;
      ['thinking', 'talking', 'online'].forEach(state => box.classList.toggle(state, talkState === state));
      placeBox();
    }
    if (!typer.hidden) placeTyper();
  };
  box.addEventListener('animationend', () => box.classList.remove('glitch'));
  box.addEventListener('click', dismiss);
  typer.addEventListener('submit', event => { event.preventDefault(); const text = input.value; input.value = ''; hear(text); });
  input.addEventListener('keydown', event => { if (event.key === 'Escape') { event.preventDefault(); closeTyper(); capri.focus({ preventScroll: true }); } });
  document.querySelector('#talk-type').addEventListener('click', openTyper);
  document.querySelector('#talk-voice').addEventListener('click', () => hear('(voice)'));
  document.addEventListener('pointerdown', event => { if (!typer.hidden && !typer.contains(event.target) && !pill.contains(event.target)) closeTyper(); });
  stage.addEventListener('pointermove', event => {
    const bounds = stage.getBoundingClientRect();
    const px = event.clientX - bounds.left;
    const py = event.clientY - bounds.top;
    if (px > x - 40 && px < x + size + 40 && py > y - 40 && py < y + size + 70) pillUntil = performance.now() + 1500;
  });
  capri.addEventListener('pointerdown', () => { pillUntil = performance.now() + 4000; });
  capri.addEventListener('focus', () => { pillUntil = performance.now() + 4000; });

  const star = document.querySelector('#star');
  const starCount = star.querySelector('span');
  const compact = new Intl.NumberFormat('en', { notation: 'compact', maximumFractionDigits: 1 });
  let stars = null;
  let starred = false;
  try { starred = localStorage.getItem('capri-star') === '1'; } catch {}
  const showStar = () => {
    starCount.textContent = stars === null ? '' : compact.format(stars);
    star.setAttribute('aria-pressed', starred);
  };
  const readStars = async (options) => {
    const response = await fetch(star.dataset.api, { ...options, signal: AbortSignal.timeout(6000) });
    if (!response.ok) throw new Error('Stars unavailable');
    stars = Math.max((await response.json()).count, stars || 0);
    showStar();
  };
  showStar();
  readStars().catch(() => {});
  star.addEventListener('click', () => {
    if (starred) return;
    starred = true;
    try { localStorage.setItem('capri-star', '1'); } catch {}
    if (stars !== null) stars += 1;
    showStar();
    if (motion) {
      const plus = document.createElement('span');
      plus.className = 'star-plus';
      plus.textContent = '+1';
      plus.setAttribute('aria-hidden', 'true');
      star.append(plus);
      plus.animate([{ transform: 'translateY(0)', opacity: 1 }, { transform: 'translateY(-10px)', opacity: 1, offset: 0.5 }, { transform: 'translateY(-18px)', opacity: 0 }], { duration: 900, easing: 'ease-out' }).finished.then(() => plus.remove());
    }
    readStars({ method: 'POST' }).catch(() => {});
  });

  const downloadCount = install.querySelector('.download-count');
  const beforeCounter = 839;
  let downloads = null;
  const readDownloads = async (options) => {
    const response = await fetch(install.dataset.api, { ...options, signal: AbortSignal.timeout(6000) });
    if (!response.ok) throw new Error('Downloads unavailable');
    downloads = Math.max((await response.json()).count + beforeCounter, downloads || 0);
    downloadCount.textContent = compact.format(downloads);
  };
  readDownloads().catch(() => {});
  [install, ...systems].forEach(link => link.addEventListener('click', () => { readDownloads({ method: 'POST', keepalive: true }).catch(() => {}); }));

  const changelog = document.querySelector('#changelog');
  const track = changelog.querySelector('.changelog-track');
  const day = new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short' });
  const readChanges = async () => {
    const response = await fetch('https://api.github.com/repos/' + repo + '/commits?sha=main&per_page=20', { signal: AbortSignal.timeout(6000) });
    if (!response.ok) throw new Error('Changes unavailable');
    const commits = (await response.json()).filter(item => item && item.commit && typeof item.html_url === 'string' && item.html_url.startsWith('https://github.com/' + repo + '/commit/'));
    const entry = item => {
      const row = document.createElement('li');
      const link = document.createElement('a');
      link.href = item.html_url;
      link.target = '_blank';
      link.rel = 'noopener noreferrer';
      const when = document.createElement('time');
      when.dateTime = item.commit.author.date;
      when.textContent = day.format(new Date(item.commit.author.date));
      const text = document.createElement('span');
      text.className = 'changelog-text';
      text.textContent = item.commit.message.split('\n')[0];
      link.append(when, text);
      row.append(link);
      return row;
    };
    const rows = commits.filter(item => !item.commit.message.startsWith('Merge ')).map(entry);
    if (!rows.length) throw new Error('No changes');
    track.replaceChildren(...rows);
    changelog.hidden = false;
    if (!motion || pane.scrollHeight <= pane.clientHeight) return;
    for (const row of rows) {
      const copy = row.cloneNode(true);
      copy.setAttribute('aria-hidden', 'true');
      copy.querySelector('a').tabIndex = -1;
      track.append(copy);
    }
    rolling = true;
    requestAnimationFrame(roll);
  };
  const pane = changelog.querySelector('.changelog-window');
  let rolling = false;
  let hovering = false;
  let rolled = 0;
  let lastFrame = 0;
  const roll = (now) => {
    if (!rolling) return;
    if (lastFrame && !hovering) {
      rolled += Math.min(now - lastFrame, 100) / 1000 * 26;
      const loop = track.scrollHeight / 2;
      if (rolled >= loop) rolled -= loop;
      pane.scrollTop = rolled;
    }
    lastFrame = now;
    requestAnimationFrame(roll);
  };
  const stopRolling = () => {
    if (!rolling) return;
    rolling = false;
    const top = pane.scrollTop % (track.scrollHeight / 2);
    for (const copy of track.querySelectorAll('li[aria-hidden="true"]')) copy.remove();
    pane.scrollTop = top;
  };
  for (const name of ['wheel', 'touchstart', 'pointerdown', 'keydown']) pane.addEventListener(name, stopRolling, { passive: true });
  pane.addEventListener('pointerenter', () => { hovering = true; });
  pane.addEventListener('pointerleave', () => { hovering = false; });
  readChanges().catch(() => {});
})();

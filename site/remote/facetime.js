(() => {
  'use strict';
  const LOOK_SIDE = 512;
  const LOOK_BYTES = 52000;
  const LOOK_EVERY = 45000;
  const LOOK_CHANGE = 14;
  const reduced = matchMedia('(prefers-reduced-motion: reduce)');
  const remote = () => window.CapriRemote;
  const make = (tag, className, text) => {
    const el = document.createElement(tag);
    if (className) el.className = className;
    if (text != null) el.textContent = text;
    return el;
  };
  const svg = (...paths) => {
    const box = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    box.setAttribute('viewBox', '0 0 24 24');
    box.setAttribute('aria-hidden', 'true');
    for (const d of paths) {
      const line = document.createElementNS('http://www.w3.org/2000/svg', 'path');
      line.setAttribute('d', d);
      box.appendChild(line);
    }
    return box;
  };
  const EYE = 'M2.5 12c2.4-4 5.6-6 9.5-6s7.1 2 9.5 6c-2.4 4-5.6 6-9.5 6s-7.1-2-9.5-6z';
  const PUPIL = 'M10 10h4v4h-4z';
  const SLASH = 'M4 4l16 16';

  const webgl = (() => {
    let known = null;
    return () => {
      if (known !== null) return known;
      try {
        const canvas = document.createElement('canvas');
        known = !!(window.WebGLRenderingContext && (canvas.getContext('webgl2') || canvas.getContext('webgl')));
      } catch (error) { known = false; }
      return known;
    };
  })();
  const available = () => webgl() && !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia);
  const seesYou = () => {
    const r = remote();
    return !!(r && r.info && Array.isArray(r.info.features) && r.info.features.includes('call-look'));
  };

  const stageBox = make('div', 'ft-stage');
  stageBox.setAttribute('aria-hidden', 'true');
  const canvas = make('canvas', 'ft-her');
  stageBox.append(canvas);
  const you = make('div', 'ft-you');
  you.setAttribute('role', 'group');
  you.setAttribute('aria-label', 'Your camera');
  const video = make('video', 'ft-video');
  video.muted = true;
  video.playsInline = true;
  video.setAttribute('playsinline', '');
  video.setAttribute('muted', '');
  video.autoplay = true;
  const youNote = make('p', 'ft-you-note', 'Camera off');
  const badge = make('span', 'ft-badge');
  badge.append(svg(EYE, PUPIL), svg(EYE, PUPIL, SLASH));
  you.append(video, youNote, badge);

  let api = null, open = false, stream = null, wantCamera = true, cameraState = 'off', model = null, loading = null, stage = null, lips = null;
  let raf = 0, last = 0, tap = null, timeData = null, freqData = null;
  let blinkAt = 0, blinkFrom = -1, glanceAt = 0, glance = [0, 0], glanceTo = [0, 0], spoke = 0, nod = 0, mood = '';
  let lastLook = null, lastLookAt = 0, looks = 0, corner = 'tr', drag = null;
  const stats = { frames: 0, looks: 0, lookBytes: 0, skipped: 0, mouth: 0, shapes: 0, failed: '' };

  const cameraText = () => ({
    off: 'Camera off',
    asking: 'Opening your camera',
    blocked: 'Camera blocked',
    missing: 'No camera found',
    live: ''
  })[cameraState] || '';
  const paintYou = () => {
    you.dataset.state = cameraState;
    youNote.textContent = cameraText();
    youNote.hidden = cameraState === 'live';
    const sees = cameraState === 'live' && seesYou();
    badge.dataset.on = String(sees);
    badge.title = sees ? 'She can see you' : cameraState === 'live' ? 'Update Capri on your PC so she can see you' : 'She can\'t see you';
    you.setAttribute('aria-label', sees ? 'Your camera: she can see you' : 'Your camera: ' + (cameraText() || 'she can\'t see you yet'));
    you.dataset.corner = corner;
    if (api) api.changed();
  };

  const stopCamera = () => {
    const was = stream;
    stream = null;
    if (was) for (const track of was.getTracks()) track.stop();
    try { video.srcObject = null; } catch (error) {}
    if (cameraState !== 'blocked' && cameraState !== 'missing') cameraState = 'off';
  };
  const startCamera = async () => {
    if (stream || !open) return;
    cameraState = 'asking';
    paintYou();
    let got;
    try {
      got = await navigator.mediaDevices.getUserMedia({ audio: false, video: { facingMode: 'user', width: { ideal: 640 }, height: { ideal: 480 }, frameRate: { ideal: 24, max: 30 } } });
    } catch (error) {
      const kind = error && error.name;
      cameraState = kind === 'NotAllowedError' || kind === 'SecurityError' ? 'blocked' : kind === 'NotFoundError' || kind === 'OverconstrainedError' ? 'missing' : 'off';
      paintYou();
      return;
    }
    if (!open || !wantCamera) { for (const track of got.getTracks()) track.stop(); return; }
    stream = got;
    const track = got.getVideoTracks()[0];
    if (track) track.onended = () => { if (stream === got) { stream = null; cameraState = 'off'; paintYou(); } };
    video.srcObject = got;
    try { await video.play(); } catch (error) {}
    cameraState = 'live';
    lastLook = null;
    paintYou();
  };

  const loadHer = () => {
    if (model) return Promise.resolve(model);
    if (loading) return loading;
    const base = new URL('.', location.href).href;
    loading = import(base + 'face3d.js').then(face => face.load(base + 'models/capri-face.glb').then(found => {
      model = { face, found };
      return model;
    })).catch(error => {
      stats.failed = String(error && error.message || error);
      loading = null;
      throw error;
    });
    return loading;
  };
  const makeStage = async () => {
    try {
      const { face, found } = await loadHer();
      if (!open || stage) return;
      stage = face.stage(canvas, found);
      stageBox.dataset.ready = 'true';
    } catch (error) {
      stageBox.dataset.ready = 'fallback';
    }
  };

  const hook = () => {
    const now = api && api.tap();
    if (!now) { tap = null; return null; }
    if (tap && tap.context === now.context && tap.node === now.node) return tap;
    try {
      const meter = now.context.createAnalyser();
      meter.fftSize = 1024;
      meter.smoothingTimeConstant = 0.2;
      now.node.connect(meter);
      tap = { context: now.context, node: now.node, meter };
      timeData = new Float32Array(meter.fftSize);
      freqData = new Float32Array(meter.frequencyBinCount);
      lips = window.CapriLipSync ? window.CapriLipSync.make() : null;
    } catch (error) { tap = null; }
    return tap;
  };
  const mouthNow = () => {
    const ear = hook();
    if (!ear || !lips || !api.speaking()) return lips ? lips(new Float32Array(8), null, 48000, 1024) : { open: 0, wide: 0, round: 0, level: 0 };
    ear.meter.getFloatTimeDomainData(timeData);
    ear.meter.getFloatFrequencyData(freqData);
    const shape = lips(timeData, freqData, ear.context.sampleRate, ear.meter.fftSize);
    if (shape.open > 0.05) stats.mouth++;
    if (shape.wide > 0.3 || shape.round > 0.3) stats.shapes++;
    return shape;
  };

  const frame = time => {
    raf = 0;
    if (!open) return;
    raf = requestAnimationFrame(frame);
    if (document.hidden || time - last < 30) return;
    const dt = Math.min(0.1, (time - last) / 1000 || 0.03);
    last = time;
    const status = api.status();
    const mouth = mouthNow();
    stats.frames++;
    stageBox.style.setProperty('--her', reduced.matches ? '0' : Math.min(1, mouth.level * 6).toFixed(3));
    if (!stage) return;
    const t = time / 1000, calm = reduced.matches;
    if (status !== mood) {
      mood = status;
      glanceAt = 0;
    }
    if (!blinkAt) blinkAt = t + 1.5 + Math.random() * 3;
    if (t >= blinkAt) {
      blinkFrom = t;
      blinkAt = t + 2.2 + Math.random() * 4 + (Math.random() < 0.18 ? -1.9 : 0);
    }
    const sinceBlink = t - blinkFrom;
    const blink = blinkFrom >= 0 && sinceBlink < 0.16 ? Math.sin(Math.PI * sinceBlink / 0.16) : 0;
    if (t >= glanceAt) {
      if (mood === 'thinking') glanceTo = [0.7 + Math.random() * 0.3, 0.8];
      else if (mood === 'talking' || mood === 'hearing') glanceTo = Math.random() < 0.75 ? [0, 0] : [(Math.random() - 0.5) * 0.9, (Math.random() - 0.5) * 0.5];
      else glanceTo = Math.random() < 0.5 ? [0, 0] : [(Math.random() - 0.5) * 1.4, (Math.random() - 0.5) * 0.8];
      glanceAt = t + (mood === 'thinking' ? 2.5 : 0.8 + Math.random() * 2.2);
    }
    const snap = Math.min(1, dt * 14);
    glance = [glance[0] + (glanceTo[0] - glance[0]) * snap, glance[1] + (glanceTo[1] - glance[1]) * snap];
    spoke += (mouth.open - spoke) * Math.min(1, dt * 6);
    const listening = mood === 'hearing';
    nod += ((listening ? 0.035 * Math.max(0, Math.sin(t * 1.7)) : 0) - nod) * Math.min(1, dt * 3);
    const breathe = calm ? 0 : Math.sin(t * 1.3) * 0.012;
    const sway = calm ? 0 : Math.sin(t * 0.43) * 0.035 + Math.sin(t * 0.71) * 0.015;
    stage.draw({
      mouth,
      blink,
      look: glance,
      turn: [sway + (listening ? 0.05 : 0) + (mood === 'thinking' ? -0.06 : 0), calm ? 0 : Math.sin(t * 0.37) * 0.02 - spoke * 0.03],
      roll: calm ? 0 : (listening ? -0.04 : mood === 'thinking' ? 0.035 : 0) + Math.sin(t * 0.29) * 0.012,
      bob: breathe + spoke * 0.02,
      nod: calm ? 0 : nod - spoke * 0.02
    });
  };

  const grab = (side, into) => {
    const w = video.videoWidth, h = video.videoHeight;
    if (!w || !h) return null;
    const scale = Math.min(1, side / Math.max(w, h));
    into.width = Math.max(1, Math.round(w * scale));
    into.height = Math.max(1, Math.round(h * scale));
    const pen = into.getContext('2d', { willReadFrequently: true });
    pen.drawImage(video, 0, 0, into.width, into.height);
    return pen;
  };
  const thumb = document.createElement('canvas');
  const shot = document.createElement('canvas');
  const changedSince = () => {
    const pen = grab(32, thumb);
    if (!pen) return null;
    const pixels = pen.getImageData(0, 0, thumb.width, thumb.height).data;
    const grey = new Uint8Array(pixels.length / 4);
    for (let i = 0; i < grey.length; i++) grey[i] = (pixels[i * 4] * 3 + pixels[i * 4 + 1] * 6 + pixels[i * 4 + 2]) / 10;
    if (!lastLook || lastLook.length !== grey.length) return { grey, change: 255 };
    let sum = 0;
    for (let i = 0; i < grey.length; i++) sum += Math.abs(grey[i] - lastLook[i]);
    return { grey, change: sum / grey.length };
  };
  const toPicture = () => {
    if (!grab(LOOK_SIDE, shot)) return null;
    let side = LOOK_SIDE;
    for (const quality of [0.72, 0.6, 0.48, 0.36]) {
      const data = shot.toDataURL('image/jpeg', quality).split(',')[1];
      if (data && data.length * 0.75 < LOOK_BYTES) return { mime: 'image/jpeg', data: data.replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '') };
    }
    side = 320;
    if (!grab(side, shot)) return null;
    const data = shot.toDataURL('image/jpeg', 0.5).split(',')[1];
    return data && data.length * 0.75 < LOOK_BYTES ? { mime: 'image/jpeg', data: data.replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '') } : null;
  };
  const lineSent = id => {
    if (!open || cameraState !== 'live' || !seesYou() || typeof id !== 'string') return false;
    const seen = changedSince();
    if (!seen) return false;
    const now = performance.now();
    if (looks > 0 && seen.change < LOOK_CHANGE && now - lastLookAt < LOOK_EVERY) { stats.skipped++; return false; }
    const picture = toPicture();
    if (!picture) return false;
    if (!remote().send('call.look', { id, picture })) return false;
    lastLook = seen.grey;
    lastLookAt = now;
    looks++;
    stats.looks++;
    stats.lookBytes += Math.round(picture.data.length * 0.75);
    you.classList.remove('flash');
    void you.offsetWidth;
    you.classList.add('flash');
    return true;
  };

  const corners = ['tl', 'tr', 'bl', 'br'];
  you.addEventListener('pointerdown', event => {
    if (event.button !== 0) return;
    const box = you.getBoundingClientRect();
    drag = { id: event.pointerId, x: event.clientX, y: event.clientY, left: box.left, top: box.top, moved: false };
    try { you.setPointerCapture(event.pointerId); } catch (error) {}
  });
  you.addEventListener('pointermove', event => {
    if (!drag || event.pointerId !== drag.id) return;
    const dx = event.clientX - drag.x, dy = event.clientY - drag.y;
    if (!drag.moved && Math.hypot(dx, dy) < 6) return;
    drag.moved = true;
    you.classList.add('dragging');
    you.style.transform = 'translate(' + dx + 'px,' + dy + 'px)';
  });
  const letGo = event => {
    if (!drag || event.pointerId !== drag.id) return;
    const moved = drag.moved;
    const box = you.getBoundingClientRect();
    drag = null;
    you.classList.remove('dragging');
    you.style.transform = '';
    if (!moved) { toggleCamera(); return; }
    const holder = (you.parentElement || document.body).getBoundingClientRect();
    const middleX = box.left + box.width / 2 - holder.left, middleY = box.top + box.height / 2 - holder.top;
    corner = (middleY < holder.height / 2 ? 't' : 'b') + (middleX < holder.width / 2 ? 'l' : 'r');
    if (!corners.includes(corner)) corner = 'tr';
    try { localStorage.setItem('capri-remote-pip', corner); } catch (error) {}
    paintYou();
  };
  you.addEventListener('pointerup', letGo);
  you.addEventListener('pointercancel', letGo);
  try { const saved = localStorage.getItem('capri-remote-pip'); if (corners.includes(saved)) corner = saved; } catch (error) {}

  const toggleCamera = () => {
    if (!open) return;
    wantCamera = !wantCamera || cameraState === 'blocked' || cameraState === 'missing';
    if (wantCamera) startCamera();
    else { stopCamera(); paintYou(); }
  };

  const show = given => {
    api = given;
    if (open) return true;
    open = true;
    mood = '';
    looks = 0;
    lastLook = null;
    stageBox.dataset.ready = 'false';
    given.room.prepend(stageBox);
    given.room.append(you);
    if (wantCamera) startCamera();
    paintYou();
    makeStage();
    if (!raf) raf = requestAnimationFrame(frame);
    return true;
  };
  const hide = () => {
    if (!open) return;
    open = false;
    cancelAnimationFrame(raf);
    raf = 0;
    stopCamera();
    if (tap) { try { tap.node.disconnect(tap.meter); } catch (error) {} }
    tap = null;
    if (stage) { stage.dispose(); stage = null; }
    stageBox.remove();
    you.remove();
  };
  document.addEventListener('visibilitychange', () => {
    if (!open) return;
    if (document.hidden) { if (stream) { stopCamera(); cameraState = 'off'; } }
    else if (wantCamera && !stream) startCamera();
    paintYou();
  });
  addEventListener('resize', () => { if (stage) stage.resize(); });

  window.CapriFaceTime = {
    available,
    show,
    hide,
    lineSent,
    toggleCamera,
    get open() { return open; },
    get camera() { return cameraState; },
    get wantCamera() { return wantCamera; },
    get seesYou() { return cameraState === 'live' && seesYou(); },
    stats: () => Object.assign({}, stats, { open, camera: cameraState, ready: stageBox.dataset.ready || '', corner })
  };
})();

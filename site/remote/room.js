(() => {
  'use strict';
  const byId = id => document.getElementById(id);
  const app = byId('app'), room = byId('room'), menu = byId('room-menu');
  const camera = byId('room-camera'), attach = byId('room-attach'), file = byId('room-attach-input');
  const close = byId('room-close'), retry = byId('room-retry'), track = byId('room-track');
  const view = byId('room-view'), video = byId('room-video'), place = byId('room-place'), status = byId('room-status');
  const her = byId('room-her'), sprite = byId('room-sprite'), tint = byId('room-tint'), heart = byId('room-heart');
  const controls = byId('room-controls'), sit = byId('room-sit'), lie = byId('room-lie'), love = byId('room-love');
  const voice = byId('room-voice'), voiceNote = byId('room-voice-note');
  let voiceOn = true, oldVoice = false;
  try { voiceOn = localStorage.getItem('capri-room-voice') !== 'off'; } catch (error) {}
  voice.setAttribute('aria-pressed', String(voiceOn));
  const voiceStatus = text => { if (!room.hidden && voiceOn) voiceNote.textContent = text; };
  const startVoice = () => {
    const r = window.CapriRemote, call = window.CapriCall;
    if (!voiceOn || !r || r.info.native || r.info.demo || r.state !== 'connected') return;
    if (call?.active) return;
    if (!r.info.features?.includes('call')) {
      if (!oldVoice) { voiceStatus("Her voice isn't on Mac or Linux PCs yet."); oldVoice = true; }
      return;
    }
    if (!r.info.features.includes('call-listen')) {
      voiceStatus('Update Capri on your PC to hear her in Room.');
      return;
    }
    if (!call?.listen()) voiceStatus('Tap Voice to hear her.');
  };
  const reduced = matchMedia('(prefers-reduced-motion: reduce)');
  const config = window.CAPRI_CONFIG || {};
  const shared = config.sprites || '../assets/sprites/';
  const own = config.ownSprites || 'sprites/';
  const clips = {};
  for (const [name, count, fps, across, fit, from = shared, type = 'webp'] of [
    ['perch', 60, 30, 10, 'perch'], ['perch_blink', 8, 30, 8, 'perch'], ['perch_blink_glitch', 8, 30, 8, 'perch'],
    ['perch_glitch', 10, 15, 10, 'perch'], ['perch_arrive', 14, 15, 14, 'square', shared, 'png'],
    ['perch_talk', 60, 30, 60, 'music', own, 'png'], ['laydown', 60, 30, 10, 'lie', own, 'png'],
    ['laydown_blink', 8, 30, 8, 'lie', shared, 'png'], ['laydown_glitch', 10, 15, 10, 'wide', shared, 'png'],
    ['laydown_talk', 60, 30, 10, 'lie', own, 'png']
  ]) clips[name] = { url: from + name + '.' + type, count, fps, across, rows: Math.ceil(count / across), fit, body: name.startsWith('laydown') ? 'lie' : 'sit' };
  const strips = new Map();
  const tints = ['mask-image', '-webkit-mask-image'].some(name => CSS.supports(name, 'url("a.png")')) && CSS.supports('mix-blend-mode', 'multiply');
  const D = Math.PI / 180, LENS = Math.tan(32.5 * D), LAG = 60;
  const shot = document.createElement('canvas');
  shot.width = 16;
  shot.height = 12;
  const turns = [];
  let version = 0, stream, session, source, transient, animation, heartTimer, currentClip, currentFrame;
  let pose = 'perch', talking = false, arAvailable = false, surface, seat, drag;
  let suppressTap = false, heartState = 'new', heartWanted = false;
  let act = { name: 'perch', from: 0, once: false, round: -1, blink: '' }, nextGlitch = 0;
  let motion = 'unknown', sensing = false, aim = null, seen = null, frameWatch = 0, pen = null, glow = null, lightAt = 0;
  let drawn = {}, sway = 0, swayAt = 0, lastYaw = null, squash = 1, depth = 1;

  const load = name => {
    if (strips.has(name)) return;
    const image = new Image();
    image.src = clips[name].url;
    const entry = { image, ready: false };
    strips.set(name, entry);
    image.decode().then(() => { entry.ready = true; }, () => { strips.delete(name); });
  };
  const ready = name => strips.get(name)?.ready === true;
  const restClip = () => pose === 'laydown' ? 'laydown' : 'perch';
  const hold = () => { act = { name: restClip(), from: performance.now(), once: false, round: -1, blink: '' }; };
  const play = name => {
    if (reduced.matches || !ready(name)) return false;
    act = { name, from: performance.now(), once: true, round: -1, blink: '' };
    if (name.endsWith('_glitch')) nextGlitch = act.from + 14000 + Math.random() * 14000;
    return true;
  };
  const arrive = () => {
    hold();
    nextGlitch = performance.now() + 14000 + Math.random() * 14000;
    if (pose === 'perch') play('perch_arrive');
  };
  const newRound = now => {
    const roll = Math.random();
    act.blink = act.name === 'perch' ? roll < 0.1 ? 'perch_blink_glitch' : roll < 0.55 ? 'perch_blink' : '' : roll < 0.55 ? 'laydown_blink' : '';
    if (now >= nextGlitch && !talking && !drag) play(act.name === 'perch' ? 'perch_glitch' : 'laydown_glitch');
  };
  const wink = () => {
    if (reduced.matches || act.once) return;
    const blink = act.name === 'perch' ? 'perch_blink_glitch' : 'laydown_blink';
    if (!ready(blink)) return;
    act.from = performance.now() - ((act.name === 'perch' ? 28 : 45) + 0.5) / 30 * 1000;
    act.round = 0;
    act.blink = blink;
  };
  const show = (name, cell) => {
    const clip = clips[name];
    if (name !== currentClip) {
      currentClip = name;
      currentFrame = -1;
      sprite.dataset.fit = clip.fit;
      her.dataset.pose = clip.body;
      const image = 'url("' + clip.url + '")', size = clip.across * 100 + '% ' + clip.rows * 100 + '%';
      sprite.style.backgroundImage = image;
      sprite.style.backgroundSize = size;
      for (const prefix of ['-webkit-', '']) {
        tint.style.setProperty(prefix + 'mask-image', image);
        tint.style.setProperty(prefix + 'mask-size', size);
      }
    }
    if (cell === currentFrame) return;
    currentFrame = cell;
    const at = (clip.across > 1 ? cell % clip.across / (clip.across - 1) * 100 : 0) + '% ' +
      (clip.rows > 1 ? Math.floor(cell / clip.across) / (clip.rows - 1) * 100 : 0) + '%';
    sprite.style.backgroundPosition = at;
    tint.style.setProperty('-webkit-mask-position', at);
    tint.style.setProperty('mask-position', at);
  };
  const drift = now => {
    if (her.hidden) return;
    const calm = reduced.matches ? 0 : 1, lie = her.dataset.pose === 'lie';
    const lift = calm * (Math.sin(now / 1000 * 1.7) * 0.5 + 0.5) * (lie ? 1.6 : 3.2);
    const rock = calm * Math.sin(now / 1000 * 0.9 + 1) * 0.6 + sway;
    sprite.style.setProperty('translate', '0 ' + (-lift).toFixed(2) + '%');
    sprite.style.setProperty('rotate', rock.toFixed(2) + 'deg');
    her.style.setProperty('--shw', (1 - lift * 0.035).toFixed(3));
    her.style.setProperty('--sho', (1 - lift * 0.06).toFixed(3));
    her.style.setProperty('--sq', squash.toFixed(3));
    her.style.setProperty('--lean', (sway * -0.9).toFixed(2) + '%');
    sway *= 0.9;
  };
  const draw = now => {
    animation = requestAnimationFrame(draw);
    drift(now);
    if (!frameWatch) follow(performance.now() - LAG);
    if (now >= lightAt) light(now);
    let clip = clips[act.name];
    let index = Math.max(0, Math.floor((now - act.from) * clip.fps / 1000));
    if (act.once && index >= clip.count) {
      hold();
      clip = clips[act.name];
      index = 0;
    }
    if (!act.once && Math.floor(index / clip.count) !== act.round) {
      act.round = Math.floor(index / clip.count);
      newRound(now);
      if (act.once) {
        clip = clips[act.name];
        index = 0;
      }
    }
    let name = act.name, cell = index % clip.count;
    if (reduced.matches) {
      name = restClip();
      cell = 0;
    } else if (talking && !act.once && ready(name + '_talk')) {
      name += '_talk';
      cell = index % 60;
    } else if (act.blink && ready(act.blink) && name === 'perch' && cell >= 28 && cell < 36) {
      name = act.blink;
      cell -= 28;
    } else if (act.blink && ready(act.blink) && name === 'laydown' && cell >= 45 && cell < 53) {
      name = act.blink;
      cell -= 45;
    }
    if (!ready(name)) {
      if (!ready('perch')) return;
      name = 'perch';
      cell = reduced.matches ? 0 : Math.floor(now * 0.03) % 60;
    }
    show(name, cell);
  };
  const setPose = next => {
    const was = pose;
    pose = next;
    sit.setAttribute('aria-pressed', String(pose === 'perch'));
    lie.setAttribute('aria-pressed', String(pose === 'laydown'));
    if (next === was) return;
    if (her.hidden || talking || !play(was === 'laydown' ? 'laydown_glitch' : 'perch_glitch')) hold();
  };

  const askMotion = () => {
    if (motion !== 'unknown' || typeof DeviceOrientationEvent === 'undefined' || typeof DeviceOrientationEvent.requestPermission !== 'function') return;
    motion = 'asking';
    let asked;
    try { asked = DeviceOrientationEvent.requestPermission(); } catch (error) { motion = 'unknown'; return; }
    Promise.resolve(asked).then(answer => {
      motion = answer === 'granted' ? 'granted' : 'denied';
      if (motion !== 'granted' || !sensing) return;
      removeEventListener('deviceorientation', turned);
      addEventListener('deviceorientation', turned);
    }, () => { motion = 'unknown'; });
  };
  const turned = event => {
    if (event.alpha == null || event.beta == null || event.gamma == null) return;
    turns.push([performance.now(), event.alpha * D, event.beta * D, event.gamma * D]);
    if (turns.length > 24) turns.shift();
  };
  const facing = time => {
    let pick = turns[0];
    for (const turn of turns) if (turn[0] <= time) pick = turn;
    const [, a, b, g] = pick, t = (screen.orientation?.angle ?? window.orientation ?? 0) * D;
    const ca = Math.cos(a), sa = Math.sin(a), cb = Math.cos(b), sb = Math.sin(b), cg = Math.cos(g), sg = Math.sin(g);
    const m = [ca * cg - sa * sb * sg, -cb * sa, cg * sa * sb + ca * sg, cg * sa + ca * sb * sg, ca * cb, sa * sg - ca * cg * sb, -cb * sg, sb, cb * cg];
    const c = Math.cos(t), s = Math.sin(t);
    return [c * m[0] - s * m[1], s * m[0] + c * m[1], m[2], c * m[3] - s * m[4], s * m[3] + c * m[4], m[5], c * m[6] - s * m[7], s * m[6] + c * m[7], m[8]];
  };
  const lens = () => {
    const width = view.clientWidth, height = view.clientHeight, wide = video.videoWidth, high = video.videoHeight;
    if (!width || !height || !wide || !high) return null;
    return { width, height, focal: Math.max(wide, high) / 2 / LENS * Math.max(width / wide, height / high) };
  };
  const toRoom = ({ m, glass }, x, y) => {
    const v = [(x - glass.width / 2) / glass.focal, (glass.height / 2 - y) / glass.focal, -1];
    return [0, 3, 6].map(i => m[i] * v[0] + m[i + 1] * v[1] + m[i + 2] * v[2]);
  };
  const elevation = point => Math.atan2(point[2], Math.hypot(point[0], point[1]));
  const reach = point => {
    const rise = elevation(point);
    return rise < -0.05 ? Math.max(0.5, Math.min(8, 1.4 / Math.tan(-rise))) : 8;
  };
  const follow = time => {
    if (!sensing || !turns.length || session || her.hidden) return;
    const glass = lens();
    if (!glass) return;
    const m = facing(time);
    seen = { m, glass };
    if (!aim || drag) {
      aim = toRoom(seen, her.offsetLeft, her.offsetTop);
      return;
    }
    const v = [0, 1, 2].map(i => m[i] * aim[0] + m[i + 3] * aim[1] + m[i + 6] * aim[2]);
    const away = v[2] > -0.08;
    if (away !== drawn.away) {
      her.style.visibility = away ? 'hidden' : '';
      drawn.away = away;
    }
    if (away) return;
    const x = glass.width / 2 + glass.focal * v[0] / -v[2], y = glass.height / 2 - glass.focal * v[1] / -v[2];
    const left = Math.max(-200, Math.min(300, x / glass.width * 100)).toFixed(2) + '%';
    const top = Math.max(-200, Math.min(300, y / glass.height * 100)).toFixed(2) + '%';
    if (left !== drawn.left) her.style.left = drawn.left = left;
    if (top !== drawn.top) her.style.top = drawn.top = top;
    const rise = elevation(aim), grow = depth;
    if (!reduced.matches) {
      const yaw = Math.atan2(m[1], m[4]), tick = performance.now();
      if (lastYaw !== null && tick - swayAt < 120) {
        let turn = yaw - lastYaw;
        if (turn > Math.PI) turn -= 2 * Math.PI;
        if (turn < -Math.PI) turn += 2 * Math.PI;
        sway = Math.max(-6, Math.min(6, sway + turn / D * 0.6));
      }
      lastYaw = yaw;
      swayAt = tick;
    }
    const flatten = Math.max(0.3, Math.min(1, Math.sin(Math.max(0.1, -rise)) * 1.15));
    squash = flatten;
    const scale = grow.toFixed(3);
    if (scale !== drawn.scale) her.style.scale = drawn.scale = scale;
    const up = [v[0] + m[6] * 0.05, v[1] + m[7] * 0.05, v[2] + m[8] * 0.05];
    if (up[2] >= 0) return;
    const dx = up[0] / -up[2] - v[0] / -v[2], dy = v[1] / -v[2] - up[1] / -up[2];
    if (dx * dx + dy * dy < 1e-8) return;
    const roll = (Math.atan2(dx, -dy) / D).toFixed(1) + 'deg';
    if (roll !== drawn.roll) her.style.rotate = drawn.roll = roll;
  };
  const watchFrames = () => {
    if (frameWatch || !video.requestVideoFrameCallback) return;
    const next = (now, meta) => {
      frameWatch = 0;
      if (!sensing) return;
      frameWatch = video.requestVideoFrameCallback(next);
      const wall = performance.now(), taken = meta?.captureTime;
      follow(taken > wall - 300 && taken < wall ? taken : wall - LAG);
    };
    frameWatch = video.requestVideoFrameCallback(next);
  };
  const startMotion = () => {
    if (sensing || stream?.getVideoTracks()[0]?.getSettings?.().facingMode !== 'environment') return;
    sensing = true;
    addEventListener('deviceorientation', turned);
    watchFrames();
  };
  const stopMotion = () => {
    if (sensing) removeEventListener('deviceorientation', turned);
    sensing = false;
    turns.length = 0;
    aim = seen = null;
    drawn = {};
    if (frameWatch) video.cancelVideoFrameCallback?.(frameWatch);
    frameWatch = 0;
    her.style.removeProperty('rotate');
    her.style.removeProperty('scale');
    her.style.removeProperty('visibility');
    lastYaw = null;
    sway = 0;
    squash = 1;
    depth = 1;
  };
  const light = now => {
    lightAt = now + 350;
    if (!tints || !stream || session || her.hidden || video.readyState < 2) return;
    const wide = video.videoWidth, high = video.videoHeight, width = view.clientWidth, height = view.clientHeight;
    if (!wide || !high || !width || !height) return;
    const scale = Math.max(width / wide, height / high), crop = [width / scale, height / scale];
    let data;
    try {
      pen = pen || shot.getContext('2d', { willReadFrequently: true });
      pen.drawImage(video, (wide - crop[0]) / 2, (high - crop[1]) / 2, crop[0], crop[1], 0, 0, 16, 12);
      data = pen.getImageData(0, 0, 16, 12).data;
    } catch (error) { return; }
    const hx = her.offsetLeft / width * 16, hy = her.offsetTop / height * 12, sum = [0, 0, 0];
    let total = 0;
    for (let i = 0; i < 192; i++) {
      const dx = i % 16 + 0.5 - hx, dy = Math.floor(i / 16) + 0.5 - hy, weight = 0.2 + Math.exp(-(dx * dx + dy * dy) / 12);
      for (let c = 0; c < 3; c++) sum[c] += weight * (data[i * 4 + c] / 255) ** 2;
      total += weight;
    }
    const color = sum.map(value => Math.sqrt(value / total));
    const level = Math.sqrt(0.2126 * color[0] ** 2 + 0.7152 * color[1] ** 2 + 0.0722 * color[2] ** 2);
    const gain = Math.max(0.55, Math.min(1, 0.42 + 1.25 * level)), mean = (color[0] + color[1] + color[2]) / 3;
    const cast = color.map(value => mean > 0.04 ? 1 + (Math.max(0.75, Math.min(1.25, value / mean)) - 1) * 0.45 : 1);
    const peak = Math.max(...cast), target = cast.map(value => value / peak * gain);
    glow = glow ? glow.map((value, i) => value + (target[i] - value) * 0.5) : target;
    tint.style.backgroundColor = 'rgb(' + glow.map(value => Math.round(value * 255)).join(', ') + ')';
  };

  const showMenu = open => {
    menu.hidden = !open;
    camera.setAttribute('aria-expanded', String(open));
    app.classList.toggle('room-menu-open', open);
    if (open) (room.hidden ? byId('room-open') : attach).focus();
  };
  const stopTracks = active => active?.getTracks().forEach(item => item.stop());
  const releaseCamera = () => {
    stopMotion();
    stopTracks(stream);
    stream = null;
    video.srcObject = null;
    video.hidden = true;
    glow = null;
    tint.style.removeProperty('background-color');
  };
  const stopHeart = () => {
    clearTimeout(heartTimer);
    heartTimer = null;
    heart.hidden = true;
    love.setAttribute('aria-pressed', 'false');
    heartWanted = false;
  };
  const finishHeart = () => {
    if (!heartWanted || room.hidden || heartTimer) return;
    if (heartState === 'failed' || reduced.matches) status.textContent = 'Heart.';
    else heart.hidden = false;
    heartTimer = setTimeout(stopHeart, 3600);
  };
  heart.addEventListener('load', () => { heartState = 'ready'; finishHeart(); });
  heart.addEventListener('error', () => { heartState = 'failed'; finishHeart(); });
  const loadHeart = () => {
    if (heartState !== 'new') return;
    heartState = 'loading';
    heart.src = own + 'heart.png';
  };
  const stopDrag = () => {
    const was = drag;
    drag = null;
    if (was && place.hasPointerCapture(was.id)) place.releasePointerCapture(was.id);
  };
  const cancelHits = () => {
    source?.cancel();
    transient?.cancel();
    source = transient = surface = seat = null;
  };
  const closeRoom = (focus = false) => {
    version++;
    stopDrag();
    window.CapriCall?.stopListening();
    talking = false;
    voiceNote.textContent = '';
    releaseCamera();
    const ending = session;
    session = null;
    cancelHits();
    if (ending) ending.end().catch(() => {});
    cancelAnimationFrame(animation);
    animation = 0;
    stopHeart();
    room.hidden = true;
    track.hidden = true;
    retry.hidden = true;
    controls.hidden = place.hidden = her.hidden = true;
    delete app.dataset.room;
    delete view.dataset.surface;
    her.style.left = '';
    her.style.top = '';
    her.style.removeProperty('--s');
    if (focus && !camera.closest('[hidden]') && !document.hidden) camera.focus();
  };
  const prepare = () => {
    if (!room.hidden) return;
    window.CapriCall?.room();
    setPose('perch');
    hold();
    for (const name of Object.keys(clips)) load(name);
    loadHeart();
  };
  const openCamera = async (lead = '', focus = true) => {
    prepare();
    const request = ++version;
    room.hidden = false;
    app.dataset.room = 'camera';
    startVoice();
    retry.hidden = track.hidden = true;
    controls.hidden = place.hidden = her.hidden = true;
    place.setAttribute('aria-label', 'Drag Capri or use arrow keys');
    her.style.left = '';
    her.style.top = '';
    her.style.removeProperty('--s');
    drawn = {};
    status.textContent = lead + 'Starting camera…';
    if (focus) close.focus();
    if (!animation) animation = requestAnimationFrame(draw);
    if (!navigator.mediaDevices?.getUserMedia) {
      status.textContent = 'Room camera is unavailable in this browser.';
      return;
    }
    try {
      const active = await navigator.mediaDevices.getUserMedia({ video: { facingMode: { ideal: 'environment' } }, audio: false });
      if (request !== version || document.hidden) { stopTracks(active); return; }
      stream = active;
      video.srcObject = stream;
      video.hidden = false;
      await video.play();
      if (request !== version) return;
      place.hidden = her.hidden = controls.hidden = false;
      arrive();
      startMotion();
      track.hidden = !arAvailable;
      status.textContent = lead + 'Drag Capri to move her.';
      for (const item of stream.getVideoTracks()) item.addEventListener('ended', () => {
        if (request !== version) return;
        releaseCamera();
        place.hidden = her.hidden = controls.hidden = track.hidden = true;
        retry.hidden = false;
        status.textContent = 'The camera stopped.';
      }, { once: true });
    } catch (error) {
      if (request !== version) return;
      releaseCamera();
      retry.hidden = false;
      status.textContent = error.name === 'NotAllowedError' ? 'Allow camera access in your browser, then retry.'
        : error.name === 'NotFoundError' ? 'No camera was found.' : 'Couldn’t start the camera.';
    }
  };

  const project = (matrix, point, w) => {
    const [x, y, z] = point;
    return [
      matrix[0] * x + matrix[4] * y + matrix[8] * z + matrix[12] * w,
      matrix[1] * x + matrix[5] * y + matrix[9] * z + matrix[13] * w,
      matrix[2] * x + matrix[6] * y + matrix[10] * z + matrix[14] * w,
      matrix[3] * x + matrix[7] * y + matrix[11] * z + matrix[15] * w
    ];
  };
  const startAR = async () => {
    prepare();
    const request = ++version;
    stopDrag();
    releaseCamera();
    room.hidden = false;
    startVoice();
    retry.hidden = true;
    controls.hidden = place.hidden = her.hidden = track.hidden = true;
    stopHeart();
    if (!animation) animation = requestAnimationFrame(draw);
    status.textContent = 'Starting AR…';
    let active;
    try {
      active = await navigator.xr.requestSession('immersive-ar', {
        requiredFeatures: ['hit-test', 'dom-overlay'], domOverlay: { root: app }
      });
      if (request !== version) { await active.end(); return; }
      session = active;
      active.addEventListener('end', () => {
        if (session !== active) return;
        cancelHits();
        session = null;
        closeRoom(true);
      }, { once: true });
      active.addEventListener('visibilitychange', () => {
        if (active === session && active.visibilityState === 'hidden') closeRoom();
      });
      const canvas = document.createElement('canvas');
      const gl = canvas.getContext('webgl', { xrCompatible: true, alpha: true });
      if (!gl) throw new Error('WebGL unavailable');
      const layer = new XRWebGLLayer(active, gl);
      active.updateRenderState({ baseLayer: layer });
      const world = await active.requestReferenceSpace('local');
      const eyes = await active.requestReferenceSpace('viewer');
      const hitSource = await active.requestHitTestSource({ space: eyes });
      if (request !== version) { hitSource.cancel(); return; }
      source = hitSource;
      try {
        const input = await active.requestHitTestSourceForTransientInput({ profile: 'generic-touchscreen' });
        if (request !== version) { input.cancel(); return; }
        transient = input;
      } catch (error) {}
      if (request !== version) return;
      app.dataset.room = 'ar';
      place.hidden = controls.hidden = false;
      place.setAttribute('aria-label', 'Place Capri at center; drag or use arrow keys');
      status.textContent = 'Aim at a surface, then tap to place Capri.';
      const frame = (now, xrFrame) => {
        if (active !== session) return;
        active.requestAnimationFrame(frame);
        gl.bindFramebuffer(gl.FRAMEBUFFER, layer.framebuffer);
        gl.clearColor(0, 0, 0, 0);
        gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
        const hit = xrFrame.getHitTestResults(source)[0]?.getPose(world)?.transform.position;
        surface = hit ? [hit.x, hit.y, hit.z] : null;
        view.toggleAttribute('data-surface', !!surface);
        if (drag?.moved && transient) {
          const inputs = xrFrame.getHitTestResultsForTransientInput(transient);
          const input = inputs.find(item => !drag.input || item.inputSource === drag.input);
          if (input) {
            drag.input = input.inputSource;
            const point = input.results[0]?.getPose(world)?.transform.position;
            if (point) seat = [point.x, point.y, point.z];
          }
        }
        const eye = xrFrame.getViewerPose(world)?.views[0];
        if (!seat || !eye) { her.hidden = true; return; }
        const position = project(eye.transform.inverse.matrix, seat, 1);
        const point = project(eye.projectionMatrix, position, 1);
        const depth = point[3];
        const x = point[0] / depth, y = point[1] / depth;
        her.hidden = depth <= 0 || Math.abs(x) > 1.3 || Math.abs(y) > 1.3;
        if (her.hidden) return;
        her.style.left = ((x + 1) * 50) + '%';
        her.style.top = ((1 - y) * 50) + '%';
        const lookFrom = eye.transform.position, drop = lookFrom.y - seat[1], run = Math.hypot(lookFrom.x - seat[0], lookFrom.z - seat[2]);
        squash = Math.max(0.25, Math.min(1, drop / Math.max(0.05, Math.hypot(drop, run)) * 1.15));
        drift(now);
        const size = view.clientHeight * eye.projectionMatrix[5] * 0.16 / depth;
        her.style.setProperty('--s', Math.max(24, Math.min(size, 300)) + 'px');
      };
      active.requestAnimationFrame(frame);
    } catch (error) {
      if (request !== version) return;
      session = null;
      cancelHits();
      if (active) await active.end().catch(() => {});
      if (request === version) openCamera('AR unavailable. ', false);
    }
  };

  camera.addEventListener('click', () => showMenu(menu.hidden));
  byId('room-open').addEventListener('click', () => {
    showMenu(false);
    askMotion();
    if (room.hidden) {
      if (arAvailable) startAR();
      else openCamera();
    } else close.focus();
  });
  attach.addEventListener('click', () => { showMenu(false); camera.focus(); file.click(); });
  close.addEventListener('click', () => closeRoom(true));
  retry.addEventListener('click', () => { askMotion(); if (arAvailable && !stream) startAR(); else openCamera(); });
  track.addEventListener('click', () => { close.focus(); startAR(); });
  sit.addEventListener('click', () => setPose('perch'));
  lie.addEventListener('click', () => setPose('laydown'));
  const showHeart = () => {
    if (heartWanted && heartState === 'loading') return;
    stopHeart();
    heartWanted = true;
    love.setAttribute('aria-pressed', 'true');
    wink();
    if (reduced.matches || heartState === 'failed') { finishHeart(); return; }
    if (heartState === 'ready') {
      heartState = 'new';
      heart.removeAttribute('src');
    }
    loadHeart();
  };
  love.addEventListener('click', showHeart);
  voice.addEventListener('click', () => {
    if (voiceOn && (voiceNote.textContent === 'Tap Voice to hear her.' || !window.CapriCall?.listening && /Tap Voice|another call/.test(voiceNote.textContent))) { startVoice(); return; }
    voiceOn = !voiceOn;
    voice.setAttribute('aria-pressed', String(voiceOn));
    try { localStorage.setItem('capri-room-voice', voiceOn ? 'on' : 'off'); } catch (error) {}
    voiceNote.textContent = '';
    if (voiceOn) startVoice();
    else window.CapriCall?.stopListening();
  });
  const moveCamera = (x, y) => {
    const box = view.getBoundingClientRect();
    const left = Math.max(16, Math.min(84, x / box.width * 100)), top = Math.max(42, Math.min(98, y / box.height * 100));
    her.style.left = left + '%';
    her.style.top = top + '%';
    her.style.removeProperty('visibility');
    drawn = { roll: drawn.roll };
    if (seen) {
      aim = toRoom(seen, left / 100 * seen.glass.width, top / 100 * seen.glass.height);
      depth = Math.max(0.4, Math.min(1.8, reach(toRoom(seen, seen.glass.width / 2, seen.glass.height * 0.74)) / reach(aim)));
    }
  };
  place.addEventListener('pointerdown', event => {
    if (drag || !event.isPrimary || event.button !== 0) return;
    suppressTap = false;
    const box = sprite.getBoundingClientRect();
    if (her.hidden || her.style.visibility === 'hidden' || event.clientX < box.left || event.clientX > box.right ||
        event.clientY < box.top || event.clientY > box.bottom) return;
    drag = { id: event.pointerId, x: event.clientX, y: event.clientY,
      left: her.offsetLeft, top: her.offsetTop, moved: false };
    place.setPointerCapture(event.pointerId);
  });
  place.addEventListener('pointermove', event => {
    if (!drag || drag.id !== event.pointerId) return;
    const x = event.clientX - drag.x, y = event.clientY - drag.y;
    if (!drag.moved && Math.hypot(x, y) <= 5) return;
    drag.moved = suppressTap = true;
    event.preventDefault();
    if (!session) moveCamera(drag.left + x, drag.top + y);
    status.textContent = '';
  });
  place.addEventListener('pointerup', event => {
    if (drag?.id !== event.pointerId) return;
    if (drag.moved) event.preventDefault();
    stopDrag();
    askMotion();
  });
  for (const type of ['pointercancel', 'lostpointercapture']) place.addEventListener(type, event => {
    if (drag?.id === event.pointerId) stopDrag();
  });
  place.addEventListener('keydown', event => {
    const directions = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] };
    const direction = directions[event.key];
    if (!direction) return;
    event.preventDefault();
    const step = event.shiftKey ? 20 : 5;
    if (session) {
      if (seat) seat = [seat[0] + direction[0] * step / 250, seat[1] - direction[1] * step / 250, seat[2]];
    } else moveCamera(her.offsetLeft + direction[0] * step, her.offsetTop + direction[1] * step);
    status.textContent = '';
  });
  place.addEventListener('click', event => {
    if (suppressTap && event.detail) { event.preventDefault(); event.stopImmediatePropagation(); return; }
    askMotion();
    if (session) {
      if (!surface) return;
      seat = surface;
    } else {
      const box = view.getBoundingClientRect();
      const x = event.detail ? (event.clientX - box.left) / box.width : 0.5;
      const y = event.detail ? (event.clientY - box.top) / box.height : 0.74;
      moveCamera(x * box.width, y * box.height);
    }
    status.textContent = '';
  });
  place.addEventListener('beforexrselect', event => event.preventDefault());
  document.addEventListener('pointerdown', event => {
    if (!menu.hidden && !menu.contains(event.target) && !camera.contains(event.target)) showMenu(false);
  });
  document.addEventListener('keydown', event => {
    if (event.key !== 'Escape') return;
    if (menu.hidden) { if (!room.hidden) closeRoom(true); return; }
    showMenu(false);
    camera.focus();
  });
  document.addEventListener('visibilitychange', () => {
    if (document.hidden) closeRoom();
  });
  addEventListener('pagehide', () => closeRoom());
  new MutationObserver(() => {
    if (app.dataset.screen !== 'chat') { showMenu(false); closeRoom(); }
  }).observe(app, { attributeFilter: ['data-screen'] });
  navigator.xr?.isSessionSupported('immersive-ar').then(supported => {
    arAvailable = supported;
    if (stream && !controls.hidden) track.hidden = !supported;
  }).catch(() => {});
  const r = window.CapriRemote;
  r?.onMessage(message => {
    if (room.hidden || message.type !== 'chat.tool' || message.state !== 'done') return;
    if (message.move === 'sit') setPose('perch');
    else if (message.move === 'lie_down') setPose('laydown');
    else if (message.move === 'heart') showHeart();
  });
  r?.onState(next => {
    if (room.hidden) return;
    if (next === 'connected') queueMicrotask(() => { if (!room.hidden) startVoice(); });
    else { window.CapriCall?.stopListening(); talking = false; voiceNote.textContent = ''; }
  });
  window.CapriRoom = { close: closeRoom, startVoice: () => { if (!room.hidden) startVoice(); }, voiceStatus, talk: on => { talking = !room.hidden && !!on; } };
})();

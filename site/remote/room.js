(() => {
  'use strict';
  const byId = id => document.getElementById(id);
  const app = byId('app'), room = byId('room'), menu = byId('room-menu');
  const camera = byId('room-camera'), attach = byId('room-attach'), file = byId('room-attach-input');
  const close = byId('room-close'), retry = byId('room-retry'), track = byId('room-track');
  const view = byId('room-view'), video = byId('room-video'), place = byId('room-place'), status = byId('room-status');
  const her = byId('room-her'), sprite = byId('room-sprite'), tint = byId('room-tint'), heart = byId('room-heart');
  const controls = byId('room-controls'), sit = byId('room-sit'), lie = byId('room-lie'), love = byId('room-love');
  const voice = byId('room-voice'), voiceNote = byId('room-voice-note'), record = byId('room-record');
  const sizeBox = byId('room-size-box'), sizer = byId('room-size'), behindButton = byId('room-behind'), stillButton = byId('room-still');
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
  let kit, kitLoading, no3d = false, stage, xr3d, canvas3d;
  let makeLife, life = null, lifeAt = 0, spots = [], hand = null;
  let grow = 1, cutout = null, cutLoading = null, cutCanvas = null, cutPen = null, cutAt = 0, behind = false, hideMe = false, cutState = '', mouthUntil = 0, stayStill = false, hostTalking = false;
  try { grow = Math.max(0.5, Math.min(4, Number(localStorage.getItem('capri-room-size')) || 1)); } catch (error) {}
  sizer.value = String(grow);
  const YOU = 0.85;
  let front = false, desk = false, you = null, lookAt = 0, lookPen, lastLook, tape, tapePen, recorder, takes, tapeTimer;
  const NEAR = 0.6, EYE = 0.9, DESK = 0.3, TILT = 14 * D, TALL = 0.24, TAPE = 60000;
  const tilt = [1, 0, 0, 0, Math.cos(TILT), Math.sin(TILT), 0, -Math.sin(TILT), Math.cos(TILT)];
  const looker = document.createElement('canvas');
  looker.width = 32;
  looker.height = 24;
  const wide = matchMedia('(min-width: 600px) and (not (pointer: coarse))');

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
    if (desk && now >= lookAt) notice(now);
    if (stage) render3d(now);
    if (recorder) film();
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
  const setPose = (next, fromLife = false) => {
    const was = pose;
    pose = next;
    if (!fromLife && life?.position) life.setPose(next, performance.now());
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
    if (life?.position) return;
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
    const rise = elevation(aim), grow = stage ? 1 : depth;
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
    const hx = (front ? width - her.offsetLeft : her.offsetLeft) / width * 16, hy = her.offsetTop / height * 12, sum = [0, 0, 0];
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

  const notice = now => {
    lookAt = now + 100;
    if (!stream || video.readyState < 2 || !video.videoWidth) return;
    let data;
    try {
      lookPen = lookPen || looker.getContext('2d', { willReadFrequently: true });
      lookPen.drawImage(video, 0, 0, 32, 24);
      data = lookPen.getImageData(0, 0, 32, 24).data;
    } catch (error) { return; }
    const gray = new Float32Array(768);
    for (let i = 0; i < 768; i++) gray[i] = (data[i * 4] + data[i * 4 + 1] + data[i * 4 + 2]) / 3;
    if (lastLook) {
      let sum = 0, sx = 0, sy = 0;
      for (let i = 0; i < 768; i++) {
        const change = Math.abs(gray[i] - lastLook[i]);
        if (change < 16) continue;
        sum += change;
        sx += change * (i % 32 + 0.5) / 32;
        sy += change * (Math.floor(i / 32) + 0.5) / 24;
      }
      if (sum > 1500) {
        const spot = [front ? 1 - sx / sum : sx / sum, sy / sum];
        you = you ? you.map((value, i) => value + (spot[i] - value) * 0.25) : spot;
      }
    }
    lastLook = gray;
  };
  const film = () => {
    const width = tape.width, height = tape.height, wide = video.videoWidth, high = video.videoHeight;
    if (!wide || !high) return;
    const scale = Math.max(width / wide, height / high), crop = [width / scale, height / scale];
    tapePen.save();
    if (front) { tapePen.translate(width, 0); tapePen.scale(-1, 1); }
    tapePen.drawImage(video, (wide - crop[0]) / 2, (high - crop[1]) / 2, crop[0], crop[1], 0, 0, width, height);
    tapePen.restore();
    if (canvas3d) tapePen.drawImage(canvas3d, 0, 0, width, height);
    if (cutCanvas && behind) tapePen.drawImage(cutCanvas, 0, 0, width, height);
    if (!heart.hidden && heart.complete && heart.naturalWidth) {
      const fit = Math.max(width / heart.naturalWidth, height / heart.naturalHeight);
      tapePen.drawImage(heart, (width - heart.naturalWidth * fit) / 2, (height - heart.naturalHeight * fit) / 2, heart.naturalWidth * fit, heart.naturalHeight * fit);
    }
  };
  const canRecord = () => desk && !!stage && typeof MediaRecorder === 'function' && typeof HTMLCanvasElement.prototype.captureStream === 'function';
  const showRecord = () => {
    record.hidden = !canRecord() || controls.hidden;
    sizeBox.hidden = behindButton.hidden = !desk || !stage || controls.hidden;
    stillButton.hidden = !stage || controls.hidden;
    record.setAttribute('aria-pressed', String(!!recorder));
    record.textContent = recorder ? 'Stop' : 'Record';
  };
  const stopRecording = (keep = true) => {
    clearTimeout(tapeTimer);
    const was = recorder;
    recorder = null;
    if (was) {
      if (!keep) was.ondataavailable = was.onstop = null;
      try { was.stop(); } catch (error) {}
    }
    showRecord();
  };
  const startRecording = () => {
    if (recorder || !canRecord()) return;
    const box = view.getBoundingClientRect(), scale = Math.min(1, 1280 / Math.max(1, box.width), 1280 / Math.max(1, box.height));
    tape = document.createElement('canvas');
    tape.width = Math.max(2, Math.round(box.width * scale / 2) * 2);
    tape.height = Math.max(2, Math.round(box.height * scale / 2) * 2);
    tapePen = tape.getContext('2d');
    const type = ['video/mp4;codecs=avc1', 'video/mp4', 'video/webm;codecs=vp9', 'video/webm'].find(kind => MediaRecorder.isTypeSupported?.(kind)) || '';
    let made;
    try { made = new MediaRecorder(tape.captureStream(30), type ? { mimeType: type, videoBitsPerSecond: 6000000 } : undefined); } catch (error) {
      status.textContent = 'Couldn’t start recording.';
      return;
    }
    takes = [];
    made.ondataavailable = event => { if (event.data?.size) takes.push(event.data); };
    made.onstop = () => {
      const kind = made.mimeType || type || 'video/webm', clip = new Blob(takes, { type: kind });
      takes = [];
      if (!clip.size) return;
      const link = document.createElement('a'), stamp = new Date();
      const pad = value => String(value).padStart(2, '0');
      link.href = URL.createObjectURL(clip);
      link.download = 'capri-room-' + stamp.getFullYear() + pad(stamp.getMonth() + 1) + pad(stamp.getDate()) + '-' + pad(stamp.getHours()) + pad(stamp.getMinutes()) + pad(stamp.getSeconds()) + (kind.includes('mp4') ? '.mp4' : '.webm');
      document.body.append(link);
      link.click();
      link.remove();
      setTimeout(() => URL.revokeObjectURL(link.href), 60000);
      if (!room.hidden) status.textContent = 'Saved your clip.';
    };
    film();
    made.start(1000);
    recorder = made;
    tapeTimer = setTimeout(() => stopRecording(), TAPE);
    status.textContent = 'Recording. Tap Stop to save it.';
    showRecord();
  };

  const showMenu = open => {
    menu.hidden = !open;
    camera.setAttribute('aria-expanded', String(open));
    app.classList.toggle('room-menu-open', open);
    if (open) (room.hidden ? byId('room-open') : attach).focus();
  };
  const stopTracks = active => active?.getTracks().forEach(item => item.stop());
  const ensure3d = () => {
    if (no3d) return Promise.reject(new Error('3D unavailable'));
    kitLoading = kitLoading || Promise.all([import('./room3d.js'), import('./room-life.js')])
      .then(([module, alive]) => module.load(own).then(loaded => { kit = loaded; makeLife = alive.createLife; return loaded; }));
    kitLoading.catch(() => { no3d = true; });
    return kitLoading;
  };
  const end3d = () => {
    stopRecording();
    if (!session) { life = null; lifeAt = 0; }
    stage?.dispose();
    stage = null;
    canvas3d?.remove();
    canvas3d = null;
    cutCanvas?.remove();
    cutCanvas = cutPen = null;
    behind = false;
    cutout?.close();
    cutout = null;
    cutLoading = null;
    cutState = '';
    her.style.removeProperty('opacity');
  };
  const endXr = () => {
    life = null;
    lifeAt = 0;
    spots = [];
    hand = null;
    xr3d?.dispose();
    xr3d = null;
    her.style.removeProperty('opacity');
  };
  const begin3d = async request => {
    if (no3d || stage) return;
    try {
      const loaded = await ensure3d();
      if (request !== version || stage || session || room.hidden) return;
      const canvas = document.createElement('canvas');
      for (const [name, value] of [['position', 'absolute'], ['inset', '0'], ['width', '100%'], ['height', '100%'], ['pointer-events', 'none']]) canvas.style.setProperty(name, value);
      view.insertBefore(canvas, place);
      try { stage = loaded.stage({ canvas }); } catch (error) { canvas.remove(); throw error; }
      canvas3d = canvas;
      her.style.opacity = '0';
      if (desk) {
        cutCanvas = document.createElement('canvas');
        for (const [name, value] of [['position', 'absolute'], ['inset', '0'], ['width', '100%'], ['height', '100%'], ['pointer-events', 'none']]) cutCanvas.style.setProperty(name, value);
        view.insertBefore(cutCanvas, place);
        cutPen = cutCanvas.getContext('2d');
        loadCutout();
      }
      showRecord();
    } catch (error) {
      no3d = true;
      end3d();
    }
  };
  const standAt = glass => {
    let dir;
    if (seen && aim) dir = [aim[0], aim[2], -aim[1]];
    else dir = [(her.offsetLeft - glass.width / 2) / glass.focal, (glass.height / 2 - her.offsetTop) / glass.focal, -1];
    if (desk) dir = [dir[0], tilt[4] * dir[1] + tilt[5] * dir[2], tilt[7] * dir[1] + tilt[8] * dir[2]];
    const flat = Math.hypot(dir[0], dir[2]);
    if (flat < 1e-4) return null;
    const eye = desk ? DESK : EYE, near = desk ? 0.3 : 0.9, most = desk ? 2.4 : 6;
    const far = -dir[1] / flat > 0.05 ? Math.max(near, Math.min(most, eye * flat / -dir[1])) : most;
    return [dir[0] / flat * far, -eye, dir[2] / flat * far];
  };
  const deskTall = () => TALL * grow;
  const resize = next => {
    grow = Math.max(0.5, Math.min(4, next));
    sizer.value = String(grow);
    try { localStorage.setItem('capri-room-size', String(grow)); } catch (error) {}
    if (desk) life?.setTall(deskTall());
  };
  const mumble = text => {
    if (room.hidden || !text) return;
    const now = performance.now();
    mouthUntil = Math.min(now + 8000, Math.max(mouthUntil, now) + Math.min(5000, 300 + String(text).length * 55));
  };
  const sayLine = text => { if (!room.hidden) { status.textContent = text; mumble(text); } };
  const newLife = tall => makeLife ? makeLife({ tall, say: sayLine, calm: () => reduced.matches || stayStill }) : null;
  const turnOf = () => {
    const m = seen?.m;
    return m ? [m[0], m[1], m[2], m[6], m[7], m[8], -m[3], -m[4], -m[5]] : desk ? tilt : [1, 0, 0, 0, 1, 0, 0, 0, 1];
  };
  const rayAt = (glass, x, y) => {
    const r = turnOf(), v = [(x - glass.width / 2) / glass.focal, (glass.height / 2 - y) / glass.focal, -1];
    return [r[0] * v[0] + r[1] * v[1] + r[2] * v[2], r[3] * v[0] + r[4] * v[1] + r[5] * v[2], r[6] * v[0] + r[7] * v[1] + r[8] * v[2]];
  };
  const screenOf = (glass, p) => {
    const r = turnOf(), c = [r[0] * p[0] + r[3] * p[1] + r[6] * p[2], r[1] * p[0] + r[4] * p[1] + r[7] * p[2], r[2] * p[0] + r[5] * p[1] + r[8] * p[2]];
    if (c[2] > -0.02) return null;
    return [glass.width / 2 + glass.focal * c[0] / -c[2], glass.height / 2 - glass.focal * c[1] / -c[2]];
  };
  const floorY = () => desk ? -DESK : -EYE;
  const onFloor = (glass, x, y) => {
    const d = rayAt(glass, x, y);
    if (d[1] > -0.02) return null;
    const t = floorY() / d[1], far = desk ? 2.4 : 6, flat = Math.hypot(d[0], d[2]) * t;
    const k = flat > far ? far / flat : 1;
    return [d[0] * t * k, floorY(), d[2] * t * k];
  };
  const cameraWorld = glass => ({
    viewer: [0, 0, 0],
    busy: talking,
    groundAt: () => floorY(),
    raised: () => false,
    nearby: () => onFloor(glass, glass.width * 0.5, glass.height * 0.8) || life.home,
    contain: (p, v) => {
      if (!desk) return;
      const near = onFloor(glass, glass.width / 2, glass.height * 0.97), far = onFloor(glass, glass.width / 2, glass.height * 0.5);
      if (near && p[2] > near[2] && v[2] > 0) { p[2] = near[2]; v[2] *= -0.4; }
      if (far && p[2] < far[2] && v[2] < 0) { p[2] = far[2]; v[2] *= -0.4; }
      if (p[2] > -0.05) return;
      const edge = -p[2] * (glass.width / 2 - 24) / glass.focal;
      if (p[0] < -edge && v[0] < 0 || p[0] > edge && v[0] > 0) {
        p[0] = Math.max(-edge, Math.min(edge, p[0]));
        v[0] *= -0.5;
      }
    },
    wander: (from, home) => {
      for (let tries = 0; tries < 8; tries++) {
        const reach = desk ? 0.22 : 0.9, angle = Math.random() * Math.PI * 2, far = reach * (0.3 + Math.random() * 0.7);
        const spot = [home[0] + Math.cos(angle) * far, floorY(), home[2] + Math.sin(angle) * far];
        const at = screenOf(glass, spot);
        if (!desk || at && at[0] > glass.width * 0.12 && at[0] < glass.width * 0.88 && at[1] > glass.height * 0.55 && at[1] < glass.height * 0.96) return spot;
      }
      return null;
    }
  });
  const lifeClip = now => life ? { pose: life.pose, clip: life.clip(now), heading: life.mode === 'walk' ? life.face : null } : { pose };
  const syncLife = () => {
    if (life && life.pose !== pose && life.mode !== 'held' && life.mode !== 'fly') setPose(life.pose, true);
  };
  const lifeStep = now => {
    const dt = lifeAt ? (now - lifeAt) / 1000 : 0;
    lifeAt = now;
    return dt;
  };
  const loadCutout = () => {
    if (cutLoading) return;
    cutState = 'loading';
    const mine = cutLoading = import('./room-cutout.js').then(module => module.startCutout(new URL('vendor/mediapipe/', location.href).href))
      .then(made => {
        if (cutLoading !== mine || !desk || !stream) { made.close(); return; }
        cutout = made;
        cutState = 'ready';
        if (hideMe) sayLine('Found you. She\'s behind you now.');
      }, error => {
        if (cutLoading !== mine) return;
        cutState = 'failed';
        console.warn('room cutout', error);
        if (hideMe) sayLine('Couldn\'t find you in the picture on this PC.');
      });
  };
  const cutLayer = (now, position) => {
    if (!cutCanvas) return;
    const far = position ? Math.hypot(position[0], position[2]) : 0;
    if (!cutout || !position || !(hideMe || far > YOU)) {
      if (behind) { cutPen.clearRect(0, 0, cutCanvas.width, cutCanvas.height); behind = false; }
      return;
    }
    if (now - cutAt < 30) return;
    cutAt = now;
    const mask = cutout.read(video, now);
    if (!mask) return;
    const width = view.clientWidth, height = view.clientHeight, wide = video.videoWidth, high = video.videoHeight;
    if (!width || !height || !wide || !high) return;
    if (cutCanvas.width !== width || cutCanvas.height !== height) { cutCanvas.width = width; cutCanvas.height = height; }
    const scale = Math.max(width / wide, height / high), crop = [width / scale, height / scale], sx = (wide - crop[0]) / 2, sy = (high - crop[1]) / 2;
    const mx = mask.width / wide, my = mask.height / high;
    cutPen.save();
    cutPen.clearRect(0, 0, width, height);
    if (front) { cutPen.translate(width, 0); cutPen.scale(-1, 1); }
    cutPen.drawImage(video, sx, sy, crop[0], crop[1], 0, 0, width, height);
    cutPen.globalCompositeOperation = 'destination-in';
    cutPen.drawImage(mask, sx * mx, sy * my, crop[0] * mx, crop[1] * my, 0, 0, width, height);
    cutPen.restore();
    behind = true;
  };
  const lookFor = position => {
    if (!desk || !you || !position) return null;
    return [(you[0] - 0.5) * 1.4, position[1] + 0.25, Math.max(position[2] + 0.35, 0.1)];
  };
  const render3d = now => {
    const glass = lens();
    if (!glass || her.hidden) {
      stage.update({ now, position: null });
      stage.draw();
      return;
    }
    const m = seen?.m;
    stage.view(glass.width, glass.height, glass.focal, m ? [m[0], m[1], m[2], m[6], m[7], m[8], -m[3], -m[4], -m[5]] : desk ? tilt : null);
    if (!life && makeLife) life = newLife(desk ? deskTall() : 0.8);
    if (life && !life.position) {
      const start = standAt(glass);
      if (start) { life.place(start, now); life.setPose(pose, now); }
    }
    const world = cameraWorld(glass), dt = lifeStep(now);
    if (life?.position) {
      if (drag?.grabbed && drag.at) life.drag(drag.at(glass), now);
      life.tick(now, dt, world);
      syncLife();
    }
    const position = life?.position || standAt(glass);
    const mouth = talking || hostTalking || now < mouthUntil;
    if (her.dataset.mouth !== String(mouth)) her.dataset.mouth = String(mouth);
    stage.update({ now, position, ...lifeClip(now), talking: mouth, tint: glow, calm: reduced.matches, turn: sway * 0.04, viewer: lookFor(position), tall: desk ? deskTall() * Math.max(0.15, Math.hypot(position[0], position[1], position[2])) / NEAR : 0, ground: floorY() });
    if (desk) cutLayer(now, position);
    stage.draw();
    if (!position) return;
    her.style.setProperty('--s', Math.max(40, Math.min(320 * (desk ? grow : 1), desk ? glass.focal * deskTall() / NEAR : glass.focal * 0.8 / Math.hypot(position[0], position[1], position[2]))).toFixed(1) + 'px');
    if (life?.position) {
      const at = screenOf(glass, position);
      her.style.visibility = at ? '' : 'hidden';
      if (at) {
        her.style.left = (at[0] / glass.width * 100).toFixed(2) + '%';
        her.style.top = (at[1] / glass.height * 100).toFixed(2) + '%';
      }
    }
  };
  const releaseCamera = () => {
    end3d();
    stopMotion();
    stopTracks(stream);
    stream = null;
    video.srcObject = null;
    video.hidden = true;
    front = desk = false;
    you = lastLook = null;
    delete view.dataset.front;
    delete view.dataset.desk;
    delete app.dataset.desk;
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
    if (was?.grabbed && life?.mode === 'held') life.release(performance.now());
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
    endXr();
    if (ending) ending.end().catch(() => {});
    cancelAnimationFrame(animation);
    animation = 0;
    stopHeart();
    room.hidden = true;
    track.hidden = true;
    retry.hidden = true;
    controls.hidden = place.hidden = her.hidden = record.hidden = sizeBox.hidden = behindButton.hidden = stillButton.hidden = true;
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
    ensure3d().catch(() => {});
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
      front = stream.getVideoTracks()[0]?.getSettings?.().facingMode !== 'environment';
      desk = front && wide.matches;
      view.toggleAttribute('data-front', front);
      view.toggleAttribute('data-desk', desk);
      app.toggleAttribute('data-desk', desk);
      if (desk) {
        her.style.left = '74%';
        her.style.top = '90%';
      }
      place.hidden = her.hidden = controls.hidden = false;
      showRecord();
      arrive();
      startMotion();
      begin3d(request);
      track.hidden = !arAvailable;
      status.textContent = lead + (desk ? 'Drag Capri anywhere on your desk.' : 'Drag Capri to move her.');
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
  const addSpot = p => {
    if (spots.some(spot => Math.hypot(spot[0] - p[0], spot[1] - p[1], spot[2] - p[2]) < 0.18)) return;
    spots.push(p.slice());
    if (spots.length > 160) spots.shift();
  };
  const floorXr = () => {
    if (!spots.length) return life?.home ? life.home[1] : 0;
    const heights = spots.map(spot => spot[1]).sort((a, b) => a - b);
    return heights[Math.floor(heights.length * 0.1)];
  };
  const groundXr = p => {
    let best = floorXr();
    for (const spot of spots) if (Math.hypot(spot[0] - p[0], spot[2] - p[2]) < 0.45 && spot[1] <= p[1] + 0.06 && spot[1] > best) best = spot[1];
    return best;
  };
  const raisedXr = p => groundXr(p) > floorXr() + 0.25;
  const xrWorld = viewer => ({
    viewer,
    busy: talking,
    groundAt: groundXr,
    raised: raisedXr,
    nearby: () => life.home,
    wander: (from, home) => {
      const near = spots.filter(spot => Math.hypot(spot[0] - home[0], spot[2] - home[2]) < 1.6 && Math.hypot(spot[0] - from[0], spot[2] - from[2]) > 0.15);
      const level = near.filter(spot => Math.abs(spot[1] - from[1]) < 0.08 && Math.hypot(spot[0] - from[0], spot[2] - from[2]) < 0.9);
      const up = near.filter(spot => Math.abs(spot[1] - from[1]) >= 0.08 && raisedXr(spot) !== raisedXr(from));
      const list = up.length && (Math.random() < 0.3 || !level.length) ? up : level;
      return list.length ? list[Math.floor(Math.random() * list.length)] : null;
    }
  });
  const settle = point => {
    if (!life) return;
    const now = performance.now();
    addSpot(point);
    if (!life.position) life.place(point, now, raisedXr(point));
    else life.goTo(point, now);
  };
  const rayOf = (xrFrame, space, world) => {
    const pose = xrFrame.getPose(space, world);
    if (!pose) return null;
    const m = pose.transform.matrix;
    return { from: [m[12], m[13], m[14]], dir: [-m[8], -m[9], -m[10]] };
  };
  const touchRay = (eye, x, y) => {
    const width = view.clientWidth, height = view.clientHeight, P = eye.projectionMatrix, M = eye.transform.matrix;
    if (!width || !height) return null;
    const c = [(2 * x / width - 1 + P[8]) / P[0], (1 - 2 * y / height + P[9]) / P[5], -1];
    const dir = [0, 1, 2].map(i => M[i] * c[0] + M[i + 4] * c[1] + M[i + 8] * c[2]);
    return { from: [M[12], M[13], M[14]], dir };
  };
  const along = (ray, far) => {
    const n = Math.hypot(ray.dir[0], ray.dir[1], ray.dir[2]) || 1;
    return ray.dir.map((value, i) => ray.from[i] + value / n * far);
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
        requiredFeatures: ['hit-test'], optionalFeatures: ['dom-overlay'], domOverlay: { root: app }
      });
      if (request !== version) { await active.end(); return; }
      session = active;
      active.addEventListener('end', () => {
        if (session !== active) return;
        cancelHits();
        session = null;
        closeRoom(true);
      }, { once: true });
      let xrWorldSpace = null;
      if (!active.domOverlayState) {
        active.addEventListener('selectstart', event => {
          if (active !== session || !life?.position || !xrWorldSpace || hand) return;
          const ray = rayOf(event.frame, event.inputSource.targetRaySpace, xrWorldSpace);
          if (!ray) return;
          const p = life.position, middle = [p[0], p[1] + 0.22, p[2]];
          const t = (middle[0] - ray.from[0]) * ray.dir[0] + (middle[1] - ray.from[1]) * ray.dir[1] + (middle[2] - ray.from[2]) * ray.dir[2];
          if (t <= 0) return;
          const close = along(ray, t);
          if (Math.hypot(close[0] - middle[0], close[1] - middle[1], close[2] - middle[2]) > 0.3) return;
          hand = { input: event.inputSource, far: t, used: true };
          life.grab(close, performance.now());
        });
        active.addEventListener('selectend', event => {
          if (hand?.input !== event.inputSource) return;
          life?.release(performance.now());
          setTimeout(() => { hand = null; }, 0);
        });
        active.addEventListener('select', event => {
          if (active !== session || hand?.input === event.inputSource) return;
          if (surface) settle(surface);
        });
      }
      active.addEventListener('visibilitychange', () => {
        if (active === session && active.visibilityState === 'hidden') closeRoom();
      });
      let gl, layer;
      if (!no3d) {
        try {
          const loaded = await ensure3d();
          if (request !== version) return;
          xr3d = loaded.stage({ xr: true });
          await xr3d.attach(active);
        } catch (error) {
          no3d = true;
          endXr();
        }
      }
      if (request !== version) return;
      if (!xr3d) {
        const canvas = document.createElement('canvas');
        gl = canvas.getContext('webgl', { xrCompatible: true, alpha: true });
        if (!gl) throw new Error('WebGL unavailable');
        layer = new XRWebGLLayer(active, gl);
        active.updateRenderState({ baseLayer: layer });
      } else her.style.opacity = '0';
      const world = await active.requestReferenceSpace('local');
      xrWorldSpace = world;
      if (xr3d && makeLife) life = newLife(0.5);
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
        if (!xr3d) {
          active.requestAnimationFrame(frame);
          gl.bindFramebuffer(gl.FRAMEBUFFER, layer.framebuffer);
          gl.clearColor(0, 0, 0, 0);
          gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
        }
        const hit = xrFrame.getHitTestResults(source)[0]?.getPose(world)?.transform.position;
        surface = hit ? [hit.x, hit.y, hit.z] : null;
        view.toggleAttribute('data-surface', !!surface);
        const eye = xrFrame.getViewerPose(world)?.views[0];
        if (life) {
          if (surface && !drag && !hand) addSpot(surface);
          const held = hand || (drag?.life && drag.moved ? drag : null);
          if (held && life.position) {
            const ray = held === drag ? eye && touchRay(eye, drag.cx, drag.cy) : rayOf(xrFrame, held.input.targetRaySpace, world);
            if (ray) {
              if (!held.grabbed && held === drag) {
                held.far = Math.hypot(...[0, 1, 2].map(i => life.position[i] - ray.from[i]));
                life.grab(along(ray, held.far), now);
                held.grabbed = true;
              } else life.drag(along(ray, held.far), now);
            }
          }
          const head = eye?.transform.position;
          life.tick(now, lifeStep(now), xrWorld(head ? [head.x, head.y, head.z] : [0, 0, 0]));
          syncLife();
          seat = life.position;
        } else if (drag?.moved && transient) {
          const inputs = xrFrame.getHitTestResultsForTransientInput(transient);
          const input = inputs.find(item => !drag.input || item.inputSource === drag.input);
          if (input) {
            drag.input = input.inputSource;
            const point = input.results[0]?.getPose(world)?.transform.position;
            if (point) seat = [point.x, point.y, point.z];
          }
        }
        if (!seat || !eye) {
          her.hidden = true;
          xr3d?.update({ now, position: null });
          return;
        }
        const head = eye.transform.position;
        xr3d?.update({ now, position: seat, viewer: [head.x, head.y, head.z], ...lifeClip(now), talking: talking || hostTalking || now < mouthUntil, calm: reduced.matches, ground: life ? groundXr(seat) : null });
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
        const size = view.clientHeight * eye.projectionMatrix[5] * (xr3d ? 0.25 : 0.16) / depth;
        her.style.setProperty('--s', Math.max(24, Math.min(size, 300)) + 'px');
      };
      if (xr3d) xr3d.loop(frame);
      else active.requestAnimationFrame(frame);
    } catch (error) {
      if (request !== version) return;
      session = null;
      cancelHits();
      endXr();
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
  record.addEventListener('click', () => { if (recorder) stopRecording(); else startRecording(); });
  sizer.addEventListener('input', () => resize(Number(sizer.value)));
  try { stayStill = localStorage.getItem('capri-room-still') === 'on'; } catch (error) {}
  stillButton.setAttribute('aria-pressed', String(stayStill));
  stillButton.addEventListener('click', () => {
    stayStill = !stayStill;
    stillButton.setAttribute('aria-pressed', String(stayStill));
    try { localStorage.setItem('capri-room-still', stayStill ? 'on' : 'off'); } catch (error) {}
    if (stayStill && life?.position && (life.mode === 'walk' || life.mode === 'hop')) life.setPose(pose, performance.now());
    sayLine(stayStill ? 'Staying right here.' : 'Free to roam.');
  });
  behindButton.addEventListener('click', () => {
    hideMe = !hideMe;
    behindButton.setAttribute('aria-pressed', String(hideMe));
    if (!hideMe) sayLine('She\'s back in front of you.');
    else if (cutState === 'ready') sayLine('She\'s behind you now.');
    else if (cutState === 'failed') sayLine('Couldn\'t find you in the picture on this PC.');
    else { sayLine('Finding you in the picture...'); if (!cutLoading) loadCutout(); }
  });
  place.addEventListener('wheel', event => {
    if (!desk || !life?.position) return;
    event.preventDefault();
    resize(grow * Math.exp(-event.deltaY * 0.0015));
  }, { passive: false });
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
  const onHer = (x, y) => {
    if (her.hidden || her.style.visibility === 'hidden') return false;
    if (life?.position) {
      const box = view.getBoundingClientRect(), ax = box.left + her.offsetLeft, ay = box.top + her.offsetTop;
      const size = parseFloat(her.style.getPropertyValue('--s')) || 120;
      return Math.abs(x - ax) < size * 0.45 && y < ay + size * 0.18 && y > ay - size * 1.15;
    }
    const box = sprite.getBoundingClientRect();
    return x >= box.left && x <= box.right && y >= box.top && y <= box.bottom;
  };
  place.addEventListener('pointerdown', event => {
    if (drag || !event.isPrimary || event.button !== 0) return;
    suppressTap = false;
    if (!onHer(event.clientX, event.clientY)) return;
    const box = view.getBoundingClientRect();
    drag = { id: event.pointerId, x: event.clientX, y: event.clientY, cx: event.clientX - box.left, cy: event.clientY - box.top,
      left: her.offsetLeft, top: her.offsetTop, moved: false, life: !!life?.position };
    place.setPointerCapture(event.pointerId);
  });
  place.addEventListener('pointermove', event => {
    if (!drag || drag.id !== event.pointerId) return;
    const x = event.clientX - drag.x, y = event.clientY - drag.y;
    const box = view.getBoundingClientRect();
    drag.cx = event.clientX - box.left;
    drag.cy = event.clientY - box.top;
    if (!drag.moved && Math.hypot(x, y) <= 5) return;
    drag.moved = suppressTap = true;
    event.preventDefault();
    if (drag.life && !session && !drag.grabbed && life?.position) {
      const glass = lens();
      if (glass) {
        const reachOut = Math.hypot(...life.position), foot = desk && screenOf(glass, life.position);
        if (foot) {
          const ox = foot[0] - drag.cx, oy = foot[1] - drag.cy;
          let last = life.position.slice();
          drag.at = sight => {
            const fx = drag.cx + ox, fy = Math.min(sight.height - 2, drag.cy + oy);
            const spot = onFloor(sight, fx, Math.max(fy, sight.height / 2 + 4));
            if (spot) last = spot;
            return last.slice();
          };
        } else drag.at = sight => {
          const d = rayAt(sight, drag.cx, drag.cy), n = Math.hypot(d[0], d[1], d[2]) || 1;
          return d.map(value => value / n * reachOut);
        };
        life.grab(drag.at(glass), performance.now());
        drag.grabbed = true;
      }
    } else if (!drag.life && !session) moveCamera(drag.left + x, drag.top + y);
  });
  place.addEventListener('pointerup', event => {
    if (drag?.id !== event.pointerId) return;
    if (drag.moved) event.preventDefault();
    if (drag.grabbed) life?.release(performance.now(), undefined, desk);
    else if (drag.life && !drag.moved) {
      life?.poke(performance.now());
      suppressTap = true;
    }
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
    if (life?.position) {
      const glass = lens(), r = session ? null : glass && turnOf();
      const right = r ? [r[0], 0, r[6]] : [1, 0, 0], ahead = r ? [-r[2], 0, -r[8]] : [0, 0, -1];
      const metres = step / (desk ? 400 : 120), p = life.position;
      life.goTo([p[0] + (right[0] * direction[0] - ahead[0] * direction[1]) * metres, p[1], p[2] + (right[2] * direction[0] - ahead[2] * direction[1]) * metres], performance.now());
    } else if (session) {
      if (seat) seat = [seat[0] + direction[0] * step / 250, seat[1] - direction[1] * step / 250, seat[2]];
    } else moveCamera(her.offsetLeft + direction[0] * step, her.offsetTop + direction[1] * step);
    status.textContent = '';
  });
  place.addEventListener('click', event => {
    if (suppressTap && event.detail) { event.preventDefault(); event.stopImmediatePropagation(); return; }
    askMotion();
    if (session) {
      if (!surface) return;
      if (life) settle(surface);
      else seat = surface;
    } else if (life?.position) {
      const glass = lens(), box = view.getBoundingClientRect();
      const x = event.detail ? event.clientX - box.left : box.width * 0.5, y = event.detail ? event.clientY - box.top : box.height * 0.74;
      if (onHer(event.clientX, event.clientY) && event.detail) life.poke(performance.now());
      else {
        const spot = glass && onFloor(glass, x, y);
        if (spot) life.goTo(spot, performance.now());
      }
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
  let roomWindow = false, hosted = false;
  try {
    const asked = new URLSearchParams(location.search).get('room');
    hosted = asked === 'app';
    roomWindow = hosted || asked === 'pc';
  } catch (error) {}
  if (hosted) {
    app.dataset.host = 'app';
    try {
      const events = new EventSource(new URL('../events', location.href).href);
      events.addEventListener('talk', event => { hostTalking = event.data === '1'; });
      events.addEventListener('error', () => { hostTalking = false; });
    } catch (error) {}
    close.addEventListener('click', () => window.close());
    let fitted = false;
    const fit = () => {
      const wide = video.videoWidth, high = video.videoHeight;
      if (!wide || !high || room.hidden) return;
      const aspect = wide / high;
      view.style.removeProperty('width');
      view.style.removeProperty('height');
      view.style.removeProperty('flex');
      view.style.removeProperty('align-self');
      const roomBox = room.getBoundingClientRect(), viewBox = view.getBoundingClientRect();
      const around = roomBox.height - viewBox.height, availW = viewBox.width, availH = Math.max(120, innerHeight - around - 4);
      if (availW < 50 || roomBox.height < 50) { setTimeout(fit, 200); return; }
      if (!fitted) {
        fitted = true;
        const grow = Math.round(availW / aspect - availH);
        if (Math.abs(grow) > 8) {
          try { window.resizeBy(0, grow); } catch (error) {}
          setTimeout(fit, 250);
          return;
        }
      }
      const width = Math.min(availW, availH * aspect);
      view.style.flex = '0 0 auto';
      view.style.alignSelf = 'center';
      view.style.width = width.toFixed(0) + 'px';
      view.style.height = (width / aspect).toFixed(0) + 'px';
    };
    video.addEventListener('loadedmetadata', fit);
    video.addEventListener('resize', fit);
    addEventListener('resize', () => requestAnimationFrame(fit));
  }
  if (roomWindow) {
    app.dataset.window = 'room';
    let opened = false;
    const openHere = () => {
      if (opened || app.dataset.screen !== 'chat' || !room.hidden) return;
      opened = true;
      byId('room-open').click();
    };
    new MutationObserver(openHere).observe(app, { attributeFilter: ['data-screen'] });
    openHere();
  }
  window.CapriRoom = { close: closeRoom, startVoice: () => { if (!room.hidden) startVoice(); }, voiceStatus, talk: on => { talking = !room.hidden && !!on; }, mumble };
})();

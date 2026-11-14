(() => {
  'use strict';
  const byId = id => document.getElementById(id);
  const app = byId('app'), room = byId('room'), menu = byId('room-menu');
  const camera = byId('room-camera'), attach = byId('room-attach'), file = byId('room-attach-input');
  const close = byId('room-close'), retry = byId('room-retry'), track = byId('room-track');
  const view = byId('room-view'), video = byId('room-video'), place = byId('room-place'), status = byId('room-status');
  const her = byId('room-her'), sprite = byId('room-sprite'), heart = byId('room-heart');
  const controls = byId('room-controls'), sit = byId('room-sit'), lie = byId('room-lie'), love = byId('room-love');
  const voice = byId('room-voice'), voiceNote = byId('room-voice-note');
  let voiceOn = true, oldVoice = false;
  try { voiceOn = localStorage.getItem('capri-room-voice') !== 'off'; } catch (error) {}
  voice.setAttribute('aria-pressed', String(voiceOn));
  const voiceStatus = text => { if (!room.hidden && voiceOn) voiceNote.textContent = text; };
  const startVoice = () => {
    const r = window.CapriRemote, call = window.CapriCall;
    if (!voiceOn || !r || r.info.native || r.info.demo || r.state !== 'connected') return;
    if (!r.info.features?.includes('call')) {
      if (!oldVoice) { voiceStatus('Her voice needs a newer Capri on your PC.'); oldVoice = true; }
      return;
    }
    if (!call?.listen()) voiceStatus('Tap Voice to hear her.');
  };
  const reduced = matchMedia('(prefers-reduced-motion: reduce)');
  const config = window.CAPRI_CONFIG || {};
  const shared = config.sprites || '../assets/sprites/';
  const own = config.ownSprites || 'sprites/';
  const clips = {
    perch: [shared + 'perch.webp', 10, 6, 'perch'],
    perch_talk: [own + 'perch_talk.png', 60, 1, 'music'],
    laydown: [own + 'laydown.png', 10, 6, 'lie'],
    laydown_talk: [own + 'laydown_talk.png', 10, 6, 'lie']
  };
  const strips = new Map();
  let version = 0, stream, session, source, transient, animation, heartTimer, currentClip, currentFrame;
  let pose = 'perch', talking = false, arAvailable = false, surface, seat, drag;
  let suppressTap = false, heartState = 'new', heartWanted = false;

  const load = name => {
    if (strips.has(name)) return;
    const image = new Image();
    image.src = clips[name][0];
    const entry = { image, ready: false };
    strips.set(name, entry);
    image.decode().then(() => { entry.ready = true; }, () => { strips.delete(name); });
  };
  const draw = now => {
    animation = requestAnimationFrame(draw);
    const speech = pose + '_talk';
    const desired = talking && !reduced.matches && strips.get(speech)?.ready ? speech : pose;
    const name = strips.get(desired)?.ready ? desired : 'perch';
    if (!strips.get(name)?.ready) return;
    const [url, columns, rows, fit] = clips[name];
    const frame = reduced.matches ? 0 : Math.floor(now * 0.03) % (columns * rows);
    if (name !== currentClip) {
      currentClip = name;
      currentFrame = -1;
      sprite.dataset.fit = fit;
      sprite.style.backgroundImage = 'url("' + url + '")';
      sprite.style.backgroundSize = (columns * 100) + '% ' + (rows * 100) + '%';
    }
    if (frame === currentFrame) return;
    currentFrame = frame;
    sprite.style.backgroundPosition = (frame % columns) / (columns - 1) * 100 + '% ' +
      (rows > 1 ? Math.floor(frame / columns) / (rows - 1) * 100 : 0) + '%';
  };
  const setPose = next => {
    pose = next;
    her.dataset.pose = pose === 'perch' ? 'sit' : 'lie';
    sit.setAttribute('aria-pressed', String(pose === 'perch'));
    lie.setAttribute('aria-pressed', String(pose === 'laydown'));
  };

  const showMenu = open => {
    menu.hidden = !open;
    camera.setAttribute('aria-expanded', String(open));
    if (open) (room.hidden ? byId('room-open') : attach).focus();
  };
  const stopTracks = active => active?.getTracks().forEach(item => item.stop());
  const releaseCamera = () => {
    stopTracks(stream);
    stream = null;
    video.srcObject = null;
    video.hidden = true;
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
  const openCamera = async (lead = '', focus = true) => {
    if (room.hidden) {
      window.CapriCall?.end();
      setPose('perch');
      for (const name of Object.keys(clips)) load(name);
      loadHeart();
    }
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
    const request = ++version;
    stopDrag();
    releaseCamera();
    controls.hidden = place.hidden = her.hidden = track.hidden = true;
    stopHeart();
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
    if (room.hidden) openCamera();
    else close.focus();
  });
  attach.addEventListener('click', () => { showMenu(false); camera.focus(); file.click(); });
  close.addEventListener('click', () => closeRoom(true));
  retry.addEventListener('click', () => openCamera());
  track.addEventListener('click', () => { close.focus(); startAR(); });
  sit.addEventListener('click', () => setPose('perch'));
  lie.addEventListener('click', () => setPose('laydown'));
  const showHeart = () => {
    if (heartWanted && heartState === 'loading') return;
    stopHeart();
    heartWanted = true;
    love.setAttribute('aria-pressed', 'true');
    if (reduced.matches || heartState === 'failed') { finishHeart(); return; }
    if (heartState === 'ready') {
      heartState = 'new';
      heart.removeAttribute('src');
    }
    loadHeart();
  };
  love.addEventListener('click', showHeart);
  voice.addEventListener('click', () => {
    if (voiceOn && voiceNote.textContent === 'Tap Voice to hear her.') { startVoice(); return; }
    voiceOn = !voiceOn;
    voice.setAttribute('aria-pressed', String(voiceOn));
    try { localStorage.setItem('capri-room-voice', voiceOn ? 'on' : 'off'); } catch (error) {}
    voiceNote.textContent = '';
    if (voiceOn) startVoice();
    else { window.CapriCall?.stopListening(); talking = false; }
  });
  const moveCamera = (x, y) => {
    const box = view.getBoundingClientRect();
    her.style.left = Math.max(16, Math.min(84, x / box.width * 100)) + '%';
    her.style.top = Math.max(42, Math.min(98, y / box.height * 100)) + '%';
  };
  place.addEventListener('pointerdown', event => {
    if (drag || !event.isPrimary || event.button !== 0) return;
    suppressTap = false;
    const box = sprite.getBoundingClientRect();
    if (her.hidden || event.clientX < box.left || event.clientX > box.right ||
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
  window.CapriRoom = { close: closeRoom, voiceStatus, talk: on => { talking = !room.hidden && !!on; } };
})();

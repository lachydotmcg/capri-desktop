/* Capri Remote's screen view (PROTOCOL.md 8.1): her PC's screen, live, while this page shows it. Frames come as JPEG in
   watch.frame parts, a few a second, sealed like every other message. The phone only watches while the view is open
   and the page is in front: closing it or switching away sends watch.stop, and the PC stops by itself when the phone
   leaves. AUTO follows the monitor she's looking at; 1, 2, 3 pick one.
     CapriScreen.supported()  this session's PC offers watch
     CapriScreen.open()       the full view, or false when the PC doesn't offer it
     CapriScreen.mini(on)     the small player over the chat (a call opens it)
     CapriScreen.close()      stop watching */
(() => {
  'use strict';
  const SAVED = 'capri-remote-screen';
  const QUALITIES = [['saver', 'Saver'], ['balanced', 'Balanced'], ['sharp', 'Sharp']];
  const C = window.CapriCrypto;
  const remote = () => window.CapriRemote;
  const make = (tag, className, text) => {
    const el = document.createElement(tag);
    if (className) el.className = className;
    if (text != null) el.textContent = text;
    return el;
  };
  const icon = path => {
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('viewBox', '0 0 24 24');
    svg.setAttribute('aria-hidden', 'true');
    const line = document.createElementNS('http://www.w3.org/2000/svg', 'path');
    line.setAttribute('d', path);
    svg.appendChild(line);
    return svg;
  };
  const button = (className, label, text, path) => {
    const el = make('button', className, text);
    el.type = 'button';
    el.setAttribute('aria-label', label);
    if (path) el.prepend(icon(path));
    return el;
  };

  // The monitor, quality and mini player corner picked last time. Storage can be blocked, so it's all guarded.
  const pick = (() => {
    let saved = {};
    try { saved = JSON.parse(localStorage.getItem(SAVED)) || {}; } catch (error) {}
    return {
      monitor: typeof saved.monitor === 'string' && /^(auto|[0-9]{1,2})$/.test(saved.monitor) ? saved.monitor : 'auto',
      quality: QUALITIES.some(([name]) => name === saved.quality) ? saved.quality : 'balanced',
      corner: /^(top|bottom)-(left|right)$/.test(saved.corner) ? saved.corner : 'top-right'
    };
  })();
  const keep = () => { try { localStorage.setItem(SAVED, JSON.stringify(pick)); } catch (error) {} };

  // ---------- The view: full screen, or the mini player over the chat ----------
  const view = make('section', 'watch');
  view.hidden = true;
  view.setAttribute('aria-label', 'Your PC screen');
  const top = make('div', 'watch-top');
  const title = make('h2', 'watch-title', 'Screen');
  const toMini = button('nbtn cyan icon', 'Shrink to the mini player', null, 'M4 4h16v16H4zM12.5 12.5h6v5h-6z');
  const shut = button('nbtn icon', 'Stop watching', null, 'M5.5 5.5l13 13M18.5 5.5l-13 13');
  top.append(title, toMini, shut);
  const stage = make('div', 'watch-stage');
  const note = make('p', 'watch-note', 'Connecting to your screen');
  note.setAttribute('role', 'status');
  stage.append(note);
  const bar = make('div', 'watch-bar');
  const screens = make('div', 'chips');
  screens.setAttribute('role', 'group');
  screens.setAttribute('aria-label', 'Monitor');
  const qualities = make('div', 'chips');
  qualities.setAttribute('role', 'group');
  qualities.setAttribute('aria-label', 'Picture');
  const ask = button('nbtn cyan ask', 'Ask Capri about your screen', 'Ask her');
  bar.append(screens, qualities, ask);
  view.append(top, stage, bar);

  const mini = make('div', 'mini');
  mini.hidden = true;
  mini.setAttribute('role', 'button');
  mini.setAttribute('tabindex', '0');
  mini.setAttribute('aria-label', 'Your PC screen: tap for full screen');
  const miniShut = button('mini-x', 'Close the mini player', null, 'M6.5 6.5l11 11M17.5 6.5l-11 11');
  mini.append(miniShut);

  // One picture, moved between the two: the frame and her mouse over it.
  const pic = make('div', 'watch-pic');
  const canvas = make('canvas');
  canvas.width = 16;
  canvas.height = 9;
  const cursor = make('span', 'watch-cursor');
  cursor.hidden = true;
  pic.append(canvas, cursor);
  pic.hidden = true;
  const paint = canvas.getContext('2d', { alpha: false });

  let mode = 'off';
  let watching = false;
  let seen = { list: [], monitor: '', auto: true };
  let building = null;
  let decoding = false;
  let waiting = null;
  let asked = '';
  let resizeTimer = 0;

  const supported = () => {
    const r = remote();
    return !!(r && r.state === 'connected' && Array.isArray(r.info.features) && r.info.features.includes('watch') && C);
  };
  const say = text => { if (window.CapriApp) window.CapriApp.say(text); };
  const holder = () => (mode === 'mini' ? mini : stage);
  // The picture as big as its holder lets it be, at the frame's own shape.
  const fit = () => {
    const box = holder();
    const room = { w: box.clientWidth - (mode === 'mini' ? 4 : 0), h: box.clientHeight - (mode === 'mini' ? 4 : 0) };
    if (room.w < 8 || room.h < 8 || canvas.width < 2) return;
    const scale = Math.min(room.w / canvas.width, room.h / canvas.height);
    pic.style.width = Math.floor(canvas.width * scale) + 'px';
    pic.style.height = Math.floor(canvas.height * scale) + 'px';
  };

  // The size the picture shows at, in device pixels: the PC sends nothing bigger.
  const viewSize = () => {
    const box = holder().getBoundingClientRect();
    const scale = Math.min(3, window.devicePixelRatio || 1);
    const fit = n => Math.max(64, Math.min(8192, Math.round(n * scale)));
    return { width: fit(box.width || 320), height: fit(box.height || 180) };
  };

  const start = () => {
    if (mode === 'off' || document.hidden || !supported()) return;
    const size = viewSize();
    const wanted = [pick.monitor, pick.quality, size.width, size.height].join(' ');
    if (watching && wanted === asked) return;
    if (remote().send('watch.start', { monitor: pick.monitor, quality: pick.quality, width: size.width, height: size.height })) {
      watching = true;
      asked = wanted;
    }
  };
  const stop = () => {
    if (watching && supported()) remote().send('watch.stop', {});
    watching = false;
    asked = '';
    building = null;
  };

  const setMode = next => {
    if (next === mode) return;
    const was = mode;
    mode = next;
    view.hidden = mode !== 'full';
    mini.hidden = mode !== 'mini';
    document.getElementById('app').classList.toggle('watching', mode === 'full');
    room();
    if (mode === 'off') {
      stop();
      pic.hidden = true;
      return;
    }
    holder().prepend(pic);
    if (was === 'off') {
      pic.hidden = true;
      note.hidden = false;
      note.textContent = 'Connecting to your screen';
      place();
    }
    if (mode === 'full') shut.focus({ preventScroll: true });
    // The new holder's size goes to the PC once it has laid out.
    requestAnimationFrame(() => {
      fit();
      start();
    });
  };

  // ---------- Frames ----------
  const decode = blob => {
    if (window.createImageBitmap) return createImageBitmap(blob);
    // Older browsers: the page's policy lets images come from data: URLs.
    return new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => {
        const image = new Image();
        image.onload = () => resolve(image);
        image.onerror = reject;
        image.src = reader.result;
      };
      reader.onerror = reject;
      reader.readAsDataURL(blob);
    });
  };
  const place = () => {
    const at = pic.dataset;
    if (at.cx == null || Number(at.cx) < 0) { cursor.hidden = true; return; }
    cursor.hidden = false;
    cursor.style.left = Number(at.cx) / 100 + '%';
    cursor.style.top = Number(at.cy) / 100 + '%';
  };
  const mouse = (cx, cy) => {
    if (!Number.isInteger(cx) || !Number.isInteger(cy)) return;
    pic.dataset.cx = cx;
    pic.dataset.cy = cy;
    place();
  };
  // Each frame is acked once it's drawn, which is what lets the PC send the next: a slow phone gets fewer.
  const show = frame => {
    if (decoding) { waiting = frame; return; }
    decoding = true;
    decode(new Blob(frame.pieces, { type: 'image/jpeg' }))
      .then(image => {
        if (mode !== 'off') {
          if (canvas.width !== frame.w || canvas.height !== frame.h) {
            canvas.width = frame.w;
            canvas.height = frame.h;
            fit();
          }
          paint.drawImage(image, 0, 0, frame.w, frame.h);
          pic.hidden = false;
          note.hidden = true;
          mouse(frame.cx, frame.cy);
        }
        if (image.close) image.close();
      })
      .catch(() => {})
      .finally(() => {
        decoding = false;
        if (watching) remote().send('watch.ack', { n: frame.n });
        if (waiting) {
          const next = waiting;
          waiting = null;
          show(next);
        }
      });
  };
  const part = message => {
    const { n, part: index, parts } = message;
    if (!Number.isInteger(n) || !Number.isInteger(index) || !Number.isInteger(parts) || parts < 1 || parts > 64) return;
    if (index === 0) {
      if (!Number.isInteger(message.w) || !Number.isInteger(message.h) || message.w < 1 || message.h < 1) return;
      building = { n, parts, w: message.w, h: message.h, cx: message.cx, cy: message.cy, pieces: [] };
    }
    // A part out of turn means one went missing: wait for the next whole frame.
    if (!building || building.n !== n || building.pieces.length !== index) { building = null; return; }
    try { building.pieces.push(C.fromB64u(message.data)); } catch (error) { building = null; return; }
    if (building.pieces.length < parts) return;
    const frame = building;
    building = null;
    if (mode !== 'off') show(frame);
  };

  // ---------- Monitors and quality ----------
  const chip = (group, value, text, on, label) => {
    const el = make('button', 'chip' + (on ? ' on' : ''), text);
    el.type = 'button';
    el.dataset.value = value;
    el.setAttribute('aria-pressed', on ? 'true' : 'false');
    if (label) el.setAttribute('aria-label', label);
    group.appendChild(el);
    return el;
  };
  const drawChips = () => {
    screens.textContent = '';
    chip(screens, 'auto', 'Auto', pick.monitor === 'auto', 'Auto: follow what Capri is looking at');
    for (const monitor of seen.list) chip(screens, monitor.id, monitor.id, pick.monitor === monitor.id, monitor.name + (monitor.primary ? ', main' : ''));
    screens.hidden = seen.list.length < 2;
    qualities.textContent = '';
    for (const [name, text] of QUALITIES) chip(qualities, name, text, pick.quality === name);
    const shown = seen.list.find(monitor => monitor.id === seen.monitor);
    title.textContent = shown ? (seen.list.length > 1 ? 'Monitor ' + shown.id : 'Screen') + (seen.auto && seen.list.length > 1 ? ' \u00b7 Auto' : '') : 'Screen';
  };
  screens.addEventListener('click', event => {
    const el = event.target.closest('.chip');
    if (!el || el.dataset.value === pick.monitor) return;
    pick.monitor = el.dataset.value;
    keep();
    drawChips();
    start();
  });
  qualities.addEventListener('click', event => {
    const el = event.target.closest('.chip');
    if (!el || el.dataset.value === pick.quality) return;
    pick.quality = el.dataset.value;
    keep();
    drawChips();
    start();
  });
  const monitorsSaid = message => {
    if (!Array.isArray(message.list)) return;
    const was = seen;
    seen = {
      list: message.list.filter(m => m && typeof m.id === 'string' && /^[0-9]{1,2}$/.test(m.id)).slice(0, 9).map(m => ({ id: m.id, name: 'Monitor ' + m.id, primary: m.primary === true })),
      monitor: typeof message.monitor === 'string' ? message.monitor : '',
      auto: message.auto === true
    };
    if (message.missing === true && !was.missing && pick.monitor !== 'auto') say('Monitor ' + pick.monitor + ' is gone: showing ' + (seen.monitor || 'another'));
    seen.missing = message.missing === true;
    drawChips();
  };

  const reasons = { 'not-allowed': 'Screen view is off on the PC', 'no-screen': 'No screen to show' };
  const stateSaid = message => {
    if (message.on === true) return;
    watching = false;
    asked = '';
    pic.hidden = true;
    note.hidden = false;
    note.textContent = reasons[message.reason] || 'Screen view stopped';
  };

  // ---------- Buttons, dragging and the page going away ----------
  toMini.addEventListener('click', () => setMode('mini'));
  shut.addEventListener('click', () => setMode('off'));
  ask.addEventListener('click', () => {
    if (window.CapriApp && window.CapriApp.deliver('Look at my screen. What\'s on it?')) setMode('mini');
  });
  miniShut.addEventListener('click', event => { event.stopPropagation(); setMode('off'); });
  miniShut.addEventListener('pointerdown', event => event.stopPropagation());
  view.addEventListener('keydown', event => { if (event.key === 'Escape') setMode('mini'); });
  mini.addEventListener('keydown', event => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); setMode('full'); } });

  // The mini player goes where it's dragged, then settles into the nearest corner of the chat. A tap opens the full view.
  let drag = null;
  let corner = { right: pick.corner.endsWith('right'), bottom: pick.corner.startsWith('bottom') };
  const bounds = () => {
    const log = document.getElementById('log');
    const box = log ? log.getBoundingClientRect() : { left: 0, top: 0, right: innerWidth, bottom: innerHeight };
    return { left: box.left + 8, top: box.top + 8, right: box.right - 8, bottom: box.bottom - 8 };
  };
  const settle = () => {
    const area = bounds();
    const size = mini.getBoundingClientRect();
    const left = corner.right ? area.right - size.width : area.left;
    let top = corner.bottom ? area.bottom - size.height : area.top;
    // A bottom corner stays above her where she sits on the reply bar.
    const her = document.querySelector('.her:not(.gone)');
    const seat = her && her.getBoundingClientRect();
    if (corner.bottom && seat && seat.width && seat.left < left + size.width && seat.right > left && seat.top < top + size.height) top = Math.max(area.top, seat.top - size.height - 4);
    mini.style.left = left + 'px';
    mini.style.top = top + 'px';
    room();
  };
  // The chat keeps clear of the mini player at its end of the log, so a short chat isn't hidden under it.
  const room = () => {
    const app = document.getElementById('app');
    const on = mode === 'mini' && !mini.classList.contains('dragging');
    app.classList.toggle('mini-top', on && !corner.bottom);
    app.classList.toggle('mini-bottom', on && corner.bottom);
    if (!on) return;
    const log = document.getElementById('log').getBoundingClientRect();
    const box = mini.getBoundingClientRect();
    app.style.setProperty('--mini-room', Math.max(0, Math.round(corner.bottom ? log.bottom - box.top : box.bottom - log.top - 8)) + 'px');
  };
  mini.addEventListener('pointerdown', event => {
    if (event.button !== 0) return;
    const size = mini.getBoundingClientRect();
    drag = { id: event.pointerId, x: event.clientX, y: event.clientY, left: size.left, top: size.top, moved: false };
    try { mini.setPointerCapture(event.pointerId); } catch (error) {}
  });
  mini.addEventListener('pointermove', event => {
    if (!drag || event.pointerId !== drag.id) return;
    const dx = event.clientX - drag.x, dy = event.clientY - drag.y;
    if (!drag.moved && Math.hypot(dx, dy) < 8) return;
    drag.moved = true;
    if (!mini.classList.contains('dragging')) {
      mini.classList.add('dragging');
      room();
    }
    mini.style.left = drag.left + dx + 'px';
    mini.style.top = drag.top + dy + 'px';
  });
  const drop = event => {
    if (!drag || event.pointerId !== drag.id) return;
    const moved = drag.moved;
    drag = null;
    mini.classList.remove('dragging');
    if (!moved) { if (event.type === 'pointerup') setMode('full'); return; }
    const area = bounds();
    const size = mini.getBoundingClientRect();
    corner = { right: size.left + size.width / 2 > (area.left + area.right) / 2, bottom: size.top + size.height / 2 > (area.top + area.bottom) / 2 };
    pick.corner = (corner.bottom ? 'bottom' : 'top') + '-' + (corner.right ? 'right' : 'left');
    keep();
    settle();
  };
  mini.addEventListener('pointerup', drop);
  mini.addEventListener('pointercancel', drop);

  addEventListener('resize', () => {
    if (mode === 'mini') settle();
    fit();
    clearTimeout(resizeTimer);
    resizeTimer = setTimeout(start, 400);
  });
  // Nothing goes while the page is in the background; it picks up again when it's back.
  document.addEventListener('visibilitychange', () => {
    if (document.hidden) stop();
    else start();
  });

  const hook = () => {
    const r = remote();
    if (!r) return;
    r.onMessage(message => {
      if (message.type === 'watch.frame') part(message);
      else if (message.type === 'watch.cursor') mouse(message.cx, message.cy);
      else if (message.type === 'watch.monitors') monitorsSaid(message);
      else if (message.type === 'watch.state') stateSaid(message);
    });
    // Every new session starts with nothing watched: ask again if the view is open.
    r.onState(state => {
      watching = false;
      asked = '';
      building = null;
      if (state === 'connected') {
        if (mode !== 'off' && !supported()) setMode('off');
        else start();
      } else if (mode !== 'off') {
        pic.hidden = true;
        note.hidden = false;
        note.textContent = 'Signal lost';
      }
    });
  };

  const mount = () => {
    const app = document.getElementById('app');
    if (!app) return;
    app.append(view, mini);
    drawChips();
    hook();
    if (window.ResizeObserver) {
      const observer = new ResizeObserver(() => {
        if (mode === 'mini') settle();
        fit();
      });
      observer.observe(mini);
      observer.observe(stage);
    }
  };
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', mount, { once: true });
  else mount();

  window.CapriScreen = {
    supported,
    open: () => {
      if (!supported()) return false;
      setMode('full');
      return true;
    },
    mini: on => {
      if (!on) { if (mode === 'mini') setMode('off'); return true; }
      if (!supported()) return false;
      if (mode === 'off') setMode('mini');
      return true;
    },
    close: () => setMode('off'),
    get mode() { return mode; }
  };
})();

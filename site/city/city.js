/* Capri's billboard fly-out, the first screen of the home page. The rest of the page
   follows it: home.js still runs her desktop stage and every chapter under this screen.
   One clock runs everything: render(t) draws the
   whole first screen for any time t, so the intro can be played, skipped, frozen or slowed.

   Debug switches (query string):
     ?intro        play it even if this browser session has seen it
     ?speed=0.25   slow motion (0.05 to 4)
     ?freeze=1.2   hold the frame at 1.2 s

   She folds into ribbons on her billboard and re-forms from them on the panel: that is the
   Drive pack transformation by Qewiw (mix_qewiw1412_86144), played frame by frame, in place.
   Between the two she travels as those same ribbons, her red and her green. No picture is
   moved for that: every ribbon is drawn again on every frame.
   Everything marked FAKE below is a 2D stand-in for a Blender render. */
(() => {
  'use strict';
  const A = window.FLYOUT;
  if (!A) return;

  // Beats, in seconds.
  // strike: her ribbons leave the billboard. hit: the first of them reaches the glass, at her seat.
  // app: her billboard starts to switch back on, showing the phone demo. lit: the demo is steady and the billboard is a link.
  const T = { fold: 0.26, dark: 0.40, launch: 0.50, strike: 0.62, hit: 1.40, form: 1.80, sit: 2.42, end: 2.50, app: 2.75, lit: 3.30 };
  // Her palette, from the live site: her pink, her green, her text colour. White is the flecks in her red.
  const PINK = '253,0,79';
  const GREEN = '1,247,109';
  const TEXT = '236,232,241';
  const WHITE = '255,255,255';
  const SEEN = 'capri-flyout-seen';

  const $ = sel => document.querySelector(sel);
  const hero = $('.fo');
  const cityCanvas = $('#fo-city');
  const front = $('#fo-front');
  const ctx = cityCanvas.getContext('2d');
  const fctx = front.getContext('2d');
  const light = document.createElement('canvas');
  const lctx = light.getContext('2d');
  const her = $('#fo-her');
  const sprite = her.querySelector('.fo-sprite');
  const billboard = $('#fo-billboard');

  const clamp = (v, a, b) => Math.min(b, Math.max(a, v));
  const lerp = (a, b, k) => a + (b - a) * k;
  const smooth = k => { k = clamp(k, 0, 1); return k * k * (3 - 2 * k); };
  // Small seeded random, so a frozen frame draws the same tear and static every time.
  const seeded = seed => () => {
    seed = (seed + 0x6D2B79F5) | 0;
    let v = Math.imul(seed ^ (seed >>> 15), 1 | seed);
    v = (v + Math.imul(v ^ (v >>> 7), 61 | v)) ^ v;
    return ((v ^ (v >>> 14)) >>> 0) / 4294967296;
  };

  const query = new URLSearchParams(location.search);
  const speed = clamp(parseFloat(query.get('speed')) || 1, 0.05, 4);
  const freezeAt = query.has('freeze') ? clamp(parseFloat(query.get('freeze')) || 0, 0, 60) : null;
  const calm = matchMedia('(prefers-reduced-motion: reduce)');

  const seen = () => { try { return sessionStorage.getItem(SEEN) === '1'; } catch { return false; } };
  const markSeen = () => { try { sessionStorage.setItem(SEEN, '1'); } catch { /* private mode */ } };

  // ---------- Pictures ----------
  const load = src => new Promise((resolve, reject) => {
    const img = new Image();
    // decoded up front, so the first frame that uses a picture does not stall on it
    img.onload = () => (img.decode ? img.decode().catch(() => {}) : Promise.resolve()).then(() => resolve(img));
    img.onerror = () => reject(new Error('missing ' + src));
    img.src = src;
  });
  const stills = {};
  const loadStill = name => stills[name] || (stills[name] = (async () => {
    const spec = A.stills[name];
    const [city, off, cables, albedo] = await Promise.all([load(spec.city), load(spec.off.src), load(spec.cables.src), load(spec.albedo)]);
    return { ...spec, name, img: { city, off, cables, albedo } };
  })());
  const wantedStill = () => { const box = hero.getBoundingClientRect(); return box.width / box.height <= 1 ? 'tall' : 'wide'; };

  let still = null;
  let wispImg = null;
  let W = 0, H = 0, dpr = 1, zoom = 1, dx = 0, dy = 0;   // the still is drawn at (dx, dy), scaled by zoom
  let hit = { x: 0, y: 0, size: 0 };                      // where her ribbons meet the glass: the middle of her seat
  let cell = { x: 0, y: 0, w: 0, h: 0 };                  // her sprite cell, in hero pixels
  let panel = { left: 0, right: 0, top: 0 };              // the panel's top edge, in hero pixels
  let mode = 'wait';                                      // wait, play, end, calm (reduced motion), freeze
  let time = 0, last = 0, startedAt = 0;                  // the clock: seconds into the intro, fed by frame steps
  let lastRun = 0, frames = 0;                            // how long the last full play took and how many frames it drew, for the checks
  let drawnKey = '';
  let anims = [];

  const toHero = (x, y) => ({ x: dx + x * zoom, y: dy + y * zoom });

  function layout() {
    const box = hero.getBoundingClientRect();
    W = box.width; H = box.height;
    // at most 2x, and at most about 6 million pixels a canvas, so a 4K screen still plays smoothly
    dpr = clamp(Math.min(window.devicePixelRatio || 1, 2, Math.sqrt(6e6 / (W * H))), 0.75, 2);
    for (const canvas of [cityCanvas, front]) {
      canvas.width = Math.round(W * dpr);
      canvas.height = Math.round(H * dpr);
    }
    zoom = Math.max(W / still.w, H / still.h);              // same maths as object-fit: cover
    dx = (W - still.w * zoom) * still.pos[0];
    dy = (H - still.h * zoom) * still.pos[1];
    light.width = still.w / 2;
    light.height = still.h / 2;
    const quad = still.screen.map(p => toHero(p[0], p[1]));
    const xs = quad.map(p => p.x), ys = quad.map(p => p.y);
    billboard.style.left = Math.min(...xs) + 'px';
    billboard.style.top = Math.min(...ys) + 'px';
    billboard.style.width = Math.max(...xs) - Math.min(...xs) + 'px';
    billboard.style.height = Math.max(...ys) - Math.min(...ys) + 'px';
    onScreen = squareTo(quad);
    const rect = her.getBoundingClientRect();
    cell = { x: rect.left - box.left, y: rect.top - box.top, w: rect.width, h: rect.height };
    const plate = $('#fo-panel').getBoundingClientRect();
    panel = { left: plate.left - box.left, right: plate.right - box.left, top: plate.top - box.top };
    hit = seatPoint();                                      // her ribbons meet the glass where she will sit
    flowAt = -1;
    drawnKey = '';
  }

  // ---------- The wisp: Qewiw's frames, never moved across the screen ----------
  /* size is how wide the clip's whole 2048 px frame is on screen; (x, y) is the middle of the
     ribbon ball. The atlas holds only the box of the frame that has ink in it (crop). */
  function drawWisp(c, name, x, y, size, turn, alpha) {
    const w = A.wisp, index = w.cells[name], k = size / w.full;
    const sx = (index % w.cols) * w.cellW, sy = Math.floor(index / w.cols) * w.cellH;
    c.save();
    c.translate(x, y);
    c.rotate(turn);
    c.globalAlpha = clamp(alpha, 0, 1);
    c.drawImage(wispImg, sx + 0.5, sy + 0.5, w.cellW - 1, w.cellH - 1, (w.crop[0] - w.ball[0]) * k, (w.crop[1] - w.ball[1]) * k, w.crop[2] * k, w.crop[3] * k);
    c.restore();
  }
  // Where the ribbon ball sits when the clip is laid over her cell, so its last frame is her perch pose exactly.
  function seatPoint() {
    const w = A.wisp, k = cell.h / A.perch.refH, size = w.inCell.size * k;
    return { x: cell.x + w.inCell.x * k + size * w.ball[0] / w.full, y: cell.y + w.inCell.y * k + size * w.ball[1] / w.full, size };
  }

  // ---------- Her ribbons ----------
  /* Between the billboard and the panel she travels as what she is made of while she forms in
     Qewiw's clip: flat ribbons of her red and her green, the red flecked with white
     like her hair. No frame of the clip is moved for this. Every ribbon is drawn again on
     every frame: a smooth centre line, and a width that swells and pinches as the ribbon twists. */
  const hash = (seed, i) => {
    let v = Math.imul((seed | 0) ^ Math.imul(i | 0, 0x9E3779B1), 0x85EBCA6B);
    v ^= v >>> 15;
    v = Math.imul(v, 0xC2B2AE35);
    v ^= v >>> 13;
    return (v >>> 0) / 2147483648 - 1;                    // -1 to 1
  };
  // Smooth noise: a fixed random height at every whole number, eased from one to the next.
  const wave = (seed, x) => { const i = Math.floor(x), f = x - i; return lerp(hash(seed, i), hash(seed, i + 1), f * f * (3 - 2 * f)); };

  /* wide: how broad it is beside the others. leaves: seconds after T.strike that its head sets off.
     lasts: how long it is, as the time its tail follows its head. twists: how many times it
     turns over along its length. around: where it starts in the bundle. The rest is the loop it
     winds into at her seat: how big (coil), how many times round (turns), how flat (squash),
     how tipped (tilt), and how far off the middle (off). No two are alike, so the loops read as
     a ball of ribbon and not as rings. */
  const RIBBONS = [
    { rgb: PINK, wide: 1.00, leaves: 0.00, lasts: 0.34, twists: 2.4, around: 0.0, coil: 1.00, turns: 1.5, squash: 0.85, tilt: 0.15, off: [0, 0], flecks: 90 },
    { rgb: GREEN, wide: 0.28, leaves: 0.03, lasts: 0.30, twists: 3.1, around: 2.2, coil: 0.82, turns: 1.8, squash: 0.60, tilt: -0.5, off: [0.12, -0.08] },
    { rgb: PINK, wide: 0.78, leaves: 0.07, lasts: 0.30, twists: 1.9, around: 4.1, coil: 0.70, turns: 1.3, squash: 0.70, tilt: 0.6, off: [-0.16, 0.1], flecks: 70 },
    { rgb: GREEN, wide: 0.22, leaves: 0.10, lasts: 0.36, twists: 2.7, around: 1.1, coil: 1.14, turns: 1.6, squash: 0.90, tilt: -0.2, off: [0.05, 0.12] },
    { rgb: PINK, wide: 0.60, leaves: 0.13, lasts: 0.28, twists: 2.2, around: 3.2, coil: 0.90, turns: 1.2, squash: 0.62, tilt: -0.7, off: [0.1, 0.05], flecks: 50 },
    { rgb: GREEN, wide: 0.20, leaves: 0.16, lasts: 0.26, twists: 3.4, around: 5.4, coil: 0.58, turns: 2.0, squash: 0.75, tilt: 0.9, off: [-0.08, -0.1] }
  ];
  const COIL = 0.9;                // how much further, in path lengths, a ribbon winds on after it reaches her seat
  const BEHIND = 0.55;             // up to here it is still behind the cable bundle
  const POINTS = 56;

  /* Where every ribbon is at time t, as two layers of shapes: behind the cable bundle (drawn in
     the city), and past it (drawn above the page). A place along the way is p: 0 at the ribbon
     ball on the billboard, 1 where it reaches her seat, and on to 1 + COIL as it winds into a
     loop there. */
  let flowAt = -1, flowNow = null;
  function ribbons(t) {
    if (t === flowAt && flowNow) return flowNow;
    const flow = { behind: [], ahead: [], lead: null, alpha: 1 - smooth((t - T.form) / 0.1) };
    flowAt = t;
    flowNow = flow;
    if (t < T.strike || flow.alpha <= 0) return flow;
    const span = T.hit - T.strike;
    const fig = toHero(still.figure[0], still.figure[1]), dip = toHero(still.dip[0], still.dip[1]);
    const small = still.figure[2] * zoom * 0.82;
    const reach = hit.size / small;                       // how much bigger the ribbon ball is at her seat than on the billboard
    const turn = hit.x >= dip.x ? 1 : -1;                 // which way round they wind at the seat
    const size = p => p <= 1 ? 1 + (reach * 1.15 - 1) * Math.pow(p, 1.15) : reach * lerp(1.15, 1.3, smooth((p - 1) / COIL));
    // a point that set off at "when" has got this far: easing off the billboard, quickest into the glass, then winding down
    const along = when => {
      const k = (t - when) / span;
      return k <= 0 ? 0 : k < 1 ? Math.pow(k, 1.3) : 1 + COIL * (1 - Math.exp(-(k - 1) * 1.3 / COIL));
    };

    RIBBONS.forEach((r, i) => {
      const head = along(T.strike + r.leaves), tail = along(T.strike + r.leaves + r.lasts);
      if (head <= 0.004) return;
      const loop = hit.size * 0.14 * r.coil;              // the loop it winds into, around the middle of her seat
      const wound = e => {                                // e: 0 where it comes in over the top of its loop, 1 where it stops
        const angle = -Math.PI / 2 + turn * e * Math.PI * 2 * r.turns;
        const far = loop * (1 - 0.45 * e) * (1 + 0.2 * wave(820 + i * 13, e * 3 + t * 0.6));
        const lx = Math.cos(angle) * far, ly = Math.sin(angle) * far * r.squash, cos = Math.cos(r.tilt * turn), sin = Math.sin(r.tilt * turn);
        return { x: hit.x + r.off[0] * loop * turn + lx * cos - ly * sin, y: hit.y + r.off[1] * loop + lx * sin + ly * cos };
      };
      const top = wound(0);
      const pull = { x: 2 * dip.x - 0.5 * (fig.x + top.x), y: 2 * dip.y - 0.5 * (fig.y + top.y) };
      const place = p => {
        const s = size(p);
        if (p > 1) return { ...wound((p - 1) / COIL), s };
        // one long swoop: off the ball, down behind the cables (it passes the dip halfway), and across to her seat
        const q = 1 - p;
        const x = q * q * fig.x + 2 * q * p * pull.x + p * p * top.x, y = q * q * fig.y + 2 * q * p * pull.y + p * p * top.y;
        // the ribbons wind round one another on the way, sway apart, and gather where they leave and where they land
        const loose = smooth(p / 0.12) * (1 - smooth((p - 0.72) / 0.28));
        const wind = r.around + p * 7.5 - t * 2.2, apart = small * 0.06 * s * loose;
        return {
          x: x + (Math.cos(wind) + wave(700 + i * 11, p * 3.1 + t * 0.9) * 0.6 + wave(720 + i * 7, p * 1.4 + t * 0.5) * 0.6) * apart,
          y: y + (Math.sin(wind) * 0.7 + wave(705 + i * 11, p * 3.1 + t * 0.9) * 0.5 + wave(725 + i * 7, p * 1.4 + t * 0.5) * 0.45) * apart, s
        };
      };
      const pts = [];
      for (let k = 0; k <= POINTS; k++) {
        const u = k / POINTS, p = lerp(tail, head, u), at = place(p);
        const flat = Math.abs(Math.sin(Math.PI * (u * r.twists + r.around + t * 1.1)));       // edge-on to face-on as it turns over
        const ends = Math.pow(Math.min(1, u * 3.2), 0.8) * Math.pow(Math.min(1, (1 - u) * 6), 0.7);
        pts.push({ x: at.x, y: at.y, p, w: k === 0 || k === POINTS ? 0 : small * 0.02 * at.s * r.wide * (0.16 + 0.84 * Math.pow(flat, 0.9)) * ends });
      }
      // her hair's white flecks, riding in the red
      const flecks = [];
      for (let n = 0; n < (r.flecks || 0); n++) {
        const u = 0.08 + 0.86 * (hash(900 + i, n) * 0.5 + 0.5), k = Math.min(POINTS - 1, Math.floor(u * POINTS)), m = u * POINTS - k;
        const a = pts[k], b = pts[k + 1], w = lerp(a.w, b.w, m);
        if (w < 2) continue;
        const nx = a.y - b.y, ny = b.x - a.x, len = Math.hypot(nx, ny) || 1, across = hash(950 + i, n) * 0.72 * w;
        flecks.push({ x: lerp(a.x, b.x, m) + nx / len * across, y: lerp(a.y, b.y, m) + ny / len * across, p: lerp(a.p, b.p, m),
          r: clamp(w * 0.07 * (0.6 + Math.abs(hash(970 + i, n))), 0.5, 2.6), a: 0.5 + 0.45 * Math.abs(hash(990 + i, n)) });
      }
      // the part of the ribbon between two places on the way, with clean ends where it is cut
      const cut = (from, to) => {
        const part = [];
        for (let k = 0; k < pts.length; k++) {
          const a = pts[k], b = pts[k + 1];
          if (a.p >= from && a.p <= to) part.push(a);
          if (b) for (const edge of [from, to]) {
            if ((a.p - edge) * (b.p - edge) < 0) { const m = (edge - a.p) / (b.p - a.p); part.push({ x: lerp(a.x, b.x, m), y: lerp(a.y, b.y, m), w: lerp(a.w, b.w, m), p: edge }); }
          }
        }
        return part;
      };
      for (const [layer, from, to] of [[flow.behind, -1, BEHIND], [flow.ahead, BEHIND, 9]]) {
        const part = cut(from, to);
        if (part.length > 1) layer.push({ pts: part, rgb: r.rgb, flecks: flecks.filter(f => f.p >= from && f.p < to) });
      }
      if (!flow.lead) { const at = place(Math.min(head, 1)); flow.lead = { x: at.x, y: at.y, s: at.s, p: Math.min(head, 1) }; }
    });
    return flow;
  }

  // How much bigger the ribbon ball is at her seat than on the billboard.
  const reachAtSeat = () => hit.size / (still.figure[2] * zoom * 0.82);

  // Flat fills, like the clip: the outline of each centre line, then the flecks on top.
  function drawRibbons(c, shapes, alpha) {
    if (!shapes.length || alpha <= 0.01) return;
    c.globalCompositeOperation = 'source-over';
    for (const shape of shapes) {
      const pts = shape.pts, n = pts.length;
      c.globalAlpha = alpha;
      c.fillStyle = `rgb(${shape.rgb})`;
      c.beginPath();
      for (let side = 1; side >= -1; side -= 2) {
        for (let j = 0; j < n; j++) {
          const i = side > 0 ? j : n - 1 - j;
          const a = pts[Math.max(0, i - 1)], b = pts[Math.min(n - 1, i + 1)];
          const nx = a.y - b.y, ny = b.x - a.x, len = Math.hypot(nx, ny) || 1;
          const half = (pts[i].w > 0 ? Math.max(pts[i].w, 0.4) : 0) * side;
          const x = pts[i].x + nx / len * half, y = pts[i].y + ny / len * half;
          if (side > 0 && j === 0) c.moveTo(x, y); else c.lineTo(x, y);
        }
      }
      c.closePath();
      c.fill();
      c.fillStyle = `rgb(${WHITE})`;
      for (const fleck of shape.flecks) {
        c.globalAlpha = alpha * fleck.a;
        c.beginPath();
        c.arc(fleck.x, fleck.y, fleck.r, 0, Math.PI * 2);
        c.fill();
      }
    }
    c.globalAlpha = 1;
  }

  // ---------- FAKE: the billboard switching off ----------
  // Flickers through beat 1, cuts to dark at 0.4 s; the pink it threw fades a little slower.
  function offAlpha(t) {
    if (t >= T.dark) return [1, smooth((t - T.dark) / 0.22)];
    for (const [from, to, level] of [[0.05, 0.09, 0.85], [0.17, 0.20, 0.6], [0.27, 0.30, 0.9], [0.30, 0.36, 0.2], [0.36, 0.40, 0.55]]) {
      if (t >= from && t < to) return [level, level * 0.7];
    }
    return [0, 0];
  }
  function screenPath(c) {
    c.beginPath();
    still.screen.forEach((p, i) => { const h = toHero(p[0], p[1]); if (i) c.lineTo(h.x, h.y); else c.moveTo(h.x, h.y); });
    c.closePath();
  }
  function drawOff(screenA, spillA) {
    const [x, y, w, h] = still.off.rect;
    const X = dx + x * zoom, Y = dy + y * zoom, Wd = w * zoom, Hd = h * zoom;
    if (screenA <= 0 && spillA <= 0) return;
    if (screenA === spillA) { ctx.globalAlpha = screenA; ctx.drawImage(still.img.off, X, Y, Wd, Hd); ctx.globalAlpha = 1; return; }
    ctx.save(); screenPath(ctx); ctx.clip(); ctx.globalAlpha = screenA; ctx.drawImage(still.img.off, X, Y, Wd, Hd); ctx.restore();
    ctx.save(); screenPath(ctx); ctx.rect(X, Y, Wd, Hd); ctx.clip('evenodd'); ctx.globalAlpha = spillA; ctx.drawImage(still.img.off, X, Y, Wd, Hd); ctx.restore();
  }
  // FAKE: the cable bundle, drawn back over whatever is behind it.
  function drawCables(alpha) {
    if (alpha <= 0.01) return;
    const [x, y, w, h] = still.cables.rect;
    ctx.globalAlpha = alpha;
    ctx.drawImage(still.img.cables, dx + x * zoom, dy + y * zoom, w * zoom, h * zoom);
    ctx.globalAlpha = 1;
  }

  // ---------- Her billboard after she has left it: the phone demo, as an ad on the screen ----------
  /* The ad is a small picture of its own: a title strip, then the Capri Remote demo clip under
     it (the clip is 9:16 and the screen is narrower, so the strip takes up the difference).
     Every frame it is laid onto the screen's four corners in strips, so it keeps the screen's
     perspective. FAKE: no glass and no LED grain, and what the ad throws on the walls is the
     old pink at part strength. The one <video> is the Remote chapter's own; the first screen
     and that chapter are never on screen together. */
  const INK = '11,9,20';
  const AD = { w: 400, h: 912, top: 196 };
  const ad = document.createElement('canvas');
  ad.width = AD.w;
  ad.height = AD.h;
  const actx = ad.getContext('2d');
  const video = $('#remote-demo');
  let poster = null, onScreen = null, titled = false, inView = true, chapterInView = false;

  // The unit square onto four corners (top left, top right, bottom right, bottom left): a true perspective map.
  function squareTo([p0, p1, p2, p3]) {
    const dx1 = p1.x - p2.x, dx2 = p3.x - p2.x, sx = p0.x - p1.x + p2.x - p3.x;
    const dy1 = p1.y - p2.y, dy2 = p3.y - p2.y, sy = p0.y - p1.y + p2.y - p3.y;
    const den = dx1 * dy2 - dx2 * dy1;
    const g = (sx * dy2 - dx2 * sy) / den, h = (dx1 * sy - sx * dy1) / den;
    const a = p1.x - p0.x + g * p1.x, b = p3.x - p0.x + h * p3.x, d = p1.y - p0.y + g * p1.y, e = p3.y - p0.y + h * p3.y;
    return (u, v) => { const w = g * u + h * v + 1; return { x: (a * u + b * v + p0.x) / w, y: (d * u + e * v + p0.y) / w }; };
  }
  // One triangle of a picture drawn onto another triangle. The clip is a hair wider than the triangle, so no seams show.
  function triangle(c, img, s0, s1, s2, d0, d1, d2) {
    const den = (s1.x - s0.x) * (s2.y - s0.y) - (s2.x - s0.x) * (s1.y - s0.y);
    const m11 = ((d1.x - d0.x) * (s2.y - s0.y) - (d2.x - d0.x) * (s1.y - s0.y)) / den;
    const m12 = ((d1.y - d0.y) * (s2.y - s0.y) - (d2.y - d0.y) * (s1.y - s0.y)) / den;
    const m21 = ((d2.x - d0.x) * (s1.x - s0.x) - (d1.x - d0.x) * (s2.x - s0.x)) / den;
    const m22 = ((d2.y - d0.y) * (s1.x - s0.x) - (d1.y - d0.y) * (s2.x - s0.x)) / den;
    const cx = (d0.x + d1.x + d2.x) / 3, cy = (d0.y + d1.y + d2.y) / 3;
    c.save();
    c.beginPath();
    [d0, d1, d2].forEach((p, i) => {
      const far = Math.hypot(p.x - cx, p.y - cy) || 1, x = p.x + (p.x - cx) / far * 0.7, y = p.y + (p.y - cy) / far * 0.7;
      if (i) c.lineTo(x, y); else c.moveTo(x, y);
    });
    c.closePath();
    c.clip();
    c.transform(m11, m12, m21, m22, d0.x - m11 * s0.x - m21 * s0.y, d0.y - m12 * s0.x - m22 * s0.y);
    c.drawImage(img, 0, 0);
    c.restore();
  }

  // How far on the ad is: it stutters on like a tube, then steadies.
  function adLevel(t) {
    if (t < T.app) return 0;
    if (mode === 'calm') return 1;
    const since = t - T.app;
    for (const [from, to, level] of [[0, 0.04, 0.5], [0.04, 0.11, 0], [0.11, 0.18, 0.8], [0.18, 0.22, 0.25]]) if (since >= from && since < to) return level;
    return lerp(0.7, 1, smooth((since - 0.22) / (T.lit - T.app - 0.22)));
  }
  function paintAd() {
    if (!titled) {
      actx.fillStyle = `rgb(${INK})`;
      actx.fillRect(0, 0, AD.w, AD.h);
      actx.fillStyle = `rgb(${PINK})`;
      actx.fillRect(28, 36, 60, 8);
      actx.textBaseline = 'alphabetic';
      actx.fillStyle = `rgb(${TEXT})`;
      actx.font = '800 64px "Big Shoulders", "Arial Narrow", sans-serif';
      const wide = actx.measureText('CAPRI REMOTE').width;
      actx.font = `800 ${Math.floor(Math.min(80, 64 * (AD.w - 54) / wide))}px "Big Shoulders", "Arial Narrow", sans-serif`;
      actx.fillText('CAPRI REMOTE', 26, 118);
      actx.fillStyle = `rgb(${GREEN})`;
      actx.font = '800 31px "Big Shoulders", "Arial Narrow", sans-serif';
      if ('letterSpacing' in actx) actx.letterSpacing = '2px';
      actx.fillText('ON YOUR PHONE', 28, 166);
      if ('letterSpacing' in actx) actx.letterSpacing = '0px';
      // drawn again until her own type has loaded
      titled = !document.fonts || document.fonts.check('800 64px "Big Shoulders"');
    }
    const live = video && video.readyState >= 2 && video.videoWidth > 0;
    const frame = live ? video : poster;
    if (frame) actx.drawImage(frame, 0, AD.top, AD.w, AD.w * 16 / 9);
    // the screen's own rows, drawn into the ad so they follow its perspective
    actx.fillStyle = `rgba(${INK},0.2)`;
    for (let y = AD.top; y < AD.h; y += 6) actx.fillRect(0, y, AD.w, 2);
  }
  function drawAd(t, level) {
    if (!onScreen) return;
    paintAd();
    const across = 2, down = 10;
    const from = (u, v) => ({ x: u * AD.w, y: v * AD.h });
    ctx.save();
    ctx.globalAlpha = level;
    for (let j = 0; j < down; j++) {
      for (let i = 0; i < across; i++) {
        const u0 = i / across, u1 = (i + 1) / across, v0 = j / down, v1 = (j + 1) / down;
        triangle(ctx, ad, from(u0, v0), from(u1, v0), from(u1, v1), onScreen(u0, v0), onScreen(u1, v0), onScreen(u1, v1));
        triangle(ctx, ad, from(u0, v0), from(u1, v1), from(u0, v1), onScreen(u0, v0), onScreen(u1, v1), onScreen(u0, v1));
      }
    }
    ctx.restore();
    if (mode === 'calm') return;
    // a slow band of light down the screen
    const at = ((t * 0.09) % 1.3) - 0.15, a = onScreen(0, at), b = onScreen(1, at), c = onScreen(1, at + 0.1), d = onScreen(0, at + 0.1);
    ctx.save();
    screenPath(ctx);
    ctx.clip();
    ctx.fillStyle = `rgba(${TEXT},${0.05 * level})`;
    ctx.beginPath();
    ctx.moveTo(a.x, a.y); ctx.lineTo(b.x, b.y); ctx.lineTo(c.x, c.y); ctx.lineTo(d.x, d.y);
    ctx.closePath();
    ctx.fill();
    ctx.restore();
  }
  // The clip plays while it can be seen: on the billboard once she has landed, or in the Remote chapter.
  // The site's own Pause motion button and reduced motion hold it on its poster.
  function demo() {
    if (!video) return;
    const stopped = document.documentElement.classList.contains('still') || mode === 'calm' || mode === 'freeze';
    const want = !stopped && ((inView && clock() >= T.hit) || chapterInView);
    if (want && video.paused) video.play().catch(() => {});
    else if (!want && !video.paused) video.pause();
  }

  // Her own sounds from the app, on the beats: she leaves her billboard, her ribbons set off, she forms.
  // A browser only allows them after a click, tap or key, so the first play of a visit is silent.
  const CUES = [[T.fold, 'leave'], [T.strike, 'hop'], [T.form, 'arrive'], [T.app, 'click']];
  function cues(from, to) {
    if (window.CapriSfx) for (const [at, name] of CUES) if (from < at && to >= at) window.CapriSfx.play(name);
  }

  // ---------- FAKE: her light on the city ----------
  /* Two soft pools, her red and her green, circling the point she is at. They are multiplied
     by a grey guess at the surfaces (albedo) and added to the still. A real version adds
     Blender light-only renders here in exactly the same way. */
  function cityLight(x, y, radius, power, t) {
    if (power <= 0.004) return;
    const lw = light.width, lh = light.height;
    const px = (x - dx) / zoom / 2, py = (y - dy) / zoom / 2, r = radius / zoom / 2;
    lctx.globalCompositeOperation = 'source-over';
    lctx.globalAlpha = 1;
    lctx.fillStyle = '#000';
    lctx.fillRect(0, 0, lw, lh);
    lctx.globalCompositeOperation = 'lighter';
    const swing = t * 5.2, away = r * 0.34;
    const pool = (cx, cy, rad, rgb, a) => {
      const g = lctx.createRadialGradient(cx, cy, 0, cx, cy, rad);
      g.addColorStop(0, `rgba(${rgb},${a})`);
      g.addColorStop(0.35, `rgba(${rgb},${a * 0.55})`);
      g.addColorStop(1, `rgba(${rgb},0)`);
      lctx.fillStyle = g;
      lctx.fillRect(0, 0, lw, lh);
    };
    pool(px + Math.cos(swing) * away, py + Math.sin(swing) * away * 0.7, r, PINK, 1);
    pool(px - Math.cos(swing) * away, py - Math.sin(swing) * away * 0.7, r * 0.85, GREEN, 0.8);
    lctx.globalCompositeOperation = 'multiply';
    lctx.drawImage(still.img.albedo, 0, 0, lw, lh);
    ctx.globalCompositeOperation = 'lighter';
    for (let left = power; left > 0; left -= 1) {
      ctx.globalAlpha = Math.min(1, left);
      ctx.drawImage(light, dx, dy, still.w * zoom, still.h * zoom);
    }
    ctx.globalCompositeOperation = 'source-over';
    ctx.globalAlpha = 1;
  }
  // The air around a point catching her light.
  function halo(c, x, y, radius, strength) {
    const g = c.createRadialGradient(x, y, 0, x, y, radius);
    g.addColorStop(0, `rgba(${PINK},${0.34 * strength})`);
    g.addColorStop(0.5, `rgba(${GREEN},${0.07 * strength})`);
    g.addColorStop(1, `rgba(${PINK},0)`);
    c.globalCompositeOperation = 'lighter';
    c.fillStyle = g;
    c.fillRect(x - radius, y - radius, radius * 2, radius * 2);
    c.globalCompositeOperation = 'source-over';
  }

  // ---------- FAKE: static on the dead screen ----------
  function deadStatic(t, strength = 1) {
    const quad = still.screen.map(p => toHero(p[0], p[1]));
    const x0 = Math.min(...quad.map(p => p.x)), y0 = Math.min(...quad.map(p => p.y));
    const w = Math.max(...quad.map(p => p.x)) - x0, h = Math.max(...quad.map(p => p.y)) - y0;
    const rnd = seeded(Math.floor(t * 11) * 7919 + 13);
    ctx.save();
    screenPath(ctx);
    ctx.clip();
    const size = Math.max(1, zoom * 1.3);
    for (let i = 0; i < 80; i++) {
      const tone = rnd();
      ctx.fillStyle = `rgba(${tone < 0.08 ? PINK : tone < 0.14 ? GREEN : TEXT},${(0.03 + rnd() * 0.09) * strength})`;
      ctx.fillRect(x0 + rnd() * w, y0 + rnd() * h, size * (1 + rnd() * 6), size);
    }
    ctx.fillStyle = `rgba(${TEXT},${0.04 * strength})`;
    ctx.fillRect(x0, y0 + ((t * 0.23) % 1) * h, w, h * 0.06);
    ctx.restore();
  }

  // ---------- The tear when she hits the glass ----------
  function tearCity(t) {
    const k = (t - T.hit) / 0.36;
    if (k < 0 || k >= 1) return;
    const rnd = seeded(Math.floor((t - T.hit) / 0.045) * 104729 + 7);
    const bands = 3 + Math.floor(rnd() * 4);
    for (let i = 0; i < bands; i++) {
      const y = Math.floor(rnd() * H), h = Math.ceil((0.02 + rnd() * 0.1) * H), shift = (rnd() * 2 - 1) * W * 0.1 * (1 - k);
      ctx.drawImage(cityCanvas, 0, y * dpr, W * dpr, h * dpr, shift, y, W, h);
    }
  }

  // ---------- The city: she folds, the ribbon ball holds, the ribbons leave ----------
  function drawCity(t) {
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.globalAlpha = 1;
    ctx.globalCompositeOperation = 'source-over';
    ctx.drawImage(still.img.city, dx, dy, still.w * zoom, still.h * zoom);
    const [screenA, spillOff] = offAlpha(t);
    const lit = adLevel(t);
    // the phone demo throws some of her pink back on the walls around the screen
    const spillA = spillOff * (1 - 0.45 * lit);
    drawOff(screenA, spillA);
    if (t >= T.dark && lit < 1) deadStatic(mode === 'calm' ? 0 : t, 1 - lit);
    if (lit > 0) { drawAd(t, lit); drawCables(1); }

    const fig = toHero(still.figure[0], still.figure[1]);
    const figH = still.figure[2] * zoom, small = figH * 0.82, reachOfLight = small * 1.9 + 130 * zoom;
    const shimmer = 0.82 + 0.18 * Math.sin(t * 61) * Math.sin(t * 23);

    if (t >= T.fold && t < T.launch) {
      // She folds into ribbons, on the billboard: the clip's own frames, one after another, in place.
      const k = (t - T.fold) / (T.launch - T.fold);
      const name = t < T.dark
        ? ['e3', 'e5', 'e7', 'e9'][Math.min(3, Math.floor((t - T.fold) / (T.dark - T.fold) * 4))]
        : ['b10', 'b12', 'b14'][Math.min(2, Math.floor((t - T.dark) / (T.launch - T.dark) * 3))];
      const size = lerp(figH * 1.7, small, smooth(k)), glow = smooth(k * 1.6);
      cityLight(fig.x, fig.y, size * 1.9 + 130 * zoom, glow * 0.8 * shimmer, t);
      halo(ctx, fig.x, fig.y, size * 0.6, glow);
      drawWisp(ctx, name, fig.x, fig.y, size, 0, 1);
      drawCables(Math.max(screenA, 0.4));
    } else if (t >= T.launch && t < T.strike + 0.3) {
      // The ribbon ball holds on the dead billboard, then comes apart as its ribbons leave it.
      const going = smooth((t - T.strike) / 0.3);
      if (t < T.strike) cityLight(fig.x, fig.y, reachOfLight, 0.85 * shimmer, t);
      halo(ctx, fig.x, fig.y, small * 0.6, 1 - going);
      const name = ['b14', 'b16', 'b18', 'b16'][Math.floor((t - T.launch) * 13) % 4];
      drawWisp(ctx, name, fig.x, fig.y, small * (1 - 0.45 * going), (t - T.launch) * 1.9, 1 - going);
      if (t < T.strike) drawCables(1);
    }
    if (t >= T.strike && t < T.form + 0.1) {
      const flow = ribbons(t);
      // her light goes down the street with the first ribbon, then dies away from her seat once it is through the glass
      if (flow.lead && t < T.hit) cityLight(flow.lead.x, flow.lead.y, reachOfLight * (0.55 + 0.22 * flow.lead.s), 0.8 * shimmer, t);
      else if (t < T.hit + 0.3) cityLight(hit.x, hit.y, reachOfLight * (0.55 + 0.22 * reachAtSeat()), 0.8 * (1 - smooth((t - T.hit) / 0.3)), t);
      drawRibbons(ctx, flow.behind, flow.alpha);
      drawCables(1);
    }
    tearCity(t);
  }

  // ---------- Above the page: her ribbons on the glass, then her re-forming on the panel ----------
  const LANDING = [['b18', T.form], ['b16', 1.90], ['b14', 1.98], ['b12', 2.06], ['b10', 2.13], ['f9', 2.20], ['f7', 2.26], ['f5', 2.31], ['f3', 2.36]];
  function drawFront(t) {
    fctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    fctx.clearRect(0, 0, W, H);
    if (t >= T.hit && t < T.hit + 0.26) {
      // the tear: a few thin bars for a moment. No flash and no cracks.
      const k = (t - T.hit) / 0.26;
      const rnd = seeded(Math.floor((t - T.hit) / 0.045) * 15485863 + 3);
      const bars = 2 + Math.floor(rnd() * 3);
      for (let i = 0; i < bars; i++) {
        const tone = rnd();
        const rgb = tone < 0.45 ? PINK : tone < 0.8 ? GREEN : TEXT;
        const w = (0.12 + rnd() * 0.36) * W, h = 2 + rnd() * 7;
        fctx.fillStyle = `rgba(${rgb},${(0.35 + rnd() * 0.4) * (1 - k)})`;
        fctx.fillRect(rnd() * (W - w), rnd() * H, w, h);
      }
    }
    if (t >= T.strike && t < T.form + 0.1) {
      // past the cables her ribbons are in front of everything, and at her seat they wind into a ball
      const flow = ribbons(t);
      if (t >= T.hit) halo(fctx, hit.x, hit.y, hit.size * 0.25, 0.9 * smooth((t - T.hit) / (T.form - T.hit)) * flow.alpha);
      drawRibbons(fctx, flow.ahead, flow.alpha);
    }
    if (t >= T.form && t < T.sit) {
      // The clip again, backwards, laid over her cell: ribbon ball, then ribbons, then her.
      let name = LANDING[0][0];
      for (const [frame, from] of LANDING) if (t >= from) name = frame;
      const settle = smooth((t - T.form) / (2.13 - T.form));
      const scale = lerp(0.5, 1, settle);
      halo(fctx, hit.x, hit.y, hit.size * 0.5 * scale, 0.9 * (1 - smooth((t - 2.13) / (T.sit - 2.13))));
      drawWisp(fctx, name, hit.x, hit.y, hit.size * scale, (1 - settle) * -0.9, smooth((t - T.form) / 0.06));
    }
  }

  // ---------- The page's own parts, scrubbed by the same clock ----------
  // Each track is a list of [time, styles]; a style holds until the next time (her glitch is stepped, never eased).
  function track(el, keys) {
    const frames = keys.map(([time, style]) => ({ ...style, offset: clamp(time / T.end, 0, 1), easing: 'step-end' }));
    if (frames[frames.length - 1].offset < 1) frames.push({ ...keys[keys.length - 1][1], offset: 1 });
    const anim = el.animate(frames, { duration: T.end * 1000, fill: 'both' });
    anim.pause();
    return anim;
  }
  const gone = { visibility: 'hidden', clipPath: 'inset(0 0 100% 0)', transform: 'none' };
  const here = { visibility: 'visible', clipPath: 'none', transform: 'none' };
  const band = (top, bottom, x) => ({ visibility: 'visible', clipPath: `inset(${top}% -60px ${bottom}% -60px)`, transform: `translateX(${x}px)` });
  const glitchIn = (el, at, push = 1) => track(el, [
    [0, gone], [at, band(44, 38, -28 * push)], [at + 0.06, band(8, 66, 20 * push)], [at + 0.12, band(58, 6, -12 * push)],
    [at + 0.18, band(0, 28, 9 * push)], [at + 0.24, band(0, 0, -4 * push)], [at + 0.30, here]
  ]);
  function buildTracks() {
    anims.forEach(anim => anim.cancel());
    anims = [
      glitchIn($('.nav'), T.hit + 0.05),
      glitchIn($('#fo-ground'), T.hit + 0.02, 1.6),
      glitchIn($('#fo-cue'), T.hit + 0.18, 0.6),
      track($('#fo-wordmark'), [
        [0, { transform: 'none', textShadow: 'none' }],
        [T.hit, { transform: 'translateX(-14px) skewX(-6deg)', textShadow: '-10px 0 #fd004f, 10px 0 #01f76d' }],
        [T.hit + 0.06, { transform: 'translateX(11px)', textShadow: '8px 0 #fd004f, -8px 0 #01f76d' }],
        [T.hit + 0.12, { transform: 'translateX(-6px) skewX(3deg)', textShadow: '-5px 0 #01f76d, 5px 0 #fd004f' }],
        [T.hit + 0.20, { transform: 'translateX(3px)', textShadow: '-3px 0 #fd004f, 3px 0 #01f76d' }],
        [T.hit + 0.28, { transform: 'none', textShadow: 'none' }]
      ]),
      track(her, [[0, { opacity: 0 }], [T.sit, { opacity: 1 }]])
    ];
  }

  function perch(t) {
    const p = A.perch;
    const frame = (A.wisp.perchFrame + Math.max(0, Math.floor((t - T.sit) * p.fps))) % p.frames;
    sprite.style.backgroundPosition = `${(frame % p.cols) / (p.cols - 1) * 100}% ${Math.floor(frame / p.cols) / (p.rows - 1) * 100}%`;
  }

  function render(t) {
    // Once she is sitting only the static and her loop change, so skip frames that would look the same.
    const key = t < T.end ? '' : Math.floor(t * 11) + ':' + Math.floor(t * A.perch.fps);
    if (key && key === drawnKey) return;
    drawnKey = key;
    drawCity(t);
    drawFront(t);
    hero.classList.toggle('fo-ad', t >= T.lit);
    const ms = Math.min(t, T.end) * 1000;
    for (const anim of anims) anim.currentTime = ms;
    perch(mode === 'calm' ? T.sit : t);
  }

  // ---------- Clock, skip, replay ----------
  /* The clock adds up frame steps, each capped at 0.25 s. A tab that was hidden, or one long
     stall, makes the intro wait instead of jumping ahead, so it starts when it is first seen. */
  const clock = () => mode === 'freeze' ? freezeAt : mode === 'calm' ? T.lit : time;
  function tick() {
    const running = mode === 'play' || mode === 'end';
    if (running) requestAnimationFrame(tick);
    const now = performance.now(), before = time;
    if (running) time += Math.min((now - last) / 1000, 0.25) * speed;
    last = now;
    if (running) cues(before, time);
    if (mode === 'play') frames++;
    if (mode === 'play' && time >= T.end) { mode = 'end'; lastRun = (now - startedAt) / 1000; }
    demo();
    // once she is sitting, nothing is drawn while the first screen is scrolled away
    if (mode === 'play' || inView) render(clock());
  }
  function play() {
    markSeen();
    const running = mode === 'play' || mode === 'end';
    mode = 'play';
    frames = 0;
    time = 0;
    last = startedAt = performance.now();
    if (!running) tick();
  }
  function rest() {                       // straight to the end state, loop running
    const running = mode === 'play' || mode === 'end';
    mode = 'end';
    time = Math.max(time, T.end);
    last = performance.now();
    if (!running) tick();
  }
  /* A press that skips the intro must not also replay it when it lands on the billboard.
     "held" stays set from that press until a moment after it is let go, however long it is held. */
  let held = false, letGo = 0;
  function skip(event) {
    if (mode !== 'play') return;
    if (event && /^(pointerdown|touchstart|keydown)$/.test(event.type)) { held = true; clearTimeout(letGo); }
    rest();
  }
  const release = () => { if (held) { clearTimeout(letGo); letGo = setTimeout(() => { held = false; }, 400); } };

  addEventListener('wheel', skip, { passive: true });
  addEventListener('touchstart', skip, { passive: true });
  addEventListener('pointerdown', skip);
  addEventListener('scroll', skip, { passive: true });
  addEventListener('keydown', event => { if (!['Shift', 'Control', 'Alt', 'Meta'].includes(event.key)) skip(event); });
  for (const type of ['pointerup', 'pointercancel', 'touchend', 'touchcancel', 'keyup']) addEventListener(type, release, { passive: true });
  // Clicking her plays it again. Her billboard is a link to the Remote chapter, once the phone demo is steady on it.
  her.addEventListener('click', () => { if (mode === 'end' && !held) play(); });
  billboard.addEventListener('click', event => { if (held || clock() < T.lit) event.preventDefault(); });

  function settle() {                     // pick the state this visit should be in
    her.disabled = calm.matches;
    if (freezeAt !== null) { mode = 'freeze'; drawnKey = ''; render(freezeAt); }
    else if (calm.matches) { mode = 'calm'; drawnKey = ''; render(T.lit); }
    // a visit that opens further down the page (a link to a chapter, a reload) does not play it out of sight
    // nor does one whose pictures took so long that the page stopped waiting for them (the timer in index.html)
    else if ((query.has('intro') || !seen()) && !window.__foLate && hero.getBoundingClientRect().bottom > innerHeight * 0.5) play();
    else rest();
  }
  calm.addEventListener('change', () => { if (still) settle(); });

  let resizing = 0;
  addEventListener('resize', () => {
    cancelAnimationFrame(resizing);
    resizing = requestAnimationFrame(async () => {
      if (!still) return;
      const name = wantedStill();
      if (name !== still.name) still = await loadStill(name);
      layout();
      render(clock());
    });
  });

  (async () => {
    // her strip is whichever size the stylesheet picked for this screen
    const strip = /url\("?([^")]+)"?\)/.exec(getComputedStyle(sprite).backgroundImage);
    [still, wispImg, , poster] = await Promise.all([loadStill(wantedStill()), load(A.wisp.src), load(strip ? strip[1] : A.perch.src), load('city/remote-demo.webp').catch(() => null)]);
    layout();
    buildTracks();
    document.documentElement.classList.remove('pre');   // the tracks hold those parts back from here on
    settle();
    // The first screen stops drawing once it is scrolled away, and the help chat keeps out of her way while it is up.
    new IntersectionObserver(([entry]) => {
      inView = entry.isIntersecting;
      document.documentElement.classList.toggle('fo-top', entry.intersectionRatio > 0.4);
      if (inView) drawnKey = '';
    }, { threshold: [0, 0.4] }).observe(hero);
    if (video) new IntersectionObserver(([entry]) => { chapterInView = entry.isIntersecting; }, { threshold: 0.2 }).observe(video);
    window.__flyout = { T, get mode() { return mode; }, get t() { return clock(); }, get lastRun() { return lastRun; }, get frames() { return frames; }, get ad() { return adLevel(clock()); }, get inView() { return inView; }, video, skip, play };
  })().catch(error => {
    // Without the pictures the plain page still stands: the still city, the headline, and the whole site under it.
    document.documentElement.classList.remove('pre');
    document.documentElement.classList.add('fo-plain');
    console.error(error);
  });
})();

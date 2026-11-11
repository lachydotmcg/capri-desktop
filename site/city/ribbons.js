/* How she gets from one place to another on the page: the same trip as in the app (app/Ribbons.cs).
   She comes apart into the ribbons she forms from: her hair's red with flecks of white in it, and
   thin lines of her green. They wind into a ball where she was and stream to where she's going.
   Where she lands she forms from the transformation animation itself, the drawings by Qewiw
   (mix_qewiw1412_86144), played as drawn, back from the ball of ribbon to her. The ribbons on the
   way are drawn by this code, afresh on every frame: no picture is slid across the screen.

   city/city.js sends her between the city's edge and her desktop with it as the page scrolls, and
   home.js uses it for her teleports on her desktop.

     CapriRibbons.trip({ from, to, size, toSize, picture, mirrored, onShown, onDone })

   from, to     functions giving a point on the screen now (they are asked on every frame, so a trip
                keeps up with a page that is scrolling): her middle where she leaves, and the middle
                of the ball she forms from where she lands
   size, toSize her box where she leaves and where she lands, in pixels (her perch cell's height)
   picture      her picture as she leaves, or null: { image, sx, sy, sw, sh, width, height, at }
   mirrored     she faces right where she lands (the animation is drawn on her facing left)
   onShown      her own picture should take over under the last drawings
   onDone       the last ribbon is in

   Returns false, and does nothing, when motion is paused or reduced or the drawings haven't loaded. */
(() => {
  'use strict';
  const PINK = '253,0,79', GREEN = '1,247,109';
  /* wide: how broad it is beside the others. leaves: how long after the first one it sets off, in
     travel times. lasts: how long it is, as the time its tail follows its head. twists: how many
     times it turns over along its length. around: where it starts in the bundle. The rest is the
     loop it winds into where she was: how big, how many times round, how flat, how tipped, and
     how far off the middle. No two are alike, so the loops read as a ball of ribbon. */
  const STRANDS = [
    [true, 1.00, 0.00, 0.52, 2.4, 0.0, 1.00, 1.5, 0.85, 0.15, 0, 0, 90],
    [false, 0.28, 0.04, 0.46, 3.1, 2.2, 0.82, 1.8, 0.60, -0.5, 0.12, -0.08, 0],
    [true, 0.78, 0.09, 0.46, 1.9, 4.1, 0.70, 1.3, 0.70, 0.6, -0.16, 0.1, 70],
    [false, 0.22, 0.13, 0.55, 2.7, 1.1, 1.14, 1.6, 0.90, -0.2, 0.05, 0.12, 0],
    [true, 0.60, 0.17, 0.43, 2.2, 3.2, 0.90, 1.2, 0.62, -0.7, 0.1, 0.05, 50],
    [false, 0.20, 0.21, 0.40, 3.4, 5.4, 0.58, 2.0, 0.75, 0.9, -0.08, -0.1, 0]
  ].map(([hair, wide, leaves, lasts, twists, around, coil, turns, squash, tilt, offX, offY, flecks]) => ({ hair, wide, leaves, lasts, twists, around, coil, turns, squash, tilt, offX, offY, flecks }));
  const GATHER = 0.24;             // her picture goes and her ribbons wind into a ball
  /* The drawings she forms from, each held for a twenty-fourth of a second. The first five are the
     ball of ribbon, back to front. The last four are its ribbons alone as they leave her, and by
     then her own picture is showing under them. */
  const DRAWINGS = ['b18', 'b16', 'b14', 'b12', 'b10', 'e9', 'e7', 'e5', 'e3'];
  const DRAWN = 1 / 24, BALL = 5, HANDOFF = BALL * DRAWN;
  const FRAME_IN_CELL = 2048 * 0.335;   // how wide the animation's whole frame is, in her perch cell's pixels (384 tall)
  const POINTS = 48;

  const clamp = x => Math.max(0, Math.min(1, x));
  const smooth = x => { x = clamp(x); return x * x * (3 - 2 * x); };
  // A fixed random number from -1 to 1 for a seed and a whole number, and smooth noise made from them.
  const hash = (seed, i) => {
    let v = Math.imul((seed | 0) ^ Math.imul(i | 0, 0x9E3779B1), 0x85EBCA6B);
    v ^= v >>> 15;
    v = Math.imul(v, 0xC2B2AE35);
    v ^= v >>> 13;
    return (v >>> 0) / 2147483648 - 1;
  };
  const wave = (seed, x) => { const whole = Math.floor(x), part = x - whole, a = hash(seed, whole), b = hash(seed, whole + 1); return a + (b - a) * part * part * (3 - 2 * part); };
  const seeded = seed => () => {
    seed = (seed + 0x6D2B79F5) | 0;
    let v = Math.imul(seed ^ (seed >>> 15), 1 | seed);
    v = (v + Math.imul(v ^ (v >>> 7), 61 | v)) ^ v;
    return ((v ^ (v >>> 14)) >>> 0) / 4294967296;
  };

  // Fairly quick, and a little longer the further she goes, so a hop along a window doesn't snap and a trip down the page doesn't drag.
  const travelTime = distance => Math.max(0.22, Math.min(0.42, 0.2 + distance / 5200));
  const landing = trip => GATHER + trip.travel;
  const length = trip => landing(trip) + Math.max(DRAWINGS.length * DRAWN, trip.travel * 0.7);
  // How much of her picture is still there where she left.
  const leaving = t => 1 - smooth((t - 0.02) / 0.16);
  /* How far along its way a point of a ribbon is, for how long ago it set off in travel times.
     -1 to 0 is the loop where she was, from its inner end to where it comes off over the top. 0 to 1
     is the way across: easing off, quickest into where she lands, and there it stops, so a ribbon
     runs into the ball she forms from until its tail is in. */
  const along = (since, lasts) => since <= 0 ? Math.max(-1, since / lasts) : since < 1 ? Math.pow(since, 1.3) : 1;
  // A place on a ribbon's loop: wound 0 is where it goes over the top, 1 is its inner end.
  const wound = (middle, turn, index, at, t, loop) => {
    const strand = STRANDS[index], size = loop * strand.coil;
    const angle = -Math.PI / 2 + turn * at * Math.PI * 2 * strand.turns;
    const far = size * (1 - 0.45 * at) * (1 + 0.2 * wave(820 + index * 13, at * 3 + t * 0.6));
    const x = Math.cos(angle) * far, y = Math.sin(angle) * far * strand.squash, cos = Math.cos(strand.tilt * turn), sin = Math.sin(strand.tilt * turn);
    return { x: middle.x + strand.offX * size * turn + x * cos - y * sin, y: middle.y + strand.offY * size + x * sin + y * cos };
  };

  // Every ribbon at one moment, as flat shapes: a smooth centre line, and a width that swells and pinches as it turns over.
  function shapes(trip, t, from, to) {
    const out = [];
    const loop = trip.body * 0.3, half = loop * 0.164;
    const dx = to.x - from.x, dy = to.y - from.y, distance = Math.hypot(dx, dy);
    // They come off over the ball's top heading one way, and land heading the way she went.
    const turn = (trip.seed & 1) === 0 ? 1 : -1, exit = -turn, side = Math.abs(dx) >= 1 ? Math.sign(dx) : exit;
    // A ribbon that comes off heading away from where she's going turns back in a tight hook, not a wide swing.
    const pull = Math.max(loop * 0.9, distance * 0.33), hook = exit === side ? pull : loop * 0.9, rise = Math.min(distance * 0.22, trip.body * 1.1);
    STRANDS.forEach((strand, i) => {
      const grown = 1 - Math.pow(1 - clamp((t - strand.leaves * GATHER) / (GATHER * (1 - strand.leaves))), 3);
      const since = (t - GATHER) / trip.travel - strand.leaves;
      const head = since > 0 ? since : -strand.lasts * (1 - grown), tail = Math.max(-strand.lasts, head - strand.lasts);
      const start = along(tail, strand.lasts), end = along(head, strand.lasts);
      if (end - start < 0.004) return;
      // They run into the middle of the ball she forms from, each from a little off it and from its own side.
      const off = wound(from, turn, i, 0, t, loop), on = { x: to.x + strand.offX * loop, y: to.y + strand.offY * loop };
      const leaveTilt = strand.tilt * turn, landTilt = strand.tilt * side;
      const pullX1 = off.x + exit * Math.cos(leaveTilt) * hook, pullY1 = off.y + exit * Math.sin(leaveTilt) * hook;
      const pullX2 = on.x - side * Math.cos(landTilt) * pull, pullY2 = on.y - side * Math.sin(landTilt) * pull;
      const line = [], wide = [];
      for (let n = 0; n <= POINTS; n++) {
        const u = n / POINTS, p = start + (end - start) * u;
        if (p <= 0) line.push(wound(from, turn, i, Math.min(1, -p), t, loop));
        else {
          // one sweep across, rising in the middle, and on the way they wind round one another, sway apart, and gather at each end
          const q = 1 - p;
          let x = q * q * q * off.x + 3 * q * q * p * pullX1 + 3 * q * p * p * pullX2 + p * p * p * on.x;
          let y = q * q * q * off.y + 3 * q * q * p * pullY1 + 3 * q * p * p * pullY2 + p * p * p * on.y - rise * 16 * p * p * q * q;
          const loose = smooth(p / 0.12) * (1 - smooth((p - 0.72) / 0.28)), wind = strand.around + p * 7.5 - t * 2.2, apart = loop * 0.5 * loose;
          x += (Math.cos(wind) + wave(700 + i * 11, p * 3.1 + t * 0.9) * 0.6 + wave(720 + i * 7, p * 1.4 + t * 0.5) * 0.6) * apart;
          y += (Math.sin(wind) * 0.7 + wave(705 + i * 11, p * 3.1 + t * 0.9) * 0.5 + wave(725 + i * 7, p * 1.4 + t * 0.5) * 0.45) * apart;
          line.push({ x, y });
        }
        const flat = Math.abs(Math.sin(Math.PI * (u * strand.twists + strand.around + t * 1.1)));   // edge on to face on as it turns over
        const ends = Math.pow(Math.min(1, u * 3.2), 0.8) * Math.pow(Math.min(1, (1 - u) * 6), 0.7);
        wide.push(n === 0 || n === POINTS ? 0 : half * strand.wide * (0.16 + 0.84 * Math.pow(flat, 0.9)) * ends);
      }
      const shape = { hair: strand.hair, line, wide, flecks: [] };
      // her hair's white flecks, riding in the red
      for (let f = 0; f < strand.flecks; f++) {
        const u = 0.08 + 0.86 * (hash(900 + i, f) * 0.5 + 0.5);
        const n = Math.min(POINTS - 1, Math.floor(u * POINTS)), part = u * POINTS - n, girth = wide[n] + (wide[n + 1] - wide[n]) * part;
        if (girth < 2) continue;
        const nx = line[n].y - line[n + 1].y, ny = line[n + 1].x - line[n].x, size = Math.hypot(nx, ny);
        if (size < 0.001) continue;
        const across = hash(950 + i, f) * 0.72 * girth;
        shape.flecks.push([line[n].x + (line[n + 1].x - line[n].x) * part + nx / size * across, line[n].y + (line[n + 1].y - line[n].y) * part + ny / size * across,
          Math.max(0.8, Math.min(3, girth * 0.14 * (0.6 + Math.abs(hash(970 + i, f))))), 0.5 + 0.45 * Math.abs(hash(990 + i, f))]);
      }
      out.push(shape);
    });
    return out;
  }

  function fillRibbon(ctx, shape) {
    const { line, wide } = shape, count = line.length, left = [], right = [];
    for (let i = 0; i < count; i++) {
      const a = line[Math.max(0, i - 1)], b = line[Math.min(count - 1, i + 1)];
      let nx = a.y - b.y, ny = b.x - a.x, size = Math.hypot(nx, ny);
      if (size < 0.0001) { nx = ny = 0; size = 1; }
      const half = wide[i] > 0 ? Math.max(wide[i], 0.4) : 0;
      left.push([line[i].x + nx / size * half, line[i].y + ny / size * half]);
      right.push([line[i].x - nx / size * half, line[i].y - ny / size * half]);
    }
    ctx.beginPath();
    ctx.moveTo(left[0][0], left[0][1]);
    for (let i = 1; i < count; i++) ctx.lineTo(left[i][0], left[i][1]);
    for (let i = count - 1; i >= 0; i--) ctx.lineTo(right[i][0], right[i][1]);
    ctx.closePath();
    ctx.fillStyle = `rgb(${shape.hair ? PINK : GREEN})`;
    ctx.fill();
    for (const [x, y, radius, bright] of shape.flecks) {
      ctx.fillStyle = `rgba(255,255,255,${bright > 0.72 ? 0.9 : 0.6})`;
      ctx.beginPath();
      ctx.arc(x, y, radius, 0, Math.PI * 2);
      ctx.fill();
    }
  }

  // Her picture as she left, and the same picture as a flat shape of her green and of her red, for the split either side of it.
  function copies(picture, dpr) {
    const width = Math.max(1, Math.round(picture.width * dpr)), height = Math.max(1, Math.round(picture.height * dpr));
    const make = tint => {
      const canvas = document.createElement('canvas');
      canvas.width = width;
      canvas.height = height;
      const c = canvas.getContext('2d');
      c.drawImage(picture.image, picture.sx, picture.sy, picture.sw, picture.sh, 0, 0, width, height);
      if (tint) {
        c.globalCompositeOperation = 'source-in';
        c.fillStyle = `rgb(${tint})`;
        c.fillRect(0, 0, width, height);
      }
      return canvas;
    };
    return { plain: make(null), green: make(GREEN), red: make(PINK), width, height };
  }
  // Her picture in slices that slide apart, with a split of her own colours either side, and a few thin bars.
  function sliced(ctx, trip, t, dpr) {
    const shown = leaving(t);
    if (!trip.copies || shown <= 0.01) return;
    const glitch = Math.min(1, 0.35 + t / 0.1), scale = trip.size / 240, at = trip.picture.at(), { width, height } = trip.copies;
    const w = trip.picture.width, h = trip.picture.height, shift = 4 * scale * glitch;
    const pick = seeded(trip.seed + Math.floor(t * 30) * 7919);
    for (let top = 0; top < h;) {
      const tall = Math.min(h - top, Math.max(3, Math.floor(h * (0.03 + pick() * 0.13))));
      const off = pick() < 1 / 3 ? (pick() * 2 - 1) * 18 * scale * glitch : 0;
      for (const [copy, x, alpha] of [[trip.copies.green, off - shift, shown * 0.6 * glitch], [trip.copies.red, off + shift, shown * 0.6 * glitch], [trip.copies.plain, off, shown]]) {
        ctx.globalAlpha = clamp(alpha);
        ctx.drawImage(copy, 0, top / h * height, width, tall / h * height, at.x + Math.round(x), at.y + top, w, tall);
      }
      top += tall;
    }
    const bars = ['255,255,255', GREEN, PINK, PINK];
    for (let i = 0, count = Math.round(5 * glitch); i < count; i++) {
      const y = at.y + h * (0.1 + pick() * 0.8), wide = w * (0.25 + pick() * 0.5), x = at.x + w * (0.5 + (pick() - 0.5) * 0.5) - wide / 2;
      ctx.globalAlpha = 210 / 255 * shown;
      ctx.fillStyle = `rgb(${bars[Math.floor(pick() * bars.length)]})`;
      ctx.fillRect(x, y, wide, Math.max(2, (1 + Math.floor(pick() * 3)) * scale));
    }
    ctx.globalAlpha = 1;
  }

  const calm = matchMedia('(prefers-reduced-motion: reduce)');
  // ?speed=0.25 slows every trip down, as it does her fly-out (city/city.js)
  const speed = Math.max(0.05, Math.min(4, parseFloat(new URLSearchParams(location.search).get('speed')) || 1));
  let canvas = null, ctx = null, trip = null, frame = 0, count = 0;

  function open() {
    if (!canvas) {
      canvas = document.createElement('canvas');
      canvas.id = 'capri-ribbons';
      canvas.setAttribute('aria-hidden', 'true');
      document.body.append(canvas);
      ctx = canvas.getContext('2d');
    }
    // at most 2x, and at most about 6 million pixels, so a 4K screen still keeps up
    const dpr = Math.max(0.75, Math.min(window.devicePixelRatio || 1, 2, Math.sqrt(6e6 / (innerWidth * innerHeight))));
    canvas.width = Math.round(innerWidth * dpr);
    canvas.height = Math.round(innerHeight * dpr);
    canvas.hidden = false;
    return dpr;
  }

  function end(finished) {
    cancelAnimationFrame(frame);
    const done = trip;
    trip = null;
    if (canvas) { ctx.setTransform(1, 0, 0, 1, 0, 0); ctx.clearRect(0, 0, canvas.width, canvas.height); canvas.hidden = true; }
    if (!done) return;
    // a trip that is cut short still lands her, so she is never left nowhere
    if (!done.shown) { done.shown = true; if (done.onShown) done.onShown(); }
    if (done.onDone) done.onDone(finished);
  }

  function step() {
    if (!trip) return;
    const t = (performance.now() - trip.started) / 1000 * speed;
    if (t >= length(trip) || api.paused) { end(true); return; }
    frame = requestAnimationFrame(step);
    const from = trip.from(), to = trip.to();
    ctx.setTransform(trip.dpr, 0, 0, trip.dpr, 0, 0);
    ctx.clearRect(0, 0, innerWidth, innerHeight);
    sliced(ctx, trip, t, trip.dpr);
    for (const shape of shapes(trip, t, from, to)) fillRibbon(ctx, shape);
    // over them the drawing she's forming from, so her ribbons run into it
    const since = t - landing(trip), drawing = since < 0 ? -1 : Math.floor(since / DRAWN);
    if (drawing >= 0 && drawing < DRAWINGS.length) api.painter(ctx, DRAWINGS[drawing], to.x, to.y, FRAME_IN_CELL * trip.toSize / 384, trip.mirrored);
    if (!trip.shown && since >= HANDOFF) { trip.shown = true; if (trip.onShown) trip.onShown(); }
  }

  const api = window.CapriRibbons = {
    painter: null,                 // city/city.js gives this: it draws one of Qewiw's drawings (ctx, name, x, y, size, mirrored)
    get paused() { return calm.matches || document.documentElement.classList.contains('still'); },
    get ready() { return !!api.painter && !api.paused; },
    get going() { return !!trip; },
    get count() { return count; },
    trip(options) {
      if (!api.ready) return false;
      if (trip) end(false);
      const dpr = open(), from = options.from(), to = options.to();
      trip = { ...options, toSize: options.toSize || options.size, body: (options.size + (options.toSize || options.size)) / 2 * 0.85, seed: Math.floor(Math.random() * 0x7fffffff),
        travel: travelTime(Math.hypot(to.x - from.x, to.y - from.y)), started: performance.now(), dpr, shown: false, copies: null };
      try { if (options.picture) trip.copies = copies(options.picture, dpr); } catch { trip.copies = null; }
      count++;
      step();
      return true;
    },
    stop() { end(false); }
  };
})();

const G = 9.8;
const LINES = {
  grab: ['Hey. Hands.', 'Up we go, I guess.', 'Careful with the merchandise.', 'Where are we going, choom?'],
  drop: ['Stuck the landing.', 'Good spot.', 'Smooth.'],
  throw: ['Wheee.', 'Catch me, choom.', 'Not the face.'],
  land: ['Still in one piece.', 'Ten out of ten landing.', 'Ow. Rude.'],
  dizzy: ['Room is spinning.', 'Give me a sec. Two of you now.', 'My pixels are scrambled.'],
  sulk: ['Okay, enough throwing.', 'I am not a ball, choom.', 'Throw me again and I glitch out of here.'],
  poke: ['Hey. Felt that.', 'You rang, choom?', 'Poke received.', 'Something on my face?'],
  annoyed: ['Okay choom. Hands to yourself.', 'Personal space. Look it up.'],
  wander: ['Checking out your place.', 'Nice room.', 'What is over here?'],
  perch: ['Good seat.', 'View is better up here.'],
  nap: ['Wake me if anything happens.'],
  wake: ['I was not asleep.']
};
const pick = list => list[Math.floor(Math.random() * list.length)];
const flat = (a, b) => Math.hypot(a[0] - b[0], a[2] - b[2]);

export const createLife = ({ tall = 0.5, say = () => {}, calm = () => false, random = Math.random } = {}) => {
  const life = {
    mode: 'sit', position: null, velocity: [0, 0, 0], target: null, home: null, act: null, face: 0,
    pose: 'perch', since: 0, quiet: 0, held: null, throws: [], pokes: [], sulkUntil: 0, nextIdea: 0, lastLine: 0
  };
  let scale, gravity;
  const size = next => {
    tall = next;
    scale = Math.max(0.3, tall / 0.5);
    gravity = G * Math.min(1, Math.max(0.45, scale));
  };
  size(tall);
  const speed = () => Math.max(0.12, tall * 0.7);
  const line = (kind, now, force = false) => {
    if (!force && now - life.lastLine < 2500) return;
    life.lastLine = now;
    say(pick(LINES[kind]), kind);
  };
  const play = (name, now) => { life.act = { name, from: now }; };
  const setMode = (mode, now) => {
    if (mode !== 'lie') life.napping = false;
    if (mode === 'walk' || mode === 'held' || mode === 'fly' || mode === 'hop') life.act = null;
    life.mode = mode;
    life.since = now;
    if (mode === 'sit' || mode === 'lie') life.pose = mode === 'lie' ? 'laydown' : 'perch';
  };
  const plan = now => { life.nextIdea = now + 5000 + random() * 9000; };

  life.place = (point, now, raised = false) => {
    life.position = point.slice();
    life.home = point.slice();
    life.velocity = [0, 0, 0];
    life.target = null;
    life.held = null;
    setMode(raised ? 'sit' : 'stand', now);
    if (!raised) play('arrive', now);
    life.quiet = now;
    plan(now);
  };
  life.clear = () => {
    life.position = life.target = life.held = life.act = null;
    life.mode = 'sit';
  };
  life.setPose = (pose, now) => {
    if (!life.position || life.mode === 'held' || life.mode === 'fly') return;
    life.target = null;
    setMode(pose === 'laydown' ? 'lie' : 'sit', now);
    life.quiet = now;
    plan(now);
  };
  life.goTo = (point, now, hop = false, mine = true) => {
    if (!life.position || life.mode === 'held' || life.mode === 'fly') return false;
    if (mine) life.quiet = now;
    if (hop || Math.abs(point[1] - life.position[1]) > 0.12) {
      life.target = { point: point.slice(), hop: true, at: now };
      setMode('hop', now);
      play('leave', now);
      return true;
    }
    if (flat(point, life.position) < 0.03) return false;
    life.target = { point: point.slice() };
    setMode('walk', now);
    return true;
  };
  life.grab = (point, now) => {
    if (!life.position) return;
    life.held = { trail: [[now, point.slice()]], offset: [life.position[0] - point[0], life.position[1] - point[1], life.position[2] - point[2]] };
    life.target = null;
    setMode('held', now);
    life.quiet = now;
    if (now < life.sulkUntil) line('sulk', now, true);
    else line('grab', now);
  };
  life.drag = (point, now) => {
    if (life.mode !== 'held' || !life.held) return;
    const o = life.held.offset;
    life.position = [point[0] + o[0], point[1] + o[1], point[2] + o[2]];
    life.held.trail.push([now, life.position.slice()]);
    while (life.held.trail.length > 2 && now - life.held.trail[0][0] > 120) life.held.trail.shift();
  };
  life.release = (now, settle) => {
    if (life.mode !== 'held') return;
    const trail = life.held?.trail || [];
    const first = trail[0], last = trail[trail.length - 1];
    let v = [0, 0, 0];
    if (first && last && last[0] - first[0] > 10) {
      const dt = (last[0] - first[0]) / 1000;
      v = [0, 1, 2].map(i => (last[1][i] - first[1][i]) / dt);
    }
    const fast = Math.hypot(v[0], v[1], v[2]);
    const cap = 7 * scale;
    if (fast > cap) v = v.map(value => value / fast * cap);
    life.held = null;
    const thrown = fast > 0.9 * scale;
    life.velocity = thrown ? v : [0, 0, 0];
    setMode('fly', now);
    if (thrown) {
      life.throws = life.throws.filter(at => now - at < 120000);
      life.throws.push(now);
      if (life.throws.length >= 3) {
        life.sulkUntil = now + 45000;
        line('sulk', now, true);
      } else line('throw', now, true);
    }
    settle?.();
  };
  life.poke = now => {
    life.quiet = now;
    life.pokes = life.pokes.filter(at => now - at < 3500);
    life.pokes.push(now);
    if (life.mode === 'lie' && life.napping) {
      life.napping = false;
      setMode('sit', now);
      line('wake', now, true);
      return;
    }
    if (life.mode === 'stand' || life.mode === 'walk') play(random() < 0.5 ? 'grin' : 'glitch', now);
    line(life.pokes.length >= 5 ? 'annoyed' : 'poke', now, true);
  };
  life.touch = now => { life.quiet = now; };
  life.setTall = next => size(next);

  life.tick = (now, dt, world) => {
    if (!life.position) return;
    dt = Math.min(0.05, Math.max(0, dt));
    if (life.act && now - life.act.from > (life.act.name === 'arrive' ? 940 : life.act.name === 'leave' ? 800 : life.act.name === 'glitch' ? 670 : 1500)) life.act = null;
    const ground = world.groundAt(life.position);
    if (life.mode === 'fly') {
      const v = life.velocity;
      v[1] -= gravity * dt;
      const drag = Math.pow(0.985, dt * 60);
      v[0] *= drag;
      v[2] *= drag;
      for (let i = 0; i < 3; i++) life.position[i] += v[i] * dt;
      world.contain?.(life.position, v);
      const viewer = world.viewer || [0, 0, 0];
      if (flat(life.position, viewer) > 7 || life.position[1] < ground - 1.5) {
        const back = world.nearby?.(viewer) || life.home;
        life.position = back.slice();
        life.velocity = [0, 0, 0];
        setMode('stand', now);
        play('arrive', now);
        return;
      }
      if (life.position[1] <= ground) {
        life.position[1] = ground;
        const impact = -v[1];
        if (impact > 1.6 * scale) {
          v[1] = impact * 0.32;
          v[0] *= 0.6;
          v[2] *= 0.6;
          return;
        }
        life.velocity = [0, 0, 0];
        const thrown = life.throws.length && now - life.throws[life.throws.length - 1] < 4000;
        if (thrown && life.throws.length >= 2) {
          setMode('dizzy', now);
          play('glitch', now);
          line('dizzy', now, true);
        } else {
          const raised = world.raised?.(life.position);
          setMode(raised ? 'sit' : 'stand', now);
          if (!raised) play('grin', now);
          line(thrown ? 'land' : raised ? 'perch' : 'drop', now, thrown);
        }
        life.home = life.position.slice();
        plan(now);
      }
      return;
    }
    if (life.mode === 'held') return;
    if (life.mode === 'hop') {
      const t = now - life.since;
      if (t > 800 && life.target) {
        life.position = life.target.point.slice();
        life.home = life.position.slice();
        life.target = null;
        const raised = world.raised?.(life.position);
        setMode(raised ? 'sit' : 'stand', now);
        play(raised ? 'perch_arrive' : 'arrive', now);
        if (raised) line('perch', now);
        plan(now);
      }
      return;
    }
    if (Math.abs(life.position[1] - ground) > 0.02 && life.mode !== 'held') {
      if (life.position[1] > ground + 0.02) {
        life.velocity = [0, 0, 0];
        setMode('fly', now);
        return;
      }
      life.position[1] = ground;
    }
    if (life.mode === 'walk' && life.target) {
      const to = life.target.point, gap = flat(to, life.position);
      const step = speed() * dt;
      if (gap <= step) {
        life.position[0] = to[0];
        life.position[2] = to[2];
        life.target = null;
        setMode(world.raised?.(life.position) ? 'sit' : 'stand', now);
        plan(now);
      } else {
        const dx = (to[0] - life.position[0]) / gap, dz = (to[2] - life.position[2]) / gap;
        life.position[0] += dx * step;
        life.position[2] += dz * step;
        life.face = Math.atan2(dx, dz);
      }
      return;
    }
    if (life.mode === 'dizzy' && now - life.since > 2600) {
      setMode('stand', now);
      play('grin', now);
      plan(now);
      return;
    }
    if (calm() || world.busy || now < life.nextIdea || life.mode === 'dizzy') return;
    plan(now);
    const idle = now - life.quiet;
    if (now < life.sulkUntil) {
      if (life.mode !== 'lie') setMode('lie', now);
      return;
    }
    if (idle > 90000 && life.mode !== 'lie') {
      setMode('lie', now);
      life.napping = true;
      line('nap', now);
      return;
    }
    if (life.mode === 'lie' && idle > 90000) return;
    const roll = random();
    if (roll < 0.45) {
      const spot = world.wander?.(life.position, life.home);
      if (spot && life.goTo(spot, now, Math.abs(spot[1] - life.position[1]) > 0.12, false)) {
        if (random() < 0.3) line('wander', now);
        return;
      }
    }
    if (roll < 0.75) setMode(world.raised?.(life.position) || random() < 0.5 ? 'sit' : 'stand', now);
    else if (roll < 0.85) setMode('lie', now);
    else if (life.mode === 'stand') play('grin', now);
  };

  life.clip = now => {
    if (life.act) return { name: life.act.name, from: life.act.from, once: true };
    if (life.mode === 'held' || life.mode === 'fly') return { name: 'dangle', from: life.since, loop: true };
    if (life.mode === 'walk') return { name: 'walk', from: life.since, loop: true };
    if (life.mode === 'dizzy') return { name: 'glitch', from: life.since, loop: true };
    if (life.mode === 'hop') return { name: 'leave', from: life.since, once: true };
    if (life.mode === 'stand') return { name: 'idle', from: life.since, loop: true };
    return null;
  };

  return life;
};

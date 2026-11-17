import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createLife } from '../../site/remote/room-life.js';

const flatWorld = (extra = {}) => ({ viewer: [0, 1.4, 0], groundAt: () => 0, raised: () => false, wander: () => null, ...extra });
const run = (life, world, from, ms, step = 16) => {
  let now = from;
  for (; now < from + ms; now += step) life.tick(now, step / 1000, world);
  return now;
};

test('room life places her, walks her to a spot and stands her there facing the way she went', () => {
  const life = createLife({ tall: 0.5, random: () => 0.99 });
  life.place([0, 0, -1], 0);
  assert.equal(life.mode, 'stand');
  assert.equal(life.clip(0).name, 'arrive');
  assert.equal(life.goTo([0.5, 0, -1], 100), true);
  assert.equal(life.mode, 'walk');
  assert.equal(life.clip(200).name, 'walk');
  run(life, flatWorld(), 100, 3000);
  assert.equal(life.mode, 'stand');
  assert.ok(Math.abs(life.position[0] - 0.5) < 1e-9);
  assert.ok(life.face > 1.4 && life.face < 1.7);
});

test('room life hops her up to a raised spot with her glitch and sits her there', () => {
  const life = createLife({ tall: 0.5, random: () => 0.99 });
  const world = flatWorld({ groundAt: p => (p[1] > 0.5 ? 0.7 : 0), raised: p => p[1] > 0.5 });
  life.place([0, 0, -1], 0);
  assert.equal(life.goTo([0.4, 0.7, -1.2], 10), true);
  assert.equal(life.mode, 'hop');
  assert.equal(life.clip(20).name, 'leave');
  run(life, world, 10, 1200);
  assert.equal(life.mode, 'sit');
  assert.equal(life.pose, 'perch');
  assert.deepEqual(life.position, [0.4, 0.7, -1.2]);
});

test('room life dangles her while held, drops her gently and throws her on a flick', () => {
  const lines = [];
  const life = createLife({ tall: 0.5, say: (text, kind) => lines.push(kind), random: () => 0.99 });
  const world = flatWorld();
  life.place([0, 0, -1], 0);
  life.grab([0, 0.2, -1], 5000);
  assert.equal(life.mode, 'held');
  assert.equal(life.clip(5100).name, 'dangle');
  life.drag([0, 0.4, -1], 5050);
  life.drag([0, 0.4, -1], 5100);
  life.release(5100);
  assert.equal(life.mode, 'fly');
  assert.deepEqual(life.velocity, [0, 0, 0]);
  let now = run(life, world, 5100, 600);
  assert.equal(life.mode, 'stand');
  assert.equal(life.position[1], 0);
  life.grab(life.position.slice(), now);
  for (let i = 1; i <= 6; i++) life.drag([life.position[0] + 0.05, life.position[1] + 0.05, life.position[2]], now + i * 16);
  life.release(now + 96);
  assert.equal(life.mode, 'fly');
  assert.ok(life.velocity[0] > 2 && life.velocity[1] > 2);
  assert.ok(lines.includes('throw'));
  run(life, world, now + 96, 3000);
  assert.ok(life.position[0] > 0.5);
  assert.equal(life.position[1], 0);
  assert.notEqual(life.mode, 'fly');
});

test('room life gets dizzy after throws back to back and sulks after three in two minutes', () => {
  const lines = [];
  const life = createLife({ tall: 0.5, say: (text, kind) => lines.push(kind), random: () => 0.99 });
  const world = flatWorld();
  life.place([0, 0, -1], 0);
  let now = 1000;
  const fling = () => {
    life.grab(life.position.slice(), now);
    for (let i = 1; i <= 6; i++) life.drag([life.position[0], life.position[1] + 0.06, life.position[2]], now + i * 16);
    life.release(now + 96);
    now = run(life, world, now + 96, 2000);
  };
  fling();
  assert.equal(life.mode, 'stand');
  fling();
  assert.equal(life.mode, 'dizzy');
  assert.ok(lines.includes('dizzy'));
  now = run(life, world, now, 3000);
  assert.equal(life.mode, 'stand');
  fling();
  assert.ok(lines.includes('sulk'));
  run(life, world, now, 20000);
  assert.equal(life.mode, 'lie');
});

test('room life wanders on her own, naps when ignored, and wakes when poked', () => {
  const lines = [];
  let roll = 0.1;
  const life = createLife({ tall: 0.5, say: (text, kind) => lines.push(kind), random: () => roll });
  const world = flatWorld({ wander: (from, home) => [home[0] + 0.3, 0, home[2]] });
  life.place([0, 0, -1], 0);
  let now = run(life, world, 0, 20000, 50);
  assert.ok(life.position[0] > 0.1);
  roll = 0.99;
  now = run(life, world, now, 100000, 100);
  assert.equal(life.mode, 'lie');
  assert.ok(lines.includes('nap'));
  life.poke(now);
  assert.equal(life.mode, 'sit');
  assert.ok(lines.includes('wake'));
});

test('room life brings her back when she flies out of reach and keeps her still when motion is calm', () => {
  const life = createLife({ tall: 0.5, calm: () => true, random: () => 0.1 });
  const world = flatWorld({ wander: () => [3, 0, 3], nearby: () => [0, 0, -1] });
  life.place([0, 0, -1], 0);
  run(life, world, 0, 30000, 100);
  assert.deepEqual(life.position, [0, 0, -1]);
  life.grab([0, 0, -1], 40000);
  life.drag([0, 0, -1], 40000);
  life.drag([0.11, 0.05, -1], 40016);
  life.release(40016);
  run(life, world, 40016, 4000);
  assert.ok(Math.hypot(life.position[0], life.position[2]) < 7.1);
});

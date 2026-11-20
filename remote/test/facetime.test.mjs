import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { chromePath, launch } from './lib/cdp.mjs';
import { FakePc } from './lib/fakes.mjs';
import * as R from './lib/capri-remote.mjs';
import * as L from './lib/live.mjs';
import { startRelay } from './lib/relay-dev.mjs';
import { ROOT, SITE, earlyAccess, startSite } from './lib/site.mjs';

const SITE_PORT = 8798, CHROME_PORT = 8799;
const SHOTS = process.env.CAPRI_PHONE_SHOTS || '';
const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
const require = createRequire(import.meta.url);
const { make } = require(join(SITE, 'lipsync.js'));

const wav = (path, rate, samples) => {
  const header = Buffer.alloc(44);
  header.write('RIFF', 0, 'ascii');
  header.writeUInt32LE(36 + samples.length * 2, 4);
  header.write('WAVEfmt ', 8, 'ascii');
  header.writeUInt32LE(16, 16);
  header.writeUInt16LE(1, 20);
  header.writeUInt16LE(1, 22);
  header.writeUInt32LE(rate, 24);
  header.writeUInt32LE(rate * 2, 28);
  header.writeUInt16LE(2, 32);
  header.writeUInt16LE(16, 34);
  header.write('data', 36, 'ascii');
  header.writeUInt32LE(samples.length * 2, 40);
  writeFileSync(path, Buffer.concat([header, Buffer.from(samples.buffer, samples.byteOffset, samples.byteLength)]));
  return path;
};
const readWav = path => {
  const bytes = readFileSync(path);
  let at = 12, rate = 0, data = null;
  while (at + 8 <= bytes.length) {
    const id = bytes.toString('ascii', at, at + 4), size = bytes.readUInt32LE(at + 4);
    if (id === 'fmt ') rate = bytes.readUInt32LE(at + 12);
    if (id === 'data') data = bytes.subarray(at + 8, at + 8 + size);
    at += 8 + size + (size & 1);
  }
  return { rate, samples: new Int16Array(data.buffer.slice(data.byteOffset, data.byteOffset + (data.length & ~1))) };
};

const vowel = (rate, seconds, formants, pitch = 210) => {
  const out = new Float32Array(Math.floor(rate * seconds));
  const gain = hz => formants.reduce((sum, [f, width, level]) => sum + level * Math.exp(-(((hz - f) / width) ** 2)), 0.02);
  for (let k = 1; k * pitch < rate / 2 - 200; k++) {
    const amp = gain(k * pitch) / k ** 0.6, phase = k * 1.7;
    for (let i = 0; i < out.length; i++) out[i] += amp * Math.sin(2 * Math.PI * k * pitch * i / rate + phase);
  }
  let peak = 0;
  for (const v of out) peak = Math.max(peak, Math.abs(v));
  for (let i = 0; i < out.length; i++) out[i] = out[i] / peak * 0.5;
  return out;
};
const hiss = (rate, seconds) => {
  const out = new Float32Array(Math.floor(rate * seconds));
  let seed = 3, last = 0;
  for (let i = 0; i < out.length; i++) {
    const white = ((seed = (seed * 1103515245 + 12345) & 0x7fffffff) / 0x7fffffff) * 2 - 1;
    out[i] = (white - last) * 0.35;
    last = white;
  }
  return out;
};
const spectrumDb = (samples, size) => {
  const out = new Float32Array(size / 2);
  for (let k = 0; k < size / 2; k++) {
    let re = 0, im = 0;
    for (let n = 0; n < size; n++) {
      const w = 0.5 - 0.5 * Math.cos(2 * Math.PI * n / (size - 1));
      re += samples[n] * w * Math.cos(2 * Math.PI * k * n / size);
      im -= samples[n] * w * Math.sin(2 * Math.PI * k * n / size);
    }
    out[k] = 10 * Math.log10((re * re + im * im) / size / size + 1e-20);
  }
  return out;
};
const settle = (follow, samples, rate) => {
  const size = 512, spectrum = spectrumDb(samples.subarray(0, size), size);
  let shape;
  for (let i = 0; i < 12; i++) shape = follow(samples.subarray(0, size), spectrum, rate, size);
  return shape;
};

test('FACETIME lip sync: her jaw follows her voice, ee spreads her mouth, oo rounds it, hiss keeps her teeth together and quiet shuts it', () => {
  const rate = 24000;
  const ah = settle(make(), vowel(rate, 0.1, [[800, 160, 1], [1200, 200, 0.5]]), rate);
  const ee = settle(make(), vowel(rate, 0.1, [[300, 90, 1], [2300, 300, 0.9], [3000, 300, 0.4]]), rate);
  const oo = settle(make(), vowel(rate, 0.1, [[320, 90, 1], [700, 120, 0.45]]), rate);
  const ss = settle(make(), hiss(rate, 0.1), rate);
  const quiet = settle(make(), new Float32Array(1024), rate);
  assert.ok(ah.open > 0.6, 'ah opens her jaw: ' + JSON.stringify(ah));
  assert.ok(ee.wide > 0.5 && ee.wide > ah.wide, 'ee spreads her mouth: ' + JSON.stringify(ee));
  assert.ok(oo.round > 0.4 && oo.wide < 0.2, 'oo rounds her mouth: ' + JSON.stringify(oo));
  assert.ok(ss.open < 0.35 && ss.wide > 0.3, 'hiss keeps her teeth together: ' + JSON.stringify(ss));
  assert.ok(quiet.open < 0.01 && quiet.wide < 0.01 && quiet.round < 0.01, 'quiet shuts her mouth');
  const follow = make();
  const loud = vowel(rate, 0.1, [[800, 160, 1]]);
  settle(follow, loud, rate);
  let shape;
  for (let i = 0; i < 8; i++) shape = follow(new Float32Array(512), null, rate, 512);
  assert.ok(shape.open < 0.05, 'her mouth closes within a few frames of silence');
});

test('FACETIME her face model is her traced art with the mouth, eye and blink rig the page drives', () => {
  const bytes = readFileSync(join(SITE, 'models', 'capri-face.glb'));
  assert.equal(bytes.readUInt32LE(0), 0x46546c67, 'a glb');
  assert.ok(bytes.length < 200000, 'small enough for a phone: ' + bytes.length);
  const json = JSON.parse(bytes.subarray(20, 20 + bytes.readUInt32LE(12)).toString('utf8'));
  const names = new Set(json.nodes.map(node => node.name));
  for (const name of ['capri', 'head', 'hair', 'face', 'eye_r', 'eye_l', 'iris_r', 'iris_l', 'mouth', 'teeth_top', 'teeth_low', 'tongue']) assert.ok(names.has(name), 'has ' + name);
  const mouth = json.meshes.find(mesh => mesh.name === 'mouth');
  assert.deepEqual(mouth.extras.targetNames, ['open', 'wide', 'round']);
  assert.ok(json.materials.every(m => /^c_[0-9a-f]{6}/.test(m.name)), 'every material names its colour');
  assert.ok(readFileSync(join(ROOT, 'tools', 'facetime-model', 'build.py'), 'utf8').includes('import bpy'), 'built in Blender');
});

const SPILLS = `(() => {
  const bad = [];
  const view = { w: innerWidth, h: innerHeight };
  const name = el => el.tagName.toLowerCase() + '.' + String(el.className.baseVal === undefined ? el.className : '').split(' ').join('.');
  if (document.documentElement.scrollWidth > view.w + 1) bad.push('the page scrolls sideways');
  const room = document.querySelector('.call-room');
  for (const el of room.querySelectorAll('.call-top, .call-mid, .call-caps, .call-keys, .call-key, .ft-you')) {
    if (el.closest('[hidden]') || !el.getClientRects().length) continue;
    const box = el.getBoundingClientRect();
    if (box.left < -0.5 || box.top < -0.5 || box.right > view.w + 0.5 || box.bottom > view.h + 0.5) bad.push(name(el) + ' is off the screen');
  }
  const you = room.querySelector('.ft-you').getBoundingClientRect(), keys = room.querySelector('.call-keys').getBoundingClientRect(), mid = room.querySelector('.call-mid').getBoundingClientRect();
  if (you.bottom > keys.top - 4) bad.push('your camera covers the keys');
  if (you.top < mid.top && you.left < mid.right && you.right > mid.left && you.bottom > mid.top) bad.push('your camera covers the status');
  return bad;
})()`;

const rig = async ({ features, mic }) => {
  const relay = await startRelay();
  const site = await startSite(SITE_PORT);
  const pc = new FakePc(relay);
  const r = { pc, page: null, device: null, lines: [], looks: [], problems: [], asked: [] };
  let reading = true, reader = null;
  r.close = async () => {
    reading = false;
    if (reader) await Promise.race([reader, wait(1500)]);
    if (r.page) await r.page.close();
    pc.close();
    await site.close();
    await relay.stop();
  };
  try {
    await pc.connect();
    const qr = await pc.showCode();
    const page = (r.page = await launch({ port: CHROME_PORT, args: ['--use-fake-ui-for-media-stream', '--use-fake-device-for-media-stream', '--use-file-for-fake-audio-capture=' + mic, '--mute-audio', '--disable-audio-output', '--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'] }));
    r.shot = async name => { if (SHOTS) { mkdirSync(SHOTS, { recursive: true }); await page.shot(join(SHOTS, name + '.png')); } };
    r.call = () => page.run('window.CapriCall.stats()');
    r.face = () => page.run('window.CapriFaceTime.stats()');
    r.tap = async selector => {
      const at = await page.run(`(() => { const el = document.querySelector(${JSON.stringify(selector)}); if (!el || !el.getClientRects().length) throw new Error('no ${selector} on screen'); const box = el.getBoundingClientRect(); return { x: box.left + box.width / 2, y: box.top + box.height / 2 }; })()`);
      await page.send('Input.dispatchMouseEvent', { type: 'mouseMoved', x: at.x, y: at.y });
      await page.send('Input.dispatchMouseEvent', { type: 'mousePressed', x: at.x, y: at.y, button: 'left', clickCount: 1 });
      await page.send('Input.dispatchMouseEvent', { type: 'mouseReleased', x: at.x, y: at.y, button: 'left', clickCount: 1 });
    };
    r.size = async (width, height) => {
      await page.send('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: 2, mobile: false });
      await wait(300);
    };
    const open = new Map();
    const read = async () => {
      while (reading) {
        let item;
        try { item = await pc.conn.next(500); } catch (error) { if (/timed out/.test(error.message)) continue; break; }
        if (item.text) continue;
        const { device: id, envelope } = R.splitPcFrame(item.data);
        const message = await pc.devices.get(id).session.open(envelope);
        r.asked.push(message.type);
        if (message.type === 'call.audio') {
          let line = open.get(message.id);
          if (!line) { line = { id: message.id, pieces: [], at: Date.now() }; open.set(message.id, line); }
          line.pieces.push(R.fromB64u(message.pcm));
          if (message.end) { open.delete(message.id); line.endAt = Date.now(); r.lines.push(line); }
        } else if (message.type === 'call.look') {
          r.looks.push({ at: Date.now(), id: message.id, picture: message.picture, bytes: JSON.stringify(message).length });
        } else if (message.type === 'call.start') {
          await pc.send(r.device.id, { type: 'call.state', on: true, voice: true, ears: true });
        }
      }
    };
    r.nextLine = async (count, ms = 20000) => {
      const end = Date.now() + ms;
      while (r.lines.length < count) {
        if (Date.now() > end) throw new Error('timed out waiting for line ' + count);
        await wait(50);
      }
      return r.lines[count - 1];
    };
    await page.send('Page.addScriptToEvaluateOnNewDocument', { source: earlyAccess() });
    await page.go(`http://127.0.0.1:${SITE_PORT}/?relay=${relay.ws}${qr.slice(qr.indexOf('#'))}`);
    r.device = await pc.allow(await pc.pairRequest());
    await pc.conn.json('joined', 10000);
    await pc.acceptHello(features);
    reader = read();
    await page.until(`document.querySelector('#app').dataset.link === 'connected'`, 'the paired PC');
    return r;
  } catch (error) {
    await r.close();
    throw error;
  }
};

const buzzLines = folder => {
  const rate = 16000, out = new Int16Array(rate * 7);
  for (let i = 0; i < out.length; i++) {
    const t = i / rate;
    if (t < 1.5 || t > 3.1) continue;
    const syllable = Math.max(0, Math.sin(2 * Math.PI * 4 * t)) ** 0.6;
    const wave = Math.sin(2 * Math.PI * 150 * t) + 0.5 * Math.sin(2 * Math.PI * 300 * t) + 0.3 * Math.sin(2 * Math.PI * 1100 * t);
    out[i] = Math.round(wave / 1.8 * syllable * 14000);
  }
  return wav(join(folder, 'lines.wav'), rate, out);
};
const herVoice = () => {
  const parts = ['hello-morning.wav', 'hello-late.wav', 'hello-miss.wav'].map(name => readWav(join(ROOT, 'assets', 'voice', name)));
  const gap = new Int16Array(parts[0].rate * 0.35);
  const all = [];
  for (const part of parts) all.push(part.samples, gap);
  const out = new Int16Array(all.reduce((n, a) => n + a.length, 0));
  let at = 0;
  for (const a of all) { out.set(a, at); at += a.length; }
  return { rate: parts[0].rate, samples: out };
};

test('FACETIME video call: her talking face fills the call, your camera sits in a corner, each line carries a snapshot of you, and hanging up lets the camera go', { skip: !chromePath(), timeout: 240000 }, async t => {
  const folder = mkdtempSync(join(tmpdir(), 'capri-facetime-'));
  let r = null;
  try {
    r = await rig({ features: ['chat', 'call', 'hush', 'pictures', 'call-look'], mic: buzzLines(folder) });
    const { page, pc } = r;
    await r.tap('#f-call');
    await page.until(`window.CapriCall.stats().status === 'listening' || window.CapriCall.stats().status === 'hearing'`, 'the call', 10000);
    assert.equal(await page.run(`document.querySelector('.call-key.video').hidden`), false, 'the call offers video');
    await r.shot('facetime-0-voice-call');

    await t.test('Video turns her face and your camera on', async () => {
      await r.tap('.call-key.video');
      await page.until(`document.querySelector('.call-room').classList.contains('video')`, 'the video layout');
      await page.until(`window.CapriFaceTime.stats().ready === 'true'`, 'her face to load', 20000);
      await page.until(`window.CapriFaceTime.camera === 'live'`, 'your camera', 10000);
      assert.equal(await page.run(`document.querySelector('.call-key.video').getAttribute('aria-pressed')`), 'true');
      assert.equal(await page.run(`document.querySelector('.ft-badge').dataset.on`), 'true', 'she can see you');
      const before = (await r.face()).frames;
      await wait(600);
      assert.ok((await r.face()).frames > before + 5, 'she is drawn every frame');
    });

    await t.test('a line he says carries one small snapshot of him, after its last part', async () => {
      const line = await r.nextLine(1);
      const end = Date.now() + 4000;
      while (!r.looks.find(look => look.id === line.id) && Date.now() < end) await wait(50);
      const look = r.looks.find(one => one.id === line.id);
      assert.ok(look, 'the line has a look');
      assert.equal(look.picture.mime, 'image/jpeg');
      const jpeg = R.fromB64u(look.picture.data);
      assert.equal(jpeg[0], 0xff);
      assert.equal(jpeg[1], 0xd8);
      const size = L.jpegSize(jpeg);
      assert.ok(Math.max(size.w, size.h) <= 512, 'at most 512 px: ' + JSON.stringify(size));
      assert.ok(look.bytes < L.MAX_PLAINTEXT, 'fits one message: ' + look.bytes);
      assert.ok(look.at >= line.endAt, 'after the line');
      if (SHOTS) writeFileSync(join(SHOTS, 'facetime-look.jpg'), jpeg);
    });

    await t.test('her mouth moves with her real voice and shuts after', async () => {
      const line = r.lines[0];
      await pc.send(r.device.id, { type: 'call.heard', id: line.id, text: 'Hey Capri, can you see me?' });
      await pc.send(r.device.id, { type: 'chat.reply', id: 'reply-1', re: line.id, text: 'Morning! Yeah I can see you. Nice hoodie.', done: true });
      const voice = herVoice();
      const before = (await r.face()).mouth;
      await pc.voice(r.device.id, voice.samples, voice.rate);
      await page.until(`window.CapriCall.stats().status === 'talking'`, 'her voice to play', 8000);
      await wait(1200);
      await r.shot('facetime-1-talking');
      await wait(1500);
      await r.shot('facetime-2-talking');
      const during = (await r.face()).mouth;
      assert.ok(during > before + 10, 'her mouth opened with her voice: ' + (during - before) + ' frames');
      await page.until(`window.CapriCall.stats().playing === 0`, 'her voice to end', 15000);
      await wait(600);
      const after = (await r.face()).mouth;
      await wait(800);
      assert.ok((await r.face()).mouth - after <= 2, 'her mouth stays shut once she is quiet');
    });

    await t.test('the call screen fits at phone sizes with video on', async () => {
      for (const [w, h] of [[390, 844], [360, 640], [430, 932]]) {
        await r.size(w, h);
        assert.deepEqual(await page.run(SPILLS), [], w + 'x' + h);
        await r.shot('facetime-3-' + w + 'x' + h);
      }
      await r.size(390, 844);
    });

    await t.test('tapping your camera turns it off, and a line then goes without a look', async () => {
      await r.tap('.ft-you');
      await page.until(`window.CapriFaceTime.camera === 'off'`, 'the camera off');
      assert.equal(await page.run(`document.querySelector('.ft-badge').dataset.on`), 'false');
      const looks = r.looks.length;
      await r.nextLine(r.lines.length + 1);
      await wait(800);
      assert.equal(r.looks.length, looks, 'no look with the camera off');
      await r.shot('facetime-4-camera-off');
      await r.tap('.ft-you');
      await page.until(`window.CapriFaceTime.camera === 'live'`, 'the camera back');
    });

    await t.test('Video off goes back to the voice call, and End lets the camera go', async () => {
      await r.tap('.call-key.video');
      await page.until(`!document.querySelector('.call-room').classList.contains('video')`, 'the voice layout');
      assert.equal(await page.run(`!!document.querySelector('.ft-you')`), false);
      assert.equal(await page.run(`window.CapriFaceTime.camera`), 'off');
      await r.tap('.call-key.video');
      await page.until(`window.CapriFaceTime.camera === 'live'`, 'the camera again');
      await r.tap('.call-key.end');
      await page.until(`window.CapriCall.state !== 'on'`, 'the call to end');
      assert.equal(await page.run(`window.CapriFaceTime.open`), false);
      assert.equal(await page.run(`window.CapriFaceTime.camera`), 'off');
    });
    assert.deepEqual(r.problems, []);
  } finally {
    if (r) await r.close();
    rmSync(folder, { recursive: true, force: true });
  }
});

test('FACETIME with a PC that cannot see: video still works, she says she cannot see you, and no look is sent', { skip: !chromePath(), timeout: 120000 }, async () => {
  const folder = mkdtempSync(join(tmpdir(), 'capri-facetime-old-'));
  let r = null;
  try {
    r = await rig({ features: ['chat', 'call', 'hush'], mic: buzzLines(folder) });
    const { page } = r;
    await r.tap('#f-call');
    await page.until(`window.CapriCall.state === 'on'`, 'the call', 10000);
    await r.tap('.call-key.video');
    await page.until(`window.CapriFaceTime.camera === 'live'`, 'your camera', 10000);
    assert.equal(await page.run(`document.querySelector('.ft-badge').dataset.on`), 'false');
    assert.match(await page.run(`document.querySelector('.ft-you').getAttribute('aria-label')`), /can't see you|Update Capri/);
    await r.nextLine(1);
    await wait(1000);
    assert.equal(r.looks.length, 0, 'no look to a PC without call-look');
    assert.equal(r.asked.includes('call.look'), false);
  } finally {
    if (r) await r.close();
    rmSync(folder, { recursive: true, force: true });
  }
});

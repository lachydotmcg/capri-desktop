// CALL in a real browser, hands-free (site/remote/call.js, PROTOCOL.md section 8.2). Headless Chrome pairs the phone page
// with a stand-in PC (lib/fakes.mjs) through the relay under wrangler dev and calls her, with a fake mic playing a WAV of
// two spoken sentences with quiet, a click and a blip between them, over and over (made with Windows' own voice). Each
// sentence must reach the PC as one line, and nothing may go while he's muted or while her voice plays; after she stops
// he's heard again, and hanging up lets go of the mic. It also shoots the call screen at two phone sizes and checks
// nothing spills out. Ports 8796 and 8797, and a free one for the relay. Skipped where Chrome or Windows' voice isn't.
// CAPRI_PHONE_SHOTS=<folder> keeps the screenshots.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { chromePath, launch } from './lib/cdp.mjs';
import { FakePc } from './lib/fakes.mjs';
import * as R from './lib/capri-remote.mjs';
import * as L from './lib/live.mjs';
import { startRelay } from './lib/relay-dev.mjs';
import { SITE, earlyAccess, startSite } from './lib/site.mjs';

const SITE_PORT = 8796, CHROME_PORT = 8797;
const SHOTS = process.env.CAPRI_PHONE_SHOTS || '';
const wait = ms => new Promise(resolve => setTimeout(resolve, ms));

// Two sentences, each with a pause inside it, in Windows' own voice at 16 kHz.
const SPEAK = [
  'param([string]$Out)',
  'Add-Type -AssemblyName System.Speech',
  '$voice = New-Object System.Speech.Synthesis.SpeechSynthesizer',
  '$format = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(16000, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)',
  '$voice.SetOutputToWaveFile($Out, $format)',
  '$say = New-Object System.Speech.Synthesis.PromptBuilder',
  '$say.AppendBreak([TimeSpan]::FromMilliseconds(1500))',
  "$say.AppendText('Hey Capri, what is the weather like today?')",
  '$say.AppendBreak([TimeSpan]::FromMilliseconds(1400))',
  "$say.AppendText('And can you open my music, please?')",
  '$say.AppendBreak([TimeSpan]::FromMilliseconds(1400))',
  '$voice.Speak($say)',
  '$voice.Dispose()'
].join('\r\n');

// The WAV's samples and where its data sits.
const readWav = path => {
  const bytes = readFileSync(path);
  let at = 12, fmt = null, data = null;
  while (at + 8 <= bytes.length) {
    const id = bytes.toString('ascii', at, at + 4), size = bytes.readUInt32LE(at + 4);
    if (id === 'fmt ') fmt = { channels: bytes.readUInt16LE(at + 10), rate: bytes.readUInt32LE(at + 12), bits: bytes.readUInt16LE(at + 22) };
    if (id === 'data') data = { at: at + 8, size: Math.min(size, bytes.length - at - 8) };
    at += 8 + size + (size & 1);
  }
  return { bytes, fmt, data };
};
// Where someone's talking: 20 ms steps over -45 dBFS, with pauses under 0.6 s inside one stretch.
const stretches = (samples, rate) => {
  const step = rate / 50, found = [];
  let open = null;
  for (let at = 0; at + step <= samples.length; at += step) {
    let sum = 0;
    for (let i = at; i < at + step; i++) sum += samples[i] * samples[i];
    const loud = 10 * Math.log10(sum / step / 32768 / 32768 + 1e-12) > -45;
    const time = at / rate;
    if (loud) {
      if (open && time - open.end > 0.6) { found.push(open); open = null; }
      if (!open) open = { start: time, end: time };
      open.end = time + 0.02;
    }
  }
  if (open) found.push(open);
  return found;
};
// The fake mic's sound: the two sentences, with a 20 ms click and a 120 ms blip in each quiet stretch between them.
const makeWav = folder => {
  const script = join(folder, 'speak.ps1'), wav = join(folder, 'two-lines.wav');
  writeFileSync(script, SPEAK);
  const made = spawnSync('powershell', ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', script, '-Out', wav], { encoding: 'utf8', timeout: 60000 });
  if (made.status !== 0) throw new Error('Windows speech failed: ' + (made.stderr || made.stdout));
  const { bytes, fmt, data } = readWav(wav);
  assert.deepEqual(fmt, { channels: 1, rate: 16000, bits: 16 }, 'the WAV is 16 kHz mono 16-bit');
  const samples = new Int16Array(bytes.buffer.slice(bytes.byteOffset + data.at, bytes.byteOffset + data.at + (data.size & ~1)));
  const said = stretches(samples, fmt.rate);
  assert.equal(said.length, 2, 'two sentences in the WAV: ' + JSON.stringify(said));
  const seconds = samples.length / fmt.rate;
  const gaps = [[said[0].end, said[1].start], [said[1].end, seconds + said[0].start]];
  let seed = 7;
  const noise = () => ((seed = (seed * 1103515245 + 12345) & 0x7fffffff) / 0x7fffffff) * 2 - 1;
  for (const [from, to] of gaps) {
    const click = Math.floor(((from + to) / 2 - 0.25) * fmt.rate), blip = Math.floor(((from + to) / 2 + 0.15) * fmt.rate);
    for (let i = 0; i < 0.02 * fmt.rate && click + i < samples.length; i++) samples[click + i] = i % 2 ? 20000 : -20000;
    for (let i = 0; i < 0.12 * fmt.rate && blip + i < samples.length; i++) samples[blip + i] = Math.round(noise() * 9000);
  }
  Buffer.from(samples.buffer).copy(bytes, data.at);
  writeFileSync(wav, bytes);
  return { wav, said, seconds };
};

// A made-up desktop as a JPEG, drawn by the page itself, for the PC screen's mini player.
const DESKTOP = `(() => {
  const canvas = document.createElement('canvas');
  canvas.width = 640; canvas.height = 360;
  const g = canvas.getContext('2d');
  const sky = g.createLinearGradient(0, 0, 640, 360);
  sky.addColorStop(0, '#1b2a4a'); sky.addColorStop(1, '#4a1c3a');
  g.fillStyle = sky; g.fillRect(0, 0, 640, 360);
  g.fillStyle = '#0d0d14'; g.fillRect(0, 336, 640, 24);
  g.fillStyle = '#e8ecf0'; g.fillRect(70, 50, 330, 210);
  g.fillStyle = '#3a6df0'; g.fillRect(70, 50, 330, 22);
  g.fillStyle = '#20202a'; g.fillRect(300, 120, 280, 170);
  g.fillStyle = '#1db954'; g.fillRect(320, 140, 60, 60);
  g.fillStyle = '#9aa0aa'; for (let i = 0; i < 6; i++) g.fillRect(90, 90 + i * 26, 200 - i * 18, 10);
  return canvas.toDataURL('image/jpeg', 0.8).split(',')[1];
})()`;

// Whether anything on the call screen (or the call bar) spills out of the screen or its box, or runs into the next part.
const SPILLS = `(() => {
  const bad = [];
  const view = { w: innerWidth, h: innerHeight };
  const name = el => el.className && el.className.baseVal === undefined ? el.tagName.toLowerCase() + '.' + String(el.className).split(' ').join('.') : el.tagName.toLowerCase();
  if (document.documentElement.scrollWidth > view.w + 1) bad.push('the page scrolls sideways');
  const check = root => {
    if (root.scrollHeight > root.clientHeight + 1) bad.push(name(root) + ' scrolls: ' + root.scrollHeight + ' > ' + root.clientHeight);
    for (const el of root.querySelectorAll('*')) {
      if (el.closest('[hidden]') || !el.getClientRects().length) continue;
      const style = getComputedStyle(el);
      if (style.visibility === 'hidden') continue;
      const box = el.getBoundingClientRect();
      if (!box.width && !box.height) continue;
      if (box.left < -0.5 || box.top < -0.5 || box.right > view.w + 0.5 || box.bottom > view.h + 0.5) bad.push(name(el) + ' is off the screen');
      if (style.display !== 'inline' && style.textOverflow !== 'ellipsis' && el.scrollWidth > el.clientWidth + 1 && el.clientWidth > 0 && !(el instanceof SVGElement)) bad.push(name(el) + ' spills sideways');
    }
  };
  const room = document.querySelector('.call-room');
  if (room && !room.hidden) {
    check(room);
    const parts = ['.call-top', '.call-who', '.call-mid', '.call-stage', '.call-caps', '.call-keys'].map(s => room.querySelector(s)).filter(el => el && el.getClientRects().length);
    for (let i = 1; i < parts.length; i++) {
      const above = parts[i - 1].getBoundingClientRect(), below = parts[i].getBoundingClientRect();
      if (below.top < above.bottom - 0.5) bad.push(name(parts[i]) + ' runs into ' + name(parts[i - 1]) + ' by ' + (above.bottom - below.top).toFixed(1) + ' px');
    }
    const mini = document.querySelector('.mini');
    if (mini && !mini.hidden) {
      const box = mini.getBoundingClientRect(), keys = room.querySelector('.call-keys').getBoundingClientRect(), top = room.querySelector('.call-top').getBoundingClientRect();
      if (box.bottom > keys.top + 0.5 || box.top < top.bottom - 0.5) bad.push('the mini player covers the keys or the top bar');
    }
  }
  const bar = document.querySelector('.callbar');
  if (bar && !bar.hidden) check(bar);
  return bad;
})()`;

test('CALL hands-free: one line per thing he says, nothing while muted or while she talks, and the mic let go at the end', { skip: (!chromePath() && 'Chrome is not installed') || (process.platform !== 'win32' && 'the fake mic is made with Windows speech'), timeout: 240000 }, async () => {
  const folder = mkdtempSync(join(tmpdir(), 'capri-call-'));
  const { wav, said, seconds } = makeWav(folder);
  const relay = await startRelay();
  const site = await startSite(SITE_PORT);
  const pc = new FakePc(relay);
  let page = null, reading = true, reader = null;
  try {
    await pc.connect();
    const qr = await pc.showCode();
    page = await launch({ port: CHROME_PORT, args: ['--use-fake-ui-for-media-stream', '--use-fake-device-for-media-stream', '--use-file-for-fake-audio-capture=' + wav, '--mute-audio', '--disable-audio-output'] });
    const shot = async name => { if (SHOTS) { mkdirSync(SHOTS, { recursive: true }); await page.shot(join(SHOTS, name + '.png')); } };
    const stats = () => page.run('window.CapriCall.stats()');
    const text = selector => page.run(`(document.querySelector(${JSON.stringify(selector)}) || {}).textContent || ''`);
    // A real tap, as a finger would: it counts as a user gesture for the sound and the mic.
    const tap = async selector => {
      const at = await page.run(`(() => { const el = document.querySelector(${JSON.stringify(selector)}); if (!el || !el.getClientRects().length) throw new Error('no ${selector} on screen'); const box = el.getBoundingClientRect(); return { x: box.left + box.width / 2, y: box.top + box.height / 2 }; })()`);
      await page.send('Input.dispatchMouseEvent', { type: 'mouseMoved', x: at.x, y: at.y });
      await page.send('Input.dispatchMouseEvent', { type: 'mousePressed', x: at.x, y: at.y, button: 'left', clickCount: 1 });
      await page.send('Input.dispatchMouseEvent', { type: 'mouseReleased', x: at.x, y: at.y, button: 'left', clickCount: 1 });
    };
    const size = async (width, height) => {
      await page.send('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: 2, mobile: false });
      await wait(250);
    };

    // Everything the phone sends the PC once its session is up, as it comes. Lines are put together part by part and
    // checked as they go.
    const lines = [], parts = [], cancels = [], problems = [], asked = [];
    const open = new Map();
    let device = null;
    const read = async () => {
      while (reading) {
        let item;
        try { item = await pc.conn.next(500); } catch (error) { if (/timed out/.test(error.message)) continue; break; }
        if (item.text) continue;
        const { device: id, envelope } = R.splitPcFrame(item.data);
        const message = await pc.devices.get(id).session.open(envelope);
        const at = Date.now();
        asked.push(message.type);
        if (message.type === 'call.audio') {
          parts.push({ at, n: message.n, id: message.id });
          let line = open.get(message.id);
          if (!line) {
            if (message.n !== 0) problems.push('line ' + message.id + ' started at part ' + message.n);
            line = { id: message.id, startAt: at, pieces: [] };
            open.set(message.id, line);
          }
          if (message.n !== line.pieces.length) problems.push('line ' + message.id + ' part ' + message.n + ' out of order');
          if (message.rate !== L.CALL_RATE) problems.push('line ' + message.id + ' at ' + message.rate + ' Hz');
          line.pieces.push(R.fromB64u(message.pcm));
          if (message.end) {
            open.delete(message.id);
            line.endAt = at;
            line.samples = L.pcmSamples(R.concat(...line.pieces));
            lines.push(line);
          }
        } else if (message.type === 'call.cancel') cancels.push(message.id);
        else if (message.type === 'call.start') await pc.send(device.id, { type: 'call.state', on: true, voice: true, ears: true });
        else if (message.type === 'watch.start') {
          const jpeg = new Uint8Array(Buffer.from(await page.run(DESKTOP), 'base64'));
          await pc.send(device.id, { type: 'watch.state', on: true });
          await pc.send(device.id, { type: 'watch.monitors', list: [{ id: '1', name: 'Monitor 1', w: 1920, h: 1080, primary: true }], monitor: '1', auto: true, missing: false });
          await pc.send(device.id, { type: 'watch.frame', n: 1, part: 0, parts: 1, w: 640, h: 360, monitor: '1', bytes: jpeg.length, cx: 6200, cy: 4100, data: R.b64u(jpeg) });
        }
      }
    };
    const nextLine = async (count, what, ms = 16000) => {
      const end = Date.now() + ms;
      while (lines.length < count) {
        if (Date.now() > end) throw new Error('timed out waiting for ' + what + ' (lines so far: ' + lines.length + ', parts: ' + parts.length + ')');
        await wait(50);
      }
      return lines[count - 1];
    };
    const seconds_ = line => line.samples.length / L.CALL_RATE;
    const db = (samples, from, to) => {
      let sum = 0;
      for (let i = from; i < to; i++) sum += samples[i] * samples[i];
      return 10 * Math.log10(sum / Math.max(1, to - from) / 32768 / 32768 + 1e-12);
    };
    // The PC's side of each line: her ears' words, then her answer in the chat (and her voice when asked for).
    const answer = async (line, heard, reply) => {
      await pc.send(device.id, { type: 'call.heard', id: line.id, text: heard });
      await pc.send(device.id, { type: 'chat.reply', id: 'reply-' + line.id, re: line.id, text: reply, done: true });
    };

    // Every mic stream the page opens, so the test can see it let go.
    await page.send('Page.addScriptToEvaluateOnNewDocument', { source: earlyAccess() + ` (() => { const devices = navigator.mediaDevices; if (!devices || !devices.getUserMedia) return; const real = devices.getUserMedia.bind(devices); window.__micStreams = []; devices.getUserMedia = constraints => real(constraints).then(stream => { window.__micStreams.push(stream); return stream; }); })();` });
    await page.go(`http://127.0.0.1:${SITE_PORT}/?relay=${relay.ws}${qr.slice(qr.indexOf('#'))}`);
    device = await pc.allow(await pc.pairRequest());
    await pc.conn.json('joined', 10000);
    await pc.acceptHello(['chat', 'call', 'watch']);
    reader = read();
    await page.until(`document.querySelector('#app').dataset.link === 'connected'`, 'the paired PC');
    assert.equal(await page.run('Array.isArray(window.__micStreams)'), true, 'the mic spy is in the page');
    assert.ok(!readFileSync(join(SITE, 'call.js'), 'utf8').includes('Too short'), 'nothing in the call can say Too short');

    // One tap rings her: the call screen comes up, the mic opens once, and the PC screen shows in the mini player.
    const startedAt = Date.now();
    await tap('#f-call');
    await page.until(`!document.querySelector('.call-room').hidden`, 'the call screen');
    await page.until(`window.CapriCall.stats().status === 'listening'`, 'listening', 10000);
    assert.equal(await page.run('window.__micStreams.length'), 1, 'one mic for the call');
    assert.equal(await page.run(`window.CapriCall.stats().sound`), 'running');
    await page.until(`!document.querySelector('.mini').hidden && !document.querySelector('.watch-pic').hidden`, 'the PC screen in the mini player', 8000);
    assert.equal(await page.run(`document.querySelector('.call-screen').getAttribute('aria-pressed')`), 'true');

    // He says two things with quiet between them: two lines, each one whole sentence with a little from before it.
    const first = await nextLine(1, 'his first line');
    await answer(first, 'Hey Capri, what is the weather like today?', 'Clear skies all day. Perfect for a walk.');
    const second = await nextLine(2, 'his second line');
    await answer(second, 'And can you open my music, please?', 'Opening your music now.');
    const longest = Math.max(said[0].end - said[0].start, said[1].end - said[1].start);
    for (const line of [first, second]) {
      const length = seconds_(line);
      assert.ok(length > 1.2 && length < longest + 1.6, `a line as long as a sentence: ${length.toFixed(2)} s (sentences ${said.map(s => (s.end - s.start).toFixed(2)).join(', ')} s)`);
      assert.ok(db(line.samples, 0, 1600) < db(line.samples, 0, line.samples.length) - 6, 'it starts with a moment from before he spoke');
    }
    assert.equal(lines.length, 2, 'a line for each sentence, and none for the click or the blip');
    assert.equal(cancels.length, 0);
    await page.until(`document.querySelector('.cap-her .cap-words').textContent === 'Opening your music now.'`, 'her caption');
    assert.equal(await text('.cap-you .cap-words'), 'And can you open my music, please?');
    const listened = await stats();
    assert.equal(listened.lines, 2);
    assert.ok(listened.blips >= 1, 'the click and the blip were heard and dropped: ' + listened.blips);
    await size(390, 844);
    await shot('call-listening-390');
    assert.deepEqual(await page.run(SPILLS), [], 'nothing spills at 390 x 844');

    // Muted, a whole round of the WAV goes by and nothing is sent.
    await tap('.call-key.mute');
    await page.until(`window.CapriCall.stats().status === 'muted'`, 'muted');
    assert.equal(await page.run(`window.__micStreams[0].getAudioTracks()[0].enabled`), false, 'the mic track is off while muted');
    assert.equal(await text('.call-word'), 'Muted');
    await shot('call-muted-390');
    const mutedAt = Date.now();
    await wait((seconds + 0.5) * 1000);
    assert.equal(parts.filter(part => part.at >= mutedAt).length, 0, 'nothing sent while muted');

    // Unmuted, the next sentence is a line again.
    await tap('.call-key.mute');
    await page.until(`window.CapriCall.stats().status === 'listening'`, 'listening again');
    const third = await nextLine(3, 'a line after unmuting', (seconds + 4) * 1000);

    // Her voice right away, longer than his next sentence: nothing goes while it plays from the speaker.
    await answer(third, 'Hey Capri, what is the weather like today?', 'Still clear skies, and a light breeze this afternoon if you head out.');
    const voiceAt = Date.now();
    const VOICE_SECONDS = 9;
    for (let n = 0; n < VOICE_SECONDS * 4; n++) await pc.send(device.id, { type: 'call.voice', n, rate: 24000, pcm: R.b64u(L.pcmBytes(L.tone(24000, 0.25, 220 + (n % 4) * 40))) });
    await page.until(`window.CapriCall.stats().status === 'talking'`, 'her talking', 4000);
    assert.equal(await text('.call-word'), 'Talking');
    await size(375, 667);
    await shot('call-talking-375');
    assert.deepEqual(await page.run(SPILLS), [], 'nothing spills at 375 x 667');
    await size(390, 844);
    await shot('call-talking-390');
    await page.until(`window.CapriCall.stats().playing === 0`, 'her voice to finish', (VOICE_SECONDS + 4) * 1000);
    const quietAt = Date.now();
    const during = parts.filter(part => part.at > voiceAt + 300 && part.at < quietAt);
    assert.equal(during.length, 0, `nothing sent while her voice played (${during.length} parts)`);
    const playedFor = (quietAt - voiceAt) / 1000;
    assert.ok(playedFor > VOICE_SECONDS - 1, `her voice played out: ${playedFor.toFixed(1)} s`);
    assert.equal((await stats()).voiceParts, VOICE_SECONDS * 4);
    // Once she's done he's heard again.
    const fourth = await nextLine(4, 'a line after she finished', (seconds + 6) * 1000);
    assert.ok(fourth.startAt > quietAt, 'that line started after her voice');
    await answer(fourth, 'And can you open my music, please?', 'Done.');

    // The screen key hides and shows the mini player; shrinking keeps the call on in a bar over the chat.
    await tap('.call-screen');
    await page.until(`document.querySelector('.mini').hidden`, 'the mini player gone');
    await tap('.call-screen');
    await page.until(`!document.querySelector('.mini').hidden`, 'the mini player back');
    await tap('.call-shrink');
    await page.until(`document.querySelector('.call-room').hidden && !document.querySelector('.callbar').hidden`, 'the call bar');
    await shot('call-bar-390');
    assert.deepEqual(await page.run(SPILLS), [], 'nothing spills in the call bar');
    await tap('.call-back');
    await page.until(`!document.querySelector('.call-room').hidden`, 'the call screen again');

    // Hanging up: the PC hears call.stop, the mic is let go, and the screen says the call ended, then closes.
    await tap('.call-key.end');
    await page.until(`window.CapriCall.stats().state === 'ended'`, 'the call ended');
    assert.equal(await text('.call-word'), 'Call ended');
    await shot('call-ended-390');
    await page.until(`window.__micStreams.every(stream => stream.getTracks().every(track => track.readyState === 'ended'))`, 'the mic let go');
    assert.equal((await stats()).micLive, false);
    await page.until(`window.CapriCall.state === 'off' && document.querySelector('.call-room').hidden`, 'the call screen closing', 4000);
    await wait(400);
    assert.ok(asked.includes('call.stop'), 'the PC heard the hang up');
    assert.deepEqual(problems, []);
    const after = await stats();
    console.log(`call: ${lines.length} lines (${lines.map(line => seconds_(line).toFixed(2) + ' s').join(', ')}), ${after.blips} dropped blips, ${after.voiceParts} voice parts, floor ${after.floor} dB, ${((Date.now() - startedAt) / 1000).toFixed(0)} s`);
  } finally {
    reading = false;
    if (reader) await Promise.race([reader, wait(1500)]);
    if (page) await page.close();
    pc.close();
    await site.close();
    await relay.stop();
    rmSync(folder, { recursive: true, force: true });
  }
});

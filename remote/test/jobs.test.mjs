import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { chromePath, launch } from './lib/cdp.mjs';
import { FakePc } from './lib/fakes.mjs';
import { HOME_SCREEN, IPHONES, SAFARI, misfitsOf, setPhone } from './lib/fit.mjs';
import * as R from './lib/capri-remote.mjs';
import { startRelay } from './lib/relay-dev.mjs';
import { earlyAccess, startSite } from './lib/site.mjs';

// JOBS (PROTOCOL.md 8.4) on a phone paired with a stand-in PC: the tile's mark, live steps, Allow, Deny, Stop, Recent,
// the call's round key, and the page at every phone size. CAPRI_PHONE_SHOTS=<folder> keeps a screenshot of each.
const SITE_PORT = 8802, CHROME_PORT = 8804;
const SHOTS = process.env.CAPRI_PHONE_SHOTS || '';

// A second of quiet for the fake mic, so the call hears no lines of his.
const quietWav = folder => {
  const path = join(folder, 'quiet.wav'), count = 16000;
  const header = Buffer.alloc(44);
  header.write('RIFF', 0, 'ascii');
  header.writeUInt32LE(36 + count * 2, 4);
  header.write('WAVEfmt ', 8, 'ascii');
  header.writeUInt32LE(16, 16);
  header.writeUInt16LE(1, 20);
  header.writeUInt16LE(1, 22);
  header.writeUInt32LE(16000, 24);
  header.writeUInt32LE(32000, 28);
  header.writeUInt16LE(2, 32);
  header.writeUInt16LE(16, 34);
  header.write('data', 36, 'ascii');
  header.writeUInt32LE(count * 2, 40);
  writeFileSync(path, Buffer.concat([header, Buffer.alloc(count * 2)]));
  return path;
};

for (const jobs of [true, false]) {
  test(jobs ? 'JOBS: live steps, answers, Stop, Recent, the call key and phone layouts' : 'JOBS stays Soon without the feature', { skip: !chromePath() && 'Chrome is not installed', timeout: 180000 }, async () => {
    const folder = mkdtempSync(join(tmpdir(), 'capri-jobs-'));
    const relay = await startRelay();
    const site = await startSite(SITE_PORT);
    const pc = new FakePc(relay);
    let page = null;
    try {
      await pc.connect();
      const qr = await pc.showCode();
      const job = await pc.jobState({ id: 'live-job', task: 'Open Steam and launch Helldivers 2' });
      await pc.jobStep(job.id, { n: 1, tool: 'open_app', label: 'Opened Steam' });
      await pc.jobStep(job.id, { n: 2, tool: 'click', label: 'Clicking Play', done: false });
      page = await launch({ port: CHROME_PORT, args: ['--use-fake-ui-for-media-stream', '--use-fake-device-for-media-stream', '--use-file-for-fake-audio-capture=' + quietWav(folder), '--mute-audio', '--disable-audio-output'] });
      const errors = [];
      page.on((method, data) => { if (method === 'Runtime.exceptionThrown') errors.push(data.exceptionDetails.text); });
      const click = selector => page.run(`document.querySelector(${JSON.stringify(selector)}).click()`);
      const text = selector => page.run(`document.querySelector(${JSON.stringify(selector)}).textContent`);
      const shot = async name => { if (SHOTS) { mkdirSync(SHOTS, { recursive: true }); await page.shot(join(SHOTS, name + '.png')); } };
      // A real tap, as a finger would: the call needs one for its sound and the mic.
      const tap = async selector => {
        const at = await page.run(`(() => { const el = document.querySelector(${JSON.stringify(selector)}); if (!el || !el.getClientRects().length) throw new Error('no ${selector} on screen'); const box = el.getBoundingClientRect(); return { x: box.left + box.width / 2, y: box.top + box.height / 2 }; })()`);
        await page.send('Input.dispatchMouseEvent', { type: 'mouseMoved', x: at.x, y: at.y });
        await page.send('Input.dispatchMouseEvent', { type: 'mousePressed', x: at.x, y: at.y, button: 'left', clickCount: 1 });
        await page.send('Input.dispatchMouseEvent', { type: 'mouseReleased', x: at.x, y: at.y, button: 'left', clickCount: 1 });
      };
      // The phone's messages as the PC reads them, serving the lists it asks for on the way.
      const next = async type => {
        for (;;) {
          const item = await pc.conn.next(10000);
          if (item.text) continue;
          const { device: from, envelope } = R.splitPcFrame(item.data);
          const message = await pc.devices.get(from).session.open(envelope);
          if (message.type === type) return message;
          if (message.type === 'job.list') await pc.jobsMessage(from, message);
        }
      };
      const onTop = selector => page.run(`(() => { const hit = document.elementFromPoint(innerWidth / 2, innerHeight / 2); return !!(hit && hit.closest(${JSON.stringify(selector)})); })()`);
      await page.send('Page.addScriptToEvaluateOnNewDocument', { source: earlyAccess() });
      await page.go(`http://127.0.0.1:${SITE_PORT}/?relay=${relay.ws}${qr.slice(qr.indexOf('#'))}`);
      const device = await pc.allow(await pc.pairRequest());
      await pc.conn.json('joined', 10000);
      await pc.acceptHello(jobs ? ['chat', 'call', 'watch', 'jobs'] : ['chat']);
      await page.until(`document.querySelector('#app').dataset.link === 'connected'`, 'the paired PC');
      if (!jobs) {
        assert.equal(await page.run(`document.querySelector('#f-jobs').getAttribute('aria-disabled')`), 'true');
        await click('#f-jobs');
        assert.equal(await page.run(`document.querySelector('#jobs').hidden`), true);
        assert.equal(await page.run(`document.querySelector('#f-jobs .soon').hidden`), false);
        await pc.send(device.id, { type: 'job.state', ...job });
        assert.equal(await page.run(`CapriJobs.open()`), false);
        assert.equal(await page.run(`document.querySelector('#f-jobs').dataset.job || ''`), '');
        return;
      }

      // The tile is marked while a job runs, and opens the page with the steps replayed at hello.
      await page.until(`document.querySelector('#f-jobs').dataset.job === 'running'`, 'the JOBS tile marked while a job runs');
      assert.equal(await page.run(`document.querySelector('#f-jobs .soon').hidden`), true);
      assert.equal(await page.run(`document.querySelector('#f-jobs').getAttribute('aria-label')`), 'Jobs: one running');
      assert.equal(await page.run(`getComputedStyle(document.querySelector('#f-jobs'), '::after').content`), '""', 'the tile shows its mark');
      await setPhone(page, IPHONES[1], SAFARI);
      await shot('jobs-tile-running');
      await click('#f-jobs');
      assert.deepEqual(await next('job.list'), { type: 'job.list' });
      await pc.jobsMessage(device.id, { type: 'job.list' });
      await page.until(`document.querySelectorAll('.job-step').length === 2`, 'the steps replayed at hello');
      assert.equal(await text('.job-task'), job.task);
      assert.equal(await text('.job-step[data-n="1"]'), 'Opened Steam');
      assert.equal(await page.run(`document.querySelector('.job-step[data-n="2"]').dataset.state`), 'running');
      assert.equal(await page.run(`document.activeElement === document.querySelector('.jobs-close')`), true, 'focus goes to Close');
      await shot('jobs-running');
      await pc.jobStep(job.id, { n: 2, label: 'Clicked Play', done: true });
      await page.until(`document.querySelector('.job-step[data-n="2"]').dataset.state === 'done'`, 'the finished step');
      assert.equal(await page.run(`document.querySelectorAll('.job-step').length`), 2);

      // Her question, with Allow and Deny under it.
      await pc.jobState({ ...job, state: 'asking', ask: 'Allow sending this message?' });
      await page.until(`!document.querySelector('.job-answers').hidden`, 'Allow and Deny');
      assert.equal(await text('.job-question'), 'Allow sending this message?');
      assert.equal(await page.run(`document.querySelector('#f-jobs').dataset.job`), 'asking');
      assert.equal(await page.run(`!!document.querySelector('.job-question').closest('.job-controls')`), true, 'her question sits with the keys that answer it');
      await shot('jobs-asking');
      await click('.job-allow');
      const yes = await next('job.answer');
      assert.deepEqual(yes, { id: job.id, yes: true, type: 'job.answer' });
      assert.equal(await page.run(`document.querySelector('.job-allow').disabled`), true);
      await pc.jobsMessage(device.id, yes);
      await page.until(`document.querySelector('.job-state').textContent === 'Running' && document.querySelector('.job-answers').hidden`, 'her answer accepted');

      await pc.jobState({ id: job.id, state: 'asking', ask: 'Allow this purchase?' });
      await page.until(`!document.querySelector('.job-answers').hidden`, 'her next question');
      await click('.job-deny');
      const no = await next('job.answer');
      assert.deepEqual(no, { id: job.id, yes: false, type: 'job.answer' });
      await pc.jobsMessage(device.id, no);
      await page.until(`document.querySelector('.job-live').hidden`, 'the denied job ending');
      assert.equal(await page.run(`document.querySelector('#f-jobs').dataset.job`), '');

      // Stop, then Recent.
      const second = await pc.jobState({ id: 'second-job', task: 'Open Discord', started: Date.now() });
      await page.until(`!document.querySelector('.job-live').hidden && document.querySelector('.job-task').textContent === 'Open Discord'`, 'the next job');
      await click('.job-stop');
      const stopped = await next('job.stop');
      assert.deepEqual(stopped, { id: second.id, type: 'job.stop' });
      await pc.jobsMessage(device.id, stopped);
      await page.until(`document.querySelector('.job-live').hidden`, 'Stop ending the job');
      await pc.jobsMessage(device.id, { type: 'job.list' });
      await page.until(`document.querySelectorAll('.job-recent-item').length === 2`, 'Recent');
      assert.match(await text('.job-recent'), /Open Discord[\s\S]*Stopped/);
      assert.match(await text('.job-recent'), /2 steps/);
      assert.match(await text('.job-recent'), /Clicked Play/);
      assert.ok(await page.run(`document.querySelector('.job-recent time').dateTime`));
      await shot('jobs-recent');

      // The longest a PC may send: the title stops at three lines, the steps scroll and follow the newest, and her
      // question and Stop stay on screen at every size.
      const long = await pc.jobState({ id: 'long-job', task: 'Open a game and check the settings. '.repeat(6).slice(0, 200), started: Date.now(), state: 'asking', ask: 'Allow this action? '.repeat(12).slice(0, 200) });
      for (let n = 1; n <= 36; n++) await pc.jobStep(long.id, { n, label: ('Clicked the settings button ' + n + ' ').repeat(4).slice(0, 120) });
      await page.until(`document.querySelector('.job-step[data-n="36"]')`, 'the last step');
      assert.equal(await page.run(`document.querySelectorAll('.job-step').length`), 30);
      assert.equal(await page.run(`Math.abs(document.querySelector('.job-steps').scrollHeight - document.querySelector('.job-steps').clientHeight - document.querySelector('.job-steps').scrollTop) < 2`), true);
      for (const mode of [SAFARI, HOME_SCREEN]) for (const size of IPHONES) {
        await setPhone(page, size, mode);
        assert.deepEqual(await misfitsOf(page), [], `JOBS at ${size.join('x')} (${mode.name})`);
        const seen = await page.run(`(() => { const frame = document.querySelector('#jobs').getBoundingClientRect(); const inside = node => { const box = node.getBoundingClientRect(); return box.height > 0 && box.top >= frame.top && box.bottom <= frame.bottom; }; return { stop: inside(document.querySelector('.job-stop')), question: inside(document.querySelector('.job-question')), title: document.querySelector('.job-task').getBoundingClientRect().height < 80 }; })()`);
        assert.deepEqual(seen, { stop: true, question: true, title: true }, `the question, Stop and a three-line title at ${size.join('x')} (${mode.name})`);
        await shot(`jobs-long-${mode.name}-${size.join('x')}`);
      }

      // On a call: a round key next to the screen key while a job runs, and the page opens over the call.
      await click('.jobs-close');
      await page.until(`document.querySelector('#jobs').hidden`, 'Close');
      await setPhone(page, IPHONES[1], SAFARI);
      await tap('#f-call');
      await next('call.start');
      await pc.send(device.id, { type: 'call.state', on: true, voice: true, ears: true });
      await page.until(`window.CapriCall.stats().status === 'listening'`, 'the call listening');
      await page.until(`!document.querySelector('.call-top .call-jobs').hidden`, 'the call key while a job runs');
      assert.equal(await page.run(`document.querySelector('.call-top .call-jobs').previousElementSibling.classList.contains('call-screen')`), true, 'next to the screen key');
      assert.equal(await page.run(`document.querySelector('.call-top .call-jobs').dataset.job`), 'asking');
      assert.equal(await page.run(`getComputedStyle(document.querySelector('.call-top .call-jobs')).borderRadius`), '50%', 'round like the call keys');
      await shot('jobs-call-key');
      await tap('.call-top .call-jobs');
      await next('job.list');
      await page.until(`!document.querySelector('#jobs').hidden`, 'JOBS from the call');
      assert.equal(await onTop('#jobs'), true, 'JOBS opens over the call');
      await shot('jobs-over-call');
      await click('.jobs-close');
      await page.until(`document.querySelector('#jobs').hidden`, 'back to the call');
      assert.equal(await onTop('.call-room'), true, 'the call is back');
      await pc.jobState({ id: long.id, state: 'done', ended: Date.now() });
      await page.until(`document.querySelector('.call-top .call-jobs').hidden`, 'the call key gone once the job is over');
      assert.deepEqual(errors, []);
    } finally {
      if (page) await page.close();
      pc.close();
      await site.close();
      await relay.stop();
      rmSync(folder, { recursive: true, force: true });
    }
  });
}

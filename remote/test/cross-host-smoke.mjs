import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { once } from 'node:events';
import { existsSync, mkdtempSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { createServer } from 'node:http';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';
import { chromePath, launch } from './lib/cdp.mjs';
import { setPhone } from './lib/fit.mjs';
import { freePort, startRelay } from './lib/relay-dev.mjs';
import { earlyAccess, ROOT, startSite } from './lib/site.mjs';

const [exe, ...rest] = process.argv.slice(2);
if (!exe || rest.length > 1 || !existsSync(resolve(exe))) throw new Error('Usage: node remote/test/cross-host-smoke.mjs <built Capri executable> [new smoke output folder]');
if (!chromePath()) throw new Error('Install Chrome or set CHROME_PATH to a Chromium executable.');
const keep = rest[0] && resolve(rest[0]);
if (keep && existsSync(keep)) throw new Error('Choose a new smoke output folder.');
const temporary = mkdtempSync(join(tmpdir(), 'capri-cross-remote-'));
const work = keep || temporary;
const exchange = join(work, 'remote-socket');
const report = join(work, 'smoke-test.txt');
const prompt = 'Hello from the browser smoke test ' + randomBytes(6).toString('hex');
const reply = 'I heard you from Capri.';
let brainHeard = false;
const brain = createServer(async (request, response) => {
  if (request.method !== 'POST' || request.url !== '/v1/chat/completions') { response.writeHead(404).end(); return; }
  try {
    let raw = '';
    for await (const part of request) raw += part;
    const body = JSON.parse(raw);
    if (body.model !== 'remote-socket-test' || !body.messages.some(m => m.role === 'user' && JSON.stringify(m.content).includes(prompt))) {
      response.writeHead(400).end();
      return;
    }
    brainHeard = true;
    response.writeHead(200, { 'content-type': 'text/event-stream' });
    for (const content of ['I heard you', ' from Capri.']) {
      response.write('data: ' + JSON.stringify({ choices: [{ index: 0, delta: { content } }] }) + '\n\n');
      await delay(40);
    }
    response.end('data: {"choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}\n\ndata: [DONE]\n\n');
  } catch {
    response.destroy();
  }
});

let relay, site, page, desktop, exited, outcome;
const file = name => join(exchange, name);
const signal = (name, value = '') => {
  writeFileSync(file(name + '.tmp'), value);
  renameSync(file(name + '.tmp'), file(name));
};
const read = async (name, pattern) => {
  const deadline = Date.now() + 30000;
  for (;;) {
    if (existsSync(file(name))) {
      const value = readFileSync(file(name), 'utf8');
      if (pattern.test(value)) return value;
    }
    if (outcome || Date.now() >= deadline) throw new Error('Desktop did not provide ' + name + (existsSync(report) ? ':\n' + readFileSync(report, 'utf8').split('\n').filter(line => line.startsWith('FAIL')).join('\n') : '.'));
    await delay(50);
  }
};

try {
  relay = await startRelay();
  site = await startSite(0);
  brain.listen(0, '127.0.0.1');
  await once(brain, 'listening');
  page = await launch({ port: await freePort() });
  await page.send('Page.addScriptToEvaluateOnNewDocument', { source: earlyAccess() });
  await setPhone(page, [390, 844]);
  desktop = spawn(resolve(exe), ['--smoke', work], {
    cwd: ROOT,
    env: { ...process.env, CAPRI_SMOKE_REMOTE_RELAY: relay.ws, CAPRI_SMOKE_REMOTE_PROVIDER: `http://127.0.0.1:${brain.address().port}` },
    stdio: 'ignore',
  });
  exited = new Promise(done => {
    desktop.once('error', error => done(outcome = { error }));
    desktop.once('exit', (code, sig) => done(outcome = { code, sig }));
  });
  const qr = await read('qr', /^https:\/\/capriai\.app\/remote\/#r=[\w-]{22}&c=[\w-]{22}&k=[\w-]{43}$/);
  await page.go(`http://127.0.0.1:${site.port}/?ios=safari&relay=${relay.ws}${new URL(qr).hash}`);
  await page.until(`document.querySelector('#app').dataset.screen === 'where'`, 'the phone pairing choice');
  await page.run(`document.querySelector('#where-here').click()`);
  const code = await read('code', /^\d{3} \d{3}$/);
  const sameCode = await page.until(`/^\\d{3} \\d{3}$/.test(document.querySelector('#wait-code').textContent) && document.querySelector('#wait-code').textContent`, 'the phone confirmation code');
  assert.ok(sameCode === code, 'The phone and desktop displayed different confirmation codes.');
  signal('allow', code);
  await page.until(`document.querySelector('#app').dataset.link === 'connected'`, 'the paired phone session');
  await page.run(`(() => { const input = document.querySelector('#msg'); input.value = ${JSON.stringify(prompt)}; input.dispatchEvent(new Event('input', { bubbles: true })); document.querySelector('#send').click(); })()`);
  await page.until(`Array.from(document.querySelectorAll('#log .capri.done .words')).some(node => node.textContent === ${JSON.stringify(reply)})`, 'the complete streamed reply', 12000);
  assert.ok(brainHeard, 'The browser message did not reach the desktop brain.');
  signal('replied');
  console.log('PASS browser pairing, matching codes, encrypted session and desktop chat round trip');
  const done = await Promise.race([exited, delay(15000, null, { ref: false })]);
  const lines = existsSync(report) ? readFileSync(report, 'utf8') : '';
  assert.ok(done && done.code === 0, 'The desktop Remote smoke run did not exit cleanly.\n' + lines);
  assert.match(lines, /^PASS Remote socket:/m);
  assert.doesNotMatch(lines, /^FAIL/m);
  console.log('PASS desktop Remote smoke and paired phone persistence');
} finally {
  if (desktop && !outcome) { desktop.kill(); await exited; }
  if (page) await page.close();
  if (site) await site.close();
  if (brain.listening) await new Promise(done => { brain.close(done); brain.closeAllConnections(); });
  if (relay) await relay.stop();
  rmSync(exchange, { recursive: true, force: true });
  rmSync(temporary, { recursive: true, force: true });
}

// Starts the relay under `wrangler dev --local` on a free port, with no network calls, and stops it again.

import { spawn, spawnSync } from 'node:child_process';
import { mkdtempSync, rmSync } from 'node:fs';
import { createRequire } from 'node:module';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

export const RELAY_DIR = join(dirname(fileURLToPath(import.meta.url)), '..', '..', 'relay');

const freePort = () =>
  new Promise((resolve, reject) => {
    const server = createServer();
    server.unref();
    server.on('error', reject);
    server.listen(0, '127.0.0.1', () => {
      const { port } = server.address();
      server.close(() => resolve(port));
    });
  });

const wranglerBin = () => {
  try {
    const require = createRequire(join(RELAY_DIR, 'package.json'));
    return join(dirname(require.resolve('wrangler/package.json')), 'bin', 'wrangler.js');
  } catch {
    throw new Error('wrangler is not installed. Run: npm --prefix remote/relay ci');
  }
};

export async function startRelay(vars = {}) {
  const port = await freePort();
  const inspector = await freePort();
  const state = mkdtempSync(join(tmpdir(), 'capri-relay-'));
  const args = [
    wranglerBin(), 'dev', '--local', '--ip', '127.0.0.1', '--port', String(port), '--inspector-port', String(inspector),
    '--persist-to', state, '--show-interactive-dev-session=false', '--log-level', 'warn',
  ];
  for (const [key, value] of Object.entries(vars)) args.push('--var', `${key}:${value}`);
  const child = spawn(process.execPath, args, {
    cwd: RELAY_DIR,
    env: {
      ...process.env,
      WRANGLER_SEND_METRICS: 'false',
      CLOUDFLARE_CF_FETCH_ENABLED: 'false',
      DO_NOT_TRACK: '1',
      WRANGLER_NO_SKILLS_UPDATE_PROMPTS: 'true',
      NO_COLOR: '1',
      FORCE_COLOR: '0',
    },
    stdio: ['ignore', 'pipe', 'pipe'],
    detached: process.platform !== 'win32',
  });
  let log = '';
  child.stdout.on('data', (chunk) => (log += chunk));
  child.stderr.on('data', (chunk) => (log += chunk));

  // On Windows workerd can hold the state files for a moment after it is killed, so keep trying.
  const removeState = async () => {
    for (let attempt = 0; attempt < 25; attempt++) {
      try {
        rmSync(state, { recursive: true, force: true });
        return;
      } catch {
        await new Promise((resolve) => setTimeout(resolve, 200));
      }
    }
  };

  let stopped;
  const stop = () =>
    (stopped ??= new Promise((resolve) => {
      const done = () => removeState().then(resolve);
      if (child.exitCode !== null || child.signalCode !== null) return done();
      child.once('exit', done);
      if (process.platform === 'win32') spawnSync('taskkill', ['/pid', String(child.pid), '/T', '/F'], { stdio: 'ignore' });
      else {
        try {
          process.kill(-child.pid, 'SIGTERM');
        } catch {
          child.kill('SIGTERM');
        }
      }
    }));

  const base = `http://127.0.0.1:${port}`;
  const deadline = Date.now() + 90000;
  for (;;) {
    if (child.exitCode !== null) throw new Error('wrangler dev exited early:\n' + log);
    try {
      const response = await fetch(base + '/');
      if (response.ok && (await response.text()).startsWith('Capri Remote relay')) break;
    } catch {}
    if (Date.now() > deadline) {
      await stop();
      throw new Error('wrangler dev did not start in time:\n' + log);
    }
    await new Promise((resolve) => setTimeout(resolve, 200));
  }
  return { base, ws: base.replace(/^http/, 'ws'), port, log: () => log, stop };
}

// Headless Chrome over the DevTools protocol, for the phone page tests: a fresh profile each run, one page, and the few
// calls the tests need. Node 22+ has the WebSocket it talks over.

import { spawn } from 'node:child_process';
import { existsSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

export const chromePath = () => [
  process.env.CHROME_PATH,
  'C:/Program Files/Google/Chrome/Application/chrome.exe',
  'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe',
  '/usr/bin/google-chrome',
  '/usr/bin/chromium',
  '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome'
].find(path => path && existsSync(path)) || null;

const wait = ms => new Promise(resolve => setTimeout(resolve, ms));

// args: more switches, like the call test's fake mic.
export const launch = async ({ port, width = 390, height = 844, args = [] }) => {
  const profile = mkdtempSync(join(tmpdir(), 'capri-phone-chrome-'));
  const chrome = spawn(chromePath(), ['--headless=new', '--remote-debugging-port=' + port, '--user-data-dir=' + profile, '--no-first-run', '--no-default-browser-check', '--disable-extensions', '--disable-background-networking', '--disable-component-update', '--disable-sync', '--window-size=' + width + ',' + height, ...(process.platform === 'win32' ? ['--disable-gpu', '--no-sandbox'] : []), ...args, 'about:blank'], { stdio: 'ignore' });
  let target = null;
  for (let tries = 0; tries < 100 && !target; tries++) {
    await wait(100);
    try {
      const list = await (await fetch('http://127.0.0.1:' + port + '/json/list')).json();
      target = list.find(item => item.type === 'page');
    } catch {}
  }
  if (!target) { chrome.kill(); throw new Error('Chrome never opened its DevTools port'); }
  const socket = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
  let next = 0;
  const waiting = new Map();
  const listeners = [];
  socket.onmessage = event => {
    const message = JSON.parse(event.data);
    if (message.id && waiting.has(message.id)) {
      const { resolve, reject } = waiting.get(message.id);
      waiting.delete(message.id);
      if (message.error) reject(new Error(message.error.message)); else resolve(message.result);
    } else if (message.method) for (const listen of listeners) listen(message.method, message.params);
  };
  socket.onclose = () => { for (const item of waiting.values()) item.reject(new Error('Chrome DevTools socket closed')); waiting.clear(); };
  const send = (method, params = {}) => new Promise((resolve, reject) => {
    const id = ++next;
    waiting.set(id, { resolve, reject });
    socket.send(JSON.stringify({ id, method, params }));
  });
  await send('Page.enable');
  await send('Runtime.enable');
  await send('Network.enable');
  await send('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: 2, mobile: false });
  const page = {
    send,
    on: listen => listeners.push(listen),
    // A script's value, awaited when it's a promise. Throws what the page threw.
    run: async expression => {
      const result = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
      if (result.exceptionDetails) throw new Error((result.exceptionDetails.exception && result.exceptionDetails.exception.description) || result.exceptionDetails.text);
      return result.result.value;
    },
    until: async (expression, what, ms = 8000) => {
      const end = Date.now() + ms;
      for (;;) {
        let value = null;
        try { value = await page.run(expression); } catch {}
        if (value) return value;
        if (Date.now() > end) throw new Error('Timed out waiting for ' + what);
        await wait(60);
      }
    },
    go: async url => { await send('Page.navigate', { url }); await wait(300); },
    shot: async path => { const { data } = await send('Page.captureScreenshot', { format: 'png' }); writeFileSync(path, Buffer.from(data, 'base64')); },
    close: async () => {
      try { await send('Browser.close'); } catch {}
      socket.close();
      chrome.kill();
      await wait(300);
      try { rmSync(profile, { recursive: true, force: true }); } catch {}
    }
  };
  return page;
};

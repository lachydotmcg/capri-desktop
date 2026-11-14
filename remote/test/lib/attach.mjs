import assert from 'node:assert/strict';
import * as R from './capri-remote.mjs';
import { earlyAccess } from './site.mjs';

export const click = (page, selector) => page.run(`document.querySelector(${JSON.stringify(selector)}).click()`);
export const type = (page, text) => page.run(`(() => { const el = document.querySelector('#msg'); el.value = ${JSON.stringify(text)}; el.dispatchEvent(new Event('input', { bubbles: true })); })()`);
export const preview = page => page.run(`document.querySelector('#attach-preview img').src`);
export const line = (page, words) => page.until(`document.body.innerText.includes(${JSON.stringify(words)})`, words);
export const picture = (page, invalid = false) => page.run(`(async () => {
  const canvas = document.createElement('canvas'); canvas.width = 2600; canvas.height = 1800;
  const ctx = canvas.getContext('2d'); const image = ctx.createImageData(canvas.width, canvas.height);
  let seed = 42; for (let i = 0; i < image.data.length; i += 4) { seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0; image.data[i] = seed >>> 24; image.data[i+1] = seed >>> 16; image.data[i+2] = seed >>> 8; image.data[i+3] = 255; }
  ctx.putImageData(image, 0, 0);
  const blob = ${invalid ? "new Blob(['not a picture'], { type: 'image/jpeg' })" : "await new Promise(resolve => canvas.toBlob(resolve, 'image/png'))"};
  const transfer = new DataTransfer(); transfer.items.add(new File([blob], 'photo.png', { type: blob.type }));
  const input = document.querySelector('#room-attach-input'); input.files = transfer.files; input.dispatchEvent(new Event('change', { bubbles: true }));
})()`);

export async function fakePc(page, features = ['chat', 'pictures']) {
  const key = R.randomBytes(32), id = R.randomBytes(16), pairing = R.randomBytes(32);
  const room = R.b64u(R.randomBytes(16)), code = R.b64u(R.randomBytes(16));
  const got = []; let session; let failure; let current = 1; let chain = Promise.resolve();
  const receive = (socket, data, binary = false) => page.run(`window.attachSockets[${socket}].onmessage({ data: ${binary ? `Uint8Array.from(${JSON.stringify([...data])}).buffer` : JSON.stringify(JSON.stringify(data))} })`);
  const send = async message => receive(current, await session.seal(message), true);
  await page.send('Runtime.addBinding', { name: 'attachTransport' });
  page.on((method, params) => {
    if (method !== 'Runtime.bindingCalled' || params.name !== 'attachTransport') return;
    chain = chain.then(async () => {
      const { socket, text, bytes } = JSON.parse(params.payload);
      if (text) {
        const frame = JSON.parse(text);
        if (frame.t === 'pair') {
          const channel = new R.Channel(await R.pairKeys(pairing, R.fromB64u(frame.nonce)), R.KIND.pair, 'pc');
          assert.equal((await channel.open(R.fromB64u(frame.env))).type, 'pair-request');
          const env = await channel.seal({ type: 'pair-allow', device: R.b64u(id), token: R.b64u(R.randomBytes(32)), key: R.b64u(key), name: 'Test PC' });
          await receive(socket, { t: 'pair-reply', env: R.b64u(env) });
          await page.until("Object.values(localStorage).some(value => value.includes('\"allowed\":true'))", 'pair saved');
          await page.run(`window.attachSockets[${socket}].onclose({ code: 4001, reason: '' })`);
        } else if (frame.t === 'auth') await receive(socket, { t: 'ready', pc: true });
        else if (frame.t === 'ping') await receive(socket, { t: 'pong' });
      } else if (bytes[1] === R.KIND.hello) {
        current = socket;
        const channel = new R.Channel(await R.helloKeys(key, id), R.KIND.hello, 'pc');
        const hello = await channel.open(Uint8Array.from(bytes)); const nonce = R.randomBytes(16);
        session = new R.Channel(await R.sessionKeys(key, R.fromB64u(hello.nonce), nonce), R.KIND.session, 'pc');
        await receive(socket, await channel.seal({ type: 'hello-ok', v: 1, nonce: R.b64u(nonce), echo: hello.nonce, name: 'Test PC', app: 'capri/test', features }), true);
      } else got.push(await session.open(Uint8Array.from(bytes)));
    }).catch(error => { failure = error; });
  });
  await page.send('Page.addScriptToEvaluateOnNewDocument', { source: earlyAccess() + `
    window.attachSockets = [];
    window.WebSocket = class {
      static OPEN = 1;
      constructor() { this.readyState = 1; this.id = attachSockets.push(this) - 1; setTimeout(() => this.onopen && this.onopen(), 0); }
      send(data) { attachTransport(JSON.stringify({ socket: this.id, ...(typeof data === 'string' ? {text:data} : {bytes:Array.from(new Uint8Array(data))}) })); }
      close() { this.readyState = 3; }
    };
  ` });
  return { hash: `#r=${room}&c=${code}&k=${R.b64u(pairing)}`, got, send, async next() {
    const end = Date.now() + 10000;
    while (!got.some(message => message.type === 'chat.send')) { if (failure) throw failure; if (Date.now() > end) throw new Error('No encrypted chat.send'); await new Promise(resolve => setTimeout(resolve, 50)); }
    return got.splice(got.findIndex(message => message.type === 'chat.send'), 1)[0];
  } };
}

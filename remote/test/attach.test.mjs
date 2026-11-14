import { test } from 'node:test';
import assert from 'node:assert/strict';
import { chromePath, launch } from './lib/cdp.mjs';
import { startSite, earlyAccess } from './lib/site.mjs';
import { GOOD, startProvider } from './lib/fake-provider.mjs';
import { jpegSize } from './lib/live.mjs';
import * as R from './lib/capri-remote.mjs';
import { click, type, picture, preview, line, fakePc } from './lib/attach.mjs';

test('phone attachments cross an encrypted PC session and stay out of saved chats', { skip: !chromePath(), timeout: 120000 }, async () => {
  const site = await startSite(8982); let page; let started = 0, closed = 0;
  try {
    page = await launch({ port: 9492, args: ['--disable-gpu', '--no-sandbox'] }); started++; await page.send('Network.disable');
    const pc = await fakePc(page);
    await page.go('http://127.0.0.1:8982/?relay=ws://127.0.0.1:8982' + pc.hash);
    await page.until("document.querySelector('#app').dataset.link === 'connected'", 'encrypted PC session');
    await picture(page, true); await line(page, "That isn't a picture I can send.");
    await picture(page); await page.until("document.querySelector('#attach-preview img')?.naturalWidth > 0", 'photo preview');
    const first = await preview(page);
    const image = R.fromB64u(first.split(',')[1].replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, ''));
    assert.ok(Math.max(...Object.values(jpegSize(image))) <= 1568);
    const bounds = await page.run("(() => { const p = document.querySelector('#attach-preview').getBoundingClientRect(), m = document.querySelector('#msg').getBoundingClientRect(); return { bottom:p.bottom, top:m.top }; })()");
    assert.ok(bounds.bottom <= bounds.top);
    assert.equal(await page.run("(() => { const button = document.querySelector('[aria-label=\"Remove picture\"]'), box = button.getBoundingClientRect(); return document.elementFromPoint(box.x + box.width / 2, box.y + box.height / 2) === button; })()"), true);
    await click(page, '[aria-label="Remove picture"]');
    assert.equal(await page.run('CapriAttach.hasPicture()'), false);
    await picture(page); await page.until('CapriAttach.hasPicture()', 'replacement picture');
    await type(page, 'a photo for you'); await click(page, '#send');
    const sent = await pc.next();
    assert.equal(sent.text, 'a photo for you'); assert.equal(sent.picture.mime, 'image/jpeg');
    assert.ok(R.encodeMessage(sent).length < 60000);
    assert.equal(R.b64u(R.fromB64u(sent.picture.data)), sent.picture.data);
    assert.ok(Math.max(...Object.values(jpegSize(R.fromB64u(sent.picture.data)))) <= 1568);
    const made = (await preview(page)).split(',')[1];
    assert.deepEqual(Buffer.from(sent.picture.data, 'base64url'), Buffer.from(made, 'base64'));
    await pc.send({ type: 'error', re: sent.id, code: 'busy', message: 'Try again.' });
    await page.until("document.querySelector('#msg').value === 'a photo for you' && CapriAttach.hasPicture()", 'failed send kept picture and text');
    await click(page, '#send'); const retry = await pc.next();
    assert.deepEqual(retry.picture, sent.picture);
    await pc.send({ type: 'chat.typing', re: retry.id, on: true });
    await pc.send({ type: 'chat.reply', re: retry.id, id: 'reply-one', text: 'Got it.', done: true });
    await page.until("!CapriAttach.hasPicture() && document.querySelector('#msg').value === ''", 'receipt cleared composer');
    await page.until("document.querySelector('.msg.you img')?.naturalWidth > 0", 'sent photo');
    await picture(page); await page.until('CapriAttach.hasPicture()', 'picture alone'); await click(page, '#send');
    const alone = await pc.next(); assert.equal(alone.text, ''); assert.ok(alone.picture.data);
    await pc.send({ type: 'chat.reply', re: alone.id, id: 'reply-two', text: 'Another photo.', done: true });
    await page.until('!CapriAttach.hasPicture()', 'picture-only receipt');
    await new Promise(resolve => setTimeout(resolve, 400));
    const stored = await page.run('JSON.stringify({local:{...localStorage},session:{...sessionStorage}})');
    assert.ok(!stored.includes(sent.picture.data)); assert.ok(!stored.includes(alone.picture.data)); assert.ok(!stored.includes('data:image/'));
    const indexed = await page.run(`(async () => { const saved = []; for (const info of await indexedDB.databases()) { const db = await new Promise((resolve, reject) => { const open = indexedDB.open(info.name); open.onsuccess = () => resolve(open.result); open.onerror = reject; }); for (const name of db.objectStoreNames) saved.push(await new Promise((resolve, reject) => { const get = db.transaction(name).objectStore(name).getAll(); get.onsuccess = () => resolve(get.result); get.onerror = reject; })); db.close(); } return JSON.stringify(saved); })()`);
    assert.ok(!indexed.includes(sent.picture.data)); assert.ok(!indexed.includes(alone.picture.data));
  } finally { if (page) { await page.close(); closed++; } await site.close(); console.log(`Attachment browsers started ${started}; closed ${closed}.`); }
});

test('phone models receive pictures only when they can see', { skip: !chromePath(), timeout: 60000 }, async () => {
  const site = await startSite(8984), provider = await startProvider(8985); let page;
  try {
    page = await launch({ port: 9494, args: ['--disable-gpu', '--no-sandbox'] }); await page.send('Network.disable');
    await page.send('Page.addScriptToEvaluateOnNewDocument', { source: earlyAccess() + `localStorage.setItem('capri-remote-mode','phone'); if (!localStorage.getItem('capri-remote-brain')) localStorage.setItem('capri-remote-brain', JSON.stringify({provider:'anthropic',key:${JSON.stringify(GOOD.anthropic)},model:'claude-haiku-5-5',models:['claude-haiku-5-5'],images:true}));` });
    const url = 'http://127.0.0.1:8984/?provider=http://127.0.0.1:8985&count=http://127.0.0.1:8985/count';
    await page.go(url); await page.until("document.querySelector('#app').dataset.link === 'connected'", 'phone brain');
    await picture(page); await page.until('CapriAttach.hasPicture()', 'phone picture');
    const made = Buffer.from((await preview(page)).split(',')[1], 'base64');
    await type(page, 'look at this'); await click(page, '#send');
    await page.until('!CapriAttach.hasPicture()', 'phone reply received');
    const request = provider.got.find(item => item.path === '/anthropic/messages');
    const turn = request.body.messages.at(-1);
    assert.ok(Array.isArray(turn.content));
    const image = turn.content.find(block => block.type === 'image');
    assert.equal(image.source.media_type, 'image/jpeg'); assert.deepEqual(Buffer.from(image.source.data, 'base64'), made);
    assert.ok(turn.content.some(block => block.type === 'text' && block.text === 'look at this'));
    await page.run("localStorage.setItem('capri-remote-brain', JSON.stringify({provider:'deepseek',key:'test-only-never-a-real-key',model:'deepseek-flash',models:['deepseek-flash'],images:false}))");
    await page.go(url); await page.until("document.querySelector('#app').dataset.link === 'connected'", 'blind phone brain');
    await picture(page); await page.until('CapriAttach.hasPicture()', 'blind brain picture');
    const count = provider.got.filter(item => /messages|completions/.test(item.path)).length;
    await type(page, 'keep this'); await click(page, '#send'); await line(page, "This brain can't see pictures.");
    assert.equal(await page.run('CapriAttach.hasPicture()'), true);
    assert.equal(await page.run("document.querySelector('#msg').value"), 'keep this');
    assert.equal(provider.got.filter(item => /messages|completions/.test(item.path)).length, count);
  } finally { if (page) await page.close(); await site.close(); await provider.close(); console.log('Native attachment browsers started 1; closed 1.'); }
});

test('old PCs open no picture picker and receive no picture', { skip: !chromePath(), timeout: 45000 }, async () => {
  const site = await startSite(8983); let page;
  try {
    page = await launch({ port: 9493, args: ['--disable-gpu', '--no-sandbox'] }); await page.send('Network.disable'); const pc = await fakePc(page, ['chat']);
    await page.go('http://127.0.0.1:8983/?relay=ws://127.0.0.1:8983' + pc.hash);
    await page.until("document.querySelector('#app').dataset.link === 'connected'", 'old PC');
    await page.run("window.pickers = 0; document.querySelector('#room-attach-input').click = () => pickers++");
    await click(page, '#room-camera'); await click(page, '#room-attach');
    await line(page, 'Update Capri on your PC to send pictures.');
    assert.equal(await page.run('pickers'), 0); assert.deepEqual(pc.got, []);
  } finally { if (page) await page.close(); await site.close(); console.log('Old-PC browsers started 1; closed 1.'); }
});

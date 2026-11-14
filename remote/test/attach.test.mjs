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
    assert.equal(await page.run("(() => { const n=document.querySelector('#attach-note').getBoundingClientRect(), b=document.querySelector('#bar').getBoundingClientRect();return n.bottom <= b.top && b.top - n.bottom < 20 && n.top >= 0; })()"), true);
    await page.until("(() => { const layer=document.querySelector('#her-layer'), bottom=parseFloat(getComputedStyle(layer).clipPath.split(' ')[2]);return layer.getBoundingClientRect().bottom - bottom <= document.querySelector('#attach-note').getBoundingClientRect().top; })()", 'sprite stays above the notice');
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
    assert.equal(await page.run('CapriAttach.busy'), false);
    await type(page, 'while she answers');
    assert.equal(await page.run("document.querySelector('#send').disabled"), false);
    await pc.send({ type: 'chat.reply', re: retry.id, id: 'reply-one', text: 'Got it.', done: true });
    await page.until("!CapriAttach.hasPicture() && document.querySelector('#msg').value === 'while she answers'", 'receipt keeps next draft');
    await type(page, '');
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
    await page.go('http://127.0.0.1:8982/?relay=ws://127.0.0.1:8982');
    await page.until("document.querySelector('#log').innerText.includes('[picture]')", 'saved picture placeholder');
    assert.equal(await page.run("[...document.querySelectorAll('.msg.you')].every(el => el.textContent.trim())"), true);
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

test('an unanswered picture releases the composer and keeps the draft', { skip: !chromePath() }, async () => {
  const site = await startSite(8990); let page;
  try {
    page = await launch({port:9500});
    const pc = await fakePc(page);
    await page.go('http://127.0.0.1:8990/?relay=ws://127.0.0.1:8990' + pc.hash);
    await page.until("document.querySelector('#app').dataset.link === 'connected'", 'PC link');
    await page.run('const timeout=window.setTimeout; window.setTimeout=(fn,ms,...args)=>{if(ms===15000){window.expireAttach=fn;ms=60000;}return timeout(fn,ms,...args);}');
    await picture(page); await page.until('CapriAttach.hasPicture()', 'unanswered picture');
    await type(page, 'keep my draft'); await click(page, '#send'); await pc.next();
    assert.equal(await page.run('CapriAttach.busy'), true);
    await page.run('expireAttach()');
    await line(page, 'No reply. Try sending again.');
    assert.equal(await page.run('!CapriAttach.busy && CapriAttach.hasPicture() && !document.querySelector("#send").disabled && document.querySelector("#msg").value === "keep my draft"'), true);
    await click(page, '#send'); const retry = await pc.next();
    await pc.send({type:'chat.reply',re:retry.id,id:'partial',text:'Looking',done:false});
    await page.until('!CapriAttach.busy && !CapriAttach.hasPicture()', 'first reply unlocks composer');
    await type(page, 'next message');
    assert.equal(await page.run('document.querySelector("#send").disabled'), false);
  } finally { if (page) await page.close(); await site.close(); }
});

test('resized bitmap decoding and the missing-API fallback honour EXIF orientation', { skip: !chromePath(), timeout: 60000 }, async () => {
  const site = await startSite(8991); let page;
  try {
    page = await launch({port:9501}); const pc = await fakePc(page);
    await page.go('http://127.0.0.1:8991/?relay=ws://127.0.0.1:8991' + pc.hash);
    await page.until("document.querySelector('#app').dataset.link === 'connected'", 'photo chat');
    await page.run(`window.bitmapCalls=[]; window.bitmapClosed=0; window.fileReads=0;
      const bitmap=window.createImageBitmap, read=FileReader.prototype.readAsDataURL;
      window.savedBitmap=bitmap;
      window.createImageBitmap=async (photo,options)=>{bitmapCalls.push(options);const value=await bitmap(photo,options),close=value.close.bind(value);value.close=()=>{bitmapClosed++;close();};return value;};
      FileReader.prototype.readAsDataURL=function(...args){fileReads++;return read.apply(this,args);};
      window.rotatedPhoto=async orientation=>{
        const canvas=document.createElement('canvas');canvas.width=80;canvas.height=40;const ctx=canvas.getContext('2d');
        for(const [color,x,y] of [['red',0,0],['lime',40,0],['blue',0,20],['yellow',40,20]]){ctx.fillStyle=color;ctx.fillRect(x,y,40,20);}
        const blob=await new Promise(resolve=>canvas.toBlob(resolve,'image/jpeg',1)),bytes=new Uint8Array(await blob.arrayBuffer());
        const exif=Uint8Array.from([255,225,0,34,69,120,105,102,0,0,73,73,42,0,8,0,0,0,1,0,18,1,3,0,1,0,0,0,orientation,0,0,0,0,0,0,0]);
        const transfer=new DataTransfer();transfer.items.add(new File([bytes.slice(0,2),exif,bytes.slice(2)],'rotated.jpg',{type:'image/jpeg'}));
        const input=document.querySelector('#room-attach-input');input.files=transfer.files;input.dispatchEvent(new Event('change',{bubbles:true}));
      };
      window.photoColors=async()=>{
        const img=document.querySelector('#attach-preview img');await img.decode();
        const c=document.createElement('canvas');c.width=20;c.height=20;const ctx=c.getContext('2d');ctx.drawImage(img,0,0,20,20);
        return {portrait:img.naturalHeight>img.naturalWidth,colors:[[5,5],[15,5],[5,15],[15,15]].map(([x,y])=>Array.from(ctx.getImageData(x,y,1,1).data).slice(0,3).map(v=>v>128?1:0).join(''))};
      };`);
    const expected = ['100,010,001,110','010,100,110,001','110,001,010,100','001,110,100,010','100,001,010,110','001,100,110,010','110,010,001,100','010,110,100,001'];
    for (const fallback of [false,true]) {
      if (fallback) await page.run('window.createImageBitmap = undefined');
      for (let orientation=1;orientation<=8;orientation++) {
        await page.run('rotatedPhoto(' + orientation + ')');
        await page.until('CapriAttach.hasPicture() && !CapriAttach.busy', 'oriented photo');
        assert.deepEqual(await page.run('photoColors()'), {portrait:orientation>=5,colors:expected[orientation-1].split(',')});
        await click(page, '[aria-label="Remove picture"]');
      }
    }
    assert.equal(await page.run('bitmapCalls.length'), 8);
    assert.equal(await page.run('bitmapClosed'), 8);
    assert.equal(await page.run('fileReads'), 8);
    assert.equal(await page.run('bitmapCalls.every(o=>o.resizeWidth===1568 && o.imageOrientation==="from-image")'), true);
  } finally { if (page) await page.close(); await site.close(); }
});

test('OpenAI and Google image refusals keep the draft and give the sightless notice', { skip: !chromePath(), timeout: 60000 }, async () => {
  const site = await startSite(8992), provider = await startProvider(8993); let page;
  try {
    page = await launch({port:9502});
    await page.send('Page.addScriptToEvaluateOnNewDocument', {source:earlyAccess() + "localStorage.setItem('capri-remote-mode','phone');"});
    const url = 'http://127.0.0.1:8992/?provider=http://127.0.0.1:8993';
    await page.go(url);
    for (const [kind,reason] of [['openai','Invalid content type. image_url is only supported by certain models.'],['google','Image input is not supported for this model.']]) {
      await page.run('localStorage.setItem("capri-remote-brain",' + JSON.stringify(JSON.stringify({provider:kind,key:'test-only-never-a-real-key',model:'blind-test',models:['blind-test']})) + ')');
      provider.state.fail = {status:400,body:{error:{message:reason}}};
      await page.go(url); await page.until('CapriRemote.state === "connected"', kind + ' phone');
      await picture(page); await page.until('CapriAttach.hasPicture()', kind + ' picture');
      await type(page, 'keep this photo'); await click(page, '#send');
      await line(page, "This brain can't see pictures.");
      assert.equal(await page.run('!CapriAttach.busy && CapriAttach.hasPicture() && document.querySelector("#msg").value === "keep this photo"'), true);
      assert.equal(await page.run('CapriNative.pictures()'), false);
      const requests=provider.got.filter(r=>/completions/.test(r.path)).length;
      await click(page, '#send');
      assert.equal(provider.got.filter(r=>/completions/.test(r.path)).length, requests);
    }
  } finally { if (page) await page.close(); await site.close(); await provider.close(); }
});

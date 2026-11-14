import { test } from 'node:test';
import assert from 'node:assert/strict';
import { chromePath, launch } from './lib/cdp.mjs';
import { HOME_SCREEN, IPHONES, misfitsOf, setPhone } from './lib/fit.mjs';
import { startSite } from './lib/site.mjs';

test('the phone room camera starts on request and releases its tracks', { skip: !chromePath() }, async () => {
  const site = await startSite(8981);
  let page;
  try {
    page = await launch({ port: 9491, args: ['--use-fake-device-for-media-stream', '--use-fake-ui-for-media-stream'] });
    await page.send('Page.addScriptToEvaluateOnNewDocument', { source: `
      window.cameraRequests = [];
      const media = navigator.mediaDevices;
      const native = media.getUserMedia.bind(media);
      media.getUserMedia = async constraints => {
        const request = { constraints };
        cameraRequests.push(request);
        request.stream = await native(constraints);
        if (window.holdCamera) await new Promise(resolve => { window.unholdCamera = resolve; });
        return request.stream;
      };
    ` });
    await page.go('http://127.0.0.1:8981/?demo');
    await page.until("document.getElementById('app').dataset.screen === 'chat'", 'demo chat');
    await setPhone(page, IPHONES[0], HOME_SCREEN);
    const click = id => page.run("document.getElementById('" + id + "').click()");
    const open = async () => { await click('room-camera'); await click('room-open'); };
    assert.equal(await page.run('cameraRequests.length'), 0);
    await click('room-camera');
    await click('room-attach');
    assert.equal(await page.run('cameraRequests.length'), 0);
    await open();
    await page.until("!document.getElementById('room-controls').hidden", 'camera controls');
    assert.deepEqual(await misfitsOf(page), []);
    assert.deepEqual(await page.run('cameraRequests[0].constraints'), { video: { facingMode: { ideal: 'environment' } }, audio: false });
    assert.equal(await page.run("document.getElementById('msg').getBoundingClientRect().height > 0"), true);
    await click('room-lie');
    await page.until("document.getElementById('room-sprite').style.backgroundImage.includes('laydown.png')", 'lie down art');
    await page.run('CapriRoom.talk(true)');
    await page.until("document.getElementById('room-sprite').style.backgroundImage.includes('laydown_talk.png')", 'talking art');
    await click('room-love');
    await page.until("document.getElementById('room-heart').complete && document.getElementById('room-heart').naturalWidth > 0", 'heart art');
    await click('room-close');
    assert.equal(await page.run("cameraRequests[0].stream.getVideoTracks()[0].readyState"), 'ended');
    assert.equal(await page.run("document.getElementById('room-video').srcObject === null"), true);
    await page.run('window.holdCamera = true');
    await open();
    await page.until('typeof unholdCamera === "function"', 'camera request waiting');
    await click('room-close');
    await page.run('unholdCamera()');
    await page.until("cameraRequests[1].stream.getVideoTracks()[0].readyState === 'ended'", 'late camera track release');
    assert.equal(await page.run("document.getElementById('room').hidden"), true);
  } finally {
    if (page) await page.close();
    await site.close();
  }
});
import { roomRig, roomPosition, roomPointer, dragRoom } from './lib/room.mjs';
import { tone } from './lib/live.mjs';
import { verifyArt } from './lib/room-art.mjs';

test('room voice, tool moves, teardown and AR use the call audio without a mic', { skip: !chromePath() }, async () => {
  const r = await roomRig();
  const { page, pc, click } = r;
  const value = id => page.run('document.getElementById(' + JSON.stringify(id) + ').getAttribute("aria-pressed")');
  const move = (move, state = 'done') => pc.send('room', { type: 'chat.tool', id: move, name: move === 'heart' ? 'animate' : 'move_yourself', state, move });
  const quiet = async () => {
    await page.until("contexts.every(c => c.state === 'closed') && CapriCall.stats().playing === 0", 'sound released');
    assert.equal(await page.run("cameraRequests.every(r => r.stream.getTracks().every(t => t.readyState === 'ended'))"), true);
    assert.equal(await page.run('cameraRequests.some(r => r.constraints.audio !== false)'), false);
  };
  try {
    await pc.voice('room', tone(24000, 0.25, 220));
    assert.equal(await page.run('audioStarts'), 0);
    await r.open();
    assert.equal(await value('room-voice'), 'true');
    assert.equal(await page.run("CapriCall.listening && !document.querySelector('.call-room').getClientRects().length && !document.getElementById('app').classList.contains('on-call')"), true);
    await move('lie_down', 'running');
    assert.equal(await value('room-sit'), 'true');
    await move('lie_down');
    assert.equal(await value('room-lie'), 'true');
    assert.equal(await value('room-sit'), 'false');
    await pc.voice('room', tone(24000, 1, 220));
    await page.until("document.getElementById('room-sprite').style.backgroundImage.includes('laydown_talk.png')", 'lying talk frames');
    await page.until("document.getElementById('room-sprite').style.backgroundImage.includes('laydown.png')", 'lying quiet frames');
    await move('sit');
    assert.equal(await value('room-sit'), 'true');
    await pc.voice('room', tone(24000, 1, 220));
    await page.until("document.getElementById('room-sprite').style.backgroundImage.includes('perch_talk.png')", 'sitting talk frames');
    await move('heart');
    assert.equal(await value('room-love'), 'true');
    await page.until("!document.getElementById('room-heart').hidden", 'heart shown');
    await page.send('Emulation.setEmulatedMedia', { features: [{name:'prefers-reduced-motion',value:'reduce'}] });
    await move('heart');
    assert.equal(await value('room-love'), 'true');
    assert.equal(await page.run("document.getElementById('room-status').textContent"), 'Heart.');
    await page.send('Emulation.setEmulatedMedia', { features: [] });
    await click('room-voice');
    const started = await page.run('audioStarts');
    await pc.voice('room', tone(24000, 0.5, 220));
    assert.equal(await page.run('audioStarts'), started);
    assert.equal(await page.run("pc.sent.at(-1).type"), 'call.stop');
    await click('room-close');
    await quiet();
    await r.open();
    assert.equal(await value('room-voice'), 'false');
    assert.equal(await page.run("pc.sent.at(-1).type"), 'call.stop');
    await click('room-voice');
    await pc.voice('room', tone(24000, 2, 220));
    await click('room-close');
    await quiet();
    assert.ok(await page.run('audioStops') > 0);
    await r.open();
    await pc.voice('room', tone(24000, 2, 220));
    await page.run("Object.defineProperty(document,'hidden',{configurable:true,value:true});document.dispatchEvent(new Event('visibilitychange'))");
    await quiet();
    assert.equal(await page.run("document.getElementById('room').hidden"), true);
    await page.run("Object.defineProperty(document,'hidden',{configurable:true,value:false})");
    await r.open();
    await pc.voice('room', tone(24000, 2, 220));
    await page.run("dispatchEvent(new Event('pagehide'))");
    await quiet();
    await r.open();
    await pc.voice('room', tone(24000, 2, 220));
    const requests = await page.run('cameraRequests.length');
    await r.state('offline');
    await page.until("contexts.every(c => c.state === 'closed') && CapriCall.stats().playing === 0 && !CapriCall.listening", 'voice stopped on link loss');
    assert.equal(await page.run("document.getElementById('room').hidden"), false);
    assert.equal(await page.run("cameraRequests.at(-1).stream.getVideoTracks()[0].readyState"), 'live');
    await page.until("!document.getElementById('room-sprite').style.backgroundImage.includes('_talk')", 'quiet frames on link loss');
    await r.state('connected');
    await page.until('CapriCall.listening', 'room voice reconnected');
    await pc.voice('room', tone(24000, 1, 220));
    await page.until("document.getElementById('room-sprite').style.backgroundImage.includes('_talk')", 'talk frames reconnected');
    assert.equal(await page.run('cameraRequests.length'), requests);
    await page.run("document.getElementById('app').dataset.screen='pair'");
    await page.until("document.getElementById('room').hidden", 'room closed with chat');
    await quiet();
    await page.run("document.getElementById('app').dataset.screen='chat'");
    await r.open();
    await click('room-track');
    await page.until("document.getElementById('app').dataset.room === 'ar' && document.getElementById('room-view').hasAttribute('data-surface')", 'AR hit');
    await click('room-place');
    await page.until("document.getElementById('room-her').style.left === '55%'", 'projected AR point');
    assert.equal(await page.run("document.getElementById('room-her').style.top"), '57.5%');
    await pc.voice('room', tone(24000, 1, 220));
    await page.until("document.getElementById('room-sprite').style.backgroundImage.includes('perch_talk.png')", 'AR voice talk frames');
    await page.run("Object.defineProperty(document,'hidden',{configurable:true,value:true});document.dispatchEvent(new Event('visibilitychange'))");
    await quiet();
    await page.run("Object.defineProperty(document,'hidden',{configurable:true,value:false})");
    assert.deepEqual(await page.run('({ends:xrState.ends,cancels:xrState.cancels,transientCancels:xrState.transientCancels})'), { ends: 1, cancels: 1, transientCancels: 1 });
    await r.open();
    await page.run('xrState.hold = true');
    await click('room-track');
    await page.until('typeof xrState.release === "function"', 'late AR source');
    await click('room-close');
    await page.run('xrState.release()');
    await page.until('xrState.cancels === 2', 'late AR source released');
    await page.run('xrState.hold = false');
    await r.open();
    await click('f-call');
    await page.until('CapriCall.stats().micLive && CapriCall.stats().shown', 'ordinary call');
    assert.equal(await page.run("document.getElementById('room').hidden && !CapriCall.listening"), true);
    await page.run('CapriCall.end()');
    await page.until("contexts.every(c => c.state === 'closed')", 'ordinary call released');
    for (const result of await verifyArt(page)) assert.equal(result.equal, true, JSON.stringify(result));
  } finally { await r.close(); }
});

test('room gives plain voice status, remembers Voice and keeps phone mode silent', { skip: !chromePath() }, async () => {
  const r = await roomRig({ features: ['chat'], port: 29882, chrome: 29892 });
  try {
    await r.open();
    assert.equal(await r.page.run("document.getElementById('room-voice-note').textContent"), 'Her voice needs a newer Capri on your PC.');
    assert.equal(await r.page.run('contexts.length'), 0);
    await r.click('room-close'); await r.open();
    assert.equal(await r.page.run("document.getElementById('room-voice-note').textContent"), '');
    await r.state('connected', { native: true });
    await r.click('room-close'); await r.open();
    assert.equal(await r.page.run("document.getElementById('room-voice-note').textContent"), '');
    await r.click('room-close');
    await r.state('connected', { native: false, features: ['chat', 'call'] });
    await r.page.run('window.blockSound = true');
    await r.open();
    await r.page.run('contexts.at(-1).suspend()');
    await r.page.until("document.getElementById('room-voice-note').textContent === 'Tap Voice to hear her.'", 'blocked sound status');
    await r.page.run('window.blockSound = false');
    await r.click('room-voice');
    await r.page.until("contexts.at(-1).state === 'running' && document.getElementById('room-voice-note').textContent === ''", 'tap unlocks voice');
    await r.click('room-voice');
    await r.page.go('http://127.0.0.1:29882/');
    await r.page.until("document.getElementById('app').dataset.screen === 'chat'", 'reloaded chat');
    await r.open();
    assert.equal(await r.page.run("document.getElementById('room-voice').getAttribute('aria-pressed')"), 'false');
    assert.equal(await r.page.run('contexts.length'), 0);
  } finally { await r.close(); }
});

test('room drags with mouse, touch and pen, keeps her frames, and uses transient AR hits', { skip: !chromePath() }, async () => {
  const r = await roomRig({port:29886,chrome:29896});
  const { page, click } = r;
  const near = (actual, expected) => assert.ok(Math.abs(actual - expected) <= 1, actual + ' != ' + expected);
  const tap = async (x, y, type = 'mouse') => {
    await roomPointer(page,type,'down',x,y); await roomPointer(page,type,'up',x,y);
  };
  try {
    await setPhone(page, [390,844], HOME_SCREEN);
    await page.send('Emulation.setTouchEmulationEnabled', {enabled:true,maxTouchPoints:1});
    await r.open();
    await page.until("document.getElementById('room-sprite').dataset.fit === 'perch'", 'drag art');
    assert.equal(await page.run("document.getElementById('room-status').textContent"), 'Drag Capri to move her.');
    assert.match(await page.run("document.getElementById('room-place').getAttribute('aria-label')"), /Drag.*arrow keys/);
    await page.run("window.placeTaps=0; document.addEventListener('click',e=>{if(e.target.id==='room-place')placeTaps++;})");
    for (const type of ['mouse','touch','pen']) {
      await click('room-place');
      const taps = await page.run('placeTaps');
      const {start,live,end} = await dragRoom(page,type,30,-18);
      near(live.left,start.left + 30); near(live.top,start.top - 18);
      near(end.left,live.left); near(end.top,live.top);
      assert.equal(await page.run('placeTaps'), taps);
      const before = await roomPosition(page);
      await tap(before.x,before.y,type);
      const after = await roomPosition(page);
      const box = await page.run("(() => {const b=document.getElementById('room-view').getBoundingClientRect();return {left:b.left,top:b.top};})()");
      near(after.left,Math.max(after.width * .16,Math.min(after.width * .84,before.x - box.left)));
      near(after.top,Math.max(after.height * .42,Math.min(after.height * .98,before.y - box.top)));
      assert.equal(await page.run('placeTaps'), taps + 1);
    }
    await click('room-place');
    let start = await roomPosition(page);
    await roomPointer(page,'mouse','down',start.x,start.y);
    await roomPointer(page,'mouse','move',start.x + 3,start.y + 2);
    await roomPointer(page,'mouse','up',start.x + 3,start.y + 2);
    assert.equal(await page.run('placeTaps'), 8);
    const box = await page.run("(() => {const b=document.getElementById('room-view').getBoundingClientRect();return {left:b.left,top:b.top,width:b.width,height:b.height};})()");
    await tap(box.left + box.width * .8,box.top + box.height * .9);
    let pos = await roomPosition(page);
    near(pos.left,box.width * .8); near(pos.top,box.height * .9);
    await click('room-place');
    for (const [dx,dy,x,y] of [[-1000,-1000,.16,.42],[1000,1000,.84,.98]]) {
      const {end} = await dragRoom(page,'mouse',dx,dy);
      near(end.left,end.width * x); near(end.top,end.height * y);
    }
    await click('room-place');
    await page.run("document.getElementById('room-place').focus()");
    start = await roomPosition(page);
    for (const [key,code,modifiers] of [['ArrowRight',39,0],['ArrowUp',38,8]]) {
      await page.send('Input.dispatchKeyEvent',{type:'keyDown',key,code:key,windowsVirtualKeyCode:code,modifiers});
      await page.send('Input.dispatchKeyEvent',{type:'keyUp',key,code:key,windowsVirtualKeyCode:code,modifiers});
    }
    pos = await roomPosition(page); near(pos.left,start.left + 5); near(pos.top,start.top - 20);
    for (const [pose,clip] of [['room-sit','perch_talk.png'],['room-lie','laydown_talk.png']]) {
      await click('room-place'); await click(pose); await r.pc.voice('room', tone(24000, 3, 220));
      await page.until("document.getElementById('room-sprite').style.backgroundImage.includes(" + JSON.stringify(clip) + ")", 'drag talking art');
      await click('room-love');
      await page.until("!document.getElementById('room-heart').hidden", 'drag heart');
      start = await roomPosition(page);
      await roomPointer(page,'mouse','down',start.x,start.y);
      await roomPointer(page,'mouse','move',start.x + 20,start.y - 10);
      const frame = await page.run("document.getElementById('room-sprite').style.backgroundPosition");
      await page.until("document.getElementById('room-sprite').style.backgroundPosition !== " + JSON.stringify(frame), 'frames playing during drag');
      assert.equal(await page.run("document.getElementById('room-heart').hidden"), false);
      assert.equal(await page.run("document.getElementById('" + pose + "').getAttribute('aria-pressed')"), 'true');
      assert.equal(await page.run("document.getElementById('room-sprite').style.backgroundImage.includes(" + JSON.stringify(clip) + ")"), true);
      await roomPointer(page,'mouse','up',start.x + 20,start.y - 10);
    }
    await page.run('xrState.hit = [0.2,0.6,-2]');
    await click('room-track');
    await page.until("document.getElementById('app').dataset.room === 'ar' && document.getElementById('room-view').hasAttribute('data-surface')", 'drag AR');
    assert.equal(await page.run('xrState.profile'), 'generic-touchscreen');
    await click('room-place');
    await page.until("document.getElementById('room-her').style.left === '55%'", 'AR seat');
    start = await roomPosition(page);
    await roomPointer(page,'touch','down',start.x,start.y);
    await roomPointer(page,'touch','move',start.x + 25,start.y + 10);
    await page.run('xrState.touch = [0.8,0.2,-2]');
    await page.until("document.getElementById('room-her').style.left === '70%'", 'transient finger hit');
    assert.equal(await page.run("document.getElementById('room-her').style.top"), '45%');
    await page.run('xrState.touch = null; xrState.hit = [-0.5,0.6,-2]');
    await page.run('new Promise(resolve=>setTimeout(resolve,100))');
    assert.equal(await page.run("document.getElementById('room-her').style.left"), '70%');
    await roomPointer(page,'touch','up',start.x + 25,start.y + 10);
    await page.run('new Promise(resolve=>setTimeout(resolve,100))');
    assert.equal(await page.run("document.getElementById('room-her').style.left"), '70%');
    await click('room-close');
    assert.equal(await page.run('xrState.transientCancels'), 1);
    for (const setting of ['noTransient','rejectTransient']) {
      await page.run('xrState.' + setting + ' = true');
      await r.open(); await click('room-track');
      await page.until("document.getElementById('app').dataset.room === 'ar' && document.getElementById('room-view').hasAttribute('data-surface')", 'AR without transient hits');
      await click('room-place');
      await page.until("!document.getElementById('room-her').hidden", 'AR fallback seat');
      const left = await page.run("document.getElementById('room-her').style.left");
      await dragRoom(page,'touch',20,10);
      assert.equal(await page.run("document.getElementById('room-her').style.left"), left);
      assert.equal(await page.run("document.getElementById('room-status').textContent"), '');
      await click('room-close');
      await page.run('xrState.' + setting + ' = false');
    }
    await r.open(); await page.run('xrState.holdTransient = true'); await click('room-track');
    await page.until('typeof xrState.releaseTransient === "function"', 'late transient source');
    await click('room-close'); await page.run('xrState.releaseTransient()');
    await page.until('xrState.transientCancels === 2', 'late transient source cancelled');
  } finally { await r.close(); }
});

test('room preloads the heart and gives it a full turn after a slow load', { skip: !chromePath() }, async () => {
  const r = await roomRig({heart:true,port:29887,chrome:29897});
  try {
    await r.open(); await r.heart.ready;
    assert.equal(await r.page.run("document.getElementById('room-heart').hidden"), true);
    await r.click('room-love'); await r.click('room-love');
    await r.page.run('new Promise(resolve=>setTimeout(resolve,3800))');
    assert.equal(r.heart.requests.length, 1);
    assert.equal(await r.page.run("document.getElementById('room-heart').hidden"), true);
    await r.heart.finish();
    await r.page.until("!document.getElementById('room-heart').hidden", 'loaded heart shown');
    await r.page.run('new Promise(resolve=>setTimeout(resolve,3300))');
    assert.equal(await r.page.run("document.getElementById('room-heart').hidden"), false);
    await r.page.until("document.getElementById('room-heart').hidden", 'full heart time finished');
    await r.click('room-love');
    await r.page.until("!document.getElementById('room-heart').hidden", 'heart plays again');
    await r.click('room-close');
    assert.equal(await r.page.run("document.getElementById('room-heart').hidden"), true);
  } finally { await r.close(); }
});

test('room gives a plain status if the heart cannot load', { skip: !chromePath() }, async () => {
  const r = await roomRig({heart:true,port:29888,chrome:29898});
  try {
    await r.open(); await r.heart.ready; await r.click('room-love');
    await r.heart.finish(true);
    await r.page.until("document.getElementById('room-status').textContent === 'Heart.'", 'failed heart status');
    assert.equal(await r.page.run("document.getElementById('room-heart').hidden"), true);
    assert.equal(await r.page.run("document.getElementById('room-love').getAttribute('aria-pressed')"), 'true');
  } finally { await r.close(); }
});

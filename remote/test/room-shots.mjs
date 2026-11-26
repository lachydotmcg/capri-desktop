import { mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { roomRig, dragRoom } from './lib/room.mjs';
import { tone } from './lib/live.mjs';
import { setPhone, SAFARI, misfitsOf } from './lib/fit.mjs';
import assert from 'node:assert/strict';

mkdirSync('room-shots', { recursive: true });
let opened = 0, closed = 0;
for (const size of [[390, 844], [360, 640], [360, 800]]) {
  const r = await roomRig({ port: 29884, chrome: 29894 }); opened++;
  const { page, click, pc } = r;
  const shot = name => page.shot(join('room-shots', size.join('x') + '-' + name + '.png'));
  try {
    await setPhone(page, size, SAFARI);
    await page.send('Emulation.setDeviceMetricsOverride', {width:size[0],height:size[1],deviceScaleFactor:1,mobile:true});
    await click('room-camera'); await shot('menu'); await click('room-camera');
    await r.open();
    await page.until("document.getElementById('room-sprite').style.backgroundImage.includes('perch.webp')", 'sitting sprite');
    await shot('sitting');
    await shot('voice-on');
    await click('room-voice');
    await shot('voice-off');
    await click('room-voice');
    if (size[0] === 390) {
      await page.send('Emulation.setTouchEmulationEnabled', {enabled:true,maxTouchPoints:1});
      const {end} = await dragRoom(page, 'touch', -300, -200);
      assert.ok(Math.abs(end.left - end.width * .16) <= 1 && Math.abs(end.top - end.height * .42) <= 1);
      await shot('dragged-corner');
      await click('room-place');
    }
    assert.deepEqual(await misfitsOf(page), []);
    await pc.send('room', { type: 'chat.tool', id: 'lie', name: 'move_yourself', state: 'done', move: 'lie_down' });
    await page.until("document.getElementById('room-sprite').style.backgroundImage.includes('laydown.png')", 'lying sprite');
    await shot('lying');
    await pc.voice('room', tone(24000, 2, 240));
    await page.until("document.getElementById('room-sprite').style.backgroundImage.includes('laydown_talk.png')", 'talking sprite');
    await shot('talking');
    await pc.send('room', { type: 'chat.tool', id: 'heart', name: 'animate', state: 'done', move: 'heart' });
    await page.until("document.getElementById('room-heart').naturalWidth === 1920", 'heart frames');
    await new Promise(resolve => setTimeout(resolve, 700));
    await shot('heart');
    await click('f-call');
    await page.until('CapriCall.stats().micLive && CapriCall.state === "on"', 'call in room');
    await page.run('CapriCall.room()');
    await pc.voice('room', tone(24000, 2, 240));
    await page.until("document.getElementById('room-sprite').style.backgroundImage.includes('laydown_talk.png')", 'call room frames');
    const layout = await page.run(`(() => {const box=id=>document.getElementById(id).getBoundingClientRect();return {view:box('room-view').height,edge:box('room').bottom,chat:box('log').top,visible:['msg','room-close'].every(id=>{const b=box(id);return b.top>=0&&b.bottom<=innerHeight;}),bar:(()=>{const b=document.querySelector('.callbar').getBoundingClientRect();return b.height>0&&b.top>=0&&b.bottom<=innerHeight;})()};})()`);
    assert.ok(layout.view >= (size[0] < 390 ? 150 : 180)); assert.ok(layout.chat >= layout.edge); assert.ok(layout.visible && layout.bar);
    await shot('during-call');
    await page.run('CapriCall.end()');
    await page.until('CapriCall.listening', 'listening after screenshot call');
    await page.run('window.blockSound = true; contexts.at(-1).suspend()');
    await page.until("document.getElementById('room-voice-note').textContent === 'Tap Voice to hear her.'", 'blocked voice');
    await shot('tap-voice');
    const note = text => page.until('document.getElementById("room-voice-note").textContent === '+JSON.stringify(text), text);
    await page.run(`window.blockSound=false;CapriCall.stopListening();pc.noAnswer=true;const timer=window.setTimeout;window.setTimeout=(fn,ms,...args)=>timer(fn,ms===8000?100:ms,...args);CapriRoom.startVoice()`);
    await note('Your PC did not answer in time. Tap Voice to try again.'); await shot('no-answer');
    for (const [reason,text] of [['not-allowed','Your PC is not taking calls. Tap Voice to try again.'],['busy',"Your PC is on another phone's call. Tap Voice to try again."],['ended','Your PC ended it. Tap Voice to try again.']]) {
      await page.run('pc.noAnswer=false;pc.answer={type:"call.state",on:false,reason:'+JSON.stringify(reason)+'}');
      await click('room-voice'); await note(text); await shot(reason);
    }
    await page.run('pc.answer={type:"call.state",on:true,voice:true,ears:false}'); await click('room-voice'); await note('');
    await pc.send('room',{type:'call.state',on:true,voice:false,ears:false}); await note('Her voice is off on your PC.'); await shot('pc-voice-off');
    await pc.send('room',{type:'call.state',on:true,voice:true,ears:false}); await note('');
    assert.deepEqual(await misfitsOf(page), []);
    await click('room-close');
    await r.state('connected', { features: ['chat'] });
    await r.open();
    await shot('newer-pc');
    assert.deepEqual(await misfitsOf(page), []);
    await setPhone(page, [640, 360], SAFARI);
    assert.deepEqual(await misfitsOf(page), []);
    assert.equal(await page.run("['room-close','room-sit','room-lie','room-love','room-voice','msg'].every(id => {const b=document.getElementById(id).getBoundingClientRect(); return b.width>0 && b.height>0 && b.top>=0 && b.bottom<=innerHeight;})"), true);
    console.log('PASS room shots ' + size.join('x') + ' and landscape controls');
  } finally { await r.close(); closed++; }
}
console.log('Browsers started ' + opened + ', closed ' + closed);

const desktop = await roomRig({port:29884,chrome:29894}); opened++;
try {
  const {page}=desktop;
  await page.send('Emulation.setDeviceMetricsOverride',{width:1000,height:800,deviceScaleFactor:1,mobile:false});
  await desktop.open();
  const color=()=>page.run('getComputedStyle(document.getElementById("room-sit"),"::after").backgroundColor');
  const filled=await color();
  const box=await page.run('(()=>{const b=document.getElementById("room-sit").getBoundingClientRect();return {x:b.x+b.width/2,y:b.y+b.height/2};})()');
  await page.send('Input.dispatchMouseEvent',{type:'mouseMoved',...box});
  await page.run('document.getElementById("room-sit").focus()');
  assert.equal(await page.run('document.getElementById("room-sit").matches(":hover")'),true);
  assert.equal(await color(),filled);
  await page.shot(join('room-shots','1000x800-pressed-hover-focus.png'));
} finally {await desktop.close();closed++;}
console.log('All screenshot browsers started '+opened+', closed '+closed);

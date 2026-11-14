import { mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { roomRig, dragRoom } from './lib/room.mjs';
import { tone } from './lib/live.mjs';
import { setPhone, SAFARI, misfitsOf } from './lib/fit.mjs';
import assert from 'node:assert/strict';

mkdirSync('room-shots', { recursive: true });
let opened = 0, closed = 0;
for (const size of [[390, 844], [360, 640]]) {
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
    await page.run('window.blockSound = true; contexts.at(-1).suspend()');
    await page.until("document.getElementById('room-voice-note').textContent === 'Tap Voice to hear her.'", 'blocked voice');
    await shot('tap-voice');
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

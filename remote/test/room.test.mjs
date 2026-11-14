import { test } from 'node:test';
import assert from 'node:assert/strict';
import { chromePath, launch } from './lib/cdp.mjs';
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
    const click = id => page.run("document.getElementById('" + id + "').click()");
    const open = async () => { await click('room-camera'); await click('room-open'); };
    assert.equal(await page.run('cameraRequests.length'), 0);
    await click('room-camera');
    await click('room-attach');
    assert.equal(await page.run('cameraRequests.length'), 0);
    await open();
    await page.until("!document.getElementById('room-controls').hidden", 'camera controls');
    assert.deepEqual(await page.run('cameraRequests[0].constraints'), { video: { facingMode: { ideal: 'environment' } }, audio: false });
    assert.equal(await page.run("document.getElementById('msg').getBoundingClientRect().height > 0"), true);
    await click('room-lie');
    await page.until("document.getElementById('room-sprite').style.backgroundImage.includes('laydown.webp')", 'lie down art');
    await page.run('CapriRoom.talk(true)');
    await page.until("document.getElementById('room-sprite').style.backgroundImage.includes('laydown_talk.webp')", 'talking art');
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

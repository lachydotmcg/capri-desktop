import { mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { chromePath, launch } from './lib/cdp.mjs';
import { ROOT, earlyAccess, startSite } from './lib/site.mjs';
import { GOOD, startProvider } from './lib/fake-provider.mjs';
import { click, type, picture, line, fakePc } from './lib/attach.mjs';

if (!chromePath()) throw new Error('Chrome is not installed');
const folder = join(ROOT, 'attach-shots'); mkdirSync(folder, { recursive: true });
const site = await startSite(8986), provider = await startProvider(8987);
let started = 0, closed = 0;
try {
  for (const [width, height] of [[390,844],[360,640]]) {
    let page;
    try {
      page = await launch({ port: 9496, width, height, args: ['--disable-gpu', '--no-sandbox'] }); started++; await page.send('Network.disable'); await page.send('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: 1, mobile: false });
      await page.send('Page.addScriptToEvaluateOnNewDocument', { source: earlyAccess() + `localStorage.setItem('capri-remote-mode','phone'); if (!localStorage.getItem('capri-remote-brain')) localStorage.setItem('capri-remote-brain', JSON.stringify({provider:'anthropic',key:${JSON.stringify(GOOD.anthropic)},model:'claude-haiku-5-5',models:['claude-haiku-5-5'],images:true}));` });
      const url = 'http://127.0.0.1:8986/?provider=http://127.0.0.1:8987&count=http://127.0.0.1:8987/count';
      const shot = name => page.shot(join(folder, `${width}x${height}-${name}.png`));
      await page.go(url); await page.until("document.querySelector('#app').dataset.link === 'connected'", 'phone chat');
      await picture(page, true); await line(page, "That isn't a picture I can send."); await shot('invalid');
      await page.until("document.querySelector('#attach-note').hidden", 'notice cleared');
      await picture(page); await page.until("document.querySelector('#attach-preview img')?.naturalWidth > 0", 'preview');
      await type(page, 'Look at this photo.'); await shot('preview'); await click(page, '#send');
      await page.until("!CapriAttach.hasPicture() && document.querySelector('.msg.you img')?.naturalWidth > 0 && document.querySelector('.msg.capri.done')", 'sent picture'); await shot('sent');
      await page.run("localStorage.setItem('capri-remote-brain', JSON.stringify({provider:'deepseek',key:'test-only-never-a-real-key',model:'deepseek-flash',models:['deepseek-flash'],images:false}))");
      await page.go(url); await page.until("document.querySelector('#app').dataset.link === 'connected'", 'blind brain');
      await picture(page); await page.until('CapriAttach.hasPicture()', 'picture'); await type(page, 'Look at this photo.'); await click(page, '#send');
      await line(page, "This brain can't see pictures."); await shot('no-vision');
    } finally { if (page) { await page.close(); closed++; } }
    try {
      page = await launch({ port: 9496, width, height, args: ['--disable-gpu', '--no-sandbox'] }); started++; await page.send('Network.disable'); await page.send('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: 1, mobile: false });
      const pc = await fakePc(page, ['chat']);
      await page.go('http://127.0.0.1:8986/?relay=ws://127.0.0.1:8986' + pc.hash);
      await page.until("document.querySelector('#app').dataset.link === 'connected'", 'old PC');
      await click(page, '#room-camera'); await click(page, '#room-attach'); await line(page, 'Update Capri on your PC to send pictures.');
      await page.shot(join(folder, `${width}x${height}-old-pc.png`));
    } finally { if (page) { await page.close(); closed++; } }
  }
} finally { await site.close(); await provider.close(); console.log(`Screenshot browsers started ${started}; closed ${closed}.`); }

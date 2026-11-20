import { test } from 'node:test';
import assert from 'node:assert/strict';
import { chromePath, launch } from './lib/cdp.mjs';
import { startSite } from './lib/site.mjs';

// MEMORY (PROTOCOL.md 8.8) against the page's demo PC: add, edit, forget with Undo, search, a day's chat log, forgetting a
// day, and what the page keeps: nothing in browser storage, and nothing on screen once the page is hidden.
const SITE_PORT = 8812, CHROME_PORT = 8814;

test('MEMORY: add, edit, forget, undo, search, chat logs and clearing', { skip: !chromePath() && 'Chrome is not installed', timeout: 120000 }, async () => {
  const site = await startSite(SITE_PORT);
  let page = null;
  try {
    page = await launch({ port: CHROME_PORT });
    const errors = [];
    page.on((method, data) => { if (method === 'Runtime.exceptionThrown') errors.push(data.exceptionDetails.text); });
    const click = selector => page.run(`document.querySelector(${JSON.stringify(selector)}).click()`);
    const count = selector => page.run(`document.querySelectorAll(${JSON.stringify(selector)}).length`);
    const settled = () => page.until(`!document.querySelector('.mem-add-key').disabled`, 'the PC answering');
    await page.go(`http://127.0.0.1:${SITE_PORT}/?demo`);
    await page.until(`document.querySelector('#f-memory').getAttribute('aria-disabled') === 'false'`, 'the Memory tile on');
    await click('#f-memory');
    await page.until(`document.querySelectorAll('.mem-item').length > 0`, 'her memory listed');
    await settled();
    assert.equal(await page.run(`document.querySelector('#memory').hidden`), false);
    const about = await count('.mem-section:first-of-type .mem-item');

    await page.run(`(() => { const input = document.querySelector('.mem-add .mem-input'); input.value = 'You take your coffee black.'; document.querySelector('.mem-add').requestSubmit(); })()`);
    await page.until(`document.body.textContent.includes('You take your coffee black.')`, 'the new note');
    await settled();
    assert.equal(await page.run(`document.querySelector('.mem-toast-text').textContent`), 'Noted.');

    await click('.mem-text');
    await page.run(`(() => { const box = document.querySelector('.mem-edit'); box.value = 'They have a dog called Rex too'; box.dispatchEvent(new Event('input')); })()`);
    await click('.mem-keys .cyan');
    await page.until(`document.body.textContent.includes('They have a dog called Rex too') && !document.querySelector('.mem-edit')`, 'the edit saved');
    await settled();

    const before = await count('.mem-item');
    await click('.mem-forget');
    await page.until(`document.querySelectorAll('.mem-item').length === ${before - 1}`, 'the note forgotten');
    await settled();
    assert.equal(await page.run(`document.querySelector('.mem-undo').hidden`), false);
    await click('.mem-undo');
    await page.until(`document.querySelectorAll('.mem-item').length === ${before}`, 'the note back');
    await settled();

    await page.run(`(() => { const find = document.querySelector('.mem-search'); find.value = 'miso'; find.dispatchEvent(new Event('input')); })()`);
    assert.equal(await count('.mem-text'), 1);
    await page.run(`(() => { const find = document.querySelector('.mem-search'); find.value = ''; find.dispatchEvent(new Event('input')); })()`);

    const logs = await count('.mem-log');
    await click('.mem-log');
    await page.until(`document.querySelectorAll('.mem-line').length > 0`, 'the chat log');
    await settled();
    await click('.mem-day-forget');
    assert.equal(await page.run(`document.querySelector('.mem-day-forget').textContent`), 'Tap again to forget this day');
    await click('.mem-day-forget');
    await page.until(`document.querySelectorAll('.mem-log').length === ${logs - 1}`, 'the day forgotten');

    const keeps = await page.run(`JSON.stringify([Object.keys(localStorage), Object.keys(sessionStorage), location.href])`);
    assert.ok(!/Rex|Miso|coffee|lofi/.test(keeps), 'nothing she remembers is kept in the browser');
    assert.equal(about > 0, true);

    await page.run(`(() => { Object.defineProperty(document, 'hidden', { configurable: true, get: () => true }); document.dispatchEvent(new Event('visibilitychange')); })()`);
    assert.equal(await count('.mem-item'), 0, 'her memory is cleared from the page when it is hidden');
    await page.run(`(() => { Object.defineProperty(document, 'hidden', { configurable: true, get: () => false }); document.dispatchEvent(new Event('visibilitychange')); })()`);
    await page.until(`document.querySelectorAll('.mem-item').length > 0`, 'her memory fetched again');

    await page.run(`document.querySelector('#memory').dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))`);
    assert.equal(await page.run(`document.querySelector('#memory').hidden`), true);
    assert.deepEqual(errors, []);
  } finally {
    if (page) await page.close();
    await site.close();
  }
});

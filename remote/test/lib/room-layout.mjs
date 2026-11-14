import assert from 'node:assert/strict';
import { picture } from './attach.mjs';

export async function* roomStates(r) {
  const { page, click } = r;
  const note = text => page.until('document.getElementById("room-voice-note").textContent === ' + JSON.stringify(text), text || 'clear voice note');
  await r.open();
  await page.until('CapriCall.listening', 'room listening');
  yield 'ready';
  await click('room-place');
  yield 'no-status';
  await click('room-close'); await r.open();
  await page.run('window.blockSound = true; contexts.at(-1).suspend()');
  await note('Tap Voice to hear her.');
  yield 'tap-voice';
  await page.run('window.blockSound = false; CapriCall.stopListening(); pc.noAnswer = true; window.roomTimeout = window.setTimeout; window.setTimeout = (fn, ms, ...args) => roomTimeout(fn, ms === 8000 ? 100 : ms, ...args); CapriRoom.startVoice()');
  await note('Your PC did not answer in time. Tap Voice to try again.');
  await page.run('window.setTimeout = roomTimeout; pc.noAnswer = false');
  yield 'no-answer';
  for (const [reason, text] of [
    ['not-allowed', 'Your PC is not taking calls. Tap Voice to try again.'],
    ['busy', "Your PC is on another phone's call. Tap Voice to try again."],
    ['ended', 'Your PC ended it. Tap Voice to try again.']
  ]) {
    await page.run('pc.answer = {type:"call.state",on:false,reason:' + JSON.stringify(reason) + '}; CapriRoom.startVoice()');
    await note(text);
    yield reason;
  }
  await page.run('pc.answer = {type:"call.state",on:true,voice:false,ears:false}; CapriRoom.startVoice()');
  await note('Her voice is off on your PC.');
  yield 'pc-voice-off';
  await page.run('CapriCall.stopListening()');
  await r.state('connected', { features: ['chat', 'pictures'] });
  await page.run('CapriRoom.startVoice()');
  await note('Her voice needs a newer Capri on your PC.');
  yield 'newer-pc';
  await page.run('pc.answer = {type:"call.state",on:true,voice:true,ears:true}');
  await r.state('connected', { features: ['chat', 'call', 'listen', 'pictures'] });
  await click('f-call');
  await page.until('CapriCall.state === "on" && CapriCall.stats().micLive', 'layout call');
  await page.run('CapriCall.room()');
  await note('');
  yield 'during-call';
}

export const roomPicture = async (page, on) => {
  if (on) {
    await picture(page);
    await page.until('CapriAttach.hasPicture() && !CapriAttach.busy', 'layout picture');
  } else await page.run('document.querySelector("#attach-preview button").click(); document.activeElement.blur()');
};

function measureRoom() {
  const round = value => Math.round(value * 1000) / 1000;
  const rect = box => Object.fromEntries(['left', 'top', 'right', 'bottom', 'width', 'height'].map(key => [key, round(box[key])]));
  const nodes = {
    room: '#room', view: '#room-view', status: '#room-status', note: '#room-voice-note',
    sit: '#room-sit', lie: '#room-lie', heart: '#room-love', voice: '#room-voice',
    ar: '#room-track', close: '#room-close', log: '#log', composer: '#composer',
    message: '#msg', actions: '#actions', callbar: '.callbar', preview: '#attach-preview'
  };
  const boxes = {}, problems = [];
  for (const [name, selector] of Object.entries(nodes)) {
    const node = document.querySelector(selector), box = node.getBoundingClientRect();
    const shown = box.width > 0 && box.height > 0;
    boxes[name] = { ...rect(box), shown };
    if (!shown || name === 'log') continue;
    if (box.left < 0 || box.right > innerWidth + .5 || box.top < 0 || box.bottom > innerHeight + .5)
      problems.push(name + ' outside viewport: ' + JSON.stringify(boxes[name]));
    for (let parent = node.parentElement; parent; parent = parent.parentElement) {
      const style = getComputedStyle(parent), bounds = parent.getBoundingClientRect();
      if (/(hidden|clip|auto|scroll)/.test(style.overflowY) && (box.top < bounds.top - .5 || box.bottom > bounds.bottom + .5))
        problems.push(name + ' clipped by ' + (parent.id || parent.tagName) + ': ' + JSON.stringify(rect(bounds)));
    }
    if (['sit', 'lie', 'heart', 'voice', 'ar', 'close', 'message', 'actions', 'callbar', 'preview'].includes(name)) {
      for (const x of [.15, .5, .85]) for (const y of [.15, .5, .85]) {
        const at = document.elementFromPoint(box.left + box.width * x, box.top + box.height * y);
        if (!at || !node.contains(at)) problems.push(name + ' covered at ' + x + ',' + y + ' by ' + (at && (at.id || at.className)));
      }
    }
    if (name === 'status' || name === 'note') {
      const range = document.createRange(); range.selectNodeContents(node);
      boxes[name].text = node.textContent;
      boxes[name].lines = [...range.getClientRects()].map(rect);
      for (const line of boxes[name].lines) {
        if (line.left < box.left - .5 || line.right > box.right + .5 || line.top < box.top - .5 || line.bottom > box.bottom + .5)
          problems.push(name + ' text outside its box: ' + JSON.stringify(line));
      }
    }
  }
  if (boxes.log.top < boxes.room.bottom - .5) problems.push('chat starts at ' + boxes.log.top + ' before room ends at ' + boxes.room.bottom);
  if (boxes.log.bottom > boxes.composer.top + .5) problems.push('chat ends at ' + boxes.log.bottom + ' after composer starts at ' + boxes.composer.top);
  if (boxes.composer.top < boxes.room.bottom - .5) problems.push('composer starts at ' + boxes.composer.top + ' before room ends at ' + boxes.room.bottom);
  const log = document.getElementById('log');
  const old = log.scrollTop; log.scrollTop = log.scrollHeight;
  const edge = boxes.room.bottom;
  for (const y of [edge - 1, edge, edge + 1]) for (const x of [17, innerWidth / 2, innerWidth - 17]) {
    const at = document.elementFromPoint(x, y);
    if (y < edge && at && log.contains(at)) problems.push('chat paints inside room at ' + x + ',' + y);
  }
  log.scrollTop = old;
  return { width: innerWidth, height: innerHeight, boxes, problems, chatClip: getComputedStyle(log).clipPath };
}

export const roomLayout = page => page.run('(' + measureRoom.toString() + ')()');

export const checkRoom = (layout, label, minView = 150) => {
  const { boxes } = layout;
  const problems = [...layout.problems];
  if (boxes.view.height < minView) problems.push('view height ' + boxes.view.height + ' < ' + minView);
  for (const name of ['sit', 'lie', 'heart', 'voice', 'ar', 'close', 'message', 'actions'])
    if (!boxes[name].shown) problems.push(name + ' is hidden: ' + JSON.stringify(boxes[name]));
  if (layout.chatClip === 'none') problems.push('chat has no clip at the room edge');
  assert.deepEqual(problems, [], label + '\n' + JSON.stringify(layout, null, 2));
};

(() => {
  'use strict';
  const SAVED = 'capri-remote-wake';
  const WAIT = 120000;
  const remote = window.CapriRemote;
  if (!remote) return;
  const byId = id => document.getElementById(id);
  const clip = (text, max) => typeof text === 'string' ? text.replace(/[\x00-\x1f\x7f‎‏‪-‮⁦-⁩]/g, ' ').trim().slice(0, max) : '';
  const macOk = mac => typeof mac === 'string' && /^[0-9A-F]{2}(:[0-9A-F]{2}){5}$/.test(mac) && mac !== '00:00:00:00:00:00';
  const castOk = ip => typeof ip === 'string' && /^(\d{1,3}\.){3}\d{1,3}$/.test(ip) && ip.split('.').every(part => Number(part) <= 255);
  const make = (tag, className, text) => {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text != null) node.textContent = text;
    return node;
  };

  const read = () => {
    try {
      const saved = JSON.parse(localStorage.getItem(SAVED) || 'null');
      return saved && Array.isArray(saved.cards) && saved.cards.length ? saved : null;
    } catch (error) { return null; }
  };
  const write = value => {
    try {
      if (value) localStorage.setItem(SAVED, JSON.stringify(value));
      else localStorage.removeItem(SAVED);
    } catch (error) {}
  };
  const tidy = message => {
    const cards = [];
    for (const card of Array.isArray(message.cards) ? message.cards.slice(0, 4) : []) {
      if (!card || !macOk(card.mac) || !castOk(card.broadcast) || cards.some(seen => seen.mac === card.mac)) continue;
      cards.push({ mac: card.mac, broadcast: card.broadcast, name: clip(card.name, 64), wired: card.wired !== false, ready: card.ready === 'on' || card.ready === 'off' ? card.ready : '' });
    }
    return cards;
  };

  const android = () => {
    const bridge = window.CapriAndroid;
    return bridge && typeof bridge.wake === 'function' ? bridge : null;
  };

  const button = byId('banner-wake');
  const banner = byId('banner');
  const title = byId('banner-title');
  const sub = byId('banner-sub');
  const sheet = byId('wake');
  const scrim = byId('scrim');
  const list = byId('wake-cards');
  const lead = byId('wake-lead');
  const note = byId('wake-note');
  let asked = false, waking = 0, wakeTimer = 0;

  const offline = () => remote.state === 'offline' && !remote.info.reason && !remote.info.demo && !remote.info.native;
  const render = () => {
    if (!button) return;
    button.hidden = !(offline() && read());
    if (button.hidden) return;
    if (waking && Date.now() - waking < WAIT) {
      title.textContent = 'Waking your PC';
      sub.textContent = 'Sent. Only works on the same network as your PC';
      banner.dataset.tone = 'wait';
    } else if (waking) {
      title.textContent = 'Still asleep';
      sub.textContent = 'Same network? Check Wake from phone on your PC';
    }
  };

  const say = (words, good) => {
    note.textContent = words;
    note.classList.toggle('good', !!good);
    note.hidden = !words;
  };

  const copy = async (text, label) => {
    try {
      await navigator.clipboard.writeText(text);
      say(label + ' copied', true);
    } catch (error) {
      say('Copy blocked here. Hold on it to select', false);
    }
  };

  const fill = saved => {
    list.replaceChildren();
    lead.textContent = android()
      ? 'Wake PC sends the wake signal to these from this phone.'
      : 'Phone browsers can\'t send the wake signal. Use Capri\'s Android app, or any Wake-on-LAN app, with these:';
    for (const card of saved.cards) {
      const item = make('li', 'wake-card');
      const head = make('p', 'wake-name', (card.name || 'Network card') + (card.wired ? '' : ' · Wi-Fi'));
      if (card.ready === 'off') head.append(make('span', 'wake-off', ' · not turned on yet'));
      const row = (label, value) => {
        const line = make('div', 'wake-row');
        const words = make('span', 'wake-label', label);
        const shown = make('code', 'wake-value', value);
        const key = make('button', 'nbtn', 'Copy');
        key.type = 'button';
        key.setAttribute('aria-label', 'Copy ' + label);
        key.addEventListener('click', () => copy(value, label));
        line.append(words, shown, key);
        return line;
      };
      item.append(head, row('MAC', card.mac), row('Broadcast', card.broadcast));
      list.append(item);
    }
  };

  const open = () => {
    const saved = read();
    if (!saved) return;
    say('', false);
    fill(saved);
    sheet.hidden = false;
    scrim.hidden = false;
    sheet.querySelector('.sheet-scroll').scrollTop = 0;
    byId('wake-done').focus({ preventScroll: true });
  };
  const close = () => {
    if (sheet.hidden) return;
    sheet.hidden = true;
    if (!byId('app').classList.contains('menu') && byId('home').hidden) scrim.hidden = true;
  };

  const send = () => {
    const saved = read();
    const bridge = android();
    if (!saved || !bridge) { open(); return; }
    let sent = 0;
    try { sent = Number(JSON.parse(bridge.wake(JSON.stringify(saved.cards.map(card => ({ mac: card.mac, broadcast: card.broadcast }))))).sent) || 0; } catch (error) { sent = 0; }
    if (!sent) { open(); say('Couldn\'t send it. Is Wi-Fi on?', false); return; }
    waking = Date.now();
    clearTimeout(wakeTimer);
    wakeTimer = setTimeout(render, WAIT + 100);
    render();
  };

  if (button) button.addEventListener('click', send);
  byId('wake-done').addEventListener('click', close);
  scrim.addEventListener('click', close);
  document.addEventListener('keydown', event => { if (event.key === 'Escape') close(); });

  remote.onMessage(message => {
    if (message.type !== 'wake.info') return;
    const cards = tidy(message);
    if (cards.length) write({ pc: clip(remote.info.pc, 64), os: clip(message.os, 16), cards, at: Date.now() });
    else write(null);
    render();
  });
  remote.onState((state, info) => {
    if (state === 'unpaired' || state === 'pairing') write(null);
    if (state === 'connected') {
      waking = 0;
      clearTimeout(wakeTimer);
      if (!asked && Array.isArray(info.features) && info.features.includes('wake')) asked = remote.send('wake.get', {});
    } else asked = false;
    if (state !== 'offline') close();
    render();
  });

  window.CapriWake = { saved: read, open, send };
})();

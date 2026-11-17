(() => {
  'use strict';
  const bridge = window.CapriAndroid;
  if (!bridge) return;
  const byId = id => document.getElementById(id);
  const read = raw => { try { return JSON.parse(raw) || {}; } catch (error) { return {}; } };
  let info = read(bridge.info());
  document.documentElement.dataset.android = 'app';

  const make = (tag, className, text) => {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text != null) node.textContent = text;
    return node;
  };

  const part = make('section', 'drawer-part');
  part.id = 'd-android';
  part.setAttribute('aria-labelledby', 'd-android-head');
  const head = make('h3', 'nhead', 'On this phone');
  head.id = 'd-android-head';
  const float = make('button', 'nbtn cyan wide');
  float.id = 'd-float';
  float.type = 'button';
  const floatNote = make('p', 'hint');
  floatNote.id = 'd-float-note';
  floatNote.setAttribute('role', 'status');
  part.append(head, float, floatNote);
  const phonePart = byId('d-phone-head')?.closest('.drawer-part');
  if (phonePart) phonePart.after(part);
  else byId('drawer')?.querySelector('.drawer-scroll')?.prepend(part);

  const render = () => {
    const on = info.bubble === true;
    float.setAttribute('aria-pressed', String(on));
    float.textContent = on ? 'Float on my screen: on' : 'Float on my screen: off';
    if (on && info.overlay === false) floatNote.textContent = 'Allow "Display over other apps" for Capri, then come back.';
    else if (on) floatNote.textContent = 'When you leave the app she floats over your other apps. Tap her to come back, drag her onto the X to put her away.';
    else floatNote.textContent = 'Let her float over your other apps, like on your PC.';
    ar.hidden = info.ar !== true;
  };
  float.addEventListener('click', () => {
    info = read(bridge.bubble(info.bubble !== true));
    render();
  });

  const menu = byId('room-menu');
  const ar = make('button', 'nbtn cyan', 'Room in AR');
  ar.id = 'room-ar';
  ar.type = 'button';
  ar.hidden = true;
  if (menu) menu.prepend(ar);
  ar.addEventListener('click', () => {
    byId('room-camera')?.click();
    if (!bridge.room()) {
      ar.hidden = true;
      byId('room-open')?.click();
    }
  });

  const refresh = () => { info = read(bridge.info()); render(); };
  window.addEventListener('capri-android', refresh);
  document.addEventListener('visibilitychange', () => { if (!document.hidden) refresh(); });
  render();

  const escape = () => document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
  window.CapriAndroidBack = () => {
    const app = byId('app');
    if (app?.classList.contains('menu')) { escape(); return true; }
    const home = byId('home');
    if (home && !home.hidden) { escape(); return true; }
    if (menu && !menu.hidden) { byId('room-camera')?.click(); return true; }
    const room = byId('room');
    if (room && !room.hidden) { byId('room-close')?.click(); return true; }
    return false;
  };

  const told = [];
  const tell = node => {
    const words = node.querySelector('.words')?.textContent?.trim();
    if (!words || told.includes(words)) return;
    told.push(words);
    if (told.length > 20) told.shift();
    try { bridge.said(words); } catch (error) {}
  };
  const live = new WeakSet();
  const log = byId('log-in');
  if (log) {
    new MutationObserver(changes => {
      for (const change of changes) {
        const node = change.target;
        if (change.type !== 'attributes' || !(node instanceof Element)) continue;
        if (!node.classList.contains('msg') || !node.classList.contains('capri')) continue;
        if (/\b(talking|thinking|quiet)\b/.test(change.oldValue || '')) live.add(node);
        if (node.classList.contains('done') && live.has(node)) { live.delete(node); tell(node); }
      }
    }).observe(log, { subtree: true, attributes: true, attributeFilter: ['class'], attributeOldValue: true });
  }
})();

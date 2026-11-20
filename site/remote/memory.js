/* Capri Remote's MEMORY (PROTOCOL.md 8.8): what she remembers about you, from your PC. The MEMORY tile opens a page like
   JOBS': her notes about you, her memories of past chats, what she learned about each game, and her daily chat logs.
   Edit a line, forget it, add one, forget a whole day, and undo what you just changed. It all lives on your PC.
   Nothing here is stored, shared or exported: what the PC sends stays in this page's variables and its rows, and is
   cleared when the page is hidden, the link drops, you unpair or you close it. Every word comes from the PC, so it's
   only ever set as text.
     CapriMemory.supported()  this session's PC offers memory
     CapriMemory.open()       the page, or false when the PC doesn't offer it
     CapriMemory.close()      back to wherever it was opened from */
(() => {
  'use strict';
  const remote = () => window.CapriRemote;
  const clip = (text, max) => typeof text === 'string' ? text.replace(/[\x00-\x1f\x7f‎‏‪-‮⁦-⁩]/g, ' ').slice(0, max) : '';
  const idOk = id => typeof id === 'string' && id.length > 0 && id.length <= 400;
  const dateOk = date => typeof date === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(date);
  const NOTES = 'M6.5 3.5h12v17h-12zM3.5 7.5h3M3.5 12h3M3.5 16.5h3M10 8h5M10 12h5M10 16h3';
  const make = (tag, className, text) => {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text != null) node.textContent = text;
    return node;
  };
  const icon = path => {
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('viewBox', '0 0 24 24');
    svg.setAttribute('aria-hidden', 'true');
    const line = document.createElementNS('http://www.w3.org/2000/svg', 'path');
    line.setAttribute('d', path);
    svg.appendChild(line);
    return svg;
  };
  const button = (className, text) => {
    const node = make('button', className, text);
    node.type = 'button';
    return node;
  };
  const supported = () => {
    const r = remote();
    return !!(r && r.state === 'connected' && !r.info.native && Array.isArray(r.info.features) && r.info.features.includes('memory'));
  };
  const dayName = date => {
    const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(date);
    if (!match) return date;
    const at = new Date(+match[1], +match[2] - 1, +match[3]);
    if (Number.isNaN(at.getTime())) return date;
    return at.toLocaleDateString([], { weekday: 'short', day: 'numeric', month: 'short' }).replace(',', '');
  };
  const count = (n, one, many) => n + ' ' + (n === 1 ? one : many);

  let data = null, day = null, shown = false, pending = null, focus = null, tile = null, query = '', editing = null, armed = '', armTimer = 0, toastTimer = 0, undoToken = '', counter = 0;
  const behind = new Map();

  const room = make('section', 'memory');
  room.id = 'memory';
  room.hidden = true;
  room.tabIndex = -1;
  room.setAttribute('aria-labelledby', 'memory-title');
  const top = make('div', 'mem-top');
  const title = make('h2', 'mem-title', 'Her memory');
  title.id = 'memory-title';
  const shut = button('nbtn icon mem-close');
  shut.id = 'memory-close';
  shut.setAttribute('aria-label', 'Close Her memory');
  shut.append(icon('M5.5 5.5l13 13M18.5 5.5l-13 13'));
  top.append(title, shut);

  const scroll = make('div', 'mem-scroll');
  const lead = make('p', 'mem-lead', 'What Capri remembers about you. It lives on your PC.');
  const note = make('p', 'mem-note');
  note.setAttribute('role', 'status');
  note.hidden = true;
  const adder = make('form', 'mem-add');
  adder.autocomplete = 'off';
  const addInput = make('input', 'mem-input');
  addInput.type = 'text';
  addInput.maxLength = 2000;
  addInput.placeholder = 'Tell her something to remember';
  addInput.setAttribute('aria-label', 'Tell her something to remember');
  addInput.enterKeyHint = 'done';
  const addKey = button('nbtn cyan mem-add-key', 'Add');
  addKey.type = 'submit';
  adder.append(addInput, addKey);
  const finder = make('input', 'mem-input mem-search');
  finder.type = 'search';
  finder.placeholder = 'Search';
  finder.setAttribute('aria-label', 'Search her memory');
  finder.autocomplete = 'off';
  const body = make('div', 'mem-body');
  scroll.append(lead, adder, finder, body);

  const dayView = make('div', 'mem-scroll mem-dayview');
  dayView.hidden = true;
  const dayBar = make('div', 'mem-daybar');
  const back = button('nbtn mem-back', 'Back');
  const dayTitle = make('h3', 'mem-daytitle');
  dayBar.append(back, dayTitle);
  const dayNote = make('p', 'mem-faint');
  dayNote.hidden = true;
  const transcript = make('ol', 'mem-lines');
  const dayForget = button('nbtn hot wide mem-day-forget', 'Forget this day');
  dayView.append(dayBar, dayNote, transcript, dayForget);

  const toast = make('div', 'mem-toast');
  toast.hidden = true;
  toast.setAttribute('role', 'status');
  const toastText = make('span', 'mem-toast-text');
  const undoKey = button('nbtn cyan mem-undo', 'Undo');
  toast.append(toastText, undoKey);
  room.append(top, note, scroll, dayView, toast);

  const tell = text => { note.textContent = text; note.hidden = !text; };
  const hideToast = () => {
    clearTimeout(toastTimer);
    toast.hidden = true;
    toastText.textContent = '';
    undoToken = '';
  };
  const showToast = (say, undo) => {
    clearTimeout(toastTimer);
    toastText.textContent = say;
    undoToken = undo;
    undoKey.hidden = !undo;
    undoKey.disabled = !!pending;
    toast.hidden = !say;
    toastTimer = setTimeout(hideToast, 8000);
  };
  const disarm = () => {
    clearTimeout(armTimer);
    if (!armed) return;
    armed = '';
    render();
  };
  const arm = name => {
    if (armed === name) { clearTimeout(armTimer); armed = ''; return true; }
    armed = name;
    clearTimeout(armTimer);
    armTimer = setTimeout(disarm, 3500);
    render();
    return false;
  };

  const wipeLocal = () => {
    data = null;
    day = null;
    editing = null;
    query = '';
    finder.value = '';
    addInput.value = '';
    clearTimeout(armTimer);
    armed = '';
    if (pending) clearTimeout(pending.timer);
    pending = null;
    hideToast();
    tell('');
    dayNote.hidden = true;
    body.replaceChildren();
    transcript.replaceChildren();
    dayView.hidden = true;
    scroll.hidden = false;
  };

  const fresh = () => 'm' + (++counter).toString(36) + Math.random().toString(36).slice(2, 10);
  const ask = (type, body, extra) => {
    if (!supported() || pending) return false;
    const id = type === 'memory.get' ? '' : fresh();
    const sent = type === 'memory.get' ? {} : Object.assign({ id }, body);
    if (!remote().send(type, sent)) { tell('Could not reach your PC. Try again.'); return false; }
    tell('');
    pending = Object.assign({ id, type, timer: setTimeout(() => {
      pending = null;
      tell('No answer from your PC. Try again.');
      render();
    }, 10000) }, extra);
    render();
    return true;
  };
  const clearPending = () => {
    if (pending) clearTimeout(pending.timer);
    pending = null;
  };

  const readItems = list => Array.isArray(list) ? list.slice(0, 400).filter(row => row && idOk(row.id) && typeof row.text === 'string').map(row => ({ id: row.id, text: clip(row.text, 400) })) : [];
  const readList = message => {
    const chats = Array.isArray(message.chats) ? message.chats.slice(0, 200).filter(row => row && typeof row.day === 'string').map(row => ({ day: clip(row.day, 80), items: readItems(row.items) })) : [];
    const games = Array.isArray(message.games) ? message.games.slice(0, 100).filter(row => row && typeof row.game === 'string').map(row => ({ game: clip(row.game, 80), items: readItems(row.items) })) : [];
    const days = Array.isArray(message.days) ? message.days.slice(0, 400).filter(row => row && dateOk(row.date)).map(row => ({ date: row.date, lines: Number.isSafeInteger(row.lines) && row.lines >= 0 ? row.lines : 0 })) : [];
    return { keeping: message.keeping !== false, private: message.private === true, cut: message.cut === true, about: readItems(message.about), chats, games, days };
  };
  const readLines = list => Array.isArray(list) ? list.slice(0, 2000).filter(row => row && typeof row.text === 'string').map(row => ({ at: clip(row.at, 40), who: clip(row.who, 20), text: clip(row.text, 2000) })) : [];
  const clock = at => {
    const parsed = typeof at === 'string' && at ? new Date(at) : null;
    if (parsed && !Number.isNaN(parsed.getTime())) return parsed.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
    return clip(at, 8);
  };

  const matches = text => !query || text.toLowerCase().includes(query);
  const entry = (row, kind) => {
    const item = make('li', 'mem-item');
    const busy = !!pending;
    if (editing && editing.item === row.id && editing.kind === kind) {
      const box = make('textarea', 'mem-edit');
      box.value = editing.draft;
      box.rows = 3;
      box.maxLength = 2000;
      box.setAttribute('aria-label', 'Edit this memory');
      box.addEventListener('input', () => { editing.draft = box.value; });
      box.addEventListener('keydown', event => {
        if (event.key === 'Escape') { event.preventDefault(); event.stopPropagation(); editing = null; render(); }
      });
      const keys = make('div', 'mem-keys');
      const save = button('nbtn cyan', 'Save');
      const cancel = button('nbtn', 'Cancel');
      save.disabled = busy;
      save.addEventListener('click', () => ask('memory.edit', { item: row.id, text: editing.draft.trim() }, { edit: row.id }));
      cancel.addEventListener('click', () => { editing = null; render(); });
      keys.append(save, cancel);
      item.append(box, keys);
      item.dataset.editing = 'true';
      return item;
    }
    const text = button('mem-text', row.text);
    text.setAttribute('aria-label', 'Edit: ' + row.text);
    text.disabled = busy;
    text.addEventListener('click', () => { editing = { item: row.id, kind, draft: row.text }; render(); });
    const forget = button('nbtn icon mem-forget');
    forget.setAttribute('aria-label', 'Forget: ' + row.text);
    forget.append(icon('M5.5 5.5l13 13M18.5 5.5l-13 13'));
    forget.disabled = busy;
    forget.addEventListener('click', () => ask('memory.forget', { item: row.id }));
    item.append(text, forget);
    return item;
  };
  const group = (heading, rows, kind, emptyText, extra) => {
    const section = make('section', 'mem-section');
    const head = make('h3', 'nhead', heading);
    const list = make('ul', 'mem-list');
    const shownRows = rows.filter(row => matches(row.text));
    if (shownRows.length) list.append(...shownRows.map(row => entry(row, kind)));
    else list.append(make('li', 'mem-empty', rows.length ? 'No match' : emptyText));
    section.append(head, list);
    if (extra) section.append(extra);
    return section;
  };
  const subhead = text => make('h4', 'mem-sub', text);

  const paint = () => {
    const busy = !!pending;
    addKey.disabled = busy;
    addInput.disabled = busy;
    undoKey.disabled = busy;
    dayForget.disabled = busy;
    back.disabled = busy;
    dayForget.textContent = armed === 'day' ? 'Tap again to forget this day' : 'Forget this day';
    dayForget.dataset.armed = armed === 'day' ? 'true' : '';
    if (day) {
      dayView.hidden = false;
      scroll.hidden = true;
      dayTitle.textContent = dayName(day.date);
      dayNote.textContent = day.ok === false ? 'That day is no longer on your PC.' : day.cut ? 'Only the newest part of this day fits here. The rest is on your PC.' : '';
      dayNote.hidden = !dayNote.textContent;
      if (day.lines) {
        const rows = day.lines.length ? day.lines.map(line => {
          const row = make('li', 'mem-line');
          row.dataset.who = line.who ? (/^capri$/i.test(line.who) ? 'capri' : 'you') : 'act';
          const meta = make('div', 'mem-line-meta');
          const when = clock(line.at);
          meta.append(make('span', 'mem-who', line.who || 'Capri did'));
          if (when) meta.append(make('span', 'mem-at', when));
          row.append(meta, make('p', 'mem-line-text', line.text));
          return row;
        }) : [make('li', 'mem-empty', 'Nothing in this day')];
        transcript.replaceChildren(...rows);
      } else transcript.replaceChildren(make('li', 'mem-empty', pending ? 'Asking your PC' : 'Nothing in this day'));
      return;
    }
    dayView.hidden = true;
    scroll.hidden = false;
    if (!data) {
      body.replaceChildren(make('p', 'mem-empty', pending ? 'Asking your PC' : supported() ? 'Nothing loaded' : 'Waiting for your PC'));
      return;
    }
    const parts = [];
    if (!data.keeping) parts.push(make('p', 'mem-note', 'Remember our chats is off on your PC, so she isn\'t keeping new chat memories.'));
    parts.push(group('About you', data.about, 'about', 'Nothing yet. Add something above.'));
    const chatRows = [];
    const chatSection = make('section', 'mem-section');
    chatSection.append(make('h3', 'nhead', 'From your chats'));
    const days = [...data.chats].reverse();
    let any = false;
    for (const chat of days) {
      const rows = chat.items.filter(row => matches(row.text));
      if (!rows.length) continue;
      any = true;
      const list = make('ul', 'mem-list');
      list.append(...rows.map(row => entry(row, 'chat')));
      chatRows.push(subhead(dayName(chat.day)), list);
    }
    if (any) chatSection.append(...chatRows);
    else chatSection.append(make('p', 'mem-empty', data.chats.some(chat => chat.items.length) ? 'No match' : 'Nothing yet. She adds to this as you chat.'));
    parts.push(chatSection);
    const gameSection = make('section', 'mem-section');
    gameSection.append(make('h3', 'nhead', 'Games'));
    let anyGame = false;
    for (const game of data.games) {
      const rows = game.items.filter(row => matches(row.text) || matches(game.game));
      if (!rows.length) continue;
      anyGame = true;
      const list = make('ul', 'mem-list');
      list.append(...rows.map(row => entry(row, 'game:' + game.game)));
      gameSection.append(subhead(game.game), list);
    }
    if (!anyGame) gameSection.append(make('p', 'mem-empty', data.games.some(game => game.items.length) ? 'No match' : 'Nothing yet. She learns games as you play.'));
    if (data.games.length) {
      const wipeGames = button('nbtn hot mem-wipe-games', armed === 'games' ? 'Tap again to forget game notes' : 'Forget game notes');
      wipeGames.disabled = busy;
      wipeGames.addEventListener('click', () => { if (arm('games')) ask('memory.wipe', { what: 'games' }); });
      gameSection.append(wipeGames);
    }
    parts.push(gameSection);
    const logSection = make('section', 'mem-section');
    logSection.append(make('h3', 'nhead', 'Chat logs'));
    const logs = data.days.filter(row => !query || dayName(row.date).toLowerCase().includes(query) || row.date.includes(query));
    if (logs.length) {
      const list = make('ul', 'mem-list');
      for (const row of logs) {
        const item = make('li', 'mem-item');
        const open = button('mem-log');
        open.append(make('span', 'mem-log-date', dayName(row.date)), make('span', 'mem-log-count', count(row.lines, 'line', 'lines')));
        open.setAttribute('aria-label', dayName(row.date) + ', ' + count(row.lines, 'line', 'lines'));
        open.disabled = busy;
        open.addEventListener('click', () => {
          day = { date: row.date, lines: null, cut: false, ok: true };
          if (!ask('memory.day', { date: row.date })) day = null;
        });
        item.append(open);
        list.append(item);
      }
      logSection.append(list);
    } else logSection.append(make('p', 'mem-empty', data.days.length ? 'No match' : 'Nothing yet. Chat logs appear after you talk.'));
    parts.push(logSection);
    if (data.cut) parts.push(make('p', 'mem-faint', 'Older entries are on your PC.'));
    const all = button('nbtn hot wide mem-wipe', armed === 'all' ? 'Tap again to forget everything' : 'Forget everything about me');
    all.disabled = busy;
    all.addEventListener('click', () => { if (arm('all')) ask('memory.wipe', { what: 'all' }); });
    parts.push(all);
    body.replaceChildren(...parts);
    const box = body.querySelector('.mem-edit');
    if (box && shown && document.activeElement !== box && !room.contains(document.activeElement)) box.focus({ preventScroll: true });
  };

  const render = () => {
    paint();
    if (shown && !room.contains(document.activeElement)) room.focus({ preventScroll: true });
  };

  adder.addEventListener('submit', event => {
    event.preventDefault();
    const text = addInput.value.trim();
    if (text) ask('memory.add', { text }, { add: true });
  });
  finder.addEventListener('input', () => {
    query = clip(finder.value, 80).trim().toLowerCase();
    if (!day) render();
  });
  undoKey.addEventListener('click', () => {
    if (undoToken) ask('memory.undo', { undo: undoToken });
  });
  back.addEventListener('click', () => {
    if (pending) return;
    day = null;
    disarm();
    render();
  });
  dayForget.addEventListener('click', () => {
    if (day && arm('day')) ask('memory.forget-day', { date: day.date }, { forgetDay: day.date });
  });

  const close = () => {
    if (!shown) return;
    shown = false;
    room.hidden = true;
    for (const [node, inert] of behind) node.inert = inert;
    behind.clear();
    wipeLocal();
    if (focus && focus.isConnected && !focus.hidden) focus.focus({ preventScroll: true });
  };
  const open = () => {
    if (!supported()) return false;
    focus = document.activeElement;
    const app = document.getElementById('app');
    if (!shown) for (const node of app.children) if (node !== room) { behind.set(node, node.inert); node.inert = true; }
    shown = true;
    room.hidden = false;
    render();
    ask('memory.get', {});
    shut.focus({ preventScroll: true });
    return true;
  };
  shut.addEventListener('click', close);
  room.addEventListener('keydown', event => {
    if (event.key === 'Escape') {
      event.preventDefault();
      if (editing) { editing = null; render(); }
      else if (day && !pending) { day = null; disarm(); render(); }
      else if (!day) close();
    }
    if (event.key !== 'Tab') return;
    const keys = [...room.querySelectorAll('button:not(:disabled), input:not(:disabled), textarea:not(:disabled)')].filter(node => node.getClientRects().length);
    if (!keys.length) return;
    const first = keys[0], last = keys[keys.length - 1];
    if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
  });

  const heard = message => {
    if (!shown || !supported()) return;
    if (message.type === 'memory.list') {
      data = readList(message);
      if (pending && pending.type === 'memory.get') clearPending();
      if (editing && !(editing.kind === 'about' ? data.about : editing.kind === 'chat' ? data.chats.flatMap(chat => chat.items) : (data.games.find(game => 'game:' + game.game === editing.kind) || { items: [] }).items).some(row => row.id === editing.item)) editing = null;
      render();
    } else if (message.type === 'memory.done') {
      if (!pending || message.re !== pending.id) return;
      const was = pending;
      clearPending();
      const say = clip(message.say, 200);
      const undone = was.type === 'memory.undo';
      if (message.ok === true) {
        if (was.add) addInput.value = '';
        if (was.edit) editing = null;
        if (was.forgetDay) day = null;
        const token = !undone && typeof message.undo === 'string' ? message.undo : '';
        showToast(say || (undone ? 'Put back' : 'Done'), token);
        tell('');
      } else {
        tell(say || 'That did not go through. Try again.');
        if (was.forgetDay) disarm();
      }
      render();
    } else if (message.type === 'memory.chat') {
      if (!pending || message.re !== pending.id || !day || day.date !== message.date) return;
      clearPending();
      if (message.ok === true) {
        day.lines = readLines(message.lines);
        day.cut = message.cut === true;
        day.ok = true;
      } else {
        day = null;
        tell('That day is no longer on your PC.');
      }
      render();
    } else if (message.type === 'error' && pending && message.re === pending.id) {
      clearPending();
      if (day && !day.lines) day = null;
      tell(message.code === 'not-allowed' ? 'Your PC isn\'t keeping notes right now.' : 'Your PC could not do that. Try again.');
      render();
    }
  };
  const leave = () => {
    wipeLocal();
    if (shown) render();
  };
  const mount = () => {
    const app = document.getElementById('app');
    tile = document.getElementById('f-memory');
    if (!app || !tile || !remote()) return;
    app.append(room);
    tile.addEventListener('click', open);
    remote().onMessage(heard);
    remote().onState(state => {
      const on = supported();
      tile.setAttribute('aria-disabled', String(!on));
      tile.setAttribute('aria-label', on ? 'Memory: what she remembers about you' : 'Memory, coming soon');
      tile.querySelector('.soon').hidden = on;
      tile.classList.remove('saying');
      if (!on) leave();
      if (state === 'unpaired' || (state === 'connected' && !on)) close();
      else if (on && shown && !data && !pending) ask('memory.get', {});
    });
    document.addEventListener('visibilitychange', () => {
      if (document.hidden) leave();
      else if (shown && supported()) { render(); ask('memory.get', {}); }
    });
  };
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', mount, { once: true });
  else mount();
  window.CapriMemory = { supported, open, close };
})();

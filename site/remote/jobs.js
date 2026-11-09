/* Capri Remote's JOBS (PROTOCOL.md 8.4): her PC jobs, whoever started them, as they happen. The JOBS tile opens a page
   like SCREEN's: the job running now with its steps, her question with Allow and Deny while she's holding an action for
   a yes, Stop, and her last ten jobs. While one runs, the tile gets a mark, and the call gets a round key that opens it.
   Labels come from the PC already cut down to what's safe to show: never what she typed.
     CapriJobs.supported()  this session's PC offers jobs
     CapriJobs.open()       the page, or false when the PC doesn't offer it
     CapriJobs.close()      back to wherever it was opened from */
(() => {
  'use strict';
  const remote = () => window.CapriRemote;
  const states = { running: 'Running', asking: 'Asking', done: 'Done', failed: 'Failed', stopped: 'Stopped' };
  const live = state => state === 'running' || state === 'asking';
  const idOk = id => typeof id === 'string' && /^[A-Za-z0-9_-]{1,64}$/.test(id);
  const clip = (text, max) => typeof text === 'string' ? text.replace(/[\x00-\x1f\x7f‎‏‪-‮⁦-⁩]/g, ' ').slice(0, max) : '';
  const LIST = 'M3.5 6.5l1.75 1.75L8.5 5M3.5 12.5l1.75 1.75L8.5 11M4 17.5h4v4H4zM11.5 6.75h9M11.5 12.75h9M11.5 19.5h6';
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
    return !!(r && r.state === 'connected' && Array.isArray(r.info.features) && r.info.features.includes('jobs'));
  };
  let current = null, recent = [], shown = false, waiting = '', timer = 0, focus = null, tile = null;
  const records = new Map(), trail = new Map(), behind = new Map();

  // ---------- The page: the job now, then Recent ----------
  const room = make('section', 'jobs');
  room.id = 'jobs';
  room.hidden = true;
  room.tabIndex = -1;
  room.setAttribute('aria-labelledby', 'jobs-title');
  const top = make('div', 'jobs-top');
  const title = make('h2', 'jobs-title', 'Jobs');
  title.id = 'jobs-title';
  const shut = button('nbtn icon jobs-close');
  shut.setAttribute('aria-label', 'Close Jobs');
  shut.append(icon('M5.5 5.5l13 13M18.5 5.5l-13 13'));
  top.append(title, shut);
  const scroll = make('div', 'jobs-scroll');
  const note = make('p', 'jobs-note');
  note.setAttribute('role', 'status');
  note.hidden = true;
  const empty = make('p', 'jobs-empty', 'No job running');
  const card = make('article', 'job-live');
  card.setAttribute('aria-label', 'Current job');
  const head = make('div', 'job-head');
  const task = make('h3', 'job-task');
  const chip = make('span', 'job-state');
  chip.setAttribute('role', 'status');
  head.append(task, chip);
  const steps = make('ol', 'job-steps');
  steps.setAttribute('aria-label', 'Job steps');
  // Her question rides with the keys that answer it, so it's never scrolled away under them.
  const controls = make('div', 'job-controls');
  const question = make('p', 'job-question');
  const answers = make('div', 'job-answers');
  const allow = button('nbtn cyan job-allow', 'Allow');
  const deny = button('nbtn job-deny', 'Deny');
  answers.append(allow, deny);
  const stop = button('nbtn hot wide job-stop', 'Stop');
  controls.append(question, answers, stop);
  card.append(head, steps, controls);
  const recentTitle = make('h3', 'nhead jobs-recent-title', 'Recent');
  const list = make('ul', 'job-recent');
  scroll.append(note, empty, card, recentTitle, list);
  room.append(top, scroll);
  // The call's key, round like the call's others, next to its screen key.
  const callKey = button('glyph call-jobs');
  callKey.hidden = true;
  callKey.append(icon(LIST));

  const time = at => {
    if (!Number.isFinite(at) || at <= 0) return '';
    return new Date(at).toLocaleString([], { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' });
  };
  const read = row => {
    if (!row || !idOk(row.id) || !Object.hasOwn(states, row.state) || !Number.isSafeInteger(row.started) || row.started <= 0 || row.started > 8640000000000000 || !Number.isSafeInteger(row.steps) || row.steps < 0) return null;
    return { id: row.id, task: clip(row.task, 200) || 'Using your computer', state: row.state, steps: row.steps, started: row.started, ended: Number.isSafeInteger(row.ended) && row.ended > 0 && row.ended <= 8640000000000000 ? row.ended : 0, ask: clip(row.ask, 200), last: clip(row.last, 120) };
  };
  const recentRow = row => {
    const item = make('li', 'job-recent-item');
    const heading = make('div', 'job-head');
    const state = make('span', 'job-state', states[row.state]);
    state.dataset.state = row.state;
    heading.append(make('h4', 'job-recent-task', row.task), state);
    const when = make('time', '', time(row.ended || row.started));
    when.dateTime = new Date(row.ended || row.started).toISOString();
    const meta = make('p', 'job-meta');
    meta.append(when, document.createTextNode(' · ' + row.steps + (row.steps === 1 ? ' step' : ' steps')));
    item.append(heading, meta);
    if (row.last) item.append(make('p', 'job-last', row.last));
    return item;
  };
  const render = () => {
    const now = supported() && current && live(current.state) ? current.state : '';
    // The tile's mark and the call's key: cyan while she works, yellow while she waits for a yes.
    if (tile) {
      tile.dataset.job = now;
      if (supported()) tile.setAttribute('aria-label', now === 'asking' ? 'Jobs: she needs your yes' : now ? 'Jobs: one running' : 'Jobs: follow and stop her PC jobs');
    }
    callKey.hidden = !now;
    callKey.dataset.job = now;
    callKey.setAttribute('aria-label', now === 'asking' ? 'Her job: she needs your yes' : 'Her job: follow it or stop it');
    card.hidden = !now;
    empty.hidden = !!now;
    if (now) {
      task.textContent = current.task;
      task.title = current.task;
      chip.textContent = states[current.state];
      chip.dataset.state = current.state;
      const asking = now === 'asking';
      question.hidden = answers.hidden = !asking;
      question.textContent = current.ask || 'Allow this action?';
      // The steps follow the newest one, unless they've scrolled back to read.
      const following = steps.scrollHeight - steps.clientHeight - steps.scrollTop < 48;
      const was = steps.scrollTop;
      const children = [...trail.values()].sort((a, b) => a.n - b.n).map(step => {
        const node = make('li', 'job-step', step.label);
        node.dataset.n = step.n;
        node.value = step.n;
        node.dataset.state = step.failed ? 'failed' : step.done ? 'done' : 'running';
        node.setAttribute('aria-label', (step.failed ? 'Failed: ' : step.done ? 'Done: ' : 'Running: ') + step.label);
        return node;
      });
      steps.replaceChildren(...children);
      steps.hidden = !children.length;
      steps.scrollTop = following ? steps.scrollHeight : was;
      stop.disabled = allow.disabled = deny.disabled = !!waiting || !supported();
    }
    const rows = recent.filter(row => !now || row.id !== current.id);
    list.replaceChildren(...(rows.length ? rows.map(recentRow) : [make('li', 'jobs-empty', 'No recent jobs')]));
  };
  const tell = text => { note.textContent = text; note.hidden = !text; };
  const clearWait = () => { clearTimeout(timer); waiting = ''; };
  // One Stop or answer at a time. The PC's next job.state for it ends the wait; nothing back in 10 s says so.
  const send = (type, body) => {
    if (!supported() || !current || waiting) return;
    if (!remote().send(type, body)) { tell('Could not reach your PC. Try again.'); return; }
    waiting = current.id;
    tell(type === 'job.stop' ? 'Stopping the job' : 'Sending your answer');
    timer = setTimeout(() => { clearWait(); tell('No answer from your PC. Try again.'); render(); }, 10000);
    render();
  };
  stop.addEventListener('click', () => current && send('job.stop', { id: current.id }));
  allow.addEventListener('click', () => current && send('job.answer', { id: current.id, yes: true }));
  deny.addEventListener('click', () => current && send('job.answer', { id: current.id, yes: false }));

  // The page covers the app, the call included, and keeps focus inside it until it closes.
  const close = () => {
    if (!shown) return;
    shown = false;
    room.hidden = true;
    for (const [node, inert] of behind) node.inert = inert;
    behind.clear();
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
    steps.scrollTop = steps.scrollHeight;
    remote().send('job.list', {});
    shut.focus({ preventScroll: true });
    return true;
  };
  shut.addEventListener('click', close);
  room.addEventListener('keydown', event => {
    if (event.key === 'Escape') { event.preventDefault(); close(); }
    if (event.key !== 'Tab') return;
    const keys = [...room.querySelectorAll('button:not(:disabled)')].filter(node => node.getClientRects().length);
    const first = keys[0], last = keys[keys.length - 1];
    if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
  });
  callKey.addEventListener('click', open);

  const heard = message => {
    if (!supported()) return;
    if (message.type === 'job.state') {
      const row = read(message);
      if (!row) return;
      const was = records.get(row.id);
      // A late running or asking for a job that's already over is old news.
      if (was && !live(was.state) && live(row.state)) return;
      row.last = (was && was.last) || (recent.find(old => old.id === row.id) || {}).last || row.last;
      if (waiting === row.id && (!was || row.state !== was.state)) { clearWait(); tell(''); }
      records.set(row.id, row);
      if (records.size > 10) records.delete(records.keys().next().value);
      recent = [row, ...recent.filter(old => old.id !== row.id)].sort((a, b) => b.started - a.started).slice(0, 10);
      if (live(row.state) && (!current || row.started >= current.started)) {
        if (!current || current.id !== row.id) trail.clear();
        current = row;
      } else if (current && current.id === row.id) {
        current = null;
        trail.clear();
        if (shown) remote().send('job.list', {});
      }
    } else if (message.type === 'job.step') {
      if (!current || message.id !== current.id || !Number.isSafeInteger(message.n) || message.n < 1 || typeof message.label !== 'string') return;
      const step = { n: message.n, label: clip(message.label, 120), done: message.done === true, failed: message.failed === true };
      trail.set(step.n, step);
      if (trail.size > 30) trail.delete(Math.min(...trail.keys()));
      const last = [...trail.values()].sort((a, b) => b.n - a.n)[0];
      current.last = last.label;
      recent = recent.map(row => row.id === current.id ? Object.assign({}, row, { last: last.label }) : row);
    } else if (message.type === 'job.recent') {
      if (!Array.isArray(message.list)) return;
      recent = message.list.map(read).filter(Boolean).sort((a, b) => b.started - a.started).slice(0, 10);
    } else if (message.type === 'error' && waiting && message.re === waiting) {
      clearWait();
      tell('That job already changed on your PC.');
    } else return;
    render();
  };
  const mount = () => {
    const app = document.getElementById('app');
    tile = document.getElementById('f-jobs');
    if (!app || !tile || !remote()) return;
    app.append(room);
    const screenKey = app.querySelector('.call-top .call-screen');
    if (screenKey) screenKey.after(callKey);
    tile.addEventListener('click', open);
    remote().onMessage(heard);
    remote().onState(() => {
      const on = supported();
      tile.setAttribute('aria-disabled', String(!on));
      tile.setAttribute('aria-label', on ? 'Jobs: follow and stop her PC jobs' : 'Jobs, coming soon');
      tile.querySelector('.soon').hidden = on;
      tile.classList.remove('saying');
      clearWait();
      current = null;
      recent = [];
      records.clear();
      trail.clear();
      tell('');
      if (!on) close();
      else if (shown) remote().send('job.list', {});
      render();
    });
  };
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', mount, { once: true });
  else mount();
  window.CapriJobs = { supported, open, close };
})();

// The Roadmap page: Capri's GitHub issues sorted into lanes. It reads GitHub live, keeps that for ten minutes,
// and falls back to issues.json (made by ci/roadmap-snapshot.py) when GitHub can't be reached.
(() => {
  'use strict';
  const repo = 'lachydotmcg/capri-desktop';
  const api = 'https://api.github.com/repos/' + repo + '/issues';
  const issue = 'https://github.com/' + repo + '/issues/';
  const root = document.documentElement;
  root.classList.remove('no-js');
  root.classList.add('js');
  const $ = (query, el = document) => el.querySelector(query);
  const $$ = (query, el = document) => [...el.querySelectorAll(query)];
  const still = matchMedia('(prefers-reduced-motion: reduce)');
  const day = new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short' });
  const clock = new Intl.DateTimeFormat('en-AU', { hour: 'numeric', minute: '2-digit' });

  // Only labelled issues go on the board. A label is Lachy's say-so, so a stray question or spam never shows here.
  const skip = new Set(['question', 'duplicate', 'invalid', 'wontfix']);
  const kindOf = labels => labels.includes('bug') ? 'bug' : labels.includes('enhancement') ? 'feature' : labels.includes('documentation') ? 'docs' : 'task';
  const kindName = { feature: 'Feature', bug: 'Bug', docs: 'Docs', task: 'Task' };
  const laneOf = item => item.l.includes('high priority') ? 'fix' : item.l.includes('roadmap') ? 'plan' : item.l.includes('help wanted') ? 'grabs' : 'plan';
  const lachy = 'lachydotmcg';

  // ---------- Credit: who suggested it, who's on it and who built it ----------
  const githubName = /^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})$/;
  const loginOf = person => person && typeof person.login === 'string' && githubName.test(person.login) ? person.login : '';
  // A PR that says "closes #12" (or fixes, resolves) credits its author on #12: open PRs on open issues, merged PRs on shipped ones.
  const closing = /\b(?:close[sd]?|fix(?:e[sd])?|resolve[sd]?)\b:?\s*(?:lachydotmcg\/capri-desktop)?#(\d+)/gi;
  const prLinks = items => {
    const links = new Map();
    for (const item of items) {
      if (!item || !item.pull_request) continue;
      const author = loginOf(item.user);
      if (!author || (item.state === 'closed' && !item.pull_request.merged_at)) continue;
      for (const match of (String(item.title || '') + '\n' + String(item.body || '')).matchAll(closing)) {
        const list = links.get(+match[1]) || [];
        if (!list.includes(author)) list.push(author);
        links.set(+match[1], list);
      }
    }
    return links;
  };
  // Lachy notes where an idea came from in the issue itself, as "Suggested by AslanBot on Discord." or "Suggested on Discord."
  const suggestedLine = body => {
    const match = /^[ \t]*Suggested\b[^\n]{0,160}/imu.exec(String(body || ''));
    return match ? match[0].trim() : '';
  };
  // The names are Discord display names, so spaces and fancy Unicode letters stay ("john sandevistan").
  // A server tag like [SAVE] and a leading @ come off, and a name with markup characters is left out.
  const displayName = raw => {
    const name = raw.replace(/\[[^\]]*\]/g, ' ').trim().replace(/^@+/, '').replace(/\s+/g, ' ').trim();
    const length = [...name].length;
    return length >= 2 && length <= 32 && !/[<>()[\]{}#:\/\\|*`]/.test(name) ? name : '';
  };
  const ideaFrom = line => {
    const match = /^Suggested(?: by (.+?))?(?: (?:on|in|from) ([A-Za-z]+))?\.?$/iu.exec(line || '');
    if (!match) return null;
    const names = (match[1] || '').split(/\s*,\s*|\s+and\s+|\s*&\s*/).map(displayName).filter(Boolean);
    return names.length || match[2] ? { names, where: match[2] || '' } : null;
  };

  const keep = item => item && !item.pull_request && Number.isInteger(item.number) && typeof item.title === 'string' && Array.isArray(item.labels) && item.labels.length > 0 && !item.labels.some(label => !label || skip.has(label.name));
  const trim = (item, links) => ({
    n: item.number,
    t: item.title,
    l: item.labels.map(label => String(label.name)),
    a: (Array.isArray(item.assignees) ? item.assignees : []).map(loginOf).filter(Boolean),
    c: Number.isInteger(item.comments) ? item.comments : 0,
    at: item.created_at,
    done: item.closed_at || null,
    u: loginOf(item.user),
    p: links.get(item.number) || [],
    s: suggestedLine(item.body)
  });
  const sane = item => item && Number.isInteger(item.n) && item.n > 0 && typeof item.t === 'string' && Array.isArray(item.l) && Array.isArray(item.a);
  const tidy = item => ({
    ...item,
    a: item.a.filter(name => typeof name === 'string' && githubName.test(name)),
    u: typeof item.u === 'string' && githubName.test(item.u) ? item.u : '',
    p: Array.isArray(item.p) ? item.p.filter(name => typeof name === 'string' && githubName.test(name)) : [],
    s: typeof item.s === 'string' ? item.s.slice(0, 200) : ''
  });

  const readJson = async url => {
    const response = await fetch(url, { signal: AbortSignal.timeout(8000) });
    if (!response.ok) throw new Error('Unavailable');
    return response.json();
  };
  const readLive = async () => {
    const open = [];
    for (let page = 1; page <= 3; page++) {
      const batch = await readJson(api + '?state=open&per_page=100&page=' + page);
      if (!Array.isArray(batch)) throw new Error('Unexpected');
      open.push(...batch);
      if (batch.length < 100) break;
    }
    const since = new Date(Date.now() - 90 * 864e5).toISOString();
    const closed = await readJson(api + '?state=closed&per_page=100&sort=updated&direction=desc&since=' + encodeURIComponent(since));
    if (!Array.isArray(closed)) throw new Error('Unexpected');
    const openLinks = prLinks(open);
    const shippedLinks = prLinks(closed);
    return {
      live: true,
      when: new Date().toISOString(),
      open: open.filter(keep).map(item => trim(item, openLinks)),
      shipped: closed.filter(item => keep(item) && item.state_reason === 'completed').map(item => trim(item, shippedLinks))
    };
  };
  const readCopy = async () => {
    const copy = await readJson('issues.json');
    if (!copy || !Array.isArray(copy.open) || !Array.isArray(copy.shipped)) throw new Error('Unexpected');
    return { live: false, when: copy.when, open: copy.open, shipped: copy.shipped };
  };

  // GitHub allows a visitor 60 reads an hour, so one look is kept for ten minutes.
  const saved = 'capri-roadmap-2';
  const fromSaved = () => {
    try {
      const kept = JSON.parse(sessionStorage.getItem(saved));
      if (kept && kept.board && Date.now() - kept.at < 6e5) return kept.board;
    } catch (error) { /* no storage */ }
    return null;
  };
  const save = board => {
    try { sessionStorage.setItem(saved, JSON.stringify({ at: Date.now(), board })); } catch (error) { /* no storage */ }
  };

  // ---------- Cards ----------
  const talkIcon = '<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="M4 5h16v11H9l-5 4z"/></svg>';
  const make = (tag, name, text) => {
    const el = document.createElement(tag);
    if (name) el.className = name;
    if (text !== undefined) el.textContent = text;
    return el;
  };
  const dateOf = text => {
    const when = new Date(text);
    return Number.isNaN(when.getTime()) ? null : when;
  };
  // A GitHub name links to their profile; a name from Discord is just their @.
  const handle = (name, onGithub) => {
    if (!onGithub) return make('span', 'at', '@' + name);
    const link = make('a', 'at', '@' + name);
    link.href = 'https://github.com/' + name;
    return link;
  };
  const credit = (words, names, onGithub) => {
    const line = make('span', 'by', words + ' ');
    names.forEach((name, at) => {
      if (at) line.append(at === names.length - 1 ? ' and ' : ', ');
      line.append(name === lachy ? 'Lachy' : handle(name, onGithub));
    });
    return line;
  };
  const card = (item, lane) => {
    const row = make('li', 'card');
    const kind = kindOf(item.l);
    const top = make('span', 'card-top');
    top.append(make('span', 'kind ' + kind, kindName[kind]), make('span', 'num', '#' + item.n));
    if (item.c > 0) {
      const talk = make('span', 'talk');
      talk.innerHTML = talkIcon;
      talk.append(String(item.c));
      talk.setAttribute('aria-label', item.c === 1 ? '1 comment' : item.c + ' comments');
      top.append(talk);
    }
    const title = make('a', 'card-title', item.t);
    title.href = issue + item.n;

    const meta = make('span', 'card-meta');
    if (item.l.includes('good first issue') && lane !== 'shipped') meta.append(make('span', 'badge', 'Good first issue'));
    if (item.l.includes('help wanted') && lane !== 'grabs' && lane !== 'shipped') meta.append(make('span', 'badge', 'Help wanted'));
    if (item.a.length && lane !== 'shipped') {
      const who = make('span', 'who');
      if (item.a.length === 1 && item.a[0] === lachy) who.textContent = "Lachy's on it";
      else {
        item.a.forEach((name, at) => {
          if (at) who.append(at === item.a.length - 1 ? ' and ' : ', ');
          who.append(name === lachy ? 'Lachy' : handle(name, true));
        });
        who.append(item.a.length === 1 ? "'s on it" : ' are on it');
      }
      meta.append(who);
    }
    const when = dateOf(lane === 'shipped' ? item.done : item.at);
    if (when) {
      const time = make('time', 'when', (lane === 'shipped' ? 'Shipped ' : '') + day.format(when));
      time.dateTime = when.toISOString();
      meta.append(time);
    }

    // Credit sits on the other side of the row: whose idea it was, then whose PR it is or who built it.
    const credits = make('span', 'credits');
    const idea = ideaFrom(item.s);
    if (idea && idea.names.length) {
      const line = credit('idea from', idea.names, idea.where.toLowerCase() === 'github');
      if (idea.where) line.title = 'On ' + idea.where;
      credits.append(line);
    } else if (idea) credits.append(make('span', 'by', 'idea from ' + idea.where));
    else if (item.u && item.u !== lachy) credits.append(credit(kind === 'bug' ? 'found by' : 'idea from', [item.u], true));
    const builders = item.p.filter(name => name !== lachy);
    if (builders.length) credits.append(credit(lane === 'shipped' ? 'built by' : 'PR from', builders, true));
    if (credits.childNodes.length) meta.append(credits);

    row.append(top, title, meta);
    return row;
  };

  // ---------- The board ----------
  const lanes = Object.fromEntries($$('[data-lane]').map(lane => [lane.dataset.lane, lane]));
  const status = $('#road-status');
  const findBox = $('#find');
  let board = null;
  let groups = null;
  let kind = 'all';
  let words = '';

  const sortBy = (list, first) => list.sort((a, b) => (first(b) - first(a)) || b.n - a.n);
  const group = () => {
    const sorted = { fix: [], plan: [], grabs: [], shipped: [] };
    for (const item of board.open) sorted[laneOf(item)].push(item);
    // Work someone's on comes first; in Up for grabs, good first issues lead.
    sortBy(sorted.fix, item => item.a.length > 0);
    sortBy(sorted.plan, item => item.a.length > 0);
    sortBy(sorted.grabs, item => item.l.includes('good first issue'));
    sorted.shipped = board.shipped.slice().sort((a, b) => (dateOf(b.done) || 0) - (dateOf(a.done) || 0)).slice(0, 24);
    for (const list of Object.values(sorted)) {
      for (const item of list) item.find = [item.t, '#' + item.n, ...item.l, ...item.a, item.u, ...item.p, item.s].join(' ').toLowerCase();
    }
    return sorted;
  };
  const fits = (item, which) => (which === 'all' || kindOf(item.l) === which) && (!words || item.find.includes(words));
  const show = () => {
    if (!groups) return;
    const counts = { all: 0, feature: 0, bug: 0 };
    let shown = 0;
    for (const [name, lane] of Object.entries(lanes)) {
      const list = groups[name] || [];
      for (const item of list) {
        if (!words || item.find.includes(words)) {
          counts.all++;
          const which = kindOf(item.l);
          if (which in counts) counts[which]++;
        }
      }
      const picked = list.filter(item => fits(item, kind));
      shown += picked.length;
      const cards = $('.cards', lane);
      cards.replaceChildren(...(picked.length ? picked.map(item => card(item, name)) : [make('li', 'empty', words ? 'Nothing matches' : 'Nothing here')]));
      $('[data-lane-count]', lane).textContent = picked.length;
      let more = $('.more', lane);
      if (!more) {
        more = make('button', 'more');
        more.type = 'button';
        more.addEventListener('click', () => {
          lane.classList.add('open');
          more.hidden = true;
          const next = $('.cards li:nth-child(9) .card-title', lane);
          if (next) next.focus();
        });
        lane.append(more);
      }
      more.textContent = 'Show all ' + picked.length;
      more.hidden = lane.classList.contains('open') || picked.length <= 8;
    }
    for (const [which, count] of Object.entries(counts)) {
      const where = $('[data-count="' + which + '"]');
      if (where) where.textContent = count;
    }
    status.textContent = shown === 1 ? '1 issue shown' : shown + ' issues shown';
  };
  const showSync = () => {
    const open = board.open.length;
    const when = dateOf(board.when);
    $('[data-open]').textContent = open;
    $('[data-shipped]').textContent = board.shipped.length;
    $('[data-sync]').textContent = !when ? 'GitHub' : board.live ? 'synced ' + clock.format(when).toLowerCase() : 'as of ' + day.format(when);
    $('#road-sys').classList.add('ready');
  };
  const take = next => {
    board = { live: next.live, when: next.when, open: next.open.filter(sane).map(tidy), shipped: next.shipped.filter(sane).map(tidy) };
    groups = group();
    showSync();
    show();
  };

  for (const button of $$('[data-kind]')) {
    button.addEventListener('click', () => {
      kind = button.dataset.kind;
      for (const other of $$('[data-kind]')) other.setAttribute('aria-pressed', String(other === button));
      show();
    });
  }
  let typing = 0;
  findBox.addEventListener('input', () => {
    clearTimeout(typing);
    typing = setTimeout(() => {
      words = findBox.value.trim().toLowerCase();
      show();
    }, 120);
  });

  const start = async () => {
    const kept = fromSaved();
    if (kept) { take(kept); return; }
    try {
      const live = await readLive();
      save(live);
      take(live);
    } catch (error) {
      try {
        take(await readCopy());
      } catch (missing) {
        $('#board').classList.add('off');
        $('#road-off').hidden = false;
      }
    }
  };
  start();

  // ---------- Her glitch, borrowed by the word: two cut bands slip sideways for a third of a second ----------
  const mark = $('#road-mark');
  let glitchTimer = 0;
  const glitch = () => {
    if (still.matches || document.hidden || mark.classList.contains('glitching')) return;
    mark.classList.add('glitching');
    setTimeout(() => mark.classList.remove('glitching'), 380);
  };
  const glitchLater = () => {
    clearTimeout(glitchTimer);
    glitchTimer = setTimeout(() => { glitch(); glitchLater(); }, 7000 + Math.random() * 6000);
  };
  mark.addEventListener('pointerenter', glitch);
  (document.fonts ? document.fonts.ready : Promise.resolve()).then(() => setTimeout(glitch, 500));
  glitchLater();
})();

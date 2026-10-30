(() => {
  const root = new URL('./', document.currentScript.src);
  const live = 'https://lachydotmcg.github.io/capri-desktop/';
  const local = ['localhost', '127.0.0.1', '[::1]'].includes(location.hostname);
  const api = (local && new URLSearchParams(location.search).get('helpapi')) || 'https://capri-reactions.lachydotmcg.workers.dev/help';
  const still = matchMedia('(prefers-reduced-motion: reduce)');
  const phone = matchMedia('(max-width: 600px)');
  const greeting = 'Ask me for help if you get stuck, choom!';
  const starters = ['Why does Windows warn me?', 'Which brain should I pick?', 'What does it cost?'];
  const fallback = "My help line's glitching right now. The Help page has everything.";
  const saved = {
    get: () => { try { return sessionStorage.getItem('capri-helper-chat'); } catch (error) { return null; } },
    set: value => { try { sessionStorage.setItem('capri-helper-chat', value); } catch (error) { } }
  };
  const make = (tag, className, text) => {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text != null) node.textContent = text;
    return node;
  };
  const icon = path => '<svg viewBox="0 0 24 24" aria-hidden="true">' + path + '</svg>';

  const helper = make('div', 'helper');
  const tab = make('button', 'helper-tab');
  tab.type = 'button';
  tab.innerHTML = icon('<path d="M4 5h16v11H10l-6 4z"/><path d="M8.5 10.5h7"/>');
  const call = make('span', 'helper-tab-words');
  call.append(make('strong', null, 'Stuck? Ask me!'), make('small', null, 'Chat with Capri'));
  tab.append(call);
  tab.hidden = true;
  tab.setAttribute('aria-controls', 'helper-panel');
  tab.setAttribute('aria-expanded', 'false');

  const panel = make('section', 'helper-panel');
  panel.id = 'helper-panel';
  panel.hidden = true;
  panel.setAttribute('role', 'dialog');
  panel.setAttribute('aria-label', 'Chat with Capri');
  const head = make('div', 'helper-head');
  const tuck = make('button', 'helper-tuck');
  tuck.type = 'button';
  tuck.setAttribute('aria-label', 'Minimise chat');
  tuck.innerHTML = icon('<path d="M6 12h12"/>');
  head.append(make('p', 'helper-name', 'Capri'), tuck);
  const log = make('ol', 'helper-log');
  log.setAttribute('aria-live', 'polite');
  const chips = make('div', 'helper-chips');
  const form = make('form', 'helper-form');
  const input = make('input');
  input.type = 'text';
  input.maxLength = 300;
  input.autocomplete = 'off';
  input.setAttribute('aria-label', 'Your question');
  const send = make('button', 'helper-send');
  send.type = 'submit';
  send.setAttribute('aria-label', 'Send');
  send.innerHTML = icon('<path d="M4 12 20 4l-4 16-4-6.5z"/><path d="m12 13.5 8-9.5"/>');
  const attach = make('button', 'helper-attach');
  attach.type = 'button';
  attach.title = 'Add a screenshot';
  attach.setAttribute('aria-label', 'Add a screenshot');
  attach.innerHTML = icon('<rect x="3" y="4.5" width="18" height="15" rx="2.5"/><circle cx="8.5" cy="9.5" r="1.8"/><path d="m3.5 17.5 5.5-5.5 4 4 2.5-2.5 5 5"/>');
  const picker = make('input');
  picker.type = 'file';
  picker.accept = 'image/*';
  picker.hidden = true;
  form.append(attach, input, send, picker);
  const pending = make('div', 'helper-pending');
  pending.hidden = true;
  const preview = make('img');
  preview.alt = 'Your screenshot';
  const unpick = make('button', 'helper-unpick');
  unpick.type = 'button';
  unpick.setAttribute('aria-label', 'Remove screenshot');
  unpick.innerHTML = icon('<path d="m7 7 10 10M17 7 7 17"/>');
  pending.append(preview, unpick);
  const drop = make('div', 'helper-drop', 'Drop a screenshot');
  drop.hidden = true;
  panel.append(head, log, chips, pending, form, drop);

  const capri = make('span', 'helper-capri');
  capri.setAttribute('aria-hidden', 'true');
  const sprite = make('span', 'helper-sprite');
  capri.append(sprite, make('span', 'helper-hit'));
  helper.append(panel, tab);
  document.body.append(helper);

  // Frames, frames a second, and for her artist's 60-frame perch, how many cells go across before the strip wraps to its next row.
  const clips = { perch: [60, 30, 10], perch_blink: [8, 30], perch_blink_glitch: [8, 30], perch_glitch: [10, 15] };
  const across = name => clips[name][2] || clips[name][0];
  const down = name => Math.ceil(clips[name][0] / across(name));
  const loaded = new Set();
  for (const name in clips) {
    const image = new Image();
    image.onload = () => loaded.add(name);
    image.src = new URL('assets/helper/' + name + '.webp', root).href;
  }
  let clip = 'perch';
  let clipStart = performance.now();
  let cycle = 0;
  let blinkWith = '';
  let nextGlitch = clipStart + 12000 + Math.random() * 12000;
  let shown = '';
  const play = name => { clip = name; clipStart = performance.now(); cycle = 0; blinkWith = ''; };
  setInterval(() => {
    if (document.hidden || !capri.isConnected) return;
    const now = performance.now();
    const motion = !still.matches;
    if (clip === 'perch_glitch' && (!motion || !loaded.has(clip))) play('perch');
    let index = Math.floor((now - clipStart) / 1000 * clips[clip][1]);
    if (clip === 'perch_glitch' && index >= clips.perch_glitch[0]) { play('perch'); index = 0; }
    if (clip === 'perch' && motion) {
      const current = Math.floor(index / clips.perch[0]);
      if (current !== cycle) {
        cycle = current;
        const roll = Math.random();
        blinkWith = roll < 0.1 ? 'perch_blink_glitch' : roll < 0.55 ? 'perch_blink' : '';
        if (now >= nextGlitch) { nextGlitch = now + 12000 + Math.random() * 12000; play('perch_glitch'); index = 0; }
      }
    }
    let name = clip;
    let cell = motion ? index % clips[clip][0] : 0;
    // A blink swaps in at cells 28 to 35 of the perch: her artist's own blink, its frames 29 to 36.
    if (name === 'perch' && blinkWith && loaded.has(blinkWith) && cell >= 28 && cell < 36) { name = blinkWith; cell -= 28; }
    if (!loaded.has(name)) return;
    if (shown === name + ':' + cell) return;
    const columns = across(name), rows = down(name);
    if (!shown.startsWith(name + ':')) {
      sprite.style.backgroundImage = 'url("' + new URL('assets/helper/' + name + '.webp', root).href + '")';
      sprite.style.backgroundSize = columns * 100 + '% ' + rows * 100 + '%';
    }
    shown = name + ':' + cell;
    sprite.style.backgroundPosition = (columns > 1 ? cell % columns / (columns - 1) * 100 : 0) + '% ' + (rows > 1 ? Math.floor(cell / columns) / (rows - 1) * 100 : 0) + '%';
  }, 1000 / 30);

  const sitOn = box => {
    if (capri.parentNode === box) return;
    box.append(capri);
    play('perch_glitch');
  };
  let calls = 0;
  const callOut = () => {
    if (calls >= 5) return;
    setTimeout(() => {
      if (!tab.hidden && !document.hidden) { calls++; play('perch_glitch'); }
      callOut();
    }, 35000 + Math.random() * 20000);
  };

  const chat = { log: [], history: [], open: false };
  const remember = () => saved.set(JSON.stringify({ log: chat.log.slice(-30), history: chat.history.slice(-6), open: chat.open }));
  const scrolled = () => { log.scrollTop = log.scrollHeight; };
  const linked = href => {
    if (typeof href !== 'string') return null;
    if (href.startsWith(live)) return new URL(href.slice(live.length), root).href;
    return /^https:\/\/discord\.gg\/[\w-]+$/.test(href) ? href : null;
  };
  const addLinks = (bubble, links) => {
    for (const link of (links || []).slice(0, 2)) {
      const href = link && linked(link.href);
      if (!href || typeof link.title !== 'string') continue;
      const anchor = make('a', 'helper-link', link.title);
      anchor.href = href;
      if (!href.startsWith(root.href)) { anchor.target = '_blank'; anchor.rel = 'noopener noreferrer'; }
      bubble.append(anchor);
    }
  };
  const say = (from, text, links, thumb) => {
    const item = make('li', 'helper-msg ' + from);
    if (thumb) {
      const shot = make('img', 'helper-shot');
      shot.src = thumb;
      shot.alt = 'Your screenshot';
      item.append(shot);
    }
    const words = make('span', 'helper-words', text);
    if (text || from === 'capri') item.append(words);
    if (links) addLinks(item, links);
    log.append(item);
    scrolled();
    return { item, words };
  };
  const typeOut = (words, text, done) => {
    if (still.matches) { words.textContent = text; done(); return; }
    const start = performance.now();
    const step = () => {
      const count = Math.min(text.length, Math.floor((performance.now() - start) / 1000 * 70));
      words.textContent = text.slice(0, count);
      scrolled();
      if (count < text.length) setTimeout(step, 30);
      else done();
    };
    step();
  };
  const showChips = () => {
    chips.replaceChildren();
    if (chat.log.some(line => line.from === 'you')) return;
    for (const question of starters) {
      const chip = make('button', 'helper-chip', question);
      chip.type = 'button';
      chip.addEventListener('click', () => ask(question));
      chips.append(chip);
    }
  };

  const place = (opened, byHand, quietly) => {
    chat.open = opened;
    panel.hidden = !opened;
    tab.hidden = opened;
    tab.setAttribute('aria-expanded', String(opened));
    const box = opened ? panel : tab;
    box.classList.remove('helper-arrive');
    if (!quietly) {
      void box.offsetWidth;
      box.classList.add('helper-arrive');
    }
    sitOn(box);
    if (quietly) play('perch');
    scrolled();
    remember();
    if (!byHand) return;
    if (!opened) tab.focus();
    else if (!phone.matches) input.focus();
  };
  tab.addEventListener('click', () => place(true, true));
  capri.addEventListener('click', () => { if (capri.parentNode === panel) play('perch_glitch'); });
  tuck.addEventListener('click', () => place(false, true));
  panel.addEventListener('keydown', event => { if (event.key === 'Escape') place(false, true); });

  let picked = null;
  let busy = false;
  const shrink = file => new Promise((resolve, reject) => {
    const url = URL.createObjectURL(file);
    const image = new Image();
    const paint = longest => {
      const scale = Math.min(1, longest / Math.max(image.naturalWidth, image.naturalHeight));
      const canvas = document.createElement('canvas');
      canvas.width = Math.max(1, Math.round(image.naturalWidth * scale));
      canvas.height = Math.max(1, Math.round(image.naturalHeight * scale));
      const paper = canvas.getContext('2d');
      paper.fillStyle = '#fff';
      paper.fillRect(0, 0, canvas.width, canvas.height);
      paper.drawImage(image, 0, 0, canvas.width, canvas.height);
      return canvas;
    };
    image.onload = () => {
      URL.revokeObjectURL(url);
      resolve({ full: paint(1280).toDataURL('image/jpeg', 0.85), thumb: paint(240).toDataURL('image/jpeg', 0.7) });
    };
    image.onerror = () => { URL.revokeObjectURL(url); reject(new Error('Unreadable picture')); };
    image.src = url;
  });
  const unchoose = () => { picked = null; pending.hidden = true; preview.removeAttribute('src'); };
  const choose = async file => {
    if (busy) return;
    if (!file || !/^image\//.test(file.type)) { say('capri', "That's not a picture. Try a screenshot."); return; }
    if (file.size > 20000000) { say('capri', "That picture's too big for me. Try a screenshot."); return; }
    try { picked = await shrink(file); } catch (error) { say('capri', "I couldn't open that picture. Try a PNG or JPG."); return; }
    preview.src = picked.thumb;
    pending.hidden = false;
    scrolled();
    if (!phone.matches) input.focus();
  };
  const imageIn = list => [...(list || [])].find(file => /^image\//.test(file.type)) || (list && list[0]);
  const carrying = event => !!event.dataTransfer && [...event.dataTransfer.types].includes('Files');
  let dragging = 0;
  attach.addEventListener('click', () => picker.click());
  picker.addEventListener('change', () => { choose(picker.files[0]); picker.value = ''; });
  unpick.addEventListener('click', () => { unchoose(); input.focus(); });
  panel.addEventListener('paste', event => {
    const file = imageIn(event.clipboardData && event.clipboardData.files);
    if (!file) return;
    event.preventDefault();
    choose(file);
  });
  panel.addEventListener('dragover', event => {
    if (!carrying(event)) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = 'copy';
    drop.hidden = false;
    clearTimeout(dragging);
    dragging = setTimeout(() => { drop.hidden = true; }, 250);
  });
  panel.addEventListener('drop', event => {
    if (!carrying(event)) return;
    event.preventDefault();
    drop.hidden = true;
    choose(imageIn(event.dataTransfer.files));
  });
  tab.addEventListener('dragenter', event => { if (carrying(event)) place(true, true); });
  const placeholder = () => { input.placeholder = phone.matches ? 'Type your question' : 'Type, or drop a screenshot'; };
  phone.addEventListener('change', placeholder);
  placeholder();

  const ask = async text => {
    const question = text.replace(/\s+/g, ' ').trim();
    const shot = picked;
    if ((!question && !shot) || busy) return;
    busy = true;
    send.disabled = true;
    attach.disabled = true;
    input.value = '';
    unchoose();
    say('you', question, null, shot && shot.thumb);
    chat.log.push(shot ? { from: 'you', text: question, thumb: shot.thumb } : { from: 'you', text: question });
    showChips();
    remember();
    const typing = make('li', 'helper-typing');
    typing.setAttribute('aria-label', 'Capri is typing');
    typing.append(make('span'), make('span'), make('span'));
    log.append(typing);
    scrolled();
    let data = null;
    try {
      const response = await fetch(api, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ question, history: chat.history.slice(-6), image: shot ? shot.full : undefined }) });
      data = await response.json();
    } catch (error) { }
    const reply = data && typeof data.reply === 'string' && data.reply.trim() ? data.reply.trim() : fallback;
    const links = data && Array.isArray(data.links) ? data.links : [{ title: 'Help page', href: live + 'help/' }];
    if (data && data.answered) chat.history.push({ role: 'user', text: question || '(sent a screenshot)' }, { role: 'assistant', text: reply });
    typing.remove();
    const answer = say('capri', '');
    typeOut(answer.words, reply, () => {
      addLinks(answer.item, links);
      scrolled();
      chat.log.push({ from: 'capri', text: reply, links });
      remember();
      busy = false;
      send.disabled = false;
      attach.disabled = false;
      if (!panel.hidden && !phone.matches) input.focus();
    });
  };
  form.addEventListener('submit', event => { event.preventDefault(); ask(input.value); });

  let before = null;
  try { before = JSON.parse(saved.get() || 'null'); } catch (error) { }
  if (before && Array.isArray(before.log)) {
    chat.log = before.log.filter(line => line && (line.from === 'you' || line.from === 'capri') && typeof line.text === 'string');
    chat.history = Array.isArray(before.history) ? before.history : [];
  }
  if (!chat.log.length) chat.log.push({ from: 'capri', text: greeting });
  for (const line of chat.log) say(line.from, line.text, line.links, typeof line.thumb === 'string' && line.thumb.startsWith('data:image/jpeg;base64,') ? line.thumb : null);
  showChips();
  if (before) place(before.open === true, false, true);
  else setTimeout(() => { if (panel.hidden && tab.hidden) place(false, false); }, 900);
  callOut();
})();

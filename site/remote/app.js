/* Capri Remote: the chat, the menu, the link screens, and Capri on the message bar. It talks to her PC only through
   CapriRemote (remote.js, in PROTOCOL.md's message names) and moves her only through CapriBar (capri.js). */
(() => {
  'use strict';
  // Early access: while this is true, the real page asks for the early-access password before anything else, pairing
  // included. Set it to false to open Capri Remote to everyone.
  const EARLY_ACCESS = false;
  const config = window.CAPRI_CONFIG || {};
  // The demo PC runs only in the preview build or when the address asks for it with ?demo. Nothing on the chat screen
  // says demo any more, so on the real page nothing fake can turn up. The demo skips the gate; ?gate shows it anyway.
  let demoAsked = false, gateAsked = false, iosAsked = '';
  try {
    const asked = new URLSearchParams(location.search);
    demoAsked = asked.has('demo');
    gateAsked = asked.has('gate');
    iosAsked = asked.get('ios') || '';
  } catch (error) {}
  // An iPhone keeps a home screen app's storage apart from Safari's, so a pairing made in one never reaches the other, and
  // a QR code always opens in Safari. ?ios=safari and ?ios=app act those two out in any browser, to check their screens.
  const iphone = iosAsked === 'safari' || iosAsked === 'app' || 'standalone' in navigator;
  const standalone = iosAsked === 'app' || (iosAsked !== 'safari' && (navigator.standalone === true || (() => {
    try { return matchMedia('(display-mode: standalone)').matches; } catch (error) { return false; }
  })()));
  const demoBuild = config.demo !== false && (config.preview === true || demoAsked);
  const $ = selector => document.querySelector(selector);
  const clamp = (value, low, high) => Math.max(low, Math.min(high, value));
  // Words from the PC, one line, without control or direction-flipping characters.
  const clip = (value, most) => String(value == null ? '' : value).replace(/[\x00-\x1f\x7f\u200e\u200f\u202a-\u202e\u2066-\u2069]/g, ' ').slice(0, most);
  const make = (tag, className, text) => {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text != null) node.textContent = text;
    return node;
  };
  const DOT = ' \u00b7 ';
  // Capri's size on the reply bar: 1 is her first size (28% of the screen's width, 84 to 128 px); 2 is twice that, big
  // enough to read in a phone video. Her seat, the room left for her above the last message and the pairing cards'
  // headroom all follow it.
  const HER_SCALE = 2;

  const app = $('#app');
  const top = $('.top');
  const log = $('#log');
  const logIn = $('#log-in');
  const composer = $('#composer');
  const bar = $('#bar');
  const input = $('#msg');
  const sendButton = $('#send');
  const micButton = $('#mic');
  const notice = $('#notice');
  const banner = $('#banner');
  const bannerTitle = $('#banner-title');
  const bannerSub = $('#banner-sub');
  const bannerRetry = $('#banner-retry');
  const gateScreen = $('#gate');
  const gateForm = $('#gate-card');
  const gateField = $('#gate-field');
  const gateWord = $('#gate-word');
  const gateWrong = $('#gate-wrong');
  const pairScreen = $('#pair');
  const pairNote = $('#pair-note');
  const replaceScreen = $('#replace');
  const waitScreen = $('#wait');
  const herLayer = $('#her-layer');
  const drawer = $('#drawer');
  const scrim = $('#scrim');
  const menuOpen = $('#menu-open');
  const actions = $('#actions');
  const unpairButton = $('#d-unpair');
  const announce = $('#announce');
  const whereScreen = $('#where');
  const whereNote = $('#where-note');
  const whereField = $('#where-field');
  const whereInput = $('#where-link');
  const pasteForm = $('#paste');
  const pasteField = $('#paste-field');
  const pasteLink = $('#paste-link');
  const homeSheet = $('#home');

  let linkState = 'unpaired';
  let linkInfo = {};
  // The early-access gate is up (it covers every screen until the password is right).
  let gateUp = false;
  let confirmHistory = false;

  // ---------- Time labels ----------
  const dayStart = at => { const day = new Date(at); day.setHours(0, 0, 0, 0); return day.getTime(); };
  const clock = at => new Date(at).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
  const dayLabel = (at, withTime) => {
    const days = Math.round((dayStart(Date.now()) - dayStart(at)) / 86400000);
    if (days <= 0) return withTime ? 'Today ' + clock(at) : 'Today';
    if (days === 1) return 'Yesterday';
    if (days < 7) return new Date(at).toLocaleDateString([], { weekday: 'short' });
    return new Date(at).toLocaleDateString([], { day: 'numeric', month: 'short' });
  };

  // ---------- Chats, kept on this phone only ----------
  const KEY = 'capri-remote-chats';
  const roles = new Set(['you', 'capri', 'tool', 'sys']);
  const states = new Set(['running', 'done', 'failed', 'cut']);
  let chats = [];
  let currentId = '';
  const nodes = new WeakMap();
  const newId = () => Date.now().toString(36) + Math.random().toString(36).slice(2, 10);
  const loadChats = () => {
    try {
      const saved = JSON.parse(localStorage.getItem(KEY));
      if (!saved || !Array.isArray(saved.chats)) return;
      chats = saved.chats.filter(chat => chat && typeof chat.id === 'string' && Array.isArray(chat.msgs)).slice(0, 40).map(chat => ({
        id: chat.id.slice(0, 40),
        at: Number(chat.at) || Date.now(),
        last: Number(chat.last) || Number(chat.at) || Date.now(),
        msgs: chat.msgs.filter(msg => msg && roles.has(msg.role) && typeof msg.text === 'string').slice(-200).map(msg => {
          const kept = { role: msg.role, text: msg.text.slice(0, 8000), at: Number(msg.at) || 0 };
          if (msg.role === 'tool') {
            kept.name = clip(msg.name, 40) || 'tool';
            // A tool still running when the page closed never said how it went.
            kept.status = states.has(msg.status) ? (msg.status === 'running' ? 'cut' : msg.status) : 'done';
          }
          if (msg.role === 'capri') {
            // Nor did a reply cut off by a reload or a dropped link.
            if (typeof msg.cut === 'string' && msg.cut) kept.cut = msg.cut.slice(0, 40);
            else if (msg.live) kept.cut = 'SIGNAL LOST';
          }
          return kept;
        }).filter(msg => msg.role !== 'capri' || msg.text || msg.cut)
      }));
      currentId = typeof saved.current === 'string' ? saved.current : '';
    } catch (error) {}
  };
  let saveTimer = 0;
  const saveChats = () => {
    clearTimeout(saveTimer);
    saveTimer = setTimeout(() => {
      try { localStorage.setItem(KEY, JSON.stringify({ chats: chats.slice(0, 40), current: currentId })); } catch (error) {}
    }, 200);
  };
  const current = () => chats.find(chat => chat.id === currentId) || null;
  // Phone-only mode (native.js) remembers the chat on screen.
  if (window.CapriNative) CapriNative.chat(current);
  const titleOf = chat => {
    const first = chat && chat.msgs.find(msg => msg.role === 'you');
    if (!first) return 'New chat';
    const words = first.text.replace(/\s+/g, ' ').trim();
    return words.length > 44 ? words.slice(0, 42).replace(/\s+\S*$/, '') + '\u2026' : words;
  };
  const startChat = greeting => {
    const now = Date.now();
    const chat = { id: newId(), at: now, last: now, msgs: [] };
    if (greeting) chat.msgs.push({ role: 'capri', text: greeting, at: now });
    chats.unshift(chat);
    currentId = chat.id;
    saveChats();
    return chat;
  };

  // A few example chats for the preview, so it opens the way it looks in use.
  const seedChats = () => {
    const now = Date.now(), minute = 60000, day = 86400000;
    const chat = (at, msgs) => ({ id: newId(), at, last: at + 2 * minute, msgs: msgs.map(msg => Object.assign({ at }, msg)) });
    chats = [
      chat(now - 9 * minute, [
        { role: 'you', text: 'play something for late night coding' },
        { role: 'tool', name: 'play_music', text: 'Edgerunners OST', status: 'done' },
        { role: 'capri', text: 'Edgerunners OST is on. I\'m swaying on your bar, don\'t look.' },
        { role: 'you', text: "what's on my screen rn" },
        { role: 'tool', name: 'look_at_screen', text: '1 screenshot', status: 'done' },
        { role: 'capri', text: 'A half-built phone app with a tiny me sitting on it. Cute. Ship it.' }
      ]),
      chat(now - day - 3 * 60 * minute, [
        { role: 'you', text: 'open discord' },
        { role: 'tool', name: 'open_app', text: 'Discord', status: 'done' },
        { role: 'capri', text: 'Discord\'s open. Try not to start drama in there.' }
      ]),
      chat(now - 3 * day, [
        { role: 'you', text: 'remind me to stretch in 20 min' },
        { role: 'tool', name: 'remind', text: '20 min', status: 'done' },
        { role: 'capri', text: 'Twenty minutes. Then you stretch, or I glitch your wallpaper.' }
      ])
    ];
    currentId = chats[0].id;
    saveChats();
  };

  // ---------- The log ----------
  const end = make('div', 'log-end');
  end.setAttribute('aria-hidden', 'true');
  let stuck = true;
  log.addEventListener('scroll', () => { stuck = log.scrollHeight - log.scrollTop - log.clientHeight < 72; }, { passive: true });
  const stick = () => { if (stuck) log.scrollTop = log.scrollHeight; };
  const toEnd = () => { stuck = true; log.scrollTop = log.scrollHeight; };
  if ('ResizeObserver' in window) new ResizeObserver(stick).observe(log);

  const stateWords = { done: 'done', failed: 'failed', cut: 'no answer' };
  const renderTool = (node, msg) => {
    node.dataset.status = msg.status || 'done';
    node.replaceChildren(document.createTextNode('> '), make('span', 't-name', msg.name || 'tool'));
    if (msg.text) node.append(document.createTextNode(DOT + msg.text));
    if (msg.status === 'running') {
      const caret = make('span', 'caret blink');
      caret.setAttribute('aria-hidden', 'true');
      node.append(document.createTextNode(' '), caret);
    } else node.append(document.createTextNode(DOT), make('span', 't-state', stateWords[msg.status] || 'done'));
    return node;
  };
  const cutWords = msg => msg.cut ? (msg.text ? ' [' : '[') + msg.cut + ']' : '';
  // Each text is its own box, with no name on it: the header names her. Screen readers still hear who said it.
  const renderMsg = msg => {
    let node;
    if (msg.role === 'you') {
      node = make('p', 'msg you');
      node.append(make('span', 'sr-only', 'You: '), make('span', 'words', msg.text));
    } else if (msg.role === 'capri') {
      node = make('p', 'msg capri done');
      const caret = make('span', 'caret');
      caret.setAttribute('aria-hidden', 'true');
      node.append(make('span', 'sr-only', 'Capri: '), make('span', 'words', msg.text), make('span', 'cut', cutWords(msg)), caret);
    } else if (msg.role === 'tool') node = renderTool(make('p', 'tool'), msg);
    else node = make('p', 'sys', '> sys: ' + msg.text);
    nodes.set(msg, node);
    return node;
  };
  const add = node => { logIn.insertBefore(node, end); stick(); };

  const starterAsks = ['play some lofi', "what's on my screen", 'open discord', 'remind me in 10 min'];
  const starters = () => {
    const box = make('div', 'starters');
    for (const ask of (window.CapriNative && CapriNative.starters()) || starterAsks) {
      const button = make('button', 'nbtn cyan', ask);
      button.type = 'button';
      button.addEventListener('click', () => deliver(ask));
      box.append(button);
    }
    return box;
  };
  const dropStarters = () => { const box = logIn.querySelector('.starters'); if (box) box.remove(); };
  const renderChat = () => {
    const chat = current();
    logIn.replaceChildren(make('p', 'chat-head', dayLabel(chat ? chat.at : Date.now(), true)));
    if (chat) for (const msg of chat.msgs) logIn.append(renderMsg(msg));
    if (!chat || !chat.msgs.some(msg => msg.role === 'you')) logIn.append(starters());
    logIn.append(end);
    for (const job of pending.values()) if (job.chat === chat) attach(job);
    toEnd();
  };

  // ---------- Her replies ----------
  // Replies on their way, by the chat.send id they answer, or by the PC's own reply id for ones nobody here asked for.
  const pending = new Map();
  const loose = new Map();
  const QUIET = 20000;
  const attach = job => {
    const node = nodes.get(job.msg);
    if (!node) return;
    node.classList.remove('done', 'thinking', 'talking', 'quiet');
    node.classList.add(job.started ? 'talking' : job.quiet ? 'quiet' : 'thinking');
  };
  // Twenty seconds with no word from her (PROTOCOL.md): the caret stops, but a late reply still lands.
  const stir = job => {
    clearTimeout(job.timer);
    job.quiet = false;
    job.timer = setTimeout(() => { job.quiet = true; if (!job.started) attach(job); }, QUIET);
    attach(job);
  };
  const anyTalking = () => [...pending.values()].some(job => job.started);
  const openReply = (key, chat) => {
    chat = chat || current() || startChat();
    const msg = { role: 'capri', text: '', at: Date.now(), live: true };
    chat.msgs.push(msg);
    chat.last = msg.at;
    const job = { key, chat, msg, started: false, quiet: false, timer: 0, tools: new Map() };
    pending.set(key, job);
    if (chat === current()) { dropStarters(); add(renderMsg(msg)); }
    stir(job);
    return job;
  };
  const settleReply = (key, failure) => {
    const job = pending.get(key);
    if (!job) return;
    pending.delete(key);
    clearTimeout(job.timer);
    delete job.msg.live;
    const node = nodes.get(job.msg);
    if (!failure && !job.msg.text) {
      // She only ran tools and had nothing to add.
      const at = job.chat.msgs.indexOf(job.msg);
      if (at >= 0) job.chat.msgs.splice(at, 1);
      if (node) node.remove();
    } else {
      if (failure) job.msg.cut = failure;
      if (node) {
        node.classList.remove('thinking', 'talking', 'quiet');
        node.classList.add('done');
        const words = node.querySelector('.words');
        if (words) words.textContent = job.msg.text;
        const cut = node.querySelector('.cut');
        if (cut) cut.textContent = cutWords(job.msg);
      }
      if (!failure) announce.textContent = 'Capri: ' + job.msg.text;
    }
    if (!anyTalking()) CapriBar.talk(false);
    saveChats();
  };
  let musicTimer = 0;
  const toolDone = (name, summary) => {
    if (name === 'play_music' || (name === 'media' && /\b(play|resume)\b/i.test(summary))) {
      CapriBar.music(true);
      clearTimeout(musicTimer);
      musicTimer = setTimeout(() => CapriBar.music(false), 60000);
    } else if (name === 'stop' || (name === 'media' && /\b(pause|stop)\b/i.test(summary))) {
      clearTimeout(musicTimer);
      CapriBar.music(false);
    }
  };
  const errorWords = { busy: 'CAPRI IS BUSY', 'bad-message': 'NOT READ', 'not-allowed': 'TURNED OFF ON THE PC', version: 'UPDATE CAPRI' };
  // Phone-only mode's own (native.js): what her brain's provider said.
  Object.assign(errorWords, { key: 'KEY REJECTED', credits: 'NO CREDITS', limit: 'TOO MANY REQUESTS', model: 'MODEL NOT ON KEY', down: 'SERVICE DOWN', net: 'NO INTERNET', empty: 'NO REPLY' });

  // One message from him to her, over chat.send, whether he typed it, tapped a starter or tapped SCREEN.
  const deliver = text => {
    if (linkState !== 'connected') { say(linkState === 'offline' ? 'PC offline' : 'Not connected yet'); return false; }
    const chat = current() || startChat();
    const now = Date.now();
    const said = { role: 'you', text: text.slice(0, 8000), at: now };
    chat.msgs.push(said);
    chat.last = now;
    dropStarters();
    add(renderMsg(said));
    const re = newId();
    openReply(re, chat);
    toEnd();
    if (!CapriRemote.send('chat.send', { id: re, text: said.text })) settleReply(re, 'NOT SENT');
    CapriBar.grin();
    saveChats();
    return true;
  };
  const send = () => {
    const text = input.value.trim();
    if (!text || !deliver(text)) return;
    input.value = '';
    autosize();
    updateSend();
  };
  // The screen view and the call (screen.js, call.js) use the chat too: a message to her, a notice, and a line he said
  // on a call as her ears heard it, which her answer lands under like a typed message's.
  const heardLine = (text, id) => {
    if (linkState !== 'connected') return;
    const chat = current() || startChat();
    const now = Date.now();
    const said = { role: 'you', text: clip(text, 8000), at: now };
    chat.msgs.push(said);
    chat.last = now;
    dropStarters();
    add(renderMsg(said));
    openReply(id, chat);
    toEnd();
    saveChats();
  };
  window.CapriApp = { deliver, heard: heardLine, say: text => say(text) };

  CapriRemote.onMessage(message => {
    const type = message.type;
    const asked = typeof message.re === 'string' ? pending.get(message.re) : null;
    if (type === 'chat.typing') {
      if (asked && message.on) stir(asked);
    } else if (type === 'chat.tool') {
      const id = clip(message.id, 64);
      const list = asked ? asked.tools : loose;
      let tool = list.get(id);
      if (!tool) {
        const chat = asked ? asked.chat : current() || startChat();
        tool = { role: 'tool', name: clip(message.name, 40) || 'tool', text: '', status: 'running', at: Date.now() };
        // A tool row sits just above the reply it belongs to.
        const at = asked ? chat.msgs.indexOf(asked.msg) : -1;
        chat.msgs.splice(at < 0 ? chat.msgs.length : at, 0, tool);
        chat.last = tool.at;
        list.set(id, tool);
        if (chat === current()) {
          const reply = asked && nodes.get(asked.msg);
          logIn.insertBefore(renderMsg(tool), reply && reply.isConnected ? reply : end);
          stick();
        }
      }
      if (message.summary) tool.text = clip(message.summary, 120);
      tool.status = message.state === 'running' || message.state === 'failed' ? message.state : 'done';
      const row = nodes.get(tool);
      if (row) renderTool(row, tool);
      if (asked) stir(asked);
      if (tool.status === 'done') toolDone(tool.name, tool.text);
      saveChats();
    } else if (type === 'chat.reply') {
      let job = asked;
      if (!job) {
        const key = 'pc:' + clip(message.id || message.re, 64);
        job = pending.get(key);
        if (!job) {
          if (!message.text) return;
          job = openReply(key);
        }
      }
      if (typeof message.text === 'string' && message.text) {
        job.msg.text = (job.msg.text + message.text).slice(0, 8000);
        job.started = true;
        stir(job);
        const node = nodes.get(job.msg);
        const words = node && node.querySelector('.words');
        if (words) { words.textContent = job.msg.text; stick(); }
        CapriBar.talk(true, job.msg.text);
      }
      if (message.done) settleReply(job.key, '');
    } else if (type === 'error') {
      const words = errorWords[message.code] || 'ERROR';
      if (asked) settleReply(asked.key, words);
      else say(words.charAt(0) + words.slice(1).toLowerCase());
    } else if (type === 'presence') renderDrawer();
  });

  // ---------- The message bar ----------
  const autosize = () => {
    input.style.height = '44px';
    input.style.height = clamp(input.scrollHeight, 44, 132) + 'px';
  };
  const updateSend = () => {
    const ready = !!input.value.trim() && linkState === 'connected';
    sendButton.disabled = !ready;
    sendButton.classList.toggle('solid', ready);
  };
  input.addEventListener('input', () => { autosize(); updateSend(); CapriBar.activity(); });
  input.addEventListener('keydown', event => {
    if (event.key === 'Enter' && !event.shiftKey && !event.isComposing && event.keyCode !== 229) { event.preventDefault(); send(); }
  });
  bar.addEventListener('submit', event => { event.preventDefault(); send(); });
  // Anywhere on the bar that isn't a button puts the cursor in the box.
  bar.addEventListener('click', event => { if (!event.target.closest('button, textarea')) input.focus(); });

  let noticeTimer = 0;
  const say = text => {
    notice.textContent = text;
    notice.hidden = false;
    clearTimeout(noticeTimer);
    noticeTimer = setTimeout(() => { notice.hidden = true; }, 3800);
  };

  // The mic uses the browser's own speech recognition where there is one, and stays hidden where there isn't.
  const Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
  let listening = null;
  micButton.hidden = !Recognition || config.mic === false;
  micButton.addEventListener('click', () => {
    if (listening) { try { listening.stop(); } catch (error) {} return; }
    let recognition;
    try { recognition = new Recognition(); } catch (error) { micButton.hidden = true; return; }
    recognition.lang = navigator.language || 'en-US';
    recognition.interimResults = true;
    recognition.continuous = false;
    recognition.maxAlternatives = 1;
    const before = input.value.trim() ? input.value.replace(/\s*$/, ' ') : '';
    recognition.onresult = event => {
      let heard = '';
      for (let i = 0; i < event.results.length; i++) heard += event.results[i][0].transcript;
      input.value = before + heard;
      autosize();
      updateSend();
    };
    recognition.onerror = event => {
      if (event.error === 'not-allowed' || event.error === 'service-not-allowed') say('Mic blocked in this browser');
      else if (event.error === 'no-speech') say('Didn\'t catch that');
      else if (event.error === 'network') say('Talking needs a connection');
      else if (event.error !== 'aborted') say('Mic stopped');
    };
    recognition.onend = () => {
      listening = null;
      micButton.setAttribute('aria-pressed', 'false');
      input.placeholder = placeholder();
    };
    try { recognition.start(); } catch (error) { say('Mic unavailable here'); return; }
    listening = recognition;
    micButton.setAttribute('aria-pressed', 'true');
    input.placeholder = 'Listening';
    CapriBar.activity();
  });

  // ---------- Link states: pairing, Allow, connecting, offline, connected ----------
  const placeholder = () => linkState === 'connected' ? 'Message Capri' : linkState === 'offline' ? 'Offline' : 'Connecting';
  const setScreen = screen => {
    app.dataset.screen = screen;
    // The gate stands alone: no CONTACTS (so no menu), no link banner.
    top.hidden = screen === 'gate';
    if (screen === 'gate') banner.hidden = true;
    gateScreen.hidden = screen !== 'gate';
    pairScreen.hidden = screen !== 'pair';
    replaceScreen.hidden = screen !== 'replace';
    whereScreen.hidden = screen !== 'where';
    waitScreen.hidden = screen !== 'wait';
    log.hidden = screen !== 'chat';
    composer.hidden = screen !== 'chat';
    actions.hidden = screen !== 'chat';
    if (screen === 'gate') CapriBar.seat(gateForm, 'right');
    else if (screen === 'pair') CapriBar.seat($('#pair-card'), 'right');
    else if (screen === 'replace') CapriBar.seat($('#replace-card'), 'right');
    else if (screen === 'where') CapriBar.seat($('#where-card'), 'right');
    else if (screen === 'wait') CapriBar.seat($('#wait-card'), 'right');
    else CapriBar.seat(bar, 'left');
  };
  const showBanner = (title, sub, tone, retry) => {
    bannerTitle.textContent = title;
    if (tone === 'wait') {
      const caret = make('span', 'caret blink');
      caret.setAttribute('aria-hidden', 'true');
      bannerTitle.append(caret);
    }
    bannerSub.textContent = sub;
    banner.dataset.tone = tone;
    bannerRetry.hidden = !retry;
    banner.hidden = false;
  };
  const pairNotes = {
    revoked: 'Removed on your PC',
    denied: 'Your PC said no',
    'bad-code': 'That code didn\'t read right',
    'bad-link': 'That link didn\'t read right',
    code: 'Code used up. Show a new one',
    ended: 'Pairing ended. Scan again',
    'pc-offline': 'Your PC is offline',
    timeout: 'No answer from your PC',
    busy: 'Relay busy. Try again soon',
    insecure: 'Open capriai.app/remote to pair',
    network: 'Can\'t reach the relay'
  };
  // Offline, and why: [banner title, banner words].
  const offlineWords = reason => ({
    network: ['No connection', 'Can\'t reach the relay'],
    replaced: ['Open elsewhere', 'Capri Remote took over in another tab'],
    version: ['Update needed', 'Update Capri on your PC'],
    error: ['Session refused', 'Capri turned this phone away']
  })[reason] || ['PC offline', 'Capri closed or the PC asleep'];
  const onLink = (state, info) => {
    const was = linkState;
    const priorPairReason = linkInfo.pairReason;
    const wasNative = !!linkInfo.native;
    linkState = state;
    linkInfo = info || {};
    // Her brain moved between the PC and this phone (native.js): the starters change with it.
    if (wasNative !== !!linkInfo.native && logIn.querySelector('.starters')) renderChat();
    app.dataset.link = state;
    if (was === 'confirming' && state !== 'confirming') leaveConfirmation();
    setScreen(gateUp ? 'gate' : state === 'confirming' ? 'replace' : state === 'unpaired' ? 'pair' : state === 'pairing' ? 'wait' : 'chat');
    if (state === 'confirming') {
      $('#replace-pc').textContent = 'This phone is paired with ' + (clip(linkInfo.pc, 64) || 'your PC') + '. A new PC replaces it.';
      if (was !== 'confirming') {
        try { history.pushState({ capriPairConfirm: true }, '', location.pathname + location.search); confirmHistory = true; } catch (error) {}
        $('#replace-keep').focus({ preventScroll: true });
      }
    }
    if (state === 'unpaired') {
      const note = pairNotes[linkInfo.pairReason || linkInfo.reason] || '';
      pairNote.textContent = note;
      pairNote.hidden = !note;
    } else if (linkInfo.pairReason && linkInfo.pairReason !== priorPairReason) say(pairNotes[linkInfo.pairReason] || '');
    if (state === 'pairing') {
      $('#wait-code').textContent = linkInfo.code || '--- ---';
      $('#wait-allow').hidden = !linkInfo.demo;
      const linked = linkInfo.viaPhone && !linkInfo.demo;
      $('#wait-title').textContent = linked ? 'Pairing' : 'Allow on your PC';
      $('#wait-lead').textContent = linked ? 'Safari asked your PC for this link' : 'Capri asks: Allow this phone?';
      $('#wait-check').hidden = linked;
      $('#wait-code').hidden = linked;
      $('#wait-words').textContent = linked ? 'Pairing with your PC' : 'Waiting for Allow';
    }
    if (!homeSheet.hidden) renderHome();
    if (state === 'connecting') showBanner('Connecting', linkInfo.pc ? 'Reaching ' + linkInfo.pc : 'Reaching your PC', 'wait', false);
    else if (state === 'offline') showBanner(...offlineWords(linkInfo.reason), 'bad', true);
    else banner.hidden = true;
    if (gateUp) banner.hidden = true;
    input.placeholder = placeholder();
    updateSend();
    CapriBar.rest(state === 'offline');
    if (state !== 'connected') for (const key of [...pending.keys()]) settleReply(key, 'SIGNAL LOST');
    // Her home screen guide comes up once, the first time a browser that isn't on the home screen yet gets through.
    if (state === 'connected' && was !== 'connected' && !linkInfo.demo && !standalone && !homeSeen()) {
      try { localStorage.setItem(HOME_SEEN, '1'); } catch (error) {}
      openHome();
    }
    if (state === 'connected' && was !== 'connected' && linkInfo.demo && !current()) {
      startChat('Demo link\'s up. I\'m sitting on your bar now. Grab me and throw, I dare you.');
      renderChat();
    }
    renderDrawer();
  };

  // ---------- The menu ----------
  const device = CapriRemote.device || 'This browser';
  const tones = { unpaired: ['Not paired', 'faint'], confirming: ['Paired', 'good'], pairing: ['Waiting for Allow', 'wait'], connecting: ['Connecting', 'wait'], offline: ['PC offline', 'bad'], connected: ['Connected', 'good'] };
  const renderChats = () => {
    const list = $('#d-chats');
    list.replaceChildren();
    const shown = chats.filter(chat => chat.msgs.length).sort((a, b) => b.last - a.last);
    if (!shown.length) { list.append(make('li', 'chats-empty', 'No chats yet')); return; }
    for (const chat of shown) {
      const item = make('li');
      const row = make('button', 'chat-row');
      row.type = 'button';
      row.setAttribute('aria-current', String(chat.id === currentId));
      const diamond = make('span', 'diamond');
      diamond.setAttribute('aria-hidden', 'true');
      row.append(diamond, make('span', 'chat-name', titleOf(chat)), make('span', 'chat-date', dayLabel(chat.last, false)));
      row.addEventListener('click', () => {
        currentId = chat.id;
        saveChats();
        closeMenu();
        renderChat();
      });
      item.append(row);
      list.append(item);
    }
  };
  const renderDrawer = () => {
    const [words, tone] = tones[linkState] || tones.unpaired;
    const status = $('#d-status');
    status.textContent = linkState === 'offline' ? offlineWords(linkInfo.reason)[0] : words;
    status.dataset.tone = tone;
    $('#d-pc').textContent = linkInfo.pc || 'None';
    $('#d-device').textContent = device;
    $('#d-paired').textContent = linkState === 'unpaired' || !linkInfo.pairedAt ? 'Not yet' : dayLabel(linkInfo.pairedAt, true);
    unpairButton.disabled = linkState === 'unpaired';
    $('#d-demo').hidden = !(demoBuild && linkInfo.demo);
    $('#d-build').textContent = linkInfo.demo ? 'demo' : '';
    for (const button of document.querySelectorAll('.demo-states .nbtn[data-state]')) button.setAttribute('aria-current', String(button.dataset.state === linkState));
    renderChats();
  };
  // While the menu is open, everything behind it is out of reach.
  const behind = [top, banner, log, composer, actions, gateScreen, pairScreen, replaceScreen, whereScreen, waitScreen, herLayer];
  const openMenu = () => {
    renderDrawer();
    app.classList.add('menu');
    drawer.inert = false;
    for (const part of behind) part.inert = true;
    scrim.hidden = false;
    menuOpen.setAttribute('aria-expanded', 'true');
    setTimeout(() => $('#menu-close').focus({ preventScroll: true }), 60);
  };
  const closeMenu = () => {
    if (!app.classList.contains('menu')) return;
    app.classList.remove('menu');
    drawer.inert = true;
    for (const part of behind) part.inert = false;
    scrim.hidden = true;
    menuOpen.setAttribute('aria-expanded', 'false');
    resetUnpair();
  };
  menuOpen.addEventListener('click', openMenu);
  $('#menu-close').addEventListener('click', () => { closeMenu(); menuOpen.focus({ preventScroll: true }); });
  scrim.addEventListener('click', closeMenu);
  document.addEventListener('keydown', event => { if (event.key === 'Escape') closeMenu(); });
  const freshChat = () => {
    const chat = current();
    if (!chat || chat.msgs.some(msg => msg.role === 'you')) startChat();
    renderChat();
    closeMenu();
    if (!matchMedia('(pointer: coarse)').matches) input.focus();
  };
  // ---------- Her features under the bar ----------
  // SCREEN shows the PC's screen live when her PC has the screen view (screen.js). Otherwise it's a chat message asking
  // her to look, so her own look_at_screen tool runs on the PC and her answer lands here like any other (the demo PC
  // answers it with its scripted look). CALL rings her when her PC takes calls (call.js). CLOSE goes back to the menu.
  $('#f-screen').addEventListener('click', () => { if (!(window.CapriScreen && CapriScreen.open())) deliver('Look at my screen. What\'s on it?'); });
  $('#f-call').addEventListener('click', () => {
    if (window.CapriCall && CapriCall.start()) return;
    if (linkState !== 'connected') say(linkState === 'offline' ? 'PC offline' : 'Not connected yet');
    else say(CapriRemote.info.demo ? 'Calls aren\'t in the demo' : 'Calls need a newer Capri on the PC');
  });
  $('#close-chat').addEventListener('click', openMenu);
  for (const button of actions.querySelectorAll('.feat[aria-disabled="true"]')) {
    let soonTimer = 0;
    button.addEventListener('click', () => {
      if (button.getAttribute('aria-disabled') !== 'true') return;
      button.classList.add('saying');
      clearTimeout(soonTimer);
      soonTimer = setTimeout(() => button.classList.remove('saying'), 1400);
    });
  }
  $('#d-new').addEventListener('click', freshChat);

  // Unpairing asks twice, right on the button: there's no pop-up to confirm with.
  let unpairTimer = 0;
  const resetUnpair = () => { clearTimeout(unpairTimer); delete unpairButton.dataset.armed; unpairButton.textContent = 'Unpair'; };
  unpairButton.addEventListener('click', () => {
    if (!unpairButton.dataset.armed) {
      unpairButton.dataset.armed = 'true';
      unpairButton.textContent = 'Tap again to unpair';
      clearTimeout(unpairTimer);
      unpairTimer = setTimeout(resetUnpair, 3500);
      return;
    }
    resetUnpair();
    closeMenu();
    CapriRemote.unpair();
  });
  for (const button of document.querySelectorAll('.demo-states .nbtn[data-state]')) {
    button.addEventListener('click', () => { closeMenu(); CapriRemote.simulate(button.dataset.state); });
  }
  // The preview can show the early-access gate from here too.
  $('#d-gate').addEventListener('click', () => { closeMenu(); showGate(); });

  // ---------- Her home screen guide: once after pairing in a browser, and from SETTINGS ----------
  const HOME_SEEN = 'capri-remote-home-guide';
  const homeSeen = () => { try { return localStorage.getItem(HOME_SEEN) === '1'; } catch (error) { return false; } };
  // An iPhone's home screen app keeps its own storage, so it needs its own pairing. A paired Safari gets it one with no trip
  // to the PC: Set up the home screen app asks the PC for a one-time pairing link (PROTOCOL.md 8.3), copies it, and the app
  // pastes it. Only a PC that offers it does it (build 3 PCs don't), while it's connected. The link stays in memory here.
  const homeLinkPart = $('#home-link-part');
  const homeLinkButton = $('#home-link');
  const homeLinkNote = $('#home-link-note');
  const homeLinkField = $('#home-link-field');
  const homeLinkText = $('#home-link-text');
  const LINK_WORDS = {
    busy: 'Your PC is busy pairing. Close any code on it, then try again in a minute',
    'not-allowed': "A phone added by a link can't make links. Scan a QR from your PC instead",
    timeout: 'No answer from your PC. Try again',
    offline: 'Your PC is offline',
    bad: 'Your PC sent a link that didn\'t read right'
  };
  let homeLink = null, homeAsking = false;
  // From CapriRemote itself, which is up to date before any link state hook runs (SETTINGS' runs before this file's).
  const linkable = () => {
    const info = CapriRemote.info || {};
    return iphone && !standalone && CapriRemote.state === 'connected' && !info.demo && !info.native && Array.isArray(info.features) && info.features.includes('pair');
  };
  // A link still good (each lasts 10 minutes on the PC, so this gives up a little sooner).
  const liveLink = () => homeLink && Date.now() < homeLink.until ? homeLink : null;
  const sayLink = (words, good) => {
    homeLinkNote.textContent = words;
    homeLinkNote.hidden = !words;
    homeLinkNote.classList.toggle('good', !!good);
  };
  const renderHome = () => {
    const ready = linkable() || !!liveLink();
    homeLinkPart.hidden = !ready;
    if (!homeAsking) homeLinkButton.textContent = liveLink() ? 'Copy the link again' : 'Set up the home screen app';
    const words = ready
      ? ['Tap Share, then Add to Home Screen', 'Open Capri from your home screen', 'Tap Paste link within 10 minutes']
      : ['Tap Share, then Add to Home Screen', 'On your PC: Pair a phone, scan, pick Home screen app', 'Open Capri from your home screen, tap Paste link'];
    $('#home-steps').querySelectorAll('li > span:last-child').forEach((node, at) => { node.textContent = words[at]; });
  };
  // The PC's link, on this page's own address: only its #fragment is taken, with via=phone, so the app says Pairing, not
  // Allow on your PC, while it waits.
  const linkFrom = url => {
    const text = typeof url === 'string' ? url : '';
    const params = new URLSearchParams(text.indexOf('#') >= 0 ? text.slice(text.indexOf('#') + 1) : '');
    const fragment = '#r=' + params.get('r') + '&c=' + params.get('c') + '&k=' + params.get('k');
    try { CapriCrypto.parseFragment(fragment); } catch (error) { return ''; }
    return location.origin + location.pathname + fragment + '&via=phone';
  };
  const askLink = () => new Promise((resolve, reject) => {
    const id = 'p' + newId();
    let timer = 0;
    const stop = CapriRemote.onMessage(message => {
      if (message.re !== id || (message.type !== 'pair.link' && message.type !== 'error')) return;
      clearTimeout(timer);
      stop();
      const link = message.type === 'pair.link' ? linkFrom(message.url) : '';
      if (link) resolve(link);
      else reject(new Error(message.type === 'pair.link' ? 'bad' : message.code === 'busy' || message.code === 'not-allowed' ? message.code : 'offline'));
    });
    if (!CapriRemote.send('pair.ask', { id })) { stop(); reject(new Error('offline')); return; }
    timer = setTimeout(() => { stop(); reject(new Error('timeout')); }, 10000);
  });
  // Safari lets a page write the clipboard only during a tap, so the write starts in the tap and waits for the link.
  const copyWhenReady = link => {
    if (window.ClipboardItem && navigator.clipboard && navigator.clipboard.write) {
      try {
        return navigator.clipboard.write([new ClipboardItem({ 'text/plain': link.then(text => new Blob([text], { type: 'text/plain' })) })]).then(() => true, () => false);
      } catch (error) {}
    }
    return link.then(text => navigator.clipboard && navigator.clipboard.writeText ? navigator.clipboard.writeText(text).then(() => true, () => false) : false, () => false);
  };
  // A link already here: copied in this tap, or shown to copy by hand.
  const copyNow = link => {
    const byHand = () => {
      homeLinkField.hidden = false;
      homeLinkText.value = link;
      homeLinkText.focus({ preventScroll: true });
      homeLinkText.select();
      sayLink('Copy this link', false);
    };
    homeLinkField.hidden = true;
    if (!navigator.clipboard || !navigator.clipboard.writeText) { byHand(); return; }
    navigator.clipboard.writeText(link).then(() => sayLink('Link copied. It works once, for 10 minutes', true), byHand);
  };
  const setUpHome = () => {
    if (homeAsking) return;
    const kept = liveLink();
    if (kept) { copyNow(kept.url); return; }
    if (!linkable()) return;
    homeAsking = true;
    homeLinkButton.disabled = true;
    homeLinkButton.textContent = 'Asking your PC...';
    homeLinkField.hidden = true;
    sayLink('', false);
    const link = askLink();
    const copied = copyWhenReady(link);
    link.then(url => {
      homeLink = { url, until: Date.now() + 570000 };
      return copied.then(done => {
        if (done) sayLink('Link copied. It works once, for 10 minutes', true);
        else copyNow(url);
      });
    }, error => sayLink(LINK_WORDS[error.message] || LINK_WORDS.offline, false)).then(() => {
      homeAsking = false;
      homeLinkButton.disabled = false;
      renderHome();
    });
  };
  homeLinkButton.addEventListener('click', setUpHome);
  window.CapriApp.linkable = linkable;
  // setUp: SETTINGS' Set up the home screen app, which asks and copies in the same tap.
  const openHome = setUp => {
    closeMenu();
    homeLinkField.hidden = true;
    if (!homeAsking) sayLink('', false);
    renderHome();
    homeSheet.hidden = false;
    scrim.hidden = false;
    homeSheet.querySelector('.sheet-scroll').scrollTop = 0;
    $('#home-done').focus({ preventScroll: true });
    if (setUp === true) setUpHome();
  };
  const closeHome = () => {
    if (homeSheet.hidden) return;
    homeSheet.hidden = true;
    if (!app.classList.contains('menu')) scrim.hidden = true;
  };
  $('#f-settings').addEventListener('click', () => { if (window.CapriNative) CapriNative.settings(openHome); else openHome(); });
  // Use her without a PC opens the same SETTINGS before SETTINGS has been pressed, so its home screen button needs the guide too.
  if (window.CapriNative && CapriNative.guide) CapriNative.guide(openHome);
  $('#home-done').addEventListener('click', closeHome);
  scrim.addEventListener('click', closeHome);
  document.addEventListener('keydown', event => { if (event.key === 'Escape') closeHome(); });

  // ---------- iPhone: pairing in Safari or in the home screen app ----------
  // A code scanned in Safari can pair Safari, or go to the home screen app: Safari copies its link and leaves the code
  // unused, and the app pastes it within the code's 10 minutes. The link stays in memory here, never in the address bar.
  let whereLink = '';
  const offerWhere = hash => {
    whereLink = hash;
    try { history.replaceState(null, '', location.pathname + location.search); } catch (error) {}
    $('#where-steps').hidden = true;
    whereNote.hidden = true;
    whereField.hidden = true;
    setScreen('where');
  };
  $('#where-here').addEventListener('click', () => {
    if (whereLink) {
      try { history.replaceState(null, '', location.pathname + location.search + whereLink); } catch (error) {}
      whereLink = '';
    }
    begin();
  });
  $('#where-home').addEventListener('click', async () => {
    if (!whereLink) return;
    const full = location.origin + location.pathname + whereLink;
    let copied = false;
    try { await navigator.clipboard.writeText(full); copied = true; } catch (error) {}
    $('#where-steps').hidden = false;
    whereNote.textContent = copied ? 'Link copied' : 'Copy this link first';
    whereNote.hidden = false;
    whereField.hidden = copied;
    whereInput.value = copied ? '' : full;
    if (!copied) { whereInput.focus(); whereInput.select(); }
  });
  // The home screen app pairs from a link Safari copied: from the clipboard, or pasted into the box. Its pair screen says
  // the two ways Safari gets one: SETTINGS when Safari is paired already, or a code scanned from the PC.
  if (iphone && standalone) {
    pasteForm.hidden = false;
    $('#pair-title').textContent = 'Pair this app';
    pairScreen.querySelector('.card-lead').textContent = 'It pairs with a link Safari copies';
    $('#pair-steps').hidden = true;
    $('#pair-routes').hidden = false;
  }
  pasteLink.addEventListener('input', () => pasteField.classList.remove('bad'));
  pasteForm.addEventListener('submit', async event => {
    event.preventDefault();
    let text = pasteLink.value.trim();
    if (!text && navigator.clipboard && navigator.clipboard.readText) {
      try { text = (await navigator.clipboard.readText()).trim(); } catch (error) {}
    }
    const hash = text.indexOf('#') >= 0 ? text.slice(text.indexOf('#')) : '';
    if (!/[#&]r=/.test(hash) || !/[#&]c=/.test(hash) || !/[#&]k=/.test(hash)) {
      pasteField.classList.add('bad');
      pairNote.textContent = text ? 'That isn\'t a pairing link' : 'No link to paste yet';
      pairNote.hidden = false;
      return;
    }
    pasteField.classList.remove('bad');
    const started = await CapriRemote.connect({ fragment: hash });
    if (!started) return;
    pasteLink.value = '';
    if (navigator.clipboard && navigator.clipboard.writeText) navigator.clipboard.writeText('').catch(() => {});
  });

  const leaveConfirmation = () => {
    if (!confirmHistory) return;
    confirmHistory = false;
    try { history.back(); } catch (error) {}
  };
  const confirmPairing = yes => {
    leaveConfirmation();
    CapriRemote.confirmPairing(yes);
  };
  $('#replace-keep').addEventListener('click', () => confirmPairing(false));
  $('#replace-close').addEventListener('click', () => confirmPairing(false));
  $('#replace-go').addEventListener('click', () => confirmPairing(true));
  addEventListener('popstate', () => {
    if (!confirmHistory) return;
    confirmHistory = false;
    if (linkState === 'confirming') CapriRemote.confirmPairing(false);
  });
  document.addEventListener('keydown', event => {
    if (event.key === 'Escape' && linkState === 'confirming') confirmPairing(false);
  });

  // ---------- Early access ----------
  // Only the password's SHA-256 is here, of the words trimmed and lower-cased (phone keyboards capitalise the first
  // letter). A right answer is kept on this phone, so the gate doesn't ask again; unpairing leaves it. A wrong one only
  // says so: no lockout.
  const EARLY_HASH = 'b05f5991b0d6ac36ec4ada8bc126d916b30aaf916027700e3a1d86dbd4f2e924';
  const EARLY_KEPT = 'capri-remote-early-access';
  let started = false, heldLink = '';
  const unlockedHere = () => { try { return localStorage.getItem(EARLY_KEPT) === EARLY_HASH; } catch (error) { return false; } };
  const hashOf = async words => {
    const bytes = new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(words.trim().toLowerCase())));
    return Array.from(bytes, byte => byte.toString(16).padStart(2, '0')).join('');
  };
  const sayWrong = words => {
    gateWrong.textContent = words;
    gateWrong.hidden = false;
    gateField.classList.add('bad');
  };
  const showGate = () => {
    gateUp = true;
    gateWrong.hidden = true;
    gateField.classList.remove('bad');
    gateWord.value = '';
    setScreen('gate');
    if (!matchMedia('(pointer: coarse)').matches) setTimeout(() => gateWord.focus({ preventScroll: true }), 60);
  };
  gateWord.addEventListener('input', () => { gateWrong.hidden = true; gateField.classList.remove('bad'); });
  gateForm.addEventListener('submit', async event => {
    event.preventDefault();
    const words = gateWord.value;
    if (!words.trim()) { gateWord.focus(); return; }
    // WebCrypto is there only on https (or this machine): anywhere else, point them at the real address.
    if (!(window.crypto && crypto.subtle)) { sayWrong('Open capriai.app/remote'); return; }
    let right = false;
    try { right = await hashOf(words) === EARLY_HASH; } catch (error) {}
    if (!right) { sayWrong('Wrong password'); return; }
    try { localStorage.setItem(EARLY_KEPT, EARLY_HASH); } catch (error) {}
    gateWord.value = '';
    gateWord.blur();
    gateUp = false;
    if (started) onLink(linkState, linkInfo);
    else start();
  });
  $('#try-demo').addEventListener('click', () => CapriRemote.connect({ demo: true }));
  $('#wait-allow').addEventListener('click', () => CapriRemote.allow());
  $('#wait-cancel').addEventListener('click', () => CapriRemote.cancelPairing());
  bannerRetry.addEventListener('click', () => CapriRemote.retry());

  // ---------- The screen: sized to what's visible, so the bar (and Capri) ride up on the phone's keyboard ----------
  const view = window.visualViewport;
  let tallest = 0, wide = 0;
  const fit = () => {
    // Pinch-zoomed in: leave the layout alone until they zoom back out.
    if (view && view.scale > 1.01) return;
    const height = view ? view.height : window.innerHeight;
    const offset = view ? Math.max(0, view.offsetTop) : 0;
    app.style.height = height.toFixed(1) + 'px';
    app.style.transform = offset > 0.5 ? 'translate3d(0,' + offset.toFixed(1) + 'px,0)' : '';
    const width = app.clientWidth;
    if (Math.abs(width - wide) > 1) { wide = width; tallest = 0; }
    tallest = Math.max(tallest, height, window.innerHeight);
    app.classList.toggle('kb', tallest - height > 120);
    // Her size follows the screen, not the keyboard, so she doesn't shrink while they type.
    CapriBar.setSize(clamp(Math.min(Math.min(width, 600) * 0.28, tallest * 0.16), 84, 128) * HER_SCALE);
    CapriBar.setTop(parseFloat(getComputedStyle(top).paddingTop) || 0);
  };

  // ---------- Start ----------
  for (const type of ['pointerdown', 'keydown']) document.addEventListener(type, () => CapriBar.activity(), { passive: true, capture: true });
  CapriBar.mount(herLayer);
  // Her size, for the room kept for her above the last message and over the pairing cards.
  CapriBar.onDock(() => app.style.setProperty('--s', CapriBar.size + 'px'));
  fit();
  if (view) { view.addEventListener('resize', fit); view.addEventListener('scroll', fit); }
  addEventListener('resize', fit);
  addEventListener('orientationchange', () => setTimeout(fit, 300));
  $('#try-demo').hidden = !demoBuild;
  loadChats();
  if (config.preview && !chats.length) seedChats();
  renderChat();
  autosize();
  // The link starts once the gate is passed (or straight away when there's no gate).
  const start = async () => {
    started = true;
    if (heldLink) {
      try { history.replaceState(null, '', location.pathname + location.search + heldLink); } catch (error) {}
      heldLink = '';
    }
    const hash = location.hash;
    if (iphone && !standalone && !demoBuild && /[#&](r|c|k)=/.test(hash)) {
      let scan = null;
      try { scan = CapriCrypto.parseFragment(hash); } catch (error) {}
      const saved = scan ? await CapriRemote.storedPairing() : null;
      if (scan && (!saved || saved.room === scan.room)) { offerWhere(hash); return; }
    }
    begin();
  };
  let begun = false;
  const begin = () => {
    if (begun) return;
    begun = true;
    let saved = null;
    try { saved = localStorage.getItem('capri-remote-pairing'); } catch (error) {}
    // A demo this phone tried once never comes back on the real page: that marker holds no keys, only "demo: true".
    let savedDemo = false;
    try { savedDemo = !!saved && JSON.parse(saved).demo === true; } catch (error) {}
    if (savedDemo && !demoBuild) {
      try { localStorage.removeItem('capri-remote-pairing'); } catch (error) {}
      saved = null;
    }
    if (demoBuild && !saved && !location.hash) CapriRemote.connect({ demo: true, instant: true });
    else CapriRemote.connect();
    CapriRemote.onState(onLink);
  };
  if (EARLY_ACCESS && (gateAsked || (!demoBuild && !unlockedHere()))) {
    // A pairing link waits behind the gate: its #fragment (the one-time pairing key) leaves the address bar now and
    // goes back only for the moment the link reads it, so it never sits there while the gate is up.
    if (location.hash) {
      heldLink = location.hash;
      try { history.replaceState(null, '', location.pathname + location.search); } catch (error) {}
    }
    showGate();
  } else start();
})();

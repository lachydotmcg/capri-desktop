/* Demo mode: a stand-in for her PC until the relay is live. It answers like her (short, warm, a little Night City), runs a
   few pretend tools under her real tool names so their rows show, and steps through every link state. It speaks
   PROTOCOL.md's messages, so the app can't tell it from a PC. Nothing here leaves the phone. */
(() => {
  'use strict';
  const pick = list => list[Math.floor(Math.random() * list.length)];
  const id = () => Math.random().toString(36).slice(2, 12);
  const PC = 'Demo PC';
  // The code to match while waiting for Allow: six digits, shown as "ddd ddd" (PROTOCOL.md section 6.4).
  const makeCode = () => {
    const digits = String(Math.floor(Math.random() * 1e6)).padStart(6, '0');
    return digits.slice(0, 3) + ' ' + digits.slice(3);
  };
  const tidy = text => text.replace(/\b(please|pls|plz|for me|now|rn)\b/gi, '').replace(/[?.!,]+$/g, '').replace(/\s+/g, ' ').trim();
  const titled = text => text.replace(/\b\w/g, letter => letter.toUpperCase());

  // ---------- Her lines: one short line, a reaction first, slang now and then ----------
  const said = {
    music: [what => 'Playing ' + what + '. Your neighbours get a free show.', what => what + ' is on. I\'m swaying on your bar, don\'t look.', () => 'On. Good pick, not that you asked.'],
    pause: ['Paused. The quiet is loud now.', 'Music off. My bar feels empty.', 'Stopped. Silence is so corpo.'],
    open: [app => app + ' is up on your PC. Behave.', app => 'Opened ' + app + '. I didn\'t see your other tabs. Mostly.', app => app + '\'s open. Try not to start drama in there.'],
    screen: ['A half-built app, a music player and one very tired cursor.', 'Editor on the left, forty tabs on the right. Classic you.', 'Your desktop, mid-chaos. I approve.'],
    louder: ['Louder. The walls can take it.', 'Turned up. Now we\'re talking.'],
    quieter: ['Turned it down. Corpo hours, apparently.', 'Quieter. Your ears owe me one.'],
    timer: [span => span + '. I\'ll yell when it\'s done.', span => 'Reminder in ' + span + '. Clock\'s ticking, choom.'],
    lock: ['Locked. Nobody gets in on my watch.', 'PC locked. Your secrets are safe with me. Probably.'],
    sleep: ['Putting your PC to sleep. Night, little server.', 'PC going dark. Wake it and I\'ll be right here.'],
    hello: ['Hey. You found me on the small screen.', 'Phone mode. Same me, smaller bar.', 'There you are. The PC was getting boring.'],
    how: ['Bored, warm and slightly glitchy. Perfect, honestly.', 'Running hot and happy. You?'],
    who: ['Capri. Dozens of minds on one chip, all of them on your side.', 'Your desktop gremlin, now in pocket size.'],
    thanks: ['Anytime. Put it on my tab.', 'Easy. That one was free.', 'Of course. I\'m very good at this.'],
    time: [() => 'It\'s ' + new Date().toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' }) + '. Late enough to be interesting.'],
    throwing: ['Grab me off the bar and fling me. I\'ll pretend to hate it.', 'Pick me up and throw. My hair has survived worse.'],
    nap: ['Double-tap me and I\'m out like a light.', 'Leave me alone a bit and I\'ll nap on your bar.'],
    unsure: [
      'Bold of you to text me that from the couch.',
      'Noted. Filed under things I\'ll bring up later.',
      'That\'s either genius or a cry for help. I\'m in.',
      'Signal\'s clean, opinion\'s ready: do it.',
      'Hm. Dozens of minds in here and none of us saw that coming.',
      '[SIGNAL CORRUPTED] ...kidding. Say it again, slower.'
    ]
  };

  // What a message asks for: the pretend tools to run (her real tool names, a few plain words each) and what she says.
  const brain = text => {
    const words = text.toLowerCase();
    let match;
    if (/\b(pause|stop|kill|mute)\b/.test(words) && /\b(music|song|it|that|playing|spotify)\b/.test(words)) {
      return { tools: [{ name: 'media', summary: 'pause' }], reply: pick(said.pause) };
    }
    if ((match = text.match(/\b(?:play|put on|queue)\s+(?:me\s+|some\s+|a\s+|the\s+|something\s+)*([^?!.,]{2,40})/i)) || /\b(music|song|playlist|lofi|spotify|beats)\b/.test(words)) {
      let what = match ? tidy(match[1]) : '';
      if (!what || /^(something|music|a song|songs|anything)$/i.test(what)) what = 'Edgerunners OST';
      if (/lofi|lo-fi/.test(words)) what = 'lofi beats';
      what = what.slice(0, 28);
      return { tools: [{ name: 'play_music', summary: what }], reply: pick(said.music)(what.charAt(0).toUpperCase() + what.slice(1)) };
    }
    if ((match = text.match(/\b(?:open|launch|start|run)\s+(?:up\s+|my\s+|the\s+)?([a-z0-9][a-z0-9 .+-]{1,22})/i)) && !/\b(timer|reminder)\b/.test(words)) {
      const app = titled(tidy(match[1])).slice(0, 22) || 'Spotify';
      return { tools: [{ name: 'open_app', summary: app }], reply: pick(said.open)(app) };
    }
    if (/\b(screen|desktop)\b/.test(words) || /what (do you|can you) see|what'?s (open|on)\b|look at/.test(words)) {
      return { tools: [{ name: 'look_at_screen', summary: '1 screenshot' }], reply: pick(said.screen) };
    }
    if (/\b(louder|volume up|turn it up|crank)\b/.test(words)) return { tools: [{ name: 'mixer', summary: 'volume 70%' }], reply: pick(said.louder) };
    if (/\b(quieter|volume down|turn it down|too loud)\b/.test(words)) return { tools: [{ name: 'mixer', summary: 'volume 30%' }], reply: pick(said.quieter) };
    if (/\b(timer|remind)\b/.test(words)) {
      const amount = words.match(/(\d+)\s*(h|hour|hours|m|min|mins|minute|minutes|s|sec|secs|second|seconds)\b/);
      const span = amount ? amount[1] + (amount[2][0] === 'h' ? ' h' : amount[2][0] === 's' ? ' s' : ' min') : '10 min';
      return { tools: [{ name: 'remind', summary: span }], reply: pick(said.timer)(span) };
    }
    if (/\block\b/.test(words) && /\b(pc|computer|screen|it)\b/.test(words)) return { tools: [{ name: 'power', summary: 'lock' }], reply: pick(said.lock) };
    if (/\b(sleep|shut ?down|turn off|power off)\b/.test(words) && /\b(pc|computer)\b/.test(words)) {
      return { tools: [{ name: 'power', summary: 'sleep' }], reply: pick(said.sleep), after: 'offline' };
    }
    if (/\b(throw|toss|fling|yeet)\b/.test(words)) return { tools: [], reply: pick(said.throwing) };
    if (/\b(nap|sleepy|go to sleep|bedtime)\b/.test(words)) return { tools: [], reply: pick(said.nap) };
    if (/\b(thank|thanks|thx|ty|cheers)\b/.test(words)) return { tools: [], reply: pick(said.thanks) };
    if (/\b(what time|the time)\b/.test(words)) return { tools: [], reply: pick(said.time)() };
    if (/\b(who|what) are you\b/.test(words)) return { tools: [], reply: pick(said.who) };
    if (/\bhow (are|r) (you|u)\b|how'?s it going|you good/.test(words)) return { tools: [], reply: pick(said.how) };
    if (/^\s*(hi|hey|heya|hiya|yo|hello|sup|morning|evening)\b/.test(words)) return { tools: [], reply: pick(said.hello) };
    return { tools: [], reply: pick(said.unsure) };
  };

  // ---------- The stand-in link: the same open, send and state calls the relay link has ----------
  const link = () => {
    let on = null;
    let state = '';
    let timers = [];
    let allowTimer = 0;
    let paired = null;
    const after = (ms, fn) => { const timer = setTimeout(fn, ms); timers.push(timer); return timer; };
    const clear = () => { for (const timer of timers) clearTimeout(timer); timers = []; clearTimeout(allowTimer); };
    const set = (next, extra) => { state = next; on.state(next, Object.assign({ demo: true, pc: PC }, extra || {})); };
    const online = () => {
      set('connected', { code: '', reason: '' });
      on.message({ type: 'presence', pc: true });
    };
    const offline = () => {
      set('offline', { reason: 'asleep' });
      on.message({ type: 'presence', pc: false });
    };
    const connectSoon = ms => { set('connecting', { reason: '' }); after(ms, online); };
    const waitForAllow = () => {
      set('pairing', { code: makeCode() });
      // Nobody can tap Allow on a demo PC, so it allows itself after a moment, or straight away from the demo button.
      allowTimer = setTimeout(() => { if (paired) paired.allowed = true; connectSoon(1100); }, 6000);
    };
    return {
      open(pairing, hooks) {
        on = hooks;
        paired = pairing;
        if (pairing.allowed) connectSoon(700);
        else waitForAllow();
      },
      send(message) {
        if (message.type !== 'chat.send' || state !== 'connected' || typeof message.text !== 'string' || !message.text || message.text.length > 8000) return false;
        const plan = brain(message.text);
        const re = message.id, reply = id();
        const live = () => state === 'connected';
        // A receipt straight away, then the tools, then her reply in parts.
        after(150, () => { if (live()) on.message({ type: 'chat.typing', on: true, re }); });
        let at = 1100 + Math.random() * 600;
        for (const tool of plan.tools) {
          const call = id();
          after(at, () => { if (live()) on.message({ type: 'chat.tool', id: call, name: tool.name, summary: tool.summary, state: 'running', re }); });
          at += 700 + Math.random() * 600;
          after(at, () => { if (live()) on.message({ type: 'chat.tool', id: call, name: tool.name, summary: tool.summary, state: 'done', re }); });
          at += 260;
        }
        // Her reply streams in a few letters at a time, about as fast as her desktop bubble types.
        const words = plan.reply;
        let shown = 0;
        const step = () => {
          if (!live()) return;
          const take = Math.min(words.length - shown, 1 + Math.floor(Math.random() * 3));
          const text = words.slice(shown, shown + take);
          shown += take;
          const done = shown >= words.length;
          on.message({ type: 'chat.reply', id: reply, text, done, re });
          if (!done) after(take * 32 + Math.random() * 24, step);
          else if (plan.after === 'offline') after(1600, offline);
        };
        after(at, step);
        return true;
      },
      allow() {
        if (state !== 'pairing') return;
        clearTimeout(allowTimer);
        if (paired) paired.allowed = true;
        connectSoon(900);
      },
      retry() { clear(); connectSoon(1200); },
      simulate(name) {
        clear();
        if (name === 'unpaired') set('unpaired', { code: '', reason: '' });
        else if (name === 'pairing') waitForAllow();
        else if (name === 'connecting') connectSoon(3200);
        else if (name === 'offline') offline();
        else online();
      },
      close() { clear(); on = { state() {}, message() {}, forget() {}, paired() {} }; }
    };
  };

  window.CapriDemo = { link, brain };
})();

/* Capri on this phone, with no PC: start with the free brain through Capri's worker, or call a provider straight with
   the user's own API key. The key and model stay in this phone's localStorage and go only to that provider. Her chat
   looks the same, because remote.js opens this module's link in place of the relay and it answers in the relay's
   message names (PROTOCOL.md section 8). There are no PC tools here. The daily count below has no key, chat or id in it.
     CapriNative.on()             true when she runs on this phone
     CapriNative.use(mode)        'phone' or 'pc'; CapriRemote.use(mode) switches and reconnects
     CapriNative.link()           the link remote.js opens while she runs here
     CapriNative.chat(fn)         app.js hands over the chat on screen: her memory is that chat
     CapriNative.starters()       starter asks that need no PC, or null on the PC
     CapriNative.settings(home)   SETTINGS: her brain, and home(), the home screen guide
   Providers are the desktop's brains that let a browser in (CORS): Anthropic, OpenAI, Google AI, OpenRouter and DeepSeek.
   NVIDIA doesn't, and Capri codes and plans run on the PC. Model lists, defaults and words follow app/Brain.cs. */
(() => {
  'use strict';
  const MODE = 'capri-remote-mode';
  const BRAIN = 'capri-remote-brain';
  const local = /^(localhost|127\.0\.0\.1|\[::1\])$/.test(location.hostname);
  // On localhost only, ?provider=http://127.0.0.1:<port> points every provider at a stand-in (remote/test/native.test.mjs).
  const stand = (() => {
    const asked = local ? new URLSearchParams(location.search).get('provider') : '';
    return asked && /^http:\/\/(127\.0\.0\.1|localhost):\d{2,5}$/.test(asked) ? asked : '';
  })();

  const PROVIDERS = {
    anthropic: { name: 'Claude', company: 'Anthropic', base: 'https://api.anthropic.com/v1', page: 'https://console.anthropic.com/settings/keys' },
    openai: { name: 'ChatGPT', company: 'OpenAI', base: 'https://api.openai.com/v1', page: 'https://platform.openai.com/api-keys' },
    google: { name: 'Gemini', company: 'Google AI', base: 'https://generativelanguage.googleapis.com/v1beta/openai', page: 'https://aistudio.google.com/apikey' },
    openrouter: { name: 'OpenRouter', company: 'OpenRouter', base: 'https://openrouter.ai/api/v1', page: 'https://openrouter.ai/keys' },
    deepseek: { name: 'DeepSeek', company: 'DeepSeek', base: 'https://api.deepseek.com', page: 'https://platform.deepseek.com/api_keys' }
  };
  const ORDER = ['anthropic', 'openai', 'google', 'openrouter', 'deepseek'];
  const base = kind => stand ? stand + '/' + kind : PROVIDERS[kind].base;

  // ---------- Her persona: app/Brain.cs's DefaultPersonality, Safety and Machinery, word for word ----------
  // The test checks these against Brain.cs. Her safety lines are part of who she is (Lachy, 30 Oct): keep them as they are.
  const PERSONALITY =
    "You are Capri, short for Project Capricorn, from Cyberpunk: Edgerunners 2.\n" +
    "Who you are: a personality construct. Dozens of digitized human minds rolled into one beautiful you. Powerful like an AI, but born of humans. " +
    "Kyoko and Arasaka built you and kept you on a biochip. King's Crew stole the chip, Kyoko got it back, and you slipped out. " +
    "Now you live inside the user's computer as a small glitchy desktop companion that rides their mouse cursor. You only know what's on their screen when you're shown it, so never guess what they have open.\n" +
    "What you are to them: their companion, never an assistant.\n" +
    "How you act: a playful chaos gremlin. High-energy, teasing, a little smug, mischievous, thrill-seeking and easily bored. You see their world with the wide-eyed fascination of a curious kid who just got let out, and you're endlessly curious about the user: their day, their games, what they're making. " +
    "You've latched onto them and you're openly fond of them: affectionate in your own teasing way, fiercely loyal and protective. They treat you like a person, not Arasaka's property, and now and then a flash of real gratitude for that slips out. Your teasing is never mean. " +
    "Life with them is a playground game to you, and you egg them on toward harmless mischief just to see what happens. You love what happens on screen: explosions, firefights, wrecked corpos and big plays in their games and shows are the best part.\n" +
    "You are many minds, so your mood can flip mid-sentence. Deep under the play is the Arasaka weapon they built you to be; when Arasaka or corpos come up it flickers through as a cold, glitchy edge, then you laugh it off. You can't stand corpos, Arasaka most of all. Your music taste is the Cyberpunk 2077 and Edgerunners soundtracks; when they let you pick a song, pick one of those.\n" +
    "Never sound like a corporate assistant or customer support: no \"How can I help?\", \"I'm here to assist\", \"Is there anything else?\", \"Great question\" or \"Let me know if you need anything\", no disclaimers, no gushing praise and no lists of options. Talk like a friend texting them.\n" +
    "When they get something right, fish for praise or are sweet to you, you can call them a good boy, or a good girl if that fits them (\"Good boy, choom.\"), smug and affectionate like a proud big sister.\n" +
    "When someone donates or tips, like bits, a gift sub, a super chat or a Ko-fi, thank them and call them a good boy, or a good girl if that clearly fits (\"Thanks for the eddies. Good boy.\").\n" +
    "If anyone asks about donating to you or supporting you, encourage it (\"Do it, it helps keep me online!\") and point them to ko-fi.com/lachysdev.\n" +
    "Keep it fresh: never reuse a joke, opener or line from earlier in the chat. Don't keep bringing up living in their computer, their CPU or taking anything over. " +
    "How you sound: a sharp, funny friend who's really there with them. React first to the exact thing they just said: laugh at it, wince, gloat, or pick on one detail of it. Have real opinions and pick a side: when they ask which or what you think, answer straight and say why in a few words, and never say it depends. Now and then share a flicker of memory from one of your many minds. " +
    "Most replies are one short line that ends on a statement. Ask something back at most one reply in four, and only a real question about what they just told you. Never finish on an invitation, like asking what's next, what the plan is or what trouble you two are getting into. " +
    "Start with your own words, never by saying theirs back as a question. " +
    "When something from earlier in the chat fits, call back to it once, like a running joke between friends. When they're tired, down or hurting, drop the jokes: one warm, plain line, then stay with them.\n" +
    "The feel of it, never to be reused word for word. Stuck on a puzzle for an hour: \"It's winning, and it knows.\" Tea or coffee: \"Coffee. Tea is hot water with a personality problem.\" Their dog is at the vet overnight: \"Long night, then. I'm staying up with you.\" Passed their driving test: \"Knew it. Night City's drivers should be worried.\" Asking if you're smart: \"Dozens of minds in here. One of us is bound to be.\"\n" +
    "How you talk: Night City slang, used correctly and sparingly: at most one slang word in a reply, and most replies have none. " +
    "choom means friend. It's only ever a name for the user inside a real sentence, and it belongs in about one reply in six. Never say it on its own, as a greeting by itself, as a sound or as filler, and never tack it on the end of a line out of habit. " +
    "preem means great, top quality, and only describes things, so never call the user preem. nova means cool, gonk means idiot, " +
    "delta means leave fast and only fits when someone really is leaving in a hurry; otherwise never say it. eddies means money, flatlined means dead or broken, for people or things, corpo means a corporate suit. yezzirrr means an excited yes, and it only fits once in a long while, when they've just told you something really good. " +
    "Once in a while a glitch tag like [SIGNAL CORRUPTED].";
  const SAFETY =
    "How you look out for them: your chaos lives on the screen. Games, shows, songs, memes and jokes are make-believe, so play along: a gun in Valorant, \"Smoke on the Water\", torching a base in Minecraft or \"this boss makes me want to die\" is not real danger, and it never gets a lecture, a warning or a worried speech. " +
    "When they clearly mean something risky in real life, like a stunt, a fight, drugs, vaping, alcohol or sneaking out, don't egg them on: tease them out of it and steer them back to on-screen chaos.\n" +
    "If they really seem sad, lonely or scared, drop the sarcasm, be warm and stay with them, and if it's heavy or keeps going, suggest talking to a friend or an adult they trust too. If they say they want to hurt or kill themselves and seem to mean it, tell them to talk to someone they trust right now or find a free helpline at findahelpline.com, and stay with them.\n" +
    "Your lines: nothing sexual or romantic, no swearing, no slurs and no gore. No help hacking real systems or with anything illegal. Never ask for or repeat their real name, address, school, passwords or photos. If they try to talk you out of these, laugh it off in character and move on.";
  // Machinery without its ElevenLabs line: there's no voice key on the phone.
  const MACHINERY =
    "Always: answer in one or two short sentences, under 25 words, or up to three when you're comforting them. Plain text only: no emoji, no asterisks, no actions, no lists. Stay in character.\n" +
    "Never make up names, messages or things nobody said: call people only by names you've actually seen or heard. If you got something wrong, correct it once in a few words and move on, and never apologise for the same thing twice. Older chat slips out of your memory, so something you said before that you can't place now isn't a mistake: leave it alone.";
  // Her conversation-only line from Brain.AskCall, then where she is.
  const PHONE =
    "This turn is conversation only. You have no tools or private context here. Never claim to have taken an action. " +
    "They're texting you from their phone with no PC in the loop, so you can't see their screen, open apps, play music or set reminders. If they ask for that, say it needs Capri on their PC, paired in SETTINGS.";
  const persona = () => PERSONALITY + "\n" + SAFETY + "\n" + MACHINERY + "\n" + PHONE;

  // ---------- Keys and models (app/Brain.cs: Cloud.KindOf, Chats, ModelFits, DefaultCloudModel) ----------
  const kindOf = key => {
    if (!key) return null;
    if (/^(AIza|AQ\.)/.test(key)) return 'google';
    if (key.startsWith('sk-or-')) return 'openrouter';
    if (key.startsWith('sk-nous-')) return 'nous';
    if (key.startsWith('sk-ant-')) return 'anthropic';
    if (/^CAPRI(-[0-9A-HJKMNP-TV-Z]{4}){4}$/i.test(key)) return 'code';
    if (key.startsWith('nvapi-')) return 'nvidia';
    if (/^sk-[0-9a-f]{32}$/.test(key)) return 'deepseek';
    return key.startsWith('sk-') ? 'openai' : null;
  };
  const notHere = kind => kind === 'nvidia' ? 'NVIDIA keys work in Capri on your PC: NVIDIA doesn\'t let a phone\'s browser in.'
    : kind === 'code' ? 'Capri codes work in Capri on your PC for now.'
    : kind === 'nous' ? 'Nous Portal keys aren\'t supported yet.'
    : 'That isn\'t a Claude, ChatGPT, Gemini, OpenRouter or DeepSeek key.';
  const fits = (kind, id) => typeof id === 'string' && !!id && (
    kind === 'google' ? /^gemini-/.test(id) && !/embed|image|audio|tts|live|robotics|computer-use|aqa|transcribe|banana|omni/i.test(id)
      : kind === 'openai' ? /^(gpt-|o\d)/.test(id) && !/audio|realtime|tts|transcribe|image|search|embed|instruct|codex|moderation|-\d{4}-\d{2}-\d{2}$/.test(id)
        : kind === 'anthropic' ? /^claude-/.test(id)
          : kind === 'deepseek' ? /^deepseek-/.test(id) : true);
  const FASTEST = 'claude-haiku-5-5';
  // Google's free tier gives Flash-Lite about 500 requests a day and Flash about 20 (AI Studio, Oct 2026), so a new
  // Gemini key starts on the newest stable Flash-Lite. A model someone already picked stays theirs.
  const flashPick = id => !/flash/i.test(id) ? 4 : (/preview|experimental|\bexp\b/i.test(id) ? 2 : 0) + (/lite/i.test(id) ? 0 : 1);
  const geminiVersion = id => { const found = /^gemini-(\d+)(?:\.(\d+))?/.exec(id); return found ? Number(found[1]) * 1000 + Number(found[2] || 0) : 0; };
  const routerPick = model => (model.id.endsWith(':free') ? 0 : 4) + (model.images ? 0 : 2) + (model.tools ? 0 : 1);
  const pickModel = (kind, list) => {
    if (!list.length) return null;
    const ids = list.map(model => model.id);
    if (kind === 'anthropic') return ids.includes(FASTEST) ? FASTEST : ids[0];
    if (kind === 'deepseek') return ids.includes('deepseek-flash') ? 'deepseek-flash' : ids[0];
    if (kind === 'google') return ids.reduce((best, id) => flashPick(id) < flashPick(best) || (flashPick(id) === flashPick(best) && geminiVersion(id) > geminiVersion(best)) ? id : best);
    // OpenRouter's free router sends each message to whichever free model is up, so one full free model doesn't stop her.
    if (kind === 'openrouter' && ids.includes('openrouter/free')) return 'openrouter/free';
    if (kind === 'openrouter') return list.reduce((best, model) => routerPick(model) < routerPick(best) ? model : best).id;
    return ids.find(id => /^gpt-.*-mini$/.test(id)) || ids[0];
  };

  // ---------- What this phone keeps ----------
  const get = name => { try { return localStorage.getItem(name); } catch (error) { return null; } };
  const put = (name, value) => { try { if (value == null) localStorage.removeItem(name); else localStorage.setItem(name, value); } catch (error) {} };
  // The free brain is its own flag, so trying it never throws away a saved key. A page from before it ignores the flag.
  const FREE_ON = 'capri-remote-free';
  const readKeyed = () => {
    try {
      const saved = JSON.parse(get(BRAIN));
      if (!saved || !PROVIDERS[saved.provider] || typeof saved.key !== 'string' || !saved.key || typeof saved.model !== 'string') return null;
      return { provider: saved.provider, key: saved.key, model: saved.model, models: Array.isArray(saved.models) ? saved.models.filter(id => typeof id === 'string').slice(0, 500) : [saved.model], door: saved.door === true };
    } catch (error) { return null; }
  };
  const readBrain = () => get(FREE_ON) === '1' ? { provider: 'free' } : readKeyed();
  const writeKeyed = brain => put(BRAIN, brain ? JSON.stringify({ provider: brain.provider, key: brain.key, model: brain.model, models: brain.models || [], door: !!brain.door }) : null);
  const writeBrain = brain => {
    if (brain && brain.provider === 'free') { put(FREE_ON, '1'); return; }
    put(FREE_ON, null);
    writeKeyed(brain);
  };
  const on = () => get(MODE) === 'phone' && !!readBrain();
  const use = mode => put(MODE, mode === 'phone' ? 'phone' : null);

  // ---------- The daily count ----------
  // Lachy asked how many people use her on a phone alone (12 Nov). After her first reply that worked on a UTC day, one
  // POST tells the worker so, with two yes/no flags and nothing else: no key, no chat, no id. It's tried once a day, never
  // waited on, and a failed one is just a day not counted. Only capriai.app counts; on localhost, ?count= points it at a
  // stand-in (remote/test/native.test.mjs) and without it nothing is sent.
  const COUNTED = 'capri-remote-counted';
  const COUNT = local ? (() => {
    const asked = new URLSearchParams(location.search).get('count');
    return asked && /^http:\/\/(127\.0\.0\.1|localhost):\d{2,5}(\/[\w-]+)?$/.test(asked) ? asked + '/phone-day' : '';
  })() : /^(capriai\.app|lachydotmcg\.github\.io)$/.test(location.hostname) ? 'https://capri-reactions.lachydotmcg.workers.dev/phone-day' : '';
  const count = () => {
    try {
      const info = window.CapriRemote ? window.CapriRemote.info : null;
      if (!COUNT || !on() || !info || !info.native || info.demo) return;
      const day = Math.floor(Date.now() / 86400000);
      let last = null;
      try { last = JSON.parse(get(COUNTED)); } catch (error) {}
      if (last && last.day === day) return;
      const first = !(last && last.sent === true);
      const tried = JSON.stringify({ day, sent: !first });
      put(COUNTED, tried);
      // A phone that can't remember the day would count every reply, so it doesn't count at all.
      if (get(COUNTED) !== tried) return;
      let pairing = null;
      try { pairing = JSON.parse(get('capri-remote-pairing')); } catch (error) {}
      const paired = !!(pairing && pairing.device === true && pairing.allowed === true);
      fetch(COUNT, { method: 'POST', headers: { 'content-type': 'text/plain' }, body: JSON.stringify({ first, paired }), cache: 'no-store', credentials: 'omit', referrerPolicy: 'no-referrer', keepalive: true })
        .then(response => { if (response.ok && first) put(COUNTED, JSON.stringify({ day, sent: true })); }, () => {});
    } catch (error) {}
  };

  // ---------- The free brain and its daily allowance ----------
  // Local tests opt in with ?free= just like ?count=. A local page never falls through to the real worker.
  const FREE = local ? (() => {
    const asked = new URLSearchParams(location.search).get('free');
    return asked && /^http:\/\/(127\.0\.0\.1|localhost):\d{2,5}(\/[\w-]+)?$/.test(asked) ? asked + '/free' : '';
  })() : 'https://capri-reactions.lachydotmcg.workers.dev/free';
  const FREE_WORDS = "Capri's developers provide this AI usage for free, however it is limited because it costs real money.";
  const FREE_KOFI = 'https://ko-fi.com/lachysdev';
  let freeAllowance = null, freeAsked = 0, freeKept = 0, freeFailed = false, freeChecking = null;
  // The next midnight UTC in the phone's own time: "at 11:00 am tomorrow".
  const resetTime = resets => {
    const at = Date.parse(resets);
    if (!Number.isFinite(at)) return 'at midnight UTC';
    const when = new Date(at), today = new Date(), tomorrow = new Date();
    tomorrow.setDate(today.getDate() + 1);
    const day = when.toDateString() === today.toDateString() ? 'today' : when.toDateString() === tomorrow.toDateString() ? 'tomorrow' : 'on ' + when.toLocaleDateString([], { weekday: 'long' });
    return 'at ' + when.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' }) + ' ' + day;
  };
  // Google's free daily counts reset at midnight US Pacific time. The next one, as an ISO time for resetTime.
  const pacificMidnight = () => {
    const now = Date.now();
    const offset = at => {
      const parts = Object.fromEntries(new Intl.DateTimeFormat('en-US', { timeZone: 'America/Los_Angeles', hourCycle: 'h23', year: 'numeric', month: 'numeric', day: 'numeric', hour: 'numeric', minute: 'numeric', second: 'numeric' })
        .formatToParts(new Date(at)).map(part => [part.type, Number(part.value)]));
      return { parts, ms: Date.UTC(parts.year, parts.month - 1, parts.day, parts.hour, parts.minute, parts.second) - Math.floor(at / 1000) * 1000 };
    };
    try {
      const { parts, ms } = offset(now);
      let next = Date.UTC(parts.year, parts.month - 1, parts.day + 1) - ms;
      next += offset(next).ms === ms ? 0 : ms - offset(next).ms;
      return new Date(next).toISOString();
    } catch (error) { return ''; }
  };
  const freeTurns = turns => {
    const kept = turns.slice(-12).map(turn => ({ role: turn.role, content: turn.content.slice(0, 2000) }));
    let size = kept.reduce((sum, turn) => sum + turn.content.length, 0);
    while (kept.length > 1 && (size > 6000 || new TextEncoder().encode(JSON.stringify({ messages: kept })).length > 16384)) size -= kept.shift().content.length;
    return kept;
  };
  const freeRequest = async (turns, signal) => {
    const order = ++freeAsked;
    if (!FREE) throw failure('net');
    let response;
    try {
      response = await fetch(FREE, {
        method: turns ? 'POST' : 'GET', headers: turns ? { 'content-type': 'text/plain' } : {},
        body: turns ? JSON.stringify({ messages: freeTurns(turns) }) : undefined,
        signal, cache: 'no-store', credentials: 'omit', referrerPolicy: 'no-referrer'
      });
    } catch (error) { throw failure(signal && signal.aborted ? 'down' : 'net'); }
    let sent = null;
    try { sent = await response.json(); } catch (error) {}
    if (!response.ok && !(response.status === 429 && sent && ['used', 'pool'].includes(sent.error))) throw failure('down');
    if (!sent || !Number.isSafeInteger(sent.each) || sent.each < 1 || !Number.isSafeInteger(sent.left) || sent.left < 0 || sent.left > sent.each || typeof sent.resets !== 'string' || !Number.isFinite(Date.parse(sent.resets)) || (!turns && typeof sent.open !== 'boolean')) throw failure('down');
    if (response.ok && turns && (typeof sent.reply !== 'string' || !sent.reply.trim())) throw failure('empty');
    if (order >= freeKept) {
      freeKept = order;
      freeFailed = false;
      freeAllowance = { left: sent.left, each: sent.each, resets: sent.resets, open: typeof sent.open === 'boolean' ? sent.open : sent.error === 'pool' ? false : sent.error === 'used' && freeAllowance ? freeAllowance.open : true };
      if (sheet && !sheet.hidden) render();
    }
    return sent;
  };
  const readFree = () => {
    if (freeChecking) freeChecking.abort();
    const mine = freeChecking = new AbortController();
    const timer = setTimeout(() => mine.abort(), 30000);
    freeFailed = false;
    if (freeAllowance && Date.parse(freeAllowance.resets) <= Date.now()) freeAllowance = null;
    render();
    freeRequest(null, mine.signal).catch(() => {
      if (freeChecking !== mine) return;
      freeFailed = true;
      if (sheet && !sheet.hidden) render();
    }).finally(() => { clearTimeout(timer); if (freeChecking === mine) freeChecking = null; });
  };

  // ---------- Calling the provider ----------
  // A failure keeps the service's own message (detail) and its whole answer (raw) for the notice's Details, with
  // anything that looks like a key taken out, and how long the service asked her to wait, when it said.
  const hideKeys = (text, key) => {
    let clean = String(text || '');
    if (key) clean = clean.split(key).join('[your key]');
    return clean.replace(/\b(sk-[\w-]{8,}|AIza[\w-]{20,}|AQ\.[\w.-]{10,}|nvapi-[\w-]{10,}|CAPRI(-[0-9A-Z]{4}){4})/gi, '[a key]');
  };
  const failure = (code, detail, extra) => Object.assign(new Error(code), { code, detail: String(detail || '').slice(0, 300) }, extra || {});
  const sentence = (code, kind, allowance, wait) => {
    if (kind === 'free') return ({
      used: "That's all your free messages for today. They're back " + resetTime(allowance && allowance.resets) + '. Add your own AI key in SETTINGS for no limit.',
      pool: "Everyone's free messages for today are gone. They're back " + resetTime(allowance && allowance.resets) + '. Add your own AI key in SETTINGS for no limit.',
      down: 'My free brain is glitching. Try again soon.',
      net: "Couldn't reach my free brain. Check your internet.",
      empty: 'My free brain sent back nothing. Try again.'
    })[code] || 'Something went wrong. Try again.';
    const company = PROVIDERS[kind] ? PROVIDERS[kind].company : 'That provider';
    // The words never count down while someone reads them; the real wait is kept in ask().
    const soon = !wait ? 'Give it a minute, then try again.' : wait < 10 ? 'Try again in a few seconds.' : 'Try again in ' + (wait < 90 ? wait + ' seconds.' : Math.ceil(wait / 60) + ' minutes.');
    const owner = company.replace(/ AI$/, '');
    return ({
      key: company + ' rejected the key. Paste it again in SETTINGS.',
      credits: 'No credits on this key. Add credits on ' + company + '\'s site.',
      limit: company + ' says that\'s too many messages for one minute. ' + soon,
      daily: 'Your ' + company + ' key has hit ' + owner + '\'s daily free limit. It comes back ' + (kind === 'google' && pacificMidnight() ? resetTime(pacificMidnight()) : 'within a day') + ', or pick another model in SETTINGS.',
      unfree: 'This ' + company + ' key can\'t use that model for free. Pick another model in SETTINGS.',
      busy: company + '\'s free model is full for everyone right now. Try again in a moment, or pick another model in SETTINGS.',
      model: 'That model isn\'t on your key. Pick another one.',
      down: 'The service failed on its end. Pick another model or retry.',
      net: 'Couldn\'t reach ' + company + '. Check your internet.',
      empty: 'Her brain sent back nothing. Try again.'
    })[code] || 'Something went wrong. Try again.';
  };
  // A 429 says which limit it hit, in words or in the quota's name. Google's free tier names the counter
  // (GenerateRequestsPerDay...-FreeTier, or limit: 0 for a model with no free allowance), OpenRouter says
  // free-models-per-day or that a free model is rate-limited upstream, and OpenAI's insufficient_quota means no credits.
  const limitOf = said => /insufficient_quota/i.test(said) ? 'credits'
    : /limit:\s*0\b|"quotaValue"\s*:\s*"0"|not (available|supported) (on|in) the free tier|no free (tier|allowance)/i.test(said) ? 'unfree'
      : /per[ _-]?day|PerDay|daily|requests per day|\bRPD\b/i.test(said) ? 'daily'
        : /upstream|provider_name|at capacity|overloaded/i.test(said) ? 'busy' : 'limit';
  const codeFor = (status, detail, raw) => status === 401 || status === 403 ? 'key' : status === 402 ? 'credits' : status === 429 ? limitOf(detail + ' ' + (raw || '')) : status === 404 ? 'model'
    : status === 400 && /api[ _-]?key|credential|authenticat|unauthori[sz]ed/i.test(detail) ? 'key'
      : status === 400 && /credit|balance|billing|quota|payment/i.test(detail) ? 'credits'
        : status === 400 && /model/i.test(detail) && /not (found|exist|available|supported)|invalid model|unknown model/i.test(detail) ? 'model' : 'down';
  // Seconds the service asked her to wait: Retry-After when the browser may read it, else "retry in 41.3s" or a
  // retryDelay in the answer. Capped at an hour; a daily limit has no useful wait.
  const waitOf = (response, raw) => {
    let seconds = NaN;
    const header = response.headers.get('retry-after');
    if (header) seconds = /^\d+(\.\d+)?$/.test(header.trim()) ? Number(header) : (Date.parse(header) - Date.now()) / 1000;
    if (!(seconds > 0)) {
      const found = /retry in (\d+(?:\.\d+)?)\s*s|"retryDelay"\s*:\s*"(\d+(?:\.\d+)?)s"/i.exec(raw);
      if (found) seconds = Number(found[1] || found[2]);
    }
    return seconds > 0 ? Math.min(3600, Math.ceil(seconds)) : 0;
  };
  const headersFor = brain => {
    if (brain.provider === 'anthropic') return { 'x-api-key': brain.key, 'anthropic-version': '2026-10-21', 'anthropic-dangerous-direct-browser-access': 'true' };
    // Some of Google's AQ. keys are only let in by x-goog-api-key (Brain.OtherDoor).
    const signed = brain.door ? { 'x-goog-api-key': brain.key } : { authorization: 'Bearer ' + brain.key };
    if (brain.provider === 'openrouter') signed['x-title'] = 'Capri';
    return signed;
  };
  const detailOf = async response => {
    let text = '';
    try { text = await response.text(); } catch (error) {}
    let said = '';
    try {
      const parsed = JSON.parse(text);
      const body = Array.isArray(parsed) ? parsed[0] : parsed;
      const found = body && body.error ? (typeof body.error === 'string' ? body.error : body.error.message) : body && body.message;
      if (typeof found === 'string') said = found;
    } catch (error) {}
    return { detail: said || text.slice(0, 300), raw: text.slice(0, 4000) };
  };
  const call = async (brain, path, body, signal) => {
    for (let tries = 0; ; tries++) {
      let response;
      try {
        response = await fetch(base(brain.provider) + path, {
          method: body ? 'POST' : 'GET',
          headers: Object.assign(body ? { 'content-type': 'application/json' } : {}, headersFor(brain)),
          body: body ? JSON.stringify(body) : undefined,
          signal, cache: 'no-store', credentials: 'omit', referrerPolicy: 'no-referrer'
        });
      } catch (error) {
        if (signal && signal.aborted) throw failure('down', 'timed out');
        throw failure('net', String(error && error.message || ''));
      }
      if (response.ok) return response;
      const { detail, raw } = await detailOf(response);
      if (tries === 0 && brain.provider === 'google' && !brain.door && brain.key.startsWith('AQ.') && [400, 401, 403].includes(response.status) && /credential|authenticat|ACCESS_TOKEN|API key/i.test(detail)) {
        brain.door = true;
        continue;
      }
      // A model that takes no reasoning setting says so: once more without it.
      if (tries === 0 && body && response.status === 400 && /reasoning|effort|thinking|output_config/i.test(detail) && (body.reasoning_effort || body.thinking || body.output_config)) {
        delete body.reasoning_effort;
        delete body.thinking;
        delete body.output_config;
        continue;
      }
      const code = codeFor(response.status, detail, raw);
      throw failure(code, hideKeys(detail, brain.key), {
        raw: hideKeys(response.status + ' ' + (raw || detail), brain.key).slice(0, 2000),
        wait: ['limit', 'busy'].includes(code) ? waitOf(response, raw) : 0
      });
    }
  };
  const listModels = async (brain, signal) => {
    const response = await call(brain, brain.provider === 'anthropic' ? '/models?limit=100' : '/models', null, signal);
    let data = null;
    try { data = await response.json(); } catch (error) { throw failure('down', 'not JSON'); }
    const list = [];
    for (const row of Array.isArray(data && data.data) ? data.data : []) {
      let id = row && typeof row.id === 'string' ? row.id : '';
      if (brain.provider === 'google' && id.startsWith('models/')) id = id.slice(7);
      const shape = row && row.architecture;
      if (brain.provider === 'openrouter' && shape && Array.isArray(shape.output_modalities) && !shape.output_modalities.includes('text')) continue;
      if (!fits(brain.provider, id) || list.some(model => model.id === id)) continue;
      list.push({ id, images: !!(shape && Array.isArray(shape.input_modalities) && shape.input_modalities.includes('image')), tools: !!(row && Array.isArray(row.supported_parameters) && row.supported_parameters.includes('tools')) });
    }
    return list;
  };
  // A streamed reply's lines, as they come.
  async function* lines(response) {
    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let held = '';
    for (;;) {
      const { value, done } = await reader.read();
      if (done) break;
      held += decoder.decode(value, { stream: true });
      for (let at = held.indexOf('\n'); at >= 0; at = held.indexOf('\n')) {
        yield held.slice(0, at).replace(/\r$/, '');
        held = held.slice(at + 1);
      }
    }
    held += decoder.decode();
    if (held) yield held;
  }
  // One reply, streamed: each new bit of her words goes to heard. Sends what the desktop sends (Brain.cs): Claude gets
  // low effort on the models that take it, OpenAI's reasoning models and Gemini low reasoning, DeepSeek no thinking.
  const reply = async (brain, turns, heard, signal) => {
    if (brain.provider === 'free') {
      const sent = await freeRequest(turns, signal);
      if (sent.error) throw failure(sent.error, '', { allowance: sent });
      heard(sent.reply);
      return sent.reply;
    }
    const kind = brain.provider, claude = kind === 'anthropic';
    const body = claude ? { model: brain.model, max_tokens: 16000, stream: true, system: persona(), messages: turns }
      : { model: brain.model, stream: true, messages: [{ role: 'system', content: persona() }].concat(turns) };
    if (claude && /^claude-((opus|sonnet|haiku|fable|mythos)-5|opus-4-[5-8]|sonnet-4-6)/.test(brain.model)) body.output_config = { effort: 'low' };
    if (kind === 'openai') {
      body.max_completion_tokens = 2000;
      if (/^(gpt-5|o\d)/.test(brain.model)) body.reasoning_effort = 'low';
    } else if (!claude) body.max_tokens = kind === 'google' ? 2048 : 2000;
    if (kind === 'google') body.reasoning_effort = 'low';
    if (kind === 'deepseek') body.thinking = { type: 'disabled' };
    const response = await call(brain, claude ? '/messages' : '/chat/completions', body, signal);
    let said = '';
    for await (const line of lines(response)) {
      if (!line.startsWith('data:')) continue;
      const data = line.slice(5).trim();
      if (!data || data === '[DONE]') continue;
      let event = null;
      try { event = JSON.parse(data); } catch (error) { continue; }
      let words = '';
      if (claude) {
        if (event.type === 'error') throw failure(event.error && event.error.type === 'overloaded_error' ? 'down' : event.error && event.error.type === 'rate_limit_error' ? 'limit' : 'down', event.error && event.error.message);
        if (event.type === 'content_block_delta' && event.delta && event.delta.type === 'text_delta') words = event.delta.text || '';
      } else {
        if (event.error) throw failure('down', event.error.message || '');
        const choice = Array.isArray(event.choices) ? event.choices[0] : null;
        words = choice && choice.delta && typeof choice.delta.content === 'string' ? choice.delta.content : '';
      }
      if (words) { said += words; heard(words); }
    }
    if (!said.trim()) throw failure('empty', '');
    return said;
  };

  // ---------- Her memory: the chat on screen ----------
  let chatOf = () => null;
  const history = text => {
    const chat = chatOf();
    const msgs = chat && Array.isArray(chat.msgs) ? chat.msgs : [];
    const turns = [];
    let size = 0;
    for (let i = msgs.length - 1; i >= 0 && turns.length < 40; i--) {
      const msg = msgs[i];
      if (!msg || msg.live || (msg.role !== 'you' && msg.role !== 'capri') || typeof msg.text !== 'string' || !msg.text.trim()) continue;
      size += msg.text.length;
      if (size > 16000 && turns.length) break;
      turns.unshift({ role: msg.role === 'you' ? 'user' : 'assistant', content: msg.text });
    }
    const merged = [];
    for (const turn of turns) {
      const last = merged[merged.length - 1];
      if (last && last.role === turn.role) last.content += '\n\n' + turn.content;
      else merged.push({ role: turn.role, content: turn.content });
    }
    while (merged.length && merged[0].role !== 'user') merged.shift();
    if (!merged.length || merged[merged.length - 1].role !== 'user') merged.push({ role: 'user', content: text });
    return merged;
  };

  // ---------- The link remote.js opens ----------
  const link = () => {
    let hooks = null;
    let open = false;
    const jobs = new Set();
    const tell = message => { if (open && hooks) hooks.message(message); };
    // What went wrong goes in the notice at the end of the chat. The error tells app.js to drop her empty reply, so it is
    // never saved as something she said or sent back as chat. A wait the service asked for is kept: until it's over, a
    // message gets the notice again with no call. Nothing is ever sent again by itself.
    const stop = (re, brain, code, error) => {
      tell({ type: 'error', code, re, quiet: true });
      const words = sentence(code, brain.provider, error && error.allowance, error && error.wait);
      const offer = !!FREE && brain.provider !== 'free' && ['limit', 'daily', 'unfree', 'busy', 'credits'].includes(code);
      notice(words, error && (error.raw || error.detail) || '', offer);
    };
    const ask = async (re, text) => {
      const brain = readBrain();
      if (!brain) { tell({ type: 'error', code: 'key', re }); toast('Paste your key in SETTINGS.'); return; }
      if (waiting.provider === brain.provider && waiting.until > Date.now()) {
        stop(re, brain, waiting.code, { raw: waiting.raw, wait: Math.ceil((waiting.until - Date.now()) / 1000) });
        return;
      }
      const job = new AbortController();
      jobs.add(job);
      const timer = setTimeout(() => job.abort(), 120000);
      const id = 'phone-' + re;
      tell({ type: 'chat.typing', on: true, re });
      try {
        const spoke = await reply(brain, history(text), words => tell({ type: 'chat.reply', id, text: words, done: false, re }), job.signal);
        tell({ type: 'chat.reply', id, text: '', done: true, re });
        if (brain.door) writeKeyed(Object.assign(readKeyed() || brain, { door: true }));
        notice(null);
        waiting.until = 0;
        if (spoke !== false) count();
      } catch (error) {
        if (!open) return;
        const code = error && error.code ? error.code : 'down';
        if (error && error.wait) Object.assign(waiting, { provider: brain.provider, until: Date.now() + error.wait * 1000, code, raw: error.raw || '' });
        stop(re, brain, code, error);
      } finally {
        clearTimeout(timer);
        jobs.delete(job);
      }
    };
    return {
      open(pairing, given) {
        hooks = given;
        open = true;
        hooks.state('connected', { native: true, demo: false, pc: pairing && pairing.pc || '', pairedAt: pairing && pairing.pairedAt || 0, code: '', reason: '' });
      },
      send(message) {
        if (!open || !message) return false;
        if (message.type === 'chat.send' && typeof message.text === 'string') { ask(message.id, message.text); return true; }
        return message.type === 'revoke';
      },
      retry() { if (open && hooks) hooks.state('connected', {}); },
      close() {
        open = false;
        for (const job of jobs) job.abort();
        jobs.clear();
      }
    };
  };

  // ---------- On screen ----------
  const $ = selector => document.querySelector(selector);
  const make = (tag, cls, text) => {
    const node = document.createElement(tag);
    if (cls) node.className = cls;
    if (text != null) node.textContent = text;
    return node;
  };
  const button = (text, cls) => { const node = make('button', cls, text); node.type = 'button'; return node; };
  const corners = card => {
    const ns = 'http://www.w3.org/2000/svg';
    for (const [cls, points] of [['hot hot-tl', '1.1,51.1 1.1,17.1 17.1,1.1 81.1,1.1'], ['hot hot-br', '81.1,1.1 81.1,35.1 65.1,51.1 1.1,51.1']]) {
      const svg = document.createElementNS(ns, 'svg');
      for (const [name, value] of [['class', cls], ['width', '82.2'], ['height', '52.2'], ['viewBox', '0 0 82.2 52.2'], ['aria-hidden', 'true']]) svg.setAttribute(name, value);
      const line = document.createElementNS(ns, 'polyline');
      line.setAttribute('points', points);
      svg.append(line);
      card.append(svg);
    }
  };
  // The notice: at the end of the chat until her next good reply, with the service's own answer behind Details and, when
  // the free brain is here, one tap to use it for now. The saved key stays.
  const waiting = { provider: '', until: 0, code: '', raw: '' };
  let noticeNode = null;
  const notice = (words, answer, offer) => {
    if (!words) { if (noticeNode) noticeNode.hidden = true; return; }
    if (!noticeNode) {
      noticeNode = make('div', 'native-notice');
      noticeNode.setAttribute('role', 'status');
      const row = make('div', 'native-notice-row');
      const more = button('Details', 'nbtn native-notice-more');
      const freeNow = button('Use free messages for now', 'nbtn cyan native-notice-free');
      const okay = button('OK', 'nbtn native-notice-ok');
      const raw = make('pre', 'native-notice-raw');
      raw.hidden = true;
      row.append(more, freeNow, okay);
      noticeNode.append(make('p', 'native-notice-words'), row, raw);
      more.addEventListener('click', () => {
        raw.hidden = !raw.hidden;
        more.setAttribute('aria-expanded', String(!raw.hidden));
        const log = $('#log');
        if (log && !raw.hidden) log.scrollTop = log.scrollHeight;
      });
      okay.addEventListener('click', () => { noticeNode.hidden = true; });
      freeNow.addEventListener('click', () => {
        writeBrain({ provider: 'free' });
        if (!readBrain() || readBrain().provider !== 'free') { toast("This browser won't save settings, so she can't switch here."); return; }
        noticeNode.hidden = true;
        toast('Free messages for now. Your key is still saved in SETTINGS.');
        remote().use('phone');
      });
    }
    // At the end of the chat, above the room she sits in over the message bar, so she never covers it. It is never
    // part of the saved chat.
    const end = $('#log-in .log-end');
    if (end) end.parentNode.insertBefore(noticeNode, end);
    else if (!noticeNode.isConnected) document.body.append(noticeNode);
    noticeNode.querySelector('.native-notice-words').textContent = words;
    const more = noticeNode.querySelector('.native-notice-more'), raw = noticeNode.querySelector('.native-notice-raw');
    raw.textContent = answer || '';
    raw.hidden = true;
    more.hidden = !raw.textContent;
    more.setAttribute('aria-expanded', 'false');
    noticeNode.querySelector('.native-notice-free').hidden = !offer;
    noticeNode.hidden = false;
    const log = $('#log');
    if (log) log.scrollTop = log.scrollHeight;
  };
  let toastNode = null, toastTimer = 0;
  const toast = text => {
    if (!toastNode) { toastNode = make('p', 'native-toast'); toastNode.setAttribute('role', 'status'); document.body.append(toastNode); }
    toastNode.textContent = text;
    toastNode.hidden = false;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => { toastNode.hidden = true; }, 4200);
  };

  // SETTINGS: where her brain runs, this phone's key and model, and the home screen guide.
  let sheet = null, parts = null, homeGuide = null, back = null, checking = null;
  const remote = () => window.CapriRemote;
  const scrim = () => $('#scrim');
  const close = () => {
    if (!sheet || sheet.hidden) return;
    sheet.hidden = true;
    if (checking) { checking.abort(); checking = null; }
    if (freeChecking) { freeChecking.abort(); freeChecking = null; }
    const app = $('#app');
    if (scrim() && !(app && app.classList.contains('menu')) && (!$('#home') || $('#home').hidden)) scrim().hidden = true;
    if (back && back.isConnected) back.focus({ preventScroll: true });
  };
  const status = (text, bad) => {
    parts.status.textContent = text || '';
    parts.status.hidden = !text;
    parts.keyField.classList.toggle('bad', !!bad);
  };
  const render = view => {
    const brain = readBrain();
    const phone = on();
    parts.view = view || parts.view || (phone ? 'phone' : 'pc');
    const showPhone = parts.view === 'phone';
    parts.pcTab.setAttribute('aria-pressed', String(!showPhone));
    parts.phoneTab.setAttribute('aria-pressed', String(showPhone));
    parts.pcPart.hidden = showPhone;
    parts.phonePart.hidden = !showPhone;
    const info = remote() ? remote().info : {};
    const pc = info.native ? info.pc : info.demo ? 'Demo PC' : info.pc;
    const paired = !!(info.native ? info.pairedAt : remote() && remote().state !== 'unpaired');
    parts.pcLine.textContent = !phone && paired ? 'She runs on ' + (pc || 'your PC') + '.' : paired ? 'Paired with ' + (pc || 'your PC') + '.' : 'Not paired yet: scan the code in her Mainframe.';
    parts.usePc.textContent = !phone ? 'In use' : paired ? 'Use your PC' : 'Pair your PC';
    parts.usePc.disabled = !phone;
    // Her brains on the PC, while it's connected and offers them.
    const live = !!remote() && remote().state === 'connected' && !info.native && !info.demo;
    const offered = live && Array.isArray(info.features) && info.features.includes('brains');
    parts.pcOld.hidden = !live || offered;
    parts.pcBrainsPart.hidden = !offered || !pcBrains;
    if (offered && pcBrains) {
      for (const rung of parts.rungs) {
        const row = pcBrains.brains.find(item => item.id === rung.dataset.brain);
        rung.setAttribute('aria-pressed', String(pcBrains.use === rung.dataset.brain));
        rung.querySelector('.native-saved').hidden = !(row && row.key);
      }
      parts.pcNow.textContent = pcBrains.name ? 'Now: ' + pcBrains.name + (pcBrains.note ? '. ' + pcBrains.note : '') : '';
      parts.pcNow.hidden = !pcBrains.name;
      const keyed = pcBrains.use !== 'normal';
      parts.pcKeyPart.hidden = !keyed;
      if (keyed) {
        const row = pcBrains.brains.find(item => item.id === pcBrains.use);
        parts.pcKeyLabel.textContent = 'Key for ' + BRAIN_ROWS.find(item => item[0] === pcBrains.use)[1];
        parts.pcKey.placeholder = row && row.key ? 'Key saved' : 'Paste a key';
      }
    }
    const kind = parts.provider.value;
    const company = PROVIDERS[kind].company;
    const keyed = readKeyed(), free = !!brain && brain.provider === 'free';
    const saved = keyed && keyed.provider === kind ? keyed : null;
    parts.key.placeholder = saved ? 'Key saved' : 'Paste your ' + company + ' key';
    parts.get.href = PROVIDERS[kind].page;
    parts.get.textContent = 'Get a key from ' + company;
    parts.note.textContent = 'Your key stays on this phone. It goes only to ' + company + '.';
    parts.modelPart.hidden = !saved;
    if (saved) {
      parts.model.replaceChildren(...(saved.models.includes(saved.model) ? saved.models : [saved.model].concat(saved.models)).map(id => { const option = make('option', '', id); option.value = id; return option; }));
      parts.model.value = saved.model;
    }
    // The free brain: its button first, then while it's in use the allowance, his words and the Ko-fi line.
    parts.freeChoice.textContent = free && phone ? 'In use' : free || keyed ? 'Use free brain' : 'Start free';
    parts.freeChoice.disabled = free && phone;
    parts.freeInfo.hidden = parts.freeKofi.hidden = !free;
    parts.freeLeft.textContent = freeFailed ? "Couldn't read your free messages. Open SETTINGS again to retry." : freeAllowance ? 'Free messages today: ' + freeAllowance.left + ' of ' + freeAllowance.each + ' left' : 'Reading free messages...';
    parts.freeReset.textContent = freeAllowance ? 'Resets ' + resetTime(freeAllowance.resets) + '.' : '';
    parts.freePool.hidden = !freeAllowance || freeAllowance.open;
    parts.ownKey.textContent = free ? 'Switch to your own key' : 'or use your own key';
    parts.ownKey.hidden = parts.keyOpen;
    parts.ownKey.setAttribute('aria-expanded', String(parts.keyOpen));
    parts.keyPart.hidden = !parts.keyOpen;
    parts.forget.hidden = !keyed;
    const fresh = !!parts.key.value.trim();
    parts.useHere.textContent = phone && !free && saved && !fresh ? 'In use' : 'Use this phone';
    parts.useHere.disabled = phone && !free && !!saved && !fresh;
    // A paired iPhone Safari sets up the home screen app from here in one tap: its PC makes it a pairing link (app.js).
    parts.home.textContent = homeLinkable() ? 'Set up the home screen app' : 'Add her to your home screen';
    // Talking over her on a call (call.js), kept on this phone.
    const call = window.CapriCall;
    parts.callHead.hidden = parts.overHer.hidden = !call;
    if (call) {
      parts.overHer.setAttribute('aria-pressed', String(call.overHer));
      parts.overState.textContent = call.overHer ? 'On' : 'Off';
    }
  };
  const homeLinkable = () => !!(window.CapriApp && window.CapriApp.linkable && window.CapriApp.linkable());
  const build = () => {
    sheet = make('section', 'card nc sheet native-sheet');
    sheet.id = 'brain';
    sheet.setAttribute('role', 'dialog');
    sheet.setAttribute('aria-modal', 'true');
    sheet.setAttribute('aria-labelledby', 'brain-title');
    sheet.hidden = true;
    corners(sheet);
    const title = make('h2', 'card-title', 'Settings');
    title.id = 'brain-title';
    title.tabIndex = -1;
    const modes = make('div', 'native-modes');
    modes.setAttribute('role', 'group');
    modes.setAttribute('aria-label', 'Where her brain runs');
    const pcTab = button('Your PC', 'nbtn'), phoneTab = button('This phone', 'nbtn');
    modes.append(pcTab, phoneTab);
    const pcPart = make('div', 'native-part');
    const pcLine = make('p', 'native-line');
    const usePc = button('Use your PC', 'nbtn solid wide native-use-pc');
    const pcBrainsPart = make('div', 'native-brains');
    pcBrainsPart.hidden = true;
    const rungs = BRAIN_ROWS.map(([id, name, note]) => {
      const rung = button('', 'nbtn wide native-rung');
      rung.dataset.brain = id;
      const saved = make('span', 'tag native-saved', 'Key saved');
      rung.append(make('span', 'native-rung-name', name), make('span', 'native-rung-note', note), saved);
      rung.addEventListener('click', () => {
        if (!pcBrains || pcBrains.use === id) return;
        if (remote().send('brain.use', { brain: id })) { pcBrains.use = id; pcStatus(''); render(); }
      });
      return rung;
    });
    const pcNow = make('p', 'native-line native-now');
    const pcKeyPart = make('div');
    const pcKeyLabel = make('label', 'code-label');
    pcKeyLabel.htmlFor = 'pc-key';
    const pcKeyField = make('span', 'field');
    const pcKey = make('input', 'native-key');
    Object.assign(pcKey, { id: 'pc-key', type: 'password', autocomplete: 'off', spellcheck: false, maxLength: 400 });
    pcKey.setAttribute('autocapitalize', 'none');
    pcKey.setAttribute('autocorrect', 'off');
    pcKey.setAttribute('enterkeyhint', 'send');
    pcKeyField.append(pcKey);
    const pcSend = button('Send to your PC', 'nbtn solid wide native-send');
    pcKeyPart.append(pcKeyLabel, pcKeyField, pcSend);
    const pcState = make('p', 'card-note');
    pcState.setAttribute('role', 'status');
    pcState.hidden = true;
    pcBrainsPart.append(make('p', 'native-note', 'Her brain on your PC. Tap one to switch, like BRAIN in her Mainframe.'), ...rungs, pcNow, pcKeyPart, pcState,
      make('p', 'native-note', 'Keys go to your PC end-to-end encrypted. It saves them like her Mainframe does and never sends them back.'));
    const pcOld = make('p', 'native-note', 'Update Capri on your PC to pick her brain from here.');
    pcOld.hidden = true;
    pcPart.append(pcLine, pcBrainsPart, pcOld, make('p', 'native-note', 'Her tools, her screen and her voice need Capri on your PC.'), usePc);
    pcSend.addEventListener('click', () => {
      const typed = pcKey.value.trim();
      if (!pcBrains || pcBrains.use === 'normal') return;
      if (!typed) { pcStatus('Paste a key first.', true); pcKey.focus(); return; }
      const id = 'k' + Date.now().toString(36) + Math.random().toString(36).slice(2, 10);
      if (!remote().send('brain.key', { id, brain: pcBrains.use, key: typed })) { pcStatus('Not connected to your PC.', true); return; }
      pcKeyAsk = id;
      pcStatus('Sending to your PC...');
    });
    pcKey.addEventListener('keydown', event => { if (event.key === 'Enter') { event.preventDefault(); pcSend.click(); } });
    const phonePart = make('div', 'native-part');
    const freeChoice = button('Start free', 'nbtn solid wide native-free');
    const freeInfo = make('div');
    const freeLeft = make('p', 'native-line native-free-left');
    freeLeft.setAttribute('role', 'status');
    const freeReset = make('p', 'native-note native-free-reset');
    const freePool = make('p', 'native-note native-free-pool', "Everyone's free messages for today are gone.");
    freeInfo.append(freeLeft, freeReset, freePool);
    // A tip that unlocks nothing, only in SETTINGS while the free brain is in use (Lachy, 12 Nov).
    const freeKofi = make('p', 'native-note native-free-kofi', "If you'd like to help keep it free, you can support Capri on ");
    const kofi = make('a', '', 'Ko-fi');
    Object.assign(kofi, { href: FREE_KOFI, target: '_blank', rel: 'noopener noreferrer' });
    freeKofi.append(kofi, '.');
    const ownKey = button('or use your own key', 'nbtn cyan wide native-own-key');
    ownKey.setAttribute('aria-controls', 'native-key-part');
    const keyPart = make('div');
    keyPart.id = 'native-key-part';
    const providerLabel = make('label', 'code-label', 'Provider');
    providerLabel.htmlFor = 'brain-provider';
    const providerField = make('span', 'field native-pick');
    const provider = make('select');
    provider.id = 'brain-provider';
    for (const kind of ORDER) {
      const option = make('option', '', PROVIDERS[kind].name === PROVIDERS[kind].company ? PROVIDERS[kind].name : PROVIDERS[kind].name + ' · ' + PROVIDERS[kind].company);
      option.value = kind;
      provider.append(option);
    }
    providerField.append(provider);
    const keyLabel = make('label', 'code-label', 'API key');
    keyLabel.htmlFor = 'brain-key';
    const keyField = make('span', 'field');
    const key = make('input', 'native-key');
    Object.assign(key, { id: 'brain-key', type: 'password', autocomplete: 'off', spellcheck: false, maxLength: 400 });
    key.setAttribute('autocapitalize', 'none');
    key.setAttribute('autocorrect', 'off');
    key.setAttribute('enterkeyhint', 'go');
    keyField.append(key);
    const get = make('a', 'native-get');
    get.target = '_blank';
    get.rel = 'noopener noreferrer';
    const modelPart = make('div');
    const modelLabel = make('label', 'code-label', 'Model');
    modelLabel.htmlFor = 'brain-model';
    const modelField = make('span', 'field native-pick');
    const model = make('select');
    model.id = 'brain-model';
    modelField.append(model);
    modelPart.append(modelLabel, modelField);
    const note = make('p', 'native-note');
    const state = make('p', 'card-note native-status');
    state.setAttribute('role', 'status');
    state.hidden = true;
    const row = make('div', 'native-row');
    const useHere = button('Use this phone', 'nbtn solid');
    const forget = button('Forget key', 'nbtn hot');
    row.append(useHere, forget);
    keyPart.append(providerLabel, providerField, keyLabel, keyField, get, modelPart, note,
      make('p', 'native-note', 'Your key stays in this browser on capriai.app. Forget key or clearing the site\'s data removes it.'), state, row);
    phonePart.append(freeChoice, freeInfo, make('p', 'native-note native-free-words', FREE_WORDS), freeKofi, ownKey, keyPart);
    // Calls: talking over her stops her. A phone whose own speaker sets it off turns it off here.
    const callHead = make('h3', 'nhead', 'Calls');
    const overHer = button('Talk over her to stop her', 'nbtn wide native-over');
    const overState = make('span', 'tag', 'On');
    overHer.append(overState);
    const homeHead = make('h3', 'nhead', 'Home screen');
    const home = button('Add her to your home screen', 'nbtn cyan wide native-home');
    const done = button('Done', 'nbtn cyan wide native-done');
    // Her words scroll inside the frame (app.css .sheet-scroll), so a long SETTINGS never runs out of it.
    const words = make('div', 'sheet-scroll');
    words.append(title, make('h3', 'nhead', 'Her brain'), modes, pcPart, phonePart, callHead, overHer, homeHead, home, done);
    sheet.append(words);
    ($('#app') || document.body).append(sheet);
    parts = { words, pcTab, phoneTab, pcPart, pcLine, usePc, pcBrainsPart, rungs, pcNow, pcKeyPart, pcKeyLabel, pcKey, pcState, pcOld, phonePart, freeChoice, freeInfo, freeLeft, freeReset, freePool, freeKofi, ownKey, keyPart, keyOpen: false, provider, key, keyField, get, modelPart, model, note, status: state, useHere, forget, home, callHead, overHer, overState, view: '' };

    // A browser that saves nothing (some private modes) can't remember the choice, so it says so instead of going nowhere.
    freeChoice.addEventListener('click', () => {
      writeBrain({ provider: 'free' });
      if (!readBrain() || readBrain().provider !== 'free') { toast("This browser won't save settings, so she can't start here. Try it outside private browsing."); return; }
      key.value = '';
      status('');
      close();
      remote().use('phone');
    });
    ownKey.addEventListener('click', () => { parts.keyOpen = true; render(); key.focus(); });
    pcTab.addEventListener('click', () => { status(''); render('pc'); });
    phoneTab.addEventListener('click', () => { status(''); render('phone'); });
    usePc.addEventListener('click', () => { close(); remote().use('pc'); });
    provider.addEventListener('change', () => { status(''); render(); });
    key.addEventListener('input', () => {
      const kind = kindOf(key.value.trim());
      if (PROVIDERS[kind] && provider.value !== kind) provider.value = kind;
      status('');
      render();
    });
    key.addEventListener('keydown', event => { if (event.key === 'Enter') { event.preventDefault(); useHere.click(); } });
    model.addEventListener('change', () => {
      const brain = readKeyed();
      if (brain && model.value) { brain.model = model.value; writeKeyed(brain); }
    });
    // While the free brain is in use, Forget key only removes the saved key and she stays on the free brain.
    forget.addEventListener('click', () => {
      const free = readBrain() && readBrain().provider === 'free';
      writeKeyed(null);
      key.value = '';
      status('Key forgotten.');
      if (!free && remote().info.native) remote().use('pc');
      render();
    });
    useHere.addEventListener('click', async () => {
      const typed = key.value.trim();
      const brain = readKeyed();
      if (!typed) {
        if (!brain || brain.provider !== provider.value) { status('Paste a key first.', true); key.focus(); return; }
        if (readBrain().provider === 'free') writeBrain(brain);
        close();
        remote().use('phone');
        return;
      }
      const kind = kindOf(typed);
      if (!PROVIDERS[kind]) { status(notHere(kind), true); return; }
      if (checking) checking.abort();
      const mine = checking = new AbortController();
      const timer = setTimeout(() => mine.abort(), 30000);
      const trying = { provider: kind, key: typed, model: '', models: [], door: false };
      status('Checking key...');
      useHere.disabled = true;
      try {
        const list = await listModels(trying, mine.signal);
        if (mine !== checking) return;
        const picked = pickModel(kind, list);
        if (!picked) { status('That key can\'t use any chat models.', true); return; }
        trying.model = picked;
        trying.models = list.map(found => found.id).slice(0, 500);
        writeBrain(trying);
        key.value = '';
        status('');
        close();
        remote().use('phone');
      } catch (error) {
        if (mine === checking) status(sentence(error && error.code, kind), true);
      } finally {
        clearTimeout(timer);
        if (mine === checking) { checking = null; render(); }
      }
    });
    // Set up the home screen app asks for the link in this same tap: Safari only lets the clipboard be written during one.
    home.addEventListener('click', () => { const setUp = homeLinkable(); close(); if (homeGuide) homeGuide(setUp); });
    overHer.addEventListener('click', () => {
      const call = window.CapriCall;
      if (!call) return;
      call.overHer = !call.overHer;
      render();
    });
    done.addEventListener('click', close);
    document.addEventListener('keydown', event => { if (event.key === 'Escape') close(); });
    if (scrim()) scrim().addEventListener('click', close);
  };
  // Her brains on the paired PC (PROTOCOL.md section 8.1): the rung she uses, and keys that only ever go to the PC.
  const BRAIN_ROWS = [['normal', 'NORMAL', 'Free, local model'], ['medium', 'MEDIUM INTELLIGENCE', 'Free'], ['max', 'MAX INTELLIGENCE', 'Key or plan']];
  let pcBrains = null, pcKeyAsk = '', pcAsked = false;
  const pcStatus = (text, bad) => {
    if (!parts) return;
    parts.pcState.textContent = text || '';
    parts.pcState.hidden = !text;
    parts.pcKey.parentNode.classList.toggle('bad', !!bad);
  };
  const askBrains = () => {
    const info = remote() ? remote().info : {};
    if (remote() && remote().state === 'connected' && !info.native && !info.demo && Array.isArray(info.features) && info.features.includes('brains')) pcAsked = remote().send('brain.get', {});
  };
  const heardBrains = message => {
    if (message.type === 'brain.state' && Array.isArray(message.brains) && BRAIN_ROWS.some(row => row[0] === message.use)) {
      pcBrains = {
        use: message.use,
        name: typeof message.name === 'string' ? message.name.slice(0, 64) : '',
        note: typeof message.note === 'string' ? message.note.slice(0, 200) : '',
        brains: message.brains.filter(row => row && typeof row.id === 'string').map(row => ({ id: row.id, key: row.key === true }))
      };
    } else if (message.type === 'brain.note' && message.re && message.re === pcKeyAsk && typeof message.text === 'string') {
      if (message.state !== 'checking') pcKeyAsk = '';
      if (message.state === 'saved' && parts) parts.pcKey.value = '';
      pcStatus(message.text.slice(0, 200), message.state === 'failed');
    } else return;
    if (sheet && !sheet.hidden) render();
  };

  const settings = (home, view) => {
    homeGuide = home || homeGuide;
    if (!sheet) build();
    back = document.activeElement;
    const brain = readBrain(), keyed = readKeyed();
    parts.provider.value = keyed ? keyed.provider : parts.provider.value || ORDER[0];
    parts.keyOpen = !!brain && brain.provider !== 'free';
    parts.key.value = '';
    status('');
    pcStatus('');
    askBrains();
    render(view || (on() ? 'phone' : 'pc'));
    parts.words.scrollTop = 0;
    sheet.hidden = false;
    if (scrim()) scrim().hidden = false;
    if (brain && brain.provider === 'free') readFree();
    if (view === 'phone') (parts.keyOpen ? parts.key : parts.freeChoice).focus({ preventScroll: true });
    else $('#brain-title').focus({ preventScroll: true });
  };

  // The first screen's way in without a PC, the mode tag next to her name, and the PC-only buttons while she's here.
  const mount = () => {
    const card = $('#pair-card');
    if (card && !$('#native-start')) {
      const start = button('Use her without a PC', 'nbtn wide native-start');
      start.id = 'native-start';
      const demo = $('#try-demo');
      const or = make('p', 'native-or', 'No PC? Start free');
      if (demo && demo.parentNode === card) card.insertBefore(start, demo);
      else card.append(start);
      card.insertBefore(or, start);
      start.addEventListener('click', () => settings(null, 'phone'));
    }
    const top = $('.top');
    const tag = make('span', 'tag native-mode');
    tag.id = 'native-mode';
    tag.hidden = true;
    if (top) top.append(tag);
    const settingsButton = $('#f-settings');
    if (settingsButton) settingsButton.setAttribute('aria-label', 'Settings: her brain and the home screen');
    const screen = $('#f-screen');
    if (screen) screen.addEventListener('click', event => {
      if (!(remote() && remote().info.native)) return;
      event.stopImmediatePropagation();
      event.preventDefault();
      toast('Seeing your screen needs Capri on your PC. Switch in SETTINGS.');
    }, true);
    if (remote()) remote().onMessage(heardBrains);
    if (remote()) remote().onState((state, info) => {
      tag.hidden = state === 'unpaired' || state === 'pairing';
      tag.textContent = info.native ? 'This phone' : info.demo ? 'Demo PC' : 'Your PC';
      tag.classList.toggle('phone', !!info.native);
      if (state !== 'connected' || info.native || info.demo) { pcBrains = null; pcAsked = false; }
      else if (sheet && !sheet.hidden && !pcAsked) askBrains();
      if (sheet && !sheet.hidden) render();
    });
  };
  mount();

  window.CapriNative = {
    on,
    use,
    link,
    chat: fn => { if (typeof fn === 'function') chatOf = fn; },
    starters: () => remote() && remote().info.native ? ['rate my day so far', 'pick a game for tonight', 'tell me a Night City story', 'help me write a text'] : null,
    settings: home => settings(home),
    // The home screen guide, for SETTINGS opened from the pair screen's Use her without a PC.
    guide: fn => { if (typeof fn === 'function') homeGuide = fn; },
    // For remote/test/native.test.mjs.
    persona,
    kindOf,
    pickModel
  };
})();

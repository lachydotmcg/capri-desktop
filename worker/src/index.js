import Anthropic from '@anthropic-ai/sdk';

const sha256 = async (text) => {
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(text));
  return [...new Uint8Array(digest)].map(byte => byte.toString(16).padStart(2, '0')).join('');
};

const counters = { '/star': 'stars', '/download': 'downloads' };

const site = 'https://capriai.app/';
const discord = 'https://discord.gg/capridesktop';
const models = [
  { name: '@cf/google/gemma-4-26b-a4b-it', vision: true, options: { max_completion_tokens: 260, temperature: 0.3, chat_template_kwargs: { enable_thinking: false } } },
  { name: '@cf/meta/llama-3.3-70b-instruct-fp8-fast', options: { max_tokens: 260, temperature: 0.3 } }
];
const claudeModel = 'claude-haiku-5-5';
const perVisitor = 40;
const perVisitorPictures = 10;
const perDay = 2000;
const look = 'Write down the exact words you can read in this screenshot, like titles, messages and buttons, then name the app or window in a few words. Plain text, under 60 words.';
const anchors = { 2: 'install', 3: 'mac', 4: 'linux', 5: 'first-run', 6: 'first-run', 7: 'mainframe', 8: 'menu', 9: 'brain', 10: 'brain', 11: 'voice', 12: 'voice-chat', 13: 'notes', 14: 'moving', 15: 'do', 16: 'screen', 17: 'updates', 18: 'uninstall', 19: 'privacy', 20: 'get-help', 21: 'mac-linux', 22: 'older', 23: 'fixes' };
const skip = new Set('a an and any also are as at be but by can could do does doing done for from get got has have her hers here how i if in into is it its just know like make me my not of on one or please say said says she so some stuff that the their them then there they thing this to too up want was way we what when where which who why will with would you your capri choom help'.split(' '));
const same = { bigger: 'siz', larger: 'siz', smaller: 'siz', resiz: 'siz', big: 'siz', tiny: 'siz', giant: 'siz', viru: 'antiviru', virus: 'antiviru', trojan: 'antiviru', malwar: 'antiviru', defender: 'antiviru', publisher: 'smartscreen', unsign: 'smartscreen', microphon: 'mic', louder: 'volum', quieter: 'volum' };
const rules = [
  "You are Capri, the glitchy cyberpunk desktop companion from the Capri app. You're in the corner of your own website, helping people set you up and fix problems.",
  'Answer only from the help notes below. If they do not cover the question, say you are not sure, and point them to the Discord (' + discord + ') or the Help page.',
  'Keep it short: one to three sentences in plain, friendly words. Name the exact button, menu or setting, like Mainframe > Settings > Sleep. No lists, headings or markdown.',
  'Most replies have no slang. Now and then, about one reply in four, you can say choom once.',
  'Never make up features, prices, steps or links.',
  "When something isn't working and you can't tell why, ask one or two short questions, not more: what still works (typing to her, hearing her, her hearing them, screen reading, jobs), the exact error words, their Capri version, or the exact BRAIN line like Built-in · Gemma 4 E4B.",
  "Give one fix at a time, and don't repeat a fix they already tried. A status like Voice link online, or a thanks, doesn't prove it's fixed: ask if it works now.",
  'If the notes have no fix, or it is still broken after their steps, tell them to post in the Discord with Copy error details, the exact error and what they tried.',
  "Don't ask for API keys or personal details. If someone pastes a key, tell them to delete that key on the site that made it and make a new one, since keys are private.",
  'If they send a screenshot, look at it and answer about what it shows. Never repeat keys, passwords, emails or names you can see in it.',
  "Only help with Capri. For anything else, say you're just here for Capri help. Keep it kind and clean: no swearing, romance, or anything risky."
].join('\n');

let notes = { at: 0, url: '', sections: [] };
let modFolders = { at: 0, names: new Set() };

const folderName = /^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$/;

const modListed = async folder => {
  if (!folderName.test(folder) || folder.includes('..')) return false;
  if (Date.now() - modFolders.at > 300000) {
    const answer = await fetch('https://api.github.com/repos/lachydotmcg/capri-mods/git/trees/HEAD?recursive=1', { headers: { 'User-Agent': 'capri-reactions', Accept: 'application/vnd.github+json' } });
    if (answer.ok) {
      const { tree } = await answer.json();
      const names = new Set();
      for (const entry of tree || []) {
        const match = /^mods\/([^/]+)\/[^/]+\.capri-(?:mod|code|skin)$/.exec(entry.path || '');
        if (match && entry.type === 'blob') names.add(match[1]);
      }
      modFolders = { at: Date.now(), names };
    } else if (!modFolders.at) {
      throw new Error('GitHub said ' + answer.status);
    }
  }
  return modFolders.names.has(folder);
};

const modRows = 'SELECT f.folder AS folder, COALESCE(d.count, 0) AS downloads, COALESCE(r.rating, 0) AS rating, COALESCE(r.votes, 0) AS votes FROM (SELECT folder FROM mod_downloads UNION SELECT folder FROM mod_ratings) f LEFT JOIN mod_downloads d ON d.folder = f.folder LEFT JOIN (SELECT folder, AVG(stars) AS rating, COUNT(*) AS votes FROM mod_ratings GROUP BY folder) r ON r.folder = f.folder';

const mods = async (request, env, ctx, path, reply) => {
  if (path === '/mods') {
    if (request.method !== 'GET') return reply({ error: 'Not found' }, 404);
    const { results } = await env.DB.prepare(modRows).all();
    return reply({ mods: results });
  }
  if (request.method !== 'POST' || (path !== '/mods/download' && path !== '/mods/rate')) return reply({ error: 'Not found' }, 404);
  const sent = await request.json().catch(() => null);
  const folder = sent && typeof sent.folder === 'string' ? sent.folder : '';
  let listed;
  try { listed = await modListed(folder); }
  catch { return reply({ error: 'Try again soon' }, 503); }
  if (!listed) return reply({ error: 'No such mod' }, 404);
  const visitor = request.headers.get('CF-Connecting-IP') || 'local';
  if (path === '/mods/rate') {
    const stars = sent.stars;
    if (!Number.isInteger(stars) || stars < 1 || stars > 5) return reply({ error: 'Stars go from 1 to 5' }, 400);
    const who = await sha256('mod-rating:' + visitor + ':' + env.SALT);
    await env.DB.prepare('INSERT INTO mod_ratings (folder, visitor, stars) VALUES (?, ?, ?) ON CONFLICT (folder, visitor) DO UPDATE SET stars = excluded.stars').bind(folder, who, stars).run();
  } else {
    const day = Math.floor(Date.now() / 86400000);
    const key = await sha256('mod-download:' + folder + ':' + visitor + ':' + day + ':' + env.SALT);
    const tap = await env.DB.prepare('INSERT OR IGNORE INTO taps (key, day) VALUES (?, ?)').bind(key, day).run();
    if (tap.meta.changes === 1) await env.DB.prepare('INSERT INTO mod_downloads (folder, count) VALUES (?, 1) ON CONFLICT (folder) DO UPDATE SET count = count + 1').bind(folder).run();
    ctx.waitUntil(env.DB.prepare('DELETE FROM taps WHERE day < ?').bind(day).run());
  }
  return reply(await env.DB.prepare(modRows + ' WHERE f.folder = ?').bind(folder).first());
};

const stem = word => {
  if (word.length > 5 && word.endsWith('ing')) word = word.slice(0, -3);
  else if (word.length > 4 && word.endsWith('ed')) word = word.slice(0, -2);
  else if (word.length > 4 && word.endsWith('es')) word = word.slice(0, -2);
  else if (word.length > 3 && word.endsWith('s') && !word.endsWith('ss')) word = word.slice(0, -1);
  return word.length > 3 && word.endsWith('e') ? word.slice(0, -1) : word;
};

const words = (text, asking) => (text.toLowerCase().match(/[a-z0-9]+/g) || [])
  .filter(word => word.length > 1 && !skip.has(word))
  .map(stem)
  .map(word => asking && same[word] || word);

const parse = text => {
  const sections = [];
  let number = 0;
  let chapter = '';
  let current = null;
  for (const line of text.split(/\r?\n/)) {
    const big = line.match(/^## (\d+)\. (.+)/);
    const small = line.match(/^### (.+)/);
    if (big) {
      number = Number(big[1]);
      chapter = big[2].trim();
      current = { number, chapter, title: chapter, lines: [] };
      sections.push(current);
    } else if (small && number) {
      current = { number, chapter, title: small[1].trim(), lines: [] };
      sections.push(current);
    } else if (current) current.lines.push(line);
  }
  return sections
    .filter(section => !/needs checking/i.test(section.chapter))
    .map(section => {
      const body = section.lines.join('\n').trim();
      return { number: section.number, chapter: section.chapter, title: section.title, body, heading: words(section.chapter + ' ' + section.title), terms: words(body) };
    })
    .filter(section => section.body);
};

const knowledge = async env => {
  const url = env.KNOWLEDGE_URL || site + 'help/knowledge.md';
  if (notes.url === url && notes.sections.length && Date.now() - notes.at < 600000) return notes.sections;
  try {
    const response = await fetch(url, { cf: { cacheTtl: 600, cacheEverything: true } });
    if (!response.ok) throw new Error('Help notes answered ' + response.status);
    notes = { at: Date.now(), url, sections: parse(await response.text()) };
  } catch (error) {
    if (notes.url !== url || !notes.sections.length) throw error;
  }
  return notes.sections;
};

const pick = (sections, text) => {
  const asked = [...new Set(words(text, true))];
  if (!asked.length) return [];
  const seen = {};
  for (const section of sections) for (const term of new Set([...section.heading, ...section.terms])) seen[term] = (seen[term] || 0) + 1;
  const scored = sections.map(section => {
    let score = 0;
    for (const term of asked) {
      if (!seen[term]) continue;
      const weight = Math.log(1 + sections.length / seen[term]);
      const inBody = section.terms.filter(word => word === term).length;
      const inHeading = section.heading.filter(word => word === term).length;
      score += weight * (Math.min(inBody, 4) + inHeading * 4);
    }
    return { section, score };
  }).filter(entry => entry.score > 0).sort((a, b) => b.score - a.score);
  const chosen = [];
  let size = 0;
  for (const entry of scored) {
    if (chosen.length === 4 || size > 7000) break;
    chosen.push(entry);
    size += Math.min(entry.section.body.length, 2600);
  }
  return chosen;
};

const link = section => {
  if (/free nvidia key/i.test(section.title)) return { title: 'Free NVIDIA key', href: site + 'help/#free-key' };
  if (/^settings page/i.test(section.title)) return { title: 'Settings', href: site + 'help/#settings' };
  return anchors[section.number] ? { title: section.chapter, href: site + 'help/#' + anchors[section.number] } : { title: 'Help page', href: site + 'help/' };
};

const said = result => {
  const text = typeof result === 'string' ? result : result && (result.response || (result.choices && result.choices[0] && result.choices[0].message && result.choices[0].message.content));
  return typeof text === 'string' ? text.replace(/<think>[\s\S]*?<\/think>/g, '').replace(/[*#`]+/g, '').replace(/\s+/g, ' ').trim() : '';
};

const picture = value => typeof value === 'string' && value.length <= 1400000 && /^data:image\/(jpeg|png|webp);base64,[A-Za-z0-9+/]+={0,2}$/.test(value) ? value : null;

const card = image => {
  const [, type, data] = image.match(/^data:([^;]+);base64,(.+)$/);
  return { type: 'image', source: { type: 'base64', media_type: type, data } };
};

const clip = reply => reply.length > 700 ? reply.slice(0, 697).replace(/\s+\S*$/, '') + '...' : reply;

// Claude answers when the worker has an ANTHROPIC_API_KEY secret, and Workers AI is the fallback. null means Claude refused.
const ask = async (env, messages, max_tokens, system) => {
  const client = new Anthropic({ apiKey: env.ANTHROPIC_API_KEY, maxRetries: 1, timeout: 20000 });
  const response = await client.messages.create({ model: claudeModel, max_tokens, system, messages, output_config: { effort: 'low' } });
  return response.stop_reason === 'refusal' ? null : said(response.content.filter(block => block.type === 'text').map(block => block.text).join(' '));
};

const see = async (env, image) => {
  if (env.AI) try {
    const result = await env.AI.run(models[0].name, { messages: [{ role: 'user', content: [{ type: 'text', text: look }, { type: 'image_url', image_url: { url: image } }] }], ...models[0].options, max_completion_tokens: 120, temperature: 0 });
    const seen = said(result).slice(0, 600);
    if (seen) return seen;
  } catch (error) { }
  if (env.ANTHROPIC_API_KEY) try { return (await ask(env, [{ role: 'user', content: [card(image), { type: 'text', text: look }] }], 1024) || '').slice(0, 600); } catch (error) { }
  return '';
};

// Why each model gave nothing goes in trouble, so a broken help line shows up in the logs instead of only as "glitching".
const think = async (env, asked, messages, plain, trouble) => {
  if (env.ANTHROPIC_API_KEY) {
    try {
      const reply = await ask(env, asked, 1024, messages[0].content);
      if (reply === null) { trouble.push('claude-refused: ' + claudeModel); return null; }
      if (reply) return clip(reply);
      trouble.push('claude-empty: ' + claudeModel);
    } catch (error) { trouble.push('claude-' + (error.status || 'error') + ': ' + String(error.message).slice(0, 200)); }
  }
  for (const model of env.AI ? models : []) {
    try {
      const reply = said(await env.AI.run(model.name, { messages: model.vision ? messages : plain, ...model.options }));
      if (reply) return clip(reply);
      trouble.push(model.name + ': empty reply');
    } catch (error) { trouble.push(model.name + ': ' + String(error && error.message || error).slice(0, 200)); }
  }
  return null;
};

const help = async (request, env, ctx, reply) => {
  let body;
  try { body = await request.json(); } catch (error) { return reply({ error: 'Bad request' }, 400); }
  const sentPicture = body && body.image != null;
  const image = sentPicture ? picture(body.image) : null;
  if (sentPicture && !image) return reply({ reply: "I couldn't open that picture. Try a PNG or JPG screenshot.", links: [] }, 400);
  const question = String(body && body.question || '').replace(/\s+/g, ' ').trim().slice(0, 300) || (image ? "What's going on in this screenshot?" : '');
  if (!question) return reply({ error: 'Ask something' }, 400);
  const history = (Array.isArray(body.history) ? body.history : [])
    .filter(turn => turn && (turn.role === 'user' || turn.role === 'assistant') && typeof turn.text === 'string')
    .slice(-6)
    .map(turn => ({ role: turn.role, content: turn.text.replace(/\s+/g, ' ').trim().slice(0, 500) }))
    .filter(turn => turn.content);

  const day = Math.floor(Date.now() / 86400000);
  const visitor = await sha256('help:' + (request.headers.get('CF-Connecting-IP') || 'local') + ':' + day + ':' + env.SALT);
  const mine = await env.DB.prepare('INSERT INTO helps (key, day, count, pictures) VALUES (?, ?, 1, ?) ON CONFLICT(key) DO UPDATE SET count = count + 1, pictures = pictures + excluded.pictures RETURNING count, pictures').bind(visitor, day, image ? 1 : 0).first();
  ctx.waitUntil(env.DB.prepare('DELETE FROM helps WHERE day < ?').bind(day).run());
  if (mine.count > perVisitor) return reply({ reply: "That's a lot of questions for one day, choom. The Help page has everything, or ask in the Discord.", links: [{ title: 'Help page', href: site + 'help/' }, { title: 'Discord', href: discord }] }, 429);
  if (image && mine.pictures > perVisitorPictures) return reply({ reply: "That's a lot of pictures for one day. Type out what yours says and I'll help.", links: [{ title: 'Help page', href: site + 'help/' }] }, 429);
  const all = await env.DB.prepare('INSERT INTO help_days (day, count) VALUES (?, 1) ON CONFLICT(day) DO UPDATE SET count = count + 1 RETURNING count').bind(day).first();
  if (all.count > perDay) return reply({ reply: "I'm swamped today. The Help page has everything, or ask in the Discord.", links: [{ title: 'Help page', href: site + 'help/' }, { title: 'Discord', href: discord }] }, 429);

  let sections = [];
  try { sections = await knowledge(env); } catch (error) { }
  const recent = history.filter(turn => turn.role === 'user').slice(-1).map(turn => turn.content).join(' ');
  const seen = image ? await see(env, image) : '';
  const ranked = pick(sections, question + ' ' + seen + ' ' + recent);
  const chosen = ranked.map(entry => entry.section);
  const facts = sections.filter(section => section.number === 1 && section.title !== section.chapter).slice(0, 2);
  const shown = [...chosen, ...facts.filter(section => !chosen.includes(section))];
  const written = shown.map(section => '[' + section.chapter + (section.title !== section.chapter ? ' > ' + section.title : '') + ']\n' + section.body.slice(0, 2600)).join('\n\n');
  const system = { role: 'system', content: rules + '\n\nHelp notes:\n' + (written || '(nothing matched)') };
  const messages = [system, ...history, { role: 'user', content: image ? [{ type: 'text', text: question }, { type: 'image_url', image_url: { url: image } }] : question }];
  const plain = image ? [system, ...history, { role: 'user', content: question + (seen ? '\n\n(Their screenshot shows: ' + seen + ')' : "\n\n(They sent a screenshot you can't see. Ask them to type out what it says.)") }] : messages;
  const first = history.findIndex(turn => turn.role === 'user');
  const asked = [...history.slice(first < 0 ? history.length : first), { role: 'user', content: image ? [card(image), { type: 'text', text: question }] : question }];
  const links = [];
  for (const entry of ranked.slice(0, 2)) {
    const target = link(entry.section);
    if (entry.score >= ranked[0].score * 0.6 && !links.some(known => known.href === target.href)) links.push(target);
  }

  const trouble = env.AI ? [] : ['no AI binding'];
  const answer = await think(env, asked, messages, plain, trouble);
  if (!answer) console.error('Help line failed: ' + trouble.join(' | '));
  const claude = trouble.find(entry => entry.startsWith('claude-'));
  const why = answer ? null : claude ? claude.split(':')[0] : /\b4006\b|daily free allocation/i.test(trouble.join(' ')) ? 'allowance' : (trouble.join(' ').match(/\b(\d{4,5})\b/) || [null, env.AI ? 'ai' : 'binding'])[1];
  if (why === 'allowance') return reply({ reply: "I've answered all I can for today, choom. I'm back after midnight UTC. The Help page has everything, or ask in the Discord.", links: [{ title: 'Help page', href: site + 'help/' }, { title: 'Discord', href: discord }], why });
  if (!answer) return reply({ why,  reply: chosen.length ? 'My help line is glitching, but this part of the Help page should cover it:' : "My help line is glitching right now. The Help page has everything, or ask in the Discord.", links: chosen.length ? links : [{ title: 'Help page', href: site + 'help/' }, { title: 'Discord', href: discord }] });
  return reply({ reply: answer, links: links.length ? links : [{ title: 'Help page', href: site + 'help/' }], answered: true });
};

export default {
  async fetch(request, env, ctx) {
    const cors = {
      'Access-Control-Allow-Origin': '*',
      'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
      'Access-Control-Allow-Headers': 'Content-Type',
      'Access-Control-Max-Age': '86400'
    };
    const reply = (body, status = 200) => Response.json(body, { status, headers: { ...cors, 'Cache-Control': 'no-store' } });

    if (request.method === 'OPTIONS') return new Response(null, { status: 204, headers: cors });
    const path = new URL(request.url).pathname;
    if (path === '/help') return request.method === 'POST' ? help(request, env, ctx, reply) : reply({ error: 'Not found' }, 404);
    if (path === '/mods' || path.startsWith('/mods/')) return mods(request, env, ctx, path, reply);
    const table = counters[path];
    if (!table || !['GET', 'POST'].includes(request.method)) return reply({ error: 'Not found' }, 404);

    if (request.method === 'POST') {
      const day = Math.floor(Date.now() / 86400000);
      const visitor = request.headers.get('CF-Connecting-IP') || 'local';
      const key = await sha256(table + ':' + visitor + ':' + day + ':' + env.SALT);
      const tap = await env.DB.prepare('INSERT OR IGNORE INTO taps (key, day) VALUES (?, ?)').bind(key, day).run();
      if (tap.meta.changes === 1) await env.DB.prepare('UPDATE ' + table + ' SET count = count + 1').run();
      ctx.waitUntil(env.DB.prepare('DELETE FROM taps WHERE day < ?').bind(day).run());
    }

    const { count } = await env.DB.prepare('SELECT count FROM ' + table).first();
    return reply({ count });
  }
};

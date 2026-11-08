const plain = value => String(value || '').replace(/\s+/g, ' ').trim()
export const name = message => plain(message.member?.displayName || message.author?.globalName || message.author?.username || 'Someone')
const prefix = /^!capri(?:\s|$)/i

// She turned down an @everyone and still typed it (Lachy, 6 Nov). A zero-width space keeps @everyone and @here from ever reading as a ping.
export const quiet = text => String(text || '').replace(/@(everyone|here)\b/gi, '@​$1')

export const fallback = "My help line's glitching right now. The Help page has everything: <https://capriai.app/help/>"
export const empty = 'Ask me something after !capri.'

export const trigger = (message, me, replied = null, channels = new Set()) => {
  if (!message.author || message.author.bot || message.author.id === me) return false
  if (message.guildId && channels.size && !channels.has(message.channelId)) return false
  return !message.guildId || prefix.test((message.content || '').trimStart()) || new RegExp('<@!?' + me + '>').test(message.content || '') || replied?.author?.id === me
}

export const question = (text, me) => plain(String(text || '').replace(new RegExp('<@!?' + me + '>', 'g'), ' ').trim().replace(prefix, '')).slice(0, 600)

const linkLine = /^[^:\n]+: <https?:\/\/[^\s<>]+>$/
const spoken = text => plain(String(text || '').split('\n').filter(line => !linkLine.test(line)).join(' '))

export const context = (message, replied, recent = [], me = null) => {
  const lines = []
  if (replied) lines.push(('Replied to ' + name(replied) + ': ' + spoken(replied.content)).slice(0, 400))
  const fresh = entry => entry.id !== message.id && entry.channelId === message.channelId && entry.createdTimestamp >= message.createdTimestamp - 600000 && entry.createdTimestamp <= message.createdTimestamp
  const newest = (a, b) => b.createdTimestamp - a.createdTimestamp || (BigInt(a.id) < BigInt(b.id) ? 1 : -1)
  const own = recent.filter(entry => fresh(entry) && entry.author?.id === message.author.id && !entry.author.bot).sort(newest).slice(0, 3).reverse()
  for (const entry of own) lines.push(('Earlier from ' + name(entry) + ': ' + plain(entry.content)).slice(0, 250))
  // Her own last lines in the channel, so she doesn't greet the next person the same way (Lachy, 6 Nov).
  const hers = me ? recent.filter(entry => fresh(entry) && entry.author?.id === me && entry.id !== replied?.id).sort(newest).slice(0, 2).reverse() : []
  for (const entry of hers) lines.push(('You said: ' + spoken(entry.content)).slice(0, 250))
  return lines.join('\n').slice(0, 1200)
}

// Lachy's greeting for each new support ticket (6 Nov).
export const greeting = me => 'Hey, how can I help? Just do !capri, <@' + me + '>, or reply to my messages for assistance!'

// A Ticket Tool ticket: a text channel like #ticket-0224, or one renamed when closed, in the tickets category.
export const ticket = channel => Boolean(channel?.guildId) && channel.type === 0 && (/^ticket-\d+$/i.test(channel.name || '') || /^tickets?$/i.test(channel.parent?.name || ''))

const oldest = (a, b) => a.createdTimestamp - b.createdTimestamp || (BigInt(a.id) < BigInt(b.id) ? -1 : 1)

// Ticket Tool's welcome: the ticket's oldest bot message that mentions someone. Whoever it mentions opened the ticket.
export const welcomed = (rows = [], me = null) => rows.filter(row => row?.author?.bot && row.author.id !== me && row.mentions?.users?.size).sort(oldest)[0] || null

// In a ticket she reads the ticket instead of the last ten minutes (Lachy, 6 Nov: "make capri actually have context"). Who opened
// it comes from Ticket Tool's welcome, then their first message, since that's usually the problem, then the newest lines that fit.
export const ticketContext = (message, replied, rows = [], me = null) => {
  const hello = me ? greeting(me) : null
  const welcome = welcomed(rows, me)
  const opener = welcome?.mentions.users.first()
  const seen = new Set()
  const said = rows.filter(row => {
    if (!row?.id || seen.has(row.id) || row.id === message.id || row.channelId !== message.channelId || row.createdTimestamp > message.createdTimestamp) return false
    seen.add(row.id)
    return row.author?.id === me ? spoken(row.content) && row.content !== hello : !row.author?.bot && plain(row.content)
  }).sort(oldest)
  const line = row => row.author?.id === me ? 'You said: ' + spoken(row.content) : name(row) + ': ' + plain(row.content)
  const first = said.find(row => row.author?.id !== me && (!opener || row.author?.id === opener.id))
  const head = 'This is support ticket #' + plain(message.channel?.name || 'ticket') + (opener ? ', opened by ' + name({ member: welcome.mentions.members?.first?.(), author: opener }) : '') + '.'
  const opening = first ? (name(first) + ' opened with: ' + plain(first.content)).slice(0, 400) : ''
  const reply = replied && replied.content !== hello && !said.some(row => row.id === replied.id) ? ('Replied to ' + name(replied) + ': ' + spoken(replied.content)).slice(0, 300) : ''
  let room = 1200 - head.length - (opening ? opening.length + 1 : 0) - (reply ? reply.length + 1 : 0)
  const later = []
  for (const row of said.filter(row => row !== first).reverse()) {
    const text = line(row).slice(0, 250)
    if (text.length + 1 > room) break
    later.unshift(text)
    room -= text.length + 1
  }
  return [head, opening, ...later, reply].filter(Boolean).join('\n').slice(0, 1200)
}

// In tickets she answers without a ping, since people rarely mention her there, keeps quiet while staff are helping, and
// !capri --off and --on switch her per ticket (Lachy, 6 Nov).
const half = 1800000

// Staff are anyone whose highest role is at or above the server's Helper role, or who has Administrator or Manage Messages.
// Staff roles are found by name, whatever the case, emoji or fancy letters, and the lowest of them is the line, so a server
// without a Helper role uses its lowest Moderator, Admin or Staff role.
const titles = /^(?:(?:trial|junior|senior|head|lead|chief) )?(?:helpers?|mods?|moderators?|admins?|administrators?|staff|support|owners?)(?: team)?$/
const bare = text => String(text || '').normalize('NFKD').replace(/\p{M}+/gu, '').toLowerCase().replace(/[^a-z]+/g, ' ').trim()
const administrator = 1n << 3n, manageMessages = 1n << 13n
export const staffLine = (roles = []) => [...roles].filter(role => titles.test(bare(role?.name))).sort((a, b) => a.position - b.position)[0] || null
export const staff = (member, roles = []) => {
  if (!member || member.user?.bot) return false
  if (member.permissions?.has?.(administrator) || member.permissions?.has?.(manageMessages)) return true
  const bar = staffLine(roles), top = member.roles?.highest?.position
  return Boolean(bar) && Number.isFinite(top) && top >= bar.position
}

// When a person has to step in she names who, and the bot does the pinging, so nothing she types can mention anyone (Lachy, 7 Nov).
// Roles are found by name like the staff line above. Only the plain role counts, so Head Moderator or Helper Ping never ring.
const callable = { helper: /^helpers?$/, moderator: /^(?:mods?|moderators?)$/, developer: /^(?:devs?|developers?)$/ }
export const pingRole = (who, roles = []) => [...roles].find(role => callable[who]?.test(bare(role?.name))) || null
const calling = { helper: 'someone needs a hand here.', moderator: 'this one needs a mod.', developer: 'this looks like a real bug.' }
export const called = (who, role) => '<@&' + role.id + '> ' + calling[who]
export const notHere = "I can't ping anyone from DMs. Open a ticket in the server and someone can take a look."

// One ping per role per channel each half hour, and one per person per role each hour, so nobody can spam staff through her.
export const pingGate = ({ channelWait = half, personWait = 3600000, clock = Date.now } = {}) => {
  const last = new Map()
  return (who, channel, person) => {
    const now = clock(), here = who + ' in ' + channel, them = who + ' for ' + person
    if (now - (last.get(here) ?? -Infinity) < channelWait || now - (last.get(them) ?? -Infinity) < personWait) return false
    last.set(here, now)
    last.set(them, now)
    return true
  }
}

// !capri --off and !capri --on, or --off and --on after an @mention of her or on their own: 'off', 'on' or null.
export const toggle = (text, me) => question(text, me).match(/^--(on|off)$/i)?.[1].toLowerCase() ?? null

export const switchedOff = "Okay, I'm off in this ticket. !capri --on brings me back."
export const switchedOn = "Okay, I'm on in this ticket."
export const notYours = 'Only staff or whoever opened this ticket can do that.'
export const ticketsOnly = 'I only switch off and on in support tickets.'

// She waits for a pause, so lines sent in a row get one answer. Each line restarts the 8 second wait, and someone typing there
// holds it 10 seconds more, but never past a minute after the last line. `ready` gets the lines, oldest first.
export const pauses = (ready, { wait = 8000, typing = 10000, longest = 60000, clock = Date.now, later = setTimeout, stop = clearTimeout } = {}) => {
  const waiting = new Map()
  const arm = (key, entry, at) => {
    stop(entry.timer)
    entry.at = at
    entry.timer = later(() => { waiting.delete(key); ready(entry.lines) }, Math.max(0, at - clock()))
  }
  return {
    add: (key, line) => {
      const entry = waiting.get(key) || { lines: [] }
      entry.lines.push(line)
      entry.last = clock()
      waiting.set(key, entry)
      arm(key, entry, entry.last + wait)
    },
    typing: key => {
      const entry = waiting.get(key)
      const at = entry ? Math.min(clock() + typing, entry.last + longest) : 0
      if (entry && at > entry.at) arm(key, entry, at)
    },
    // Lines that no longer need her: all of them when staff post, or one person's when they ask her directly.
    drop: (key, keep = () => false) => {
      const entry = waiting.get(key)
      if (!entry) return
      entry.lines = entry.lines.filter(keep)
      if (!entry.lines.length) { stop(entry.timer); waiting.delete(key) }
    },
    count: key => waiting.get(key)?.lines.length || 0
  }
}

// Staff are handling a ticket while one of them has posted there in the last half hour. Bots never count, so Ticket Tool's
// welcome and her greeting don't, and neither do --off and --on lines or anything before the last --on (`since`).
export const handling = async (rows, isStaff, now, since = 0, me = null) => {
  const people = new Set()
  for (const row of rows) if (row?.author && !row.author.bot && row.author.id !== me && row.createdTimestamp > Math.max(now - half, since || 0) && !toggle(row.content, me)) people.add(row.author.id)
  for (const id of people) if (await isStaff(id)) return true
  return false
}

// The lines someone sent before a pause, oldest first, as one question. The worker turns line breaks into spaces, so slashes
// keep the lines apart.
export const together = (lines, me) => lines.map(line => question(line.content, me)).filter(Boolean).join(' / ').slice(0, 600)

// ticket-state.json keeps each ticket's last --off or --on as { off, at }. Anything malformed is dropped, and so is an --on
// past its half hour, since by then it changes nothing.
export const switches = (text, now = Date.now()) => {
  const state = new Map()
  let saved = null
  try { saved = JSON.parse(text) } catch {}
  if (!saved || typeof saved !== 'object' || Array.isArray(saved)) return state
  for (const [id, entry] of Object.entries(saved)) {
    if (/^\d{1,20}$/.test(id) && typeof entry?.off === 'boolean' && Number.isFinite(entry.at) && (entry.off || entry.at > now - half)) state.set(id, { off: entry.off, at: entry.at })
  }
  return state
}

export const history = (chain, me) => {
  const turns = []
  for (let i = 0; i < Math.min(chain.length, 6); i += 2) {
    const answer = chain[i], asked = chain[i + 1]
    if (answer?.author?.id !== me || !asked?.author || asked.author.bot || asked.author.id === me || answer.reference?.messageId !== asked.id) break
    if (i && chain[i - 1].reference?.messageId !== answer.id) break
    const text = question(asked.content, me)
    const said = spoken(answer.content).slice(0, 500)
    if (!text || !said) break
    // Each question carries who asked it, so someone replying to her answer for another person isn't taken for them.
    turns.unshift({ role: 'user', text: (name(asked) + ': ' + text).slice(0, 500) }, { role: 'assistant', text: said })
  }
  return turns
}

export const attachment = (...messages) => {
  for (const message of messages) for (const file of message?.attachments?.values() || []) {
    if (!Number.isFinite(file.size) || file.size <= 0 || file.size >= 1048576) continue
    try {
      const url = new URL(file.url)
      if (url.protocol !== 'https:' || !['cdn.discordapp.com', 'media.discordapp.net'].includes(url.hostname) || url.username || url.password || url.port) continue
      const ext = (file.name || url.pathname).match(/\.(png|jpe?g|webp)$/i)?.[1].toLowerCase()
      const type = file.contentType?.split(';')[0].trim().toLowerCase() || (ext ? 'image/' + (ext === 'jpg' ? 'jpeg' : ext) : '')
      if (['image/png', 'image/jpeg', 'image/webp'].includes(type)) return { url: url.href, type }
    } catch {}
  }
  return null
}

export const links = entries => {
  const lines = [], seen = new Set()
  for (const entry of Array.isArray(entries) ? entries : []) {
    if (typeof entry?.title !== 'string' || typeof entry.href !== 'string') continue
    try {
      const url = new URL(entry.href)
      const title = plain(entry.title).replace(/[<>]/g, '').slice(0, 120)
      if (!title || !['https:', 'http:'].includes(url.protocol) || url.username || url.password || /[\s<>]/.test(url.href) || url.href.length > 1500 || seen.has(url.href)) continue
      lines.push(title + ': <' + url.href + '>')
      seen.add(url.href)
    } catch {}
  }
  return lines.join('\n')
}

const reason = value => typeof value === 'string' && /^(allowance|ai|binding|claude-(refused|empty|error|\d{3})|\d{4,5})$/.test(value) ? value : null

export const result = (status, body) => {
  const why = reason(body?.why)
  const reply = typeof body?.reply === 'string' ? body.reply.trim() : ''
  if (reply && (status === 429 || (status >= 200 && status < 300 && (body.answered === true || why === 'allowance')))) {
    const more = links(body.links)
    const ping = body.answered === true && typeof body.ping === 'string' && Object.hasOwn(callable, body.ping) ? body.ping : null
    return { text: quiet(reply) + (more ? '\n\n' + more : ''), why: status === 429 ? why || 'limit' : body.answered === true ? 'answered' : why, ...(ping ? { ping } : {}) }
  }
  return { text: fallback, why: why || (status >= 400 && status <= 599 ? 'http-' + status : 'bad-reply') }
}

export const chunks = (text, size = 1900) => {
  if (!Number.isInteger(size) || size < 2) throw new RangeError('Chunk size must be at least 2.')
  const out = []
  while (text.length) {
    let end = Math.min(size, text.length)
    if (end < text.length) {
      const line = text.lastIndexOf('\n', end - 1)
      if (line > 0) end = line + 1
      if (/[\uD800-\uDBFF]/.test(text[end - 1]) && /[\uDC00-\uDFFF]/.test(text[end])) end--
    }
    out.push(text.slice(0, end))
    text = text.slice(end)
  }
  return out
}

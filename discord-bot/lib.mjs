const plain = value => String(value || '').replace(/\s+/g, ' ').trim()
const name = message => plain(message.member?.displayName || message.author?.globalName || message.author?.username || 'Someone')
const prefix = /^!capri(?:\s|$)/i

export const fallback = "My help line's glitching right now. The Help page has everything: <https://capriai.app/help/>"
export const empty = 'Ask me something after !capri.'

export const trigger = (message, me, replied = null, channels = new Set()) => {
  if (!message.author || message.author.bot || message.author.id === me) return false
  if (message.guildId && channels.size && !channels.has(message.channelId)) return false
  return !message.guildId || prefix.test((message.content || '').trimStart()) || new RegExp('<@!?' + me + '>').test(message.content || '') || replied?.author?.id === me
}

export const question = (text, me) => plain(String(text || '').replace(new RegExp('<@!?' + me + '>', 'g'), ' ').trim().replace(prefix, '')).slice(0, 600)

export const context = (message, replied, recent = []) => {
  const lines = []
  if (replied) lines.push(('Replied to ' + name(replied) + ': ' + plain(replied.content)).slice(0, 400))
  const own = recent.filter(entry => entry.id !== message.id && entry.channelId === message.channelId && entry.author?.id === message.author.id && !entry.author.bot && entry.createdTimestamp >= message.createdTimestamp - 600000 && entry.createdTimestamp <= message.createdTimestamp)
    .sort((a, b) => b.createdTimestamp - a.createdTimestamp || (BigInt(a.id) < BigInt(b.id) ? 1 : -1))
    .slice(0, 3).reverse()
  for (const entry of own) lines.push(('Earlier from ' + name(entry) + ': ' + plain(entry.content)).slice(0, 250))
  return lines.join('\n').slice(0, 1200)
}

export const history = (chain, me) => {
  const turns = []
  for (let i = 0; i < Math.min(chain.length, 6); i += 2) {
    const answer = chain[i], asked = chain[i + 1]
    if (answer?.author?.id !== me || !asked?.author || asked.author.bot || asked.author.id === me || answer.reference?.messageId !== asked.id) break
    if (i && chain[i - 1].reference?.messageId !== answer.id) break
    const text = question(asked.content, me).slice(0, 500)
    const said = plain((answer.content || '').split('\n').filter(line => !/^[^:\n]+: <https?:\/\/[^\s<>]+>$/.test(line)).join(' ')).slice(0, 500)
    if (!text || !said) break
    turns.unshift({ role: 'user', text }, { role: 'assistant', text: said })
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
    return { text: reply + (more ? '\n\n' + more : ''), why: status === 429 ? why || 'limit' : body.answered === true ? 'answered' : why }
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

import { existsSync } from 'node:fs'
import { appendFile } from 'node:fs/promises'
import { createHash } from 'node:crypto'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { trigger, question, context, history, attachment, result, chunks, fallback, empty } from './lib.mjs'

const here = path.dirname(fileURLToPath(import.meta.url))
if (existsSync(path.join(here, '.env'))) process.loadEnvFile(path.join(here, '.env'))

const token = process.env.DISCORD_TOKEN?.trim()
const secret = process.env.CAPRI_RELAY_SECRET?.trim()
const url = process.env.CAPRI_HELP_URL?.trim() || 'https://capri-reactions.lachydotmcg.workers.dev/discord'
const channels = new Set((process.env.CAPRI_CHANNELS || '').split(',').map(id => id.trim()).filter(Boolean))
if (!token) { console.error('DISCORD_TOKEN is missing.'); process.exit(1) }
if (!secret) { console.error('CAPRI_RELAY_SECRET is missing.'); process.exit(1) }

const { Client, GatewayIntentBits, Partials } = await import('discord.js')
const client = new Client({
  intents: [GatewayIntentBits.Guilds, GatewayIntentBits.GuildMessages, GatewayIntentBits.MessageContent, GatewayIntentBits.DirectMessages],
  partials: [Partials.Channel]
})

const back = async message => {
  if (!message.reference?.messageId || (message.reference.channelId && message.reference.channelId !== message.channelId)) return null
  return message.fetchReference().catch(() => null)
}

// The asker's last three messages and her own last two in the last ten minutes, so she follows them and doesn't repeat herself.
const recent = async message => {
  const found = [], hers = []
  let before = message.id
  for (let page = 0; (found.length < 3 || hers.length < 2) && page < 2; page++) {
    const batch = await message.channel.messages.fetch({ limit: 100, before })
    const rows = [...batch.values()].sort((a, b) => b.createdTimestamp - a.createdTimestamp || (BigInt(a.id) < BigInt(b.id) ? 1 : -1))
    if (!rows.length) break
    for (const row of rows) {
      if (row.createdTimestamp < message.createdTimestamp - 600000) return [...found, ...hers]
      if (row.author.id === message.author.id && !row.author.bot && found.length < 3) found.push(row)
      if (row.author.id === client.user.id && hers.length < 2) hers.push(row)
      if (found.length === 3 && hers.length === 2) return [...found, ...hers]
    }
    const last = rows.at(-1).id
    if (last === before || rows.length < 100) break
    before = last
  }
  return [...found, ...hers]
}

const chain = async (message, replied) => {
  const found = [], seen = new Set([message.id])
  let answer = replied
  for (let i = 0; i < 3; i++) {
    if (answer?.author.id !== client.user.id || seen.has(answer.id)) break
    seen.add(answer.id)
    const asked = await back(answer)
    if (!asked || asked.author.bot || seen.has(asked.id)) break
    seen.add(asked.id)
    found.push(answer, asked)
    if (i < 2) answer = await back(asked)
  }
  return history(found, client.user.id)
}

const picture = async file => {
  if (!file) return null
  const response = await fetch(file.url, { redirect: 'error', signal: AbortSignal.timeout(10000) })
  if (!response.ok || !response.body) return null
  const type = response.headers.get('Content-Type')?.split(';')[0].trim().toLowerCase()
  if (type && !['image/png', 'image/jpeg', 'image/webp'].includes(type)) { await response.body.cancel(); return null }
  if (Number(response.headers.get('Content-Length')) >= 1048576) { await response.body.cancel(); return null }
  const parts = []
  let size = 0
  for await (const part of response.body) {
    size += part.length
    if (size >= 1048576) return null
    parts.push(Buffer.from(part))
  }
  return size ? 'data:' + (type || file.type) + ';base64,' + Buffer.concat(parts).toString('base64') : null
}

const say = async (message, text) => {
  for (const content of chunks(text)) await message.reply({ content, allowedMentions: { parse: [], repliedUser: true } })
}

const log = async (user, why) => {
  const hash = createHash('sha256').update(user).digest('hex').slice(0, 12)
  try { await appendFile(path.join(here, 'bot.log'), new Date().toISOString() + ' ' + hash + ' ' + why + '\n') }
  catch { console.error('Could not write bot.log.') }
}

client.on('messageCreate', async message => {
  if (message.author.bot || message.author.id === client.user.id) return
  if (message.guildId && channels.size && !channels.has(message.channelId)) return
  const cached = message.reference?.messageId ? message.channel.messages.cache.get(message.reference.messageId) : null
  if (!trigger(message, client.user.id, cached, channels) && message.mentions.repliedUser?.id !== client.user.id) return
  const replied = cached || await back(message)
  if (!trigger(message, client.user.id, replied, channels)) return
  const asked = question(message.content, client.user.id)
  let why = 'empty', timer
  try {
    if (!asked) { await say(message, empty); return }
    await message.channel.sendTyping().catch(() => {})
    timer = setInterval(() => { message.channel.sendTyping().catch(() => {}) }, 8000)
    const [own, turns, image] = await Promise.all([
      recent(message).catch(() => []),
      chain(message, replied).catch(() => []),
      picture(attachment(message, replied)).catch(() => null)
    ])
    why = 'unreachable'
    const response = await fetch(url, {
      method: 'POST',
      headers: { Authorization: 'Bearer ' + secret, 'Content-Type': 'application/json' },
      body: JSON.stringify({ user: message.author.id, question: asked, history: turns, context: context(message, replied, own, client.user.id), image }),
      redirect: 'error',
      signal: AbortSignal.timeout(60000)
    })
    const answer = result(response.status, await response.json().catch(() => null))
    why = answer.why
    await say(message, answer.text)
  } catch {
    if (why !== 'unreachable') why = 'send'
    try { await say(message, fallback) } catch { console.error('Capri could not send a reply.') }
  } finally {
    clearInterval(timer)
    await log(message.author.id, why)
  }
})

client.once('clientReady', () => { console.log('Capri is online.') })
client.on('error', () => { console.error('Discord connection failed.') })
client.login(token).catch(() => { console.error('Could not connect Capri. Check DISCORD_TOKEN and Message Content Intent.'); process.exit(1) })

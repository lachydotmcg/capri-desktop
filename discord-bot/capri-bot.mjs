import { appendFileSync, existsSync, readFileSync, renameSync, writeFileSync } from 'node:fs'
import { appendFile } from 'node:fs/promises'
import { createHash } from 'node:crypto'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { pingRole, pingGate, called, notHere, trigger, question, context, history, attachment, result, chunks, fallback, empty, name, greeting, ticket, ticketContext, welcomed, staff, toggle, switchedOff, switchedOn, notYours, ticketsOnly, pauses, handling, together, switches } from './lib.mjs'

const here = path.dirname(fileURLToPath(import.meta.url))
if (existsSync(path.join(here, '.env'))) process.loadEnvFile(path.join(here, '.env'))

// She stopped once with nothing to show why (6 Nov), so a crash leaves its first line in bot.log before she exits.
const died = error => {
  try { appendFileSync(path.join(here, 'bot.log'), new Date().toISOString() + ' crashed ' + String(error?.stack || error).split('\n')[0].slice(0, 300) + '\n') } catch {}
  process.exit(1)
}
process.on('uncaughtException', died)
process.on('unhandledRejection', died)

const token = process.env.DISCORD_TOKEN?.trim()
const secret = process.env.CAPRI_RELAY_SECRET?.trim()
const url = process.env.CAPRI_HELP_URL?.trim() || 'https://capri-reactions.lachydotmcg.workers.dev/discord'
const channels = new Set((process.env.CAPRI_CHANNELS || '').split(',').map(id => id.trim()).filter(Boolean))
if (!token) { console.error('DISCORD_TOKEN is missing.'); process.exit(1) }
if (!secret) { console.error('CAPRI_RELAY_SECRET is missing.'); process.exit(1) }

const { Client, GatewayIntentBits, Partials } = await import('discord.js')
// Typing events let her wait while someone in a ticket is still typing. That intent isn't privileged, so the portal needs nothing.
const client = new Client({
  intents: [GatewayIntentBits.Guilds, GatewayIntentBits.GuildMessages, GatewayIntentBits.MessageContent, GatewayIntentBits.DirectMessages, GatewayIntentBits.GuildMessageTyping],
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

// A support ticket's first few messages (Ticket Tool's welcome and the opener's problem) and its newest forty, for ticketContext.
const ticketRows = async message => {
  const [start, latest] = await Promise.all([
    message.channel.messages.fetch({ limit: 5, after: '0' }),
    message.channel.messages.fetch({ limit: 40, before: message.id })
  ])
  return [...start.values(), ...latest.values()]
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

// She asked for a person. Only that one role can ring, in its own message, and bot.log gets what happened.
const gate = pingGate()
const summon = async (message, who) => {
  if (!message.guild) {
    await say(message, notHere)
    return '-ping-dm'
  }
  const role = pingRole(who, message.guild.roles.cache.values())
  if (!role) return '-ping-no-role'
  if (!gate(who, message.channelId, message.author.id)) return '-ping-held'
  await message.channel.send({ content: called(who, role), allowedMentions: { parse: [], roles: [role.id] } })
  return '-ping-' + who
}

const log = async (user, why) => {
  const hash = createHash('sha256').update(user).digest('hex').slice(0, 12)
  try { await appendFile(path.join(here, 'bot.log'), new Date().toISOString() + ' ' + hash + ' ' + why + '\n') }
  catch { console.error('Could not write bot.log.') }
}

// Anything unexpected while she handles a message goes to bot.log, and she stays online.
const slipped = error => appendFile(path.join(here, 'bot.log'), new Date().toISOString() + ' slipped ' + String(error?.stack || error).split('\n')[0].slice(0, 300) + '\n').catch(() => {})

// Her answer from the help worker as a reply to `message`. She types while it thinks, says the fallback if anything breaks, and
// bot.log gets why, after `tag`. `build` gathers the question and its context.
const respond = async (message, build, tag = '') => {
  let why = 'unreachable', timer
  try {
    await message.channel.sendTyping().catch(() => {})
    timer = setInterval(() => { message.channel.sendTyping().catch(() => {}) }, 8000)
    const body = await build()
    const response = await fetch(url, {
      method: 'POST',
      headers: { Authorization: 'Bearer ' + secret, 'Content-Type': 'application/json' },
      body: JSON.stringify({ user: message.author.id, name: name(message), ...body }),
      redirect: 'error',
      signal: AbortSignal.timeout(60000)
    })
    const answer = result(response.status, await response.json().catch(() => null))
    why = answer.why
    await say(message, answer.text)
    if (answer.ping) why += await summon(message, answer.ping).catch(() => '-ping-failed')
  } catch {
    if (why !== 'unreachable') why = 'send'
    try { await say(message, fallback) } catch { console.error('Capri could not send a reply.') }
  } finally {
    clearInterval(timer)
    await log(message.author.id, tag + why)
  }
}

// A ping, !capri, a reply to her or a DM: she answers right away.
const answer = async (message, replied) => {
  const me = client.user.id, asked = question(message.content, me)
  if (!asked) {
    try { await say(message, empty) } catch { console.error('Capri could not send a reply.') }
    return log(message.author.id, 'empty')
  }
  const inTicket = ticket(message.channel)
  await respond(message, async () => {
    const [own, turns, image] = await Promise.all([
      (inTicket ? ticketRows(message) : recent(message)).catch(() => []),
      chain(message, replied).catch(() => []),
      picture(attachment(message, replied)).catch(() => null)
    ])
    return { question: asked, history: turns, context: inTicket ? ticketContext(message, replied, own, me) : context(message, replied, own, me), image }
  })
}

// Each ticket's last --off or --on, in ticket-state.json beside her so it outlives a restart (Lachy, 6 Nov).
const stateFile = path.join(here, 'ticket-state.json')
const switched = (() => { try { return switches(readFileSync(stateFile, 'utf8')) } catch { return new Map() } })()
const keep = () => {
  try {
    writeFileSync(stateFile + '.tmp', JSON.stringify(Object.fromEntries(switched)))
    renameSync(stateFile + '.tmp', stateFile)
  } catch { console.error('Could not save ticket-state.json.') }
}

// Without the server members intent she only learns roles from messages, so anyone not seen yet is fetched by id.
const isStaff = async (guild, id) => staff(guild.members.cache.get(id) || await guild.members.fetch(id).catch(() => null), guild.roles.cache.values())

// Un-pinged lines in a ticket, once they pause: one answer for them all, unless she was switched off meanwhile or staff are
// handling the ticket. Lines from anyone else stay in the context.
const auto = async lines => {
  const me = client.user.id, last = lines.at(-1), where = last.channelId
  if (switched.get(where)?.off) return
  const theirs = lines.filter(line => line.author.id === last.author.id)
  const rows = await ticketRows(last).catch(() => [])
  if (await handling(rows, id => isStaff(last.guild, id), Date.now(), switched.get(where)?.at, me)) return log(last.author.id, 'ticket-staff-quiet')
  const asked = together(theirs, me)
  const image = await picture(attachment(...[...theirs].reverse())).catch(() => null)
  if (!asked && !image) return log(last.author.id, 'auto-empty')
  const skip = new Set(theirs.map(line => line.id))
  await respond(last, () => ({ question: asked, history: [], context: ticketContext(last, null, rows.filter(row => !skip.has(row.id)), me), image }), 'auto-')
}

// One answer at a time per ticket, so the next one reads the last.
const busy = new Map()
const waiting = pauses(lines => {
  const where = lines[0].channelId
  const next = (busy.get(where) || Promise.resolve()).then(() => auto(lines)).catch(slipped)
  busy.set(where, next)
  next.then(() => { if (busy.get(where) === next) busy.delete(where) })
})

// !capri --off and --on (Lachy, 6 Nov): staff or whoever opened the ticket can switch her, and she says so in one line.
const flip = async (message, which) => {
  const me = client.user.id
  try {
    if (!ticket(message.channel)) {
      await say(message, ticketsOnly)
      return await log(message.author.id, 'switch-not-ticket')
    }
    if (!staff(message.member, message.guild.roles.cache.values())) {
      const start = await message.channel.messages.fetch({ limit: 5, after: '0' })
      if (message.author.id !== welcomed([...start.values()], me)?.mentions.users.first()?.id) {
        await say(message, notYours)
        return await log(message.author.id, 'ticket-switch-refused')
      }
    }
    switched.set(message.channelId, { off: which === 'off', at: message.createdTimestamp })
    keep()
    if (which === 'off') waiting.drop(message.channelId)
    await say(message, which === 'off' ? switchedOff : switchedOn)
    await log(message.author.id, 'ticket-' + which)
  } catch {
    await log(message.author.id, 'ticket-switch-failed')
  }
}

const heard = async message => {
  if (message.author.bot || message.author.id === client.user.id) return
  if (message.guildId && channels.size && !channels.has(message.channelId)) return
  const me = client.user.id, where = message.channelId
  const cached = message.reference?.messageId ? message.channel.messages.cache.get(message.reference.messageId) : null
  const maybe = trigger(message, me, cached, channels) || message.mentions.repliedUser?.id === me
  if (!ticket(message.channel)) {
    if (!maybe) return
    const replied = cached || await back(message)
    if (!trigger(message, me, replied, channels)) return
    const which = toggle(message.content, me)
    return which ? flip(message, which) : answer(message, replied)
  }
  // In a ticket every message is for her. Switched off, only --on reaches her, and a staff line drops whatever was waiting.
  const which = toggle(message.content, me), off = switched.get(where)?.off
  if (which === 'on' || (which && !off)) return flip(message, which)
  if (off) return
  const helper = staff(message.member, message.guild.roles.cache.values())
  if (helper) waiting.drop(where)
  if (maybe) {
    const replied = cached || await back(message)
    if (trigger(message, me, replied, channels)) {
      waiting.drop(where, line => line.author.id !== message.author.id)
      return answer(message, replied)
    }
  }
  if (!helper) waiting.add(where, message)
}
client.on('messageCreate', message => { heard(message).catch(slipped) })

// Someone typing in a ticket holds her answer, so she doesn't cut in before their next line lands.
client.on('typingStart', typing => { if (!typing.user?.bot) waiting.typing(typing.channel?.id) })

// A deleted ticket takes its waiting lines and its switch with it.
client.on('channelDelete', channel => {
  waiting.drop(channel.id)
  if (switched.delete(channel.id)) keep()
})

// Each new support ticket gets Lachy's greeting, a moment after Ticket Tool's own welcome (6 Nov).
client.on('channelCreate', async channel => {
  if (!ticket(channel) || (channels.size && !channels.has(channel.id))) return
  await new Promise(done => setTimeout(done, 4000))
  try {
    await channel.send({ content: greeting(client.user.id), allowedMentions: { parse: [] } })
    await log(channel.id, 'ticket-greeted')
  } catch {
    await log(channel.id, 'ticket-greet-failed')
  }
})

client.once('clientReady', () => {
  console.log('Capri is online.')
  try { appendFileSync(path.join(here, 'bot.log'), new Date().toISOString() + ' online\n') } catch {}
})
client.on('error', () => { console.error('Discord connection failed.') })
client.login(token).catch(() => { console.error('Could not connect Capri. Check DISCORD_TOKEN and Message Content Intent.'); process.exit(1) })

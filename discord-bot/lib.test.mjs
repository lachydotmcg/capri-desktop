import test from 'node:test'
import assert from 'node:assert/strict'
import { pingRole, pingGate, called } from './lib.mjs'
import { trigger, question, context, history, attachment, links, result, chunks, fallback, empty, quiet, name, greeting, ticket, ticketContext, welcomed, staff, staffLine, toggle, switchedOff, switchedOn, notYours, ticketsOnly, pauses, handling, together, switches } from './lib.mjs'

const me = '999'
const now = 1800000000000
const message = (content = '', fields = {}) => ({
  id: '100', channelId: '10', guildId: '20', content, createdTimestamp: now,
  author: { id: '123', username: 'Sam', bot: false }, ...fields
})
const answer = (id, asked, content) => message(content, { id, author: { id: me, bot: true }, reference: { messageId: asked } })
const file = (fields = {}) => ({ url: 'https://cdn.discordapp.com/attachments/10/20/picture.png?ex=123', name: 'picture.png', contentType: 'image/png', size: 500, ...fields })
const attached = (...files) => ({ attachments: new Map(files.map((entry, i) => [String(i), entry])) })

test('direct user mentions, prefix, replies and DMs trigger', () => {
  for (const text of ['<@999> voice?', '<@!999> voice?', 'voice? <@999>', '!capri voice?', '!CaPrI voice?']) assert.equal(trigger(message(text), me), true, text)
  assert.equal(trigger(message('still broken'), me, answer('90', '80', 'Try this.')), true)
  assert.equal(trigger(message('voice?', { guildId: null }), me), true)
})

test('everyone, here, roles and similar prefixes do not trigger', () => {
  for (const text of ['@everyone voice?', '@here voice?', '<@&999> voice?', '<@998> voice?', '!capricorn voice?', 'try !capri voice?']) assert.equal(trigger(message(text), me), false, text)
  assert.equal(trigger(message('still broken'), me, message('Someone else replied.')), false)
})

test('bots and Capri herself are ignored even in DMs', () => {
  assert.equal(trigger(message('!capri voice?', { author: { id: '456', bot: true } }), me), false)
  assert.equal(trigger(message('voice?', { guildId: null, author: { id: me, bot: false } }), me), false)
})

test('channel limits apply to all server triggers and allow DMs', () => {
  const channels = new Set(['11'])
  assert.equal(trigger(message('<@999> voice?'), me, null, channels), false)
  assert.equal(trigger(message('still broken'), me, answer('90', '80', 'Try this.'), channels), false)
  assert.equal(trigger(message('!capri voice?', { channelId: '11' }), me, null, channels), true)
  assert.equal(trigger(message('voice?', { guildId: null }), me, null, channels), true)
})

test('cleanup removes Capri mentions and the leading prefix only', () => {
  assert.equal(question('  <@999> !CaPrI how\n do I change <@!999> her voice? ', me), 'how do I change her voice?')
  assert.equal(question('!capri ask <@123> about !capri', me), 'ask <@123> about !capri')
  assert.equal(question('!capricorn voice?', me), '!capricorn voice?')
  assert.equal(question('!capri ' + 'x'.repeat(700), me).length, 600)
})

test('bare triggers leave no question for the worker', () => {
  for (const text of ['!capri', '!CAPRI  ', '<@999>', '<@!999> !capri <@999>']) {
    assert.equal(trigger(message(text), me), true)
    assert.equal(question(text, me), '')
  }
  assert.equal(empty, 'Ask me something after !capri.')
})

test('context includes the reply and only the askers last three recent messages', () => {
  const replied = message('Open\nSettings.', { id: '80', member: { displayName: 'Capri Support' } })
  const recent = [
    message('old fourth', { id: '81', createdTimestamp: now - 500000 }),
    message('first', { id: '82', createdTimestamp: now - 400000 }),
    message('second', { id: '83', createdTimestamp: now - 300000 }),
    message('last', { id: '84', createdTimestamp: now - 100000 }),
    message('other person', { id: '85', author: { id: '456' } }),
    message('other channel', { id: '86', channelId: '11' }),
    message('too old', { id: '87', createdTimestamp: now - 600001 }),
    message('future', { id: '88', createdTimestamp: now + 1 }),
    message('current')
  ]
  const text = context(message('question'), replied, recent)
  assert.equal(text, 'Replied to Capri Support: Open Settings.\nEarlier from Sam: first\nEarlier from Sam: second\nEarlier from Sam: last')
})

test('context uses Discord ids to order messages in the same millisecond', () => {
  const recent = ['81', '84', '82', '83'].map(id => message(id, { id, createdTimestamp: now - 1 }))
  assert.equal(context(message(), null, recent), 'Earlier from Sam: 82\nEarlier from Sam: 83\nEarlier from Sam: 84')
})

test('context bounds the referenced message and the total text', () => {
  const text = context(message(), message('r'.repeat(1000)), ['81', '82', '83'].map(id => message('s'.repeat(1000), { id })))
  assert.equal(text.split('\n')[0].length, 400)
  assert.ok(text.length <= 1200)
  assert.equal(text.split('\n').length, 4)
  assert.equal(context(message(), null), '')
})

test('context adds her own last two lines without their links, and skips the one they replied to', () => {
  const said = (id, content, ago) => message(content, { id, author: { id: me, username: 'Capri Bot', bot: true }, createdTimestamp: now - ago })
  const replied = said('70', 'Pick it under VOICE.\n\nHer voice: <https://capriai.app/help/#voice>', 50000)
  const recent = [said('71', 'Took you long enough.', 400000), said('72', 'There you are.\n\nHelp page: <https://capriai.app/help/>', 300000), said('73', 'Cute try.', 200000), replied, said('74', 'too old', 600001)]
  assert.equal(context(message('and then?'), replied, recent, me), 'Replied to Capri Bot: Pick it under VOICE.\nYou said: There you are.\nYou said: Cute try.')
  assert.equal(context(message('and then?'), null, recent), '')
})

test('a new ticket gets Lachys greeting with her own mention', () => {
  assert.equal(greeting(me), 'Hey, how can I help? Just do !capri, <@999>, or reply to my messages for assistance!')
})

test('tickets are text channels named ticket-N or sitting in the tickets category', () => {
  const channel = (fields = {}) => ({ guildId: '20', type: 0, name: 'ticket-0224', parent: { name: 'tickets' }, ...fields })
  assert.equal(ticket(channel()), true)
  assert.equal(ticket(channel({ parent: null })), true)
  assert.equal(ticket(channel({ name: 'closed-0224' })), true)
  for (const other of [channel({ name: 'general', parent: { name: '💬 COMMUNITY' } }), channel({ type: 2 }), channel({ guildId: null }), null]) assert.equal(ticket(other), false)
})

const tool = { id: '777', username: 'Ticket Tool', bot: true }, capri = { id: me, username: 'Capri Bot', bot: true }, mod = { id: '456', username: 'Mod', bot: false }
const at = (id, ago, content, author = { id: '123', username: 'Sam', bot: false }, fields = {}) => message(content, { id, author, createdTimestamp: now - ago, ...fields })
const welcome = at('60', 9000000, 'Welcome! Support will be with you shortly.', tool, { mentions: { users: { size: 1, first: () => ({ id: '123', username: 'Sam' }) }, members: { first: () => ({ displayName: 'Sam K' }) } } })
const asked = message('!capri what now?', { channel: { name: 'ticket-0224' } })

test('ticket context names the opener, starts with their first message and follows the ticket in order', () => {
  const rows = [
    at('66', 60000, 'still nothing'),
    at('65', 120000, 'Try Forget, then paste the key again.\n\nHelp page: <https://capriai.app/help/>', capri),
    welcome,
    at('61', 8990000, greeting(me), capri),
    at('62', 8000000, 'my voice stopped working after the update'),
    at('63', 7000000, 'which voice do you use?', mod),
    at('64', 6000000, 'fish audio'),
    at('67', 1000, 'other channel', undefined, { channelId: '11' }),
    at('68', -1000, 'future'),
    at('64', 6000000, 'fish audio')
  ]
  assert.equal(ticketContext(asked, null, rows, me), [
    'This is support ticket #ticket-0224, opened by Sam K.',
    'Sam opened with: my voice stopped working after the update',
    'Mod: which voice do you use?',
    'Sam: fish audio',
    'You said: Try Forget, then paste the key again.',
    'Sam: still nothing'
  ].join('\n'))
})

test('ticket context keeps the opening and the newest lines inside 1200 characters', () => {
  const rows = [welcome, at('62', 8000000, 'o'.repeat(1000))]
  for (let i = 0; i < 20; i++) rows.push(at(String(70 + i), 5000000 - i * 1000, 'line ' + i + ' ' + 'x'.repeat(200), mod))
  const text = ticketContext(asked, null, rows, me)
  const lines = text.split('\n')
  assert.ok(text.length <= 1200)
  assert.equal(lines[1].length, 400)
  assert.ok(lines[1].startsWith('Sam opened with: ooo'))
  assert.ok(lines.at(-1).startsWith('Mod: line 19 '))
  assert.ok(!text.includes('line 0 '))
})

test('ticket context adds a replied message from outside the ticket rows, but never her greeting', () => {
  const old = at('50', 9500000, 'Pick your mic under VOICE.', capri)
  assert.equal(ticketContext(asked, old, [welcome], me), 'This is support ticket #ticket-0224, opened by Sam K.\nReplied to Capri Bot: Pick your mic under VOICE.')
  assert.equal(ticketContext(asked, at('61', 8990000, greeting(me), capri), [welcome], me), 'This is support ticket #ticket-0224, opened by Sam K.')
  assert.equal(ticketContext(message('hi', { channel: { name: 'ticket-0001' } }), null, [], me), 'This is support ticket #ticket-0001.')
})

test('the welcome is the oldest bot message with a mention that is not hers', () => {
  const hers = at('61', 8990000, greeting(me), capri, { mentions: { users: { size: 1 } } })
  assert.equal(welcomed([hers, at('59', 9100000, 'no mention', tool), welcome], me), welcome)
  assert.equal(welcomed([hers, at('62', 8000000, '<@999> hi', undefined, { mentions: { users: { size: 1 } } })], me), null)
})

const role = (name, position) => ({ id: String(500 + position), name, position })
const ranks = [role('Admin', 89), role('🛡️ Head Moderator', 79), role('Moderator', 72), role('Ticket Tool', 71), role('Helpers ✨', 67), role('Helper Pings', 5), role('Supporter', 4), role('@everyone', 0)]
const member = (position, granted = [], fields = {}) => ({ user: { bot: false }, roles: { highest: { position } }, permissions: { has: bit => granted.includes(bit) }, ...fields })
const administrator = 8n, manage = 8192n

test('staff have a highest role at or above Helper, or Administrator or Manage Messages', () => {
  assert.equal(staffLine(ranks).position, 67)
  for (const position of [67, 72, 89]) assert.equal(staff(member(position), ranks), true, String(position))
  for (const position of [66, 5, 0]) assert.equal(staff(member(position), ranks), false, String(position))
  assert.equal(staff(member(0, [administrator]), ranks), true)
  assert.equal(staff(member(0, [manage]), ranks), true)
  assert.equal(staff(member(0, [1024n]), ranks), false)
  assert.equal(staff(member(89, [], { user: { bot: true } }), ranks), false)
  assert.equal(staff(null, ranks), false)
  // The bot passes the server's role cache as an iterator.
  assert.equal(staff(member(70), new Map(ranks.map(entry => [entry.id, entry])).values()), true)
  assert.equal(staff(member(50), new Map(ranks.map(entry => [entry.id, entry])).values()), false)
})

test('staff roles are found by name whatever the case or decoration, and without Helper the lowest staff role is the line', () => {
  assert.equal(staffLine([role('𝐇𝐄𝐋𝐏𝐄𝐑', 9), role('Trial-Mod', 12)]).position, 9)
  assert.equal(staffLine([role('Admin', 30), role('MODERATOR 🔨', 20), role('Helper Ping', 2), role('Capri AI Helper', 1)]).position, 20)
  assert.equal(staffLine([role('━━ Staff ━━', 40), role('Hélper', 8)]).position, 8)
  assert.equal(staffLine([role('Supporter', 3), role('Ex Mod', 2), role('Modder', 1)]), null)
  assert.equal(staff(member(50), [role('Members', 1)]), false)
  assert.equal(staff(member(0, [manage]), []), true)
})

test('--off and --on switch her after !capri, after an @mention of her, or on their own', () => {
  for (const [text, which] of [['!capri --off', 'off'], ['!CAPRI   --ON', 'on'], ['<@999> --off', 'off'], ['<@!999> --on', 'on'], ['--off <@999>', 'off'], ['--off', 'off'], [' --On ', 'on']]) assert.equal(toggle(text, me), which, text)
  for (const text of ['!capri --offline', '!capri off', '!capri --off please', '!capri -off', '!capri', '', 'turn --off', '!capricorn --off', '<@998> --off', '!capri --on --off']) assert.equal(toggle(text, me), null, text)
})

test('she confirms a switch in one short line, and turns down anyone but staff and the opener', () => {
  for (const line of [switchedOff, switchedOn, notYours, ticketsOnly]) assert.ok(line.length <= 60 && !line.includes('\n'), line)
  assert.ok(switchedOff.includes('!capri --on'))
  assert.equal(notYours, 'Only staff or whoever opened this ticket can do that.')
})

// A clock and timers that only move when the test says so.
const clockwork = () => {
  let now = 0, count = 0
  const timers = new Map()
  return {
    clock: () => now,
    later: (run, ms) => { timers.set(++count, { run, at: now + ms }); return count },
    stop: id => { timers.delete(id) },
    pass: ms => {
      const end = now + ms
      for (let next; (next = [...timers].filter(([, timer]) => timer.at <= end).sort((a, b) => a[1].at - b[1].at)[0]);) {
        timers.delete(next[0])
        now = next[1].at
        next[1].run()
      }
      now = end
    }
  }
}
const line = (id, author = '123') => ({ id, author: { id: author } })

test('lines sent in a row wait for an 8 second pause and get one answer', () => {
  const time = clockwork(), got = []
  const waiting = pauses(lines => got.push([time.clock(), lines.map(entry => entry.id)]), time)
  waiting.add('t1', line('a')); time.pass(5000)
  waiting.add('t1', line('b')); time.pass(7000)
  waiting.add('t1', line('c')); time.pass(7999)
  assert.deepEqual(got, [])
  assert.equal(waiting.count('t1'), 3)
  time.pass(1)
  assert.deepEqual(got, [[20000, ['a', 'b', 'c']]])
  assert.equal(waiting.count('t1'), 0)
  waiting.add('t1', line('d')); time.pass(9000)
  assert.deepEqual(got.at(-1), [28000, ['d']])
})

test('typing holds the answer 10 seconds at a time, never past a minute after the last line', () => {
  const time = clockwork(), got = []
  const waiting = pauses(() => got.push(time.clock()), time)
  waiting.typing('t1')
  waiting.add('t1', line('a'))
  time.pass(6000); waiting.typing('t1')
  time.pass(9000)
  assert.deepEqual(got, [])
  time.pass(1000)
  assert.deepEqual(got, [16000])
  waiting.add('t1', line('b'))
  for (let i = 0; i < 9; i++) { time.pass(7000); waiting.typing('t1') }
  assert.deepEqual(got, [16000, 76000])
  waiting.typing('t2')
  time.pass(60000)
  assert.equal(got.length, 2)
})

test('a staff line drops everything waiting in that ticket, and a direct ask drops only the askers lines', () => {
  const time = clockwork(), got = []
  const waiting = pauses(lines => got.push(lines.map(entry => entry.id)), time)
  waiting.add('t1', line('a')); waiting.add('t1', line('b', '124')); waiting.add('t2', line('c'))
  waiting.drop('t1', entry => entry.author.id !== '123')
  time.pass(8000)
  assert.deepEqual(got, [['b'], ['c']])
  waiting.add('t1', line('d')); waiting.add('t1', line('e', '124'))
  waiting.drop('t1')
  waiting.drop('t3')
  time.pass(60000)
  assert.equal(got.length, 2)
})

test('lines from one pause make one question, oldest first, without her mention or the prefix', () => {
  const lines = ['her voice cut out', '', 'it started after the update', '<@999> it happens every time', '!capri version 0.8.4'].map((content, i) => message(content, { id: String(i) }))
  assert.equal(together(lines, me), 'her voice cut out / it started after the update / it happens every time / version 0.8.4')
  assert.equal(together([message('x'.repeat(500)), message('y'.repeat(500))], me).length, 600)
  assert.equal(together([message(''), message('<@999>')], me), '')
})

test('she keeps quiet while staff have posted in the last half hour, and bots, her greeting and switch lines never count', async () => {
  const isStaff = id => id === mod.id
  const rows = [welcome, at('61', 8990000, greeting(me), capri), at('62', 500, 'my voice broke'), at('63', 1000, 'ticket tool says hi', tool)]
  assert.equal(await handling(rows, isStaff, now, 0, me), false)
  assert.equal(await handling([...rows, at('64', 1799000, 'let me look', mod)], isStaff, now, 0, me), true)
  assert.equal(await handling([...rows, at('64', 1800000, 'let me look', mod)], isStaff, now, 0, me), false)
  assert.equal(await handling([...rows, at('64', 1000, '!capri --on', mod), at('65', 900, '--off', mod)], isStaff, now, 0, me), false)
  assert.equal(await handling(rows, () => true, now, 0, me), true)
  assert.equal(await handling([at('66', 1000, 'welcome back', tool), at('67', 900, 'hey', capri)], () => true, now, 0, me), false)
})

test('an --on lets her talk again even if staff posted before it, and each person is checked once', async () => {
  const later = [at('64', 60000, 'all yours, Capri', mod), at('65', 50000, '!capri --on', mod), at('66', 1000, 'still broken')]
  assert.equal(await handling(later, async id => id === mod.id, now, now - 50000, me), false)
  assert.equal(await handling([...later, at('67', 500, 'one more thing', mod)], async id => id === mod.id, now, now - 50000, me), true)
  let checked = 0
  await handling([at('70', 3000, 'a'), at('71', 2000, 'b'), at('72', 1000, 'c')], () => { checked++; return false }, now, 0, me)
  assert.equal(checked, 1)
})

test('ticket switches come back from JSON after a restart, without malformed entries or a stale --on', () => {
  const saved = { 111: { off: true, at: now - 9000000 }, 222: { off: false, at: now - 60000 }, 333: { off: false, at: now - 1800001 }, abc: { off: true, at: now }, 444: { off: 'yes', at: now }, 555: { off: true }, 666: null }
  assert.deepEqual([...switches(JSON.stringify(saved), now)], [['111', { off: true, at: now - 9000000 }], ['222', { off: false, at: now - 60000 }]])
  for (const text of ['', 'not json', '[]', 'null', '42', '"text"']) assert.equal(switches(text, now).size, 0, text)
})

test('history follows three complete pairs in conversation order', () => {
  const chain = []
  for (let i = 4; i > 0; i--) {
    chain.push(answer(String(i * 10 + 1), String(i * 10), 'answer ' + i + '\n\nHelp page: <https://capriai.app/help/>'))
    chain.push(message('!capri question ' + i, { id: String(i * 10), reference: { messageId: String((i - 1) * 10 + 1) } }))
  }
  assert.deepEqual(history(chain, me), [
    { role: 'user', text: 'Sam: question 2' }, { role: 'assistant', text: 'answer 2' },
    { role: 'user', text: 'Sam: question 3' }, { role: 'assistant', text: 'answer 3' },
    { role: 'user', text: 'Sam: question 4' }, { role: 'assistant', text: 'answer 4' }
  ])
})

test('history stops at missing, unrelated or bot-authored questions', () => {
  const said = answer('91', '90', 'Try Settings.')
  assert.deepEqual(history([said], me), [])
  assert.deepEqual(history([said, message('voice?', { id: '89' })], me), [])
  assert.deepEqual(history([said, message('voice?', { id: '90', author: { id: '456', bot: true } })], me), [])
  const asked = message('!capri voice?', { id: '90' })
  assert.deepEqual(history([said, asked, answer('81', '80', 'Earlier.'), message('earlier?', { id: '80' })], me), [
    { role: 'user', text: 'Sam: voice?' }, { role: 'assistant', text: 'Try Settings.' }
  ])
})

test('history clips turns and keeps other human questions in the reply chain', () => {
  const said = answer('91', '90', 'a'.repeat(700))
  const asked = message('<@999> ' + 'q'.repeat(700), { id: '90', author: { id: '456', bot: false } })
  assert.deepEqual(history([said, asked], me), [{ role: 'user', text: 'Someone: ' + 'q'.repeat(491) }, { role: 'assistant', text: 'a'.repeat(500) }])
})

test('her replies never carry a working @everyone or @here', () => {
  const said = result(200, { reply: 'Pinging @everyone is a no-go, and @HERE too.', answered: true, links: [] }).text
  assert.equal(said, 'Pinging @​everyone is a no-go, and @​HERE too.')
  assert.equal(quiet('mail me@heresy.com'), 'mail me@heresy.com')
  assert.equal(name(message('hi', { member: { displayName: 'Talia  Yang' } })), 'Talia Yang')
})

test('attachments prefer the current message then the referenced message', () => {
  const current = file(), replied = file({ url: 'https://media.discordapp.net/attachments/10/20/other.webp', name: 'other.webp', contentType: 'image/webp' })
  assert.deepEqual(attachment(attached(current), attached(replied)), { url: current.url, type: 'image/png' })
  assert.deepEqual(attachment(attached(), attached(replied)), { url: replied.url, type: 'image/webp' })
})

test('attachments reject other hosts, redirects by URL, credentials and oversized files', () => {
  for (const url of ['https://example.com/picture.png', 'https://cdn.discordapp.com.example.com/picture.png', 'http://cdn.discordapp.com/picture.png', 'https://user:pass@cdn.discordapp.com/picture.png', 'https://cdn.discordapp.com:444/picture.png', 'not a url']) assert.equal(attachment(attached(file({ url }))), null, url)
  for (const size of [0, -1, 1048576, 1048577, NaN, undefined]) assert.equal(attachment(attached(file({ size }))), null)
  assert.ok(attachment(attached(file({ size: 1048575 }))))
})

test('attachments accept supported MIME types and infer JPEG when MIME is absent', () => {
  assert.equal(attachment(attached(file({ name: 'shot.JPEG', contentType: undefined }))).type, 'image/jpeg')
  assert.equal(attachment(attached(file({ name: 'shot.jpg', contentType: undefined }))).type, 'image/jpeg')
  assert.equal(attachment(attached(file({ contentType: 'image/webp; charset=binary' }))).type, 'image/webp')
  for (const contentType of ['image/gif', 'image/svg+xml', 'text/html']) assert.equal(attachment(attached(file({ contentType }))), null)
  assert.equal(attachment(attached(file({ name: 'shot.gif', contentType: undefined }))), null)
})

test('attachments can skip an invalid file and use the first eligible picture', () => {
  const valid = file({ url: 'https://media.discordapp.net/attachments/10/20/valid.png' })
  assert.deepEqual(attachment(attached(file({ size: 1048576 }), valid, file())), { url: valid.url, type: 'image/png' })
  assert.equal(attachment(null, null), null)
})

test('link lines suppress previews and reject unsafe or malformed links', () => {
  assert.equal(links([
    { title: ' Help\n page ', href: 'https://capriai.app/help/' },
    { title: 'Duplicate', href: 'https://capriai.app/help/' },
    { title: 'Discord', href: 'https://discord.gg/F4fDHfGtFk' },
    { title: 'bad', href: 'javascript:alert(1)' },
    { title: 'bad', href: 'https://user:pass@example.com/' },
    { title: '', href: 'https://example.com/' }, null, { title: 'missing' }
  ]), 'Help page: <https://capriai.app/help/>\nDiscord: <https://discord.gg/F4fDHfGtFk>')
  assert.equal(links(null), '')
})

test('successful replies put link lines below the answer', () => {
  assert.deepEqual(result(200, { reply: ' Open Settings. ', answered: true, links: [{ title: 'Voice', href: 'https://capriai.app/help/#voice' }] }), {
    text: 'Open Settings.\n\nVoice: <https://capriai.app/help/#voice>', why: 'answered'
  })
})

test('429 replies are shown as written even without answered', () => {
  const reply = "That's a lot of pictures for one day. Type out what yours says and I'll help."
  assert.deepEqual(result(429, { reply, links: [] }), { text: reply, why: 'limit' })
  assert.deepEqual(result(429, { reply, why: 'allowance' }), { text: reply, why: 'allowance' })
})

test('the shared model allowance reply is retained', () => {
  assert.deepEqual(result(200, { reply: 'Back after midnight UTC.', why: 'allowance' }), { text: 'Back after midnight UTC.', why: 'allowance' })
})

test('malformed or failed worker replies use the exact fallback', () => {
  for (const body of [null, {}, [], { reply: '' }, { reply: '   ', answered: true }, { reply: 42, answered: true }, { reply: 'missing status' }]) assert.equal(result(200, body).text, fallback)
  assert.deepEqual(result(503, { reply: "Discord help isn't set up yet." }), { text: fallback, why: 'http-503' })
  assert.deepEqual(result(200, { reply: 'glitching', why: 'claude-error' }), { text: fallback, why: 'claude-error' })
  assert.equal(result(200, { why: 'private text\nextra log line' }).why, 'bad-reply')
  assert.equal(fallback, "My help line's glitching right now. The Help page has everything: <https://capriai.app/help/>")
})

test('chunks preserve text and keep Discord messages at 1900 characters', () => {
  const text = 'a'.repeat(1901) + '\n' + 'b'.repeat(4000)
  const parts = chunks(text)
  assert.equal(parts.join(''), text)
  assert.ok(parts.every(part => part.length > 0 && part.length <= 1900))
  assert.equal(chunks('x'.repeat(1900)).length, 1)
  assert.deepEqual(chunks(''), [])
})

test('chunks keep link lines together at line boundaries', () => {
  const text = 'a'.repeat(1880) + '\n\nHelp page: <https://capriai.app/help/>'
  const parts = chunks(text)
  assert.equal(parts.join(''), text)
  assert.equal(parts.at(-1), 'Help page: <https://capriai.app/help/>')
})

test('chunks do not split a Unicode surrogate pair', () => {
  const text = 'a'.repeat(1899) + '\u{1D11E}' + 'b'.repeat(1900)
  const parts = chunks(text)
  assert.equal(parts.join(''), text)
  assert.ok(parts.every(part => !/[\uD800-\uDBFF]$/.test(part) && !/^[\uDC00-\uDFFF]/.test(part)))
  assert.throws(() => chunks('abc', 1), RangeError)
})

test('she only rings a plain Helper, Moderator or Developer role', () => {
  const roles = [role('Helper Ping', 2), role('Modder', 3), role('Helper', 5), role('𝐌𝐎𝐃𝐄𝐑𝐀𝐓𝐎𝐑', 72), role('🛡️ Head Moderator', 79), role('👑 Developer', 90)]
  assert.equal(pingRole('helper', roles).position, 5)
  assert.equal(pingRole('moderator', roles.values()).position, 72)
  assert.equal(pingRole('developer', roles).position, 90)
  assert.equal(pingRole('everyone', roles), null)
  assert.equal(pingRole('helper', [role('Members', 1)]), null)
  assert.equal(called('developer', role('Developer', 90)), '<@&590> this looks like a real bug.')
})

test('one ping per role per channel each half hour, and per person per role each hour', () => {
  let at = 0
  const gate = pingGate({ clock: () => at })
  assert.equal(gate('helper', 'c1', 'u1'), true)
  assert.equal(gate('helper', 'c1', 'u2'), false)
  assert.equal(gate('moderator', 'c1', 'u1'), true)
  assert.equal(gate('helper', 'c2', 'u1'), false)
  assert.equal(gate('helper', 'c2', 'u2'), true)
  at = 1800000
  assert.equal(gate('helper', 'c1', 'u3'), true)
  assert.equal(gate('helper', 'c3', 'u1'), false)
  at = 3600000
  assert.equal(gate('helper', 'c3', 'u1'), true)
})

test('a ping only comes from an answered reply, and only for a known role', () => {
  assert.equal(result(200, { reply: 'Getting a human.', answered: true, ping: 'helper' }).ping, 'helper')
  assert.equal(result(200, { reply: 'Hm.', answered: true, ping: 'everyone' }).ping, undefined)
  assert.equal(result(200, { reply: 'Hm.', answered: true, ping: 'constructor' }).ping, undefined)
  assert.equal(result(200, { reply: 'Hm.', answered: true }).ping, undefined)
  assert.equal(result(429, { reply: 'Slow down.', why: 'limit', ping: 'helper' }).ping, undefined)
})

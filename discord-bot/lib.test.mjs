import test from 'node:test'
import assert from 'node:assert/strict'
import { trigger, question, context, history, attachment, links, result, chunks, fallback, empty } from './lib.mjs'

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

test('history follows three complete pairs in conversation order', () => {
  const chain = []
  for (let i = 4; i > 0; i--) {
    chain.push(answer(String(i * 10 + 1), String(i * 10), 'answer ' + i + '\n\nHelp page: <https://capriai.app/help/>'))
    chain.push(message('!capri question ' + i, { id: String(i * 10), reference: { messageId: String((i - 1) * 10 + 1) } }))
  }
  assert.deepEqual(history(chain, me), [
    { role: 'user', text: 'question 2' }, { role: 'assistant', text: 'answer 2' },
    { role: 'user', text: 'question 3' }, { role: 'assistant', text: 'answer 3' },
    { role: 'user', text: 'question 4' }, { role: 'assistant', text: 'answer 4' }
  ])
})

test('history stops at missing, unrelated or bot-authored questions', () => {
  const said = answer('91', '90', 'Try Settings.')
  assert.deepEqual(history([said], me), [])
  assert.deepEqual(history([said, message('voice?', { id: '89' })], me), [])
  assert.deepEqual(history([said, message('voice?', { id: '90', author: { id: '456', bot: true } })], me), [])
  const asked = message('!capri voice?', { id: '90' })
  assert.deepEqual(history([said, asked, answer('81', '80', 'Earlier.'), message('earlier?', { id: '80' })], me), [
    { role: 'user', text: 'voice?' }, { role: 'assistant', text: 'Try Settings.' }
  ])
})

test('history clips turns and keeps other human questions in the reply chain', () => {
  const said = answer('91', '90', 'a'.repeat(700))
  const asked = message('<@999> ' + 'q'.repeat(700), { id: '90', author: { id: '456', bot: false } })
  assert.deepEqual(history([said, asked], me), [{ role: 'user', text: 'q'.repeat(500) }, { role: 'assistant', text: 'a'.repeat(500) }])
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

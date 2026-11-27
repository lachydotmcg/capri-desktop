import assert from 'node:assert/strict';
import { EventEmitter } from 'node:events';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { setImmediate as tick } from 'node:timers/promises';
import { ChannelType, Events, PermissionFlagsBits } from 'discord.js';
import { VoiceConnectionStatus } from '@discordjs/voice';
import { test } from 'node:test';
import { run } from './companion.mjs';

const app = '100000000000000001';
const otherApp = '100000000000000002';
const owner = '200000000000000001';
const guest = '200000000000000002';
const bot = '200000000000000003';
const server = '300000000000000001';
const otherServer = '300000000000000002';
const firstRole = '400000000000000001';
const secondRole = '400000000000000002';

function gate() {
  let release;
  const wait = new Promise(resolve => { release = resolve; });
  return { release, wait };
}

async function fixture(home, appId = app) {
  const client = new EventEmitter();
  const input = new EventEmitter();
  const user = { id: bot, username: 'Capri', displayAvatarURL: () => 'about:blank' };
  let registration;
  Object.assign(client, {
    user, login: async () => {},
    isReady: () => true, destroy: async () => {},
    application: { fetch: async () => ({
      id: appId, owner: { id: owner }, commands: { create: async command => { registration = command.toJSON(); } },
    }) },
    guilds: { cache: new Map() },
  });
  input.close = () => {};
  const guild = id => ({ id, name: 'Server', voiceStates: { cache: new Map() } });
  const here = guild(server);
  const there = guild(otherServer);
  client.guilds.cache.set(here.id, here);
  client.guilds.cache.set(there.id, there);
  const notices = [];
  const route = [];
  const ready = gate();
  ready.release();
  let readiness = ready.wait;
  let posting;
  const voice = (guild, id) => ({
    guild, id, name: 'Testing', type: ChannelType.GuildVoice, members: new Map(),
    permissionsFor: () => ({ has: () => true }),
    send: async notice => { notices.push(notice); await posting; },
  });
  const selected = voice(here, '500000000000000001');
  const elsewhere = voice(here, '500000000000000002');
  const remote = voice(there, '500000000000000003');
  function move(id, stateGuild, channel) {
    const before = stateGuild.voiceStates.cache.get(id) ?? { id, guild: stateGuild };
    const after = { id, guild: stateGuild, channel, channelId: channel?.id };
    stateGuild.voiceStates.cache.set(id, after);
    client.emit(Events.VoiceStateUpdate, before, after);
  }
  function info(status, extra = {}) {
    return Object.assign(new EventEmitter(), { state: { status }, ...extra });
  }
  run(client, input, {
    joinVoiceChannel(options) {
      const connection = info(VoiceConnectionStatus.Ready, {
        destroy() { this.state.status = VoiceConnectionStatus.Destroyed; this.emit(VoiceConnectionStatus.Destroyed); },
        subscribe() {}, receiver: { speaking: new EventEmitter() },
      });
      route.push({ options, connection });
      return connection;
    },
    createAudioPlayer: () => info('idle', { stop() {} }),
    entersState: () => readiness,
  });
  input.emit('line', JSON.stringify({ type: 'start', home }));
  await tick();

  async function command(name, { id = owner, roleId, guild = here, roles = [], manage = true } = {}) {
    let reply;
    await client.listeners(Events.InteractionCreate)[0]({
      commandName: 'capri', guildId: guild.id, guild, user: { id }, member: { roles },
      memberPermissions: { has: permission => manage && permission === PermissionFlagsBits.ManageGuild },
      isButton: () => false, isChatInputCommand: () => true, inGuild: () => true,
      options: { getSubcommand: () => name, getRole: () => roleId && { id: roleId } },
      reply: async result => { reply = result.content; },
      deferReply: async () => {}, editReply: async result => { reply = result; },
    });
    return reply;
  }

  return {
    client, command, here, there, move, selected, elsewhere, remote, notices, route, registration,
    holdReady() { const pause = gate(); readiness = pause.wait; return pause.release; },
    holdNotice() { const pause = gate(); posting = pause.wait; return pause.release; },
  };
}

test('roles are opt-in, scoped to the bot and server, and survive reconnects', async () => {
  const home = mkdtempSync(join(tmpdir(), 'capri-summon-role-'));
  try {
    const api = await fixture(home);
    const config = api.registration.options.find(option => option.name === 'summon-role');
    assert.equal(config.options[0].type, 8);
    assert.equal(config.options[0].required, false);
    const guestHere = { id: guest, roles: [firstRole] };
    api.move(owner, api.here, api.elsewhere);
    api.move(guest, api.here, api.selected);
    assert.match(await api.command('join', guestHere), /bot owner or/);
    assert.match(await api.command('summon-role', { ...guestHere, roleId: firstRole }), /Only the bot owner/);
    assert.match(await api.command('summon-role', { roleId: firstRole, manage: false }), /Manage Server/);
    assert.match(await api.command('summon-role', { roleId: server }), /other than @everyone/);
    assert.match(await api.command('summon-role', { roleId: firstRole }), /can now use/);
    assert.match(await api.command('ask', guestHere), /Only the bot owner/);
    assert.match(await api.command('leave', guestHere), /Only the bot owner/);
    api.move(guest, api.there, api.remote);
    assert.match(await api.command('join', { ...guestHere, guild: api.there }), /bot owner or/);
    const separate = await fixture(home, otherApp);
    separate.move(guest, separate.here, separate.selected);
    assert.match(await separate.command('join', guestHere), /bot owner or/);
    const restarted = await fixture(home);
    restarted.move(guest, restarted.here, restarted.selected);
    assert.match(await restarted.command('join', { id: guest, roles: { cache: new Set([firstRole]) } }), /joined/);
    assert.equal(restarted.route[0].options.channelId, restarted.selected.id);
    assert.match(restarted.notices[0].content, /person who invited her/);
    assert.match(await restarted.command('leave', { guild: restarted.there }), /no active voice session/);
    assert.match(await restarted.command('leave'), /left the voice channel/);
    assert.match(await restarted.command('summon-role'), /Only the bot owner/);
    assert.match(await api.command('join', guestHere), /bot owner or/);
    writeFileSync(join(home, 'summon-roles', app + '-' + server + '.json'), '{"roleId":');
    assert.match(await api.command('join', guestHere), /bot owner or/);
    assert.match(await api.command('join'), /joined/);
    assert.equal(api.route[0].options.channelId, api.elsewhere.id);
    assert.match(await api.command('leave'), /left the voice channel/);
  } finally { rmSync(home, { recursive: true, force: true }); }
});

test('voice presence and role configuration are checked across both startup awaits', async () => {
  const home = mkdtempSync(join(tmpdir(), 'capri-summon-role-races-'));
  try {
    const api = await fixture(home);
    const guestHere = { id: guest, roles: [firstRole] };
    await api.command('summon-role', { roleId: firstRole });
    api.move(guest, api.here, api.selected);
    const ready = api.holdReady();
    const first = api.command('join', guestHere);
    await tick();
    api.move(guest, api.here, api.elsewhere);
    ready();
    assert.doesNotMatch(await first, /I joined/);
    assert.equal(api.notices.length, 0);

    api.move(guest, api.here, api.selected);
    let post = api.holdNotice();
    const second = api.command('join', guestHere);
    await tick();
    assert.equal(api.notices.length, 1);
    await api.command('summon-role', { roleId: secondRole });
    post();
    assert.doesNotMatch(await second, /I joined/);
    assert.equal(api.route.at(-1).connection.receiver.speaking.listenerCount('start'), 0);

    await api.command('summon-role', { roleId: firstRole });
    post = api.holdNotice();
    const third = api.command('join', guestHere);
    await tick();
    api.move(guest, api.here);
    post();
    assert.doesNotMatch(await third, /I joined/);

    api.move(owner, api.here, api.selected);
    api.move(guest, api.here, api.selected);
    assert.match(await api.command('join', guestHere), /joined/);
    let connection = api.route.at(-1).connection;
    api.move(owner, api.here);
    assert.equal(connection.state.status, VoiceConnectionStatus.Ready);
    await api.command('summon-role', { roleId: secondRole });
    assert.equal(connection.state.status, VoiceConnectionStatus.Destroyed);

    await api.command('summon-role', { roleId: firstRole });
    assert.match(await api.command('join', guestHere), /joined/);
    connection = api.route.at(-1).connection;
    api.move(bot, api.here, api.selected);
    api.move(bot, api.here);
    assert.equal(connection.state.status, VoiceConnectionStatus.Destroyed);

    assert.match(await api.command('join', guestHere), /joined/);
    connection = api.route.at(-1).connection;
    api.client.emit(Events.GuildRoleDelete, { id: firstRole, guild: api.here });
    assert.equal(connection.state.status, VoiceConnectionStatus.Destroyed);

    api.move(owner, api.here, api.selected);
    assert.match(await api.command('join'), /joined/);
    connection = api.route.at(-1).connection;
    await api.command('summon-role');
    assert.equal(connection.state.status, VoiceConnectionStatus.Ready);
    api.move(owner, api.here);
    assert.equal(connection.state.status, VoiceConnectionStatus.Destroyed);
  } finally { rmSync(home, { recursive: true, force: true }); }
});

test('a missing or unavailable stable home fails closed', async () => {
  const home = mkdtempSync(join(tmpdir(), 'capri-summon-role-unavailable-'));
  try {
    writeFileSync(join(home, 'summon-roles'), '');
    for (const directory of [undefined, 'relative', home]) {
      const api = await fixture(directory);
      assert.match(await api.command('summon-role', { roleId: firstRole }), /couldn’t save/);
      assert.match(await api.command('join', { id: guest, roles: [firstRole] }), /bot owner or/);
    }
  } finally { rmSync(home, { recursive: true, force: true }); }
});

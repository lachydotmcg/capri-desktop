import { createInterface } from 'node:readline';
import { once } from 'node:events';
import { Readable } from 'node:stream';
import { randomUUID } from 'node:crypto';
import {
  ApplicationIntegrationType, ChannelType, Client, Events, GatewayIntentBits, InteractionContextType,
  MessageFlags, PermissionFlagsBits, SlashCommandBuilder,
} from 'discord.js';
import {
  AudioPlayerStatus, EndBehaviorType, StreamType, VoiceConnectionStatus, createAudioPlayer,
  createAudioResource, entersState, joinVoiceChannel,
} from '@discordjs/voice';
import Opus from 'opusscript';

const speechRate = 16_000;
const speechBytesPerMs = speechRate * 2 / 1000;
const maxSpeechBytes = speechRate * 2 * 12;
const speechEndPause = 900;
const maxQueuedTurns = 3;
const maxTurnAge = 12_000;
const noMentions = { parse: [] };
const avatarImage = { extension: 'png', forceStatic: true, size: 128 };
const required = [
  PermissionFlagsBits.ViewChannel, PermissionFlagsBits.Connect,
  PermissionFlagsBits.Speak, PermissionFlagsBits.SendMessages,
];

const command = new SlashCommandBuilder()
  .setName('capri').setDescription('Talk to the Capri companion running on your PC')
  .setContexts(InteractionContextType.Guild)
  .setIntegrationTypes(ApplicationIntegrationType.GuildInstall)
  .addSubcommand(option => option.setName('join').setDescription('Bring Capri into your current voice channel'))
  .addSubcommand(option => option.setName('leave').setDescription('End Capri’s voice session'))
  .addSubcommand(option => option.setName('ask').setDescription('Ask Capri a question in private')
    .addStringOption(words => words.setName('message').setDescription('What to say to Capri').setRequired(true).setMaxLength(1000)));

if (process.argv.includes('--check')) {
  const encoder = new Opus(48_000, 2, Opus.Application.VOIP);
  const decoder = new Opus(speechRate, 1, Opus.Application.VOIP);
  try {
    const packet = encoder.encode(Buffer.alloc(960 * 4), 960);
    if (decoder.decode(packet).length !== 20 * speechBytesPerMs) throw new Error('Opus speech decoding failed');
  } finally {
    decoder.delete();
    encoder.delete();
  }
} else {
  run();
}

function run() {
  const client = new Client({
    intents: [GatewayIntentBits.Guilds, GatewayIntentBits.GuildVoiceStates],
    allowedMentions: noMentions,
    rest: { timeout: 12_000, retries: 1 },
  });
  const input = createInterface({ input: process.stdin, terminal: false });
  let ownerId;
  let call;
  let pending;
  let starting = false;
  let closed = false;
  let available = false;

  function tell(message) {
    if (!closed) process.stdout.write(JSON.stringify(message) + '\n');
  }

  function finishTurn(answer) {
    if (!pending) return;
    const turn = pending;
    pending = undefined;
    clearTimeout(turn.timeout);
    turn.resolve(answer);
    void drain(call);
  }

  function leave(room) {
    if (!room || call !== room) return;
    call = undefined;
    if (pending?.room === room) finishTurn(null);
    room.turns.length = 0;
    room.greet = false;
    room.nextAudio = null;
    for (const stream of room.streams.values()) stream.destroy();
    room.player.stop(true);
    if (room.connection.state.status !== VoiceConnectionStatus.Destroyed) room.connection.destroy();
    tell({ type: 'left', session: room.id });
  }

  function stop(code = 0) {
    if (closed) return;
    closed = true;
    leave(call);
    finishTurn(null);
    input.close();
    client.destroy().catch(() => {});
    process.exitCode = code;
    process.stdin.unref?.();
  }

  function turn(room, sender, fields) {
    if (pending || closed) return Promise.resolve(null);
    const id = randomUUID();
    return new Promise(resolve => {
      const timeout = setTimeout(() => finishTurn(null), 100_000);
      pending = { id, room, resolve, timeout };
      tell({ type: 'turn', id, session: room?.id ?? '', ...sender, ...fields });
    });
  }

  async function ready() {
    const application = await client.application.fetch();
    ownerId = application.owner?.ownerId ?? application.owner?.id;
    if (!ownerId) throw new Error('Discord application has no owner');
    if (closed) return;
    await application.commands.create(command);
    tell({ type: 'ready', clientId: application.id, name: client.user.username, avatar: client.user.displayAvatarURL(avatarImage) });
    available = true;
  }

  async function answer(interaction) {
    const action = interaction.options.getSubcommand();
    if (action === 'leave') {
      if (call?.voice.guild.id !== interaction.guildId) {
        await interaction.reply({ content: 'Capri has no active voice session here.', flags: MessageFlags.Ephemeral });
        return;
      }
      leave(call);
      await interaction.reply({ content: 'Capri left the voice channel.', flags: MessageFlags.Ephemeral });
      return;
    }
    if (action === 'join') {
      await interaction.deferReply({ flags: MessageFlags.Ephemeral });
      const result = await join(interaction.guild?.voiceStates.cache.get(ownerId)?.channel);
      await interaction.editReply(result.text);
      return;
    }

    await interaction.deferReply({ flags: MessageFlags.Ephemeral });
    if (pending) {
      await interaction.editReply('Capri is in the middle of another reply.');
      return;
    }
    const result = await turn(undefined, { name: interaction.user.displayName, speaker: 1 },
      { text: interaction.options.getString('message', true) });
    await interaction.editReply({ content: result?.text?.slice(0, 1900) || 'Capri did not get a reply from her selected model.' });
  }

  function roomInfo(room) {
    const voice = room.voice;
    const members = room.connected ? voice.members : null;
    const participants = [];
    function person(member, bot = false) {
      const user = member?.user ?? client.user;
      participants.push({
        id: user.id, name: member?.displayName ?? user.username, bot,
        avatar: (member ?? user).displayAvatarURL(avatarImage), muted: !!member?.voice.mute,
        speaking: bot ? room.player.state.status === AudioPlayerStatus.Playing : room.speaking.has(user.id),
      });
    }
    if (room.connected) {
      person(members?.get(client.user.id), true);
      const owner = members?.get(ownerId);
      if (owner) person(owner);
      for (const member of members?.values() ?? []) {
        if (participants.length >= 12) break;
        if (member.user.id !== client.user.id && member.user.id !== ownerId) person(member);
      }
    }
    room.visible = new Set(participants.map(member => member.id));
    const total = room.connected ? (members?.size ?? 0) + (members?.has(client.user.id) ? 0 : 1) : 0;
    return { session: room.id, server: voice.guild.name, channel: voice.name, participants, total };
  }

  function speaking(room, id, active) {
    if (call !== room || !room.connected) return;
    if (active) room.speaking.add(id);
    else room.speaking.delete(id);
    if (room.visible.has(id)) tell({ type: 'speaker', session: room.id, id, active });
  }

  async function join(voice) {
    if (voice?.type !== ChannelType.GuildVoice) {
      return { ok: false, text: 'Join a server voice channel where your bot is installed, then ask me again.' };
    }
    if (call) {
      if (call.connected && call.voice.id === voice.id) return { ok: true, text: 'I’m already in ' + voice.name + '.' };
      return { ok: false, text: 'I already have a voice session. Use /capri leave in that server first.' };
    }
    const permissions = voice.permissionsFor(client.user);
    if (!permissions?.has(required)) {
      return { ok: false, text: 'Give the bot View Channel, Connect, Speak and Send Messages in this voice channel.' };
    }

    const connection = joinVoiceChannel({
      guildId: voice.guild.id, channelId: voice.id, adapterCreator: voice.guild.voiceAdapterCreator,
      selfDeaf: false, selfMute: false,
    });
    const player = createAudioPlayer();
    const room = {
      id: randomUUID(), voice, connection, player, streams: new Map(), speakers: new Map(),
      turns: [], nextAudio: null, draining: false, connected: false, greet: false, speaking: new Set(), visible: new Set(),
    };
    call = room;
    tell({ type: 'joining', ...roomInfo(room) });
    connection.on('error', () => leave(room));
    connection.on(VoiceConnectionStatus.Disconnected, () => leave(room));
    connection.on(VoiceConnectionStatus.Destroyed, () => leave(room));
    player.on('error', () => tell({ type: 'notice', text: 'Discord could not play the voice reply.' }));
    player.on(AudioPlayerStatus.Idle, () => play(room));
    player.on('stateChange', (before, after) => {
      const was = before.status === AudioPlayerStatus.Playing;
      const now = after.status === AudioPlayerStatus.Playing;
      if (was !== now) speaking(room, client.user.id, now);
    });
    connection.subscribe(player);
    try {
      await entersState(connection, VoiceConnectionStatus.Ready, 15_000);
      if (call !== room || voice.guild.voiceStates.cache.get(ownerId)?.channel?.id !== voice.id) {
        leave(room);
        return { ok: false, text: 'The Discord call changed while I was joining. Ask me again from the call.' };
      }
      await voice.send('Capri is here and can chime in as you talk. The host PC transcribes call audio, and its selected AI provider may process this conversation. The bot owner can ask Capri to wait for her name. Capri leaves when her owner leaves or runs /capri leave.');
      if (call !== room) return { ok: false, text: 'The Discord call ended while I was joining.' };
      room.connected = true;
      tell({ type: 'joined', ...roomInfo(room) });
      connection.receiver.speaking.on('start', userId => { speaking(room, userId, true); listen(room, userId); });
      connection.receiver.speaking.on('end', userId => speaking(room, userId, false));
      return { ok: true, text: 'I joined ' + voice.name + ' on my bot account.', room };
    } catch {
      leave(room);
      return { ok: false, text: 'I couldn’t join and post the listening notice. Check the channel permissions and whether it has room.' };
    }
  }

  async function joinOwner(id) {
    let result = { ok: false, text: 'The bot is still connecting to Discord. Try again in a moment.' };
    try {
      if (available && !closed && client.isReady()) {
        const rooms = [];
        for (const guild of client.guilds.cache.values()) {
          if (guild.available === false) continue;
          const voice = guild.voiceStates.cache.get(ownerId)?.channel;
          if (voice?.type === ChannelType.GuildVoice) rooms.push(voice);
        }
        result = rooms.length > 1
          ? { ok: false, text: 'I can see more than one call. Run /capri join in the server you want me in.' }
          : await join(rooms[0]);
      }
    } catch {
      result = { ok: false, text: 'I couldn’t open the Discord voice connection.' };
    }
    tell({ type: 'owner-join-result', id, ok: result.ok, text: result.text });
    if (result.room && call === result.room) {
      result.room.greet = true;
      void drain(result.room);
    }
  }

  function play(room) {
    if (call !== room || !room.nextAudio || room.player.state.status !== AudioPlayerStatus.Idle) return;
    const audio = room.nextAudio;
    room.nextAudio = null;
    room.player.play(audio);
  }

  function quiet(room) {
    room.turns.length = 0;
    room.greet = false;
    room.nextAudio = null;
    room.player.stop(true);
  }

  async function drain(room) {
    if (!room || call !== room || pending || room.draining) return;
    room.draining = true;
    try {
      while (call === room && !pending && (room.greet || room.turns.length > 0)) {
        let result;
        if (room.greet) {
          room.greet = false;
          result = await turn(room, {}, { greet: true });
        } else {
          const heard = room.turns.shift();
          if (Date.now() - heard.at > maxTurnAge) continue;
          result = await turn(room, heard.speaker, { audio: heard.audio, duringPlayback: heard.duringPlayback });
        }
        if (call !== room || !result) continue;
        if (result.quiet) { quiet(room); continue; }
        if (!result.text) continue;
        if (result.audio) {
          const samples = Buffer.from(result.audio, 'base64');
          room.nextAudio = createAudioResource(Readable.from([samples]), { inputType: StreamType.Raw });
          play(room);
        } else {
          await room.voice.send({ content: result.text.slice(0, 1900), allowedMentions: noMentions });
        }
      }
    } catch {
      if (call === room) tell({ type: 'notice', text: 'There was a problem replying in the voice channel.' });
    } finally {
      room.draining = false;
      if ((room.greet || room.turns.length > 0) && call === room && !pending) void drain(room);
    }
  }

  function listen(room, userId) {
    if (call !== room || userId === client.user.id || room.streams.has(userId) || room.streams.size >= 4) return;
    const cached = room.voice.guild.members.cache.get(userId);
    if (cached?.user.bot) return;
    let duringPlayback = room.player.state.status === AudioPlayerStatus.Playing;

    const opus = room.connection.receiver.subscribe(userId, {
      end: { behavior: EndBehaviorType.AfterSilence, duration: speechEndPause },
    });
    room.streams.set(userId, opus);
    // Decode straight to the recognizer's sampling rate and use Opus's downmix.
    const codec = new Opus(speechRate, 1, Opus.Application.VOIP);
    const pcm = Buffer.allocUnsafe(maxSpeechBytes);
    let bytes = 0, packetBytes = 0;
    let packetEnd = 0;
    let disposed = false;
    // A speaking event can arrive before any decryptable audio. Release an empty subscription.
    const initialPacketTimeout = setTimeout(() => opus.destroy(), 1500);

    function submit(continuing) {
      if (packetBytes >= 8000 && call === room) {
        void enqueueSpeech(room, userId, cached, pcm.subarray(0, bytes).toString('base64'), duringPlayback);
      }
      // Keep the last 180 ms across a forced boundary so recognition has the start of a split word.
      const overlap = continuing ? Math.min(bytes, 180 * speechBytesPerMs) : 0;
      if (overlap) pcm.copy(pcm, 0, bytes - overlap, bytes);
      bytes = overlap;
      packetBytes = 0;
      duringPlayback = room.player.state.status === AudioPlayerStatus.Playing;
    }

    function dispose() {
      if (disposed) return;
      disposed = true;
      clearTimeout(initialPacketTimeout);
      room.streams.delete(userId);
      codec.delete();
    }
    opus.on('error', dispose);
    opus.once('close', dispose);
    opus.on('data', packet => {
      if (disposed) return;
      clearTimeout(initialPacketTimeout);
      const arrived = performance.now();
      let decoded;
      try {
        decoded = codec.decode(packet);
      } catch {
        return;
      }
      // Discord can suppress quiet packets. Preserve a real pause between words while leaving ordinary jitter alone.
      const gap = packetEnd > 0 ? arrived - packetEnd : 0;
      const pause = gap >= 75 ? Math.round(Math.min(gap, 500) * speechRate / 1000) * 2 : 0;
      packetEnd = arrived + decoded.length / speechBytesPerMs;
      if (bytes + pause + decoded.length > pcm.length) submit(true);
      if (pause) pcm.fill(0, bytes, bytes + pause);
      bytes += pause;
      decoded.copy(pcm, bytes);
      bytes += decoded.length;
      packetBytes += decoded.length;
    });
    opus.once('end', () => {
      dispose();
      submit(false);
    });
  }

  async function enqueueSpeech(room, userId, cached, audio, duringPlayback) {
    try {
      const at = Date.now();
      const member = cached ?? await room.voice.guild.members.fetch(userId);
      if (member.user.bot || member.voice.channelId !== room.voice.id || call !== room) return;
      if (!room.speakers.has(userId)) room.speakers.set(userId, room.speakers.size + 1);
      const owner = userId === ownerId;
      const heard = { at, duringPlayback, speaker: { name: member.displayName, speaker: room.speakers.get(userId), owner }, audio };
      if (owner && duringPlayback) {
        room.turns.unshift(heard);
        if (room.turns.length > maxQueuedTurns) room.turns.pop();
      } else {
        room.turns.push(heard);
        if (room.turns.length > maxQueuedTurns) room.turns.shift();
      }
      void drain(room);
    } catch {
      if (call === room) tell({ type: 'notice', text: 'There was a problem identifying a voice channel speaker.' });
    }
  }

  client.on(Events.VoiceStateUpdate, (before, after) => {
    const room = call;
    if (!room || before.guild.id !== room.voice.guild.id) return;
    const moved = before.channelId !== after.channelId;
    if (moved && before.channelId === room.voice.id && (before.id === ownerId || before.id === client.user.id)) leave(room);
    else if (room.connected && (moved || before.mute !== after.mute) && (before.channelId === room.voice.id || after.channelId === room.voice.id))
      tell({ type: 'participants', ...roomInfo(room) });
  });

  client.on(Events.InteractionCreate, async interaction => {
    if (!interaction.isChatInputCommand() || interaction.commandName !== 'capri') return;
    try {
      if (!available || !interaction.inGuild() || interaction.user.id !== ownerId) {
        await interaction.reply({ content: 'The bot’s owner can use these commands.', flags: MessageFlags.Ephemeral });
        return;
      }
      await answer(interaction);
    } catch {
      const content = 'Capri could not finish that Discord command.';
      if (interaction.deferred) await interaction.editReply(content).catch(() => {});
      else if (!interaction.replied) await interaction.reply({ content, flags: MessageFlags.Ephemeral }).catch(() => {});
    }
  });

  client.on(Events.Error, () => tell({ type: 'notice', text: 'Discord connection interrupted.' }));
  input.on('close', () => stop());
  input.on('line', async line => {
    let message;
    try { message = JSON.parse(line.replace(/^\uFEFF/, '')); } catch { return; }
    if (message?.type === 'stop') { stop(); return; }
    if (message?.type === 'reply' && pending?.id === message.id) { finishTurn(message); return; }
    if (message?.type === 'join-owner' && typeof message.id === 'string' && message.id.length === 36) { await joinOwner(message.id); return; }
    if (call?.connected && message?.session === call.id) {
      if (message.type === 'leave-voice') { leave(call); return; }
      if (message.type === 'quiet-voice') {
        const room = call;
        quiet(room);
        if (pending?.room === room) finishTurn(null);
        return;
      }
    }
    if (message?.type !== 'start' || starting) return;
    starting = true;
    const readiness = new AbortController();
    const timeout = setTimeout(() => {
      tell({ type: 'error', text: 'Discord did not finish connecting. Check your connection and try again.' });
      stop(1);
      readiness.abort();
    }, 45_000);
    try {
      await client.login(message.token);
      if (!closed && !client.isReady()) await once(client, Events.ClientReady, { signal: readiness.signal });
      if (!closed) await ready();
    } catch {
      tell({ type: 'error', text: client.isReady()
        ? 'Discord signed in but could not register /capri. Check that Server Install is enabled.'
        : 'Discord could not sign in. Check the bot token and your connection.' });
      stop(1);
    } finally {
      clearTimeout(timeout);
    }
  });
}

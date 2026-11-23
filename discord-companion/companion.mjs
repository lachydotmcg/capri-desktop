import { createInterface } from 'node:readline';
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

const maxSamples = 16_000 * 12;
const noMentions = { parse: [] };
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
  const codec = new Opus(48_000, 2, Opus.Application.VOIP);
  try {
    const packet = codec.encode(Buffer.alloc(960 * 4), 960);
    if (codec.decode(packet).length !== 960 * 4) throw new Error('Opus round trip failed');
  } finally {
    codec.delete();
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
  }

  function leave(room) {
    if (!room || call !== room) return;
    call = undefined;
    if (pending?.room === room) finishTurn(null);
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
    tell({ type: 'ready', clientId: application.id, name: client.user.username });
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
      await join(interaction);
      return;
    }

    if (pending) {
      await interaction.reply({ content: 'Capri is in the middle of another reply.', flags: MessageFlags.Ephemeral });
      return;
    }
    await interaction.deferReply({ flags: MessageFlags.Ephemeral });
    const result = await turn(undefined, { name: interaction.user.displayName, speaker: 1 },
      { text: interaction.options.getString('message', true) });
    await interaction.editReply({ content: result?.text?.slice(0, 1900) || 'Capri did not get a reply from her selected model.' });
  }

  async function join(interaction) {
    const voice = interaction.guild?.voiceStates.cache.get(ownerId)?.channel;
    if (voice?.type !== ChannelType.GuildVoice) {
      await interaction.editReply('Join a server voice channel, then run /capri join again.');
      return;
    }
    if (call) {
      await interaction.editReply('Capri already has a voice session. Use /capri leave in that server first.');
      return;
    }
    const permissions = voice.permissionsFor(client.user);
    if (!permissions?.has(required)) {
      await interaction.editReply('Give the bot View Channel, Connect, Speak and Send Messages in this voice channel.');
      return;
    }

    const connection = joinVoiceChannel({
      guildId: voice.guild.id, channelId: voice.id, adapterCreator: voice.guild.voiceAdapterCreator,
      selfDeaf: false, selfMute: false,
    });
    const player = createAudioPlayer();
    const room = { id: randomUUID(), voice, connection, player, streams: new Map(), speakers: new Map() };
    call = room;
    connection.on('error', () => leave(room));
    connection.on(VoiceConnectionStatus.Disconnected, () => leave(room));
    connection.on(VoiceConnectionStatus.Destroyed, () => leave(room));
    player.on('error', () => tell({ type: 'notice', text: 'Discord could not play the voice reply.' }));
    connection.subscribe(player);
    try {
      await entersState(connection, VoiceConnectionStatus.Ready, 15_000);
      if (call !== room) return;
      await voice.send('Capri is here. Say “Capri” to get a reply. This PC transcribes the call audio and its selected AI provider may process lines addressed to Capri. The bot leaves when its owner leaves the channel or runs /capri leave.');
      if (call !== room) return;
      tell({ type: 'joined', session: room.id, channel: voice.name });
      connection.receiver.speaking.on('start', userId => listen(room, userId));
      await interaction.editReply('Capri is listening in ' + voice.name + '.');
    } catch {
      leave(room);
      await interaction.editReply('Capri could not open voice and post the listening notice. Check channel permissions and whether it has space.');
    }
  }

  function listen(room, userId) {
    if (call !== room || pending || room.player.state.status !== AudioPlayerStatus.Idle
        || userId === client.user.id || room.streams.has(userId) || room.streams.size >= 4) return;
    const cached = room.voice.guild.members.cache.get(userId);
    if (cached?.user.bot) return;

    const opus = room.connection.receiver.subscribe(userId, {
      end: { behavior: EndBehaviorType.AfterSilence, duration: 750 },
    });
    room.streams.set(userId, opus);
    const codec = new Opus(48_000, 2, Opus.Application.VOIP);
    const pcm = Buffer.allocUnsafe(maxSamples * 2);
    let bytes = 0;
    let disposed = false;
    let ending = false;
    function endCapture() {
      if (ending || opus.destroyed || opus.readableEnded) return;
      ending = true;
      opus.push(null);
    }
    const cutoff = setTimeout(endCapture, 12_500);
    function dispose() {
      if (disposed) return;
      disposed = true;
      clearTimeout(cutoff);
      room.streams.delete(userId);
      codec.delete();
    }
    opus.on('error', dispose);
    opus.once('close', dispose);
    opus.on('data', packet => {
      try {
        const decoded = codec.decode(packet);
        // 48 kHz stereo Opus to 16 kHz mono for Capri's existing recognizer.
        for (let i = 0; i + 11 < decoded.length && bytes < pcm.length; i += 12) {
          const mean = (decoded.readInt16LE(i) + decoded.readInt16LE(i + 2)
            + decoded.readInt16LE(i + 4) + decoded.readInt16LE(i + 6)
            + decoded.readInt16LE(i + 8) + decoded.readInt16LE(i + 10)) / 6;
          pcm.writeInt16LE(Math.round(mean), bytes);
          bytes += 2;
        }
        if (bytes === pcm.length) endCapture();
      } catch {
        opus.destroy();
      }
    });
    opus.once('end', async () => {
      dispose();
      if (bytes < 8000 || call !== room || pending) return;
      try {
        const member = cached ?? await room.voice.guild.members.fetch(userId);
        if (member.user.bot || member.voice.channelId !== room.voice.id || call !== room || pending) return;
        if (!room.speakers.has(userId)) room.speakers.set(userId, room.speakers.size + 1);
        const result = await turn(room, { name: member.displayName, speaker: room.speakers.get(userId) },
          { audio: pcm.subarray(0, bytes).toString('base64') });
        if (!result?.text || call !== room) return;
        if (result.audio) {
          const samples = Buffer.from(result.audio, 'base64');
          room.player.play(createAudioResource(Readable.from([samples]), { inputType: StreamType.Raw }));
        } else {
          await room.voice.send({ content: result.text.slice(0, 1900), allowedMentions: noMentions });
        }
      } catch {
        if (call === room) tell({ type: 'notice', text: 'There was a problem replying in the voice channel.' });
      }
    });
  }

  client.on(Events.VoiceStateUpdate, (before, after) => {
    const room = call;
    if (room && before.guild.id === room.voice.guild.id && before.channelId === room.voice.id
        && after.channelId !== room.voice.id && (before.id === ownerId || before.id === client.user.id)) leave(room);
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
    if (message?.type !== 'start' || starting) return;
    starting = true;
    const timeout = setTimeout(() => {
      tell({ type: 'error', text: 'Discord did not finish connecting. Check your connection and try again.' });
      stop(1);
    }, 45_000);
    try {
      await client.login(message.token);
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

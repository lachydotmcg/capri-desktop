using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CapriDesktop
{
    // The local child owns Discord. Only conversation and audio cross the redirected pipes.
    internal sealed partial class DiscordCompanion
    {
        internal const string Portal = "https://discord.com/developers/applications";
        internal const string NodeDownload = "https://nodejs.org/en/download";
        private const string Rules = "\nYou're speaking as a bot in a Discord server. Each turn names a speaker and has a speaker number unique within the call. Address them by their displayed name when useful. Keep replies to one or two short sentences.\n";
        private const int InvitePermissions = (1 << 10) | (1 << 11) | (1 << 20) | (1 << 21);
        private static readonly Regex NameCall = new Regex(@"\bcapri\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        internal sealed class View
        {
            internal string Status, Invite;
            internal bool Running, Saved, Problem;
        }

        private sealed class Run
        {
            internal readonly CallSession Voice = new CallSession(Rules);
            internal readonly object Output = new object();
            internal volatile Process Child;
            internal bool IsBot, Ready;
            internal string Token, AppId, Name, SessionId;
            internal int Replying;
        }

        private readonly object gate = new object(), preparing = new object();
        private readonly string home, saved;
        private readonly Func<string, CallSession, int, Func<bool>, string> ask;
        private readonly Func<string, byte[]> speak;
        private volatile Run active;
        private string status = "Offline. Connect a bot to get its server invite.";
        private bool problem;

        internal DiscordCompanion(string data, Func<string, CallSession, int, Func<bool>, string> ask, Func<string, byte[]> speak)
        {
            home = Path.Combine(data, "discord-companion");
            saved = Path.Combine(home, "token.dat");
            this.ask = ask;
            this.speak = speak;
        }

        internal View Snapshot()
        {
            lock (gate)
            {
                Run run = active;
                return new View
                {
                    Status = status, Problem = problem, Running = run != null, Saved = File.Exists(saved),
                    Invite = run != null && run.Ready
                        ? "https://discord.com/oauth2/authorize?client_id=" + run.AppId + "&integration_type=0&scope=bot%20applications.commands&permissions=" + InvitePermissions
                        : null
                };
            }
        }

        internal void Connect(string typed)
        {
            lock (gate)
            {
                if (active != null) return;
                string token = (typed ?? "").Trim();
                try { if (token.Length == 0 && File.Exists(saved)) token = Voice.Unprotect(File.ReadAllText(saved)) ?? ""; }
                catch (IOException) { Set("Capri could not read the saved bot token. Paste it again.", true); return; }
                catch (UnauthorizedAccessException) { Set("Capri could not read the saved bot token. Paste it again.", true); return; }
                if (!Regex.IsMatch(token, @"\A[A-Za-z0-9._-]{20,256}\z"))
                {
                    Set("Paste the bot token from the Bot tab in Discord’s Developer Portal.", true);
                    return;
                }
                Run run = new Run { Token = token };
                active = run;
                Set("Starting the local Discord helper…", false);
                ThreadPool.QueueUserWorkItem(delegate { Start(run); });
            }
        }

        internal void Stop(bool forget = false)
        {
            Run run;
            lock (gate)
            {
                run = active;
                active = null;
                if (run != null) { run.Token = null; run.Voice.Change(false); }
                Set(forget ? "Bot token removed from this PC." : "Offline. The local helper is stopped.", false);
                if (forget)
                {
                    try { File.Delete(saved); File.Delete(saved + ".new"); }
                    catch (DirectoryNotFoundException) { }
                    catch (IOException) { Set("The bot stopped. Windows could not remove its saved token.", true); }
                    catch (UnauthorizedAccessException) { Set("The bot stopped. Windows could not remove its saved token.", true); }
                }
            }
            if (run != null) ThreadPool.QueueUserWorkItem(delegate { End(run); });
        }

        private void Set(string message, bool failure)
        {
            status = message;
            problem = failure;
        }

        private void Receive(Run run, string line)
        {
            if (active != run || string.IsNullOrEmpty(line) || line.Length > 540000) return;
            object packet;
            if (!PluginJson.TryParse(line, out packet)) return;
            string type = PluginJson.Str(packet, "type");
            if (type == "turn") { Reply(run, packet); return; }
            lock (gate)
            {
                if (active != run) return;
                if (type == "ready")
                {
                    string id = PluginJson.Str(packet, "clientId") ?? "";
                    if (!Regex.IsMatch(id, @"\A[0-9]{17,20}\z") || run.Ready) return;
                    run.Name = PluginJson.Str(packet, "name") ?? "Capri";
                    run.AppId = id;
                    run.Ready = true;
                    try
                    {
                        string temp = saved + ".new";
                        File.WriteAllText(temp, Voice.Protect(run.Token), Utf8);
                        if (File.Exists(saved)) File.Replace(temp, saved, null);
                        else File.Move(temp, saved);
                        Set(run.Name + " is online. Run /capri join in your server.", false);
                    }
                    catch (Exception) { Set(run.Name + " is online. Windows could not save the token.", true); }
                    finally { run.Token = null; }
                }
                else if (type == "joined" && run.Ready)
                {
                    run.Voice.Change(true);
                    run.SessionId = PluginJson.Str(packet, "session");
                    Set("Listening in " + PluginJson.Str(packet, "channel") + ".", false);
                }
                else if (type == "left" && run.SessionId == PluginJson.Str(packet, "session"))
                {
                    run.Voice.Change(false);
                    run.SessionId = null;
                    Set(run.Name + " is online. Run /capri join in Discord.", false);
                }
                else if (type == "notice" || type == "error")
                    Set(PluginJson.Str(packet, "text") ?? "The bot needs to reconnect.", type == "error");
            }
        }

        private void Reply(Run run, object packet)
        {
            string id = PluginJson.Str(packet, "id"), room = PluginJson.Str(packet, "session");
            string audio = PluginJson.Str(packet, "audio"), words = PluginJson.Str(packet, "text");
            Guid turn;
            if (!Guid.TryParse(id, out turn)) return;
            bool voice = !string.IsNullOrEmpty(room) && audio != null;
            CallSession session;
            int version;
            lock (gate)
            {
                if (active != run || !run.Ready || (voice && room != run.SessionId)) return;
                session = voice ? run.Voice : new CallSession(Rules);
                if (!voice) session.Change(true);
                version = session.Generation;
            }
            if (Interlocked.CompareExchange(ref run.Replying, 1, 0) != 0)
            {
                SendReply(run, id, voice ? null : "Capri is finishing the previous turn.", null);
                return;
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                string text = null, voiced = null;
                Func<bool> current = delegate { return active == run && session.Current(version); };
                try
                {
                    if (voice)
                    {
                        byte[] raw = Convert.FromBase64String(audio);
                        if (raw.Length < 8000 || raw.Length > Ears.Rate * 24 || raw.Length % 2 != 0 || !current()) return;
                        short[] samples = new short[raw.Length / 2];
                        Buffer.BlockCopy(raw, 0, samples, 0, raw.Length);
                        words = Hearing.Text(samples);
                        if (!NameCall.IsMatch(words)) return;
                    }
                    if (string.IsNullOrWhiteSpace(words) || words.Length > 1500 || !current()) return;
                    string name = Regex.Replace(PluginJson.Str(packet, "name") ?? "Discord member", @"[\p{C}]+", " ").Trim();
                    if (name.Length > 80) name = name.Substring(0, 80);
                    object number = PluginJson.Get(packet, "speaker");
                    double speaker = number is double ? (double)number : 1;
                    text = Brain.Visible(ask("Speaker " + speaker + " (" + Voice.Quote(name) + "): " + words, session, version, current));
                    if (text.Length > 1500) text = text.Substring(0, 1500);
                    if (voice && text.Length > 0 && current())
                    {
                        try { voiced = VoicePcm(speak(text)); }
                        catch (Exception) { }
                    }
                }
                catch (Exception)
                {
                    lock (gate) if (active == run) Set("Check the selected model and speech recognition in Mainframe.", true);
                    if (!voice) text = "Capri couldn’t get a reply from her selected model.";
                }
                finally
                {
                    Interlocked.Exchange(ref run.Replying, 0);
                    if (current()) SendReply(run, id, text, voiced);
                }
            });
        }

        private static void SendReply(Run run, string id, string text, string voiced)
        {
            Send(run, "{\"type\":\"reply\",\"id\":" + PluginJson.Quote(id)
                + ",\"text\":" + PluginJson.Quote(text) + ",\"audio\":" + PluginJson.Quote(voiced) + "}");
        }

        // Discord's player takes 48 kHz signed stereo PCM. The app's WAV reader works with every selected voice.
        private static string VoicePcm(byte[] wav)
        {
            if (wav == null) return null;
            int rate;
            float[] pcm = YourVoice.Wav(wav, out rate);
            if (pcm == null || rate <= 0 || pcm.Length == 0 || pcm.Length > (long)rate * 30) return null;
            short[] mono = new short[pcm.Length];
            for (int i = 0; i < pcm.Length; i++) mono[i] = (short)Math.Round(Math.Max(-1, Math.Min(1, pcm[i])) * short.MaxValue);
            if (rate != 48000) mono = VoiceFx.Resample(mono, rate / 48000.0);
            byte[] stereo = new byte[mono.Length * 4];
            for (int i = 0; i < mono.Length; i++)
            {
                stereo[4 * i] = stereo[4 * i + 2] = (byte)mono[i];
                stereo[4 * i + 1] = stereo[4 * i + 3] = (byte)(mono[i] >> 8);
            }
            return Convert.ToBase64String(stereo);
        }
    }

    internal sealed partial class Companion
    {
        private DiscordCompanion discordCompanion;
        internal DiscordCompanion DiscordBot
        {
            get
            {
                if (discordCompanion == null) discordCompanion = new DiscordCompanion(Path.GetDirectoryName(settingsPath),
                    delegate(string words, CallSession session, int version, Func<bool> current)
                    { return brain.AskCall(words, session, version, current); }, DiscordVoice);
                return discordCompanion;
            }
        }

        private byte[] DiscordVoice(string words)
        {
            Func<string, byte[]> free;
            Func<string, byte[]> chosen = PickVoice(false, out free, false);
            if (chosen == null) return null;
            string problem;
            return Voiced(chosen, free, words, out problem, false);
        }
    }
}

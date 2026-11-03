using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;

namespace CapriDesktop
{
    // "Playing Capri" on their Discord profile, through the Rich Presence pipe the Discord app opens on this PC.
    // No login and no bot: when Discord isn't running there's no pipe, and she tries again later.
    internal sealed class Presence : IDisposable
    {
        // The Discord application's id. Empty keeps her off Discord.
        internal static string AppId = "1564889759157850182";
        internal static string PipeName = "discord-ipc-";
        internal const string Picture = "capri";
        private const string Site = "https://capriai.app/";
        private const string Server = "https://discord.gg/F4fDHfGtFk";
        // Discord only animates pictures at a public link, so her moving art sits on her site. Until it's there, or while the
        // site's down, her still picture shows.
        private const string Art = Site + "assets/presence/";
        private const int Wait = 5000;
        private const int Retry = 30000;
        private const int Refused = 600000;
        // Discord takes five updates every 20 seconds, so they go out at least 4 seconds apart.
        private const int Spacing = 4000;
        // Sending the same line again now and then is how she notices Discord quit and came back.
        private const int Refresh = 60000;
        internal static Func<bool> ArtLive = OnSite;

        private readonly object gate = new object();
        private readonly AutoResetEvent nudge = new AutoResetEvent(false);
        private readonly string version;
        private readonly long started = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        private readonly int pid = Process.GetCurrentProcess().Id;
        private string wanted;
        private string noted;
        private Thread worker;
        private volatile bool closing;
        private volatile string status = "Off";
        private bool plain;
        // How many of her there are while she's cloned, for Discord's party count.
        internal volatile int Crowd;

        internal Presence(string version) { this.version = version; }

        internal string Status { get { return status; } }

        // What she's doing, in a few plain words, or null to take her off their profile.
        internal void Show(string doing)
        {
            if (string.IsNullOrEmpty(AppId)) return;
            lock (gate)
            {
                if (doing == wanted) return;
                wanted = doing;
                if (worker == null && doing != null)
                {
                    worker = new Thread(Run);
                    worker.IsBackground = true;
                    worker.Name = "Discord status";
                    worker.Start();
                }
            }
            nudge.Set();
        }

        public void Dispose()
        {
            closing = true;
            nudge.Set();
        }

        private void Run()
        {
            while (!closing)
            {
                if (Wanted() == null) { status = "Off"; nudge.WaitOne(); continue; }
                NamedPipeClientStream pipe = Connect();
                if (pipe == null) { status = "No Discord"; Rest(Retry); continue; }
                int pause = Retry;
                using (pipe)
                {
                    try { pause = Talk(pipe); }
                    catch (IOException) { status = "No Discord"; }
                    catch (TimeoutException) { status = "No Discord"; }
                    catch (ObjectDisposedException) { }
                    catch (InvalidOperationException) { }
                }
                Rest(pause);
            }
        }

        // Waits out a pause after Discord was missing or said no. What she's doing can change meanwhile without
        // knocking on Discord again; only quitting or turning it off cuts the pause short.
        private void Rest(int pause)
        {
            int until = unchecked(Environment.TickCount + pause);
            while (!closing && Wanted() != null)
            {
                int left = unchecked(until - Environment.TickCount);
                if (left <= 0) return;
                nudge.WaitOne(left);
            }
        }

        private string Wanted() { lock (gate) return wanted; }

        private static NamedPipeClientStream Connect()
        {
            foreach (string name in Names())
            {
                NamedPipeClientStream pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
                try { pipe.Connect(100); return pipe; }
                catch (TimeoutException) { pipe.Dispose(); }
                catch (IOException) { pipe.Dispose(); }
                catch (UnauthorizedAccessException) { pipe.Dispose(); }
#if CROSS
                catch (Exception) { pipe.Dispose(); }
#endif
            }
            return null;
        }

        // Windows: the pipes discord-ipc-0 to 9. Mac and Linux: sockets with those names in Discord's runtime or temp
        // folder, or where its Flatpak and Snap apps put them, as full paths, which .NET opens as Unix sockets.
        internal static List<string> Names()
        {
            List<string> names = new List<string>();
#if CROSS
            foreach (string folder in Folders())
                for (int i = 0; i < 10; i++)
                {
                    string path = Path.Combine(folder, PipeName + i);
                    if (File.Exists(path)) names.Add(path);
                }
#else
            for (int i = 0; i < 10; i++) names.Add(PipeName + i);
#endif
            return names;
        }

#if CROSS
        private static List<string> Folders()
        {
            List<string> folders = new List<string>();
            foreach (string variable in new[] { "XDG_RUNTIME_DIR", "TMPDIR", "TMP", "TEMP" })
            {
                string folder = Environment.GetEnvironmentVariable(variable);
                if (!string.IsNullOrEmpty(folder) && !folders.Contains(folder)) folders.Add(folder);
            }
            if (!folders.Contains("/tmp")) folders.Add("/tmp");
            string runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            if (!string.IsNullOrEmpty(runtime))
            {
                folders.Add(Path.Combine(runtime, "app", "com.discordapp.Discord"));
                folders.Add(Path.Combine(runtime, "snap.discord"));
                folders.Add(Path.Combine(runtime, ".flatpak", "dev.vencord.Vesktop", "xdg-run"));
            }
            return folders;
        }
#endif

        // One connection: hello, then a line whenever what she's doing changes. Hanging up takes her off their profile.
        // Returns how long to wait before calling again.
        private int Talk(Stream pipe)
        {
            Send(pipe, 0, Json(new DiscordHello { ClientId = AppId }));
            int op;
            string said = Receive(pipe, out op);
            if (op != 1) { Note("Discord refused her: " + Reason(said)); return Refused; }
            bool moving = ArtLive();
            string sent = null;
            int last = unchecked(Environment.TickCount - Refresh);
            while (!closing)
            {
                string doing = Wanted();
                if (doing == null) return 0;
                int since = unchecked(Environment.TickCount - last);
                if (doing == sent && since < Refresh) { nudge.WaitOne(Refresh - since); continue; }
                if (since < Spacing) { nudge.WaitOne(Spacing - since); continue; }
                Send(pipe, 1, Json(Activity(doing, moving)));
                last = Environment.TickCount;
                sent = doing;
                if (Answer(pipe)) status = "Showing: " + doing;
                else if (!plain) { plain = true; sent = null; }
            }
            return 0;
        }

        // Her artist's animation for what she's doing.
        internal static string Look(string doing)
        {
            switch (doing)
            {
                case "Chatting": return "chatting";
                case "Dancing": case "Dancing to music": return "dancing";
                case "Asleep": return "asleep";
                case "Voice chatting": return "voice";
                case "Taking a break": return "break";
                case "Using the computer": return "computer";
                case "Playing a game": case "Gaming": return "game";
                case "Streaming": return "live";
                case "Multiplying": return "clones";
                default: return "plugged";
            }
        }

        // The little picture in the corner: a glitch cursor or a glitch controller.
        internal static string Badge(string doing)
        {
            return doing == "Using the computer" ? "cursor" : doing == "Playing a game" || doing == "Gaming" || doing == "Streaming" ? "controller" : null;
        }

        // Voice chat reads "Listening to Capri" and Gaming Mode "Watching Capri". The rest stay "Playing Capri".
        internal static int Header(string doing) { return doing == "Voice chatting" ? 2 : doing == "Gaming" ? 3 : 0; }

        private static bool OnSite()
        {
            try
            {
                HttpWebRequest asking = (HttpWebRequest)WebRequest.Create(Art + "plugged.gif");
                asking.Method = "HEAD";
                asking.Timeout = Wait;
                using (HttpWebResponse answer = (HttpWebResponse)asking.GetResponse()) return answer.StatusCode == HttpStatusCode.OK;
            }
            catch (WebException) { return false; }
            catch (IOException) { return false; }
        }

        private DiscordCommand Activity(string doing, bool moving)
        {
            DiscordActivity activity = new DiscordActivity
            {
                Details = doing,
                Times = new DiscordTimes { Start = started },
                Assets = new DiscordAssets { Image = Picture, Text = "Capri " + version },
                Buttons = new[] { new DiscordButton { Label = "Get Capri", Url = Site }, new DiscordButton { Label = "Join her Discord", Url = Server } }
            };
            // If Discord turns any of the extras down, she goes back to the plain line for the rest of this run.
            if (!plain)
            {
                int crowd = Crowd;
                string badge = Badge(doing);
                // The version on the end makes Discord fetch her art again after an update, not a copy it kept.
                string fresh = ".gif?v=" + version;
                activity.Type = Header(doing);
                if (crowd > 1) activity.Party = new DiscordParty { Id = "capri-clones", Size = new[] { crowd, Math.Max(crowd, 9999) } };
                if (moving) activity.Assets.Image = Art + Look(doing) + fresh;
                if (moving && badge != null)
                {
                    activity.Assets.Small = Art + badge + fresh;
                    activity.Assets.SmallText = doing;
                }
            }
            return new DiscordCommand { Nonce = Guid.NewGuid().ToString(), Args = new DiscordArgs { Pid = pid, Activity = activity } };
        }

        // Discord answers every command. A ping gets its pong, and an error goes in errors.txt once.
        private bool Answer(Stream pipe)
        {
            while (true)
            {
                int op;
                string said = Receive(pipe, out op);
                if (op == 3) { Send(pipe, 4, said); continue; }
                if (op == 2) throw new IOException("Discord closed the connection: " + Reason(said));
                DiscordReply reply = Parse(said);
                if (reply == null || reply.Evt != "ERROR") return true;
                Note("Discord turned down her status: " + Reason(said));
                return false;
            }
        }

        private void Note(string problem)
        {
            status = problem;
            if (problem == noted) return;
            noted = problem;
            Brain.Log("Discord status", -1, problem);
        }

        private static string Reason(string said)
        {
            DiscordReply reply = Parse(said);
            return reply != null && reply.Data != null && !string.IsNullOrEmpty(reply.Data.Message) ? reply.Data.Message : said;
        }

        // A frame is its opcode and length, both 32-bit little-endian, then that many bytes of JSON.
        internal static void Send(Stream pipe, int op, string json)
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            byte[] frame = new byte[8 + body.Length];
            BitConverter.GetBytes(op).CopyTo(frame, 0);
            BitConverter.GetBytes(body.Length).CopyTo(frame, 4);
            body.CopyTo(frame, 8);
            pipe.Write(frame, 0, frame.Length);
            pipe.Flush();
        }

        private static string Receive(Stream pipe, out int op)
        {
            byte[] head = Take(pipe, 8, Wait);
            op = BitConverter.ToInt32(head, 0);
            int length = BitConverter.ToInt32(head, 4);
            if (length < 0 || length > 1 << 20) throw new IOException("Discord sent a frame " + length + " bytes long.");
            return Encoding.UTF8.GetString(Take(pipe, length, Wait));
        }

        // Exactly count bytes, or an exception once wait milliseconds pass with nothing.
        internal static byte[] Take(Stream pipe, int count, int wait)
        {
            byte[] buffer = new byte[count];
            int got = 0;
            while (got < count)
            {
                IAsyncResult reading = pipe.BeginRead(buffer, got, count - got, null, null);
                if (!reading.AsyncWaitHandle.WaitOne(wait)) throw new TimeoutException("Discord didn't answer.");
                int read = pipe.EndRead(reading);
                if (read == 0) throw new EndOfStreamException("Discord hung up.");
                got += read;
            }
            return buffer;
        }

        private static string Json(object value)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                new DataContractJsonSerializer(value.GetType()).WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static DiscordReply Parse(string json)
        {
            try
            {
                using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(json ?? "")))
                    return (DiscordReply)new DataContractJsonSerializer(typeof(DiscordReply)).ReadObject(stream);
            }
            catch (SerializationException) { return null; }
        }
    }

    [DataContract]
    internal sealed class DiscordHello
    {
        [DataMember(Name = "v")] public int V = 1;
        [DataMember(Name = "client_id")] public string ClientId;
    }

    [DataContract]
    internal sealed class DiscordCommand
    {
        [DataMember(Name = "cmd")] public string Cmd = "SET_ACTIVITY";
        [DataMember(Name = "args")] public DiscordArgs Args;
        [DataMember(Name = "nonce")] public string Nonce;
    }

    [DataContract]
    internal sealed class DiscordArgs
    {
        [DataMember(Name = "pid")] public int Pid;
        [DataMember(Name = "activity", EmitDefaultValue = false)] public DiscordActivity Activity;
    }

    [DataContract]
    internal sealed class DiscordActivity
    {
        [DataMember(Name = "details")] public string Details;
        [DataMember(Name = "type", EmitDefaultValue = false)] public int Type;
        [DataMember(Name = "party", EmitDefaultValue = false)] public DiscordParty Party;
        [DataMember(Name = "timestamps")] public DiscordTimes Times;
        [DataMember(Name = "assets")] public DiscordAssets Assets;
        [DataMember(Name = "buttons")] public DiscordButton[] Buttons;
    }

    [DataContract]
    internal sealed class DiscordTimes
    {
        [DataMember(Name = "start")] public long Start;
    }

    [DataContract]
    internal sealed class DiscordAssets
    {
        [DataMember(Name = "large_image")] public string Image;
        [DataMember(Name = "large_text")] public string Text;
        [DataMember(Name = "small_image", EmitDefaultValue = false)] public string Small;
        [DataMember(Name = "small_text", EmitDefaultValue = false)] public string SmallText;
    }

    [DataContract]
    internal sealed class DiscordParty
    {
        [DataMember(Name = "id")] public string Id;
        [DataMember(Name = "size")] public int[] Size;
    }

    [DataContract]
    internal sealed class DiscordButton
    {
        [DataMember(Name = "label")] public string Label;
        [DataMember(Name = "url")] public string Url;
    }

    [DataContract]
    internal sealed class DiscordReply
    {
        [DataMember(Name = "cmd")] public string Cmd;
        [DataMember(Name = "evt")] public string Evt;
        [DataMember(Name = "data")] public DiscordData Data;
    }

    [DataContract]
    internal sealed class DiscordData
    {
        [DataMember(Name = "code")] public int Code;
        [DataMember(Name = "message")] public string Message;
    }
}

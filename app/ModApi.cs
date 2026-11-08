using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;
#if !CROSS
using System.Windows.Forms;
#endif

namespace CapriDesktop
{
    // The code mod API, compiled into the Windows app and the Mac and Linux one, so a mod written for one runs on the other.
    // What a code mod can reach besides Say, Log, Art, Sound and PlayIntro. Everything here is on ICapriHost, so a mod written for
    // the first API still compiles. Hooks run on her window's thread, and a mod that throws in one is logged, never let take her down.
    //
    // On Mac and Linux, the parts whose types only Windows has are left out: Art, ICapriIntro, CapriFrame and Ui.Drawing (GDI's
    // Bitmap and Graphics), Ui.SetOptions, Ui.Window, Mainframe.ShowPage and Mainframe.Window (WinForms' Control and Form), and
    // Screen's Looked and Capture (Bitmap). A mod that uses one doesn't compile there, so she refuses it at import and names
    // what's Windows-only, before asking to run it. Everything else is on every platform, and a feature a Mac or Linux PC
    // doesn't have answers the way Windows does on a PC without it, never with an error: a hotkey that can't be taken is false,
    // no battery is -1, and an event that never happens there, like Locked, never fires.

    public interface ICapriHost
    {
        string Folder { get; }
        void Say(string line);
        void Log(string line);
#if !CROSS
        Bitmap Art(string name);
#endif
        void PlayIntro();
        void Sound(string name);
        int Api { get; }
        string CapriVersion { get; }
        bool Has(string permission);
        ICapriChat Chat { get; }
        ICapriMemory Memory { get; }
        ICapriUi Ui { get; }
        ICapriMainframe Mainframe { get; }
        ICapriScreen Screen { get; }
        ICapriVoice Voice { get; }
        ICapriTimers Timers { get; }
        ICapriPersonality Personality { get; }
        ICapriSettings Settings { get; }
        ICapriSkins Skins { get; }
        ICapriBody Her { get; }
        ICapriBrain Brain { get; }
        ICapriStore Store { get; }
        ICapriSystem System { get; }
        ICapriMods Mods { get; }
        ICapriKeys Keys { get; }
        ICapriSounds Sounds { get; }
        ICapriMusic Music { get; }
    }

#if !CROSS
    public interface ICapriIntro
    {
        double Seconds { get; }
        void Begin(Bitmap desktop, Rectangle screen, Rectangle her);
        void Draw(Graphics graphics, Rectangle screen, Rectangle her, double at);
    }
#endif

    public interface ICapriMod
    {
        void Start(ICapriHost capri);
        bool Heard(string words);
        void Stop();
    }

    public sealed class CapriPermissionException : InvalidOperationException
    {
        public CapriPermissionException(string message) : base(message) { }
    }

    public sealed class CapriMessage
    {
        // Sending, Replying and Speaking can change Text. Cancel on Sending means she ignores it; on Speaking she only shows it.
        public string Text;
        public bool Cancel;
        public string Original { get; internal set; }
        // "you" for what they typed or said, "mod" for a mod's Chat.Send, "reply" for her answers, "remark" for her own lines.
        public string Source { get; internal set; }
        public DateTime At { get; internal set; }
    }

#if !CROSS
    public sealed class CapriFrame
    {
        public Graphics Graphics { get; internal set; }
        // Her whole window and her body in it, in the window's own pixels.
        public Rectangle Window { get; internal set; }
        public Rectangle Body { get; internal set; }
        public Point Head { get; internal set; }
        public bool FacingLeft { get; internal set; }
        // The strip being drawn, like "idle" or "perch", and which frame of it.
        public string Animation { get; internal set; }
        public int Frame { get; internal set; }
        public int Size { get; internal set; }
    }
#endif

    public interface ICapriTimer
    {
        bool Running { get; }
        void Cancel();
    }

    public sealed class CapriToolCall
    {
        // The tool she's about to use, like "open_app", and what it does in plain words, like "open Spotify".
        public string Tool { get; internal set; }
        public string Doing { get; internal set; }
        public string Name { get; internal set; }
        public string Action { get; internal set; }
        public string Text { get; internal set; }
        // Cancel stops it before it runs, and Reason is what her brain is told instead.
        public bool Cancel;
        public string Reason;
    }

    public interface ICapriChat
    {
        event Action<CapriMessage> Sending;
        event Action<CapriMessage> Replying;
        event Action<CapriMessage> Replied;
        event Action<string, string> ToolUsed;
        event Action<CapriToolCall> ToolUsing;
        void AddContext(Func<string> context);
        void ClearContext();
        string LastHeard { get; }
        string LastReply { get; }
        bool Busy { get; }
        void Send(string text);
        bool Start(string about);
        string[] Days();
        string Transcript(DateTime day);
    }

    public interface ICapriMemory
    {
        string[] Notes { get; }
        string Remember(string fact);
        string Forget(string about);
        bool Update(string oldText, string newText);
        void Set(string label, string value);
        string Memories { get; }
        bool WriteMemories(string text);
    }

    public interface ICapriUi
    {
#if !CROSS
        event Action<CapriFrame> Drawing;
#endif
        void Redraw();
        void AddMenuItem(string text, Action clicked);
#if !CROSS
        void SetOptions(string title, Func<Control> make);
#endif
        void Bubble(string text);
#if !CROSS
        Form Window { get; }
#endif
    }

    public interface ICapriMainframe
    {
        event Action Opened;
        event Action Closed;
        bool Open { get; }
        void Show();
        void Hide();
        void ShowSettings();
        void ShowMods();
#if !CROSS
        void ShowPage(string title, Func<Control> make);
#endif
        void ClosePage();
#if !CROSS
        Form Window { get; }
#endif
    }

    // Empty on Mac and Linux, where screenshots would be Bitmaps, but still there, so a mod reaching for it is told which part.
    public interface ICapriScreen
    {
#if !CROSS
        event Action<Bitmap> Looked;
        Bitmap Capture();
        Bitmap Capture(Rectangle area);
#endif
    }

    public interface ICapriVoice
    {
        event Action<CapriMessage> Speaking;
        event Action<string> Started;
        event Action Finished;
        event Action<string, bool> Heard;
        bool Talking { get; }
        void Speak(string text);
        void Stop();
    }

    public interface ICapriTimers
    {
        ICapriTimer After(double seconds, Action run);
        ICapriTimer Every(double seconds, Action run);
        ICapriTimer At(DateTime when, Action run);
    }

    public interface ICapriPersonality
    {
        string[] Notes { get; }
        void Add(string note);
        void Remove(string note);
        void Clear();
        string Nickname { get; set; }
    }

    public interface ICapriSettings
    {
        event Action Changed;
        string[] Switches { get; }
        string[] Sliders { get; }
        bool? IsOn(string name);
        bool Turn(string name, bool on);
        int Level(string name);
        string[] Steps(string name);
        bool SetLevel(string name, int step);
        int Size { get; set; }
    }

    public interface ICapriSkins
    {
        string[] Installed { get; }
        string Current { get; }
        string[] Accessories { get; }
        bool IsAccessory(string id);
        bool Use(string id);
        bool Equip(string id, bool on);
    }

    public interface ICapriBody
    {
        event Action<string> StateChanged;
        event Action Clicked;
        event Action Patted;
        event Action Dragged;
        event Action<Point> Dropped;
        Rectangle Bounds { get; }
        Point Head { get; }
        int Size { get; }
        string State { get; }
        string Animation { get; }
        string Animate(string name);
        bool Dragging { get; }
        Rectangle Screen { get; }
        void MoveTo(Point hip);
        string Move(string action);
        string SitOn(string window);
    }

    public interface ICapriBrain
    {
        bool Ready { get; }
        void Ask(string instructions, string question, Action<string> answered);
        bool AddTool(string name, string description, string takes, Func<string, string> run);
        void RemoveTool(string name);
        string[] Tools { get; }
    }

    public interface ICapriStore
    {
        string[] Keys { get; }
        string Get(string key);
        string Get(string key, string fallback);
        double Number(string key, double fallback);
        bool Flag(string key, bool fallback);
        void Set(string key, string value);
        void Set(string key, double value);
        void Set(string key, bool value);
        bool Remove(string key);
    }

    public interface ICapriSystem
    {
        event Action Away;
        event Action Back;
        event Action Locked;
        event Action Unlocked;
        event Action Sleeping;
        event Action Woke;
        event Action NewDay;
        event Action<int, bool> BatteryChanged;
        TimeSpan IdleFor { get; }
        double AwayMinutes { get; set; }
        bool IsAway { get; }
        int Battery { get; }
        bool Charging { get; }
    }

    public interface ICapriMods
    {
        string Id { get; }
        string[] Running { get; }
        bool IsRunning(string id);
        void Send(string topic, string data);
        void SendTo(string id, string topic, string data);
        event Action<string, string, string> Received;
    }

    public interface ICapriKeys
    {
        bool Hotkey(string keys, Action pressed);
        void Remove(string keys);
    }

    public interface ICapriSounds
    {
        bool Play(string file);
        bool Play(string file, double volume);
        void Cue(string name);
    }

    // A song or video playing in an app, as Windows' media session told it at a read, about once a second.
    public sealed class CapriTrack
    {
        public string Title { get; internal set; }
        public string Artist { get; internal set; }
        public string Album { get; internal set; }
        // The name to show, like "Spotify" or "Chrome", and the app's own id, which Music.Follow takes.
        public string App { get; internal set; }
        public string AppId { get; internal set; }
        public bool Playing { get; internal set; }
        public bool Music { get; internal set; }
        public bool Video { get; internal set; }
        // Seconds into it at the read, and how long it is; Length is 0 when the app doesn't say.
        public double Position { get; internal set; }
        public double Length { get; internal set; }
        public double Rate { get; internal set; }
        public bool CanSeek { get; internal set; }
        public bool CanSkip { get; internal set; }
        public bool CanBack { get; internal set; }
        // Null when the app doesn't say. Repeat is "off", "one" or "all".
        public bool? Shuffle { get; internal set; }
        public string Repeat { get; internal set; }
        // The same song in the same app has the same key.
        public string Key { get; internal set; }
        internal DateTime ReadAt;
        internal object Source;

        // Where it's got to by now: it plays on from where it was read.
        public double At()
        {
            double at = Position + (Playing ? Math.Max(0, (DateTime.UtcNow - ReadAt).TotalSeconds) * (Rate > 0 ? Rate : 1) : 0);
            return Length > 0 ? Math.Min(at, Length) : at;
        }
    }

    // What's playing on the PC and the presses that go back to the app playing it, the same session her mini player reads. Presses
    // go off her window's thread and the next read shows them. On Mac and Linux nothing reads yet: Now is null, presses are false,
    // Levels are all 0, and Changed and Updated never fire.
    public interface ICapriMusic
    {
        // A new song, or null when nothing has a title any more.
        event Action<CapriTrack> Changed;
        // Every read, about once a second, and soon after a press.
        event Action<CapriTrack> Updated;
        CapriTrack Now { get; }
        // Every app with something in Windows' media session, Windows' own pick first.
        CapriTrack[] Sessions { get; }
        // Follows one app's session by its AppId, or null for whatever Windows picks.
        string Following { get; }
        void Follow(string appId);
        bool Play();
        bool Pause();
        bool Toggle();
        bool Next();
        bool Previous();
        bool Seek(double seconds);
        bool SetShuffle(bool on);
        bool SetRepeat(string mode);
        // Spotify's own heart for the song, or null outside Spotify or when it can't tell.
        bool? Liked { get; }
        bool Like(bool on);
        // The cover the app gave Windows for Now, as PNG or JPEG bytes, or null.
        byte[] Art { get; }
        // The app's own volume in Windows' volume mixer, 0 to 1, or -1 before it has made a sound.
        double Volume { get; set; }
        bool Muted { get; set; }
        // How loud the PC's sound is right now in that many bands from deep bass to treble, each 0 to 1, for a visualizer. It
        // listens only while a mod keeps asking, and stops a few seconds after the last ask.
        float[] Levels(int bands);
        float Loudness { get; }
        // False keeps her own mini player away while this mod runs, for a mod that shows its own.
        bool BuiltInPlayer { get; set; }
        // Brings the app playing it to the front.
        bool Open();
    }

    internal sealed partial class Mods
    {
        internal const int Api = 3;

        // What each permission lets a code mod do, as her approve box says it.
        internal static readonly string[,] PermissionWords =
        {
            { "chat", "read and change what you and Capri say to each other, and talk to her for you" },
            { "transcripts", "read her saved chat transcripts" },
            { "memory", "read, add, change and delete her notes about you and her memories" },
            { "screen", "take screenshots and see what she sees on your screen" },
            { "voice", "hear what your mic picks up and change what she says out loud" },
            { "personality", "add to her personality and change what she calls you" },
            { "settings", "read and change her settings, like her size and volume" },
            { "skins", "change her outfit and equip or take off her accessories" },
            { "brain", "ask her brain questions, which can use your API credits" },
            { "tools", "add tools her brain can use by itself, and see or stop the tools she's about to use" },
            { "system", "know when you're away or back, when your PC locks, sleeps or wakes, and its battery" },
            { "hotkeys", "add keyboard shortcuts that work in any app" },
            { "music", "see what's playing, control your music apps and their volume, and hear how loud your PC's sound is" }
        };

        internal static bool KnownPermission(string name)
        {
            for (int i = 0; i < PermissionWords.GetLength(0); i++) if (PermissionWords[i, 0] == name) return true;
            return false;
        }

        internal static string PermissionMeaning(string name)
        {
            for (int i = 0; i < PermissionWords.GetLength(0); i++) if (PermissionWords[i, 0] == name) return PermissionWords[i, 1];
            return name;
        }

        internal static string[] ReadPermissions(string value)
        {
            List<string> asked = new List<string>();
            foreach (string part in Regex.Split(value ?? "", @"[\s,]+"))
            {
                string name = part.Trim().ToLowerInvariant();
                if (name.Length == 0) continue;
                if (!KnownPermission(name))
                {
                    List<string> known = new List<string>();
                    for (int i = 0; i < PermissionWords.GetLength(0); i++) known.Add(PermissionWords[i, 0]);
                    throw new ModException("\"" + part.Trim() + "\" isn't a permission. A code mod can ask for " + string.Join(", ", known.ToArray()) + ".");
                }
                if (!asked.Contains(name)) asked.Add(name);
            }
            return asked.ToArray();
        }

        // What her box says before a code mod runs, the same on every platform: what it asks for, that it runs with full access to
        // the PC, and its fingerprint.
        internal static string Consent(ModFile mod, string hash)
        {
            string asks = "";
            foreach (string permission in mod.Permissions ?? new string[0]) asks += "\n- " + PermissionMeaning(permission);
            return mod.Name + (string.IsNullOrEmpty(mod.Version) ? "" : " v" + mod.Version) + (string.IsNullOrEmpty(mod.Author) ? "" : ", which says it's by " + mod.Author) + ", is a code mod.\n\n"
                + (asks.Length == 0 ? "It asks for none of Capri's permissions.\n\n" : "Through Capri, it asks to:" + asks + "\n\n")
                + "Code mods run as part of Capri with full access to your PC: your files, your accounts and the API keys she has saved. Nothing limits what it can do, and Capri doesn't check whether it's safe.\n\n"
                + "Only run it if you wrote it, or you've read it and trust who did.\n\nFile fingerprint (SHA-256):\n" + hash + "\n\nRun this code?";
        }
    }

    // A code mod's own saved values, kept in store.json in its folder between runs. The same on every platform.
    internal sealed class ModStore : ICapriStore
    {
        private readonly object gate = new object();
        private readonly Func<string> folder;
        private readonly string id;
        private Dictionary<string, string> store;

        internal ModStore(Func<string> modFolder, string modId)
        {
            folder = modFolder;
            id = modId;
        }

        private static System.Runtime.Serialization.Json.DataContractJsonSerializer Serializer()
        {
            return new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Dictionary<string, string>), new System.Runtime.Serialization.Json.DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
        }

        private Dictionary<string, string> Stored()
        {
            if (store != null) return store;
            store = new Dictionary<string, string>();
            string file = System.IO.Path.Combine(folder(), "store.json");
            try
            {
                if (System.IO.File.Exists(file))
                    using (System.IO.FileStream reading = System.IO.File.OpenRead(file))
                    {
                        Dictionary<string, string> read = Serializer().ReadObject(reading) as Dictionary<string, string>;
                        if (read != null) store = read;
                    }
            }
            catch (Exception error)
            {
                if (!(error is System.IO.IOException || error is UnauthorizedAccessException || error is System.Runtime.Serialization.SerializationException || error is System.Xml.XmlException)) throw;
                Brain.Log("mod " + id, -1, "Its store.json couldn't be read, so it starts empty: " + error.Message);
            }
            return store;
        }

        private void Save()
        {
            string file = System.IO.Path.Combine(folder(), "store.json"), writing = file + ".new";
            try
            {
                using (System.IO.FileStream output = System.IO.File.Create(writing)) Serializer().WriteObject(output, store);
                if (System.IO.File.Exists(file)) System.IO.File.Replace(writing, file, null); else System.IO.File.Move(writing, file);
            }
            catch (System.IO.IOException error) { Brain.Log("mod " + id, -1, "Its store.json couldn't be saved: " + error.Message); }
            catch (UnauthorizedAccessException error) { Brain.Log("mod " + id, -1, "Its store.json couldn't be saved: " + error.Message); }
        }

        private static string Key(string key)
        {
            string clean = (key ?? "").Trim();
            if (clean.Length == 0 || clean.Length > 100) throw new ArgumentException("A store key is 1 to 100 characters.");
            return clean;
        }

        public string[] Keys { get { lock (gate) { List<string> keys = new List<string>(Stored().Keys); keys.Sort(StringComparer.Ordinal); return keys.ToArray(); } } }
        public string Get(string key) { return Get(key, null); }

        public string Get(string key, string fallback)
        {
            string found;
            lock (gate) return Stored().TryGetValue(Key(key), out found) ? found : fallback;
        }

        public double Number(string key, double fallback)
        {
            double number;
            string found = Get(key, null);
            return found != null && double.TryParse(found, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number) ? number : fallback;
        }

        public bool Flag(string key, bool fallback)
        {
            string found = Get(key, null);
            return found == "true" ? true : found == "false" ? false : fallback;
        }

        public void Set(string key, string value)
        {
            string named = Key(key);
            if (value == null) { Remove(named); return; }
            if (value.Length > 64 * 1024) throw new ArgumentException("A stored value is at most 64 KB.");
            lock (gate)
            {
                Dictionary<string, string> kept = Stored();
                string was;
                if (kept.TryGetValue(named, out was) && was == value) return;
                long total = value.Length;
                foreach (KeyValuePair<string, string> entry in kept) if (entry.Key != named) total += entry.Key.Length + entry.Value.Length;
                if (total > 1024 * 1024) throw new InvalidOperationException("A mod's store holds at most 1 MB.");
                kept[named] = value;
                Save();
            }
        }

        public void Set(string key, double value) { Set(key, value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)); }
        public void Set(string key, bool value) { Set(key, value ? "true" : "false"); }

        public bool Remove(string key)
        {
            lock (gate)
            {
                if (!Stored().Remove(Key(key))) return false;
                Save();
                return true;
            }
        }
    }

    internal static class ModSound
    {
        // A WAV file in the mod's folder, 8, 16, 24 or 32 bit or float, made into her own 24 kHz sound, at most 30 seconds.
        internal static short[] ReadWave(string file, double volume)
        {
            byte[] bytes;
            try { bytes = System.IO.File.ReadAllBytes(file); }
            catch (System.IO.IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
            if (bytes.Length < 44 || System.Text.Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF" || System.Text.Encoding.ASCII.GetString(bytes, 8, 4) != "WAVE") return null;
            int format = 0, channels = 0, rate = 0, bits = 0, dataAt = -1, dataLength = 0;
            for (int at = 12; at + 8 <= bytes.Length;)
            {
                string chunk = System.Text.Encoding.ASCII.GetString(bytes, at, 4);
                int length = BitConverter.ToInt32(bytes, at + 4);
                if (length < 0) return null;
                if (chunk == "fmt " && at + 24 <= bytes.Length)
                {
                    format = BitConverter.ToUInt16(bytes, at + 8);
                    channels = BitConverter.ToUInt16(bytes, at + 10);
                    rate = BitConverter.ToInt32(bytes, at + 12);
                    bits = BitConverter.ToUInt16(bytes, at + 22);
                    if (format == 0xFFFE && at + 34 <= bytes.Length) format = BitConverter.ToUInt16(bytes, at + 32);
                }
                else if (chunk == "data") { dataAt = at + 8; dataLength = Math.Min(length, bytes.Length - dataAt); break; }
                at += 8 + length + (length & 1);
            }
            bool floats = format == 3 && bits == 32;
            if (dataAt < 0 || channels < 1 || channels > 8 || rate < 4000 || rate > 192000 || !(format == 1 && (bits == 8 || bits == 16 || bits == 24 || bits == 32) || floats)) return null;
            int step = bits / 8 * channels, frames = dataLength / step;
            int outCount = (int)Math.Min((long)frames * Sfx.Rate / rate, Sfx.Rate * 30L);
            if (outCount <= 0) return null;
            double gain = Math.Max(0, Math.Min(1, volume)) * Math.Min(100, Sfx.Level) / 100.0;
            short[] samples = new short[outCount];
            for (int i = 0; i < outCount; i++)
            {
                int frame = (int)Math.Min(frames - 1, (long)i * rate / Sfx.Rate);
                double sum = 0;
                for (int c = 0; c < channels; c++)
                {
                    int at = dataAt + frame * step + c * bits / 8;
                    double sample;
                    if (floats) sample = BitConverter.ToSingle(bytes, at);
                    else if (bits == 8) sample = (bytes[at] - 128) / 128.0;
                    else if (bits == 16) sample = BitConverter.ToInt16(bytes, at) / 32768.0;
                    else if (bits == 24) sample = ((bytes[at] << 8 | bytes[at + 1] << 16 | bytes[at + 2] << 24) >> 8) / 8388608.0;
                    else sample = BitConverter.ToInt32(bytes, at) / 2147483648.0;
                    sum += sample;
                }
                samples[i] = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, sum / channels * gain * 32767));
            }
            return samples;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace CapriDesktop
{
    internal sealed class CallApp
    {
        internal readonly string Choice, Name;
        internal readonly string[] Names;

        internal CallApp(string choice, string name, params string[] names) { Choice = choice; Name = name; Names = names; }
        internal string Caption { get { return Name.ToUpperInvariant(); } }
        internal string Label { get { return Name == "Discord" ? Name : Caption; } }
        internal string NotOpen { get { return Name + " isn't open."; } }
        // Browsers and Steam play videos and games all day, so Auto never takes them for a call: you pick them yourself.
        internal bool InAuto { get { return Name != "Browser" && Name != "Steam"; } }
        internal bool Has(string name)
        {
            if (name != null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
            foreach (string own in Names) if (string.Equals(own, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }

    internal sealed class CallProcess
    {
        internal int Id, Parent;
        internal string Name;
        internal DateTime Started;
    }

    internal sealed class CallPick
    {
        internal CallApp App;
        internal int Id;
    }

    internal static class CallApps
    {
        internal static readonly CallApp[] Apps = {
            new CallApp("Discord", "Discord", "Discord", "DiscordPTB", "DiscordCanary"),
            new CallApp("Zoom", "Zoom", "Zoom"),
            new CallApp("Microsoft Teams", "Teams", "ms-teams", "Teams"),
            new CallApp("WhatsApp", "WhatsApp", "WhatsApp"),
            new CallApp("Telegram", "Telegram", "Telegram"),
            new CallApp("Slack", "Slack", "slack"),
            new CallApp("Signal", "Signal", "Signal"),
            new CallApp("Skype", "Skype", "Skype", "SkypeApp"),
            new CallApp("TeamSpeak", "TeamSpeak", "ts3client_win64", "ts3client_win32", "TeamSpeak"),
            new CallApp("Mumble", "Mumble", "mumble"),
            new CallApp("Steam voice chat", "Steam", "steam", "steamwebhelper"),
            new CallApp("Browser (Google Meet, web calls)", "Browser", "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "MicrosoftEdge", "MicrosoftEdgeCP")
        };
        internal static readonly string[] Choices = MakeChoices();
        internal static Func<List<CallProcess>> Processes = Scan;
        internal static Func<List<AppSound>> Sounds = Mixer.Sounds;
        private static readonly object gate = new object();
        private static int setting;
        private static volatile string choice = "Auto";
        private static volatile CallApp current = Apps[0];
        // The process tree her call ears have open (0 when none), and how many looks in a row found another app instead.
        private static int hearing, drift;
        private static object owner;

        internal static string Choice { get { return choice; } }
        internal static CallApp Current { get { return current; } }
        internal const string Note = "Browser captures sound from all tabs.";

        private static string[] MakeChoices()
        {
            string[] names = new string[Apps.Length + 1];
            names[0] = "Auto";
            for (int i = 0; i < Apps.Length; i++) names[i + 1] = Apps[i].Choice;
            return names;
        }

        internal static CallApp Get(string name)
        {
            foreach (CallApp app in Apps)
                if (string.Equals(name, app.Choice, StringComparison.OrdinalIgnoreCase) || string.Equals(name, app.Name, StringComparison.OrdinalIgnoreCase)) return app;
            return null;
        }

        internal static string Clean(string name) { CallApp app = Get(name); return app == null ? "Auto" : app.Choice; }

        // Picking Auto mid-call keeps the app she's hearing, so it still holds her while it makes sound instead of handing her to
        // Discord. Off a call, Auto starts from Discord as before.
        internal static void Choose(string name)
        {
            lock (gate) { setting++; choice = Clean(name); current = Get(choice) ?? (hearing != 0 ? current : Apps[0]); drift = 0; }
        }

        internal static int Find()
        {
            int version, keep;
            string selected;
            lock (gate) { version = setting; selected = choice; keep = hearing; }
            List<CallProcess> processes = Processes();
            CallPick picked = Pick(selected, processes, null);
            // Discord counts by its process alone, so while she's hearing another call, its sound gets checked first: Discord
            // opening or restarting mid-call doesn't take her off it.
            bool holding = selected == "Auto" && keep != 0 && current.InAuto && current != Apps[0];
            if (selected == "Auto" ? picked == null || holding : Get(selected) != Apps[0])
            {
                List<AppSound> sounds = Sounds();
                // In Auto, the app she's hearing keeps her while it still makes sound, even when an app higher on the list does too.
                CallApp now = current;
                CallPick still = selected == "Auto" && keep != 0 && now.InAuto ? PickApp(now, processes, sounds, true) : null;
                if (still != null && still.Id == keep) picked = still;
                // A quiet spell on her call, like you talking or a pause, isn't the call moving: only another call app's sound
                // takes her off it, so a silent Discord that's open can't take her for good.
                else if (holding) picked = Audible(processes, sounds);
                else picked = Pick(selected, processes, sounds);
            }
            lock (gate)
            {
                if (setting != version) return 0;
                bool held = selected == "Auto" && keep != 0 && keep == hearing;
                if (picked == null) { if (held) drift = 0; return 0; }
                // A ping or a notification in another app doesn't pull her off the call she's hearing: Auto moves after three looks in a row, about three seconds.
                if (held && picked.Id != keep && ++drift < 3) return keep;
                drift = 0;
                current = picked.App;
                return picked.Id;
            }
        }

        // Her call ears say which process tree they opened, and let go of it when they close.
        internal static void Heard(int pid, object who) { lock (gate) { hearing = pid; owner = who; drift = 0; } }
        internal static void Left(int pid, object who) { lock (gate) if (hearing == pid && object.ReferenceEquals(owner, who)) { hearing = 0; owner = null; drift = 0; } }

        internal static CallPick Pick(string selected, List<CallProcess> processes, List<AppSound> sounds)
        {
            if (processes == null) return null;
            CallApp app = Get(selected);
            if (app != null) return PickApp(app, processes, app == Apps[0] ? null : sounds, false);
            CallPick discord = PickApp(Apps[0], processes, null, false);
            if (discord != null) return discord;
            foreach (CallApp other in Apps)
            {
                if (!other.InAuto) continue;
                CallPick picked = PickApp(other, processes, sounds, true);
                if (picked != null) return picked;
            }
            return null;
        }

        // The first call app in Auto that's making sound, Discord included, or null when none is.
        private static CallPick Audible(List<CallProcess> processes, List<AppSound> sounds)
        {
            if (processes == null) return null;
            foreach (CallApp app in Apps)
            {
                if (!app.InAuto) continue;
                CallPick heard = PickApp(app, processes, sounds, true);
                if (heard != null) return heard;
            }
            return null;
        }

        private static CallPick PickApp(CallApp app, List<CallProcess> processes, List<AppSound> sounds, bool audible)
        {
            Dictionary<int, CallProcess> byId = new Dictionary<int, CallProcess>();
            foreach (CallProcess process in processes) byId[process.Id] = process;
            bool steamHelper = app.Name == "Steam" && processes.Exists(delegate(CallProcess process) { return string.Equals(process.Name, "steamwebhelper.exe", StringComparison.OrdinalIgnoreCase) || string.Equals(process.Name, "steamwebhelper", StringComparison.OrdinalIgnoreCase); });
            CallProcess first = null, playing = null;
            foreach (CallProcess process in processes)
            {
                if (!app.Has(process.Name)) continue;
                if (steamHelper && !process.Name.StartsWith("steamwebhelper", StringComparison.OrdinalIgnoreCase)) continue;
                CallProcess root = process, parent = process;
                HashSet<int> seen = new HashSet<int>();
                while (seen.Add(parent.Id) && byId.TryGetValue(parent.Parent, out parent))
                    if (app.Has(parent.Name) && parent.Started <= root.Started && (!steamHelper || parent.Name.StartsWith("steamwebhelper", StringComparison.OrdinalIgnoreCase))) root = parent;
                if (first == null || root.Started < first.Started) first = root;
                if (sounds == null) continue;
                foreach (AppSound sound in sounds)
                {
                    if (!sound.Playing || sound.Muted || sound.Level <= 0 || sound.Peak <= 0.00001f) continue;
                    int id = sound.Id;
                    seen.Clear();
                    while (seen.Add(id))
                    {
                        if (id == root.Id) { if (playing == null || root.Started < playing.Started) playing = root; break; }
                        CallProcess child;
                        if (!byId.TryGetValue(id, out child)) break;
                        id = child.Parent;
                    }
                }
            }
            CallProcess found = playing ?? (audible ? null : first);
            return found == null ? null : new CallPick { App = app, Id = found.Id };
        }

        // Start times from the last look, by process: they never change, and asking Windows again for every browser tab each second adds up.
        private static Dictionary<int, CallProcess> starts = new Dictionary<int, CallProcess>();
        private static readonly object scanGate = new object();

        private static List<CallProcess> Scan()
        {
            List<CallProcess> processes = new List<CallProcess>();
            IntPtr snapshot = CreateToolhelp32Snapshot(2, 0);
            if (snapshot == new IntPtr(-1)) return processes;
            lock (scanGate)
            {
                Dictionary<int, CallProcess> seen = new Dictionary<int, CallProcess>();
                try
                {
                    ProcessEntry entry = new ProcessEntry { Size = Marshal.SizeOf(typeof(ProcessEntry)) };
                    if (Process32FirstW(snapshot, ref entry))
                        do
                        {
                            DateTime started = DateTime.MinValue;
                            bool known = false;
                            foreach (CallApp app in Apps) known |= app.Has(entry.File);
                            CallProcess before;
                            if (known && starts.TryGetValue(entry.Id, out before) && before.Parent == entry.Parent && string.Equals(before.Name, entry.File, StringComparison.OrdinalIgnoreCase)) started = before.Started;
                            else if (known)
                            {
                                try { using (Process process = Process.GetProcessById(entry.Id)) started = process.StartTime; }
                                catch (Exception) { continue; }
                            }
                            CallProcess found = new CallProcess { Id = entry.Id, Parent = entry.Parent, Name = entry.File, Started = started };
                            if (known) seen[entry.Id] = found;
                            processes.Add(found);
                        }
                        while (Process32NextW(snapshot, ref entry));
                }
                finally { CloseHandle(snapshot); }
                starts = seen;
            }
            return processes;
        }

        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(int flags, int processId);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry entry);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry entry);
        [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ProcessEntry
        {
            internal int Size, Usage, Id;
            internal UIntPtr Heap;
            internal int Module, Threads, Parent, Priority, Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string File;
        }
    }

    // The people on your call: Discord's own sound, which Windows hands her apart from everything else the PC plays (the
    // per-app capture OBS's Application Audio Capture uses), so in call mode she hears them as well as you (Lachy, 3 Nov:
    // "I don't think what they say is picked up rn"). Her voice reaches the call through your mic, and Discord never plays
    // your own mic back to you, so she doesn't hear herself in it.
    internal sealed class CallEars : IDisposable
    {
        internal const string NotOpen = "Discord isn't open.";
        // Discord and its test builds.
        private readonly object gate = new object();
        // Two seconds of Discord at her ears' rate. Only the newest two chunks of it are ever mixed in.
        private readonly short[] ring = new short[Ears.Rate * 2];
        private readonly CallEcho echo = new CallEcho();
        private int start, count;
        private double heard, level;
        private int heardCount, levelAt = Environment.TickCount - 100000;
        private volatile bool running;
        private volatile string problem = NotOpen;
        private int generation;

        // The process whose sound is Discord's, 0 when it isn't open. The smoke test stands in for it.
        internal static Func<int> Find = CallApps.Find;

        internal bool Hearing { get { return running && problem == null; } }

        // Why she can't hear Discord while she's trying to, or null.
        internal string Problem { get { return running ? problem : null; } }

        // Discord's loudness over its last tenth of a second, or 0 when nothing has come in for that long.
        internal double Level { get { lock (gate) return Environment.TickCount - levelAt < 300 ? level : 0; } }

        internal void Start()
        {
            if (running) return;
            running = true;
            problem = CallApps.Current.NotOpen;
            int mine = Interlocked.Increment(ref generation);
            new Thread(delegate() { Listen(mine); }) { IsBackground = true, Name = "Capri call ears" }.Start();
        }

        // Her thread sees this within 10 ms, or once Windows answers a capture it's opening, and closes the capture itself.
        internal void Stop()
        {
            running = false;
            Interlocked.Increment(ref generation);
            lock (gate) { start = count = 0; heard = 0; heardCount = 0; level = 0; echo.Clear(); }
        }

        public void Dispose() { Stop(); }

        private bool Live(int mine) { return running && Volatile.Read(ref generation) == mine; }

        private void Listen(int mine)
        {
            object owner = new object();
            string told = null;
            while (Live(mine))
            {
                int pid = 0;
                try { pid = Find(); }
                catch (Exception) { }
                if (!Live(mine)) break;
                if (pid == 0) { Say(mine, CallApps.Current.NotOpen); Rest(mine, 500); continue; }
                CallApp app = CallApps.Current;
                Tap tap = null;
                try
                {
                    string failed;
                    tap = Tap.Open(pid, app.Name, out failed);
                    if (tap == null)
                    {
                        Say(mine, failed);
                        if (failed != told) Brain.Log("call ears", -1, failed);
                        told = failed;
                        Rest(mine, 15000);
                        continue;
                    }
                    told = null;
                    CallApps.Heard(pid, owner);
                    Say(mine, null);
                    Brain.Log("call ears", -1, "Hearing " + app.Name + ": process " + pid + " and the ones it started, as 16000 Hz mono 16-bit PCM.");
                    int lookedAt = Environment.TickCount;
                    while (Live(mine))
                    {
                        Thread.Sleep(10);
                        if (!tap.Read(this, mine)) { Brain.Log("call ears", -1, app.Name + "'s sound stopped coming. Opening it again."); break; }
                        if (Environment.TickCount - lookedAt < 1000) continue;
                        lookedAt = Environment.TickCount;
                        if (Gone(pid)) break;
                        int next = Find();
                        if (next != 0 && (next != pid || CallApps.Current != app))
                        {
                            lock (gate) { start = count = 0; heard = 0; heardCount = 0; level = 0; echo.Clear(); }
                            break;
                        }
                    }
                }
                catch (Exception error)
                {
                    Say(mine, "Couldn't hear " + app.Name + ": " + Voice.Short(error.Message));
                    Brain.Log("call ears", -1, error.Message);
                    Rest(mine, 5000);
                }
                finally { if (tap != null) { tap.Dispose(); CallApps.Left(pid, owner); } }
            }
        }

        private void Say(int mine, string why) { if (Live(mine)) problem = why; }

        private void Rest(int mine, int milliseconds)
        {
            for (int waited = 0; waited < milliseconds && Live(mine); waited += 50) Thread.Sleep(50);
        }

        private static bool Gone(int pid)
        {
            try { using (Process process = Process.GetProcessById(pid)) return process.HasExited; }
            catch (ArgumentException) { return true; }
            catch (Exception) { return false; }
        }
        // What Discord played, onto the end of her ring, with its loudness kept every tenth of a second.
        internal void Add(short[] samples, int length, int? mine = null)
        {
            lock (gate)
            {
                if (mine.HasValue && !Live(mine.Value)) return;
                for (int i = 0; i < length; i++)
                {
                    ring[(start + count) % ring.Length] = samples[i];
                    if (count < ring.Length) count++;
                    else start = (start + 1) % ring.Length;
                    double sample = samples[i] / 32768.0;
                    heard += sample * sample;
                    if (++heardCount < Ears.Chunk) continue;
                    level = Math.Sqrt(heard / heardCount);
                    levelAt = Environment.TickCount;
                    heard = 0;
                    heardCount = 0;
                }
            }
        }

        // Your mic's chunk with the same stretch of Discord added to it, and how loud each was, to tell whose line it is. Anything
        // older than the newest two chunks of Discord is dropped first, so it never falls behind your voice. Discord's stretch
        // on its own goes in alone when it's given, for CallOver while she talks.
        internal short[] Mix(short[] chunk, out double mine, out double theirs, short[] alone = null)
        {
            short[] mixed = new short[chunk.Length];
            double own = 0, call = 0;
            lock (gate)
            {
                int extra = count - 2 * chunk.Length;
                if (extra > 0) { start = (start + extra) % ring.Length; count -= extra; }
                for (int i = 0; i < chunk.Length; i++)
                {
                    int theirSample = 0;
                    if (count > 0) { theirSample = ring[start]; start = (start + 1) % ring.Length; count--; }
                    own += (double)chunk[i] * chunk[i];
                    call += (double)theirSample * theirSample;
                    mixed[i] = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, chunk[i] + theirSample));
                    if (alone != null && i < alone.Length) alone[i] = (short)theirSample;
                }
            }
            mine = chunk.Length == 0 ? 0 : Math.Sqrt(own / chunk.Length) / 32768.0;
            theirs = chunk.Length == 0 ? 0 : Math.Sqrt(call / chunk.Length) / 32768.0;
            return mixed;
        }

        internal CallChunk Split(short[] chunk)
        {
            lock (gate)
            {
                double mine, theirs;
                short[] call = new short[chunk.Length];
                Mix(chunk, out mine, out theirs, call);
                return echo.Split(chunk, call);
            }
        }

        [DllImport("Mmdevapi.dll", ExactSpelling = true)]
        private static extern int ActivateAudioInterfaceAsync([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid iid, IntPtr parameters, IActivated done, out IActivation operation);

        // Windows' per-app capture of one process and the ones it started, converted by Windows to her ears' 16 kHz mono 16-bit PCM.
        private sealed class Tap : IDisposable
        {
            private ISoundClient client;
            private ISoundCapture capture;
            private short[] samples = new short[0];

            internal static Tap Open(int pid, string app, out string failed)
            {
                failed = null;
                IntPtr parameters = Marshal.AllocHGlobal(12), blob = Marshal.AllocHGlobal(8 + 2 * IntPtr.Size), format = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WaveFormat)));
                ISoundClient client = null;
                IActivation operation = null;
                CallActivated done = new CallActivated();
                try
                {
                    // AUDIOCLIENT_ACTIVATION_PARAMS: process loopback (1) of this process, its children included (0).
                    Marshal.WriteInt32(parameters, 0, 1);
                    Marshal.WriteInt32(parameters, 4, pid);
                    Marshal.WriteInt32(parameters, 8, 0);
                    // A PROPVARIANT holding them as a VT_BLOB.
                    for (int i = 0; i < 8 + 2 * IntPtr.Size; i++) Marshal.WriteByte(blob, i, 0);
                    Marshal.WriteInt16(blob, 0, 65);
                    Marshal.WriteInt32(blob, 8, 12);
                    Marshal.WriteIntPtr(blob, 8 + IntPtr.Size, parameters);
                    Guid clientId = typeof(ISoundClient).GUID;
                    int result;
                    try { result = ActivateAudioInterfaceAsync(@"VAD\Process_Loopback", ref clientId, blob, done, out operation); }
                    catch (EntryPointNotFoundException) { failed = Refused(-1, app); return null; }
                    if (result != 0) { failed = Refused(result, app); return null; }
                    if (!done.Finished.WaitOne(5000)) { failed = "Windows took too long to hand over " + app + "'s sound."; return null; }
                    int activated;
                    object found;
                    result = operation.GetActivateResult(out activated, out found);
                    if (result != 0 || activated != 0 || found == null) { Wasapi.Release(found); failed = Refused(result != 0 ? result : activated, app); return null; }
                    client = (ISoundClient)found;
                    Marshal.StructureToPtr(WaveFormat.Mono(Ears.Rate), format, false);
                    // Loopback, with Windows converting to her rate and one channel at its default quality.
                    result = client.Initialize(0, 0x00020000 | unchecked((int)0x80000000) | 0x08000000, 1000000, 0, format, IntPtr.Zero);
                    if (result != 0) { failed = Refused(result, app); return null; }
                    Guid captureId = typeof(ISoundCapture).GUID;
                    object service;
                    result = client.GetService(ref captureId, out service);
                    if (result != 0) { failed = Refused(result, app); return null; }
                    result = client.Start();
                    if (result != 0) { Wasapi.Release(service); failed = Refused(result, app); return null; }
                    Tap tap = new Tap { client = client, capture = (ISoundCapture)service };
                    client = null;
                    return tap;
                }
                finally
                {
                    Marshal.FreeHGlobal(parameters);
                    Marshal.FreeHGlobal(blob);
                    Marshal.FreeHGlobal(format);
                    Wasapi.Release(client);
                    Wasapi.Release(operation);
                    done.Dispose();
                }
            }

            private static string Refused(int result, string app)
            {
                return "Windows wouldn't hand her " + app + "'s sound" + (result == -1 ? "" : " (0x" + result.ToString("X8") + ")") + ". It needs Windows 11 or an up-to-date Windows 10.";
            }

            // Everything Windows has for her since the last read, into her ring. False when the capture broke.
            internal bool Read(CallEars into, int mine)
            {
                while (true)
                {
                    if (!into.Live(mine)) return true;
                    int waiting;
                    if (capture.GetNextPacketSize(out waiting) != 0) return false;
                    if (waiting == 0) return true;
                    IntPtr data;
                    int frames, flags;
                    long position, time;
                    if (capture.GetBuffer(out data, out frames, out flags, out position, out time) != 0) return false;
                    try
                    {
                        if (samples.Length < frames) samples = new short[frames];
                        if ((flags & 2) != 0 || data == IntPtr.Zero) Array.Clear(samples, 0, frames);
                        else Marshal.Copy(data, samples, 0, frames);
                        into.Add(samples, frames, mine);
                    }
                    finally { capture.ReleaseBuffer(frames); }
                }
            }

            public void Dispose()
            {
                try { if (client != null) client.Stop(); }
                catch (Exception) { }
                Wasapi.Release(capture);
                Wasapi.Release(client);
                capture = null;
                client = null;
            }
        }

        private static void SmokeApps(List<string> checks)
        {
            string[][] names = {
                new[] { "Discord", "Discord", "DiscordPTB", "DiscordCanary" }, new[] { "Zoom", "Zoom" },
                new[] { "Microsoft Teams", "ms-teams", "Teams" }, new[] { "WhatsApp", "WhatsApp" },
                new[] { "Telegram", "Telegram" }, new[] { "Slack", "slack" }, new[] { "Signal", "Signal" },
                new[] { "Skype", "Skype", "SkypeApp" }, new[] { "TeamSpeak", "ts3client_win64", "ts3client_win32", "TeamSpeak" },
                new[] { "Mumble", "mumble" }, new[] { "Steam voice chat", "steam", "steamwebhelper" },
                new[] { "Browser (Google Meet, web calls)", "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "MicrosoftEdge", "MicrosoftEdgeCP" }
            };
            if (ChoicesWrong(names)) throw new Exception("The call app choices changed.");
            foreach (string[] app in names)
                for (int i = 1; i < app.Length; i++)
                {
                    List<CallProcess> processes = new List<CallProcess> { new CallProcess { Id = 12, Name = app[i].ToUpperInvariant() + ".exe", Started = new DateTime(2026, 11, 4) } };
                    CallPick picked = CallApps.Pick(app[0], processes, null);
                    if (picked == null || picked.Id != 12 || picked.App.Choice != app[0]) throw new Exception("Call app didn't resolve " + app[i] + ".");
                }
            checks.Add("PASS call app names: every listed app and its Windows process names resolve, including old and new Teams, Skype, TeamSpeak and Edge builds");

            List<CallProcess> open = new List<CallProcess> {
                new CallProcess { Id = 1, Name = "Discord", Started = new DateTime(2026, 11, 1) },
                new CallProcess { Id = 2, Parent = 1, Name = "Discord", Started = new DateTime(2026, 11, 2) },
                new CallProcess { Id = 10, Name = "Teams", Started = new DateTime(2026, 11, 1) },
                new CallProcess { Id = 20, Name = "Zoom", Started = new DateTime(2026, 11, 2) },
                new CallProcess { Id = 21, Parent = 20, Name = "CptHost", Started = new DateTime(2026, 11, 3) }
            };
            List<AppSound> sounds = new List<AppSound> { new AppSound { Id = 21, App = "CptHost.exe", Playing = true, Level = 1, Peak = 0.2f } };
            CallPick pickedAuto = CallApps.Pick("Auto", open, sounds);
            if (pickedAuto == null || pickedAuto.Id != 1 || pickedAuto.App.Name != "Discord") throw new Exception("Auto didn't prefer open Discord.");
            open.RemoveRange(0, 2);
            pickedAuto = CallApps.Pick("Auto", open, sounds);
            if (pickedAuto == null || pickedAuto.Id != 20 || pickedAuto.App.Name != "Zoom") throw new Exception("Auto didn't choose the app making sound through its child.");
            sounds[0].Muted = true;
            if (CallApps.Pick("Auto", open, sounds) != null) throw new Exception("Auto chose a muted app.");
            sounds[0].Muted = false; sounds[0].Peak = 0;
            if (CallApps.Pick("Auto", open, sounds) != null) throw new Exception("Auto chose an active but silent session.");
            sounds[0].Peak = 0.2f; sounds[0].Playing = false;
            if (CallApps.Pick("Auto", open, sounds) != null) throw new Exception("Auto chose an inactive session.");
            sounds[0].Playing = true;
            if (CallApps.Pick("Microsoft Teams", open, sounds).Id != 10 || CallApps.Pick("Discord", open, sounds) != null) throw new Exception("An explicit call app followed another app's sound.");
            open.Add(new CallProcess { Id = 30, Name = "ms-teams", Started = new DateTime(2026, 11, 3) });
            open.Add(new CallProcess { Id = 31, Parent = 30, Name = "msedgewebview2", Started = new DateTime(2026, 11, 3) });
            sounds[0].Id = 31;
            if (CallApps.Pick("Auto", open, sounds).Id != 30) throw new Exception("New Teams lost its WebView sound.");
            open.Clear();
            open.Add(new CallProcess { Id = 40, Name = "chrome", Started = new DateTime(2026, 11, 1) });
            open.Add(new CallProcess { Id = 50, Name = "chrome", Started = new DateTime(2026, 11, 2) });
            open.Add(new CallProcess { Id = 51, Parent = 50, Name = "chrome", Started = new DateTime(2026, 11, 3) });
            sounds[0].Id = 51;
            if (CallApps.Pick("Browser", open, sounds).Id != 50) throw new Exception("Browser capture chose the silent browser tree.");
            open.Add(new CallProcess { Id = 60, Name = "steam", Started = new DateTime(2026, 11, 1) });
            open.Add(new CallProcess { Id = 61, Parent = 60, Name = "steamwebhelper", Started = new DateTime(2026, 11, 2) });
            sounds[0].Id = 61;
            if (CallApps.Pick("Steam voice chat", open, sounds).Id != 61) throw new Exception("Steam voice captured the launcher and its games instead of its web helper.");
            checks.Add("PASS Auto keeps Discord first, otherwise follows audible call process trees; silent, muted and inactive apps stay unpicked, explicit choices stay fixed, and browser and Steam voice capture choose the sounding tree");

            string was = CallApps.Choice;
            Func<List<CallProcess>> processesWas = CallApps.Processes;
            Func<List<AppSound>> soundsWas = CallApps.Sounds;
            Func<int> findWas = Find;
            object owner = new object(), nextOwner = new object();
            try
            {
                CallApps.Processes = delegate { return new List<CallProcess> { new CallProcess { Id = 71, Name = "Zoom" } }; };
                CallApps.Sounds = delegate { return new List<AppSound> { new AppSound { Id = 71, Playing = true, Level = 1, Peak = 0.1f } }; };
                CallApps.Choose("Auto");
                if (CallApps.Find() != 71 || CallApps.Current.Name != "Zoom") throw new Exception("The live call finder didn't use its fake processes and sounds.");
                CallApps.Choose("Zoom");
                Find = delegate { return 0; };
                using (CallEars ears = new CallEars())
                {
                    ears.Start();
                    if (ears.Problem != "Zoom isn't open." || ears.Hearing) throw new Exception("Zoom's missing-app line changed.");
                    ears.Stop();
                }
                CallApps.Processes = delegate
                {
                    CallApps.Choose("Microsoft Teams");
                    return new List<CallProcess> { new CallProcess { Id = 71, Name = "Zoom" } };
                };
                CallApps.Choose("Zoom");
                if (CallApps.Find() != 0 || CallApps.Current.Name != "Teams") throw new Exception("An old app lookup overwrote a new call app choice.");

                List<CallProcess> two = new List<CallProcess> { new CallProcess { Id = 90, Name = "Zoom" }, new CallProcess { Id = 91, Name = "slack" }, new CallProcess { Id = 92, Name = "chrome" } };
                AppSound zoom = new AppSound { Id = 90, Playing = true, Level = 1, Peak = 0.1f }, slack = new AppSound { Id = 91, Playing = true, Level = 1, Peak = 0.1f }, video = new AppSound { Id = 92, Playing = true, Level = 1, Peak = 0.1f };
                List<AppSound> playing = new List<AppSound> { video };
                CallApps.Processes = delegate { return two; };
                CallApps.Sounds = delegate { return new List<AppSound>(playing); };
                CallApps.Choose("Auto");
                if (CallApps.Find() != 0) throw new Exception("Auto took a browser playing a video for the call.");
                playing.Add(zoom);
                if (CallApps.Find() != 90 || CallApps.Current.Name != "Zoom") throw new Exception("Auto didn't take Zoom next to a browser video.");
                CallApps.Heard(90, owner);
                playing.Remove(zoom); playing.Add(slack);
                if (CallApps.Find() != 90 || CallApps.Find() != 90 || CallApps.Current.Name != "Zoom") throw new Exception("A ping in another app pulled Auto off the call.");
                if (CallApps.Find() != 91 || CallApps.Current.Name != "Slack") throw new Exception("Auto never moved to the app that kept making sound.");
                CallApps.Left(90, owner);
                CallApps.Heard(91, owner);
                playing.Add(zoom);
                if (CallApps.Find() != 91 || CallApps.Find() != 91 || CallApps.Find() != 91 || CallApps.Current.Name != "Slack") throw new Exception("Auto left the app she's hearing while it still made sound.");
                CallApps.Left(91, owner);
                if (CallApps.Find() != 90 || CallApps.Current.Name != "Zoom") throw new Exception("Auto stayed with an app after her ears let go of it.");
                CallApps.Heard(90, owner);
                two.Add(new CallProcess { Id = 93, Name = "Discord" });
                if (CallApps.Find() != 90 || CallApps.Find() != 90 || CallApps.Find() != 90 || CallApps.Find() != 90 || CallApps.Current.Name != "Zoom") throw new Exception("A silent Discord opening pulled Auto off the call she's hearing.");
                playing.Remove(zoom); playing.Remove(slack);
                if (CallApps.Find() != 0 || CallApps.Find() != 0 || CallApps.Find() != 0 || CallApps.Find() != 0 || CallApps.Current.Name != "Zoom") throw new Exception("A quiet spell on the call she's hearing moved Auto to a silent Discord.");
                AppSound discord = new AppSound { Id = 93, Playing = true, Level = 1, Peak = 0.1f };
                playing.Add(discord);
                if (CallApps.Find() != 90 || CallApps.Find() != 90 || CallApps.Find() != 93 || CallApps.Current.Name != "Discord") throw new Exception("Auto didn't go back to Discord once Discord made sound and the call she was hearing stayed quiet.");
                CallApps.Left(90, owner);
                playing.Remove(discord); playing.Add(slack);
                two.RemoveAt(two.Count - 1);
                checks.Add("PASS Auto never takes a browser video for the call, stays on the app she's hearing through a ping elsewhere, Discord opening, a quiet spell or while it still makes sound, and moves after about three seconds of another call app's sound");
                playing.Add(zoom);
                CallApps.Choose("Zoom"); CallApps.Heard(90, owner);
                CallApps.Choose("Auto");
                if (CallApps.Find() != 90) throw new Exception("The new call ears did not find the same process.");
                CallApps.Heard(90, nextOwner);
                playing.Remove(zoom);
                if (CallApps.Find() != 90) throw new Exception("The new ears did not hold their process.");
                CallApps.Left(90, owner);
                if (CallApps.Find() != 90 || CallApps.Find() != 91) throw new Exception("Old ears cleared the new hold or reset its drift.");
                playing.Add(zoom); CallApps.Heard(91, nextOwner);
                if (CallApps.Find() != 91) throw new Exception("The new ears did not keep their sounding app.");
                CallApps.Left(91, nextOwner);
                if (CallApps.Find() != 90) throw new Exception("The owning ears did not release their hold.");
                checks.Add("PASS old call ears cannot clear a new hold on the same pid or reset its drift; the owning ears still release it");

                // Picking Auto mid-call used to reset her to Discord, so a silent Discord took her off Zoom while Zoom made sound.
                two.Add(new CallProcess { Id = 93, Name = "Discord" });
                playing.Remove(slack);
                CallApps.Choose("Zoom");
                if (CallApps.Find() != 90 || CallApps.Current.Name != "Zoom") throw new Exception("Zoom wasn't found before the switch to Auto.");
                CallApps.Heard(90, owner);
                CallApps.Choose("Auto");
                if (CallApps.Current.Name != "Zoom") throw new Exception("Switching the call app to Auto mid-call dropped the app she was hearing for " + CallApps.Current.Name + ".");
                for (int look = 0; look < 5; look++)
                    if (CallApps.Find() != 90 || CallApps.Current.Name != "Zoom") throw new Exception("Switching to Auto mid-call moved her to a silent Discord while Zoom made sound.");
                CallApps.Left(90, owner);
                CallApps.Choose("Zoom");
                CallApps.Choose("Auto");
                if (CallApps.Current.Name != "Discord") throw new Exception("Off a call, Auto no longer started from Discord.");
                two.RemoveAt(two.Count - 1);
                checks.Add("PASS switching the call app to Auto mid-call keeps her on the app she's hearing while it makes sound, even with a silent Discord open; off a call Auto still starts from Discord");
            }
            finally { Find = findWas; CallApps.Processes = processesWas; CallApps.Sounds = soundsWas; CallApps.Left(90, owner); CallApps.Left(91, owner); CallApps.Left(90, nextOwner); CallApps.Left(91, nextOwner); CallApps.Choose(was); }
            checks.Add("PASS call capture keeps its swappable finder, names the chosen app when it isn't open and rejects a lookup from an old setting");
        }

        private static bool ChoicesWrong(string[][] names)
        {
            if (CallApps.Choices.Length != names.Length + 1 || CallApps.Choices[0] != "Auto") return true;
            for (int i = 0; i < names.Length; i++) if (CallApps.Choices[i + 1] != names[i][0]) return true;
            return false;
        }

        internal static void Smoke(List<string> checks)
        {
            SmokeApps(checks);
            CallEars ears = new CallEars();
            short[] discord = new short[Ears.Chunk * 3];
            for (int i = 0; i < discord.Length; i++) discord[i] = (short)(i % 2 == 0 ? 8000 : -8000);
            ears.Add(discord, discord.Length);
            if (Math.Abs(ears.Level - 8000 / 32768.0) > 0.001) throw new Exception("Discord's loudness was " + ears.Level + ".");
            short[] mic = new short[Ears.Chunk];
            for (int i = 0; i < mic.Length; i++) mic[i] = 30000;
            double mine, theirs;
            short[] mixed = ears.Mix(mic, out mine, out theirs);
            if (mixed[0] != short.MaxValue || mixed[1] != 22000 || Math.Abs(mine - 30000 / 32768.0) > 0.001 || Math.Abs(theirs - 8000 / 32768.0) > 0.001) throw new Exception("Discord's sound didn't mix into the mic's chunk.");
            ears.Mix(mic, out mine, out theirs);
            short[] after = ears.Mix(mic, out mine, out theirs);
            if (after[0] != 30000 || theirs != 0) throw new Exception("More than Discord's newest two chunks were mixed in.");
            checks.Add("PASS call ears: Discord's sound adds into the mic's chunks and clips instead of wrapping, only its newest 0.2 s is mixed in, and its loudness is measured");

            SmokeSources(checks);

            Func<int> findWas = Find;
            try
            {
                Find = delegate { return 0; };
                ears.Start();
                DateTime until = DateTime.UtcNow.AddSeconds(2);
                while (ears.Problem != NotOpen && DateTime.UtcNow < until) Thread.Sleep(10);
                if (ears.Problem != NotOpen || ears.Hearing) throw new Exception("Call ears with no Discord said: " + ears.Problem);
                ears.Stop();
                if (ears.Problem != null || ears.Level != 0) throw new Exception("Stopped call ears kept a problem or a level.");
                int stopped = ears.generation;
                ears.Add(discord, discord.Length, stopped - 1);
                ears.Mix(new short[Ears.Chunk], out mine, out theirs);
                if (theirs != 0 || ears.Level != 0) throw new Exception("A stopped call listener put old sound back into the ring.");
                ears.Start();
                ears.Add(discord, discord.Length, stopped - 1);
                ears.Mix(new short[Ears.Chunk], out mine, out theirs);
                if (theirs != 0 || ears.Level != 0) throw new Exception("A restarted call listener accepted the old listener's sound.");
                ears.Stop();
            }
            finally
            {
                ears.Stop();
                Find = findWas;
            }
            checks.Add("PASS call ears without Discord: they say Discord isn't open, and stopping clears it");
            checks.Add("PASS stopped and restarted call ears reject sound from the old listener");

            using (CallActivated activation = new CallActivated())
            {
                if (activation.Finished.WaitOne(0) || activation.ActivateCompleted(null) != 0 || !activation.Finished.WaitOne(0)) throw new Exception("A call activation did not signal its result.");
                activation.Dispose();
                if (!activation.Finished.SafeWaitHandle.IsClosed || activation.ActivateCompleted(null) != 0) throw new Exception("A late call activation touched a closed wait handle.");
            }
            checks.Add("PASS call activation closes its wait handle and accepts a late Windows callback after cleanup");

            short[] alone = new short[Ears.Chunk];
            ears.Add(discord, discord.Length);
            ears.Mix(mic, out mine, out theirs, alone);
            if (alone[0] != 8000 || alone[1] != -8000) throw new Exception("Discord's stretch didn't come out on its own.");
            short[] loud = new short[Ears.Chunk], quiet = new short[Ears.Chunk];
            for (int i = 0; i < loud.Length; i++) loud[i] = (short)(i % 2 == 0 ? 3000 : -3000);
            Func<short[], double> level = delegate(short[] chunk) { return chunk[0] == 0 ? 0 : 0.09; };
            CallOver over = new CallOver();
            // A laugh: six loud chunks, then quiet. She talks on, and nothing of it is kept for her ears.
            bool fired = false;
            foreach (short[] chunk in new[] { loud, loud, loud, loud, loud, loud, quiet, quiet, quiet, quiet, quiet, quiet, quiet }) fired |= over.Heard(chunk, level(chunk), 0.02);
            if (fired || over.Release().Count != 0) throw new Exception("A laugh on the call stopped her or was kept as a line.");
            // A line: the ninth loud chunk of twelve stops her, once, and her ears get the newest 1.4 s of it from its start.
            int firedAt = -1;
            bool[] talking = { false, true, true, false, true, true, true, true, true, true, true, true, true, true, true, true };
            short[][] line = new short[talking.Length][];
            for (int i = 0; i < line.Length; i++) line[i] = (short[])(talking[i] ? loud : quiet).Clone();
            for (int i = 0; i < line.Length; i++) if (over.Heard(line[i], level(line[i]), 0.02)) { if (firedAt >= 0) throw new Exception("Talking over her stopped her twice."); firedAt = i; }
            List<short[]> kept = over.Release();
            if (firedAt != 10 || kept.Count != CallOver.Kept || kept[0] != line[line.Length - CallOver.Kept] || kept[kept.Count - 1] != line[line.Length - 1] || over.Release().Count != 0)
                throw new Exception("Talking over her came out wrong: stopped at chunk " + firedAt + ", " + kept.Count + " chunks kept.");
            // Her newer answer ignored the stop: still talked over, it stops that one too.
            for (int i = 0; i < CallOver.Needs; i++) over.Heard(loud, 0.09, 0.02);
            over.Rearm();
            if (!over.Heard(loud, 0.09, 0.02)) throw new Exception("Being talked over again didn't stop her newer answer.");
            over.Reset();
            checks.Add("PASS call talk-over: about a second of someone on the call talking over her stops her once and hands her ears their line from its start, a laugh doesn't, and a newer answer can be stopped too");
            // Your own mic on a call: a laugh lets her finish, and about a second of you talking stops her. Off a call, two chunks still do.
            TalkOver yours = new TalkOver();
            for (int i = 0; i < 6; i++) yours.Heard(0.03, 0.1, 0.02, true);
            bool laughed = false;
            for (int i = 0; i < 6; i++) laughed |= yours.Heard(0.4, 0.1, 0.02, true);
            for (int i = 0; i < TalkOver.CallWindow; i++) laughed |= yours.Heard(0.03, 0.1, 0.02, true);
            int stoppedAt = -1;
            for (int i = 0; i < TalkOver.CallWindow && stoppedAt < 0; i++) if (yours.Heard(0.4, 0.1, 0.02, true)) stoppedAt = i;
            TalkOver off = new TalkOver();
            for (int i = 0; i < 6; i++) off.Heard(0.03, 0.1, 0.02);
            bool offStopped = false;
            for (int i = 0; i < 2; i++) offStopped |= off.Heard(0.4, 0.1, 0.02);
            if (laughed || stoppedAt != TalkOver.CallNeeds - 1 || !offStopped) throw new Exception("Your mic talking over her on a call came out wrong: laugh " + laughed + ", stopped at chunk " + stoppedAt + ", off a call " + offStopped + ".");
            checks.Add("PASS your mic on a call: a laugh lets her finish and about a second of you talking over her stops her; off a call a fifth of a second still does");
        }

        internal static short[] Pattern(int length, int seed)
        {
            Random random = new Random(seed);
            short[] samples = new short[length];
            double level = 0;
            for (int i = 0; i < length; i++) { level += 0.4 * (random.Next(-14000, 14001) - level); samples[i] = (short)level; }
            return samples;
        }

        private static void SmokeSources(List<string> checks)
        {
            CallEars ears = new CallEars();
            CallChunk[] chunks = new CallChunk[12];
            for (int i = 0; i < chunks.Length; i++)
            {
                bool user = i / 3 % 2 == 0;
                short[] sound = Pattern(Ears.Chunk, i + 1), quiet = new short[Ears.Chunk];
                ears.Add(user ? quiet : sound, Ears.Chunk);
                chunks[i] = ears.Split(user ? sound : quiet);
                if (chunks[i].Source != (user ? CallSource.User : CallSource.Discord)) throw new Exception("The call run lost its source at chunk " + i + ".");
            }
            if (ears.Split(new short[Ears.Chunk]).Source != CallSource.Quiet) throw new Exception("Quiet call audio gained a speaker.");
            Func<short[], string> runWas = CapriDesktop.Hearing.Run;
            try
            {
                int decoded = 0;
                string[] words = { "the", "in", "cat", "the" };
                CapriDesktop.Hearing.Run = delegate(short[] samples)
                {
                    if (samples.Length != Ears.Chunk * 3 + Ears.Rate * 3 / 10 || decoded >= words.Length) throw new Exception("The recognizer got the wrong source run.");
                    return words[decoded++];
                };
                CallTranscript line = CallTranscript.Read(chunks);
                string user, discord;
                line.Words(out user, out discord);
                if (decoded != 4 || line.Text != "You: the\nDiscord: cat\nYou: in\nDiscord: the" || user != "the in" || discord != "cat the") throw new Exception("Recognized call runs lost their order or their speaker.");
            }
            finally { CapriDesktop.Hearing.Run = runWas; }
            checks.Add("PASS call sources: user, Discord, user, Discord and quiet keep their chunk labels, and fake recognition keeps each word in speaking order with separate user and Discord words");
            CallTranscript crosstalk = new CallTranscript();
            crosstalk.Parts.Add(new CallPart { Words = "open my notes", Echo = true, Chunks = 10, Echoes = 1 });
            crosstalk.Parts.Add(new CallPart { FromCall = true, Words = "nice" });
            crosstalk.Parts.Add(new CallPart { Words = "see you later", Echo = true, Chunks = 4, Echoes = 3 });
            string caller = crosstalk.App.Label;
            if (crosstalk.Text != "You: open my notes\n" + caller + ": nice\n" + caller + ": see you later" || crosstalk.Sure.Length != 0 || crosstalk.Parts[0].Leaked || !crosstalk.Parts[2].Leaked) throw new Exception("A moment of crosstalk relabelled their sentence, or echo could wake or answer her.");
            checks.Add("PASS a sentence with a moment of crosstalk stays theirs, one that's mostly their speakers' echo is the call's, and neither can wake or answer her");

            short[] reference = Pattern(Ears.Chunk * 14, 71), voice = Pattern(reference.Length, 83);
            foreach (int lag in new[] { 0, Ears.Rate * 120 / 1000, Ears.Rate * 237 / 1000, Ears.Rate * 4 / 10 })
            {
                CallEcho leak = new CallEcho(), overlap = new CallEcho(), room = new CallEcho();
                Random noise = new Random(97);
                for (int at = 0; at < reference.Length; at += Ears.Chunk)
                {
                    short[] call = new short[Ears.Chunk], mic = new short[Ears.Chunk], both = new short[Ears.Chunk], reverb = new short[Ears.Chunk];
                    for (int i = 0; i < Ears.Chunk; i++)
                    {
                        if (at + i < Ears.Chunk * 8) call[i] = reference[at + i];
                        int delayed = at + i - lag;
                        mic[i] = (short)((delayed >= 0 && delayed < Ears.Chunk * 8 ? reference[delayed] * 0.3 : 0) + noise.Next(-20, 21));
                        both[i] = (short)(mic[i] + voice[at + i]);
                        double tail = noise.Next(-20, 21);
                        foreach (KeyValuePair<int, double> tap in new[] { new KeyValuePair<int, double>(0, 0.3), new KeyValuePair<int, double>(Ears.Rate * 40 / 1000, 0.15), new KeyValuePair<int, double>(Ears.Rate * 90 / 1000, 0.08) })
                        {
                            int back = delayed - tap.Key;
                            if (back >= 0 && back < Ears.Chunk * 8) tail += reference[back] * tap.Value;
                        }
                        reverb[i] = (short)tail;
                    }
                    CallChunk copied = leak.Split(mic, call), spoken = overlap.Split(both, call), echoed = room.Split(reverb, call);
                    if (lag < Ears.Rate * 4 / 10 && (echoed.Source == CallSource.User || echoed.Source == CallSource.Both) && !echoed.Echo) throw new Exception("A room echo of the call could speak as the user at lag " + lag + ".");
                    if (spoken.Echo) throw new Exception("Talking over a call leak was taken for its echo at lag " + lag + ".");
                    bool playing = Speaker.Loudness(call, 0, call.Length) >= 0.003;
                    bool copiedSound = Speaker.Loudness(mic, 0, mic.Length) >= 0.003;
                    if (copied.Source != (playing || copiedSound ? CallSource.Discord : CallSource.Quiet) || Speaker.Loudness(copied.User, 0, copied.User.Length) != 0) throw new Exception("A Discord leak gained user words at lag " + lag + ".");
                    if (spoken.Source != (playing ? CallSource.Both : CallSource.User) || Speaker.Loudness(spoken.User, 0, spoken.User.Length) < 0.02) throw new Exception("Talking over a Discord leak lost the user at lag " + lag + ".");
                }
                leak.Clear();
                if (leak.Split(new short[Ears.Chunk], new short[Ears.Chunk]).Source != CallSource.Quiet) throw new Exception("A cleared call kept its echo reference.");
            }
            checks.Add("PASS call leaks: normalized delayed fits at 0, 120, 237 and 400 ms remove a 0.3 Discord copy with room noise, while user speech over it keeps its own source");
        }
    }

    internal enum CallSource { Quiet, User, Discord, Both }

    internal sealed class CallChunk
    {
        internal readonly short[] User, Discord, Samples;
        internal readonly CallSource Source;
        // Their part here came with the call's echo and wasn't clearly louder than it: it shows as theirs, but it can't wake her or say yes.
        internal readonly bool Echo;

        internal CallChunk(short[] user, short[] discord, bool leak = false, bool echo = false)
        {
            bool own = Speaker.Loudness(user, 0, user.Length) >= 0.003;
            Echo = own && echo;
            bool call = Speaker.Loudness(discord, 0, discord.Length) >= 0.003;
            Source = own ? call ? CallSource.Both : CallSource.User : call || leak ? CallSource.Discord : CallSource.Quiet;
            User = own ? user : new short[user.Length];
            Discord = call ? discord : new short[discord.Length];
            Samples = new short[user.Length];
            for (int i = 0; i < Samples.Length; i++) Samples[i] = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, User[i] + Discord[i]));
        }
    }

    internal sealed class CallEcho
    {
        private const int Lags = Ears.Rate * 4 / 10;
        private readonly short[] recent = new short[Lags + Ears.Chunk];

        internal void Clear() { Array.Clear(recent, 0, recent.Length); }

        internal CallChunk Split(short[] mic, short[] discord)
        {
            int length = mic.Length;
            if (length == 0) return new CallChunk(mic, discord);
            if (length > Ears.Chunk || discord.Length != length) throw new ArgumentException("The call chunk has the wrong length.");
            Array.Copy(recent, length, recent, 0, recent.Length - length);
            Array.Copy(discord, 0, recent, recent.Length - length, length);
            int at = recent.Length - length, points = (length + 7) / 8;
            int[] lags = new int[64];
            double[] scores = new double[64];
            double own = 0, ownSum = 0;
            bool reference = false;
            foreach (short sample in recent) if (Math.Abs((int)sample) > 16) { reference = true; break; }
            for (int i = 0; i < length; i += 8) { own += (double)mic[i] * mic[i]; ownSum += mic[i]; }
            own -= ownSum * ownSum / points;
            // Every 8th sample can come out under the level that makes a chunk theirs, so the search starts at half of it.
            for (int lag = 0; reference && own >= points * 0.0015 * 0.0015 * 32768.0 * 32768.0 && lag <= Lags; lag++)
            {
                double dot = 0, power = 0, sum = 0;
                for (int i = 0; i < length; i += 8)
                {
                    double sample = recent[at + i - lag];
                    dot += mic[i] * sample;
                    power += sample * sample;
                    sum += sample;
                }
                dot -= ownSum * sum / points;
                power -= sum * sum / points;
                double fit = power > points * 16 ? dot * dot / power : 0;
                if (fit <= scores[scores.Length - 1]) continue;
                int slot = scores.Length - 1;
                while (slot > 0 && fit > scores[slot - 1]) { scores[slot] = scores[slot - 1]; lags[slot] = lags[slot - 1]; slot--; }
                scores[slot] = fit;
                lags[slot] = lag;
            }
            short[] user = (short[])mic.Clone();
            bool leak = false;
            double micSum = 0, micPower = 0, score = 0, gain = 0, mean = 0;
            int best = 0;
            for (int i = 0; i < length; i++) { micSum += mic[i]; micPower += (double)mic[i] * mic[i]; }
            micPower -= micSum * micSum / length;
            for (int candidate = 0; candidate < lags.Length && scores[candidate] > 0; candidate++)
            {
                int lag = lags[candidate];
                double dot = 0, power = 0, sum = 0;
                for (int i = 0; i < length; i++)
                {
                    double sample = recent[at + i - lag];
                    dot += mic[i] * sample;
                    power += sample * sample;
                    sum += sample;
                }
                dot -= micSum * sum / length;
                power -= sum * sum / length;
                double fit = power > 0 ? dot * dot / power : 0;
                if (fit <= score || fit < micPower * 0.04) continue;
                score = fit;
                best = lag;
                gain = Math.Max(-2, Math.Min(2, dot / power));
                mean = sum / length;
            }
            leak = score / length >= 0.003 * 0.003 * 32768.0 * 32768.0;
            double left = 0, taken = 0;
            if (leak)
                for (int i = 0; i < length; i++)
                {
                    double took = gain * (recent[at + i - best] - mean);
                    user[i] = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, Math.Round(mic[i] - micSum / length - took)));
                    left += (double)user[i] * user[i];
                    taken += took * took;
                }
            // One tap never takes all of a room's echo, so what's left only speaks for them when it's well over what came out. An echo
            // too faint to take out, like a room's tail after they stop, gets the same test against what it would have taken.
            bool echo = leak ? left < taken * 4 : score > 0 && micPower - score < score * 4;
            return new CallChunk(user, (short[])discord.Clone(), leak, echo);
        }
    }

    internal sealed class CallPart
    {
        internal bool FromCall;
        internal string Who;
        internal string Words;
        internal int At;
        internal bool Echo;
        // How many of its chunks went into it, and how many of those had the call's echo as loud as them.
        internal int Chunks, Echoes;

        // Mostly the call's sound through their speakers, so it's the call's words. A quick bit of crosstalk in their own
        // sentence leaves it theirs, though Sure still keeps all of it from waking or answering her.
        internal bool Leaked { get { return Echo && Echoes * 2 >= Chunks; } }
    }

    internal sealed class CallTranscript
    {
        internal readonly List<CallPart> Parts = new List<CallPart>();
        internal readonly CallApp App = CallApps.Current;

        internal CallTranscript() { }

        internal CallTranscript(string text, bool fromCall)
        {
            string words = Hearing.Clean(text);
            if (words.Length > 0) Parts.Add(new CallPart { Words = words, FromCall = fromCall });
        }

        internal void Words(out string user, out string discord)
        {
            StringBuilder own = new StringBuilder(), call = new StringBuilder();
            foreach (CallPart part in Parts) (part.FromCall ? call : own).Append(part.Words).Append(' ');
            user = own.ToString().Trim();
            discord = call.ToString().Trim();
        }

        // Their words that can wake her or answer her: none from a stretch where the call's echo was as loud as they were.
        internal string Sure
        {
            get
            {
                StringBuilder own = new StringBuilder();
                foreach (CallPart part in Parts) if (!part.FromCall && !part.Echo) own.Append(part.Words).Append(' ');
                return own.ToString().Trim();
            }
        }

        // Words from a stretch that was mostly the call's echo are labelled the call's, so her brain never reads a friend heard
        // through speakers as "You".
        internal string Text
        {
            get
            {
                if (Parts.Count == 1) return (Parts[0].FromCall || Parts[0].Leaked ? CallSession.Others : "") + Parts[0].Words;
                StringBuilder text = new StringBuilder();
                foreach (CallPart part in Parts)
                {
                    if (text.Length > 0) text.Append('\n');
                    text.Append(part.FromCall || part.Leaked ? App.Label + ": " : "You: ").Append(part.Words);
                }
                return text.ToString();
            }
        }

        internal static CallTranscript Read(CallChunk[] chunks)
        {
            CallTranscript line = new CallTranscript();
            foreach (bool fromCall in new[] { false, true })
            {
                int i = 0;
                while (i < chunks.Length)
                {
                    if (!Active(chunks[i], fromCall)) { i++; continue; }
                    int first = i++, last = i;
                    while (i < chunks.Length)
                    {
                        if (Active(chunks[i], fromCall)) { last = ++i; continue; }
                        int gap = i;
                        while (i < chunks.Length && chunks[i].Source == CallSource.Quiet && i - gap < 2) i++;
                        if (i < chunks.Length && Active(chunks[i], fromCall)) continue;
                        break;
                    }
                    int count = 0, echoes = 0;
                    for (int j = first; j < last; j++) { count += chunks[j].Samples.Length; if (!fromCall && chunks[j].Echo) echoes++; }
                    short[] samples = new short[count + Ears.Rate * 3 / 10];
                    int at = Ears.Rate * 15 / 100;
                    for (int j = first; j < last; j++)
                    {
                        short[] part = fromCall ? chunks[j].Discord : chunks[j].User;
                        Array.Copy(part, 0, samples, at, part.Length);
                        at += part.Length;
                    }
                    string words = Hearing.Text(samples);
                    if (words.Length > 0) line.Parts.Add(new CallPart { FromCall = fromCall, Words = words, At = first, Echo = echoes > 0, Chunks = last - first, Echoes = echoes });
                }
            }
            line.Parts.Sort(delegate(CallPart left, CallPart right)
            {
                int order = left.At.CompareTo(right.At);
                return order == 0 ? left.FromCall.CompareTo(right.FromCall) : order;
            });
            // Only stretches that agree on being echo join, so a stretch of a friend's echo after a pause can't be outvoted by
            // your words before it and read as yours. Your stretch beside it still reads as yours, but it can't wake her or say
            // yes, as when they joined: the echo check could have missed part of the friend.
            for (int i = line.Parts.Count - 1; i > 0; i--)
            {
                if (line.Parts[i - 1].FromCall != line.Parts[i].FromCall) continue;
                if (line.Parts[i - 1].Leaked != line.Parts[i].Leaked) { line.Parts[i - 1].Echo = line.Parts[i].Echo = true; continue; }
                line.Parts[i - 1].Words += " " + line.Parts[i].Words;
                line.Parts[i - 1].Echo |= line.Parts[i].Echo;
                line.Parts[i - 1].Chunks += line.Parts[i].Chunks;
                line.Parts[i - 1].Echoes += line.Parts[i].Echoes;
                line.Parts.RemoveAt(i);
            }
            return line;
        }

        private static bool Active(CallChunk chunk, bool fromCall)
        {
            return fromCall ? chunk.Source == CallSource.Discord && Speaker.Loudness(chunk.Discord, 0, chunk.Discord.Length) >= 0.003 || chunk.Source == CallSource.Both
                : chunk.Source == CallSource.User || chunk.Source == CallSource.Both;
        }
    }

    // Someone on the call talking over her. While she talks her ears get your mic alone, and Discord's sound waits here: once nine
    // of its last twelve chunks are a voice's level, about a second of talking inside 1.2 s, she stops, and once she has her ears
    // get its newest 1.4 s, the start of their line included. A laugh, a "yeah" or a "nah nah" is shorter, so it lets her finish
    // (Lachy, 3 Nov: "maybe we need some interruption and stuff like that when someone talks over as well"; 4 Nov: half a second
    // cut her off mid-answer on a real call).
    internal sealed class CallOver
    {
        internal const int Window = 12, Needs = 9, Kept = 14;
        private readonly bool[] loud = new bool[Window];
        private readonly List<short[]> held = new List<short[]>();
        private int at;
        internal bool Fired { get; private set; }

        // One chunk of Discord while she talks, with its loudness. True the once it's time to stop her.
        internal bool Heard(short[] call, double level, double wake)
        {
            held.Add(call);
            if (held.Count > Kept) held.RemoveAt(0);
            loud[at] = level >= wake;
            at = (at + 1) % Window;
            if (Fired) return false;
            int count = 0;
            foreach (bool each in loud) if (each) count++;
            if (count < Needs) return false;
            Fired = true;
            return true;
        }

        // A stop her newer answer ignored: being talked over still can stop that one.
        internal void Rearm() { Fired = false; }

        // She's stopped talking: what was held if someone talked over her, else nothing, and it starts again.
        internal List<short[]> Release()
        {
            List<short[]> line = Fired ? new List<short[]>(held) : new List<short[]>();
            Reset();
            return line;
        }

        internal void Reset()
        {
            held.Clear();
            Array.Clear(loud, 0, Window);
            at = 0;
            Fired = false;
        }
    }

    // IActivateAudioInterfaceCompletionHandler, which she gives Windows. Imported, not just visible: Windows asks her callback
    // for it and for IAgileObject by id, and her assembly isn't COM-visible, so only imported interfaces answer (0x8000000E).
    [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IActivated
    {
        [PreserveSig] int ActivateCompleted(IActivation operation);
    }

    // IAgileObject: tells Windows her callback can be called from any thread.
    [ComImport, Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAgileObject { }

    // IActivateAudioInterfaceAsyncOperation, which Windows gives her.
    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IActivation
    {
        [PreserveSig] int GetActivateResult(out int result, [MarshalAs(UnmanagedType.IUnknown)] out object activated);
    }

    // Windows calls this when the capture she asked for is ready, from its own thread.
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    internal sealed class CallActivated : IActivated, IAgileObject, IDisposable
    {
        internal readonly ManualResetEvent Finished = new ManualResetEvent(false);
        private readonly object gate = new object();
        private bool closed;

        public int ActivateCompleted(IActivation operation)
        {
            lock (gate) if (!closed) Finished.Set();
            return 0;
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (closed) return;
                closed = true;
                Finished.Dispose();
            }
        }
    }
}

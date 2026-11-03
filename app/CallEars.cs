using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace CapriDesktop
{
    // The people on your call: Discord's own sound, which Windows hands her apart from everything else the PC plays (the
    // per-app capture OBS's Application Audio Capture uses), so in call mode she hears them as well as you (Lachy, 3 Nov:
    // "I don't think what they say is picked up rn"). Her voice reaches the call through your mic, and Discord never plays
    // your own mic back to you, so she doesn't hear herself in it.
    internal sealed class CallEars : IDisposable
    {
        internal const string NotOpen = "Discord isn't open.";
        // Discord and its test builds.
        private static readonly string[] Names = { "Discord", "DiscordPTB", "DiscordCanary" };
        private readonly object gate = new object();
        // Two seconds of Discord at her ears' rate. Only the newest two chunks of it are ever mixed in.
        private readonly short[] ring = new short[Ears.Rate * 2];
        private int start, count;
        private double heard, level;
        private int heardCount, levelAt = Environment.TickCount - 100000;
        private volatile bool running;
        private volatile string problem = NotOpen;
        private int generation;

        // The process whose sound is Discord's, 0 when it isn't open. The smoke test stands in for it.
        internal static Func<int> Find = Discord;

        internal bool Hearing { get { return running && problem == null; } }

        // Why she can't hear Discord while she's trying to, or null.
        internal string Problem { get { return running ? problem : null; } }

        // Discord's loudness over its last tenth of a second, or 0 when nothing has come in for that long.
        internal double Level { get { lock (gate) return Environment.TickCount - levelAt < 300 ? level : 0; } }

        internal void Start()
        {
            if (running) return;
            running = true;
            problem = NotOpen;
            int mine = Interlocked.Increment(ref generation);
            new Thread(delegate() { Listen(mine); }) { IsBackground = true, Name = "Capri call ears" }.Start();
        }

        // Her thread sees this within 10 ms, or once Windows answers a capture it's opening, and closes the capture itself.
        internal void Stop()
        {
            if (!running) return;
            running = false;
            Interlocked.Increment(ref generation);
            lock (gate) { start = count = 0; heard = 0; heardCount = 0; level = 0; }
        }

        public void Dispose() { Stop(); }

        private bool Live(int mine) { return running && Volatile.Read(ref generation) == mine; }

        private void Listen(int mine)
        {
            string told = null;
            while (Live(mine))
            {
                int pid = 0;
                try { pid = Find(); }
                catch (Exception) { }
                if (!Live(mine)) break;
                if (pid == 0) { Say(mine, NotOpen); Rest(mine, 2000); continue; }
                Tap tap = null;
                try
                {
                    string failed;
                    tap = Tap.Open(pid, out failed);
                    if (tap == null)
                    {
                        Say(mine, failed);
                        if (failed != told) Brain.Log("call ears", -1, failed);
                        told = failed;
                        Rest(mine, 15000);
                        continue;
                    }
                    told = null;
                    Say(mine, null);
                    Brain.Log("call ears", -1, "Hearing Discord: process " + pid + " and the ones it started, as 16000 Hz mono 16-bit PCM.");
                    int lookedAt = Environment.TickCount;
                    while (Live(mine))
                    {
                        Thread.Sleep(10);
                        if (!tap.Read(this, mine)) { Brain.Log("call ears", -1, "Discord's sound stopped coming. Opening it again."); break; }
                        if (Environment.TickCount - lookedAt < 1000) continue;
                        lookedAt = Environment.TickCount;
                        if (Gone(pid)) break;
                    }
                }
                catch (Exception error)
                {
                    Say(mine, "Couldn't hear Discord: " + Voice.Short(error.Message));
                    Brain.Log("call ears", -1, error.Message);
                    Rest(mine, 5000);
                }
                finally { if (tap != null) tap.Dispose(); }
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

        // Discord's first process, the one its others were started from, so its capture takes in all their sound.
        private static int Discord()
        {
            int found = 0;
            DateTime first = DateTime.MaxValue;
            foreach (string name in Names)
                foreach (Process process in Process.GetProcessesByName(name))
                    using (process)
                    {
                        try { if (process.StartTime < first) { first = process.StartTime; found = process.Id; } }
                        catch (Exception) { }
                    }
            return found;
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

        // Whether Discord was most of the sound in the newest `chunks` of her ears' chunks, given each chunk's loudness from
        // your mic and from Discord, oldest first, with `total` chunks taken so far: someone on the call said that line.
        internal static bool FromCall(double[] mine, double[] theirs, int total, int chunks)
        {
            double own = 0, call = 0;
            for (int i = 1; i <= Math.Min(chunks, Math.Min(total, mine.Length)); i++)
            {
                int at = (total - i) % mine.Length;
                own += mine[at] * mine[at];
                call += theirs[at] * theirs[at];
            }
            return call > own;
        }

        [DllImport("Mmdevapi.dll", ExactSpelling = true)]
        private static extern int ActivateAudioInterfaceAsync([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid iid, IntPtr parameters, IActivated done, out IActivation operation);

        // Windows' per-app capture of one process and the ones it started, converted by Windows to her ears' 16 kHz mono 16-bit PCM.
        private sealed class Tap : IDisposable
        {
            private ISoundClient client;
            private ISoundCapture capture;
            private short[] samples = new short[0];

            internal static Tap Open(int pid, out string failed)
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
                    catch (EntryPointNotFoundException) { failed = Refused(-1); return null; }
                    if (result != 0) { failed = Refused(result); return null; }
                    if (!done.Finished.WaitOne(5000)) { failed = "Windows took too long to hand over Discord's sound."; return null; }
                    int activated;
                    object found;
                    result = operation.GetActivateResult(out activated, out found);
                    if (result != 0 || activated != 0 || found == null) { Wasapi.Release(found); failed = Refused(result != 0 ? result : activated); return null; }
                    client = (ISoundClient)found;
                    Marshal.StructureToPtr(WaveFormat.Mono(Ears.Rate), format, false);
                    // Loopback, with Windows converting to her rate and one channel at its default quality.
                    result = client.Initialize(0, 0x00020000 | unchecked((int)0x80000000) | 0x08000000, 1000000, 0, format, IntPtr.Zero);
                    if (result != 0) { failed = Refused(result); return null; }
                    Guid captureId = typeof(ISoundCapture).GUID;
                    object service;
                    result = client.GetService(ref captureId, out service);
                    if (result != 0) { failed = Refused(result); return null; }
                    result = client.Start();
                    if (result != 0) { Wasapi.Release(service); failed = Refused(result); return null; }
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

            private static string Refused(int result)
            {
                return "Windows wouldn't hand her Discord's sound" + (result == -1 ? "" : " (0x" + result.ToString("X8") + ")") + ". It needs Windows 11 or an up-to-date Windows 10.";
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

        // Discord's sound mixed into a mic, the mix's loudness, whose lines are whose, and Discord's absence, without a real Discord.
        internal static void Smoke(List<string> checks)
        {
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

            double[] own = new double[10], call = new double[10];
            for (int i = 0; i < 6; i++) { own[i] = 0.05; call[i] = 0.001; }
            for (int i = 6; i < 10; i++) { own[i] = 0.004; call[i] = 0.03; }
            if (!FromCall(own, call, 10, 4) || FromCall(own, call, 10, 10) || FromCall(own, call, 6, 3)) throw new Exception("A line's speaker came out wrong.");
            if (!FromCall(own, call, 20, 2) || FromCall(own, call, 13, 2) || FromCall(own, call, 0, 4)) throw new Exception("Whose line it is broke once her chunk count wrapped.");
            checks.Add("PASS call lines: a line that's mostly Discord's sound counts as someone else on the call, and one that's mostly the mic's as the user's");

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
            // A laugh: three loud chunks, then quiet. She talks on, and nothing of it is kept for her ears.
            bool fired = false;
            foreach (short[] chunk in new[] { loud, loud, loud, quiet, quiet, quiet, quiet, quiet }) fired |= over.Heard(chunk, level(chunk), 0.02);
            if (fired || over.Release().Count != 0) throw new Exception("A laugh on the call stopped her or was kept as a line.");
            // A line: the fifth loud chunk of seven stops her, once, and her ears get the newest second of it from its start.
            int firedAt = -1;
            bool[] talking = { false, true, true, false, true, true, true, true, true, true, true, true };
            short[][] line = new short[talking.Length][];
            for (int i = 0; i < line.Length; i++) line[i] = (short[])(talking[i] ? loud : quiet).Clone();
            for (int i = 0; i < line.Length; i++) if (over.Heard(line[i], level(line[i]), 0.02)) { if (firedAt >= 0) throw new Exception("Talking over her stopped her twice."); firedAt = i; }
            List<short[]> kept = over.Release();
            if (firedAt != 6 || kept.Count != CallOver.Kept || kept[0] != line[line.Length - CallOver.Kept] || kept[kept.Count - 1] != line[line.Length - 1] || over.Release().Count != 0)
                throw new Exception("Talking over her came out wrong: stopped at chunk " + firedAt + ", " + kept.Count + " chunks kept.");
            // Her newer answer ignored the stop: still talked over, it stops that one too.
            for (int i = 0; i < 5; i++) over.Heard(loud, 0.09, 0.02);
            over.Rearm();
            if (!over.Heard(loud, 0.09, 0.02)) throw new Exception("Being talked over again didn't stop her newer answer.");
            over.Reset();
            checks.Add("PASS call talk-over: half a second of someone on the call talking over her stops her once and hands her ears their line from its start, a laugh doesn't, and a newer answer can be stopped too");
        }
    }

    // Someone on the call talking over her. While she talks her ears get your mic alone, and Discord's sound waits here: once five
    // of its last seven chunks are a voice's level, half a second of them inside 0.7 s, she stops, and once she has her ears get
    // its newest second, the start of their line included. A laugh or a "yeah" is shorter, so it lets her finish (Lachy, 3 Nov:
    // "maybe we need some interruption and stuff like that when someone talks over as well").
    internal sealed class CallOver
    {
        internal const int Window = 7, Needs = 5, Kept = 10;
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

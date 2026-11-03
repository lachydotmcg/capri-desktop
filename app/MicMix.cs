using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;

namespace CapriDesktop
{
    // An audio device her mic mix uses: their mic, or the virtual cable's input, VB-CABLE's or Steam's.
    internal sealed class MixDevice
    {
        internal readonly string Id;
        internal readonly string Name;
        // For a cable, the mic it comes out of, which Discord or Roblox hears, or null when Windows doesn't list it.
        internal readonly string Far;

        internal MixDevice(string id, string name) : this(id, name, null) { }

        internal MixDevice(string id, string name, string far)
        {
            Id = id;
            Name = name;
            Far = far;
        }
    }

    // A device that went away or a WASAPI call on it that failed. Her mic mix closes it and looks again.
    internal sealed class MixLost : Exception
    {
        internal MixLost(string message) : base(message) { }
    }

    // The mic her mix hears: WASAPI on a real PC, a hum in the smoke test.
    internal interface IMixMic : IDisposable
    {
        int Rate { get; }
        // Frames in a packet, about 10 ms.
        int Period { get; }
        // Set when a packet may be waiting, or null for her to look every few ms.
        WaitHandle Ready { get; }
        // Whole packets heard since the last read, as one channel from -1 to 1, as many as fit. Throws MixLost once the mic is gone.
        int Read(float[] into);
    }

    // The cable her mix plays into: CABLE Input on a real PC, a recorder in the smoke test.
    internal interface IMixCable : IDisposable
    {
        int Rate { get; }
        // Frames Windows takes at a time, about 10 ms.
        int Period { get; }
        WaitHandle Ready { get; }
        // Frames written that haven't played yet. Throws MixLost once the cable is gone.
        int Queued();
        // Plays count samples from -1 to 1 on every channel.
        void Write(float[] mono, int count);
    }

    // Where her mix finds its devices, so the smoke test can hand it stand-ins and never open a real one.
    internal interface IMixDevices
    {
        // The cable to play into, with its far end when Windows lists it: the chosen one while it's there, else VB-CABLE's
        // before Steam's (MicMix.CableFor), or null.
        MixDevice Cable(string chosen);
        // Every cable there is to choose from, VB-CABLE's before Steam's, each with its far end when Windows lists it.
        List<MixDevice> Cables();
        // The programs hearing a mic right now, like "Discord", for Settings to say Discord has picked the cable's far end.
        List<string> Listening(string mic);
        // How many dB Windows' volume on the cable's far end adds, and whether Windows has it muted. False when it can't say.
        bool Boost(MixDevice cable, out double boost, out bool muted);
        // The mic for their pick: the picked one while it's plugged in, else Windows' default, else any other, but never a
        // cable's far end. Null when there's no mic.
        MixDevice Mic(string chosen);
        IMixMic OpenMic(MixDevice mic);
        IMixCable OpenCable(MixDevice cable);
    }

    // Her voice in your mic (F66, Lachy: "OPTION B!!"). Windows can't make a new mic without a driver, so she plays into a
    // virtual cable: VB-CABLE when it's there, else Steam's own virtual mic, which Steam puts on many gamers' PCs. Without either,
    // Settings runs VB-Audio's own VB-CABLE setup from the copy she carries (CableSetup). While the switch is on she plays their
    // real mic plus her voice into the cable, they pick its far end (CABLE Output, or Steam Streaming Microphone) as the mic in
    // Discord or Roblox, and friends hear them both, her at a steady speaking level whatever her speakers' volume. Her voice
    // still plays on their speakers, her sound effects stay out, and
    // her own ears never listen to the cable's far end. Steam's far end turns everything up 30 dB through Windows' own volume on
    // it, so she plays under by whatever that volume adds, read four times a second. A cable whose far end is turned off in
    // Windows can't reach Discord or Roblox, so one with its far end on comes first, and Settings says to turn it on.
    //
    // One thread does it all. Each wake it takes the mic's new packets, resampled to the cable's rate into a short queue, then
    // tops the cable up to two of Windows' periods with that queue and her voice added in, through a soft limiter. The queue
    // holds a packet and a half, counting what the mic has heard since its last packet, so the mic path is about 40 to 45 ms:
    // that, the two periods on the cable, and Windows taking a period at a time. The mic's resampler is nudged a hair faster or
    // slower to hold that level, so the mic's clock and the cable's never drift apart, and a packet that comes late waits on
    // the cable's spare period instead of leaving a gap. A device that goes away is closed and looked for again every second,
    // and every two seconds she checks they still have the mic and cable she should be using.
    internal sealed class MicMix : IDisposable
    {
        internal const string Missing = "Needs VB-CABLE (free).";
        internal const string Unopened = "The virtual mic won't open. Trying again.";
        // What they pick in Discord or Roblox for each cable.
        internal const string CableEnd = "CABLE Output", SteamEnd = "Steam Streaming Microphone";
        internal const string NoMic = "No mic found, so only her voice goes in.";
        internal const string MicGone = "Your mic won't open, so only her voice goes in.";
        // Where she finds the mic and the cable. The smoke test swaps in stand-ins.
        internal static IMixDevices Devices = new Endpoints();
        // The mix that's on, for her speakers to hand her voice to.
        internal static volatile MicMix Current;
        // How often she checks the devices are still the right ones, how soon one that failed is tried again, and how long a
        // mic can send nothing before she counts it gone, like after the PC slept, in seconds.
        internal const double LookGap = 2, RetryGap = 1, Hushed = 3;
        // How often she reads Windows' volume on the cable's far end, in seconds, so turning it up can't blast friends for long,
        // and what Steam's far end adds, in dB, while Windows won't say.
        internal const double TrimGap = 0.25, SteamBoost = 30;
        // Her voice goes in at a steady speaking level whatever her speakers' volume: her voiced moments (20 ms pieces over 1% of
        // full scale) are brought to about -17 dBFS, by a quarter to four times her own level, and the limiter keeps her peaks
        // under full scale (Lachy, 3 Nov: friends on Discord found her "too quiet for it to come through legible" at volume 50).
        internal const double Speaking = 0.14, Voiced = 0.01, LeastLift = 0.25, MostLift = 4;
        // Her voice waits at most this many seconds for the cable, so a stalled cable can't pile it up.
        private const int MostWaiting = 120;
        // How the mic's clock is steered to keep its queue level: as a part of its rate for each second of audio the level is
        // off, straight away (Pull) and building up each second it stays off (Lasting); over how many seconds the level is
        // smoothed; and the most it's ever steered.
        private const double Pull = 0.4, Lasting = 0.04, Settle = 0.5, MostSteer = 0.002;
        private readonly IMixDevices devices;
        private readonly Func<double> clock;
        private readonly object gate = new object();
        private readonly List<Lane> lanes = new List<Lane>();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private Thread worker;
        private volatile bool stopping;
        private volatile bool open;
        private volatile bool relook;
        private volatile string chosen, cableChosen;
        // What's wrong for Settings in a few words, or null while it's mixing their mic.
        internal volatile string Problem;
        // For the smoke test and errors.txt: opens and losses, frames written and frames the mic queue ran short.
        internal volatile int MicOpens, CableOpens, MicLosses, CableLosses;
        internal long Written, Starved;

        // The rest is the mix thread's own, or the test's while it steps one by hand.
        private IMixMic mic;
        private IMixCable cable;
        private MixDevice micDevice, cableDevice;
        private MixResampler micRate;
        private Ring queue;
        private SoftLimiter limiter;
        private float[] heard = new float[0], made = new float[0], mix = new float[0];
        private WaitHandle[] handles;
        private double cableAt, micAt, lookAt, heardAt, trimAt;
        private int target, period, cushion, jump;
        private bool steering, farMuted, trimRead;
        private double level, held, steeredAt;
        // What the mix is played at, under 1 when Windows turns the cable's far end up.
        private float trim = 1;
        private string cableProblem, micProblem, logged;

        internal MicMix(IMixDevices devices, Func<double> clock)
        {
            this.devices = devices;
            if (clock == null)
            {
                Stopwatch own = Stopwatch.StartNew();
                clock = delegate { return own.Elapsed.TotalSeconds; };
            }
            this.clock = clock;
        }

        // Their mic pick, an endpoint id, or null for Windows' default. A new pick is opened on the next pass.
        internal string Chosen { get { return chosen; } set { chosen = value; relook = true; wake.Set(); } }

        // Their cable pick on Settings' Output, an endpoint id, or null for VB-CABLE's before Steam's. Moves her on the next pass.
        internal string CableChosen { get { return cableChosen; } set { cableChosen = value; relook = true; wake.Set(); } }

        // Mixing into CABLE Input right now.
        internal bool Mixing { get { return open; } }

        internal string MicName { get { MixDevice device = micDevice; return device == null ? null : device.Name; } }

        internal string CableName { get { MixDevice device = cableDevice; return device == null || !open ? null : device.Name; } }

        // How far the mic's clock is being steered, as a part of its rate, for the smoke test.
        internal double Steered { get { MixResampler resampler = micRate; return resampler == null ? 0 : resampler.Step / resampler.Ratio - 1; } }

        // VB-CABLE's two ends: CABLE Input, which she plays into, and CABLE Output, which Discord or Roblox hears. A renamed one still
        // has VB-Audio's own name for the cable in brackets after it. Steam's virtual mic has the same name on both ends, "Speakers
        // (Steam Streaming Microphone)" to play into and "Microphone (Steam Streaming Microphone)" to hear.
        private static readonly Regex InputName = new Regex(@"\bCABLE\s+Input\b|VB-Audio Virtual Cable", RegexOptions.IgnoreCase);
        private static readonly Regex OutputName = new Regex(@"\bCABLE(?:-\w+)?\s+Output\b|VB-Audio Virtual Cable|Steam Streaming Microphone", RegexOptions.IgnoreCase);
        private static readonly Regex VbOutput = new Regex(@"\bCABLE\s+Output\b|VB-Audio Virtual Cable", RegexOptions.IgnoreCase);
        private static readonly Regex SteamName = new Regex(@"Steam Streaming Microphone", RegexOptions.IgnoreCase);

        internal static bool IsCableIn(string name) { return !string.IsNullOrEmpty(name) && InputName.IsMatch(name); }

        // A cable's far end, VB-CABLE's or Steam's, which carries her voice while it's in their mic.
        internal static bool IsCableOut(string name) { return !string.IsNullOrEmpty(name) && OutputName.IsMatch(name); }

        internal static bool IsSteam(string name) { return !string.IsNullOrEmpty(name) && SteamName.IsMatch(name); }

        // The end they pick as their mic in Discord or Roblox for a cable.
        internal static string EndOf(MixDevice cable) { return cable != null && IsSteam(cable.Name) ? SteamEnd : CableEnd; }

        // Settings' note for an end, short enough for its column.
        internal static string PickLine(string end) { return end == SteamEnd ? "Pick Steam Streaming Microphone in Discord or Roblox." : "Pick " + end + " as your mic in Discord or Roblox."; }

        // The cable from Windows' active speakers and mics, with its far end: the one they picked on Settings' Output while it's
        // there, else VB-CABLE's before Steam's, since they installed it for this, and either with its far end on before one
        // Windows has turned off. Without a far end it's still the cable, for Settings to say to turn it on. Null when there's no cable.
        internal static MixDevice CableFor(List<MixDevice> speakers, List<MixDevice> mics, string chosen = null)
        {
            List<MixDevice> cables = Cables(speakers, mics);
            if (!string.IsNullOrEmpty(chosen)) foreach (MixDevice cable in cables) if (cable.Id == chosen) return cable;
            foreach (MixDevice cable in cables) if (cable.Far != null) return cable;
            return cables.Count > 0 ? cables[0] : null;
        }

        // Every cable from Windows' active speakers, VB-CABLE's before Steam's, each with its far end when Windows lists it.
        internal static List<MixDevice> Cables(List<MixDevice> speakers, List<MixDevice> mics)
        {
            List<MixDevice> cables = new List<MixDevice>();
            foreach (bool steam in new[] { false, true })
                foreach (MixDevice found in speakers)
                {
                    if (!(steam ? IsSteam(found.Name) : IsCableIn(found.Name))) continue;
                    MixDevice far = FarFor(found, mics);
                    cables.Add(new MixDevice(found.Id, found.Name, far == null ? null : far.Id));
                }
            return cables;
        }

        // What Discord or Roblox lists a cable's far end as, like "Microphone (Steam Streaming Microphone)": Windows' name for it,
        // or while Windows has it off, its usual one.
        internal static string FarName(MixDevice cable)
        {
            string named = cable == null || cable.Far == null ? null : Ears.ListedName(cable.Far);
            if (!string.IsNullOrEmpty(named)) return named;
            return cable != null && IsSteam(cable.Name) ? "Microphone (" + SteamEnd + ")" : "CABLE Output (VB-Audio Virtual Cable)";
        }

        // The line in Settings when Windows has a cable's far end muted, so friends hear nothing.
        internal static string MutedLine(string end) { return "Windows has " + end + " muted."; }

        // The line in Settings when Windows has a cable's far end turned off, so Discord or Roblox can't pick it.
        internal static string OffLine(string end) { return "Turn on " + end + " in Windows."; }

        // The far end of a cable from Windows' active mics: Steam's for Steam's, CABLE Output for VB-CABLE's, or a renamed one with
        // VB-Audio's own name for the cable in brackets after it. Null when it's not listed.
        internal static MixDevice FarFor(MixDevice cable, List<MixDevice> mics)
        {
            bool steam = IsSteam(cable.Name);
            foreach (MixDevice found in mics) if (steam ? IsSteam(found.Name) : VbOutput.IsMatch(found.Name)) return found;
            return null;
        }

        // The mic for a pick from Windows' active mics: the picked one, else Windows' default, else the first, but never a cable's
        // far end, which carries her own voice while it's in their mic. Null when there's no other mic.
        internal static MixDevice MicFor(List<MixDevice> mics, string picked, string defaultId)
        {
            MixDevice usual = null, first = null;
            foreach (MixDevice found in mics)
            {
                if (IsCableOut(found.Name)) continue;
                if (!string.IsNullOrEmpty(picked) && found.Id == picked) return found;
                if (found.Id == defaultId) usual = found;
                if (first == null) first = found;
            }
            return usual ?? first;
        }

        // Her speakers hand over what they play here. Only her voice's speakers do, so her sound effects stay out of their mic.
        // It comes at her own level, not her speakers' volume, and goes in at Speaking.
        internal static void Hear(object speaker, short[] samples, int rate)
        {
            MicMix running = Current;
            if (running != null && samples != null && samples.Length > 0 && rate > 0) running.Take(speaker, samples, rate, true);
        }

        // A speaker stopped or was cut off: what it had waiting for the cable goes too.
        internal static void Hush(object speaker)
        {
            MicMix running = Current;
            if (running != null) running.Drop(speaker);
        }

        // Leveled, it's lifted or lowered to Speaking on its way in; otherwise it goes in at its own level.
        internal void Take(object speaker, short[] samples, int rate, bool leveled = false)
        {
            // Nothing waits while there's no cable to play it into. A copy waits, since a speaker's caller may reuse its own.
            if (!open) return;
            short[] copy = (short[])samples.Clone();
            lock (gate)
            {
                if (!open) return;
                Lane lane = lanes.Find(delegate(Lane each) { return each.Owner == speaker; });
                if (lane == null) { lane = new Lane(speaker); lanes.Add(lane); }
                lane.Add(copy, rate, leveled);
            }
        }

        internal void Drop(object speaker)
        {
            lock (gate)
            {
                Lane lane = lanes.Find(delegate(Lane each) { return each.Owner == speaker; });
                if (lane != null) lane.Clear();
            }
        }

        // Samples of her voice still waiting for the cable, for the smoke test.
        internal int Waiting { get { lock (gate) { int total = 0; foreach (Lane lane in lanes) total += lane.Waiting; return total; } } }

        internal void Start()
        {
            if (worker != null) return;
            stopping = false;
            Current = this;
            worker = new Thread(Run) { IsBackground = true, Name = "Capri mic mix" };
            worker.SetApartmentState(ApartmentState.MTA);
            worker.Priority = ThreadPriority.Highest;
            worker.Start();
        }

        // Stops the thread and lets both devices go. A mix stepped by hand closes at once.
        internal void Stop()
        {
            if (Current == this) Current = null;
            Thread running = worker;
            worker = null;
            if (running == null) { Close(); return; }
            stopping = true;
            // Nothing more of hers goes in from here, even while the worker's stuck in a device call (Codex review, 2 Nov).
            open = false;
            lock (gate) lanes.Clear();
            wake.Set();
            if (Thread.CurrentThread != running && !running.Join(3000)) Note("Her mic mix took too long to stop.");
        }

        internal bool Running { get { Thread running = worker; return running != null && running.IsAlive; } }

        public void Dispose() { Stop(); }

        [DllImport("avrt.dll", CharSet = CharSet.Unicode)] private static extern IntPtr AvSetMmThreadCharacteristics(string task, ref int index);
        [DllImport("avrt.dll")] private static extern bool AvRevertMmThreadCharacteristics(IntPtr handle);

        private void Run()
        {
            // Windows' audio scheduling class, so a busy game doesn't make her late with the cable.
            IntPtr boosted = IntPtr.Zero;
            int task = 0;
            try { boosted = AvSetMmThreadCharacteristics("Pro Audio", ref task); }
            catch (Exception) { }
            try
            {
                while (!stopping)
                {
                    try { Step(); }
                    catch (Exception error) { Note("Her mic mix hit a snag: " + error.Message); }
                    if (stopping) break;
                    if (handles == null) handles = Handles();
                    if (handles.Length == 1) wake.WaitOne(cable == null ? 250 : 5);
                    else WaitHandle.WaitAny(handles, cable == null ? 250 : 20);
                }
            }
            finally
            {
                Close();
                if (boosted != IntPtr.Zero) try { AvRevertMmThreadCharacteristics(boosted); } catch (Exception) { }
            }
        }

        private WaitHandle[] Handles()
        {
            List<WaitHandle> waits = new List<WaitHandle> { wake };
            if (mic != null && mic.Ready != null) waits.Add(mic.Ready);
            if (cable != null && cable.Ready != null) waits.Add(cable.Ready);
            return waits.ToArray();
        }

        // One wake: the cable opened when it's time, then a pass. After that, while the cable has the most it'll have to play so
        // a slow Windows call can't leave it short, the devices are checked again and the mic opened when it's time.
        internal void Step()
        {
            double now = clock();
            if (cable == null)
            {
                if (now < cableAt) return;
                OpenCable(now);
                if (cable == null) return;
                lookAt = now + LookGap;
            }
            try { Pass(); }
            catch (Exception error) { LoseCable(now, error.Message); }
            if (cable != null && (now >= lookAt || relook))
            {
                lookAt = now + LookGap;
                relook = false;
                Look(now);
            }
            if (cable != null && now >= trimAt) Trim(now);
            if (cable != null && mic == null && now >= micAt) OpenMic(now);
            Problem = cable == null ? cableProblem : cableDevice.Far == null ? OffLine(EndOf(cableDevice)) : farMuted ? MutedLine(EndOf(cableDevice)) : micProblem;
        }

        // The cable that's open is still the one to use and the mic is still the one to hear: a new pick, a new Windows default,
        // their own mic plugged back in or VB-CABLE newly installed over Steam's moves her to it. A far end turned on or off in
        // Windows is picked up too.
        private void Look(double now)
        {
            MixDevice wantCable, wantMic;
            try
            {
                wantCable = devices.Cable(cableChosen);
                wantMic = devices.Mic(chosen);
            }
            catch (Exception) { return; }
            if (wantCable == null || wantCable.Id != cableDevice.Id)
            {
                LoseCable(now, wantCable == null ? cableDevice.Name + " is gone." : "The cable changed to " + wantCable.Name + ".");
                return;
            }
            if (wantCable.Far != cableDevice.Far)
            {
                cableDevice = wantCable;
                trimRead = false;
                trimAt = now;
            }
            if (mic != null && (wantMic == null || wantMic.Id != micDevice.Id)) { CloseMic(); micAt = now; }
            else if (mic == null && wantMic != null) micAt = now;
        }

        // A far end Windows turns up, like Steam's mic at its +30 dB, would play their mic and her voice that much too loud, so
        // the mix goes in that much under. One turned down is left alone, since they chose it. A read that fails keeps the last
        // trim, or before any read plays Steam's under by its usual +30 dB.
        private void Trim(double now)
        {
            trimAt = now + TrimGap;
            MixDevice found = cableDevice;
            bool muted, read;
            double boost;
            try { read = devices.Boost(found, out boost, out muted); }
            catch (Exception) { read = false; boost = 0; muted = false; }
            float was = trim;
            if (read)
            {
                trimRead = true;
                farMuted = muted;
                trim = boost > 0.1 ? (float)Math.Pow(10, -Math.Min(boost, 60) / 20) : 1;
            }
            else if (!trimRead)
            {
                farMuted = false;
                trim = IsSteam(found.Name) ? (float)Math.Pow(10, -SteamBoost / 20) : 1;
            }
            // A change of a decibel or more goes in the notes, so a volume slider being dragged doesn't fill them.
            if (Math.Abs(20 * Math.Log10(trim / was)) >= 1)
                Note("Her mic mix plays " + found.Name + " " + (-20 * Math.Log10(trim)).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " dB under, " + (read ? "since Windows turns its far end up that much." : "Steam's usual, since Windows won't say its far end's volume."));
        }

        // What the mix is played at, for the smoke test.
        internal float Trimmed { get { return trim; } }

        private void OpenCable(double now)
        {
            cableAt = now + RetryGap;
            MixDevice found;
            try { found = devices.Cable(cableChosen); }
            catch (Exception error) { cableProblem = Unopened; Note("Windows wouldn't list its speakers for her mic mix: " + error.Message); return; }
            if (found == null) { cableProblem = Missing; Problem = Missing; return; }
            try { cable = devices.OpenCable(found); }
            catch (Exception error) { cableProblem = Unopened; Problem = Unopened; Note(found.Name + " wouldn't open for her mic mix: " + error.Message); return; }
            cableDevice = found;
            trim = 1;
            trimRead = false;
            Trim(now);
            int rate = cable.Rate;
            period = Math.Max(1, cable.Period);
            // Two of Windows' periods, at least 10 ms more than one, so a late wake still finds something to play.
            target = period + Math.Max(period, rate / 100);
            jump = rate * 40 / 1000;
            queue = new Ring(rate);
            limiter = new SoftLimiter(rate);
            mix = new float[Math.Max(target, rate / 5)];
            made = new float[rate / 2];
            if (mic != null) Tune();
            cableProblem = null;
            open = true;
            handles = null;
            CableOpens++;
            Note("Her mic mix opened " + found.Name + " at " + rate + " Hz, periods of " + period + " frames, topped up to " + target + " frames.");
        }

        private void OpenMic(double now)
        {
            micAt = now + RetryGap;
            MixDevice found;
            try { found = devices.Mic(chosen); }
            catch (Exception) { found = null; }
            if (found == null) { micProblem = NoMic; micAt = now + LookGap; return; }
            try { mic = devices.OpenMic(found); }
            catch (Exception error) { micProblem = MicGone; Note("Her mic mix couldn't open " + found.Name + ": " + error.Message); return; }
            micDevice = found;
            micProblem = null;
            heardAt = now;
            Tune();
            handles = null;
            MicOpens++;
            Note("Her mic mix hears " + found.Name + " at " + mic.Rate + " Hz in packets of " + mic.Period + " frames.");
        }

        // The mic's resampler and queue for the cable's rate. The first top-up after this lines the queue up.
        private void Tune()
        {
            micRate = new MixResampler(mic.Rate, cable.Rate);
            queue.Clear();
            steering = false;
            cushion = Math.Max(1, (int)Math.Ceiling((double)Math.Max(1, mic.Period) * cable.Rate / mic.Rate));
            if (heard.Length < mic.Rate / 2) heard = new float[mic.Rate / 2];
        }

        private void LoseMic(double now, string why)
        {
            CloseMic();
            micAt = now + RetryGap;
            micProblem = MicGone;
            MicLosses++;
            Note("Her mic mix lost the mic: " + why);
        }

        private void LoseCable(double now, string why)
        {
            CloseMic();
            CloseCable();
            cableAt = now + RetryGap;
            Problem = cableProblem = Unopened;
            CableLosses++;
            farMuted = trimRead = false;
            Note("Her mic mix lost its cable: " + why);
        }

        private void CloseMic()
        {
            IMixMic closing = mic;
            mic = null;
            micDevice = null;
            micRate = null;
            handles = null;
            if (closing != null) try { closing.Dispose(); } catch (Exception) { }
        }

        private void CloseCable()
        {
            open = false;
            IMixCable closing = cable;
            cable = null;
            cableDevice = null;
            handles = null;
            lock (gate) lanes.Clear();
            if (closing != null) try { closing.Dispose(); } catch (Exception) { }
        }

        private void Close()
        {
            CloseMic();
            CloseCable();
        }

        // What the mic heard goes in its queue at the cable's rate, then the cable is topped up: the mic, her voice from each of
        // her speakers, and the limiter over both.
        private void Pass()
        {
            double now = clock();
            if (mic != null)
            {
                try
                {
                    int got;
                    while ((got = mic.Read(heard)) > 0)
                    {
                        heardAt = now;
                        micRate.Push(heard, 0, got);
                        int resampled;
                        while ((resampled = micRate.Pull(made, 0, made.Length)) > 0) queue.Add(made, resampled);
                    }
                    if (now - heardAt > Hushed) throw new MixLost("Nothing came from it for " + Hushed + " s.");
                }
                catch (Exception error) { LoseMic(now, error.Message); }
            }
            int queued = cable.Queued(), want = Math.Min(target - queued, mix.Length);
            if (want <= 0) return;
            if (mic != null && Steer(want, now))
            {
                int took = queue.Take(mix, want);
                if (took < want)
                {
                    // The mic's packet is late. While the cable still has a period to play, just what came goes in and the rest
                    // waits for the packet, so a busy PC's late packets don't turn into gaps. Past that, silence fills in.
                    int need = Math.Max(took, Math.Min(want, period - queued));
                    if (need > took) Interlocked.Add(ref Starved, need - took);
                    want = need;
                    if (want <= 0) return;
                }
            }
            else Array.Clear(mix, 0, want);
            lock (gate) foreach (Lane lane in lanes) lane.Mix(mix, want, cable.Rate);
            limiter.Run(mix, want);
            if (trim != 1) for (int i = 0; i < want; i++) mix[i] *= trim;
            // A pass that was stuck in a device call when the switch went off writes nothing once it comes back.
            if (stopping) return;
            cable.Write(mix, want);
            Interlocked.Add(ref Written, want);
        }

        // Keeps a packet and a half of the mic queued after each top-up, counting what the mic has heard since its last packet,
        // so the level doesn't jump by a packet as the mic's packets slide past the cable's periods. More, and the mic's clock
        // runs fast, so it's resampled a hair faster until the level's back; less, a hair slower. Part of that nudge comes
        // straight from how far off the level is and part builds up while it stays off, which ends up matching the two clocks
        // exactly instead of settling a little short. While the mic warms up, or after it sent nothing for a while, the cable
        // gets silence until enough is queued, then what's over is dropped and it starts from there; a pile-up of 40 ms is
        // dropped too. False while it warms up.
        private bool Steer(int want, double now)
        {
            double packet = (double)cushion / cable.Rate;
            if (steering && now - heardAt > Math.Max(0.05, 4 * packet)) steering = false;
            double coming = Math.Max(0, Math.Min(cushion, (now - heardAt) * cable.Rate)), aim = 1.5 * cushion, left = queue.Count - want + coming;
            if (!steering)
            {
                if (left < aim) return false;
                queue.Drop((int)(left - aim));
                level = aim;
                held = 0;
                steeredAt = now;
                steering = true;
                return true;
            }
            if (left > aim + jump)
            {
                queue.Drop((int)(left - aim));
                left = level = aim;
            }
            double since = Math.Max(0, now - steeredAt);
            steeredAt = now;
            level += (left - level) * Math.Min(1, since / Settle);
            double off = (level - aim) / cable.Rate;
            held = Math.Max(-MostSteer, Math.Min(MostSteer, held + off * Lasting * since));
            micRate.Step = micRate.Ratio * (1 + Math.Max(-MostSteer, Math.Min(MostSteer, off * Pull + held)));
            return true;
        }

        // errors.txt gets each new thing once, written off the mix thread so the cable never waits on the disk. One pool thread
        // at a time writes them, so a burst of them can't crowd out the rest of her.
        private static readonly Queue<string> notes = new Queue<string>();
        private static bool noting;

        private void Note(string what)
        {
            if (what == logged) return;
            logged = what;
            lock (notes)
            {
                if (notes.Count < 64) notes.Enqueue(what);
                if (noting) return;
                noting = true;
            }
            ThreadPool.QueueUserWorkItem(delegate { WriteNotes(); });
        }

        private static void WriteNotes()
        {
            while (true)
            {
                string next;
                lock (notes)
                {
                    if (notes.Count == 0) { noting = false; return; }
                    next = notes.Dequeue();
                }
                try { Brain.Log("mic mix", -1, next); }
                catch (Exception) { }
            }
        }

        // Notes still waiting for errors.txt, for the smoke test.
        internal static bool Noting { get { lock (notes) return noting; } }

        // Her voice from one of her speakers, waiting for the cable and resampled to its rate as it goes in.
        private sealed class Lane
        {
            internal readonly object Owner;
            private readonly Queue<short[]> waiting = new Queue<short[]>();
            private MixResampler resampler;
            private int rate, queued;
            private bool tail;
            private float[] fed = new float[0], heard = new float[0];
            // How loud her voiced moments have been, 0 before any or for a lane that keeps its own level.
            private double spoken;

            internal Lane(object owner) { Owner = owner; }

            internal int Waiting { get { return queued; } }

            // What her voice is multiplied by on its way into the mix.
            internal float Lift { get { return spoken > 0 ? (float)Math.Max(LeastLift, Math.Min(MostLift, Speaking / spoken)) : 1; } }

            internal void Add(short[] samples, int sampleRate, bool leveled)
            {
                // A speaker reopens for a new rate, which clears it first, so what's waiting is all at one rate.
                if (sampleRate != rate) { Clear(); rate = sampleRate; }
                if (leveled) Measure(samples, sampleRate);
                waiting.Enqueue(samples);
                queued += samples.Length;
                while (queued > MostWaiting * rate && waiting.Count > 1) queued -= waiting.Dequeue().Length;
            }

            internal void Clear()
            {
                waiting.Clear();
                queued = 0;
                resampler = null;
                tail = false;
            }

            // Her voiced moments in what just came, folded into how loud she's been, so the lift settles within a line or two
            // and a quiet word doesn't swing it.
            private void Measure(short[] samples, int sampleRate)
            {
                int piece = Math.Max(1, sampleRate / 50), pieces = 0;
                double total = 0;
                for (int at = 0; at + piece <= samples.Length; at += piece)
                {
                    double energy = 0;
                    for (int i = at; i < at + piece; i++) energy += (double)samples[i] * samples[i];
                    energy /= piece * 32768.0 * 32768.0;
                    if (energy < Voiced * Voiced) continue;
                    total += energy;
                    pieces++;
                }
                if (pieces == 0) return;
                double level = Math.Sqrt(total / pieces);
                spoken = spoken > 0 ? spoken * 0.7 + level * 0.3 : level;
            }

            // Adds the next count samples of her voice at the cable's rate into the mix. Once she runs out, a moment of silence
            // goes in after her last sample so it comes out too.
            internal void Mix(float[] into, int count, int to)
            {
                if (rate == 0) return;
                if (resampler == null || resampler.To != to) resampler = new MixResampler(rate, to);
                if (heard.Length < count) heard = new float[count];
                float lift = Lift / 32768f;
                int got = 0;
                while (got < count)
                {
                    got += resampler.Pull(heard, got, count - got);
                    if (got >= count) break;
                    if (waiting.Count > 0)
                    {
                        short[] next = waiting.Dequeue();
                        queued -= next.Length;
                        if (fed.Length < next.Length) fed = new float[next.Length];
                        for (int i = 0; i < next.Length; i++) fed[i] = next[i] * lift;
                        resampler.Push(fed, 0, next.Length);
                        tail = true;
                    }
                    else if (tail) { resampler.Silence(); tail = false; }
                    else break;
                }
                for (int i = 0; i < got; i++) into[i] += heard[i];
            }
        }

        // The mic's queue at the cable's rate: a ring that drops its oldest when it's full.
        private sealed class Ring
        {
            private readonly float[] items;
            private int start, count;

            internal Ring(int size) { items = new float[Math.Max(1, size)]; }

            internal int Count { get { return count; } }

            internal void Clear() { start = count = 0; }

            internal void Drop(int frames)
            {
                frames = Math.Max(0, Math.Min(count, frames));
                start = (start + frames) % items.Length;
                count -= frames;
            }

            internal void Add(float[] from, int frames)
            {
                int skip = Math.Max(0, frames - items.Length);
                frames -= skip;
                if (count + frames > items.Length) Drop(count + frames - items.Length);
                int end = (start + count) % items.Length, first = Math.Min(frames, items.Length - end);
                Array.Copy(from, skip, items, end, first);
                if (frames > first) Array.Copy(from, skip + first, items, 0, frames - first);
                count += frames;
            }

            // Moves up to frames into into, and zeros the rest of it. Returns how many were there.
            internal int Take(float[] into, int frames)
            {
                int took = Math.Min(frames, count), first = Math.Min(took, items.Length - start);
                Array.Copy(items, start, into, 0, first);
                if (took > first) Array.Copy(items, 0, into, first, took - first);
                if (frames > took) Array.Clear(into, took, frames - took);
                Drop(took);
                return took;
            }
        }
    }

    // A windowed sinc like VoiceFx's, for a stream: samples go in as they come and come out at another rate. Going down in rate
    // it filters first, so nothing aliases, and going up it keeps the images out. Its step can be nudged a hair while it runs,
    // which is how her mic mix keeps a mic's clock and the cable's together.
    internal sealed class MixResampler
    {
        private const int Phases = 256;
        private const int Reach = 16;
        internal readonly int From, To;
        // Samples in for each one out, as built, and as it's nudged now.
        internal readonly double Ratio;
        internal double Step;
        private readonly int reach, taps;
        private readonly float[] kernel;
        private float[] held = new float[4096];
        private int count;
        private double at;

        internal MixResampler(int from, int to)
        {
            From = from;
            To = to;
            Ratio = Step = (double)from / to;
            // At the same rate it only steps between samples, so it keeps everything; otherwise it cuts a little under the lower Nyquist.
            double cutoff = from == to ? 1 : 0.95 * Math.Min(1, 1 / Ratio);
            reach = (int)Math.Ceiling(Reach / cutoff);
            taps = 2 * reach;
            kernel = new float[(Phases + 1) * taps];
            double[] row = new double[taps];
            for (int phase = 0; phase <= Phases; phase++)
            {
                double fraction = (double)phase / Phases, total = 0;
                for (int k = 0; k < taps; k++)
                {
                    double x = k - reach + 1 - fraction, t = x * cutoff;
                    row[k] = (Math.Abs(t) < 1e-9 ? 1 : Math.Sin(Math.PI * t) / (Math.PI * t)) * (Math.Abs(x) >= reach ? 0 : 0.5 + 0.5 * Math.Cos(Math.PI * x / reach));
                    total += row[k];
                }
                for (int k = 0; k < taps; k++) kernel[phase * taps + k] = (float)(row[k] / total);
            }
            // Silence before the first sample, so the first one comes out as soon as the samples after it are in.
            count = reach - 1;
            at = reach - 1;
        }

        // How many input samples it holds back to see ahead.
        internal int Lookahead { get { return reach; } }

        internal void Push(float[] samples, int offset, int length)
        {
            if (count + length > held.Length)
            {
                float[] grown = new float[Math.Max(held.Length * 2, count + length)];
                Array.Copy(held, grown, count);
                held = grown;
            }
            Array.Copy(samples, offset, held, count, length);
            count += length;
        }

        // Enough silence after the last sample that everything before it comes out.
        internal void Silence()
        {
            if (count + reach > held.Length) Push(new float[reach], 0, reach);
            else { Array.Clear(held, count, reach); count += reach; }
        }

        // As many samples out as the ones in allow, up to most.
        internal int Pull(float[] into, int offset, int most)
        {
            int made = 0;
            while (made < most)
            {
                int whole = (int)at;
                if (whole + reach >= count) break;
                int row = (int)Math.Round((at - whole) * Phases) * taps, start = whole - reach + 1;
                float value = 0;
                for (int k = 0; k < taps; k++) value += held[start + k] * kernel[row + k];
                into[offset + made++] = value;
                at += Step;
            }
            int done = Math.Min(count, Math.Max(0, (int)at - reach + 1));
            if (done > 0)
            {
                Array.Copy(held, done, held, 0, count - done);
                count -= done;
                at -= done;
            }
            return made;
        }
    }

    // Keeps the mix under full scale without a hard edge. Below the knee every sample goes through untouched; above it the gain
    // eases off so peaks bend toward the ceiling and never reach it. It drops at once on a peak and comes back over about 80 ms.
    internal sealed class SoftLimiter
    {
        internal const float Knee = 0.8f, Ceiling = 0.97f;
        private readonly float letUp;
        private float envelope;

        internal SoftLimiter(int rate) { letUp = (float)Math.Exp(-1.0 / (0.08 * Math.Max(1, rate))); }

        internal void Run(float[] samples, int count)
        {
            for (int i = 0; i < count; i++)
            {
                float sample = samples[i];
                if (float.IsNaN(sample) || float.IsInfinity(sample)) sample = samples[i] = 0;
                float size = Math.Abs(sample);
                envelope = Math.Max(size, envelope * letUp);
                if (envelope <= Knee) continue;
                float bent = Knee + (Ceiling - Knee) * (float)Math.Tanh((envelope - Knee) / (Ceiling - Knee));
                samples[i] = sample * (bent / envelope);
            }
        }
    }

    // A WASAPI mix format read and written as samples from -1 to 1: 32-bit float or 16, 24 or 32-bit PCM, any number of channels.
    // Her mix hears a mic's channels as one and plays the same into every channel of the cable.
    internal sealed class PcmShape
    {
        internal readonly int Rate, Channels, Bits;
        internal readonly bool Float;
        private float[] floats = new float[0];
        private short[] shorts = new short[0];
        private int[] ints = new int[0];
        private byte[] bytes = new byte[0];

        internal PcmShape(IntPtr format)
        {
            int tag = Marshal.ReadInt16(format, 0) & 0xFFFF;
            Channels = Marshal.ReadInt16(format, 2);
            Rate = Marshal.ReadInt32(format, 4);
            int align = Marshal.ReadInt16(format, 12);
            Bits = Marshal.ReadInt16(format, 14);
            if (tag == 0xFFFE && Marshal.ReadInt16(format, 16) >= 22) tag = Marshal.ReadInt32(format, 24);
            Float = tag == 3 && Bits == 32;
            if ((!Float && !(tag == 1 && (Bits == 16 || Bits == 24 || Bits == 32))) || Channels < 1 || Rate < 8000 || align != Channels * Bits / 8)
                throw new MixLost("Windows mixes it in a format she can't use (tag " + tag + ", " + Bits + "-bit, " + Channels + " channels, " + Rate + " Hz).");
        }

        internal string Name { get { return Rate + " Hz, " + Channels + (Channels == 1 ? " channel, " : " channels, ") + Bits + "-bit " + (Float ? "float" : "PCM"); } }

        private static void Room<T>(ref T[] buffer, int size) { if (buffer.Length < size) buffer = new T[size]; }

        // A packet's frames as one channel, into into from offset on.
        internal void Hear(IntPtr data, int frames, float[] into, int offset)
        {
            int count = frames * Channels;
            float share = 1f / Channels;
            if (Float)
            {
                Room(ref floats, count);
                Marshal.Copy(data, floats, 0, count);
                for (int frame = 0; frame < frames; frame++)
                {
                    float sum = 0;
                    for (int channel = frame * Channels; channel < (frame + 1) * Channels; channel++) sum += floats[channel];
                    into[offset + frame] = float.IsNaN(sum) || float.IsInfinity(sum) ? 0 : sum * share;
                }
            }
            else if (Bits == 16)
            {
                Room(ref shorts, count);
                Marshal.Copy(data, shorts, 0, count);
                for (int frame = 0; frame < frames; frame++)
                {
                    float sum = 0;
                    for (int channel = frame * Channels; channel < (frame + 1) * Channels; channel++) sum += shorts[channel];
                    into[offset + frame] = sum * share / 32768f;
                }
            }
            else if (Bits == 24)
            {
                Room(ref bytes, count * 3);
                Marshal.Copy(data, bytes, 0, count * 3);
                for (int frame = 0; frame < frames; frame++)
                {
                    float sum = 0;
                    for (int channel = frame * Channels; channel < (frame + 1) * Channels; channel++) sum += bytes[channel * 3] | bytes[channel * 3 + 1] << 8 | (sbyte)bytes[channel * 3 + 2] << 16;
                    into[offset + frame] = sum * share / 8388608f;
                }
            }
            else
            {
                Room(ref ints, count);
                Marshal.Copy(data, ints, 0, count);
                for (int frame = 0; frame < frames; frame++)
                {
                    double sum = 0;
                    for (int channel = frame * Channels; channel < (frame + 1) * Channels; channel++) sum += ints[channel];
                    into[offset + frame] = (float)(sum * share / 2147483648.0);
                }
            }
        }

        // frames of one channel into every channel at data.
        internal void Play(float[] mono, int frames, IntPtr data)
        {
            int count = frames * Channels;
            if (Float)
            {
                Room(ref floats, count);
                for (int frame = 0; frame < frames; frame++) for (int channel = 0; channel < Channels; channel++) floats[frame * Channels + channel] = mono[frame];
                Marshal.Copy(floats, 0, data, count);
            }
            else if (Bits == 16)
            {
                Room(ref shorts, count);
                for (int frame = 0; frame < frames; frame++)
                {
                    short sample = (short)Math.Round(Math.Max(-1f, Math.Min(1f, mono[frame])) * 32767);
                    for (int channel = 0; channel < Channels; channel++) shorts[frame * Channels + channel] = sample;
                }
                Marshal.Copy(shorts, 0, data, count);
            }
            else if (Bits == 24)
            {
                Room(ref bytes, count * 3);
                for (int frame = 0; frame < frames; frame++)
                {
                    int sample = (int)Math.Round(Math.Max(-1f, Math.Min(1f, mono[frame])) * 8388607);
                    for (int channel = 0; channel < Channels; channel++)
                    {
                        int at = (frame * Channels + channel) * 3;
                        bytes[at] = (byte)sample;
                        bytes[at + 1] = (byte)(sample >> 8);
                        bytes[at + 2] = (byte)(sample >> 16);
                    }
                }
                Marshal.Copy(bytes, 0, data, count * 3);
            }
            else
            {
                Room(ref ints, count);
                for (int frame = 0; frame < frames; frame++)
                {
                    int sample = (int)Math.Round(Math.Max(-1.0, Math.Min(1.0, mono[frame])) * 2147483647.0);
                    for (int channel = 0; channel < Channels; channel++) ints[frame * Channels + channel] = sample;
                }
                Marshal.Copy(ints, 0, data, count);
            }
        }
    }

    internal static class Wasapi
    {
        // AUDCLNT_STREAMFLAGS_EVENTCALLBACK: Windows sets an event each period instead of her polling.
        internal const int Woken = 0x00040000;

        internal static void Check(int result, string operation)
        {
            if (result != 0) throw new MixLost("WASAPI " + operation + " failed (0x" + result.ToString("X8") + ").");
        }

        internal static ISoundClient Client(ISoundDevices devices, string id, out ISoundDevice device)
        {
            Check(devices.GetDevice(id, out device), "find the device");
            if (device == null) throw new MixLost("Windows has no device " + id + ".");
            Guid clientId = typeof(ISoundClient).GUID;
            object found;
            Check(device.Activate(ref clientId, 23, IntPtr.Zero, out found), "activate");
            return (ISoundClient)found;
        }

        // A period in frames: Windows' default for the device, or 10 ms when it won't say.
        internal static int Period(ISoundClient client, int rate)
        {
            long normal, shortest;
            return client.GetDevicePeriod(out normal, out shortest) == 0 && normal > 0 ? (int)Math.Max(1, normal * rate / 10000000) : rate / 100;
        }

        internal static void Release(object com)
        {
            if (com != null && Marshal.IsComObject(com)) try { Marshal.ReleaseComObject(com); } catch (Exception) { }
        }
    }

    // Their mic over WASAPI in shared mode, woken by Windows each packet, in whatever format Windows mixes it in.
    internal sealed class MicIn : IMixMic
    {
        private readonly AutoResetEvent ready = new AutoResetEvent(false);
        private ISoundDevices devices;
        private ISoundDevice device;
        private ISoundClient client;
        private ISoundCapture capture;
        private readonly PcmShape shape;
        private readonly int period;

        internal MicIn(MixDevice mic)
        {
            IntPtr format = IntPtr.Zero;
            try
            {
                devices = (ISoundDevices)new SoundDevices();
                client = Wasapi.Client(devices, mic.Id, out device);
                Wasapi.Check(client.GetMixFormat(out format), "mix format");
                if (format == IntPtr.Zero) throw new MixLost("Windows gave no format for the mic.");
                shape = new PcmShape(format);
                // 100 ms of room, so a slow pass loses nothing. Packets still come every period, which sets the delay.
                Wasapi.Check(client.Initialize(0, Wasapi.Woken, 1000000, 0, format, IntPtr.Zero), "initialize");
                Mixer.NoDucking(client);
                period = Wasapi.Period(client, shape.Rate);
                Wasapi.Check(client.SetEventHandle(ready.SafeWaitHandle.DangerousGetHandle()), "event");
                Guid captureId = typeof(ISoundCapture).GUID;
                object service;
                Wasapi.Check(client.GetService(ref captureId, out service), "capture service");
                capture = (ISoundCapture)service;
                Wasapi.Check(client.Start(), "start");
            }
            catch { Dispose(); throw; }
            finally { if (format != IntPtr.Zero) Marshal.FreeCoTaskMem(format); }
        }

        public int Rate { get { return shape.Rate; } }
        public int Period { get { return period; } }
        public WaitHandle Ready { get { return ready; } }
        internal string Format { get { return shape.Name; } }

        public int Read(float[] into)
        {
            int got = 0;
            while (true)
            {
                int waiting;
                Wasapi.Check(capture.GetNextPacketSize(out waiting), "packet size");
                if (waiting <= 0 || got + waiting > into.Length) return got;
                IntPtr data;
                int frames, flags;
                long position, time;
                Wasapi.Check(capture.GetBuffer(out data, out frames, out flags, out position, out time), "get buffer");
                try
                {
                    int kept = Math.Min(frames, into.Length - got);
                    if ((flags & 2) != 0 || data == IntPtr.Zero) Array.Clear(into, got, kept);
                    else shape.Hear(data, kept, into, got);
                    got += kept;
                }
                finally { capture.ReleaseBuffer(frames); }
            }
        }

        public void Dispose()
        {
            try { if (client != null) client.Stop(); } catch (Exception) { }
            Wasapi.Release(capture);
            Wasapi.Release(client);
            Wasapi.Release(device);
            Wasapi.Release(devices);
            capture = null;
            client = null;
            device = null;
            devices = null;
            ready.Dispose();
        }
    }

    // CABLE Input over WASAPI in shared mode, in whatever format Windows mixes it in, woken each time Windows takes a period.
    internal sealed class CableOut : IMixCable
    {
        private readonly AutoResetEvent ready = new AutoResetEvent(false);
        private ISoundDevices devices;
        private ISoundDevice device;
        private ISoundClient client;
        private ISoundRender render;
        private readonly PcmShape shape;
        private readonly int period, frames;

        internal CableOut(MixDevice cable)
        {
            IntPtr format = IntPtr.Zero;
            try
            {
                devices = (ISoundDevices)new SoundDevices();
                client = Wasapi.Client(devices, cable.Id, out device);
                Wasapi.Check(client.GetMixFormat(out format), "mix format");
                if (format == IntPtr.Zero) throw new MixLost("Windows gave no format for CABLE Input.");
                shape = new PcmShape(format);
                // 50 ms of room. She only ever keeps about two periods in it.
                Wasapi.Check(client.Initialize(0, Wasapi.Woken, 500000, 0, format, IntPtr.Zero), "initialize");
                Mixer.NoDucking(client);
                Wasapi.Check(client.GetBufferSize(out frames), "buffer size");
                if (frames <= 0) throw new MixLost("Windows gave her no room on CABLE Input.");
                period = Math.Min(Wasapi.Period(client, shape.Rate), frames / 2);
                Wasapi.Check(client.SetEventHandle(ready.SafeWaitHandle.DangerousGetHandle()), "event");
                Guid renderId = typeof(ISoundRender).GUID;
                object service;
                Wasapi.Check(client.GetService(ref renderId, out service), "render service");
                render = (ISoundRender)service;
                // A period of silence first, so Windows has something the moment it starts.
                IntPtr data;
                Wasapi.Check(render.GetBuffer(period, out data), "get buffer");
                Wasapi.Check(render.ReleaseBuffer(period, 2), "release buffer");
                Wasapi.Check(client.Start(), "start");
            }
            catch { Dispose(); throw; }
            finally { if (format != IntPtr.Zero) Marshal.FreeCoTaskMem(format); }
        }

        public int Rate { get { return shape.Rate; } }
        public int Period { get { return period; } }
        public WaitHandle Ready { get { return ready; } }
        internal string Format { get { return shape.Name; } }

        public int Queued()
        {
            int padding;
            Wasapi.Check(client.GetCurrentPadding(out padding), "padding");
            return padding;
        }

        public void Write(float[] mono, int count)
        {
            count = Math.Min(count, frames - Queued());
            if (count <= 0) return;
            IntPtr data;
            Wasapi.Check(render.GetBuffer(count, out data), "get buffer");
            shape.Play(mono, count, data);
            Wasapi.Check(render.ReleaseBuffer(count, 0), "release buffer");
        }

        public void Dispose()
        {
            try { if (client != null) client.Stop(); } catch (Exception) { }
            Wasapi.Release(render);
            Wasapi.Release(client);
            Wasapi.Release(device);
            Wasapi.Release(devices);
            render = null;
            client = null;
            device = null;
            devices = null;
            ready.Dispose();
        }
    }

    // IMMDeviceCollection.
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISoundDeviceList
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int Item(int index, out ISoundDevice device);
    }

    // Her mix's devices on a real PC, from Windows' list of active endpoints and their names in Windows' own records.
    internal sealed class Endpoints : IMixDevices
    {
        // The far end's volume, kept open between reads, since her mix reads it four times a second.
        private readonly object reading = new object();
        private ISpeakerVolume volume;
        private string volumeOf;

        public MixDevice Cable(string chosen) { return MicMix.CableFor(Active(0), Active(1), chosen); }

        public List<MixDevice> Cables() { return MicMix.Cables(Active(0), Active(1)); }

        // Windows' audio sessions on the mic that are hearing it now, by program, like "Discord". Discord opens its mic while
        // it's in a call or testing it.
        public List<string> Listening(string mic)
        {
            List<string> names = new List<string>();
            if (string.IsNullOrEmpty(mic)) return names;
            ISoundDevices devices = null;
            ISoundDevice device = null;
            object manager = null;
            IMixSessions sessions = null;
            try
            {
                devices = (ISoundDevices)new SoundDevices();
                if (devices.GetDevice(mic, out device) != 0 || device == null) return names;
                Guid managerId = typeof(IMixSessionManager).GUID;
                if (device.Activate(ref managerId, 23, IntPtr.Zero, out manager) != 0 || manager == null) return names;
                int count;
                if (((IMixSessionManager)manager).GetSessionEnumerator(out sessions) != 0 || sessions == null || sessions.GetCount(out count) != 0) return names;
                for (int i = 0; i < count; i++)
                {
                    object control = null;
                    try
                    {
                        if (sessions.GetSession(i, out control) != 0 || control == null) continue;
                        IMixSession session = control as IMixSession;
                        int state, pid;
                        // 1 is AudioSessionStateActive: the program has the mic open now, not just earlier.
                        if (session == null || session.GetState(out state) != 0 || state != 1 || session.GetProcessId(out pid) != 0 || pid == 0) continue;
                        using (Process program = Process.GetProcessById(pid)) if (!names.Contains(program.ProcessName)) names.Add(program.ProcessName);
                    }
                    catch (Exception) { }
                    finally { Wasapi.Release(control); }
                }
                return names;
            }
            catch (Exception) { return names; }
            finally
            {
                Wasapi.Release(sessions);
                Wasapi.Release(manager);
                Wasapi.Release(device);
                Wasapi.Release(devices);
            }
        }

        // IAudioSessionManager2, up to its sessions.
        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMixSessionManager
        {
            [PreserveSig] int GetAudioSessionControl(IntPtr session, int flags, out IntPtr control);
            [PreserveSig] int GetSimpleAudioVolume(IntPtr session, int flags, out IntPtr volume);
            [PreserveSig] int GetSessionEnumerator(out IMixSessions sessions);
        }

        // IAudioSessionEnumerator.
        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMixSessions
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object session);
        }

        // IAudioSessionControl2, up to its program.
        [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMixSession
        {
            [PreserveSig] int GetState(out int state);
            [PreserveSig] int GetDisplayName(out IntPtr name);
            [PreserveSig] int SetDisplayName(IntPtr name, IntPtr context);
            [PreserveSig] int GetIconPath(out IntPtr path);
            [PreserveSig] int SetIconPath(IntPtr path, IntPtr context);
            [PreserveSig] int GetGroupingParam(out Guid group);
            [PreserveSig] int SetGroupingParam(ref Guid group, IntPtr context);
            [PreserveSig] int RegisterAudioSessionNotification(IntPtr notification);
            [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notification);
            [PreserveSig] int GetSessionIdentifier(out IntPtr identifier);
            [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr identifier);
            [PreserveSig] int GetProcessId(out int processId);
        }

        public bool Boost(MixDevice cable, out double boost, out bool muted)
        {
            boost = 0;
            muted = false;
            lock (reading)
            {
                try
                {
                    if (cable == null || string.IsNullOrEmpty(cable.Far)) { Forget(); return false; }
                    if (volume == null || volumeOf != cable.Far)
                    {
                        Forget();
                        volume = Volume(cable.Far);
                        volumeOf = cable.Far;
                        if (volume == null) return false;
                    }
                    float decibels;
                    bool off;
                    // A far end that's gone fails here, and is opened again on the next read.
                    if (volume.GetMasterVolumeLevel(out decibels) != 0 || float.IsNaN(decibels)) { Forget(); return false; }
                    if (volume.GetMute(out off) == 0) muted = off;
                    boost = decibels;
                    return true;
                }
                catch (Exception) { Forget(); return false; }
            }
        }

        private void Forget()
        {
            Wasapi.Release(volume);
            volume = null;
            volumeOf = null;
        }

        // A mic's own volume control, or null when Windows won't hand it over.
        private static ISpeakerVolume Volume(string id)
        {
            ISoundDevices devices = null;
            ISoundDevice device = null;
            try
            {
                devices = (ISoundDevices)new SoundDevices();
                Guid volumeId = typeof(ISpeakerVolume).GUID;
                object found;
                if (devices.GetDevice(id, out device) != 0 || device == null || device.Activate(ref volumeId, 23, IntPtr.Zero, out found) != 0) return null;
                return found as ISpeakerVolume;
            }
            finally
            {
                Wasapi.Release(device);
                Wasapi.Release(devices);
            }
        }

        public MixDevice Mic(string chosen) { return MicMix.MicFor(Active(1), chosen, Default(1)); }

        public IMixMic OpenMic(MixDevice mic) { return new MicIn(mic); }

        public IMixCable OpenCable(MixDevice cable) { return new CableOut(cable); }

        // Every active endpoint one way: 0 for speakers and cables in, 1 for mics.
        internal static List<MixDevice> Active(int flow)
        {
            List<MixDevice> found = new List<MixDevice>();
            ISoundDevices devices = null;
            ISoundDeviceList listed = null;
            try
            {
                devices = (ISoundDevices)new SoundDevices();
                IntPtr list;
                if (devices.EnumAudioEndpoints(flow, 1, out list) != 0 || list == IntPtr.Zero) return found;
                try { listed = (ISoundDeviceList)Marshal.GetObjectForIUnknown(list); }
                finally { Marshal.Release(list); }
                int count;
                if (listed.GetCount(out count) != 0) return found;
                for (int i = 0; i < count; i++)
                {
                    ISoundDevice device;
                    if (listed.Item(i, out device) != 0 || device == null) continue;
                    try
                    {
                        string id;
                        if (device.GetId(out id) == 0 && !string.IsNullOrEmpty(id)) found.Add(new MixDevice(id, Ears.EndpointName(id) ?? id));
                    }
                    finally { Marshal.ReleaseComObject(device); }
                }
                return found;
            }
            finally
            {
                Wasapi.Release(listed);
                Wasapi.Release(devices);
            }
        }

        internal static string Default(int flow)
        {
            ISoundDevices devices = null;
            ISoundDevice device = null;
            try
            {
                devices = (ISoundDevices)new SoundDevices();
                string id;
                if (devices.GetDefaultAudioEndpoint(flow, 0, out device) != 0 || device == null || device.GetId(out id) != 0) return null;
                return id;
            }
            catch (Exception) { return null; }
            finally
            {
                Wasapi.Release(device);
                Wasapi.Release(devices);
            }
        }
    }
}

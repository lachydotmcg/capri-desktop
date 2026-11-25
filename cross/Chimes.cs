using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;

namespace CapriDesktop
{
    internal enum Chime { Sent, Heard, Listening }

    internal static class Chimes
    {
        internal const int Rate = 48000;
        internal static readonly string[] Sets = { "glass", "pulse", "air", "shard", "burst", "crush" };
        internal static readonly string[] Titles = { "Glass", "Pulse", "Air", "Shard", "Burst", "Crush" };
        internal static readonly string[] Names = { "sent", "heard", "listening" };
        internal const int Glass = 0, Pulse = 1, Air = 2, Shard = 3, Burst = 4, Crush = 5;
        internal static readonly int[] Picked = { Glass, Shard, Burst, Crush, Pulse, Air };
        internal const int DefaultSet = Glass;
        internal const int DefaultVolume = 40;
        internal const int Gap = 300;
        internal const int Again = 700;
        internal const int Echo = 700;
        internal const int After = 300;
        internal const string NoPulsePlayer = "Her chimes need pacat or pw-cat. Install pulseaudio-utils or pipewire-bin.";
        internal const string NoMacPlayer = "Her chimes need afplay, which comes with macOS.";

        internal static volatile bool On = true;
        internal static volatile int Volume = DefaultVolume;
        internal static volatile int Set = DefaultSet;
        internal static bool Testing, Tested;
        internal static Action BeforePlay;

        private static readonly object gate = new object();
        private static readonly bool[] wanted = { true, true, true };
        private static readonly long[] lastOf = { long.MinValue / 4, long.MinValue / 4, long.MinValue / 4 };
        private static long last = long.MinValue / 4;
        private static long maskUntil = long.MinValue / 4;
        private static long maskFrom = long.MinValue / 4;
        private static int masks, maskBody;
        private static readonly int[] bodies = new int[Sets.Length * 3];
        private static readonly float[][] made = new float[Sets.Length * 3][];
        private static long leftUntil;
        private static int ticket, playTicket;
        private static Process current;
        private static bool warned;
        internal static int Played;

        internal static Func<short[], bool> Out = PlaySound;
        internal static Func<double> Left = delegate { return Remaining(); };
        internal static Func<long> Clock = delegate { return Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency; };

        internal static bool Wanted(Chime chime) { lock (gate) return wanted[(int)chime]; }
        internal static void Want(Chime chime, bool on) { lock (gate) wanted[(int)chime] = on; }

        private static double Remaining()
        {
            long end = Interlocked.Read(ref leftUntil);
            long now = Clock();
            return end <= now ? 0 : (end - now) / 1000.0;
        }

        internal static bool Play(Chime chime, bool preview = false)
        {
            Action before;
            Func<short[], bool> output = Out;
            int volume = Volume, set = Set;
            if (output == null || !On || volume <= 0 || Testing && !Tested || !Wanted(chime) && !preview) return false;
            short[] sound;
            int mine;
            lock (gate)
            {
                long now = Clock();
                if (now - last < Gap || now - lastOf[(int)chime] < Again && !preview) return false;
                Func<double> left = Left;
                if (left != null && left() > 0) return false;
                sound = Render(set, chime, volume);
                mine = ++ticket;
                playTicket = mine;
                last = lastOf[(int)chime] = now;
                maskUntil = now + sound.Length * 1000L / Rate + Echo;
                maskFrom = now;
                maskBody = Body(set, chime);
                masks++;
                Played++;
                Interlocked.Exchange(ref leftUntil, now + sound.Length * 1000L / Rate);
                before = BeforePlay;
            }
            try { if (before != null) before(); } catch (Exception) { }
            bool played;
            try { played = output(sound); }
            catch (Exception error) { Brain.Log("chime", -1, error.Message); played = false; }
            if (played) return true;
            lock (gate)
            {
                if (playTicket == mine)
                {
                    Interlocked.Exchange(ref leftUntil, 0);
                    maskUntil = 0;
                    if (Played > 0) Played--;
                }
            }
            return false;
        }

        internal static bool Masking { get { lock (gate) return Clock() < maskUntil; } }

        internal static bool Holding { get { lock (gate) return Clock() < maskUntil + After; } }

        internal static bool Mask(out int which, out long since, out int body)
        {
            lock (gate)
            {
                long now = Clock();
                which = masks;
                since = now - maskFrom;
                body = maskBody;
                return now < maskUntil;
            }
        }

        internal static void Reset()
        {
            lock (gate)
            {
                last = maskUntil = maskFrom = long.MinValue / 4;
                for (int i = 0; i < lastOf.Length; i++) lastOf[i] = long.MinValue / 4;
                Played = 0;
                Interlocked.Exchange(ref leftUntil, 0);
            }
        }

        internal static void Warm()
        {
            Stopwatch took = Stopwatch.StartNew();
            for (int set = 0; set < Sets.Length; set++)
                for (int chime = 0; chime < 3; chime++) Body(set, (Chime)chime);
            Warmed = took.ElapsedMilliseconds;
        }

        internal static long Warmed = -1;

        internal static double Gain(int volume) { return Math.Pow(Math.Max(0, Math.Min(100, volume)) / 100.0, 1.5); }

        internal static short[] Render(int set, Chime chime, int volume)
        {
            float[] shape = Made(set, chime);
            double gain = Gain(volume);
            short[] samples = new short[shape.Length];
            for (int i = 0; i < shape.Length; i++) samples[i] = (short)Math.Round(Math.Max(-1, Math.Min(1, shape[i] * gain)) * 32767);
            return samples;
        }

        private static int Body(int set, Chime chime)
        {
            set = Math.Max(0, Math.Min(Sets.Length - 1, set));
            int at = set * 3 + (int)chime;
            if (bodies[at] > 0) return bodies[at];
            float[] shape = Made(set, chime);
            int frame = Rate / 100, last = 0;
            double most = 0;
            double[] loud = new double[shape.Length / frame];
            for (int f = 0; f < loud.Length; f++)
            {
                double sum = 0;
                for (int i = f * frame; i < (f + 1) * frame; i++) sum += shape[i] * shape[i];
                loud[f] = Math.Sqrt(sum / frame);
                most = Math.Max(most, loud[f]);
            }
            for (int f = 0; f < loud.Length; f++) if (loud[f] >= most * 0.5) last = f;
            bodies[at] = (last + 1) * 10;
            return bodies[at];
        }

        private static float[] Made(int set, Chime chime)
        {
            set = Math.Max(0, Math.Min(Sets.Length - 1, set));
            int at = set * 3 + (int)chime;
            float[] shape = made[at];
            if (shape != null) return shape;
            shape = set == Pulse ? MakePulse(chime) : set == Air ? MakeAir(chime) : set == Shard ? MakeShard(chime) : set == Burst ? MakeBurst(chime) : set == Crush ? MakeCrush(chime) : MakeGlass(chime);
            made[at] = shape;
            return shape;
        }

        private const double D5 = 587.33, Fs5 = 739.99, A5 = 880.0, B5 = 987.77, D6 = 1174.66;
        private const double A3 = 220.0, D4 = 293.66, Fs4 = 369.99, A4 = 440.0;

        private static float[] MakeGlass(Chime chime)
        {
            double[] buffer = new double[Samples(0.34)];
            if (chime == Chime.Sent)
            {
                Strike(buffer, 0, A5, 0.8, 0.07);
                Strike(buffer, 0.045, D6, 1.0, 0.085);
            }
            else if (chime == Chime.Heard)
            {
                Strike(buffer, 0, D6, 0.75, 0.06);
                Strike(buffer, 0.062, A5, 1.0, 0.1);
            }
            else
            {
                Strike(buffer, 0, Fs5, 0.6, 0.06);
                Strike(buffer, 0.04, A5, 0.75, 0.065);
                Strike(buffer, 0.08, D6, 1.0, 0.09);
            }
            return Finish(buffer, 0.42, 6000);
        }

        private static void Strike(double[] buffer, double at, double pitch, double loud, double decay)
        {
            double[] ratios = { 1, 2.76, 5.4 }, gains = { 1, 0.22, 0.07 }, fades = { 1, 1.9, 3.2 };
            int start = Samples(at);
            Random random = new Random((int)pitch * 7 + start);
            for (int i = start; i < buffer.Length; i++)
            {
                double t = (double)(i - start) / Rate;
                double attack = Math.Min(1, t / 0.0015);
                double sum = 0;
                for (int p = 0; p < ratios.Length; p++) sum += gains[p] * Math.Exp(-t * fades[p] / decay) * Math.Sin(2 * Math.PI * pitch * ratios[p] * t);
                if (t < 0.004) sum += 0.12 * (1 - t / 0.004) * (random.NextDouble() * 2 - 1);
                buffer[i] += loud * attack * sum;
            }
        }

        private static float[] MakePulse(Chime chime)
        {
            double[] buffer = new double[Samples(0.36)];
            if (chime == Chime.Sent) Synth(buffer, 0, D4, A4, 0.05, 1.0, 0.11);
            else if (chime == Chime.Heard)
            {
                Synth(buffer, 0, A4, A4, 0, 0.8, 0.07);
                Synth(buffer, 0.09, D4, D4, 0, 1.0, 0.1);
            }
            else
            {
                Synth(buffer, 0, D4, D4, 0, 0.6, 0.06);
                Synth(buffer, 0.07, Fs4, Fs4, 0, 0.75, 0.06);
                Synth(buffer, 0.14, A4, A4, 0, 1.0, 0.09);
            }
            return Finish(buffer, 0.5, 3200);
        }

        private static void Synth(double[] buffer, double at, double from, double to, double glide, double loud, double decay)
        {
            int start = Samples(at);
            double phaseA = 0, phaseB = 0;
            for (int i = start; i < buffer.Length; i++)
            {
                double t = (double)(i - start) / Rate;
                double along = glide <= 0 ? 1 : Math.Min(1, t / glide);
                double pitch = from * Math.Pow(to / from, 1 - Math.Pow(1 - along, 2));
                phaseA += 2 * Math.PI * pitch * 1.0008 / Rate;
                phaseB += 2 * Math.PI * pitch * 0.9992 / Rate;
                double attack = Math.Min(1, t / 0.004);
                double body = Math.Exp(-t / decay), bright = Math.Exp(-t / (decay * 0.35));
                double wave = 0;
                foreach (double phase in new[] { phaseA, phaseB })
                    wave += Math.Sin(phase) + bright * (0.35 * Math.Sin(2 * phase) + 0.15 * Math.Sin(3 * phase) + 0.06 * Math.Sin(4 * phase));
                buffer[i] += loud * attack * body * wave * 0.5;
            }
        }

        private static float[] MakeAir(Chime chime)
        {
            double[] buffer = new double[Samples(0.36)];
            if (chime == Chime.Sent) Chirp(buffer, 0, 1500, 2300, 0.07, 1.0, 0.05);
            else if (chime == Chime.Heard)
            {
                Chirp(buffer, 0, 2300, 1750, 0.05, 0.8, 0.035);
                Chirp(buffer, 0.085, 2000, 1500, 0.06, 1.0, 0.05);
            }
            else Chirp(buffer, 0, 1200, 2000, 0.16, 1.0, 0.08);
            return Finish(buffer, 0.36, 8000);
        }

        private static void Chirp(double[] buffer, double at, double from, double to, double sweep, double loud, double decay)
        {
            int start = Samples(at);
            Random random = new Random((int)from + start);
            double phase = 0, low = 0, band = 0;
            for (int i = start; i < buffer.Length; i++)
            {
                double t = (double)(i - start) / Rate;
                double along = Math.Min(1, t / sweep);
                double pitch = from * Math.Pow(to / from, along);
                phase += 2 * Math.PI * pitch / Rate;
                double envelope = Math.Min(1, t / 0.006) * (t < sweep ? 1 : Math.Exp(-(t - sweep) / decay));
                double f = 2 * Math.Sin(Math.PI * Math.Min(pitch * 1.1, Rate / 6.0) / Rate);
                double noise = random.NextDouble() * 2 - 1;
                low += f * band;
                double high = noise - low - 0.9 * band;
                band += f * high;
                buffer[i] += loud * envelope * (0.55 * Math.Sin(phase) + 0.18 * Math.Sin(2 * phase) + 0.5 * band);
            }
        }

        private static float[] MakeShard(Chime chime)
        {
            double[] buffer = new double[Samples(0.36)];
            if (chime == Chime.Sent)
            {
                double[] grain = new double[Samples(0.02)];
                Strike(grain, 0, A5, 1.0, 0.05);
                Repeat(buffer, grain, 0, 0.026, 3, 0.75);
                Snapped(buffer, 0.085, D6, 1.0, 0.09, 0.03, 0.014, 2);
            }
            else if (chime == Chime.Heard)
            {
                Strike(buffer, 0, D6, 0.75, 0.05);
                Strike(buffer, 0.06, A5, 1.0, 0.11);
                Crushed(buffer, 0.11, 0.3, 2, 14, 5);
            }
            else
            {
                double[] note = new double[Samples(0.016)];
                double[] pitches = { Fs5, A5, D6 };
                for (int i = 0; i < 3; i++)
                {
                    Array.Clear(note, 0, note.Length);
                    Strike(note, 0, pitches[i], 1.0, 0.04);
                    Repeat(buffer, note, i * 0.055, 0.02, 2, 0.6 + i * 0.15);
                }
                Strike(buffer, 0.15, D6, 0.9, 0.08);
            }
            return Finish(buffer, 0.42, 7000);
        }

        private static float[] MakeBurst(Chime chime)
        {
            double[] buffer = new double[Samples(0.34)];
            if (chime == Chime.Sent)
            {
                Stepped(buffer, 0, new double[] { 1175, 1319, 1480, 1760, 1976, 2349 }, 0.011, 0.6, 1.0, 0.012);
                double[] tail = new double[Samples(0.03)];
                Stepped(tail, 0, new double[] { 2349 }, 0.012, 1.0, 1.0, 0.004);
                Repeat(buffer, tail, 0.08, 0.03, 3, 0.5);
            }
            else if (chime == Chime.Heard)
            {
                Blip(buffer, 0, 0.035, 2349, 1760, 0.9, 1.0, 0.03);
                double[] blip = new double[Samples(0.034)];
                Blip(blip, 0, 0.03, 1760, 1175, 1.0, 1.0, 0.004);
                Repeat(buffer, blip, 0.06, 0.034, 3, 0.5);
            }
            else
            {
                double[] hops = { 1480, 1976, 1480, 1976, 1480, 1976, 1480, 1976, 2349 };
                Hopping(buffer, 0, hops, 0.024, 0.0016, 0.55, 1.0, 0.05);
            }
            Crushed(buffer, 0, 0.34, 2, 2, 7);
            return Finish(buffer, chime == Chime.Listening ? 0.2 : 0.27, 5500);
        }

        private static void Stepped(double[] buffer, double at, double[] pitches, double step, double loudFrom, double loudTo, double decay)
        {
            double[] lengths = new double[pitches.Length];
            for (int i = 0; i < lengths.Length; i++) lengths[i] = step;
            Hop(buffer, at, pitches, lengths, loudFrom, loudTo, decay);
        }

        private static void Hopping(double[] buffer, double at, double[] pitches, double step, double shrink, double loudFrom, double loudTo, double decay)
        {
            double[] lengths = new double[pitches.Length];
            for (int i = 0; i < lengths.Length; i++) lengths[i] = Math.Max(0.008, step - i * shrink);
            Hop(buffer, at, pitches, lengths, loudFrom, loudTo, decay);
        }

        private static void Hop(double[] buffer, double at, double[] pitches, double[] lengths, double loudFrom, double loudTo, double decay)
        {
            int start = Samples(at), edge = Samples(0.0015);
            double total = 0;
            foreach (double length in lengths) total += length;
            int body = Samples(total), tail = Samples(decay * 4);
            double phase = 0;
            int step = 0;
            double stepEnd = lengths[0];
            for (int i = 0; i < body + tail && start + i < buffer.Length; i++)
            {
                double t = (double)i / Rate;
                while (step < pitches.Length - 1 && t >= stepEnd) { step++; stepEnd += lengths[step]; }
                phase += 2 * Math.PI * pitches[step] / Rate;
                double loud = loudFrom + (loudTo - loudFrom) * Math.Min(1, t / total);
                double envelope = Math.Min(1, (double)(i + 1) / edge) * (i < body ? 1 : Math.Exp(-(t - total) / decay) * (1 - (double)(i - body) / tail));
                buffer[start + i] += loud * envelope * (Math.Sin(phase) + 0.15 * Math.Sin(3 * phase));
            }
        }

        private static float[] MakeCrush(Chime chime)
        {
            double[] buffer = new double[Samples(0.36)];
            if (chime == Chime.Sent)
            {
                Synth(buffer, 0, D4, A4, 0.09, 1.0, 0.12);
                Crushed(buffer, 0, 0.36, 6, 6, 5);
                Gate(buffer, 0.045, 0.012);
                Gate(buffer, 0.085, 0.01);
            }
            else if (chime == Chime.Heard)
            {
                Synth(buffer, 0, A4, A4, 0, 0.8, 0.06);
                Synth(buffer, 0.08, D4, D4, 0, 1.0, 0.12);
                Crushed(buffer, 0.08, 0.36, 3, 16, 4);
                Gate(buffer, 0.15, 0.012);
                Gate(buffer, 0.19, 0.016);
            }
            else
            {
                Synth(buffer, 0, D4, D4, 0, 0.7, 0.3);
                Synth(buffer, 0.12, A4, A4, 0, 0.8, 0.09);
                Crushed(buffer, 0, 0.12, 10, 3, 4);
                double[] gaps = { 0.016, 0.042, 0.064, 0.082, 0.096 };
                foreach (double gap in gaps) Gate(buffer, gap, 0.012 - gap * 0.06);
            }
            return Finish(buffer, 0.45, 3500);
        }

        private static void Repeat(double[] buffer, double[] grain, double at, double gap, int count, double fade)
        {
            int edge = Samples(0.0015);
            for (int copy = 0; copy < count; copy++)
            {
                int start = Samples(at + copy * gap);
                double loud = Math.Pow(fade, copy);
                for (int i = 0; i < grain.Length && start + i < buffer.Length; i++)
                {
                    double ramp = Math.Min(1, Math.Min((double)(i + 1) / edge, (double)(grain.Length - i) / edge));
                    buffer[start + i] += grain[i] * loud * ramp;
                }
            }
        }

        private static void Snapped(double[] buffer, double at, double pitch, double loud, double decay, double snapAt, double snapFor, double ratio)
        {
            int start = Samples(at);
            double phase = 0, upper = 0;
            for (int i = start; i < buffer.Length; i++)
            {
                double t = (double)(i - start) / Rate;
                double now = t >= snapAt && t < snapAt + snapFor ? pitch * ratio : pitch;
                phase += 2 * Math.PI * now / Rate;
                upper += 2 * Math.PI * now * 2.76 / Rate;
                double attack = Math.Min(1, t / 0.0015);
                buffer[i] += loud * attack * (Math.Exp(-t / decay) * Math.Sin(phase) + 0.2 * Math.Exp(-t * 1.9 / decay) * Math.Sin(upper));
            }
        }

        private static void Blip(double[] buffer, double at, double length, double from, double to, double loud, double depth, double decay)
        {
            int start = Samples(at), count = Samples(length), edge = Samples(0.0015), tail = Samples(decay * 4);
            double phase = 0, wobble = 0;
            for (int i = 0; i < count + tail && start + i < buffer.Length; i++)
            {
                double t = (double)i / Rate;
                double along = Math.Min(1, t / length);
                double pitch = from * Math.Pow(to / from, along);
                wobble += 2 * Math.PI * pitch * 0.5 / Rate;
                phase += 2 * Math.PI * pitch / Rate + depth * Math.Exp(-t / 0.01) * Math.Sin(wobble) * 0.2;
                double envelope = Math.Min(1, (double)(i + 1) / edge) * (i < count ? 1 : Math.Exp(-(t - length) / decay) * (1 - (double)(i - count) / tail));
                buffer[start + i] += loud * envelope * (Math.Sin(phase) + 0.25 * Math.Sin(3 * phase));
            }
        }

        private static void Crushed(double[] buffer, double from, double to, int holdFrom, int holdTo, int bits)
        {
            int start = Samples(from), end = Math.Min(buffer.Length, Samples(to));
            double most = 0;
            for (int i = start; i < end; i++) most = Math.Max(most, Math.Abs(buffer[i]));
            if (most <= 0 || end <= start) return;
            double steps = Math.Pow(2, bits - 1) / most, held = 0;
            int left = 0;
            for (int i = start; i < end; i++)
            {
                if (left-- <= 0)
                {
                    double along = (double)(i - start) / (end - start);
                    left = (int)Math.Round(holdFrom + (holdTo - holdFrom) * along) - 1;
                    held = Math.Round(buffer[i] * steps) / steps;
                }
                buffer[i] = held;
            }
        }

        private static void Gate(double[] buffer, double at, double length)
        {
            int start = Samples(at), count = Samples(Math.Max(0.002, length)), edge = Samples(0.001);
            for (int i = 0; i < count && start + i < buffer.Length; i++)
            {
                double depth = Math.Min(1, Math.Min((double)(i + 1) / edge, (double)(count - i) / edge));
                buffer[start + i] *= 1 - depth;
            }
        }

        private static float[] Finish(double[] buffer, double peak, double lowpass)
        {
            double smooth = 0, k = 1 - Math.Exp(-2 * Math.PI * lowpass / Rate), most = 0;
            for (int i = 0; i < buffer.Length; i++)
            {
                smooth += k * (buffer[i] - smooth);
                buffer[i] = smooth;
                most = Math.Max(most, Math.Abs(smooth));
            }
            int fade = Samples(0.02);
            float[] shape = new float[buffer.Length];
            for (int i = 0; i < buffer.Length; i++)
            {
                double tail = Math.Min(1, (double)(buffer.Length - 1 - i) / fade);
                shape[i] = (float)(most <= 0 ? 0 : buffer[i] / most * peak * tail);
            }
            return shape;
        }

        private static int Samples(double seconds) { return (int)Math.Round(seconds * Rate); }

        internal static bool Mac { get { return OperatingSystem.IsMacOS(); } }

        internal static string PlayerProblem(bool mac) { return mac ? NoMacPlayer : NoPulsePlayer; }

        internal static (string Tool, List<string> Args) Command(int rate, bool mac, string wav, Func<string, bool> onPath)
        {
            if (mac) return ("afplay", new List<string> { wav });
            bool pulse = onPath != null && onPath("pacat"), wire = onPath != null && onPath("pw-cat");
            if (!pulse && !wire) return (null, null);
            if (pulse)
                return ("pacat", new List<string> { "--playback", "--raw", "--format=s16le", "--channels=1", "--rate=" + rate, "--client-name=Capri", "--stream-name=Capri chime", "--latency-msec=40", "--process-time-msec=20" });
            return ("pw-cat", new List<string> { "--playback", "--raw", "--format=s16", "--channels=1", "--rate=" + rate, "--latency=40ms", "-P", "{ application.name = \"Capri\" media.name = \"Capri chime\" }", "-" });
        }

        private static void Note(bool mac)
        {
            if (warned) return;
            warned = true;
            Brain.Log("chime", -1, PlayerProblem(mac));
        }

        private static bool PlaySound(short[] samples)
        {
            bool mac = Mac;
            string wav = mac ? Path.Combine(Path.GetTempPath(), "capri-chime-" + Guid.NewGuid().ToString("N") + ".wav") : null;
            var command = Command(Rate, mac, wav, SpeechAudio.OnPath);
            if (command.Tool == null) { Note(mac); return false; }
            int mine = Volatile.Read(ref playTicket);
            string tool = command.Tool;
            List<string> args = command.Args;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Process process = null;
                try
                {
                    lock (gate) { if (mine != ticket) return; }
                    if (mac) File.WriteAllBytes(wav, Voice.Wave(samples, Rate));
                    var info = SpeechAudio.Start(tool, args);
                    if (!mac) info.RedirectStandardInput = true;
                    process = Process.Start(info);
                    if (process == null) { Note(mac); return; }
                    lock (gate) { if (mine != ticket) { try { process.Kill(true); } catch (Exception) { } return; } current = process; }
                    Task<string> errors = process.StandardError.ReadToEndAsync();
                    if (mac) { if (!process.WaitForExit(5000)) process.Kill(true); }
                    else
                    {
                        byte[] bytes = new byte[samples.Length * 2];
                        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
                        process.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
                        process.StandardInput.Close();
                        if (!process.WaitForExit(5000)) process.Kill(true);
                        if (process.ExitCode != 0) Brain.Log("chime", -1, "Audio playback failed: " + errors.GetAwaiter().GetResult());
                    }
                }
                catch (Exception error) { Brain.Log("chime", -1, error.Message); }
                finally
                {
                    lock (gate) { if (current == process) current = null; }
                    try { if (process != null) process.Dispose(); } catch (Exception) { }
                    if (wav != null) { try { File.Delete(wav); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
                }
            });
            return true;
        }

        internal static void Hush()
        {
            Process playing;
            lock (gate)
            {
                ticket++;
                playTicket = ticket;
                Interlocked.Exchange(ref leftUntil, 0);
                maskUntil = 0;
                playing = current;
                current = null;
            }
            try { if (playing != null && !playing.HasExited) playing.Kill(true); } catch (Exception) { }
        }

        internal static bool Same(short[] a, short[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static double Alike(short[] a, short[] b)
        {
            double energyA = 0, energyB = 0, best = 0;
            foreach (short sample in a) energyA += (double)sample * sample;
            foreach (short sample in b) energyB += (double)sample * sample;
            if (energyA <= 0 || energyB <= 0) return 0;
            int most = Rate / 100;
            for (int lag = -most; lag <= most; lag += 4)
            {
                double sum = 0;
                for (int i = Math.Max(0, -lag); i < a.Length && i + lag < b.Length; i++) sum += (double)a[i] * b[i + lag];
                best = Math.Max(best, Math.Abs(sum) / Math.Sqrt(energyA * energyB));
            }
            return best;
        }

        internal static void Checks(Pet pet, Mainframe frame, List<string> checks)
        {
            List<string> table = new List<string>();
            for (int set = 0; set < Sets.Length; set++)
                for (int kind = 0; kind < 3; kind++)
                {
                    short[] full = Render(set, (Chime)kind, 100), soft = Render(set, (Chime)kind, DefaultVolume);
                    int peak = 0, softPeak = 0;
                    foreach (short sample in full) peak = Math.Max(peak, Math.Abs((int)sample));
                    foreach (short sample in soft) softPeak = Math.Max(softPeak, Math.Abs((int)sample));
                    int ms = full.Length * 1000 / Rate;
                    string name = Sets[set] + " " + Names[kind];
                    if (ms > 400 || peak >= 32000 || peak < 4000 || full[0] != 0 || full[full.Length - 1] != 0 || soft[soft.Length - 1] != 0)
                        throw new Exception("The " + name + " chime is too long, clips, is silent or clicks at an end: " + ms + " ms, peak " + peak);
                    double ratio = (double)softPeak / peak;
                    if (Math.Abs(ratio - Gain(DefaultVolume)) > 0.01) throw new Exception("The " + name + " chime's volume curve is off: " + ratio);
                    table.Add(name + " " + ms + " ms, peak " + (20 * Math.Log10(peak / 32768.0)).ToString("0.0", CultureInfo.InvariantCulture) + " dBFS");
                }
            for (int set = 0; set < Sets.Length; set++)
            {
                double alike = Alike(Render(set, Chime.Sent, 100), Render(set, Chime.Heard, 100));
                if (alike > 0.8) throw new Exception("The " + Sets[set] + " set's sent and heard chimes sound too alike: " + alike);
            }
            checks.Add("PASS her chimes: all six sets made in code, no sound files, none longer than 400 ms, none clips or starts or ends off zero, 40% is the volume curve's 12 dB down, and each set's sent and heard chimes differ (" + string.Join("; ", table.ToArray()) + ")");

            Settings fresh = new Settings();
            if (fresh.NoChimes || fresh.NoChimeSent || fresh.NoChimeHeard || fresh.NoChimeListening || fresh.ChimeVolume != 0 || fresh.ChimeSet != null)
                throw new Exception("Her chimes didn't start on Glass at 40% in new settings.");
            DataContractJsonSerializer reader = new DataContractJsonSerializer(typeof(Settings));
            Func<string, Settings> older = delegate(string text) { return (Settings)reader.ReadObject(new MemoryStream(Encoding.UTF8.GetBytes(text))); };
            Settings legacy = older("{\"NoChimes\":true}");
            Pet.MigrateChimes(legacy);
            if (legacy.NoChimes || !legacy.NoChimeSent || !legacy.NoChimeHeard || !legacy.NoChimeListening)
                throw new Exception("Chimes off in older settings didn't become all three chimes off.");
            Settings recovered = Pet.Kept(new Settings(), Pet.Newest(new Settings { NoChimeHeard = true, ChimeVolume = 70, ChimeSet = "shard" }));
            if (!recovered.NoChimeHeard || recovered.ChimeVolume != 70 || recovered.ChimeSet != "shard")
                throw new Exception("Chimes didn't survive the older settings copy.");
            Settings kept = Pet.Kept(new Settings { ChimeVolume = 70, ChimeSet = "shard" }, Pet.Newest(new Settings { ChimeVolume = 20, ChimeSet = "glass" }));
            if (kept.ChimeVolume != 70 || kept.ChimeSet != "shard") throw new Exception("An older settings copy overwrote chimes that were already there.");
            string path = Path.Combine(Path.GetTempPath(), "capri-chime-settings-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                using (FileStream stream = File.Create(path)) reader.WriteObject(stream, new Settings { Sounds = 30, Typing = 2, ChimeVolume = 70, ChimeSet = "shard", NoChimeHeard = true });
                string notice;
                Settings saved = Pet.ReadSettings(path, out notice);
                if (notice != null || saved.Sounds != 30 || saved.Typing != 2 || saved.ChimeVolume != 70 || saved.ChimeSet != "shard" || !saved.NoChimeHeard)
                    throw new Exception("Her settings round-trip lost chimes or the sounds beside them: " + notice);
            }
            finally { try { File.Delete(path); } catch (Exception) { } }
            checks.Add("PASS chimes settings: new and older settings start on Glass at 40%, Chimes off in an older copy turns all three off, a newer copy keeps the chimes it saved, and the settings file round-trips the chimes beside Sounds and Typing sound");

            var pulse = Command(Rate, false, null, tool => true);
            var pipewire = Command(Rate, false, null, tool => tool == "pw-cat");
            var neither = Command(Rate, false, null, tool => false);
            var mac = Command(Rate, true, "/tmp/capri-chime-check.wav", tool => false);
            string pulseLine = pulse.Args == null ? "" : string.Join(" ", pulse.Args);
            string wireLine = pipewire.Args == null ? "" : string.Join(" ", pipewire.Args);
            if (pulse.Tool != "pacat" || pulseLine != "--playback --raw --format=s16le --channels=1 --rate=48000 --client-name=Capri --stream-name=Capri chime --latency-msec=40 --process-time-msec=20")
                throw new Exception("Her chimes picked the wrong PulseAudio player: " + pulse.Tool + " / " + pulseLine);
            if (pipewire.Tool != "pw-cat" || wireLine != "--playback --raw --format=s16 --channels=1 --rate=48000 --latency=40ms -P { application.name = \"Capri\" media.name = \"Capri chime\" } -")
                throw new Exception("Her chimes picked the wrong PipeWire player: " + pipewire.Tool + " / " + wireLine);
            if (neither.Tool != null) throw new Exception("Her chimes found a player that isn't there: " + neither.Tool);
            if (mac.Tool != "afplay" || mac.Args == null || mac.Args.Count != 1 || mac.Args[0] != "/tmp/capri-chime-check.wav")
                throw new Exception("Her Mac chimes picked the wrong player: " + mac.Tool);
            if (PlayerProblem(false) != "Her chimes need pacat or pw-cat. Install pulseaudio-utils or pipewire-bin." || PlayerProblem(true) != "Her chimes need afplay, which comes with macOS.")
                throw new Exception("Her chimes' missing-player message is wrong.");
            if (Mac != OperatingSystem.IsMacOS()) throw new Exception("Her chimes don't use afplay on this system: " + Mac);
            checks.Add("PASS chimes output: " + (OperatingSystem.IsMacOS() ? "she plays them through afplay on a temp wav" : "she plays them through pacat, or pw-cat on a PipeWire desktop without pulseaudio-utils") + ", and a computer with neither says exactly why instead of failing silently");

            Func<short[], bool> outWas = Out;
            Func<double> leftWas = Left;
            Func<long> clockWas = Clock;
            bool onWas = On, testingWas = Testing, testedWas = Tested;
            int volumeWas = Volume, setWas = Set;
            bool sentWas = Wanted(Chime.Sent), heardWas = Wanted(Chime.Heard), listeningWas = Wanted(Chime.Listening);
            bool noSentWas = pet.Saved.NoChimeSent, noHeardWas = pet.Saved.NoChimeHeard, noListeningWas = pet.Saved.NoChimeListening;
            int savedVolumeWas = pet.Saved.ChimeVolume;
            string savedSetWas = pet.Saved.ChimeSet;
            bool advancedWas = pet.Saved.AdvancedSettings;
            Func<Action, CancellationToken, Task<string>> listenWas = pet.Listen;
            bool silentWas = pet.Saved.Silent, nameWas = pet.Saved.ListenCapri;
            long now = 1000000;
            double ringing = 0;
            List<short[]> played = new List<short[]>();
            try
            {
                Testing = true;
                Tested = true;
                Clock = delegate { return now; };
                Left = delegate { return ringing; };
                Out = delegate(short[] sound) { played.Add(sound); return true; };
                Reset();
                pet.Saved.NoChimeSent = pet.Saved.NoChimeHeard = pet.Saved.NoChimeListening = false;
                pet.Saved.ChimeVolume = 0;
                pet.Saved.ChimeSet = null;
                pet.ShowChimes();
                frame.Sync();

                bool first = Play(Chime.Sent), twice = Play(Chime.Sent);
                now += 400;
                bool again = Play(Chime.Sent), other = Play(Chime.Heard);
                now += 100;
                bool crowded = Play(Chime.Listening);
                bool masking = Masking;
                now += played[played.Count - 1].Length * 1000L / Rate + Echo + 1;
                bool unmasked = !Masking;
                now += 2000;
                ringing = 0.2;
                bool stacked = Play(Chime.Listening);
                ringing = 0;
                bool free = Play(Chime.Listening);
                if (!first || twice || again || !other || crowded || !masking || !unmasked || stacked || !free || played.Count != 3)
                    throw new Exception("Her chimes stacked or repeated: first " + first + ", twice " + twice + ", again " + again + ", other " + other + ", crowded " + crowded + ", mask " + masking + "/" + unmasked + ", stacked " + stacked + ", free " + free);
                if (!Same(played[0], Render(Glass, Chime.Sent, 40)) || !Same(played[1], Render(Glass, Chime.Heard, 40)))
                    throw new Exception("A chime wasn't Glass at 40% out of the box.");
                checks.Add("PASS her chimes never stack or repeat fast: the same one waits 700 ms, any two 300 ms, one still ringing drops the next, and her ears' mask covers each chime and 700 ms after; Glass at 40% plays out of the box");

                now += 10000; played.Clear(); Reset();
                pet.Hear("stop");
                if (played.Count != 1 || !Same(played[0], Render(Glass, Chime.Sent, 40))) throw new Exception("A typed message didn't play Message sent once: " + played.Count);
                now += 10000; played.Clear(); Reset();
                pet.HearDropped("stop");
                if (played.Count != 0) throw new Exception("A dropped file played Message sent: " + played.Count);
                now += 10000; played.Clear(); Reset();
                pet.Listen = (decoding, stop) => Task.FromResult("stop");
                pet.Saved.ListenCapri = false;
                pet.Saved.Silent = true;
                pet.VoiceTurn();
                for (int i = 0; i < 500 && (pet.Listening || played.Count == 0); i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Thread.Sleep(2); }
                if (played.Count != 1 || !Same(played[0], Render(Glass, Chime.Heard, 40))) throw new Exception("A TALK line didn't play Heard you once at its end: " + played.Count);
                pet.Talk.Dismiss();
                now += 10000; played.Clear(); Reset();
                pet.NameLineAccepted("hey capri");
                if (played.Count != 1 || !Same(played[0], Render(Glass, Chime.Listening, 40))) throw new Exception("Listen for Capri hearing its name didn't play Listening once: " + played.Count);
                now += 10000; played.Clear(); Reset();
                pet.NameLineAccepted("stop");
                if (played.Count != 1 || !Same(played[0], Render(Glass, Chime.Heard, 40))) throw new Exception("A name-listening line didn't play Heard you once: " + played.Count);
                now += 10000; played.Clear(); Reset();
                pet.NameLineAccepted("hey capri stop");
                if (played.Count != 1 || !Same(played[0], Render(Glass, Chime.Heard, 40)))
                    throw new Exception("An accepted request line with her name didn't play Heard you once: " + played.Count);
                if (!NameListeningText.Named("hey capri") || NameListeningText.Named("stop")) throw new Exception("Her name check missed the name or found one that wasn't there.");
                checks.Add("PASS chimes play on their events: a typed message plays Message sent, a dropped file plays none, a TALK line plays Heard you as it ends, Listen for Capri hearing her name on its own plays Listening, and an accepted request line plays Heard you");

                now += 10000; played.Clear(); Reset(); ringing = 0;
                if (!Play(Chime.Listening)) throw new Exception("The listening chime didn't play for the mask.");
                MethodInfo stateMethod = typeof(Pet).GetMethod("NameState", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(bool?) }, null);
                if (stateMethod == null || BeforePlay == null) throw new Exception("Her name listening isn't wired to her chimes.");
                var masked = (NameListeningState)stateMethod.Invoke(pet, new object[] { (bool?)false });
                if (!masked.Speaking) throw new Exception("A chime didn't pause her name listening: " + masked);
                now += played[played.Count - 1].Length * 1000L / Rate + Echo + 1;
                var clear = (NameListeningState)stateMethod.Invoke(pet, new object[] { (bool?)false });
                if (Masking || clear.Speaking && !SpeechAudio.MicrophoneHeld) throw new Exception("A chime kept her name listening paused after its mask: " + clear);
                checks.Add("PASS chimes stay out of her ears: before a chime plays she revokes the name listener's in-flight transcript, her name listening reports Speaking for the chime and 700 ms after, then comes back, so the chime is never transcribed");

                string help = SelfKnow.Matching("how do I turn off the chimes?") ?? "";
                if (!help.Contains("Mainframe > Settings > VOICE") || !help.Contains("when a TALK line ends") || !help.Contains("when Listen for Capri accepts a request line") || !help.Contains("a line that is just her name") || !help.Contains("not when her microphone opens") || !help.Contains("settings tool doesn't change") || help.Contains("on a call") || help.Contains("Ask her"))
                    throw new Exception("Her chimes help is missing, wrong about which Listen for Capri lines play Heard you or Listening, or claims more than this build does: " + help);
                checks.Add("PASS chimes help: it names the rows, says Heard you plays when a TALK line ends and on an accepted Listen for Capri request line, says Listening plays on a line that is just her name rather than when the mic opens, and doesn't claim her settings tool changes them or that calls behave as on Windows");

                pet.SetAdvancedSettings(true);
                frame.ShowSettings(true);
                frame.Sync();
                foreach (string label in new[] { "Sound set", "Chime volume", "Message sent", "Heard you", "Listening" })
                    if (frame.Row(label) == null || !frame.Row(label).IsVisible) throw new Exception("SOUNDS lost its row: " + label);
                if (frame.Overlap() != null || frame.Outside() != null || frame.TextFits() != null)
                    throw new Exception("The chime rows overlap or run past Settings: " + (frame.Overlap() ?? frame.Outside() ?? frame.TextFits()));
                pet.SetChime(Chime.Sent, true); pet.SetChime(Chime.Heard, true); pet.SetChime(Chime.Listening, true);
                pet.SetChimeVolume(40); pet.SetChimeSet(Glass); frame.Sync();
                if (frame.ChimesStatus != "on | 40% | sent heard listening | enabled") throw new Exception("SOUNDS didn't show her chimes on at 40%: " + frame.ChimesStatus);
                frame.Change("Message sent", 0); frame.Change("Heard you", 0); frame.Change("Listening", 0);
                if (frame.ChimesStatus != "off | 40% |  | disabled" || pet.ChimesOn) throw new Exception("All three chimes off didn't silence them or grey the rows: " + frame.ChimesStatus);
                frame.Change("Heard you", 1);
                if (frame.ChimesStatus != "on | 40% | heard | enabled" || !pet.ChimeOn(Chime.Heard)) throw new Exception("Heard you's switch didn't work on its own: " + frame.ChimesStatus);
                frame.Change("Chime volume", 8);
                if (pet.ChimeVolume != 90 || pet.Saved.ChimeVolume != 90 || frame.Value("Chime volume") != "90%") throw new Exception("Chime volume didn't move or save: " + frame.Value("Chime volume"));
                for (int i = 0; i < Picked.Length; i++)
                {
                    now += 10000; played.Clear(); Reset();
                    frame.Change("Sound set", i);
                    if (pet.ChimeSet != Picked[i] || ((NeonPicker)frame.Row("Sound set")).Selected != i) throw new Exception("Sound set didn't pick " + Titles[Picked[i]]);
                    if (played.Count != 1 || !Same(played[0], Render(Picked[i], Chime.Heard, pet.ChimeVolume))) throw new Exception("Sound set's " + Titles[Picked[i]] + " preview didn't play once: " + played.Count);
                }
                string accessible = AutomationProperties.GetName(frame.Row("Message sent"));
                if (accessible != "Message sent. A soft rising tick when your typed message goes to her") throw new Exception("Message sent lost its Windows accessible name: " + accessible);
                if ((ToolTip.GetTip(frame.Row("Sound set")) as string) != "Which set of chimes she plays" || (ToolTip.GetTip(frame.Row("Chime volume")) as string) != "How loud her chimes are")
                    throw new Exception("Sound set or Chime volume lost its Windows hint.");
                if (AutomationProperties.GetName(frame.Row("Sound set")) != "Which set of chimes she plays"
                    || AutomationProperties.GetName(frame.Row("Chime volume")) != "How loud her chimes are")
                    throw new Exception("Chime controls changed their Windows accessible names.");
                pet.SetAdvancedSettings(false);
                frame.ShowSettings(true);
                frame.Sync();
                foreach (string label in new[] { "Sound set", "Chime volume", "Message sent", "Heard you", "Listening" })
                    if (!frame.Row(label).IsVisible) throw new Exception("SOUNDS lost its row in Simple Settings: " + label);
                if (frame.Overlap() != null || frame.Outside() != null || frame.TextFits() != null)
                    throw new Exception("The chime rows overlap or run past Simple Settings: " + (frame.Overlap() ?? frame.Outside() ?? frame.TextFits()));
                checks.Add("PASS SOUNDS in Mainframe: Sound set, Chime volume, Message sent, Heard you and Listening sit in the VOICE section with Windows' exact names, hints and accessible names; all three off silences and greys them, each switch works alone, volume runs 10% to 100%, and every set previews its heard chime");
            }
            finally
            {
                Out = outWas;
                Left = leftWas;
                Clock = clockWas;
                On = onWas;
                Testing = testingWas;
                Tested = testedWas;
                Volume = volumeWas;
                Set = setWas;
                Want(Chime.Sent, sentWas);
                Want(Chime.Heard, heardWas);
                Want(Chime.Listening, listeningWas);
                pet.Saved.NoChimeSent = noSentWas;
                pet.Saved.NoChimeHeard = noHeardWas;
                pet.Saved.NoChimeListening = noListeningWas;
                pet.Saved.ChimeVolume = savedVolumeWas;
                pet.Saved.ChimeSet = savedSetWas;
                pet.SetAdvancedSettings(advancedWas);
                pet.Listen = listenWas;
                pet.Saved.Silent = silentWas;
                pet.Saved.ListenCapri = nameWas;
                pet.ShowChimes();
                pet.Talk.Dismiss();
                frame.Sync();
            }
        }
    }
}

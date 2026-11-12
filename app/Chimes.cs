using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace CapriDesktop
{
    // Her little sounds that say she got you (Lachy, 12 Nov: "a nice sound to signify" when you finish speaking): your typed
    // message went, she heard you, and she's listening after Hey Capri. They play dozens of times a day, so each is short and
    // soft, two never stack, and the same one never repeats fast. Every one is made here from sine waves and a breath of noise,
    // so there's no sample anyone else owns.
    internal enum Chime { Sent, Heard, Listening }

    // Her ears through a chime. Its echo rises while the chime plays, a little late through Bluetooth, and only fades after,
    // so sound past that which is louder than the echo could still be, twice running, is someone talking over it. From then
    // until the mask ends everything counts as them. Until then what the mic hears is held, never thrown away.
    internal sealed class EchoGate
    {
        // Bluetooth plays up to about 400 ms late, and a mic chunk is 100 ms long.
        internal const int Late = 500;
        // The most the echo can be falls this much every 100 ms: about 3 dB, slower than any chime or a room's ring fades.
        internal const double Fall = 0.7;
        private int heard = -1, over;
        private double ceiling;
        private long last;
        private bool voiced;

        // False while what the mic hears could be her chime.
        internal bool Voice(double level, double wake)
        {
            int which, body;
            long since;
            if (!Chimes.Mask(out which, out since, out body)) return true;
            if (which != heard) { heard = which; ceiling = 0; over = 0; voiced = false; last = since; }
            if (voiced) return true;
            double fall = Math.Pow(Fall, Math.Max(0, since - last) / 100.0);
            last = since;
            if (since < body + Late) { ceiling = Math.Max(ceiling, level); over = 0; return false; }
            ceiling *= fall;
            if (level < wake || level <= ceiling) { over = 0; return false; }
            if (++over < 2) return false;
            voiced = true;
            return true;
        }
    }

    internal static class Chimes
    {
        internal const int Rate = 48000;
        // Three sets to pick from by ear. Glass ships; Pulse and Air are kept for the audition.
        internal static readonly string[] Sets = { "glass", "pulse", "air" };
        internal static readonly string[] Names = { "sent", "heard", "listening" };
        internal const int Glass = 0, Pulse = 1, Air = 2;
        internal const int DefaultSet = Glass;
        internal const int DefaultVolume = 40;
        internal static volatile bool On = true;
        internal static volatile int Volume = DefaultVolume;
        internal static volatile int Set = DefaultSet;
        private static readonly bool[] wanted = { true, true, true };
        // Where a chime goes: her own Plain speaker, which keeps out of her voice in their mic. A check swaps it for a list.
        internal static Action<short[]> Out;
        // Seconds her chime speaker still has to play, so a new chime never lands on one still ringing.
        internal static Func<double> Left;
        // In ms. A check runs its own clock so it can step past the gaps without waiting for them.
        internal static Func<long> Clock = delegate { return Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency; };
        // Any two chimes are this far apart at least, and the same one this far.
        internal const int Gap = 300;
        internal const int Again = 700;
        // Her ears take this long after a chime ends as the chime, on top of its own length, since speakers and Bluetooth play late.
        internal const int Echo = 700;
        private static readonly object gate = new object();
        private static readonly long[] lastOf = { long.MinValue / 4, long.MinValue / 4, long.MinValue / 4 };
        private static long last = long.MinValue / 4;
        private static long maskUntil = long.MinValue / 4;
        private static long maskFrom = long.MinValue / 4;
        private static int masks, maskBody;
        // What her ears hold of the mic through a chime is kept this long after its mask too, so a line that carries on past
        // the mask starts with everything they said under it.
        internal const int After = 300;
        private static readonly int[] bodies = new int[Sets.Length * 3];
        private static readonly float[][] made = new float[Sets.Length * 3][];
        internal static int Played;

        internal static bool Wanted(Chime chime) { return wanted[(int)chime]; }
        internal static void Want(Chime chime, bool on) { wanted[(int)chime] = on; }

        // True when it played. A chime that's off, too soon, or would land on one still ringing is dropped, never queued. A
        // preview, from the volume slider, plays with that chime's own switch off.
        internal static bool Play(Chime chime, bool preview = false)
        {
            Action<short[]> output = Out;
            int volume = Volume;
            if (output == null || !On || volume <= 0 || !wanted[(int)chime] && !preview) return false;
            short[] sound;
            lock (gate)
            {
                long now = Clock();
                if (now - last < Gap || now - lastOf[(int)chime] < Again) return false;
                Func<double> left = Left;
                if (left != null && left() > 0) return false;
                sound = Render(Set, chime, volume);
                last = lastOf[(int)chime] = now;
                maskUntil = now + sound.Length * 1000L / Rate + Echo;
                maskFrom = now;
                maskBody = Body(Set, chime);
                masks++;
                Played++;
            }
            output(sound);
            return true;
        }

        // While a chime plays and just after, a mic chunk that isn't part of a line already begun is her chime, not them.
        internal static bool Masking { get { lock (gate) return Clock() < maskUntil; } }

        // Her ears keep what they hear through a chime's mask and just after it.
        internal static bool Holding { get { lock (gate) return Clock() < maskUntil + After; } }

        // The chime masking her ears: which one it is, ms since it began and how long its body lasts. False when none is.
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

        // How far into a chime it's still within half its loudest, in ms. Past that it only fades.
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

        internal static void Reset()
        {
            lock (gate)
            {
                last = maskUntil = maskFrom = long.MinValue / 4;
                for (int i = 0; i < lastOf.Length; i++) lastOf[i] = long.MinValue / 4;
                Played = 0;
            }
        }

        // Each set's three made at start-up, so a chime only has to be scaled to their volume when it plays.
        internal static void Warm()
        {
            Stopwatch took = Stopwatch.StartNew();
            for (int set = 0; set < Sets.Length; set++)
                for (int chime = 0; chime < 3; chime++) Body(set, (Chime)chime);
            Warmed = took.ElapsedMilliseconds;
        }

        internal static long Warmed = -1;

        // Their volume is a percent, heard as a curve so the low end isn't all one loudness: 40% is about 12 dB under 100%.
        internal static double Gain(int volume) { return Math.Pow(Math.Max(0, Math.Min(100, volume)) / 100.0, 1.5); }

        internal static short[] Render(int set, Chime chime, int volume)
        {
            float[] shape = Made(set, chime);
            double gain = Gain(volume);
            short[] samples = new short[shape.Length];
            for (int i = 0; i < shape.Length; i++) samples[i] = (short)Math.Round(Math.Max(-1, Math.Min(1, shape[i] * gain)) * 32767);
            return samples;
        }

        private static float[] Made(int set, Chime chime)
        {
            set = Math.Max(0, Math.Min(Sets.Length - 1, set));
            int at = set * 3 + (int)chime;
            float[] shape = made[at];
            if (shape != null) return shape;
            shape = set == Pulse ? MakePulse(chime) : set == Air ? MakeAir(chime) : MakeGlass(chime);
            made[at] = shape;
            return shape;
        }

        // The notes are D major's pentatonic, so any two chimes heard close together agree with each other.
        private const double D5 = 587.33, Fs5 = 739.99, A5 = 880.0, B5 = 987.77, D6 = 1174.66;
        private const double A3 = 220.0, D4 = 293.66, Fs4 = 369.99, A4 = 440.0;

        // Glass: a small struck glass, its overtones off the harmonic series like a real one. Sent goes up, Heard comes down and
        // settles, Listening opens up in three.
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
                // The tap of the strike: 4 ms of noise, faint.
                if (t < 0.004) sum += 0.12 * (1 - t / 0.004) * (random.NextDouble() * 2 - 1);
                buffer[i] += loud * attack * sum;
            }
        }

        // Pulse: a soft low synth, two detuned voices with their brightness falling away faster than their body. The same shapes
        // as Glass: Sent glides up, Heard steps down, Listening rises in three.
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

        // Air: a breath with a little chirp in it, like a soft bird call through a filter. Sent rises, Heard is two falling
        // chirps, Listening is one slower rise.
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
                // The breath: noise through a state-variable band-pass that follows the chirp.
                double f = 2 * Math.Sin(Math.PI * Math.Min(pitch * 1.1, Rate / 6.0) / Rate);
                double noise = random.NextDouble() * 2 - 1;
                low += f * band;
                double high = noise - low - 0.9 * band;
                band += f * high;
                buffer[i] += loud * envelope * (0.55 * Math.Sin(phase) + 0.18 * Math.Sin(2 * phase) + 0.5 * band);
            }
        }

        // Every chime ends the same way: a gentle low-pass, peaks brought to the set's level, and a fade to true silence, since
        // stopping on a sample that isn't zero clicks.
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

        // A 16-bit mono WAV, for the audition files.
        internal static void Save(string path, short[] samples)
        {
            using (BinaryWriter writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
                writer.Write(36 + samples.Length * 2);
                writer.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(Rate);
                writer.Write(Rate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
                writer.Write(samples.Length * 2);
                foreach (short sample in samples) writer.Write(sample);
            }
        }
    }
}

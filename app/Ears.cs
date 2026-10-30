using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
#if !CROSS
using System.Speech.AudioFormat;
using System.Speech.Recognition;
#endif
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace CapriDesktop
{

#if !CROSS
    [StructLayout(LayoutKind.Sequential)]
    internal struct WaveFormat
    {
        internal short Tag, Channels;
        internal int SampleRate, BytesPerSecond;
        internal short Align, Bits, Extra;

        internal static WaveFormat Mono(int rate) { return new WaveFormat { Tag = 1, Channels = 1, SampleRate = rate, BytesPerSecond = rate * 2, Align = 2, Bits = 16 }; }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WaveHeader
    {
        internal IntPtr Data;
        internal int Length, Recorded;
        internal IntPtr User;
        internal int Flags, Loops;
        internal IntPtr Next, Reserved;
    }

    internal sealed class Mic
    {
        internal readonly string Id;
        internal readonly string Name;

        internal Mic(string id, string name)
        {
            Id = id;
            Name = name;
        }
    }

    internal sealed class Ears : IDisposable
    {
        internal const int Rate = 16000;
        internal const int Chunk = Rate / 10;
        private const int Buffers = 4;
        internal event Action<short[]> Heard;
        internal event Action<double> Level;
        internal volatile bool Muted;
        internal volatile string Chosen;
        private IntPtr device;
        private IntPtr[] headers;
        private Thread worker;
        private AutoResetEvent signal;
        private volatile bool running;

        [DllImport("winmm.dll")] private static extern int waveInOpen(out IntPtr device, int id, ref WaveFormat format, IntPtr callback, IntPtr instance, int flags);
        [DllImport("winmm.dll")] private static extern int waveInPrepareHeader(IntPtr device, IntPtr header, int size);
        [DllImport("winmm.dll")] private static extern int waveInUnprepareHeader(IntPtr device, IntPtr header, int size);
        [DllImport("winmm.dll")] private static extern int waveInAddBuffer(IntPtr device, IntPtr header, int size);
        [DllImport("winmm.dll")] private static extern int waveInStart(IntPtr device);
        [DllImport("winmm.dll")] private static extern int waveInReset(IntPtr device);
        [DllImport("winmm.dll")] private static extern int waveInClose(IntPtr device);
        [DllImport("winmm.dll")] private static extern int waveInGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] private static extern int waveInGetDevCaps(IntPtr id, ref MicCaps caps, int size);
        [DllImport("winmm.dll")] private static extern int waveInMessage(IntPtr device, int message, out int size, IntPtr unused);
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] private static extern int waveInMessage(IntPtr device, int message, StringBuilder text, IntPtr size);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MicCaps
        {
            internal short Maker, Product;
            internal int Driver;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string Name;
            internal int Formats;
            internal short Channels, Reserved;
        }

        internal bool Listening { get { return running; } }

        internal static List<Mic> List()
        {
            List<Mic> found = new List<Mic>();
            int count = waveInGetNumDevs();
            for (int i = 0; i < count; i++)
            {
                MicCaps caps = new MicCaps();
                if (waveInGetDevCaps(new IntPtr(i), ref caps, Marshal.SizeOf(typeof(MicCaps))) != 0) continue;
                string id = Endpoint(i);
                found.Add(new Mic(id ?? caps.Name, FullName(id) ?? caps.Name));
            }
            return found;
        }

        private static string Endpoint(int index)
        {
            int size;
            if (waveInMessage(new IntPtr(index), 0x0812, out size, IntPtr.Zero) != 0 || size <= 0) return null;
            StringBuilder text = new StringBuilder(size / 2 + 1);
            if (waveInMessage(new IntPtr(index), 0x0811, text, new IntPtr(size)) != 0) return null;
            return text.Length == 0 ? null : text.ToString();
        }

        private static string FullName(string id)
        {
            if (id == null) return null;
            int cut = id.LastIndexOf("}.{", StringComparison.Ordinal);
            if (cut < 0) return null;
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Capture\" + id.Substring(cut + 2) + @"\Properties"))
                {
                    if (key == null) return null;
                    string kind = key.GetValue("{a45c254e-df1c-4efd-8020-67d146a850e0},2") as string;
                    string name = key.GetValue("{b3f8fa53-0004-438e-9003-51a46e139bfc},6") as string;
                    if (string.IsNullOrEmpty(kind)) return string.IsNullOrEmpty(name) ? null : name;
                    return string.IsNullOrEmpty(name) ? kind : kind == "Microphone" ? name : kind + " (" + name + ")";
                }
            }
            catch (Exception) { return null; }
        }

        private static int Pick(string chosen)
        {
            if (string.IsNullOrEmpty(chosen)) return -1;
            List<Mic> mics = List();
            for (int i = 0; i < mics.Count; i++) if (mics[i].Id == chosen) return i;
            return -1;
        }

        internal void Start()
        {
            if (running) return;
            WaveFormat format = WaveFormat.Mono(Rate);
            signal = new AutoResetEvent(false);
            int size = Marshal.SizeOf(typeof(WaveHeader));
            int id = Pick(Chosen);
            IntPtr wake = signal.SafeWaitHandle.DangerousGetHandle();
            if (waveInOpen(out device, id, ref format, wake, IntPtr.Zero, 0x00050000) != 0 && (id == -1 || waveInOpen(out device, -1, ref format, wake, IntPtr.Zero, 0x00050000) != 0))
            {
                signal.Dispose();
                signal = null;
                throw new VoiceException("Can't open your mic. Pick another under VOICE.");
            }
            headers = new IntPtr[Buffers];
            for (int i = 0; i < Buffers; i++)
            {
                WaveHeader header = new WaveHeader { Data = Marshal.AllocHGlobal(Chunk * 2), Length = Chunk * 2 };
                headers[i] = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(header, headers[i], false);
                waveInPrepareHeader(device, headers[i], size);
                waveInAddBuffer(device, headers[i], size);
            }
            running = true;
            waveInStart(device);
            worker = new Thread(Pump);
            worker.IsBackground = true;
            worker.Name = "Capri ears";
            worker.Start();
        }

        internal void Stop()
        {
            if (!running) return;
            running = false;
            signal.Set();
            worker.Join(1000);
            waveInReset(device);
            int size = Marshal.SizeOf(typeof(WaveHeader));
            foreach (IntPtr pointer in headers)
            {
                waveInUnprepareHeader(device, pointer, size);
                WaveHeader header = (WaveHeader)Marshal.PtrToStructure(pointer, typeof(WaveHeader));
                Marshal.FreeHGlobal(header.Data);
                Marshal.FreeHGlobal(pointer);
            }
            waveInClose(device);
            device = IntPtr.Zero;
            headers = null;
            signal.Dispose();
            signal = null;
        }

        public void Dispose() { Stop(); }

        private void Pump()
        {
            int size = Marshal.SizeOf(typeof(WaveHeader));
            int flags = (int)Marshal.OffsetOf(typeof(WaveHeader), "Flags");
            int next = 0;
            try
            {
                while (running)
                {
                    signal.WaitOne(250);
                    while (running)
                    {
                        IntPtr pointer = headers[next];
                        WaveHeader header = (WaveHeader)Marshal.PtrToStructure(pointer, typeof(WaveHeader));
                        if ((header.Flags & 1) == 0) break;
                        short[] chunk = new short[header.Recorded / 2];
                        Marshal.Copy(header.Data, chunk, 0, chunk.Length);
                        Marshal.WriteInt32(pointer, flags, header.Flags & ~1);
                        waveInAddBuffer(device, pointer, size);
                        next = (next + 1) % Buffers;
                        try { Feed(chunk); }
                        catch (Exception) { }
                    }
                }
            }
            catch (Exception) { }
        }

        internal void Feed(short[] chunk)
        {
            Action<double> level = Level;
            if (level != null) level(Muted ? 0 : Speaker.Loudness(chunk, 0, chunk.Length));
            Action<short[]> heard = Heard;
            if (heard != null) heard(Muted ? new short[chunk.Length] : chunk);
        }
    }

    internal sealed class Speaker : IDisposable
    {
        private readonly bool silent;
        internal bool Plain;
        private readonly List<IntPtr> pending = new List<IntPtr>();
        private readonly List<int> shapes = new List<int>();
        private readonly List<short> leftover = new List<short>();
        private IntPtr device;
        private int rate;
        private double loudest = 0.05;
        private long began;
        private long until;

        [DllImport("winmm.dll")] private static extern int waveOutOpen(out IntPtr device, int id, ref WaveFormat format, IntPtr callback, IntPtr instance, int flags);
        [DllImport("winmm.dll")] private static extern int waveOutPrepareHeader(IntPtr device, IntPtr header, int size);
        [DllImport("winmm.dll")] private static extern int waveOutUnprepareHeader(IntPtr device, IntPtr header, int size);
        [DllImport("winmm.dll")] private static extern int waveOutWrite(IntPtr device, IntPtr header, int size);
        [DllImport("winmm.dll")] private static extern int waveOutReset(IntPtr device);
        [DllImport("winmm.dll")] private static extern int waveOutClose(IntPtr device);

        internal Speaker(bool quiet) { silent = quiet; }

        internal static double Loudness(short[] samples, int start, int count)
        {
            if (count <= 0) return 0;
            double sum = 0;
            for (int i = start; i < start + count; i++) sum += (double)samples[i] * samples[i];
            return Math.Sqrt(sum / count) / 32768.0;
        }

        internal void Open(int sampleRate)
        {
            if (sampleRate == rate && (silent || device != IntPtr.Zero)) return;
            Close();
            rate = sampleRate;
            if (silent) return;
            WaveFormat format = WaveFormat.Mono(rate);
            int failed = waveOutOpen(out device, -1, ref format, IntPtr.Zero, IntPtr.Zero, 0);
            if (failed != 0)
            {
                device = IntPtr.Zero;
                throw new VoiceException(Speakers(failed));
            }
        }

        internal static string Speakers(int failed)
        {
            if (failed == 2 || failed == 6) return "Can't open your speakers: Windows has no speakers or headphones on. Pick one in Settings > System > Sound.";
            if (failed == 4) return "Can't open your speakers: another app has them to itself. Close it, or untick exclusive mode in your speaker's Sound settings.";
            return "Can't open your speakers (Windows error " + failed + "). Check Settings > System > Sound, then try again.";
        }

        internal bool Busy { get { return Stopwatch.GetTimestamp() < Interlocked.Read(ref until) + Stopwatch.Frequency * 35 / 100; } }

        internal void Play(short[] samples)
        {
            if (rate == 0 || samples.Length == 0) return;
            Sweep();
            long now = Stopwatch.GetTimestamp();
            if (now >= Interlocked.Read(ref until)) { began = now; shapes.Clear(); leftover.Clear(); Interlocked.Exchange(ref until, now); }
            Interlocked.Add(ref until, samples.Length * Stopwatch.Frequency / rate);
            leftover.AddRange(samples);
            int step = Math.Max(1, rate / 12);
            short[] all = leftover.ToArray();
            int at = 0;
            for (; at + step <= all.Length; at += step)
            {
                double level = Loudness(all, at, step);
                loudest = Math.Max(loudest * 0.995, level);
                int index = shapes.Count;
                shapes.Add(level <= Math.Max(0.004, loudest * 0.08) ? -1 : level < loudest * 0.3 ? (index % 3 == 0 ? 0 : 1) : level < loudest * 0.55 ? (index % 2 == 1 ? 2 : 1) : level < loudest * 0.85 ? 3 : 4);
            }
            leftover.RemoveRange(0, at);
            if (silent || device == IntPtr.Zero) return;
            if (!Plain) samples = Voice.Louder(samples);
            int size = Marshal.SizeOf(typeof(WaveHeader));
            WaveHeader header = new WaveHeader { Data = Marshal.AllocHGlobal(samples.Length * 2), Length = samples.Length * 2 };
            Marshal.Copy(samples, 0, header.Data, samples.Length);
            IntPtr pointer = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(header, pointer, false);
            waveOutPrepareHeader(device, pointer, size);
            waveOutWrite(device, pointer, size);
            pending.Add(pointer);
        }

        internal int Shape()
        {
            long now = Stopwatch.GetTimestamp();
            if (now >= Interlocked.Read(ref until) || shapes.Count == 0) return -1;
            int index = (int)((now - began) * 12 / Stopwatch.Frequency);
            return index >= 0 && index < shapes.Count ? shapes[index] : -1;
        }

        internal void Clear()
        {
            if (device != IntPtr.Zero) waveOutReset(device);
            Sweep();
            shapes.Clear();
            leftover.Clear();
            Interlocked.Exchange(ref until, 0);
        }

        private void Sweep()
        {
            int size = Marshal.SizeOf(typeof(WaveHeader));
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                WaveHeader header = (WaveHeader)Marshal.PtrToStructure(pending[i], typeof(WaveHeader));
                if ((header.Flags & 1) == 0) continue;
                waveOutUnprepareHeader(device, pending[i], size);
                Marshal.FreeHGlobal(header.Data);
                Marshal.FreeHGlobal(pending[i]);
                pending.RemoveAt(i);
            }
        }

        internal void Close()
        {
            Clear();
            if (device != IntPtr.Zero) waveOutClose(device);
            device = IntPtr.Zero;
            rate = 0;
        }

        public void Dispose() { Close(); }
    }


#endif
    internal static class Hearing
    {
        internal const string NoRecognizer = "Windows has no offline speech recognizer for your language. Turn on Online speech recognition in Settings > Privacy & security > Speech.";
        internal static Func<short[], string> Run = Recognize;
        // Her own Parakeet ears while they suit her language; Windows' offline recognizer otherwise.
        internal static volatile LocalEars Own;
        private static readonly object gate = new object();
#if !CROSS
        private static SpeechRecognitionEngine engine;
#endif

        internal static string Text(short[] samples) { return Clean(Run(samples)); }

        internal static string Clean(string text)
        {
            string plain = Regex.Replace(text ?? "", @"\s+", " ").Trim();
            return Regex.IsMatch(plain, @"[\p{L}\p{N}]") ? plain : "";
        }

        internal static string Pick(IList<string> cultures, string wanted)
        {
            string language = wanted.Split('-')[0];
            foreach (string culture in cultures) if (string.Equals(culture, wanted, StringComparison.OrdinalIgnoreCase)) return culture;
            foreach (string culture in cultures) if (string.Equals(culture.Split('-')[0], language, StringComparison.OrdinalIgnoreCase)) return culture;
            foreach (string culture in cultures) if (string.Equals(culture, "en-US", StringComparison.OrdinalIgnoreCase)) return culture;
            return cultures.Count > 0 ? cultures[0] : null;
        }

#if !CROSS
        private static SpeechRecognitionEngine Open()
        {
            List<RecognizerInfo> installed = new List<RecognizerInfo>(SpeechRecognitionEngine.InstalledRecognizers());
            List<string> cultures = installed.ConvertAll(delegate(RecognizerInfo info) { return info.Culture.Name; });
            string chosen = Pick(cultures, CultureInfo.CurrentUICulture.Name);
            if (chosen == null) throw new IOException(NoRecognizer);
            SpeechRecognitionEngine made = new SpeechRecognitionEngine(installed[cultures.IndexOf(chosen)]);
            made.LoadGrammar(new DictationGrammar());
            return made;
        }


#endif
        private static string Recognize(short[] samples)
        {
            LocalEars own = Own;
            if (own != null && own.Ready) return own.Hear(samples);
#if CROSS
            throw new IOException("Set up her local ears under Settings > Voice.");
#else
            lock (gate)
            {
                if (engine == null) engine = Open();
                byte[] bytes = new byte[samples.Length * 2];
                Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
                StringBuilder words = new StringBuilder();
                ManualResetEvent done = new ManualResetEvent(false);
                EventHandler<SpeechRecognizedEventArgs> recognized = delegate(object sender, SpeechRecognizedEventArgs e) { lock (words) words.Append(e.Result.Text).Append(' '); };
                EventHandler<RecognizeCompletedEventArgs> completed = delegate { done.Set(); };
                engine.SpeechRecognized += recognized;
                engine.RecognizeCompleted += completed;
                try
                {
                    using (MemoryStream stream = new MemoryStream(bytes))
                    {
                        engine.SetInputToAudioStream(stream, new SpeechAudioFormatInfo(Ears.Rate, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
                        engine.RecognizeAsync(RecognizeMode.Multiple);
                        if (!done.WaitOne(20000))
                        {
                            engine.RecognizeAsyncCancel();
                            done.WaitOne(3000);
                            throw new IOException("Hearing that took too long. Try a shorter line.");
                        }
                    }
                }
                finally
                {
                    engine.SpeechRecognized -= recognized;
                    engine.RecognizeCompleted -= completed;
                    engine.SetInputToNull();
                }
                lock (words) return words.ToString();
            }
#endif
        }
    }

    // Her own ears: NVIDIA's Parakeet TDT 0.6B v2 (CC-BY-4.0, English), run by the sherpa-onnx her free voice ships.
    // They hear whichever mic is picked, offline, without Windows' online speech setting.
    internal sealed class LocalEars : IDisposable
    {
        internal const string Pack = "parakeet-tdt-0.6b-v2-int8";
        internal static string PackUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8.tar.bz2";
        internal static string PackSha = "157c157bc51155e03e37d2466522a3a737dd9c72bb25f36eb18912964161e1ad";
        internal static long PackSize = 482468385;
        private const string Inside = "sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8";
        internal static readonly string[] Needed = { "encoder.int8.onnx", "decoder.int8.onnx", "joiner.int8.onnx", "tokens.txt" };
        private readonly string folder;
        private readonly string engine;
        private readonly object gate = new object();
        private IntPtr recognizer;
        internal volatile bool Cancel;

        internal LocalEars(string data)
        {
            folder = Path.Combine(Path.Combine(data, "ears"), Pack);
            engine = LocalVoice.Folder(data);
        }

        internal bool Installed { get { return Complete(folder); } }

        // Ready to hear: the model is in, and so is the free voice that brings sherpa-onnx.
        internal bool Ready
        {
            get
            {
#if CROSS
                return IntPtr.Size == 8 && Installed && SpeechRuntime.Installed(engine);
#else
                return IntPtr.Size == 8 && Installed && File.Exists(Path.Combine(engine, "sherpa-onnx-c-api.dll")) && File.Exists(Path.Combine(engine, "onnxruntime.dll"));
#endif
            }
        }

        private static bool Complete(string where)
        {
            foreach (string name in Needed) if (!File.Exists(Path.Combine(where, name))) return false;
            return true;
        }

        internal void Install(Action<string, long, long> progress)
        {
            if (IntPtr.Size != 8) throw new IOException("Her ears need a 64-bit system.");
            string root = Path.GetDirectoryName(folder);
            Directory.CreateDirectory(root);
            long needed = PackSize * 3 + (64L << 20);
#if CROSS
            // Unix profiles may be on a separate mount from /. Query the actual destination.
            string drive = root;
#else
            string drive = Path.GetPathRoot(root);
#endif
            if (new DriveInfo(drive).AvailableFreeSpace < needed) throw new IOException("Her ears need " + Engine.Amount(needed) + " free on " + drive.TrimEnd('\\') + ".");
            string tarball = folder + ".tar.bz2";
            Engine.Fetch(PackUrl, tarball, PackSize, PackSha, "her ears", progress, delegate { return Cancel; });
            progress("Unpacking her ears", PackSize, PackSize);
            string staging = folder + ".new";
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);
            Untar(tarball, staging);
            string unpacked = Path.Combine(staging, Inside);
            if (!Complete(unpacked)) throw new IOException("The ears download is missing files.");
#if CROSS
            SpeechRuntime.WriteAsrNotice(unpacked);
#endif
            lock (gate)
            {
                Close();
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
                Directory.Move(unpacked, folder);
            }
            Directory.Delete(staging, true);
            File.Delete(tarball);
        }

        // Windows 10 and 11 ship bsdtar, which unpacks .tar.bz2. The checksum was already matched, so this is the known archive.
        private static void Untar(string tarball, string into)
        {
#if CROSS
            SpeechRuntime.Unpack(tarball, into);
#else
            string tar = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe");
            if (!File.Exists(tar)) throw new IOException("Windows is missing tar.exe, which unpacks her ears.");
            ProcessStartInfo start = new ProcessStartInfo(tar, "-xjf \"" + tarball + "\" -C \"" + into + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            using (Process unpacking = Process.Start(start))
            {
                string problem = unpacking.StandardError.ReadToEnd();
                unpacking.WaitForExit();
                if (unpacking.ExitCode != 0) throw new IOException("Couldn't unpack her ears: " + Voice.Short(problem.Trim()));
            }
#endif
        }

        internal void Warm() { lock (gate) Open(); }

        internal string Hear(short[] samples)
        {
            lock (gate)
            {
                Open();
                IntPtr stream = SherpaOnnxCreateOfflineStream(recognizer);
                if (stream == IntPtr.Zero) throw new IOException("Her ears glitched.");
                IntPtr result = IntPtr.Zero;
                try
                {
                    float[] wave = new float[samples.Length];
                    for (int i = 0; i < samples.Length; i++) wave[i] = samples[i] / 32768f;
                    SherpaOnnxAcceptWaveformOffline(stream, Ears.Rate, wave, wave.Length);
                    SherpaOnnxDecodeOfflineStream(recognizer, stream);
                    result = SherpaOnnxGetOfflineStreamResult(stream);
                    return result == IntPtr.Zero ? "" : Text(Marshal.ReadIntPtr(result));
                }
                finally
                {
                    if (result != IntPtr.Zero) SherpaOnnxDestroyOfflineRecognizerResult(result);
                    SherpaOnnxDestroyOfflineStream(stream);
                }
            }
        }

        private void Open()
        {
            if (recognizer != IntPtr.Zero) return;
            if (!Ready) throw new IOException("Her ears aren't in yet.");
            LocalVoice.Load(engine);
            List<IntPtr> held = new List<IntPtr>();
            Func<string, IntPtr> keep = delegate(string value) { IntPtr pointer = Utf8(value); held.Add(pointer); return pointer; };
            // SherpaOnnxOfflineRecognizerConfig from sherpa-onnx 1.13.8 on 64-bit Windows, written by offset. Every field left at
            // zero takes the library's default: 16 kHz features, greedy search, no language model.
            IntPtr config = Marshal.AllocHGlobal(ConfigBytes);
            try
            {
                for (int at = 0; at < ConfigBytes; at += 8) Marshal.WriteInt64(config, at, 0);
                Marshal.WriteIntPtr(config, 8, keep(Path.Combine(folder, "encoder.int8.onnx")));
                Marshal.WriteIntPtr(config, 16, keep(Path.Combine(folder, "decoder.int8.onnx")));
                Marshal.WriteIntPtr(config, 24, keep(Path.Combine(folder, "joiner.int8.onnx")));
                Marshal.WriteIntPtr(config, 104, keep(Path.Combine(folder, "tokens.txt")));
                Marshal.WriteInt32(config, 112, Math.Max(1, Math.Min(4, Environment.ProcessorCount / 2)));
                Marshal.WriteIntPtr(config, 120, keep("cpu"));
                Marshal.WriteIntPtr(config, 128, keep("nemo_transducer"));
                recognizer = SherpaOnnxCreateOfflineRecognizer(config);
            }
            finally
            {
                Marshal.FreeHGlobal(config);
                foreach (IntPtr pointer in held) Marshal.FreeHGlobal(pointer);
            }
            if (recognizer == IntPtr.Zero) throw new IOException("Her ears wouldn't start.");
        }

        // Far more than the real struct, which only grows at its end as sherpa-onnx adds models.
        private const int ConfigBytes = 4096;

        private void Close()
        {
            if (recognizer == IntPtr.Zero) return;
            SherpaOnnxDestroyOfflineRecognizer(recognizer);
            recognizer = IntPtr.Zero;
        }

        // Unloads the model, about 650 MB of memory, once voice chat ends.
        public void Dispose() { lock (gate) Close(); }

        private static IntPtr Utf8(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? "");
            IntPtr pointer = Marshal.AllocHGlobal(bytes.Length + 1);
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
            Marshal.WriteByte(pointer, bytes.Length, 0);
            return pointer;
        }

        private static string Text(IntPtr pointer)
        {
            if (pointer == IntPtr.Zero) return "";
            int length = 0;
            while (Marshal.ReadByte(pointer, length) != 0) length++;
            byte[] bytes = new byte[length];
            Marshal.Copy(pointer, bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }

        [DllImport("sherpa-onnx-c-api.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr SherpaOnnxCreateOfflineRecognizer(IntPtr config);
        [DllImport("sherpa-onnx-c-api.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void SherpaOnnxDestroyOfflineRecognizer(IntPtr recognizer);
        [DllImport("sherpa-onnx-c-api.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr SherpaOnnxCreateOfflineStream(IntPtr recognizer);
        [DllImport("sherpa-onnx-c-api.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void SherpaOnnxDestroyOfflineStream(IntPtr stream);
        [DllImport("sherpa-onnx-c-api.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void SherpaOnnxAcceptWaveformOffline(IntPtr stream, int sampleRate, float[] samples, int count);
        [DllImport("sherpa-onnx-c-api.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void SherpaOnnxDecodeOfflineStream(IntPtr recognizer, IntPtr stream);
        [DllImport("sherpa-onnx-c-api.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr SherpaOnnxGetOfflineStreamResult(IntPtr stream);
        [DllImport("sherpa-onnx-c-api.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void SherpaOnnxDestroyOfflineRecognizerResult(IntPtr result);
    }

#if !CROSS
    internal static class Tempo
    {
        internal const int Rate = 100;
        internal const double Slowest = 70;
        internal const double Fastest = 140;
        internal const double Clear = 2.3;

        internal static double Fold(double bpm)
        {
            if (bpm <= 0) return 0;
            while (bpm >= Fastest) bpm /= 2;
            while (bpm < Slowest) bpm *= 2;
            return bpm;
        }

        internal static double Toward(double bpm, double near)
        {
            if (bpm <= 0 || near <= 0) return Fold(bpm);
            while (bpm >= near * 1.414) bpm /= 2;
            while (bpm < near / 1.414) bpm *= 2;
            return bpm;
        }

        internal static bool Find(float[] onsets, int count, out double bpm, out double lastBeat, out double strength)
        {
            bpm = 0;
            lastBeat = 0;
            strength = 0;
            if (count < Rate * 4) return false;
            double[] onset = new double[count];
            double sum = 0;
            for (int i = 0; i < count; i++) { onset[i] = onsets[i]; sum += onset[i]; }
            double spread = 0;
            for (int i = 0; i < count; i++) { onset[i] -= sum / count; spread += onset[i] * onset[i]; }
            spread = Math.Sqrt(spread / count);
            if (spread <= 1e-9) return false;
            double best = 0, bestScore = double.MinValue, phase;
            for (double period = Rate * 60.0 / 200; period <= Rate * 60.0 / 50; period += 0.5)
            {
                double octaves = Math.Log(Rate * 60.0 / period / 120, 2);
                double score = Pulses(onset, period, 1, 0.5, out phase) * Math.Exp(-0.5 * octaves * octaves);
                if (score > bestScore) { bestScore = score; best = period; }
            }
            double fine = best, fineScore = double.MinValue;
            for (double period = best - 0.5; period <= best + 0.5; period += 0.05)
            {
                double score = Pulses(onset, period, 1, 0.5, out phase);
                if (score > fineScore) { fineScore = score; fine = period; }
            }
            Pulses(onset, fine, 0, 0, out phase);
            bpm = Rate * 60.0 / fine;
            lastBeat = phase / Rate;
            strength = fineScore / spread;
            return strength >= Clear;
        }

        private static double Pulses(double[] onset, double period, double half, double quarter, out double phase)
        {
            int count = onset.Length;
            double top = double.MinValue;
            phase = 0;
            for (int k = 0; k < period; k++)
            {
                double sum = 0;
                int beats = 0;
                for (double at = count - 1 - k; at >= 0; at -= period)
                {
                    sum += At(onset, at) + half * At(onset, at - period / 2);
                    if (quarter > 0) sum += quarter * (At(onset, at - period / 4) + At(onset, at - period * 3 / 4));
                    beats++;
                }
                sum /= beats;
                if (sum > top) { top = sum; phase = k; }
            }
            return top;
        }

        private static double At(double[] onset, double x)
        {
            int i = (int)Math.Floor(x);
            if (i < 0 || i >= onset.Length - 1) return 0;
            double f = x - i;
            return onset[i] * (1 - f) + onset[i + 1] * f;
        }
    }

    internal sealed class Biquad
    {
        private readonly double b0, b1, b2, a1, a2;
        private double x1, x2, y1, y2;

        private Biquad(double b0, double b1, double b2, double a0, double a1, double a2)
        {
            this.b0 = b0 / a0;
            this.b1 = b1 / a0;
            this.b2 = b2 / a0;
            this.a1 = a1 / a0;
            this.a2 = a2 / a0;
        }

        internal static Biquad Low(double rate, double cut)
        {
            double w = 2 * Math.PI * cut / rate, c = Math.Cos(w), alpha = Math.Sin(w) / (2 * 0.7071);
            return new Biquad((1 - c) / 2, 1 - c, (1 - c) / 2, 1 + alpha, -2 * c, 1 - alpha);
        }

        internal static Biquad High(double rate, double cut)
        {
            double w = 2 * Math.PI * cut / rate, c = Math.Cos(w), alpha = Math.Sin(w) / (2 * 0.7071);
            return new Biquad((1 + c) / 2, -(1 + c), (1 + c) / 2, 1 + alpha, -2 * c, 1 - alpha);
        }

        internal double Run(double x)
        {
            double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1;
            x1 = x;
            y2 = y1;
            y1 = y;
            return y;
        }
    }

    internal sealed class Onset
    {
        private const double Floor = 1e-4;
        internal readonly int Block;
        private readonly Biquad bass, midLow, midHigh, treble;
        private readonly double[] energy = new double[3];
        private readonly double[] before = new double[3];
        internal int Filled;
        private bool primed;

        internal Onset(int rate)
        {
            Block = Math.Max(1, rate / Tempo.Rate);
            bass = Biquad.Low(rate, 150);
            midLow = Biquad.High(rate, 300);
            midHigh = Biquad.Low(rate, 3000);
            treble = Biquad.High(rate, 5000);
        }

        internal bool Add(double sample, out float strength)
        {
            double low = bass.Run(sample), mid = midHigh.Run(midLow.Run(sample)), high = treble.Run(sample);
            energy[0] += low * low;
            energy[1] += mid * mid;
            energy[2] += high * high;
            strength = 0;
            if (++Filled < Block) return false;
            double rise = 0;
            for (int i = 0; i < 3; i++)
            {
                double level = Math.Log(Floor + Math.Sqrt(energy[i] / Block));
                if (primed) rise += Math.Max(0, level - before[i]);
                before[i] = level;
                energy[i] = 0;
            }
            strength = (float)rise;
            Filled = 0;
            primed = true;
            return true;
        }
    }

    internal sealed class BeatLock
    {
        internal double Bpm;
        internal double BeatAt;
        private double pending;
        private int agree;
        private int misses;

        internal void Take(bool steady, double bpm, double beatAt)
        {
            if (!steady)
            {
                if (++misses >= 8) { Bpm = 0; pending = 0; agree = 0; }
                return;
            }
            misses = 0;
            double kept = Tempo.Toward(bpm, Bpm);
            if (Bpm > 0 && Near(kept, Bpm)) { Bpm = Bpm * 0.8 + kept * 0.2; BeatAt = beatAt; pending = 0; agree = 0; return; }
            double guess = Tempo.Toward(bpm, pending);
            if (pending > 0 && Near(guess, pending)) agree++;
            else { guess = Tempo.Fold(bpm); pending = guess; agree = 1; }
            if (agree < (Bpm > 0 ? 3 : 2)) return;
            Bpm = guess;
            BeatAt = beatAt;
            pending = 0;
            agree = 0;
        }

        private static bool Near(double a, double b) { return Math.Abs(a - b) / b < 0.04; }
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class SoundDevices { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISoundDevices
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, int states, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out ISoundDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISoundDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object found);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISoundClient
    {
        [PreserveSig] int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr session);
        [PreserveSig] int GetBufferSize(out int frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out int frames);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long normal, out long shortest);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr handle);
        [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISoundCapture
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out int frames, out int flags, out long position, out long time);
        [PreserveSig] int ReleaseBuffer(int frames);
        [PreserveSig] int GetNextPacketSize(out int frames);
    }

    internal sealed class SoundTap : IDisposable
    {
        internal readonly string Device;
        internal readonly int Rate;
        internal readonly int Channels;
        internal readonly int Bits;
        internal readonly bool Float;
        private readonly ISoundClient client;
        private readonly ISoundCapture capture;
        private float[] floats = new float[0];
        private short[] shorts = new short[0];
        private int[] ints = new int[0];

        private SoundTap(string device, ISoundClient client, ISoundCapture capture, int rate, int channels, int bits, bool isFloat)
        {
            Device = device;
            this.client = client;
            this.capture = capture;
            Rate = rate;
            Channels = channels;
            Bits = bits;
            Float = isFloat;
        }

        internal string Format { get { return Rate + " Hz, " + Channels + " channels, " + Bits + "-bit " + (Float ? "float" : "PCM"); } }

        internal static string DefaultDevice()
        {
            ISoundDevices devices = null;
            ISoundDevice device = null;
            try
            {
                devices = (ISoundDevices)new SoundDevices();
                string id;
                if (devices.GetDefaultAudioEndpoint(0, 1, out device) != 0 || device == null || device.GetId(out id) != 0) return null;
                return id;
            }
            catch (Exception) { return null; }
            finally
            {
                if (device != null) Marshal.ReleaseComObject(device);
                if (devices != null) Marshal.ReleaseComObject(devices);
            }
        }

        internal static SoundTap Open(bool start)
        {
            ISoundDevices devices = null;
            ISoundDevice device = null;
            ISoundClient client = null;
            IntPtr format = IntPtr.Zero;
            try
            {
                devices = (ISoundDevices)new SoundDevices();
                string id;
                if (devices.GetDefaultAudioEndpoint(0, 1, out device) != 0 || device == null || device.GetId(out id) != 0) return null;
                Guid clientId = typeof(ISoundClient).GUID;
                object found;
                if (device.Activate(ref clientId, 23, IntPtr.Zero, out found) != 0) return null;
                client = (ISoundClient)found;
                if (client.GetMixFormat(out format) != 0 || format == IntPtr.Zero) return null;
                int tag = Marshal.ReadInt16(format, 0) & 0xFFFF;
                int channels = Marshal.ReadInt16(format, 2);
                int rate = Marshal.ReadInt32(format, 4);
                int bits = Marshal.ReadInt16(format, 14);
                if (tag == 0xFFFE) tag = Marshal.ReadInt32(format, 24);
                bool isFloat = tag == 3 && bits == 32;
                if ((!isFloat && !(tag == 1 && (bits == 16 || bits == 32))) || channels < 1 || rate < 8000) return null;
                if (client.Initialize(0, 0x00020000, 1000000, 0, format, IntPtr.Zero) != 0) return null;
                Guid captureId = typeof(ISoundCapture).GUID;
                object service;
                if (client.GetService(ref captureId, out service) != 0) return null;
                if (start && client.Start() != 0) { Marshal.ReleaseComObject(service); return null; }
                SoundTap tap = new SoundTap(id, client, (ISoundCapture)service, rate, channels, bits, isFloat);
                client = null;
                return tap;
            }
            catch (Exception) { return null; }
            finally
            {
                if (format != IntPtr.Zero) Marshal.FreeCoTaskMem(format);
                if (client != null) Marshal.ReleaseComObject(client);
                if (device != null) Marshal.ReleaseComObject(device);
                if (devices != null) Marshal.ReleaseComObject(devices);
            }
        }

        internal bool Read(Action<float> push, Onset onset, out long newest)
        {
            newest = 0;
            while (true)
            {
                int waiting;
                if (capture.GetNextPacketSize(out waiting) != 0) return false;
                if (waiting == 0) return true;
                IntPtr data;
                int frames, flags;
                long position, time;
                if (capture.GetBuffer(out data, out frames, out flags, out position, out time) != 0) return false;
                int count = frames * Channels;
                bool silent = (flags & 2) != 0 || data == IntPtr.Zero;
                if (!silent && Float) { if (floats.Length < count) floats = new float[count]; Marshal.Copy(data, floats, 0, count); }
                else if (!silent && Bits == 16) { if (shorts.Length < count) shorts = new short[count]; Marshal.Copy(data, shorts, 0, count); }
                else if (!silent) { if (ints.Length < count) ints = new int[count]; Marshal.Copy(data, ints, 0, count); }
                for (int frame = 0; frame < frames; frame++)
                {
                    double mix = 0;
                    if (!silent)
                        for (int i = frame * Channels; i < (frame + 1) * Channels; i++) mix += Float ? floats[i] : Bits == 16 ? shorts[i] / 32768.0 : ints[i] / 2147483648.0;
                    float strength;
                    if (onset.Add(mix / Channels, out strength)) push(strength);
                }
                capture.ReleaseBuffer(frames);
                if (time > 0) newest = time + (long)(frames * 1e7 / Rate);
            }
        }

        public void Dispose()
        {
            try { client.Stop(); } catch (Exception) { }
            try { Marshal.ReleaseComObject(capture); } catch (Exception) { }
            try { Marshal.ReleaseComObject(client); } catch (Exception) { }
        }
    }

    internal sealed class Groove
    {
        internal const double Latency = 0.03;
        private const double Step = 1.0 / Tempo.Rate;
        private const int Window = Tempo.Rate * 8;
        private readonly Func<double> now;
        private readonly object gate = new object();
        private BeatLock beat = new BeatLock();
        private volatile int playing;
        private int started;

        internal Groove(Func<double> now) { this.now = now; }

        internal void Want(bool on)
        {
            if (on == (playing != 0)) return;
            if (!on) { playing = 0; return; }
            int run = ++started;
            lock (gate) beat = new BeatLock();
            playing = run;
            Thread worker = new Thread(delegate() { Listen(run); });
            worker.IsBackground = true;
            worker.Name = "Capri beat";
            worker.Start();
        }

        internal bool Locked(out double bpm, out double beatAt)
        {
            lock (gate) { bpm = beat.Bpm; beatAt = beat.BeatAt; }
            return bpm > 0;
        }

        internal void Pretend(double bpm, double beatAt) { lock (gate) { beat = new BeatLock(); beat.Bpm = bpm; beat.BeatAt = beatAt; } }

        internal static string Probe()
        {
            string format = null;
            Thread check = new Thread(delegate()
            {
                SoundTap tap = SoundTap.Open(false);
                if (tap == null) return;
                format = tap.Format;
                tap.Dispose();
            });
            check.IsBackground = true;
            check.Start();
            check.Join(5000);
            return format;
        }

        private void Listen(int run)
        {
            SoundTap tap = null;
            float[] ring = new float[Window];
            float[] ordered = new float[Window];
            int head = 0, count = 0, pushed = 0;
            Onset onset = null;
            Action<float> push = delegate(float level)
            {
                ring[head] = level;
                head = (head + 1) % Window;
                if (count < Window) count++;
                pushed++;
            };
            try
            {
                double heardAt = now(), checkedAt = heardAt, openedAt = -10, estimateAt = heardAt + 1;
                while (playing == run)
                {
                    Thread.Sleep(10);
                    double at = now();
                    if (tap != null && at - checkedAt > 5)
                    {
                        checkedAt = at;
                        if (SoundTap.DefaultDevice() != tap.Device) { tap.Dispose(); tap = null; }
                    }
                    if (tap == null)
                    {
                        if (at - openedAt < 2) continue;
                        openedAt = at;
                        tap = SoundTap.Open(true);
                        count = 0;
                        heardAt = at;
                        if (tap == null) continue;
                        onset = new Onset(tap.Rate);
                    }
                    int before = pushed;
                    long newest;
                    if (!tap.Read(push, onset, out newest)) { tap.Dispose(); tap = null; continue; }
                    double clockAt = at;
                    if (newest > 0) clockAt = newest * 1e-7 + at - Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency - onset.Filled / (double)tap.Rate;
                    if (Math.Abs(clockAt - at) > 0.2) clockAt = at;
                    if (pushed != before) heardAt = at;
                    else for (; at - heardAt > 0.05; heardAt += Step) push(0);
                    if (at < estimateAt || count < Tempo.Rate * 6) continue;
                    estimateAt = at + 0.5;
                    for (int i = 0; i < count; i++) ordered[i] = ring[(head - count + i + Window) % Window];
                    double bpm, lastBeat, strength;
                    bool steady = Tempo.Find(ordered, count, out bpm, out lastBeat, out strength);
                    lock (gate) if (playing == run) beat.Take(steady, bpm, clockAt - lastBeat - Step / 2 + Latency);
                }
            }
            catch (Exception) { }
            finally { if (tap != null) tap.Dispose(); }
        }
    }
#endif
}

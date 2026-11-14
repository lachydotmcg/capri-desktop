using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace CapriDesktop
{
    internal sealed class Ears
    {
        private const int Rate = 16000;
        private const int Frame = Rate / 50;
        private const uint SpeechWindow = (1u << 15) - 1;
        private const uint CloseOnExec = 1;
        private const string Recorder = "parec";
        private const string Recognizer = "sherpa-onnx-offline";
        private const string MacRecorder = "capri-mic";
        private const string English = "parakeet-tdt-0.6b-v2-int8";
        private static readonly string[] European = { "bg", "hr", "cs", "da", "nl", "en", "et", "fi", "fr", "de", "el", "hu", "it", "lv", "lt", "mt", "pl", "pt", "ro", "sk", "sl", "es", "sv", "ru", "uk" };
        private static readonly string[] SenseVoice = { "zh", "en", "ja", "ko", "yue" };
        private readonly string folder;
        private readonly Func<string> chosen;

        internal Ears(string data, Func<string> chosen) { folder = Path.Combine(data, "ears"); this.chosen = chosen; }

        internal async Task<string> Listen(Action decoding, CancellationToken stop)
        {
            stop.ThrowIfCancellationRequested();
            bool mac = OperatingSystem.IsMacOS();
            string mic = mac ? Beside(MacRecorder) : Find(Recorder), engine = Find(Recognizer, folder);
            if (mic == null) throw new IOException(mac ? "My mic helper is missing from this build. Reinstall Capri so I can hear you." : "Install parec (PulseAudio tools) so I can hear your mic.");
            string place = mac ? folder : "Capri/ears under your config folder";
            if (engine == null) throw new IOException("Extract sherpa-onnx into " + place + ", or put sherpa-onnx-offline on PATH, so I can hear your words.");
            string language = Brain.LanguageCode(Brain.Language ?? "English");
            Hearing model = Pick(Models(folder), language, chosen == null ? null : chosen());
            if (model == null)
                throw new IOException(language == "en" ? "Put my English Parakeet model in " + (mac ? Path.Combine(folder, English) : "Capri/ears/" + English + " under your config folder") + "."
                    : "I don't have ears for " + (Brain.Language ?? "English") + " yet. Extract a sherpa-onnx model that hears it, like SenseVoice for Chinese, Japanese and Korean, into " + place + ".");

            short[] samples = await Record(mic, mac, stop).ConfigureAwait(false);
            if (samples.Length == 0) return "";
            stop.ThrowIfCancellationRequested();
            decoding();
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(stop);
            limit.CancelAfter(TimeSpan.FromSeconds(30));
            string spare = null;
            try
            {
                FileStream wave;
                string audio;
                // macOS has no memfd, and sherpa-onnx won't read a pipe, so her Mac ears write a file only you can read and
                // delete it once they've heard it.
                if (mac)
                {
                    spare = audio = Path.Combine(Path.GetTempPath(), "capri-voice-" + Guid.NewGuid().ToString("N") + ".wav");
                    wave = new FileStream(spare, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite });
                }
                else
                {
                    int fd = memfd_create("capri-voice", CloseOnExec);
                    if (fd < 0) throw new IOException();
                    wave = new FileStream(new SafeFileHandle(new IntPtr(fd), true), FileAccess.Write);
                    audio = "/proc/" + Environment.ProcessId + "/fd/" + fd;
                }
                using var held = wave;
                wave.Write(Voice.Wave(samples, Rate));
                wave.Flush();
                using Process run = Start(engine, Arguments(model, language, audio));
                using var kill = limit.Token.Register(() => Stop(run));
                Task<string> reply = run.StandardOutput.ReadToEndAsync(limit.Token);
                Task errors = run.StandardError.BaseStream.CopyToAsync(Stream.Null, limit.Token);
                try
                {
                    await run.WaitForExitAsync(limit.Token).ConfigureAwait(false);
                    await errors.ConfigureAwait(false);
                    string json = await reply.ConfigureAwait(false);
                    if (run.ExitCode != 0) throw new IOException();
                    using JsonDocument result = JsonDocument.Parse(json);
                    return result.RootElement.GetProperty("text").GetString();
                }
                finally { Stop(run); }
            }
            catch (Exception error) when (error is IOException || error is InvalidOperationException || error is Win32Exception
                || error is JsonException || error is System.Collections.Generic.KeyNotFoundException || error is OperationCanceledException)
            {
                stop.ThrowIfCancellationRequested();
                throw new IOException(limit.IsCancellationRequested ? "My offline ears took too long to answer." : "My offline ears couldn't read that. Check the sherpa-onnx and Parakeet setup.");
            }
            finally
            {
                if (spare != null)
                    try { File.Delete(spare); }
                    catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { }
            }
        }

        private static async Task<short[]> Record(string mic, bool mac, CancellationToken stop)
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(stop);
            limit.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                using Process run = mac ? Start(mic) : Start(mic, "--raw", "--format=s16le", "--rate=16000", "--channels=1", "--latency-msec=50", "--client-name=Capri");
                using var kill = limit.Token.Register(() => Stop(run));
                Task errors = run.StandardError.BaseStream.CopyToAsync(Stream.Null, limit.Token);
                try { return await Words(run.StandardOutput.BaseStream, limit.Token).ConfigureAwait(false); }
                finally
                {
                    Stop(run);
                    await errors.ConfigureAwait(false);
                }
            }
            catch (Exception error) when (error is IOException || error is Win32Exception || error is OperationCanceledException)
            {
                stop.ThrowIfCancellationRequested();
                throw new IOException(mac ? "I couldn't hear your default mic. Let me use it in System Settings > Privacy & Security > Microphone, then try TALK again."
                    : "I couldn't hear your default mic. Check your sound settings and try TALK again.");
            }
        }

        internal sealed class Hearing
        {
            internal string Name;
            internal string Tokens, Encoder, Decoder, Joiner, Voice;
            internal string[] Hears;
        }

        // Every model she can use is a folder of its own under her ears folder, put there by you, so she never downloads or ships one.
        // She tells them apart by their files: a transducer like Parakeet has an encoder, a decoder and a joiner, and SenseVoice
        // keeps one model next to its tokens. Its folder name says which languages it hears; a name she doesn't know hears any.
        internal static List<Hearing> Models(string ears)
        {
            var found = new List<Hearing>();
            string[] folders;
            try { folders = Directory.GetDirectories(ears); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { return found; }
            Array.Sort(folders, StringComparer.Ordinal);
            foreach (string place in folders)
            {
                string tokens = Part(place, "tokens", ".txt");
                if (tokens == null) continue;
                string name = Path.GetFileName(place), lower = name.ToLowerInvariant();
                var model = new Hearing { Name = name, Tokens = tokens };
                if (lower.Contains("sense-voice") || lower.Contains("sensevoice"))
                {
                    model.Voice = Part(place, "model", ".onnx");
                    model.Hears = SenseVoice;
                    if (model.Voice == null) continue;
                }
                else
                {
                    model.Encoder = Part(place, "encoder", ".onnx");
                    model.Decoder = Part(place, "decoder", ".onnx");
                    model.Joiner = Part(place, "joiner", ".onnx");
                    if (model.Encoder == null || model.Decoder == null || model.Joiner == null) continue;
                    if (lower.Contains("parakeet") && lower.Contains("v2")) model.Hears = new[] { "en" };
                    else if (lower.Contains("parakeet") && (lower.Contains("v3") || lower.Contains("ultra"))) model.Hears = European;
                }
                found.Add(model);
            }
            return found;
        }

        // The model that hears the fewest languages, her own language among them, knows it best, so English stays on Parakeet v2
        // even with SenseVoice beside it. A model she can't place is only a last resort.
        internal static Hearing Pick(List<Hearing> models, string language, string chosen = null)
        {
            // The model you picked under VOICE > Her ears hears every language, while its folder is still there.
            if (chosen != null) foreach (Hearing model in models) if (model.Name == chosen) return model;
            Hearing best = null, unknown = null;
            foreach (Hearing model in models)
            {
                if (model.Hears == null) { if (unknown == null) unknown = model; continue; }
                if (Array.IndexOf(model.Hears, language) < 0) continue;
                if (best == null || model.Hears.Length < best.Hears.Length) best = model;
            }
            return best ?? unknown;
        }

        internal static string[] Arguments(Hearing model, string language, string audio)
        {
            if (model.Voice != null)
                return new[] { "--num-threads=2", "--tokens=" + model.Tokens, "--sense-voice-model=" + model.Voice,
                    "--sense-voice-language=" + (Array.IndexOf(SenseVoice, language) >= 0 ? language : "auto"), "--sense-voice-use-itn=1", audio };
            return new[] { "--num-threads=2", "--tokens=" + model.Tokens, "--encoder=" + model.Encoder, "--decoder=" + model.Decoder, "--joiner=" + model.Joiner, audio };
        }

        // Her int8 file when the model ships one, since it's the one these packs are made for.
        private static string Part(string place, string start, string end)
        {
            string plain = null;
            try
            {
                foreach (string file in Directory.GetFiles(place))
                {
                    string name = Path.GetFileName(file);
                    if (!name.StartsWith(start, StringComparison.OrdinalIgnoreCase) && !name.Contains("-" + start, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!name.EndsWith(end, StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.Contains(".int8.", StringComparison.OrdinalIgnoreCase)) return file;
                    if (plain == null || name.Length < Path.GetFileName(plain).Length) plain = file;
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { }
            return plain;
        }

        internal static async Task<short[]> Words(Stream mic, CancellationToken stop)
        {
            short[] samples = new short[12 * Rate];
            byte[] frame = new byte[Frame * 2];
            int used = 0, quiet = 0;
            uint recentSound = 0;
            bool heard = false;
            while (used < samples.Length)
            {
                await mic.ReadExactlyAsync(frame, stop).ConfigureAwait(false);
                long energy = 0;
                for (int i = 0; i < Frame; i++)
                {
                    short sample = BinaryPrimitives.ReadInt16LittleEndian(frame.AsSpan(i * 2, 2));
                    samples[used++] = sample;
                    energy += sample * sample;
                }
                bool sound = energy >= (long)Frame * 250 * 250;
                quiet = sound ? 0 : quiet + 1;
                if (!heard)
                {
                    recentSound = ((recentSound << 1) | (sound ? 1u : 0u)) & SpeechWindow;
                    heard = BitOperations.PopCount(recentSound) >= 5;
                }
                if (!heard && used >= 4 * Rate) break;
                if (heard && quiet >= 40) break;
            }
            return heard ? samples.AsSpan(0, used).ToArray() : Array.Empty<short>();
        }

        // Opened from the app menu she doesn't get the PATH a shell's .bashrc sets, so she also looks in the bin folder of a build
        // extracted into her ears folder, then ~/.local/bin, /usr/local/bin and /usr/bin.
        private static string Find(string name, string ears = null)
        {
            var places = new List<string>((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator));
            try { if (ears != null && Directory.Exists(ears)) foreach (string build in Directory.GetDirectories(ears)) places.Add(Path.Combine(build, "bin")); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { }
            places.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin"));
            if (OperatingSystem.IsMacOS()) places.Add("/opt/homebrew/bin");
            places.Add("/usr/local/bin");
            places.Add("/usr/bin");
            foreach (string path in places)
            {
                if (!Path.IsPathFullyQualified(path)) continue;
                string file = Path.Combine(path, name);
                if (File.Exists(file)) return file;
            }
            return null;
        }

        private static string Beside(string name)
        {
            string file = Path.Combine(AppContext.BaseDirectory, name);
            return File.Exists(file) ? file : null;
        }

        private static Process Start(string file, params string[] args)
        {
            var start = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string arg in args) start.ArgumentList.Add(arg);
            return Process.Start(start);
        }

        private static void Stop(Process run)
        {
            try { if (!run.HasExited) run.Kill(); }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }
        }

        [DllImport("libc")] private static extern int memfd_create(string name, uint flags);
    }
}

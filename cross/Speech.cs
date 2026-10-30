using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace CapriDesktop
{
    internal static class SpeechSetup
    {
        internal static bool TryRun(string[] args, out int code)
        {
            code = 0;
            if (args.Length == 0 || args[0] != "--setup-speech") return false;
            if (args.Length < 2 || args.Length > 3 || (args.Length == 3 && args[2] != "--ears"))
            {
                Console.Error.WriteLine("Usage: Capri --setup-speech <profile-directory> [--ears]"); code = 2; return true;
            }
            string data = Path.GetFullPath(args[1]);
            using (var voice = new LocalVoice(data))
            using (var ears = new LocalEars(data))
            {
                ConsoleCancelEventHandler cancel = (sender, e) => { e.Cancel = true; voice.Cancel = ears.Cancel = true; };
                Console.CancelKeyPress += cancel;
                try
                {
                    SpeechRuntime.RequireLinux();
                    Action<string, long, long> progress = (text, done, total) => Console.WriteLine(text + ": " + done + "/" + total);
                    if (!voice.Installed) voice.Install(progress);
                    SpeechRuntime.Load(LocalVoice.Folder(data));
                    if (args.Length == 3 && !ears.Installed) ears.Install(progress);
                    Console.WriteLine("Local speech is installed in " + data + ". No microphone or playback was opened.");
                }
                catch (Exception error) { Console.Error.WriteLine(error.Message); code = 1; }
                finally { Console.CancelKeyPress -= cancel; }
            }
            return true;
        }
    }

    internal sealed class SpeechWork : IDisposable
    {
        [ThreadStatic] private static CancellationToken token;
        [ThreadStatic] private static List<CancellationTokenRegistration> requests;
        internal SpeechWork(CancellationToken cancellation) { token = cancellation; requests = new List<CancellationTokenRegistration>(); }
        internal static void Check() { token.ThrowIfCancellationRequested(); }
        internal static LocalVoice.AudioCallback Progress { get { CancellationToken current = token; return (samples, count) => current.IsCancellationRequested ? 0 : 1; } }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int ProgressWithArg(IntPtr samples, int count, float progress, IntPtr arg);
        internal static ProgressWithArg ConfigProgress { get { CancellationToken current = token; return (samples, count, progress, arg) => current.IsCancellationRequested ? 0 : 1; } }
        internal static void Track(System.Net.HttpWebRequest request) { if (requests != null) requests.Add(token.Register(request.Abort)); }
        public void Dispose() { foreach (var request in requests) request.Dispose(); requests = null; token = default; }
    }

    // This bundle's C ABI is pinned to the shared Windows speech bindings, not the host's search path.
    internal static class SpeechRuntime
    {
        internal const string Version = "1.13.8";
        internal const string Bundle = "sherpa-onnx-v1.13.8-linux-x64-shared-lib";
        internal const string Url = "https://github.com/k2-fsa/sherpa-onnx/releases/download/v1.13.8/" + Bundle + ".tar.bz2";
        internal const string Sha = "3892d184be41027e18165e67f549cd4e4cdd8dcd73ac5579e97afd55e14e30b6";
        internal const long Size = 9816899;
        private static IntPtr sherpa;
        internal static readonly string[] Libraries = { "libonnxruntime.so", "libsherpa-onnx-c-api.so" };
        // Digests of the library bytes in the release archive, filled from the verified release receipt.
        internal static readonly string[] Digests = { "4b3607aebd1784b26b6f9b20e4bd974c7ab8287043e4d095cb7d2cb40b5e566e", "b8351ca1632571ac108adbb317bcc4bf7cfe84b72690e3017316b0da3e1e344f" };
        internal static string Folder(string pack) { return Path.Combine(pack, Bundle); }
        internal static bool Installed(string pack) { return Libraries.All(name => File.Exists(Path.Combine(Folder(pack), name))); }
        internal static void RequireLinux()
        {
            if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
                throw new VoiceException("Local voice currently needs Linux x64 on this build.");
        }
        internal static void Install(string pack, Action<string, long, long> progress, Func<bool> stopped)
        {
            RequireLinux();
            string archive = pack + ".runtime.tar.bz2", staging = pack + ".runtime.new";
            Engine.Fetch(Url, archive, Size, Sha, "her speech runtime", progress, stopped);
            Directory.CreateDirectory(staging);
            try
            {
                Unpack(archive, staging);
                string source = Path.Combine(staging, Bundle, "lib"), target = Folder(pack);
                Directory.CreateDirectory(target);
                for (int i = 0; i < Libraries.Length; i++)
                {
                    Verify(Path.Combine(source, Libraries[i]), Digests[i]);
                    File.Copy(Path.Combine(source, Libraries[i]), Path.Combine(target, Libraries[i]), true);
                }
            }
            finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
            File.Delete(archive);
        }
        internal static void WriteAsrNotice(string folder)
        {
            foreach (string name in new[] { "parakeet-NOTICE.txt", "CC-BY-4.0.txt" })
            using (Stream source = typeof(SpeechRuntime).Assembly.GetManifestResourceStream("Capri.Speech." + name))
            using (Stream output = File.Create(Path.Combine(folder, name))) source.CopyTo(output);
        }
        internal static void Verify(string path, string digest)
        {
            using (FileStream stream = File.OpenRead(path))
                if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(digest, StringComparison.OrdinalIgnoreCase))
                    throw new VoiceException("The speech runtime checksum changed: " + Path.GetFileName(path) + ". Set up her free voice again.");
        }
        internal static void Load(string pack)
        {
            RequireLinux();
            if (sherpa != IntPtr.Zero) return;
            for (int i = 0; i < Libraries.Length; i++) Verify(Path.Combine(Folder(pack), Libraries[i]), Digests[i]);
            NativeLibrary.Load(Path.GetFullPath(Path.Combine(Folder(pack), Libraries[0])));
            sherpa = NativeLibrary.Load(Path.GetFullPath(Path.Combine(Folder(pack), Libraries[1])));
            NativeLibrary.SetDllImportResolver(typeof(LocalVoice).Assembly, (name, assembly, path) => name == "sherpa-onnx-c-api.dll" ? sherpa : IntPtr.Zero);
            var version = Marshal.GetDelegateForFunctionPointer<VersionText>(NativeLibrary.GetExport(sherpa, "SherpaOnnxGetVersionStr"));
            if (Marshal.PtrToStringUTF8(version()) != Version) throw new VoiceException("The speech runtime version does not match Capri's bindings.");
        }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr VersionText();

        // Inspect untrusted model archives before extraction: only regular files and directories
        // confined to the staging directory are allowed.
        internal static void Unpack(string archive, string into)
        {
            string tool = File.Exists("/usr/bin/bsdtar") ? "/usr/bin/bsdtar" : "/usr/bin/tar";
            string listing = SpeechAudio.Run(tool, new[] { "-tf", archive });
            foreach (string name in listing.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                if (Path.IsPathRooted(name) || name.Split('/').Contains("..")) throw new VoiceException("That model archive contains an unsafe path.");
            string detailed = SpeechAudio.Run(tool, new[] { "-tvf", archive });
            foreach (string line in detailed.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                if (line[0] != '-' && line[0] != 'd') throw new VoiceException("That model archive contains a link or special file.");
            SpeechAudio.Run(tool, new[] { "-xf", archive, "-C", into });
        }
    }

    internal static class SpeechAudio
    {
        private static readonly object gate = new object();
        private static Process playing;
        private static int generation;
        private static long microphoneAfter;
        internal static event Action<string> Failed;
        internal static bool Busy { get { lock (gate) return playing != null; } }
        internal static bool MicrophoneHeld { get { lock (gate) return playing != null || Stopwatch.GetTimestamp() < microphoneAfter; } }
        internal static ProcessStartInfo Start(string tool, IEnumerable<string> args)
        {
            var info = new ProcessStartInfo(tool) { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true };
            foreach (string arg in args) info.ArgumentList.Add(arg);
            return info;
        }
        internal static string Run(string tool, IEnumerable<string> args)
        {
            var info = Start(tool, args); info.RedirectStandardOutput = true;
            using (var process = Process.Start(info))
            {
                Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(60000)) { process.Kill(true); throw new VoiceException(tool + " timed out."); }
                if (process.ExitCode != 0) throw new VoiceException(Voice.Short(error.Result));
                return output.Result;
            }
        }
        internal static byte[] Decode(Stream input)
        {
            var info = Start("ffmpeg", new[] { "-v", "error", "-i", "pipe:0", "-ac", "1", "-ar", "24000", "-f", "wav", "pipe:1" });
            info.RedirectStandardInput = true; info.RedirectStandardOutput = true;
            using (var process = Process.Start(info))
            using (var output = new MemoryStream())
            {
                var error = process.StandardError.ReadToEndAsync();
                var read = process.StandardOutput.BaseStream.CopyToAsync(output);
                var write = Task.Run(() => { input.CopyTo(process.StandardInput.BaseStream); process.StandardInput.Close(); });
                if (!process.WaitForExit(60000)) { process.Kill(true); throw new VoiceException("Reading that audio clip took too long."); }
                Task.WaitAll(read, write);
                if (process.ExitCode != 0) throw new VoiceException(Voice.Short(error.Result));
                return output.ToArray();
            }
        }
        internal static void Play(byte[] wav)
        {
            int expected;
            lock (gate) { Stop(); expected = generation; }
            int rate; short[] samples = Voice.Samples(wav, out rate);
            if (samples.Length == 0) throw new VoiceException("That voice returned no PCM audio.");
            byte[] bytes = new byte[samples.Length * 2]; Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
            var args = new List<string> { "--playback", "--raw", "--format=s16le", "--channels=1", "--rate=" + rate, "--client-name=Capri", "--stream-name=Capri voice", "--latency-msec=40", "--process-time-msec=20" };
            var info = Start("pacat", args); info.RedirectStandardInput = true;
            Process process; int ticket;
            lock (gate)
            {
                if (expected != generation) return;
                ticket = generation;
                try { process = Process.Start(info); playing = process; }
                catch (Exception error) { Failed?.Invoke("Audio playback failed: " + error.Message); return; }
            }
            Task.Run(async () =>
            {
                try
                {
                    var error = process.StandardError.ReadToEndAsync();
                    await process.StandardInput.BaseStream.WriteAsync(bytes); process.StandardInput.Close();
                    await process.WaitForExitAsync();
                    if (process.ExitCode != 0) throw new VoiceException(Voice.Short(await error));
                }
                catch (Exception error) { lock (gate) { if (ticket == generation) Failed?.Invoke("Audio playback failed: " + error.Message); } }
                finally { lock (gate) { if (playing == process) { playing = null; microphoneAfter = Stopwatch.GetTimestamp() + (long)(0.6 * Stopwatch.Frequency); } } process.Dispose(); }
            });
        }
        internal static void Stop()
        {
            lock (gate)
            {
                generation++; microphoneAfter = 0;
                if (playing == null) return;
                try { if (!playing.HasExited) playing.Kill(true); } catch (InvalidOperationException) { }
                playing = null;
            }
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace CapriDesktop
{
    internal static class SpeechAudio
    {
        private static readonly object gate = new object();
        private static Process playing;
        private static int generation, preparing = -1;
        private static long microphoneAfter;
        internal static event Action<string> Failed;
        // Subscribers revoke microphone authority immediately; no IPC/device wait belongs in this callback.
        internal static event Action BeforePlayback;
        internal static bool Busy { get { lock (gate) return preparing >= 0 || playing != null; } }
        internal static bool MicrophoneHeld { get { lock (gate) return preparing >= 0 || playing != null || Stopwatch.GetTimestamp() < microphoneAfter; } }
        private static void HoldTail() { microphoneAfter = Math.Max(microphoneAfter, Stopwatch.GetTimestamp() + (long)(0.6 * Stopwatch.Frequency)); }
        internal static ProcessStartInfo Start(string tool, IEnumerable<string> args)
        {
            var info = new ProcessStartInfo(tool) { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true };
            foreach (string arg in args) info.ArgumentList.Add(arg);
            return info;
        }
        // Raspberry Pi OS and other PipeWire desktops leave out pulseaudio-utils, so without pacat she plays through pw-cat.
        internal static (string Tool, List<string> Args) Command(int rate, Func<string, bool> onPath)
        {
            if (onPath("pacat") || !onPath("pw-cat"))
                return ("pacat", new List<string> { "--playback", "--raw", "--format=s16le", "--channels=1", "--rate=" + rate, "--client-name=Capri", "--stream-name=Capri voice", "--latency-msec=40", "--process-time-msec=20" });
            return ("pw-cat", new List<string> { "--playback", "--raw", "--format=s16", "--channels=1", "--rate=" + rate, "--latency=40ms", "-P", "{ application.name = \"Capri\" media.name = \"Capri voice\" }", "-" });
        }

        internal static bool OnPath(string tool)
        {
            foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':'))
            {
                string path = Path.Combine(folder, tool);
                try { if (folder.Length > 0 && File.Exists(path) && (File.GetUnixFileMode(path) & UnixFileMode.UserExecute) != 0) return true; }
                catch (Exception) { }
            }
            return false;
        }

        private static readonly ConcurrentDictionary<string, bool> found = new ConcurrentDictionary<string, bool>();
        private static bool Installed(string tool) { return found.GetOrAdd(tool, OnPath); }

        internal static void Checks(List<string> checks)
        {
            var pulse = Command(24000, tool => true);
            var pipewire = Command(24000, tool => tool == "pw-cat");
            var neither = Command(24000, tool => false);
            var pulseOnly = Command(22050, tool => tool == "pacat");
            if (pulse.Tool != "pacat" || neither.Tool != "pacat" || pulseOnly.Tool != "pacat" || pipewire.Tool != "pw-cat" || !pipewire.Args.Contains("--rate=24000") || pipewire.Args[pipewire.Args.Count - 1] != "-"
                || !pulseOnly.Args.Contains("--rate=22050") || !pulseOnly.Args.Contains("--format=s16le") || !pulseOnly.Args.Contains("--raw"))
                throw new Exception("Her voice picked the wrong audio player.");
            checks.Add("PASS voice output: she speaks through pacat, or through pw-cat on a PipeWire desktop without pulseaudio-utils, like Raspberry Pi OS");
        }

        internal static void Play(byte[] wav)
        {
            int expected;
            lock (gate) { Stop(); expected = generation; preparing = expected; }
            try
            {
                // Hold is reserved before the gate callback, including slow decoding/device startup.
                BeforePlayback?.Invoke();
                int rate; short[] samples = Voice.Samples(wav, out rate);
                if (samples.Length == 0) throw new VoiceException("That voice returned no PCM audio.");
                byte[] bytes = new byte[samples.Length * 2]; Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
                var command = Command(rate, Installed);
                var info = Start(command.Tool, command.Args); info.RedirectStandardInput = true;
                Process process; int ticket;
                lock (gate)
                {
                    if (expected != generation) return;
                    ticket = generation;
                    try { process = Process.Start(info); playing = process; preparing = -1; }
                    catch (Exception error) { Failed?.Invoke(Installed(command.Tool) ? "Audio playback failed: " + error.Message : "She can't speak without pacat or pw-cat. Install pulseaudio-utils or pipewire-bin."); return; }
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
                    finally { lock (gate) { if (playing == process) { playing = null; HoldTail(); } } process.Dispose(); }
                });
            }
            finally
            {
                lock (gate) if (preparing == expected) { preparing = -1; HoldTail(); }
            }
        }
        internal static void Stop()
        {
            lock (gate)
            {
                generation++;
                if (preparing >= 0 || playing != null) HoldTail();
                preparing = -1;
                if (playing == null) return;
                try { if (!playing.HasExited) playing.Kill(true); } catch (InvalidOperationException) { }
                playing = null;
            }
        }
    }
}

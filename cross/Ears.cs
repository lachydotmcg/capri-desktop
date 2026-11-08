using System;
using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
        private const uint CloseOnExec = 1;
        private const string Model = "parakeet-tdt-0.6b-v2-int8";
        private const string Recorder = "parec";
        private const string Recognizer = "sherpa-onnx-offline";
        private readonly string folder;

        internal Ears(string data) { folder = Path.Combine(data, "ears", Model); }

        internal async Task<string> Listen(Action decoding, CancellationToken stop)
        {
            stop.ThrowIfCancellationRequested();
            string mic = Find(Recorder), engine = Find(Recognizer);
            if (mic == null) throw new IOException("Install parec (PulseAudio tools) so I can hear your mic.");
            if (engine == null) throw new IOException("Put sherpa-onnx-offline on PATH so I can hear your words.");
            string encoder = Path.Combine(folder, "encoder.int8.onnx"), decoder = Path.Combine(folder, "decoder.int8.onnx");
            string joiner = Path.Combine(folder, "joiner.int8.onnx"), tokens = Path.Combine(folder, "tokens.txt");
            if (!File.Exists(encoder) || !File.Exists(decoder) || !File.Exists(joiner) || !File.Exists(tokens))
                throw new IOException("Put my English Parakeet model files in Capri/ears/" + Model + " under your config folder.");

            short[] samples = await Record(mic, stop).ConfigureAwait(false);
            if (samples.Length == 0) return "";
            stop.ThrowIfCancellationRequested();
            decoding();
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(stop);
            limit.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                int fd = memfd_create("capri-voice", CloseOnExec);
                if (fd < 0) throw new IOException();
                using var handle = new SafeFileHandle(new IntPtr(fd), true);
                using var wave = new FileStream(handle, FileAccess.Write);
                wave.Write(Voice.Wave(samples, Rate));
                wave.Flush();
                string audio = "/proc/" + Environment.ProcessId + "/fd/" + fd;
                using Process run = Start(engine, "--num-threads=2", "--tokens=" + tokens, "--encoder=" + encoder,
                    "--decoder=" + decoder, "--joiner=" + joiner, audio);
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
        }

        private static async Task<short[]> Record(string mic, CancellationToken stop)
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(stop);
            limit.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                using Process run = Start(mic, "--raw", "--format=s16le", "--rate=16000", "--channels=1", "--latency-msec=50", "--client-name=Capri");
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
                throw new IOException("I couldn't hear your default mic. Check your sound settings and try TALK again.");
            }
        }

        internal static async Task<short[]> Words(Stream mic, CancellationToken stop)
        {
            short[] samples = new short[12 * Rate];
            byte[] frame = new byte[Frame * 2];
            int used = 0, voiced = 0, quiet = 0;
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
                voiced = sound ? voiced + 1 : 0;
                quiet = sound ? 0 : quiet + 1;
                if (voiced >= 5) heard = true;
                if (!heard && used >= 4 * Rate) break;
                if (heard && quiet >= 40) break;
            }
            return heard ? samples.AsSpan(0, used).ToArray() : Array.Empty<short>();
        }

        private static string Find(string name)
        {
            foreach (string path in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                if (!Path.IsPathFullyQualified(path)) continue;
                string file = Path.Combine(path, name);
                if (File.Exists(file)) return file;
            }
            return null;
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

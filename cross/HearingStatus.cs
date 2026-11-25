using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CapriDesktop
{
    internal enum HearingAction { Test, Stop, Type, Ears }

    internal sealed class HearingStatus
    {
        internal const string Ready = "Hearing: her own ears · default mic · ready";
        internal const string Starting = "Hearing: her own ears · default mic · starting";
        internal const string Working = "Hearing: her own ears · default mic · working";
        internal const string NoEars = "Her ears aren't installed";
        internal const string NoWords = "No words came back";
        internal const string Quiet = "Mic is too quiet: raise its input volume";
        internal const string TestSilent = "No sound reached the mic test";
        internal const string MicFailed = "Mic failed: ";
        internal const string EarsFailed = "Her ears failed: ";
        internal const string Tip = "She hears one line at a time through TALK, using your default mic. This line says when her ears aren't installed, when the mic fails, or when sound comes back without words. Test mic listens for up to 10 seconds; Type instead opens her type box. Pick your microphone in your system sound settings, because she can't switch it here.";
        internal const string TestTip = "Listens to your default mic for up to 10 seconds and says whether sound reaches her. Click Stop to end it early.";
        internal const string TypeTip = "Opens her TYPE box so you can chat without your mic.";
        internal const string EarsTip = "Shows the folder her speech models go in.";
        // Windows' thresholds, so a quiet mic means the same on every platform.
        internal const double MicAlive = 0.0005, QuietWake = 0.006;
        internal const int TestSeconds = 10;

        internal readonly string Text;
        internal readonly bool Problem;
        internal readonly HearingAction[] Actions;

        internal HearingStatus(string text, bool problem, params HearingAction[] actions)
        {
            Text = text;
            Problem = problem;
            Actions = actions;
        }

        internal static string Name(HearingAction action)
        {
            switch (action)
            {
                case HearingAction.Test: return "Test mic";
                case HearingAction.Stop: return "Stop";
                case HearingAction.Type: return "Type instead";
                default: return "Open ears folder";
            }
        }
    }

    internal sealed class HearingLine
    {
        private readonly object gate = new object();
        private bool listening, testing, open, quiet, testSilent, keptQuiet, keptTestSilent;
        private double peak, testEnds;
        private string trouble, result, keptTrouble, keptResult;

        internal bool Testing { get { lock (gate) return testing; } }

        internal void Listening(bool on)
        {
            lock (gate) { listening = on; open = false; if (on) peak = 0; }
        }

        // The level sink: every frame the tap hears, 0 to 1. It says whether the line on screen changed.
        internal bool Level(double level)
        {
            bool changed = false;
            lock (gate)
            {
                if (!open) { open = true; changed = true; }
                if (level > peak) peak = level;
                if ((testSilent || quiet) && level >= HearingStatus.QuietWake) { testSilent = false; quiet = false; changed = true; }
            }
            return changed;
        }

        internal void Heard()
        {
            lock (gate) { trouble = result = null; quiet = testSilent = false; }
        }

        internal void Missed()
        {
            lock (gate)
            {
                // Her recorder sends all zeros when nothing reaches the mic, but any sound at all, however faint,
                // means the mic is alive and only too quiet to hear words over. Windows calls that a quiet mic.
                if (peak <= 0) return;
                trouble = null;
                if (peak >= HearingStatus.QuietWake) { result = HearingStatus.NoWords; quiet = testSilent = false; }
                else { quiet = true; result = null; testSilent = false; }
            }
        }

        internal void Failed(string reason)
        {
            string sentence = Sentence(reason);
            if (sentence.Length == 0) return;
            lock (gate)
            {
                trouble = Installs(sentence) ? HearingStatus.NoEars
                    : sentence.IndexOf("offline ears", StringComparison.OrdinalIgnoreCase) >= 0 ? HearingStatus.EarsFailed + sentence
                    : HearingStatus.MicFailed + sentence;
            }
        }

        internal void StartTest(double now)
        {
            lock (gate)
            {
                testing = true;
                open = false;
                peak = 0;
                // What the line said before the test, so Stop can put it back instead of reporting a test they cut short.
                keptTrouble = trouble; keptResult = result; keptQuiet = quiet; keptTestSilent = testSilent;
                trouble = result = null;
                quiet = testSilent = false;
                testEnds = now + HearingStatus.TestSeconds;
            }
        }

        internal bool TestDue(double now) { lock (gate) return testing && now >= testEnds; }

        internal void TestCancelled()
        {
            lock (gate)
            {
                testing = false;
                open = false;
                peak = testEnds = 0;
                trouble = keptTrouble; result = keptResult; quiet = keptQuiet; testSilent = keptTestSilent;
                keptTrouble = keptResult = null;
                keptQuiet = keptTestSilent = false;
            }
        }

        internal void TestDone()
        {
            lock (gate)
            {
                testing = false;
                open = false;
                testSilent = peak == 0;
                quiet = peak > 0 && peak < HearingStatus.QuietWake;
                result = null;
                keptTrouble = keptResult = null;
                keptQuiet = keptTestSilent = false;
            }
        }

        internal void Reset()
        {
            lock (gate)
            {
                listening = testing = open = quiet = testSilent = false;
                peak = testEnds = 0;
                trouble = result = keptTrouble = keptResult = null;
                keptQuiet = keptTestSilent = false;
            }
        }

        internal HearingStatus Read(bool installed)
        {
            lock (gate)
            {
                if (!installed || trouble == HearingStatus.NoEars) return new HearingStatus(HearingStatus.NoEars, true, HearingAction.Ears);
                if (trouble != null)
                {
                    bool heard = trouble.StartsWith(HearingStatus.EarsFailed, StringComparison.Ordinal);
                    return heard ? new HearingStatus(trouble, true, HearingAction.Test, HearingAction.Type)
                        : new HearingStatus(trouble, true, HearingAction.Type);
                }
                if (testSilent) return new HearingStatus(HearingStatus.TestSilent, true, HearingAction.Test, HearingAction.Type);
                if (quiet) return new HearingStatus(HearingStatus.Quiet, true, HearingAction.Test, HearingAction.Type);
                if (result != null) return new HearingStatus(result, true, HearingAction.Test, HearingAction.Type);
                if (testing) return new HearingStatus(open ? HearingStatus.Working : HearingStatus.Starting, false, HearingAction.Stop);
                if (listening) return new HearingStatus(open ? HearingStatus.Working : HearingStatus.Starting, false);
                return new HearingStatus(HearingStatus.Ready, false);
            }
        }

        private static bool Installs(string reason)
        {
            return reason.StartsWith("Put my ", StringComparison.Ordinal) || reason.StartsWith("Extract sherpa-onnx", StringComparison.Ordinal);
        }

        private static string Sentence(string reason)
        {
            string text = Regex.Replace(reason ?? "", @"\s+", " ").Trim();
            int stop = text.IndexOf(". ", StringComparison.Ordinal);
            if (stop > 0) text = text.Substring(0, stop);
            return text.TrimEnd('.');
        }
    }

    // The ears only give back words, not levels, so she taps the same recorder they use for the seconds she holds
    // the mic: parec on Linux and capri-mic on a Mac, both at her ears' 16 kHz mono. It never speaks and only shows
    // on the hearing line, and if it can't start she simply has nothing to measure.
    internal static class HearingTap
    {
        private const int Rate = 16000;
        private const int Frame = Rate / 50;

        internal static async Task Run(Action<double> level, CancellationToken stop)
        {
            string file = Recorder();
            if (file == null || stop.IsCancellationRequested) return;
            bool mac = OperatingSystem.IsMacOS();
            var start = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            if (!mac) foreach (string arg in new[] { "--raw", "--format=s16le", "--rate=16000", "--channels=1", "--latency-msec=50", "--client-name=Capri" }) start.ArgumentList.Add(arg);
            Process run = null;
            try
            {
                run = Process.Start(start);
                if (run == null) return;
                run.ErrorDataReceived += delegate { };
                run.BeginErrorReadLine();
                byte[] frame = new byte[Frame * 2];
                while (!stop.IsCancellationRequested)
                {
                    await run.StandardOutput.BaseStream.ReadExactlyAsync(frame, stop).ConfigureAwait(false);
                    long energy = 0;
                    for (int i = 0; i < Frame; i++)
                    {
                        short sample = BinaryPrimitives.ReadInt16LittleEndian(frame.AsSpan(i * 2, 2));
                        energy += sample * sample;
                    }
                    level(Math.Sqrt((double)energy / Frame) / 32768.0);
                }
            }
            catch (Exception error) when (error is IOException || error is Win32Exception || error is OperationCanceledException || error is InvalidOperationException) { }
            finally
            {
                if (run != null)
                {
                    try { if (!run.HasExited) run.Kill(); }
                    catch (Exception error) when (error is InvalidOperationException || error is Win32Exception || error is NotSupportedException) { }
                    run.Dispose();
                }
            }
        }

        // The same places her ears look for a recorder: a desktop launch doesn't get a shell's PATH.
        internal static string Recorder()
        {
            if (OperatingSystem.IsMacOS())
            {
                string beside = Path.Combine(AppContext.BaseDirectory, "capri-mic");
                return File.Exists(beside) ? beside : null;
            }
            var places = new List<string>((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator));
            places.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin"));
            places.Add("/usr/local/bin");
            places.Add("/usr/bin");
            foreach (string place in places)
            {
                if (!Path.IsPathFullyQualified(place)) continue;
                string file = Path.Combine(place, "parec");
                if (File.Exists(file)) return file;
            }
            return null;
        }
    }
}

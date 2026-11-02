using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using Windows.Foundation;
using Windows.Media.SpeechRecognition;

namespace CapriDesktop
{
    internal abstract class Dictation : IDisposable
    {
        internal const string PrivacyOff = "Online speech recognition is off in Windows. Turn it on in Settings > Privacy & security > Speech.";
        internal const string MicBlocked = "Windows is blocking the mic. Turn on Microphone access and Let desktop apps access your microphone in Settings > Privacy & security > Microphone.";
        internal const string NoMic = "Windows can't find a mic. Plug one in, or pick one in Settings > System > Sound.";
        internal static Func<Dictation> Make = delegate { return new WindowsDictation(); };
        internal static Func<string> DefaultMic = delegate
        {
            try { return Windows.Media.Devices.MediaDevice.GetDefaultAudioCaptureId(Windows.Media.Devices.AudioDeviceRole.Default); }
            catch (Exception) { return null; }
        };
        internal Action<string> Heard;
        // What it thinks they've said so far, while they're still saying it.
        internal Action<string> Partial;
        internal Action<string> Lost;
        internal Action<string> Rejected;
        // The language picked in Settings, English included, or null to listen in Windows' own speech language.
        internal string Language;

        internal abstract void Start(Action<string> started);
        internal abstract void Hold(bool held);
        public abstract void Dispose();

        internal static string OtherMic(string chosen, string name, string defaultId)
        {
            if (string.IsNullOrEmpty(chosen) || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(defaultId) || defaultId.IndexOf(chosen, StringComparison.OrdinalIgnoreCase) >= 0) return null;
            return "Windows speech only hears your default mic, not " + name + ". Make " + name + " the default in Windows Settings > System > Sound > Input, or pick Mic: default here.";
        }

        internal static string Explain(Exception error)
        {
            while (error is AggregateException && error.InnerException != null) error = error.InnerException;
            if (error.HResult == unchecked((int)0x80045509)) return PrivacyOff;
            if (error.HResult == unchecked((int)0x80070005)) return MicBlocked;
            if (error.HResult == unchecked((int)0xC00DABE0)) return NoMic;
            return "Windows speech recognition didn't start: " + Voice.Short(error.Message);
        }
    }

    internal sealed class WindowsDictation : Dictation
    {
        private readonly object gate = new object();
        private SpeechRecognizer recognizer;
        private bool closed;
        private bool running;
        private bool held;
        private bool busy;
        private bool opened;
        private DateTime began;
        private int quick;

        internal static string Probe()
        {
            try
            {
                using (SpeechRecognizer made = new SpeechRecognizer())
                {
                    made.Constraints.Add(new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "dictation"));
                    SpeechRecognitionCompilationResult compiled = made.CompileConstraintsAsync().AsTask().Result;
                    return compiled.Status == SpeechRecognitionResultStatus.Success ? null : "Windows speech recognition couldn't load (" + compiled.Status + ").";
                }
            }
            catch (Exception error) { return Explain(error); }
        }

        internal override void Start(Action<string> started)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                string problem = null;
                try { Open(); }
                catch (Exception error) { problem = Explain(error); Brain.Log("ears", -1, "Windows speech failed (0x" + error.HResult.ToString("X8") + "): " + problem); Close(); }
                started(problem);
            });
        }

        // Windows speech's dictation language for her language: the one for their region first ("en-AU"), then the first that
        // speaks it, Chinese by its script. Null when Windows speech doesn't have it, so it listens in its own language.
        internal static string Tag(string language, IList<string> supported, string region)
        {
            if (language == null || supported == null) return null;
            string code = Brain.LanguageCode(language);
            string script = language == "Traditional Chinese" ? "TW|HK|MO|Hant" : language == "Simplified Chinese" ? "CN|SG|Hans" : null;
            string first = null;
            foreach (string tag in supported)
            {
                if (string.IsNullOrEmpty(tag)) continue;
                string[] parts = tag.Split('-');
                string lead = parts[0].ToLowerInvariant();
                if (lead != code && !(code == "no" && (lead == "nb" || lead == "nn"))) continue;
                if (script != null && !Regex.IsMatch(tag, "-(" + script + ")(-|$)", RegexOptions.IgnoreCase)) continue;
                if (!string.IsNullOrEmpty(region) && parts.Length > 1 && string.Equals(parts[parts.Length - 1], region, StringComparison.OrdinalIgnoreCase)) return tag;
                if (first == null) first = tag;
            }
            return first;
        }

        private static Windows.Globalization.Language Tongue(string language)
        {
            if (language == null) return null;
            try
            {
                List<string> tags = new List<string>();
                foreach (Windows.Globalization.Language one in SpeechRecognizer.SupportedTopicLanguages) tags.Add(one.LanguageTag);
                string tag = Tag(language, tags, System.Globalization.RegionInfo.CurrentRegion.TwoLetterISORegionName);
                return tag == null ? null : new Windows.Globalization.Language(tag);
            }
            catch (Exception) { return null; }
        }

        private void Open()
        {
            if (Ears.PrivacyBlocked()) throw new UnauthorizedAccessException(MicBlocked);
            Windows.Globalization.Language tongue = Tongue(Language);
            SpeechRecognizer made = tongue == null ? new SpeechRecognizer() : new SpeechRecognizer(tongue);
            lock (gate)
            {
                if (closed) { made.Dispose(); throw new ObjectDisposedException("Dictation"); }
                recognizer = made;
            }
            made.Constraints.Add(new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "dictation"));
            SpeechRecognitionCompilationResult compiled = made.CompileConstraintsAsync().AsTask().Result;
            if (compiled.Status != SpeechRecognitionResultStatus.Success) throw new IOException("Windows speech recognition couldn't load (" + compiled.Status + ").");
            try { made.ContinuousRecognitionSession.AutoStopSilenceTimeout = TimeSpan.FromMinutes(30); } catch (ArgumentException) { }
            made.ContinuousRecognitionSession.ResultGenerated += Result;
            made.HypothesisGenerated += Guessed;
            made.ContinuousRecognitionSession.Completed += Ended;
            lock (gate)
            {
                if (closed) throw new ObjectDisposedException("Dictation");
                opened = true;
                running = true;
                began = DateTime.UtcNow;
            }
            made.ContinuousRecognitionSession.StartAsync().AsTask().Wait();
            string mic = DefaultMic();
            Brain.Log("ears", -1, "Windows speech opened default mic " + (mic ?? "unknown") + "; Windows speech manages its capture format and conversion.");
        }

        internal override void Hold(bool hold)
        {
            lock (gate)
            {
                held = hold;
                if (busy || closed || !opened || running == !held) return;
                busy = true;
            }
            ThreadPool.QueueUserWorkItem(delegate { Settle(); });
        }

        private void Settle()
        {
            while (true)
            {
                SpeechRecognizer current;
                bool start;
                lock (gate)
                {
                    if (closed || running == !held) { busy = false; return; }
                    current = recognizer;
                    start = !running;
                    running = start;
                    if (start) began = DateTime.UtcNow;
                }
                try
                {
                    if (start) current.ContinuousRecognitionSession.StartAsync().AsTask().Wait();
                    else current.ContinuousRecognitionSession.StopAsync().AsTask().Wait();
                }
                catch (Exception error)
                {
                    lock (gate) busy = false;
                    Fail(Explain(error));
                    return;
                }
            }
        }

        private void Guessed(SpeechRecognizer sender, SpeechRecognitionHypothesisGeneratedEventArgs args)
        {
            Action<string> guessed = Partial;
            if (guessed != null) guessed(args.Hypothesis.Text);
        }

        private void Result(SpeechContinuousRecognitionSession session, SpeechContinuousRecognitionResultGeneratedEventArgs args)
        {
            SpeechRecognitionResult result = args.Result;
            string text = (result.Text ?? "").Trim();
            lock (gate) if (closed || held) return;
            if (result.Status != SpeechRecognitionResultStatus.Success || result.Confidence == SpeechRecognitionConfidence.Rejected || text.Length == 0)
            {
                Action<string> rejected = Rejected;
                if (rejected != null) rejected("No words came back");
                return;
            }
            Action<string> heard = Heard;
            if (heard != null) heard(text);
        }

        private void Ended(SpeechContinuousRecognitionSession session, SpeechContinuousRecognitionCompletedEventArgs args)
        {
            SpeechRecognitionResultStatus status = args.Status;
            bool fatal = status == SpeechRecognitionResultStatus.MicrophoneUnavailable || status == SpeechRecognitionResultStatus.TopicLanguageNotSupported || status == SpeechRecognitionResultStatus.GrammarLanguageMismatch || status == SpeechRecognitionResultStatus.GrammarCompilationFailure;
            lock (gate)
            {
                if (closed || !running) return;
                Brain.Log("ears", -1, "Windows speech session ended: " + status + ".");
                running = false;
                quick = DateTime.UtcNow - began < TimeSpan.FromSeconds(5) ? quick + 1 : 0;
                if (!fatal && quick < 3)
                {
                    if (busy) return;
                    busy = true;
                    bool pause = quick > 0;
                    ThreadPool.QueueUserWorkItem(delegate { if (pause) Thread.Sleep(700); Settle(); });
                    return;
                }
            }
            Fail(status == SpeechRecognitionResultStatus.NetworkFailure ? "Windows speech recognition lost its internet connection." : status == SpeechRecognitionResultStatus.MicrophoneUnavailable ? "Windows speech recognition couldn't use the mic." : "Windows speech recognition stopped (" + status + ").");
        }

        private void Fail(string problem)
        {
            if (!Close()) return;
            Action<string> lost = Lost;
            if (lost != null) lost(problem);
        }

        private bool Close()
        {
            SpeechRecognizer current;
            lock (gate)
            {
                if (closed) return false;
                closed = true;
                current = recognizer;
                recognizer = null;
            }
            if (current == null) return true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { current.ContinuousRecognitionSession.CancelAsync().AsTask().Wait(3000); } catch (Exception) { }
                try { current.Dispose(); } catch (Exception) { }
            });
            return true;
        }

        public override void Dispose() { Close(); }
    }

    internal sealed class StandInDictation : Dictation
    {
        internal string Problem;
        internal bool Late;
        internal bool Started;
        internal bool Held;
        internal bool Disposed;
        internal Action<string> Pending;

        internal override void Start(Action<string> started)
        {
            Started = true;
            if (Late) Pending = started;
            else started(Problem);
        }

        internal override void Hold(bool held) { Held = held; }

        public override void Dispose() { Disposed = true; }
    }
}

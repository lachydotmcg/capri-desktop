using System;
using System.IO;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace CapriDesktop
{
    internal sealed partial class Settings
    {
        // A new opt-in: older dormant microphone flags do not grant consent.
        [DataMember] internal bool ListenCapri;
    }

    internal static class NameListeningText
    {
        internal static string Request(string text)
        {
            text = (text ?? "").Trim();
            text = Regex.Replace(text, @"^(?:hey\s+)?capri(?:[\s,:.!?]+|$)", "", RegexOptions.IgnoreCase);
            return Regex.Replace(text, @"[\s,:.!?]+capri[.!?]*$", "", RegexOptions.IgnoreCase).Trim();
        }
    }

    internal sealed partial class Pet
    {
        private sealed record NameContext(IBrainProvider Provider, string Model, Cloud Remote, string RemoteModel,
            object Local, string Session, string Source, string Fingerprint, string Python, string Manifest,
            string RemoteKey, string VoiceConfiguration);
        private readonly OwnedNameEars nameEars = new OwnedNameEars();
        private NameContext nameContext;
        private Task nameLifetime = Task.CompletedTask;
        private long nameEpoch;
        private bool nameStopped = true, nameStartup, nameClosed, nameChecking, nameStarting;
        private string nameIdentity, nameStatus = "Local name listening is off.";
        private double nextNameIdentityCheck;

        internal bool NameListeningEnabled => Saved.ListenCapri && !nameStopped;
        internal string NameListeningStatus => nameStatus;
        private bool NameBarred => nameClosed || quitting || away || Hushed || Asleep || Listening ||
            Talk?.Typer.Secret == true || HyprlandDesktop.Current?.VisualSnapshot?.Locked != false;

        private NameContext CaptureNameContext() => new NameContext(Brain.Provider, Brain.Model, Brain.Remote,
            Brain.Remote?.Model, Brain.Local, HyprlandDesktop.Current?.SessionId, Saved.MicrophoneNode,
            Saved.MicrophoneFingerprint, Saved.OwnedAsrPython, Saved.OwnedAsrManifest,
            Brain.Remote?.Key, Saved.VoiceProvider + "|" + Saved.VoiceId + "|" + Saved.OwnedVoicePython + "|" + Saved.OwnedVoiceProfile + "|" + Saved.Silent + "|" + Saved.LocalVoiceUrl + "|" + Saved.LocalVoiceModel + "|" + Saved.LocalVoiceName + "|" + Saved.LocalVoiceFormat + "|" + Saved.SpeechModel);

        private bool SameNameContext(NameContext value)
        {
            if (value == null) return false;
            return ReferenceEquals(value.Provider, Brain.Provider) && ReferenceEquals(value.Remote, Brain.Remote) &&
                ReferenceEquals(value.Local, Brain.Local) && value.Model == Brain.Model && value.RemoteModel == Brain.Remote?.Model &&
                value.RemoteKey == Brain.Remote?.Key && value.Session == HyprlandDesktop.Current?.SessionId &&
                value.Source == Saved.MicrophoneNode && value.Fingerprint == Saved.MicrophoneFingerprint &&
                value.Python == Saved.OwnedAsrPython && value.Manifest == Saved.OwnedAsrManifest &&
                value.VoiceConfiguration == Saved.VoiceProvider + "|" + Saved.VoiceId + "|" + Saved.OwnedVoicePython + "|" +
                    Saved.OwnedVoiceProfile + "|" + Saved.Silent + "|" + Saved.LocalVoiceUrl + "|" + Saved.LocalVoiceModel + "|" +
                    Saved.LocalVoiceName + "|" + Saved.LocalVoiceFormat + "|" + Saved.SpeechModel;
        }

        // Provider.Identity may read active.json. Only workers call this method.
        private static string NameIdentity(NameContext value)
        {
            string account = value.Provider?.Identity ?? "local";
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account + "|" + value.Model + "|" +
                value.RemoteModel + "|" + value.Session + "|" + value.Source + "|" + value.Fingerprint)));
        }

        private NameListeningState NameState() => new NameListeningState(nameIdentity, Saved.MicrophoneNode,
            Saved.MicrophoneFingerprint, NameBarred, SpeechAudio.MicrophoneHeld,
            Talk?.ThinkingNow == true || ComputerJobs.Active, ComputerJobs.GameRunning);

        private void InitializeNameListening()
        {
            SpeechAudio.BeforePlayback += GateNamePlayback;
            nameStartup = Saved.ListenCapri;
            Closed += (_, _) =>
            {
                nameClosed = true;
                SpeechAudio.BeforePlayback -= GateNamePlayback;
                StopNameListening("Local name listening stopped.");
            };
        }

        private void GateNamePlayback() => nameEars.GateForPlayback();

        internal void SetNameListening(bool on)
        {
            if (!OperatingSystem.IsLinux() || Program.Smoke != null) return;
            Saved.ListenCapri = on;
            Save();
            if (!on) { StopNameListening("Local name listening is off."); return; }
            StartNameListening();
        }

        private void SetNameStatus(string status)
        {
            if (nameStatus == status) return;
            nameStatus = status;
            NotifySettings();
        }

        private void StopNameListening(string status = "Name listening stopped. Turn Listen for Capri on to resume.")
        {
            nameEpoch++;
            nameStopped = true;
            nameStarting = false;
            nameStartup = false;
            nameEars.Stop();
            SetNameStatus(status);
        }

        private async void StartNameListening()
        {
            StopNameListening();
            if (NameBarred) { SetNameStatus(Listening ? "Finish TALK, then turn Listen for Capri on." : "Name listening paused. Unmute and wake Capri, then turn it on."); return; }
            NameContext captured = CaptureNameContext();
            if (string.IsNullOrWhiteSpace(captured.Source) || string.IsNullOrWhiteSpace(captured.Fingerprint))
            { ChooseNameMicrophone(); return; }
            long ticket = nameEpoch;
            nameContext = captured;
            nameStopped = false;
            nameStarting = true;
            SetNameStatus("Starting local name listening…");
            try
            {
                await nameLifetime;
                string identity = await Task.Run(() =>
                {
                    if (!File.Exists(captured.Python) || !File.Exists(captured.Manifest))
                        throw new InvalidOperationException("Configure the retained local decoder before enabling Listen for Capri.");
                    return NameIdentity(captured);
                });
                if (ticket != nameEpoch || nameStopped) return;
                if (NameBarred || !SameNameContext(captured))
                { StopNameListening("The listening context changed during startup. Re-enable Listen for Capri."); return; }
                nameContext = captured;
                nameIdentity = identity;
                nextNameIdentityCheck = Now + 1;
                nameLifetime = nameEars.Start(captured.Python, captured.Manifest, NameState(),
                    status => Dispatcher.UIThread.Post(() => { if (ticket == nameEpoch && !nameStopped) SetNameStatus(status); }),
                    transcript => ValidateNameFinal(ticket, captured, identity, transcript),
                    error => Dispatcher.UIThread.Post(() => { if (ticket == nameEpoch) StopNameListening(error); }));
            }
            catch (Exception error)
            {
                if (ticket == nameEpoch) StopNameListening(error is InvalidOperationException ? error.Message :
                    "Local name listening could not start. Check the retained decoder and account setup.");
            }
            finally { if (ticket == nameEpoch) nameStarting = false; }
        }

        private void ChooseNameMicrophone()
        {
            long ticket = nameEpoch;
            Talk.Answer("(microphone)", "Checking available physical microphones…");
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    byte[] inventory = ComputerProcess.Bytes("/usr/bin/pw-dump", Array.Empty<string>(), System.Threading.CancellationToken.None, 5000, 4 * 1024 * 1024);
                    System.Diagnostics.ProcessStartInfo info = ComputerProcess.Start("/usr/bin/python3", new[] { System.IO.Path.Combine(AppContext.BaseDirectory, "bridge/speech/sources.py") });
                    using System.Diagnostics.Process filter = System.Diagnostics.Process.Start(info);
                    using System.Threading.CancellationTokenSource timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                    using System.Threading.CancellationTokenRegistration abort = timeout.Token.Register(() => Bridge.Kill(filter));
                    var read = filter.StandardOutput.ReadToEndAsync();
                    var errors = filter.StandardError.ReadToEndAsync();
                    filter.StandardInput.BaseStream.Write(inventory); filter.StandardInput.Close();
                    filter.WaitForExit();
                    if (filter.ExitCode != 0) throw new InvalidOperationException("Microphone inventory could not be verified.");
                    using System.Text.Json.JsonDocument rows = System.Text.Json.JsonDocument.Parse(read.GetAwaiter().GetResult());
                    var choices = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Take(System.Linq.Enumerable.Select(rows.RootElement.EnumerateArray(), row => row.Clone()), 8));
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (ticket != nameEpoch || !Saved.ListenCapri) return;
                        if (choices.Length == 0) { Talk.Answer("(microphone)", "No physical microphone is available."); return; }
                        string prompt = "Choose a microphone near you for Listen for Capri by number.\n";
                        for (int i = 0; i < choices.Length; i++) prompt += (i + 1) + ": " + choices[i].GetProperty("description").GetString() + "\n";
                        Talk.AskFor("(microphone)", prompt, selected =>
                        {
                            if (ticket != nameEpoch || !Saved.ListenCapri) return;
                            if (!int.TryParse(selected, out int index) || index < 1 || index > choices.Length) { Talk.Answer("(microphone)", "No microphone selected. Turn Listen for Capri on to choose again."); return; }
                            Saved.MicrophoneNode = choices[index - 1].GetProperty("node").GetString();
                            Saved.MicrophoneFingerprint = choices[index - 1].GetProperty("fingerprint").GetString();
                            Save(); StartNameListening();
                        });
                    });
                }
                catch (Exception error) { Dispatcher.UIThread.Post(() => { if (ticket == nameEpoch && Saved.ListenCapri) Talk.Answer("(microphone)", error.Message); }); }
            });
        }

        private void ValidateNameFinal(long ticket, NameContext captured, string identity, NameTranscript transcript)
        {
            // Do not run account disk I/O on the UI thread. The generation and
            // context are checked again after the UI dispatcher receives it.
            int providerGeneration = captured.Provider?.Generation ?? 0;
            _ = Task.Run(() =>
            {
                bool current;
                try { current = identity == NameIdentity(captured); }
                catch (Exception) { current = false; }
                Dispatcher.UIThread.Post(() =>
                {
                    if (ticket != nameEpoch || nameStopped || !Saved.ListenCapri) return;
                    if (!current || !SameNameContext(captured))
                    { StopNameListening("The account, model or microphone changed. Re-enable Listen for Capri."); return; }
                    // An ordinary new Hear cancels the provider generation too. Drop
                    // that stale queued line without ending the valid name window.
                    if (providerGeneration != (captured.Provider?.Generation ?? 0)) return;
                    if (NameBarred || SpeechAudio.MicrophoneHeld || !nameEars.IsCurrent(transcript)) return;
                    string request = NameListeningText.Request(transcript.Text);
                    if (request.Length != 0) Hear(request, true);
                });
            });
        }

        private void TickNameListening()
        {
            if (!OperatingSystem.IsLinux() || Program.Smoke != null || nameClosed) return;
            if (nameStartup && opened && HyprlandDesktop.Current?.VisualSnapshot?.Locked == false)
            { nameStartup = false; StartNameListening(); }
            if (nameStopped) return;
            if (NameBarred || nameContext != null && !SameNameContext(nameContext))
            { StopNameListening("Name listening stopped after a state change. Turn Listen for Capri on to resume."); return; }
            if (nameStarting || !nameEars.Active) return;
            nameEars.Update(NameState());
            if (nameChecking || Now < nextNameIdentityCheck) return;
            nameChecking = true;
            nextNameIdentityCheck = Now + 1;
            long ticket = nameEpoch;
            NameContext captured = nameContext;
            string identity = nameIdentity;
            _ = Task.Run(() =>
            {
                bool current;
                try { current = identity == NameIdentity(captured); }
                catch (Exception) { current = false; }
                Dispatcher.UIThread.Post(() =>
                {
                    nameChecking = false;
                    if (ticket == nameEpoch && !nameStopped && (!current || !SameNameContext(captured)))
                        StopNameListening("The account changed. Turn Listen for Capri on to resume.");
                });
            });
        }

    }
}

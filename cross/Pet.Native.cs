using System;
using System.IO;
using System.Runtime.Serialization;
using Avalonia.Controls;
using Avalonia.Threading;

namespace CapriDesktop
{
    internal sealed partial class Settings
    {
        [DataMember] internal int LinuxMigrationVersion;
        [DataMember] internal bool NativePlan;
        [DataMember] internal bool OwnPlanAccount;
        [DataMember] internal string PlanPython;
        [DataMember] internal string PlanAccounts;
        [DataMember] internal string PlanModel;
        [DataMember] internal string PlanVisionModel;
        [DataMember] internal string PlanEffort;
        [DataMember] internal string PlanVisionEffort;
        [DataMember] internal int? PlanTokens;
        [DataMember] internal int? PlanVisionTokens;
        [DataMember] internal bool CubicVoiceVolume;
        [DataMember] internal string OwnedVoicePython;
        [DataMember] internal string OwnedVoiceProfile;
        [DataMember] internal string OwnedAsrPython;
        [DataMember] internal string OwnedAsrManifest;
        [DataMember] internal string MicrophoneNode;
        [DataMember] internal string MicrophoneFingerprint;
    }

    internal sealed partial class Pet
    {
        private System.Diagnostics.Process accountConnection;
        private volatile int accountConnectionGeneration;
        private readonly object accountGate = new object();
        private bool accountConnecting;
        private void CancelAccountConnection() { lock (accountGate) { accountConnectionGeneration++; accountConnecting = false; if (accountConnection != null) Bridge.Kill(accountConnection); accountConnection = null; } }
        private readonly VoiceActionRecovery voiceRecovery = new VoiceActionRecovery();
        private readonly OwnedEars ownedEars = new OwnedEars();
        private int listeningGeneration, speechGeneration;
        private string listeningAccount, listeningSession;
        private MenuItem voiceStyle;
        internal bool RetainedVoice { get { return Speakers.Known(Saved.VoiceProvider) == Speakers.Owned; } }
        private bool SpeechCurrent(ISpeaker speaker, int ticket) { return ticket == speechGeneration && ReferenceEquals(speaker, Speaker) && Speaks; }
        private void CancelSpeech() { speechGeneration++; ownedVoice.Cancel(); voice.Stop(); }
        private void LeavePlan() { TerminalJobs.StopAll(); CancelAccountConnection(); Brain.Provider?.Cancel(); Brain.Provider = null; Saved.NativePlan = false; }
        private string ListeningAccount { get { return Brain.Provider == null ? Brain.Model + "|" + Brain.Remote?.Model : Brain.Provider.Identity + "|" + Brain.Provider.Generation; } }
        private void CheckListeningSession()
        {
            if (!ownedEars.Active) return;
            bool current;
            try { current = listeningAccount == ListeningAccount && listeningSession == HyprlandDesktop.Current?.SessionId && HyprlandDesktop.Current?.Snapshot?.Locked == false; }
            catch (Exception) { current = false; }
            if (!current) { StopListening(); Talk.Answer("(voice)", "Listening stopped because the account or desktop session changed."); }
        }
        private readonly OwnedSpeaker ownedVoice = new OwnedSpeaker();

        private void InitializeNativeAdapters()
        {
            if (!OperatingSystem.IsLinux() || Program.Smoke != null) return;
            TerminalJobs.Context = () => (Brain.Provider?.Identity ?? Brain.Model ?? "") + "|" + HyprlandDesktop.Current?.SessionId + "|" + HyprlandDesktop.Current?.Snapshot?.Locked + "|" + Security.Saved;
            Voice.CubicLoudness = Saved.CubicVoiceVolume;
            ownedVoice.Python = Saved.OwnedVoicePython;
            ownedVoice.Profile = Saved.OwnedVoiceProfile;
            ownedVoice.Cache = Path.Combine(folder, "speech-cache");
            Voice.Playback = SpeechAudio.Play;
            Voice.Silence = SpeechAudio.Stop;
            SpeechAudio.Failed += problem => Dispatcher.UIThread.Post(() => { voiceProblem = problem; NotifySettings(); });
            if (Saved.NativePlan) SelectPlan();
            InitializeNameListening();
            Closed += (_, _) => { TerminalJobs.Shutdown(); CancelAccountConnection(); ComputerJobs.Stop(); ownedVoice.Dispose(); StopListening(); SpeechAudio.Stop(); };
        }

        private void SelectPlan()
        {
            if (string.IsNullOrEmpty(Saved.PlanAccounts) || string.IsNullOrEmpty(Saved.PlanModel) || !File.Exists(Saved.PlanPython))
                throw new BrainException("Configure the retained ChatGPT account and Python runtime before selecting ChatGPT plan.");
            TerminalJobs.StopAll();
            ComputerJobs.Stop();
            StopListening();
            Brain.Cancel();
            Brain.Remote = null;
            Brain.Provider = new PlanBrain(Saved.PlanPython, Saved.PlanAccounts, Saved.PlanModel, Saved.PlanVisionModel, Saved.PlanEffort, Saved.PlanVisionEffort, Saved.PlanTokens, Saved.PlanVisionTokens, Saved.OwnPlanAccount);
            Brain.Model = Saved.PlanModel;
            Saved.NativePlan = true;
        }

        internal void UsePlan()
        {
            if (!OperatingSystem.IsLinux()) { frameProblem = "ChatGPT plan linking is available in this Linux candidate."; NotifySettings(); return; }
            if (accountConnecting) { frameProblem = "Sign-in is already open in your browser."; NotifySettings(); return; }
            if (!File.Exists(Saved.PlanPython)) { frameProblem = "Configure the pinned ChatGPT Python runtime first."; NotifySettings(); return; }
            string directory;
            try { directory = NativeAccount.Initialize(folder); }
            catch (Exception error) { frameProblem = error.Message; NotifySettings(); return; }
            accountConnecting = true;
            int ticket;
            lock (accountGate) ticket = ++accountConnectionGeneration;
            frameProblem = "Connecting ChatGPT plan…"; NotifySettings();
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    string[] models = NativeAccount.Run(Saved.PlanPython, directory, true, Saved.PlanModel,
                        status => Dispatcher.UIThread.Post(() => { if (ticket == accountConnectionGeneration) { frameProblem = status; NotifySettings(); } }),
                        child => { lock (accountGate) { if (ticket == accountConnectionGeneration) accountConnection = child; else Bridge.Kill(child); } }, () => ticket == accountConnectionGeneration && !quitting);
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (ticket != accountConnectionGeneration) return;
                        Saved.PlanAccounts = directory; Saved.OwnPlanAccount = true;
                        if (Array.IndexOf(models, Saved.PlanModel) < 0) Saved.PlanModel = models[0];
                        if (Array.IndexOf(models, Saved.PlanVisionModel) < 0) Saved.PlanVisionModel = Saved.PlanModel;
                        SelectPlan(); Save(); frameModels = models; frameProblem = "ChatGPT plan connected."; NotifySettings();
                        Talk.Answer("(ChatGPT plan)", "Connected. You can type or talk to me now.");
                    });
                }
                catch (Exception error) { Dispatcher.UIThread.Post(() => { if (ticket == accountConnectionGeneration) { frameProblem = error.Message; NotifySettings(); Talk.Answer("(ChatGPT plan)", error.Message); } }); }
                finally { Dispatcher.UIThread.Post(() => { if (ticket == accountConnectionGeneration) { accountConnection = null; accountConnecting = false; } }); }
            });
        }

        private void ShowPlanModel()
        {
            frameModels = new[] { Saved.PlanModel };
            brainMenu.Items.Clear();
            brainMenu.Items.Add(new MenuItem { Header = "ChatGPT plan · " + Saved.PlanModel, IsEnabled = false });
            frameProblem = null;
            NotifySettings();
        }

        internal void StopNativeWork()
        {
            bool terminalPending = TerminalJobs.StopAll();
            asking++; CancelAccountConnection(); ComputerJobs.Stop(); StopListening(); CancelSpeech(); Brain.Cancel();
            Talk.Answer("(stop)", terminalPending ? "Terminal stop requested. Check its job status for completion." : "Stopped.");
        }

        private void StopListening(bool stopName = true)
        {
            if (stopName) StopNameListening();
            voiceRecovery.Cancel();
            listeningGeneration++;
            ownedEars.Stop();
        }

        internal async void ListenOnce()
        {
            if (!OperatingSystem.IsLinux()) { NoVoice(); return; }
            if (nameTalkHandoff) { StopListening(); return; }
            StopNameListening("TALK owns the microphone. Re-enable Listen for Capri afterward.");
            if (!nameLifetime.IsCompleted)
            {
                int handoff = ++listeningGeneration;
                nameTalkHandoff = true;
                try { await nameLifetime; }
                finally { nameTalkHandoff = false; }
                if (handoff != listeningGeneration || quitting) return;
            }
            if (ownedEars.Active) { StopListening(); Talk.Answer("(voice)", "Listening stopped."); return; }
            if (SpeechAudio.MicrophoneHeld) { Talk.Answer("(voice)", "Wait for my speech to finish, then tap TALK."); return; }
            if (!File.Exists(Saved.OwnedAsrPython) || !File.Exists(Saved.OwnedAsrManifest))
            { Talk.Answer("(voice)", "Select your microphone and retained local decoder in the Linux profile setup first."); return; }
            if (string.IsNullOrEmpty(Saved.MicrophoneNode) || string.IsNullOrEmpty(Saved.MicrophoneFingerprint)) { ChooseMicrophone(); return; }
            string account = listeningAccount = ListeningAccount;
            string session = listeningSession = HyprlandDesktop.Current?.SessionId;
            int ticket = ++listeningGeneration;
            Talk.Answer("(voice)", "Opening your selected microphone. Tap TALK again to stop.");
            _ = ownedEars.Listen(Saved.OwnedAsrPython, Saved.OwnedAsrManifest, Saved.MicrophoneNode, Saved.MicrophoneFingerprint,
                () => Dispatcher.UIThread.Post(() => { if (ticket == listeningGeneration) Talk.Answer("(voice)", "Listening for one sentence…"); }),
                text => Dispatcher.UIThread.Post(() =>
                {
                    string current = Brain.Provider == null ? Brain.Model + "|" + Brain.Remote?.Model : Brain.Provider.Identity + "|" + Brain.Provider.Generation;
                    if (ticket == listeningGeneration && account == current && session == HyprlandDesktop.Current?.SessionId && HyprlandDesktop.Current?.Snapshot?.Locked == false) Hear(text, true);
                    else if (ticket == listeningGeneration) { StopListening(); Talk.Answer("(voice)", "The account or desktop session changed; that transcript was not submitted."); }
                }),
                error => Dispatcher.UIThread.Post(() => { if (ticket == listeningGeneration) Talk.Answer("(voice)", error); }));
        }

        private void ChooseMicrophone()
        {
            int ticket = ++listeningGeneration;
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
                        if (ticket != listeningGeneration) return;
                        if (choices.Length == 0) { Talk.Answer("(microphone)", "No physical microphone is available."); return; }
                        string prompt = "Choose a microphone near you by number. This starts listening for one sentence; TALK stops it.\n";
                        for (int i = 0; i < choices.Length; i++) prompt += (i + 1) + ": " + choices[i].GetProperty("description").GetString() + "\n";
                        Talk.AskFor("(microphone)", prompt, selected =>
                        {
                            if (ticket != listeningGeneration) return;
                            if (!int.TryParse(selected, out int index) || index < 1 || index > choices.Length) { Talk.Answer("(microphone)", "No microphone selected. Tap TALK to choose again."); return; }
                            Saved.MicrophoneNode = choices[index - 1].GetProperty("node").GetString();
                            Saved.MicrophoneFingerprint = choices[index - 1].GetProperty("fingerprint").GetString();
                            Save(); ListenOnce();
                        });
                    });
                }
                catch (Exception error) { Dispatcher.UIThread.Post(() => { if (ticket == listeningGeneration) Talk.Answer("(microphone)", error.Message); }); }
            });
        }

        internal void RestoreDraft(Typer typer)
        {
            string path = Path.Combine(folder, "draft.txt");
            if (File.Exists(path)) typer.Draft = File.ReadAllText(path);
            typer.Typed += () =>
            {
                if (typer.Secret) return;
                string temporary = path + ".new";
                File.WriteAllText(temporary, typer.Draft);
                File.Move(temporary, path, true);
            };
        }

        private static byte[] StyledSpeech(ISpeaker speaker, string text)
        {
            byte[] wav = speaker.Speak(text);
            return speaker is OwnedSpeaker ? wav : VoiceFx.Styled(wav, text);
        }
    }
}

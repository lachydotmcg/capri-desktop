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
        private int speechGeneration;
        private MenuItem voiceStyle;
        internal bool RetainedVoice { get { return Speakers.Known(Saved.VoiceProvider) == Speakers.Owned; } }
        private bool SpeechCurrent(ISpeaker speaker, int ticket) { return ticket == speechGeneration && ReferenceEquals(speaker, Speaker) && Speaks; }
        private void CancelSpeech() { speechGeneration++; ownedVoice.Cancel(); voice.Stop(); }
        private void LeavePlan() { TerminalJobs.StopAll(); CancelAccountConnection(); Brain.Provider?.Cancel(); Brain.Provider = null; Saved.NativePlan = false; }
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
            Closed += (_, _) => { TerminalJobs.Shutdown(); CancelAccountConnection(); ComputerJobs.Stop(); ownedVoice.Dispose(); StopNativeListening(); SpeechAudio.Stop(); };
        }

        private void SelectPlan()
        {
            if (string.IsNullOrEmpty(Saved.PlanAccounts) || string.IsNullOrEmpty(Saved.PlanModel) || !File.Exists(Saved.PlanPython))
                throw new BrainException("Configure the retained ChatGPT account and Python runtime before selecting ChatGPT plan.");
            TerminalJobs.StopAll();
            ComputerJobs.Stop();
            StopNativeListening();
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
            if (!File.Exists(Saved.PlanPython)) { frameProblem = "ChatGPT plan needs its Python environment from tools/plan-requirements.lock; set PlanPython to that environment's Python executable."; NotifySettings(); Talk.Answer("(ChatGPT plan)", frameProblem); return; }
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
            asking++; CancelAccountConnection(); ComputerJobs.Stop(); StopNativeListening(); CancelSpeech(); Brain.Cancel();
            Talk.Answer("(stop)", terminalPending ? "Terminal stop requested. Check its job status for completion." : "Stopped.");
        }

        private void StopNativeListening(bool stopName = true)
        {
            if (stopName) StopNameListening();
            voiceRecovery.Cancel();
            StopListening();
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

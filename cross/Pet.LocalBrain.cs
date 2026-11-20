using System;
using System.Threading;
using Avalonia.Threading;

namespace CapriDesktop
{
    internal sealed partial class Settings
    {
        [System.Runtime.Serialization.DataMember] internal bool OwnBrain;
    }

    internal sealed partial class Pet
    {
        private Engine ownEngine;
        private bool choosingBrain;

        internal Engine OwnEngine { get { return ownEngine ?? (ownEngine = new Engine(folder)); } }

        internal bool SettingUpBrain { get { return pulling || choosingBrain; } }

        internal int BrainPercent { get { return pulled; } }

        internal static bool BrainViaEngine(bool ollamaReachable, bool engineKnown)
        {
            return !ollamaReachable && engineKnown;
        }

        internal void SetUpLocalBrain()
        {
            if (SettingUpBrain) return;
            choosingBrain = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool ollama = true;
                try { Brain.InstalledModels(); }
                catch (Exception) { ollama = false; }
                Dispatcher.UIThread.Post(delegate
                {
                    choosingBrain = false;
                    if (BrainViaEngine(ollama, Engine.ThisAsset() != null)) PullOwnBrain(); else PullBrain(Starter);
                });
            });
        }

        internal void CancelBrainSetup() { if (ownEngine != null) ownEngine.Cancel = true; }

        private void PullOwnBrain()
        {
            if (pulling) return;
            Engine engine = OwnEngine;
            Pick pick = Engine.Fit(Engine.ReadMachine());
            engine.Cancel = false;
            pulling = true;
            pulled = -1;
            pullingName = pick.Name;
            Talk.Answer(BrainHeard, "Downloading my brain, " + pick.Name + " (" + pick.Download + "). BRAIN in Capri Mainframe shows its progress.");
            ThreadPool.QueueUserWorkItem(delegate
            {
                string problem = null;
                try
                {
                    engine.InstallBinary(pick, delegate(string label, long done, long total) { pulled = (int)(done * 100 / Math.Max(1, total)); });
                    Brain.LocalPick = pick;
                    Brain.Local = engine;
                    Brain.Model = pick.Name;
                    engine.Ensure(pick);
                }
                catch (OperationCanceledException) { problem = "Cancelled. It picks up where it left off."; }
                catch (Exception error)
                {
                    if (!engine.Cancel) Brain.Log("brain setup", -1, Brain.Redacted(error.Message));
                    problem = Brain.Brief(error.Message) ?? "Couldn't set up my brain: " + Voice.Short(error.Message);
                }
                Dispatcher.UIThread.Post(delegate
                {
                    pulling = false;
                    pulled = -1;
                    if (problem != null)
                    {
                        if (Brain.Local == engine) { Brain.Local = null; Brain.LocalPick = null; Brain.Model = null; }
                        if (MainframeTier == 0) frameProblem = problem;
                        NotifySettings();
                        Talk.Notice(BrainHeard, problem);
                        return;
                    }
                    frameModels = new[] { pick.Name };
                    frameProblem = null;
                    Saved.Model = pick.Name;
                    Saved.OwnBrain = true;
                    Save();
                    NotifySettings();
                    Talk.Notice(BrainHeard, "My brain's in. Talk to me, choom.");
                });
            });
        }

        private void RestoreOwnBrain()
        {
            if (!Saved.OwnBrain || Brain.Remote != null || Brain.Server != null || Brain.Provider != null || Engine.ThisAsset() == null) return;
            Engine engine = OwnEngine;
            Pick pick = engine.Installed();
            if (pick == null) return;
            Brain.LocalPick = pick;
            Brain.Local = engine;
            Brain.Model = pick.Name;
            frameModels = new[] { pick.Name };
        }

        private void LeaveOwnBrain()
        {
            if (Brain.Local == null) return;
            Brain.Local = null;
            Brain.LocalPick = null;
            Saved.OwnBrain = false;
        }

        private void StopOwnBrain()
        {
            Engine engine = ownEngine;
            if (engine == null) return;
            engine.Cancel = true;
            engine.Stop();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace CapriDesktop
{
    internal sealed class MacQuit
    {
        private static MacQuit active;
        private bool exited;
        private bool left;
        private int code;
        internal bool Stopping { get; private set; }
        internal static bool Quitting { get { return OperatingSystem.IsMacOS() && active != null && active.Stopping; } }
        internal Action Prepare;
        internal Action<int> Shutdown;
        internal Action<Action> Post = action => Dispatcher.UIThread.Post(action, DispatcherPriority.Send);
        internal Action<int, Action> After = (ms, action) =>
        {
            new Thread(() => { Thread.Sleep(ms); action(); }) { IsBackground = true, Name = "Capri quit" }.Start();
        };
        internal Action Kill = () => { using (Process process = Process.GetCurrentProcess()) process.Kill(); };
        internal Action<Exception> Failed = Program.Crashed;

        internal void Attach(IClassicDesktopStyleApplicationLifetime desktop)
        {
            active = this;
            desktop.ShutdownRequested += Requested;
            desktop.Exit += (_, _) => Exited();
        }

        private void Attach(QuitDesktop desktop)
        {
            active = this;
            desktop.ShutdownRequested += Requested;
            desktop.Exit += Exited;
        }

        internal void Requested(object sender, ShutdownRequestedEventArgs e)
        {
            e.Cancel = false;
            Request();
        }

        // Her own Quit hands in her glitch-out as the outro and leaves when it ends. Anything that asks again meanwhile, like
        // the system or her window closing under it, leaves at once. A failing exit code is never replaced by a clean one.
        internal void Request(int code = 0, Action<Action> outro = null)
        {
            if (code != 0) this.code = code;
            if (Stopping) { Leave(); return; }
            Stopping = true;
            After(5000, Kill);
            if (outro != null)
            {
                try { outro(Leave); return; }
                catch (Exception error) { Failed(error); }
            }
            Leave();
        }

        private void Leave()
        {
            if (left) return;
            left = true;
            try { Prepare(); }
            catch (Exception error) { Failed(error); }
            finally { Post(() => { if (!exited) Shutdown(code); }); }
        }

        internal void Exited() { exited = true; }

        internal void Clean(params Action[] steps)
        {
            foreach (Action step in steps)
            {
                try { step(); }
                catch (Exception error) { Failed(error); }
            }
        }

        internal static bool MinimizeMainframe(bool mac, bool linux, bool finishing, WindowCloseReason reason)
        {
            return !mac && !linux && !finishing && reason == WindowCloseReason.WindowClosing;
        }

        internal static bool AllowClose(bool mac, bool stopping, WindowCloseReason reason)
        {
            return mac && (stopping || reason == WindowCloseReason.ApplicationShutdown || reason == WindowCloseReason.OSShutdown);
        }

        internal static void Checks(List<string> checks)
        {
            MacQuit before = active;
            try
            {
                var calls = new List<string>();
                var posted = new Queue<Action>();
                Action kill = null;
                bool mainframe = true, sheet = true, pet = true;
                int armed = 0, killed = 0;
                var desktop = new QuitDesktop();
                var quit = new MacQuit
                {
                    Prepare = () => calls.Add("saved and listener removed"),
                    Post = posted.Enqueue,
                    After = (ms, action) => { if (ms != 5000) throw new Exception("Mac quit has the wrong deadline."); armed++; kill = action; calls.Add("armed"); },
                    Kill = () => killed++,
                    Shutdown = desktop.Shutdown
                };
                quit.Attach(desktop);
                desktop.Close.Add(() => { pet = false; quit.Request(); return true; });
                desktop.Close.Add(() => { if (MinimizeMainframe(true, false, false, WindowCloseReason.ApplicationShutdown)) return false; mainframe = sheet = false; return true; });
                desktop.Close.Add(() => AllowClose(true, quit.Stopping, WindowCloseReason.ApplicationShutdown));
                if (!desktop.TryShutdown() || !quit.Stopping || armed != 1 || posted.Count != 1 || calls.Count != 2 || calls[0] != "armed")
                    throw new Exception("A system quit was cancelled, repeated or missed its cleanup.");
                posted.Dequeue()();
                if (mainframe || sheet || pet || !desktop.Ended || desktop.Forced != 0) throw new Exception("A system quit left Mainframe open or missed shutdown.");
                checks.Add("PASS Mac system quit: cleanup runs once and shutdown closes Mainframe, its sheet and the pet");

                foreach (WindowCloseReason reason in Enum.GetValues(typeof(WindowCloseReason)))
                {
                    if (MinimizeMainframe(true, false, false, reason) || !AllowClose(true, true, reason))
                        throw new Exception("A Mac window can veto a close during quit.");
                }
                if (!AllowClose(true, false, WindowCloseReason.OSShutdown) || !AllowClose(true, false, WindowCloseReason.ApplicationShutdown)
                    || AllowClose(true, false, WindowCloseReason.WindowClosing) || AllowClose(false, true, WindowCloseReason.ApplicationShutdown)
                    || MinimizeMainframe(false, true, false, WindowCloseReason.WindowClosing)
                    || !MinimizeMainframe(false, false, false, WindowCloseReason.WindowClosing))
                    throw new Exception("Mac quit changed an ordinary editor close or another platform's close policy.");
                pet = false; mainframe = true;
                if (!MinimizeMainframe(true, false, false, WindowCloseReason.WindowClosing)) mainframe = false;
                if (pet || mainframe) throw new Exception("Mainframe cannot close after the pet has gone.");
                checks.Add("PASS Mac window close: Mainframe closes with the pet gone and no close is vetoed during quit; Linux keeps its close policy");

                kill();
                if (killed != 1) throw new Exception("Mac quit lost its deadline after the lifetime exited.");
                desktop = new QuitDesktop();
                mainframe = true;
                quit = new MacQuit
                {
                    Prepare = () => { }, Post = posted.Enqueue, After = (ms, action) => kill = action,
                    Kill = () => killed++, Shutdown = desktop.Shutdown
                };
                quit.Attach(desktop);
                desktop.Close.Add(() => { mainframe = false; return true; });
                desktop.Close.Add(() => false);
                if (desktop.TryShutdown() || posted.Count != 1) throw new Exception("A stranded window did not queue forced shutdown.");
                kill();
                if (killed != 2 || desktop.Ended) throw new Exception("The Mac quit deadline waits for the UI dispatcher.");
                posted.Dequeue()();
                if (mainframe || !desktop.Ended || desktop.Forced != 1) throw new Exception("Mac quit waited for a hidden or closing window.");
                checks.Add("PASS Mac shutdown fallback: a stranded window cannot keep the lifetime open");
                calls.Clear(); posted.Clear(); armed = 0;
                quit = new MacQuit
                {
                    Prepare = () => { throw new Exception("cleanup failed"); },
                    Failed = error => calls.Add(error.Message),
                    Post = posted.Enqueue,
                    After = (ms, action) => { armed++; kill = action; },
                    Kill = () => killed++,
                    Shutdown = code => calls.Add("shutdown " + code)
                };
                quit.Request(1);
                if (armed != 1 || posted.Count != 1 || calls[0] != "cleanup failed") throw new Exception("Failed cleanup stopped Mac quit.");
                posted.Dequeue()();
                quit.Clean(() => { throw new Exception("step failed"); }, () => calls.Add("saved"));
                if (calls[1] != "shutdown 1" || calls[3] != "saved") throw new Exception("Mac quit skipped shutdown or later cleanup after an error.");
                quit = new MacQuit { Prepare = () => { }, Post = posted.Enqueue, After = (ms, action) => { }, Shutdown = code => { throw new Exception("Shutdown ran twice."); } };
                quit.Request(); quit.Exited(); posted.Dequeue()();
                checks.Add("PASS Mac quit deadline: it stays armed after lifetime exit; failed cleanup still reaches shutdown and saving");

                calls.Clear(); posted.Clear(); armed = 0;
                Action leave = null;
                quit = new MacQuit
                {
                    Prepare = () => calls.Add("cleaned"), Post = posted.Enqueue, After = (ms, action) => armed++,
                    Kill = () => killed++, Shutdown = code => calls.Add("shutdown " + code)
                };
                quit.Request(0, then => { calls.Add("outro"); leave = then; });
                if (!quit.Stopping || armed != 1 || posted.Count != 0 || calls.Count != 1 || calls[0] != "outro") throw new Exception("Her Mac quit cleaned up before her outro played, or never armed its deadline.");
                leave(); leave();
                if (posted.Count != 1 || calls.Count != 2 || calls[1] != "cleaned") throw new Exception("Her Mac quit outro ended without one cleanup and one shutdown.");
                posted.Dequeue()();
                if (calls[2] != "shutdown 0") throw new Exception("Her Mac quit outro did not reach shutdown.");
                calls.Clear();
                quit = new MacQuit
                {
                    Prepare = () => calls.Add("cleaned"), Post = posted.Enqueue, After = (ms, action) => { },
                    Kill = () => killed++, Shutdown = code => calls.Add("shutdown " + code)
                };
                quit.Request(0, then => calls.Add("outro"));
                quit.Request(1);
                if (posted.Count != 1 || calls.Count != 2 || calls[1] != "cleaned") throw new Exception("A second quit request during her outro did not leave at once.");
                posted.Dequeue()();
                if (calls[2] != "shutdown 1") throw new Exception("A failing exit code was replaced by a clean one.");
                calls.Clear();
                quit = new MacQuit
                {
                    Prepare = () => calls.Add("cleaned"), Failed = error => calls.Add(error.Message), Post = posted.Enqueue, After = (ms, action) => { },
                    Kill = () => killed++, Shutdown = code => calls.Add("shutdown " + code)
                };
                quit.Request(0, then => { throw new Exception("outro failed"); });
                if (posted.Count != 1 || calls.Count != 2 || calls[0] != "outro failed" || calls[1] != "cleaned") throw new Exception("A failed outro stopped her Mac quit.");
                posted.Clear();
                checks.Add("PASS Mac quit outro: her own Quit plays her outro before cleanup, a second request or a failed outro leaves at once, and a failing exit code is kept");
            }
            finally { active = before; }
        }

        // A stand-in for the app's lifetime in the checks. Avalonia doesn't let app code implement its own lifetime interface.
        private sealed class QuitDesktop
        {
            internal readonly List<Func<bool>> Close = new List<Func<bool>>();
            internal bool Ended;
            internal int Forced;
            internal event EventHandler<ShutdownRequestedEventArgs> ShutdownRequested;
            internal event Action Exit;
            internal bool TryShutdown()
            {
                var request = new ShutdownRequestedEventArgs();
                ShutdownRequested?.Invoke(this, request);
                if (request.Cancel) return false;
                bool closed = true;
                foreach (Func<bool> close in Close) if (!close()) closed = false;
                if (closed) End();
                return closed;
            }
            internal void Shutdown(int code)
            {
                Forced++;
                foreach (Func<bool> close in Close) close();
                End();
            }
            private void End() { Ended = true; Exit?.Invoke(); }
        }
    }

    internal sealed partial class Pet
    {
        private MacQuit macQuit;

        internal void AttachMacQuit()
        {
            macQuit = new MacQuit { Prepare = PrepareMacQuit, Shutdown = desktop.Shutdown };
            macQuit.Attach(desktop);
            Closing += (_, _) => macQuit.Request();
            Closed += (_, _) => macQuit.Request();
        }

        private void PrepareMacQuit()
        {
            quitting = true;
            macQuit.Clean(() => { if (fileEditor != null) fileEditor.KeepDraftsNow(); }, Save, () => { if (husk != null) husk.Dispose(); }, frames.Stop, ClonesOff,
                StopAwake, ModList.StopCode, ComputerJobs.Stop, SelfJobs.Stop, TerminalJobs.Shutdown,
                () => ownedVoice?.Cancel(), () => StopNativeListening(), Brain.Cancel, voice.Stop,
                () => { if (presence != null) presence.Dispose(); }, () => Talk.Dismiss());
        }
    }
}

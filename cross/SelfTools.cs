using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Avalonia.Threading;

namespace CapriDesktop
{
    internal sealed class SelfTools
    {
        private sealed class Later
        {
            internal Action Run;
            internal int Ticket;
            internal bool Shown;
            internal Func<bool> Current;
        }

        private readonly List<Later> later = new List<Later>();

        internal void After(Action run, int ticket, Func<bool> current)
        {
            later.Add(new Later { Run = run, Ticket = ticket, Current = current });
        }

        internal void Replied(int ticket)
        {
            foreach (Later item in later) if (item.Ticket == ticket) item.Shown = true;
        }

        internal bool Pending(int ticket)
        {
            return later.Exists(item => item.Ticket == ticket && (item.Current == null || item.Current()));
        }

        internal void Tick(int ticket, bool finished)
        {
            foreach (Later item in later.ToArray())
            {
                if (item.Ticket != ticket || item.Current != null && !item.Current()) { later.Remove(item); continue; }
                if (!item.Shown || !finished) continue;
                later.Remove(item);
                item.Run();
            }
        }

        internal static bool InstallAsked(ToolArguments args)
        {
            if (args.FromFriend || args.FromAgent || string.IsNullOrWhiteSpace(args.Asked)) return false;
            string text = args.Asked.Trim();
            return Regex.IsMatch(text, @"\A(?:hey[, ]+)?(?:capri[,!: ]+)?(?:please )?(?:(?:can|could|would|will) you )?(?:update (?:capri|yourself)|install (?:the |your |a )?(?:(?:latest|new) )?(?:capri )?update(?: for capri)?|get (?:the )?(?:latest|new) (?:version of capri|capri update))(?: (?:now|please|to (?:the )?latest version))?[.!?]*\z", RegexOptions.IgnoreCase);
        }

        internal static ToolArguments JobRequest(string text)
        {
            text = (text ?? "").Trim();
            if (Regex.IsMatch(text, @"\A(?:please )?(?:(?:can|could|would) you )?(?:stop|cancel) (?:the |your |my )?(?:(?:running|current|terminal) )?job(?: please)?[.!?]*\z", RegexOptions.IgnoreCase)) return new ToolArguments { Action = "stop" };
            if (Regex.IsMatch(text, @"\A(?:job status|what(?:'s| is) (?:the |your )?job status|how(?:'s| is) (?:the |your )?job going)[.!?]*\z", RegexOptions.IgnoreCase)) return new ToolArguments { Action = "status" };
            Match change = Regex.Match(text, @"\A(?:please )?(?:(?:can|could|would) you )?change (?:the |your |my )?(?:running |current )?job to (.+)\z", RegexOptions.IgnoreCase);
            return change.Success ? new ToolArguments { Action = "change", Task = change.Groups[1].Value } : null;
        }

    }

    internal static class SelfJobs
    {
        private sealed class Running
        {
            internal ToolArguments Args;
            internal Action Cancel;
            internal bool Stopped;
            internal long Sequence = long.MinValue;
            internal int Revision;
        }

        private static readonly object gate = new object();
        private static Running running;
        [ThreadStatic] private static Running local;
        [ThreadStatic] private static int revision;

        internal static bool Active { get { lock (gate) return running != null && !running.Stopped; } }

        internal static bool Current()
        {
            lock (gate) return local == null || !local.Stopped && local.Revision == revision;
        }

        internal static void Stop()
        {
            lock (gate) if (running != null) { running.Stopped = true; running.Cancel(); }
        }

        internal static string Run(ToolArguments args, Func<ToolArguments, string> work, Action cancel)
        {
            Running job = new Running { Args = args.Copy(), Cancel = cancel };
            lock (gate)
            {
                var wait = System.Diagnostics.Stopwatch.StartNew();
                while (running != null && running.Stopped && wait.ElapsedMilliseconds < 1000)
                    Monitor.Wait(gate, (int)Math.Max(1, 1000 - wait.ElapsedMilliseconds));
                if (running != null) return "I'm already on a job.";
                running = job;
            }
            local = job;
            try
            {
                while (true)
                {
                    ToolArguments next;
                    lock (gate)
                    {
                        if (job.Stopped) return "Stopped my job.";
                        revision = job.Revision;
                        next = job.Args.Copy();
                    }
                    if (next.Current != null && !next.Current()) return "That job has ended.";
                    string result = work(next);
                    lock (gate)
                    {
                        if (job.Stopped) return "Stopped my job.";
                        if (revision == job.Revision) return result;
                    }
                }
            }
            finally { local = null; lock (gate) if (running == job) { running = null; Monitor.PulseAll(gate); } }
        }

        internal static string Control(ToolArguments args)
        {
            lock (gate)
            {
                if (running == null) return TerminalJobs.SelfControl(args);
                if (args.Current != null && !args.Current()) return "That job has ended.";
                if (args.Action == "status") return running.Stopped ? "Stopping my job." : "I'm working on " + Voice.Short(running.Args.Task) + ".";
                if (args.Action == "stop") { running.Stopped = true; running.Cancel(); return "Stopped my job."; }
                if (args.Sequence.HasValue && args.Sequence.Value <= running.Sequence) return "Changed what my job should do.";
                running.Sequence = args.Sequence ?? (running.Sequence == long.MinValue ? 0 : running.Sequence + 1);
                running.Args.Task = args.Task;
                running.Args.Asked = args.Task;
                running.Revision++;
                running.Cancel();
                return "Changed what my job should do.";
            }
        }
    }

    internal static partial class ComputerJobs
    {
        private static string Run(ComputerJob job, long? admitted = null)
        {
            return SelfJobs.Run(new ToolArguments { Task = job.Task, Asked = job.Asked, Current = job.Current }, args =>
            {
                if (args.Task != job.Task) job = new ComputerJob { Task = args.Task, Asked = args.Asked, Current = args.Current };
                return RunOnce(job, admitted);
            }, CancelSelf);
        }

        private static void CancelSelf()
        {
            lock (gate) { pending = null; running?.Cancel(); }
        }
    }

    internal static partial class TerminalJobs
    {
        internal static string SelfControl(ToolArguments args)
        {
            if (args.FromFriend || args.FromAgent) return "Only your own request can control a terminal job.";
            if (args.Current != null && !args.Current()) return "That job has ended.";
            lock (gate)
            {
                Job[] active = jobs.Values.Where(Active).OrderByDescending(job => job.Started).ToArray();
                if (active.Length == 0) return "I'm not on a job right now.";
                if (active.Length != 1) return "More than one terminal job is running. Use terminal_job with its job ID.";
                if (args.Action == "status") return Read(active[0], 0);
                if (args.Action == "change") return "A running terminal command can't be changed. Stop it and ask me to run the new command.";
                Stop(active[0]);
                return "Stopping my terminal job.";
            }
        }
    }

    internal sealed partial class Pet
    {
        internal static void SelfChecks(Pet pet, List<string> checks)
        {
            var queued = new SelfTools();
            bool closed = false, current = true;
            queued.After(() => closed = true, 1, () => current);
            queued.Tick(1, true);
            if (closed) throw new Exception("Quit ran before its reply was shown.");
            queued.Replied(1);
            queued.Tick(1, false);
            if (closed) throw new Exception("Quit interrupted its spoken reply.");
            queued.Tick(1, true);
            if (!closed) throw new Exception("Quit did not close its fake window after the reply.");
            closed = false;
            queued.After(() => closed = true, 1, () => current);
            queued.Replied(1); current = false; queued.Tick(1, true);
            if (closed) throw new Exception("A cancelled reply still quit.");
            current = true;
            queued.After(() => closed = true, 1, null);
            queued.Replied(1); queued.Tick(2, true);
            if (closed) throw new Exception("An old reply quit a newer turn.");
            checks.Add("PASS self quit waits for its own finished reply and drops cancelled or replaced replies");

            Action silence = Voice.Silence;
            bool silent = pet.Saved.Silent;
            string provider = pet.Saved.VoiceProvider;
            int stopped = 0;
            try
            {
                Voice.Silence = () => stopped++;
                pet.Saved.Silent = false;
                string said = Hands.Do("speech", new ToolArguments { Action = "stop" });
                if (said != "Stopped talking." || stopped != 1 || pet.Saved.Silent || pet.Saved.VoiceProvider != provider) throw new Exception("Speech did not stop its fake player while keeping voice enabled.");
            }
            finally { Voice.Silence = silence; pet.Saved.Silent = silent; }
            checks.Add("PASS self speech stops fake playback and keeps the saved voice enabled");

            Action closeWas = pet.SelfClose;
            Action<string> browseWas = pet.SelfBrowse;
            Func<string> updateWas = pet.ReadUpdate;
            Action<string, string[]> launchWas = Limbs.Launch;
            string news = pet.UpdateNews, seen = pet.Saved.UpdateSeen;
            string note = pet.updateNote, page = pet.updatePage;
            var item = pet.updateItem;
            int at = item == null ? -1 : pet.ContextMenu.Items.IndexOf(item);
            try
            {
                int opens = 0;
                pet.SelfClose = () => closed = true;
                pet.SelfBrowse = url => { if (url != pet.UpdatePage) throw new Exception("Install used a different update page from the menu."); opens++; };
                foreach (string tag in new[] { "99.1.0", System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(), "0.0.1" })
                {
                    pet.ShowUpdate("{\"tag_name\":\"v" + tag + "\"}", true);
                    string status = Hands.Do("updates", new ToolArguments { Action = "status" });
                    if (tag == "99.1.0" ? !status.Contains("99.1.0") : !status.Contains("latest version")) throw new Exception("Update status disagreed with its fake release: " + tag);
                }
                pet.ShowUpdate("{\"tag_name\":\"v99.1.0\"}", true);
                foreach (string hint in new[] { "there might be an update", "can you check for Capri updates?", "don't update Capri", "if there's an update install it", "is Capri up to date?" })
                    if (!Hands.Do("updates", new ToolArguments { Action = "install", Asked = hint }).Contains("clearly")) throw new Exception("An update hint authorized installation.");
                if (opens != 0) throw new Exception("Update status or a hint opened a page.");
                foreach (string ask in new[] { "Update Capri", "please update yourself", "can you install the Capri update?" })
                    if (!SelfTools.InstallAsked(new ToolArguments { Asked = ask })) throw new Exception("A clear update request was refused.");
                if (SelfTools.InstallAsked(new ToolArguments { Asked = "Update Capri", FromAgent = true }) || SelfTools.InstallAsked(new ToolArguments { Asked = "Update Capri", FromFriend = true })) throw new Exception("An outside update request was accepted.");
                int ticket = pet.asking;
                closed = false;
                Hands.Do("quit", new ToolArguments { Action = "quit" });
                pet.selfTools.Tick(ticket, true);
                if (closed) throw new Exception("The quit seam skipped its reply.");
                pet.selfTools.Replied(ticket);
                pet.selfTools.Tick(ticket, false);
                if (closed) throw new Exception("The quit seam skipped speech.");
                pet.selfTools.Tick(ticket, true);
                if (!closed) throw new Exception("The quit seam did not close its fake window.");
                int newsTicketWas = pet.newsTicket;
                long newsQuietWas = pet.newsQuiet;
                bool newsPendingWas = pet.newsPending;
                string versionWas = pet.Saved.LastVersion;
                Func<string, string> newsThinkWas = pet.Think;
                string problemWas = pet.frameProblem;
                try
                {
                    closed = false;
                    pet.newsTicket = ticket; pet.newsQuiet = -1;
                    Hands.Do("quit", new ToolArguments { Action = "quit" });
                    if (pet.HoldNews(ticket) || pet.asking != ticket) throw new Exception("Held news discarded an agreed quit.");
                    if (!pet.ShowReply(ticket, "(updated)", "I'll quit after this reply.", 0, null, false, pet.inputTurn, pet.answerTurn)) throw new Exception("A held quit reply was not shown.");
                    pet.selfTools.Tick(ticket, false);
                    if (closed) throw new Exception("A held quit skipped its line.");
                    pet.selfTools.Tick(ticket, true);
                    if (!closed) throw new Exception("A held reply lost its agreed quit.");
                    closed = false;
                    pet.Talk.Dismiss();
                    pet.newsTicket = ticket; pet.newsQuiet = -1;
                    pet.Think = delegate
                    {
                        Hands.Do("quit", new ToolArguments { Action = "quit" });
                        throw new BrainException("Test reply failure.");
                    };
                    pet.Ask(ticket, "(updated)", 0);
                    var replyWait = System.Diagnostics.Stopwatch.StartNew();
                    while (pet.Talk.Reply == null && replyWait.ElapsedMilliseconds < 3000) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
                    if (pet.Talk.Reply != News.Line || pet.asking != ticket) throw new Exception("A held fallback reply lost its quit line.");
                    pet.selfTools.Tick(ticket, false);
                    if (closed) throw new Exception("A fallback quit skipped its line.");
                    pet.selfTools.Tick(ticket, true);
                    if (!closed) throw new Exception("A fallback reply never released its agreed quit.");
                }
                finally
                {
                    pet.newsTicket = newsTicketWas; pet.newsQuiet = newsQuietWas;
                    pet.newsPending = newsPendingWas; pet.Saved.LastVersion = versionWas;
                    pet.Think = newsThinkWas; pet.frameProblem = problemWas;
                    pet.Talk.Dismiss();
                }
                Hands.Do("updates", new ToolArguments { Action = "install", Asked = "Update Capri" });
                pet.selfTools.Tick(ticket, true);
                if (opens != 0) throw new Exception("Install opened before its reply.");
                pet.selfTools.Replied(ticket); pet.selfTools.Tick(ticket, false);
                if (opens != 0) throw new Exception("Install interrupted speech.");
                pet.selfTools.Tick(ticket, true);
                if (opens != 1) throw new Exception("Install did not open the menu's page once.");
                pet.ReadUpdate = () => "{\"tag_name\":\"v99.4.0\"}";
                string checking = Hands.Do("updates", new ToolArguments { Action = "check" });
                if (checking != "I'm checking for an update." || !pet.updateChecking || opens != 1) throw new Exception("Checking installed an update or did not use the existing checker.");
                var wait = System.Diagnostics.Stopwatch.StartNew();
                while (pet.updateChecking && wait.ElapsedMilliseconds < 3000) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
                if (pet.updateChecking || !Hands.Do("updates", new ToolArguments { Action = "status" }).Contains("99.4.0") || opens != 1) throw new Exception("The fake update check did not report its release without opening it.");
                string opened = null;
                Limbs.Launch = (tool, args) => { if (tool != (OperatingSystem.IsMacOS() ? "open" : "xdg-open") || args.Length != 1) throw new Exception("Check-in editing used the wrong opener."); opened = args[0]; };
                string edit = Hands.Do("capri_settings", new ToolArguments { Action = "edit_check_in_lines" });
                if (opened == null || !opened.EndsWith("check-ins.md", StringComparison.Ordinal) || edit != "Opened your check-in lines.") throw new Exception("The settings tool did not open the existing check-in lines.");
                checks.Add("PASS self updates report newer, same and older releases; hints cannot install; a clear ask opens the menu page after the reply");
                checks.Add("PASS self settings edit_check_in_lines opens the existing editor on Mac and Linux");
                checks.Add("PASS self quit survives held news and fallback replies, shows its line and waits for it to finish");
            }
            finally
            {
                pet.SelfClose = closeWas; pet.SelfBrowse = browseWas; pet.ReadUpdate = updateWas; Limbs.Launch = launchWas;
                if (pet.updateItem != null) pet.ContextMenu.Items.Remove(pet.updateItem);
                pet.updateItem = item;
                if (at >= 0) pet.ContextMenu.Items.Insert(Math.Min(at, pet.ContextMenu.Items.Count), item);
                pet.updateNote = note; pet.updatePage = page; pet.UpdateNews = news; pet.Saved.UpdateSeen = seen;
            }

            if (SelfJobs.Control(new ToolArguments { Action = "status" }) != "I'm not on a job right now." || SelfJobs.Control(new ToolArguments { Action = "change", Task = "new task" }) != "I'm not on a job right now.") throw new Exception("Job control invented a running job.");
            int runs = 0, cancels = 0;
            string result = SelfJobs.Run(new ToolArguments { Task = "first" }, args =>
            {
                runs++;
                if (runs == 1)
                {
                    if (!SelfJobs.Control(new ToolArguments { Action = "status" }).Contains("first")) throw new Exception("Job status lost its task.");
                    if (Hands.Do("job_control", new ToolArguments { Action = "change", Task = "second", Sequence = 2 }) != "Changed what my job should do.") throw new Exception("Job change did not dispatch.");
                    SelfJobs.Control(new ToolArguments { Action = "change", Task = "stale", Sequence = 1 });
                    if (SelfJobs.Current()) throw new Exception("A changed job still accepted the old action.");
                }
                else
                {
                    if (args.Task != "second" || args.Asked != "second" || !SelfJobs.Current()) throw new Exception("A correction was lost or kept the old request.");
                    if (Hands.Do("job_control", new ToolArguments { Action = "stop" }) != "Stopped my job." || SelfJobs.Current()) throw new Exception("Job stop left the old action current.");
                }
                return "fake job";
            }, () => cancels++);
            if (runs != 2 || cancels != 2 || result != "Stopped my job." || SelfJobs.Control(new ToolArguments { Action = "status" }) != "I'm not on a job right now.") throw new Exception("Job control did not change and stop its fake job.");
            ToolArguments correction = SelfTools.JobRequest("change the job to Use Firefox instead");
            if (correction == null || correction.Action != "change" || correction.Task != "Use Firefox instead" || SelfTools.JobRequest("how is the job going?").Action != "status" || SelfTools.JobRequest("stop your job").Action != "stop" || SelfTools.JobRequest("don't stop your job") != null) throw new Exception("A job request was lost or a negation became a stop.");
            checks.Add("PASS self jobs report no job, report a running task, apply ordered corrections and stop before another action");

            Func<string, string> thinkWas = pet.Think;
            silent = pet.Saved.Silent;
            try
            {
                pet.Saved.Silent = true;
                pet.Think = delegate
                {
                    if (!Hands.Do("job_control", new ToolArguments { Action = "status" }).Contains("chat job")) throw new Exception("Chat stopped a job before its status tool.");
                    return Hands.Do("job_control", new ToolArguments { Action = "change", Task = "changed by chat" });
                };
                int changed = 0;
                result = SelfJobs.Run(new ToolArguments { Task = "chat job" }, args =>
                {
                    if (++changed == 1)
                    {
                        pet.Talk.Dismiss();
                        pet.Hear("Could you carry on using Firefox instead?");
                        var wait = System.Diagnostics.Stopwatch.StartNew();
                        while (pet.Talk.Reply == null && wait.ElapsedMilliseconds < 3000) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
                        if (pet.Talk.Reply != "Changed what my job should do." || !SelfJobs.Active) throw new Exception("An ordinary chat lost its running job before control.");
                    }
                    else
                    {
                        if (args.Task != "changed by chat") throw new Exception("Chat did not change the running job.");
                        pet.Hear("stop");
                    }
                    return "fake job";
                }, delegate { });
                if (changed != 2 || result != "Stopped my job." || SelfJobs.Active) throw new Exception("A plain stop did not end the chat-controlled job.");
            }
            finally { pet.Think = thinkWas; pet.Saved.Silent = silent; pet.Talk.Dismiss(); SelfJobs.Stop(); }
            checks.Add("PASS self jobs survive ordinary chat for status and change, and a plain stop ends them");

            using (var entered = new ManualResetEvent(false))
            using (var cancelled = new ManualResetEvent(false))
            {
                string oldResult = null;
                Exception problem = null;
                Thread old = new Thread(delegate()
                {
                    try
                    {
                        oldResult = SelfJobs.Run(new ToolArguments { Task = "old job" }, args =>
                        {
                            entered.Set();
                            if (!cancelled.WaitOne(3000)) throw new Exception("The old job was never stopped.");
                            Thread.Sleep(100);
                            return "old result";
                        }, () => cancelled.Set());
                    }
                    catch (Exception error) { problem = error; }
                }) { IsBackground = true };
                old.Start();
                try
                {
                    if (!entered.WaitOne(3000)) throw new Exception("The fake old job never started.");
                    ComputerJobs.Stop();
                    result = SelfJobs.Run(new ToolArguments { Task = "new job" }, args => "new result", delegate { });
                    if (result != "new result") throw new Exception("A stopped job refused its immediate replacement.");
                }
                finally { cancelled.Set(); SelfJobs.Stop(); if (!old.Join(3000)) throw new Exception("The fake old job never unwound."); }
                if (problem != null) throw problem;
                if (oldResult != "Stopped my job." || SelfJobs.Active) throw new Exception("A stopped job survived its replacement.");
            }
            checks.Add("PASS self jobs stop through ComputerJobs and admit a replacement after the old job unwinds");

            var fields = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
            var terminals = (System.Collections.IDictionary)typeof(TerminalJobs).GetField("jobs", fields).GetValue(null);
            Type terminal = typeof(TerminalJobs).GetNestedType("Job", System.Reflection.BindingFlags.NonPublic);
            var instance = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            object fake = Activator.CreateInstance(terminal, true);
            string id = Guid.NewGuid().ToString("N"), home = System.IO.Path.Combine(Program.Smoke, "self-terminal-" + id);
            System.IO.Directory.CreateDirectory(home);
            terminal.GetField("Id", instance).SetValue(fake, id);
            terminal.GetField("Directory", instance).SetValue(fake, home);
            terminal.GetField("Cwd", instance).SetValue(fake, home);
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(home, "status.json"), "{\"state\":\"running\",\"output\":\"fake\",\"total_bytes\":4,\"exit_code\":null,\"truncated\":false,\"interactive\":false,\"error\":null,\"stop_reason\":null}");
                terminals.Add(id, fake);
                if (!Hands.Do("job_control", new ToolArguments { Action = "status" }).Contains(id)) throw new Exception("Job control did not find a fake terminal job.");
                string refused = Hands.Do("job_control", new ToolArguments { Action = "change", Task = "another command" });
                if (!refused.Contains("can't be changed") || System.IO.File.Exists(System.IO.Path.Combine(home, "stop"))) throw new Exception("A terminal correction silently changed or stopped a command.");
                if (Hands.Do("job_control", new ToolArguments { Action = "stop" }) != "Stopping my terminal job." || System.IO.File.ReadAllText(System.IO.Path.Combine(home, "stop")) != "stop\n") throw new Exception("Job control did not request a fake terminal stop.");
                System.IO.File.WriteAllText(System.IO.Path.Combine(home, "status.json"), "{\"state\":\"exited\"}");
                if (Hands.Do("job_control", new ToolArguments { Action = "status" }) != "I'm not on a job right now.") throw new Exception("An ended terminal job was reported as running.");
            }
            finally { terminals.Remove(id); System.IO.Directory.Delete(home, true); }
            checks.Add("PASS self terminal jobs read a fake receipt, request an owned stop, refuse command rewriting and stop reporting ended jobs");

            bool mac = Hands.OnMac;
            try
            {
                foreach (bool platform in new[] { false, true })
                {
                    Hands.OnMac = platform;
                    var kit = Hands.KitFor();
                    foreach (string name in new[] { "speech", "quit", "updates", "job_control" })
                        if (!kit.Has(name)) throw new Exception("A self tool is still missing on " + (platform ? "Mac" : "Linux") + ": " + name);
                    if (!kit.Has("capri_settings:edit_check_in_lines")) throw new Exception("Check-in editing is still missing.");
                    foreach (string name in Hands.SettingNames())
                        if (!Hands.CrossSetting(name) && pet.SettingOn(name).HasValue || !Hands.CrossSetting(name) && pet.LevelOf(name) >= 0) throw new Exception("An implemented cross setting is still refused: " + name);
                }
            }
            finally { Hands.OnMac = mac; }
            checks.Add("PASS self tool and settings missing lists agree on Mac and Linux");
        }

        internal readonly SelfTools selfTools = new SelfTools();
        internal Action SelfClose;
        internal Action<string> SelfBrowse = Browse;
        private bool selfUpdate;
        private int selfUpdateTicket;
        private Func<bool> selfUpdateCurrent;

        private void AttachSelfTools()
        {
            SelfClose = Quit;
            Hands.StopSpeech = SelfSpeech;
            Hands.Quit = SelfQuit;
            Hands.Updates = SelfUpdate;
            Hands.JobControl = SelfJobs.Control;
        }

        private string SelfSpeech()
        {
            if (!Dispatcher.UIThread.CheckAccess()) return Dispatcher.UIThread.Invoke(SelfSpeech);
            CancelSpeech();
            return "Stopped talking.";
        }

        private bool SelfJobRequest(string text)
        {
            ToolArguments args = SelfTools.JobRequest(text);
            if (args == null) return false;
            args.Asked = text;
            string said = SelfJobs.Control(args);
            Reply(asking, text, said);
            return true;
        }

        private string SelfQuit(ToolArguments args)
        {
            if (!Dispatcher.UIThread.CheckAccess()) return Dispatcher.UIThread.Invoke(() => SelfQuit(args));
            selfTools.After(() => SelfClose(), asking, args.Current);
            return "I'll quit after this reply.";
        }

        private string SelfUpdate(ToolArguments args)
        {
            if (!Dispatcher.UIThread.CheckAccess()) return Dispatcher.UIThread.Invoke(() => SelfUpdate(args));
            if (args.Action == "status") return string.IsNullOrEmpty(MainframeUpdate) ? "I haven't checked for updates yet." : MainframeUpdate;
            if (args.Action == "check") { CheckUpdate(true); return "I'm checking for an update."; }
            if (!SelfTools.InstallAsked(args)) return "Ask me clearly to update Capri first.";
            selfTools.After(delegate
            {
                if (updatePage != null) SelfBrowse(updatePage);
                else
                {
                    selfUpdate = true; selfUpdateTicket = asking; selfUpdateCurrent = args.Current;
                    CheckUpdate(true);
                }
            }, asking, args.Current);
            return "I'll open the Capri release page after this reply.";
        }

        internal void CheckSelfTools()
        {
            selfTools.Tick(asking, Talk.Finished && !Listening);
            if (!selfUpdate) return;
            if (selfUpdateTicket != asking || selfUpdateCurrent != null && !selfUpdateCurrent()) { selfUpdate = false; return; }
            if (updateChecking) return;
            selfUpdate = false;
            if (updatePage != null) SelfBrowse(updatePage);
        }

        private string EditCheckInLines()
        {
            if (!careLines.Create()) return "Couldn't open check-ins.md.";
            Limbs.Launch(OperatingSystem.IsMacOS() ? "open" : "xdg-open", new[] { System.IO.Path.Combine(Program.Smoke ?? folder, "check-ins.md") });
            return "Opened your check-in lines.";
        }
    }
}

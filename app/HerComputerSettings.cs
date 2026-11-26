using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Preferences
    {
        [DataMember] public bool HerComputer;
    }

    internal sealed partial class Companion
    {
        private HerComputerWindow herComputer;

        internal bool HerComputerOn { get { return preferences.HerComputer; } }

        private string HerComputerFolder { get { return Path.Combine(Path.GetDirectoryName(settingsPath), "her-computer"); } }

        internal void ToggleHerComputer()
        {
            preferences.HerComputer = !preferences.HerComputer;
            if (!preferences.HerComputer) CloseHerComputer();
            SavePreferences();
            mainframe.Sync();
        }

        internal void OpenHerComputer()
        {
            try { ShowHerComputer(); }
            catch (Exception error) { Notice.Tell(mainframe, error.Message, "Her computer", true); }
        }

        private HerComputerWindow ShowHerComputer() { return ShowHerComputer(true); }

        private HerComputerWindow ShowHerComputer(bool activate)
        {
            MakeHerComputer();
            if (!activate)
            {
                if (!herComputer.Visible) herComputer.ShowQuietly();
                return herComputer;
            }
            if (!herComputer.Visible) herComputer.Show();
            if (herComputer.WindowState == FormWindowState.Minimized) herComputer.WindowState = FormWindowState.Normal;
            herComputer.Activate();
            return herComputer;
        }

        private void ShowCapricordForJoin()
        {
            if (!preferences.HerComputer || IsDisposed) return;
            try { ShowHerComputer(false).ShowCapricord(); }
            catch (Exception error) { Brain.Log("her computer", -1, "Capricord view: " + error.Message); }
        }

        private void MakeHerComputer()
        {
            if (herComputer != null && !herComputer.IsDisposed) return;
            herComputer = new HerComputerWindow(new HerComputer(HerComputerFolder));
            herComputer.Ask = delegate(string system, string user) { return brain.Aside(system, user, 700, true); };
            herComputer.CapricordSnapshot = delegate { return discordCompanion == null ? null : discordCompanion.Snapshot(); };
            if (DiscordCompanion.Included)
            {
                herComputer.CapricordJoin = delegate { JoinLinkedDiscord("join my Discord call"); };
                herComputer.CapricordSetup = mainframe.OpenDiscordBot;
                herComputer.CapricordControl = delegate(string session, bool leave) { DiscordBot.CapricordControl(session, leave); };
            }
            herComputer.CanThink = delegate { return brain.Model != null && !Brain.Off; };
            herComputer.Tell = delegate(string line) { Post(delegate { SayOwn("her computer", line); }); };
            herComputer.HandOff = delegate(HerJob done) { Post(delegate { HerComputerHandOff(done); }); };
            herComputer.Finished = delegate(HerJob done) { Post(delegate { HerErrandDone(done); }); };
            herComputer.CheckedIn = HerErrandCheckIn;
            herComputer.Asked = delegate(HerJob asking) { Post(delegate { HerErrandAsked(asking); }); };
            herComputer.StateChanged = delegate(HerJob job, string state) { Post(delegate { RemoteErrand(job, state); }); };
            ErrandOpenNote = delegate(string note) { if (!preferences.HerComputer) return; HerComputerWindow window = ShowHerComputer(); window.ShowNote(note); };
            ErrandWatch = delegate(string task) { if (!preferences.HerComputer) return; HerComputerWindow window = ShowHerComputer(false); string said = window.Watch(task); if (said != null) Remark("", said, InChat || Hushed || FullScreen()); };
            if (mainframe.Icon != null) herComputer.Icon = mainframe.Icon;
        }

        internal const int ErrandCheckInGap = 10;

        private void HerErrandDone(HerJob done)
        {
            if (IsDisposed || quitting || done == null) return;
            string answer = (done.Answer ?? "").Trim();
            if (answer.Length == 0) return;
            if (RemoteErrandNews("done", HerComputerWindow.FirstLine(answer), done.Id)) Brain.Log("her computer", 0, "errand done while away, told the phone");
            DeliverErrand(done.Id, done.Task, answer, done.Note, done.Suggest);
        }

        private bool HerErrandCheckIn(HerJob job, string line)
        {
            if (job == null || job.Updates.Count >= HerJobs.MostUpdates) return false;
            if (job.Updates.Count > 0 && (DateTime.Now - HerJobs.When(job.Updates[job.Updates.Count - 1].At)).TotalMinutes < ErrandCheckInGap) return false;
            Post(delegate
            {
                if (IsDisposed || quitting) return;
                if (preferences.CheckIns && !Hushed && RemoteErrandNews("checkin", line, job.Id)) return;
                HerComputerWindow window = herComputer;
                if (preferences.CheckIns && !Hushed && (window == null || window.IsDisposed || !window.Visible || window.WindowState == FormWindowState.Minimized)) Remark("", line, InChat || Hushed || FullScreen());
            });
            return true;
        }

        private void HerErrandAsked(HerJob asking)
        {
            if (IsDisposed || quitting || asking == null || string.IsNullOrEmpty(asking.Question)) return;
            HerComputerWindow window = herComputer;
            if (window != null && !window.IsDisposed && window.Visible && window.WindowState != FormWindowState.Minimized) return;
            Remark("", asking.Question, InChat || Hushed || FullScreen());
        }

        private void HerComputerBackground()
        {
            if (!preferences.HerComputer || (herComputer != null && !herComputer.IsDisposed)) return;
            try
            {
                HerJobs saved = new HerJobs(HerComputerFolder);
                if (saved.Waiting == 0 && saved.Repeating().Count == 0) return;
                MakeHerComputer();
                herComputer.RunHidden();
            }
            catch (Exception error) { Brain.Log("her computer", -1, "background start failed: " + error.Message); }
        }

        private void CloseHerComputer()
        {
            if (herComputer != null && !herComputer.IsDisposed) herComputer.CloseForGood();
            herComputer = null;
            ForgetErrands();
        }

        internal static string HerComputerJob(string said)
        {
            string text = (said ?? "").Trim();
            Match on = Regex.Match(text, @"\s*,?\s*\b(?:on|using|use|with|from|in) (?:your|ur) (?:own |little )?(?:computer|pc|laptop)\b[.!?]*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!on.Success) return null;
            string job = (text.Substring(0, on.Index) + " " + text.Substring(on.Index + on.Length)).Trim().TrimStart(',', ':', ';', '-').Trim();
            job = Regex.Replace(job, @"^(?:hey\s+)?capri[,!.]?\s*", "", RegexOptions.IgnoreCase).Trim();
            job = Regex.Replace(job, @"^(?:(?:can|could|would|will) you\s+|please\s+|go\s+|and\s+|to\s+)+", "", RegexOptions.IgnoreCase).Trim().TrimEnd('?', '.', '!', ',').Trim();
            if (Regex.IsMatch(job, @"^(?:(?:what(?:'s| is| are)?|how(?:'s| is)?)(?: you| it)?(?: doing| going| up to)?(?: there)?|anything|something|stuff|things)$", RegexOptions.IgnoreCase)) return "";
            return job;
        }

        internal static string HerErrandJob(string said)
        {
            string text = Regex.Replace((said ?? "").Trim(), @"\s+", " ");
            text = Regex.Replace(text, @"^(?:hey\s+)?capri[,!.]?\s*", "", RegexOptions.IgnoreCase).Trim();
            const RegexOptions options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
            Match errand = Regex.Match(text, @"^(?:(?:can|could|would|will) you\s+|please\s+)*(?:(?:go\s+)?(?:run|do)\s+(?:me\s+)?an errand|errand)\s*(?:for me)?\s*[:,-]?\s*(?:to\s+)?(.{3,})$", options);
            if (errand.Success) return Tidy(errand.Groups[1].Value);
            Match background = Regex.Match(text, @"^(.{3,}?)\s*,?\s*(?:in the background|while i'?m (?:away|out|gone|asleep|at work|at school|busy|sleeping|offline)|while i (?:sleep|work|game|play))[.!?]*$", options);
            if (background.Success && !Regex.IsMatch(background.Groups[1].Value, @"^(?:(?:can|could|would|will) you\s+|please\s+|go\s+)*(?:play|open|launch|start|run|listen|put on|stream|record|keep|leave|have)\b", options)) return Tidy(background.Groups[1].Value);
            if (Regex.IsMatch(text, @"\bkeep an eye on\b", options)) return Tidy(text);
            if (Regex.IsMatch(text, @"\b(?:let me know|tell me|ping me|notify me|text me)\s+(?:when|if|once|as soon as)\b.*\b(?:drops?|on sale|sale|back in stock|restocks?|in stock|comes? out|releases?|released|announced|goes live|drops|price|cheaper|available|tickets|results?|score)\b", options)) return Tidy(text);
            return null;
        }

        private static string Tidy(string job)
        {
            job = Regex.Replace(job ?? "", @"^(?:(?:can|could|would|will) you\s+|please\s+|go\s+|and\s+|to\s+)+", "", RegexOptions.IgnoreCase).Trim().TrimEnd('?', '.', '!', ',').Trim();
            return job.Length < 3 ? null : job;
        }

        internal static bool HerComputerOpenAsked(string said)
        {
            return Regex.IsMatch((said ?? "").Trim(), @"\A(?:(?:hey\s+)?capri[,!.]?\s*)?(?:(?:can|could|would) you\s+)?(?:open|show(?: me)?|let me see|bring up)\s+(?:your|ur)\s+(?:own\s+|little\s+)?(?:computer|pc)(?: please)?[.!?]*\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private string herHandOffAsk;

        private string HerComputerAsk(string input)
        {
            if (input != null && herHandOffAsk != null && input.Trim() == herHandOffAsk) { herHandOffAsk = null; return null; }
            string errand = HerComputerJob(input) == null ? HerErrandJob(input) : null;
            if (!preferences.HerComputer) return errand == null || !Regex.IsMatch(input ?? "", @"\berrand\b", RegexOptions.IgnoreCase) ? null : "I can run that in the background on my own computer. Turn on Her own computer in Mainframe under HER COMPUTER, then ask me again.";
            if (errand != null)
            {
                HerComputerWindow running = ShowHerComputer(false);
                return running.Start(errand, "") ?? "On it. I'll work on that on my computer and come find you when it's done.";
            }
            if (HerComputerOpenAsked(input)) { OpenHerComputer(); return "Opened my computer."; }
            if (Regex.IsMatch(input ?? "", @"\bstop\b.{0,30}\b(?:your|ur) (?:own |little )?(?:computer|pc)\b", RegexOptions.IgnoreCase) && herComputer != null && herComputer.Busy) { herComputer.StopTask(); return "Ok, I'll stop what I'm doing on my computer."; }
            string job = HerComputerJob(input);
            if (job == null) return null;
            HerComputerWindow window = ShowHerComputer();
            if (job.Length == 0) return window.Busy ? "I'm busy on my computer: " + window.Doing + "." : "Just pottering about on my computer. Want me to do something on it?";
            string after;
            job = HerComputerSplit(job, out after);
            string problem = window.Start(job, after);
            if (problem == null && after.Length > 0) return "Looking it up on my computer first, then I'll " + HerComputer.Short(after, 80) + " on yours.";
            return problem ?? "On it, on my computer.";
        }

        internal static string HerComputerSplit(string job, out string after)
        {
            after = "";
            string text = Regex.Replace(Regex.Replace((job ?? "").Trim(), @"\s+", " "), @" ,", ",");
            Match then = Regex.Match(text, @"(?:\s*,\s*|\s+)(?:and\s+)?then\s+((?:go\s+|and\s+)?(?:load(?:\s+it)?\s+up|load|launch|open|start(?:\s+up)?|boot(?:\s+it)?\s+up|hop\s+on|jump\s+on|get\s+on|play|go\s+play|go\s+get|go\s+and\s+get)\b.*)$", RegexOptions.IgnoreCase);
            if (!then.Success || then.Index == 0 || Regex.IsMatch(then.Groups[1].Value, @"\b(?:results?|links?|pages?|tabs?|sites?|websites?|articles?|videos?|notes?|diary|stickers?)\b", RegexOptions.IgnoreCase)) return text;
            after = then.Groups[1].Value.Trim().TrimEnd('.', '!', '?').Trim();
            return text.Substring(0, then.Index).Trim().TrimEnd(',', '.').Trim();
        }

        private void HerComputerHandOff(HerJob done)
        {
            if (IsDisposed || quitting || string.IsNullOrEmpty(done.After)) return;
            string found = HerComputer.Short(Regex.Replace(done.Answer ?? "", @"\s+", " ").Trim(), 900);
            string ask = (done.After + ". You just researched this yourself (" + HerComputer.Short(done.Task, 200) + ") and found: " + found).Trim();
            herHandOffAsk = ask;
            int waited = 0;
            System.Windows.Forms.Timer wait = new System.Windows.Forms.Timer { Interval = 400 };
            wait.Tick += delegate
            {
                waited += wait.Interval;
                if (TalkingNow && waited < 30000) return;
                wait.Stop();
                wait.Dispose();
                if (IsDisposed || quitting) return;
                Hear(ask, done.After, "> you: ");
            };
            wait.Start();
        }

        internal string HerComputerShown { get { return (preferences.HerComputer ? "on" : "off") + " | " + (herComputer == null || herComputer.IsDisposed ? "closed" : herComputer.Visible ? "open" : "hidden"); } }

        internal const string HerScreenId = "her";
        internal static readonly Rectangle HerScreenBounds = new Rectangle(-200000, -200000, HerComputer.Width, HerComputer.Height);

        internal void AddHerScreen(List<RemoteMonitor> monitors)
        {
            if (preferences.HerComputer) monitors.Add(new RemoteMonitor { Id = HerScreenId, Name = "Capri's computer", Device = HerScreenId, Bounds = HerScreenBounds });
        }

        private Bitmap HerScreenShot()
        {
            Bitmap shot = null;
            Action take = delegate
            {
                if (!preferences.HerComputer || IsDisposed) return;
                HerComputerWindow window = ShowHerComputer(false);
                window.Watched();
                shot = window.Snapshot();
            };
            if (!InvokeRequired) { take(); return shot; }
            using (ManualResetEvent done = new ManualResetEvent(false))
            {
                try { BeginInvoke(new Action(delegate { try { take(); } catch (Exception) { } finally { try { done.Set(); } catch (ObjectDisposedException) { } } })); }
                catch (InvalidOperationException) { return null; }
                if (!done.WaitOne(1500)) return null;
            }
            return shot;
        }

        internal sealed class HerOrDesktop : RemoteCapture
        {
            private readonly Companion her;
            private RemoteGrab desktop;
            private bool mine, any;
            private int lastWidth;
            private ulong lastPrint;

            internal HerOrDesktop(Companion her) { this.her = her; }

            internal override bool Open(RemoteMonitor monitor)
            {
                mine = monitor != null && monitor.Id == HerScreenId;
                any = false;
                lastWidth = 0;
                if (mine) return true;
                if (desktop == null) desktop = new RemoteGrab();
                return desktop.Open(monitor);
            }

            internal override Bitmap Next(int maxWidth)
            {
                if (!mine) return desktop == null ? null : desktop.Next(maxWidth);
                Bitmap full;
                try { full = her.HerScreenShot(); }
                catch (Exception) { return null; }
                if (full == null) return null;
                Bitmap frame = full;
                if (maxWidth > 0 && maxWidth < full.Width)
                {
                    frame = new Bitmap(maxWidth, Math.Max(1, full.Height * maxWidth / full.Width));
                    using (Graphics g = Graphics.FromImage(frame))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        g.DrawImage(full, new Rectangle(0, 0, frame.Width, frame.Height));
                    }
                    full.Dispose();
                }
                ulong print = RemoteGrab.Print(frame);
                if (any && maxWidth == lastWidth && print == lastPrint) { frame.Dispose(); return null; }
                any = true;
                lastWidth = maxWidth;
                lastPrint = print;
                return frame;
            }

            public override void Dispose()
            {
                RemoteGrab was = desktop;
                desktop = null;
                if (was != null) was.Dispose();
            }
        }
    }

    internal sealed partial class Mainframe
    {
        private Toggle herComputerSwitch;
        private Key herComputerOpen;

        private void BuildHerComputer(int y)
        {
            Section("HER COMPUTER >", 0, y);
            herComputerSwitch = Switch("Her own computer", 0, y + 24, delegate { if (!syncing) capri.ToggleHerComputer(); });
            herComputerSwitch.AccessibleName = "Her own computer. Off by default. She gets a little computer of her own in a separate window, with a real browser on her own profile, notes and a diary. Watch her use it or give her jobs to do on it";
            herComputerOpen = Make("Open her computer", 28, y + 56, 328, 22, delegate { capri.OpenHerComputer(); }, "Her own computer");
            herComputerOpen.AccessibleName = "Open her computer's window";
            Label hint = Caption("Say \"find me a recipe on your computer\" and\nshe does it there. Beta, Windows only.", 28, y + 86, Look.Mono(8.5f, FontStyle.Regular, GraphicsUnit.Point), Faint);
            hint.AutoSize = false;
            hint.Bounds = new Rectangle(28, y + 86, 328, 36);
        }

        private void SyncHerComputer()
        {
            herComputerSwitch.Checked = capri.HerComputerOn;
            herComputerOpen.Enabled = capri.HerComputerOn;
        }

        internal string HerComputerShown { get { return (herComputerSwitch.Checked ? "on" : "off") + " | " + (herComputerOpen.Enabled ? "can open" : "can't open"); } }
    }
}

using System;
using System.Collections.Generic;
using System.IO;

namespace CapriDesktop
{
    internal static class TimelineChecks
    {
        private static void Expect(bool good, string problem) { if (!good) throw new Exception(problem); }

        internal static List<string> Run()
        {
            List<string> passed = new List<string>();
            string scratch = Path.Combine(Path.GetTempPath(), "capri-timeline-" + Guid.NewGuid().ToString("N"));
            string kept = Path.Combine(scratch, "data"), top = Path.Combine(scratch, "files");
            Directory.CreateDirectory(kept);
            Directory.CreateDirectory(top);
            DateTime now = new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);
            Action<Deed> changedWas = Timeline.Changed;
            Func<string, ToolArguments, string> runWas = Timeline.Run;
            Func<DateTime> nowWas = Timeline.Now;
            Func<string, string> undoFilesWas = Timeline.UndoFiles;
            Func<string, bool> filesBackWas = Timeline.FilesBack;
            Func<string, int> openWindowsWas = Timeline.OpenWindows;
            int windowsOpen = 1;
            FileKeeper keeperWas = Hands.Keeper;
            Action<FileChange> keeperChangedWas = FileKeeper.Changed;
            Action<string, string> keeperUsedWas = FileKeeper.Used;
            string memoryWas = Brain.MemoryPath, folderWas = Timeline.Folder;
            List<Deed> told = new List<Deed>();
            List<string> ran = new List<string>();
            string answer = "Closed Spotify.";
            try
            {
                Timeline.Now = delegate { return now; };
                Timeline.OpenWindows = delegate { return windowsOpen; };
                Timeline.Changed = delegate(Deed deed) { told.Add(deed); };
                Timeline.Run = delegate(string tool, ToolArguments args)
                {
                    ran.Add(tool + ":" + args.Action + ":" + (args.Name ?? args.Text ?? "") + ":" + (Timeline.Quiet ? "quiet" : "loud") + ":" + Timeline.Keeps("open_app", new ToolArguments { Name = "x" }));
                    return answer;
                };
                Timeline.Open(kept);
                Expect(Timeline.Recent().Count == 0 && Timeline.List().StartsWith("I haven't done anything", StringComparison.Ordinal), "A new timeline wasn't empty.");

                Expect(Timeline.Keeps("open_app", new ToolArguments { Name = "Spotify" }) && Timeline.Keeps("files", new ToolArguments { Action = "move" }) && Timeline.Keeps("capri_settings", new ToolArguments { Action = "off" })
                    && Timeline.Keeps("mod", new ToolArguments { Name = "tool" }) && Timeline.Keeps("media", new ToolArguments { Action = "pause" }), "Her timeline skipped something she changed.");
                Expect(!Timeline.Keeps("look_at_screen", new ToolArguments()) && !Timeline.Keeps("files", new ToolArguments { Name = "beach" }) && !Timeline.Keeps("files", new ToolArguments { Action = "undo" })
                    && !Timeline.Keeps("capri_settings", new ToolArguments { Action = "list" }) && !Timeline.Keeps("timeline", new ToolArguments()) && !Timeline.Keeps("use_computer", new ToolArguments { Task = "x" })
                    && !Timeline.Keeps("media", new ToolArguments { Action = "now_playing" }) && !Timeline.Keeps("remind", new ToolArguments { Action = "list" }), "Her timeline kept a look, a read or a job's own call.");
                Timeline.Hush(true);
                bool hushed = !Timeline.Keeps("open_app", new ToolArguments { Name = "Spotify" });
                Timeline.Hush(false);
                Expect(hushed && Timeline.Keeps("open_app", new ToolArguments { Name = "Spotify" }), "Hushing her timeline didn't hold or didn't let go.");
                passed.Add("PASS her timeline keeps what she changes on the computer, not her looks, reads, lists or a job's own call, and stays quiet while she undoes or works a job");

                Deed opened = Timeline.Add(new Deed { Tool = "open_app", What = "Opened Spotify.", UndoTool = "app_window", UndoAction = "close", UndoName = "Spotify" });
                now = now.AddMinutes(1);
                Deed mod = Timeline.Add(new Deed { Tool = "mod", What = "Posted the clip to the channel.", Kept = Timeline.ModKept });
                now = now.AddMinutes(1);
                Deed failed = Timeline.Add(new Deed { Tool = "open_app", What = "Couldn't find an app called Spotfy.", Failed = true, UndoTool = "app_window", UndoAction = "close", UndoName = "Spotfy" });
                Expect(opened.Undoable && !mod.Undoable && mod.Kept == Timeline.ModKept && !failed.Undoable && failed.Kept == null && told.Count == 3, "Her timeline marked what can go back wrongly.");
                string list = Timeline.List();
                Expect(list.Contains("1. ") && list.IndexOf("Spotfy", StringComparison.Ordinal) < list.IndexOf("Posted the clip", StringComparison.Ordinal) && list.IndexOf("Posted the clip", StringComparison.Ordinal) < list.IndexOf("Opened Spotify", StringComparison.Ordinal)
                    && list.Contains("Opened Spotify (can undo)") && list.Contains("can't undo: " + Timeline.ModKept.TrimEnd('.')) && list.Contains("(didn't work)"), "Her timeline list wasn't newest first with what can go back: " + list);
                passed.Add("PASS her timeline lists what she did newest first and numbered, saying which can be undone and why the rest can't");

                string securityWas = Security.Saved;
                bool wakeWas = Security.On(Security.Wake);
                try
                {
                    Security.Load(Security.Of(Security.Guarded));
                    ToolArguments undoOpened = new ToolArguments { Action = "undo", Number = 3 };
                    string held = Security.Gate("timeline", undoOpened);
                    Expect(Security.IsHold(held) && held.Contains("\"Close Spotify?") && ran.Count == 0, "Undo from her timeline skipped the SECURITY toggle its undo needs: " + held);
                    Security.Drop();
                    Expect(Security.Gate("timeline", new ToolArguments { Action = "undo" }) == null && Security.Gate("timeline", new ToolArguments { Action = "list" }) == null, "Her timeline asked first to list, or to undo something with nothing to run.");
                    Security.Load(Security.Of(Security.Trusted));
                    Expect(Security.Gate("timeline", undoOpened) == null, "Undo from her timeline asked first on Trusted.");
                }
                finally { Security.Drop(); Security.Load(securityWas, wakeWas); }
                passed.Add("PASS undo from her timeline asks first when their SECURITY settings ask before what it would do, like closing an app on Guarded");

                string undo = Timeline.Undo((int?)null);
                Expect(undo.StartsWith("I can't undo", StringComparison.Ordinal) && undo.Contains(Timeline.ModKept) && ran.Count == 0, "Undo that skipped past her newest action or ran a mod tool: " + undo);
                answer = "Couldn't close Spotify: it's still open.";
                undo = Timeline.Undo((int?)3);
                Expect(undo.StartsWith("Couldn't undo", StringComparison.Ordinal) && ran.Count == 1 && Timeline.Find(opened.Id).Undoable, "A failed undo was marked done: " + undo);
                answer = "Closed Spotify.";
                windowsOpen = 2;
                undo = Timeline.Undo((int?)3);
                Expect(undo.StartsWith("I won't close Spotify", StringComparison.Ordinal) && ran.Count == 1 && Timeline.Find(opened.Id).Undoable, "Undo closed every window of an app with more open than the one she opened: " + undo);
                windowsOpen = 1;
                told.Clear();
                undo = Timeline.Undo((int?)3);
                Expect(undo.StartsWith("Undid it: Opened Spotify.", StringComparison.Ordinal) && ran.Count == 2 && ran[1] == "app_window:close:Spotify:quiet:False", "Undo didn't close what she opened, quietly: " + undo + " / " + string.Join(", ", ran.ToArray()));
                Deed back = Timeline.Find(opened.Id);
                Expect(back.Undone && !back.Undoable && back.Back == "Closed Spotify." && told.Count == 1 && told[0].Undone, "An undo wasn't kept or told.");
                Expect(Timeline.Undo(opened.Id).StartsWith("Already put back", StringComparison.Ordinal) && ran.Count == 2, "Undoing twice ran it again.");
                Expect(Timeline.Undo((int?)9).StartsWith("There's no number 9", StringComparison.Ordinal) && Timeline.Undo((int?)1).StartsWith("That one didn't work", StringComparison.Ordinal), "Undo by number went wrong.");
                passed.Add("PASS undo takes back her newest action or a numbered one, quietly, says plainly when it can't, keeps a failed undo undoable, never runs one twice, and won't close an app with more windows open than the one she opened");

                object job = new object();
                told.Clear();
                for (int i = 1; i <= 50; i++) Timeline.Step(job, "Send hi to Sam on Discord", i == 50 ? "Typed \"hi\" and pressed Enter" : "Clicked step " + i, i == 45);
                Deed running = Timeline.Recent().FindLast(delegate(Deed deed) { return deed.Job; });
                Expect(running != null && running.Steps.Count == Timeline.StepsKept && running.StepCount == 50 && running.Ended == null && told.Count == 0 && running.Kept == Timeline.JobKept, "A job's steps weren't kept together or were told before it ended.");
                Expect(running.Steps.Exists(delegate(string step) { return step == "\u2717 Clicked step 45"; }) && running.Steps[0] == "Clicked step 11", "A job's steps lost their order or a failed step lost its mark.");
                Timeline.Ended(job, "done");
                Timeline.Ended(job, "done");
                Expect(told.Count == 1 && told[0].Job && told[0].Ended == "done" && told[0].Steps[told[0].Steps.Count - 1].StartsWith("Typed", StringComparison.Ordinal), "An ended job wasn't told once with its steps.");
                Timeline.List();
                Expect(Timeline.Undo((int?)1).StartsWith("I can't undo that job. " + Timeline.JobKept, StringComparison.Ordinal), "Undo claimed it could take back clicks and typing.");
                passed.Add("PASS a job's clicks and typing go under one entry with its newest " + Timeline.StepsKept + " steps, told once when it ends, and undo says they can't be taken back");

                Timeline.Changed = null;
                Timeline.Open(kept);
                List<Deed> reread = Timeline.Recent();
                Expect(reread.Count == 4 && reread[0].Undone && reread[0].Back == "Closed Spotify." && reread[3].Job && reread[3].StepCount == 50 && reread[3].Ended == "done" && reread[1].Kept == Timeline.ModKept, "Her timeline didn't come back the same after a restart.");
                string saved = File.ReadAllText(Path.Combine(kept, "timeline.json"));
                Expect(!saved.Contains("\"UndoLevel\"") && !saved.Contains("null"), "Her timeline file kept empty fields.");
                for (int i = 0; i < Timeline.Kept + 10; i++) Timeline.Add(new Deed { Tool = "media", What = "Paused number " + i + "." });
                Expect(Timeline.Recent().Count == Timeline.Kept && Timeline.Recent()[0].What == "Paused number 10.", "Her timeline grew past " + Timeline.Kept + ".");
                now = now.AddDays(Timeline.Days + 1);
                Timeline.Add(new Deed { Tool = "media", What = "Paused a month later." });
                Expect(Timeline.Recent().Count == 1, "Her timeline kept things older than " + Timeline.Days + " days.");
                passed.Add("PASS her timeline comes back after a restart, keeps no empty fields, and holds her newest " + Timeline.Kept + " things from the last " + Timeline.Days + " days");

                Timeline.Run = delegate(string tool, ToolArguments args) { return Hands.Do(tool, args); };
                Timeline.Open(kept);
                Brain.MemoryPath = Path.Combine(kept, "me.md");
                File.WriteAllText(Brain.MemoryPath, "- They like teal keyboards with clicky switches\n");
                bool personal = Security.On(Security.Personal);
                Security.Set(Security.Personal, false);
                string note;
                try { note = Hands.Do("remember", new ToolArguments { Text = "They like teal keyboards" }); }
                finally { Security.Set(Security.Personal, personal); }
                Deed noted = Timeline.Recent().FindLast(delegate(Deed deed) { return deed.Tool == "remember"; });
                Expect(note.StartsWith("Saved to my notes", StringComparison.Ordinal) && noted != null && noted.Undoable && noted.UndoAction == "forget" && noted.UndoText == "They like teal keyboards", "A note she saved couldn't be undone: " + note);
                undo = Timeline.Undo((int?)null);
                Expect(undo.StartsWith("Undid it", StringComparison.Ordinal) && File.ReadAllText(Brain.MemoryPath) == "- They like teal keyboards with clicky switches\n" && Timeline.Recent().Count == 2, "Undoing a note didn't take out just that note, or the forget was kept too: " + undo);

                FileKeeper.Changed = null;
                FileKeeper.Used = null;
                Hands.Keeper = new FileKeeper(kept) { Roots = delegate { return new[] { top }; } };
                Timeline.UndoFiles = delegate(string id) { return Hands.Keeper.Undo(id); };
                Timeline.FilesBack = delegate(string id) { return Hands.Keeper.Recent().Exists(delegate(FileChange change) { return change.Id == id && change.Undone; }); };
                File.WriteAllText(Path.Combine(top, "list.txt"), "milk");
                string renamed = Hands.Do("files", new ToolArguments { Action = "rename", Path = Path.Combine(top, "list.txt"), Name = "shopping.txt" });
                Deed moved = Timeline.Recent().FindLast(delegate(Deed deed) { return deed.Tool == "files"; });
                Expect(File.Exists(Path.Combine(top, "shopping.txt")) && moved != null && moved.Files != null && moved.Undoable, "A file change wasn't kept with its undo: " + renamed);
                undo = Timeline.Undo(moved.Id);
                Expect(undo.StartsWith("Undid it", StringComparison.Ordinal) && File.Exists(Path.Combine(top, "list.txt")) && !File.Exists(Path.Combine(top, "shopping.txt")), "Undoing a file change from her timeline didn't put it back: " + undo);
                Hands.Do("files", new ToolArguments { Action = "rename", Path = Path.Combine(top, "list.txt"), Name = "groceries.txt" });
                Deed again = Timeline.Recent().FindLast(delegate(Deed deed) { return deed.Tool == "files"; });
                Hands.Keeper.Undo();
                Expect(again.Id != moved.Id && Timeline.Find(again.Id).Undone && !Timeline.Find(again.Id).Undoable, "A file change undone in Workspace still showed as undoable.");
                Deed old = Timeline.Add(new Deed { Tool = "files", What = "Moved an old photo.", Files = "0123456789abcdef0123456789abcdef" });
                string tooOld = Timeline.Undo(old.Id);
                Expect(old.Undoable && !Timeline.Find(old.Id).Undoable && tooOld.Contains(Timeline.FilesGone), "A file change she no longer keeps still showed as undoable, or said it went back: " + tooOld);
                passed.Add("PASS notes and file changes she makes through her tools are kept with a real undo that takes out only that note, a file change put back elsewhere shows as undone, and one she no longer keeps says it can't go back");

                object paying = new object();
                Timeline.Step(paying, "Pay for the order\nTheir exact words: \"use my card 4242 4242 4242 4242\"", "Typed \"4242 4242 4242 4242\"", false);
                Timeline.Step(paying, "Pay for the order", "Typed \"sam@example.com\" and pressed Enter", false);
                Timeline.Step(paying, "Pay for the order", "Typed \"hi\" and pressed Enter", false);
                Timeline.Ended(paying, "done");
                Deed paid = Timeline.Recent().FindLast(delegate(Deed deed) { return deed.Job; });
                string written = File.ReadAllText(Path.Combine(kept, "timeline.json"));
                Expect(paid != null && paid.What == "Pay for the order" && paid.Steps.Count == 3 && paid.Steps[0] == Timeline.Typed && paid.Steps[1] == Timeline.Typed && paid.Steps[2] == "Typed \"hi\" and pressed Enter"
                    && !written.Contains("4242") && !written.Contains("example.com") && !written.Contains("exact words"), "A job's card number, email or their exact words reached her timeline: " + (paid == null ? "missing" : paid.What + " / " + string.Join(" / ", paid.Steps.ToArray())));
                Timeline.List();
                Deed after = Timeline.Add(new Deed { Tool = "media", What = "Paused the timeline check.", UndoTool = "timeline_check", UndoAction = "none" });
                string numbered = Timeline.Undo((int?)1);
                Expect(numbered.StartsWith("I can't undo that job", StringComparison.Ordinal) && Timeline.Find(after.Id).Undoable, "Undo by number took what she did after her list, not what the list showed: " + numbered);
                passed.Add("PASS her timeline keeps card numbers, emails and their exact words out of a job's steps, and undo by number takes what her list showed");

                Func<List<OpenWindow>> windowsWas = Hands.Windows;
                try
                {
                    Hands.Windows = delegate { return new List<OpenWindow> { new OpenWindow(new IntPtr(9), "Inbox - Google Chrome", "chrome") }; };
                    Expect(Hands.OpenAlready("Chrome") && !Hands.OpenAlready("Spotify"), "She'd offer to close an app that was open before she opened it.");
                }
                finally { Hands.Windows = windowsWas; }
                passed.Add("PASS her undo won't offer to close an app that was open before she opened it");
#if !CROSS
                Deed shown = new Deed { Id = "x", When = now.ToString("o"), Tool = "use_computer", What = "Send hi to Sam", Steps = new List<string> { "Clicked Sam", "Typed \"hi\"" }, StepCount = 20, Ended = "asked", Kept = Timeline.JobKept };
                string card = Workspace.DeedText(shown);
                Expect(card.Contains("Job: Send hi to Sam") && card.Contains("18 steps before") && card.Contains("Typed \"hi\"") && card.Contains("Stopped to ask you something.") && card.Contains(Timeline.JobKept), "Her What she did card for a job was wrong: " + card);
                passed.Add("PASS her What she did card shows a job's newest steps, how it ended and why it can't be undone");
#endif
            }
            finally
            {
                Timeline.Changed = changedWas; Timeline.Run = runWas; Timeline.Now = nowWas; Timeline.UndoFiles = undoFilesWas; Timeline.FilesBack = filesBackWas; Timeline.OpenWindows = openWindowsWas;
                Hands.Keeper = keeperWas; FileKeeper.Changed = keeperChangedWas; FileKeeper.Used = keeperUsedWas; Brain.MemoryPath = memoryWas;
                Timeline.Open(folderWas);
                try { Directory.Delete(scratch, true); } catch (Exception) { }
            }
            return passed;
        }
    }
}

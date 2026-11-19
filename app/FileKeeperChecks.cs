using System;
using System.Collections.Generic;
using System.IO;

namespace CapriDesktop
{
    internal static class FileKeeperChecks
    {
        private static void Expect(bool good, string problem) { if (!good) throw new Exception(problem); }

        internal static List<string> Run()
        {
            List<string> passed = new List<string>();
            string scratch = Path.Combine(Path.GetTempPath(), "capri-keeper-" + Guid.NewGuid().ToString("N"));
            string top = Path.Combine(scratch, "keeper"), kept = Path.Combine(scratch, "keeper-data");
            if (Directory.Exists(top)) Directory.Delete(top, true);
            if (Directory.Exists(kept)) Directory.Delete(kept, true);
            string trip = Path.Combine(top, "Trip"), work = Path.Combine(top, "Work"), loose = Path.Combine(top, "Loose");
            Directory.CreateDirectory(trip); Directory.CreateDirectory(work); Directory.CreateDirectory(loose);
            DateTime now = new DateTime(2026, 11, 19, 15, 0, 0);
            string beach = Write(trip, "beach day.mp4", "video", now.AddDays(-3)), dog = Write(trip, "dog at beach.jpg", "photo", now.AddDays(-40));
            string lease = Write(work, "notes 2026.txt", "The lease for the flat ends in March.", now.AddDays(-1)), budget = Write(work, "budget.csv", "rent,food", now);
            Write(loose, "clip.mov", "v", now); Write(loose, "song.mp3", "m", now); Write(loose, "scan.pdf", "p", now); Write(loose, "mystery.qqq", "?", now);

            Action<FileChange> changedWas = FileKeeper.Changed;
            List<FileChange> told = new List<FileChange>();
            FileKeeper.Changed = told.Add;
            try
            {
                FileKeeper keeper = new FileKeeper(kept) { Now = delegate { return now; } };
                keeper.Roots = delegate { return new[] { top }; };
                string found = keeper.Do(new ToolArguments { Name = "beach" }, null);
                Expect(found.Contains("1. ") && found.Contains(beach) && found.Contains(dog), "Her file search missed names: " + found);
                found = keeper.Do(new ToolArguments { Name = "beach", Kind = "video" }, null);
                Expect(found.Contains(beach) && !found.Contains(dog), "Her file search ignored the kind: " + found);
                found = keeper.Do(new ToolArguments { Kind = "photo", When = "last week" }, null);
                Expect(found.StartsWith("I didn't find", StringComparison.Ordinal), "An old photo matched last week: " + found);
                found = keeper.Do(new ToolArguments { When = "this week" }, null);
                Expect(found.Contains(beach) && found.Contains(lease) && !found.Contains(dog), "This week's files were wrong: " + found);
                found = keeper.Do(new ToolArguments { Search = "lease flat" }, null);
                Expect(found.Contains(lease) && !found.Contains(budget) && found.Contains("has those words inside"), "Words inside a file weren't found: " + found);
                found = keeper.Do(new ToolArguments { Kind = "video", Search = "beach" }, null);
                Expect(found.Contains(beach) && found.Contains("can't see inside videos"), "A video was searched inside or the limit went unsaid: " + found);
                found = keeper.Do(new ToolArguments { Name = "budget", Folder = trip }, null);
                Expect(found.StartsWith("I didn't find", StringComparison.Ordinal), "A named folder didn't limit the search: " + found);
                Expect(keeper.Do(new ToolArguments { When = "the 40th of never" }, null).StartsWith("I couldn't read the date", StringComparison.Ordinal), "An unreadable date was ignored.");
                passed.Add("PASS her files tool finds by name, kind, date and words inside documents, limits to a named folder, says it can't see inside videos, and numbers what it finds");

                DateTime from, to;
                Expect(FileKeeper.Dates("today", now, out from, out to) && from == now.Date && to == now.Date.AddDays(1), "today");
                Expect(FileKeeper.Dates("yesterday", now, out from, out to) && from == now.Date.AddDays(-1), "yesterday");
                Expect(FileKeeper.Dates("last week", now, out from, out to) && from == new DateTime(2026, 11, 9) && to == new DateTime(2026, 11, 16), "last week");
                Expect(FileKeeper.Dates("2026-11-03", now, out from, out to) && from == new DateTime(2026, 11, 3) && to == new DateTime(2026, 11, 4), "a day");
                Expect(FileKeeper.Dates("2026-11", now, out from, out to) && from == new DateTime(2026, 11, 1) && to == new DateTime(2026, 12, 1), "a month");
                Expect(FileKeeper.Dates("december", now, out from, out to) && from == new DateTime(2025, 12, 1), "a month name in the future means last year's");
                Expect(FileKeeper.Dates("2026-11-01 to 2026-11-07", now, out from, out to) && from == new DateTime(2026, 11, 1) && to == new DateTime(2026, 11, 8), "a range");
                Expect(FileKeeper.Dates("before 2026-01", now, out from, out to) && from == DateTime.MinValue && to == new DateTime(2026, 1, 1), "before");
                Expect(FileKeeper.Dates("last 3 days", now, out from, out to) && from == new DateTime(2026, 11, 17), "last 3 days");
                Expect(FileKeeper.Dates("3rd November", now, out from, out to) && from == new DateTime(2026, 11, 3), "3rd November");
                Expect(!FileKeeper.Dates("someday soon", now, out from, out to), "nonsense was read as a date");
                passed.Add("PASS her file dates read today, yesterday, last week, days, months, ranges, before and after the way they're said");

                keeper.Do(new ToolArguments { Name = "beach" }, null);
                string moved = keeper.Do(new ToolArguments { Action = "move", Path = "1, 2", Target = "Beach" }, null);
                string beachFolder = Path.Combine(trip, "Beach");
                Expect(moved.StartsWith("Moved ", StringComparison.Ordinal) && moved.EndsWith(FileKeeper.UndoLine, StringComparison.Ordinal) && File.Exists(Path.Combine(beachFolder, "beach day.mp4")) && File.Exists(Path.Combine(beachFolder, "dog at beach.jpg")) && !File.Exists(beach), "Moving numbered results into a new folder beside them failed: " + moved);
                Write(trip, "beach day.mp4", "a second one", now);
                keeper.Do(new ToolArguments { Name = "beach day", Folder = trip, Kind = "video" }, null);
                moved = keeper.Do(new ToolArguments { Action = "move", Path = "all", Target = beachFolder }, null);
                Expect(File.Exists(Path.Combine(beachFolder, "beach day (2).mp4")) && File.ReadAllText(Path.Combine(beachFolder, "beach day.mp4")) == "video", "A move overwrote a file with the same name: " + moved);
                Expect(keeper.Undo().StartsWith("Put back 1", StringComparison.Ordinal) && File.ReadAllText(beach) == "a second one", "Undo didn't put the second move back.");
                string undone = keeper.Undo();
                Expect(undone.StartsWith("Put back 2", StringComparison.Ordinal) && File.ReadAllText(Path.Combine(trip, "beach day (2).mp4")) == "video" && File.Exists(dog) && !Directory.Exists(beachFolder), "Undo didn't put the first move back or left its folder: " + undone);
                File.Delete(beach); File.Move(Path.Combine(trip, "beach day (2).mp4"), beach);
                passed.Add("PASS her moves take numbered results into a new folder beside them, never overwrite, and undo puts each change back in order and removes folders she made");

                keeper.Do(new ToolArguments { Name = "notes" }, null);
                string renamed = keeper.Do(new ToolArguments { Action = "rename", Path = "1", Name = "lease notes" }, null);
                Expect(File.Exists(Path.Combine(work, "lease notes.txt")) && !File.Exists(lease) && renamed.Contains("lease notes.txt"), "Renaming lost the extension: " + renamed);
                Expect(keeper.Do(new ToolArguments { Action = "rename", Path = "1", Name = "a/b" }, null).StartsWith("A name can't have", StringComparison.Ordinal), "A name with a slash was allowed.");
                keeper.Undo();
                Expect(File.Exists(lease), "Undoing a rename didn't change it back.");
                string made = keeper.Do(new ToolArguments { Action = "make_folder", Name = "Taxes", Folder = work }, null);
                Expect(Directory.Exists(Path.Combine(work, "Taxes")) && made.StartsWith("Made the folder", StringComparison.Ordinal), "Making a folder failed: " + made);
                keeper.Undo();
                Expect(!Directory.Exists(Path.Combine(work, "Taxes")), "Undoing a new folder left it.");
                passed.Add("PASS her renames keep the extension and refuse slashes, make_folder makes one where asked, and undo reverses both");

                string sorted = keeper.Do(new ToolArguments { Action = "organise", Folder = loose }, null);
                Expect(File.Exists(Path.Combine(loose, "Videos", "clip.mov")) && File.Exists(Path.Combine(loose, "Music", "song.mp3")) && File.Exists(Path.Combine(loose, "Documents", "scan.pdf")) && File.Exists(Path.Combine(loose, "mystery.qqq")), "Sorting by kind went wrong: " + sorted);
                Expect(new FileKeeper(kept).Recent().Count == keeper.Recent().Count && !keeper.Recent()[keeper.Recent().Count - 1].Undone, "Her file changes weren't kept for undo after a restart.");
                Expect(new FileKeeper(kept).Undo().StartsWith("Put back 3", StringComparison.Ordinal) && File.Exists(Path.Combine(loose, "clip.mov")) && !Directory.Exists(Path.Combine(loose, "Videos")), "Undo after a restart didn't unsort the folder.");
                passed.Add("PASS sorting a folder groups loose files by kind and leaves what she doesn't recognise, and undo works after a restart");

                Expect(keeper.Guarded(Path.GetPathRoot(top), false) != null && keeper.Guarded(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), false) != null, "A whole drive or the user folder could be changed.");
    #if CROSS
                Expect(keeper.Guarded("/usr/lib", false) != null && keeper.Guarded("/etc", true) != null, "A system folder could be changed.");
    #else
                Expect(keeper.Guarded(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32"), false) != null && keeper.Guarded(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), true) != null, "A system folder could be changed.");
    #endif
                Expect(keeper.Do(new ToolArguments { Action = "move", Path = top, Target = Path.Combine(top, "Inside") }, null).StartsWith("I can't move", StringComparison.Ordinal), "A folder was moved into itself.");
                Expect(keeper.Do(new ToolArguments { Action = "organise", Folder = Path.GetPathRoot(top) }, null).StartsWith("I won't sort", StringComparison.Ordinal), "A whole drive could be sorted.");
                passed.Add("PASS her file changes refuse whole drives, the user folder, system and app folders, and moving a folder into itself");

                string saved = Security.Saved;
                FileKeeper keeperWas = Hands.Keeper;
                try
                {
                    Hands.Keeper = keeper;
                    Security.Load(Security.Of(Security.Trusted));
                    ToolArguments move = new ToolArguments { Action = "move", Path = lease, Target = "Elsewhere" };
                    Expect(Hands.Valid("files", move) && Hands.Valid("files", new ToolArguments { Action = "undo" }) && !Hands.Valid("files", new ToolArguments()) && !Hands.Valid("files", new ToolArguments { Action = "move", Path = "1" }), "Her files tool validated the wrong calls.");
                    string held = Security.Gate("files", move);
                    Expect(Security.IsHold(held) && held.Contains("ove notes 2026.txt into Elsewhere") && File.Exists(lease), "Moving files on Trusted didn't ask first: " + held);
                    Security.Drop();
                    Expect(Security.Gate("files", new ToolArguments { Name = "lease" }) == null && Security.Gate("files", new ToolArguments { Action = "undo" }) == null && Security.Gate("files", new ToolArguments { Action = "make_folder", Name = "x", Folder = work }) == null, "Finding, undoing or making a folder asked first.");
                    Security.Load(Security.Of(Security.Blackwall));
                    Expect(Security.Gate("files", move) == null, "Blackwall still asked before moving files.");
                }
                finally
                {
                    Security.Drop();
                    Security.Load(saved);
                    Hands.Keeper = keeperWas;
                }
                passed.Add("PASS moving, renaming and sorting files ask first on every preset but Blackwall, while finding, making a folder and undo don't");
                Expect(told.Count >= 10 && told.Exists(delegate(FileChange change) { return change.Undone && change.What.StartsWith("sorted ", StringComparison.Ordinal); }), "Her file changes and undos weren't announced for Workspace.");
            }
            finally { FileKeeper.Changed = changedWas; }
#if !CROSS
            using (Workspace work = new Workspace(Path.Combine(scratch, "work"), "CapriWorkspace-" + System.Diagnostics.Process.GetCurrentProcess().Id + "-files-" + Guid.NewGuid().ToString("N")))
            {
                work.Home = Path.Combine(scratch, "home");
                FileChange change = new FileChange { Id = "a1", What = "moved 1 into Beach", Steps = new List<FileStep> { new FileStep { From = "", To = Path.Combine(scratch, "Beach") }, new FileStep { From = Path.Combine(scratch, "a.mp4"), To = Path.Combine(scratch, "Beach", "a.mp4") } } };
                work.LogFiles(change);
                work.LogFiles(change);
                WorkspaceChat chat = work.FilesChat();
                List<WorkspaceItem> logged;
                lock (chat.Gate) logged = chat.Items.FindAll(delegate(WorkspaceItem item) { return item.Kind == "files"; });
                Expect(work.Chats().Count == 1 && chat.Title == Workspace.FilesTitle && logged.Count == 1 && logged[0].Text.StartsWith("Moved 1 into Beach.\n" + Path.Combine(scratch, "a.mp4") + " \u2192 ", StringComparison.Ordinal), "Her file change wasn't logged once in Workspace.");
                string undid = null;
                work.UndoFiles = delegate(string id) { undid = id; return "Put back 1 (moved 1 into Beach)."; };
                Expect(work.UndoFile(chat, logged[0]) == "Put back 1 (moved 1 into Beach)." && undid == "a1" && logged[0].Decision == "undone" && work.UndoFile(chat, logged[0]) == null, "Workspace's Undo key didn't undo her change once.");
                change.Undone = true;
                int count;
                lock (chat.Gate) count = chat.Items.Count;
                work.LogFiles(change);
                lock (chat.Gate) Expect(chat.Items.Count == count && chat.Items[count - 1].Text == "Put back 1 (moved 1 into Beach).", "An undo from chat was shown twice in Workspace.");
            }
            passed.Add("PASS her file changes show once in Workspace's Her files chat with an Undo key that puts them back once");
#endif
            Directory.Delete(scratch, true);
            return passed;
        }

        private static string Write(string folder, string name, string text, DateTime when)
        {
            string path = Path.Combine(folder, name);
            File.WriteAllText(path, text);
            File.SetCreationTime(path, when);
            File.SetLastWriteTime(path, when);
            return path;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CapriDesktop
{
    internal static class MemoryBook
    {
        internal const string Feature = "memory";
        internal const int Most = 56000, DayMost = 40000, DaysShown = 120, TextMost = 300;
        private const int UndoKept = 12, UndoChars = 3000000;

        internal const string About = "about", Chats = "chats", Games = "games", Days = "days";

        internal sealed class Item
        {
            internal string Id, Kind, Group, Text, Path;
            internal int Line;
        }

        internal sealed class Day
        {
            internal string Date;
            internal int Lines;
        }

        internal sealed class Said
        {
            internal string At, Who, Text;
        }

        internal sealed class Result
        {
            internal string Code, Say, Undo;
            internal bool Ok;
        }

        private sealed class Kept
        {
            internal string Path, Before, After;
            internal object Gate;
            internal string[] Queued;
        }

        private sealed class Change
        {
            internal string Token;
            internal List<Kept> Files = new List<Kept>();
            internal int Size;
        }

        internal static event Action Changed;
        private static readonly List<Change> undo = new List<Change>();
        private static readonly object undoGate = new object();
        private static readonly Queue<Action> work = new Queue<Action>();
        private static bool working;
        private static readonly Regex DayFile = new Regex(@"^\d{4}-\d{2}-\d{2}$");
        private static readonly Regex Spoken = new Regex(@"^\*\*(\d{1,2}:\d{2}) ([^*]{1,40}?):\*\* ?(.*)$");
        private static readonly Regex Done = new Regex(@"^\*(\d{1,2}:\d{2}) (.+)\*$");

        internal static bool Ready { get { return Brain.MemoryPath != null; } }

        internal static void Queue(Action job)
        {
            lock (work)
            {
                work.Enqueue(job);
                if (working) return;
                working = true;
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                while (true)
                {
                    Action next;
                    lock (work)
                    {
                        if (work.Count == 0) { working = false; return; }
                        next = work.Dequeue();
                    }
                    try { next(); }
                    catch (Exception problem) { Brain.Log("memory book", -1, problem.GetType().Name + ": " + problem.Message); }
                }
            });
        }

        private static string Read(string path)
        {
            try { return path != null && File.Exists(path) ? File.ReadAllText(path) : null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        private static string Hash(string text)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                StringBuilder id = new StringBuilder();
                for (int i = 0; i < 9; i++) id.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                return id.ToString();
            }
        }

        private static string Bare(string line)
        {
            string text = line.Trim();
            if (text.StartsWith("- ", StringComparison.Ordinal) || text.StartsWith("* ", StringComparison.Ordinal)) text = text.Substring(2).Trim();
            return text;
        }

        internal static List<Item> Parse(string kind, string path, string text)
        {
            List<Item> items = new List<Item>();
            if (text == null) return items;
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            string group = kind == Games ? Path.GetFileNameWithoutExtension(path ?? "") : "";
            Dictionary<string, int> seen = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith(">", StringComparison.Ordinal)) continue;
                if (line.StartsWith("#", StringComparison.Ordinal))
                {
                    string heading = line.TrimStart('#').Trim();
                    if (kind == Chats && line.StartsWith("## ", StringComparison.Ordinal)) group = heading;
                    else if (kind == Games && line.StartsWith("# ", StringComparison.Ordinal) && heading.Length > 0) group = heading;
                    continue;
                }
                bool bullet = line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal);
                if (kind != About && !bullet) continue;
                string words = Bare(line);
                if (words.Length == 0) continue;
                string key = group + "\n" + words;
                int count;
                seen.TryGetValue(key, out count);
                seen[key] = count + 1;
                items.Add(new Item { Kind = kind, Group = group, Text = words, Path = path, Line = i, Id = kind.Substring(0, 1) + Hash(kind + "\n" + Path.GetFileName(path ?? "") + "\n" + key + "\n" + count) });
            }
            return items;
        }

        internal static List<string> GameFiles()
        {
            List<string> found = new List<string>();
            string folder = GameNotes.Folder;
            try
            {
                if (folder == null || !Directory.Exists(folder)) return found;
                foreach (string file in Directory.GetFiles(folder, "*.md"))
                    if (!file.EndsWith(".controls.md", StringComparison.OrdinalIgnoreCase)) found.Add(file);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            found.Sort(delegate(string a, string b) { return File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)); });
            return found;
        }

        internal static List<Item> Everything()
        {
            List<Item> all = new List<Item>();
            lock (Brain.NotesGate) all.AddRange(Parse(About, Brain.MemoryPath, Read(Brain.MemoryPath)));
            lock (Brain.ChatGate) all.AddRange(Parse(Chats, Brain.MemoriesPath, Read(Brain.MemoriesPath)));
            lock (GameNotes.Gate) foreach (string file in GameFiles()) all.AddRange(Parse(Games, file, Read(file)));
            return all;
        }

        private static string TranscriptFolder { get { return Brain.TranscriptPath; } }

        internal static List<Day> DayList()
        {
            List<Day> days = new List<Day>();
            string folder = TranscriptFolder;
            try
            {
                if (folder == null || !Directory.Exists(folder)) return days;
                foreach (string file in Directory.GetFiles(folder, "*.md"))
                {
                    string date = Path.GetFileNameWithoutExtension(file);
                    if (!DayFile.IsMatch(date)) continue;
                    int lines = 0;
                    foreach (string line in File.ReadAllLines(file)) if (line.StartsWith("*", StringComparison.Ordinal)) lines++;
                    days.Add(new Day { Date = date, Lines = lines });
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            days.Sort(delegate(Day a, Day b) { return string.CompareOrdinal(b.Date, a.Date); });
            return days;
        }

        private static string DayPath(string date)
        {
            string folder = TranscriptFolder;
            if (folder == null || date == null || !DayFile.IsMatch(date)) return null;
            DateTime parsed;
            if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)) return null;
            return Path.Combine(folder, date + ".md");
        }

        internal static List<Said> ReadDay(string date, out bool cut)
        {
            cut = false;
            List<Said> lines = new List<Said>();
            string path = DayPath(date), text;
            if (path == null) return null;
            lock (Brain.ChatGate) text = Read(path);
            if (text == null) return null;
            foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                Match spoken = Spoken.Match(line);
                if (spoken.Success) { lines.Add(new Said { At = spoken.Groups[1].Value, Who = spoken.Groups[2].Value.Trim(), Text = spoken.Groups[3].Value }); continue; }
                Match done = Done.Match(line);
                if (done.Success) lines.Add(new Said { At = done.Groups[1].Value, Who = "", Text = done.Groups[2].Value });
            }
            int size = 0, from = lines.Count;
            while (from > 0 && size + lines[from - 1].Text.Length + 40 <= DayMost) size += lines[--from].Text.Length + 40;
            if (from > 0) { cut = true; lines.RemoveRange(0, from); }
            return lines;
        }

        private static string Clean(string text, int max)
        {
            string words = Regex.Replace(text ?? "", @"[\u0000-\u001f\u007f\u200e\u200f\u202a-\u202e\u2066-\u2069]+", " ");
            words = Regex.Replace(words, @"\s+", " ").Trim(' ', '-', '*', '"');
            words = words.TrimStart('#', '>', ' ');
            if (words.Length > max) words = RemoteMessages.Clip(words, max).TrimEnd();
            return words;
        }

        private static object GateFor(string kind)
        {
            return kind == About ? Brain.NotesGate : kind == Games ? GameNotes.Gate : Brain.ChatGate;
        }

        private static string HeadFor(string kind)
        {
            return kind == About ? Brain.NotesHead : kind == Chats ? Brain.MemoriesHead : "";
        }

        private static string Write(string path, string kind, string text)
        {
            if (text == null)
            {
                try { if (File.Exists(path)) File.Delete(path); return null; }
                catch (IOException) { return "failed"; }
                catch (UnauthorizedAccessException) { return "failed"; }
            }
            try
            {
                WrittenNote note = new WrittenNote(path, HeadFor(kind), GateFor(kind));
                return note.Save(text) == null ? null : "failed";
            }
            catch (IOException) { return "failed"; }
            catch (UnauthorizedAccessException) { return "failed"; }
        }

        private static Result Fail(string code, string say) { return new Result { Ok = false, Code = code, Say = say }; }

        private static Result Keep(string code, string say, Change change)
        {
            string token = null;
            if (change != null && change.Files.Count > 0)
            {
                token = RemoteMessages.NewId();
                change.Token = token;
                foreach (Kept kept in change.Files) change.Size += (kept.Before ?? "").Length + (kept.After ?? "").Length + (kept.Queued == null ? 0 : string.Join("\n", kept.Queued).Length);
                lock (undoGate)
                {
                    if (change.Size <= UndoChars) undo.Add(change);
                    else token = null;
                    int total = 0;
                    for (int i = undo.Count - 1; i >= 0; i--)
                    {
                        total += undo[i].Size;
                        if (undo.Count - i > UndoKept || total > UndoChars) { undo.RemoveRange(0, i + 1); break; }
                    }
                }
            }
            Action changed = Changed;
            if (changed != null) try { changed(); } catch (Exception) { }
            return new Result { Ok = true, Code = code, Say = say, Undo = token };
        }

        private static Item Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (Item item in Everything()) if (item.Id == id) return item;
            return null;
        }

        private static string Rewrite(string text, Item item, string replacement, out string before)
        {
            before = text;
            List<string> lines = new List<string>(text.Replace("\r\n", "\n").Split('\n'));
            if (item.Line >= lines.Count || Bare(lines[item.Line]) != item.Text) return null;
            if (replacement != null)
            {
                lines[item.Line] = "- " + replacement;
                return string.Join("\n", lines.ToArray());
            }
            lines.RemoveAt(item.Line);
            if (item.Kind == Chats && item.Group.Length > 0)
            {
                int heading = -1;
                for (int i = Math.Min(item.Line, lines.Count) - 1; i >= 0; i--) if (lines[i].Trim().StartsWith("## ", StringComparison.Ordinal)) { heading = i; break; }
                if (heading >= 0)
                {
                    bool left = false;
                    for (int i = heading + 1; i < lines.Count && !lines[i].Trim().StartsWith("## ", StringComparison.Ordinal); i++)
                        if (lines[i].Trim().StartsWith("- ", StringComparison.Ordinal) || lines[i].Trim().StartsWith("* ", StringComparison.Ordinal)) { left = true; break; }
                    if (!left)
                    {
                        lines.RemoveAt(heading);
                        while (heading < lines.Count && lines[heading].Trim().Length == 0 && (heading == 0 || lines[heading - 1].Trim().Length == 0)) lines.RemoveAt(heading);
                    }
                }
            }
            return string.Join("\n", lines.ToArray());
        }

        internal static Result Edit(string id, string text)
        {
            Item item = Find(id);
            if (item == null) return Fail("changed", "That memory changed on your PC. Here's what she has now.");
            string words = Clean(text, item.Kind == About ? 200 : TextMost);
            if (words.Length == 0) return Fail("empty", "Write something, or forget it instead.");
            if (Brain.Withheld(words)) return Fail("private", Brain.Private);
            words = char.ToUpperInvariant(words[0]) + words.Substring(1);
            if (words == item.Text) return Fail("same", "Nothing changed.");
            return Apply(item, words);
        }

        internal static Result Forget(string id)
        {
            Item item = Find(id);
            if (item == null) return Fail("changed", "That memory changed on your PC. Here's what she has now.");
            return Apply(item, null);
        }

        private static Result Apply(Item item, string words)
        {
            lock (GateFor(item.Kind))
            {
                string current = Read(item.Path), before;
                if (current == null) return Fail("changed", "That memory changed on your PC. Here's what she has now.");
                string after = Rewrite(current, item, words, out before);
                if (after == null) return Fail("changed", "That memory changed on your PC. Here's what she has now.");
                if (Write(item.Path, item.Kind, after) != null) return Fail("failed", "Couldn't change her notes on your PC.");
                Change change = new Change();
                change.Files.Add(new Kept { Path = item.Path, Before = before, After = after, Gate = GateFor(item.Kind) });
                return Keep(words == null ? "forgot" : "saved", words == null ? "Forgot it." : "Saved.", change);
            }
        }

        internal static Result Add(string text)
        {
            string words = Clean(text, 200);
            if (words.Length == 0) return Fail("empty", "What should she remember?");
            string path = Brain.MemoryPath;
            if (path == null) return Fail("off", "She can't keep notes on this PC yet.");
            lock (Brain.NotesGate)
            {
                string before = Read(path);
                string said = Brain.Note(words);
                string after = Read(path);
                if (string.Equals(before, after, StringComparison.Ordinal))
                {
                    if (said == Brain.Private) return Fail("private", said);
                    if (said.StartsWith("Already", StringComparison.Ordinal)) return Fail("same", said);
                    if (said.IndexOf("full", StringComparison.Ordinal) >= 0) return Fail("full", said);
                    return Fail("failed", said);
                }
                Change change = new Change();
                change.Files.Add(new Kept { Path = path, Before = before, After = after, Gate = Brain.NotesGate });
                return Keep("added", said, change);
            }
        }

        private static string[] Dequeue(Func<string, bool> drop)
        {
            string queue = Brain.QueueFile;
            if (queue == null || !File.Exists(queue)) return new string[0];
            List<string> kept = new List<string>(), dropped = new List<string>();
            foreach (string line in File.ReadAllLines(queue)) (drop(line) ? dropped : kept).Add(line);
            if (dropped.Count > 0) File.WriteAllText(queue, kept.Count == 0 ? "" : string.Join("\n", kept.ToArray()) + "\n");
            return dropped.ToArray();
        }

        internal static Result ForgetDay(string date)
        {
            string path = DayPath(date);
            if (path == null) return Fail("bad", "That isn't a day she kept.");
            lock (Brain.ChatGate)
            {
                string before = Read(path);
                if (before == null) return Fail("changed", "That day is already gone.");
                try
                {
                    File.Delete(path);
                    string[] queued = Dequeue(delegate(string line) { return line.StartsWith(date + " ", StringComparison.Ordinal); });
                    Change change = new Change();
                    change.Files.Add(new Kept { Path = path, Before = before, After = null, Gate = Brain.ChatGate, Queued = queued });
                    return Keep("forgot", "Forgot that day's chat.", change);
                }
                catch (IOException) { return Fail("failed", "Couldn't delete that day on your PC."); }
                catch (UnauthorizedAccessException) { return Fail("failed", "Couldn't delete that day on your PC."); }
            }
        }

        internal static Result Wipe(string what)
        {
            if (what != About && what != Chats && what != Days && what != Games && what != "all") return Fail("bad", "She doesn't know what to wipe.");
            Change change = new Change();
            try
            {
                if (what == About || what == "all")
                    lock (Brain.NotesGate)
                    {
                        string path = Brain.MemoryPath, before = Read(path);
                        if (before != null && Parse(About, path, before).Count > 0)
                        {
                            if (Write(path, About, Brain.NotesHead) != null) return Fail("failed", "Couldn't wipe her notes.");
                            change.Files.Add(new Kept { Path = path, Before = before, After = Brain.NotesHead, Gate = Brain.NotesGate });
                        }
                    }
                if (what == Chats || what == Days || what == "all")
                    lock (Brain.ChatGate)
                    {
                        string path = Brain.MemoriesPath, before = Read(path);
                        if (what != Days && before != null && Parse(Chats, path, before).Count > 0)
                        {
                            if (Write(path, Chats, Brain.MemoriesHead) != null) return Fail("failed", "Couldn't wipe her memories.");
                            change.Files.Add(new Kept { Path = path, Before = before, After = Brain.MemoriesHead, Gate = Brain.ChatGate });
                        }
                        if (what != Chats)
                            foreach (Day day in DayList())
                            {
                                string file = DayPath(day.Date), text = Read(file);
                                if (text == null) continue;
                                File.Delete(file);
                                change.Files.Add(new Kept { Path = file, Before = text, After = null, Gate = Brain.ChatGate });
                            }
                        string[] queued = Dequeue(delegate(string line) { return true; });
                        if (queued.Length > 0) change.Files.Add(new Kept { Path = null, Gate = Brain.ChatGate, Queued = queued });
                    }
                if (what == Games)
                    lock (GameNotes.Gate)
                        foreach (string file in GameFiles())
                        {
                            string text = Read(file);
                            if (text == null) continue;
                            File.Delete(file);
                            change.Files.Add(new Kept { Path = file, Before = text, After = null, Gate = GameNotes.Gate });
                        }
            }
            catch (IOException) { return Fail("failed", "Couldn't wipe everything. Some of it may be left."); }
            catch (UnauthorizedAccessException) { return Fail("failed", "Couldn't wipe everything. Some of it may be left."); }
            if (change.Files.Count == 0) return Fail("same", "There was nothing to forget.");
            return Keep("forgot", what == "all" ? "She forgot everything about you." : "Wiped.", change);
        }

        internal static Result Undo(string token)
        {
            Change change = null;
            lock (undoGate)
                for (int i = undo.Count - 1; i >= 0; i--)
                    if (undo[i].Token == token) { change = undo[i]; break; }
            if (change == null) return Fail("gone", "It's too late to undo that.");
            List<object> gates = new List<object>();
            foreach (object gate in new[] { Brain.NotesGate, Brain.ChatGate, GameNotes.Gate })
                foreach (Kept kept in change.Files) if (kept.Gate == gate && !gates.Contains(gate)) gates.Add(gate);
            return Under(gates, 0, delegate
            {
                foreach (Kept kept in change.Files)
                    if (kept.Path != null && !string.Equals(Read(kept.Path), kept.After, StringComparison.Ordinal)) return Fail("changed", "Her notes changed since, so she can't undo that.");
                try
                {
                    foreach (Kept kept in change.Files)
                    {
                        if (kept.Path != null && Write(kept.Path, KindOf(kept), kept.Before) != null) return Fail("failed", "Couldn't undo that on your PC.");
                        if (kept.Queued != null && kept.Queued.Length > 0 && Brain.QueueFile != null)
                        {
                            string now = File.Exists(Brain.QueueFile) ? File.ReadAllText(Brain.QueueFile) : "";
                            File.WriteAllText(Brain.QueueFile, string.Join("\n", kept.Queued) + "\n" + now);
                        }
                    }
                }
                catch (IOException) { return Fail("failed", "Couldn't undo that on your PC."); }
                catch (UnauthorizedAccessException) { return Fail("failed", "Couldn't undo that on your PC."); }
                lock (undoGate) undo.Remove(change);
                return Keep("undone", "Put it back.", null);
            });
        }

        private static string KindOf(Kept kept)
        {
            return kept.Gate == Brain.NotesGate ? About : kept.Gate == GameNotes.Gate ? Games : string.Equals(kept.Path, Brain.MemoriesPath, StringComparison.OrdinalIgnoreCase) ? Chats : Days;
        }

        private static Result Under(List<object> gates, int at, Func<Result> run)
        {
            if (at >= gates.Count) return run();
            lock (gates[at]) return Under(gates, at + 1, run);
        }

        private static RemoteJson.Writer ItemJson(Item item)
        {
            return new RemoteJson.Writer().Text("id", item.Id).Text("text", Clean(item.Text, TextMost));
        }

        internal static string List()
        {
            List<Item> all = Everything();
            List<Day> days = DayList();
            int gameMost = 40, chatGroups = 400, dayMost = DaysShown;
            for (int tries = 0; ; tries++)
            {
                string json = ListJson(all, days, gameMost, chatGroups, dayMost, tries > 0);
                if (Encoding.UTF8.GetByteCount(json) <= Most || tries > 12) return json;
                dayMost = Math.Max(14, dayMost / 2);
                gameMost = Math.Max(3, gameMost / 2);
                if (tries > 3) chatGroups = Math.Max(3, chatGroups / 2);
            }
        }

        private static string ListJson(List<Item> all, List<Day> days, int gameMost, int chatGroups, int dayMost, bool cut)
        {
            List<RemoteJson.Writer> about = new List<RemoteJson.Writer>();
            List<string> chatKeys = new List<string>(), gameKeys = new List<string>();
            Dictionary<string, List<Item>> chats = new Dictionary<string, List<Item>>(StringComparer.Ordinal), games = new Dictionary<string, List<Item>>(StringComparer.OrdinalIgnoreCase);
            foreach (Item item in all)
            {
                if (item.Kind == About) { about.Add(ItemJson(item)); continue; }
                bool chat = item.Kind == Chats;
                Dictionary<string, List<Item>> groups = chat ? chats : games;
                string key = chat ? item.Group : item.Path;
                if (!groups.ContainsKey(key)) { groups[key] = new List<Item>(); (chat ? chatKeys : gameKeys).Add(key); }
                groups[key].Add(item);
            }
            List<RemoteJson.Writer> chatJson = new List<RemoteJson.Writer>();
            for (int i = Math.Max(0, chatKeys.Count - chatGroups); i < chatKeys.Count; i++)
                chatJson.Add(new RemoteJson.Writer().Text("day", Clean(chatKeys[i], 80)).Objects("items", chats[chatKeys[i]].ConvertAll<RemoteJson.Writer>(ItemJson)));
            List<RemoteJson.Writer> gameJson = new List<RemoteJson.Writer>();
            foreach (string key in gameKeys)
            {
                List<Item> items = games[key];
                string name = Clean(items[0].Group, 80);
                if (items.Count > gameMost) items = items.GetRange(items.Count - gameMost, gameMost);
                gameJson.Add(new RemoteJson.Writer().Text("game", name).Objects("items", items.ConvertAll<RemoteJson.Writer>(ItemJson)));
            }
            List<RemoteJson.Writer> dayJson = new List<RemoteJson.Writer>();
            for (int i = 0; i < days.Count && i < dayMost; i++) dayJson.Add(new RemoteJson.Writer().Text("date", days[i].Date).Number("lines", days[i].Lines));
            return new RemoteJson.Writer().Text("type", "memory.list").Flag("keeping", Brain.Keeping).Flag("private", Brain.Discreet).Flag("cut", cut || days.Count > dayMost)
                .Objects("about", about).Objects("chats", chatJson).Objects("games", gameJson).Objects("days", dayJson).ToString();
        }

        internal static string DayJson(string re, string date)
        {
            bool cut;
            List<Said> lines = ReadDay(date, out cut);
            RemoteJson.Writer reply = new RemoteJson.Writer().Text("type", "memory.chat").Text("re", re).Text("date", date ?? "");
            if (lines == null) return reply.Flag("ok", false).ToString();
            List<RemoteJson.Writer> list = new List<RemoteJson.Writer>();
            foreach (Said line in lines) list.Add(new RemoteJson.Writer().Text("at", line.At).Text("who", Clean(line.Who, 40)).Text("text", RemoteMessages.Clip(line.Text, 2000)));
            return reply.Flag("ok", true).Flag("cut", cut).Objects("lines", list).ToString();
        }

        internal static string DoneJson(string re, Result result)
        {
            RemoteJson.Writer done = new RemoteJson.Writer().Text("type", "memory.done").Text("re", re).Flag("ok", result.Ok).Text("code", result.Code).Text("say", RemoteMessages.CleanText(result.Say, 200));
            if (result.Undo != null) done.Text("undo", result.Undo);
            return done.ToString();
        }

        internal static bool Asked(string type)
        {
            return type == "memory.get" || type == "memory.edit" || type == "memory.forget" || type == "memory.add" || type == "memory.day" || type == "memory.forget-day" || type == "memory.wipe" || type == "memory.undo";
        }

        internal static List<string> Answer(RemoteJson.Map message)
        {
            List<string> replies = new List<string>();
            string type = message.Text("type"), id = message.Text("id");
            if (type == "memory.get") { replies.Add(List()); return replies; }
            if (!RemoteMessages.IdOk(id)) { replies.Add(RemoteMessages.Error("bad-message", null)); return replies; }
            if (type == "memory.day") { replies.Add(DayJson(id, message.Text("date"))); return replies; }
            Result result;
            string item = message.Text("item"), text = message.Text("text");
            switch (type)
            {
                case "memory.edit": result = item == null || text == null || text.Length > 2000 ? null : Edit(item, text); break;
                case "memory.forget": result = item == null ? null : Forget(item); break;
                case "memory.add": result = text == null || text.Length > 2000 ? null : Add(text); break;
                case "memory.forget-day": result = ForgetDay(message.Text("date")); break;
                case "memory.wipe": result = message.Text("what") == null ? null : Wipe(message.Text("what")); break;
                case "memory.undo": result = message.Text("undo") == null ? null : Undo(message.Text("undo")); break;
                default: result = null; break;
            }
            if (result == null) { replies.Add(RemoteMessages.Error("bad-message", id)); return replies; }
            replies.Add(DoneJson(id, result));
            replies.Add(List());
            return replies;
        }

        internal static void Smoke(List<string> checks)
        {
            string folder = Path.Combine(Path.GetTempPath(), "capri-memory-book-" + Guid.NewGuid().ToString("N"));
            string notesWas = Brain.MemoryPath, memoriesWas = Brain.MemoriesPath, transcriptsWas = Brain.TranscriptPath, gamesWas = GameNotes.Folder;
            bool keepingWas = Brain.Keeping;
            try
            {
                Directory.CreateDirectory(Path.Combine(folder, "transcripts"));
                Directory.CreateDirectory(Path.Combine(folder, "games"));
                Brain.MemoryPath = Path.Combine(folder, "me.md");
                Brain.MemoriesPath = Path.Combine(folder, "memories.md");
                Brain.TranscriptPath = Path.Combine(folder, "transcripts");
                GameNotes.Folder = Path.Combine(folder, "games");
                Brain.Keeping = true;
                File.WriteAllText(Brain.MemoryPath, Brain.NotesHead + "- Pronouns: she/her\n- They like cats\n- They like cats\n");
                File.WriteAllText(Brain.MemoriesPath, Brain.MemoriesHead + "## Wednesday 28 October 2026\n- They beat Malenia after 40 tries.\n\n## Thursday 29 October 2026\n- They started learning piano.\n- I helped them fix their mic.\n");
                File.WriteAllText(Path.Combine(Brain.TranscriptPath, "2026-10-28.md"), "# Capri and you, Wednesday 28 October 2026\n> Kept on this PC only.\n\n**10:00 You:** hi\n**10:00 Capri:** hey choom\n*10:01 She glanced at your screen: Elden Ring.*\n");
                File.WriteAllText(Path.Combine(Brain.TranscriptPath, "2026-10-29.md"), "# Capri and you\n\n**09:00 You:** piano time\n");
                File.WriteAllText(Path.Combine(Brain.TranscriptPath, "unfolded.txt"), "2026-10-28 10:00 You: hi\n2026-10-29 09:00 You: piano time\n");
                File.WriteAllText(Path.Combine(GameNotes.Folder, "fortnite.md"), "# Fortnite\n\n- Sprint is shift\n");
                File.WriteAllText(Path.Combine(GameNotes.Folder, "fortnite.controls.md"), "# Controls for Fortnite\n\nW forward\n");

                Func<string, RemoteJson.Map> list = delegate(string json) { RemoteJson.Map map = RemoteJson.ParseMap(json); if (map == null || map.Text("type") != "memory.list") throw new Exception("memory.list didn't parse."); return map; };
                Func<RemoteJson.Map, string, List<RemoteJson.Map>> rows = delegate(RemoteJson.Map map, string name)
                {
                    List<RemoteJson.Map> found = new List<RemoteJson.Map>();
                    foreach (object row in map.Items(name) ?? new List<object>()) found.Add(row as RemoteJson.Map);
                    return found;
                };
                RemoteJson.Map first = list(List());
                List<RemoteJson.Map> about = rows(first, "about"), chats = rows(first, "chats"), games = rows(first, "games"), days = rows(first, "days");
                if (about.Count != 3 || about[0].Text("text") != "Pronouns: she/her" || about[1].Text("id") == about[2].Text("id")) throw new Exception("me.md's notes didn't list once each with their own ids.");
                if (chats.Count != 2 || chats[1].Text("day") != "Thursday 29 October 2026" || rows(chats[1], "items").Count != 2) throw new Exception("memories.md didn't list by day.");
                if (games.Count != 1 || games[0].Text("game") != "Fortnite" || rows(games[0], "items").Count != 1) throw new Exception("Game notes didn't list, or a controls file was listed.");
                if (days.Count != 2 || days[0].Text("date") != "2026-10-29" || !first.Is("keeping", true)) throw new Exception("Chat days didn't list newest first.");

                string cats = about[1].Text("id");
                Result edited = Edit(cats, "they love cats and dogs");
                if (!edited.Ok || edited.Undo == null || !File.ReadAllText(Brain.MemoryPath).Contains("- They love cats and dogs\n- They like cats")) throw new Exception("Editing a note didn't change just that line.");
                Result secret = Edit(about[0].Text("id"), "my password is hunter2");
                if (secret.Ok || secret.Code != "private" || File.ReadAllText(Brain.MemoryPath).Contains("hunter2")) throw new Exception("A password was saved through an edit.");
                Result undone = Undo(edited.Undo);
                if (!undone.Ok || File.ReadAllText(Brain.MemoryPath).Contains("dogs") || Undo(edited.Undo).Code != "gone") throw new Exception("Undo didn't put a note back once.");
                Result pronouns = Edit(about[0].Text("id"), "Pronouns: they/them");
                if (!pronouns.Ok || Edit(about[0].Text("id"), "anything").Code != "changed") throw new Exception("An edit to a note that had changed went through.");
                Result later = Add("they collect vinyl");
                if (Undo(pronouns.Undo).Code != "changed" || !Undo(later.Undo).Ok || !Undo(pronouns.Undo).Ok || !File.ReadAllText(Brain.MemoryPath).Contains("she/her")) throw new Exception("Undo went over a later change, or didn't work in order.");

                string mic = rows(chats[1], "items")[1].Text("id"), malenia = rows(chats[0], "items")[0].Text("id");
                Result forgot = Forget(malenia);
                string memories = File.ReadAllText(Brain.MemoriesPath);
                if (!forgot.Ok || memories.Contains("Malenia") || memories.Contains("Wednesday") || !memories.Contains("## Thursday 29 October 2026\n- They started learning piano.\n- I helped them fix their mic.")) throw new Exception("Forgetting a day's only memory left its heading or touched the others.");
                Result micGone = Forget(mic);
                if (!micGone.Ok || File.ReadAllText(Brain.MemoriesPath).Contains("mic")) throw new Exception("Forgetting a chat memory didn't work.");
                if (!Undo(micGone.Undo).Ok || !Undo(forgot.Undo).Ok || !File.ReadAllText(Brain.MemoriesPath).Contains("mic") || !File.ReadAllText(Brain.MemoriesPath).Contains("Malenia")) throw new Exception("Undo didn't bring a forgotten chat memory back.");

                Result added = Add("they main Sukuna");
                if (!added.Ok || added.Code != "added" || !File.ReadAllText(Brain.MemoryPath).Contains("- They main Sukuna")) throw new Exception("Adding a note from Remote didn't save it.");
                if (Add("my phone number is 0412 345 678").Code != "private" || Add("they main Sukuna").Code != "same" || Add("   ").Code != "empty") throw new Exception("Adding a note didn't refuse personal details, repeats or nothing.");

                bool cut;
                List<Said> said = ReadDay("2026-10-28", out cut);
                if (said == null || said.Count != 3 || said[1].Who != "Capri" || said[1].Text != "hey choom" || said[2].Who != "" || cut) throw new Exception("A chat day didn't read back line by line.");
                if (ReadDay("../me", out cut) != null || ReadDay("2026-13-45", out cut) != null || ForgetDay("..\\me").Ok) throw new Exception("A day outside her transcripts was read or deleted.");
                Result day = ForgetDay("2026-10-28");
                if (!day.Ok || File.Exists(Path.Combine(Brain.TranscriptPath, "2026-10-28.md")) || File.ReadAllText(Brain.QueueFile).Contains("2026-10-28")) throw new Exception("Forgetting a day left its transcript or its lines waiting to become memories.");
                if (!Undo(day.Undo).Ok || !File.Exists(Path.Combine(Brain.TranscriptPath, "2026-10-28.md")) || !File.ReadAllText(Brain.QueueFile).Contains("2026-10-28 10:00 You: hi")) throw new Exception("Undo didn't bring a forgotten day back.");

                Result all = Wipe("all");
                if (!all.Ok || Parse(About, Brain.MemoryPath, File.ReadAllText(Brain.MemoryPath)).Count != 0 || Parse(Chats, Brain.MemoriesPath, File.ReadAllText(Brain.MemoriesPath)).Count != 0 || DayList().Count != 0 || File.ReadAllText(Brain.QueueFile).Length != 0 || GameFiles().Count != 1) throw new Exception("Forget everything left something about them, or took her game notes.");
                if (!Undo(all.Undo).Ok || !File.ReadAllText(Brain.MemoryPath).Contains("Sukuna") || DayList().Count != 2) throw new Exception("Undo didn't bring everything back.");
                if (Wipe("nonsense").Ok) throw new Exception("An unknown wipe went through.");

                List<string> replies = Answer(RemoteJson.ParseMap("{\"type\":\"memory.forget\",\"id\":\"f1\",\"item\":\"" + cats + "\"}"));
                RemoteJson.Map done = RemoteJson.ParseMap(replies[0]);
                if (replies.Count != 2 || done.Text("type") != "memory.done" || done.Text("re") != "f1" || !done.Is("ok", true) || done.Text("undo") == null || list(replies[1]) == null) throw new Exception("memory.forget didn't answer with memory.done and a fresh list.");
                if (RemoteJson.ParseMap(Answer(RemoteJson.ParseMap("{\"type\":\"memory.edit\",\"id\":\"bad id!\",\"item\":\"x\",\"text\":\"y\"}"))[0]).Text("code") != "bad-message") throw new Exception("A bad message id was taken.");
                RemoteJson.Map chat = RemoteJson.ParseMap(Answer(RemoteJson.ParseMap("{\"type\":\"memory.day\",\"id\":\"d1\",\"date\":\"2026-10-29\"}"))[0]);
                if (chat.Text("type") != "memory.chat" || !chat.Is("ok", true) || rows(chat, "lines").Count != 1) throw new Exception("memory.day didn't answer with the day's lines.");

                StringBuilder big = new StringBuilder(Brain.MemoriesHead);
                for (int i = 0; i < 600; i++) big.Append("## Day " + i + "\n- " + new string('x', 180) + " " + i + "\n");
                File.WriteAllText(Brain.MemoriesPath, big.ToString());
                string huge = List();
                if (Encoding.UTF8.GetByteCount(huge) > Most || !list(huge).Is("cut", true)) throw new Exception("A long memory list went over the link's size.");

                RemoteLink link = new RemoteLink(RemoteState.Fresh(), "ws://127.0.0.1:1", "Test PC", "test", null);
                if (Array.IndexOf(link.FeaturesNow, Feature) < 0) throw new Exception("Her memory wasn't offered at hello.");
            }
            finally
            {
                Brain.MemoryPath = notesWas;
                Brain.MemoriesPath = memoriesWas;
                Brain.TranscriptPath = transcriptsWas;
                GameNotes.Folder = gamesWas;
                Brain.Keeping = keepingWas;
                lock (undoGate) undo.Clear();
                try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            checks.Add("PASS her memory on Remote: notes, chat memories, game notes and chat days list with stable ids; edit, forget, add and forget a day change only that, refuse personal details and stale ids, drop a forgotten day's waiting lines, undo puts each back once, forget everything keeps her game notes, and the list fits the link");
        }
    }
}

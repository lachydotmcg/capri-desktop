using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    [DataContract]
    internal sealed class Deed
    {
        [DataMember(EmitDefaultValue = false)] public string Id;
        [DataMember(EmitDefaultValue = false)] public string When;
        [DataMember(EmitDefaultValue = false)] public string Tool;
        [DataMember(EmitDefaultValue = false)] public string What;
        [DataMember(EmitDefaultValue = false)] public List<string> Steps;
        [DataMember(EmitDefaultValue = false)] public int StepCount;
        [DataMember(EmitDefaultValue = false)] public string Ended;
        [DataMember(EmitDefaultValue = false)] public bool Failed;
        [DataMember(EmitDefaultValue = false)] public string UndoTool;
        [DataMember(EmitDefaultValue = false)] public string UndoAction;
        [DataMember(EmitDefaultValue = false)] public string UndoName;
        [DataMember(EmitDefaultValue = false)] public string UndoLevel;
        [DataMember(EmitDefaultValue = false)] public string UndoText;
        [DataMember(EmitDefaultValue = false)] public string Files;
        [DataMember(EmitDefaultValue = false)] public string Kept;
        [DataMember(EmitDefaultValue = false)] public bool Undone;
        [DataMember(EmitDefaultValue = false)] public string Back;

        internal bool Undoable { get { return !Undone && !Failed && (UndoTool != null || Files != null); } }

        internal bool Job { get { return Steps != null; } }

        internal DateTime At
        {
            get
            {
                DateTime at;
                return DateTime.TryParseExact(When ?? "", "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out at) ? at.ToLocalTime() : DateTime.MinValue;
            }
        }

        internal ToolArguments UndoArguments() { return new ToolArguments { Action = UndoAction, Name = UndoName, Level = UndoLevel, Text = UndoText }; }

        internal Deed Copy()
        {
            Deed copy = (Deed)MemberwiseClone();
            if (Steps != null) copy.Steps = new List<string>(Steps);
            return copy;
        }
    }

    internal static class Timeline
    {
        internal const string Description = "List what you did on their computer lately, newest first and numbered, or undo one of your own actions when they ask, like undo that, put it back, change it back or what did you just do. undo with no number takes back your newest action; number undoes that one from list. Opening apps and sites, music, your settings, notes, minimizing and file changes can be undone. Clicks, typing and mod tools can't, so say so plainly.";
        internal const string Schema = "{\"type\":\"object\",\"properties\":{\"action\":{\"type\":\"string\",\"description\":\"list by default, or undo\",\"enum\":[\"list\",\"undo\"]},\"number\":{\"type\":\"integer\",\"description\":\"undo: which one, by its number from list; leave out for the newest\"}}}";
        internal const int Kept = 400, StepsKept = 40, Listed = 12, Days = 30;
        internal const string JobKept = "Clicks and typing can't be taken back from here. Ctrl+Z in that app may undo what I typed.";
        internal const string ModKept = "That ran in a mod or plug-in, outside me, so I can't take it back.";
        internal const string PlainKept = "I can't take that one back.";
#if CROSS
        private const string Older = "I keep older ones too, going back 30 days.";
#else
        private const string Older = "Older ones are in Workspace, in What she did.";
#endif

        internal static Action<Deed> Changed;
        internal static Func<string, ToolArguments, string> Run = delegate(string tool, ToolArguments args) { return Hands.Do(tool, args); };
        internal static Func<string, string> UndoFiles = delegate(string id) { return Hands.Keeper.Undo(id); };
        internal static Func<string, bool> FilesBack = delegate(string id) { return Hands.Keeper.Recent().Exists(delegate(FileChange change) { return change.Id == id && change.Undone; }); };
        internal static Func<DateTime> Now = delegate { return DateTime.UtcNow; };

        private static readonly object gate = new object();
        private static readonly List<Deed> deeds = new List<Deed>();
        private static readonly Dictionary<object, Deed> jobs = new Dictionary<object, Deed>();
        private static readonly HashSet<string> undoing = new HashSet<string>(StringComparer.Ordinal);
        private static SavedList<Deed> saved;
        private static DateTime savedAt;
        [ThreadStatic] private static int hushed;

        internal static string Folder { get; private set; }

        internal static void Open(string folder)
        {
            lock (gate)
            {
                deeds.Clear();
                jobs.Clear();
                Folder = folder;
                saved = string.IsNullOrEmpty(folder) ? null : new SavedList<Deed>(Path.Combine(folder, "timeline.json"), "timeline");
                if (saved != null) deeds.AddRange(saved.Read(delegate(Deed deed) { return !string.IsNullOrEmpty(deed.Id) && !string.IsNullOrEmpty(deed.When) && !string.IsNullOrEmpty(deed.What); }));
                Trim();
            }
        }

        internal static bool Quiet { get { return hushed > 0; } }

        internal static void Hush(bool on) { hushed = Math.Max(0, hushed + (on ? 1 : -1)); }

        internal static bool Keeps(string tool, ToolArguments args)
        {
            if (Quiet || args == null) return false;
            string action = args.Action ?? "";
            switch (tool)
            {
                case "media": return action != "now_playing" && action != "volume_now";
                case "open_app": case "open_website": case "play_music": case "shortcut": case "power": case "gaming_mode": case "clip": case "desktop_icons":
                case "playlist": case "mod": case "call_errand": case "call_say": case "watch_along": case "run_terminal": case "linux_system":
                    return true;
                case "app_window": case "remember": return true;
                case "capri_settings": return action == "on" || action == "off" || action == "set";
                case "files": return FileKeeper.Changes(args) || action == "make_folder";
                case "website_nickname": case "remind": case "routine": case "watch_for": return action.Length > 0 && action != "list" && action != "check";
                case "app_volume": return action.Length > 0 && action != "now";
                case "terminal_job": return action.Length > 0 && action != "status" && action != "read";
                default: return false;
            }
        }

        internal static Deed Add(Deed deed)
        {
            if (deed == null || string.IsNullOrWhiteSpace(deed.What)) return null;
            Deed shown;
            lock (gate)
            {
                deed.Id = Guid.NewGuid().ToString("N");
                deed.When = Now().ToString("o", CultureInfo.InvariantCulture);
                deed.What = Line(deed.What, 200);
                if (deed.Failed || deed.Undoable) deed.Kept = null;
                else if (string.IsNullOrEmpty(deed.Kept)) deed.Kept = PlainKept;
                deeds.Add(deed);
                Trim();
                Save(true);
                shown = deed.Copy();
            }
            Tell(shown);
            return shown;
        }

        internal static void Step(object job, string task, string label, bool failed)
        {
            if (job == null || string.IsNullOrWhiteSpace(label)) return;
            lock (gate)
            {
                Deed deed;
                if (!jobs.TryGetValue(job, out deed))
                {
                    deed = new Deed { Id = Guid.NewGuid().ToString("N"), When = Now().ToString("o", CultureInfo.InvariantCulture), Tool = "use_computer", What = Line(string.IsNullOrWhiteSpace(task) ? "A job on your computer" : task, 200), Steps = new List<string>(), Kept = JobKept };
                    jobs[job] = deed;
                    deeds.Add(deed);
                    Trim();
                }
                deed.StepCount++;
                deed.Steps.Add(Line((failed ? "\u2717 " : "") + label, 120));
                if (deed.Steps.Count > StepsKept) deed.Steps.RemoveRange(0, deed.Steps.Count - StepsKept);
                Save(false);
            }
        }

        internal static void Ended(object job, string outcome)
        {
            if (job == null) return;
            Deed shown;
            lock (gate)
            {
                Deed deed;
                if (!jobs.TryGetValue(job, out deed)) return;
                jobs.Remove(job);
                deed.Ended = string.IsNullOrEmpty(outcome) ? "stopped" : outcome;
                Save(true);
                shown = deed.Copy();
            }
            Tell(shown);
        }

        internal static List<Deed> Recent()
        {
            List<Deed> copies = new List<Deed>();
            lock (gate) foreach (Deed deed in deeds) copies.Add(deed.Copy());
            foreach (Deed deed in copies) Refresh(deed);
            return copies;
        }

        internal static Deed Find(string id)
        {
            lock (gate)
            {
                Deed deed = deeds.Find(delegate(Deed one) { return one.Id == id; });
                if (deed == null) return null;
                Deed copy = deed.Copy();
                Refresh(copy);
                return copy;
            }
        }

        internal static string Do(ToolArguments args)
        {
            if (args == null) args = new ToolArguments();
            if (args.Action == "undo") return Undo(args.Number);
            return List();
        }

        internal static string List()
        {
            List<Deed> recent = Recent();
            if (recent.Count == 0) return "I haven't done anything on this computer yet that I keep track of.";
            StringBuilder text = new StringBuilder("What I did, newest first:");
            int number = 0;
            for (int i = recent.Count - 1; i >= 0 && number < Listed; i--)
            {
                Deed deed = recent[i];
                text.Append('\n').Append(++number).Append(". ").Append(deed.At.ToString("ddd h:mm tt", CultureInfo.InvariantCulture)).Append(": ").Append(Said(deed));
            }
            if (recent.Count > Listed) text.Append("\n").Append(Older);
            return text.ToString();
        }

        internal static string Said(Deed deed)
        {
            string what = deed.Job ? "a job, " + Voice.Quote(deed.What) + ", " + deed.StepCount + (deed.StepCount == 1 ? " step" : " steps") + (deed.Ended == null ? ", still going" : deed.Ended == "done" ? "" : ", " + deed.Ended) : deed.What.TrimEnd('.');
            string state = deed.Undone ? "undone" : deed.Failed ? "didn't work" : deed.Undoable ? "can undo" : "can't undo: " + (deed.Kept ?? PlainKept).TrimEnd('.');
            return what + " (" + state + ")";
        }

        internal static Deed Target(ToolArguments args)
        {
            if (args == null || args.Action != "undo") return null;
            List<Deed> recent = Recent();
            if (args.Number != null)
            {
                int index = recent.Count - args.Number.Value;
                return args.Number.Value < 1 || index < 0 ? null : recent[index];
            }
            for (int i = recent.Count - 1; i >= 0; i--)
                if (!recent[i].Undone && !recent[i].Failed) return recent[i];
            return null;
        }

        internal static string Undo(int? number)
        {
            Deed deed = Target(new ToolArguments { Action = "undo", Number = number });
            if (deed != null) return Undo(deed.Id);
            return number != null ? "There's no number " + number.Value + " in what I did. Call timeline with list to see the numbers." : "There's nothing of mine to undo.";
        }

        internal static string Undo(string id)
        {
            Deed deed = Find(id);
            if (deed == null) return "I don't have that one any more.";
            if (deed.Undone) return "Already put back: " + deed.What.TrimEnd('.') + ".";
            if (deed.Failed) return "That one didn't work in the first place, so there's nothing to undo.";
            if (!deed.Undoable) return "I can't undo " + (deed.Job ? "that job" : Voice.Quote(deed.What.TrimEnd('.'))) + ". " + (deed.Kept ?? PlainKept);
            lock (gate) if (!undoing.Add(deed.Id)) return "I'm already putting that back.";
            string result;
            try
            {
                Hush(true);
                try { result = deed.Files != null ? UndoFiles(deed.Files) : Run(deed.UndoTool, deed.UndoArguments()); }
                finally { Hush(false); }
            }
            catch (Exception error) { result = Hands.Problem(error); }
            finally { lock (gate) undoing.Remove(deed.Id); }
            bool back = deed.Files != null ? !Regex.IsMatch(result ?? "", @"^(There's nothing|Couldn't)") : !Hands.Failed(result);
            if (!back) return "Couldn't undo " + Voice.Quote(deed.What.TrimEnd('.')) + ": " + (result ?? "").TrimEnd('.') + ".";
            Deed shown;
            lock (gate)
            {
                Deed kept = deeds.Find(delegate(Deed one) { return one.Id == deed.Id; });
                if (kept == null) return result;
                kept.Undone = true;
                kept.Back = Line(result, 200);
                Save(true);
                shown = kept.Copy();
            }
            Tell(shown);
            return "Undid it: " + deed.What.TrimEnd('.') + ". " + result;
        }

        internal static string Line(string text, int most)
        {
            string line = Regex.Replace(text ?? "", @"\s+", " ").Trim();
            return line.Length > most ? line.Substring(0, most - 3).TrimEnd() + "..." : line;
        }

        private static void Refresh(Deed deed)
        {
            if (deed.Files == null || deed.Undone) return;
            try { if (FilesBack(deed.Files)) deed.Undone = true; }
            catch (Exception) { }
        }

        private static void Trim()
        {
            DateTime oldest = Now().AddDays(-Days);
            deeds.RemoveAll(delegate(Deed deed)
            {
                if (jobs.ContainsValue(deed)) return false;
                DateTime at;
                return DateTime.TryParseExact(deed.When, "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out at) && at.ToUniversalTime() < oldest;
            });
            if (deeds.Count > Kept) deeds.RemoveRange(0, deeds.Count - Kept);
        }

        private static void Save(bool now)
        {
            if (saved == null) return;
            DateTime at = Now();
            if (!now && at - savedAt < TimeSpan.FromSeconds(3)) return;
            savedAt = at;
            try { saved.Write(deeds, ".saving"); }
            catch (Exception) { }
        }

        private static void Tell(Deed deed)
        {
            Action<Deed> changed = Changed;
            if (changed == null || deed == null) return;
            try { changed(deed); }
            catch (Exception) { }
        }
    }
}

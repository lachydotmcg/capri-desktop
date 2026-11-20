using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    [DataContract]
    internal sealed class HerJob
    {
        [DataMember] public string Id = "";
        [DataMember] public string Task = "";
        [DataMember] public string Repeat = "";
        [DataMember] public string State = "queued";
        [DataMember] public long Added;
        [DataMember] public long Ended;
        [DataMember] public long NextRun;
        [DataMember] public string Answer = "";
        [DataMember] public string Note = "";
        [DataMember] public string After = "";
        [DataMember] public string Replay = "";
        [DataMember] public string Kind = "";
        [DataMember] public long At;
        [DataMember] public int Every;
        [DataMember] public int Minute = -1;
        [DataMember] public int Day = -1;
        [DataMember] public bool Until;
        [DataMember] public int Round;
        [DataMember] public string Progress = "";
        [DataMember] public List<HerUpdate> Updates = new List<HerUpdate>();
        [DataMember] public string Question = "";
        [DataMember] public string Reply = "";
        [DataMember] public string Suggest = "";
        [DataMember] public string Last = "";
        [DataMember] public string Parent = "";
        [DataMember] public long Started;
        [DataMember] public int Waits;

        [OnDeserializing]
        private void Reading(StreamingContext context)
        {
            Minute = -1;
            Day = -1;
        }

        internal bool Watching { get { return Kind == "watch"; } }

        internal HerJob Copy()
        {
            HerJob copy = (HerJob)MemberwiseClone();
            copy.Updates = new List<HerUpdate>();
            if (Updates != null) foreach (HerUpdate update in Updates) if (update != null) copy.Updates.Add(new HerUpdate { At = update.At, Text = update.Text });
            return copy;
        }
    }

    [DataContract]
    internal sealed class HerUpdate
    {
        [DataMember] public long At;
        [DataMember] public string Text = "";
    }

    [DataContract]
    internal sealed class HerJobList
    {
        [DataMember] public List<HerJob> Queue = new List<HerJob>();
        [DataMember] public List<HerJob> Repeats = new List<HerJob>();
        [DataMember] public List<HerJob> Done = new List<HerJob>();
    }

    internal sealed class HerJobs
    {
        internal const int MostQueued = 10, MostRepeats = 6, MostDone = 30, MostUpdates = 8, MostProgress = 2400;
        internal static readonly string[] Repeats = { "", "daily", "weekly" };
        internal static readonly string[] AllRepeats = { "", "daily", "weekly", "hourly" };

        private readonly string path;
        private readonly object gate = new object();
        private HerJobList list;
        internal Func<DateTime> Now = delegate { return DateTime.Now; };

        internal HerJobs(string folder)
        {
            path = Path.Combine(folder, "jobs.json");
            list = Load();
        }

        internal List<HerJob> Queue() { lock (gate) return Copies(list.Queue); }
        internal List<HerJob> Repeating() { lock (gate) return Copies(list.Repeats); }
        internal List<HerJob> Done() { lock (gate) return Copies(list.Done); }
        internal int Waiting { get { lock (gate) return list.Queue.Count; } }

        private static List<HerJob> Copies(List<HerJob> from)
        {
            List<HerJob> copy = new List<HerJob>();
            foreach (HerJob job in from) copy.Add(job.Copy());
            return copy;
        }

        internal HerJob Add(string task, string repeat, string after = "")
        {
            return Plan(new HerJob { Task = task, Repeat = repeat, After = after });
        }

        internal HerJob Plan(HerJob wanted)
        {
            if (wanted == null) return null;
            string task = HerComputer.Short((wanted.Task ?? "").Trim(), 300);
            string after = HerComputer.Short((wanted.After ?? "").Trim(), 300);
            if (task.Length == 0) return null;
            string repeat = Array.IndexOf(AllRepeats, wanted.Repeat ?? "") < 0 ? "" : wanted.Repeat ?? "";
            DateTime now = Now();
            HerJob job = new HerJob
            {
                Id = NewId(), Task = task, Repeat = repeat, Added = Stamp(now), After = repeat.Length > 0 ? "" : after,
                Kind = wanted.Kind == "watch" ? "watch" : "", Every = repeat == "hourly" ? Math.Max(1, Math.Min(24, wanted.Every)) : 0,
                Minute = wanted.Minute >= 0 && wanted.Minute < 1440 ? wanted.Minute : -1, Day = wanted.Day >= 0 && wanted.Day <= 6 ? wanted.Day : -1,
                Until = wanted.Until && wanted.Kind == "watch", At = wanted.At > Stamp(now) ? wanted.At : 0
            };
            lock (gate)
            {
                if (repeat.Length > 0)
                {
                    if (list.Repeats.Count >= MostRepeats) return null;
                    job.State = "repeat";
                    job.NextRun = job.At > 0 ? job.At : Stamp(now);
                    job.At = 0;
                    list.Repeats.Add(job);
                }
                else
                {
                    if (list.Queue.Count >= MostQueued) return null;
                    list.Queue.Add(job);
                }
            }
            Save();
            return job.Copy();
        }

        private static string NewId() { return Guid.NewGuid().ToString("N").Substring(0, 12); }

        internal bool Remove(string id)
        {
            bool gone;
            lock (gate) gone = list.Queue.RemoveAll(job => job.Id == id && job.State != "running") + list.Repeats.RemoveAll(job => job.Id == id) > 0;
            if (gone) Save();
            return gone;
        }

        internal HerJob Find(string id)
        {
            lock (gate)
            {
                HerJob found = list.Queue.Find(job => job.Id == id) ?? list.Repeats.Find(job => job.Id == id) ?? list.Done.Find(job => job.Id == id);
                return found == null ? null : found.Copy();
            }
        }

        internal HerJob Next()
        {
            HerJob next = null;
            bool changed = false;
            lock (gate)
            {
                long now = Stamp(Now());
                foreach (HerJob repeat in list.Repeats)
                {
                    if (repeat.NextRun > now || list.Queue.Exists(job => job.Parent == repeat.Id || job.Task == repeat.Task && job.Parent.Length == 0)) continue;
                    if (list.Queue.Count >= MostQueued) break;
                    list.Queue.Add(new HerJob { Id = NewId(), Task = repeat.Task, Repeat = repeat.Repeat, Added = now, Kind = repeat.Kind, Until = repeat.Until, Last = repeat.Last ?? "", Parent = repeat.Id });
                    repeat.NextRun = Stamp(HerSchedule.Next(repeat.Repeat, repeat.Every, repeat.Minute, repeat.Day, When(Math.Max(now, repeat.NextRun))));
                    changed = true;
                }
                if (!list.Queue.Exists(job => job.State == "running"))
                    foreach (HerJob job in list.Queue)
                        if (job.State == "queued" && job.At <= now)
                        {
                            job.State = "running";
                            if (job.Started == 0) job.Started = now;
                            next = job.Copy();
                            changed = true;
                            break;
                        }
            }
            if (changed) Save();
            return next;
        }

        internal HerJob Running()
        {
            lock (gate)
            {
                HerJob found = list.Queue.Find(job => job.State == "running");
                return found == null ? null : found.Copy();
            }
        }

        internal List<HerJob> Asking()
        {
            lock (gate) return Copies(list.Queue.FindAll(job => job.State == "asking"));
        }

        internal long NextDue()
        {
            lock (gate)
            {
                long soonest = long.MaxValue;
                foreach (HerJob job in list.Queue) if (job.State == "queued") soonest = Math.Min(soonest, job.At);
                foreach (HerJob repeat in list.Repeats) soonest = Math.Min(soonest, repeat.NextRun);
                return soonest;
            }
        }

        internal void Pause(string id, string state, string question, long at, string progress, int round) { Pause(id, state, question, at, progress, round, -1); }

        internal void Pause(string id, string state, string question, long at, string progress, int round, int waits)
        {
            lock (gate)
            {
                HerJob job = list.Queue.Find(one => one.Id == id);
                if (job == null) return;
                job.State = state == "asking" ? "asking" : "queued";
                job.Question = state == "asking" ? HerComputer.Short(question ?? "", 300) : "";
                job.Reply = "";
                job.At = state == "asking" ? 0 : Math.Max(0, at);
                job.Progress = Tail(progress ?? "", MostProgress);
                job.Round = Math.Max(0, round);
                if (waits >= 0) job.Waits = waits;
            }
            Save();
        }

        internal bool Answer(string id, string reply)
        {
            lock (gate)
            {
                HerJob job = list.Queue.Find(one => one.Id == id && one.State == "asking");
                if (job == null || string.IsNullOrWhiteSpace(reply)) return false;
                job.Reply = HerComputer.Short(reply, 300);
                job.State = "queued";
                job.At = 0;
            }
            Save();
            return true;
        }

        internal int AddUpdate(string id, string text)
        {
            text = HerComputer.Short(text ?? "", 400);
            if (text.Length == 0) return -1;
            int count;
            lock (gate)
            {
                HerJob job = list.Queue.Find(one => one.Id == id);
                if (job == null) return -1;
                if (job.Updates == null) job.Updates = new List<HerUpdate>();
                job.Updates.Add(new HerUpdate { At = Stamp(Now()), Text = text });
                while (job.Updates.Count > MostUpdates) job.Updates.RemoveAt(0);
                count = job.Updates.Count;
            }
            Save();
            return count;
        }

        internal void Finish(string id, string state, string answer, string note) { Finish(id, state, answer, note, ""); }

        internal void Finish(string id, string state, string answer, string note, string suggest)
        {
            lock (gate)
            {
                HerJob job = list.Queue.Find(one => one.Id == id);
                if (job == null) return;
                list.Queue.Remove(job);
                HerJob parent = job.Parent.Length == 0 ? null : list.Repeats.Find(one => one.Id == job.Parent);
                if (parent != null && state == "done")
                {
                    parent.Last = HerComputer.Short(answer ?? "", 600);
                    if (parent.Until) list.Repeats.Remove(parent);
                }
                if (state == "quiet")
                {
                    if (parent != null) parent.Ended = Stamp(Now());
                }
                else
                {
                    job.State = state;
                    job.Answer = HerComputer.Short(answer ?? "", 2000);
                    job.Note = note ?? "";
                    job.Suggest = HerComputer.Short(suggest ?? "", 200);
                    job.Question = "";
                    job.Ended = Stamp(Now());
                    list.Done.Insert(0, job);
                    while (list.Done.Count > MostDone) list.Done.RemoveAt(list.Done.Count - 1);
                }
            }
            Save();
        }

        internal static string Tail(string text, int limit)
        {
            text = (text ?? "").Trim();
            return text.Length <= limit ? text : "\u2026" + text.Substring(text.Length - limit + 1);
        }

        internal void SetReplay(string id, string replay)
        {
            bool found = false;
            lock (gate)
            {
                HerJob job = list.Done.Find(one => one.Id == id);
                if (job != null) { job.Replay = replay ?? ""; found = true; }
            }
            if (found) Save();
        }

        internal static long Stamp(DateTime when) { return when.Ticks; }
        internal static DateTime When(long stamp) { return new DateTime(Math.Max(0, Math.Min(DateTime.MaxValue.Ticks, stamp))); }

        internal static string Due(long stamp)
        {
            DateTime when = When(stamp);
            if (when.Date == DateTime.Now.Date) return "today " + when.ToString("h:mm tt", CultureInfo.CurrentCulture).ToLowerInvariant();
            if (when.Date == DateTime.Now.Date.AddDays(1)) return "tomorrow " + when.ToString("h:mm tt", CultureInfo.CurrentCulture).ToLowerInvariant();
            return when.ToString("ddd h:mm tt", CultureInfo.CurrentCulture).ToLowerInvariant();
        }

        internal static string RepeatOf(string said, out string task)
        {
            task = (said ?? "").Trim();
            Match daily = Regex.Match(task, @"\b(?:every ?day|daily|every morning|each morning|every night|each day)\b[,.]?", RegexOptions.IgnoreCase);
            Match weekly = Regex.Match(task, @"\b(?:every ?week|weekly|once a week|every (?:monday|tuesday|wednesday|thursday|friday|saturday|sunday))\b[,.]?", RegexOptions.IgnoreCase);
            Match found = daily.Success ? daily : weekly;
            if (!found.Success) return "";
            task = Regex.Replace((task.Substring(0, found.Index) + " " + task.Substring(found.Index + found.Length)).Trim(), @"\s+", " ").Trim(' ', ',', '.');
            return daily.Success ? "daily" : "weekly";
        }

        private HerJobList Load()
        {
            HerJobList loaded = null;
            try
            {
                if (File.Exists(path))
                    using (FileStream stream = File.OpenRead(path)) loaded = new DataContractJsonSerializer(typeof(HerJobList)).ReadObject(stream) as HerJobList;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SerializationException) { }
            catch (InvalidCastException) { }
            return Fixed(loaded);
        }

        internal static HerJobList Fixed(HerJobList loaded)
        {
            HerJobList fixedList = loaded ?? new HerJobList();
            if (fixedList.Queue == null) fixedList.Queue = new List<HerJob>();
            if (fixedList.Repeats == null) fixedList.Repeats = new List<HerJob>();
            if (fixedList.Done == null) fixedList.Done = new List<HerJob>();
            foreach (List<HerJob> jobs in new[] { fixedList.Queue, fixedList.Repeats, fixedList.Done })
                jobs.RemoveAll(job => job == null || string.IsNullOrWhiteSpace(job.Task) || string.IsNullOrEmpty(job.Id));
            foreach (List<HerJob> jobs in new[] { fixedList.Queue, fixedList.Repeats, fixedList.Done })
                foreach (HerJob job in jobs)
                {
                    if (job.After == null) job.After = "";
                    if (job.Replay == null) job.Replay = "";
                    if (job.Kind != "watch") job.Kind = "";
                    if (job.Progress == null) job.Progress = "";
                    if (job.Question == null) job.Question = "";
                    if (job.Reply == null) job.Reply = "";
                    if (job.Suggest == null) job.Suggest = "";
                    if (job.Last == null) job.Last = "";
                    if (job.Parent == null) job.Parent = "";
                    if (job.Answer == null) job.Answer = "";
                    if (job.Note == null) job.Note = "";
                    if (job.Updates == null) job.Updates = new List<HerUpdate>();
                    job.Updates.RemoveAll(update => update == null || string.IsNullOrEmpty(update.Text));
                    if (job.Minute < -1 || job.Minute >= 1440) job.Minute = -1;
                    if (job.Day < -1 || job.Day > 6) job.Day = -1;
                }
            foreach (HerJob job in fixedList.Queue)
            {
                job.State = job.State == "asking" && job.Question.Length > 0 ? "asking" : "queued";
                if (Array.IndexOf(AllRepeats, job.Repeat ?? "") < 0) job.Repeat = "";
            }
            fixedList.Repeats.RemoveAll(job => job.Repeat != "daily" && job.Repeat != "weekly" && job.Repeat != "hourly");
            foreach (HerJob job in fixedList.Repeats) if (job.Repeat == "hourly") job.Every = Math.Max(1, Math.Min(24, job.Every));
            while (fixedList.Queue.Count > MostQueued) fixedList.Queue.RemoveAt(fixedList.Queue.Count - 1);
            while (fixedList.Repeats.Count > MostRepeats) fixedList.Repeats.RemoveAt(fixedList.Repeats.Count - 1);
            while (fixedList.Done.Count > MostDone) fixedList.Done.RemoveAt(fixedList.Done.Count - 1);
            return fixedList;
        }

        internal void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temp = path + ".tmp";
                lock (gate)
                {
                    using (FileStream stream = File.Create(temp)) new DataContractJsonSerializer(typeof(HerJobList)).WriteObject(stream, list);
                }
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        internal static string Check(string folder)
        {
            string task;
            if (RepeatOf("every morning check the weather", out task) != "daily" || task != "check the weather") return "Her jobs missed a daily repeat.";
            if (RepeatOf("find roblox news weekly", out task) != "weekly" || task != "find roblox news") return "Her jobs missed a weekly repeat.";
            if (RepeatOf("find me a recipe", out task) != "" || task != "find me a recipe") return "Her jobs saw a repeat that wasn't there.";
            DateTime clock = new DateTime(2026, 11, 18, 9, 0, 0);
            HerJobs jobs = new HerJobs(folder) { Now = delegate { return clock; } };
            HerJob first = jobs.Add("find a recipe", ""), second = jobs.Add("write a poem", ""), daily = jobs.Add("check the weather", "daily");
            if (first == null || second == null || daily == null || jobs.Waiting != 2 || jobs.Repeating().Count != 1) return "Her jobs didn't queue.";
            HerJob running = jobs.Next();
            if (running == null || running.Id != first.Id || jobs.Running() == null) return "Her jobs didn't start the first one.";
            if (jobs.Waiting != 3) return "Her jobs didn't queue a repeat that was due.";
            jobs.Finish(running.Id, "done", "pancakes", "x.md");
            HerJobs again = new HerJobs(folder) { Now = delegate { return clock; } };
            List<HerJob> queue = again.Queue();
            if (again.Done().Count != 1 || again.Done()[0].Answer != "pancakes" || queue.Count != 2 || queue[0].Id != second.Id || queue[0].State != "queued") return "Her jobs didn't come back the way she left them.";
            if (again.Next().Id != second.Id || again.Waiting != 2) return "Her jobs ran a repeat twice in a day.";
            clock = clock.AddDays(1).AddMinutes(1);
            again.Finish(second.Id, "done", "roses", "");
            again.Remove(again.Queue()[0].Id);
            again.Next();
            if (again.Running() == null || again.Running().Task != "check the weather") return "Her jobs missed tomorrow's repeat.";
            HerJob chained = again.Add("look up the pirate token", "", "load up fortnite and play it");
            if (chained == null || chained.After != "load up fortnite and play it" || new HerJobs(folder).Queue().Find(job => job.Id == chained.Id).After != chained.After) return "Her jobs lost what to do after.";
            for (int i = 0; i < MostQueued + 3; i++) again.Add("job " + i, "");
            if (again.Waiting != MostQueued) return "Her jobs piled up past the limit.";
            if (Fixed(new HerJobList { Queue = null, Repeats = new List<HerJob> { new HerJob { Id = "a", Task = "x", Repeat = "minutely" } }, Done = new List<HerJob> { null } }).Repeats.Count != 0) return "Her jobs kept a broken repeat.";
            using (MemoryStream older = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{\"Queue\":[],\"Repeats\":[{\"Id\":\"b\",\"Task\":\"check the weather\",\"Repeat\":\"daily\",\"State\":\"repeat\"}],\"Done\":[]}")))
            {
                HerJobList kept = Fixed(new DataContractJsonSerializer(typeof(HerJobList)).ReadObject(older) as HerJobList);
                if (kept.Repeats.Count != 1 || kept.Repeats[0].Minute != -1 || kept.Repeats[0].Day != -1) return "Her jobs from before set times moved to midnight.";
            }
            return null;
        }
    }
}

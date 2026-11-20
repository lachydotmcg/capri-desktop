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

        internal HerJob Copy()
        {
            return new HerJob { Id = Id, Task = Task, Repeat = Repeat, State = State, Added = Added, Ended = Ended, NextRun = NextRun, Answer = Answer, Note = Note, After = After, Replay = Replay };
        }
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
        internal const int MostQueued = 10, MostRepeats = 6, MostDone = 30;
        internal static readonly string[] Repeats = { "", "daily", "weekly" };

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
            task = HerComputer.Short((task ?? "").Trim(), 300);
            after = HerComputer.Short((after ?? "").Trim(), 300);
            if (task.Length == 0) return null;
            repeat = Array.IndexOf(Repeats, repeat ?? "") < 0 ? "" : repeat ?? "";
            DateTime now = Now();
            HerJob job = new HerJob { Id = Guid.NewGuid().ToString("N").Substring(0, 12), Task = task, Repeat = repeat, Added = Stamp(now), After = repeat.Length > 0 ? "" : after };
            lock (gate)
            {
                if (repeat.Length > 0)
                {
                    if (list.Repeats.Count >= MostRepeats) return null;
                    job.State = "repeat";
                    job.NextRun = Stamp(now);
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

        internal bool Remove(string id)
        {
            bool gone;
            lock (gate) gone = list.Queue.RemoveAll(job => job.Id == id) + list.Repeats.RemoveAll(job => job.Id == id) > 0;
            if (gone) Save();
            return gone;
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
                    if (repeat.NextRun > now || list.Queue.Exists(job => job.Task == repeat.Task)) continue;
                    if (list.Queue.Count >= MostQueued) break;
                    list.Queue.Add(new HerJob { Id = Guid.NewGuid().ToString("N").Substring(0, 12), Task = repeat.Task, Repeat = repeat.Repeat, Added = now });
                    repeat.NextRun = Stamp(When(now).AddDays(repeat.Repeat == "weekly" ? 7 : 1));
                    changed = true;
                }
                if (!list.Queue.Exists(job => job.State == "running"))
                    foreach (HerJob job in list.Queue)
                        if (job.State == "queued") { job.State = "running"; next = job.Copy(); changed = true; break; }
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

        internal void Finish(string id, string state, string answer, string note)
        {
            lock (gate)
            {
                HerJob job = list.Queue.Find(one => one.Id == id);
                if (job == null) return;
                list.Queue.Remove(job);
                job.State = state;
                job.Answer = HerComputer.Short(answer ?? "", 2000);
                job.Note = note ?? "";
                job.Ended = Stamp(Now());
                list.Done.Insert(0, job);
                while (list.Done.Count > MostDone) list.Done.RemoveAt(list.Done.Count - 1);
            }
            Save();
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
            foreach (List<HerJob> jobs in new[] { fixedList.Queue, fixedList.Repeats, fixedList.Done }) foreach (HerJob job in jobs) { if (job.After == null) job.After = ""; if (job.Replay == null) job.Replay = ""; }
            foreach (HerJob job in fixedList.Queue) { job.State = "queued"; if (Array.IndexOf(Repeats, job.Repeat ?? "") < 0) job.Repeat = ""; }
            fixedList.Repeats.RemoveAll(job => job.Repeat != "daily" && job.Repeat != "weekly");
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
            if (Fixed(new HerJobList { Queue = null, Repeats = new List<HerJob> { new HerJob { Id = "a", Task = "x", Repeat = "hourly" } }, Done = new List<HerJob> { null } }).Repeats.Count != 0) return "Her jobs kept a broken repeat.";
            return null;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using CapriDesktop;

namespace CapriEverydayJobs
{
    internal static class Program
    {
        private const string Usage = "Usage: Capri.EverydayJobs --brain \"<label>=<spec>\" [--jobs id,id] [--repeat n] [--out results.json] [--md table.md] [--floor percent]\n"
            + "       Capri.EverydayJobs --merge a.json b.json ... --md table.md\n"
            + "       Capri.EverydayJobs --list\n"
            + "--warmup runs the first job once, unscored, so a local server has its prompt cached\n"
            + "Specs: scripted | server:<address>|<model> | key:<ENV_VAR>|<model or default> | claude-plan:<model>";

        private static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string brain = null, only = null, output = null, table = null, claude = "claude";
            int repeat = 1, floor = -1;
            bool warmup = false;
            List<string> merging = null;
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                Func<string> next = () => { if (i + 1 >= args.Length) throw new ArgumentException(arg + " needs a value"); return args[++i]; };
                if (arg == "--brain") brain = next();
                else if (arg == "--jobs") only = next();
                else if (arg == "--repeat") repeat = int.Parse(next(), CultureInfo.InvariantCulture);
                else if (arg == "--out") output = next();
                else if (arg == "--md") table = next();
                else if (arg == "--floor") floor = int.Parse(next(), CultureInfo.InvariantCulture);
                else if (arg == "--claude") claude = next();
                else if (arg == "--warmup") warmup = true;
                else if (arg == "--merge") merging = new List<string>();
                else if (arg == "--list") { foreach (Job job in Jobs.All) Console.WriteLine(job.Id.PadRight(15) + job.Area.PadRight(11) + job.Ask + "  ->  " + job.Goal); return 0; }
                else if (merging != null && !arg.StartsWith("--", StringComparison.Ordinal)) merging.Add(arg);
                else { Console.Error.WriteLine(Usage); return 2; }
            }
            if (merging != null)
            {
                List<JsonObject> brains = new List<JsonObject>();
                foreach (string file in merging) if (File.Exists(file)) brains.Add(Report.Load(file)); else Console.Error.WriteLine("skipped missing " + file);
                string merged = Report.Table(brains);
                if (table != null) File.WriteAllText(table, merged);
                Console.WriteLine(merged);
                return 0;
            }
            if (brain == null) { Console.Error.WriteLine(Usage); return 2; }
            int split = brain.IndexOf('=');
            string label = split > 0 ? brain.Substring(0, split) : brain, spec = split > 0 ? brain.Substring(split + 1) : brain;
            spec = Resolved(spec);
            string kind, model;
            Describe(spec, out kind, out model);

            List<Job> jobs = new List<Job>();
            foreach (Job job in Jobs.All) if (only == null || Array.IndexOf(only.Split(','), job.Id) >= 0) jobs.Add(job);
            string root = Path.Combine(Path.GetTempPath(), "capri-everyday-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            List<KeyValuePair<Job, List<Outcome>>> results = new List<KeyValuePair<Job, List<Outcome>>>();
            try
            {
                if (warmup && jobs.Count > 0)
                {
                    Outcome warm = Run(jobs[0], spec, Path.Combine(root, "warmup"), claude);
                    Console.WriteLine("warmed up in {0:0.0}s{1}", warm.Seconds, warm.Error == null ? "" : " (" + Flat(warm.Error) + ")");
                }
                foreach (Job job in jobs)
                {
                    List<Outcome> runs = new List<Outcome>();
                    for (int n = 0; n < repeat; n++)
                    {
                        Outcome run = Run(job, spec, Path.Combine(root, job.Id + "-" + n), claude);
                        runs.Add(run);
                        Console.WriteLine("{0} {1} [{2}] {3:0.0}s {4}{5}", run.Pass ? "PASS" : "FAIL", job.Id, label, run.Seconds,
                            run.Calls.Count == 0 ? "no tools" : string.Join(", ", run.Calls.ConvertAll(call => call.Shown).ToArray()),
                            run.Pass ? "" : " :: " + run.Why + " :: " + Flat(run.Error ?? run.Reply));
                    }
                    results.Add(new KeyValuePair<Job, List<Outcome>>(job, runs));
                }
            }
            finally
            {
                try { Directory.Delete(root, true); }
                catch (IOException) { }
            }
            JsonObject saved = Report.Brain(label, kind, model, results);
            if (output != null) Report.Save(saved, output);
            string shown = Report.Table(new List<JsonObject> { saved });
            if (table != null) File.WriteAllText(table, shown);
            Console.WriteLine();
            Console.WriteLine(shown);
            int passed = (int)saved["passed"], total = (int)saved["total"];
            if (floor >= 0 && total > 0 && passed * 100 < floor * total) { Console.WriteLine("FAIL pass rate " + passed + "/" + total + " is under " + floor + "%"); return 1; }
            return 0;
        }

        private static string Flat(string text) { text = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim(); return text.Length > 220 ? text.Substring(0, 219) + "…" : text; }

        private static string Resolved(string spec)
        {
            if (!spec.StartsWith("key:", StringComparison.Ordinal) || !spec.EndsWith("|default", StringComparison.Ordinal)) return spec;
            string variable = spec.Substring(4, spec.Length - 4 - "|default".Length), key = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrEmpty(key)) return spec;
            List<string> models = Brain.CloudModels(key);
            string picked = Brain.DefaultCloudModel(Cloud.KindOf(key), models);
            Console.WriteLine("default model for this key: " + (picked ?? "none listed"));
            return picked == null ? spec : "key:" + variable + "|" + picked;
        }

        private static void Describe(string spec, out string kind, out string model)
        {
            if (spec == "scripted") { kind = "scripted"; model = "ideal answers"; return; }
            int colon = spec.IndexOf(':');
            if (colon < 0) throw new ArgumentException("Unknown brain " + spec);
            kind = spec.Substring(0, colon);
            string rest = spec.Substring(colon + 1);
            int bar = rest.LastIndexOf('|');
            model = bar >= 0 ? rest.Substring(bar + 1) : rest;
            if (kind == "key")
            {
                string key = Environment.GetEnvironmentVariable(bar >= 0 ? rest.Substring(0, bar) : rest);
                kind = string.IsNullOrEmpty(key) ? "key" : "key " + (Cloud.KindOf(key) ?? "unknown");
            }
        }

        private static Hands.Kit Kit(bool blind)
        {
            List<string[]> missing = new List<string[]>
            {
                new[] { "run_terminal", "Native terminal jobs are unavailable on this platform." },
                new[] { "terminal_job", "Native terminal jobs are unavailable on this platform." }
            };
            if (blind)
            {
                missing.Add(new[] { "look_at_screen", "this brain can't see pictures" });
                missing.Add(new[] { "watch_for", "this brain can't see pictures" });
            }
            return Hands.KitWithout(missing);
        }

        private static Outcome Run(Job job, string spec, string folder, string claude)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "shopping-list.txt"), Jobs.ShoppingList);
            Brain.MemoryPath = Path.Combine(folder, "me.md");
            Brain.MemoriesPath = Path.Combine(folder, "memories.md");
            Brain.PersonalityPath = Path.Combine(folder, Brain.PersonalityName);
            Brain.TranscriptPath = Path.Combine(folder, "transcripts");
            Brain.LogPath = Path.Combine(folder, "errors.txt");
            Brain.VaultFolder = null;
            Brain.Keeping = false;
            Outcome run = new Outcome();
            FakePc pc = new FakePc(job, run);
            Brain brain = new Brain { OwnSteps = true, CloudWait = 180000 };
            brain.Kit = Kit;
            brain.Act = pc.Act;
            string list = Path.Combine(folder, "shopping-list.txt");
            if (spec == "scripted")
            {
                brain.Remote = new Cloud("sk-proj-everyday-jobs", "scripted");
                brain.Model = "scripted";
                brain.Provider = new ScriptedBrain(job, list, run);
            }
            else if (spec.StartsWith("server:", StringComparison.Ordinal))
            {
                string rest = spec.Substring(7);
                int bar = rest.LastIndexOf('|');
                brain.SetServer(rest.Substring(0, bar), null);
                brain.Model = rest.Substring(bar + 1);
            }
            else if (spec.StartsWith("key:", StringComparison.Ordinal))
            {
                string rest = spec.Substring(4);
                int bar = rest.LastIndexOf('|');
                string key = Environment.GetEnvironmentVariable(rest.Substring(0, bar));
                if (string.IsNullOrEmpty(key)) { run.Error = "no key in " + rest.Substring(0, bar); run.Why = "not run: no key"; return run; }
                string model = rest.Substring(bar + 1);
                brain.Remote = new Cloud(key, model);
                brain.Model = model;
            }
            else if (spec.StartsWith("claude-plan:", StringComparison.Ordinal))
            {
                string model = spec.Substring(12);
                brain.Remote = new Cloud(Plans.ClaudeKey, model);
                brain.Model = model;
                brain.Provider = new ClaudePlan(claude, model, folder, run);
            }
            else throw new ArgumentException("Unknown brain " + spec);
            Stopwatch clock = Stopwatch.StartNew();
            try { run.Reply = brain.Ask(job.AskFor(folder)) ?? ""; }
            catch (BrainException error) { run.Error = error.Message; }
            catch (Exception error) { run.Error = error.GetType().Name + ": " + error.Message; }
            run.Seconds = clock.Elapsed.TotalSeconds;
            run.Why = run.Error != null ? "error: " + run.Error : job.Check(run);
            return run;
        }
    }
}

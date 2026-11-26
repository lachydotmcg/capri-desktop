using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using CapriDesktop;

internal static class Program
{
    private static bool behaviorOnly;

    private static int Main(string[] args)
    {
        try
        {
            behaviorOnly = args.Length > 1 && args[1] == "--behavior";
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Run(Path.Combine(args[0], "all"), new[] { "--no-steps" }, false);
            Run(Path.Combine(args[0], "only"), new[] { "--only", "SmokeReactQuick" }, true);
            Steps(Path.Combine(args[0], "steps"));
            Shards(Path.Combine(args[0], "shards"));
            Groups(Path.Combine(args[0], "groups"));
            SingleStep(Path.Combine(args[0], "single-step"));
            Unavailable(Path.Combine(args[0], "unavailable"));
            if (!behaviorOnly)
            {
                Recovery(Path.Combine(args[0], "empty"), false);
                Recovery(Path.Combine(args[0], "interrupted"), true);
                NestedRecovery(Path.Combine(args[0], "nested"));
                FailedParentIsolation(Path.Combine(args[0], "failed-parent"));
                FailedChildRecovery(Path.Combine(args[0], "failed-child"));
            }
            Console.WriteLine(behaviorOnly ? "PASS smoke behavior preserved" : "PASS smoke section timing artifacts and preserved behavior");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL " + error.Message);
            return 1;
        }
    }

    private static SmokeOptions Settings(string folder, string[] options)
    {
        List<string> args = new List<string>(new[] { "--smoke-test", folder });
        args.AddRange(options);
        return SmokeOptions.Parse(args.ToArray());
    }

    private static void Run(string folder, string[] options, bool filtered)
    {
        Directory.CreateDirectory(folder);
        SmokeOptions settings = Settings(folder, options);
        Check(!settings.RunSteps, "Section selection must not enable steps.");
        SmokeRun.Start(folder, settings);
        List<string> checks = new List<string>();
        Check(SmokeRun.EnterSection("SmokeReactions"), "The selected parent must run.");
        if (filtered) Check(!SmokeRun.EnterSection("SmokeReactFeed"), "An unselected sibling must not run.");
        Check(SmokeRun.EnterSection("SmokeReactQuick"), "The selected child must run.");
        checks.Add("PASS child unchanged");
        SmokeRun.LeaveSection("SmokeReactQuick", checks);
        checks.Add("PASS parent unchanged");
        SmokeRun.LeaveSection("SmokeReactions", checks);
        SmokeRun.ValidateSelection(checks);
        SmokeRun.Complete(checks);
        Check(File.ReadAllLines(Path.Combine(folder, "sections-done.txt")).Length == 2, "Only completed sections must be recorded.");
        string[] progress = File.ReadAllLines(Path.Combine(folder, "progress.txt"));
        Check(progress.Length == 5 && progress[0] == "done closing" && progress[3].Split('\t').Length == 4, "The progress contract must not change.");
        Report(folder, checks);
        if (!behaviorOnly)
        {
            string[][] rows = Timings(folder, 2);
            Check(rows[0][0] == "SmokeReactQuick" && rows[0][1] == "1", "The child must have its own timing row.");
            Check(rows[1][0] == "SmokeReactions" && rows[1][1] == "0", "The parent must have its own timing row.");
            double child = Number(rows[0][2]), parent = Number(rows[1][2]);
            Check(parent >= child && Math.Abs(Number(rows[0][3]) - child) < 0.001, "Nested inclusive timing must include the child.");
            Check(Math.Abs(Number(rows[1][3]) - (parent - child)) <= 0.002, "Own time must not double-count the child.");
        }
        SmokeRun.Start(folder, settings);
        Check(!SmokeRun.EnterSection("SmokeReactions"), "Completed sections must remain skipped on resume.");
        SmokeRun.Complete(checks);
        if (!behaviorOnly) Timings(folder, 2);
    }

    private static void Steps(string folder)
    {
        Directory.CreateDirectory(folder);
        SmokeOptions settings = Settings(folder, new[] { "--steps" });
        Check(settings.RunSteps, "--steps must keep running steps.");
        SmokeRun.Start(folder, settings);
        Check(!SmokeRun.EnterSection("BeforeStep"), "--steps must skip sections outside a step.");
        SmokeRun.Begin("step", "0", 60);
        Check(SmokeRun.EnterSection("InsideStep"), "Sections inside a step must still run.");
        Check(SmokeRun.EnterSection("DeepInsideStep"), "A section inside a step's section must run too.");
        List<string> checks = new List<string>(new[] { "PASS step unchanged" });
        SmokeRun.LeaveSection("DeepInsideStep", checks);
        SmokeRun.LeaveSection("InsideStep", checks);
        SmokeRun.Complete(checks);
        Report(folder, checks);
        if (!behaviorOnly)
        {
            string[][] rows = Timings(folder, 2);
            Check(rows[0][0] == "DeepInsideStep" && rows[1][0] == "InsideStep", "Skipped sections must not get timing rows.");
        }
    }

    private static void Shards(string folder)
    {
        string[] names = { "CheckOcr", "SmokeSounds", "SmokeCamera", "SmokeLookout", "SmokeFetch", "SmokeEmotes", "SmokePerf", "SmokeFirstRun", "SmokeReactions", "SmokeThrows", "SmokeCloudPicker", "SmokeMods", "SmokeLeaks" };
        int[] owners = new int[names.Length];
        for (int part = 0; part < 4; part++)
        {
            string shard = Path.Combine(folder, part.ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(shard);
            SmokeOptions settings = Settings(shard, new[] { "--no-steps", "--shard", part + "/4" });
            Check(!settings.RunSteps, "A shard must not run steps.");
            SmokeRun.Start(shard, settings);
            List<string> checks = new List<string>();
            for (int i = 0; i < names.Length; i++)
            {
                if (!SmokeRun.EnterSection(names[i])) continue;
                owners[i]++;
                Check(SmokeRun.EnterSection("Inside" + names[i]), "A section inside a shard's section must run with it.");
                SmokeRun.LeaveSection("Inside" + names[i], checks);
                checks.Add("PASS " + names[i]);
                SmokeRun.LeaveSection(names[i], checks);
            }
            SmokeRun.ValidateSelection(checks);
            SmokeRun.Complete(checks);
            Report(shard, checks);
        }
        for (int i = 0; i < names.Length; i++) Check(owners[i] == 1, names[i] + " ran in " + owners[i] + " shards instead of one.");
        Check(SmokeRun.ShardOf("SmokeSounds", 4) == SmokeRun.ShardOf("smokesounds", 4), "A shard must not depend on letter case.");
        foreach (string[] bad in new[] { new[] { "--shard", "4/4", "--no-steps" }, new[] { "--shard", "0/0", "--no-steps" }, new[] { "--shard", "1", "--no-steps" }, new[] { "--shard", "-1/4", "--no-steps" }, new[] { "--shard", "0/4" }, new[] { "--shard", "0/4", "--no-steps", "--only", "SmokeSounds" } })
        {
            bool refused = false;
            try { Settings(folder, bad); }
            catch (ArgumentException) { refused = true; }
            Check(refused, "--shard must refuse " + string.Join(" ", bad) + ".");
        }
        Console.WriteLine("PASS every section runs in exactly one of four shards");
    }

    private static string FindCapriSource()
    {
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppDomain.CurrentDomain.BaseDirectory })
        {
            DirectoryInfo dir = new DirectoryInfo(start);
            while (dir != null)
            {
                string candidate = Path.Combine(Path.Combine(dir.FullName, "app"), "Capri.cs");
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
        }
        return null;
    }

    private static void Groups(string folder)
    {
        Dictionary<string, string> owner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        KeyValuePair<string, string[]>[] groups = SmokeRun.AllGroups();
        Check(groups.Length >= 5, "Expected smoke groups.");
        foreach (KeyValuePair<string, string[]> group in groups)
        {
            Check(group.Value.Length > 0, "Group " + group.Key + " must not be empty.");
            foreach (string section in group.Value)
            {
                Check(!string.IsNullOrEmpty(section), "Group " + group.Key + " contains an empty section.");
                Check(!owner.ContainsKey(section), "Section " + section + " is listed in more than one group.");
                owner[section] = group.Key;
                Check(string.Equals(SmokeRun.GroupOf(section), group.Key, StringComparison.OrdinalIgnoreCase), "GroupOf(" + section + ") returned the wrong group.");
            }
        }
        string capriPath = FindCapriSource();
        Check(capriPath != null, "Could not locate app/Capri.cs to verify group coverage.");
        string source = File.ReadAllText(capriPath);
        int start = source.IndexOf("private void RunSmokeSections(", StringComparison.Ordinal);
        int end = source.IndexOf("private static void SmokeRedaction(", start, StringComparison.Ordinal);
        Check(start > 0 && end > start, "Could not locate RunSmokeSections in app/Capri.cs.");
        HashSet<string> inSource = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        inSource.Add("CheckOcr");
        foreach (Match match in Regex.Matches(source.Substring(start, end - start), @"Section\s*\(\s*checks\s*,\s*""([^""]+)"""))
            inSource.Add(match.Groups[1].Value);
        foreach (string section in inSource)
            Check(owner.ContainsKey(section), "Smoke section " + section + " is missing from SmokeRun groups.");
        foreach (string section in owner.Keys)
            Check(inSource.Contains(section), "SmokeRun group lists unknown or removed section " + section + ".");

        string voiceFolder = Path.Combine(folder, "voice");
        Directory.CreateDirectory(voiceFolder);
        SmokeOptions voiceOptions = Settings(voiceFolder, new[] { "--group", "voice" });
        Check(!voiceOptions.RunSteps && voiceOptions.Groups.Length == 1, "--group must disable steps.");
        SmokeRun.Start(voiceFolder, voiceOptions);
        Check(SmokeRun.EnterSection("SmokeVoiceDetails"), "A section in the picked group must run.");
        List<string> checks = new List<string>();
        checks.Add("PASS voice group selected");
        SmokeRun.LeaveSection("SmokeVoiceDetails", checks);
        Check(!SmokeRun.EnterSection("SmokeMods"), "A section outside the picked group must not run.");
        SmokeRun.Complete(checks);
        Check(SmokeRun.GroupSections("Voice").Length > 0, "Expected Voice group sections.");
        SmokeOptions combined = Settings(voiceFolder, new[] { "--group", "Mods,Remote", "--only", "SmokeMods,SmokeTips" });
        Check(combined.Only.Length == 11, "--group and --only must merge without duplicates: got " + combined.Only.Length + ".");
        foreach (string[] bad in new[] { new[] { "--group", "Missing" }, new[] { "--group", "Voice," }, new[] { "--group", "Voice", "--steps" }, new[] { "--group", "Voice", "--no-steps", "--shard", "0/4" } })
        {
            bool refused = false;
            try { Settings(folder, bad); }
            catch (ArgumentException) { refused = true; }
            Check(refused, "--group must refuse " + string.Join(" ", bad) + ".");
        }
        Console.WriteLine("PASS every smoke section belongs to one group and --group selects only its sections");
    }

    private static void SingleStep(string folder)
    {
        Directory.CreateDirectory(folder);
        SmokeOptions step14 = Settings(folder, new[] { "--step", "14" });
        Check(step14.RunSteps && step14.Step == 14, "--step 14 must run steps up to 14.");
        SmokeRun.Start(folder, step14);
        Check(SmokeRun.StartStep(0) == 0 && SmokeRun.StartStep(3) == 0 && SmokeRun.StartStep(14) == 14 && SmokeRun.StartStep(16) == 14 && SmokeRun.StartStep(17) == 17 && SmokeRun.StartStep(22) == 17, "StartStep must resolve each root prerequisite.");
        Check(SmokeRun.LastStep == 14, "LastStep must stop at the requested step.");
        Check(!SmokeRun.EnterSection("SmokeMods"), "--step must skip standalone sections outside steps.");
        SmokeRun.Begin("step", "14", 60);
        Check(SmokeRun.EnterSection("InsideStep14"), "Sections inside the requested step must run.");
        List<string> checks = new List<string>(new[] { "PASS single step" });
        SmokeRun.LeaveSection("InsideStep14", checks);
        SmokeRun.StepDone(14, true, checks);
        Check(SmokeRun.StartStep(15) == 15, "Resume after passing prerequisite must start at the next step.");
        SmokeRun.Complete(checks);
        foreach (string[] bad in new[] { new[] { "--step", "-1" }, new[] { "--step", "23" }, new[] { "--step", "abc" }, new[] { "--step", "0", "--steps" }, new[] { "--step", "0", "--no-steps" }, new[] { "--step", "0", "--only", "SmokeMods" }, new[] { "--step", "0", "--group", "Voice" }, new[] { "--step", "0", "--shard", "0/4" } })
        {
            bool refused = false;
            try { Settings(folder, bad); }
            catch (ArgumentException) { refused = true; }
            Check(refused, "--step must refuse " + string.Join(" ", bad) + ".");
        }
        foreach (int step in new[] { 14, 17 })
        {
            string recovered = Path.Combine(folder, "recover-" + step);
            Directory.CreateDirectory(recovered);
            File.WriteAllText(Path.Combine(recovered, "steps-done.txt"), "FAIL " + step + "\n");
            SmokeRun.Start(recovered, Settings(recovered, new[] { "--step", step.ToString() }));
            Check(SmokeRun.NextStep > SmokeRun.LastStep, "A worker already recorded step " + step + " as failed.");
        }
        Console.WriteLine("PASS --step runs a single step chain and skips recorded failures on recovery");
    }

    private static void Unavailable(string folder)
    {
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(Path.Combine(folder, "smoke-timings.txt"));
        SmokeRun.Start(folder, Settings(folder, new[] { "--no-steps" }));
        Check(SmokeRun.EnterSection("DiagnosticUnavailable"), "Diagnostics must not affect selection.");
        List<string> checks = new List<string>(new[] { "PASS diagnostic failure does not change checks" });
        SmokeRun.LeaveSection("DiagnosticUnavailable", checks);
        SmokeRun.Complete(checks);
        Report(folder, checks);
        string invalid = Path.Combine(folder, "invalid");
        Directory.CreateDirectory(Path.Combine(invalid, "smoke-timings.txt"));
        int exit = SmokeRun.Run(new[] { "--smoke-test", invalid, "--unknown" });
        Check(exit == 1, "The command must keep its existing failure exit code.");
        string report = File.ReadAllText(Path.Combine(invalid, "smoke-test.txt"));
        Check(report.Contains("FAIL smoke: Unknown smoke option: --unknown") && report.Contains("TOTAL PASS 0 / LIMIT 0 / FAIL 1 / SKIP 0"), "An unavailable timing artifact must not prevent normal command error handling.");
        Console.WriteLine("PASS original command error and exit code unchanged");
    }

    private static void Report(string folder, List<string> checks)
    {
        Check(string.Join("\n", File.ReadAllLines(Path.Combine(folder, "smoke-test.txt"))) == string.Join("\n", checks.ToArray()), "Check output and ordering must remain unchanged.");
        foreach (string line in checks) Console.WriteLine(line);
    }

    private static void Recovery(string folder, bool interrupted)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "smoke-timings.txt");
        string header = "section\tdepth\tinclusive_ms\town_ms\tparent";
        string completed = "Completed\t0\t12.000\t12.000\t";
        File.WriteAllText(path, interrupted ? header + "\n" + completed + "\nInterrupted\t0\t" : "");
        if (interrupted) File.WriteAllText(Path.Combine(folder, "sections-done.txt"), "Completed\n");
        SmokeRun.Start(folder, Settings(folder, new[] { "--no-steps" }));
        if (interrupted) Check(!SmokeRun.EnterSection("Completed"), "Recovery must keep completed sections skipped.");
        Check(SmokeRun.EnterSection("AfterRecovery"), "The next section must run on recovery.");
        List<string> checks = new List<string>(new[] { "PASS after recovery" });
        SmokeRun.LeaveSection("AfterRecovery", checks);
        SmokeRun.Complete(checks);
        string[][] rows = Timings(folder, interrupted ? 2 : 1);
        if (interrupted) Check(File.ReadAllLines(path)[1] == completed, "Recovery must preserve completed timing rows.");
        Check(rows[rows.Length - 1][0] == "AfterRecovery" && rows[rows.Length - 1][1] == "0", "Recovery must not append to an interrupted row.");
        Report(folder, checks);
    }

    private static void NestedRecovery(string folder)
    {
        Directory.CreateDirectory(folder);
        SmokeOptions settings = Settings(folder, new[] { "--no-steps" });
        List<string> checks = new List<string>();
        Resume(Path.Combine(folder, "completed"), settings, 2, checks);
        Resume(Path.Combine(folder, "open"), settings, 1, checks);
    }

    private static void Resume(string folder, SmokeOptions settings, int completed, List<string> checks)
    {
        Directory.CreateDirectory(folder);
        SmokeRun.Start(folder, settings);
        Check(SmokeRun.EnterSection("Outer") && SmokeRun.EnterSection("Middle") && SmokeRun.EnterSection("Inner"), "The nested sections must start.");
        Thread.Sleep(120);
        SmokeRun.LeaveSection("Inner", checks);
        if (completed == 2) SmokeRun.LeaveSection("Middle", checks);
        string path = Path.Combine(folder, "smoke-timings.txt"), retained = File.ReadAllText(path);
        SmokeRun.Start(folder, settings);
        Check(SmokeRun.EnterSection("Outer"), "The outer section must resume.");
        Check(completed == 1 ? SmokeRun.EnterSection("Middle") : !SmokeRun.EnterSection("Middle"), "Only completed children must stay skipped.");
        Thread.Sleep(20);
        if (completed == 1) SmokeRun.LeaveSection("Middle", checks);
        SmokeRun.LeaveSection("Outer", checks);
        SmokeRun.Complete(checks);
        string[][] rows = Timings(folder, 3);
        Check(rows[0][0] == "Inner" && rows[0][1] == "2" && rows[0][4] == "Middle" && rows[1][0] == "Middle" && rows[1][1] == "1" && rows[1][4] == "Outer" && rows[2][0] == "Outer" && rows[2][1] == "0" && rows[2][4] == "", "Rows must keep their names, depths and parents.");
        Check(File.ReadAllText(path).StartsWith(retained, StringComparison.Ordinal), "Completed rows must stay intact.");
        double inner = Number(rows[0][2]), middle = Number(rows[1][2]), parent = Number(rows[2][2]);
        Check(middle >= inner && parent >= middle && Math.Abs(Number(rows[1][3]) - (middle - inner)) <= 0.01 && Math.Abs(Number(rows[2][3]) - (parent - middle)) <= 0.01, "Resumed inclusive must include retained children exactly once.");
        string finished = File.ReadAllText(path);
        SmokeRun.Start(folder, settings);
        Check(!SmokeRun.EnterSection("Outer") && !SmokeRun.EnterSection("Middle") && !SmokeRun.EnterSection("Inner"), "Completed sections must stay skipped.");
        Check(File.ReadAllText(path) == finished, "A repeated resume must not duplicate or change timing rows.");
    }

    private static void FailedParentIsolation(string folder)
    {
        Directory.CreateDirectory(folder);
        SmokeOptions settings = Settings(folder, new[] { "--no-steps" });
        List<string> checks = new List<string>();
        SmokeRun.Start(folder, settings);
        Check(SmokeRun.EnterSection("SmokeThrows") && SmokeRun.EnterSection("SmokeThrowReads"), "The parent and child must start.");
        Thread.Sleep(120);
        SmokeRun.LeaveSection("SmokeThrowReads", checks);
        string path = Path.Combine(folder, "smoke-timings.txt"), retained = File.ReadAllText(path);
        checks.Add("FAIL section SmokeThrows: worker exited 2");
        File.AppendAllText(Path.Combine(folder, "sections-done.txt"), "SmokeThrows" + Environment.NewLine);
        SmokeRun.Flush(checks);
        SmokeRun.Start(folder, settings);
        Check(!SmokeRun.EnterSection("SmokeThrows"), "The failed parent must remain skipped.");
        Check(SmokeRun.EnterSection("SmokeBrainBodies"), "The next section must start.");
        Thread.Sleep(20);
        SmokeRun.LeaveSection("SmokeBrainBodies", checks);
        SmokeRun.Complete(checks);
        string[][] rows = Timings(folder, 2);
        Check(File.ReadAllText(path).StartsWith(retained, StringComparison.Ordinal), "The prior child row must remain intact.");
        Check(rows[0][0] == "SmokeThrowReads" && rows[0][1] == "1" && rows[0][4] == "SmokeThrows" && rows[1][0] == "SmokeBrainBodies" && rows[1][1] == "0" && rows[1][4] == "", "The retained and next rows must keep their owners and depths.");
        Check(Math.Abs(Number(rows[1][2]) - Number(rows[1][3])) <= 0.001, "An unrelated section must not inherit the failed parent's child.");
        Console.WriteLine("PASS failed parent timing isolation");
    }

    private static void FailedChildRecovery(string folder)
    {
        Directory.CreateDirectory(folder);
        SmokeOptions settings = Settings(folder, new[] { "--no-steps" });
        List<string> checks = new List<string>();
        SmokeRun.Start(folder, settings);
        Check(SmokeRun.EnterSection("SmokeThrows") && SmokeRun.EnterSection("SmokeThrowReads"), "The parent and first child must start.");
        Thread.Sleep(120);
        SmokeRun.LeaveSection("SmokeThrowReads", checks);
        Check(SmokeRun.EnterSection("SmokeThrowSlow"), "The second child must start.");
        checks = SmokeRun.Checks();
        checks.Add("FAIL section SmokeThrowSlow: worker exited 2");
        File.AppendAllText(Path.Combine(folder, "sections-done.txt"), "SmokeThrowSlow" + Environment.NewLine);
        File.AppendAllText(Path.Combine(folder, "failure.txt"), checks[checks.Count - 1] + Environment.NewLine);
        SmokeRun.Flush(checks);
        string path = Path.Combine(folder, "smoke-timings.txt"), retained = File.ReadAllText(path);
        SmokeRun.Start(folder, settings);
        Check(SmokeRun.EnterSection("SmokeThrows"), "The parent must resume.");
        Check(!SmokeRun.EnterSection("SmokeThrowReads") && !SmokeRun.EnterSection("SmokeThrowSlow"), "Completed and failed children must be skipped.");
        Check(File.ReadAllText(path) == retained, "Skipped children must not alter timings.");
        Check(SmokeRun.EnterSection("SmokeThrowFast"), "The next child must start.");
        Thread.Sleep(20);
        SmokeRun.LeaveSection("SmokeThrowFast", checks);
        SmokeRun.LeaveSection("SmokeThrows", checks);
        SmokeRun.Complete(checks);
        string[][] rows = Timings(folder, 3);
        Check(rows[0][0] == "SmokeThrowReads" && rows[0][1] == "1" && rows[0][4] == "SmokeThrows" && rows[1][0] == "SmokeThrowFast" && rows[1][1] == "1" && rows[1][4] == "SmokeThrows" && rows[2][0] == "SmokeThrows" && rows[2][1] == "0" && rows[2][4] == "", "Rows must keep their names, depths and parents.");
        double children = Number(rows[0][2]) + Number(rows[1][2]);
        double parent = Number(rows[2][2]), own = Number(rows[2][3]);
        Check(parent >= children && Math.Abs(parent - own - children) <= 0.01, "The resumed parent must include its retained completed child exactly once.");
    }

    private static string[][] Timings(string folder, int count)
    {
        string path = Path.Combine(folder, "smoke-timings.txt");
        Check(File.Exists(path), "Expected smoke-timings.txt for completed sections.");
        string[] lines = File.ReadAllLines(path);
        Check(lines.Length == count + 1 && lines[0] == "section\tdepth\tinclusive_ms\town_ms\tparent", "Expected one header and a row per completed section.");
        string[][] rows = new string[count][];
        for (int i = 0; i < count; i++)
        {
            rows[i] = lines[i + 1].Split('\t');
            Check(rows[i].Length == 5, "Timing rows must have five fields.");
            Check(Number(rows[i][2]) >= 0 && Number(rows[i][3]) >= 0, "Timings must be nonnegative.");
        }
        return rows;
    }

    private static double Number(string text) { return double.Parse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture); }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}

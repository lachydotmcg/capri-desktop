using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

internal static class Program
{
    private const string Header = "Capri cross-platform smoke test on Linux 6.8.0";
    private const string Completion = "PASS a system quit with her Mainframe and a sheet open closes every window and ends her";
    private static int failures;
    private static string root;

    private sealed class Outcome
    {
        internal int Exit;
        internal string Output;
        internal string Summary
        {
            get { return "exit=" + Exit + " output=[" + Program.Brief(Output) + "]"; }
        }
    }

    private sealed class Site
    {
        internal readonly string File, Report, MinPass;
        internal Site(string file, string report, string minPass) { File = file; Report = report; MinPass = minPass; }
    }

    private static string Brief(string text)
    {
        string flat = text.Replace("\r", "").Replace("\n", " | ").Trim();
        return flat.Length <= 300 ? flat : "..." + flat.Substring(flat.Length - 300);
    }

    private static void Check(string name, string result, bool good)
    {
        Console.WriteLine("{0} {1} result=[{2}]", good ? "PASS" : "FAIL", name, result);
        if (!good) failures++;
    }

    private static string Report(int passes, bool completion, string fail, params string[] extra)
    {
        List<string> lines = new List<string> { Header };
        for (int i = 1; i <= passes - (completion ? 1 : 0); i++) lines.Add("PASS synthetic check " + i);
        if (fail != null) lines.Add("FAIL " + fail);
        lines.AddRange(extra);
        if (completion) lines.Add(Completion);
        return string.Join("\n", lines) + "\n";
    }

    private static void Write(string path, string text)
    {
        System.IO.File.WriteAllText(path, text);
    }

    private static Outcome Gate(string dir, string report, int minPass, int appExit)
    {
        ProcessStartInfo info = new ProcessStartInfo("bash");
        info.WorkingDirectory = dir;
        info.UseShellExecute = false;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.ArgumentList.Add(Path.Combine(root, "ci", "smoke-gate.sh"));
        info.ArgumentList.Add(report);
        info.ArgumentList.Add(minPass.ToString());
        info.ArgumentList.Add(appExit.ToString());
        using (Process process = Process.Start(info))
        {
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new Outcome { Exit = process.ExitCode, Output = stdout + stderr };
        }
    }

    private static void GateScenarios(string scratch)
    {
        string dir = Path.Combine(scratch, "gate");
        Directory.CreateDirectory(dir);
        Write(Path.Combine(dir, "complete.txt"), Report(20, true, null));
        Outcome ok = Gate(dir, "complete.txt", 20, 0);
        Check("complete-report-passes", ok.Summary, ok.Exit == 0 && ok.Output.Contains("smoke gate: 20 PASS lines, at least 20 required"));

        Outcome missing = Gate(dir, "absent.txt", 20, 0);
        Check("missing-report-fails", missing.Summary, missing.Exit != 0 && missing.Output.Contains("no report at absent.txt"));

        Write(Path.Combine(dir, "incomplete.txt"), Report(25, false, null));
        Outcome incomplete = Gate(dir, "incomplete.txt", 20, 0);
        Check("incomplete-report-fails", incomplete.Summary,
            incomplete.Exit != 0 && incomplete.Output.Contains("missing the system quit completion line") && incomplete.Output.Contains("PASS synthetic check 24"));

        Write(Path.Combine(dir, "failed.txt"), Report(21, true, "she could not open her Mainframe"));
        Outcome failed = Gate(dir, "failed.txt", 20, 0);
        Check("explicit-fail-fails", failed.Summary,
            failed.Exit != 0 && failed.Output.Contains("has a FAIL line") && failed.Output.Contains("FAIL she could not open her Mainframe"));

        Outcome crash = Gate(dir, "complete.txt", 20, 3);
        Check("nonzero-app-exit-fails", crash.Summary, crash.Exit != 0 && crash.Output.Contains("exited with status 3"));

        Write(Path.Combine(dir, "short.txt"), Report(19, true, null));
        Outcome below = Gate(dir, "short.txt", 20, 0);
        Check("below-threshold-fails", below.Summary, below.Exit != 0 && below.Output.Contains("19 PASS lines is below the 20 threshold"));

        Write(Path.Combine(dir, "noise.txt"), Report(19, true, null, "LIMIT she has no microphone on this desktop", "SKIP her wallpaper check here", "INFO the timing file is unavailable"));
        Outcome noise = Gate(dir, "noise.txt", 20, 0);
        Check("limit-skip-info-do-not-count", noise.Summary, noise.Exit != 0 && noise.Output.Contains("19 PASS lines is below the 20 threshold"));

        Write(Path.Combine(dir, "failonly.txt"), Report(3, false, null));
        Outcome failonly = Gate(dir, "failonly.txt", 0, 0);
        Check("fail-only-site-requires-completion", failonly.Summary, failonly.Exit != 0 && failonly.Output.Contains("missing the system quit completion line"));

        Write(Path.Combine(dir, "failonly-complete.txt"), Report(3, true, null));
        Outcome failonlyOk = Gate(dir, "failonly-complete.txt", 0, 0);
        Check("fail-only-site-passes-with-completion", failonlyOk.Summary, failonlyOk.Exit == 0);
    }

    private static void WorkflowScenarios()
    {
        Site[] sites =
        {
            new Site("ci.yml", "smoke/linux/smoke-test.txt", "20"),
            new Site("ci.yml", "smoke/mac/smoke-test.txt", "20"),
            new Site("mac-linux.yml", "smoke/mac-arm64/smoke-test.txt", "0"),
            new Site("mac-linux.yml", "smoke/mac-x64/smoke-test.txt", "0"),
            new Site("mac-linux.yml", "smoke/linux/smoke-test.txt", "0"),
            new Site("mac-linux.yml", "smoke/appimage/smoke-test.txt", "20")
        };
        Dictionary<string, string> texts = new Dictionary<string, string>();
        foreach (Site site in sites)
        {
            if (!texts.ContainsKey(site.File)) texts[site.File] = File.ReadAllText(Path.Combine(root, ".github", "workflows", site.File));
            string[] lines = texts[site.File].Replace("\r\n", "\n").Split('\n');
            int gate = Array.FindIndex(lines, line => line.Trim() == "bash ci/smoke-gate.sh " + site.Report + " " + site.MinPass + " \"$rc\"");
            string reason = gate < 0 ? "no gate line for threshold " + site.MinPass : "";
            if (gate >= 0)
            {
                int wipe = -1;
                int launch = -1;
                for (int i = 0; i < gate; i++)
                {
                    if (lines[i].Trim() == "rm -f " + site.Report) wipe = i;
                    if (lines[i].Contains("--smoke") && lines[i].Contains("\"$PWD/" + Path.GetDirectoryName(site.Report) + "\"")) launch = i;
                }
                if (wipe < 0) reason = "no rm -f " + site.Report + " before the gate";
                else if (launch < 0) reason = "no launch line for " + site.Report;
                else if (launch < wipe) reason = "the report is deleted after the launch";
                else if (!lines[launch].Contains("|| rc=$?")) reason = "the launch does not capture the exit code";
                else if (lines[launch].Contains("|| true")) reason = "the launch still discards the exit code";
            }
            Check("workflow-gate-" + site.Report.Replace('/', '-').Replace(".txt", ""), reason == "" ? "gate, stale deletion and exit capture in place" : "checked " + site.File + " reason=[" + reason + "]", reason == "");
        }
        foreach (KeyValuePair<string, int> expected in new[] { new KeyValuePair<string, int>("ci.yml", 3), new KeyValuePair<string, int>("mac-linux.yml", 2) })
        {
            int smokeUploads = CountOf(texts[expected.Key], "name: smoke-");
            bool oldGate = texts[expected.Key].Contains("grep -q '^FAIL'") || texts[expected.Key].Contains("grep -c '^PASS'");
            Check("workflow-" + expected.Key + "-artifacts-and-old-gate", "smoke uploads=" + smokeUploads + " old inline gate=" + oldGate,
                smokeUploads == expected.Value && !oldGate);
        }
    }

    private static int CountOf(string text, string needle)
    {
        int count = 0;
        for (int at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal)) count++;
        return count;
    }

    private static string Root()
    {
        DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ci", "check-linux.sh"))) return dir.FullName;
            dir = dir.Parent;
        }
        return Directory.GetCurrentDirectory();
    }

    private static int Main()
    {
        root = Root();
        string scratch = Directory.CreateTempSubdirectory("capri-smoke-gate-").FullName;
        try
        {
            GateScenarios(scratch);
            WorkflowScenarios();
        }
        finally
        {
            try { Directory.Delete(scratch, true); } catch (IOException) { }
        }
        Console.WriteLine("failures={0}", failures);
        return failures == 0 ? 0 : 1;
    }
}

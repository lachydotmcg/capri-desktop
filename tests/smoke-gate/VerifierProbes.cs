using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

internal static class VerifierProbes
{
    private const string Completion = "PASS a system quit with her Mainframe and a sheet open closes every window and ends her";
    private static int failures;

    private sealed class Result
    {
        internal int Exit;
        internal string Output, Error;
    }

    private static Result Gate(string root, string dir, string report, string minimum, string appExit)
    {
        ProcessStartInfo info = new ProcessStartInfo("bash");
        info.WorkingDirectory = dir;
        info.UseShellExecute = false;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        foreach (string arg in new[] { Path.Combine(root, "ci", "smoke-gate.sh"), report, minimum, appExit })
            info.ArgumentList.Add(arg);
        using (Process process = Process.Start(info))
        {
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new Result { Exit = process.ExitCode, Output = output, Error = error };
        }
    }

    private static void Check(string name, bool good)
    {
        Console.WriteLine("{0} verifier-{1}", good ? "PASS" : "FAIL", name);
        if (!good) failures++;
    }

    private static string Report(int passes)
    {
        return string.Concat(Enumerable.Repeat("PASS check\n", passes - 1)) + Completion + "\n";
    }

    private static void Case(string root, string dir, string name, string text, int minimum, int appExit, int expected)
    {
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, text);
        Result result = Gate(root, dir, name, minimum.ToString(), appExit.ToString());
        Check(name, result.Exit == expected
            && result.Output.StartsWith(text, StringComparison.Ordinal)
            && File.ReadAllText(path) == text);
    }

    internal static int Run(string root, string scratch)
    {
        string dir = Path.Combine(scratch, "verifier");
        Directory.CreateDirectory(dir);
        string good = Report(20);

        Case(root, dir, "empty", "", 20, 0, 1);
        Case(root, dir, "header", "Capri smoke\n", 0, 0, 1);
        Case(root, dir, "fail", "FAIL broken\n" + good, 20, 0, 1);
        Case(root, dir, "trailing-space", good.Substring(0, good.Length - 1) + " \n", 20, 0, 1);
        Case(root, dir, "crlf", good.Replace("\n", "\r\n"), 20, 0, 1);
        Case(root, dir, "exact", good, 20, 0, 0);
        Case(root, dir, "below", Report(19), 20, 0, 1);
        Case(root, dir, "zero", Completion + "\n", 0, 0, 0);
        Case(root, dir, "spaces in name", good, 20, 0, 0);
        Case(root, dir, "-x", good, 20, 0, 0);
        Case(root, dir, "exit3", good, 20, 3, 1);
        Case(root, dir, "exit137", good, 20, 137, 1);
        Case(root, dir, "exit143", good, 20, 143, 1);
        Case(root, dir, "nonfinal", good + "INFO after completion\n", 20, 0, 1);

        string noise = string.Concat(Enumerable.Repeat("PASS check\n", 18))
            + "LIMIT x\nSKIP x\nINFO x\n" + Completion + "\n";
        Case(root, dir, "noise", noise, 20, 0, 1);

        Result success = Gate(root, dir, "exact", "20", "0");
        Check("baseline-success-output", success.Exit == 0 && success.Output == good);
        Result failed = Gate(root, dir, "fail", "20", "0");
        Check("baseline-fail-output", failed.Exit == 1 && failed.Output == "FAIL broken\n" + good);

        Result missing = Gate(root, dir, "missing", "20", "0");
        Check("missing-cat", missing.Exit != 0 && missing.Output == "" && missing.Error.Contains("cat:"));
        Directory.CreateDirectory(Path.Combine(dir, "directory"));
        Result directory = Gate(root, dir, "directory", "20", "0");
        Check("directory-cat", directory.Exit != 0 && directory.Output == "" && directory.Error.Contains("cat:"));

        Result badMinimum = Gate(root, dir, "exact", "nope", "0");
        Check("nonnumeric-minimum", badMinimum.Exit != 0 && File.ReadAllText(Path.Combine(dir, "exact")) == good);
        Result badExit = Gate(root, dir, "exact", "20", "nope");
        Check("nonnumeric-app-exit", badExit.Exit != 0 && File.ReadAllText(Path.Combine(dir, "exact")) == good);
        Result negativeMinimum = Gate(root, dir, "exact", "-1", "0");
        Check("negative-minimum", negativeMinimum.Exit != 0 && negativeMinimum.Output == ""
            && negativeMinimum.Error.Contains("non-negative integer"));
        Result negativeExit = Gate(root, dir, "exact", "20", "-1");
        Check("negative-app-exit", negativeExit.Exit != 0 && negativeExit.Output == ""
            && negativeExit.Error.Contains("non-negative integer"));
        return failures;
    }
}

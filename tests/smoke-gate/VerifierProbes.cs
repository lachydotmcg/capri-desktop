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

    private static Result Gate(string root, string dir, string report, string minimum, string appExit, string shims = null)
    {
        ProcessStartInfo info = new ProcessStartInfo("bash");
        info.WorkingDirectory = dir;
        info.UseShellExecute = false;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        if (shims != null) info.Environment["PATH"] = shims + Path.PathSeparator + info.Environment["PATH"];
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

    private static string Shim(string dir, string name, string command, string body)
    {
        string bin = Path.Combine(dir, name);
        Directory.CreateDirectory(bin);
        string script = Path.Combine(bin, command);
        File.WriteAllText(script, "#!/bin/sh\n" + body + "\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        return bin;
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
        const string overflow = "9223372036854775808";
        Result largeMinimum = Gate(root, dir, "exact", overflow, "0");
        Check("overflow-minimum", largeMinimum.Exit == 2 && largeMinimum.Output == ""
            && largeMinimum.Error.Contains("min-pass must be a non-negative integer"));
        Result largeExit = Gate(root, dir, "exact", "20", overflow);
        Check("overflow-app-exit", largeExit.Exit == 2 && largeExit.Output == ""
            && largeExit.Error.Contains("app-exit-code must be a non-negative integer"));
        Result padded = Gate(root, dir, "exact", "000020", "000");
        Check("padded-decimal", padded.Exit == 0 && padded.Output == good
            && padded.Error.Contains("20 PASS lines, at least 000020 required"));
        Result paddedEight = Gate(root, dir, "exact", "000008", "000");
        Check("padded-eight-is-decimal", paddedEight.Exit == 0 && paddedEight.Output == good
            && paddedEight.Error == "smoke gate: 20 PASS lines, at least 000008 required\n");
        Result largest = Gate(root, dir, "exact", "9223372036854775807", "0");
        Check("largest-signed-minimum", largest.Exit == 1 && largest.Output == good
            && largest.Error == "smoke gate: 20 PASS lines is below the 9223372036854775807 threshold\n");

        string noGrep = Shim(dir, "no-grep", "grep", "exit 127");
        Result inspection = Gate(root, dir, "below", "20", "0", noGrep);
        Check("missing-grep-fails-closed", inspection.Exit == 1 && inspection.Output == Report(19)
            && inspection.Error.Contains("could not inspect the report for FAIL lines"));
        string badCount = Shim(dir, "bad-count", "grep", "if [ \"$1\" = '-q' ]; then exit 1; fi\nexit 2");
        Result count = Gate(root, dir, "below", "20", "0", badCount);
        Check("grep-count-error-fails-closed", count.Exit == 1 && count.Output == Report(19)
            && count.Error.Contains("could not count PASS lines"));
        string badTail = Shim(dir, "bad-tail", "tail", "printf '%s\\n' '" + Completion + "'\nexit 1");
        Result last = Gate(root, dir, "exact", "20", "0", badTail);
        Check("tail-error-fails-closed", last.Exit == 1 && last.Output == good
            && last.Error.Contains("could not read the last report line"));

        string exact = Path.Combine(dir, "exact");
        byte[] before = File.ReadAllBytes(exact);
        string badQuery = Shim(dir, "bad-query", "grep", "if [ \"$1\" = '-q' ]; then exit 2; fi\nprintf '20\\n'");
        Result query = Gate(root, dir, "exact", "20", "0", badQuery);
        Check("grep-query-error-keeps-report", query.Exit == 1 && query.Output == good
            && query.Error == "smoke gate: could not inspect the report for FAIL lines\n"
            && before.SequenceEqual(File.ReadAllBytes(exact)));
        string partialCount = Shim(dir, "partial-count", "grep", "if [ \"$1\" = '-q' ]; then exit 1; fi\nprintf '20\\n'\nexit 2");
        Result partial = Gate(root, dir, "exact", "20", "0", partialCount);
        Check("grep-count-output-before-error-keeps-report", partial.Exit == 1 && partial.Output == good
            && partial.Error == "smoke gate: could not count PASS lines\n"
            && before.SequenceEqual(File.ReadAllBytes(exact)));
        return failures;
    }
}

using System;
using System.IO;

internal static class Program
{
    private static int failures;

    private static void Pass(string line)
    {
        Console.WriteLine("PASS " + line);
    }

    private static void Fail(string line)
    {
        Console.WriteLine("FAIL " + line);
        failures++;
    }

    private static string Root(string[] args)
    {
        string asked = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] != "--root") continue;
            if (asked != null) throw new ArgumentException("--root is given twice, so the pin cannot tell which checkout to read");
            if (i + 1 >= args.Length) throw new ArgumentException("--root needs a directory after it, so the pin has nothing to read");
            if (args[i + 1].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("--root '" + args[i + 1] + "' looks like another flag, so the pin has nothing to read");
            asked = args[i + 1];
            i++;
        }
        if (asked == null) return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string root;
        try
        {
            root = Path.GetFullPath(asked);
        }
        catch (Exception)
        {
            throw new ArgumentException("--root '" + asked + "' is not a path, so the pin has nothing to read");
        }
        if (!Directory.Exists(root)) throw new ArgumentException("--root '" + asked + "' is not a directory, so the pin has nothing to read");
        return root;
    }

    private static int Main(string[] args)
    {
        string root;
        try
        {
            root = Root(args);
        }
        catch (ArgumentException error)
        {
            Fail(error.Message);
            Console.WriteLine("failures={0}", failures);
            return 1;
        }
        try
        {
            SettingsSearch.Check(root, Pass);
        }
        catch (Exception error)
        {
            Fail("settings search: " + error.Message);
        }
        try
        {
            MailMove.Check(Pass);
        }
        catch (Exception error)
        {
            Fail("mail move: " + error.Message);
        }
        Console.WriteLine("failures={0}", failures);
        return failures == 0 ? 0 : 1;
    }
}

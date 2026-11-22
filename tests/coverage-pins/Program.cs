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
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--root") return Path.GetFullPath(args[i + 1]);
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }

    private static int Main(string[] args)
    {
        string root = Root(args);
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

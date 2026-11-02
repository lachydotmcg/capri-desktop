using System;
using System.Collections.Generic;
using System.Reflection;

internal static class CallModeCheck
{
    private static int Main(string[] args)
    {
        try
        {
            Assembly app = Assembly.LoadFrom(args[0]);
            MethodInfo check = app.GetType("CapriDesktop.Companion", true).GetMethod("SmokeCallMode", BindingFlags.Static | BindingFlags.NonPublic);
            List<string> checks = new List<string>();
            check.Invoke(null, new object[] { checks, args[1] });
            foreach (string line in checks) Console.WriteLine(line);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.InnerException ?? error);
            return 1;
        }
    }
}

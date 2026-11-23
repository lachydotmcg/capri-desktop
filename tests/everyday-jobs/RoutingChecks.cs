using System;
using System.Collections.Generic;
using System.IO;
using CapriDesktop;

namespace CapriEverydayJobs
{
    internal static class RoutingChecks
    {
        internal static int Run()
        {
            Func<List<OpenWindow>> windows = Hands.Windows;
            try
            {
                Hands.Windows = () => new List<OpenWindow>();
                Require(!InstantClose("close chrome"), "a missing window must not become an instant close");
                Hands.Windows = FakePc.Windows;
                Require(InstantClose("close chrome"), "close chrome must bypass planner reinterpretation");
                Require(InstantClose("please close Chrome"), "polite close must retain the direct parser");
                foreach (string ask in new[] { "open chrome", "don't close chrome", "should I close chrome?", "close chrome and discord", "close chrome then open discord", "close chrome and close discord", "open discord. close chrome", "don't close chrome. Capri, close chrome", "close unknown-app", "close tab", "close chrome tab", "close YouTube", "close it", "close your mouth" })
                    Require(!InstantClose(ask), "unexpected instant app close: " + ask);
                Require(Hands.Heard("close YouTube", out string tabTool, out ToolArguments tab, true) && tabTool == "app_window" && tab.Action == "close" && !Hands.Instant(tabTool, tab), "a matching browser title must retain its existing route");
                DirectoryInfo folder = Directory.CreateTempSubdirectory("capri-routing-");
                if (!string.Equals(folder.Parent?.FullName, Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The routing fixture wasn't created in the temporary directory.");
                try
                {
                    Job close = new Job {
                        Ask = "close chrome",
                        Ideal = new[] { "open_app {\"name\":\"chrome\"}", "Done." },
                        Check = run => Jobs.Need(run, "app_window close Chrome", call => call.Tool == "app_window" && call.Args.Action == "close" && string.Equals(call.Args.Name, "chrome", StringComparison.OrdinalIgnoreCase))
                    };
                    Outcome outcome = Program.Run(close, "scripted", folder.FullName, null);
                    Require(outcome.Pass && outcome.Calls.Count == 1, "planning runtime must close rather than use the erroneous open: " + outcome.Why);
                }
                finally { folder.Delete(true); }
                Console.WriteLine("PASS offline routing checks");
                return 0;
            }
            finally { Hands.Windows = windows; }
        }

        private static bool InstantClose(string ask)
        {
            return Hands.Heard(ask, out string tool, out ToolArguments args, true) && tool == "app_window" && args.Action == "close" && Hands.Instant(tool, args, ask);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;

namespace CapriDesktop
{
    internal static class SmokeReport
    {
        internal static bool Write(string file, List<string> lines, string failure, Func<bool> systemQuit)
        {
            lines.Add(failure != null ? "FAIL " + failure : "FAIL A system quit with her Mainframe and a sheet open did not end her.");
            File.WriteAllLines(file, lines);
            if (failure != null) return false;
            bool ended = false;
            try { ended = systemQuit(); }
            catch (Exception error) { lines.Add("INFO " + error.Message); }
            if (ended) lines[lines.Count - 1] = "PASS a system quit with her Mainframe and a sheet open closes every window and ends her";
            File.WriteAllLines(file, lines);
            return ended;
        }
    }
}

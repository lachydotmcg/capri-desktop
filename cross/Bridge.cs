using System;
using System.Diagnostics;
using System.IO;

namespace CapriDesktop
{
    internal static class Bridge
    {
        internal static Process Start(string python, string worker, string kind, string resource, string cache = null)
        {
            if (!Path.IsPathFullyQualified(python) || !File.Exists(python)) throw new InvalidOperationException("Configure the retained local Python runtime first.");
            string script = Path.Combine(AppContext.BaseDirectory, "bridge", worker);
            if (!File.Exists(script)) throw new InvalidOperationException("The packaged native bridge is missing.");
            ProcessStartInfo start = new ProcessStartInfo("/usr/bin/python3") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            start.ArgumentList.Add("-u"); start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "bridge", "sandbox.py"));
            foreach (string value in new[] { "--kind", kind, "--python", python, "--worker", script, "--resource", resource }) start.ArgumentList.Add(value);
            if (cache != null) { start.ArgumentList.Add("--cache"); start.ArgumentList.Add(cache); }
            foreach (string name in new[] { "DISPLAY", "WAYLAND_DISPLAY", "HYPRLAND_INSTANCE_SIGNATURE", "DBUS_SESSION_BUS_ADDRESS", "PYTHONPATH", "PYTHONHOME", "OPENAI_API_KEY", "ANTHROPIC_API_KEY" }) start.Environment.Remove(name);
            Process process = Process.Start(start);
            process.ErrorDataReceived += (sender, args) => { };
            process.BeginErrorReadLine();
            return process;
        }

        internal static string ReadLine(StreamReader reader, int limit)
        {
            System.Text.StringBuilder line = new System.Text.StringBuilder();
            while (true)
            {
                int value = reader.Read();
                if (value < 0) return line.Length == 0 ? null : throw new IOException("The bridge ended within a message.");
                if (value == '\n') return line.ToString().TrimEnd('\r');
                if (line.Length >= limit) throw new IOException("The bridge message exceeded its limit.");
                line.Append((char)value);
            }
        }

        internal static void Kill(Process process)
        {
            try { if (!process.HasExited) process.Kill(true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }
    }
}

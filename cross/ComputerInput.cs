using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;

namespace CapriDesktop
{
    internal static class ComputerProcess
    {
        internal static ProcessStartInfo Start(string executable, string[] arguments)
        {
            ProcessStartInfo start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            return start;
        }

        [DllImport("libc", SetLastError = true)] private static extern int kill(int pid, int signal);
        internal static void Stop(Process process)
        {
            try
            {
                if (process.HasExited) return;
                process.StandardInput.Close();
                kill(process.Id, 15);
                if (!process.WaitForExit(300)) { process.Kill(); process.WaitForExit(1000); }
            }
            catch (InvalidOperationException) { }
            catch (IOException) { }
        }

        internal static byte[] Bytes(string executable, string[] arguments, CancellationToken token, int timeout, int maximum)
        {
            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(timeout);
            using Process process = Process.Start(Start(executable, arguments)) ?? throw new InvalidOperationException("Couldn't start " + executable + ".");
            using CancellationTokenRegistration stopped = deadline.Token.Register(() => Stop(process));
            Task<string> error = Error(process.StandardError, deadline.Token);
            using MemoryStream result = new MemoryStream();
            byte[] buffer = new byte[8192];
            try
            {
                int count;
                while ((count = process.StandardOutput.BaseStream.ReadAsync(buffer, deadline.Token).AsTask().GetAwaiter().GetResult()) > 0)
                {
                    if (result.Length + count > maximum) throw new InvalidOperationException(executable + " output exceeded its limit.");
                    result.Write(buffer, 0, count);
                }
                process.WaitForExitAsync(deadline.Token).GetAwaiter().GetResult();
                string detail = error.GetAwaiter().GetResult().Trim();
                if (process.ExitCode != 0) throw new InvalidOperationException(executable + " failed (exit " + process.ExitCode + ")" + (detail.Length == 0 ? "." : ": " + detail));
                return result.ToArray();
            }
            finally { Stop(process); }
        }

        private static async Task<string> Error(StreamReader stream, CancellationToken token)
        {
            StringBuilder message = new StringBuilder();
            char[] buffer = new char[1024];
            int count;
            while ((count = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                if (message.Length < 2048) message.Append(buffer, 0, Math.Min(count, 2048 - message.Length));
            return message.ToString();
        }
    }

    internal interface IComputerInput
    {
        void Execute(ComputerAction action, ComputerFrame frame, IComputerDesktop desktop, Action<double, double> guard, CancellationToken token);
    }

    internal sealed class ArchInput : IComputerInput
    {
        internal static string Helper { get { return Path.Combine(AppContext.BaseDirectory, "native", "capri-input"); } }
        internal static bool Available { get { return OperatingSystem.IsLinux() && File.Exists(Helper); } }

        internal static bool Probe(CancellationToken token)
        {
            if (!Available) return false;
            try
            {
                byte[] result = ComputerProcess.Bytes(Helper, new[] { "--probe" }, token, 2000, 4096);
                using JsonDocument protocols = JsonDocument.Parse(result);
                return protocols.RootElement.GetProperty("virtual_pointer").GetBoolean() && protocols.RootElement.GetProperty("virtual_keyboard").GetBoolean();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { return false; }
        }

        public void Execute(ComputerAction action, ComputerFrame frame, IComputerDesktop desktop, Action<double, double> guard, CancellationToken token)
        {
            if (!Available) throw new InvalidOperationException("Native mouse and keyboard input needs Capri's optional native/capri-input helper.");
            double px = frame.Source.PointerX, py = frame.Source.PointerY;
            guard(px, py);
            if (action.Action == "click" || action.Action == "double_click" || action.Action == "drag" || action.Action == "scroll")
            {
                (int x, int y) = frame.ToLogical(action.X, action.Y);
                if (!frame.Source.Target.Area.Contains(x, y)) throw new InvalidOperationException("The proposed point is outside the bound target window.");
                desktop.Move(x, y, token);
                px = x; py = y;
                guard(px, py);
            }
            string payload, mode;
            double dx = 0, dy = 0;
            if (action.Action == "type") { mode = "text"; payload = action.Text; }
            else if (action.Action == "press") { mode = "key"; payload = action.Keys.ToUpperInvariant(); }
            else if (action.Action == "scroll") { mode = "scroll"; payload = action.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture); }
            else if (action.Action == "drag")
            {
                (int x, int y) = frame.ToLogical(action.EndX, action.EndY);
                if (!frame.Source.Target.Area.Contains(x, y)) throw new InvalidOperationException("The drag must stay inside the bound target window.");
                dx = x - px; dy = y - py;
                mode = "drag"; payload = FormattableString.Invariant($"{dx} {dy} {action.Duration}");
            }
            else { mode = action.Action; payload = action.Button ?? "left"; }
            using Process helper = Process.Start(ComputerProcess.Start(Helper, new[] { mode })) ?? throw new InvalidOperationException("Couldn't start native input.");
            using CancellationTokenRegistration stopped = token.Register(() => ComputerProcess.Stop(helper));
            Task<string> errors = helper.StandardError.ReadToEndAsync();
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(payload);
                byte[] length = Encoding.ASCII.GetBytes(bytes.Length + "\n");
                helper.StandardInput.BaseStream.Write(length);
                helper.StandardInput.BaseStream.Write(bytes);
                helper.StandardInput.BaseStream.Flush();
                string ready = helper.StandardOutput.ReadLineAsync(token).AsTask().WaitAsync(TimeSpan.FromSeconds(3), token).GetAwaiter().GetResult();
                if (ready != "READY") throw new InvalidOperationException("The compositor does not support the requested native input, or the payload was rejected.");
                guard(px, py);
                helper.StandardInput.Write("G"); helper.StandardInput.Flush();
                if (mode == "drag")
                    for (int step = 1; step <= 20; step++)
                    {
                        string line = helper.StandardOutput.ReadLineAsync(token).AsTask().WaitAsync(TimeSpan.FromSeconds(2), token).GetAwaiter().GetResult();
                        if (line != "STEP " + step) throw new InvalidOperationException("Native drag stopped before its next segment.");
                        guard(px + dx * (step - 1) / 20, py + dy * (step - 1) / 20);
                        helper.StandardInput.Write("G"); helper.StandardInput.Flush();
                    }
                if (mode == "text")
                {
                    long began = Stopwatch.GetTimestamp();
                    while (!helper.WaitForExit(40))
                    {
                        guard(px, py);
                        if (Stopwatch.GetElapsedTime(began) > TimeSpan.FromSeconds(5)) throw new TimeoutException("Native text delivery timed out.");
                    }
                }
                else helper.WaitForExitAsync(token).WaitAsync(TimeSpan.FromSeconds(5), token).GetAwaiter().GetResult();
                if (helper.ExitCode != 0) throw new InvalidOperationException("Native input ended without a confirmed delivery (exit " + helper.ExitCode + ").");
                guard(px + dx, py + dy);
                errors.GetAwaiter().GetResult();
            }
            finally { ComputerProcess.Stop(helper); }
        }
    }
}

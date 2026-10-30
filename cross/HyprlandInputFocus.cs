using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CapriDesktop
{
    internal static class HyprlandInputFocus
    {
        // One disposable read-only native child, even when Open is repeated. A
        // stalled X server must not pin runtime workers or delay cancellation.
        private static readonly SemaphoreSlim query = new SemaphoreSlim(1, 1);

        internal static async Task<bool> HasKeyboardFocusAsync(IntPtr window, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!OperatingSystem.IsLinux() || window.ToInt64() <= 1 || window.ToInt64() > uint.MaxValue) return false;
            await query.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                deadline.CancelAfter(TimeSpan.FromMilliseconds(750));
                var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "native", "capri-input"))
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                start.ArgumentList.Add("--x11-focus");
                start.ArgumentList.Add(window.ToInt64().ToString(CultureInfo.InvariantCulture));
                cancellation.ThrowIfCancellationRequested();
                using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the native keyboard-focus observer.");
                try
                {
                    Task<string> output = ReadBounded(process.StandardOutput, deadline.Token);
                    Task<string> error = ReadBounded(process.StandardError, deadline.Token);
                    await Task.WhenAll(process.WaitForExitAsync(deadline.Token), output, error).ConfigureAwait(false);
                    string result = (await output.ConfigureAwait(false)).Trim();
                    await error.ConfigureAwait(false);
                    deadline.Token.ThrowIfCancellationRequested();
                    if (process.ExitCode != 0 || (result != "FOCUSED" && result != "UNFOCUSED"))
                        throw new InvalidOperationException("The native keyboard-focus observer could not read the X11 session.");
                    return result == "FOCUSED";
                }
                catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
                { throw new InvalidOperationException("The native keyboard-focus observer timed out."); }
                finally
                {
                    // Only this exact child belongs to the observation. Kill and
                    // reap it before allowing another query to start.
                    if (!process.HasExited) process.Kill();
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }
            }
            finally { query.Release(); }
        }

        private static async Task<string> ReadBounded(StreamReader stream, CancellationToken cancellation)
        {
            char[] text = new char[128];
            int length = 0, count;
            while ((count = await stream.ReadAsync(text.AsMemory(length), cancellation).ConfigureAwait(false)) != 0)
            {
                length += count;
                if (length == text.Length) throw new InvalidOperationException("The native keyboard-focus observer exceeded its output limit.");
            }
            return new string(text, 0, length);
        }
    }
}

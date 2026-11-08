using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CapriDesktop
{
    // XWayland only: a read-only observer, never a global keyboard grab or compositor binding.
    internal sealed class NativeGameEscape : IDisposable
    {
        private readonly Process child;
        private readonly object gate = new object();
        private readonly CancellationToken stopToken;
        private readonly CancellationTokenSource stopped;
        private readonly CancellationTokenRegistration cancelled;
        private int armed, disposed;
        internal bool Armed => Volatile.Read(ref armed) != 0 && Volatile.Read(ref disposed) == 0;
        private NativeGameEscape(Process child, CancellationToken token)
        {
            this.child = child;
            stopped = CancellationTokenSource.CreateLinkedTokenSource(token);
            stopToken = stopped.Token;
            cancelled = stopped.Token.Register(() => ComputerProcess.Stop(child));
        }
        internal static NativeGameEscape Start(ComputerSnapshot binding, Action escape, CancellationToken token, Action failed = null)
        {
            if (!OperatingSystem.IsLinux() || binding?.Target?.XWayland != true) return null;
            string script = Path.Combine(AppContext.BaseDirectory, "bridge", "escape_watch.py");
            string python = ArchApps.Executable("python3");
            if (python == null || !File.Exists(script)) return null;
            Process process = null;
            NativeGameEscape watch = null;
            try
            {
                token.ThrowIfCancellationRequested();
                process = Process.Start(ComputerProcess.Start(python, new[] { "-u", script }));
                if (process == null) return null;
                watch = new NativeGameEscape(process, token);
                string ready = process.StandardOutput.ReadLineAsync(token).AsTask().WaitAsync(TimeSpan.FromSeconds(2), token).GetAwaiter().GetResult();
                if (ready != "READY") { watch.Dispose(); return null; }
                Volatile.Write(ref watch.armed, 1);
                _ = watch.Listen(escape, failed ?? escape);
                return watch;
            }
            catch (OperationCanceledException) { watch?.Dispose(); if (watch == null && process != null) ComputerProcess.Stop(process); throw; }
            catch (Exception error) when (error is IOException || error is TimeoutException || error is InvalidOperationException || error is System.ComponentModel.Win32Exception)
            { watch?.Dispose(); if (watch == null && process != null) ComputerProcess.Stop(process); return null; }
        }
        private async Task Listen(Action escape, Action failed)
        {
            try
            {
                string signal = await child.StandardOutput.ReadLineAsync(stopToken).ConfigureAwait(false);
                lock (gate)
                {
                    if (Volatile.Read(ref disposed) != 0 || stopToken.IsCancellationRequested) return;
                    Volatile.Write(ref armed, 0);
                    if (signal == "ESC") escape(); else failed();
                }
            }
            catch (OperationCanceledException) when (stopToken.IsCancellationRequested) { }
            catch (Exception error) when (error is IOException || error is InvalidOperationException)
            {
                Volatile.Write(ref armed, 0);
                lock (gate) if (Volatile.Read(ref disposed) == 0 && !stopToken.IsCancellationRequested) failed();
            }
        }
        public void Dispose()
        {
            lock (gate)
            {
                if (disposed != 0) return;
                Volatile.Write(ref disposed, 1); Volatile.Write(ref armed, 0);
            }
            stopped.Cancel(); cancelled.Dispose();
            ComputerProcess.Stop(child); child.Dispose(); stopped.Dispose();
        }
    }
}

using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
namespace CapriDesktop
{
    internal sealed class DiagnosticsWindow : Form
    {
        private readonly Timer timer = new Timer { Interval = 1000 };
        private readonly Process process = Process.GetCurrentProcess();
        private readonly Stopwatch elapsed = Stopwatch.StartNew();
        private readonly Label readout = new Label { Dock = DockStyle.Fill, Padding = new Padding(12) };
        private double lastWall, lastCpu;
        private long lastFrames;
        internal DiagnosticsWindow(Func<long> frames, Func<string> state)
        {
            Text = "Capri / Diagnostics (local only)"; ClientSize = new Size(390, 160);
            BackColor = Color.FromArgb(18, 21, 30); ForeColor = Color.Gainsboro;
            Controls.Add(readout); lastFrames = frames(); lastCpu = process.TotalProcessorTime.TotalSeconds;
            timer.Tick += delegate
            {
                process.Refresh(); double now = elapsed.Elapsed.TotalSeconds, cpu = process.TotalProcessorTime.TotalSeconds;
                long rendered = frames(); double seconds = now - lastWall;
                readout.Text = string.Format("CPU: {0:F1}% of total logical CPU\r\nPrivate memory: {1:F1} MiB | Working set: {2:F1} MiB\r\nRendered FPS: {3:F1}\r\n{4}\r\nSampling once/second; nothing is recorded.",
                    RuntimePolicy.CpuPercent(cpu - lastCpu, seconds, Environment.ProcessorCount), process.PrivateMemorySize64 / 1048576.0,
                    process.WorkingSet64 / 1048576.0, seconds > 0 ? (rendered - lastFrames) / seconds : 0, state());
                lastWall = now; lastCpu = cpu; lastFrames = rendered;
            };
            Shown += delegate { timer.Start(); };
        }
        protected override void Dispose(bool disposing)
        { if (disposing) { timer.Dispose(); process.Dispose(); } base.Dispose(disposing); }
    }
}

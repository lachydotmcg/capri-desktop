using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using SkiaSharp;

namespace CapriDesktop
{
    internal readonly record struct ComputerRect(int X, int Y, int Width, int Height)
    {
        internal bool Contains(double x, double y) { return x >= X && y >= Y && x < X + Width && y < Y + Height; }
        internal ComputerRect Intersect(ComputerRect other)
        {
            int x = Math.Max(X, other.X), y = Math.Max(Y, other.Y);
            return new ComputerRect(x, y, Math.Max(0, Math.Min(X + Width, other.X + other.Width) - x), Math.Max(0, Math.Min(Y + Height, other.Y + other.Height) - y));
        }
    }

    internal sealed record ComputerOutput(string Name, ComputerRect Area, double Scale, int Transform, int PixelWidth, int PixelHeight);
    internal sealed record ComputerTarget(string Address, int Pid, string Birth, string App, ComputerRect Area, int Monitor, int Workspace, bool? XWayland = null);
    internal sealed record ComputerSnapshot(string Session, string Layout, ComputerTarget Target, ComputerOutput Output, double PointerX, double PointerY, bool Active, long FocusEpoch = 0, long ObservationEpoch = 0);

    internal sealed class ComputerObservationChangedException : InvalidOperationException
    {
        internal ComputerObservationChangedException(string message) : base(message) { }
    }

    internal sealed class ComputerFrame
    {
        internal readonly string Id = Guid.NewGuid().ToString("N");
        internal ComputerSnapshot Source;
        internal ComputerRect Area;
        internal byte[] Png, Jpeg;
        internal int Width, Height;
        internal long Taken = Stopwatch.GetTimestamp();

        internal (int X, int Y) ToLogical(double x, double y)
        {
            if (!double.IsFinite(x) || !double.IsFinite(y) || x < 0 || y < 0 || x >= Width || y >= Height)
                throw new InvalidOperationException("The proposed point is outside the current screenshot.");
            return (Area.X + Math.Min(Area.Width - 1, (int)Math.Floor(x * Area.Width / Width)), Area.Y + Math.Min(Area.Height - 1, (int)Math.Floor(y * Area.Height / Height)));
        }
    }

    internal interface IComputerDesktop
    {
        ComputerSnapshot Snapshot(string output = null, bool focus = false);
        ComputerFrame Capture(ComputerSnapshot source, bool crop, CancellationToken token);
        void Move(int x, int y, CancellationToken token);
    }

    internal sealed class ArchComputer : IComputerDesktop
    {
        private readonly HyprlandDesktop desktop;
        private readonly CancellationToken lifetime;
        private readonly bool allowDesktop;
        private readonly Func<int, string> processIdentity;
        internal ArchComputer(HyprlandDesktop desktop, CancellationToken lifetime = default, bool allowDesktop = false, Func<int, string> processIdentity = null)
        { this.desktop = desktop; this.lifetime = lifetime; this.allowDesktop = allowDesktop; this.processIdentity = processIdentity ?? HyprlandDesktop.ReadProcessIdentity; }

        private static ComputerRect Rect(Avalonia.Rect rect)
        {
            int x = (int)Math.Floor(rect.X), y = (int)Math.Floor(rect.Y);
            return new ComputerRect(x, y, (int)Math.Ceiling(rect.Right) - x, (int)Math.Ceiling(rect.Bottom) - y);
        }

        public ComputerSnapshot Snapshot(string output = null, bool focus = false)
        {
            HyprlandSnapshot snapshot = desktop.CaptureSnapshotAsync(lifetime).GetAwaiter().GetResult();
            if (snapshot.Locked) throw new InvalidOperationException("Screen capture and input are unavailable while the session is locked.");
            HyprlandWindow picked = snapshot.Windows.Where(window => window.Pid != Environment.ProcessId && !window.AppId.Equals("Capri", StringComparison.OrdinalIgnoreCase) && snapshot.Visible(window) &&
                (output == null || snapshot.Outputs.Any(display => display.Name.Equals(output, StringComparison.OrdinalIgnoreCase) && display.Id == window.Monitor)))
                .OrderBy(window => window.Address == snapshot.ActiveAddress ? -1 : window.FocusOrder < 0 ? int.MaxValue : window.FocusOrder).FirstOrDefault();
            if (picked == null && !allowDesktop) throw new InvalidOperationException("There is no recent application window to use.");
            if (picked == null)
            {
                HyprlandOutput display = output == null ? snapshot.Outputs.FirstOrDefault(item => item.Focused) ?? snapshot.Outputs.FirstOrDefault() : snapshot.Outputs.FirstOrDefault(item => item.Name.Equals(output, StringComparison.OrdinalIgnoreCase));
                if (display == null) throw new InvalidOperationException("The requested output is not connected.");
                ComputerOutput desktopOutput = new ComputerOutput(display.Name, Rect(display.Logical), display.Scale, display.Transform, display.PixelWidth, display.PixelHeight);
                ComputerTarget background = new ComputerTarget("desktop", Environment.ProcessId, processIdentity(Environment.ProcessId), "desktop", desktopOutput.Area, display.Id, display.Workspace);
                return new ComputerSnapshot(snapshot.SessionId + "|" + Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") + "|" + Environment.UserName, snapshot.LayoutId, background, desktopOutput, snapshot.Pointer.X, snapshot.Pointer.Y, false, desktop.FocusEpoch, snapshot.ObservationEpoch);
            }
            string birth = processIdentity(picked.Pid);
            if (string.IsNullOrEmpty(birth)) throw new InvalidOperationException("The target process identity is unavailable.");
            ComputerTarget target = new ComputerTarget("0x" + picked.Address.ToString("x"), picked.Pid, birth, picked.AppId, Rect(picked.Logical), picked.Monitor, picked.Workspace, picked.XWayland);
            HyprlandOutput monitor = output == null ? snapshot.Outputs.FirstOrDefault(item => item.Id == picked.Monitor) : snapshot.Outputs.FirstOrDefault(item => item.Name.Equals(output, StringComparison.OrdinalIgnoreCase));
            if (monitor == null) throw new InvalidOperationException("The requested output is not connected.");
            ComputerOutput selected = new ComputerOutput(monitor.Name, Rect(monitor.Logical), monitor.Scale, monitor.Transform, monitor.PixelWidth, monitor.PixelHeight);
            if (focus && snapshot.ActiveAddress != picked.Address)
            {
                OpenWindow window = desktop.ListWindows().Find(candidate => candidate.Handle.ToInt64() == picked.Address);
                if (window == null || !desktop.Steer(window, "focus")) throw new InvalidOperationException(desktop.LastError ?? "Couldn't focus the recent app.");
                ComputerSnapshot focused = Snapshot(output, false);
                if (focused.Target != target || focused.Layout != snapshot.LayoutId || !focused.Active) throw new InvalidOperationException("The intended target changed while gaining focus.");
                return focused;
            }
            string identity = snapshot.SessionId + "|" + Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") + "|" + Environment.UserName;
            return new ComputerSnapshot(identity, snapshot.LayoutId, target, selected, snapshot.Pointer.X, snapshot.Pointer.Y, snapshot.ActiveAddress == picked.Address, desktop.FocusEpoch, snapshot.ObservationEpoch);
        }

        // Explicit outputs are observations of that display, independent of the
        // application focused elsewhere. A crop pins an exact window on that output.
        internal ComputerSnapshot Observe(string output = null, ComputerTarget target = null)
        {
            lifetime.ThrowIfCancellationRequested();
            HyprlandSnapshot snapshot;
            try { snapshot = desktop.ObserveSnapshotAsync(lifetime).GetAwaiter().GetResult(); }
            catch (HyprlandObservationChangedException error) { throw new ComputerObservationChangedException(error.Message); }
            if (snapshot.Locked) throw new InvalidOperationException("Screen capture is unavailable while the session is locked.");
            HyprlandOutput monitor = output == null ? null : snapshot.Outputs.FirstOrDefault(item => item.Name.Equals(output, StringComparison.OrdinalIgnoreCase));
            if (output != null && monitor == null) throw new InvalidOperationException("The requested output is not connected.");
            HyprlandWindow picked = null;
            if (target != null)
            {
                picked = snapshot.Windows.FirstOrDefault(window => "0x" + window.Address.ToString("x") == target.Address && window.Pid != Environment.ProcessId && snapshot.Visible(window) && (monitor == null || window.Monitor == monitor.Id));
                if (picked == null) throw new ComputerObservationChangedException("The requested capture window is no longer visible on that output.");
            }
            else if (output == null)
                picked = snapshot.Windows.Where(window => window.Pid != Environment.ProcessId && snapshot.Visible(window))
                    .OrderBy(window => window.Address == snapshot.ActiveAddress ? -1 : window.FocusOrder < 0 ? int.MaxValue : window.FocusOrder).FirstOrDefault();
            if (picked == null && output == null && !allowDesktop) throw new InvalidOperationException("There is no recent application window to use.");
            monitor ??= picked == null ? snapshot.Outputs.FirstOrDefault(item => item.Focused) ?? snapshot.Outputs.FirstOrDefault() : snapshot.Outputs.FirstOrDefault(item => item.Id == picked.Monitor);
            if (monitor == null) throw new InvalidOperationException("The requested output is not connected.");
            ComputerOutput selected = new ComputerOutput(monitor.Name, Rect(monitor.Logical), monitor.Scale, monitor.Transform, monitor.PixelWidth, monitor.PixelHeight);
            string birth = processIdentity(picked == null ? Environment.ProcessId : picked.Pid);
            if (string.IsNullOrEmpty(birth)) throw new InvalidOperationException("The capture process identity is unavailable.");
            ComputerTarget selectedTarget = picked == null
                ? new ComputerTarget("desktop", Environment.ProcessId, birth, "desktop", selected.Area, monitor.Id, monitor.Workspace)
                : new ComputerTarget("0x" + picked.Address.ToString("x"), picked.Pid, birth, picked.AppId, Rect(picked.Logical), picked.Monitor, picked.Workspace, picked.XWayland);
            if (target != null && selectedTarget != target) throw new ComputerObservationChangedException("The requested capture window, process or geometry changed.");
            lifetime.ThrowIfCancellationRequested();
            return new ComputerSnapshot(snapshot.SessionId + "|" + Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") + "|" + Environment.UserName,
                snapshot.LayoutId, selectedTarget, selected, snapshot.Pointer.X, snapshot.Pointer.Y, picked != null && picked.Address == snapshot.ActiveAddress, desktop.FocusEpoch, snapshot.ObservationEpoch);
        }

        internal static void SameObservation(ComputerSnapshot before, ComputerSnapshot after, bool crop)
        {
            if (before.Session != after.Session) throw new ComputerObservationChangedException("The desktop session changed during capture.");
            if (before.ObservationEpoch != after.ObservationEpoch) throw new ComputerObservationChangedException("A session, lock or output event revoked this observation.");
            if (before.Layout != after.Layout || before.Output != after.Output) throw new ComputerObservationChangedException("The output layout changed during capture.");
            if (crop && before.Target != after.Target) throw new ComputerObservationChangedException("The capture window, process or geometry changed.");
        }

        public ComputerFrame Capture(ComputerSnapshot source, bool crop, CancellationToken token) => Capture(source, crop, token, false);
        internal ComputerFrame CaptureObservation(ComputerSnapshot source, bool crop, CancellationToken token) => Capture(source, crop, token, true);

        private ComputerFrame Capture(ComputerSnapshot source, bool crop, CancellationToken token, bool observation)
        {
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime);
            token = linked.Token;
            token.ThrowIfCancellationRequested();
            ComputerRect area = crop ? source.Target.Area.Intersect(source.Output.Area) : source.Output.Area;
            if (area.Width <= 0 || area.Height <= 0) throw new InvalidOperationException("The target is not visible on the selected output.");
            byte[] png = ComputerProcess.Bytes("grim", CaptureArguments(source, area, true), token, 10000, 64 * 1024 * 1024);
            token.ThrowIfCancellationRequested(); lifetime.ThrowIfCancellationRequested();
            ComputerSnapshot after = observation ? Observe(source.Output.Name, crop ? source.Target : null) : Snapshot(source.Output.Name);
            if (observation) SameObservation(source, after, crop);
            else ComputerJobs.Same(source, after, false);
            using SKBitmap bitmap = SKBitmap.Decode(png);
            if (bitmap == null || bitmap.Width < 1 || bitmap.Height < 1 || bitmap.Width > 2048 || bitmap.Height > 2048) throw new InvalidOperationException("grim returned an unreadable or oversized image.");
            int width = bitmap.Width, height = bitmap.Height;
            using SKImage image = SKImage.FromBitmap(bitmap);
            using SKData encoded = image.Encode(SKEncodedImageFormat.Jpeg, 90);
            token.ThrowIfCancellationRequested(); lifetime.ThrowIfCancellationRequested();
            return new ComputerFrame { Source = after, Area = area, Png = png, Jpeg = encoded.ToArray(), Width = width, Height = height };
        }

        internal static string[] CaptureArguments(ComputerSnapshot source, ComputerRect area, bool crop)
        {
            string geometry = area.X + "," + area.Y + " " + area.Width + "x" + area.Height;
            // Downscale the complete logical area instead of dropping its right/bottom.
            double scale = Math.Min(source.Output.Scale, 2048.0 / Math.Max(area.Width, area.Height));
            return new[] { crop ? "-g" : "-o", crop ? geometry : source.Output.Name, "-s", scale.ToString(CultureInfo.InvariantCulture), "-t", "png", "-" };
        }

        public void Move(int x, int y, CancellationToken token)
        {
            if (!desktop.MovePointerAsync(x, y, token).GetAwaiter().GetResult()) throw new InvalidOperationException(desktop.LastError ?? "The pointer move could not be verified.");
        }
    }
}

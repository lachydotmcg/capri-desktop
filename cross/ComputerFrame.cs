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
    internal sealed record ComputerTarget(string Address, int Pid, string Birth, string App, ComputerRect Area, int Monitor, int Workspace);
    internal sealed record ComputerSnapshot(string Session, string Layout, ComputerTarget Target, ComputerOutput Output, double PointerX, double PointerY, bool Active);

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
        internal ArchComputer(HyprlandDesktop desktop, CancellationToken lifetime = default, bool allowDesktop = false) { this.desktop = desktop; this.lifetime = lifetime; this.allowDesktop = allowDesktop; }

        private static ComputerRect Rect(Avalonia.Rect rect)
        {
            int x = (int)Math.Floor(rect.X), y = (int)Math.Floor(rect.Y);
            return new ComputerRect(x, y, (int)Math.Ceiling(rect.Right) - x, (int)Math.Ceiling(rect.Bottom) - y);
        }

        public ComputerSnapshot Snapshot(string output = null, bool focus = false)
        {
            HyprlandSnapshot snapshot = desktop.CaptureSnapshotAsync(lifetime).GetAwaiter().GetResult();
            if (snapshot.Locked) throw new InvalidOperationException("Screen capture and input are unavailable while the session is locked.");
            HyprlandWindow picked = snapshot.Windows.Where(window => window.Pid != Environment.ProcessId && !window.AppId.Equals("Capri", StringComparison.OrdinalIgnoreCase) && snapshot.Visible(window))
                .OrderBy(window => window.Address == snapshot.ActiveAddress ? -1 : window.FocusOrder < 0 ? int.MaxValue : window.FocusOrder).FirstOrDefault();
            if (picked == null && !allowDesktop) throw new InvalidOperationException("There is no recent application window to use.");
            if (picked == null)
            {
                HyprlandOutput display = output == null ? snapshot.Outputs.FirstOrDefault(item => item.Focused) ?? snapshot.Outputs.FirstOrDefault() : snapshot.Outputs.FirstOrDefault(item => item.Name.Equals(output, StringComparison.OrdinalIgnoreCase));
                if (display == null) throw new InvalidOperationException("The requested output is not connected.");
                ComputerOutput desktopOutput = new ComputerOutput(display.Name, Rect(display.Logical), display.Scale, display.Transform, display.PixelWidth, display.PixelHeight);
                ComputerTarget background = new ComputerTarget("desktop", Environment.ProcessId, HyprlandDesktop.ReadProcessIdentity(Environment.ProcessId), "desktop", desktopOutput.Area, display.Id, display.Workspace);
                return new ComputerSnapshot(snapshot.SessionId + "|" + Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") + "|" + Environment.UserName, snapshot.LayoutId, background, desktopOutput, snapshot.Pointer.X, snapshot.Pointer.Y, false);
            }
            string birth = HyprlandDesktop.ReadProcessIdentity(picked.Pid);
            if (string.IsNullOrEmpty(birth)) throw new InvalidOperationException("The target process identity is unavailable.");
            ComputerTarget target = new ComputerTarget("0x" + picked.Address.ToString("x"), picked.Pid, birth, picked.AppId, Rect(picked.Logical), picked.Monitor, picked.Workspace);
            HyprlandOutput monitor = output == null ? snapshot.Outputs.FirstOrDefault(item => item.Id == picked.Monitor) : snapshot.Outputs.FirstOrDefault(item => item.Name.Equals(output, StringComparison.OrdinalIgnoreCase));
            if (monitor == null) throw new InvalidOperationException("The requested output is not connected.");
            ComputerOutput selected = new ComputerOutput(monitor.Name, Rect(monitor.Logical), monitor.Scale, monitor.Transform, monitor.PixelWidth, monitor.PixelHeight);
            if (focus && snapshot.ActiveAddress != picked.Address)
            {
                OpenWindow window = desktop.ListWindows().Find(candidate => candidate.Handle.ToInt64() == picked.Address);
                if (window == null || !desktop.Steer(window, "switch_to")) throw new InvalidOperationException(desktop.LastError ?? "Couldn't focus the recent app.");
                ComputerSnapshot focused = Snapshot(output, false);
                if (focused.Target != target || focused.Layout != snapshot.LayoutId || !focused.Active) throw new InvalidOperationException("The intended target changed while gaining focus.");
                return focused;
            }
            string identity = snapshot.SessionId + "|" + Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") + "|" + Environment.UserName;
            return new ComputerSnapshot(identity, snapshot.LayoutId, target, selected, snapshot.Pointer.X, snapshot.Pointer.Y, snapshot.ActiveAddress == picked.Address);
        }

        public ComputerFrame Capture(ComputerSnapshot source, bool crop, CancellationToken token)
        {
            ComputerRect area = crop ? source.Target.Area.Intersect(source.Output.Area) : source.Output.Area;
            if (area.Width <= 0 || area.Height <= 0) throw new InvalidOperationException("The target is not visible on the selected output.");
            byte[] png = ComputerProcess.Bytes("grim", CaptureArguments(source, area, crop), token, 10000, 64 * 1024 * 1024);
            ComputerSnapshot after = Snapshot(source.Output.Name);
            ComputerJobs.Same(source, after, false);
            using SKBitmap bitmap = SKBitmap.Decode(png);
            if (bitmap == null || bitmap.Width < 1 || bitmap.Height < 1) throw new InvalidOperationException("grim returned an unreadable image.");
            double factor = Math.Min(1, 1600.0 / Math.Max(bitmap.Width, bitmap.Height));
            int width = Math.Max(1, (int)Math.Round(bitmap.Width * factor)), height = Math.Max(1, (int)Math.Round(bitmap.Height * factor));
            using SKBitmap scaled = bitmap.Resize(new SKImageInfo(width, height), SKFilterQuality.High);
            using SKImage image = SKImage.FromBitmap(scaled);
            using SKData encoded = image.Encode(SKEncodedImageFormat.Jpeg, 90);
            return new ComputerFrame { Source = after, Area = area, Png = png, Jpeg = encoded.ToArray(), Width = width, Height = height };
        }

        internal static string[] CaptureArguments(ComputerSnapshot source, ComputerRect area, bool crop)
        {
            string geometry = area.X + "," + area.Y + " " + area.Width + "x" + area.Height;
            return new[] { crop ? "-g" : "-o", crop ? geometry : source.Output.Name, "-s", source.Output.Scale.ToString(CultureInfo.InvariantCulture), "-t", "png", "-" };
        }

        public void Move(int x, int y, CancellationToken token)
        {
            if (!desktop.MovePointerAsync(x, y, token).GetAwaiter().GetResult()) throw new InvalidOperationException(desktop.LastError ?? "The pointer move could not be verified.");
        }
    }
}

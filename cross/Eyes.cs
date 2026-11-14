using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text.Json;
using SkiaSharp;

namespace CapriDesktop
{
    internal sealed record EyesObservation(byte[] Png, ComputerFrame Frame, long Ticket, bool WindowCrop = false);
    // "Check my screen" on Mac and Linux: a screenshot from the system's own tool, shrunk to a JPEG her brain can look at.
    internal static class Eyes
    {
        // A whole-screen picture's file bytes, or null when no tool here could take one. The smoke test hands in its own.
        internal static string Session
        {
            get
            {
                string session = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
                return !string.IsNullOrWhiteSpace(session) ? session : string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")) ? "x11" : "wayland";
            }
        }

        private static readonly Func<byte[]> defaultShoot = delegate { return RequestNative(null, false); };
        internal static Func<byte[]> Shoot = defaultShoot;
        // The same for Watch my screen, with only the tools that don't flash, play a shutter sound or ask first.
        internal static Func<byte[]> Peek = delegate { return Recordable() ? RequestNative(null, true) : null; };
        private static readonly object captureGate = new object();
        private static CancellationTokenSource captures = new CancellationTokenSource();

        private static long generation;
        internal static Action<string, ComputerSnapshot, int> Diagnostic;
        internal static long Ticket() { lock (captureGate) return generation; }
        internal static void CheckTicket(long ticket)
        {
            lock (captureGate)
                if (ticket != generation || captures.IsCancellationRequested) throw new OperationCanceledException("The requested screen observation was cancelled.");
        }

        internal static byte[] Request(string focus, string screen = null) { return Observe(focus, screen).Png; }

        // Metadata validation never retries or recaptures inference. Board-pixel validity is a separate task check.
        internal static void Validate(EyesObservation observation)
        {
            CheckTicket(observation.Ticket);
            if (observation.Frame == null) return;
            CancellationToken token;
            lock (captureGate) token = captures.Token;
            HyprlandDesktop desktop = HyprlandDesktop.Current;
            if (desktop == null) throw new InvalidOperationException("The native desktop connection changed during observation.");
            ComputerSnapshot source = observation.Frame.Source;
            ComputerSnapshot after = new ArchComputer(desktop, token, true).Observe(source.Output.Name, observation.WindowCrop ? source.Target : null);
            ArchComputer.SameObservation(source, after, observation.WindowCrop);
            CheckTicket(observation.Ticket);
        }

        internal static EyesObservation Observe(string focus, string screen = null)
        {
            CancellationToken token;
            long ticket;
            lock (captureGate) { token = captures.Token; ticket = generation; }
            if (Shoot != defaultShoot)
            {
                byte[] picture = Shoot();
                CheckTicket(ticket);
                return new EyesObservation(picture, null, ticket);
            }
            return RequestNative(focus, screen, false, ticket, token);
        }

        internal static void Stop()
        {
            lock (captureGate) { generation++; captures.Cancel(); captures.Dispose(); captures = new CancellationTokenSource(); }
        }

        private static byte[] RequestNative(string focus, bool quiet)
        {
            CancellationToken token;
            long ticket;
            lock (captureGate) { token = captures.Token; ticket = generation; }
            return RequestNative(focus, null, quiet, ticket, token).Png;
        }

        private static EyesObservation RequestNative(string focus, string screen, bool quiet, long ticket, CancellationToken token)
        {
            CheckTicket(ticket);
            Stopwatch clock = Stopwatch.StartNew();
            HyprlandDesktop desktop = HyprlandDesktop.Current;
            if (desktop == null)
            {
                if (!string.IsNullOrWhiteSpace(screen) || ScreenSelection.HasHint(focus))
                    throw new InvalidOperationException(OperatingSystem.IsMacOS() ? Hands.MacScreen : "I can't select that display on this desktop. Requested display selection requires the native Hyprland connection.");
                byte[] picture = Screenshot(quiet ? QuietTools(OperatingSystem.IsMacOS(), Session, Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP")) : Tools(OperatingSystem.IsMacOS(), Session, Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP")), OperatingSystem.IsMacOS());
                CheckTicket(ticket);
                return new EyesObservation(picture, null, ticket);
            }
            ArchComputer computer = new ArchComputer(desktop, token, true);
            string session = null, layout = null, selection = null, boundOutput = null;
            ComputerTarget boundTarget = null;
            long observationEpoch = desktop.ObservationEpoch;
            void Trace(string stage, ComputerSnapshot source, int attempt, Exception error = null)
            {
                Diagnostic?.Invoke(stage, source, attempt);
                VisionDiagnostics.Write("capture-" + ticket, stage, ticket, 0, clock.Elapsed.TotalMilliseconds, new
                {
                    attempt, requested_screen_hash = VisionDiagnostics.Hash(screen),
                    output = source?.Output,
                    target = source?.Target == null ? null : new { source.Target.Address, source.Target.Pid, source.Target.Birth, source.Target.Area, source.Target.Monitor, source.Target.Workspace },
                    observation_epoch = source?.ObservationEpoch,
                    action_epoch = desktop.ActionEpoch,
                    live_observation_epoch = desktop.ObservationEpoch,
                    events = desktop.ObservationEvents,
                    error_type = error?.GetType().Name, raw_error = error?.Message
                });
            }
            ComputerFrame frame = Fresh(attempt =>
            {
                HyprlandSnapshot map;
                try { map = desktop.ObserveSnapshotAsync(token).GetAwaiter().GetResult(); }
                catch (HyprlandObservationChangedException error) { throw new ComputerObservationChangedException(error.Message); }
                if (map.Locked) throw new InvalidOperationException("Screen capture is unavailable while the session is locked.");
                VisionDiagnostics.Write("capture-" + ticket, "map", ticket, 0, clock.Elapsed.TotalMilliseconds, new
                {
                    attempt, requested_screen_hash = VisionDiagnostics.Hash(screen),
                    session = VisionDiagnostics.Hash(map.SessionId), layout = VisionDiagnostics.Hash(map.LayoutId),
                    observation_epoch = map.ObservationEpoch,
                    outputs = map.Outputs.Take(16).Select(o => new { o.Name, o.Logical, o.Scale, o.Transform, o.PixelWidth, o.PixelHeight, o.Focused })
                });
                string selected = ScreenSelection.Resolve(map, screen, focus);
                if (session == null) { session = map.SessionId; layout = map.LayoutId; selection = selected; }
                else if (session != map.SessionId || layout != map.LayoutId || selection != selected)
                    throw new InvalidOperationException("The requested display or layout changed during observation. Ask me to look again.");
                bool crop = selected == null;
                ComputerSnapshot source = computer.Observe(boundOutput ?? selected, crop ? boundTarget : null);
                if (source.Layout != map.LayoutId) throw new ComputerObservationChangedException("The output layout changed during display selection.");
                if (observationEpoch != source.ObservationEpoch)
                    throw new InvalidOperationException("The desktop was locked or its connected outputs changed during observation. Ask me to look again.");
                boundOutput ??= source.Output.Name;
                if (crop) boundTarget ??= source.Target;
                Trace("selected", source, attempt);
                CheckTicket(ticket);
                ComputerFrame captured = computer.CaptureObservation(source, crop, token);
                CheckTicket(ticket);
                Trace("captured", captured.Source, attempt);
                return captured;
            }, () => { token.ThrowIfCancellationRequested(); CheckTicket(ticket); }, (attempt, error) => Trace("revoked", null, attempt, error));
            return new EyesObservation(frame.Png, frame, ticket, selection == null);
        }

        // Only revoked observations retry. Selection errors, locks, cancellation and inference never replay.
        internal static T Fresh<T>(Func<int, T> attempt, Action current, Action<int, Exception> revoked = null)
        {
            for (int number = 1; ; number++)
            {
                current();
                try
                {
                    T result = attempt(number);
                    current();
                    return result;
                }
                catch (ComputerObservationChangedException error)
                {
                    revoked?.Invoke(number, error);
                    if (number >= 3) throw;
                    current();
                }
            }
        }

        internal static string Unwatchable
        {
            get
            {
                return UnwatchableFor(OperatingSystem.IsMacOS(), Session, Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"));
            }
        }

        internal static string UnwatchableFor(bool mac, string session, string desktop)
        {
            if (mac) return "I can't watch your screen yet. Allow Capri under System Settings > Privacy & Security > Screen Recording, then open me again. If I'm already on that list, remove me with the minus button first.";
            if (Kde(desktop) && string.Equals(session, "wayland", StringComparison.OrdinalIgnoreCase)) return "I can't watch your screen yet. Install Spectacle for KDE, then ask again.";
            return "I can't watch your screen quietly here: this desktop's screenshots flash or ask first. I'll still look whenever you ask me to check your screen. On X11, installing scrot or maim fixes it.";
        }

        private const string Graphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        [DllImport(Graphics)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CGPreflightScreenCaptureAccess();
        [DllImport(Graphics)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CGRequestScreenCaptureAccess();
        private static bool askedToRecord;
        private static bool recordingTold;
        internal static Func<bool> ScreenAllowed = CGPreflightScreenCaptureAccess;
        internal static Func<bool> ScreenRequest = Recordable;
        internal static Func<string[], string, bool> Take = TakeFile;

        internal static bool CanRecord()
        {
            return ScreenAllowed();
        }

        internal static string ScreenProblem()
        {
            lock (captureGate)
            {
                if (!recordingTold)
                {
                    recordingTold = true;
                    ScreenRequest();
                }
                return MissingFor(true, null, null);
            }
        }

        // Without Screen Recording a Mac screenshot is just the wallpaper, so she'd skip every glance without saying why. The first
        // time, Mac asks them; until they allow it and open her again, she says once that she can't watch.
        private static bool Recordable()
        {
            if (!OperatingSystem.IsMacOS()) return true;
            try
            {
                if (CanRecord()) return true;
                if (askedToRecord) return false;
                askedToRecord = true;
                try { ForgetOldBuild(); }
                catch (Exception) { }
                return CGRequestScreenCaptureAccess();
            }
            catch (Exception) { return true; }
        }

        // Each release is signed on its own, so macOS keeps showing Capri as allowed while that only fits the version they allowed,
        // and deleting her doesn't clear it (ASAP, 31 Oct: "7.6 is the only one that can watch my screen"). Clearing it once per
        // version lets Mac ask again.
        private static void ForgetOldBuild()
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Capri");
            string marker = Path.Combine(folder, "screen-asked.txt");
            string version = typeof(Eyes).Assembly.GetName().Version.ToString();
            if (!FreshAsk(File.Exists(marker) ? File.ReadAllText(marker) : null, version)) return;
            Directory.CreateDirectory(folder);
            File.WriteAllText(marker, version);
            ProcessStartInfo reset = new ProcessStartInfo("tccutil") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string word in new[] { "reset", "ScreenCapture", "io.github.lachydotmcg.capri" }) reset.ArgumentList.Add(word);
            using (Process clearing = Process.Start(reset)) clearing.WaitForExit(5000);
        }

        internal static bool FreshAsk(string asked, string version) { return (asked ?? "").Trim() != version; }

        internal static string Missing
        {
            get
            {
                return MissingFor(OperatingSystem.IsMacOS(), Session, Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"));
            }
        }

        internal static string MissingFor(bool mac, string session, string desktop)
        {
            // Said as it is when the step failed, so it's written for them, like the Linux line below.
            if (mac) return "I need Screen Recording to see your screen. Turn Capri on in System Settings > Privacy & Security > Screen Recording, then reopen me. If I'm already on that list, remove me with the minus button first.";
            string tool = Kde(desktop) ? "Spectacle for KDE" : string.Equals(session, "wayland", StringComparison.OrdinalIgnoreCase)
                ? (desktop ?? "").ToLowerInvariant().Contains("gnome") ? "gnome-screenshot for GNOME" : "grim for your desktop" : "scrot or maim";
            return "Couldn't take a screenshot. Install " + tool + ", then ask again.";
        }

        private static bool Kde(string desktop) { return (desktop ?? "").ToLowerInvariant().Contains("kde"); }

        // The tools that can take a screenshot here, best first, with "{0}" where the file to write goes.
        internal static List<string[]> Tools(bool mac, string session, string desktop)
        {
            List<string[]> tools = new List<string[]>();
            if (mac)
            {
                tools.Add(new[] { "screencapture", "-x", "-t", "jpg", "{0}" });
                return tools;
            }
            string where = (desktop ?? "").ToLowerInvariant();
            string[] spectacle = { "spectacle", "-b", "-n", "-f", "-o", "{0}" };
            string[] gnome = { "gnome-screenshot", "-f", "{0}" };
            if (string.Equals(session, "wayland", StringComparison.OrdinalIgnoreCase))
            {
                if (Kde(desktop)) { tools.Add(spectacle); return tools; }
                if (where.Contains("gnome")) tools.Add(gnome);
                tools.Add(new[] { "grim", "{0}" });
                tools.Add(spectacle);
                if (!where.Contains("gnome")) tools.Add(gnome);
                return tools;
            }
            // ImageMagick's import rings the bell once before it grabs the screen and twice after, unless it's told to be silent.
            tools.Add(new[] { "import", "-silent", "-window", "root", "{0}" });
            tools.Add(new[] { "scrot", "{0}" });
            tools.Add(new[] { "maim", "{0}" });
            tools.Add(gnome);
            tools.Add(spectacle);
            tools.Add(new[] { "xfce4-screenshooter", "-f", "-s", "{0}" });
            return tools;
        }

        // Watching takes a screenshot every few minutes, so it skips gnome-screenshot (a flash and a shutter sound), the XFCE tool,
        // and Spectacle outside KDE's Wayland, where it would ask first.
        internal static List<string[]> QuietTools(bool mac, string session, string desktop)
        {
            List<string[]> quiet = new List<string[]>();
            bool wayland = string.Equals(session, "wayland", StringComparison.OrdinalIgnoreCase);
            bool kde = Kde(desktop);
            foreach (string[] tool in Tools(mac, session, desktop))
            {
                if (tool[0] == "gnome-screenshot" || tool[0] == "xfce4-screenshooter") continue;
                if (tool[0] == "spectacle" && wayland && !kde) continue;
                quiet.Add(tool);
            }
            return quiet;
        }

        internal static byte[] Screenshot(List<string[]> tools, bool mac)
        {
            if (mac && !CanRecord()) throw new InvalidOperationException(ScreenProblem());
            foreach (string[] tool in tools)
            {
                string path = Path.Combine(Path.GetTempPath(), "capri-screen-" + Guid.NewGuid().ToString("N") + (mac ? ".jpg" : ".png"));
                byte[] shot = null;
                try
                {
                    if (!Take(tool, path)) continue;
                    shot = File.ReadAllBytes(path);
                }
                catch (Exception) { }
                finally
                {
                    try { File.Delete(path); }
                    catch (Exception) { }
                }
                if (mac && !CanRecord()) throw new InvalidOperationException(ScreenProblem());
                if (shot != null && shot.Length > 0) return shot;
            }
            return null;
        }

        // A tool that isn't installed throws when it starts, so the next one gets a turn.
        private static bool TakeFile(string[] tool, string path)
        {
            ProcessStartInfo start = new ProcessStartInfo(tool[0]) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            for (int i = 1; i < tool.Length; i++) start.ArgumentList.Add(tool[i] == "{0}" ? path : tool[i]);
            try
            {
                using (Process process = Process.Start(start))
                {
                    if (process == null) return false;
                    process.StandardOutput.ReadToEndAsync();
                    process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(10000))
                    {
                        try { process.Kill(); }
                        catch (Exception) { }
                        return false;
                    }
                    return process.ExitCode == 0 && File.Exists(path);
                }
            }
            catch (Exception) { return false; }
        }

        // The screen's fingerprint for Glances.Moved: the picture shrunk to 64 x 36, as RGB bytes. Null if it can't be read.
        internal static byte[] Print(byte[] picture)
        {
            if (picture == null || picture.Length == 0) return null;
            SKBitmap read;
            try { read = SKBitmap.Decode(picture); }
            catch (Exception) { return null; }
            using (SKBitmap original = read)
            {
                if (original == null || original.Width <= 0 || original.Height <= 0) return null;
                using (SKBitmap tiny = original.Resize(new SKImageInfo(64, 36), SKFilterQuality.Medium))
                {
                    if (tiny == null) return null;
                    byte[] print = new byte[64 * 36 * 3];
                    for (int y = 0; y < 36; y++)
                        for (int x = 0; x < 64; x++)
                        {
                            SKColor pixel = tiny.GetPixel(x, y);
                            int i = (y * 64 + x) * 3;
                            print[i] = pixel.Red;
                            print[i + 1] = pixel.Green;
                            print[i + 2] = pixel.Blue;
                        }
                    return print;
                }
            }
        }

        // The picture as a JPEG at most this wide, the size her brain looks at on Windows too. Null if it can't be read.
        internal static byte[] Jpeg(byte[] picture, int widest)
        {
            if (picture == null || picture.Length == 0) return null;
            // Skia throws, rather than giving back null, for bytes it can't read.
            SKBitmap read;
            try { read = SKBitmap.Decode(picture); }
            catch (Exception) { return null; }
            using (SKBitmap original = read)
            {
                if (original == null || original.Width <= 0 || original.Height <= 0) return null;
                SKBitmap scaled = original;
                if (original.Width > widest)
                {
                    scaled = original.Resize(new SKImageInfo(widest, Math.Max(1, (int)((long)original.Height * widest / original.Width))), SKFilterQuality.Medium);
                    if (scaled == null) return null;
                }
                try
                {
                    using (SKImage image = SKImage.FromBitmap(scaled))
                    using (SKData data = image == null ? null : image.Encode(SKEncodedImageFormat.Jpeg, 80))
                        return data == null ? null : data.ToArray();
                }
                finally
                {
                    if (scaled != original) scaled.Dispose();
                }
            }
        }
    }
}

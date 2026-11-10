using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace CapriDesktop
{
    internal static class MacFingers
    {
        internal const string Permission = "Turn Capri on in System Settings > Privacy & Security > Accessibility, then ask again.";
        internal const string Pane = "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility";
        private const string Graphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        private const string Access = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
        private const string Core = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private static readonly object gate = new object();
        private static bool opened;
        internal static Func<bool> Trusted = AXIsProcessTrusted;
        internal static Action<string, string[]> Launch = Limbs.Launch;
        internal static Action<Event> Sink = Post;
        internal static Action<int> Pause = Thread.Sleep;
        internal static Func<Point> Position = Cursor;
        internal static Func<Display[]> Displays = Screens;
        private static int inputPane;

        [StructLayout(LayoutKind.Sequential)]
        internal struct Point
        {
            internal double X, Y;
            internal Point(double x, double y) { X = x; Y = y; }
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect
        {
            internal Point Origin, Size;
            internal ComputerRect Area => new ComputerRect((int)Origin.X, (int)Origin.Y, (int)Size.X, (int)Size.Y);
        }
        internal sealed record Display(uint Id, int Index, ComputerRect Area, int PixelsWide, int PixelsHigh);
        internal sealed record Event(uint Kind, Point At, int Button = 0, int Count = 0, ushort Key = 0, ulong Flags = 0, string Text = null, int Wheel = 0);

        [DllImport(Access)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool AXIsProcessTrusted();
        [DllImport(Graphics)] private static extern IntPtr CGEventCreateMouseEvent(IntPtr source, uint kind, Point point, uint button);
        [DllImport(Graphics)] private static extern IntPtr CGEventCreateScrollWheelEvent2(IntPtr source, uint units, uint wheels, int first, int second, int third);
        [DllImport(Graphics)] private static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort key, [MarshalAs(UnmanagedType.I1)] bool down);
        [DllImport(Graphics)] private static extern void CGEventKeyboardSetUnicodeString(IntPtr ev, UIntPtr length, [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U2)] char[] text);
        [DllImport(Graphics)] private static extern void CGEventSetFlags(IntPtr ev, ulong flags);
        [DllImport(Graphics)] private static extern void CGEventSetIntegerValueField(IntPtr ev, uint field, long value);
        [DllImport(Graphics)] private static extern long CGEventGetIntegerValueField(IntPtr ev, uint field);
        [DllImport(Graphics)] private static extern void CGEventPost(uint tap, IntPtr ev);
        [DllImport(Graphics)] private static extern IntPtr CGEventCreate(IntPtr source);
        [DllImport(Graphics)] private static extern Point CGEventGetLocation(IntPtr ev);
        [DllImport(Graphics)] private static extern int CGGetActiveDisplayList(uint maximum, [Out] uint[] ids, out uint count);
        [DllImport(Graphics)] private static extern uint CGMainDisplayID();
        [DllImport(Graphics)] private static extern Rect CGDisplayBounds(uint id);
        [DllImport(Graphics)] private static extern UIntPtr CGDisplayPixelsWide(uint id);
        [DllImport(Graphics)] private static extern UIntPtr CGDisplayPixelsHigh(uint id);
        [DllImport(Graphics)] private static extern IntPtr CGWindowListCopyWindowInfo(uint options, uint relative);
        [DllImport(Graphics)] private static extern IntPtr CGSessionCopyCurrentDictionary();
        [DllImport(Graphics)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CGPreflightScreenCaptureAccess();
        [DllImport(Graphics)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CGPreflightListenEventAccess();
        [DllImport(Core)] private static extern void CFRelease(IntPtr value);
        [DllImport(Core)] private static extern nint CFArrayGetCount(IntPtr array);
        [DllImport(Core)] private static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, nint index);
        [DllImport(Core)] private static extern IntPtr CFStringCreateWithCharacters(IntPtr allocator, [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U2)] char[] text, nint length);
        [DllImport(Core)] private static extern IntPtr CFDictionaryGetValue(IntPtr dictionary, IntPtr key);
        [DllImport(Core)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CFNumberGetValue(IntPtr number, int kind, out long value);
        [DllImport(Core)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CFBooleanGetValue(IntPtr value);
        [DllImport(Core)] private static extern nint CFStringGetLength(IntPtr value);
        [DllImport(Core)] private static extern void CFStringGetCharacters(IntPtr value, Range range, [Out] char[] text);
        [StructLayout(LayoutKind.Sequential)] private struct Range { internal nint Start, Length; }
        [DllImport(Graphics)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CGRectMakeWithDictionaryRepresentation(IntPtr dictionary, out Rect rect);
        [DllImport(Access)] private static extern IntPtr AXUIElementCreateSystemWide();
        [DllImport(Access)] private static extern int AXUIElementCopyAttributeValue(IntPtr element, IntPtr attribute, out IntPtr value);
        [DllImport(Access)] private static extern int AXUIElementGetPid(IntPtr element, out int pid);

        internal static bool Available
        {
            get
            {
                if (!OperatingSystem.IsMacOS()) return false;
                return Allowed();
            }
        }

        private static bool Allowed()
        {
            bool allowed = Trusted();
            if (allowed) lock (gate) opened = false;
            return allowed;
        }

        internal static string Problem()
        {
            if (Allowed()) return null;
            lock (gate)
            {
                if (!opened)
                {
                    opened = true;
                    try { Launch("/usr/bin/open", new[] { Pane }); }
                    catch (Exception error) { Brain.Log("accessibility", -1, error.Message); }
                }
            }
            return Permission;
        }

        private static void Ready()
        {
            string problem = Problem();
            if (problem != null) throw new InvalidOperationException(problem);
        }

        private static void Post(Event value)
        {
            IntPtr ev = value.Kind == 10 || value.Kind == 11
                ? CGEventCreateKeyboardEvent(IntPtr.Zero, value.Key, value.Kind == 10)
                : value.Kind == 22 ? CGEventCreateScrollWheelEvent2(IntPtr.Zero, 1, 1, value.Wheel, 0, 0)
                : CGEventCreateMouseEvent(IntPtr.Zero, value.Kind, value.At, (uint)value.Button);
            if (ev == IntPtr.Zero) throw new InvalidOperationException("Couldn't create a Mac input event.");
            try
            {
                CGEventSetFlags(ev, value.Flags);
                CGEventSetIntegerValueField(ev, 42, Environment.ProcessId);
                if (value.Count > 0) CGEventSetIntegerValueField(ev, 1, value.Count);
                if (value.Text != null) CGEventKeyboardSetUnicodeString(ev, (UIntPtr)value.Text.Length, value.Text.ToCharArray());
                CGEventPost(0, ev);
            }
            finally { CFRelease(ev); }
        }

        private static Point Cursor()
        {
            IntPtr ev = CGEventCreate(IntPtr.Zero);
            if (ev == IntPtr.Zero) throw new InvalidOperationException("Couldn't read the Mac pointer.");
            try { return CGEventGetLocation(ev); }
            finally { CFRelease(ev); }
        }

        private static Display[] Screens()
        {
            uint[] ids = new uint[32];
            if (CGGetActiveDisplayList((uint)ids.Length, ids, out uint count) != 0 || count == 0 || count >= ids.Length)
                throw new InvalidOperationException("Couldn't read the Mac display layout.");
            uint main = CGMainDisplayID();
            var ordered = ids.Take((int)count).OrderBy(id => id == main ? 0 : 1).ToArray();
            return ordered.Select((id, index) => new Display(id, index + 1, CGDisplayBounds(id).Area, (int)CGDisplayPixelsWide(id), (int)CGDisplayPixelsHigh(id))).ToArray();
        }

        internal static ComputerRect Bounds(Display[] displays)
        {
            int x = displays.Min(d => d.Area.X), y = displays.Min(d => d.Area.Y);
            return new ComputerRect(x, y, displays.Max(d => d.Area.X + d.Area.Width) - x, displays.Max(d => d.Area.Y + d.Area.Height) - y);
        }

        internal static Point Fraction(double x, double y)
        {
            ComputerRect area = Bounds(Displays());
            return new Point(area.X + Math.Clamp(x, 0, 1) * (area.Width - 1), area.Y + Math.Clamp(y, 0, 1) * (area.Height - 1));
        }

        internal static double[] Mouse()
        {
            Point at = Position();
            ComputerRect area = Bounds(Displays());
            return new[] { (at.X - area.X) / Math.Max(1, area.Width - 1), (at.Y - area.Y) / Math.Max(1, area.Height - 1) };
        }

        internal static void Move(double x, double y, bool drag = false)
        {
            Ready();
            Sink(new Event(drag ? 6u : 5u, new Point(x, y)));
            Pause(20);
        }

        internal static void MoveTo(double x, double y)
        {
            Point at = Fraction(x, y);
            Move(at.X, at.Y);
            Fingers.LastX = Math.Clamp(x, 0, 1); Fingers.LastY = Math.Clamp(y, 0, 1);
        }

        private static uint Down(string button) => button == "right" ? 3u : button == "middle" ? 25u : 1u;
        private static int Button(string button) => button == "right" ? 1 : button == "middle" ? 2 : 0;
        internal static void Click(string button, int times, Action guard = null)
        {
            Ready();
            for (int i = 1; i <= Math.Max(1, times); i++)
            {
                guard?.Invoke();
                Point at = Position();
                try { Sink(new Event(Down(button), at, Button(button), i)); Pause(30); }
                finally { Sink(new Event(Down(button) + 1, at, Button(button), i)); }
                Pause(70);
            }
        }

        internal static void Drag(double x, double y, double endX, double endY, int duration = 500, Action<double, double> guard = null)
        {
            Ready();
            Move(x, y);
            Point at = new Point(x, y);
            try
            {
                guard?.Invoke(x, y);
                Sink(new Event(1, at, Count: 1));
                for (int step = 1; step <= 20; step++)
                {
                    guard?.Invoke(at.X, at.Y);
                    at = new Point(x + (endX - x) * step / 20, y + (endY - y) * step / 20);
                    Sink(new Event(6, at));
                    Pause(Math.Max(1, duration / 20));
                }
            }
            finally { Sink(new Event(2, at, Count: 1)); }
        }

        internal static void Scroll(int notches, Action guard = null)
        {
            Ready();
            for (long i = 0; i < Math.Abs((long)notches); i++)
            {
                guard?.Invoke();
                Sink(new Event(22, Position(), Wheel: notches > 0 ? -1 : 1)); Pause(30);
            }
        }

        private static readonly Dictionary<string, ushort> Named = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            { "a", 0 }, { "s", 1 }, { "d", 2 }, { "f", 3 }, { "h", 4 }, { "g", 5 }, { "z", 6 }, { "x", 7 }, { "c", 8 }, { "v", 9 }, { "b", 11 },
            { "q", 12 }, { "w", 13 }, { "e", 14 }, { "r", 15 }, { "y", 16 }, { "t", 17 }, { "1", 18 }, { "2", 19 }, { "3", 20 }, { "4", 21 },
            { "6", 22 }, { "5", 23 }, { "equals", 24 }, { "=", 24 }, { "9", 25 }, { "7", 26 }, { "minus", 27 }, { "-", 27 }, { "8", 28 }, { "0", 29 },
            { "]", 30 }, { "o", 31 }, { "u", 32 }, { "[", 33 }, { "i", 34 }, { "p", 35 }, { "enter", 36 }, { "return", 36 }, { "l", 37 }, { "j", 38 },
            { "quote", 39 }, { "k", 40 }, { "semicolon", 41 }, { "backslash", 42 }, { "comma", 43 }, { ",", 43 }, { "slash", 44 }, { "/", 44 }, { "n", 45 },
            { "m", 46 }, { "period", 47 }, { ".", 47 }, { "tab", 48 }, { "space", 49 }, { "grave", 50 }, { "backspace", 51 }, { "esc", 53 }, { "escape", 53 },
            { "cmd", 55 }, { "command", 55 }, { "meta", 55 }, { "super", 55 }, { "win", 55 }, { "shift", 56 }, { "capslock", 57 }, { "alt", 58 }, { "option", 58 },
            { "altgr", 61 }, { "ctrl", 59 }, { "control", 59 }, { "fn", 63 }, { "f1", 122 }, { "f2", 120 }, { "f3", 99 }, { "f4", 118 }, { "f5", 96 },
            { "f6", 97 }, { "f7", 98 }, { "f8", 100 }, { "f9", 101 }, { "f10", 109 }, { "f11", 103 }, { "f12", 111 }, { "home", 115 }, { "pageup", 116 },
            { "delete", 117 }, { "del", 117 }, { "end", 119 }, { "pagedown", 121 }, { "left", 123 }, { "right", 124 }, { "down", 125 }, { "up", 126 }
        };

        internal static int Code(string name) => Named.TryGetValue(name, out ushort value) ? value : -1;
        private static ulong Flag(int code) => code == 55 ? 1UL << 20 : code == 56 ? 1UL << 17 : code == 58 || code == 61 ? 1UL << 19 : code == 59 ? 1UL << 18 : code == 63 ? 1UL << 23 : 0;

        internal static bool Keys(string chord, Action guard = null)
        {
            int[] codes = (chord ?? "").Replace(" ", "").Split('+').Where(part => part.Length > 0).Select(Code).ToArray();
            if (codes.Length == 0 || codes.Any(code => code < 0)) return false;
            Ready();
            ulong flags = 0;
            List<int> down = new List<int>();
            try
            {
                foreach (int code in codes)
                {
                    guard?.Invoke(); flags |= Flag(code); down.Add(code);
                    Sink(new Event(10, default, Key: (ushort)code, Flags: flags)); Pause(15);
                }
            }
            finally
            {
                for (int i = down.Count - 1; i >= 0; i--)
                {
                    flags &= ~Flag(down[i]);
                    Sink(new Event(11, default, Key: (ushort)down[i], Flags: flags)); Pause(15);
                }
            }
            Pause(60); return true;
        }

        internal static bool Type(string text, Action guard = null)
        {
            Ready();
            foreach (var rune in (text ?? "").EnumerateRunes())
            {
                guard?.Invoke();
                string value = rune.ToString();
                try { Sink(new Event(10, default, Text: value)); }
                finally { Sink(new Event(11, default, Text: value)); }
                Pause(12);
            }
            return true;
        }

        internal static string JobKeys(string keys)
        {
            string value = (keys ?? "").ToLowerInvariant();
            if (value == "alt+f4") return "cmd+w";
            if (value == "alt+tab") return "cmd+tab";
            if (value == "super+d" || value == "win+d") return "fn+f11";
            if (value == "super+l" || value == "win+l") return "ctrl+cmd+q";
            if (value == "alt+left") return "cmd+[";
            if (value == "alt+right") return "cmd+]";
            if (value == "ctrl+y") return "cmd+shift+z";
            if (value == "ctrl+tab" || value == "ctrl+shift+tab") return value;
            return value.Replace("ctrl+", "cmd+");
        }

        internal static string ChordKeys(byte[] keys)
        {
            string hex = string.Join(" ", keys.Select(key => key.ToString("X2")));
            if (hex == "5B 4C") return "ctrl+cmd+q";
            if (hex == "5B 44") return "fn+f11";
            if (hex == "12 09") return "cmd+tab";
            if (hex == "12 73") return "cmd+w";
            if (hex == "11 09") return "ctrl+tab";
            if (hex == "11 10 09") return "ctrl+shift+tab";
            if (hex == "11 59") return "cmd+shift+z";
            List<string> names = new List<string>();
            foreach (byte key in keys)
            {
                string name = key == 0x11 ? "cmd" : key == 0x10 ? "shift" : key == 0x12 ? "option" : key == 0x5B ? "cmd"
                    : key == 0x09 ? "tab" : key == 0x0D ? "enter" : key == 0x1B ? "esc" : key == 0x20 ? "space"
                    : key >= 0x41 && key <= 0x5A || key >= 0x30 && key <= 0x39 ? ((char)key).ToString().ToLowerInvariant()
                    : key >= 0x70 && key <= 0x7B ? "f" + (key - 0x6F) : null;
                if (name == null) throw new InvalidOperationException("That shortcut has no Mac key mapping.");
                names.Add(name);
            }
            return string.Join("+", names);
        }

        internal static void Chord(byte[] keys)
        {
            using (InControl.Hold())
                if (!Keys(ChordKeys(keys))) throw new InvalidOperationException("That shortcut has no Mac key mapping.");
        }

        internal static readonly Dictionary<string, string> Shortcuts = new Dictionary<string, string>
        {
            { "discord_mute", "cmd+shift+m" }, { "discord_deafen", "cmd+shift+d" }, { "show_desktop", "fn+f11" },
            { "emoji", "ctrl+cmd+space" }, { "narrator", "cmd+f5" }, { "magnifier", "option+cmd+equals" }, { "record", "cmd+shift+5" }, { "quick_settings", "fn+c" }, { "voice_typing", "fn+d" }
        };
        internal static readonly string[] ClipboardKeys = { "cmd+space", "cmd+4" };

        internal static string Shortcut(string name)
        {
            if (name == "clipboard")
            {
                if (!OperatingSystem.IsMacOSVersionAtLeast(26)) return "Clipboard history needs macOS Tahoe 26 or later.";
                string missing = Problem();
                if (missing != null) return missing;
                using (InControl.Hold())
                {
                    Keys(ClipboardKeys[0]); Pause(200); Keys(ClipboardKeys[1]);
                }
                return "Opened Spotlight's clipboard history. Choose Allow if Spotlight asks.";
            }
            if (!Shortcuts.TryGetValue(name, out string keys)) return null;
            string problem = Problem();
            if (problem != null) return problem;
            if (name.StartsWith("discord_", StringComparison.Ordinal))
            {
                ComputerSnapshot snapshot = new Desktop().Snapshot();
                if (snapshot.Target.App.IndexOf("discord", StringComparison.OrdinalIgnoreCase) < 0) return "Bring Discord to the front, then ask again.";
            }
            using (InControl.Hold()) Keys(keys);
            if (name == "magnifier") return "Sent the Mac zoom shortcut. Enable keyboard shortcuts in System Settings > Accessibility > Zoom if it did nothing.";
            if (name == "record") return "Opened screenshot and recording controls. Choose Record, or use the stop button in the menu bar.";
            if (name == "voice_typing") return "Sent the Dictation shortcut. Enable Dictation in System Settings > Keyboard if it did nothing.";
            return "Sent " + keys.Replace("+", " + ") + ".";
        }

        internal static bool GameMatches(ComputerJob job)
        {
            string owner = job.Asked ?? job.Task ?? "";
            string app = job.Binding.Target.App;
            string wanted = Regex.Replace(owner.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ");
            string name = Regex.Replace(app.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();
            bool named = name.Length > 1 && (" " + wanted + " ").Contains(" " + name + " ", StringComparison.Ordinal);
            bool current = Regex.IsMatch(owner, @"(?i)^\s*(?:(?:please|can you|could you)\s+)?(?:play|continue|resume)(?:\s+(?:(?:the|this|current|open|active)\s+)*game)?(?:\s+with me)?(?:\s+(?:for|until)\b.*)?\s*[.!?]*$");
            if (named || current) job.Game.AppName = app;
            return named || current;
        }

        private static IntPtr Field(IntPtr dictionary, string name)
        {
            IntPtr key = CFStringCreateWithCharacters(IntPtr.Zero, name.ToCharArray(), name.Length);
            try { return CFDictionaryGetValue(dictionary, key); }
            finally { CFRelease(key); }
        }
        private static long Number(IntPtr dictionary, string name)
        {
            IntPtr value = Field(dictionary, name);
            if (value == IntPtr.Zero || !CFNumberGetValue(value, 4, out long number)) throw new InvalidOperationException("Couldn't read the Mac window.");
            return number;
        }
        private static string Text(IntPtr dictionary, string name)
        {
            IntPtr value = Field(dictionary, name);
            if (value == IntPtr.Zero) return "";
            nint length = CFStringGetLength(value);
            char[] text = new char[(int)length];
            CFStringGetCharacters(value, new Range { Length = length }, text);
            return new string(text);
        }

        private static int Focused()
        {
            IntPtr system = AXUIElementCreateSystemWide(), app = IntPtr.Zero;
            IntPtr key = CFStringCreateWithCharacters(IntPtr.Zero, "AXFocusedApplication".ToCharArray(), 20);
            try
            {
                if (AXUIElementCopyAttributeValue(system, key, out app) != 0 || app == IntPtr.Zero || AXUIElementGetPid(app, out int pid) != 0)
                    throw new InvalidOperationException("Couldn't read the focused Mac app.");
                return pid;
            }
            finally { if (app != IntPtr.Zero) CFRelease(app); CFRelease(key); CFRelease(system); }
        }

        private static string Session()
        {
            IntPtr session = CGSessionCopyCurrentDictionary();
            if (session == IntPtr.Zero) throw new InvalidOperationException("The Mac desktop session is unavailable.");
            try
            {
                IntPtr console = Field(session, "kCGSSessionOnConsoleKey"), done = Field(session, "kCGSessionLoginDoneKey");
                if (console == IntPtr.Zero || !CFBooleanGetValue(console) || done == IntPtr.Zero || !CFBooleanGetValue(done))
                    throw new InvalidOperationException("The Mac desktop session is inactive.");
                IntPtr user = Field(session, "kCGSSessionUserIDKey");
                if (user == IntPtr.Zero || !CFNumberGetValue(user, 4, out long id)) throw new InvalidOperationException("Couldn't read the Mac desktop session.");
                return id.ToString(CultureInfo.InvariantCulture);
            }
            finally { CFRelease(session); }
        }

        internal static ProcessStartInfo CaptureCommand(Display display, string path)
        {
            return ComputerProcess.Start("/usr/sbin/screencapture", new[] { "-x", "-D", display.Index.ToString(CultureInfo.InvariantCulture), "-t", "png", path });
        }

        private static byte[] Capture(Display display, CancellationToken token)
        {
            if (!CGPreflightScreenCaptureAccess()) throw new InvalidOperationException(Eyes.MissingFor(true, null, null));
            string path = Path.Combine(Path.GetTempPath(), "capri-hands-" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                ProcessStartInfo start = CaptureCommand(display, path);
                ComputerProcess.Bytes(start.FileName, start.ArgumentList.ToArray(), token, 5000, 4096);
                if (!File.Exists(path)) throw new InvalidOperationException(Eyes.MissingFor(true, null, null));
                return File.ReadAllBytes(path);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        internal static byte[] Picture()
        {
            Display[] displays = Displays();
            ComputerRect area = Bounds(displays);
            using SKBitmap picture = new SKBitmap(area.Width, area.Height);
            using (SKCanvas canvas = new SKCanvas(picture))
            {
                canvas.Clear(SKColors.Black);
                foreach (Display display in displays)
                {
                    using SKBitmap image = SKBitmap.Decode(Capture(display, CancellationToken.None));
                    if (image == null) throw new InvalidOperationException("Couldn't read the Mac screenshot.");
                    canvas.DrawBitmap(image, new SKRect(display.Area.X - area.X, display.Area.Y - area.Y, display.Area.X + display.Area.Width - area.X, display.Area.Y + display.Area.Height - area.Y));
                }
            }
            using SKImage result = SKImage.FromBitmap(picture);
            using SKData data = result.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }

        internal sealed class Desktop : IComputerDesktop
        {
            private readonly CancellationToken lifetime;
            internal Desktop(CancellationToken lifetime = default) { this.lifetime = lifetime; }

            public ComputerSnapshot Snapshot(string output = null, bool focus = false)
            {
                lifetime.ThrowIfCancellationRequested(); Ready();
                string session = Session();
                Display[] displays = Displays();
                string layout = string.Join("|", displays.Select(d => d.ToString()));
                int active = Focused();
                IntPtr windows = CGWindowListCopyWindowInfo(1 | 16, 0);
                if (windows == IntPtr.Zero) throw new InvalidOperationException("Couldn't read the Mac windows.");
                try
                {
                    for (nint i = 0; i < CFArrayGetCount(windows); i++)
                    {
                        IntPtr window = CFArrayGetValueAtIndex(windows, i);
                        int pid = (int)Number(window, "kCGWindowOwnerPID");
                        if (pid == Environment.ProcessId || Number(window, "kCGWindowLayer") != 0) continue;
                        if (!CGRectMakeWithDictionaryRepresentation(Field(window, "kCGWindowBounds"), out Rect rect)) continue;
                        ComputerRect area = rect.Area;
                        if (area.Width <= 1 || area.Height <= 1) continue;
                        Display display = displays.OrderByDescending(d => { ComputerRect shared = d.Area.Intersect(area); return (long)shared.Width * shared.Height; }).First();
                        if (output != null && output != display.Id.ToString(CultureInfo.InvariantCulture)) throw new InvalidOperationException("The target moved to another display.");
                        ComputerOutput screen = new ComputerOutput(display.Id.ToString(CultureInfo.InvariantCulture), display.Area, (double)display.PixelsWide / display.Area.Width, 0, display.PixelsWide, display.PixelsHigh);
                        ComputerTarget target = new ComputerTarget("0x" + Number(window, "kCGWindowNumber").ToString("x"), pid, MacNative.ProcessIdentity(pid), Text(window, "kCGWindowOwnerName"), area, (int)display.Id, 0);
                        Point pointer = Position();
                        if (active != pid) throw new InvalidOperationException("Bring the target app to the front, then ask again.");
                        return new ComputerSnapshot(session, layout, target, screen, pointer.X, pointer.Y, true);
                    }
                    throw new InvalidOperationException("Bring the target app to the front, then ask again.");
                }
                finally { CFRelease(windows); }
            }

            public ComputerFrame Capture(ComputerSnapshot source, bool crop, CancellationToken token)
            {
                token.ThrowIfCancellationRequested(); lifetime.ThrowIfCancellationRequested();
                long taken = Stopwatch.GetTimestamp();
                ComputerJobs.Same(source, Snapshot(source.Output.Name), true);
                Display display = Displays().Single(d => d.Id.ToString(CultureInfo.InvariantCulture) == source.Output.Name);
                using SKBitmap image = SKBitmap.Decode(MacFingers.Capture(display, token)) ?? throw new InvalidOperationException("Couldn't read the Mac screenshot.");
                ComputerRect area = crop ? source.Target.Area.Intersect(display.Area) : display.Area;
                if (area.Width <= 0 || area.Height <= 0) throw new InvalidOperationException("The target is outside the Mac screenshot.");
                int left = (int)Math.Floor((area.X - display.Area.X) * (double)image.Width / display.Area.Width);
                int top = (int)Math.Floor((area.Y - display.Area.Y) * (double)image.Height / display.Area.Height);
                int right = (int)Math.Ceiling((area.X + area.Width - display.Area.X) * (double)image.Width / display.Area.Width);
                int bottom = (int)Math.Ceiling((area.Y + area.Height - display.Area.Y) * (double)image.Height / display.Area.Height);
                int width = Math.Min(1280, right - left), height = Math.Max(1, (int)Math.Round((bottom - top) * (double)width / (right - left)));
                using SKBitmap result = new SKBitmap(width, height);
                using (SKCanvas canvas = new SKCanvas(result)) canvas.DrawBitmap(image, new SKRect(left, top, right, bottom), new SKRect(0, 0, width, height));
                using SKImage encoded = SKImage.FromBitmap(result);
                using SKData png = encoded.Encode(SKEncodedImageFormat.Png, 100);
                using SKData jpg = encoded.Encode(SKEncodedImageFormat.Jpeg, 85);
                ComputerJobs.Same(source, Snapshot(source.Output.Name), true);
                return new ComputerFrame { Source = source, Area = area, Width = width, Height = height, Png = png.ToArray(), Jpeg = jpg.ToArray(), Taken = taken };
            }

            public void Move(int x, int y, CancellationToken token)
            {
                token.ThrowIfCancellationRequested(); lifetime.ThrowIfCancellationRequested();
                ComputerRect area = Bounds(Displays());
                Chores.Track((x - area.X) / (double)Math.Max(1, area.Width - 1), (y - area.Y) / (double)Math.Max(1, area.Height - 1));
                MacFingers.Move(x, y);
            }
        }

        internal sealed class Input : IComputerInput
        {
            public void Execute(ComputerAction action, ComputerFrame frame, IComputerDesktop desktop, Action<double, double> guard, CancellationToken token, Action<double, double> completed = null)
            {
                Ready();
                double x = frame.Source.PointerX, y = frame.Source.PointerY;
                void Check() { token.ThrowIfCancellationRequested(); Ready(); guard(x, y); }
                Check();
                if (action.Action == "click" || action.Action == "double_click" || action.Action == "drag" || action.Action == "scroll")
                {
                    var at = frame.ToLogical(action.X, action.Y);
                    if (!frame.Source.Target.Area.Contains(at.X, at.Y)) throw new InvalidOperationException("The point is outside the bound target window.");
                    desktop.Move(at.X, at.Y, token); x = at.X; y = at.Y; Check();
                }
                if (action.Action == "type") Type(action.Text, Check);
                else if (action.Action == "press")
                {
                    if (!Keys(JobKeys(action.Keys), Check)) throw new InvalidOperationException("That key has no Mac mapping.");
                }
                else if (action.Action == "scroll") Scroll(action.Amount, Check);
                else if (action.Action == "drag")
                {
                    var end = frame.ToLogical(action.EndX, action.EndY);
                    if (!frame.Source.Target.Area.Contains(end.X, end.Y)) throw new InvalidOperationException("The drag ends outside the bound target window.");
                    Drag(x, y, end.X, end.Y, action.Duration, (px, py) => { x = px; y = py; Check(); });
                    x = end.X; y = end.Y;
                }
                else if (action.Action == "click" || action.Action == "double_click") Click(action.Button, action.Action == "double_click" ? 2 : 1, Check);
                else throw new InvalidOperationException("That Mac input action is unavailable.");
                token.ThrowIfCancellationRequested(); (completed ?? guard)(x, y);
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Callback(IntPtr proxy, uint kind, IntPtr ev, IntPtr context);
        [DllImport(Graphics)] private static extern IntPtr CGEventTapCreate(uint tap, uint place, uint options, ulong mask, Callback callback, IntPtr context);
        [DllImport(Graphics)] private static extern void CGEventTapEnable(IntPtr tap, [MarshalAs(UnmanagedType.I1)] bool enable);
        [DllImport(Core)] private static extern IntPtr CFMachPortCreateRunLoopSource(IntPtr allocator, IntPtr port, nint order);
        [DllImport(Core)] private static extern IntPtr CFRunLoopGetCurrent();
        [DllImport(Core)] private static extern void CFRunLoopAddSource(IntPtr loop, IntPtr source, IntPtr mode);
        [DllImport(Core)] private static extern int CFRunLoopRunInMode(IntPtr mode, double seconds, [MarshalAs(UnmanagedType.I1)] bool returnAfterSourceHandled);
        [DllImport(Core)] private static extern void CFMachPortInvalidate(IntPtr port);

        internal static bool Stops(uint kind, long source) => kind == 0xFFFFFFFE || kind == 0xFFFFFFFF || source != Environment.ProcessId;

        private static string ListenProblem()
        {
            if (CGPreflightListenEventAccess()) return "Couldn't listen for Esc, so no screen job started.";
            if (Interlocked.Exchange(ref inputPane, 1) == 0)
            {
                try { Launch("/usr/bin/open", new[] { "x-apple.systempreferences:com.apple.preference.security?Privacy_ListenEvent" }); }
                catch (Exception error) { Brain.Log("escape", -1, error.Message); }
            }
            return "Allow Capri under System Settings > Privacy & Security > Input Monitoring, then ask again.";
        }

        internal sealed class Escape : IDisposable
        {
            private readonly Thread thread;
            private readonly Action stop;
            private readonly CancellationToken token;
            private readonly ManualResetEventSlim ready = new ManualResetEventSlim();
            private int disposed, armed;
            internal bool Armed => Volatile.Read(ref armed) != 0;
            internal Escape(Action stop, CancellationToken token)
            {
                this.stop = stop; this.token = token;
                thread = new Thread(Listen) { IsBackground = true, Name = "Capri Esc" }; thread.Start();
                if (!ready.Wait(3000) || !Armed)
                {
                    Dispose();
                    throw new InvalidOperationException(ListenProblem());
                }
                Interlocked.Exchange(ref inputPane, 0);
            }
            private void Listen()
            {
                IntPtr tap = IntPtr.Zero, source = IntPtr.Zero, mode = IntPtr.Zero;
                Callback callback = (proxy, kind, ev, context) =>
                {
                    try
                    {
                        if (kind == 0xFFFFFFFE || kind == 0xFFFFFFFF) { Volatile.Write(ref armed, 0); stop(); }
                        else if (Stops(kind, CGEventGetIntegerValueField(ev, 42))) stop();
                    }
                    catch (Exception) { Volatile.Write(ref armed, 0); }
                    return ev;
                };
                try
                {
                    tap = CGEventTapCreate(1, 0, 1, (1UL << 1) | (1UL << 3) | (1UL << 5) | (1UL << 10) | (1UL << 25), callback, IntPtr.Zero);
                    if (tap == IntPtr.Zero) return;
                    source = CFMachPortCreateRunLoopSource(IntPtr.Zero, tap, 0);
                    if (source == IntPtr.Zero) return;
                    string name = "kCFRunLoopDefaultMode";
                    mode = CFStringCreateWithCharacters(IntPtr.Zero, name.ToCharArray(), name.Length);
                    CFRunLoopAddSource(CFRunLoopGetCurrent(), source, mode); CGEventTapEnable(tap, true);
                    Volatile.Write(ref armed, 1); ready.Set();
                    while (Volatile.Read(ref disposed) == 0 && !token.IsCancellationRequested && Armed)
                        CFRunLoopRunInMode(mode, 0.05, true);
                }
                catch (Exception error) { Brain.Log("escape", -1, error.Message); if (Armed) stop(); }
                finally
                {
                    Volatile.Write(ref armed, 0); ready.Set();
                    if (tap != IntPtr.Zero) CFMachPortInvalidate(tap);
                    if (source != IntPtr.Zero) CFRelease(source);
                    if (tap != IntPtr.Zero) CFRelease(tap);
                    if (mode != IntPtr.Zero) CFRelease(mode);
                    GC.KeepAlive(callback);
                }
            }
            public void Dispose() { Interlocked.Exchange(ref disposed, 1); if (Thread.CurrentThread != thread) thread.Join(1000); }
        }

        private sealed class CheckDesktop : IComputerDesktop
        {
            internal ComputerSnapshot Source;
            internal Action<int, int> Moving;
            public ComputerSnapshot Snapshot(string output = null, bool focus = false) => Source;
            public ComputerFrame Capture(ComputerSnapshot source, bool crop, CancellationToken token) { throw new Exception("Mac fake captured a real screen"); }
            public void Move(int x, int y, CancellationToken token) { token.ThrowIfCancellationRequested(); Moving(x, y); }
        }

        internal static void Checks(List<string> checks)
        {
            var trusted = Trusted; var launch = Launch; var sink = Sink; var pause = Pause; var position = Position; var displays = Displays;
            var handsReady = Hands.HandsReady; var handsProblem = Hands.HandsProblem;
            bool macWas = Hands.OnMac, ownWas = Hands.OwnHands, clipboardWas = Hands.MacClipboard;
            bool wasOpened = opened;
            try
            {
                List<Event> events = new List<Event>(); int panes = 0;
                Trusted = () => true; Sink = events.Add; Pause = _ => { }; Position = () => new Point(30, 40);
                Displays = () => new[] { new Display(1, 1, new ComputerRect(0, 0, 1440, 900), 2880, 1800), new Display(2, 2, new ComputerRect(-1920, -200, 1920, 1080), 1920, 1080) };
                Click("left", 1);
                if (events.Count != 2 || events[0].Kind != 1 || events[1].Kind != 2 || events.Any(e => e.Count != 1)) throw new Exception("Mac click sequence");
                events.Clear(); Click("left", 2);
                if (!events.Select(e => e.Kind).SequenceEqual(new uint[] { 1, 2, 1, 2 }) || !events.Select(e => e.Count).SequenceEqual(new[] { 1, 1, 2, 2 })) throw new Exception("Mac double click sequence");
                events.Clear(); Click("right", 1);
                if (!events.Select(e => e.Kind).SequenceEqual(new uint[] { 3, 4 }) || events.Any(e => e.Button != 1)) throw new Exception("Mac right click sequence");
                events.Clear(); Drag(-100, 20, 100, 80);
                if (events.Count != 23 || events[0].Kind != 5 || events[1].Kind != 1 || events.Skip(2).Take(20).Any(e => e.Kind != 6) || events[^1].Kind != 2 || events[^1].At.X != 100) throw new Exception("Mac drag sequence");
                events.Clear(); Scroll(2); Scroll(-1);
                if (!events.Select(e => e.Wheel).SequenceEqual(new[] { -1, -1, 1 }) || events.Any(e => e.Kind != 22)) throw new Exception("Mac scroll sequence");
                events.Clear(); Type("héllo 👋");
                if (string.Concat(events.Where(e => e.Kind == 10).Select(e => e.Text)) != "héllo 👋" || events.Count != 14 || events[^1].Text.Length != 2) throw new Exception("Mac Unicode sequence");
                events.Clear(); Keys("cmd+c");
                if (!events.Select(e => e.Key).SequenceEqual(new ushort[] { 55, 8, 8, 55 }) || events[1].Flags != 1UL << 20 || events[^1].Flags != 0) throw new Exception("Mac command sequence");
                events.Clear(); int called = 0;
                try { Keys("cmd+c", () => { if (++called == 2) throw new OperationCanceledException(); }); } catch (OperationCanceledException) { }
                if (events.Count != 2 || events[^1].Kind != 11 || events[^1].Flags != 0) throw new Exception("Mac cancelled chord release");
                foreach (int scale in new[] { 1, 2 })
                {
                    ComputerFrame scaled = new ComputerFrame { Area = new ComputerRect(-1920, -200, 1920, 1080), Width = 1920 * scale, Height = 1080 * scale };
                    var at = scaled.ToLogical(960 * scale, 540 * scale);
                    if (at.X != -960 || at.Y != 340) throw new Exception("Mac screenshot point scaling");
                }
                if (Fraction(0, 0).X != -1920 || Fraction(0, 0).Y != -200) throw new Exception("Mac desktop origin");
                if (Stops(10, Environment.ProcessId) || !Stops(10, 0) || !Stops(0xFFFFFFFE, Environment.ProcessId)) throw new Exception("Mac Esc and owner input stop");
                Launch = (tool, args) => { if (tool != "/usr/bin/open" || args.Length != 1 || args[0] != Pane) throw new Exception("Mac accessibility pane"); panes++; };
                Trusted = () => false; opened = false;
                if (Problem() != Permission || Problem() != Permission || panes != 1) throw new Exception("Mac accessibility line");
                Trusted = () => true;
                if (Problem() != null) throw new Exception("Mac permission refresh");
                foreach (string chord in Shortcuts.Values)
                    if (chord.Split('+').Any(part => Code(part) < 0)) throw new Exception("Mac shortcut key map");
                string[] names = { "discord_mute", "discord_deafen", "show_desktop", "emoji", "narrator", "magnifier", "record", "quick_settings", "voice_typing" };
                string[] chords = { "cmd+shift+m", "cmd+shift+d", "fn+f11", "ctrl+cmd+space", "cmd+f5", "option+cmd+equals", "cmd+shift+5", "fn+c", "fn+d" };
                for (int i = 0; i < names.Length; i++)
                {
                    if (Shortcuts[names[i]] != chords[i]) throw new Exception("Mac shortcut mapping " + names[i]);
                    events.Clear(); Keys(Shortcuts[names[i]]);
                    if (events.Count != chords[i].Split('+').Length * 2 || events[^1].Flags != 0) throw new Exception("Mac shortcut release " + names[i]);
                }
                if (ChordKeys(new byte[] { 0x11, 0x43 }) != "cmd+c" || ChordKeys(new byte[] { 0x11, 0x56 }) != "cmd+v" || ChordKeys(new byte[] { 0x12, 0x09 }) != "cmd+tab"
                    || ChordKeys(new byte[] { 0x12, 0x73 }) != "cmd+w" || ChordKeys(new byte[] { 0x5B, 0x4C }) != "ctrl+cmd+q" || ChordKeys(new byte[] { 0x5B, 0x44 }) != "fn+f11") throw new Exception("Mac Windows chord equivalents");
                events.Clear(); called = 0;
                try { Drag(0, 0, 100, 100, guard: (x, y) => { if (++called == 3) throw new OperationCanceledException(); }); } catch (OperationCanceledException) { }
                if (events[^1].Kind != 2 || events.Count(e => e.Kind == 1) != 1) throw new Exception("Mac cancelled drag release");
                Hands.OnMac = true; Hands.HandsReady = () => Trusted(); Hands.HandsProblem = Problem; Hands.MacClipboard = true;
                Trusted = () => false; opened = true;
                if (!Hands.KitFor().Missing.ContainsKey("use_computer") || Hands.Unable("use_computer", new ToolArguments()) != Permission) throw new Exception("Mac Accessibility gap");
                Trusted = () => true;
                Hands.Kit mac = Hands.KitFor();
                if (!Hands.OwnHands || mac.Missing.ContainsKey("use_computer") || !mac.Missing.ContainsKey("stream")) throw new Exception("Mac available hands gaps");
                if (!mac.Keys.OrderBy(value => value).SequenceEqual(names.Concat(new[] { "screenshot", "task_manager", "clipboard" }).OrderBy(value => value))) throw new Exception("Mac shortcut availability");
                if (!ClipboardKeys.SequenceEqual(new[] { "cmd+space", "cmd+4" })) throw new Exception("Mac clipboard keys");
                Hands.MacClipboard = false;
                if (Hands.Unable("shortcut", new ToolArguments { Name = "clipboard" }) != "Clipboard history needs macOS Tahoe 26 or later.") throw new Exception("Mac clipboard version gap");
                Hands.OnMac = false; Hands.HandsReady = null; Hands.HandsProblem = null; Hands.OwnHands = false;
                Hands.Kit linux = Hands.KitFor();
                if (!linux.Keys.OrderBy(value => value).SequenceEqual(new[] { "screenshot", "show_desktop", "task_manager" }.OrderBy(value => value)) || !linux.Missing.ContainsKey("use_computer") || !linux.Missing.ContainsKey("stream")) throw new Exception("Linux hands gaps changed");
                Hands.OwnHands = true;
                if (Hands.KitFor().Missing.ContainsKey("use_computer") || Hands.KitFor().Missing.ContainsKey("stream")) throw new Exception("Linux available hands changed");
                foreach (string keys in ComputerAction.AllowedKeys)
                    if (JobKeys(keys).Split('+').Any(part => Code(part) < 0)) throw new Exception("Mac job key map");
                Point cursor = new Point(-900, 120);
                Position = () => cursor;
                Sink = ev => { events.Add(ev); if (ev.Kind == 5 || ev.Kind == 6) cursor = ev.At; };
                ComputerRect room = new ComputerRect(-960, 100, 400, 200);
                CheckDesktop desktop = new CheckDesktop
                {
                    Source = new ComputerSnapshot("fake", "fake", new ComputerTarget("0x1", 1, "fake", "fake", room, 1, 0), new ComputerOutput("1", room, 2, 0, 800, 400), cursor.X, cursor.Y, true),
                    Moving = (x, y) => Move(x, y)
                };
                ComputerFrame frame = new ComputerFrame { Source = desktop.Source, Area = room, Width = 800, Height = 400 };
                int guards = 0, completed = 0;
                Action<double, double> guard = (x, y) => { guards++; if (x != cursor.X || y != cursor.Y) throw new Exception("Mac input guard lost the pointer"); };
                events.Clear();
                new Input().Execute(new ComputerAction { Action = "click", X = 200, Y = 100, Button = "left" }, frame, desktop, guard, CancellationToken.None, (x, y) => { guard(x, y); completed++; });
                if (cursor.X != -860 || cursor.Y != 150 || events.Count != 3 || guards < 3 || completed != 1) throw new Exception("Mac computer input seam");
                desktop.Source = desktop.Source with { PointerX = cursor.X, PointerY = cursor.Y }; frame.Source = desktop.Source;
                events.Clear();
                new Input().Execute(new ComputerAction { Action = "press", Keys = "CTRL+C" }, frame, desktop, guard, CancellationToken.None);
                if (!events.Select(ev => ev.Key).SequenceEqual(new ushort[] { 55, 8, 8, 55 })) throw new Exception("Mac computer job command key");
                events.Clear();
                try { new Input().Execute(new ComputerAction { Action = "click", X = 800, Y = 0, Button = "left" }, frame, desktop, guard, CancellationToken.None); throw new Exception("Mac accepted an out-of-frame point"); }
                catch (InvalidOperationException) { }
                if (events.Count != 0) throw new Exception("Mac moved before rejecting an out-of-frame point");
                Trusted = () => false;
                try { new Input().Execute(new ComputerAction { Action = "press", Keys = "CTRL+C" }, frame, desktop, guard, CancellationToken.None); throw new Exception("Mac ignored revoked Accessibility"); }
                catch (InvalidOperationException error) { if (error.Message != Permission) throw; }
                if (events.Count != 0) throw new Exception("Mac posted input without Accessibility");
                Trusted = () => true;
                var start = CaptureCommand(Displays()[1], "shot.png");
                if (start.FileName != "/usr/sbin/screencapture" || !start.ArgumentList.SequenceEqual(new[] { "-x", "-D", "2", "-t", "png", "shot.png" })) throw new Exception("Mac capture command");
                checks.Add("PASS Mac input events, Unicode, command keys, cancellation release, display scaling and Accessibility refresh");
                checks.Add("PASS Mac job input guards, shortcut maps, Esc filter and platform capability gaps through fakes");
            }
            finally
            {
                Trusted = trusted; Launch = launch; Sink = sink; Pause = pause; Position = position; Displays = displays; opened = wasOpened;
                Hands.HandsReady = handsReady; Hands.HandsProblem = handsProblem; Hands.OnMac = macWas; Hands.OwnHands = ownWas;
                Hands.MacClipboard = clipboardWas;
            }
        }
    }
}

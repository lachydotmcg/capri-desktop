using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;

namespace CapriDesktop
{
    // Her mouse and keyboard on Linux: two virtual devices made through /dev/uinput, so they work the same on X11, KDE, GNOME
    // and Hyprland. They drive the one cursor the desktop has, since Wayland gives each seat only one. Writing to /dev/uinput
    // needs a udev rule they add once; without it Problem says how.
    internal static class Fingers
    {
        internal const string Device = "/dev/uinput";
        internal const string Rule = "KERNEL==\"uinput\", SUBSYSTEM==\"misc\", TAG+=\"uaccess\", OPTIONS+=\"static_node=uinput\"";
        internal const string Setup = "echo '" + Rule + "' | sudo tee /etc/udev/rules.d/60-capri-uinput.rules && sudo udevadm control --reload && sudo udevadm trigger --name-match=uinput";
        private const int Span = 65535;
        private const ushort Syn = 0, Key = 1, Rel = 2, Abs = 3;
        private const ushort Left = 0x110, Right = 0x111, Middle = 0x112;
        private const ushort Wheel = 8, Sideways = 6;
        private const uint SetEv = 0x40045564, SetKey = 0x40045565, SetRel = 0x40045566, SetAbs = 0x40045567, Create = 0x5501;
        private static readonly object gate = new object();
        private static int pointer = -1, keyboard = -1;
        // Where she last put the cursor, 0 to 1 across the whole desktop, so a job can tell when they've grabbed the mouse.
        internal static double LastX = -1, LastY = -1;

        [DllImport("libc", SetLastError = true)] private static extern int open(string path, int flags);
        [DllImport("libc", SetLastError = true)] private static extern int close(int fd);
        [DllImport("libc", SetLastError = true)] private static extern int access(string path, int mode);
        [DllImport("libc", SetLastError = true)] private static extern int ioctl(int fd, uint request, int value);
        [DllImport("libc", SetLastError = true)] private static extern int ioctl(int fd, uint request);
        [DllImport("libc", SetLastError = true)] private static extern IntPtr write(int fd, byte[] data, IntPtr count);

        // Null when she can use them; otherwise what to do about it, in her words.
        internal static string Problem
        {
            get
            {
                if (!OperatingSystem.IsLinux()) return "Using the mouse and keyboard isn't ready on this PC yet.";
                if (!File.Exists(Device)) return "Using the mouse and keyboard isn't ready on this PC yet: there's no " + Device + ". Run sudo modprobe uinput, then ask again.";
                if (access(Device, 2) != 0) return "Using the mouse and keyboard isn't ready on this PC yet: Capri can't write to " + Device + ". Add the udev rule from Settings > Security > Mouse and keyboard setup, then ask again.";
                return null;
            }
        }

        private static void Ready()
        {
            lock (gate)
            {
                if (pointer >= 0 && keyboard >= 0) return;
                string problem = Problem;
                if (problem != null) throw new InvalidOperationException(problem);
                if (pointer < 0) pointer = Make("Capri mouse", delegate(int fd)
                {
                    ioctl(fd, SetEv, Key);
                    foreach (ushort button in new[] { Left, Right, Middle }) ioctl(fd, SetKey, button);
                    ioctl(fd, SetEv, Rel);
                    ioctl(fd, SetRel, Wheel);
                    ioctl(fd, SetRel, Sideways);
                    ioctl(fd, SetEv, Abs);
                    ioctl(fd, SetAbs, 0);
                    ioctl(fd, SetAbs, 1);
                }, true);
                if (keyboard < 0) keyboard = Make("Capri keyboard", delegate(int fd)
                {
                    ioctl(fd, SetEv, Key);
                    for (int code = 1; code < 249; code++) ioctl(fd, SetKey, code);
                }, false);
                // The desktop takes a moment to pick up new devices before it listens to them.
                Thread.Sleep(600);
            }
        }

        // The legacy uinput_user_dev setup: a name, an id, then four arrays of 64 axis limits. Only X and Y have a range.
        private static int Make(string name, Action<int> features, bool absolute)
        {
            int fd = open(Device, 0x801);
            if (fd < 0) throw new InvalidOperationException("Capri couldn't open " + Device + " (error " + Marshal.GetLastWin32Error() + ").");
            features(fd);
            byte[] setup = new byte[80 + 8 + 4 + 64 * 4 * 4];
            byte[] named = System.Text.Encoding.ASCII.GetBytes(name);
            Array.Copy(named, setup, Math.Min(79, named.Length));
            BitConverter.GetBytes((ushort)0x06).CopyTo(setup, 80);
            BitConverter.GetBytes((ushort)0xCA9).CopyTo(setup, 82);
            BitConverter.GetBytes((ushort)(absolute ? 1 : 2)).CopyTo(setup, 84);
            BitConverter.GetBytes((ushort)1).CopyTo(setup, 86);
            if (absolute)
            {
                BitConverter.GetBytes(Span).CopyTo(setup, 92);
                BitConverter.GetBytes(Span).CopyTo(setup, 96);
            }
            if (write(fd, setup, new IntPtr(setup.Length)).ToInt64() != setup.Length || ioctl(fd, Create) < 0)
            {
                close(fd);
                throw new InvalidOperationException("Capri couldn't make her " + name.Substring(6) + " (error " + Marshal.GetLastWin32Error() + ").");
            }
            return fd;
        }

        private static void Send(int fd, params int[] events)
        {
            byte[] packed = new byte[events.Length / 3 * 24];
            for (int i = 0; i < events.Length / 3; i++)
            {
                BitConverter.GetBytes((ushort)events[i * 3]).CopyTo(packed, i * 24 + 16);
                BitConverter.GetBytes((ushort)events[i * 3 + 1]).CopyTo(packed, i * 24 + 18);
                BitConverter.GetBytes(events[i * 3 + 2]).CopyTo(packed, i * 24 + 20);
            }
            write(fd, packed, new IntPtr(packed.Length));
        }

        // The cursor to x and y, each 0 to 1 across the whole desktop, which is how screenshots of it are laid out too.
        internal static void MoveTo(double x, double y)
        {
            Ready();
            x = Math.Max(0, Math.Min(1, x));
            y = Math.Max(0, Math.Min(1, y));
            Send(pointer, Abs, 0, (int)Math.Round(x * Span), Abs, 1, (int)Math.Round(y * Span), Syn, 0, 0);
            LastX = x;
            LastY = y;
            Thread.Sleep(40);
        }

        internal static void Click(string button, int times)
        {
            Ready();
            ushort code = button == "right" ? Right : button == "middle" ? Middle : Left;
            for (int i = 0; i < Math.Max(1, times); i++)
            {
                Send(pointer, Key, code, 1, Syn, 0, 0);
                Thread.Sleep(30);
                Send(pointer, Key, code, 0, Syn, 0, 0);
                Thread.Sleep(70);
            }
        }

        internal static void Drag(double fromX, double fromY, double toX, double toY)
        {
            MoveTo(fromX, fromY);
            Send(pointer, Key, Left, 1, Syn, 0, 0);
            for (int step = 1; step <= 12; step++)
            {
                Thread.Sleep(20);
                MoveTo(fromX + (toX - fromX) * step / 12, fromY + (toY - fromY) * step / 12);
            }
            Send(pointer, Key, Left, 0, Syn, 0, 0);
        }

        // Notches of the wheel: positive scrolls down, like the wheel turned toward them.
        internal static void Scroll(int notches)
        {
            Ready();
            for (int i = 0; i < Math.Abs(notches); i++)
            {
                Send(pointer, Rel, Wheel, notches > 0 ? -1 : 1, Syn, 0, 0);
                Thread.Sleep(30);
            }
        }

        private static readonly Dictionary<string, int> Named = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "esc", 1 }, { "escape", 1 }, { "backspace", 14 }, { "tab", 15 }, { "enter", 28 }, { "return", 28 }, { "ctrl", 29 }, { "control", 29 },
            { "shift", 42 }, { "alt", 56 }, { "space", 57 }, { "capslock", 58 }, { "super", 125 }, { "meta", 125 }, { "win", 125 }, { "cmd", 125 },
            { "altgr", 100 }, { "home", 102 }, { "up", 103 }, { "pageup", 104 }, { "left", 105 }, { "right", 106 }, { "end", 107 }, { "down", 108 },
            { "pagedown", 109 }, { "insert", 110 }, { "delete", 111 }, { "del", 111 }, { "print", 99 }, { "menu", 127 },
            { "minus", 12 }, { "-", 12 }, { "comma", 51 }, { ",", 51 }, { "period", 52 }, { ".", 52 }, { "slash", 53 }, { "/", 53 }
        };

        private const string Letters = "qwertyuiop    asdfghjkl     zxcvbnm";

        // The evdev code for a key name like ctrl, enter, f5, a or 7.
        internal static int Code(string name)
        {
            int code;
            if (Named.TryGetValue(name, out code)) return code;
            Match f = Regex.Match(name, @"^f([1-9]|1[0-2])$", RegexOptions.IgnoreCase);
            if (f.Success) { int n = int.Parse(f.Groups[1].Value); return n <= 10 ? 58 + n : 76 + n; }
            if (name.Length == 1)
            {
                char c = char.ToLowerInvariant(name[0]);
                if (c >= '1' && c <= '9') return 2 + c - '1';
                if (c == '0') return 11;
                int at = Letters.IndexOf(c);
                if (at >= 0 && c != ' ') return 16 + at;
            }
            return -1;
        }

        // A key or chord like enter, ctrl+l or ctrl+shift+t: all pressed in order, then let go backwards.
        internal static bool Keys(string chord)
        {
            List<int> codes = new List<int>();
            foreach (string part in (chord ?? "").ToLowerInvariant().Replace(" ", "").Split('+'))
            {
                if (part.Length == 0) continue;
                int code = Code(part);
                if (code < 0) return false;
                codes.Add(code);
            }
            if (codes.Count == 0) return false;
            Ready();
            foreach (int code in codes) { Send(keyboard, Key, code, 1, Syn, 0, 0); Thread.Sleep(15); }
            for (int i = codes.Count - 1; i >= 0; i--) { Send(keyboard, Key, codes[i], 0, Syn, 0, 0); Thread.Sleep(15); }
            Thread.Sleep(60);
            return true;
        }

        // Words typed key by key when they're plain letters, digits and spaces, which sit in the same place on nearly every
        // layout; anything else goes through the clipboard and ctrl+v, so accents and symbols come out right on any layout.
        internal static Func<string, bool> Copy = WlCopy;

        internal static bool Type(string text)
        {
            text = text ?? "";
            if (text.Length == 0) return true;
            if (Regex.IsMatch(text, @"^[A-Za-z0-9 ]{1,200}$"))
            {
                Ready();
                foreach (char c in text)
                {
                    int code = c == ' ' ? 57 : Code(c.ToString());
                    bool upper = char.IsUpper(c);
                    if (upper) Send(keyboard, Key, 42, 1, Syn, 0, 0);
                    Send(keyboard, Key, code, 1, Syn, 0, 0, Key, code, 0, Syn, 0, 0);
                    if (upper) Send(keyboard, Key, 42, 0, Syn, 0, 0);
                    Thread.Sleep(12);
                }
                return true;
            }
            if (!Copy(text)) return false;
            Thread.Sleep(120);
            return Keys("ctrl+v");
        }

        private static bool WlCopy(string text)
        {
            string tool = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") != null ? "wl-copy" : "xclip";
            try
            {
                ProcessStartInfo start = new ProcessStartInfo(tool) { UseShellExecute = false, RedirectStandardInput = true, CreateNoWindow = true };
                if (tool == "xclip") { start.ArgumentList.Add("-selection"); start.ArgumentList.Add("clipboard"); }
                using (Process copying = Process.Start(start))
                {
                    if (copying == null) return false;
                    copying.StandardInput.Write(text);
                    copying.StandardInput.Close();
                    copying.WaitForExit(3000);
                    return copying.HasExited && copying.ExitCode == 0;
                }
            }
            catch (Exception) { return false; }
        }
    }
}

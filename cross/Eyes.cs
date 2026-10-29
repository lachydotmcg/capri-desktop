using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace CapriDesktop
{
    // "Check my screen" on Mac and Linux: a screenshot from the system's own tool, shrunk to a JPEG her brain can look at.
    internal static class Eyes
    {
        // A whole-screen picture's file bytes, or null when no tool here could take one. The smoke test hands in its own.
        internal static Func<byte[]> Shoot = delegate { return Screenshot(Tools(OperatingSystem.IsMacOS(), Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"))); };
        // The same for Watch my screen, with only the tools that don't flash, play a shutter sound or ask first.
        internal static Func<byte[]> Peek = delegate { return Recordable() ? Screenshot(QuietTools(OperatingSystem.IsMacOS(), Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"))) : null; };

        internal static string Unwatchable
        {
            get
            {
                return OperatingSystem.IsMacOS()
                    ? "I can't watch your screen yet. Allow Capri under System Settings > Privacy & Security > Screen Recording, then open me again."
                    : "I can't watch your screen quietly here: this desktop's screenshots flash or ask first. I'll still look whenever you ask me to check your screen. On X11, installing scrot or maim fixes it.";
            }
        }

        private const string Graphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        [DllImport(Graphics)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CGPreflightScreenCaptureAccess();
        [DllImport(Graphics)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CGRequestScreenCaptureAccess();
        private static bool askedToRecord;

        // Without Screen Recording a Mac screenshot is just the wallpaper, so she'd skip every glance without saying why. The first
        // time, Mac asks them; until they allow it and open her again, she says once that she can't watch.
        private static bool Recordable()
        {
            if (!OperatingSystem.IsMacOS()) return true;
            try
            {
                if (CGPreflightScreenCaptureAccess()) return true;
                if (askedToRecord) return false;
                askedToRecord = true;
                return CGRequestScreenCaptureAccess();
            }
            catch (Exception) { return true; }
        }

        internal static string Missing
        {
            get
            {
                return OperatingSystem.IsMacOS()
                    ? "You couldn't take a screenshot. Tell them to allow Capri under System Settings > Privacy & Security > Screen Recording, then open you again."
                    : "You couldn't take a screenshot. Tell them to install grim (Wayland), scrot (X11) or gnome-screenshot (GNOME), then ask again.";
            }
        }

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
                if (where.Contains("kde")) tools.Add(spectacle);
                if (where.Contains("gnome")) tools.Add(gnome);
                tools.Add(new[] { "grim", "{0}" });
                if (!where.Contains("kde")) tools.Add(spectacle);
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
            bool kde = (desktop ?? "").ToLowerInvariant().Contains("kde");
            foreach (string[] tool in Tools(mac, session, desktop))
            {
                if (tool[0] == "gnome-screenshot" || tool[0] == "xfce4-screenshooter") continue;
                if (tool[0] == "spectacle" && wayland && !kde) continue;
                quiet.Add(tool);
            }
            return quiet;
        }

        private static byte[] Screenshot(List<string[]> tools)
        {
            bool mac = OperatingSystem.IsMacOS();
            foreach (string[] tool in tools)
            {
                string path = Path.Combine(Path.GetTempPath(), "capri-screen-" + Guid.NewGuid().ToString("N") + (mac ? ".jpg" : ".png"));
                try
                {
                    if (!Take(tool, path)) continue;
                    byte[] shot = File.ReadAllBytes(path);
                    if (shot.Length > 0) return shot;
                }
                catch (Exception) { }
                finally
                {
                    try { File.Delete(path); }
                    catch (Exception) { }
                }
            }
            return null;
        }

        // A tool that isn't installed throws when it starts, so the next one gets a turn.
        private static bool Take(string[] tool, string path)
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

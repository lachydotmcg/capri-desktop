using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Threading;

namespace CapriDesktop
{
    // labwc keeps an XWayland window's own "keep above" request only when the user's rc.xml allows it, so her overlays
    // go over other windows the way it keeps menus there: as unmanaged override-redirect windows, with no config change.
    internal static class Labwc
    {
        internal static readonly bool Current = OperatingSystem.IsLinux() && Detect(
            Environment.GetEnvironmentVariable("LABWC_PID"),
            Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"),
            Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"),
            Environment.GetEnvironmentVariable("DISPLAY"),
            Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE"),
            Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"),
            ProcessName, HoldsDisplayLock);

        // Started from a shell, a service or ssh, she may not inherit labwc's variables, so the process that holds the
        // Wayland display's lock file, which is always the compositor, settles it. A desktop that names itself isn't scanned.
        internal static bool Detect(string labwcPid, string currentDesktop, string waylandDisplay, string x11Display, string hyprland, string runtime, Func<int, string> processName, Func<string, bool> labwcHoldsLock)
        {
            if (string.IsNullOrEmpty(waylandDisplay) || string.IsNullOrEmpty(x11Display) || !string.IsNullOrEmpty(hyprland)) return false;
            if (int.TryParse(labwcPid, out int pid) && pid > 0 && processName(pid) == "labwc") return true;
            foreach (string desktop in (currentDesktop ?? "").Split(':'))
                if (desktop.Equals("labwc", StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(currentDesktop)) return false;
            string socket = waylandDisplay.StartsWith("/") ? waylandDisplay : string.IsNullOrEmpty(runtime) ? null : Path.Combine(runtime, waylandDisplay);
            return socket != null && labwcHoldsLock(socket + ".lock");
        }

        internal static void Checks(List<string> checks)
        {
            Func<int, string> names = pid => pid == 42 ? "labwc" : pid == 7 ? "sway" : null;
            Func<string, bool> noLock = path => false, labwcLock = path => path == "/run/user/1000/wayland-1.lock" || path == "/tmp/labwc-socket.lock";
            if (!Detect("42", null, "wayland-0", ":0", null, "/run/user/1000", names, noLock) || !Detect(null, "labwc:wlroots", "wayland-0", ":0", null, "/run/user/1000", names, noLock)
                || !Detect(null, null, "/tmp/labwc-socket", ":0", null, null, names, labwcLock) || !Detect(null, "", "wayland-1", ":0", null, "/run/user/1000", names, labwcLock))
                throw new Exception("labwc detection missed a labwc session.");
            if (Detect("7", "sway", "wayland-0", ":0", null, "/run/user/1000", names, noLock) || Detect("42", "labwc", null, ":0", null, "/run/user/1000", names, labwcLock) || Detect("42", "labwc", "wayland-0", null, null, "/run/user/1000", names, labwcLock)
                || Detect("42", "labwc", "wayland-0", ":0", "hypr", "/run/user/1000", names, labwcLock) || Detect("99", "labwcish", "wayland-0", ":0", null, "/run/user/1000", names, noLock) || Detect(null, "GNOME", "wayland-1", ":0", null, "/run/user/1000", names, labwcLock)
                || Detect(null, null, "/tmp/sway-socket", ":0", null, null, names, labwcLock) || Detect(null, null, "wayland-1", ":0", null, null, names, labwcLock))
                throw new Exception("labwc detection took a session that isn't labwc with XWayland.");
            checks.Add("PASS labwc detection needs labwc's process, desktop name or Wayland lock with XWayland, and leaves Hyprland and other wlroots desktops alone");
        }

        private static bool HoldsDisplayLock(string lockFile)
        {
            try
            {
                foreach (string folder in Directory.EnumerateDirectories("/proc"))
                {
                    if (!int.TryParse(Path.GetFileName(folder), out int pid) || ProcessName(pid) != "labwc") continue;
                    try
                    {
                        foreach (string handle in Directory.EnumerateFileSystemEntries(Path.Combine(folder, "fd")))
                            if (new FileInfo(handle).LinkTarget == lockFile) return true;
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
            return false;
        }

        private static string ProcessName(int pid)
        {
            try { return File.ReadAllText("/proc/" + pid + "/comm").Trim(); }
            catch (Exception) { return null; }
        }
    }

    // An X window only becomes override-redirect when it maps, so one whose Always on top changes while shown maps again.
    // With Always on top off she stays an ordinary managed window that other windows can cover, as asked.
    internal static class LabwcOverlay
    {
        private static readonly ConditionalWeakTable<Window, StrongBox<bool>> unmanaged = new ConditionalWeakTable<Window, StrongBox<bool>>();
        private static bool failed;

        internal static void Register(Window window)
        {
            if (!unmanaged.TryAdd(window, new StrongBox<bool>(false))) return;
            window.PropertyChanged += (_, e) => { if (e.Property == Window.TopmostProperty) Dispatcher.UIThread.Post(() => Remap(window)); };
        }

        internal static bool Prepare(Window window)
        {
            if (!unmanaged.TryGetValue(window, out StrongBox<bool> state)) return false;
            if (failed) return true;
            try
            {
                NativeX11.Unmanaged(window, window.Topmost);
                state.Value = window.Topmost;
            }
            catch (Exception error)
            {
                failed = true;
                Brain.Log("labwc", -1, "Her windows can't stay above others on labwc: " + error.Message);
            }
            return true;
        }

        private static void Remap(Window window)
        {
            if (failed || !unmanaged.TryGetValue(window, out StrongBox<bool> state) || !window.IsVisible || state.Value == window.Topmost) return;
            // Hiding her lets go of a hold, so a raise that ends because they grabbed her waits until she's let go.
            if (window is Pet pet && pet.Held) { DispatcherTimer.RunOnce(() => Remap(window), TimeSpan.FromMilliseconds(250)); return; }
            window.Hide();
            window.Show();
        }
    }
}

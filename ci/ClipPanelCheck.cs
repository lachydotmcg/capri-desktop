using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal static class ClipPanelCheck
{
    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
    [STAThread]
    private static int Main(string[] args)
    {
        Directory.CreateDirectory(args[1]);
        try
        {
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { SetProcessDPIAware(); }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Assembly assembly = Assembly.LoadFrom(args[0]);
            Type type = assembly.GetType("CapriDesktop.Companion", true);
            using (EventWaitHandle restore = new EventWaitHandle(false, EventResetMode.AutoReset))
            using (EventWaitHandle shown = new EventWaitHandle(false, EventResetMode.ManualReset))
            using (Form app = (Form)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { args[1], restore, shown, true }, null))
            {
                Form panel = (Form)type.GetField("mainframe", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(app);
                List<string> checks = new List<string>();
                type.GetMethod("SmokeClipPanel", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(app, new object[] { checks, args[1] });
                File.WriteAllLines(Path.Combine(args[1], "panel-progress.txt"), checks);
                panel.Show();
                Application.DoEvents();
                type.GetMethod("SmokeClipPanel", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(app, new object[] { checks, args[1] });
                File.WriteAllLines(Path.Combine(args[1], "panel-progress.txt"), checks);
                type.GetMethod("SmokeBrainLayout", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(app, new object[] { checks });
                File.WriteAllLines(Path.Combine(args[1], "panel-result.txt"), checks);
                return 0;
            }
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(args[1], "panel-result.txt"), "FAIL " + (error.InnerException ?? error));
            return 1;
        }
    }
}

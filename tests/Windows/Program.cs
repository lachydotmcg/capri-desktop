using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CapriDesktop;
using CapriDesktop.Services;
internal static class Program
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int checks;
    private static T Field<T>(object instance, string name) { return (T)instance.GetType().GetField(name, Fields).GetValue(instance); }
    private static object Call(object instance, string name, params object[] args) { return instance.GetType().GetMethod(name, Fields).Invoke(instance, args); }
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    private static void PumpUntil(Func<bool> done)
    {
        var watch = Stopwatch.StartNew();
        while (!done()) { if (watch.Elapsed.TotalSeconds > 12) throw new TimeoutException("WinForms operation did not complete"); Application.DoEvents(); Thread.Sleep(10); }
    }
    [STAThread] private static int Main()
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        int result = 1;
        // Keep a real WinForms message loop alive while individual tested windows close.
        // DoEvents alone can uninstall the synchronization context between test cases.
        using (var host = new Form { ShowInTaskbar = false, WindowState = FormWindowState.Minimized, Text = "Capri regression host" })
        {
            host.Shown += delegate
            {
                host.BeginInvoke((Action)delegate { try { result = RunChecks(); } finally { host.Close(); } });
            };
            Application.Run(host);
        }
        return result;
    }
    private static int RunChecks()
    {
        string root = Path.Combine(Path.GetTempPath(), "capri-windows-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            AssetCatalog.Validate(typeof(Companion).Assembly); Check(true, "Embedded assets present");
            for (int i = 0; i < AssetCatalog.Sprites.Length; i++)
            {
                using (var image = Companion.LoadArt("Capri.Sprite." + AssetCatalog.Sprites[i] + ".png"))
                    Check(image != null && image.Width % AssetCatalog.Frames[i] == 0, "Sprite decoding");
            }
            var activities = new List<ChatActivity>();
            for (int cycle = 0; cycle < 12; cycle++)
            {
                using (var window = new ChatWindow(root, delegate(ChatActivity a, string t) { activities.Add(a); }, delegate(CharacterCommand c) { return "done"; }, delegate { return "idle"; }))
                {
                    window.Show(); Application.DoEvents();
                    Field<TextBox>(window, "input").Text = "Привет";
                    Task typing = (Task)Call(window, "Send");
                    if (cycle % 2 == 0)
                    {
                        window.Close(); window.RestoreChat();
                        PumpUntil(delegate { return typing.IsCompleted; }); typing.GetAwaiter().GetResult();
                        Check(!window.IsDisposed && Field<Button>(window, "send").Enabled, "Reopen during fade/cancellation");
                    }
                    else
                    {
                        PumpUntil(delegate { return typing.IsCompleted; }); typing.GetAwaiter().GetResult();
                        Check(Field<ConversationSession>(window, "session").Messages.Count == 2, "Typed response completes");
                    }
                    Field<TextBox>(window, "input").Text = "help";
                    typing = (Task)Call(window, "Send"); window.Shutdown();
                    PumpUntil(delegate { return typing.IsCompleted; }); typing.GetAwaiter().GetResult();
                    Check(window.IsDisposed, "Immediate shutdown cancels typing");
                }
            }
            Check(activities.Contains(ChatActivity.Talking), "Dialogue emits talking state");
            Check(!File.Exists(Path.Combine(root, "conversations.json")), "Default UI sessions do not persist");
            using (var restore = new EventWaitHandle(false, EventResetMode.AutoReset))
            using (var companion = new Companion(root, restore, true))
            {
                companion.Show(); Application.DoEvents();
                Call(companion, "ChatCommand", CharacterCommand.Sleep);
                Check(Field<bool>(companion, "manualRest"), "Sleep command integrated");
                Call(companion, "OpenTextChat"); Application.DoEvents();
                Check(Field<Clip>(companion, "clip").State == AnimationState.Sleep, "Opening chat preserves sleep");
                Call(companion, "ChatCommand", CharacterCommand.Wake);
                Check(!Field<bool>(companion, "manualRest"), "Wake command integrated");
                companion.ToggleHidden(); Application.DoEvents();
                Check(!Field<System.Windows.Forms.Timer>(companion, "frames").Enabled, "Hidden animation stops");
                companion.BringHere(); Application.DoEvents();
                Check(Screen.FromRectangle(companion.Bounds).WorkingArea.IntersectsWith(companion.Bounds), "Restore remains on a monitor");
                companion.ShowDiagnostics(); Application.DoEvents();
                companion.Close();
                Check(Field<ChatWindow>(companion, "textChat").IsDisposed, "Companion shutdown closes chat");
                Check(Field<DiagnosticsWindow>(companion, "diagnostics").IsDisposed, "Companion shutdown disposes diagnostics");
            }
            Console.WriteLine("PASS " + checks + " Windows checks: actual sprite decoding, rapid chat lifecycle, typing cancellation, animation conflicts, hide/restore and shutdown");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}

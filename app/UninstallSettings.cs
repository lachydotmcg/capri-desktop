using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using CapriSetup;

namespace CapriDesktop
{
    internal sealed partial class Companion
    {
        internal const int KindInstalled = 0, KindLoose = 1, KindStore = 2;
        internal const string AppsPage = "ms-settings:appsfeatures";
        internal static Action<string, Removal> Starter = CapriSetup.Program.Launch;

        internal static int GoodbyeKind(bool packaged, bool installed) { return packaged ? KindStore : installed ? KindInstalled : KindLoose; }

        internal int Goodbye { get { return GoodbyeKind(Packaged.Is, CapriSetup.Program.Installed(Application.ExecutablePath)); } }

        internal static string ForgetBlock(int kind, string home, string local)
        {
            string usual = Path.Combine(local, "Capri");
            bool shared = string.Equals(Path.GetFullPath(home ?? usual).TrimEnd('\\'), usual.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            if (kind == KindStore && !shared) return "Windows deletes everything she knows on this PC when it uninstalls the Store copy.";
            if (kind != KindInstalled && File.Exists(Path.Combine(usual, ".capri-install"))) return "Capri is also installed on this PC. To delete what she knows, uninstall her in Settings > Apps > Installed apps.";
            return null;
        }

        internal string ForgetNote { get { return ForgetBlock(Goodbye, herFolder, CapriSetup.Program.Local); } }

        internal static Removal Farewell(int kind, bool forget, string exe, int id, long started)
        {
            Removal plan = new Removal { Copy = kind != KindInstalled, Forget = forget, After = id, Started = started };
            if (kind == KindLoose) plan.App = exe;
            return kind == KindStore && !forget ? null : plan;
        }

        internal string Uninstall(bool forget)
        {
            int kind = Goodbye;
            Removal plan;
            using (Process me = Process.GetCurrentProcess()) plan = Farewell(kind, forget && ForgetNote == null, Application.ExecutablePath, me.Id, me.StartTime.ToUniversalTime().Ticks);
            if (plan != null)
            {
                try { Starter(Application.ExecutablePath, plan); }
                catch (IOException error) { return "Capri couldn't start her uninstaller. " + error.Message; }
                catch (UnauthorizedAccessException error) { return "Capri couldn't start her uninstaller. " + error.Message; }
                catch (System.ComponentModel.Win32Exception error) { return "Capri couldn't start her uninstaller. " + error.Message; }
            }
            if (kind == KindStore) Browse(AppsPage);
            if (plan != null) Quit();
            return null;
        }
    }

    internal sealed partial class Mainframe
    {
        private Key uninstallKey;

        private void BuildUninstall(int y)
        {
            Section("UNINSTALL >", 0, y);
            uninstallKey = Make("Uninstall Capri", 28, y + 24, 328, 22, delegate { ShowUninstall(); });
            uninstallKey.Accent = Hot;
            uninstallKey.AccessibleName = "Uninstall Capri: remove her from this PC, and choose whether everything she knows goes too";
            Label hint = Caption("Removes her from this PC. You choose whether\nher memories, chats and keys go too.", 28, y + 54, Look.Mono(8.5f, FontStyle.Regular, GraphicsUnit.Point), Faint);
            hint.AutoSize = false;
            hint.Bounds = new Rectangle(28, y + 54, 328, 36);
        }

        internal static string UninstallWords(int kind)
        {
            if (kind == Companion.KindStore) return "Windows removes the Store copy. Settings > Apps > Installed apps opens: find CapriAI, press the three dots, then Uninstall.";
            if (kind == Companion.KindLoose) return "She closes, then this copy of Capri.exe and her Start with Windows entry are deleted.";
            return "She closes, then her app, brain and voice downloads, shortcuts and Installed apps entry are removed.";
        }

        internal const string ForgetWords = "Her memories, notes, chats, keys, settings, mods, her computer and her downloads. Your clips and Obsidian vault stay.";

        internal void ShowUninstall()
        {
            int kind = capri.Goodbye;
            string blocked = capri.ForgetNote;
            using (Form dialog = new Form())
            {
                dialog.SuspendLayout();
                dialog.AutoScaleMode = AutoScaleMode.None;
                dialog.Text = "Uninstall Capri";
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MaximizeBox = false;
                dialog.MinimizeBox = false;
                dialog.ShowInTaskbar = false;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.BackColor = Neon.Sheet;
                dialog.ForeColor = Neon.SheetInk;
                dialog.Font = Look.Mono(9f, FontStyle.Regular, GraphicsUnit.Point);
                dialog.ClientSize = new Size(520, 204);
                Label say = Hint(UninstallWords(kind), 38, 34);
                CheckBox wipe = new CheckBox { Text = "Also delete everything she knows", Bounds = new Rectangle(16, 82, 488, 24), FlatStyle = FlatStyle.Flat, BackColor = Neon.Sheet, ForeColor = Neon.SheetInk };
                wipe.AccessibleName = "Also delete everything she knows: " + ForgetWords;
                Label wipeNote = Hint(blocked ?? ForgetWords, 110, 34);
                wipe.Enabled = blocked == null;
                wipe.Visible = blocked == null || kind != Companion.KindStore;
                Button go = DialogButton(kind == Companion.KindStore ? "Open Settings" : "Uninstall", 16, 158, 180, Hot);
                Button cancel = DialogButton("Cancel", 404, 158, 100, Neon.SheetDim);
                wipe.CheckedChanged += delegate { go.Text = kind == Companion.KindStore ? (wipe.Checked ? "Forget, then Settings" : "Open Settings") : wipe.Checked ? "Uninstall and forget" : "Uninstall"; };
                go.Click += delegate
                {
                    string problem = capri.Uninstall(wipe.Checked);
                    if (problem != null) { Notice.Tell(dialog, problem, "Capri couldn't uninstall", true); return; }
                    dialog.Close();
                };
                cancel.Click += delegate { dialog.Close(); };
                dialog.CancelButton = cancel;
                dialog.Controls.AddRange(new Control[] { Heading("UNINSTALL CAPRI >", 12), say, wipe, wipeNote, go, cancel });
                Fit(dialog);
                dialog.ShowDialog(this);
            }
        }

        internal string UninstallShown { get { return uninstallKey == null ? "missing" : uninstallKey.Text + " | " + (uninstallKey.Accent == Hot ? "hot" : "plain"); } }
    }
}

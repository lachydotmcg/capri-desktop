using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Pictures of a window as the screen would show it: Windows' own copy of the window, cut to the window's real outline and
    // laid over a light backdrop, with any open menu laid on top where it sits. A light backdrop is where a frame that
    // disagrees with its outline shows, as slivers at the corners.
    internal static class LookShots
    {
        [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
        [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr window, IntPtr region);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);

        internal static readonly Color Paper = Color.FromArgb(226, 228, 240);
        internal const int Margin = 28;

        // Windows' copy of one window, as it draws it, children and all. PW_RENDERFULLCONTENT first, then the plain copy if
        // the first came back empty.
        internal static Bitmap Copy(Control window)
        {
            Bitmap shot = new Bitmap(Math.Max(1, window.Width), Math.Max(1, window.Height), PixelFormat.Format32bppArgb);
            foreach (uint flags in new uint[] { 2, 0 })
            {
                bool done;
                using (Graphics graphics = Graphics.FromImage(shot))
                {
                    graphics.Clear(Color.Magenta);
                    IntPtr dc = graphics.GetHdc();
                    try { done = PrintWindow(window.Handle, dc, flags); }
                    finally { graphics.ReleaseHdc(dc); }
                }
                if (done && !Blank(shot)) return shot;
            }
            return shot;
        }

        private static bool Blank(Bitmap shot)
        {
            Color first = shot.GetPixel(shot.Width / 2, shot.Height / 2);
            for (int y = 0; y < shot.Height; y += Math.Max(1, shot.Height / 12))
                for (int x = 0; x < shot.Width; x += Math.Max(1, shot.Width / 12))
                    if (shot.GetPixel(x, y).ToArgb() != first.ToArgb()) return false;
            return true;
        }

        // The window's real outline, from Windows rather than WinForms, or its whole box when it has none.
        internal static Region Outline(Control window)
        {
            IntPtr handle = CreateRectRgn(0, 0, 0, 0);
            try
            {
                int kind = GetWindowRgn(window.Handle, handle);
                if (kind == 2 || kind == 3) return Region.FromHrgn(handle);
                return new Region(new Rectangle(0, 0, window.Width, window.Height));
            }
            finally { DeleteObject(handle); }
        }

        internal static void Lay(Graphics sheet, Control window, Point at)
        {
            using (Bitmap shot = Copy(window))
            using (Region shape = Outline(window))
            {
                shape.Translate(at.X, at.Y);
                sheet.SetClip(shape, CombineMode.Replace);
                sheet.DrawImageUnscaled(shot, at.X, at.Y);
                sheet.ResetClip();
            }
        }

        // The window and its open menus over the light backdrop, saved as a PNG.
        internal static void Save(Form window, string path, params ToolStripDropDown[] menus)
        {
            window.Refresh();
            Application.DoEvents();
            Rectangle area = window.Bounds;
            foreach (ToolStripDropDown menu in menus) if (menu != null && menu.Visible) area = Rectangle.Union(area, menu.Bounds);
            using (Bitmap sheet = new Bitmap(area.Width + Margin * 2, area.Height + Margin * 2, PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(sheet))
                {
                    graphics.Clear(Paper);
                    using (Pen line = new Pen(Color.FromArgb(206, 210, 228), 1))
                        for (int x = -sheet.Height; x < sheet.Width; x += 18) graphics.DrawLine(line, x, 0, x + sheet.Height, sheet.Height);
                    Lay(graphics, window, new Point(window.Left - area.Left + Margin, window.Top - area.Top + Margin));
                    foreach (ToolStripDropDown menu in menus)
                    {
                        if (menu == null || !menu.Visible) continue;
                        menu.Refresh();
                        Lay(graphics, menu, new Point(menu.Left - area.Left + Margin, menu.Top - area.Top + Margin));
                    }
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                sheet.Save(path, ImageFormat.Png);
            }
        }
    }


    internal sealed partial class Companion
    {
        // Pictures of the Workspace window in every state it has, with stand-in chats, at 100%, 125% and 150% and at its
        // smallest size, plus the Mainframe beside it. Each picture is also checked for crowding.
        private void SmokeWorkspaceLook(List<string> checks, string folder)
        {
            folder = Path.Combine(folder, Guid.NewGuid().ToString("N"));
            string pictures = Path.Combine(folder, "look");
            Directory.CreateDirectory(pictures);
            string projects = Path.Combine(folder, "Projects");
            string project = Path.Combine(projects, "capri-desktop"), other = Path.Combine(projects, "metis-orchestrator"), notes = Path.Combine(projects, "notes for the workspace window rebuild");
            string deep = Path.Combine(projects, "a folder with a very long name that keeps going", "and another folder inside it", "capri-website");
            foreach (string made in new[] { project, other, notes, deep }) Directory.CreateDirectory(made);
            string plan = Path.Combine(notes, "PLAN.md"), markup = Path.Combine(notes, "attempt-1-lachys-markup.png");
            File.WriteAllText(plan, "plan");
            File.WriteAllText(markup, "picture");
            List<string> saved = new List<string>(), crowded = new List<string>();
            string pipe = "CapriWorkspace-" + System.Diagnostics.Process.GetCurrentProcess().Id + "-look-";
            Action<WorkspaceWindow, string, ToolStripDropDown> shoot = delegate(WorkspaceWindow window, string name, ToolStripDropDown menu)
            {
                window.Refresh();
                Application.DoEvents();
                string problem = window.Crowded();
                if (problem != null) crowded.Add(name + ": " + problem);
                string path = Path.Combine(pictures, name + ".png");
                LookShots.Save(window, path, menu);
                saved.Add(path);
            };
            using (Workspace empty = new Workspace(Path.Combine(folder, "empty"), pipe + Guid.NewGuid().ToString("N")))
            using (WorkspaceWindow window = new WorkspaceWindow(empty, delegate { return false; }, 1f, false))
            {
                window.Location = new Point(40, 40);
                window.Show(this);
                window.ClientSize = new Size(1080, 740);
                shoot(window, "01-no-chats", null);
                if (!window.ChooseShown) throw new Exception("The empty Workspace doesn't show Choose folder.");
                window.Close();
            }
            using (Workspace work = new Workspace(Path.Combine(folder, "data"), pipe + Guid.NewGuid().ToString("N")))
            {
                work.Executable = Path.Combine(folder, "no-claude-here.cmd");
                WorkspaceChat chat = work.NewChat(project, false);
                work.AddFolders(chat, new[] { other, deep });
                work.AskTrust(chat);
                work.Trust(chat, true);
                lock (chat.Gate) chat.Items.Clear();
                work.AddFiles(chat, new[] { plan, markup });
                WorkspaceChat second = work.NewChat(other, false);
                work.Trust(second, true);
                work.Change(second, "Metis composer notes", null, null);
                string website = Path.Combine(projects, "capri-website");
                Directory.CreateDirectory(website);
                WorkspaceChat third = work.NewChat(website, false);
                work.Change(third, "Website download band", null, "Sonnet");
                using (WorkspaceWindow window = new WorkspaceWindow(work, delegate { return false; }, 1f, false))
                {
                    window.Location = new Point(40, 40);
                    window.Show(this);
                    window.ClientSize = new Size(1080, 740);
                    window.ShowChat(chat);
                    window.Rename("Workspace window rebuild");
                    window.Input.Text = "Look at the plan and the markup, then tell me what's left for the Workspace window.";
                    window.RefreshChat();
                    shoot(window, "02-new-chat-chips", null);

                    window.Input.Text = "";
                    lock (chat.Gate) { chat.Sent.Add(plan); chat.Sent.Add(markup); }
                    work.Add(chat, new WorkspaceItem { Kind = "user", Text = "Look at the plan and the markup, then tell me what's left for the Workspace window.", Files = plan + "\n" + markup });
                    work.Add(chat, new WorkspaceItem { Kind = "text", Text = "Here's what's left, in the order I'd do it.\n\n## Frame\nThe window paints a cut frame but never shapes itself to it, so the corners show slivers.\n```cs\nprivate void Shape() { Neon.Clip(this, Z(16), Z(6)); }\n```\n- Shape it on every resize, like the Mainframe.\n- Draw the title with room for the W." });
                    work.Add(chat, new WorkspaceItem { Kind = "tool", Id = "t1", Name = "Read", Text = "Read " + Path.Combine(project, "app", "WorkspaceWindow.cs"), Turn = "x" });
                    work.Add(chat, new WorkspaceItem { Kind = "result", Id = "t1", Text = "962 lines", Turn = "x" });
                    work.Add(chat, new WorkspaceItem { Kind = "tool", Id = "t2", Name = "Edit", Text = "Edit " + Path.Combine(project, "app", "WorkspaceWindow.cs"), Turn = "x" });
                    work.Add(chat, new WorkspaceItem { Kind = "result", Id = "t2", Text = "The file " + Path.Combine(project, "app", "WorkspaceWindow.cs") + " has been updated.\n  604 |         protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Shape(); Place(); }", Turn = "x" });
                    work.Add(chat, new WorkspaceItem { Kind = "tool", Id = "t3", Name = "Bash", Text = "Bash: powershell -File ci\\build-windows.ps1 -OutputDirectory out", Turn = "x" });
                    work.Add(chat, new WorkspaceItem { Kind = "result", Id = "t3", Text = "Built out\\Capri.exe", Turn = "x" });
                    work.Add(chat, new WorkspaceItem { Kind = "text", Text = "The frame now matches its outline. Next up are the dropdowns." });
                    work.Add(chat, new WorkspaceItem { Kind = "end", Text = "48.2s | $0.31" });
                    window.RefreshChat();
                    window.ToggleTool("t2");
                    window.ScrollToEnd();
                    shoot(window, "03-conversation", null);

                    WorkspaceItem approval = new WorkspaceItem { Kind = "approval", Name = "Bash", Text = "Bash: npm test -- --runInBand", Input = HuskJson.Write(HuskJson.Map("command", "npm test -- --runInBand", "description", "Run the tests once, in band", "timeout", 120000)) };
                    approval.Ask = new WorkspaceApproval { Item = approval };
                    work.Add(chat, approval);
                    window.RefreshChat();
                    window.ScrollToEnd();
                    shoot(window, "04-approval", null);

                    WorkspaceTurn running = new WorkspaceTurn { Chat = chat };
                    chat.Live = running;
                    window.RefreshChat();
                    shoot(window, "05-running", null);
                    chat.Live = null;
                    lock (chat.Gate) chat.Items.Remove(approval);
                    approval.Ask.Answered.Dispose();
                    approval.Ask = null;
                    window.RefreshChat();

                    window.ShowChat(third);
                    work.Add(third, new WorkspaceItem { Kind = "user", Text = "Make the download band match the new home page." });
                    work.AskTrust(third);
                    window.RefreshChat();
                    shoot(window, "06-trust", null);

                    window.ShowChat(chat);
                    window.ScrollToEnd();
                    window.PickMode("Everything");
                    if (window.Asking == null) throw new Exception("Everything didn't ask first.");
                    shoot(window, "07-everything-confirm", null);
                    window.Pending(window.Asking, false);
                    if (chat.Mode == "Everything") throw new Exception("Keep didn't keep the mode.");

                    shoot(window, "08-plus-menu", window.OpenMenu("add"));
                    shoot(window, "09-mode-menu", window.OpenMenu("mode"));
                    shoot(window, "10-model-menu", window.OpenMenu("model"));
                    ToolStripDropDown closing = window.OpenMenu("model");
                    closing.Close();
                    Application.DoEvents();

                    WorkspaceChat longChat = work.NewChat(project, false);
                    work.Trust(longChat, true);
                    work.Change(longChat, "A long chat", null, null);
                    lock (longChat.Gate)
                        for (int i = 0; i < 400; i++)
                        {
                            if (i % 4 == 0) longChat.Items.Add(new WorkspaceItem { Kind = "user", Text = "Question " + i + ": what does this part of the app do?" });
                            else if (i % 4 == 3) longChat.Items.Add(new WorkspaceItem { Kind = "tool", Id = "l" + i, Name = "Grep", Text = "Grep Workspace" });
                            else longChat.Items.Add(new WorkspaceItem { Kind = "text", Text = "Answer " + i + ". It reads the chat, lays out each item and draws only what's on screen, so a long chat stays quick." });
                        }
                    work.Save(longChat);
                    window.ShowChat(longChat);
                    window.ScrollConversation(window.ConversationTall / 2);
                    shoot(window, "11-long-scrolled", null);

                    window.ShowChat(chat);
                    window.ClientSize = new Size(WorkspaceWindow.Narrowest, WorkspaceWindow.Shortest);
                    window.ScrollToEnd();
                    shoot(window, "12-smallest-100", null);
                    window.Close();
                }
                foreach (int scale in new[] { 125, 150 })
                {
                    float z = scale / 100f;
                    using (WorkspaceWindow window = new WorkspaceWindow(work, delegate { return false; }, z, false))
                    {
                        window.Location = new Point(0, 0);
                        window.Show(this);
                        window.ShowChat(chat);
                        window.ClientSize = new Size((int)Math.Round(WorkspaceWindow.Wide * z), Math.Min((int)Math.Round(WorkspaceWindow.Tall * z), 1032));
                        window.ScrollToEnd();
                        shoot(window, "13-conversation-" + scale, null);
                        window.ClientSize = new Size((int)Math.Round(WorkspaceWindow.Narrowest * z), (int)Math.Round(WorkspaceWindow.Shortest * z));
                        window.ScrollToEnd();
                        shoot(window, "14-smallest-" + scale, null);
                        shoot(window, "15-mode-menu-" + scale, window.OpenMenu("mode"));
                        window.Close();
                    }
                }
            }
            bool shown = mainframe.Visible;
            FormWindowState state = mainframe.WindowState;
            try
            {
                if (!shown) mainframe.Show();
                if (mainframe.WindowState != FormWindowState.Normal) mainframe.WindowState = FormWindowState.Normal;
                mainframe.Settle();
                string path = Path.Combine(pictures, "mainframe.png");
                LookShots.Save(mainframe, path);
                saved.Add(path);
            }
            finally
            {
                if (mainframe.WindowState != state) mainframe.WindowState = state;
                if (!shown) mainframe.Hide();
            }
            foreach (string path in saved) if (!File.Exists(path) || new FileInfo(path).Length < 2000) throw new Exception("Workspace look picture missing: " + path);
            checks.Add("PASS Workspace look pictures saved: " + saved.Count + " in " + pictures);
            if (crowded.Count > 0) throw new Exception("Workspace look is crowded: " + string.Join(" | ", crowded.ToArray()));
            checks.Add("PASS Workspace look: nothing outside, overlapping or cut off in any of the " + (saved.Count - 1) + " Workspace pictures");
        }
    }
}

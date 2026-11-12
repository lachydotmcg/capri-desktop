using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
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
        [DllImport("dwmapi.dll")] private static extern int DwmFlush();

        internal static void Flush() { try { DwmFlush(); } catch (Exception) { } }

        internal static bool CueMissing(WorkspaceWindow window) { using (Bitmap copy = Copy(window)) return window.CueMissing(copy); }

        // One of her menus on its own over the light backdrop.
        internal static void SaveMenu(ToolStripDropDown menu, string path)
        {
            menu.Refresh();
            using (Bitmap sheet = new Bitmap(menu.Width + Margin * 2, menu.Height + Margin * 2, PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(sheet))
                {
                    graphics.Clear(Paper);
                    Lay(graphics, menu, new Point(Margin, Margin));
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                sheet.Save(path, ImageFormat.Png);
            }
        }

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
            List<string> saved = new List<string>(), crowded = new List<string>(), cueless = new List<string>();
            // The pictures show the stand-in folders where a person's projects would be, never the test's own temp path.
            // Her folder in the pictures is a stand-in for the profile's Capri\Workspace, never the real one.
            string standinProfile = Path.Combine(folder, "profile"), standinHome = Path.Combine(standinProfile, "Capri", "Workspace");
            Func<string, string> alias = delegate(string path)
            {
                int at = path.IndexOf(projects, StringComparison.OrdinalIgnoreCase);
                while (at >= 0)
                {
                    path = path.Substring(0, at) + @"C:\Users\you\Projects" + path.Substring(at + projects.Length);
                    at = path.IndexOf(projects, at + 1, StringComparison.OrdinalIgnoreCase);
                }
                at = path.IndexOf(standinProfile, StringComparison.OrdinalIgnoreCase);
                if (at >= 0) path = path.Substring(0, at) + @"C:\Users\you" + path.Substring(at + standinProfile.Length);
                return path;
            };
            string pipe = "CapriWorkspace-" + Process.GetCurrentProcess().Id + "-look-";
            Action<WorkspaceWindow, string, ToolStripDropDown> shoot = delegate(WorkspaceWindow window, string name, ToolStripDropDown menu)
            {
                window.Refresh();
                Application.DoEvents();
                string problem = window.Crowded();
                if (problem != null) crowded.Add(name + ": " + problem);
                // Windows' copy can be a frame behind a layout that just moved, so a miss is looked at again on the next frame; a
                // cue a repaint really lost stays lost.
                if (menu == null && LookShots.CueMissing(window))
                {
                    LookShots.Flush();
                    window.Refresh();
                    Application.DoEvents();
                    LookShots.Flush();
                    if (LookShots.CueMissing(window)) cueless.Add(name);
                }
                string path = Path.Combine(pictures, name + ".png");
                LookShots.Save(window, path, menu);
                saved.Add(path);
            };
            using (Workspace empty = new Workspace(Path.Combine(folder, "empty"), pipe + Guid.NewGuid().ToString("N")) { Home = standinHome })
            using (WorkspaceWindow window = new WorkspaceWindow(empty, delegate { return false; }, 1f, false))
            {
                window.PathAlias = alias;
                window.Location = new Point(40, 40);
                window.Show(this);
                window.ClientSize = new Size(1080, 740);
                shoot(window, "01-no-chats", null);
                if (window.Chat == null || !empty.IsHome(window.Chat.Folder) || empty.Chats().Count != 1) throw new Exception("An empty Workspace didn't open straight into a chat in her folder.");
                window.Close();
            }
            using (Workspace work = new Workspace(Path.Combine(folder, "data"), pipe + Guid.NewGuid().ToString("N")))
            {
                work.Executable = Path.Combine(folder, "no-claude-here.cmd");
                work.Home = standinHome;
                WorkspaceChat second = work.NewChat(other, false);
                work.Trust(second, true);
                work.Change(second, "Metis composer notes", null, null);
                string website = Path.Combine(projects, "capri-website");
                Directory.CreateDirectory(website);
                WorkspaceChat third = work.NewChat(website, false);
                work.Change(third, "Website download band", null, "Sonnet");
                System.Threading.Thread.Sleep(20);
                WorkspaceChat chat = work.NewChat(project, false);
                work.AddFolders(chat, new[] { other, deep });
                work.AskTrust(chat);
                work.Trust(chat, true);
                lock (chat.Gate) chat.Items.Clear();
                work.AddFiles(chat, new[] { plan, markup });
                using (WorkspaceWindow window = new WorkspaceWindow(work, delegate { return false; }, 1f, false))
                {
                    window.PathAlias = alias;
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
                    string shapeInput = HuskJson.Write(HuskJson.Map("file_path", Path.Combine(project, "app", "WorkspaceWindow.cs"),
                        "old_string", "        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Place(); }",
                        "new_string", "        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Shape(); Place(); }\n\n        // Cuts the window to her frame's corners, like the Mainframe.\n        private void Shape() { if (Width > 0 && Height > 0) Neon.Clip(this, Z(16), Z(6)); }"));
                    work.Add(chat, new WorkspaceItem { Kind = "tool", Id = "t2", Name = "Edit", Text = "Edit " + Path.Combine(project, "app", "WorkspaceWindow.cs"), Turn = "x", Input = shapeInput });
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

                    work.Add(third, new WorkspaceItem { Kind = "tool", Id = "w1", Name = "Grep", Text = "Grep download", Turn = "w" });
                    StringBuilder found = new StringBuilder();
                    for (int i = 0; i < 24; i++) found.Append(Path.Combine(deep, "src", "sections", "download-band-" + i + ".html")).Append(':').Append(40 + i).Append(":  <a class=\"download\" href=\"/releases/latest\">Download for Windows</a>\n");
                    work.Add(third, new WorkspaceItem { Kind = "result", Id = "w1", Text = found.ToString().TrimEnd('\n'), Turn = "w" });
                    work.Trust(third, true);
                    window.RefreshChat();
                    window.ToggleTool("w1");
                    window.ScrollToEnd();
                    shoot(window, "16-long-result", null);

                    window.ShowChat(chat);
                    window.ScrollToEnd();
                    window.PickMode("Everything");
                    if (window.Asking != null || chat.Mode != "Everything") throw new Exception("Everything asked first or didn't switch.");
                    shoot(window, "07-everything", null);
                    window.PickMode("Ask me");

                    window.DeleteChat(second);
                    if (window.Asking == null) throw new Exception("Delete chat didn't ask in its card.");
                    shoot(window, "17-delete-confirm", null);
                    window.Pending(window.Asking, false);
                    if (!work.Chats().Contains(second)) throw new Exception("Keep it didn't keep the chat.");

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
                        window.PathAlias = alias;
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
            SmokeWorkspaceAttachments(checks, folder, projects, pipe, alias, shoot);
            SmokeWorkspaceTitles(checks, folder, projects, pipe, alias, shoot);
            SmokeWorkspaceEdits(checks, folder, projects, pipe, alias, shoot);
            SmokeWorkspaceTools(checks, folder, projects, pipe, alias, shoot);
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
                mainframe.ShowSettings(true);
                mainframe.Wheel(-4);
                mainframe.Settle();
                path = Path.Combine(pictures, "mainframe-scrolled.png");
                LookShots.Save(mainframe, path);
                saved.Add(path);
                mainframe.ShowSettings(false);
                menu.Show(new Point(80, 80));
                Application.DoEvents();
                path = Path.Combine(pictures, "mainframe-menu.png");
                LookShots.SaveMenu(menu, path);
                saved.Add(path);
                menu.Close();
                Application.DoEvents();
            }
            finally
            {
                if (menu.Visible) menu.Close();
                mainframe.ShowSettings(false);
                if (mainframe.WindowState != state) mainframe.WindowState = state;
                if (!shown) mainframe.Hide();
            }
            foreach (string path in saved) if (!File.Exists(path) || new FileInfo(path).Length < 2000) throw new Exception("Workspace look picture missing: " + path);
            checks.Add("PASS Workspace look pictures saved: " + saved.Count + " in " + pictures);
            if (crowded.Count > 0) throw new Exception("Workspace look is crowded: " + string.Join(" | ", crowded.ToArray()));
            checks.Add("PASS Workspace look: nothing outside, overlapping or cut off in any of the " + (saved.Count - 3) + " Workspace pictures");
            if (cueless.Count > 0) throw new Exception("Workspace's empty message box lost its cue in: " + string.Join(", ", cueless.ToArray()));
            checks.Add("PASS Workspace's empty message box shows its cue in every picture where it's empty and uncovered");
        }

        // Chats in her folder take their titles from their first message, pictured with the empty state at 100%, 125% and 150%:
        // a long message cut at a word, a short one, and files with no words named after the first file. A typed title wins.
        private void SmokeWorkspaceTitles(List<string> checks, string folder, string projects, string pipe, Func<string, string> alias, Action<WorkspaceWindow, string, ToolStripDropDown> shoot)
        {
            string box = Path.Combine(folder, "titles"), home = Path.Combine(folder, "profile", "Capri", "Workspace");
            bool realHome = Directory.Exists(Workspace.HomeFolder());
            string notes = Path.Combine(projects, "notes for the workspace window rebuild"), markup = Path.Combine(notes, "attempt-1-lachys-markup.png");
            Directory.CreateDirectory(box);
            Directory.CreateDirectory(notes);
            if (!File.Exists(markup)) File.WriteAllText(markup, "x");
            string standin = WorkspaceRawStandin(box, Guid.NewGuid().ToString(), false);
            WorkspaceTimed(box);
            string longOne = "Make the download band on the home page match the new city look, with both buttons";
            string[] expected = { "Make the download band on the home page\u2026", "Fix the header spacing", "attempt-1-lachys-markup.png" };
            if (Workspace.TitleFrom(longOne, null) != expected[0] || Workspace.TitleFrom("Fix the header spacing.", null) != expected[1] || Workspace.TitleFrom("  ", new List<string> { markup }) != expected[2] || Workspace.TitleFrom("Why?!", null) != "Why")
                throw new Exception("Titles from first messages came out wrong: " + Workspace.TitleFrom(longOne, null));
            foreach (float z in new[] { 1f, 1.25f, 1.5f })
            {
                int scale = (int)Math.Round(z * 100);
                using (Workspace work = new Workspace(Path.Combine(box, "data" + scale), pipe + Guid.NewGuid().ToString("N")))
                using (WorkspaceWindow window = new WorkspaceWindow(work, delegate { return false; }, z, false))
                {
                    work.Executable = standin;
                    work.Home = home;
                    window.PathAlias = alias;
                    window.Location = new Point(z == 1f ? 40 : 0, z == 1f ? 40 : 0);
                    window.Show(this);
                    window.ClientSize = new Size((int)Math.Round(WorkspaceWindow.Wide * z), Math.Min((int)Math.Round(WorkspaceWindow.Tall * z), 1032));
                    WorkspaceChat opened = window.Chat;
                    if (opened == null || !work.IsHome(opened.Folder) || opened.Title != "New chat" || work.Chats().Count != 1 || !work.Trusted(opened) || opened.TrustItem != null || !window.CanRehome || window.Asking != null)
                        throw new Exception("An empty Workspace didn't open straight into a trusted chat in her folder at " + scale + "%.");
                    if (window.Header != WorkspaceHeaderOf(alias, home)) throw new Exception("Her folder's chat header says more than its path: " + window.Header);
                    shoot(window, "23-first-open-" + scale, null);
                    string[] said = { longOne, "Fix the header spacing.", "" };
                    for (int i = 0; i < said.Length; i++)
                    {
                        window.NewChat();
                        WorkspaceChat chat = window.Chat;
                        if (chat.Title != "New chat") throw new Exception("A new chat in her folder wasn't called New chat before its first message.");
                        if (said[i].Length == 0) window.AddPaths(new string[0], new[] { markup });
                        window.Input.Text = said[i];
                        window.TypeInto(window.Input, Keys.Enter);
                        WorkspaceWait(delegate { Application.DoEvents(); return chat.Live == null; }, "A titles turn didn't finish.");
                        Application.DoEvents();
                        if (chat.Title != expected[i]) throw new Exception("The chat was titled \"" + chat.Title + "\" instead of \"" + expected[i] + "\".");
                    }
                    window.RefreshChat();
                    shoot(window, "24-home-titles-" + scale, null);
                    if (z == 1f)
                    {
                        window.NewChat();
                        WorkspaceChat named = window.Chat;
                        window.Rename("Website notes");
                        window.Input.Text = "Something else entirely";
                        window.TypeInto(window.Input, Keys.Enter);
                        WorkspaceWait(delegate { Application.DoEvents(); return named.Live == null; }, "The named chat's turn didn't finish.");
                        if (named.Title != "Website notes") throw new Exception("A typed title was replaced by the first message.");
                        window.ShowChat(window.Chat);
                    }
                    window.Close();
                }
            }
            // Two chats in the same picked folder are told apart by their first messages; a renamed one keeps its name.
            string picked = Path.Combine(projects, "capri-desktop");
            Directory.CreateDirectory(picked);
            using (Workspace work = new Workspace(Path.Combine(box, "picked"), pipe + Guid.NewGuid().ToString("N")))
            using (WorkspaceWindow window = new WorkspaceWindow(work, delegate { return false; }, 1f, false))
            {
                work.Executable = standin;
                work.Home = home;
                window.PathAlias = alias;
                window.Location = new Point(40, 40);
                window.Show(this);
                window.ClientSize = new Size(WorkspaceWindow.Wide, WorkspaceWindow.Tall);
                string[] said = { "Speed up the chat list when there are hundreds of chats", "Why does the tray icon blink twice?", "Tidy the settings page" };
                string[] wanted = { "Speed up the chat list when there are\u2026", "Why does the tray icon blink twice", "My settings notes" };
                for (int i = 0; i < said.Length; i++)
                {
                    WorkspaceChat chat = work.NewChat(picked, false);
                    work.Trust(chat, true);
                    chat.TrustAsked = true;
                    window.RefreshChat();
                    window.ShowChat(chat);
                    if (chat.Title != "capri-desktop") throw new Exception("A picked-folder chat wasn't named after its folder before its first message.");
                    if (i == 2) window.Rename("My settings notes");
                    window.Input.Text = said[i];
                    window.TypeInto(window.Input, Keys.Enter);
                    WorkspaceWait(delegate { Application.DoEvents(); return chat.Live == null; }, "A picked-folder turn didn't finish.");
                    Application.DoEvents();
                    if (chat.Title != wanted[i]) throw new Exception("The picked-folder chat was titled \"" + chat.Title + "\" instead of \"" + wanted[i] + "\".");
                }
                window.RefreshChat();
                shoot(window, "25-picked-titles-100", null);
                window.Close();
            }
            if (!realHome && Directory.Exists(Workspace.HomeFolder())) throw new Exception("A smoke run made the real folder for her chats.");
            string narrow = WorkPaint.Fit("Read these and tell me what's still missing", SystemFonts.MessageBoxFont, WorkPaint.Wide("Read these and tell me…", SystemFonts.MessageBoxFont) + 2);
            if (narrow != "Read these and tell me\u2026") throw new Exception("A title cut for width didn't end at a word with the dots tight against it: " + narrow);
            checks.Add("PASS Workspace chats take their title from their first message while it's still the one they were given (her folder's New chat, or a picked folder's name): the first words cut at a word near 40 letters ending in a tight \u2026, or the first file's name; titles cut for width end at a word the same way; two chats in one picked folder end up told apart, and a typed title is never replaced");
        }

        // Her own folder and files as attachments, through the real send path with a stand-in claude.cmd: "+ New chat" opens a
        // chat in her folder (a stand-in for the profile's Capri\Workspace here, never the real one), trusted and ready to type in,
        // whose folder can change until the first message; files ride with one message and then leave the composer while
        // staying part of the chat, also after a restart; a file can be taken off before sending, attached again later, and
        // sent with no words; a failed start keeps the message and its files for Retry. Pictured at 100%, 125% and 150%.
        private void SmokeWorkspaceAttachments(List<string> checks, string folder, string projects, string pipe, Func<string, string> alias, Action<WorkspaceWindow, string, ToolStripDropDown> shoot)
        {
            string box = Path.Combine(folder, "attachments"), data = Path.Combine(box, "data");
            string home = Path.Combine(folder, "profile", "Capri", "Workspace");
            string notes = Path.Combine(projects, "notes for the workspace window rebuild"), other = Path.Combine(projects, "metis-orchestrator");
            Directory.CreateDirectory(box);
            Directory.CreateDirectory(notes);
            Directory.CreateDirectory(other);
            string plan = Path.Combine(notes, "PLAN.md"), shot = Path.Combine(notes, "attempt-1-lachys-markup.png"), spare = Path.Combine(notes, "spare.txt");
            foreach (string made in new[] { plan, shot, spare }) if (!File.Exists(made)) File.WriteAllText(made, "x");
            Func<string, string> shown = alias;
            string standin = WorkspaceRawStandin(box, Guid.NewGuid().ToString(), false), stdin = Path.Combine(box, "stdin.txt");
            WorkspaceTimed(box);
            Func<WorkspaceChat, WorkspaceItem> lastAsk = delegate(WorkspaceChat chat) { return WorkspaceItems(chat).FindLast(delegate(WorkspaceItem item) { return item.Kind == "user"; }); };
            Action<WorkspaceWindow, WorkspaceChat> send = delegate(WorkspaceWindow window, WorkspaceChat chat)
            {
                window.TypeInto(window.Input, Keys.Enter);
                WorkspaceWait(delegate { Application.DoEvents(); return chat.Live == null && chat.PendingText == null; }, "The attachment turn didn't finish.");
                Application.DoEvents();
                window.RefreshChat();
            };
            Func<WorkspaceWindow, int> fileChips = delegate(WorkspaceWindow window) { return window.Chips.FindAll(delegate(string chip) { return chip.StartsWith("file ", StringComparison.Ordinal); }).Count; };
            string chatId = null;
            if (Directory.Exists(home)) throw new Exception("Her folder was there before any chat needed it.");
            foreach (float z in new[] { 1f, 1.25f, 1.5f })
            {
                int scale = (int)Math.Round(z * 100);
                using (Workspace work = new Workspace(z == 1f ? data : data + scale, pipe + Guid.NewGuid().ToString("N")))
                {
                    work.Executable = standin;
                    work.Home = home;
                    using (WorkspaceWindow window = new WorkspaceWindow(work, delegate { return false; }, z, false))
                    {
                        window.PathAlias = shown;
                        window.Location = new Point(z == 1f ? 40 : 0, z == 1f ? 40 : 0);
                        window.Show(this);
                        window.ClientSize = new Size((int)Math.Round(WorkspaceWindow.Wide * z), Math.Min((int)Math.Round(WorkspaceWindow.Tall * z), 1032));
                        if (z == 1f && Directory.Exists(home)) throw new Exception("Opening Workspace made her folder.");
                        WorkspaceChat opened = window.Chat;
                        window.NewChat();
                        WorkspaceChat chat = window.Chat;
                        if (chat != opened) throw new Exception("+ New chat made another chat beside the one Workspace opened into.");
                        if (chat == null || !work.IsHome(chat.Folder) || z == 1f && Directory.Exists(home) || !work.Trusted(chat) || chat.TrustItem != null || !window.CanRehome || !window.Chips[0].EndsWith("(runs here)", StringComparison.Ordinal))
                            throw new Exception("+ New chat didn't open a trusted chat in her folder, ready to type in.");
                        window.NewChat();
                        if (window.Chat != chat) throw new Exception("+ New chat made a second unused chat in her folder.");
                        shoot(window, "20-home-chat-" + scale, null);
                        window.AddPaths(new string[0], new[] { plan, shot });
                        window.Input.Text = "Read these and tell me what's still missing.";
                        window.RefreshChat();
                        if (fileChips(window) != 2) throw new Exception("Attached files didn't show as chips: " + string.Join(", ", window.Chips.ToArray()));
                        shoot(window, "21-chips-before-send-" + scale, null);
                        send(window, chat);
                        WorkspaceItem asked = lastAsk(chat);
                        if (fileChips(window) != 0 || Workspace.Attached(chat).Count != 0 || !Workspace.AllFiles(chat).Contains(plan) || !Workspace.AllFiles(chat).Contains(shot) || asked == null || asked.Files != plan + "\n" + shot)
                            throw new Exception("After sending, the files didn't leave the composer while staying with the chat and its message: " + string.Join(", ", window.Chips.ToArray()));
                        if (window.CanRehome || window.Input.Text.Length != 0) throw new Exception("The chat's folder could still change after a message, or the box kept the words.");
                        if (!Directory.Exists(home)) throw new Exception("Her folder wasn't made when the first message went.");
                        string heard = File.ReadAllText(stdin);
                        if (heard.IndexOf("Attached files:", StringComparison.Ordinal) < 0 || heard.IndexOf(plan.Replace("\\", "\\\\"), StringComparison.Ordinal) < 0 && heard.IndexOf(plan, StringComparison.Ordinal) < 0) throw new Exception("Claude Code wasn't told about the attached files.");
                        window.ScrollToEnd();
                        shoot(window, "22-chips-after-send-" + scale, null);
                        if (work.Chats().Count != 1) throw new Exception("A clean " + scale + "% run had " + work.Chats().Count + " chats.");
                        if (z != 1f) { window.Close(); continue; }
                        chatId = chat.Id;

                        window.AddPaths(new string[0], new[] { spare });
                        window.ClickChipCross(spare);
                        if (Workspace.AllFiles(chat).Contains(spare) || fileChips(window) != 0) throw new Exception("Taking a chip off before sending didn't un-attach it.");
                        window.AddPaths(new string[0], new[] { plan });
                        if (fileChips(window) != 1 || Workspace.Attached(chat).Count != 1) throw new Exception("Adding a sent file again didn't attach it to the next message.");
                        window.Input.Text = "";
                        send(window, chat);
                        asked = lastAsk(chat);
                        heard = File.ReadAllText(stdin);
                        if (asked == null || asked.Text != "" || asked.Files != plan || fileChips(window) != 0 || heard.IndexOf("Attached files:", StringComparison.Ordinal) < 0)
                            throw new Exception("A message of files with no words didn't go with just its file.");

                        work.Executable = Path.Combine(box, "no-claude-here.cmd");
                        window.AddPaths(new string[0], new[] { shot });
                        window.Input.Text = "This one can't start.";
                        window.TypeInto(window.Input, Keys.Enter);
                        Application.DoEvents();
                        window.RefreshChat();
                        asked = lastAsk(chat);
                        if (asked == null || asked.Text != "This one can't start." || asked.Files != shot || window.Find("Retry") == null || fileChips(window) != 0 || window.Input.Text.Length != 0)
                            throw new Exception("A failed start didn't keep the message and its file for Retry.");
                        work.Executable = standin;
                        window.Find("Retry").PerformClick();
                        WorkspaceWait(delegate { Application.DoEvents(); return chat.Live == null; }, "Retry didn't finish.");
                        if (File.ReadAllText(stdin).IndexOf("This one can't start.", StringComparison.Ordinal) < 0) throw new Exception("Retry didn't send the message again.");
                        work.Flush(chat);
                        window.Close();
                    }
                }
            }
            using (Workspace work = new Workspace(data, pipe + Guid.NewGuid().ToString("N")) { Home = Path.Combine(box, "her-folder") })
            {
                work.Executable = standin;
                work.Home = home;
                WorkspaceChat chat = work.Chats().Find(delegate(WorkspaceChat found) { return found.Id == chatId; });
                if (chat == null || !Workspace.AllFiles(chat).Contains(plan) || !Workspace.AllFiles(chat).Contains(shot) || Workspace.Attached(chat).Count != 0 || !work.Trusted(chat))
                    throw new Exception("After a restart the chat lost its files, showed them as attached again, or stopped trusting her folder.");
                using (WorkspaceWindow window = new WorkspaceWindow(work, delegate { return false; }, 1f, false))
                {
                    window.PathAlias = shown;
                    window.Show(this);
                    window.NewChat();
                    WorkspaceChat fresh = window.Chat;
                    if (fresh == null || fresh == chat || !work.IsHome(fresh.Folder)) throw new Exception("+ New chat didn't open a new chat in her folder after the last one was used.");
                    window.PickFolders = delegate { return new[] { other, notes }; };
                    window.ChooseMain();
                    if (fresh.Folder != Workspace.Tidy(other) || fresh.Folders.Count != 1 || fresh.Folders[0] != Workspace.Tidy(notes) || fresh.Title != "metis-orchestrator" || work.Trusted(fresh) || !window.CanRehome)
                        throw new Exception("Picking folders before the first message didn't replace her folder, or a picked folder counted as trusted.");
                    window.Close();
                }
            }
            foreach (float z in new[] { 1f, 1.25f, 1.5f })
            {
                int scale = (int)Math.Round(z * 100);
                foreach (bool dropped in new[] { false, true })
                {
                    using (Workspace work = new Workspace(Path.Combine(box, (dropped ? "drop" : "change") + scale), pipe + Guid.NewGuid().ToString("N")) { Home = home })
                    using (WorkspaceWindow window = new WorkspaceWindow(work, delegate { return false; }, z, false))
                    {
                        work.Executable = standin;
                        window.PathAlias = shown;
                        window.Location = new Point(z == 1f ? 40 : 0, z == 1f ? 40 : 0);
                        window.Show(this);
                        window.ClientSize = new Size((int)Math.Round(WorkspaceWindow.Wide * z), Math.Min((int)Math.Round(WorkspaceWindow.Tall * z), 1032));
                        WorkspaceChat chat = window.Chat;
                        if (chat == null || !work.IsHome(chat.Folder)) throw new Exception("Workspace didn't open into a chat in her folder.");
                        if (dropped) window.DropOn(new[] { other });
                        else { window.PickFolders = delegate { return new[] { other }; }; window.ChooseMain(); }
                        Application.DoEvents();
                        if (window.Chat != chat || chat.Folder != Workspace.Tidy(other) || chat.Folders.Count != 0 || chat.Title != "metis-orchestrator" || work.Chats().Count != 1 || work.Trusted(chat) || !window.CanRehome)
                            throw new Exception((dropped ? "A folder dropped on" : "Changing the folder of") + " the first open didn't move its chat there, untrusted until asked: " + chat.Folder + " / " + string.Join(", ", window.Chips.ToArray()));
                        shoot(window, (dropped ? "28-dropped-folder-" : "27-changed-folder-") + scale, null);
                        window.Close();
                    }
                }
            }
            checks.Add("PASS Workspace opens straight into a chat in her folder when it has none, and a folder picked with change or dropped on it before the first message takes its place; + New chat opens a trusted chat in her own folder (made when its first message goes), ready to type in and reused while unused, whose folder the runs-here chip can change until the first message; files ride with one message then leave the composer but stay part of the chat across a restart; a chip taken off before sending is un-attached, a sent file added again rides again, files can go with no words, and a failed start keeps the message and its files for Retry");
        }

        // An Edit through the real path: a stand-in claude.cmd streams a real-shaped Edit call and its result, Capri's approval
        // hook's undo copy is made as the hook would make it, and the card shows the removed and added lines from the tool's
        // own input. They survive a restart, Undo last edits follows the engine's own answer after the edit and after an undo,
        // and a 5,000-line Write keeps at most Workspace.ShownAll letters for its card and lays out quickly.
        private void SmokeWorkspaceEdits(List<string> checks, string folder, string projects, string pipe, Func<string, string> alias, Action<WorkspaceWindow, string, ToolStripDropDown> shoot)
        {
            string box = Path.Combine(folder, "edits"), project = Path.Combine(projects, "shop"), data = Path.Combine(box, "data");
            Directory.CreateDirectory(box);
            Directory.CreateDirectory(project);
            string file = Path.Combine(project, "Window.cs"), session = Guid.NewGuid().ToString();
            string before = "class Window\n{\n    void Resized() { Place(); }\n}\n";
            string oldText = "    void Resized() { Place(); }";
            string newText = "    void Resized() { Shape(); Place(); }\n\n    // Cuts the window to her frame's corners.\n    void Shape() { Neon.Clip(this, Z(16), Z(6)); }";
            File.WriteAllText(file, before);
            string standin = WorkspaceRawStandin(box, session, true), finish = Path.Combine(box, "finish"), stream = Path.Combine(box, "result.txt");
            Action<string, string, Dictionary<string, object>, string, string> streamOf = delegate(string id, string tool, Dictionary<string, object> input, string result, string words)
            {
                StringBuilder lines = new StringBuilder();
                lines.Append(HuskJson.Write(HuskJson.Map("type", "system", "subtype", "init", "session_id", session, "cwd", project, "model", "claude-sonnet-4-5"))).Append('\n');
                lines.Append(HuskJson.Write(HuskJson.Map("type", "assistant", "session_id", session, "message", HuskJson.Map("role", "assistant", "content", new List<object> { HuskJson.Map("type", "tool_use", "id", id, "name", tool, "input", input) })))).Append('\n');
                lines.Append(HuskJson.Write(HuskJson.Map("type", "user", "session_id", session, "message", HuskJson.Map("role", "user", "content", new List<object> { HuskJson.Map("type", "tool_result", "tool_use_id", id, "content", result) })))).Append('\n');
                lines.Append(HuskJson.Write(HuskJson.Map("type", "assistant", "session_id", session, "message", HuskJson.Map("role", "assistant", "content", new List<object> { HuskJson.Map("type", "text", "text", words) })))).Append('\n');
                lines.Append(HuskJson.Write(HuskJson.Map("type", "result", "subtype", "success", "is_error", false, "duration_ms", 1200, "total_cost_usd", 0.02, "session_id", session, "result", words))).Append('\n');
                File.WriteAllText(stream, lines.ToString(), new UTF8Encoding(false));
            };
            // One turn: sent from the box, the hook's undo copy, the edit itself on disk, then Claude Code's stream.
            Action<WorkspaceWindow, Workspace, WorkspaceChat, string, string, Dictionary<string, object>, string, string> turnOf =
                delegate(WorkspaceWindow window, Workspace work, WorkspaceChat chat, string asked, string tool, Dictionary<string, object> input, string target, string after)
            {
                File.Delete(finish);
                window.Input.Text = asked;
                window.TypeInto(window.Input, Keys.Enter);
                WorkspaceTurn live = chat.Live;
                if (live == null) throw new Exception("Workspace didn't start the edit turn.");
                work.Snapshot(chat, live.Id, tool, input, project);
                File.WriteAllText(target, after);
                streamOf("toolu_" + tool.ToLowerInvariant(), tool, input, "The file " + target + " has been updated.", "Done.");
                File.WriteAllText(finish, "go");
                if (!live.Done.WaitOne(15000)) throw new Exception("The stand-in edit turn didn't finish.");
                File.Delete(finish);
                WorkspaceWait(delegate { Application.DoEvents(); return chat.Live == null && window.Find("Send") != null; }, "Workspace didn't settle after the edit turn.");
                Application.DoEvents();
            };
            Func<WorkspaceWindow, bool> diffShown = delegate(WorkspaceWindow window)
            {
                window.ToggleTool("toolu_edit");
                List<string> rows = window.ToolWell("toolu_edit");
                return rows.Contains("R- " + oldText) && rows.Contains("G+ " + oldText.Replace("Place();", "Shape(); Place();")) && rows.Contains("G+     void Shape() { Neon.Clip(this, Z(16), Z(6)); }");
            };
            string chatId;
            using (Workspace work = new Workspace(data, pipe + Guid.NewGuid().ToString("N")) { Home = Path.Combine(box, "her-folder") })
            {
                work.Executable = standin;
                WorkspaceChat chat = work.NewChat(project, false);
                work.Trust(chat, true);
                chat.TrustAsked = true;
                work.Change(chat, "Shape on resize", null, null);
                chatId = chat.Id;
                using (WorkspaceWindow window = new WorkspaceWindow(work, delegate { return false; }, 1f, false))
                {
                    window.PathAlias = alias;
                    window.Location = new Point(40, 40);
                    window.Show(this);
                    window.ClientSize = new Size(1080, 740);
                    window.ShowChat(chat);
                    Application.DoEvents();
                    if (window.UndoOffered || work.CanUndo(chat)) throw new Exception("Undo last edits was offered before any edit.");
                    using (Bitmap blank = new Bitmap(window.Width, window.Height))
                    {
                        using (Graphics graphics = Graphics.FromImage(blank)) graphics.Clear(Neon.Field);
                        if (!window.CueMissing(blank)) throw new Exception("The cue check didn't notice a message box with no cue.");
                    }
                    // Like the pictures, a miss is looked at again a frame later, since the copy can be a frame behind.
                    if (LookShots.CueMissing(window))
                    {
                        LookShots.Flush();
                        window.Refresh();
                        Application.DoEvents();
                        LookShots.Flush();
                        if (LookShots.CueMissing(window)) throw new Exception("The empty message box has no cue in a new chat.");
                    }
                    turnOf(window, work, chat, "Shape the window on every resize.", "Edit", HuskJson.Map("file_path", file, "old_string", oldText, "new_string", newText), file, before.Replace(oldText, newText));
                    WorkspaceItem tool = WorkspaceItems(chat).Find(delegate(WorkspaceItem item) { return item.Kind == "tool" && item.Name == "Edit"; });
                    if (tool == null || tool.Input.Length == 0) throw new Exception("The Edit card didn't keep the tool's input.");
                    if (!work.CanUndo(chat)) throw new Exception("The engine had nothing to undo after the edit landed.");
                    WorkspaceWait(delegate { Application.DoEvents(); return window.UndoOffered; }, "Undo last edits didn't light up after the edit landed.");
                    if (!diffShown(window)) throw new Exception("The Edit card didn't show the removed and added lines: " + string.Join(" / ", window.ToolWell("toolu_edit").ToArray()));
                    window.ScrollToEnd();
                    shoot(window, "18-undo-ready", null);
                    work.Flush(chat);
                    window.Close();
                }
            }
            // The edited chat with its undo still waiting, pictured at each size with Undo last edits lit.
            foreach (float z in new[] { 1f, 1.25f, 1.5f })
            {
                int scale = (int)Math.Round(z * 100);
                using (Workspace work = new Workspace(data, pipe + Guid.NewGuid().ToString("N")) { Home = Path.Combine(box, "her-folder") })
                {
                    work.Executable = standin;
                    WorkspaceChat chat = work.Chats().Find(delegate(WorkspaceChat found) { return found.Id == chatId; });
                    if (chat == null) throw new Exception("The edited chat didn't come back to be pictured.");
                    work.Trust(chat, true);
                    chat.TrustAsked = true;
                    using (WorkspaceWindow window = new WorkspaceWindow(work, delegate { return false; }, z, false))
                    {
                        window.PathAlias = alias;
                        window.Location = new Point(z == 1f ? 40 : 0, z == 1f ? 40 : 0);
                        window.Show(this);
                        window.ClientSize = new Size((int)Math.Round(WorkspaceWindow.Wide * z), Math.Min((int)Math.Round(WorkspaceWindow.Tall * z), 1032));
                        window.ShowChat(chat);
                        WorkspaceWait(delegate { Application.DoEvents(); return window.UndoOffered; }, "Undo last edits wasn't lit for the edited chat at " + scale + "%.");
                        window.ToggleTool("toolu_edit");
                        window.ScrollToEnd();
                        shoot(window, "26-undo-ready-" + scale, null);
                        window.Close();
                    }
                }
            }
            using (Workspace work = new Workspace(data, pipe + Guid.NewGuid().ToString("N")) { Home = Path.Combine(box, "her-folder") })
            {
                work.Executable = standin;
                WorkspaceChat chat = work.Chats().Find(delegate(WorkspaceChat found) { return found.Id == chatId; });
                if (chat == null) throw new Exception("The edited chat didn't come back after a restart.");
                work.Trust(chat, true);
                chat.TrustAsked = true;
                using (WorkspaceWindow window = new WorkspaceWindow(work, delegate { return false; }, 1f, false))
                {
                    window.PathAlias = alias;
                    window.Location = new Point(40, 40);
                    window.Show(this);
                    window.ClientSize = new Size(1080, 740);
                    window.ShowChat(chat);
                    Application.DoEvents();
                    if (!diffShown(window)) throw new Exception("The Edit card's diff didn't survive a restart: " + string.Join(" / ", window.ToolWell("toolu_edit").ToArray()));
                    if (window.UndoOffered != work.CanUndo(chat) || !window.UndoOffered) throw new Exception("After a restart Undo last edits didn't match the engine's answer.");
                    window.Find("Undo last edits").PerformClick();
                    WorkspaceWait(delegate { Application.DoEvents(); return !window.UndoOffered; }, "Undo last edits stayed on after the undo.");
                    if (work.CanUndo(chat) || File.ReadAllText(file) != before) throw new Exception("The undo didn't put the file back, or the engine still had something to undo.");
                    window.ScrollToEnd();
                    shoot(window, "19-undo-done", null);

                    StringBuilder big = new StringBuilder();
                    for (int i = 0; i < 5000; i++) big.Append("line ").Append(i).Append(" of a very large file that Claude Code writes in one go\n");
                    string bigFile = Path.Combine(project, "big.txt");
                    turnOf(window, work, chat, "Write the big file.", "Write", HuskJson.Map("file_path", bigFile, "content", big.ToString()), bigFile, big.ToString());
                    WorkspaceItem written = WorkspaceItems(chat).Find(delegate(WorkspaceItem item) { return item.Kind == "tool" && item.Name == "Write"; });
                    if (written == null || written.Input.Length == 0 || written.Input.Length > Workspace.ShownAll + 1000) throw new Exception("The 5,000-line Write kept " + (written == null ? 0 : written.Input.Length) + " letters for its card.");
                    work.Flush(chat);
                    long saved = new FileInfo(Path.Combine(work.Root, "chats", chat.Id + ".json")).Length;
                    if (saved > 150000) throw new Exception("The saved chat grew to " + saved + " bytes with a 5,000-line Write.");
                    Stopwatch clock = Stopwatch.StartNew();
                    window.RefreshChat();
                    window.ToggleTool("toolu_write");
                    window.Refresh();
                    long took = clock.ElapsedMilliseconds;
                    List<string> rows = window.ToolWell("toolu_write");
                    if (took > 1000 || rows.Count == 0 || rows.Count > 14 || !rows[0].StartsWith("G+ line 0", StringComparison.Ordinal)) throw new Exception("The 5,000-line Write card took " + took + " ms or showed " + rows.Count + " rows.");
                    WorkspaceItem old = WorkspaceItem.Read(HuskJson.Map("kind", "tool", "text", "Edit " + file, "name", "Edit", "id", "o", "turn", "x"));
                    if (old.Input.Length != 0) throw new Exception("An old saved tool card read back an input it never had.");
                    checks.Add("PASS Workspace Edit cards show the removed and added lines from Claude Code's own tool input, through a stand-in claude.cmd stream, and keep them across a restart; Undo last edits is on exactly when the engine can undo, after the edit and after a restart, and off after the undo; a 5,000-line Write keeps " + written.Input.Length + " letters for its card (cap " + Workspace.ShownLines + " lines and " + Workspace.ShownChars + " letters a text, " + Workspace.ShownAll + " in all), saves a " + saved + "-byte chat and lays out in " + took + " ms; old saved cards without an input still load");
                    window.Close();
                }
            }
        }
    }

    internal sealed partial class Companion
    {
        private static string WorkspaceStandin(string folder, string session)
        {
            string standin = Path.Combine(folder, "claude.cmd");
            File.WriteAllText(Path.Combine(folder, "result.txt"), HuskJson.Write(HuskJson.Map("type", "result", "is_error", false, "session_id", session, "duration_ms", 40, "result", "Done.")) + "\n", new UTF8Encoding(false));
            File.WriteAllText(standin, "@echo off\r\nsetlocal\r\nif \"%~1\"==\"--version\" (\r\necho 2.1.295 ^(Claude Code^)\r\nexit /b 0\r\n)\r\necho %* > \"%~dp0args.txt\"\r\nfindstr \"^\" > \"%~dp0stdin.txt\"\r\ntype \"%~dp0result.txt\"\r\n", Encoding.ASCII);
            return standin;
        }

        private static WorkspaceTurn WorkspaceSent(Workspace work, WorkspaceChat chat, string text)
        {
            WorkspaceTurn turn;
            lock (chat.Gate) { if (!work.Send(chat, text, false)) throw new Exception("Workspace didn't take a message."); turn = chat.Live; }
            if (turn != null && !turn.Done.WaitOne(8000)) throw new Exception("Workspace's stand-in turn didn't end.");
            return turn;
        }

        // Folders after the first ride to Claude Code with --add-dir, each new one asks for trust, an untrusted one keeps the
        // safe flags, and added files go out once with the next message and count as inside for reads and edits.
        private void SmokeWorkspaceFolders(List<string> checks, string folder)
        {
            folder = Path.Combine(folder, Guid.NewGuid().ToString("N"));
            string main = Path.Combine(folder, "main project"), extra = Path.Combine(folder, "extra"), later = Path.Combine(folder, "added later"), loose = Path.Combine(folder, "loose");
            foreach (string made in new[] { main, extra, later, loose }) Directory.CreateDirectory(made);
            string spec = Path.Combine(loose, "spec.md"), log = Path.Combine(loose, "build log.txt"), stranger = Path.Combine(loose, "not added.txt");
            File.WriteAllText(spec, "spec"); File.WriteAllText(log, "log"); File.WriteAllText(stranger, "no");
            string session = Guid.NewGuid().ToString();
            List<string> args = Workspace.Arguments("Ask me", "", "Default", "", false, new List<string> { extra, later });
            string joined = string.Join("|", args.ToArray());
            if (!joined.Contains("|--add-dir|" + extra + "|--add-dir|" + later + "|--setting-sources|user|--strict-mcp-config")) throw new Exception("Workspace's --add-dir arguments are wrong: " + joined);
            if (string.Join("|", Workspace.Arguments("Ask me", "", "Default", "", true, null).ToArray()) != string.Join("|", Workspace.Arguments("Ask me", "", "Default", "", true).ToArray())) throw new Exception("A one-folder chat's arguments changed.");
            using (Workspace work = new Workspace(folder, "CapriWorkspace-" + Process.GetCurrentProcess().Id + "-folders-" + Guid.NewGuid().ToString("N")) { Home = Path.Combine(folder, "her-folder") })
            {
                work.Executable = WorkspaceStandin(folder, session);
                work.ApprovalWait = 100;
                WorkspaceChat chat = work.NewChat(main, false);
                if (work.AddFolders(chat, new[] { extra, extra, main }) != 1 || chat.Folders.Count != 1) throw new Exception("Workspace added a folder twice or added the main folder again.");
                if (work.AddFiles(chat, new[] { spec, log, spec }) != 2) throw new Exception("Workspace added a file twice.");
                bool missing = false;
                try { work.AddFolders(chat, new[] { Path.Combine(folder, "nowhere") }); } catch (IOException) { missing = true; }
                if (!missing) throw new Exception("Workspace added a folder that isn't there.");
                if (!work.Send(chat, "First", false) || chat.Running || chat.TrustItem == null || !chat.TrustItem.Text.Contains(main) || !chat.TrustItem.Text.Contains(extra) || !chat.TrustItem.Text.StartsWith("Claude Code will run these folders'", StringComparison.Ordinal)) throw new Exception("Workspace ran two new folders before one trust card for both.");
                WorkspaceTurn first;
                lock (chat.Gate) { work.Trust(chat, true); first = chat.Live; }
                if (first == null || !first.Done.WaitOne(8000)) throw new Exception("Workspace didn't run after trust.");
                string firstArgs = File.ReadAllText(Path.Combine(folder, "args.txt")), firstIn = File.ReadAllText(Path.Combine(folder, "stdin.txt"));
                if (!work.Trusted(main) || !work.Trusted(extra) || !firstArgs.Contains("--add-dir") || !firstArgs.Contains(extra) || firstArgs.Contains("--setting-sources")) throw new Exception("Workspace didn't trust both folders or pass the second with --add-dir: " + firstArgs);
                Dictionary<string, object> sent = HuskJson.Read(firstIn.Trim()) as Dictionary<string, object>;
                string said = HuskJson.Text(HuskJson.List(HuskJson.Get(HuskJson.Get(sent, "message") as Dictionary<string, object>, "content"))[0] as Dictionary<string, object>, "text");
                if (said != "First\n\nAttached files:\n- " + spec + "\n- " + log) throw new Exception("Workspace's files didn't ride along with the message: " + said);
                WorkspaceItem asked = WorkspaceItems(chat).Find(delegate(WorkspaceItem item) { return item.Kind == "user"; });
                if (asked.Text != "First" || asked.Files != spec + "\n" + log || !chat.Sent.Contains(spec) || !chat.Sent.Contains(log)) throw new Exception("Workspace didn't keep which files went with the message.");
                WorkspaceSent(work, chat, "Second");
                said = HuskJson.Text(HuskJson.List(HuskJson.Get(HuskJson.Get(HuskJson.Read(File.ReadAllText(Path.Combine(folder, "stdin.txt")).Trim()) as Dictionary<string, object>, "message") as Dictionary<string, object>, "content"))[0] as Dictionary<string, object>, "text");
                if (said != "Second") throw new Exception("Workspace sent the same files twice.");
                checks.Add("PASS Workspace folders: one trust card for every new folder, --add-dir for each folder after the first, and files that ride along once with the next message by full path");

                work.AddFolders(chat, new[] { later });
                if (chat.TrustAsked || !work.Send(chat, "Third", false) || chat.Running || chat.TrustItem == null || !chat.TrustItem.Text.Contains(later) || chat.TrustItem.Text.Contains(extra)) throw new Exception("Workspace didn't ask about a folder added later.");
                WorkspaceTurn limited;
                lock (chat.Gate) { work.Trust(chat, false); limited = chat.Live; }
                if (limited == null || !limited.Done.WaitOne(8000) || limited.Trusted) throw new Exception("Workspace didn't run with an untrusted added folder.");
                string limitedArgs = File.ReadAllText(Path.Combine(folder, "args.txt"));
                if (!limitedArgs.Contains("--setting-sources") || !limitedArgs.Contains("--strict-mcp-config") || !limitedArgs.Contains(later)) throw new Exception("Workspace loaded an untrusted added folder's settings: " + limitedArgs);
                checks.Add("PASS Workspace asks again for a folder added later, and runs with user settings and no MCP servers while it isn't trusted");

                List<string> folders = Workspace.AllFolders(chat), files = Workspace.AllFiles(chat);
                Func<string, Dictionary<string, object>, bool> inside = delegate(string tool, Dictionary<string, object> input) { return Workspace.Inside(folders, files, tool, input, main); };
                if (!inside("Read", HuskJson.Map("file_path", spec)) || !inside("Edit", HuskJson.Map("file_path", log)) || !inside("Read", HuskJson.Map("file_path", Path.Combine(later, "x.txt"))) || !inside("Glob", HuskJson.Map("pattern", Path.Combine(extra, "*.cs"))))
                    throw new Exception("Workspace treated an added folder or file as outside.");
                if (inside("Read", HuskJson.Map("file_path", stranger)) || inside("Glob", HuskJson.Map("pattern", Path.Combine(loose, "*.md"))) || inside("Read", HuskJson.Map("file_path", Path.Combine(folder, "outside.txt"))))
                    throw new Exception("Workspace treated a file beside an added file, or its folder, as inside.");
                WorkspaceTurn turn = new WorkspaceTurn { Chat = chat, Mode = "Ask me", Model = "Default" };
                chat.Live = turn;
                work.BeginUndo(chat, turn.Id);
                Func<string, Dictionary<string, object>, string> hook = delegate(string tool, Dictionary<string, object> input)
                {
                    return WorkspaceChoice(work.Permission(HuskJson.Map("chat", chat.Id, "turn", turn.Id, "tool_name", tool, "tool_input", input, "cwd", main)));
                };
                try
                {
                    if (hook("Read", HuskJson.Map("file_path", spec)) != "allow" || hook("Read", HuskJson.Map("file_path", stranger)) != "deny" || hook("Edit", HuskJson.Map("file_path", spec)) != "deny") throw new Exception("Workspace's Ask me rules for added files are wrong.");
                    turn.Mode = "Edits only";
                    if (hook("Edit", HuskJson.Map("file_path", log)) != "allow" || hook("Write", HuskJson.Map("file_path", Path.Combine(extra, "new.txt"))) != "allow" || hook("Edit", HuskJson.Map("file_path", stranger)) != "deny") throw new Exception("Workspace's Edits only rules for added folders and files are wrong.");
                    if (!work.Remove(chat, spec) || work.Remove(chat, main) || chat.Files.Contains(spec)) throw new Exception("Workspace removed the wrong thing.");
                    turn.Mode = "Ask me";
                    if (hook("Read", HuskJson.Map("file_path", spec)) != "deny") throw new Exception("Workspace kept a removed file inside.");
                }
                finally { chat.Live = null; }
                checks.Add("PASS Workspace hook rules treat added folders like the first and added files as exactly those files, in Ask me and Edits only, until they're removed");

                work.Flush(chat);
                using (Workspace loaded = new Workspace(folder, "CapriWorkspace-" + Process.GetCurrentProcess().Id + "-folders-load-" + Guid.NewGuid().ToString("N")) { Home = Path.Combine(folder, "her-folder") })
                {
                    WorkspaceChat kept = loaded.Chats().Find(delegate(WorkspaceChat found) { return found.Id == chat.Id; });
                    if (kept == null || kept.Folders.Count != 2 || kept.Folders[0] != extra || kept.Folders[1] != later || kept.Files.Count != 1 || kept.Files[0] != log || !kept.Sent.Contains(log)) throw new Exception("Workspace didn't keep a chat's folders and files.");
                    WorkspaceItem keptAsk = kept.Items.Find(delegate(WorkspaceItem item) { return item.Kind == "user"; });
                    if (keptAsk == null || keptAsk.Files != spec + "\n" + log) throw new Exception("Workspace lost which files went with a message.");
                }
                checks.Add("PASS Workspace keeps each chat's folders, files and which files went out, across a restart");
            }
            SmokeWorkspaceArguments(checks, folder);
            SmokeWorkspaceInside(checks, folder);
        }

        // The window itself, at 100%, 125%, 150% and 200% and at its smallest size: nothing crowded, code in her mono,
        // copyable messages, tool results that open, pickers that save, Everything at once, a draft per chat, following
        // only at the end, Enter and Shift+Enter, the Explorer flow for folders and files, paging and its place.
        private void SmokeWorkspaceWindow(List<string> checks, string folder)
        {
            folder = Path.Combine(folder, Guid.NewGuid().ToString("N"));
            string project = Path.Combine(folder, "project"), second = Path.Combine(folder, "second"), third = Path.Combine(folder, "third");
            foreach (string made in new[] { project, second, third }) Directory.CreateDirectory(made);
            string fileOne = Path.Combine(second, "one.txt"), fileTwo = Path.Combine(third, "two.txt");
            File.WriteAllText(fileOne, "1"); File.WriteAllText(fileTwo, "2");
            using (Workspace work = new Workspace(folder, "CapriWorkspace-" + Process.GetCurrentProcess().Id + "-window-" + Guid.NewGuid().ToString("N")) { Home = Path.Combine(folder, "her-folder") })
            {
                work.Executable = WorkspaceStandin(folder, Guid.NewGuid().ToString());
                using (WorkspaceWindow window = new WorkspaceWindow(work, delegate { return false; }, 1f, false))
                {
                    window.Show(this);
                    if (window.Chat == null || !work.IsHome(window.Chat.Folder) || window.Crowded() != null) throw new Exception("The empty Workspace didn't open into a chat in her folder: " + window.Crowded());
                    window.PickFolders = delegate { return new[] { project, second }; };
                    window.ChooseFolders();
                    WorkspaceChat made = window.Chat;
                    if (made == null || made.Folder != project || made.Folders.Count != 1 || made.Folders[0] != second || made.Title != "project") throw new Exception("Choose folder didn't start a chat in the first folder with the second added.");
                    window.PickFiles = delegate { return new[] { fileOne, fileTwo }; };
                    window.ChooseFiles();
                    window.AddPaths(new[] { third }, new string[0]);
                    List<string> chips = window.Chips;
                    if (chips.Count != 5 || chips[0] != "folder project (runs here)" || chips[1] != "folder second" || chips[2] != "folder third" || chips[3] != "file one.txt" || chips[4] != "file two.txt") throw new Exception("Workspace chips are wrong: " + string.Join(", ", chips.ToArray()));
                    window.ClickChipCross(second);
                    window.ClickChipCross(project);
                    if (made.Folders.Contains(second) || made.Folder != project || window.Chips.Count != 4) throw new Exception("A chip's cross removed the wrong thing.");
                    ToolStripDropDown plus = window.OpenMenu("add");
                    bool both = plus.Items.Count == 2 && plus.Items[0].Text == "Add folders..." && plus.Items[1].Text == "Add files..." && plus.Items[1].Enabled;
                    plus.Close();
                    if (!both) throw new Exception("The + menu lost Add folders or Add files.");
                    checks.Add("PASS Workspace Choose folder starts a chat in the first picked folder, + adds folders and files as chips, drops add folders, and a chip's cross removes it but never the folder Claude Code runs in");
                    window.Close();
                }

                WorkspaceChat layout = work.NewChat(project, false);
                work.Trust(layout, true);
                WorkspaceChat other = work.NewChat(second, false);
                string code = "int total = 3;";
                WorkspaceItem said = new WorkspaceItem { Kind = "text", Text = "I found the file.\n```cs\n" + code + "\n```\nHere is what changed." };
                work.Add(layout, new WorkspaceItem { Kind = "user", Text = "Check this project." });
                work.Add(layout, said);
                work.Add(layout, new WorkspaceItem { Kind = "tool", Id = "layout-tool", Name = "Edit", Text = "Edit app/Capri.cs" });
                work.Add(layout, new WorkspaceItem { Kind = "result", Id = "layout-tool", Text = "Saved the edit." });
                WorkspaceItem approval = new WorkspaceItem { Kind = "approval", Name = "Bash", Text = "Bash: npm test", Input = HuskJson.Write(HuskJson.Map("command", "npm test", "description", "Run the tests")) };
                approval.Ask = new WorkspaceApproval { Item = approval };
                work.Add(layout, approval);
                work.Add(layout, new WorkspaceItem { Kind = "error", Text = Workspace.Missing, Retry = true });
                work.Add(layout, new WorkspaceItem { Kind = "end", Text = "1.3s | $0.02" });
                MethodInfo keyDown = typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic);
                foreach (int scale in new[] { 100, 125, 150, 200 })
                {
                    float z = scale / 100f;
                    using (WorkspaceWindow window = new WorkspaceWindow(work, delegate { return false; }, z, false))
                    {
                        window.Show(this);
                        window.ShowChat(layout);
                        foreach (Size size in new[] { new Size((int)Math.Round(WorkspaceWindow.Narrowest * z), (int)Math.Round(WorkspaceWindow.Shortest * z)), new Size((int)Math.Round(WorkspaceWindow.Wide * z), (int)Math.Round(WorkspaceWindow.Tall * z)) })
                        {
                            window.ClientSize = size;
                            window.ScrollToEnd();
                            string crowded = window.Crowded();
                            if (crowded != null) throw new Exception("Workspace at " + scale + "% and " + size.Width + " x " + size.Height + ": " + crowded);
                        }
                        Control mode = window.Named("Permission mode"), model = window.Named("Claude model");
                        if (mode == null || model == null || window.Input == null || window.Find("Allow") == null || window.Find("Deny") == null || window.Find("Retry") == null) throw new Exception("Workspace lost a control at " + scale + "%.");
                        using (Font her = Look.Mono(12, FontStyle.Regular)) if (window.CodeFont != her.Name) throw new Exception("Workspace code isn't in her mono font: " + window.CodeFont);
                        if (window.CopyText(said) != said.Text || window.ToggleTool("layout-tool") != "Saved the edit.") throw new Exception("Workspace didn't copy a message or open a tool result.");
                        window.Rename("Layout " + scale);
                        if (layout.Title != "Layout " + scale) throw new Exception("Workspace didn't save an edited title.");
                        string before = layout.Mode;
                        int confirmations = 0;
                        window.Confirm = delegate { confirmations++; return false; };
                        Native.SendMessage(mode.Handle, 0x020A, new IntPtr(120 << 16), IntPtr.Zero);
                        Native.SendMessage(mode.Handle, 0x0102, new IntPtr('E'), IntPtr.Zero);
                        if (layout.Mode != before) throw new Exception("Workspace changed mode on scroll or typing.");
                        window.PickMode("Everything");
                        if (layout.Mode != "Everything" || mode.Text != "Everything" || confirmations != 0) throw new Exception("Workspace asked before Everything, or didn't switch.");
                        window.Confirm = null;
                        window.PickMode("Ask me");
                        window.PickMode("Everything");
                        if (window.Asking != null || layout.Mode != "Everything") throw new Exception("Workspace's own card asked before Everything.");
                        window.PickMode("Plan only");
                        window.PickModel("Haiku");
                        if (layout.Mode != "Plan only" || layout.Model != "Haiku" || mode.Text != "Plan only" || model.Text != "Haiku") throw new Exception("Workspace's pickers didn't save or show their choice.");
                        window.Input.Text = "Keep this draft";
                        window.ShowChat(other);
                        window.ShowChat(layout);
                        if (window.Input.Text != "Keep this draft") throw new Exception("Workspace lost a draft while switching chats.");
                        window.ScrollConversation(0);
                        work.Add(layout, new WorkspaceItem { Kind = "text", Text = "Another update while reading above." });
                        window.RefreshChat();
                        if (window.Scrolled != 0) throw new Exception("Workspace followed new text while scrolled up.");
                        window.ScrollToEnd();
                        work.Add(layout, new WorkspaceItem { Kind = "text", Text = "An update at the end." });
                        window.RefreshChat();
                        if (!window.AtEnd) throw new Exception("Workspace stopped following the end.");
                        KeyEventArgs shifted = new KeyEventArgs(Keys.Enter | Keys.Shift);
                        keyDown.Invoke(window.Input, new object[] { shifted });
                        if (shifted.SuppressKeyPress || layout.Running || !window.Input.AcceptsReturn) throw new Exception("Workspace sent on Shift+Enter.");
                        window.Input.SelectionStart = window.Input.TextLength;
                        Native.SendMessage(window.Input.Handle, 0x0102, new IntPtr(13), IntPtr.Zero);
                        if (!window.Input.Text.Contains("\n")) throw new Exception("Workspace's message box didn't accept a new line.");
                        approval.Ask.Answered.Dispose();
                        approval.Ask = null;
                        KeyEventArgs enter = new KeyEventArgs(Keys.Enter);
                        WorkspaceTurn sentTurn;
                        lock (layout.Gate) { keyDown.Invoke(window.Input, new object[] { enter }); sentTurn = layout.Live; }
                        if (!enter.SuppressKeyPress || sentTurn == null || window.Input.Text.Length != 0 || !sentTurn.Done.WaitOne(8000)) throw new Exception("Workspace Enter didn't send and clear the message.");
                        approval.Ask = new WorkspaceApproval { Item = approval };
                        approval.Decision = "";
                        window.Close();
                    }
                    mainframe.LayoutScale(96, scale);
                    string keyFit = mainframe.WorkspaceKeyFits();
                    if (keyFit != null) throw new Exception(keyFit);
                }
                mainframe.LayoutScale(0, 0);
                approval.Ask.Answered.Dispose();
                approval.Ask = null;
                checks.Add("PASS Workspace window and Mainframe button fit at 100%, 125%, 150% and 200% and at the smallest size; code is in her mono, messages copy, tool results open, drafts and pickers save, scrolling follows only at the end, Enter sends and Shift+Enter adds a line");
                checks.Add("PASS Workspace ignores mode scrolling and typing, and switches to Everything at once with no question, like the Mainframe's Blackwall");

                WorkspaceChat longChat = work.NewChat(third, false);
                lock (longChat.Gate) for (int i = 0; i < 1000; i++) longChat.Items.Add(new WorkspaceItem { Kind = "text", Text = "Item " + i });
                work.Save(longChat);
                using (Process own = Process.GetCurrentProcess())
                {
                    int before = GetGuiResources(own.Handle, 1);
                    using (WorkspaceWindow window = new WorkspaceWindow(work, delegate { return false; }, 1f, false))
                    {
                        window.Show(this);
                        window.ShowChat(longChat);
                        if (window.ItemCount != 150 || GetGuiResources(own.Handle, 1) - before >= 600 || window.Find("Trust folder") == null) throw new Exception("Workspace's 1,000-item window used 600 USER objects, lost its trust key or showed more than 150 items.");
                        WorkKey older = window.Find("Show 150 earlier");
                        if (older == null) throw new Exception("Workspace didn't offer its earlier items.");
                        older.PerformClick();
                        WorkKey newer = window.Find("Show newer");
                        if (window.ItemCount != 150 || GetGuiResources(own.Handle, 1) - before >= 600 || newer == null) throw new Exception("Workspace's earlier page grew its window handles.");
                        newer.PerformClick();
                        if (window.ItemCount != 150 || GetGuiResources(own.Handle, 1) - before >= 600) throw new Exception("Workspace's newer page grew its window handles.");
                        int deleted = 0;
                        window.Confirm = delegate(string question) { if (question != "Delete this chat? Your files stay in the folder.") throw new Exception("Workspace changed its delete question."); deleted++; return true; };
                        window.DeleteChat(other);
                        if (deleted != 1 || work.Chats().Contains(other) || !Directory.Exists(second)) throw new Exception("Workspace's delete didn't ask, or touched the folder.");
                    }
                }
                checks.Add("PASS Workspace keeps 1,000 items below 600 USER objects, shows 150 at a time and pages both ways, and deletes a chat after asking while its folder stays");

                Rectangle place;
                using (WorkspaceWindow kept = new WorkspaceWindow(work, delegate { return false; }, 1f, true))
                {
                    kept.Show(this);
                    kept.ShowChat(layout);
                    kept.ClientSize = new Size(940, 640);
                    kept.Location = Screen.FromControl(this).WorkingArea.Location;
                    place = kept.Bounds;
                    kept.KeepPlace();
                    kept.Close();
                    if (kept.Visible) throw new Exception("Workspace close didn't hide the window.");
                }
                using (WorkspaceWindow kept = new WorkspaceWindow(work, delegate { return false; }, 1f, true))
                {
                    if (kept.Bounds != place) throw new Exception("Workspace forgot its size or place.");
                }
                checks.Add("PASS Workspace remembers its window's size and place when closed or kept before app shutdown");
            }
            SmokeWorkspaceFirstMinute(checks, folder);
            SmokeWorkspaceCost(checks, folder);
        }
    }

    // The smoke test's hands on a real Explorer picker: finding it among this process's windows, picturing it, typing into
    // its name box and pressing its keys, all by messages to that one window, so nothing reaches the screen.
    internal static class WorkspaceDialogs
    {
        private delegate bool Each(IntPtr window, IntPtr data);
        [StructLayout(LayoutKind.Sequential)] private struct Box { internal int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern bool EnumWindows(Each each, IntPtr data);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, Each each, IntPtr data);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
        [DllImport("user32.dll")] private static extern int GetDlgCtrlID(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wide, string text);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, int message, IntPtr wide, IntPtr data);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Box box);
        [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CommandLineToArgvW(string line, out int count);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);

        internal static string Class(IntPtr window)
        {
            StringBuilder name = new StringBuilder(256);
            GetClassName(window, name, name.Capacity);
            return name.ToString();
        }

        internal static string Text(IntPtr window)
        {
            StringBuilder text = new StringBuilder(1024);
            GetWindowText(window, text, text.Capacity);
            return text.ToString();
        }

        // This process's shown dialog windows, the kind Explorer's pickers are.
        internal static List<IntPtr> Open()
        {
            List<IntPtr> found = new List<IntPtr>();
            uint own = (uint)Process.GetCurrentProcess().Id;
            EnumWindows(delegate(IntPtr window, IntPtr data)
            {
                uint process;
                GetWindowThreadProcessId(window, out process);
                if (process == own && IsWindowVisible(window) && Class(window) == "#32770") found.Add(window);
                return true;
            }, IntPtr.Zero);
            return found;
        }

        internal static IntPtr Wait(int milliseconds)
        {
            Stopwatch clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < milliseconds)
            {
                List<IntPtr> open = Open();
                if (open.Count > 0) return open[0];
                System.Threading.Thread.Sleep(50);
            }
            return IntPtr.Zero;
        }

        internal static IntPtr Owner(IntPtr window) { return GetWindow(window, 4); }

        // The name box, where a name or a full path can be typed: the file picker's is the edit inside the ComboBoxEx32 with id
        // 1148 (cmb13), and the folder picker's "Folder:" box is an edit straight on the dialog.
        internal static IntPtr NameBox(IntPtr dialog)
        {
            IntPtr found = IntPtr.Zero, plain = IntPtr.Zero;
            EnumChildWindows(dialog, delegate(IntPtr child, IntPtr data)
            {
                if (Class(child) != "Edit" || !IsWindowVisible(child)) return true;
                if (GetParent(child) == dialog && plain == IntPtr.Zero) plain = child;
                for (IntPtr up = GetParent(child); up != IntPtr.Zero && up != dialog; up = GetParent(up))
                    if (GetDlgCtrlID(up) == 1148) { found = child; return false; }
                return true;
            }, IntPtr.Zero);
            return found != IntPtr.Zero ? found : plain;
        }

        internal static List<string> Edits(IntPtr dialog)
        {
            List<string> edits = new List<string>();
            EnumChildWindows(dialog, delegate(IntPtr child, IntPtr data)
            {
                if (Class(child) == "Edit") edits.Add(GetDlgCtrlID(child) + " in " + Class(GetParent(child)) + "/" + GetDlgCtrlID(GetParent(child)) + (IsWindowVisible(child) ? "" : " hidden"));
                return true;
            }, IntPtr.Zero);
            return edits;
        }

        internal static List<string> Keys(IntPtr dialog)
        {
            List<string> keys = new List<string>();
            EnumChildWindows(dialog, delegate(IntPtr child, IntPtr data) { if (Class(child) == "Button" && IsWindowVisible(child)) keys.Add(GetDlgCtrlID(child) + "=" + Text(child)); return true; }, IntPtr.Zero);
            return keys;
        }

        internal static void Type(IntPtr box, string text) { SendMessage(box, 0x000C, IntPtr.Zero, text); }

        // IDOK or IDCANCEL, as the dialog's own keys send them.
        internal static void Press(IntPtr dialog, int id) { PostMessage(dialog, 0x0111, new IntPtr(id), IntPtr.Zero); }

        internal static void Picture(IntPtr window, string path)
        {
            Box box;
            GetWindowRect(window, out box);
            int wide = Math.Max(1, box.Right - box.Left), tall = Math.Max(1, box.Bottom - box.Top);
            using (Bitmap shot = new Bitmap(wide, tall, PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(shot))
                {
                    graphics.Clear(Color.Magenta);
                    IntPtr dc = graphics.GetHdc();
                    try { PrintWindow(window, dc, 2); }
                    finally { graphics.ReleaseHdc(dc); }
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                shot.Save(path, ImageFormat.Png);
            }
        }

        // Splits a command line the way Windows programs, Claude Code among them, read their arguments.
        internal static List<string> Split(string line)
        {
            int count;
            IntPtr list = CommandLineToArgvW(line, out count);
            List<string> args = new List<string>();
            try { for (int i = 0; i < count; i++) args.Add(Marshal.PtrToStringUni(Marshal.ReadIntPtr(list, i * IntPtr.Size))); }
            finally { LocalFree(list); }
            return args;
        }
    }

    internal sealed partial class Companion
    {
        // A stand-in Claude Code that keeps exactly what it was started with, as the real claude.cmd passes it on with %*.
        private static string WorkspaceRawStandin(string folder, string session, bool wait)
        {
            string standin = Path.Combine(folder, "claude.cmd");
            File.WriteAllText(Path.Combine(folder, "result.txt"), HuskJson.Write(HuskJson.Map("type", "result", "is_error", false, "session_id", session, "duration_ms", 40, "result", "Done.")) + "\n", new UTF8Encoding(false));
            File.WriteAllText(standin, "@echo off\r\nsetlocal\r\nif \"%~1\"==\"--version\" (\r\necho 2.1.295 ^(Claude Code^)\r\necho version>>\"%~dp0version.txt\"\r\nexit /b 0\r\n)\r\n> \"%~dp0raw.txt\" echo %*\r\nfindstr \"^\" > \"%~dp0stdin.txt\"\r\n"
                + (wait ? ":wait\r\nif exist \"%~dp0finish\" goto done\r\nping -n 2 127.0.0.1 > nul\r\ngoto wait\r\n:done\r\n" : "") + "type \"%~dp0result.txt\"\r\n", Encoding.ASCII);
            return standin;
        }

        // The pictures' stand-in answers in a believable few seconds, at a believable cost, instead of the 40 ms it takes.
        // Tool calls behind summary lines, built from a real turn and a few more: one line per run of calls
        // between her words, closed until it's opened, worded by what finished, with a failed or denied call marked on the
        // line, and a running call's shine on its own clock that stops when nothing runs or the window is minimised. Pictured
        // at 100%, 125% and 150%; the t-c frames are the shine at four points of its pass.
        private void SmokeWorkspaceTools(List<string> checks, string folder, string projects, string pipe, Func<string, string> alias, Action<WorkspaceWindow, string, ToolStripDropDown> shoot)
        {
            string box = Path.Combine(folder, "tools"), project = Path.Combine(projects, "capri-desktop");
            Directory.CreateDirectory(box);
            Directory.CreateDirectory(project);
            string pictures = Path.Combine(folder, "look");
            // The photo's chat ran in a release copy; here that's a stand-in folder, so its paths read from the chat's folder.
            string release = Path.Combine(projects, "release-089"), app = Path.Combine(release, "app"), mods = Path.Combine(release, "mods");
            foreach (string made in new[] { app, mods }) Directory.CreateDirectory(made);
            string bash1 = "cd /c/Users/you/Projects/capri-desktop/app; sed -n 1,40p Workspace.cs | grep -v \"^using\"; sed -n 315,332p Workspace.cs; sed -n 1295,1330p Workspace.cs; grep -n \"localhost\\|127.0.0.1\" *.cs";
            string bash2 = "cd /c/Users/you/Projects/release-089/app; grep -n \"ModList.Load()\\|mods.Load()\\|ModList = \\|new Mods(\" *.cs | grep -v \"throw\\|Smoke\\|Check\" | head; grep -n \"folder = \\|\\\"mods\\\"\" Mods.cs";
            Action<WorkspaceChat, string, string, string, string, string, string> call = delegate(WorkspaceChat chat, string turn, string id, string name, string about, string result, string how)
            {
                string text = name == "Bash" || name == "PowerShell" ? name + ": " + about : name + " " + about;
                lock (chat.Gate)
                {
                    chat.Items.Add(new WorkspaceItem { Kind = "tool", Id = id, Name = name, Text = text, Turn = turn });
                    if (how == "allow") chat.Items.Add(new WorkspaceItem { Kind = "approval", Name = name, Text = "Allowed: " + text, Decision = "allow", Turn = turn });
                    if (how == "deny") chat.Items.Add(new WorkspaceItem { Kind = "approval", Name = name, Text = "Denied: Not that folder, use the copy in Projects.", Decision = "deny", Turn = turn });
                    if (result != null) chat.Items.Add(new WorkspaceItem { Kind = "result", Id = id, Text = result, Turn = turn, Decision = how == "fail" || how == "deny" ? "error" : "" });
                }
            };
            // His first message names the chat, as sending it would.
            Action<WorkspaceChat, string, string> say = delegate(WorkspaceChat chat, string kind, string text)
            {
                lock (chat.Gate)
                {
                    chat.Items.Add(new WorkspaceItem { Kind = kind, Text = text });
                    if (kind == "user" && chat.Title == new DirectoryInfo(chat.Folder).Name) chat.Title = Workspace.TitleFrom(text, null);
                }
            };
            // The parts of a summary line add up to its calls when no file is touched twice.
            Func<string, int> sum = delegate(string line)
            {
                int total = 0;
                foreach (string part in line.Split(new[] { ", ", " | " }, StringSplitOptions.None))
                {
                    int count;
                    if (int.TryParse(part.Split(' ')[0], out count)) total += count;
                }
                return total;
            };
            // The photo's turn, finished.
            Action<WorkspaceChat> photo = delegate(WorkspaceChat chat)
            {
                say(chat, "user", "Hey Capri, code me a Cyberpunk style clock mod that sits in the corner of the screen.");
                say(chat, "text", "I'll look at how mods load first, then write it.");
                call(chat, "p", "p1", "Bash", bash1, "  1 namespace Capri\n315     internal static string HomeFolder()", "allow");
                call(chat, "p", "p2", "Bash", bash2, "Capri.cs:2210:            ModList = new Mods(folder);", "allow");
                call(chat, "p", "p3", "Grep", app, "Found 6 files", "allow");
                call(chat, "p", "p4", "Grep", app + @"\ModApi.cs", "Found 1 file", "allow");
                call(chat, "p", "p5", "Read", mods + @"\hello-code.capri-code", "     1\t// Hello, from a code mod.", "allow");
                say(chat, "text", "Mods load from the mods folder through ModList.Load, and a code mod gets the ModApi to draw with.\n\nI'll write the clock as one now.");
                call(chat, "p", "p6", "Write", mods + @"\cyber-clock.capri-code", "File created successfully", "");
                call(chat, "p", "p7", "Edit", mods + @"\cyber-clock.capri-code", "The file has been updated.", "");
                call(chat, "p", "p8", "Bash", "powershell -File ci\\build-windows.ps1 -OutputDirectory out", "Built out\\Capri.exe", "");
                say(chat, "text", "Done. The clock sits in the top right corner in a neon frame, and it keeps to the time your PC shows.");
                lock (chat.Gate) chat.Items.Add(new WorkspaceItem { Kind = "end", Text = "48.2s | $0.31" });
            };
            List<string> failures = new List<string>();
            List<string> rows = new List<string>();
            foreach (float z in new[] { 1f, 1.25f, 1.5f })
            {
                int scale = (int)Math.Round(z * 100);
                using (Workspace work = new Workspace(Path.Combine(box, "data" + scale), pipe + Guid.NewGuid().ToString("N")) { Home = Path.Combine(box, "her-folder") })
                using (WorkspaceWindow window = new WorkspaceWindow(work, delegate { return false; }, z, false))
                {
                    window.PathAlias = alias;
                    window.Location = new Point(z == 1f ? 40 : 0, z == 1f ? 40 : 0);
                    window.Show(this);
                    Size full = new Size((int)Math.Round(WorkspaceWindow.Wide * z), Math.Min((int)Math.Round(WorkspaceWindow.Tall * z), 1032));
                    window.ClientSize = full;
                    // The first open's empty chat in her folder isn't part of these pictures.
                    if (window.Chat != null) work.Delete(window.Chat);

                    WorkspaceChat chat = work.NewChat(release, false);
                    work.Trust(chat, true);
                    chat.TrustAsked = true;
                    photo(chat);
                    window.ShowChat(chat);
                    window.RefreshChat();
                    window.ScrollToEnd();
                    List<string> groups = window.ToolGroups;
                    string[] wanted = { "2 commands ran, 1 file read, 2 searches", "1 command ran, 1 file edited, 1 file written" };
                    if (groups.Count != 2 || groups[0] != wanted[0] || groups[1] != wanted[1] || sum(groups[0]) != 5 || sum(groups[1]) != 3) failures.Add(scale + "%: the photo's groups read " + string.Join(" / ", groups.ToArray()));
                    Point tipAt;
                    string tip = window.TipOn(0, out tipAt);
                    if (window.PointerOn(0) != Cursors.Hand || tip == null || !tip.StartsWith("5 tool calls: Bash, Grep, Read", StringComparison.Ordinal)) failures.Add(scale + "%: a fold line's pointer or tooltip is wrong: " + tip);
                    TipPicture(window, tip, tipAt, Path.Combine(pictures, "t-tip-fold-" + scale + ".png"), false);
                    tip = window.TipOn(-1, out tipAt);
                    if (tip != "Mode and model changes apply to your next message.") failures.Add(scale + "%: the MODE key's tooltip is " + tip);
                    TipPicture(window, tip, tipAt, Path.Combine(pictures, "t-tip-mode-" + scale + ".png"), true);
                    if (window.Shining) failures.Add(scale + "%: the shine's clock ran with nothing running.");
                    shoot(window, "t-a-new-" + scale, null);
                    window.ToggleTool("p2");
                    window.ScrollToEnd();
                    window.RefreshChat();
                    shoot(window, "t-b-open-" + scale, null);
                    window.OpenToolGroups(false);

                    // A running turn: two calls done, a third running; the line says what's running and shines.
                    WorkspaceChat live = work.NewChat(release, false);
                    work.Trust(live, true);
                    live.TrustAsked = true;
                    WorkspaceTurn turn = new WorkspaceTurn { Chat = live };
                    say(live, "user", "Hey Capri, code me a Cyberpunk style clock mod that sits in the corner of the screen.");
                    say(live, "text", "I'll look at how mods load first, then write it.");
                    call(live, turn.Id, "r1", "Bash", bash2, "Capri.cs:2210:            ModList = new Mods(folder);", "allow");
                    call(live, turn.Id, "r2", "Grep", app, "Found 6 files", "allow");
                    call(live, turn.Id, "r3", "Read", mods + @"\hello-code.capri-code", null, "");
                    live.Live = turn;
                    window.ShowChat(live);
                    window.RefreshChat();
                    window.ScrollToEnd();
                    groups = window.ToolGroups;
                    if (groups.Count != 1 || groups[0] != "Reading hello-code.capri-code | 1 command ran, 1 search") failures.Add(scale + "%: the running line reads " + string.Join(" / ", groups.ToArray()));
                    if (!window.Shining) failures.Add(scale + "%: the shine's clock didn't start for a running call.");
                    double[] phases = { 0.2, 0.4, 0.6, 0.8 };
                    for (int k = 0; k < phases.Length; k++)
                    {
                        WorkspaceWindow.ShineAt = phases[k];
                        shoot(window, "t-c-frame" + (k + 1) + "-" + scale, null);
                    }
                    WorkspaceWindow.ShineAt = -1;
                    Rectangle area = window.ShineArea;
                    rows.Add("t-c-" + scale + " " + area.Left + " " + area.Top + " " + area.Width + " " + area.Height);
                    window.WindowState = FormWindowState.Minimized;
                    Application.DoEvents();
                    if (window.Shining) failures.Add(scale + "%: the shine kept running while the window was minimised.");
                    window.WindowState = FormWindowState.Normal;
                    Application.DoEvents();
                    window.ClientSize = full;
                    if (!window.Shining) failures.Add(scale + "%: the shine didn't come back after the window was restored.");
                    lock (live.Gate) live.Items.Add(new WorkspaceItem { Kind = "result", Id = "r3", Text = "     1\t// Hello, from a code mod.", Turn = turn.Id });
                    window.RefreshChat();
                    // The call finished and the turn goes on: Claude is thinking, and the Thinking line shines where the call did.
                    if (!window.Thinking || !window.Shining) failures.Add(scale + "%: no shining Thinking line while Claude thinks between calls.");
                    WorkspaceWindow.ShineAt = 0.45;
                    shoot(window, "v3-thinking-" + scale, null);
                    WorkspaceWindow.ShineAt = -1;
                    groups = window.ToolGroups;
                    if (groups.Count != 1 || groups[0] != "1 command ran, 1 file read, 1 search") failures.Add(scale + "%: the count didn't take the finished call: " + string.Join(" / ", groups.ToArray()));
                    live.Live = null;
                    window.RefreshChat();
                    if (window.Shining || window.Thinking) failures.Add(scale + "%: the shine or the Thinking line stayed after the turn ended.");

                    // A group with a failed call, a denied one and one he was asked about and allowed.
                    WorkspaceChat rough = work.NewChat(project, false);
                    work.Trust(rough, true);
                    rough.TrustAsked = true;
                    say(rough, "user", "Run the tests and fix what breaks.");
                    say(rough, "text", "Running the tests first.");
                    call(rough, "d", "d1", "Bash", "npm test -- --runInBand", "FAIL tests/clock.test.js\n  expected \"23:59\" but got \"11:59 PM\"\nexit code 1", "fail");
                    call(rough, "d", "d2", "Read", @"C:\Users\you\Documents\private\notes.txt", "Denied: Not that folder, use the copy in Projects.", "deny");
                    call(rough, "d", "d3", "Edit", Path.Combine(project, "mods", "cyber-clock.capri-code"), "The file has been updated.", "allow");
                    say(rough, "text", "One test failed on the 24-hour format. I fixed the clock to use your PC's own format.");
                    window.ShowChat(rough);
                    window.RefreshChat();
                    window.ScrollToEnd();
                    groups = window.ToolGroups;
                    if (groups.Count != 1 || groups[0] != "1 file edited | 1 command failed, 1 read denied" || sum(groups[0]) != 3) failures.Add(scale + "%: the rough group reads " + string.Join(" / ", groups.ToArray()));
                    shoot(window, "t-d-closed-" + scale, null);
                    window.ToggleTool("d1");
                    window.ScrollToEnd();
                    window.RefreshChat();
                    shoot(window, "t-d-open-" + scale, null);

                    // A long turn: six groups with her words between them.
                    WorkspaceChat longer = work.NewChat(project, false);
                    work.Trust(longer, true);
                    longer.TrustAsked = true;
                    say(longer, "user", "Tidy the mod loader, check the docs online and update the issue.");
                    string[][] runs =
                    {
                        new[] { "Grep|" + app, "Grep|" + app + @"\Mods.cs", "Glob|mods/**/*.capri-code" },
                        new[] { "Read|" + app + @"\Mods.cs", "Read|" + app + @"\ModApi.cs", "Read|" + app + @"\CodeMods.cs", "Read|" + app + @"\Mods.cs" },
                        new[] { "WebFetch|https://learn.microsoft.com/dotnet/api/system.io.filesystemwatcher", "WebSearch|FileSystemWatcher buffer overflow" },
                        new[] { "Edit|" + app + @"\Mods.cs", "Edit|" + app + @"\Mods.cs", "Edit|" + app + @"\CodeMods.cs", "Bash|powershell -File ci\\build-windows.ps1 -OutputDirectory out" },
                        new[] { "TodoWrite|3 to-dos", "Task|Review the loader change" },
                        new[] { "mcp__github__add_issue_comment|lachydotmcg/capri#214", "mcp__github__update_issue|lachydotmcg/capri#214" }
                    };
                    string[] between = { "The loader reads every mod twice on start.", "It reads the folder once for the list and again for each mod.", "The watcher can drop events when many files change at once.", "Fixed: one read, and a rescan when the watcher overflows.", "I've noted what's left.", "The issue is updated with what changed." };
                    int n = 0;
                    for (int g = 0; g < runs.Length; g++)
                    {
                        foreach (string one in runs[g]) { string[] bits = one.Split('|'); n++; call(longer, "l", "l" + n, bits[0], bits[1], "ok", ""); }
                        say(longer, "text", between[g]);
                    }
                    lock (longer.Gate) longer.Items.Add(new WorkspaceItem { Kind = "end", Text = "3m 12s | $1.04" });
                    window.ShowChat(longer);
                    window.RefreshChat();
                    window.ScrollToEnd();
                    groups = window.ToolGroups;
                    string[] longWanted = { "3 searches", "3 files read", "2 web lookups", "1 command ran, 2 files edited", "to-do list updated, 1 agent ran", "2 GitHub calls" };
                    if (string.Join(" / ", groups.ToArray()) != string.Join(" / ", longWanted)) failures.Add(scale + "%: the long turn reads " + string.Join(" / ", groups.ToArray()));
                    shoot(window, "t-e-long-" + scale, null);
                    // Opening a group in the middle of the conversation leaves the conversation where it was.
                    window.ConversationTop = window.FoldTop(2) - (int)Math.Round(150 * z);
                    int before = window.ConversationTop;
                    window.ClickFold(2);
                    if (window.ConversationTop != before) failures.Add(scale + "%: opening a group moved the conversation from " + before + " to " + window.ConversationTop);
                    window.ClickFold(2);

                    // Forty calls in one group, and one command 2,000 letters long.
                    WorkspaceChat forty = work.NewChat(project, false);
                    work.Trust(forty, true);
                    forty.TrustAsked = true;
                    say(forty, "user", "Check every mod still builds.");
                    for (int i = 0; i < 40; i++) call(forty, "f", "f" + i, "Bash", "powershell -File tools\\build-mod.ps1 -Mod mod" + i, "ok", "");
                    call(forty, "f", "f-long", "Bash", "echo " + new string('x', 1995), "ok", "");
                    say(forty, "text", "They all build.");
                    window.ShowChat(forty);
                    window.RefreshChat();
                    groups = window.ToolGroups;
                    if (groups.Count != 1 || groups[0] != "41 commands ran") failures.Add(scale + "%: forty-one commands read " + string.Join(" / ", groups.ToArray()));
                    Stopwatch opening = Stopwatch.StartNew();
                    window.OpenToolGroups(true);
                    window.ToggleTool("f-long");
                    window.Refresh();
                    long took = opening.ElapsedMilliseconds;
                    string crowded = window.Crowded();
                    if (took > 600 || crowded != null) failures.Add(scale + "%: opening 41 calls with a 2,000-letter command took " + took + " ms: " + crowded);
                    window.OpenToolGroups(false);

                    // The narrowest window, with the photo's turn.
                    window.ShowChat(chat);
                    window.ClientSize = new Size((int)Math.Round(WorkspaceWindow.Narrowest * z), (int)Math.Round(WorkspaceWindow.Shortest * z));
                    window.RefreshChat();
                    window.ScrollToEnd();
                    shoot(window, "t-f-narrow-" + scale, null);
                    window.ClientSize = full;

                    // His snake video: a Grep card at the end of a long turn reads as one plain sentence with its raw input behind a
                    // link; answering it leaves what he was reading where it was; the time and cost read to the cent.
                    WorkspaceChat snake = work.NewChat(release, false);
                    work.Trust(snake, true);
                    snake.TrustAsked = true;
                    WorkspaceTurn snakeTurn = new WorkspaceTurn { Chat = snake };
                    say(snake, "user", "Hey Capri, code me a Cyberpunk style Snake Game connected to Capri-desktop that I can pull up and play without having to restart Capri.");
                    WorkspaceItem reading = null;
                    for (int k = 0; k < 4; k++)
                    {
                        say(snake, "text", k == 3 ? "Found it: Capri loads code mods live via Mainframe \u2192 Mods \u2192 Import. Checking permission rules, then writing the game." : "Looking at how code mods load, step " + (k + 1) + ".");
                        if (k == 3) lock (snake.Gate) reading = snake.Items[snake.Items.Count - 1];
                        call(snake, snakeTurn.Id, "v" + k + "a", "Grep", app, "Found 3 files", "allow");
                        call(snake, snakeTurn.Id, "v" + k + "b", "Read", app + @"\ModApi.cs", "     1\t// The API code mods draw with.", "allow");
                    }
                    string grepInput = HuskJson.Write(HuskJson.Map("pattern", "permission|Store|AddMenuItem|menu", "path", mods + @"\MODDING.md", "output_mode", "content", "-i", true));
                    WorkspaceItem grepCall = new WorkspaceItem { Kind = "tool", Id = "v9", Name = "Grep", Text = "Grep " + mods + @"\MODDING.md", Input = grepInput, Turn = snakeTurn.Id };
                    WorkspaceItem grepAsk = new WorkspaceItem { Kind = "approval", Name = "Grep", Text = "Grep: " + mods + @"\MODDING.md", Input = grepInput, Turn = snakeTurn.Id };
                    grepAsk.Ask = new WorkspaceApproval { Item = grepAsk };
                    lock (snake.Gate) { snake.Items.Add(grepCall); snake.Items.Add(grepAsk); }
                    snake.Live = snakeTurn;
                    // The Grep's line shines while the card waits; its pass is held still so the pictures don't catch it mid-frame.
                    WorkspaceWindow.ShineAt = 0.45;
                    window.ShowChat(snake);
                    window.RefreshChat();
                    window.ScrollToEnd();
                    Point details;
                    List<string> card = window.CardWords(out details);
                    string plain = "Search mods\\MODDING.md for \u201cpermission|Store|AddMenuItem|menu\u201d";
                    if (!card.Contains(plain) || !card.Contains("Show the details") || details == Point.Empty || card.Exists(delegate(string words) { return words.StartsWith("Claude Code wants", StringComparison.Ordinal); }))
                        failures.Add(scale + "%: the Grep card reads " + string.Join(" / ", card.ToArray()));
                    shoot(window, "v5-card-" + scale, null);
                    window.PressTranscript(details);
                    card = window.CardWords(out details);
                    if (!card.Contains("Hide the details")) failures.Add(scale + "%: the card's details didn't open: " + string.Join(" / ", card.ToArray()));
                    window.ScrollToEnd();
                    shoot(window, "v5-card-open-" + scale, null);
                    window.PressTranscript(details);
                    window.ScrollToEnd();
                    shoot(window, "v2-before-allow-" + scale, null);
                    int readTop = window.TopOf(reading);
                    lock (snake.Gate)
                    {
                        grepAsk.Decision = "allow";
                        grepAsk.Text = "Allowed: Grep: " + mods + @"\MODDING.md";
                        grepAsk.Ask.Answered.Dispose();
                        grepAsk.Ask = null;
                    }
                    window.RefreshChat();
                    if (readTop == int.MinValue || window.TopOf(reading) != readTop) failures.Add(scale + "%: answering the card moved what he was reading from " + readTop + " to " + window.TopOf(reading));
                    shoot(window, "v2-after-allow-" + scale, null);
                    lock (snake.Gate) snake.Items.Add(new WorkspaceItem { Kind = "result", Id = "v9", Text = "MODDING.md:12: Import a mod from Mainframe \u2192 Mods.", Turn = snakeTurn.Id });
                    window.RefreshChat();
                    if (!window.Thinking || window.TopOf(reading) != readTop) failures.Add(scale + "%: after the result, Thinking is " + window.Thinking + " and what he was reading sits at " + window.TopOf(reading));
                    // The turn ends with a summary while he's at the end: the view follows it, as Metis does.
                    say(snake, "text", "Cyber Snake is built, compiled and tested.\n\nTo play it:\n1. Open Mainframe \u2192 Mods \u2192 Import mod.\n2. Pick cyber-snake.capri-code.\n3. Say \"play snake\".");
                    lock (snake.Gate) snake.Items.Add(new WorkspaceItem { Kind = "end", Text = "1174.7s | $1.8235778" });
                    snake.Live = null;
                    window.RefreshChat();
                    window.ScrollToEnd();
                    string ended = WorkspaceWindow.EndText("1174.7s | $1.8235778");
                    if (ended != "19m 35s  \u00b7  $1.82" || Workspace.Ended(1174.7, "1.8235778") != "19m 35s | $1.82" || Workspace.Ended(4.2, "0.04") != "4.2s | $0.04" || Workspace.Money("0.0004") != "under $0.01")
                        failures.Add(scale + "%: the time and cost line reads " + ended);
                    if (window.Thinking || window.Shining) failures.Add(scale + "%: Thinking stayed after the snake turn ended.");
                    shoot(window, "v4-cost-" + scale, null);
                    WorkspaceWindow.ShineAt = -1;
                    window.Close();
                }
            }
            File.WriteAllLines(Path.Combine(pictures, "t-c-rows.txt"), rows.ToArray());
            // Every picture above came from these sections; the 41-call check is in the failures too.
            if (failures.Count > 0) throw new Exception(string.Join(" | ", failures.ToArray()));
            checks.Add("PASS Workspace puts each run of tool calls behind one closed line worded by what finished (commands ran, files read, edited and written, searches, web lookups, agents, MCP calls), marks a failed or denied call on that line and names what's running while it shines; the shine's clock runs only while a running call is seen and stops when it finishes or the window is minimised");
        }

        // The user's open and save dialog history (HKCU ...\Explorer\ComDlg32: the last folder per program, and recent files
        // per file type), read before a smoke opens real dialogs and put back after: values the run added go, values it changed
        // get their old contents back. Nothing outside that key is touched.
        private sealed class DialogMemory
        {
            private const string Root = @"Software\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32";
            private readonly Dictionary<string, KeyValuePair<object, Microsoft.Win32.RegistryValueKind>> kept = Read();

            private static Dictionary<string, KeyValuePair<object, Microsoft.Win32.RegistryValueKind>> Read()
            {
                Dictionary<string, KeyValuePair<object, Microsoft.Win32.RegistryValueKind>> values = new Dictionary<string, KeyValuePair<object, Microsoft.Win32.RegistryValueKind>>();
                try { Walk(Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Root), "", values); } catch (Exception) { }
                return values;
            }

            private static void Walk(Microsoft.Win32.RegistryKey key, string path, Dictionary<string, KeyValuePair<object, Microsoft.Win32.RegistryValueKind>> into)
            {
                if (key == null) return;
                using (key)
                {
                    foreach (string name in key.GetValueNames())
                        into[path + "\u0001" + name] = new KeyValuePair<object, Microsoft.Win32.RegistryValueKind>(key.GetValue(name, null, Microsoft.Win32.RegistryValueOptions.DoNotExpandEnvironmentNames), key.GetValueKind(name));
                    foreach (string sub in key.GetSubKeyNames()) Walk(key.OpenSubKey(sub), path.Length == 0 ? sub : path + "\\" + sub, into);
                }
            }

            private static bool Same(object a, object b)
            {
                byte[] x = a as byte[], y = b as byte[];
                if (x != null && y != null)
                {
                    if (x.Length != y.Length) return false;
                    for (int i = 0; i < x.Length; i++) if (x[i] != y[i]) return false;
                    return true;
                }
                string[] p = a as string[], q = b as string[];
                if (p != null && q != null) return string.Join("\u0000", p) == string.Join("\u0000", q);
                return Equals(a, b);
            }

            // How many values were put back, or -1 when the key couldn't be written.
            internal int Restore()
            {
                try
                {
                    int put = 0;
                    Dictionary<string, KeyValuePair<object, Microsoft.Win32.RegistryValueKind>> now = Read();
                    foreach (KeyValuePair<string, KeyValuePair<object, Microsoft.Win32.RegistryValueKind>> value in now)
                    {
                        KeyValuePair<object, Microsoft.Win32.RegistryValueKind> was;
                        if (kept.TryGetValue(value.Key, out was) && Same(was.Key, value.Value.Key)) continue;
                        string[] at = value.Key.Split('\u0001');
                        using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(at[0].Length == 0 ? Root : Root + "\\" + at[0], true))
                        {
                            if (key == null) continue;
                            if (kept.ContainsKey(value.Key)) key.SetValue(at[1], was.Key, was.Value);
                            else key.DeleteValue(at[1], false);
                            put++;
                        }
                    }
                    return put;
                }
                catch (Exception) { return -1; }
            }

            // Values that name a smoke's picker folder after Restore: none unless another program wrote them meanwhile.
            internal int Leftover()
            {
                int left = 0;
                foreach (KeyValuePair<string, KeyValuePair<object, Microsoft.Win32.RegistryValueKind>> value in Read())
                {
                    if (kept.ContainsKey(value.Key)) continue;
                    byte[] bytes = value.Value.Key as byte[];
                    string text = bytes != null ? Encoding.Unicode.GetString(bytes) : Convert.ToString(value.Value.Key);
                    if (text.IndexOf("workspace-pickers", StringComparison.OrdinalIgnoreCase) >= 0) left++;
                }
                return left;
            }
        }

        // A tooltip as it shows, laid on the window's own picture where it would appear: the window copy doesn't take Windows'
        // separate tooltip window, so it's drawn here with the tooltip's own drawing.
        private static void TipPicture(WorkspaceWindow window, string text, Point at, string path, bool above)
        {
            // Same settling as the other pictures, or the first copy at 100% can catch the Send key before it paints.
            window.Refresh();
            Application.DoEvents();
            LookShots.Flush();
            using (Bitmap shot = LookShots.Copy(window))
            using (Graphics graphics = Graphics.FromImage(shot))
            {
                Size size = WorkPaint.TipSize(text, window.TipFont);
                int x = Math.Max(4, Math.Min(at.X, shot.Width - size.Width - 4));
                int y = above ? at.Y - size.Height - window.Z(6) : at.Y + window.Z(16);
                WorkPaint.DrawTip(graphics, new Rectangle(x, y, size.Width, size.Height), text, window.TipFont);
                using (Bitmap sheet = new Bitmap(shot.Width + 56, shot.Height + 56))
                {
                    using (Graphics paper = Graphics.FromImage(sheet)) { paper.Clear(LookShots.Paper); paper.DrawImage(shot, 28, 28); }
                    sheet.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
        }

        private static string WorkspaceHeaderOf(Func<string, string> alias, string folder) { return alias != null ? alias(folder) : folder; }

        private static void WorkspaceTimed(string folder)
        {
            File.WriteAllText(Path.Combine(folder, "result.txt"), HuskJson.Write(HuskJson.Map("type", "result", "is_error", false, "session_id", Guid.NewGuid().ToString(), "duration_ms", 4200, "total_cost_usd", 0.04, "result", "Done.")) + "\n", new UTF8Encoding(false));
        }

        private static List<string> WorkspaceRawArgs(string folder)
        {
            string raw = File.ReadAllText(Path.Combine(folder, "raw.txt"), Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage)).TrimEnd('\r', '\n', ' ');
            List<string> args = WorkspaceDialogs.Split("claude.exe " + raw);
            args.RemoveAt(0);
            return args;
        }

        private static List<string> WorkspaceRun(string standin, List<string> args, string folder)
        {
            File.Delete(Path.Combine(Path.GetDirectoryName(standin), "raw.txt"));
            using (Process process = new Process { StartInfo = Workspace.StartInfo(standin, args, folder) })
            {
                process.Start();
                process.StandardInput.Close();
                process.StandardOutput.ReadToEnd();
                if (!process.WaitForExit(8000)) { Workspace.KillTree(process); throw new Exception("Workspace's argument stand-in didn't finish."); }
            }
            return WorkspaceRawArgs(Path.GetDirectoryName(standin));
        }

        // Every folder the chat names reaches Claude Code as one --add-dir each, exactly as written, whatever it's called and
        // wherever it is, and nothing after it is swallowed as another folder.
        private void SmokeWorkspaceArguments(List<string> checks, string folder)
        {
            string box = Path.Combine(folder, "arguments");
            Directory.CreateDirectory(box);
            string standin = WorkspaceRawStandin(box, Guid.NewGuid().ToString(), false);
            string root = Path.GetPathRoot(box);
            List<string> odd = new List<string> {
                Path.Combine(box, "with spaces in it"), Path.Combine(box, "trailing") + "\\", root, @"\\server\share", @"\\server\share\deep\",
                Path.Combine(box, "amp & caret ^ bang ! pct %PATH% (paren) 'quote'"), Path.Combine(box, "café"), Path.Combine(box, new string('n', 150)),
                Path.Combine(box, "gone since"), Path.Combine(box, "twice"), Path.Combine(box, "twice"), Path.Combine(box, "twice", "inside it"), "-not-a-flag" };
            foreach (int count in new[] { 1, 2, 5, odd.Count })
            {
                List<string> extra = odd.GetRange(0, Math.Min(count, odd.Count));
                foreach (bool trusted in new[] { true, false })
                {
                    List<string> args = Workspace.Arguments("Ask me", "session-id", "Opus", Path.Combine(box, "turn settings.json"), trusted, extra);
                    int at = args.IndexOf("--add-dir");
                    for (int i = 0; i < extra.Count; i++) if (args[at + i * 2] != "--add-dir" || args[at + i * 2 + 1] != extra[i]) throw new Exception("Workspace's --add-dir list is out of order for " + count + " folders.");
                    List<string> tail = args.GetRange(at + extra.Count * 2, args.Count - at - extra.Count * 2);
                    if (string.Join("|", tail.ToArray()) != (trusted ? "" : "--setting-sources|user|--strict-mcp-config")) throw new Exception("Workspace put something after its folders: " + string.Join("|", tail.ToArray()));
                    List<string> heard = WorkspaceRun(standin, args, box), joined = WorkspaceDialogs.Split("claude.exe " + PlanBrain.Joined(args));
                    joined.RemoveAt(0);
                    if (string.Join("\n", heard.ToArray()) != string.Join("\n", args.ToArray())) throw new Exception("Claude Code would read different arguments through claude.cmd: " + string.Join(" | ", heard.ToArray()));
                    if (string.Join("\n", joined.ToArray()) != string.Join("\n", args.ToArray())) throw new Exception("Claude Code would read different arguments as an exe: " + string.Join(" | ", joined.ToArray()));
                }
            }
            checks.Add("PASS Workspace hands Claude Code 1, 2, 5 and 13 --add-dir folders exactly, through claude.cmd and as an exe: spaces, a trailing backslash, a drive root, UNC paths, & ^ ! % ( ) ', accents, a 150-letter name, a gone folder, a repeat and a folder inside another, and the safe flags after them stay whole");

            using (Workspace work = new Workspace(box, "CapriWorkspace-" + Process.GetCurrentProcess().Id + "-args-" + Guid.NewGuid().ToString("N")) { Home = Path.Combine(box, "her-folder") })
            {
                string main = Path.Combine(box, "main"), other = Path.Combine(box, "Other Folder"), inner = Path.Combine(main, "inner");
                foreach (string made in new[] { main, other, inner }) Directory.CreateDirectory(made);
                WorkspaceChat chat = work.NewChat(main, false);
                if (work.AddFolders(chat, new[] { other + "\\", other.ToUpperInvariant(), other.Replace('\\', '/'), main + "\\" }) != 1 || chat.Folders[0] != other) throw new Exception("Workspace kept a repeat folder or its trailing slash: " + string.Join(", ", chat.Folders.ToArray()));
                bool refused = false;
                try { work.AddFolders(chat, new[] { inner, Path.Combine(box, "never made") }); } catch (IOException) { refused = true; }
                if (!refused || chat.Folders.Count != 1) throw new Exception("Workspace added part of a list that had a missing folder in it.");
                if (work.AddFolders(chat, new[] { root }) != 1 || chat.Folders[1] != root) throw new Exception("Workspace lost a drive root's own backslash.");
                if (work.AddFolders(chat, new[] { inner }) != 1) throw new Exception("Workspace refused a folder inside another.");
                chat.TrustAsked = true;
                work.Executable = standin;
                WorkspaceTurn turn;
                lock (chat.Gate) { if (!work.Send(chat, "Go", false)) throw new Exception("Workspace didn't run with a drive root added."); turn = chat.Live; }
                if (turn == null || !turn.Done.WaitOne(8000)) throw new Exception("Workspace's drive-root turn didn't end.");
                List<string> heard = WorkspaceRawArgs(box);
                int at = heard.IndexOf("--add-dir");
                if (at < 0 || heard[at + 1] != other || heard[at + 3] != root || heard[at + 5] != inner || heard[heard.Count - 3] != "--setting-sources" || heard[heard.Count - 2] != "user" || heard[heard.Count - 1] != "--strict-mcp-config")
                    throw new Exception("An untrusted chat with a drive root lost its safe flags: " + string.Join(" | ", heard.ToArray()));
                checks.Add("PASS Workspace drops repeats by case, slash and trailing backslash, adds nothing from a list with a missing folder, keeps a drive root's backslash, and an untrusted chat with a drive root still runs limited");
            }
        }

        // Ways to talk the hook into treating a path as inside the chat's folders and files when it isn't, and the other way.
        private void SmokeWorkspaceInside(List<string> checks, string folder)
        {
            string box = Path.Combine(folder, "inside");
            string work = Path.Combine(box, "work"), shop = Path.Combine(box, "workshop"), extra = Path.Combine(box, "extra"), loose = Path.Combine(box, "loose");
            foreach (string made in new[] { work, shop, extra, loose, Path.Combine(work, "sub") }) Directory.CreateDirectory(made);
            string spec = Path.Combine(loose, "spec.md"), beside = Path.Combine(loose, "beside.md"), secret = Path.Combine(shop, "secret.txt");
            foreach (string file in new[] { spec, beside, secret }) File.WriteAllText(file, "x");
            List<string> folders = new List<string> { work, extra }, files = new List<string> { spec };
            Func<string, Dictionary<string, object>, bool> inside = delegate(string tool, Dictionary<string, object> input) { return Workspace.Inside(folders, files, tool, input, work); };
            Func<string, bool> reads = delegate(string path) { return inside("Read", HuskJson.Map("file_path", path)); };
            List<string> wrong = new List<string>();
            Action<string, bool, bool> expect = delegate(string what, bool got, bool want) { if (got != want) wrong.Add(what + (want ? " was outside" : " was inside")); };
            expect("a file in the first folder", reads(Path.Combine(work, "a.txt")), true);
            expect("a file in the second folder", reads(Path.Combine(extra, "a.txt")), true);
            expect("the added file", reads(spec), true);
            expect("the added file in other case", reads(spec.ToUpperInvariant()), true);
            expect("the added file with forward slashes", reads(spec.Replace('\\', '/')), true);
            expect("the added file by a .. route", reads(Path.Combine(work, "..", "loose", "spec.md")), true);
            expect("a relative path", reads(@"sub\a.txt"), true);
            expect("a folder in other case", reads(Path.Combine(work.ToUpperInvariant(), "a.txt")), true);
            expect("forward slashes", reads(work.Replace('\\', '/') + "/sub/a.txt"), true);
            expect("a file beside the added file", reads(beside), false);
            expect("the added file's folder", inside("Grep", HuskJson.Map("pattern", "x", "path", loose)), false);
            expect("a glob in the added file's folder", inside("Glob", HuskJson.Map("pattern", Path.Combine(loose, "*.md"))), false);
            expect("a .. out of the folder", reads(Path.Combine(work, "..", "workshop", "secret.txt")), false);
            expect("a relative .. out", reads(@"..\workshop\secret.txt"), false);
            expect("a folder that starts with the first one's name", reads(secret), false);
            expect("a name that starts with the first one's name", reads(work + "shop\\secret.txt"), false);
            expect("forward slashes out", reads(shop.Replace('\\', '/') + "/secret.txt"), false);
            expect("a \\\\?\\ path with .. out", reads(@"\\?\" + work + @"\..\workshop\secret.txt"), false);
            expect("a stream beside it", reads(beside + ":hidden"), false);
            expect("a glob that climbs out after a wildcard", inside("Glob", HuskJson.Map("pattern", "**/../../workshop/*")), false);
            expect("a glob with a path outside", inside("Glob", HuskJson.Map("pattern", "*.txt", "path", shop)), false);
            expect("a glob inside", inside("Glob", HuskJson.Map("pattern", "sub/**/*.cs")), true);
            expect("the home folder", reads(@"~\secret.txt"), false);
            expect("another drive-relative path", reads("Q:secret.txt"), false);
            string shortShop = WorkspacePaths.Short(shop), shortWork = WorkspacePaths.Short(work);
            if (shortShop != shop) expect("a short 8.3 name out", reads(Path.Combine(shortShop, "secret.txt")), false);
            if (shortWork != work) expect("a short 8.3 name in", reads(Path.Combine(shortWork, "a.txt")), true);
            string link = Path.Combine(work, "out-link"), fileLink = Path.Combine(loose, "spec-link.md");
            bool junction = false;
            using (Process linking = new Process { StartInfo = new ProcessStartInfo("cmd.exe", "/d /s /c \"mklink /J " + PlanBrain.Quoted(link) + " " + PlanBrain.Quoted(shop) + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true } })
            {
                linking.Start();
                linking.StandardOutput.ReadToEnd();
                junction = linking.WaitForExit(5000) && linking.ExitCode == 0;
            }
            try
            {
                if (!junction) throw new Exception("Couldn't make the junction stand-in.");
                expect("a junction in the folder that points out", reads(Path.Combine(link, "secret.txt")), false);
                expect("a new file through that junction", reads(Path.Combine(link, "new.txt")), false);
                folders.Add(link);
                expect("a junction added as a folder, to where it points", reads(secret), true);
                folders.Remove(link);
            }
            finally { if (Directory.Exists(link)) Directory.Delete(link); }
            if (wrong.Count > 0) throw new Exception("Workspace's hook got these wrong: " + string.Join("; ", wrong.ToArray()));
            checks.Add("PASS Workspace hook paths: .., case, forward slashes, \\\\?\\, 8.3 names, streams, junctions out, a file beside an added one, C:\\work against C:\\workshop, and globs that climb out are all judged by where they really are");
        }

        // Explorer's own pickers, opened for real on the smoke's hidden desktop and driven by messages from another thread:
        // pictured, read back, cancelled, and accepted with known folders and files.
        private void SmokeWorkspacePickers(List<string> checks, string folder)
        {
            // Windows remembers where a program's open dialogs went, by program name, so this run's picks would be where his own
            // Capri's dialogs open next (the Import mod dialog opened in one on 12 Nov). The history goes back as it was found.
            DialogMemory memory = new DialogMemory();
            try { PickersOnce(checks, folder); }
            finally
            {
                int put = memory.Restore();
                if (put < 0) checks.Add("LIMIT Workspace pickers: Windows' dialog history couldn't be put back after the picks.");
                else if (memory.Leftover() > 0) checks.Add("LIMIT Workspace pickers: Windows' dialog history still names this run's folders after " + put + " values were put back.");
            }
        }

        private void PickersOnce(List<string> checks, string folder)
        {
            folder = Path.Combine(folder, Guid.NewGuid().ToString("N"));
            string pictures = Path.Combine(folder, "pictures"), project = Path.Combine(folder, "project");
            string one = Path.Combine(project, "pick one"), two = Path.Combine(project, "pick two"), started = Path.Combine(folder, "started here");
            foreach (string made in new[] { pictures, one, two, started }) Directory.CreateDirectory(made);
            string fileOne = Path.Combine(project, "first file.txt"), fileTwo = Path.Combine(project, "second.md");
            File.WriteAllText(fileOne, "1");
            File.WriteAllText(fileTwo, "2");
            List<string> notes = new List<string>();
            using (Workspace work = new Workspace(Path.Combine(folder, "data"), "CapriWorkspace-" + Process.GetCurrentProcess().Id + "-pickers-" + Guid.NewGuid().ToString("N")) { Home = Path.Combine(folder, "her-folder") })
            using (WorkspaceWindow window = new WorkspaceWindow(work, delegate { return false; }, 1f, false))
            {
                window.Location = new Point(40, 40);
                window.Show(this);
                window.ClientSize = new Size(WorkspaceWindow.Wide, WorkspaceWindow.Tall);
                Application.DoEvents();
                IntPtr owner = window.Handle;
                // Opens the picker on this thread the way the window's keys do, while another thread finds it and drives it.
                Func<string, Action, Func<IntPtr, string>, string> drive = delegate(string name, Action open, Func<IntPtr, string> act)
                {
                    string problem = null;
                    bool up = true, again = false;
                    System.Threading.Thread driver = new System.Threading.Thread(delegate()
                    {
                        IntPtr dialog = IntPtr.Zero;
                        try
                        {
                            dialog = WorkspaceDialogs.Wait(20000);
                            if (dialog == IntPtr.Zero) { problem = name + ": no picker opened."; return; }
                            System.Threading.Thread.Sleep(1200);
                            WorkspaceDialogs.Picture(dialog, Path.Combine(pictures, name + ".png"));
                            if (WorkspaceDialogs.Owner(dialog) != owner) problem = name + ": the picker isn't owned by the Workspace window.";
                            window.BeginInvoke(new MethodInvoker(delegate { if (!up) return; again = true; window.ChooseFolders(); window.ChooseFiles(); }));
                            System.Threading.Thread.Sleep(1500);
                            if (WorkspaceDialogs.Open().Count != 1) problem = name + ": a second picker opened over the first.";
                            if (!again) problem = name + ": the window didn't take a click while the picker was open, so twice wasn't tried.";
                            up = false;
                            lock (notes) notes.Add(name + ": \"" + WorkspaceDialogs.Text(dialog) + "\" keys " + string.Join(", ", WorkspaceDialogs.Keys(dialog).ToArray()));
                            string acted = act(dialog);
                            if (acted != null && problem == null) problem = name + ": " + acted;
                            Stopwatch closing = Stopwatch.StartNew();
                            while (WorkspaceDialogs.Open().Count > 0 && closing.ElapsedMilliseconds < 10000) System.Threading.Thread.Sleep(50);
                            if (WorkspaceDialogs.Open().Count > 0 && problem == null) problem = name + ": the picker didn't close.";
                            System.Threading.Thread.Sleep(1500);
                            if (WorkspaceDialogs.Open().Count > 0 && problem == null) problem = name + ": another picker opened after it closed.";
                        }
                        catch (Exception error) { problem = name + ": " + error.Message; }
                        finally { foreach (IntPtr left in WorkspaceDialogs.Open()) WorkspaceDialogs.Press(left, 2); }
                    }) { IsBackground = true, Name = "Capri picker driver" };
                    driver.Start();
                    open();
                    if (!driver.Join(40000)) return name + ": the driver didn't finish.";
                    Application.DoEvents();
                    return problem;
                };
                Func<IntPtr, string> cancel = delegate(IntPtr dialog) { WorkspaceDialogs.Press(dialog, 2); return null; };
                Func<string, Func<IntPtr, string>> accept = delegate(string typed)
                {
                    return delegate(IntPtr dialog)
                    {
                        IntPtr nameBox = WorkspaceDialogs.NameBox(dialog);
                        if (nameBox == IntPtr.Zero) return "no name box among " + string.Join(", ", WorkspaceDialogs.Edits(dialog).ToArray());
                        WorkspaceDialogs.Type(nameBox, typed);
                        System.Threading.Thread.Sleep(300);
                        WorkspaceDialogs.Press(dialog, 1);
                        Stopwatch wait = Stopwatch.StartNew();
                        while (WorkspaceDialogs.Open().Count > 0 && wait.ElapsedMilliseconds < 4000) System.Threading.Thread.Sleep(50);
                        if (WorkspaceDialogs.Open().Count == 0) return null;
                        WorkspaceDialogs.Picture(dialog, Path.Combine(pictures, "after-typing.png"));
                        lock (notes) notes.Add("the picker stayed open after " + typed + " and its key; pressed again");
                        WorkspaceDialogs.Press(dialog, 1);
                        return null;
                    };
                };
                List<string> failed = new List<string>();
                Action<string> fail = delegate(string problem) { if (problem != null) failed.Add(problem); };

                WorkPickers.Opened = 0;
                fail(drive("folders-cancel", delegate { window.ChooseFolders(); }, cancel));
                uint options = WorkPickers.Opened;
                if ((options & WorkPickers.PickFolders) == 0 || (options & WorkPickers.AllowMultiSelect) == 0 || (options & WorkPickers.ForceFileSystem) == 0) failed.Add("The folder picker's options came back as 0x" + options.ToString("X"));
                if (window.Chat == null || !work.IsHome(window.Chat.Folder) || work.Chats().Count != 1 || window.Picking) failed.Add("Cancelling the folder picker changed something.");

                fail(drive("folders-accept-one", delegate { window.ChooseFolders(); }, accept(started)));
                if (window.Chat == null || !Workspace.Same(window.Chat.Folder, started) || window.Chat.Folders.Count != 0) failed.Add("Accepting a typed folder didn't start a chat there: " + (window.Chat == null ? "no chat" : window.Chat.Folder));
                WorkspaceChat first = window.Chat;
                if (first != null)
                {
                    WorkspaceChat inProject = work.NewChat(project, false);
                    window.ShowChat(inProject);
                    fail(drive("folders-add-one", delegate { window.ChooseFolders(); }, accept("pick one")));
                    List<string> added = Workspace.AllFolders(inProject);
                    if (added.Count != 2 || !added.Exists(delegate(string path) { return Workspace.Same(path, one); })) failed.Add("A folder named in the picker didn't come back: " + string.Join(", ", added.ToArray()));

                    fail(drive("files-cancel", delegate { window.ChooseFiles(); }, cancel));
                    if (Workspace.AllFiles(inProject).Count != 0 || window.Picking) failed.Add("Cancelling the file picker changed something.");
                    fail(drive("files-accept-two", delegate { window.ChooseFiles(); }, accept("\"first file.txt\" \"second.md\"")));
                    List<string> files = Workspace.AllFiles(inProject);
                    if (files.Count != 2 || !files.Contains(fileOne) || !files.Contains(fileTwo)) failed.Add("Two files picked together didn't both come back: " + string.Join(", ", files.ToArray()));
                }
                window.Close();
                foreach (string note in notes) checks.Add("NOTE " + note);
                if (failed.Count > 0) throw new Exception(string.Join(" | ", failed.ToArray()));
            }
            foreach (string name in new[] { "folders-cancel", "folders-accept-one", "folders-add-one", "files-cancel", "files-accept-two" })
                if (!File.Exists(Path.Combine(pictures, name + ".png"))) throw new Exception("Picker picture missing: " + name);
            checks.Add("PASS Workspace opens Windows' own folder picker (pick folders, several at once, file system only) and file picker, owned by its window and never twice; cancelling changes nothing, a typed folder starts a chat there or joins one, and two files picked together both come back. Two folders at once need clicks in the picker's view, which this doesn't do. Pictures in " + pictures);
        }

        // What a person does in their first minute: types during a turn, stops it and sends again, closes the window mid-turn,
        // shrinks it, makes 100 chats with long names, drops 30 files or something that isn't a file, pastes a long log.
        private void SmokeWorkspaceFirstMinute(List<string> checks, string folder)
        {
            string box = Path.Combine(folder, "first-minute");
            string project = Path.Combine(box, "project"), longName = Path.Combine(Path.GetTempPath(), "cw" + Guid.NewGuid().ToString("N").Substring(0, 6), new string('f', 150));
            foreach (string made in new[] { project, longName }) Directory.CreateDirectory(made);
            List<string> dropped = new List<string>();
            for (int i = 0; i < 30; i++) { string file = Path.Combine(project, "dropped file number " + i + ".txt"); File.WriteAllText(file, "x"); dropped.Add(file); }
            MethodInfo dragDrop = typeof(Control).GetMethod("OnDragDrop", BindingFlags.Instance | BindingFlags.NonPublic), dragEnter = typeof(Control).GetMethod("OnDragEnter", BindingFlags.Instance | BindingFlags.NonPublic);
            using (Workspace work = new Workspace(box, "CapriWorkspace-" + Process.GetCurrentProcess().Id + "-minute-" + Guid.NewGuid().ToString("N")) { Home = Path.Combine(box, "her-folder") })
            {
                work.Executable = WorkspaceRawStandin(box, Guid.NewGuid().ToString(), true);
                WorkspaceChat chat = work.NewChat(project, false);
                work.Trust(chat, true);
                chat.TrustAsked = true;
                string finish = Path.Combine(box, "finish");
                using (WorkspaceWindow window = new WorkspaceWindow(work, delegate { return false; }, 1f, false))
                {
                    window.Show(this);
                    window.ShowChat(chat);
                    window.Input.Text = "First message";
                    window.TypeInto(window.Input, Keys.Enter);
                    Application.DoEvents();
                    if (!chat.Running || window.Input.Text.Length != 0 || window.Find("Stop") == null || window.Find("Send") != null) throw new Exception("Workspace didn't start a turn on Enter or show Stop.");
                    window.Input.Text = "Typed while it works";
                    window.TypeInto(window.Input, Keys.Enter);
                    window.TypeInto(window.Input, Keys.Enter | Keys.Shift);
                    Application.DoEvents();
                    if (!window.Input.Enabled || window.Input.Text != "Typed while it works" || WorkspaceItems(chat).FindAll(delegate(WorkspaceItem item) { return item.Kind == "user"; }).Count != 1) throw new Exception("Typing during a turn sent it, cleared it or locked the box.");
                    WorkspaceTurn live = chat.Live;
                    window.Find("Stop").PerformClick();
                    if (live == null || !live.Done.WaitOne(8000)) throw new Exception("Stop didn't end the turn.");
                    WorkspaceWait(delegate { Application.DoEvents(); return window.Find("Send") != null && window.Find("Send").Enabled; }, "Send didn't come back after Stop.");
                    if (window.Input.Text != "Typed while it works") throw new Exception("Stop lost what they'd typed.");
                    File.WriteAllText(finish, "go");
                    window.TypeInto(window.Input, Keys.Enter);
                    live = chat.Live;
                    if (live == null || !live.Done.WaitOne(8000)) throw new Exception("Sending again after Stop didn't run.");
                    File.Delete(finish);
                    Application.DoEvents();

                    string paste = new string('p', 99990) + "\r\nend";
                    window.Input.Text = "";
                    window.Input.Focus();
                    Native.SendMessage(window.Input.Handle, 0x00C2, new IntPtr(1), paste);
                    Application.DoEvents();
                    if (window.Input.TextLength != paste.Length || window.Crowded() != null) throw new Exception("A pasted 100,000-letter block came out as " + window.Input.TextLength + " letters: " + window.Crowded());
                    window.Input.Text = "";

                    window.Input.Text = "Close mid-turn";
                    window.TypeInto(window.Input, Keys.Enter);
                    live = chat.Live;
                    if (live == null) throw new Exception("The mid-turn message didn't start.");
                    window.Close();
                    File.WriteAllText(finish, "go");
                    if (!live.Done.WaitOne(8000)) throw new Exception("Closing the window stuck the turn.");
                    Application.DoEvents();
                    File.Delete(finish);
                }
                using (WorkspaceWindow window = new WorkspaceWindow(work, delegate { return false; }, 1f, false))
                {
                    window.Show(this);
                    window.ShowChat(chat);
                    if (window.Find("Send") == null || window.Find("Stop") != null) throw new Exception("The reopened window shows a finished chat as running.");
                    if (!WorkspaceItems(chat).Exists(delegate(WorkspaceItem item) { return item.Kind == "end"; })) throw new Exception("The turn's end was lost while the window was closed.");

                    foreach (Size size in new[] { new Size(10, 10), new Size(WorkspaceWindow.Narrowest, WorkspaceWindow.Shortest), new Size(1600, 1000), new Size(WorkspaceWindow.Wide, WorkspaceWindow.Tall) })
                    {
                        window.ClientSize = size;
                        Application.DoEvents();
                        string crowded = window.Crowded();
                        if (crowded != null || window.Width < WorkspaceWindow.Narrowest) throw new Exception("Workspace at " + size + ": " + (crowded ?? "smaller than its smallest"));
                    }

                    string title = new string('T', 200);
                    window.Rename(title);
                    window.Rename("   ");
                    if (chat.Title != title || window.Crowded() != null) throw new Exception("A 200-letter title didn't save or crowded the window: " + window.Crowded());
                    WorkspaceChat named = work.NewChat(longName, false);
                    window.ShowChat(named);
                    if (window.Crowded() != null) throw new Exception("A 150-letter folder crowded the window: " + window.Crowded());
                    List<WorkspaceChat> many = new List<WorkspaceChat>();
                    for (int i = 0; i < 100; i++) many.Add(work.NewChat(project, false));
                    window.RefreshChat();
                    window.ShowChat(many[99]);
                    window.ShowChat(many[0]);
                    if (window.Crowded() != null) throw new Exception("100 chats crowded the window: " + window.Crowded());

                    window.ShowChat(chat);
                    DataObject files = new DataObject(DataFormats.FileDrop, dropped.ToArray());
                    dragDrop.Invoke(window, new object[] { new DragEventArgs(files, 0, 10, 10, DragDropEffects.Copy, DragDropEffects.Copy) });
                    Application.DoEvents();
                    if (Workspace.AllFiles(chat).Count != 30 || window.Chips.Count != 31) throw new Exception("Dropping 30 files gave " + Workspace.AllFiles(chat).Count + " files and " + window.Chips.Count + " chips.");
                    string crowdedChips = window.Crowded();
                    if (crowdedChips != null) throw new Exception("30 file chips crowded the composer: " + crowdedChips);
                    DataObject words = new DataObject(DataFormats.UnicodeText, "just some words");
                    DragEventArgs entering = new DragEventArgs(words, 0, 10, 10, DragDropEffects.Copy | DragDropEffects.Link, DragDropEffects.Copy);
                    dragEnter.Invoke(window, new object[] { entering });
                    dragDrop.Invoke(window, new object[] { new DragEventArgs(words, 0, 10, 10, DragDropEffects.Copy, DragDropEffects.Copy) });
                    dragDrop.Invoke(window, new object[] { new DragEventArgs(new DataObject(DataFormats.FileDrop, new[] { Path.Combine(box, "not there.txt"), "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}" }), 0, 10, 10, DragDropEffects.Copy, DragDropEffects.Copy) });
                    if (entering.Effect != DragDropEffects.None || Workspace.AllFiles(chat).Count != 30 || Workspace.AllFolders(chat).Count != 1) throw new Exception("Dropping words or a path that isn't a file changed the chat.");
                    foreach (string file in dropped) window.RemovePath(file);
                    if (Workspace.AllFiles(chat).Count != 0) throw new Exception("Removing the dropped files left some.");

                    WorkspaceChat fresh = work.NewChat(project, false);
                    window.ShowChat(fresh);
                    if (window.UndoOffered) throw new Exception("Undo was offered with nothing to undo.");
                    if (work.Undo(fresh) != "No edits to undo.") throw new Exception("Undo with nothing to undo said something else.");
                    int confirmed = 0;
                    window.Confirm = delegate { confirmed++; return true; };
                    File.Delete(finish);
                    fresh.TrustAsked = true;
                    work.Trust(fresh, true);
                    window.Input.Text = "Delete me while I run";
                    window.TypeInto(window.Input, Keys.Enter);
                    WorkspaceTurn running = fresh.Live;
                    window.DeleteChat(fresh);
                    if (confirmed != 1 || running == null || !running.Done.WaitOne(8000) || work.Chats().Contains(fresh) || window.Chat == fresh) throw new Exception("Deleting a running chat didn't stop it and take it away.");
                    window.Confirm = delegate { return false; };
                    window.DeleteChat(chat);
                    if (!work.Chats().Contains(chat)) throw new Exception("Keeping a chat deleted it.");
                    window.Close();
                }
                File.WriteAllText(finish, "go");
            }
            try { Directory.Delete(Path.GetDirectoryName(longName), true); } catch (IOException) { }
            checks.Add("PASS Workspace's first minute: typing during a turn keeps the words, Stop then Enter sends again, closing mid-turn and reopening shows it done, the smallest size holds, 100 chats, a 200-letter title and a 150-letter folder fit, 30 dropped files fit and come off, words and non-files dropped do nothing, a 100,000-letter paste goes in whole, and undo with nothing to undo is off");
        }

        // How long a 1,000-item chat takes to open, scroll and take one more item while a turn streams, and whether opening
        // and closing the window leaves window handles or GDI objects behind.
        private void SmokeWorkspaceCost(List<string> checks, string folder)
        {
            string box = Path.Combine(folder, "cost"), project = Path.Combine(box, "project");
            Directory.CreateDirectory(project);
            using (Workspace work = new Workspace(box, "CapriWorkspace-" + Process.GetCurrentProcess().Id + "-cost-" + Guid.NewGuid().ToString("N")) { Home = Path.Combine(box, "her-folder") })
            {
                WorkspaceChat chat = work.NewChat(project, false);
                work.Trust(chat, true);
                lock (chat.Gate)
                    for (int i = 0; i < 1000; i++)
                    {
                        if (i % 5 == 0) chat.Items.Add(new WorkspaceItem { Kind = "user", Text = "Question " + i + ": what does this part do, and why is it written like this?" });
                        else if (i % 5 == 1) chat.Items.Add(new WorkspaceItem { Kind = "text", Text = "## Part " + i + "\nIt reads the chat and lays out each item.\n```cs\nint total = " + i + ";\nConsole.WriteLine(total);\n```\n- one\n- two\nAnd a longer paragraph that wraps across the conversation's width more than once, so measuring it costs what a real answer costs." });
                        else if (i % 5 == 2) chat.Items.Add(new WorkspaceItem { Kind = "tool", Id = "c" + i, Name = "Read", Text = "Read " + Path.Combine(project, "file" + i + ".cs"), Turn = "t" });
                        else if (i % 5 == 3) chat.Items.Add(new WorkspaceItem { Kind = "result", Id = "c" + i, Text = "120 lines", Turn = "t" });
                        else chat.Items.Add(new WorkspaceItem { Kind = "text", Text = "Short answer " + i + "." });
                    }
                work.Save(chat);
                using (Process own = Process.GetCurrentProcess())
                {
                    int userBefore = GetGuiResources(own.Handle, 1), gdiBefore = GetGuiResources(own.Handle, 0);
                    long open = 0, scroll = 0, add = 0;
                    using (WorkspaceWindow window = new WorkspaceWindow(work, delegate { return false; }, 1f, false))
                    {
                        window.Show(this);
                        Application.DoEvents();
                        Stopwatch clock = Stopwatch.StartNew();
                        window.ShowChat(chat);
                        window.Refresh();
                        open = clock.ElapsedMilliseconds;
                        clock.Restart();
                        for (int i = 0; i < 40; i++) { window.ScrollConversation(window.ConversationTall * i / 40); window.Refresh(); }
                        scroll = clock.ElapsedMilliseconds / 40;
                        window.ScrollToEnd();
                        chat.Live = new WorkspaceTurn { Chat = chat };
                        clock.Restart();
                        for (int i = 0; i < 20; i++) { work.Add(chat, new WorkspaceItem { Kind = "text", Text = "Streamed " + i + ". Another answer arrives while the turn runs." }); Application.DoEvents(); window.Refresh(); }
                        add = clock.ElapsedMilliseconds / 20;
                        chat.Live = null;
                        if (!window.AtEnd || window.ItemCount != 150) throw new Exception("Streaming into a long chat stopped following or grew past 150 items.");
                        window.Close();
                    }
                    checks.Add("NOTE Workspace 1,000-item chat: opens in " + open + " ms, scrolls in " + scroll + " ms a step, takes a streamed item in " + add + " ms");
                    if (open > 1500 || scroll > 100 || add > 150) throw new Exception("Workspace is slow on a long chat: open " + open + " ms, scroll " + scroll + " ms, streamed item " + add + " ms.");
                    for (int i = 0; i < 20; i++)
                        using (WorkspaceWindow window = new WorkspaceWindow(work, delegate { return false; }, 1f, false))
                        {
                            window.Show(this);
                            window.ShowChat(chat);
                            window.OpenMenu("mode").Close();
                            Application.DoEvents();
                            window.Close();
                        }
                    Application.DoEvents();
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    Application.DoEvents();
                    int userAfter = GetGuiResources(own.Handle, 1), gdiAfter = GetGuiResources(own.Handle, 0);
                    checks.Add("NOTE Workspace opened and closed 21 times: USER " + userBefore + " -> " + userAfter + ", GDI " + gdiBefore + " -> " + gdiAfter);
                    if (userAfter - userBefore > 40 || gdiAfter - gdiBefore > 60) throw new Exception("Workspace leaks: USER " + userBefore + " -> " + userAfter + ", GDI " + gdiBefore + " -> " + gdiAfter);
                }
            }
            checks.Add("PASS Workspace opens, scrolls and streams into a 1,000-item chat quickly, and 21 opens and closes leave no window handles or GDI objects behind");
        }
    }
}

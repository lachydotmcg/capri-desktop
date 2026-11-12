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
            string pipe = "CapriWorkspace-" + Process.GetCurrentProcess().Id + "-look-";
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
            using (Workspace work = new Workspace(folder, "CapriWorkspace-" + Process.GetCurrentProcess().Id + "-folders-" + Guid.NewGuid().ToString("N")))
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
                using (Workspace loaded = new Workspace(folder, "CapriWorkspace-" + Process.GetCurrentProcess().Id + "-folders-load-" + Guid.NewGuid().ToString("N")))
                {
                    WorkspaceChat kept = loaded.Chats().Find(delegate(WorkspaceChat found) { return found.Id == chat.Id; });
                    if (kept == null || kept.Folders.Count != 2 || kept.Folders[0] != extra || kept.Folders[1] != later || kept.Files.Count != 1 || kept.Files[0] != log || !kept.Sent.Contains(log)) throw new Exception("Workspace didn't keep a chat's folders and files.");
                    WorkspaceItem keptAsk = kept.Items.Find(delegate(WorkspaceItem item) { return item.Kind == "user"; });
                    if (keptAsk == null || keptAsk.Files != spec + "\n" + log) throw new Exception("Workspace lost which files went with a message.");
                }
                checks.Add("PASS Workspace keeps each chat's folders, files and which files went out, across a restart");
            }
        }

        // The window itself, at 100%, 125%, 150% and 200% and at its smallest size: nothing crowded, code in her mono,
        // copyable messages, tool results that open, pickers that save, Everything asked first, a draft per chat, following
        // only at the end, Enter and Shift+Enter, the Explorer flow for folders and files, paging and its place.
        private void SmokeWorkspaceWindow(List<string> checks, string folder)
        {
            folder = Path.Combine(folder, Guid.NewGuid().ToString("N"));
            string project = Path.Combine(folder, "project"), second = Path.Combine(folder, "second"), third = Path.Combine(folder, "third");
            foreach (string made in new[] { project, second, third }) Directory.CreateDirectory(made);
            string fileOne = Path.Combine(second, "one.txt"), fileTwo = Path.Combine(third, "two.txt");
            File.WriteAllText(fileOne, "1"); File.WriteAllText(fileTwo, "2");
            using (Workspace work = new Workspace(folder, "CapriWorkspace-" + Process.GetCurrentProcess().Id + "-window-" + Guid.NewGuid().ToString("N")))
            {
                work.Executable = WorkspaceStandin(folder, Guid.NewGuid().ToString());
                using (WorkspaceWindow window = new WorkspaceWindow(work, delegate { return false; }, 1f, false))
                {
                    window.Show(this);
                    if (!window.ChooseShown || window.Crowded() != null) throw new Exception("The empty Workspace doesn't offer Choose folder: " + window.Crowded());
                    window.PickFolders = delegate { return new[] { project, second }; };
                    window.ChooseFolders();
                    WorkspaceChat made = window.Chat;
                    if (made == null || made.Folder != project || made.Folders.Count != 1 || made.Folders[0] != second || made.Title != "project" || window.ChooseShown) throw new Exception("Choose folder didn't start a chat in the first folder with the second added.");
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
                        window.Confirm = delegate(string question)
                        {
                            if (question != "Everything lets Claude Code run anything without asking. Switch?") throw new Exception("Workspace changed its Everything warning.");
                            confirmations++;
                            return false;
                        };
                        window.PickMode("Everything");
                        if (confirmations != 1 || layout.Mode != before) throw new Exception("Workspace switched to Everything without confirmation.");
                        Native.SendMessage(mode.Handle, 0x020A, new IntPtr(120 << 16), IntPtr.Zero);
                        Native.SendMessage(mode.Handle, 0x0102, new IntPtr('E'), IntPtr.Zero);
                        if (layout.Mode != before) throw new Exception("Workspace changed mode on scroll or typing.");
                        window.Confirm = delegate { confirmations++; return true; };
                        window.PickMode("Everything");
                        if (layout.Mode != "Everything" || confirmations != 2) throw new Exception("Workspace didn't accept an intentional Everything switch.");
                        window.PickMode("Plan only");
                        window.PickModel("Haiku");
                        if (layout.Mode != "Plan only" || layout.Model != "Haiku" || mode.Text != "Plan only" || model.Text != "Haiku") throw new Exception("Workspace's pickers didn't save or show their choice.");
                        window.Confirm = null;
                        window.PickMode("Everything");
                        if (window.Asking == null || layout.Mode != "Plan only") throw new Exception("Workspace's own Everything card didn't ask first.");
                        window.Pending(window.Asking, false);
                        if (layout.Mode != "Plan only" || window.Asking != null) throw new Exception("Keep didn't keep the mode.");
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
                checks.Add("PASS Workspace ignores mode scrolling and typing, and asks before every switch to Everything, in its own card and through Confirm");

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
        }
    }
}

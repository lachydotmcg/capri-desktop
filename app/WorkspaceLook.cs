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
        private void SmokeWorkspaceLook(List<string> checks, string folder)
        {
            folder = Path.Combine(folder, Guid.NewGuid().ToString("N"));
            string pictures = Path.Combine(folder, "look");
            Directory.CreateDirectory(pictures);
            string project = Path.Combine(folder, "capri-desktop");
            Directory.CreateDirectory(project);
            List<string> saved = new List<string>();
            using (Workspace work = new Workspace(Path.Combine(folder, "data"), "CapriWorkspace-" + System.Diagnostics.Process.GetCurrentProcess().Id + "-look-" + Guid.NewGuid().ToString("N")))
            {
                using (WorkspaceWindow window = new WorkspaceWindow(work, delegate { return false; }, 1f, false))
                {
                    window.Location = new Point(40, 40);
                    window.Show(this);
                    window.ClientSize = new Size(1080, 740);
                    string path = Path.Combine(pictures, "old-01-no-chats.png");
                    LookShots.Save(window, path);
                    saved.Add(path);
                    WorkspaceChat chat = work.NewChat(project, false);
                    chat.TrustAsked = true;
                    work.Add(chat, new WorkspaceItem { Kind = "user", Text = "Why does the Workspace window's frame look wrong at the corners?" });
                    work.Add(chat, new WorkspaceItem { Kind = "text", Text = "The window paints a cut frame but never shapes itself to it.\n```cs\nNeon.Clip(this, 16 * s, 6 * s);\n```\nShaping it on every resize fixes the corners." });
                    work.Add(chat, new WorkspaceItem { Kind = "tool", Id = "look-tool", Name = "Read", Text = "Read app/WorkspaceWindow.cs" });
                    work.Add(chat, new WorkspaceItem { Kind = "result", Id = "look-tool", Text = "962 lines." });
                    work.Add(chat, new WorkspaceItem { Kind = "end", Text = "4.2s | $0.03" });
                    window.ShowChat(chat);
                    window.RefreshChat();
                    path = Path.Combine(pictures, "old-02-conversation.png");
                    LookShots.Save(window, path);
                    saved.Add(path);
                    for (int i = 0; i < 12; i++) work.Add(chat, new WorkspaceItem { Kind = i % 2 == 0 ? "user" : "text", Text = "Line " + i + " of a longer chat, long enough to wrap across the conversation at the smallest window size the Workspace allows." });
                    WorkspaceItem approval = new WorkspaceItem { Kind = "approval", Name = "Bash", Text = "Bash: npm test", Input = HuskJson.Write(HuskJson.Map("command", "npm test -- --runInBand --verbose", "description", "Run the tests")) };
                    approval.Ask = new WorkspaceApproval { Item = approval };
                    work.Add(chat, approval);
                    window.ClientSize = new Size(900, 600);
                    window.RefreshChat();
                    path = Path.Combine(pictures, "old-03-smallest-approval.png");
                    LookShots.Save(window, path);
                    saved.Add(path);
                    approval.Ask.Answered.Dispose();
                    approval.Ask = null;
                    window.Close();
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
            checks.Add("PASS Workspace look pictures saved: " + string.Join(", ", saved.ConvertAll(delegate(string p) { return Path.GetFileName(p); }).ToArray()) + " in " + pictures);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CapriDesktop
{
    // A control that paints what sits behind its children, so a key or a field on it reads as cut out of the same panel.
    internal interface WorkGround { void PaintGround(Graphics graphics, Rectangle area); }

    internal static class WorkPaint
    {
        internal static float Scale(Control control)
        {
            for (Control at = control; at != null; at = at.Parent)
            {
                WorkspaceWindow window = at as WorkspaceWindow;
                if (window != null) return window.Zoom;
            }
            return control.DeviceDpi / 96f;
        }

        internal static void Behind(Graphics graphics, Control control)
        {
            int x = 0, y = 0;
            for (Control at = control; at.Parent != null; at = at.Parent)
            {
                x += at.Left; y += at.Top;
                WorkGround ground = at.Parent as WorkGround;
                if (ground == null) continue;
                GraphicsState state = graphics.Save();
                graphics.TranslateTransform(-x, -y);
                ground.PaintGround(graphics, new Rectangle(x, y, control.Width, control.Height));
                graphics.Restore(state);
                return;
            }
            using (Brush fill = new SolidBrush(Neon.Ground)) graphics.FillRectangle(fill, control.ClientRectangle);
        }

        // A plate in her look: its fill, its rim, and its bottom right corner clipped.
        internal static void Plate(Graphics graphics, RectangleF box, float cut, Color fill, Color rim, float line)
        {
            SmoothingMode was = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath plate = Neon.Cut(box, 0, 0, cut, 0))
            {
                if (fill.A > 0) using (Brush brush = new SolidBrush(fill)) graphics.FillPath(brush, plate);
                if (rim.A > 0 && line > 0) using (Pen pen = new Pen(rim, line)) graphics.DrawPath(pen, plate);
            }
            graphics.SmoothingMode = was;
        }

        internal static void FolderMark(Graphics graphics, RectangleF box, Color colour, float s)
        {
            SmoothingMode was = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float w = box.Width, h = box.Height * 0.78f, top = box.Top + (box.Height - h) / 2;
            PointF[] outline =
            {
                new PointF(box.Left, top + h), new PointF(box.Left, top), new PointF(box.Left + w * 0.38f, top),
                new PointF(box.Left + w * 0.48f, top + h * 0.18f), new PointF(box.Right, top + h * 0.18f), new PointF(box.Right, top + h)
            };
            using (Pen pen = new Pen(colour, Math.Max(1f, 1.2f * s))) { pen.LineJoin = LineJoin.Miter; graphics.DrawPolygon(pen, outline); }
            graphics.SmoothingMode = was;
        }

        internal static void FileMark(Graphics graphics, RectangleF box, Color colour, float s)
        {
            SmoothingMode was = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float w = box.Height * 0.72f, left = box.Left + (box.Width - w) / 2, fold = w * 0.38f;
            PointF[] outline =
            {
                new PointF(left, box.Top), new PointF(left + w - fold, box.Top), new PointF(left + w, box.Top + fold),
                new PointF(left + w, box.Bottom), new PointF(left, box.Bottom)
            };
            using (Pen pen = new Pen(colour, Math.Max(1f, 1.2f * s)))
            {
                pen.LineJoin = LineJoin.Miter;
                graphics.DrawPolygon(pen, outline);
                graphics.DrawLines(pen, new[] { new PointF(left + w - fold, box.Top), new PointF(left + w - fold, box.Top + fold), new PointF(left + w, box.Top + fold) });
            }
            graphics.SmoothingMode = was;
        }

        internal static void Cross(Graphics graphics, PointF middle, float half, Color colour, float s)
        {
            SmoothingMode was = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(colour, Math.Max(1f, 1.3f * s)))
            {
                graphics.DrawLine(pen, middle.X - half, middle.Y - half, middle.X + half, middle.Y + half);
                graphics.DrawLine(pen, middle.X - half, middle.Y + half, middle.X + half, middle.Y - half);
            }
            graphics.SmoothingMode = was;
        }

        // A dropdown's ▽, like the Mainframe's: an outline, filled while the mouse is on it.
        internal static void Drop(Graphics graphics, float left, float middle, float s, Color colour, bool filled)
        {
            SmoothingMode was = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            PointF[] mark = { new PointF(left, middle - 3 * s), new PointF(left + 9 * s, middle - 3 * s), new PointF(left + 4.5f * s, middle + 3.5f * s) };
            if (filled) using (Brush brush = new SolidBrush(colour)) graphics.FillPolygon(brush, mark);
            using (Pen pen = new Pen(colour, 1.3f * s)) { pen.LineJoin = LineJoin.Miter; graphics.DrawPolygon(pen, mark); }
            graphics.SmoothingMode = was;
        }

        // Her thin scroll thumb: a dim rail and a cyan thumb, sized to how much of the content is in view.
        internal static RectangleF Thumb(Rectangle rail, int view, int content, int offset)
        {
            if (content <= view || view <= 0) return RectangleF.Empty;
            float tall = Math.Max(rail.Height * (float)view / content, Math.Min(rail.Height, 24 * (rail.Width / 3f)));
            float at = rail.Top + (rail.Height - tall) * offset / (float)(content - view);
            return new RectangleF(rail.Left, at, rail.Width, tall);
        }

        internal static void Rail(Graphics graphics, Rectangle rail, int view, int content, int offset, bool lit)
        {
            RectangleF thumb = Thumb(rail, view, content, offset);
            if (thumb.IsEmpty) return;
            using (Brush track = new SolidBrush(Neon.Dash)) graphics.FillRectangle(track, rail);
            using (Brush fill = new SolidBrush(lit ? Neon.Tone(160, 248, 251) : Neon.Cyan)) graphics.FillRectangle(fill, thumb);
        }

        internal const TextFormatFlags Line = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | Kept;
        // Text drawn by GDI ignores a Graphics' shift and clip unless told to keep them, and the conversation scrolls by shifting.
        internal const TextFormatFlags Kept = TextFormatFlags.PreserveGraphicsTranslateTransform | TextFormatFlags.PreserveGraphicsClipping;
        internal const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.ExpandTabs | Kept;

        internal static int Wide(string text, Font font) { return TextRenderer.MeasureText(text, font, Size.Empty, Line).Width; }

        internal static int Tall(string text, Font font, int width)
        {
            if (string.IsNullOrEmpty(text)) return font.Height;
            return TextRenderer.MeasureText(text, font, new Size(Math.Max(1, width), int.MaxValue), Wrap).Height;
        }
    }

    // A key in her look, drawn like the Mainframe's: a dark box with a red rim and its bottom right corner clipped. Cyan is
    // the step to take, solid is the window's main step, red ends something. A chevron makes it a dropdown with its small
    // faint caption, like MODE, before its choice in cyan. Glyphs: 1 minimize, 2 close, 3 maximize, 4 plus.
    internal sealed class WorkKey : Button
    {
        internal Color Accent = Neon.Rose;
        internal bool Solid, Chevron;
        internal int Glyph;
        internal string Caption = "";
        internal Font CaptionFont;
        private bool hot, down;

        internal WorkKey()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Neon.Ground;
            Cursor = Cursors.Hand;
            UseMnemonic = false;
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        // How wide the key needs to be for all its words.
        internal int Needs
        {
            get
            {
                float s = WorkPaint.Scale(this);
                int text = WorkPaint.Wide(Text, Font);
                if (Chevron) return (int)Math.Ceiling(10 * s + (Caption.Length > 0 && CaptionFont != null ? WorkPaint.Wide(Caption, CaptionFont) + 7 * s : 0) + text + 30 * s);
                return (int)Math.Ceiling(text + 24 * s);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            float s = WorkPaint.Scale(this);
            Graphics graphics = e.Graphics;
            WorkPaint.Behind(graphics, this);
            float line = Math.Max(1f, s);
            RectangleF box = new RectangleF(line / 2, line / 2, Width - line, Height - line);
            float x = Width / 2f, y = Height / 2f;
            bool lit = hot || (Focused && ShowFocusCues);
            if (Glyph != 0)
            {
                if (hot) WorkPaint.Plate(graphics, box, 4 * s, down ? Neon.Pressed : Neon.Hover, Color.Empty, 0);
                Color ink = !Enabled ? Neon.Tone(112, 94, 104) : lit ? (Glyph == 2 ? Neon.Hot : Neon.Cyan) : Glyph == 4 ? Neon.Cyan : Neon.Faint;
                if (Glyph == 4) WorkPaint.Plate(graphics, box, 5 * s, hot ? (down ? Neon.Tone(18, 62, 67) : Neon.Tone(13, 45, 49)) : Neon.Deep, lit ? Neon.Cyan : Neon.Tone(28, 84, 92), line);
                SmoothingMode was = graphics.SmoothingMode;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen pen = new Pen(ink, 1.5f * s))
                {
                    if (Glyph == 1) graphics.DrawLine(pen, x - 5 * s, y, x + 5 * s, y);
                    else if (Glyph == 2)
                    {
                        graphics.DrawLine(pen, x - 4.5f * s, y - 4.5f * s, x + 4.5f * s, y + 4.5f * s);
                        graphics.DrawLine(pen, x - 4.5f * s, y + 4.5f * s, x + 4.5f * s, y - 4.5f * s);
                    }
                    else if (Glyph == 3) graphics.DrawRectangle(pen, x - 4.5f * s, y - 4 * s, 9 * s, 8 * s);
                    else
                    {
                        graphics.DrawLine(pen, x - 5.5f * s, y, x + 5.5f * s, y);
                        graphics.DrawLine(pen, x, y - 5.5f * s, x, y + 5.5f * s);
                    }
                }
                graphics.SmoothingMode = was;
                return;
            }
            Color face, rim, ink2;
            if (!Enabled) { face = Neon.Field; rim = Neon.Tone(52, 22, 32); ink2 = Neon.Tone(112, 94, 104); }
            else if (Solid) { face = down ? ControlPaint.Dark(Accent, 0.1f) : hot ? ControlPaint.Light(Accent, 0.35f) : Accent; rim = face; ink2 = Neon.OnInk; }
            else if (Accent == Neon.Cyan) { face = down ? Neon.Tone(18, 62, 67) : hot ? Neon.Tone(13, 45, 49) : Neon.Deep; rim = lit ? Neon.Cyan : Neon.Tone(28, 84, 92); ink2 = Neon.Cyan; }
            else if (Accent == Neon.Hot) { face = down ? Neon.Tone(72, 18, 30) : hot ? Neon.Tone(54, 16, 28) : Neon.Tone(34, 12, 22); rim = lit ? Neon.Tone(255, 124, 122) : Neon.Hot; ink2 = Neon.Tone(255, 166, 162); }
            else { face = down ? Neon.Pressed : hot ? Neon.Hover : Neon.Field; rim = lit ? Neon.Hot : Neon.Edge; ink2 = Chevron ? Neon.Cyan : Accent; }
            WorkPaint.Plate(graphics, box, 5 * s, face, rim, line);
            if (Chevron)
            {
                float left = 10 * s;
                if (Caption.Length > 0 && CaptionFont != null)
                {
                    int wide = WorkPaint.Wide(Caption, CaptionFont);
                    TextRenderer.DrawText(graphics, Caption, CaptionFont, new Rectangle((int)left, 0, wide + 2, Height), Enabled ? Neon.Faint : ink2, WorkPaint.Line);
                    left += wide + 7 * s;
                }
                WorkPaint.Drop(graphics, Width - 19 * s, y, s, Enabled ? Neon.Cyan : ink2, hot);
                TextRenderer.DrawText(graphics, Text, Font, new Rectangle((int)left, 0, Math.Max(1, (int)(Width - 24 * s - left)), Height), ink2, WorkPaint.Line | TextFormatFlags.EndEllipsis);
                return;
            }
            if (Solid && Enabled && Focused && ShowFocusCues)
                WorkPaint.Plate(graphics, RectangleF.Inflate(box, -3 * s, -3 * s), 4 * s, Color.Empty, Neon.OnInk, line);
            TextRenderer.DrawText(graphics, Text, Font, ClientRectangle, ink2, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    // A one-line text box in her look, like the Mainframe's: a borderless box in a dark well with a cut corner, cyan while
    // it has the cursor, and a faint cue while it's empty.
    internal sealed class WorkField : Control
    {
        internal readonly TextBox Box = new TextBox();
        private string cue = "";

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wide, string text);

        internal WorkField()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.IBeam;
            Box.BorderStyle = BorderStyle.None;
            Box.BackColor = Neon.Well;
            Box.ForeColor = Neon.White;
            Box.GotFocus += delegate { Invalidate(); };
            Box.LostFocus += delegate { Invalidate(); };
            Box.HandleCreated += delegate { ShowCue(); };
            Controls.Add(Box);
        }

        internal string Cue { get { return cue; } set { cue = value ?? ""; ShowCue(); } }

        private void ShowCue() { if (Box.IsHandleCreated) SendMessage(Box.Handle, 0x1501, new IntPtr(1), cue); }

        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Box.Font = Font; PerformLayout(); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            float s = WorkPaint.Scale(this);
            int left = (int)(10 * s), height = Box.PreferredHeight;
            Box.SetBounds(left, (Height - height) / 2, Math.Max(1, Width - left * 2), height);
        }

        protected override void OnMouseDown(MouseEventArgs e) { Box.Focus(); base.OnMouseDown(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            float s = WorkPaint.Scale(this), line = Math.Max(1f, s);
            WorkPaint.Behind(e.Graphics, this);
            WorkPaint.Plate(e.Graphics, new RectangleF(line / 2, line / 2, Width - line, Height - line), 5 * s, Neon.Well, Box.Focused ? Neon.Cyan : Neon.WellEdge, line);
        }
    }

    // The regular Explorer windows for adding folders and files: Windows' own folder picker (IFileOpenDialog with
    // FOS_PICKFOLDERS), which takes several folders at once, and the file picker with Multiselect.
    internal static class WorkPickers
    {
        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")] private class FileOpenDialogClass { }

        [ComImport, Guid("d57c7288-d4ad-4768-be02-9d969532d960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint count, IntPtr types);
            void SetFileTypeIndex(uint index);
            void GetFileTypeIndex(out uint index);
            void Advise(IntPtr events, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint options);
            void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem folder);
            void SetFolder(IShellItem folder);
            void GetFolder(out IShellItem folder);
            void GetCurrentSelection(out IShellItem item);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem item);
            void AddPlace(IShellItem item, int place);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
            void Close(int result);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr filter);
            void GetResults(out IShellItemArray items);
            void GetSelectedItems(out IShellItemArray items);
        }

        [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr context, ref Guid handler, ref Guid id, out IntPtr result);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint kind, out IntPtr name);
            void GetAttributes(uint mask, out uint attributes);
            void Compare(IShellItem other, uint hint, out int order);
        }

        [ComImport, Guid("b63ea76d-1f85-456f-a19c-48159efa858b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemArray
        {
            void BindToHandler(IntPtr context, ref Guid handler, ref Guid id, out IntPtr result);
            void GetPropertyStore(int flags, ref Guid id, out IntPtr store);
            void GetPropertyDescriptionList(IntPtr key, ref Guid id, out IntPtr list);
            void GetAttributes(int how, uint mask, out uint attributes);
            void GetCount(out uint count);
            void GetItemAt(uint index, out IShellItem item);
            void EnumItems(out IntPtr items);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(string path, IntPtr context, ref Guid id, out IShellItem item);

        private const uint PickFolders = 0x20, ForceFileSystem = 0x40, AllowMultiSelect = 0x200, PathMustExist = 0x800, NoChangeDir = 0x8;

        // Empty when they cancel.
        internal static string[] Folders(IWin32Window owner, string title, string start)
        {
            IFileOpenDialog dialog = (IFileOpenDialog)new FileOpenDialogClass();
            try
            {
                uint options;
                dialog.GetOptions(out options);
                dialog.SetOptions(options | PickFolders | ForceFileSystem | AllowMultiSelect | PathMustExist | NoChangeDir);
                dialog.SetTitle(title);
                dialog.SetOkButtonLabel("Add");
                if (!string.IsNullOrEmpty(start) && Directory.Exists(start))
                {
                    try
                    {
                        Guid item = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");
                        IShellItem folder;
                        SHCreateItemFromParsingName(start, IntPtr.Zero, ref item, out folder);
                        dialog.SetFolder(folder);
                        Marshal.ReleaseComObject(folder);
                    }
                    catch (Exception) { }
                }
                int shown = dialog.Show(owner == null ? IntPtr.Zero : owner.Handle);
                if (shown != 0) return new string[0];
                IShellItemArray results;
                dialog.GetResults(out results);
                uint count;
                results.GetCount(out count);
                List<string> paths = new List<string>();
                for (uint i = 0; i < count; i++)
                {
                    IShellItem item;
                    results.GetItemAt(i, out item);
                    IntPtr name;
                    item.GetDisplayName(0x80058000, out name);
                    try { string path = Marshal.PtrToStringUni(name); if (!string.IsNullOrEmpty(path)) paths.Add(path); }
                    finally { Marshal.FreeCoTaskMem(name); Marshal.ReleaseComObject(item); }
                }
                Marshal.ReleaseComObject(results);
                return paths.ToArray();
            }
            finally { Marshal.ReleaseComObject(dialog); }
        }

        internal static string[] Files(IWin32Window owner, string title, string start)
        {
            using (OpenFileDialog picker = new OpenFileDialog { Multiselect = true, Title = title, CheckFileExists = true, RestoreDirectory = true, Filter = "All files (*.*)|*.*" })
            {
                if (!string.IsNullOrEmpty(start) && Directory.Exists(start)) picker.InitialDirectory = start;
                return picker.ShowDialog(owner) == DialogResult.OK ? picker.FileNames : new string[0];
            }
        }
    }
}

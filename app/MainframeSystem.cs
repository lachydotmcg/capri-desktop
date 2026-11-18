using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Mainframe
    {
        private Key resourcesKey;
        private Label resourcesLine;
        private ResourcesSheet resourcesSheet;
        private readonly SystemUse lineUse = new SystemUse();
        private readonly System.Windows.Forms.Timer resourcesClock = new System.Windows.Forms.Timer { Interval = 2000 };

        private void MakeResourcesKey(int y)
        {
            resourcesKey = Lower(Make("Resources", 28, y, 112, 22, delegate { ToggleResources(); }));
            resourcesKey.Chevron = true;
            resourcesKey.AccessibleName = "Resources: what's using your CPU, RAM and GPU, Capri's parts apart from other programs";
            resourcesLine = Lower(Caption("", 148, y, Look.Mono(8.5f, FontStyle.Regular, GraphicsUnit.Point), Faint));
            resourcesLine.AutoSize = false;
            resourcesLine.AutoEllipsis = true;
            resourcesLine.TextAlign = ContentAlignment.MiddleRight;
            resourcesLine.Bounds = new Rectangle(148, y, 208, 22);
            resourcesLine.AccessibleName = "CPU and RAM in use";
            resourcesClock.Tick += delegate { ShowResourcesLine(); };
            VisibleChanged += delegate { resourcesClock.Enabled = Visible; if (Visible) ShowResourcesLine(); };
        }

        private void ShowResourcesLine()
        {
            if (resourcesLine == null || WindowState == FormWindowState.Minimized) return;
            long used, total;
            SystemUse.Memory(out used, out total);
            double cpu = lineUse.TotalCpu();
            resourcesLine.Text = ResourcesWords(cpu, used, total);
        }

        internal static string ResourcesWords(double cpu, long used, long total)
        {
            string ram = total > 0 ? "RAM " + UseView.Of(used, total).Replace(" of ", "/") : "RAM -";
            return (cpu < 0 ? "CPU -" : "CPU " + UseView.Percent(cpu)) + " · " + ram;
        }

        internal string ResourcesLine { get { return resourcesLine == null ? null : resourcesLine.Text; } }

        internal Rectangle ResourcesBounds { get { return ResourcesOpen ? resourcesSheet.Bounds : Rectangle.Empty; } }

        internal bool ResourcesOpen { get { return resourcesSheet != null && !resourcesSheet.IsDisposed && resourcesSheet.Visible; } }

        internal void ToggleResources()
        {
            if (ResourcesOpen) { resourcesSheet.Close(); return; }
            float fit = SheetZoom(ResourcesSheet.Wide, ResourcesSheet.Tall);
            if (resourcesSheet != null && !resourcesSheet.IsDisposed && resourcesSheet.Zoom != fit) resourcesSheet.Close();
            if (resourcesSheet == null || resourcesSheet.IsDisposed)
            {
                resourcesSheet = new ResourcesSheet(this, fit, true);
                resourcesSheet.FormClosed += delegate { resourcesKey.Pick(false); };
            }
            if (!resourcesSheet.Visible) resourcesSheet.Show(this);
            resourcesSheet.Dock();
            resourcesKey.Pick(true);
        }

        internal string ResourcesSheetAt(float zoom, UseView view, string picture)
        {
            using (ResourcesSheet sheet = new ResourcesSheet(this, zoom, false))
            {
                sheet.Fill(view);
                sheet.Show(this);
                sheet.Dock();
                string crowded = sheet.Crowded();
                if (crowded == null) crowded = sheet.Shows(view);
                if (picture != null) sheet.Picture(picture);
                sheet.Close();
                return crowded;
            }
        }

        internal string ResourcesDocked(string picture)
        {
            bool wasShown = Visible;
            FormWindowState wasState = WindowState;
            Point wasAt = Location;
            try
            {
                Restore();
                Application.DoEvents();
                if (picture != null)
                    using (Bitmap shot = new Bitmap(ClientSize.Width, ClientSize.Height))
                    {
                        DrawToBitmap(shot, new Rectangle(Point.Empty, shot.Size));
                        shot.Save(picture, System.Drawing.Imaging.ImageFormat.Png);
                    }
                ToggleResources();
                if (!ResourcesOpen || !resourcesKey.Picked) return "Resources didn't open its panel or light its key";
                Rectangle area = Screen.FromControl(this).WorkingArea, under = resourcesSheet.Bounds;
                bool docked = under.Top == Bottom || under.Bottom == Top || under.Bottom == area.Bottom;
                bool centred = Math.Abs(under.Left + under.Width / 2 - (Left + Width / 2)) <= 2 || under.Left == area.Left || under.Right == area.Right;
                if (!docked || !centred) return "the panel " + under + " isn't docked to the Mainframe " + Bounds;
                Location = new Point(Left - 40, Top);
                Application.DoEvents();
                Rectangle moved = resourcesSheet.Bounds;
                if (moved.Left != under.Left - 40 && moved.Left != area.Left) return "the panel didn't follow the Mainframe: " + moved + " after " + under;
                ToggleResources();
                if (ResourcesOpen || resourcesKey.Picked) return "Resources didn't close its panel or unlight its key";
                return null;
            }
            finally
            {
                if (ResourcesOpen) resourcesSheet.Close();
                Location = wasAt;
                if (!wasShown) Hide();
                else if (WindowState != wasState) WindowState = wasState;
            }
        }

        private sealed class UseBar : Control
        {
            private double ours, theirs;

            internal UseBar()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                BackColor = Well;
                TabStop = false;
                AccessibleRole = AccessibleRole.ProgressBar;
            }

            internal double Ours { get { return ours; } }
            internal double Theirs { get { return theirs; } }

            internal void Show(double capri, double rest)
            {
                ours = Math.Max(0, Math.Min(100, capri));
                theirs = Math.Max(0, Math.Min(100 - ours, rest));
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics graphics = e.Graphics;
                using (Brush fill = new SolidBrush(Well)) graphics.FillRectangle(fill, ClientRectangle);
                float inner = Math.Max(0, Width - 2);
                float capri = (float)(inner * ours / 100), rest = (float)(inner * theirs / 100);
                if (capri > 0) using (Brush fill = new SolidBrush(Cyan)) graphics.FillRectangle(fill, 1, 1, Math.Max(1, capri), Height - 2);
                if (rest > 0) using (Brush fill = new SolidBrush(Rose)) graphics.FillRectangle(fill, 1 + capri, 1, rest, Height - 2);
                using (Pen rim = new Pen(WellEdge)) graphics.DrawRectangle(rim, 0, 0, Width - 1, Height - 1);
            }
        }

        private sealed class ResourcesSheet : ModSheet
        {
            internal const int Wide = 380, Tall = 600, Mine = 5, Theirs = 6;
            private static readonly string[] Meters = { "CPU", "RAM", "GPU" };
            private readonly bool live;
            private readonly UseBar[] bars = new UseBar[3];
            private readonly Label[] amounts = new Label[3];
            private readonly Label[][] mine = new Label[Mine][], theirs = new Label[Theirs][];
            private readonly Label summary, capriTotal, othersTotal, gpuNote;
            private readonly Slider brainVram;
            private readonly System.Windows.Forms.Timer clock = new System.Windows.Forms.Timer { Interval = 2000 };
            private readonly SystemUse use = new SystemUse();
            private long vram = -1;
            private string gpuName;
            private int sampling;
            private UseView shown;

            internal ResourcesSheet(Mainframe owner, float zoom, bool live) : base(owner, zoom, "Capri resources", "RESOURCES >", Wide, Tall)
            {
                this.live = live;
                Words("What's using your PC right now", 28, 52, 328, 16, Small(), Faint);
                Font name = Type(9.5f * zoom, true), value = Type(9.5f * zoom);
                for (int i = 0; i < Meters.Length; i++)
                {
                    int y = 80 + 26 * i;
                    Words(Meters[i], 28, y, 40, 18, name, Rose);
                    bars[i] = new UseBar();
                    bars[i].Bounds = Box(72, y + 3, 144, 12);
                    bars[i].AccessibleName = Meters[i] + " use";
                    Controls.Add(bars[i]);
                    amounts[i] = Words("-", 222, y, 134, 18, value, White);
                    amounts[i].TextAlign = ContentAlignment.MiddleRight;
                }
                gpuNote = Words("", 28, 158, 328, 16, Small(), Faint);
                Words("Brain VRAM", 28, 183, 160, 18, name, Rose);
                brainVram = new Slider(Brain.GpuLimitNames);
                brainVram.Font = Type(9.5f * zoom);
                brainVram.Bounds = Box(196, 181, 160, 22);
                brainVram.AccessibleName = "Brain VRAM: the most graphics card memory her built-in brain or Ollama takes. The rest runs in RAM";
                brainVram.Value = Math.Max(0, Array.IndexOf(Brain.GpuLimits, Frame.capri.GpuLimit));
                brainVram.Changed += delegate { Frame.capri.SetGpuLimit(Brain.GpuLimits[brainVram.Value]); };
                Controls.Add(brainVram);
                Words("The rest of her brain runs in RAM: slower replies, more room for your games.", 28, 208, 328, 30, Small(), Faint);
                Font band = Type(10f * zoom, true);
                Heading(this, "CAPRI >", new Point(Z(28), Z(250)), band, White, 352);
                capriTotal = Words("", 150, 249, 206, 18, Small(), Cyan);
                capriTotal.TextAlign = ContentAlignment.MiddleRight;
                string[] heads = { "CPU", "RAM", "GPU", "VRAM" };
                for (int c = 0; c < heads.Length; c++)
                {
                    Label head = Words(heads[c], ColumnAt(c), 276, ColumnWide(c), 14, Small(), Faint);
                    head.TextAlign = ContentAlignment.MiddleRight;
                }
                for (int i = 0; i < Mine; i++) mine[i] = Row(294 + 20 * i, Cyan);
                Heading(this, "OTHER PROGRAMS >", new Point(Z(28), Z(408)), band, White, 352);
                othersTotal = Words("", 150, 407, 206, 18, Small(), Rose);
                othersTotal.TextAlign = ContentAlignment.MiddleRight;
                for (int i = 0; i < Theirs; i++) theirs[i] = Row(434 + 20 * i, White);
                summary = Words("", 28, 556, 328, 34, Type(9f * zoom), Cyan);
                clock.Tick += delegate { Sample(); };
                Frame.Fit(this);
                if (!live) return;
                Sample();
                clock.Start();
            }

            private static int ColumnAt(int c) { return c == 0 ? 164 : c == 1 ? 204 : c == 2 ? 262 : 298; }

            private static int ColumnWide(int c) { return c == 0 ? 36 : c == 1 ? 54 : c == 2 ? 32 : 58; }

            private Label[] Row(int y, Color colour)
            {
                Label[] row = new Label[5];
                row[0] = Words("", 28, y, 132, 18, Type(9.5f * Zoom), colour);
                for (int c = 0; c < 4; c++)
                {
                    row[c + 1] = Words("", ColumnAt(c), y, ColumnWide(c), 18, Type(9f * Zoom), c == 0 ? White : Faint);
                    row[c + 1].TextAlign = ContentAlignment.MiddleRight;
                }
                return row;
            }

            private void Sample()
            {
                if (Interlocked.CompareExchange(ref sampling, 1, 0) != 0) return;
                int brain = Frame.capri.BrainServerId;
                bool first = shown == null;
                Run(delegate
                {
                    UseView view = null;
                    try
                    {
                        if (vram < 0) { Machine found = Machine.Read(); gpuName = found.Gpu; vram = found.Vram; }
                        view = use.Sample(brain, true);
                        if (first) { Thread.Sleep(700); view = use.Sample(brain, true); }
                    }
                    catch (Exception error) { Brain.Log("resources", -1, error.GetType().Name + ": " + error.Message); }
                    finally { Interlocked.Exchange(ref sampling, 0); }
                    if (view != null) Back(delegate { Fill(view); });
                });
            }

            internal void Fill(UseView view)
            {
                shown = view;
                if (view.VramTotal <= 0) view.VramTotal = Math.Max(0, vram);
                if (view.GpuName == null) view.GpuName = gpuName;
                bars[0].Show(view.CapriCpu, Math.Max(0, view.Cpu - view.CapriCpu));
                amounts[0].Text = UseView.Percent(view.Cpu);
                double total = view.RamTotal > 0 ? view.RamTotal : 1;
                double capriRam = 100.0 * view.CapriTotal.Ram / total;
                bars[1].Show(capriRam, Math.Max(0, 100.0 * view.RamUsed / total - capriRam));
                amounts[1].Text = view.RamTotal > 0 ? UseView.Of(view.RamUsed, view.RamTotal) : "-";
                if (view.Gpu >= 0)
                {
                    double capriGpu = Math.Min(view.Gpu, Math.Max(0, view.CapriTotal.Gpu));
                    bars[2].Show(capriGpu, view.Gpu - capriGpu);
                    amounts[2].Text = UseView.Percent(view.Gpu) + (view.VramUsed >= 0 && view.VramTotal > 0 ? " · " + UseView.Of(view.VramUsed, view.VramTotal) : "");
                }
                else
                {
                    bars[2].Show(0, 0);
                    amounts[2].Text = "-";
                }
                gpuNote.Text = view.Gpu >= 0 ? "Cyan is Capri, red is everything else" + (string.IsNullOrEmpty(view.GpuName) ? "" : " · " + Shorter(view.GpuName)) : "Cyan is Capri, red is everything else · Windows didn't share GPU use";
                capriTotal.Text = Totals(view.CapriTotal);
                othersTotal.Text = Totals(view.OthersTotal);
                Put(mine, view.Capri);
                Put(theirs, view.Others);
                summary.Text = Summary(view);
            }

            private static string Shorter(string gpu) { return gpu.Replace("NVIDIA GeForce ", "").Replace("AMD Radeon ", "Radeon ").Replace("(R)", "").Replace("(TM)", ""); }

            private static string Totals(UseRow row)
            {
                string words = UseView.Percent(row.Cpu) + " CPU · " + UseView.Bytes(row.Ram);
                if (row.Gpu >= 0) words += " · " + UseView.Percent(row.Gpu) + " GPU";
                return words;
            }

            private void Put(Label[][] rows, List<UseRow> found)
            {
                for (int i = 0; i < rows.Length; i++)
                {
                    UseRow row = i < found.Count ? found[i] : null;
                    rows[i][0].Text = row == null ? "" : row.Count > 1 ? row.Name + " ×" + row.Count : row.Name;
                    rows[i][1].Text = row == null ? "" : UseView.Percent(row.Cpu);
                    rows[i][2].Text = row == null ? "" : UseView.Bytes(row.Ram);
                    rows[i][3].Text = row == null ? "" : UseView.Percent(row.Gpu);
                    rows[i][4].Text = row == null ? "" : row.Vram > 0 ? UseView.Bytes(row.Vram) : row.Vram == 0 ? "0 MB" : "-";
                }
            }

            internal static string Summary(UseView view)
            {
                if (view.RamTotal <= 0) return "";
                double ram = 100.0 * view.CapriTotal.Ram / view.RamTotal;
                string most = view.Others.Count > 0 ? view.Others[0].Name : null;
                return "Capri uses " + UseView.Percent(view.CapriCpu) + " of your CPU and " + UseView.Percent(ram) + " of your RAM." + (most == null ? "" : " Using the most: " + most + ".");
            }

            internal string Shows(UseView view)
            {
                if (amounts[0].Text != UseView.Percent(view.Cpu)) return "CPU says " + amounts[0].Text;
                if (view.Capri.Count > 0 && !mine[0][0].Text.StartsWith(view.Capri[0].Name, StringComparison.Ordinal)) return "Capri's first row says " + mine[0][0].Text;
                if (view.Others.Count > 0 && !theirs[0][0].Text.StartsWith(view.Others[0].Name, StringComparison.Ordinal)) return "the first other program says " + theirs[0][0].Text;
                foreach (UseBar bar in bars) if (bar.Ours + bar.Theirs > 100.01) return "a bar runs past full";
                if (summary.Text.Length == 0) return "no summary";
                foreach (Label amount in amounts)
                    if (TextRenderer.MeasureText(amount.Text, amount.Font).Width > amount.Width) return "a meter's amount is cut: " + amount.Text;
                foreach (Label[][] rows in new[] { mine, theirs })
                    foreach (Label[] row in rows)
                        for (int c = 1; c < row.Length; c++)
                            if (row[c].Text.Length > 0 && TextRenderer.MeasureText(row[c].Text, row[c].Font).Width > row[c].Width) return "a number is cut: " + row[c].Text;
                return null;
            }

            internal void Dock()
            {
                if (Frame.WindowState == FormWindowState.Minimized) return;
                Rectangle area = Screen.FromControl(Frame).WorkingArea;
                int x = Frame.Left + (Frame.Width - Width) / 2;
                int y = Frame.Bottom;
                if (y + Height > area.Bottom) y = Frame.Top - Height >= area.Top ? Frame.Top - Height : area.Bottom - Height;
                Location = new Point(Math.Max(area.Left, Math.Min(area.Right - Width, x)), Math.Max(area.Top, y));
            }

            protected override void OnLoad(EventArgs e)
            {
                base.OnLoad(e);
                Dock();
                Frame.LocationChanged += Follow;
                Frame.SizeChanged += Follow;
            }

            private void Follow(object sender, EventArgs e) { if (Visible && Frame.WindowState != FormWindowState.Minimized) Dock(); }

            protected override void OnFormClosed(FormClosedEventArgs e)
            {
                clock.Stop();
                Frame.LocationChanged -= Follow;
                Frame.SizeChanged -= Follow;
                base.OnFormClosed(e);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) clock.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}

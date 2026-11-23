using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Mainframe
    {
        private Key spendKey;
        private Label spendLine;
        private SpendSheet spendSheet;
        private volatile bool spendPending;

        private void MakeSpendKey(int y)
        {
            spendKey = Lower(Make("Spend", 28, y, 112, 22, delegate { ToggleSpend(); }));
            spendKey.Chevron = true;
            spendKey.AccessibleName = "Spend: what her brain cost on your API keys today and this month, model by model";
            spendLine = Lower(Caption("", 148, y, Look.Mono(8.5f, FontStyle.Regular, GraphicsUnit.Point), Faint));
            spendLine.AutoSize = false;
            spendLine.AutoEllipsis = true;
            spendLine.TextAlign = ContentAlignment.MiddleRight;
            spendLine.Bounds = new Rectangle(148, y, 208, 22);
            spendLine.AccessibleName = "Brain spend today and this month";
            Spend.Changed += SpendChanged;
            VisibleChanged += delegate { if (Visible) ShowSpendLine(); };
            Disposed += delegate { Spend.Changed -= SpendChanged; };
        }

        private void SpendChanged()
        {
            if (spendPending || !IsHandleCreated || IsDisposed) return;
            spendPending = true;
            try { BeginInvoke(new MethodInvoker(delegate { spendPending = false; ShowSpendLine(); if (SpendOpen) spendSheet.Fill(); })); }
            catch (InvalidOperationException) { spendPending = false; }
        }

        private void ShowSpendLine()
        {
            if (spendLine == null || IsDisposed) return;
            spendLine.Text = Spend.Line();
        }

        internal string SpendLine { get { return spendLine == null ? null : spendLine.Text; } }

        internal bool SpendOpen { get { return spendSheet != null && !spendSheet.IsDisposed && spendSheet.Visible; } }

        internal void ToggleSpend()
        {
            if (SpendOpen) { spendSheet.Close(); return; }
            if (ResourcesOpen) resourcesSheet.Close();
            float fit = SheetZoom(SpendSheet.Wide, SpendSheet.Tall);
            if (spendSheet != null && !spendSheet.IsDisposed && spendSheet.Zoom != fit) spendSheet.Close();
            if (spendSheet == null || spendSheet.IsDisposed)
            {
                spendSheet = new SpendSheet(this, fit);
                spendSheet.FormClosed += delegate { spendKey.Pick(false); };
            }
            spendSheet.Fill();
            if (!spendSheet.Visible) spendSheet.Show(this);
            spendSheet.Dock();
            spendKey.Pick(true);
        }

        internal string SpendSheetAt(float zoom, List<SpendRow> today, List<SpendRow> month, string main, string small, string picture)
        {
            using (SpendSheet sheet = new SpendSheet(this, zoom))
            {
                sheet.Fill(today, month, main, small);
                sheet.Show(this);
                sheet.Dock();
                string crowded = sheet.Crowded() ?? sheet.Shows(today, month, small);
                if (picture != null) sheet.Picture(picture);
                sheet.Close();
                return crowded;
            }
        }

        internal string SpendDocked()
        {
            bool wasShown = Visible;
            FormWindowState wasState = WindowState;
            try
            {
                Restore();
                Application.DoEvents();
                ToggleResources();
                ToggleSpend();
                if (!SpendOpen || !spendKey.Picked) return "Spend didn't open its panel or light its key";
                if (ResourcesOpen) return "the Resources panel stayed open under the Spend panel";
                Rectangle area = Screen.FromControl(this).WorkingArea, under = spendSheet.Bounds;
                bool docked = under.Top == Bottom || under.Bottom == Top || under.Bottom == area.Bottom;
                if (!docked) return "the Spend panel " + under + " isn't docked to the Mainframe " + Bounds;
                ToggleSpend();
                if (SpendOpen || spendKey.Picked) return "Spend didn't close its panel or unlight its key";
                return null;
            }
            finally
            {
                if (SpendOpen) spendSheet.Close();
                if (ResourcesOpen) resourcesSheet.Close();
                if (!wasShown) Hide();
                else if (WindowState != wasState) WindowState = wasState;
            }
        }

        internal static string SmallJobsWords(string main, string small)
        {
            if (string.IsNullOrEmpty(main)) return "Pick a brain with an API key and she'll count what it costs here.";
            if (string.IsNullOrEmpty(small)) return "Small jobs stay on " + main + ": no cheaper model from the same provider is on your key.";
            return "Comments, glances, quick tool picks, translations and Code Mode summaries go to " + small + " instead of " + main + ".";
        }

        private sealed class SpendSheet : ModSheet
        {
            internal const int Wide = 380, Tall = 490, Rows = 8;
            private readonly Label today, month, monthTotal, smallJobs;
            private readonly Label[][] rows = new Label[Rows][];

            internal SpendSheet(Mainframe owner, float zoom) : base(owner, zoom, "Capri spend", "SPEND >", Wide, Tall)
            {
                Words("What her brain cost on your API keys", 28, 52, 328, 16, Small(), Faint);
                Font name = Type(9.5f * zoom, true), big = Type(13f * zoom, true);
                Words("TODAY", 28, 82, 120, 22, name, Rose);
                today = Words("$0", 196, 78, 160, 26, big, White);
                today.TextAlign = ContentAlignment.MiddleRight;
                Words("THIS MONTH", 28, 112, 120, 22, name, Rose);
                month = Words("$0", 196, 108, 160, 26, big, Cyan);
                month.TextAlign = ContentAlignment.MiddleRight;
                Font band = Type(10f * zoom, true);
                Heading(this, "MODELS >", new Point(Z(28), Z(150)), band, White, 352);
                monthTotal = Words("", 150, 149, 206, 18, Small(), Faint);
                monthTotal.TextAlign = ContentAlignment.MiddleRight;
                string[] heads = { "Asks", "Tokens", "Cost" };
                for (int c = 0; c < heads.Length; c++)
                {
                    Label head = Words(heads[c], ColumnAt(c), 174, ColumnWide(c), 14, Small(), Faint);
                    head.TextAlign = ContentAlignment.MiddleRight;
                }
                for (int i = 0; i < Rows; i++)
                {
                    int y = 192 + 20 * i;
                    rows[i] = new Label[4];
                    rows[i][0] = Words("", 28, y, 150, 18, Type(9.5f * zoom), White);
                    for (int c = 0; c < 3; c++)
                    {
                        rows[i][c + 1] = Words("", ColumnAt(c), y, ColumnWide(c), 18, Type(9f * zoom), c == 2 ? Cyan : Faint);
                        rows[i][c + 1].TextAlign = ContentAlignment.MiddleRight;
                    }
                }
                Heading(this, "SMALL JOBS >", new Point(Z(28), Z(360)), band, White, 352);
                smallJobs = Words("", 28, 386, 328, 52, Type(9f * zoom), Cyan);
                Words("List prices from each provider. Free tiers, credits and discounts aren't taken off.", 28, 446, 328, 30, Small(), Faint);
                Frame.Fit(this);
            }

            private static int ColumnAt(int c) { return c == 0 ? 182 : c == 1 ? 230 : 286; }

            private static int ColumnWide(int c) { return c == 0 ? 44 : c == 1 ? 52 : 70; }

            internal void Fill()
            {
                Fill(Spend.Today(), Spend.Month(), Frame.capri.BrainModelShown, Frame.capri.SmallJobs);
            }

            internal void Fill(List<SpendRow> todays, List<SpendRow> months, string main, string small)
            {
                today.Text = Spend.Money(Spend.Total(todays));
                month.Text = Spend.Money(Spend.Total(months));
                int asks = 0;
                long tokens = 0;
                foreach (SpendRow row in months) { asks += row.Asks; tokens += row.Tokens; }
                monthTotal.Text = months.Count == 0 ? "nothing yet this month" : asks + (asks == 1 ? " ask · " : " asks · ") + Spend.Tokens(tokens) + " tokens";
                for (int i = 0; i < Rows; i++)
                {
                    SpendRow row = i < months.Count ? months[i] : null;
                    rows[i][0].Text = row == null ? "" : RowName(row);
                    rows[i][1].Text = row == null ? "" : row.Asks.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    rows[i][2].Text = row == null ? "" : Spend.Tokens(row.Tokens);
                    rows[i][3].Text = row == null ? "" : Cost(row);
                }
                smallJobs.Text = SmallJobsWords(main, small);
            }

            internal static string RowName(SpendRow row)
            {
                string model = row.Model ?? "";
                return row.Kind != null && row.Kind.StartsWith("other:", StringComparison.Ordinal) ? Spend.Company(row.Kind) + " · " + model : model;
            }

            internal static string Cost(SpendRow row)
            {
                if (Spend.Free(row.Kind, row.Model)) return "free";
                if (row.Cost == 0 && row.Unpriced > 0) return "?";
                return Spend.Money(row.Cost);
            }

            internal string Shows(List<SpendRow> todays, List<SpendRow> months, string small)
            {
                if (today.Text != Spend.Money(Spend.Total(todays))) return "today says " + today.Text;
                if (month.Text != Spend.Money(Spend.Total(months))) return "this month says " + month.Text;
                if (months.Count > 0 && rows[0][0].Text != RowName(months[0])) return "the first row says " + rows[0][0].Text;
                if (small != null && smallJobs.Text.IndexOf(small, StringComparison.Ordinal) < 0) return "small jobs say " + smallJobs.Text;
                foreach (Label amount in new[] { today, month })
                    if (TextRenderer.MeasureText(amount.Text, amount.Font).Width > amount.Width) return "an amount is cut: " + amount.Text;
                foreach (Label[] row in rows)
                    for (int c = 1; c < row.Length; c++)
                        if (row[c].Text.Length > 0 && TextRenderer.MeasureText(row[c].Text, row[c].Font).Width > row[c].Width) return "a number is cut: " + row[c].Text;
                Size words = TextRenderer.MeasureText(smallJobs.Text, smallJobs.Font, new Size(smallJobs.Width, int.MaxValue), TextFormatFlags.WordBreak);
                if (words.Height > smallJobs.Height) return "the small jobs line is cut: " + smallJobs.Text;
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
                Frame.LocationChanged -= Follow;
                Frame.SizeChanged -= Follow;
                base.OnFormClosed(e);
            }
        }
    }
}

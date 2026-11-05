using System;
using System.Drawing;
using System.IO;
using System.Media;
using System.Reflection;
using System.Windows.Forms;

namespace CapriDesktop
{
    // A note from Capri or her installer in her look, instead of Windows' grey box: its title in rose, or hot red when
    // something went wrong, the message under it and OK. Enter or Esc closes it, and Ctrl + C copies it like Windows' own.
    internal sealed class Notice : Form, LookWindow
    {
        private const int Wide = 440;
        private readonly string caption;
        private readonly string message;
        private readonly Label title;
        private readonly Label body;
        private readonly Plate ok;

        // Shows a notice and waits for OK. If her own window can't show, Windows' box says it instead.
        internal static void Tell(IWin32Window owner, string message, string caption, bool error)
        {
            try
            {
                using (Notice notice = new Notice(message, caption, error))
                {
                    if (owner == null) { notice.ShowInTaskbar = true; notice.StartPosition = FormStartPosition.CenterScreen; }
                    if (error) SystemSounds.Hand.Play(); else SystemSounds.Asterisk.Play();
                    notice.ShowDialog(owner);
                }
            }
            catch (Exception)
            {
                MessageBoxIcon icon = error ? MessageBoxIcon.Error : MessageBoxIcon.Information;
                if (owner == null) MessageBox.Show(message, caption, MessageBoxButtons.OK, icon);
                else MessageBox.Show(owner, message, caption, MessageBoxButtons.OK, icon);
            }
        }

        internal Notice(string message, string caption, bool error)
        {
            this.message = message;
            this.caption = caption;
            Text = caption;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Neon.Ground;
            ForeColor = Neon.White;
            DoubleBuffered = true;
            float s = DeviceDpi / 96f;
            Font = Neon.Type(10.5f);
            using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("Capri.Chip.ico")) if (source != null) Icon = new Icon(source);
            int wide = Px(Wide, s);
            int inset = Px(26, s);
            title = new Neon.Caption();
            title.Text = caption.ToUpperInvariant();
            title.Font = Neon.Display(15f);
            title.ForeColor = error ? Neon.Hot : Neon.Rose;
            title.AutoSize = true;
            title.Location = new Point(inset, Px(18, s));
            Controls.Add(title);
            body = new Neon.Caption();
            body.Text = message;
            body.Font = Font;
            body.ForeColor = Neon.White;
            body.MaximumSize = new Size(wide - 2 * inset, 0);
            body.AutoSize = true;
            body.Location = new Point(inset, title.Bottom + Px(12, s));
            Controls.Add(body);
            ok = new Plate();
            ok.Text = "OK";
            ok.Accent = Neon.Cyan;
            ok.Font = Neon.Type(10f, true);
            ok.Size = new Size(Px(96, s), Px(30, s));
            ok.Location = new Point(wide - inset - ok.Width, body.Bottom + Px(22, s));
            ok.DialogResult = DialogResult.OK;
            Controls.Add(ok);
            AcceptButton = ok;
            CancelButton = ok;
            ClientSize = new Size(wide, ok.Bottom + Px(22, s));
            Neon.Clip(this, 16 * s, 6 * s);
        }

        private static int Px(float value, float s) { return (int)Math.Round(value * s); }

        internal string Said { get { return title.Text + "\n" + body.Text; } }

        internal bool Fits { get { return body.PreferredSize.Height <= body.Height && body.Right <= ClientSize.Width && ok.Bottom <= ClientSize.Height && ok.Top >= body.Bottom; } }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ok.Focus();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.C)
            {
                try { Clipboard.SetText(caption + Environment.NewLine + Environment.NewLine + message); } catch (System.Runtime.InteropServices.ExternalException) { }
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        // It drags from anywhere that isn't the button.
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == 0x0084 && message.Result == new IntPtr(1)) message.Result = new IntPtr(2);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { Neon.Backdrop(e.Graphics, ClientSize, 210 * DeviceDpi / 96f); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            float s = DeviceDpi / 96f;
            Neon.Frame(e.Graphics, ClientSize, s, 16 * s, 6 * s, 52 * s, 26 * s);
        }
    }
}

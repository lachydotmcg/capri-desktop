using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Mainframe
    {
        private DiscordBotSheet discordBotSheet;

        private void MakeDiscordBotKey(int y)
        {
            if (!DiscordCompanion.Included) return;
            Key key = Lower(Make("Discord Bot", 28, y, 128, 22, delegate { OpenDiscordBot(); }));
            key.Chevron = true;
            key.AccessibleName = "Discord Bot. Set up Capri’s own Discord bot for your server.";
        }

        private void OpenDiscordBot()
        {
            float zoom = SheetZoom(DiscordBotSheet.Wide, DiscordBotSheet.Tall);
            if (discordBotSheet != null && !discordBotSheet.IsDisposed && discordBotSheet.Zoom != zoom) discordBotSheet.Close();
            if (discordBotSheet == null || discordBotSheet.IsDisposed) discordBotSheet = new DiscordBotSheet(this, zoom, capri.DiscordBot);
            if (!discordBotSheet.Visible) discordBotSheet.Show(this);
            discordBotSheet.Activate();
        }

        private sealed class DiscordBotSheet : ModSheet
        {
            internal const int Wide = 620, Tall = 568;
            private readonly DiscordCompanion bot;
            private readonly Entry token;
            private readonly Key connect, invite, forget, stop;
            private readonly Label status;
            private readonly Timer update = new Timer { Interval = 450 };

            internal DiscordBotSheet(Mainframe frame, float zoom, DiscordCompanion bot)
                : base(frame, zoom, "Discord Bot", "DISCORD BOT >", Wide, Tall)
            {
                this.bot = bot;
                Controls.Add(new DiscordArtwork { Bounds = Box(24, 62, 182, 155) });
                Words("Bring Capri into your server", 224, 70, 370, 32, Display(15f * Zoom), White);
                Words("She joins as your own bot and uses the model and voice selected in Mainframe.", 224, 111, 372, 46, Type(10f * Zoom), White);
                Words("The bot runs while Capri is open on this PC.", 224, 164, 370, 21, Small(), Faint);

                Words("CREATE A BOT", 24, 223, 272, 22, Type(10f * Zoom, true), Cyan);
                Words("Create an application, enable Server Install, and copy the token from its Bot tab.", 24, 250, 274, 43, Type(9.5f * Zoom), White);
                Make("Discord Developer Portal", 24, 300, 230, 27, delegate { OpenPage(DiscordCompanion.Portal); });

                Words("TALK IN DISCORD", 322, 223, 274, 22, Type(10f * Zoom, true), Cyan);
                Words("Add the bot and run /capri join. She chimes in when it fits. /capri leave ends the call.", 322, 250, 274, 44, Type(9.5f * Zoom), White);
                Words("Ask her to wait for her name. Say “Capri, join in again” to bring her back into the conversation.", 322, 298, 274, 42, Type(9.5f * Zoom), Faint);

                Words("BOT TOKEN", 24, 345, 124, 20, Type(9.5f * Zoom, true), White);
                token = new Entry { Font = Type(10f * Zoom), Bounds = Box(24, 369, 370, 28) };
                token.Cue = "Paste from Discord";
                token.Box.UseSystemPasswordChar = true;
                token.Box.AccessibleName = "Discord bot token";
                Controls.Add(token);
                connect = Make("Connect bot", 406, 369, 190, 28, delegate
                {
                    string value = token.Box.Text;
                    token.Box.Clear();
                    bot.Connect(value);
                    Fill();
                });
                connect.Accent = Cyan;
                connect.Loud = true;

                Words("Saved on this PC and protected by Windows.", 24, 406, 369, 21, Small(), Faint);
                Make("Install Node.js", 426, 405, 170, 24, delegate { OpenPage(DiscordCompanion.NodeDownload); });
                Words("Node.js 22.12 or newer. The first connection installs the voice helper.", 24, 434, 572, 21, Small(), Faint);
                status = Words("", 24, 466, 572, 38, Type(9.5f * Zoom), White);
                status.AccessibleName = "Discord bot connection status";
                invite = Make("Add to server", 24, 518, 176, 28, delegate
                {
                    string url = bot.Snapshot().Invite;
                    if (url != null) OpenPage(url);
                });
                invite.Accent = Cyan;
                stop = Make("Stop bot", 222, 518, 176, 28, delegate { bot.Stop(); Fill(); });
                forget = Make("Forget token", 420, 518, 176, 28, delegate { bot.Stop(true); Fill(); });

                token.Box.TextChanged += delegate { Fill(); };
                update.Tick += delegate { Fill(); };
                Shown += delegate { Fill(); update.Start(); };
                Disposed += delegate { update.Dispose(); };
                Fill();
                ResumeLayout(false);
            }

            private void Fill()
            {
                DiscordCompanion.View view = bot.Snapshot();
                status.ForeColor = view.Problem ? Rose : Cyan;
                status.Text = view.Status;
                token.Enabled = connect.Enabled = !view.Running;
                connect.Text = view.Saved && token.Box.Text.Length == 0 ? "Use saved token" : "Connect bot";
                invite.Enabled = view.Invite != null;
                stop.Enabled = view.Running;
                forget.Enabled = view.Saved;
            }

            private void OpenPage(string url)
            {
                try { McpSignIn.Open(url); }
                catch (System.ComponentModel.Win32Exception) { status.Text = "Windows could not open your browser."; status.ForeColor = Rose; }
            }
        }

        private sealed class DiscordArtwork : Control
        {
            private readonly Bitmap capri = Companion.LoadArt("Capri.Her.capri-perch.png");

            internal DiscordArtwork()
            {
                DoubleBuffered = true;
                TabStop = false;
                AccessibleName = "Capri sitting on the Discord logo";
                AccessibleRole = AccessibleRole.Graphic;
            }

            protected override void OnPaintBackground(PaintEventArgs e) { Backdrop(e.Graphics, this, ScaleOf(this)); }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                float scale = Width / 182f;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                Color purple = ColorTranslator.FromHtml(ServiceLogo.Discord.Hex);
                PluginArt.Logo(e.Graphics, ServiceLogo.Discord, new RectangleF(38 * scale, 86 * scale, 108 * scale, 81 * scale), purple);
                if (capri != null) e.Graphics.DrawImage(capri, new RectangleF(15 * scale, 1 * scale, 139 * scale, 139 * scale));
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && capri != null) capri.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}

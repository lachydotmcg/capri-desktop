using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Mainframe
    {
        private ModOptionsSheet mailSheet;

        internal string MailSheetTitle { get { return mailSheet == null || mailSheet.IsDisposed ? null : mailSheet.Text; } }

        private void OpenMailSetup(Mod mod)
        {
            if (mailSheet != null && !mailSheet.IsDisposed) mailSheet.Close();
            Control made = MailPanel(mod);
            mailSheet = new ModOptionsSheet(this, SheetZoom(620, made.Height + 84), mod.File.Id, "Set up Email", made, 620, made.Height + 84);
            mailSheet.Show(this);
            mailSheet.Activate();
        }

        private Control MailPanel(Mod mod)
        {
            Panel panel = new Panel { BackColor = Ground, ForeColor = White, Width = 572 };
            MailAccount saved = MailTools.Account;
            Label intro = new Label { Text = "Give her your email address and an app password. She uses them to check, search and read your mail, and she always asks you before she sends one.", Font = Type(9f), ForeColor = Faint, AutoSize = false, Bounds = new Rectangle(0, 0, 552, 40), UseMnemonic = false };
            Label addressName = new Label { Text = "Email address", Font = Type(9f, true), ForeColor = White, AutoSize = false, Bounds = new Rectangle(0, 48, 180, 20), UseMnemonic = false };
            TextBox address = new TextBox { Text = saved == null ? "" : saved.Address, Bounds = new Rectangle(190, 46, 362, 24), BackColor = Field, ForeColor = White, BorderStyle = BorderStyle.FixedSingle, AccessibleName = "Email address" };
            Label passwordName = new Label { Text = "App password", Font = Type(9f, true), ForeColor = White, AutoSize = false, Bounds = new Rectangle(0, 82, 180, 20), UseMnemonic = false };
            TextBox password = new TextBox { UseSystemPasswordChar = true, Bounds = new Rectangle(190, 80, 362, 24), BackColor = Field, ForeColor = White, BorderStyle = BorderStyle.FixedSingle, AccessibleName = "App password" };
            Label help = new Label { Font = Type(8.5f), ForeColor = Faint, AutoSize = false, Bounds = new Rectangle(190, 108, 362, 34), UseMnemonic = false };
            Label incomingName = new Label { Text = "Incoming (IMAP) server", Font = Type(9f, true), ForeColor = White, AutoSize = false, Bounds = new Rectangle(0, 150, 180, 20), UseMnemonic = false };
            TextBox incoming = new TextBox { Text = saved == null ? "" : saved.ImapHost + ":" + saved.ImapPort, Bounds = new Rectangle(190, 148, 362, 24), BackColor = Field, ForeColor = White, BorderStyle = BorderStyle.FixedSingle, AccessibleName = "Incoming mail server" };
            Label outgoingName = new Label { Text = "Outgoing (SMTP) server", Font = Type(9f, true), ForeColor = White, AutoSize = false, Bounds = new Rectangle(0, 184, 180, 20), UseMnemonic = false };
            TextBox outgoing = new TextBox { Text = saved == null ? "" : saved.SmtpHost + ":" + saved.SmtpPort, Bounds = new Rectangle(190, 182, 362, 24), BackColor = Field, ForeColor = White, BorderStyle = BorderStyle.FixedSingle, AccessibleName = "Outgoing mail server" };
            Label status = new Label { Text = saved != null && saved.Password.Length > 0 ? "Connected as " + saved.Address + "." : "", Font = Type(9f), ForeColor = Faint, AutoSize = false, Bounds = new Rectangle(0, 218, 552, 36), UseMnemonic = false };
            Key connect = new Key { Bounds = new Rectangle(0, 260, 160, 28), Font = Type(9f), Text = "Connect", Accent = Cyan };
            connect.AccessibleName = "Connect email";
            Key forget = new Key { Bounds = new Rectangle(172, 260, 160, 28), Font = Type(9f), Text = "Forget account", Accent = Rose, Visible = saved != null };
            Action shown = delegate
            {
                string note = MailAccount.Unsupported(address.Text);
                help.Text = note ?? Mods.MailSetupHelp(address.Text);
                help.ForeColor = note != null ? Rose : Faint;
                bool hosts = Mods.NeedsHosts(address.Text);
                incomingName.Visible = incoming.Visible = outgoingName.Visible = outgoing.Visible = hosts;
            };
            address.TextChanged += delegate { shown(); };
            shown();
            connect.Click += delegate
            {
                string problem;
                MailAccount account = Mods.MailFromForm(address.Text, password.Text, incoming.Text, outgoing.Text, out problem);
                if (account == null) { status.Text = problem; status.ForeColor = Rose; return; }
                connect.Enabled = false;
                connect.Text = "Connecting...";
                status.Text = "Signing in to " + account.Provider + "...";
                status.ForeColor = Faint;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    string trouble = MailTools.Test(account);
                    if (trouble == null)
                    {
                        try { account.Save(MailTools.Folder); }
                        catch (Exception error) { trouble = "Connected, but couldn't save it: " + error.Message; }
                        if (trouble == null) MailTools.Account = account;
                    }
                    try
                    {
                        BeginInvoke(new Action(delegate
                        {
                            connect.Enabled = true;
                            connect.Text = "Connect";
                            password.Text = "";
                            status.Text = trouble ?? "Connected as " + account.Address + ". She can use your email now.";
                            status.ForeColor = trouble == null ? Cyan : Rose;
                            forget.Visible = MailTools.Account != null;
                            if (trouble == null) ModSays(mod.File.Name + " is on and connected to " + account.Address + ".", false);
                        }));
                    }
                    catch (InvalidOperationException) { }
                });
            };
            forget.Click += delegate
            {
                MailTools.Forget();
                forget.Visible = false;
                status.Text = "She forgot that account. Its app password is gone from this PC.";
                status.ForeColor = Faint;
            };
            panel.Controls.AddRange(new Control[] { intro, addressName, address, passwordName, password, help, incomingName, incoming, outgoingName, outgoing, status, connect, forget });
            panel.Height = 296;
            return panel;
        }

        private void Started(Mod mod)
        {
            if (mod == null || !mod.On) return;
            if (Mods.Builtin(mod, "mail") && !MailTools.Ready) { OpenMailSetup(mod); ModSays(mod.File.Name + " is on. Set it up in the window that opened.", false); return; }
            if (!Mods.Online(mod) || Plugins.SignedIn(mod.File.Id)) return;
            ModSays(mod.File.Name + " is on. Sign in to it in your browser.", false);
            Plugins.SignIn(mod.File.Id, delegate(string problem)
            {
                try { BeginInvoke(new Action(delegate { ModSays(problem == null ? "Signed in to " + mod.File.Name + "." : "Couldn't sign in to " + mod.File.Name + ": " + problem, problem != null); FillMods(); })); }
                catch (InvalidOperationException) { }
            });
        }
    }
}

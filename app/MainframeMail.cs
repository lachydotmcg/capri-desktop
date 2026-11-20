using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
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
            mailSheet = new ModOptionsSheet(this, SheetZoom(620, made.Height + 84), mod == null ? "email" : mod.File.Id, "Set up Email", made, 620, made.Height + 84);
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
            Toggle watch = new Toggle { Text = "Watch for new mail", Font = Type(9.5f), Switch = 96, Bounds = new Rectangle(0, 304, 552, 22), Checked = MailTools.Watch };
            watch.AccessibleName = "Watch for new mail. She tells you when new mail lands and warns you about phishing; off by default";
            Label watchNote = new Label { Text = "She keeps an eye on your inbox and speaks up when mail lands, with a heads-up if it looks like phishing. Nothing gets marked read.", Font = Type(8.5f), ForeColor = Faint, AutoSize = false, Bounds = new Rectangle(0, 330, 552, 34), UseMnemonic = false };
            Toggle hands = new Toggle { Text = "Deal with phishing on her own", Font = Type(9.5f), Switch = 96, Bounds = new Rectangle(0, 372, 552, 22), Checked = MailTools.HandsSet, Dimmed = !MailTools.Watch };
            hands.AccessibleName = "Deal with phishing on her own. Only while she watches: mail she's sure is phishing goes to spam without asking; off by default";
            Label handsNote = new Label { Text = "Only while she's watching. Mail she's sure is phishing goes straight to spam. Anything else waits for your yes.", Font = Type(8.5f), ForeColor = Faint, AutoSize = false, Bounds = new Rectangle(0, 398, 552, 34), UseMnemonic = false };
            bool filling = false;
            EventHandler saveSwitches = delegate
            {
                if (filling) return;
                hands.Dimmed = !watch.Checked;
                try { MailTools.SetWatch(watch.Checked, hands.Checked); }
                catch (Exception error)
                {
                    if (!(error is IOException || error is UnauthorizedAccessException)) throw;
                    status.Text = "Couldn't save that: " + error.Message;
                    status.ForeColor = Rose;
                    return;
                }
                status.Text = !watch.Checked ? "She's not watching your inbox." : hands.Checked ? "She's watching your inbox and deals with phishing she's sure of." : "She's watching your inbox and tells you when mail lands.";
                status.ForeColor = watch.Checked ? Cyan : Faint;
            };
            watch.CheckedChanged += saveSwitches;
            hands.CheckedChanged += saveSwitches;
            forget.Click += delegate
            {
                MailTools.Forget();
                forget.Visible = false;
                filling = true;
                watch.Checked = hands.Checked = false;
                hands.Dimmed = true;
                filling = false;
                status.Text = "She forgot that account. Its app password is gone from this PC.";
                status.ForeColor = Faint;
            };
            panel.Controls.AddRange(new Control[] { intro, addressName, address, passwordName, password, help, incomingName, incoming, outgoingName, outgoing, status, connect, forget, watch, watchNote, hands, handsNote });
            panel.Height = 436;
            return panel;
        }

        internal void CheckMailPanel(List<string> checks, string output)
        {
            string savedFolder = MailTools.Folder;
            MailAccount savedAccount = MailTools.Account;
            string dir = Path.Combine(output, "mail-panel");
            try
            {
                Directory.CreateDirectory(dir);
                MailTools.Folder = dir;
                MailTools.Forget();
                MailAccount demo = MailAccount.ForAddress("capri.demo@gmail.com");
                demo.Password = "demo-panel-pass-1";
                demo.Save(dir);
                MailTools.Account = demo;
                string account = File.ReadAllText(MailAccount.FilePath(dir));
                using (Control made = MailPanel(null))
                {
                    Toggle watch = null, hands = null;
                    foreach (Control c in made.Controls)
                    {
                        Toggle t = c as Toggle;
                        if (t != null && t.Text == "Watch for new mail") watch = t;
                        else if (t != null && t.Text == "Deal with phishing on her own") hands = t;
                    }
                    if (watch == null || hands == null || watch.Checked || hands.Checked || !hands.Dimmed || MailTools.Watch || MailTools.OwnHands)
                        throw new Exception("Email's settings didn't open with both new switches off, and dealing with phishing waiting on watching.");
                    MailPicture(Path.Combine(output, "mail-setup.png"));
                    InvokeOnClick(watch, EventArgs.Empty);
                    if (!watch.Checked || !MailTools.Watch || MailTools.OwnHands || hands.Dimmed) throw new Exception("Turning on Watch for new mail didn't save it, or let her deal with phishing on her own.");
                    InvokeOnClick(hands, EventArgs.Empty);
                    string saved = File.ReadAllText(Path.Combine(dir, MailTools.WatchName));
                    if (!MailTools.OwnHands || !saved.Contains("\"watch\":true") || !saved.Contains("\"own_hands\":true") || saved.Contains("demo-panel-pass-1") || File.ReadAllText(MailAccount.FilePath(dir)) != account)
                        throw new Exception("Email's switches weren't saved on their own, apart from the account and its password: " + saved);
                    MailPicture(Path.Combine(output, "mail-setup-on.png"));
                    InvokeOnClick(watch, EventArgs.Empty);
                    if (MailTools.Watch || MailTools.OwnHands || !MailTools.HandsSet || !hands.Dimmed) throw new Exception("Turning watching off didn't stop her dealing with phishing on her own.");
                    MailTools.Forget();
                    if (MailTools.Watch || MailTools.HandsSet || File.Exists(Path.Combine(dir, MailTools.WatchName))) throw new Exception("Forgetting the account kept its mail switches.");
                }
            }
            finally
            {
                MailTools.Folder = savedFolder;
                MailTools.Account = savedAccount;
            }
            checks.Add("PASS mail: Email's settings show Watch for new mail and Deal with phishing on her own, both off, saved apart from the password, and dealing with phishing only counts while she watches");
        }

        private void MailPicture(string path)
        {
            Control made = MailPanel(null);
            using (ModOptionsSheet sheet = new ModOptionsSheet(this, SheetZoom(620, made.Height + 84), "email", "Set up Email", made, 620, made.Height + 84))
            {
                sheet.Show(this);
                sheet.Picture(path);
                sheet.Close();
            }
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

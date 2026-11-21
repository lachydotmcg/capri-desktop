using System;
using System.Threading;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace CapriDesktop
{
    internal sealed partial class Mainframe
    {
        private Window mailBox;

        internal Window MailSetupWindow { get { return mailBox; } }

        private TextBlock MailWords(string text, IBrush ink, bool bold = false)
        {
            return new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = ink, FontFamily = MainframeTheme.Text, FontSize = MainframeTheme.Size, FontWeight = bold ? FontWeight.Bold : FontWeight.Normal };
        }

        private TextBox MailEntry(string name, string text, bool secret)
        {
            TextBox box = new TextBox { Text = text, Background = MainframeTheme.Field, Foreground = MainframeTheme.White, FontFamily = MainframeTheme.Text, FontSize = MainframeTheme.Size, Margin = new Thickness(0, 2, 0, 8) };
            if (secret) box.PasswordChar = '•';
            AutomationProperties.SetName(box, name);
            return box;
        }

        private static NeonSwitch MailSwitch(string name, bool on)
        {
            NeonSwitch toggle = new NeonSwitch { Text = name, Height = 22, Margin = new Thickness(0, 12, 0, 4), Checked = on };
            AutomationProperties.SetName(toggle, name);
            return toggle;
        }

        internal void OpenMailSetup(Mod mod)
        {
            if (mailBox != null) mailBox.Close();
            MailAccount saved = MailTools.Account;
            Window box = new Window { Title = "Set up Email", Width = 520, SizeToContent = SizeToContent.Height, CanResize = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = MainframeTheme.Ground };
            StackPanel all = new StackPanel { Margin = new Thickness(16) };
            all.Children.Add(MailWords("Give her your email address and an app password. She uses them to check, search and read your mail, and she always asks you before she sends one." + (McpSignIn.Keeps ? "" : " On this system she keeps the app password only until she closes."), MainframeTheme.Faint));
            all.Children.Add(new Border { Height = 10 });
            all.Children.Add(MailWords("Email address", MainframeTheme.White, true));
            TextBox address = MailEntry("Email address", saved == null ? "" : saved.Address, false);
            all.Children.Add(address);
            all.Children.Add(MailWords("App password", MainframeTheme.White, true));
            TextBox password = MailEntry("App password", "", true);
            all.Children.Add(password);
            TextBlock help = MailWords("", MainframeTheme.Faint);
            all.Children.Add(help);
            StackPanel hosts = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            hosts.Children.Add(MailWords("Incoming (IMAP) server", MainframeTheme.White, true));
            TextBox incoming = MailEntry("Incoming mail server", saved == null ? "" : saved.ImapHost + ":" + saved.ImapPort, false);
            hosts.Children.Add(incoming);
            hosts.Children.Add(MailWords("Outgoing (SMTP) server", MainframeTheme.White, true));
            TextBox outgoing = MailEntry("Outgoing mail server", saved == null ? "" : saved.SmtpHost + ":" + saved.SmtpPort, false);
            hosts.Children.Add(outgoing);
            all.Children.Add(hosts);
            TextBlock status = MailWords(saved != null && saved.Password.Length > 0 ? "Connected as " + saved.Address + "." : "", MainframeTheme.Faint);
            status.Margin = new Thickness(0, 8, 0, 8);
            all.Children.Add(status);
            NeonButton connect = new NeonButton { Text = "Connect", Width = 140, Height = 28, Accent = MainframeTheme.Cyan, IsDefault = true };
            NeonButton forget = new NeonButton { Text = "Forget account", Width = 140, Height = 28, Accent = MainframeTheme.Rose, IsVisible = saved != null };
            StackPanel keys = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            keys.Children.Add(connect);
            keys.Children.Add(forget);
            all.Children.Add(keys);
            NeonSwitch watch = MailSwitch("Watch for new mail", MailTools.Watch);
            all.Children.Add(watch);
            all.Children.Add(MailWords("She keeps an eye on your inbox and speaks up when mail lands, with a heads-up if it looks like phishing. Nothing gets marked read.", MainframeTheme.Faint));
            NeonSwitch hands = MailSwitch("Deal with phishing on her own", MailTools.HandsSet);
            hands.Opacity = MailTools.Watch ? 1 : 0.5;
            all.Children.Add(hands);
            all.Children.Add(MailWords("Only while she's watching. Mail she's sure is phishing goes straight to spam. Anything else waits for your yes.", MainframeTheme.Faint));
            bool filling = false;
            Action saveSwitches = delegate
            {
                if (filling) return;
                hands.Opacity = watch.Checked ? 1 : 0.5;
                try { MailTools.SetWatch(watch.Checked, hands.Checked); }
                catch (Exception error) when (error is System.IO.IOException || error is UnauthorizedAccessException)
                {
                    status.Text = "Couldn't save that: " + error.Message;
                    status.Foreground = MainframeTheme.Rose;
                    return;
                }
                status.Text = !watch.Checked ? "She's not watching your inbox." : hands.Checked ? "She's watching your inbox and deals with phishing she's sure of." : "She's watching your inbox and tells you when mail lands.";
                status.Foreground = watch.Checked ? MainframeTheme.Cyan : MainframeTheme.Faint;
            };
            watch.IsCheckedChanged += (_, _) => saveSwitches();
            hands.IsCheckedChanged += (_, _) => saveSwitches();
            Action shown = delegate
            {
                string note = MailAccount.Unsupported(address.Text);
                help.Text = note ?? Mods.MailSetupHelp(address.Text);
                help.Foreground = note != null ? MainframeTheme.Rose : MainframeTheme.Faint;
                hosts.IsVisible = Mods.NeedsHosts(address.Text);
            };
            address.TextChanged += delegate { shown(); };
            shown();
            connect.Click += delegate
            {
                string problem;
                MailAccount account = Mods.MailFromForm(address.Text, password.Text, incoming.Text, outgoing.Text, out problem);
                if (account == null) { status.Text = problem; status.Foreground = MainframeTheme.Rose; return; }
                connect.IsEnabled = false;
                connect.Text = "Connecting...";
                status.Text = "Signing in to " + account.Provider + "...";
                status.Foreground = MainframeTheme.Faint;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    string trouble = MailTools.Test(account);
                    if (trouble == null)
                    {
                        try { account.Save(MailTools.Folder); }
                        catch (Exception error) { trouble = "Connected, but couldn't save it: " + error.Message; }
                        MailTools.Account = account;
                    }
                    Dispatcher.UIThread.Post(delegate
                    {
                        connect.IsEnabled = true;
                        connect.Text = "Connect";
                        password.Text = "";
                        status.Text = trouble ?? "Connected as " + account.Address + ". She can use your email now.";
                        status.Foreground = trouble == null ? MainframeTheme.Cyan : MainframeTheme.Rose;
                        forget.IsVisible = MailTools.Account != null;
                        if (trouble == null) ModSays(mod.File.Name + " is on and connected to " + account.Address + ".");
                    });
                });
            };
            forget.Click += delegate
            {
                MailTools.Forget();
                forget.IsVisible = false;
                filling = true;
                watch.Checked = hands.Checked = false;
                hands.Opacity = 0.5;
                filling = false;
                status.Text = "She forgot that account and its app password.";
                status.Foreground = MainframeTheme.Faint;
            };
            box.Content = all;
            box.Closed += delegate { if (mailBox == box) mailBox = null; };
            mailBox = box;
            box.Show(this);
        }

        private void Started(Mod mod)
        {
            if (mod == null || !mod.On || Program.Smoke != null) return;
            if (Mods.Builtin(mod, "mail") && !MailTools.Ready) { OpenMailSetup(mod); ModSays(mod.File.Name + " is on. Set it up in the window that opened."); return; }
            if (!Mods.Online(mod) || Plugins.SignedIn(mod.File.Id)) return;
            ModSays(mod.File.Name + " is on. Sign in to it in your browser.");
            Plugins.SignIn(mod.File.Id, delegate(string problem)
            {
                Dispatcher.UIThread.Post(delegate { ModSays(problem == null ? "Signed in to " + mod.File.Name + "." : "Couldn't sign in to " + mod.File.Name + ": " + problem); ModsChanged(); });
            });
        }
    }
}

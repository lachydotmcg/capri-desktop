using System;
using System.IO;
using System.Threading;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace CapriDesktop
{
    internal sealed partial class Mainframe
    {
        internal static Func<MailAccount, string> MailTest = MailTools.Test;
        internal MailWindow MailSetupWindow { get; private set; }

        internal void OpenMailSetup(Mod mod)
        {
            MailWindow open = MailSetupWindow;
            if (open != null) { MailSetupWindow = null; open.Close(); }
            MailWindow sheet = new MailWindow(pet, this, mod);
            MailSetupWindow = sheet;
            sheet.Closing += delegate { if (MailSetupWindow == sheet) MailSetupWindow = null; };
            sheet.Closed += delegate { if (MailSetupWindow == sheet) MailSetupWindow = null; };
            ShowSheet(sheet);
        }

        internal void MailChanged(string said)
        {
            if (said != null) ModSays(said);
            ModsChanged();
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

    // Set up Email in three steps, like Windows' (app/MainframeMail.cs): which email it is, making an app password there, then
    // pasting it in. Once it's connected it shows who it's connected as and her two mail switches instead.
    internal sealed class MailWindow : MainframeWindow
    {
        internal const int Wide = 620, Tall = 520;
        private readonly Mainframe frame;
        private readonly Mod mod;
        private bool filling, shut;
        internal bool Inline;
        internal int Step;
        internal Mods.MailProvider Picked;
        internal readonly PlugTile[] Tiles = new PlugTile[Mods.MailProviders.Length];
        internal readonly NeonCaption StepLine, Question, PickNote, Separate, Status, Doing, AddressName, PasswordName, IncomingName, OutgoingName, WatchNote, HandsNote;
        internal readonly NeonCaption[] Numbers = new NeonCaption[3], Lines = new NeonCaption[3];
        internal readonly NeonEntry Address, Password, Incoming, Outgoing;
        internal readonly NeonButton OpenPage, BackKey, NextKey, Connect, Forget, Another;
        internal readonly NeonSwitch WatchSwitch, HandsSwitch;

        internal MailWindow(Pet owner, Mainframe frame, Mod mod) : base("Set up Email")
        {
            this.frame = frame;
            this.mod = mod;
            Steered = true; FitsScreen = true;
            ShowInTaskbar = false;
            MailAccount saved = MailTools.Account;
            Label("SET UP EMAIL", 24, 15, 480, 29, MainframeTheme.Rose, 20, true).Display = true;
            NeonButton close = Put(Page, new NeonButton { Glyph = 2 }, Wide - 54, 20, 26, 22, "Close");
            close.Click += delegate { Close(); };
            StepLine = Label("", 24, 58, 572, 18, MainframeTheme.Faint, 34.0 / 3);
            Question = Label("", 24, 76, 572, 32, MainframeTheme.White, 20, true);
            for (int i = 0; i < Tiles.Length; i++)
            {
                Mods.MailProvider provider = Mods.MailProviders[i];
                PlugTile tile = Put(Page, new PlugTile { Picture = Mods.Envelope, Logo = provider.Logo, Tint = provider.Tint, PlateTall = 64, Text = provider.Name }, 24 + i % 4 * 146, 120 + i / 4 * 100, 134, 90, provider.Name);
                tile.Click += delegate { Pick(provider, true); };
                Tiles[i] = tile;
            }
            AddressName = Label("Or type your address", 24, 332, 200, 22, MainframeTheme.White, MainframeTheme.Size, true);
            Address = Field("Email address", "you@example.com", 224, 328, 372, false);
            PickNote = Label("", 24, 366, 572, 44, MainframeTheme.Faint, MainframeTheme.Size, false, true);
            for (int i = 0; i < 3; i++)
            {
                Numbers[i] = Label((i + 1).ToString(), 24, 120 + i * 44, 24, 30, MainframeTheme.Cyan, 20, true);
                Lines[i] = Label("", 56, 124 + i * 44, 540, 40, MainframeTheme.White, MainframeTheme.Size, false, true);
            }
            OpenPage = Key("Open the page", 56, 260, 320, 32, Open);
            OpenPage.Accent = MainframeTheme.Cyan;
            OpenPage.Loud = true;
            Separate = Label(Note(), 24, 312, 572, 44, MainframeTheme.Faint, MainframeTheme.Size, false, true);
            Lingo.Changed += RewordNote;
            PasswordName = Label("App password", 24, 162, 160, 22, MainframeTheme.White, MainframeTheme.Size, true);
            Password = Field("App password", "", 190, 158, 406, true);
            IncomingName = Label("IMAP server", 24, 200, 160, 22, MainframeTheme.White, MainframeTheme.Size, true);
            Incoming = Field("Incoming mail server", "imap.example.com:993", 190, 196, 406, false);
            OutgoingName = Label("SMTP server", 24, 238, 160, 22, MainframeTheme.White, MainframeTheme.Size, true);
            Outgoing = Field("Outgoing mail server", "smtp.example.com:587", 190, 234, 406, false);
            Status = Label("", 24, 276, 572, 44, MainframeTheme.Faint, MainframeTheme.Size, false, true);
            Doing = Label("She checks, searches and reads your mail. She asks before she sends any.", 24, 112, 572, 22);
            WatchSwitch = Put(Page, new NeonSwitch { Text = "Watch for new mail", Checked = MailTools.Watch }, 24, 150, 572, 24, "Watch for new mail. She tells you when new mail lands and warns you about phishing; off by default");
            WatchNote = Label("She speaks up when mail lands, with a heads-up if it looks like phishing. Nothing gets marked read.", 24, 178, 572, 36, MainframeTheme.Faint, 34.0 / 3, false, true);
            HandsSwitch = Put(Page, new NeonSwitch { Text = "Deal with phishing on her own", Checked = MailTools.HandsSet }, 24, 222, 572, 24, "Deal with phishing on her own. Only while she watches: mail she's sure is phishing goes to spam without asking; off by default");
            HandsNote = Label("Only while she watches. Mail she's sure is phishing goes to spam. The rest waits for your yes.", 24, 250, 572, 36, MainframeTheme.Faint, 34.0 / 3, false, true);
            BackKey = Key("Back", 24, 466, 140, 30, delegate { Go(Step - 1); });
            NextKey = Key("Next", 456, 466, 140, 30, delegate { if (Picked != null && !Picked.Closed) Go(Step + 1); });
            NextKey.Accent = MainframeTheme.Cyan;
            NextKey.Loud = true;
            Connect = Key("Connect", 456, 466, 140, 30, Join);
            Connect.Accent = MainframeTheme.Cyan;
            Connect.Loud = true;
            AutomationProperties.SetName(Connect, "Connect email");
            Forget = Key("Forget account", 24, 466, 170, 30, Drop);
            Another = Key("Use another email", 426, 466, 170, 30, delegate { Address.Box.Text = ""; Password.Box.Text = ""; Say("", MainframeTheme.Faint); Go(1); });
            // Avalonia says a box's text changed only after it's drawn, so the pick follows the text itself as it's typed.
            Address.Box.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) Program.Guard(Typed); };
            WatchSwitch.IsCheckedChanged += (_, _) => Program.Guard(Switches);
            HandsSwitch.IsCheckedChanged += (_, _) => Program.Guard(Switches);
            Closed += delegate { Lingo.Changed -= RewordNote; shut = true; };
            if (saved != null)
            {
                Address.Box.Text = saved.Address;
                Incoming.Box.Text = saved.ImapHost + ":" + saved.ImapPort;
                Outgoing.Box.Text = saved.SmtpHost + ":" + saved.SmtpPort;
            }
            Fit(Wide, Tall, Pet.FrameSize(owner.Saved.MainframeSize) == 0 ? 1 : owner.Saved.MainframeSize / 100.0);
            Go(MailTools.Ready ? 4 : 1);
        }

        // The app-password note is put together in her language when the sheet is built, so a language change rebuilds it
        // while the sheet is open, like the tray refills on its next update. One composer keeps both paths the same.
        private string Note()
        {
            return Lingo.Of(Mods.AppPasswordLine) + (McpSignIn.Keeps ? "" : " " + Lingo.Of("She keeps it only until she closes."));
        }

        private void RewordNote()
        {
            Separate.Text = Note();
        }

        private NeonCaption Label(string text, int x, int y, int width, int height, IBrush ink = null, double size = MainframeTheme.Size, bool bold = false, bool top = false)
        {
            return Put(Page, new NeonCaption { Text = text, Ink = ink ?? MainframeTheme.Faint, Size = size, Bold = bold, Ellipsis = true, Top = top }, x, y, width, height);
        }

        private NeonButton Key(string text, int x, int y, int width, int height, Action action)
        {
            NeonButton key = Put(Page, new NeonButton { Text = text }, x, y, width, height, text);
            key.Click += delegate { action(); };
            return key;
        }

        private NeonEntry Field(string name, string cue, int x, int y, int width, bool secret)
        {
            NeonEntry entry = Put(Page, new NeonEntry(cue, secret), x, y, width, 28, name);
            AutomationProperties.SetName(entry.Box, name);
            entry.Box.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) { e.Handled = true; Program.Guard(Accept); } };
            return entry;
        }

        private static void Move(Control control, double x, double y, double width, double height)
        {
            Canvas.SetLeft(control, x); Canvas.SetTop(control, y); control.Width = width; control.Height = height;
        }

        internal string Crowded() { return ModsWindow.Crowded(Page, Wide, Tall); }

        internal void Say(string text, IBrush ink)
        {
            Status.Text = text;
            Status.Ink = ink;
            PickNote.Text = Step == 1 ? text : "";
            PickNote.Ink = ink;
            Status.InvalidateVisual();
            PickNote.InvalidateVisual();
        }

        internal void Pick(Mods.MailProvider provider, bool clicked)
        {
            Picked = provider;
            if (clicked)
            {
                Mods.MailProvider typed = Mods.ProviderFor(Address.Box.Text);
                if (typed != null && typed != provider) Address.Box.Text = "";
            }
            for (int i = 0; i < Tiles.Length; i++) { Tiles[i].Lit = Mods.MailProviders[i] == provider; Tiles[i].InvalidateVisual(); }
            PickNote.Text = provider.Closed ? provider.Note : "";
            PickNote.Ink = provider.Closed ? MainframeTheme.Rose : MainframeTheme.Faint;
            PickNote.InvalidateVisual();
            NextKey.IsEnabled = !provider.Closed;
            if (Step == 3) Go(3);
        }

        private void Typed()
        {
            Mods.MailProvider found = Mods.ProviderFor(Address.Box.Text);
            if (found != null && found != Picked) Pick(found, false);
            else if (Step == 3) Go(3);
        }

        private void Accept()
        {
            if (Step == 3) { if (Connect.IsEnabled) Join(); }
            else if (Step < 3 && Picked != null && !Picked.Closed) Go(Step + 1);
        }

        private void Open()
        {
            if (Picked == null || Picked.Page == null) return;
            try { Plugins.OpenBrowser(Picked.Page); }
            catch (Exception error)
            {
                if (!(error is McpException || error is System.ComponentModel.Win32Exception || error is InvalidOperationException || error is IOException)) throw;
                Say("Couldn't open your browser. The page is " + Picked.Page, MainframeTheme.Rose);
            }
        }

        internal void Go(int step)
        {
            int was = Step;
            Step = Math.Max(1, Math.Min(4, step));
            if (Step == 4 && !MailTools.Ready) Step = 1;
            if (Step > 1 && Step < 4 && (Picked == null || Picked.Closed)) Step = 1;
            if (Step != was) Status.Text = "";
            bool one = Step == 1, two = Step == 2, three = Step == 3, done = Step == 4;
            string name = Picked == null ? "" : ": " + Picked.Name.ToUpperInvariant();
            StepLine.Text = done ? "" : "STEP " + Step + " OF 3" + (one ? "" : name);
            Question.Text = one ? "Which email?" : two ? "Make an app password" : three ? "Paste it" : "Connected as " + MailTools.Account.Address;
            foreach (PlugTile tile in Tiles) tile.IsVisible = one;
            AddressName.IsVisible = Address.IsVisible = one || three;
            AddressName.Text = one ? "Or type your address" : "Email address";
            if (one) { Move(AddressName, 24, 332, 200, 22); Move(Address, 224, 328, 372, 28); }
            else { Move(AddressName, 24, 124, 160, 22); Move(Address, 190, 120, 406, 28); }
            PickNote.IsVisible = one;
            if (one && Picked != null && Picked.Closed) { PickNote.Text = Picked.Note; PickNote.Ink = MainframeTheme.Rose; }
            for (int i = 0; i < 3; i++)
            {
                Numbers[i].IsVisible = Lines[i].IsVisible = two && i < Picked.Steps.Length;
                Lines[i].Text = two && i < Picked.Steps.Length ? Picked.Steps[i] : "";
            }
            bool page = two && Picked.Page != null;
            OpenPage.IsVisible = page;
            if (page) { OpenPage.Text = Picked.PageKey; AutomationProperties.SetName(OpenPage, Picked.PageKey); }
            Separate.IsVisible = two;
            Move(Separate, 24, page ? 312 : 262, 572, 44);
            bool hosts = three && Mods.HostsShown(Picked, Address.Box.Text);
            PasswordName.IsVisible = Password.IsVisible = three;
            IncomingName.IsVisible = Incoming.IsVisible = OutgoingName.IsVisible = Outgoing.IsVisible = hosts;
            Status.IsVisible = two || three || done;
            Move(Status, 24, done ? 300 : three ? (hosts ? 276 : 200) : page ? 366 : 316, 572, 44);
            Doing.IsVisible = WatchSwitch.IsVisible = HandsSwitch.IsVisible = WatchNote.IsVisible = HandsNote.IsVisible = Forget.IsVisible = Another.IsVisible = done;
            BackKey.IsVisible = two || three;
            NextKey.IsVisible = one || two;
            NextKey.IsEnabled = Picked != null && !Picked.Closed;
            Connect.IsVisible = three;
            Connect.IsEnabled = three && !Picked.Closed && Connect.Text == "Connect";
            if (done)
            {
                filling = true;
                WatchSwitch.Checked = MailTools.Watch;
                HandsSwitch.Checked = MailTools.HandsSet;
                HandsSwitch.Opacity = WatchSwitch.Checked ? 1 : 0.5;
                filling = false;
            }
            foreach (Control control in Page.Children) control.InvalidateVisual();
        }

        private void Join()
        {
            string problem;
            MailAccount account = Mods.MailFromForm(Address.Box.Text, Password.Box.Text, Incoming.Box.Text, Outgoing.Box.Text, out problem);
            if (account == null) { Say(problem, MainframeTheme.Rose); return; }
            Connect.IsEnabled = false;
            Connect.Text = "Connecting...";
            Say("Signing in to " + account.Provider + "...", MainframeTheme.Faint);
            Func<MailAccount, string> test = Mainframe.MailTest;
            bool inline = Inline;
            Action work = delegate
            {
                string trouble = test(account);
                if (trouble == null)
                {
                    try { account.Save(MailTools.Folder); }
                    catch (Exception error) { trouble = "Connected, but couldn't save it: " + error.Message; }
                    MailTools.Account = account;
                }
                Action shown = delegate
                {
                    if (shut) { if (trouble == null) frame.MailChanged(null); return; }
                    Connect.Text = "Connect";
                    Password.Box.Text = "";
                    if (trouble != null) { Go(3); Say(trouble, MainframeTheme.Rose); return; }
                    Go(4);
                    Say("She can use your email now.", MainframeTheme.Cyan);
                    frame.MailChanged(mod == null ? null : mod.File.Name + " is on and connected to " + account.Address + ".");
                };
                if (inline) shown();
                else Dispatcher.UIThread.Post(delegate { Program.Guard(shown); });
            };
            if (inline) work();
            else ThreadPool.QueueUserWorkItem(delegate { work(); });
        }

        private void Switches()
        {
            if (filling) return;
            HandsSwitch.Opacity = WatchSwitch.Checked ? 1 : 0.5;
            try { MailTools.SetWatch(WatchSwitch.Checked, HandsSwitch.Checked); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Say("Couldn't save that: " + error.Message, MainframeTheme.Rose);
                return;
            }
            Say(!WatchSwitch.Checked ? "She's not watching your inbox." : HandsSwitch.Checked ? "She's watching your inbox and deals with phishing she's sure of." : "She's watching your inbox and tells you when mail lands.", WatchSwitch.Checked ? MainframeTheme.Cyan : MainframeTheme.Faint);
        }

        private void Drop()
        {
            MailTools.Forget();
            filling = true;
            WatchSwitch.Checked = HandsSwitch.Checked = false;
            HandsSwitch.Opacity = 0.5;
            filling = false;
            Address.Box.Text = "";
            Password.Box.Text = "";
            Picked = null;
            foreach (PlugTile tile in Tiles) { tile.Lit = false; tile.InvalidateVisual(); }
            Go(1);
            Say("She forgot that account and its app password.", MainframeTheme.Faint);
            frame.MailChanged(null);
        }
    }
}

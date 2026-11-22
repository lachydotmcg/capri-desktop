using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Mainframe
    {
        private MailSheet mailSheet;
        private static Func<MailAccount, string> mailTest = MailTools.Test;

        internal string MailSheetTitle { get { return mailSheet == null || mailSheet.IsDisposed ? null : mailSheet.Text; } }

        private void OpenMailSetup(Mod mod)
        {
            if (mailSheet != null && !mailSheet.IsDisposed) mailSheet.Close();
            mailSheet = new MailSheet(this, SheetZoom(MailSheet.Wide, MailSheet.Tall), mod);
            mailSheet.Show(this);
            mailSheet.Activate();
        }

        private void MailChanged(Mod mod, string said)
        {
            if (mod != null && said != null) ModSays(said, false);
            FillMods();
            RefreshPluginSheet(false);
        }

        // Email in three steps: which email it is, making an app password there, then pasting it in. Once it's connected the
        // window shows who it's connected as and her two mail switches instead.
        private sealed class MailSheet : ModSheet
        {
            internal const int Wide = 620, Tall = 520;
            private readonly Mod mod;
            internal int Step;
            internal Mods.MailProvider Picked;
            internal readonly PlugTile[] Tiles = new PlugTile[Mods.MailProviders.Length];
            internal readonly Label StepLine, Title, PickNote, Separate, Status, Doing, AddressName, PasswordName, IncomingName, OutgoingName, WatchNote, HandsNote;
            internal readonly Label[] Numbers = new Label[3], Lines = new Label[3];
            internal readonly Entry Address, Password, Incoming, Outgoing;
            internal readonly Key OpenPage, BackKey, NextKey, Connect, Forget, Another;
            internal readonly Toggle WatchSwitch, HandsSwitch;
            private bool filling;

            internal MailSheet(Mainframe owner, float zoom, Mod mod)
                : base(owner, zoom, "Set up Email", "SET UP EMAIL >", Wide, Tall)
            {
                this.mod = mod;
                MailAccount saved = MailTools.Account;
                StepLine = Words("", 24, 58, 572, 18, Small(), Faint);
                Title = Words("", 24, 76, 572, 32, Type(15f * Zoom, true), White);
                for (int i = 0; i < Tiles.Length; i++)
                {
                    Mods.MailProvider provider = Mods.MailProviders[i];
                    Color tint = ColorTranslator.FromHtml(provider.Tint);
                    PlugTile tile = new PlugTile { Picture = Mods.Envelope, Logo = provider.Logo, Tint = Neon.Tone(tint.R, tint.G, tint.B), PlateTall = 64, Text = provider.Name };
                    tile.Font = Type(9.5f * Zoom, true);
                    tile.Bounds = Box(24 + i % 4 * 146, 120 + i / 4 * 100, 134, 90);
                    tile.AccessibleName = provider.Name;
                    tile.Click += delegate { Pick(provider, true); };
                    Controls.Add(tile);
                    Tiles[i] = tile;
                }
                AddressName = Words("Or type your address", 24, 332, 200, 22, Type(9.5f * Zoom, true), White);
                Address = Field("Email address", "you@example.com", 224, 328, 372);
                PickNote = Wrapped(Words("", 24, 366, 572, 44, Type(9.5f * Zoom), Faint));
                for (int i = 0; i < 3; i++)
                {
                    Numbers[i] = Words((i + 1).ToString(), 24, 120 + i * 44, 24, 30, Type(14f * Zoom, true), Cyan);
                    Lines[i] = Wrapped(Words("", 56, 124 + i * 44, 540, 40, Type(10.5f * Zoom), White));
                }
                OpenPage = Make("Open the page", 56, 260, 320, 32, delegate { Open(); });
                OpenPage.Accent = Cyan;
                OpenPage.Loud = true;
                Separate = Wrapped(Words(Mods.AppPasswordLine, 24, 312, 572, 44, Type(9.5f * Zoom), Faint));
                PasswordName = Words("App password", 24, 162, 160, 22, Type(9.5f * Zoom, true), White);
                Password = Field("App password", "", 190, 158, 406);
                Password.Box.UseSystemPasswordChar = true;
                IncomingName = Words("IMAP server", 24, 200, 160, 22, Type(9.5f * Zoom, true), White);
                Incoming = Field("Incoming mail server", "imap.example.com:993", 190, 196, 406);
                OutgoingName = Words("SMTP server", 24, 238, 160, 22, Type(9.5f * Zoom, true), White);
                Outgoing = Field("Outgoing mail server", "smtp.example.com:587", 190, 234, 406);
                Status = Wrapped(Words("", 24, 276, 572, 44, Type(9.5f * Zoom), Faint));
                Doing = Words("She checks, searches and reads your mail. She asks before she sends any.", 24, 112, 572, 22, Type(9.5f * Zoom), Faint);
                WatchSwitch = new Toggle { Text = "Watch for new mail", Font = Type(9.5f * Zoom), Switch = 96, Bounds = Box(24, 150, 572, 24), Checked = MailTools.Watch };
                WatchSwitch.AccessibleName = "Watch for new mail. She tells you when new mail lands and warns you about phishing; off by default";
                WatchNote = Wrapped(Words("She speaks up when mail lands, with a heads-up if it looks like phishing. Nothing gets marked read.", 24, 178, 572, 36, Small(), Faint));
                HandsSwitch = new Toggle { Text = "Deal with phishing on her own", Font = Type(9.5f * Zoom), Switch = 96, Bounds = Box(24, 222, 572, 24), Checked = MailTools.HandsSet, Dimmed = !MailTools.Watch };
                HandsSwitch.AccessibleName = "Deal with phishing on her own. Only while she watches: mail she's sure is phishing goes to spam without asking; off by default";
                HandsNote = Wrapped(Words("Only while she watches. Mail she's sure is phishing goes to spam. The rest waits for your yes.", 24, 250, 572, 36, Small(), Faint));
                Controls.Add(WatchSwitch);
                Controls.Add(HandsSwitch);
                BackKey = Make("Back", 24, 466, 140, 30, delegate { Go(Step - 1); });
                NextKey = Make("Next", 456, 466, 140, 30, delegate { if (Picked != null && !Picked.Closed) Go(Step + 1); });
                NextKey.Accent = Cyan;
                NextKey.Loud = true;
                Connect = Make("Connect", 456, 466, 140, 30, delegate { Join(); });
                Connect.Accent = Cyan;
                Connect.Loud = true;
                Connect.AccessibleName = "Connect email";
                Forget = Make("Forget account", 24, 466, 170, 30, delegate { Drop(); });
                Another = Make("Use another email", 426, 466, 170, 30, delegate { Address.Box.Text = ""; Password.Box.Text = ""; Say("", Faint); Go(1); });
                Address.Box.TextChanged += delegate { Typed(); };
                EventHandler saveSwitches = delegate { Switches(); };
                WatchSwitch.CheckedChanged += saveSwitches;
                HandsSwitch.CheckedChanged += saveSwitches;
                if (saved != null)
                {
                    Address.Box.Text = saved.Address;
                    Incoming.Box.Text = saved.ImapHost + ":" + saved.ImapPort;
                    Outgoing.Box.Text = saved.SmtpHost + ":" + saved.SmtpPort;
                }
                Frame.Fit(this);
                Go(MailTools.Ready ? 4 : 1);
            }

            private static Label Wrapped(Label label) { label.AutoEllipsis = false; return label; }

            private Entry Field(string name, string cue, int x, int y, int width)
            {
                Entry entry = new Entry();
                entry.Bounds = Box(x, y, width, 28);
                entry.Font = Type(9.5f * Zoom);
                entry.Cue = cue;
                entry.Box.AccessibleName = name;
                Controls.Add(entry);
                return entry;
            }

            private void Put(Control control, int x, int y, int width, int height)
            {
                float s = ScaleOf(this);
                control.Bounds = new Rectangle((int)Math.Round(x * s), (int)Math.Round(y * s), (int)Math.Round(width * s), (int)Math.Round(height * s));
            }

            internal void Say(string text, Color colour)
            {
                Status.Text = text;
                Status.ForeColor = colour;
                PickNote.Text = Step == 1 ? text : "";
                PickNote.ForeColor = colour;
            }

            internal void Pick(Mods.MailProvider provider, bool clicked)
            {
                Picked = provider;
                if (clicked)
                {
                    Mods.MailProvider typed = Mods.ProviderFor(Address.Box.Text);
                    if (typed != null && typed != provider) Address.Box.Text = "";
                }
                for (int i = 0; i < Tiles.Length; i++) { Tiles[i].Lit = Mods.MailProviders[i] == provider; Tiles[i].Invalidate(); }
                PickNote.Text = provider.Closed ? provider.Note : "";
                PickNote.ForeColor = provider.Closed ? Rose : Faint;
                NextKey.Enabled = !provider.Closed;
                if (Step == 3) Go(3);
            }

            private void Typed()
            {
                Mods.MailProvider found = Mods.ProviderFor(Address.Box.Text);
                if (found != null && found != Picked) Pick(found, false);
                else if (Step == 3) Go(3);
            }

            private void Open()
            {
                if (Picked == null || Picked.Page == null) return;
                try { Plugins.OpenBrowser(Picked.Page); }
                catch (Exception error)
                {
                    if (!(error is McpException || error is System.ComponentModel.Win32Exception || error is InvalidOperationException || error is IOException)) throw;
                    Say("Couldn't open your browser. The page is " + Picked.Page, Rose);
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
                MailAccount account = MailTools.Account;
                Title.Text = one ? "Which email?" : two ? "Make an app password" : three ? "Paste it" : "Connected as " + account.Address;
                foreach (PlugTile tile in Tiles) tile.Visible = one;
                AddressName.Visible = Address.Visible = one || three;
                AddressName.Text = one ? "Or type your address" : "Email address";
                if (one) { Put(AddressName, 24, 332, 200, 22); Put(Address, 224, 328, 372, 28); }
                else { Put(AddressName, 24, 124, 160, 22); Put(Address, 190, 120, 406, 28); }
                PickNote.Visible = one;
                if (one && Picked != null && Picked.Closed) { PickNote.Text = Picked.Note; PickNote.ForeColor = Rose; }
                for (int i = 0; i < 3; i++)
                {
                    Numbers[i].Visible = Lines[i].Visible = two;
                    Lines[i].Text = two && i < Picked.Steps.Length ? Picked.Steps[i] : "";
                }
                bool page = two && Picked.Page != null;
                OpenPage.Visible = page;
                if (page) { OpenPage.Text = Picked.PageKey; OpenPage.AccessibleName = Picked.PageKey; }
                Separate.Visible = two;
                Put(Separate, 24, page ? 312 : 262, 572, 44);
                bool hosts = three && Mods.HostsShown(Picked, Address.Box.Text);
                PasswordName.Visible = Password.Visible = three;
                IncomingName.Visible = Incoming.Visible = OutgoingName.Visible = Outgoing.Visible = hosts;
                Status.Visible = two || three || done;
                Put(Status, 24, done ? 300 : three ? (hosts ? 276 : 200) : page ? 366 : 316, 572, 44);
                Doing.Visible = WatchSwitch.Visible = HandsSwitch.Visible = WatchNote.Visible = HandsNote.Visible = Forget.Visible = Another.Visible = done;
                BackKey.Visible = two || three;
                NextKey.Visible = one || two;
                NextKey.Enabled = Picked != null && !Picked.Closed;
                Connect.Visible = three;
                Connect.Enabled = three && !Picked.Closed && Connect.Text == "Connect";
                AcceptButton = three ? Connect : one || two ? NextKey : null;
                if (done)
                {
                    filling = true;
                    WatchSwitch.Checked = MailTools.Watch;
                    HandsSwitch.Checked = MailTools.HandsSet;
                    HandsSwitch.Dimmed = !WatchSwitch.Checked;
                    filling = false;
                }
            }

            private void Join()
            {
                string problem;
                MailAccount account = Mods.MailFromForm(Address.Box.Text, Password.Box.Text, Incoming.Box.Text, Outgoing.Box.Text, out problem);
                if (account == null) { Say(problem, Rose); return; }
                Connect.Enabled = false;
                Connect.Text = "Connecting...";
                Say("Signing in to " + account.Provider + "...", Faint);
                Run(delegate
                {
                    string trouble = mailTest(account);
                    if (trouble == null)
                    {
                        try { account.Save(MailTools.Folder); }
                        catch (Exception error) { trouble = "Connected, but couldn't save it: " + error.Message; }
                        if (trouble == null) MailTools.Account = account;
                    }
                    Back(delegate
                    {
                        Connect.Text = "Connect";
                        Password.Box.Text = "";
                        if (trouble != null) { Go(3); Say(trouble, Rose); return; }
                        Go(4);
                        Say("She can use your email now.", Cyan);
                        Frame.MailChanged(mod, mod == null ? null : mod.File.Name + " is on and connected to " + account.Address + ".");
                    });
                });
            }

            private void Switches()
            {
                if (filling) return;
                HandsSwitch.Dimmed = !WatchSwitch.Checked;
                try { MailTools.SetWatch(WatchSwitch.Checked, HandsSwitch.Checked); }
                catch (Exception error)
                {
                    if (!(error is IOException || error is UnauthorizedAccessException)) throw;
                    Say("Couldn't save that: " + error.Message, Rose);
                    return;
                }
                Say(!WatchSwitch.Checked ? "She's not watching your inbox." : HandsSwitch.Checked ? "She's watching your inbox and deals with phishing she's sure of." : "She's watching your inbox and tells you when mail lands.", WatchSwitch.Checked ? Cyan : Faint);
            }

            private void Drop()
            {
                MailTools.Forget();
                filling = true;
                WatchSwitch.Checked = HandsSwitch.Checked = false;
                HandsSwitch.Dimmed = true;
                filling = false;
                Address.Box.Text = "";
                Password.Box.Text = "";
                Picked = null;
                foreach (PlugTile tile in Tiles) { tile.Lit = false; tile.Invalidate(); }
                Go(1);
                Say("She forgot that account. Its app password is gone from this PC.", Faint);
                Frame.MailChanged(mod, null);
            }
        }

        internal void CheckMailPanel(List<string> checks, string output)
        {
            FormWindowState wasState = WindowState;
            Restore();
            string savedFolder = MailTools.Folder;
            MailAccount savedAccount = MailTools.Account;
            Action<string> wasOpen = Plugins.OpenBrowser;
            Func<MailAccount, string> wasTest = mailTest;
            string dir = Path.Combine(output, "mail-panel");
            try
            {
                Directory.CreateDirectory(dir);
                MailTools.Folder = dir;
                MailTools.Forget();
                string opened = null;
                Plugins.OpenBrowser = delegate(string url) { opened = url; };
                OpenMailSetup(null);
                MailSheet sheet = mailSheet;
                sheet.Inline = true;
                string[] names = Array.ConvertAll(Mods.MailProviders, one => one.Name);
                if (sheet.Step != 1 || sheet.Title.Text != "Which email?" || string.Join(",", Array.ConvertAll(sheet.Tiles, tile => tile.Text)) != "Gmail,iCloud,Yahoo,AOL,Fastmail,Outlook / Hotmail,Other"
                    || Array.Exists(sheet.Tiles, tile => tile.Lit || tile.Picture != Mods.Envelope || !tile.Visible) || sheet.NextKey.Enabled || sheet.BackKey.Visible || sheet.Password.Visible)
                    throw new Exception("Set up Email didn't open on Which email? with its seven envelopes and nothing picked: " + string.Join(",", names));
                if (sheet.Tiles[0].Logo != ServiceLogo.Gmail || sheet.Tiles[1].Logo != ServiceLogo.ICloud || Array.FindAll(sheet.Tiles, tile => tile.Logo != null).Length != 2)
                    throw new Exception("Set up Email's Gmail and iCloud picks didn't show their own logos, or another pick did.");
                string crowded = sheet.Crowded();
                if (crowded != null) throw new Exception("Set up Email's first step doesn't fit: " + crowded);
                sheet.Picture(Path.Combine(output, "mail-step1.png"));
                InvokeOnClick(sheet.Tiles[5], EventArgs.Empty);
                if (sheet.Picked == null || !sheet.Picked.Closed || sheet.PickNote.Text != MailAccount.OutlookNote || sheet.NextKey.Enabled || !sheet.Tiles[5].Lit) throw new Exception("Outlook didn't stop at its note.");
                InvokeOnClick(sheet.NextKey, EventArgs.Empty);
                if (sheet.Step != 1) throw new Exception("Outlook went on past its note.");
                sheet.Address.Box.Text = "sam@hotmail.co.uk";
                if (!sheet.Picked.Closed || sheet.NextKey.Enabled) throw new Exception("A Hotmail address didn't pick Outlook / Hotmail.");
                sheet.Address.Box.Text = "sam@icloud.com";
                if (sheet.Picked.Name != "iCloud" || !sheet.Tiles[1].Lit || sheet.Tiles[5].Lit || !sheet.NextKey.Enabled) throw new Exception("Typing an iCloud address didn't pick iCloud.");
                InvokeOnClick(sheet.Tiles[0], EventArgs.Empty);
                if (sheet.Picked.Name != "Gmail" || sheet.Address.Box.Text.Length != 0 || !sheet.NextKey.Enabled) throw new Exception("Picking Gmail kept an iCloud address, or left Next off.");
                InvokeOnClick(sheet.NextKey, EventArgs.Empty);
                Mods.MailProvider gmail = Mods.MailProviders[0];
                if (sheet.Step != 2 || sheet.Title.Text != "Make an app password" || sheet.StepLine.Text != "STEP 2 OF 3: GMAIL" || sheet.Lines[0].Text != gmail.Steps[0] || sheet.Lines[2].Text != gmail.Steps[2]
                    || !sheet.OpenPage.Visible || sheet.OpenPage.Text != "Open Gmail's app password page" || sheet.Separate.Text != Mods.AppPasswordLine || !sheet.Separate.Visible || sheet.Tiles[0].Visible)
                    throw new Exception("Gmail's app password step didn't show its own lines and page key: " + sheet.Lines[0].Text);
                InvokeOnClick(sheet.OpenPage, EventArgs.Empty);
                if (opened != "https://myaccount.google.com/apppasswords") throw new Exception("Open Gmail's app password page opened " + opened);
                crowded = sheet.Crowded();
                if (crowded != null) throw new Exception("Set up Email's second step doesn't fit: " + crowded);
                sheet.Picture(Path.Combine(output, "mail-step2.png"));
                InvokeOnClick(sheet.NextKey, EventArgs.Empty);
                if (sheet.Step != 3 || sheet.Title.Text != "Paste it" || !sheet.Address.Visible || !sheet.Password.Visible || sheet.Incoming.Visible || sheet.Outgoing.Visible || !sheet.Connect.Visible || sheet.NextKey.Visible)
                    throw new Exception("Gmail's paste step didn't ask for just the address and app password.");
                sheet.Address.Box.Text = "capri.demo@gmail.com";
                crowded = sheet.Crowded();
                if (crowded != null) throw new Exception("Set up Email's third step doesn't fit: " + crowded);
                sheet.Picture(Path.Combine(output, "mail-step3.png"));
                InvokeOnClick(sheet.BackKey, EventArgs.Empty);
                InvokeOnClick(sheet.BackKey, EventArgs.Empty);
                InvokeOnClick(sheet.Tiles[6], EventArgs.Empty);
                InvokeOnClick(sheet.NextKey, EventArgs.Empty);
                if (sheet.Step != 2 || sheet.OpenPage.Visible || sheet.Lines[0].Text != Mods.OtherMail.Steps[0]) throw new Exception("Other's app password step showed a page key or another email's lines.");
                InvokeOnClick(sheet.NextKey, EventArgs.Empty);
                if (sheet.Step != 3 || !sheet.Incoming.Visible || !sheet.Outgoing.Visible || !sheet.IncomingName.Visible) throw new Exception("Other didn't ask for its IMAP and SMTP servers.");
                sheet.Address.Box.Text = "sam@example.org";
                if (sheet.Picked != Mods.OtherMail || !sheet.Incoming.Visible) throw new Exception("An address Capri doesn't know didn't keep its server fields.");
                crowded = sheet.Crowded();
                if (crowded != null) throw new Exception("Set up Email's third step for Other doesn't fit: " + crowded);
                sheet.Picture(Path.Combine(output, "mail-step3-other.png"));
                checks.Add("PASS mail: Set up Email goes Which email?, Make an app password, Paste it; Gmail and iCloud show their own logos, Gmail shows its own steps and opens its app password page, Other asks for its servers, and Outlook / Hotmail stops at its note");

                MailAccount tested = null;
                mailTest = delegate(MailAccount account) { tested = account; return null; };
                sheet.Address.Box.Text = "capri.demo@gmail.com";
                sheet.Password.Box.Text = "demo-panel-pass-1";
                InvokeOnClick(sheet.Connect, EventArgs.Empty);
                if (tested == null || tested.Address != "capri.demo@gmail.com" || tested.ImapHost != "imap.gmail.com" || MailTools.Account == null || MailTools.Account.Password != "demo-panel-pass-1" || !File.Exists(MailAccount.FilePath(dir))
                    || sheet.Step != 4 || sheet.Title.Text != "Connected as capri.demo@gmail.com" || sheet.Password.Box.Text.Length != 0 || !sheet.Forget.Visible || !sheet.WatchSwitch.Visible)
                    throw new Exception("Connect didn't sign in with the pasted address and app password, save them and show who it's connected as: " + sheet.Title.Text + " / " + sheet.Status.Text);
                string kept = File.ReadAllText(MailAccount.FilePath(dir));
                Toggle watch = sheet.WatchSwitch, hands = sheet.HandsSwitch;
                if (watch.Checked || hands.Checked || !hands.Dimmed || MailTools.Watch || MailTools.OwnHands)
                    throw new Exception("Email's settings didn't open with both new switches off, and dealing with phishing waiting on watching.");
                crowded = sheet.Crowded();
                if (crowded != null) throw new Exception("Email's connected window doesn't fit: " + crowded);
                sheet.Picture(Path.Combine(output, "mail-connected.png"));
                InvokeOnClick(watch, EventArgs.Empty);
                if (!watch.Checked || !MailTools.Watch || MailTools.OwnHands || hands.Dimmed) throw new Exception("Turning on Watch for new mail didn't save it, or let her deal with phishing on her own.");
                InvokeOnClick(hands, EventArgs.Empty);
                string saved = File.ReadAllText(Path.Combine(dir, MailTools.WatchName));
                if (!MailTools.OwnHands || !saved.Contains("\"watch\":true") || !saved.Contains("\"own_hands\":true") || saved.Contains("demo-panel-pass-1") || File.ReadAllText(MailAccount.FilePath(dir)) != kept)
                    throw new Exception("Email's switches weren't saved on their own, apart from the account and its password: " + saved);
                sheet.Picture(Path.Combine(output, "mail-connected-on.png"));
                InvokeOnClick(watch, EventArgs.Empty);
                if (MailTools.Watch || MailTools.OwnHands || !MailTools.HandsSet || !hands.Dimmed) throw new Exception("Turning watching off didn't stop her dealing with phishing on her own.");
                sheet.Close();
                OpenMailSetup(null);
                if (mailSheet.Step != 4 || mailSheet.Title.Text != "Connected as capri.demo@gmail.com" || mailSheet.WatchSwitch.Checked || !mailSheet.HandsSwitch.Checked) throw new Exception("Set up Email didn't open on who it's connected as once it was.");
                sheet = mailSheet;
                InvokeOnClick(sheet.Forget, EventArgs.Empty);
                if (MailTools.Account != null || MailTools.Watch || MailTools.HandsSet || File.Exists(Path.Combine(dir, MailTools.WatchName)) || File.Exists(MailAccount.FilePath(dir)) || sheet.Step != 1 || sheet.WatchSwitch.Checked)
                    throw new Exception("Forget account kept the account or its mail switches.");
                sheet.Close();
            }
            finally
            {
                mailTest = wasTest;
                Plugins.OpenBrowser = wasOpen;
                if (mailSheet != null && !mailSheet.IsDisposed) mailSheet.Close();
                MailTools.Folder = savedFolder;
                MailTools.Account = savedAccount;
                WindowState = wasState;
            }
            checks.Add("PASS mail: Connect signs in with the pasted address and app password and saves them, then shows who it's connected as with Watch for new mail and Deal with phishing on her own, both off and saved apart from the password, and Forget account takes all of it away");
        }

        private void Started(Mod mod)
        {
            if (mod == null || !mod.On) return;
            if (Mods.Builtin(mod, "mail") && !MailTools.Ready) { OpenMailSetup(mod); ModSays(mod.File.Name + " is on. Set it up in the window that opened.", false); return; }
            if (!Mods.Online(mod) || Plugins.SignedIn(mod.File.Id)) return;
            ModSays(mod.File.Name + " is on. Sign in to it in your browser.", false);
            Plugins.SignIn(mod.File.Id, delegate(string problem)
            {
                try { BeginInvoke(new Action(delegate { ModSays(problem == null ? "Signed in to " + mod.File.Name + "." : "Couldn't sign in to " + mod.File.Name + ": " + problem, problem != null); FillMods(); RefreshPluginSheet(false); })); }
                catch (InvalidOperationException) { }
            });
        }
    }
}

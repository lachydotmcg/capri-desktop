using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Mainframe
    {
        private static readonly string[] GrantOrder = { Plugins.Usual, Plugins.Always, Plugins.Ask, Plugins.Off };
        private const int PlugTiles = 4, PlugHead = 144, PlugTop = 168, PlugTall = 94;
        private readonly PlugTile[] plugTiles = new PlugTile[PlugTiles];
        private Key plugBack, plugNext;
        private Label plugEmpty;
        private int plugFirst;
        private List<Mod> plugShown = new List<Mod>();
        private PluginSheet pluginSheet;
        private PluginTokenSheet pluginTokenSheet;

        internal static string GrantWords(string grant)
        {
            switch (grant)
            {
                case Plugins.Always: return "Always run";
                case Plugins.Ask: return "Ask first";
                case Plugins.Off: return "Off";
                default: return "Like SECURITY";
            }
        }

        private static string KeyWords(string grant, bool asks)
        {
            return asks && grant != Plugins.Off ? "Always asks" : GrantWords(grant);
        }

        internal static string NextPluginGrant(PluginTool tool)
        {
            string had = Plugins.GrantOf(tool.Plugin, tool.Permission);
            string next = Plugins.AlwaysAsks(tool) ? (had == Plugins.Off ? Plugins.Usual : Plugins.Off) : GrantOrder[(Array.IndexOf(GrantOrder, had) + 1) % GrantOrder.Length];
            Plugins.Grant(tool.Plugin, tool.Permission, next);
            return next;
        }

        private static string PluginOn(Mod mod)
        {
            if (!mod.On) return mod.File.Name + " is in, and off.";
            string skills = mod.Skills.Length == 0 ? "" : " She can read its " + (mod.Skills.Length == 1 ? "skill" : mod.Skills.Length + " skills") + " when they fit.";
            return mod.Info != null && mod.Info.Servers.Length > 0 ? mod.File.Name + " is on. Its tools are starting." + skills : mod.File.Name + " is on." + skills;
        }

        internal string PluginSheetTitle { get { return pluginSheet == null || pluginSheet.IsDisposed ? null : pluginSheet.Text; } }

        private void BuildPlugs()
        {
            Section("PLUG-INS >", 0, PlugHead);
            plugBack = Make("Back", 252, PlugHead - 3, 50, 20, delegate { plugFirst -= PlugTiles; FillPlugs(); });
            plugBack.AccessibleName = "Show the plug-ins before these";
            plugNext = Make("Next", 306, PlugHead - 3, 50, 20, delegate { plugFirst += PlugTiles; FillPlugs(); });
            plugNext.AccessibleName = "Show more plug-ins";
            Explain(plugBack, "Plug-in pages", "MainframePlugins.cs");
            Explain(plugNext, "Plug-in pages", "MainframePlugins.cs");
            plugEmpty = ModLabel("No plug-ins yet. Import mod takes one.", 28, PlugTop, 328, 20, Font, Faint);
            for (int i = 0; i < PlugTiles; i++)
            {
                int slot = i;
                PlugTile tile = new PlugTile();
                tile.Font = Type(9f, true);
                tile.Bounds = new Rectangle(28 + i * 84, PlugTop, 76, PlugTall);
                tile.Click += delegate { OpenPlugin(slot); };
                Controls.Add(tile);
                Explain(tile, "PLUG-INS >", "MainframePlugins.cs");
                plugTiles[i] = tile;
            }
        }

        private void FillPlugs()
        {
            List<Mod> plugs = Mods.PluginsOf(capri.ModList.All);
            plugShown = plugs;
            int total = plugs.Count;
            plugFirst = Math.Max(0, Math.Min(plugFirst, (Math.Max(0, total - 1) / PlugTiles) * PlugTiles));
            plugEmpty.Visible = total == 0;
            for (int i = 0; i < PlugTiles; i++)
            {
                Mod mod = plugFirst + i < total ? plugs[plugFirst + i] : null;
                PlugTile tile = plugTiles[i];
                tile.Visible = mod != null;
                if (mod == null) continue;
                string state = mod.Changed ? "Off" : Mods.PluginState(mod);
                tile.Mod = mod;
                tile.Text = mod.File.Name;
                tile.Picture = Mods.PluginPicture(mod);
                tile.Logo = Mods.PluginLogo(mod);
                tile.State = state;
                tile.StateColour = state == "Off" ? Faint : state == "Set up" || state == "Sign in" ? Rose : Cyan;
                tile.Lit = mod.On;
                tile.AccessibleName = mod.File.Name + ", " + state;
                tile.Invalidate();
            }
            bool pages = total > PlugTiles;
            plugBack.Visible = plugNext.Visible = pages;
            plugBack.Enabled = plugFirst > 0;
            plugNext.Enabled = plugFirst + PlugTiles < total;
        }

        private void ShowPlug(Mod mod)
        {
            int at = Mods.PluginsOf(capri.ModList.All).FindIndex(one => one.File.Id == mod.File.Id);
            if (at >= 0) plugFirst = at / PlugTiles * PlugTiles;
        }

        private Mod ShownPlug(int slot)
        {
            int at = plugFirst + slot;
            if (at < plugShown.Count)
            {
                string id = plugShown[at].File.Id;
                Mod current = capri.ModList.All.Find(one => one.Plugin && string.Equals(one.File.Id, id, StringComparison.OrdinalIgnoreCase));
                if (current != null) return current;
            }
            FillMods();
            ModSays("That plug-in is no longer installed. The list is updated.", true);
            return null;
        }

        private void OpenPlugin(int slot)
        {
            Mod mod = ShownPlug(slot);
            if (mod != null) OpenPluginSheet(mod);
        }

        private Mod PluginNamed(string id) { return capri.ModList.All.Find(one => one.Plugin && one.File.Id == id); }

        private void OpenPluginSheet(Mod mod)
        {
            Point? at = null;
            if (pluginSheet != null && !pluginSheet.IsDisposed)
            {
                if (pluginSheet.ModId == mod.File.Id) at = pluginSheet.Location;
                pluginSheet.Close();
            }
            int tall = PluginSheet.TallFor(mod);
            pluginSheet = new PluginSheet(this, SheetZoom(PluginSheet.Wide, tall), mod, tall);
            pluginSheet.Show(this);
            if (at.HasValue) pluginSheet.Location = at.Value;
            pluginSheet.Activate();
        }

        // Out of the window's way while they work in another one: it catches up when it's next in front.
        private void RefreshPluginSheet(bool now)
        {
            if (pluginSheet == null || pluginSheet.IsDisposed) return;
            Mod mod = PluginNamed(pluginSheet.ModId);
            if (mod == null) { pluginSheet.Close(); return; }
            if (PluginSheet.Stamp(mod) == pluginSheet.Built) return;
            if (now || Form.ActiveForm == pluginSheet) OpenPluginSheet(mod);
            else pluginSheet.Stale = true;
        }

        private void RemovePlugin(Mod mod)
        {
            try
            {
                capri.ModList.Remove(mod);
                if (pluginSheet != null && !pluginSheet.IsDisposed && pluginSheet.ModId == mod.File.Id) pluginSheet.Close();
                FillMods();
                ModSays(mod.File.Name + " is removed.", false);
            }
            catch (IOException error) { ModSays("Couldn't remove that plug-in: " + error.Message, true); }
            catch (UnauthorizedAccessException error) { ModSays("Couldn't remove that plug-in: " + error.Message, true); }
        }

        private void SignInFrom(PluginSheet sheet, Mod mod)
        {
            if (Mods.TokenServer(mod) != null) { OpenPluginToken(mod); return; }
            sheet.Main.Text = "Signing in...";
            sheet.Main.Enabled = false;
            Plugins.SignIn(mod.File.Id, delegate(string problem)
            {
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        ModSays(problem == null ? "Signed in to " + mod.File.Name + "." : "Couldn't sign in to " + mod.File.Name + ": " + problem, problem != null);
                        FillMods();
                        if (problem == null) { RefreshPluginSheet(true); return; }
                        if (sheet.IsDisposed) return;
                        sheet.State.Text = "Couldn't sign in: " + problem;
                        sheet.State.ForeColor = Rose;
                        sheet.Main.Text = "Sign in";
                        sheet.Main.Enabled = true;
                    }));
                }
                catch (InvalidOperationException) { }
            });
        }

        private void OpenPluginToken(Mod mod)
        {
            if (pluginTokenSheet != null && !pluginTokenSheet.IsDisposed) pluginTokenSheet.Close();
            pluginTokenSheet = new PluginTokenSheet(this, SheetZoom(620, 450), mod);
            pluginTokenSheet.Show(this);
            pluginTokenSheet.Activate();
        }

        private sealed class PluginTokenSheet : ModSheet
        {
            private static Label Wrapped(Label label) { label.AutoEllipsis = false; return label; }
            internal readonly Entry Token;
            internal readonly Label Status;
            internal readonly Key Connect;

            internal PluginTokenSheet(Mainframe owner, float zoom, Mod mod) : base(owner, zoom, "Connect " + mod.File.Name, "CONNECT " + mod.File.Name.ToUpperInvariant() + " >", 620, 450)
            {
                Wrapped(Words(Mods.TokenSetupNote(mod), 24, 65, 572, 82, Type(10f * Zoom), White));
                Words("Sent only to " + new Uri(Mods.TokenServer(mod).Url).Host, 24, 155, 572, 22, Small(), Faint);
                if (Mods.TokenPage(mod) != null) Make(Mods.TokenPageLabel(mod), 24, 190, 310, 30, delegate { McpSignIn.Open(Mods.TokenPage(mod)); });
                Words(Mods.TokenLabel(mod), 24, 244, 180, 22, Type(9.5f * Zoom, true), White);
                Token = new Entry { Font = Type(10f * Zoom) };
                Token.Bounds = Box(210, 240, 386, 28);
                Token.Box.UseSystemPasswordChar = true;
                Token.Box.AccessibleName = Mods.TokenLabel(mod) + " for " + mod.File.Name;
                Controls.Add(Token);
                Wrapped(Words(McpSignIn.Keeps ? "Kept on this PC, protected by Windows. Forget token turns this plug-in off and removes its saved sign-in." : "Kept only until Capri closes. Forget token turns this plug-in off.", 24, 282, 572, 42, Small(), Faint));
                Status = Wrapped(Words("", 24, 332, 572, 44, Small(), Faint));
                Connect = Make("Connect", 426, 390, 170, 30, delegate
                {
                    Mod current = Frame.PluginNamed(mod.File.Id);
                    if (current == null || !current.On) { Status.Text = "That plug-in is off or removed."; return; }
                    string value = Token.Box.Text;
                    Token.Box.Clear();
                    Connect.Enabled = false;
                    Status.Text = "Connecting...";
                    Plugins.ConnectToken(mod.File.Id, mod.Hash, value, delegate(string problem)
                    {
                        try { Frame.BeginInvoke(new Action(delegate
                        {
                            Frame.FillMods(); Frame.RefreshPluginSheet(false);
                            if (IsDisposed) return;
                            Connect.Enabled = true;
                            Status.ForeColor = problem == null ? Cyan : Rose;
                            Status.Text = problem == null ? "Connected. You can close this window." : "Couldn't connect: " + problem;
                        })); } catch (InvalidOperationException) { }
                    });
                });
                Connect.Accent = Cyan; Connect.Loud = true;
                Make("Forget token", 24, 390, 170, 30, delegate
                {
                    Mod current = Frame.PluginNamed(mod.File.Id);
                    if (current != null && current.On) Frame.Switch(current);
                    McpSignIn.Forget(mod.File.Id);
                    Token.Box.Clear(); Close();
                });
                FormClosed += delegate { Token.Box.Clear(); };
                Frame.Fit(this);
            }
        }

        // A plug-in's own window: its picture and what it does, the one thing to do next, and when she uses each of its tools.
        private sealed class PluginSheet : ModSheet
        {
            internal const int Wide = 620, ToolsTop = 290, Row = 56;
            internal readonly string ModId, Built;
            internal bool Stale;
            internal readonly PlugTile Art;
            internal readonly Label NameLine, About, State, ToolsNote;
            internal readonly Key Main, TurnOff, Remove;
            internal readonly List<Key> Grants = new List<Key>();

            internal static int TallFor(Mod mod) { return Math.Max(400, Math.Min(760, ToolsTop + Math.Max(1, Plugins.ToolsOf(mod.File.Id).Count) * Row + 24)); }

            internal static string Stamp(Mod mod)
            {
                MailAccount account = Mods.Builtin(mod, "mail") ? MailTools.Account : null;
                return mod.On + "|" + mod.Changed + "|" + Mods.PluginState(mod) + "|" + Plugins.ToolsOf(mod.File.Id).Count + "|" + Plugins.Trouble(mod.File.Id) + "|" + (account == null ? "" : account.Address);
            }

            internal PluginSheet(Mainframe owner, float zoom, Mod mod, int tall)
                : base(owner, zoom, mod.File.Name, mod.File.Name.ToUpperInvariant() + " >", Wide, tall)
            {
                ModId = mod.File.Id;
                Built = Stamp(mod);
                bool mail = Mods.Builtin(mod, "mail"), online = Mods.Online(mod);
                string state = Mods.PluginState(mod), trouble = Plugins.Trouble(mod.File.Id);
                List<PluginTool> tools = Plugins.ToolsOf(mod.File.Id);
                Art = new PlugTile { Picture = Mods.PluginPicture(mod), Logo = Mods.PluginLogo(mod), Lit = mod.On, Still = true, PlateTall = 104 };
                Art.Bounds = Box(24, 60, 128, 104);
                Art.TabStop = false;
                Art.Cursor = Cursors.Default;
                Art.AccessibleName = mod.File.Name;
                Controls.Add(Art);
                NameLine = Words(mod.File.Name, 168, 64, 428, 30, Type(16f * Zoom, true), White);
                About = Words(Mods.PluginLine(mod), 168, 98, 428, 22, Type(10f * Zoom), Faint);
                string said;
                Color tone = Cyan;
                if (mod.Changed) { said = "Changed. Connect asks you to approve it again."; tone = Rose; }
                else if (!mod.On) { said = mail ? "Off. Connect, then add your email." : Mods.TokenServer(mod) != null ? "Off. Connect, then add your " + Mods.TokenLabel(mod).ToLowerInvariant() + "." : Mods.PublicStarter(mod) ? "Off. No account or sign-in required." : online ? "Off. Connect, then sign in on its page." : "Off."; tone = Faint; }
                else if (mail) { MailAccount account = MailTools.Account; said = MailTools.Ready ? "Connected as " + account.Address : "On. Set up your email next."; if (!MailTools.Ready) tone = Rose; }
                else if (state == "Sign in") { said = Mods.TokenServer(mod) != null ? "Needs your " + Mods.TokenLabel(mod).ToLowerInvariant() + "." : "Needs you to sign in."; tone = Rose; }
                else if (Mods.PublicStarter(mod) && (trouble == null || trouble == "starting")) said = "On. No account or sign-in required.";
                else if (state == "Connected") said = "Signed in";
                else if (trouble != null && trouble != "starting") { said = "On. Its tools didn't start."; tone = Rose; }
                else said = "On";
                State = Words(said, 168, 124, 428, 22, Type(10f * Zoom, true), tone);
                Main = Make("Turn off", 24, 184, 170, 30, delegate { });
                TurnOff = Make("Turn off", 202, 184, 150, 30, delegate { Frame.Switch(Frame.PluginNamed(ModId)); });
                TurnOff.AccessibleName = "Turn off " + mod.File.Name;
                TurnOff.Visible = false;
                Main.Click += delegate
                {
                    Mod now = Frame.PluginNamed(ModId);
                    if (now == null) { Close(); return; }
                    if (!now.On || Main.Text == "Turn off") Frame.Switch(now);
                    else if (Mods.Builtin(now, "mail")) Frame.OpenMailSetup(now);
                    else if (Main.Text == "Sign in" || Main.Text == "Set up token" || Main.Text == "Token settings") Frame.SignInFrom(this, now);
                };
                if (!mod.On) Main.Text = mail || online ? "Connect" : "Turn on";
                else if (mail) Main.Text = MailTools.Ready ? "Email settings" : "Set up email";
                else if (Mods.TokenServer(mod) != null) Main.Text = state == "Connected" ? "Token settings" : "Set up token";
                else if (state == "Sign in") Main.Text = "Sign in";
                bool action = Main.Text != "Turn off";
                if (action) { Main.Accent = Cyan; Main.Loud = !mod.On || Main.Text != "Email settings"; }
                TurnOff.Visible = mod.On && action;
                Main.AccessibleName = Main.Text == "Turn on" || Main.Text == "Turn off" ? Main.Text + " " + mod.File.Name : Main.Text + (Main.Text == "Sign in" || Main.Text == "Connect" ? " to " : " for ") + mod.File.Name;
                Remove = Make("Remove", 456, 184, 140, 30, delegate { Mod now = Frame.PluginNamed(ModId); if (now != null) Frame.RemovePlugin(now); else Close(); });
                Remove.AccessibleName = "Remove " + mod.File.Name;
                Remove.Visible = !mod.Starter;
                Heading(this, "TOOLS >", new Point(Z(38), Z(232)), Type(10f * Zoom, true), White, 572);
                string note = tools.Count > 0 ? "Like SECURITY asks first the way SECURITY says."
                    : !mod.On ? "Turn it on to see its tools."
                    : mod.Info == null || mod.Info.Servers.Length == 0 ? "No tools. " + (mod.Skills.Length == 1 ? "It brings a skill she reads when it fits." : "It brings skills she reads when they fit.")
                    : trouble == "starting" ? "Her tools from it are starting."
                    : trouble == McpSignInNeeded.Said ? (Mods.PublicStarter(mod) ? "This public service could not be reached." : Mods.TokenServer(mod) != null ? "Add your token to see its tools." : "Sign in to see its tools.")
                    : trouble != null ? "Its tools didn't start: " + trouble
                    : "Her tools from it show here once it has started.";
                ToolsNote = Words(note, 24, 258, 572, 22, Small(), tools.Count == 0 && trouble != null && trouble != "starting" && trouble != McpSignInNeeded.Said && mod.On ? Rose : Faint);
                if (tools.Count > 0)
                {
                    Panel rows = new Panel { BackColor = Ground, ForeColor = White, AutoScroll = true, Bounds = Box(24, ToolsTop, 572, tall - ToolsTop - 24) };
                    int y = 0;
                    foreach (PluginTool tool in tools)
                    {
                        PluginTool one = tool;
                        bool asks = Plugins.AlwaysAsks(tool);
                        string title = mod.Info.Servers.Length > 1 ? tool.Server + ": " + tool.Title : tool.Title;
                        Label name = new Note { Text = title, Font = Type(10f * Zoom, true), ForeColor = White, AutoSize = false, AutoEllipsis = true, Bounds = Box(0, y, 400, 20), UseMnemonic = false };
                        Label about = new Note { Text = tool.Description, Font = Look.Mono(8.5f * Zoom, FontStyle.Regular, GraphicsUnit.Point), ForeColor = Faint, AutoSize = false, AutoEllipsis = true, Bounds = Box(0, y + 20, 400, 30), UseMnemonic = false };
                        Key grant = new Key { Bounds = Box(412, y + 6, 140, 26), Font = Type(9f * Zoom, true), Text = KeyWords(Plugins.GrantOf(mod.File.Id, tool.Permission), asks) };
                        grant.AccessibleName = "When she uses " + title;
                        grant.Accent = Plugins.GrantOf(mod.File.Id, tool.Permission) == Plugins.Off ? Rose : Cyan;
                        grant.Click += delegate
                        {
                            string next = NextPluginGrant(one);
                            grant.Text = KeyWords(next, asks);
                            grant.Accent = next == Plugins.Off ? Rose : Cyan;
                            grant.Invalidate();
                        };
                        rows.Controls.Add(name);
                        rows.Controls.Add(about);
                        rows.Controls.Add(grant);
                        Grants.Add(grant);
                        y += Row;
                    }
                    Controls.Add(rows);
                }
                Frame.Fit(this);
            }

            protected override void OnActivated(EventArgs e)
            {
                base.OnActivated(e);
                if (!Stale) return;
                Stale = false;
                try { Frame.BeginInvoke(new Action(delegate { Frame.RefreshPluginSheet(true); })); }
                catch (InvalidOperationException) { }
            }
        }

        // A plug-in on the Mods page: its logo or picture on a plate cut at every corner, its name, and one word for where it's
        // at. The email window's picks are these too, each in its own colour.
        private sealed class PlugTile : Button
        {
            internal Mod Mod;
            internal int Picture;
            internal ServiceLogo Logo;
            internal string State = "";
            internal Color StateColour = Color.Empty;
            internal Color Tint = Color.Empty;
            internal bool Lit, Still;
            internal int PlateTall = 60;
            private bool hot, down;

            internal PlugTile()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
                BackColor = Ground;
                Cursor = Cursors.Hand;
            }

            internal bool Marked { get { return !Still && Focused && ShowFocusCues; } }

            internal Rectangle NameArea(float s)
            {
                int top = (int)Math.Round(PlateTall * s);
                return new Rectangle(0, top, Width, Math.Max(1, Math.Min(Height - top, (int)Math.Round(18 * s))));
            }

            internal Rectangle StateArea(float s)
            {
                Rectangle name = NameArea(s);
                return new Rectangle(0, name.Bottom, Width, Math.Max(1, Height - name.Bottom));
            }

            internal Font SmallFont() { return new Font(Font.FontFamily, Font.Size * 0.86f, Font.Style, Font.Unit); }

            internal float CutSize(float s) { return Math.Min(10 * s, (PlateTall * s - 8 * s) / 4); }

            protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }

            protected override void OnMouseLeave(EventArgs e) { hot = false; down = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }

            protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                float s = ScaleOf(this);
                Graphics graphics = e.Graphics;
                Backdrop(graphics, this, s);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float pad = 4 * s;
                RectangleF plate = new RectangleF(pad, pad, Width - 2 * pad, PlateTall * s - 2 * pad);
                float cut = CutSize(s);
                bool live = !Still && Enabled && (hot || Marked);
                PluginArt.Plate(graphics, plate, s, cut, Lit || live ? Cyan : DeepEdge, Neon.Sheet, Lit, live ? (down ? 44 : 22) : 0);
                Color ink = !Tint.IsEmpty ? Tint : Lit ? Cyan : Faint;
                Color accent = !Tint.IsEmpty ? Tint : Lit ? Rose : Faint;
                Color fill = !Tint.IsEmpty ? Color.FromArgb(Tint.R / 6, Tint.G / 6, Tint.B / 6) : Lit ? Deep : Field;
                float inset = Math.Max(8 * s, plate.Height * 0.16f);
                RectangleF art = RectangleF.Inflate(plate, -inset, -inset);
                if (Logo == null) PluginArt.Draw(graphics, Picture, art, ink, accent, fill);
                else
                {
                    Color brand = ColorTranslator.FromHtml(Logo.Ink);
                    PluginArt.Logo(graphics, Logo, art, Neon.Tone(brand.R, brand.G, brand.B));
                }
                if (Marked || Spotted(this)) using (GraphicsPath mark = PluginArt.Around(plate, cut, 2 * s)) FocusMark(graphics, mark, s, Spotted(this));
                TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
                if (Text.Length > 0) TextRenderer.DrawText(graphics, Lingo.Of(Text), Font, NameArea(s), Lit || !Tint.IsEmpty ? White : Faint, flags);
                if (State.Length > 0) using (Font small = SmallFont()) TextRenderer.DrawText(graphics, Lingo.Of(State), small, StateArea(s), StateColour.IsEmpty ? Faint : StateColour, flags);
            }
        }

        internal void CheckPluginPanel(List<string> checks, Mod mod)
        {
            if (PluginOn(mod) != mod.File.Name + " is on. Its tools are starting. She can read its skill when they fit.") throw new Exception("Importing a plug-in didn't say what it has: " + PluginOn(mod));
            bool wasShown = modded;
            FormWindowState wasState = WindowState;
            Restore();
            try
            {
                ShowMods(true);
                int at = plugShown.FindIndex(one => one.File.Id == mod.File.Id);
                PlugTile tile = at >= plugFirst && at < plugFirst + PlugTiles ? plugTiles[at - plugFirst] : null;
                if (tile == null || !tile.Visible || tile.Text != mod.File.Name || tile.Picture != Mods.Plug || tile.State != "On" || !tile.Lit || modShown.Exists(one => one.Plugin))
                    throw new Exception("An imported plug-in didn't get a lit tile under PLUG-INS, apart from INSTALLED: " + (tile == null ? "no tile" : tile.Text + " " + tile.State));
                InvokeOnClick(tile, EventArgs.Empty);
                PluginSheet sheet = pluginSheet;
                if (sheet == null || sheet.IsDisposed || sheet.ModId != mod.File.Id || sheet.Main.Text != "Turn off" || sheet.TurnOff.Visible || !sheet.Remove.Visible || sheet.State.Text != "On")
                    throw new Exception("Its tile didn't open its own window with Turn off and Remove: " + (sheet == null ? "none" : sheet.Text + " / " + sheet.Main.Text + " / " + sheet.State.Text));
                string crowded = sheet.Crowded();
                if (crowded != null) throw new Exception("A plug-in's window doesn't fit: " + crowded);
                List<Key> keys = sheet.Grants;
                if (keys.Count != 3 || !keys.TrueForAll(key => key.Text == "Like SECURITY")) throw new Exception("A plug-in's tools didn't each get a Like SECURITY key.");
                InvokeOnClick(keys[0], EventArgs.Empty);
                if (keys[0].Text != "Always run" || Plugins.GrantOf(mod.File.Id, "echo") != Plugins.Always) throw new Exception("A plug-in tool's key didn't set it to always run.");
                InvokeOnClick(keys[0], EventArgs.Empty);
                InvokeOnClick(keys[0], EventArgs.Empty);
                if (keys[0].Text != "Off" || Plugins.GrantOf(mod.File.Id, "echo") != Plugins.Off) throw new Exception("A plug-in tool's key didn't go round to off.");
                InvokeOnClick(keys[0], EventArgs.Empty);
                if (keys[0].Text != "Like SECURITY" || Plugins.GrantOf(mod.File.Id, "echo") != Plugins.Usual) throw new Exception("A plug-in tool's key didn't come back round to its usual.");
                sheet.Close();
            }
            finally
            {
                if (pluginSheet != null && !pluginSheet.IsDisposed) pluginSheet.Close();
                if (!wasShown) ShowMods(false);
                WindowState = wasState;
            }
            checks.Add("PASS plug-ins: an imported plug-in gets a lit tile under PLUG-INS and none under INSTALLED, and its window sets each tool to always run, ask first, off or like SECURITY");
        }

        private void CheckPlugSection(List<string> checks, string output)
        {
            Mods mods = capri.ModList;
            string startersFile = Path.Combine(mods.PluginFolder, "starters.txt");
            Func<ModFile, string, bool> wasAsk = ModAsk;
            Action<string> wasOpen = Plugins.OpenBrowser;
            string mailWas = MailTools.Folder;
            MailAccount accountWas = MailTools.Account;
            List<string> extra = new List<string>();
            FormWindowState wasState = WindowState;
            Restore();
            try
            {
                MailTools.Folder = Path.Combine(output, "plug-section-mail");
                Directory.CreateDirectory(MailTools.Folder);
                MailTools.Reload();
                Plugins.OpenBrowser = delegate(string url) { throw new McpException("No browser in this check."); };
                mods.AddStarters();
                mods.Load();
                ShowMods(true);
                string[] names = { "Email", "Notion", "Linear", "Todoist" };
                int[] pictures = { Mods.Envelope, Mods.Page, Mods.Checklist, Mods.Plug };
                ServiceLogo[] logos = { null, ServiceLogo.Notion, ServiceLogo.Linear, ServiceLogo.Todoist };
                for (int i = 0; i < 4; i++)
                    if (!plugTiles[i].Visible || plugTiles[i].Text != names[i] || plugTiles[i].Picture != pictures[i] || plugTiles[i].Logo != logos[i] || plugTiles[i].State != "Off" || plugTiles[i].Lit)
                        throw new Exception("PLUG-INS didn't show " + names[i] + " with its logo or picture, off: " + plugTiles[i].Text + " " + plugTiles[i].State);
                if (plugEmpty.Visible || !plugBack.Visible || !plugNext.Visible || plugBack.Enabled || !plugNext.Enabled || modShown.Exists(one => one.Plugin) || modNames[0].Visible || !modEmpty.Visible)
                    throw new Exception("The starters did not page separately from INSTALLED.");
                modStatusTimer.Stop();
                modStatus.Text = "";
                using (Bitmap page = new Bitmap(Width, Height))
                {
                    DrawToBitmap(page, new Rectangle(Point.Empty, Size));
                    page.Save(Path.Combine(output, "mods-plugins.png"), ImageFormat.Png);
                }
                string crowded = PlugsCrowded();
                if (crowded != null) throw new Exception("The Mods page with PLUG-INS overlaps " + crowded);
                checks.Add("PASS plug-ins: PLUG-INS on the Mods page shows Email, Notion and Linear as tiles, Email with an envelope and Notion and Linear with their own logos, each saying Off, INSTALLED lists none of them, and nothing overlaps at 100% or 150% in her font or the fallback");
                CheckModsFitWithPlugs(checks, output);

                InvokeOnClick(plugTiles[1], EventArgs.Empty);
                PluginSheet notion = pluginSheet;
                if (notion == null || notion.IsDisposed || notion.ModId != "notion" || notion.Text != "Notion" || notion.Art.Logo != ServiceLogo.Notion || notion.Main.Text != "Connect" || notion.TurnOff.Visible || notion.Remove.Visible
                    || notion.About.Text != "Searches, reads and edits your Notion pages." || notion.State.Text != "Off. Connect, then sign in on its page.")
                    throw new Exception("Notion's tile didn't open Notion's window with Connect: " + (notion == null ? "none" : notion.Text + " / " + notion.Main.Text + " / " + notion.State.Text));
                crowded = notion.Crowded();
                if (crowded != null) throw new Exception("Notion's window doesn't fit: " + crowded);
                notion.Picture(Path.Combine(output, "plugin-sheet-notion.png"));
                notion.Close();

                foreach (string tokenId in new[] { "github", "context7" })
                {
                    Mod tokenMod = PluginNamed(tokenId);
                    OpenPluginToken(tokenMod);
                    if (pluginTokenSheet == null || !pluginTokenSheet.Token.Box.UseSystemPasswordChar || pluginTokenSheet.Token.Box.Text.Length != 0 || pluginTokenSheet.Token.Box.AccessibleName != Mods.TokenLabel(tokenMod) + " for " + tokenMod.File.Name) throw new Exception("The API-key or personal-token entry was not empty, masked and labelled.");
                    crowded = pluginTokenSheet.Crowded();
                    if (crowded != null) throw new Exception("Credential setup doesn't fit: " + crowded);
                    pluginTokenSheet.Picture(Path.Combine(output, "plugin-token-" + tokenId + ".png"));
                    pluginTokenSheet.Close();
                }

                string consent = null;
                ModAsk = delegate(ModFile file, string hash) { consent = Mods.Consent(file, hash); return false; };
                InvokeOnClick(plugTiles[0], EventArgs.Empty);
                PluginSheet email = pluginSheet;
                if (email == null || email.ModId != Mods.MailStarter || email.Main.Text != "Connect") throw new Exception("Email's tile didn't open Email's window with Connect.");
                InvokeOnClick(email.Main, EventArgs.Empty);
                Mod mail = PluginNamed(Mods.MailStarter);
                if (consent == null || !consent.EndsWith("Turn this plug-in on?", StringComparison.Ordinal) || mail.On || MailSheetTitle != null || modStatus.Text != "Email stays off. Nothing ran.")
                    throw new Exception("Connect on Email didn't ask with her plug-in box first, or a no still turned it on: " + modStatus.Text);
                consent = null;
                ModAsk = delegate(ModFile file, string hash) { consent = Mods.Consent(file, hash); return true; };
                InvokeOnClick(pluginSheet.Main, EventArgs.Empty);
                mail = PluginNamed(Mods.MailStarter);
                if (consent == null || !mail.On || MailSheetTitle != "Set up Email" || mailSheet.Step != 1 || plugTiles[0].State != "Set up" || pluginSheet == null || pluginSheet.IsDisposed || pluginSheet.Main.Text != "Set up email" || !pluginSheet.TurnOff.Visible)
                    throw new Exception("Connect on Email didn't turn it on and open its setup at step 1: " + MailSheetTitle + " / " + plugTiles[0].State);
                mailSheet.Close();
                InvokeOnClick(pluginSheet.TurnOff, EventArgs.Empty);
                if (PluginNamed(Mods.MailStarter).On || plugTiles[0].State != "Off" || pluginSheet.Main.Text != "Connect") throw new Exception("Turn off on Email's window didn't turn it off.");
                pluginSheet.Close();
                MailTools.Account = new MailAccount { Address = "capri.demo@gmail.com", Password = "demo-tile-pass" };
                FillMods();
                if (plugTiles[0].Logo != ServiceLogo.Gmail || plugTiles[0].Picture != Mods.Envelope) throw new Exception("Email's tile didn't show Gmail's logo once a Gmail account was connected.");
                MailTools.Account = null;
                FillMods();
                if (plugTiles[0].Logo != null) throw new Exception("Email's tile kept a logo with no account connected.");
                checks.Add("PASS plug-ins: a tile opens that plug-in's own window, Notion's with its page, Connect and no Remove, and Connect on Email asks with her plug-in box, stays off on a no, and on a yes turns it on and opens Set up Email, and Email's tile shows Gmail's logo once a Gmail account is connected");

                for (int i = 0; i < 2; i++)
                {
                    string id = "smoke-plug-" + (char)('a' + i);
                    string file = Path.Combine(mods.Folder, id + Mods.PluginExtension);
                    WritePlugin(file, id, "Smoke Plug " + (char)('A' + i));
                    extra.Add(file);
                }
                mods.Load();
                FillMods();
                if (!plugBack.Visible || !plugNext.Visible || plugBack.Enabled || !plugNext.Enabled || !plugTiles[3].Visible || plugTiles[3].Picture != Mods.Plug)
                    throw new Exception("The starters and imported plug-ins didn't page under PLUG-INS.");
                crowded = PlugsCrowded();
                if (crowded != null) throw new Exception("PLUG-INS with Back and Next overlaps " + crowded);
                List<Mod> expectedPlugs = Mods.PluginsOf(mods.All);
                for (int first = 0; first < expectedPlugs.Count; first += PlugTiles)
                {
                    if (plugFirst != first || plugBack.Enabled != (first > 0) || plugNext.Enabled != (first + PlugTiles < expectedPlugs.Count)) throw new Exception("Plug-in paging controls did not match the current page.");
                    for (int slot = 0; slot < PlugTiles; slot++)
                    {
                        int at = first + slot;
                        if (plugTiles[slot].Visible != (at < expectedPlugs.Count) || at < expectedPlugs.Count && plugTiles[slot].Text != expectedPlugs[at].File.Name) throw new Exception("Plug-in paging skipped or repeated an expected starter or import.");
                    }
                    if (plugNext.Enabled) InvokeOnClick(plugNext, EventArgs.Empty);
                }
                while (plugBack.Enabled) InvokeOnClick(plugBack, EventArgs.Empty);
                if (plugFirst != 0 || plugTiles[0].Text != "Email") throw new Exception("Back didn't go back to the first plug-ins.");
                checks.Add("PASS plug-ins: more than four plug-ins page with Back and Next in the PLUG-INS band");
            }
            finally
            {
                ModAsk = wasAsk;
                Plugins.OpenBrowser = wasOpen;
                if (pluginSheet != null && !pluginSheet.IsDisposed) pluginSheet.Close();
                if (mailSheet != null && !mailSheet.IsDisposed) mailSheet.Close();
                if (pluginTokenSheet != null && !pluginTokenSheet.IsDisposed) pluginTokenSheet.Close();
                foreach (Mod one in mods.All.ToArray()) if (one.Plugin) mods.Remove(one);
                foreach (string file in extra) try { if (File.Exists(file)) File.Delete(file); } catch (IOException) { }
                try { if (File.Exists(startersFile)) File.Delete(startersFile); }
                catch (IOException) { }
                mods.Load();
                MailTools.Folder = mailWas;
                MailTools.Account = accountWas;
                FillMods();
                WindowState = wasState;
            }
        }

        private string PlugsCrowded()
        {
            int[][] scales = { new[] { 96, 0 }, new[] { 96, 1 }, new[] { 144, 0 } };
            try
            {
                foreach (int[] scale in scales)
                {
                    LayoutScale(scale[0], 100, scale[1] == 1);
                    ShowMods(true);
                    string crowded = TextFits() ?? Overlap();
                    if (crowded != null) return "at " + scale[0] * 100 / 96 + "%" + (scale[1] == 1 ? " on the fallback font: " : ": ") + crowded;
                }
                return null;
            }
            finally { LayoutScale(0, 0); ShowMods(true); }
        }

        private static void WritePlugin(string file, string id, string name)
        {
            string manifest = "{\"name\":\"" + id + "\",\"displayName\":\"" + name + "\",\"version\":\"1.0.0\",\"author\":{\"name\":\"Smoke\"},\"description\":\"A plug-in for the Mods page.\",\"mcpServers\":{\"web\":{\"type\":\"http\",\"url\":\"https://example.com/mcp\"}},\"capri\":{\"permissions\":[\"network\"]}}";
            using (MemoryStream bytes = new MemoryStream())
            {
                using (ZipArchive zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
                using (Stream into = zip.CreateEntry(".claude-plugin/plugin.json").Open())
                {
                    byte[] text = new UTF8Encoding(false).GetBytes(manifest);
                    into.Write(text, 0, text.Length);
                }
                File.WriteAllBytes(file, bytes.ToArray());
            }
        }
    }
}

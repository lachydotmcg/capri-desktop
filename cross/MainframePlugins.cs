using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace CapriDesktop
{
    // Her plug-ins on the Mods page, like Windows' (app/MainframePlugins.cs): PLUG-INS above INSTALLED, a tile for each with its
    // picture, its name and one word for where it's at, four at a time, and a window for each with the one thing to do next.
    internal sealed partial class Mainframe
    {
        private const int PlugTiles = 4, PlugHead = 180, PlugTop = 204;
        private readonly PlugTile[] plugTiles = new PlugTile[PlugTiles];
        private NeonButton plugBack, plugNext;
        private NeonCaption plugEmpty;
        private int plugFirst;
        private List<Mod> plugShown = new List<Mod>();
        internal PluginWindow PluginSheet { get; private set; }
        internal PluginTokenWindow PluginTokenSheet { get; private set; }
        internal PlugTile[] PlugTileRow { get { return plugTiles; } }
        internal NeonButton PlugBack { get { return plugBack; } }
        internal NeonButton PlugNext { get { return plugNext; } }
        internal int PlugFirst { get { return plugFirst; } }
        internal bool PlugEmptyShown { get { return plugEmpty.IsVisible; } }
        internal bool InstalledHasPlugins { get { return modShown.Exists(one => one.Plugin); } }

        private void BuildPlugs()
        {
            Section("PLUG-INS", 0, PlugHead);
            plugBack = Key("Back", 252, PlugHead - 3, 50, 20, delegate { plugFirst -= PlugTiles; FillMods(); });
            AutomationProperties.SetName(plugBack, "Show the plug-ins before these");
            MainframeTips.Explain(plugBack, "Plug-in pages");
            plugNext = Key("Next", 306, PlugHead - 3, 50, 20, delegate { plugFirst += PlugTiles; FillMods(); });
            AutomationProperties.SetName(plugNext, "Show more plug-ins");
            MainframeTips.Explain(plugNext, "Plug-in pages");
            plugEmpty = Caption("No plug-ins yet. Import mod takes one.", 28, PlugTop, 328, 20, MainframeTheme.Faint);
            for (int i = 0; i < PlugTiles; i++)
            {
                int slot = i;
                PlugTile tile = Add(new PlugTile(), 28 + i * 84, PlugTop, 76, 94);
                tile.Click += delegate { OpenPlugin(slot); };
                MainframeTips.Explain(tile, "PLUG-INS");
                plugTiles[i] = tile;
            }
            // A plug-in's tools start and stop on their own threads, so its tile and window catch up here.
            Plugins.ToolsChanged = delegate { Dispatcher.UIThread.Post(delegate { Program.Guard(delegate { if (!closed) { FillMods(); RefreshPluginSheet(); } }); }); };
        }

        private void FillPlugs()
        {
            List<Mod> plugs = Mods.PluginsOf(pet.ModList.All);
            plugShown = plugs;
            int total = plugs.Count;
            plugFirst = Math.Max(0, Math.Min(plugFirst, (Math.Max(0, total - 1) / PlugTiles) * PlugTiles));
            if (!modded) return;
            plugEmpty.IsVisible = total == 0;
            for (int i = 0; i < PlugTiles; i++)
            {
                Mod mod = plugFirst + i < total ? plugs[plugFirst + i] : null;
                PlugTile tile = plugTiles[i];
                tile.IsVisible = mod != null;
                if (mod == null) continue;
                string state = mod.Changed ? "Off" : Mods.PluginState(mod);
                tile.Mod = mod;
                tile.Text = mod.File.Name;
                tile.Picture = Mods.PluginPicture(mod);
                tile.Logo = Mods.PluginLogo(mod);
                tile.State = state;
                tile.StateInk = state == "Off" ? MainframeTheme.Faint : state == "Set up" || state == "Sign in" ? MainframeTheme.Rose : MainframeTheme.Cyan;
                tile.Lit = mod.On;
                AutomationProperties.SetName(tile, mod.File.Name + ", " + state);
                tile.InvalidateVisual();
            }
            bool pages = total > PlugTiles;
            plugBack.IsVisible = plugNext.IsVisible = pages;
            plugBack.IsEnabled = plugFirst > 0;
            plugNext.IsEnabled = plugFirst + PlugTiles < total;
        }

        private void ShowPlug(Mod mod)
        {
            int at = Mods.PluginsOf(pet.ModList.All).FindIndex(one => one.File.Id == mod.File.Id);
            if (at >= 0) plugFirst = at / PlugTiles * PlugTiles;
        }

        private Mod PluginNamed(string id)
        {
            return pet.ModList.All.Find(one => one.Plugin && string.Equals(one.File.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        internal void OpenPlugin(int slot)
        {
            Mod mod = plugFirst + slot < plugShown.Count ? PluginNamed(plugShown[plugFirst + slot].File.Id) : null;
            if (mod != null) { OpenPluginSheet(mod); return; }
            ModsChanged();
            ModSays("That plug-in is no longer installed. The list is updated.");
        }

        internal void OpenPluginSheet(Mod mod)
        {
            PluginWindow open = PluginSheet;
            if (open != null && open.ModId == mod.File.Id) { open.Fill(mod); open.WindowState = WindowState.Normal; open.Activate(); return; }
            if (open != null) { PluginSheet = null; open.Close(); }
            PluginWindow sheet = new PluginWindow(pet, this, mod);
            PluginSheet = sheet;
            sheet.Closing += delegate { if (PluginSheet == sheet) PluginSheet = null; };
            sheet.Closed += delegate { if (PluginSheet == sheet) PluginSheet = null; };
            ShowSheet(sheet);
        }

        private void RefreshPluginSheet()
        {
            PluginWindow open = PluginSheet;
            if (open == null) return;
            Mod mod = PluginNamed(open.ModId);
            if (mod == null) { PluginSheet = null; open.Close(); return; }
            open.Fill(mod);
        }

        internal void PluginMain(PluginWindow sheet)
        {
            Mod mod = PluginNamed(sheet.ModId);
            if (mod == null) { RefreshPluginSheet(); return; }
            if (!mod.On || sheet.Main.Text == "Turn off") Switch(mod);
            else if (Mods.Builtin(mod, "mail")) OpenMailSetup(mod);
            else if (Mods.TokenServer(mod) != null) OpenPluginToken(mod);
            else if (sheet.Main.Text == "Sign in")
            {
                Plugins.SignIn(mod.File.Id, delegate(string problem) { Dispatcher.UIThread.Post(delegate { ModSays(problem == null ? "Signed in to " + mod.File.Name + "." : "Couldn't sign in: " + problem); ModsChanged(); }); });
            }
        }

        internal void OpenPluginToken(Mod mod)
        {
            if (PluginTokenSheet != null) PluginTokenSheet.Close();
            PluginTokenWindow sheet = new PluginTokenWindow(pet, this, mod);
            PluginTokenSheet = sheet;
            sheet.Closed += delegate { if (PluginTokenSheet == sheet) PluginTokenSheet = null; };
            ShowSheet(sheet);
        }

        internal void PluginTokenChanged(string id, bool forget)
        {
            Mod mod = PluginNamed(id);
            if (forget)
            {
                if (mod != null && mod.On) Switch(mod);
                McpSignIn.Forget(id);
            }
            ModsChanged();
        }

        internal void PluginTurnOff(PluginWindow sheet)
        {
            Mod mod = PluginNamed(sheet.ModId);
            if (mod == null) RefreshPluginSheet();
            else if (mod.On) Switch(mod);
        }

        internal void RemovePlugin(PluginWindow sheet)
        {
            if (Refused(true)) return;
            Mod mod = PluginNamed(sheet.ModId);
            if (mod != null)
            {
                try { pet.ModList.Remove(mod); ModSays("Removed " + mod.File.Name + "."); }
                catch (Exception error) { ModSays(error.Message); }
            }
            ModsChanged();
        }
    }

    internal sealed class PluginTokenWindow : MainframeWindow
    {
        internal string Crowded() { return ModsWindow.Crowded(Page, 620, 450); }
        internal readonly NeonEntry Token;
        internal readonly NeonCaption Status;
        internal readonly NeonButton Connect;

        internal PluginTokenWindow(Pet owner, Mainframe frame, Mod mod) : base("Connect " + mod.File.Name)
        {
            Steered = true; FitsScreen = true; ShowInTaskbar = false;
            NeonButton shut = Put(Page, new NeonButton { Glyph = 2 }, 566, 20, 26, 22, "Close");
            shut.Click += delegate { Close(); };
            Put(Page, new NeonCaption { Text = "CONNECT " + mod.File.Name.ToUpperInvariant(), Ink = MainframeTheme.Rose, Size = 20, Bold = true, Display = true }, 24, 15, 540, 29);
            Put(Page, new NeonCaption { Text = Mods.TokenSetupNote(mod), Ink = MainframeTheme.White, Size = MainframeTheme.Size, Top = true, Wrap = true }, 24, 65, 572, 82);
            Put(Page, new NeonCaption { Text = "Sent only to " + new Uri(Mods.TokenServer(mod).Url).Host, Ink = MainframeTheme.Faint }, 24, 155, 572, 22);
            if (mod.Starter && mod.File.Id == "github")
            {
                NeonButton page = Put(Page, new NeonButton { Text = "Open GitHub's token settings" }, 24, 190, 310, 30);
                page.Click += delegate { McpSignIn.Open("https://github.com/settings/personal-access-tokens"); };
            }
            Put(Page, new NeonCaption { Text = "Personal access token", Ink = MainframeTheme.White, Bold = true }, 24, 244, 180, 22);
            Token = Put(Page, new NeonEntry("", true), 210, 240, 386, 28, "Personal access token for " + mod.File.Name);
            AutomationProperties.SetName(Token.Box, "Personal access token for " + mod.File.Name);
            Put(Page, new NeonCaption { Text = "Kept only until Capri closes. Forget token turns this plug-in off.", Ink = MainframeTheme.Faint, Top = true, Wrap = true }, 24, 282, 572, 42);
            Status = Put(Page, new NeonCaption { Text = "", Ink = MainframeTheme.Faint, Top = true, Wrap = true }, 24, 332, 572, 44);
            Connect = Put(Page, new NeonButton { Text = "Connect", Loud = true, Accent = MainframeTheme.Cyan }, 426, 390, 170, 30);
            Connect.Click += delegate
            {
                string value = Token.Box.Text;
                Token.Box.Text = "";
                Connect.IsEnabled = false;
                Status.Text = "Connecting...";
                Plugins.ConnectToken(mod.File.Id, mod.Hash, value, delegate(string problem) { Dispatcher.UIThread.Post(delegate
                {
                    frame.PluginTokenChanged(mod.File.Id, false);
                    Connect.IsEnabled = true;
                    Status.Ink = problem == null ? MainframeTheme.Cyan : MainframeTheme.Rose;
                    Status.Text = problem == null ? "Connected. You can close this window." : "Couldn't connect: " + problem;
                }); });
            };
            NeonButton forget = Put(Page, new NeonButton { Text = "Forget token" }, 24, 390, 170, 30);
            forget.Click += delegate { frame.PluginTokenChanged(mod.File.Id, true); Token.Box.Text = ""; Close(); };
            Closed += delegate { Token.Box.Text = ""; };
            double zoom = Pet.FrameSize(owner.Saved.MainframeSize) == 0 ? 1 : owner.Saved.MainframeSize / 100.0;
            Fit(620, 450, zoom);
        }
    }

    // A plug-in's own window, like Windows': its picture and what it does, the one thing to do next, and its tools. Mac and Linux
    // have no keys for each tool yet, so they ask first the way SECURITY says.
    internal sealed class PluginWindow : MainframeWindow
    {
        internal const int Wide = 620, ToolsTop = 290, Row = 52;
        private readonly double zoom;
        private readonly List<Control> toolRows = new List<Control>();
        private int tall;
        internal readonly string ModId;
        internal readonly PlugTile Art;
        internal readonly NeonCaption NameLine, About, State, ToolsNote;
        internal readonly NeonButton Main, TurnOff, Remove;
        internal int ToolsShown { get { return toolRows.Count / 2; } }

        internal PluginWindow(Pet owner, Mainframe frame, Mod mod) : base(mod.File.Name)
        {
            ModId = mod.File.Id;
            zoom = Pet.FrameSize(owner.Saved.MainframeSize) == 0 ? 1 : owner.Saved.MainframeSize / 100.0;
            Steered = true; FitsScreen = true;
            ShowInTaskbar = false;
            Label(mod.File.Name.ToUpperInvariant(), 24, 15, 500, 29, MainframeTheme.Rose, 20, true).Display = true;
            NeonButton shut = Put(Page, new NeonButton { Glyph = 2 }, Wide - 54, 20, 26, 22, "Close");
            shut.Click += delegate { Close(); };
            Art = Put(Page, new PlugTile { Still = true, PlateTall = 104, IsEnabled = false, IsHitTestVisible = false }, 24, 60, 128, 104, mod.File.Name);
            NameLine = Label(mod.File.Name, 168, 64, 428, 30, MainframeTheme.White, 20, true);
            About = Label("", 168, 98, 428, 22);
            State = Label("", 168, 124, 428, 22, MainframeTheme.Cyan, MainframeTheme.Size, true);
            Main = Put(Page, new NeonButton { Text = "Connect" }, 24, 184, 170, 30);
            TurnOff = Put(Page, new NeonButton { Text = "Turn off" }, 202, 184, 150, 30);
            Remove = Put(Page, new NeonButton { Text = "Remove" }, 456, 184, 140, 30);
            Put(Page, new NeonHead { Text = "TOOLS" }, 24, 227, 572, 24);
            ToolsNote = Label("", 24, 258, 572, 22, MainframeTheme.Faint, 34.0 / 3);
            Main.Click += delegate { frame.PluginMain(this); };
            TurnOff.Click += delegate { frame.PluginTurnOff(this); };
            Remove.Click += delegate { frame.RemovePlugin(this); };
            Fill(mod);
        }

        private NeonCaption Label(string text, int x, int y, int width, int height, IBrush ink = null, double size = MainframeTheme.Size, bool bold = false)
        {
            return Put(Page, new NeonCaption { Text = text, Ink = ink ?? MainframeTheme.Faint, Size = size, Bold = bold, Ellipsis = true }, x, y, width, height);
        }

        internal void Fill(Mod mod)
        {
            bool mail = Mods.Builtin(mod, "mail"), online = Mods.Online(mod);
            string state = Mods.PluginState(mod), trouble = Plugins.Trouble(mod.File.Id), name = mod.File.Name;
            List<PluginTool> tools = Plugins.ToolsOf(mod.File.Id);
            Art.Picture = Mods.PluginPicture(mod);
            Art.Logo = Mods.PluginLogo(mod);
            Art.Lit = mod.On;
            NameLine.Text = name;
            About.Text = Mods.PluginLine(mod);
            string said;
            IBrush tone = MainframeTheme.Cyan;
            if (mod.Changed) { said = "Changed. Connect asks you to approve it again."; tone = MainframeTheme.Rose; }
            else if (!mod.On) { said = mail ? "Off. Connect, then add your email." : Mods.TokenServer(mod) != null ? "Off. Connect, then add your personal token." : online ? "Off. Connect, then sign in on its page." : "Off."; tone = MainframeTheme.Faint; }
            else if (mail) { said = MailTools.Ready ? "Connected as " + MailTools.Account.Address : "On. Set up your email next."; if (!MailTools.Ready) tone = MainframeTheme.Rose; }
            else if (state == "Sign in") { said = Mods.TokenServer(mod) != null ? "Needs a personal access token." : "Needs you to sign in."; tone = MainframeTheme.Rose; }
            else if (state == "Connected") said = "Signed in";
            else if (trouble != null && trouble != "starting") { said = "On. Its tools didn't start."; tone = MainframeTheme.Rose; }
            else said = "On";
            State.Text = said;
            State.Ink = tone;
            Main.Text = !mod.On ? (mail || online ? "Connect" : "Turn on") : mail ? (MailTools.Ready ? "Email settings" : "Set up email") : Mods.TokenServer(mod) != null ? (state == "Connected" ? "Token settings" : "Set up token") : state == "Sign in" ? "Sign in" : "Turn off";
            bool action = Main.Text != "Turn off";
            Main.Accent = action ? MainframeTheme.Cyan : MainframeTheme.Rose;
            Main.Loud = action && (!mod.On || Main.Text != "Email settings");
            AutomationProperties.SetName(Main, Main.Text == "Connect" ? "Connect to " + name : Main.Text == "Turn on" || Main.Text == "Turn off" ? Main.Text + " " + name : Main.Text + " for " + name);
            TurnOff.IsVisible = mod.On && action;
            AutomationProperties.SetName(TurnOff, "Turn off " + name);
            Remove.IsVisible = !mod.Starter;
            AutomationProperties.SetName(Remove, "Remove " + name);
            ToolsNote.Text = tools.Count == 1 ? "One tool. It asks first the way SECURITY says."
                : tools.Count > 1 ? tools.Count + " tools. They ask first the way SECURITY says."
                : !mod.On ? "Turn it on to see its tools."
                : mod.Info == null || mod.Info.Servers.Length == 0 ? "No tools. " + (mod.Skills.Length == 1 ? "It brings a skill she reads when it fits." : "It brings skills she reads when they fit.")
                : trouble == "starting" ? "Her tools from it are starting."
                : trouble == McpSignInNeeded.Said ? (Mods.TokenServer(mod) != null ? "Add your token to see its tools." : "Its tools show once you're signed in.")
                : trouble != null ? "Its tools didn't start: " + trouble
                : "Her tools from it show here once it has started.";
            ToolsNote.Ink = tools.Count == 0 && mod.On && trouble != null && trouble != "starting" && trouble != McpSignInNeeded.Said ? MainframeTheme.Rose : MainframeTheme.Faint;
            foreach (Control row in toolRows) Page.Children.Remove(row);
            toolRows.Clear();
            for (int i = 0; i < tools.Count; i++)
            {
                int y = ToolsTop + i * Row;
                toolRows.Add(Label(tools[i].Title ?? tools[i].Tool, 24, y, 572, 20, MainframeTheme.White, MainframeTheme.Size, true));
                NeonCaption about = Label(tools[i].Description ?? "", 24, y + 20, 572, 28, MainframeTheme.Faint, 34.0 / 3);
                about.Top = true;
                toolRows.Add(about);
            }
            int wanted = Math.Max(400, ToolsTop + Math.Max(1, tools.Count) * Row + 24);
            if (wanted != tall) { tall = wanted; Fit(Wide, tall, zoom); }
            foreach (Control control in Page.Children) control.InvalidateVisual();
        }

        internal string Crowded() { return ModsWindow.Crowded(Page, Wide, tall); }
    }

    // A plug-in on her Mods page, like Windows': its logo or picture on a plate cut at every corner, its name, and one word for
    // where it's at. Set up Email's picks are these too, each in its own colour.
    internal sealed class PlugTile : NeonButton
    {
        internal Mod Mod;
        internal int Picture;
        internal ServiceLogo Logo;
        internal string State = "";
        internal IBrush StateInk;
        internal string Tint;
        internal bool Lit, Still;
        internal double PlateTall = 60, StateSize = 34.0 / 3;

        internal Rect NameArea { get { return new Rect(0, PlateTall, Width, Math.Max(1, Math.Min(Height - PlateTall, 18))); } }
        internal Rect StateArea { get { Rect name = NameArea; return new Rect(0, name.Bottom, Width, Math.Max(1, Height - name.Bottom)); } }
        private double CutSize { get { return Math.Min(10, (PlateTall - 8) / 4); } }
        private string Shown { get { return Mod != null && !Mod.Starter ? MainframeTheme.Fitted(Text, Width - 4, TextSize, true) : Text; } }

        public override void Render(DrawingContext context)
        {
            Rect plate = new Rect(4, 4, Math.Max(0, Bounds.Width - 8), Math.Max(0, PlateTall - 8));
            double cut = CutSize;
            bool live = !Still && IsEnabled && (IsPointerOver || IsKeyboardFocusWithin);
            Color cyan = Of(MainframeTheme.Cyan);
            PlugArt.Plate(context, plate, cut, Lit || live ? cyan : Of(MainframeTheme.DeepEdge), Lit, live ? (IsPressed ? 44 : 22) : 0);
            Color ink, accent, fill;
            if (Tint != null) { ink = accent = MainframeTheme.Tint(Tint); fill = PlugArt.Toward(PlugArt.Sheet, ink, 1 / 6.0); }
            else if (Lit) { ink = cyan; accent = Of(MainframeTheme.Rose); fill = Of(MainframeTheme.Deep); }
            else { ink = accent = Of(MainframeTheme.Faint); fill = Of(MainframeTheme.Field); }
            Rect art = plate.Deflate(Math.Max(8, plate.Height * 0.16));
            if (Logo == null) PlugArt.Draw(context, Picture, art, ink, accent, fill);
            else PlugArt.Logo(context, Logo, art, MainframeTheme.Tint(Logo.Ink));
            bool spot = MainframeTheme.Spotted(this);
            if (spot || MainframeTheme.Marked(this)) context.DrawGeometry(null, new Pen(spot ? MainframeTheme.Cyan : MainframeTheme.White, 2), PlugArt.Around(plate, cut, 2));
            if (Text.Length > 0) MainframeTheme.Write(context, Shown, NameArea, Lit || Tint != null ? MainframeTheme.White : MainframeTheme.Faint, TextSize, true, TextAlignment.Center);
            if (State.Length > 0) MainframeTheme.Write(context, State, StateArea, StateInk ?? MainframeTheme.Faint, StateSize, false, TextAlignment.Center);
        }

        private static Color Of(IBrush brush) { return ((ISolidColorBrush)brush).Color; }

        internal override string TextFits()
        {
            if (MainframeTheme.Words(Shown, TextSize, true).Width > Width - 4 + 0.5) return Text;
            return State.Length > 0 && MainframeTheme.Words(State, StateSize).Width > Width - 4 + 0.5 ? State : null;
        }
    }

    // Her plug-in pictures, like Windows' (app/PluginArt.cs), on a plate cut at every corner like her speech box: a service's own
    // logo where it has one, and otherwise her drawings, an envelope for email, a page, a checklist, or a plug for the rest.
    internal static class PlugArt
    {
        internal static Color Sheet { get { return MainframeTheme.Tint("#090B0F"); } }

        internal static Color Toward(Color from, Color to, double amount)
        {
            return Color.FromRgb((byte)Math.Round(from.R + (to.R - from.R) * amount), (byte)Math.Round(from.G + (to.G - from.G) * amount), (byte)Math.Round(from.B + (to.B - from.B) * amount));
        }

        internal static void Plate(DrawingContext context, Rect box, double cut, Color edge, bool glow, int wash)
        {
            if (box.Width <= 0 || box.Height <= 0) return;
            Geometry path = Look.Plate(box, cut, -1, box.Center.Y, 0, true);
            if (glow) context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(46, edge.R, edge.G, edge.B)), 7), path);
            Color sheet = Sheet;
            context.DrawGeometry(new SolidColorBrush(Color.FromArgb(238, sheet.R, sheet.G, sheet.B)), null, path);
            using (context.PushGeometryClip(path))
            {
                Pen scan = new Pen(new SolidColorBrush(Color.FromArgb(9, 255, 255, 255)), 1);
                for (double row = box.Top; row < box.Bottom; row += 3) context.DrawLine(scan, new Point(box.Left, row), new Point(box.Right, row));
                if (wash > 0) context.DrawGeometry(new SolidColorBrush(Color.FromArgb((byte)wash, edge.R, edge.G, edge.B)), null, path);
            }
            context.DrawGeometry(null, new Pen(new SolidColorBrush(edge), 2), Look.Plate(box.Deflate(1), cut - 0.586, -1, box.Center.Y, 0, true));
        }

        // The same cut, a little outside the plate, for the keyboard's mark.
        internal static Geometry Around(Rect box, double cut, double by)
        {
            Rect wider = box.Inflate(by);
            return Look.Plate(wider, cut + by * 0.586, -1, wider.Center.Y, 0, true);
        }

        internal static void Draw(DrawingContext context, int picture, Rect area, Color ink, Color accent, Color fill)
        {
            double k = Math.Min(area.Width / 48, area.Height / 38);
            if (k <= 0) return;
            using (context.PushTransform(Matrix.CreateScale(k, k) * Matrix.CreateTranslation(area.Center.X, area.Center.Y)))
            {
                IBrush body = new SolidColorBrush(fill), spot = new SolidColorBrush(accent);
                Pen pen = new Pen(new SolidColorBrush(ink), 2);
                Pen bright = new Pen(new SolidColorBrush(accent), 2.2, null, PenLineCap.Round, PenLineJoin.Miter);
                if (picture == Mods.Envelope) Envelope(context, ink, pen, bright, body);
                else if (picture == Mods.Page) Page(context, ink, pen, bright, body);
                else if (picture == Mods.Checklist) Checklist(context, ink, pen, bright, body);
                else Plug(context, pen, body, spot);
            }
        }

        // A service's own logo, filled in its colour and centred a little inside the area so it sits like her drawings do.
        internal static void Logo(DrawingContext context, ServiceLogo logo, Rect area, Color ink)
        {
            double size = Math.Min(area.Width, area.Height) * 0.86;
            if (size <= 0) return;
            using (context.PushTransform(Matrix.CreateScale(size / 24, size / 24) * Matrix.CreateTranslation(area.X + (area.Width - size) / 2, area.Y + (area.Height - size) / 2)))
                context.DrawGeometry(new SolidColorBrush(ink), null, Outline(logo));
        }

        internal static Geometry Outline(ServiceLogo logo)
        {
            StreamGeometry shape = new StreamGeometry();
            using (StreamGeometryContext c = shape.Open())
            {
                c.SetFillRule(FillRule.NonZero);
                foreach (ServiceLogo.Figure figure in logo.Figures)
                {
                    c.BeginFigure(new Point(figure.X, figure.Y), true);
                    foreach (double[] step in figure.Steps)
                    {
                        if (step.Length == 2) c.LineTo(new Point(step[0], step[1]));
                        else c.CubicBezierTo(new Point(step[0], step[1]), new Point(step[2], step[3]), new Point(step[4], step[5]));
                    }
                    c.EndFigure(figure.Closed);
                }
            }
            return shape;
        }

        private static Geometry Lines(bool closed, params Point[] points)
        {
            StreamGeometry geometry = new StreamGeometry();
            using (StreamGeometryContext c = geometry.Open())
            {
                c.BeginFigure(points[0], closed);
                for (int i = 1; i < points.Length; i++) c.LineTo(points[i]);
                c.EndFigure(closed);
            }
            return geometry;
        }

        private static void Envelope(DrawingContext context, Color ink, Pen pen, Pen bright, IBrush body)
        {
            context.DrawGeometry(body, pen, Look.Plate(new Rect(-21, -14, 42, 28), 3, -1, 0, 0, true));
            context.DrawGeometry(null, bright, Lines(false, new Point(-17, -10), new Point(0, 3), new Point(17, -10)));
            Pen fold = new Pen(new SolidColorBrush(Color.FromArgb(120, ink.R, ink.G, ink.B)), 1.4);
            context.DrawLine(fold, new Point(-17, 10), new Point(-6, -1));
            context.DrawLine(fold, new Point(17, 10), new Point(6, -1));
        }

        private static void Page(DrawingContext context, Color ink, Pen pen, Pen bright, IBrush body)
        {
            context.DrawGeometry(body, pen, Lines(true, new Point(-14, -17), new Point(6, -17), new Point(14, -9), new Point(14, 17), new Point(-14, 17)));
            context.DrawGeometry(null, pen, Lines(false, new Point(6, -17), new Point(6, -9), new Point(14, -9)));
            context.DrawLine(bright, new Point(-9, -10), new Point(1, -10));
            Pen words = new Pen(new SolidColorBrush(ink), 1.6, null, PenLineCap.Round);
            context.DrawLine(words, new Point(-9, -2), new Point(9, -2));
            context.DrawLine(words, new Point(-9, 4), new Point(9, 4));
            context.DrawLine(words, new Point(-9, 10), new Point(3, 10));
        }

        private static void Checklist(DrawingContext context, Color ink, Pen pen, Pen bright, IBrush body)
        {
            context.DrawGeometry(body, pen, Look.Plate(new Rect(-17, -17, 34, 34), 4, -1, 0, 0, true));
            Pen thin = new Pen(new SolidColorBrush(ink), 1.5);
            Pen words = new Pen(new SolidColorBrush(ink), 1.6, null, PenLineCap.Round);
            for (int row = 0; row < 3; row++)
            {
                double y = -10 + row * 9;
                context.DrawRectangle(null, thin, new Rect(-11, y - 3, 6, 6));
                context.DrawLine(words, new Point(-1, y), new Point(11, y));
                if (row < 2) context.DrawGeometry(null, bright, Lines(false, new Point(-10, y), new Point(-8, y + 2), new Point(-4, y - 4)));
            }
        }

        private static void Plug(DrawingContext context, Pen pen, IBrush body, IBrush spot)
        {
            context.FillRectangle(spot, new Rect(-7, -16, 3, 8));
            context.FillRectangle(spot, new Rect(4, -16, 3, 8));
            context.DrawGeometry(body, pen, Look.Plate(new Rect(-11, -8, 22, 15), 3, -1, 0, 0, true));
            context.DrawLine(pen, new Point(-4, 7), new Point(-4, 10));
            context.DrawLine(pen, new Point(4, 7), new Point(4, 10));
            context.DrawLine(pen, new Point(-6, 10), new Point(6, 10));
            StreamGeometry cord = new StreamGeometry();
            using (StreamGeometryContext c = cord.Open())
            {
                c.BeginFigure(new Point(0, 10), false);
                c.CubicBezierTo(new Point(0, 17), new Point(10, 13), new Point(16, 18));
                c.EndFigure(false);
            }
            context.DrawGeometry(null, pen, cord);
        }
    }
}

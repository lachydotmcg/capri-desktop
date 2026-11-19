using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Mainframe
    {
        private static readonly string[] GrantOrder = { Plugins.Usual, Plugins.Always, Plugins.Ask, Plugins.Off };
        private ModOptionsSheet pluginSheet;

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

        private static string PluginAbout(Mod mod)
        {
            List<PluginTool> tools = Plugins.ToolsOf(mod.File.Id);
            string trouble = Plugins.Trouble(mod.File.Id);
            string what = mod.Info == null || mod.Info.Servers.Length == 0 ? "" : !mod.On ? "" : tools.Count > 0 ? tools.Count + (tools.Count == 1 ? " tool. " : " tools. ") : trouble == "starting" ? "Starting its tools. " : trouble == McpSignInNeeded.Said ? "Needs you to sign in. " : trouble != null ? "Its tools didn't start. " : "";
            if (mod.Skills.Length > 0) what += mod.Skills.Length + (mod.Skills.Length == 1 ? " skill. " : " skills. ");
            return "PLUG-IN. " + what;
        }

        private void OpenPluginOptions(Mod mod)
        {
            if (pluginSheet != null && !pluginSheet.IsDisposed) pluginSheet.Close();
            Control made = PluginPanel(mod);
            pluginSheet = new ModOptionsSheet(this, SheetZoom(620, Math.Min(760, made.Height + 84)), mod.File.Id, mod.File.Name + " tools", made, 620, Math.Min(760, made.Height + 84));
            pluginSheet.Show(this);
            pluginSheet.Activate();
        }

        private static string PluginOn(Mod mod)
        {
            if (!mod.On) return mod.File.Name + " is in, and off.";
            string skills = mod.Skills.Length == 0 ? "" : " She can read its " + (mod.Skills.Length == 1 ? "skill" : mod.Skills.Length + " skills") + " when they fit.";
            return mod.Info != null && mod.Info.Servers.Length > 0 ? mod.File.Name + " is on. Its tools are starting." + skills : mod.File.Name + " is on." + skills;
        }

        internal string PluginSheetTitle { get { return pluginSheet == null || pluginSheet.IsDisposed ? null : pluginSheet.Text; } }

        private Control PluginPanel(Mod mod)
        {
            Panel panel = new Panel { BackColor = Ground, ForeColor = White, AutoScroll = true, Width = 572 };
            List<PluginTool> tools = Plugins.ToolsOf(mod.File.Id);
            string trouble = Plugins.Trouble(mod.File.Id);
            string top = tools.Count > 0
                ? "Like SECURITY asks first the way SECURITY > Use tools your code mods added says, and for tools that change things, Send, post and delete too."
                : trouble == "starting" ? "Her tools from it are starting. Open this again in a moment."
                : trouble == McpSignInNeeded.Said ? "It needs you to sign in before her tools from it show. Capri opens your browser for that, and only when you press Sign in."
                : trouble != null ? "Its tools didn't start: " + trouble
                : "Her tools from it show here once it has started.";
            Label head = new Label { Text = top, Font = Type(9f), ForeColor = trouble != null && trouble != "starting" && trouble != McpSignInNeeded.Said && tools.Count == 0 ? Rose : Faint, AutoSize = false, Bounds = new Rectangle(0, 0, 552, 40), UseMnemonic = false };
            panel.Controls.Add(head);
            int y = 46;
            if (trouble == McpSignInNeeded.Said)
            {
                Key sign = new Key { Bounds = new Rectangle(0, 46, 200, 26), Font = Type(9f), Text = "Sign in", Accent = Cyan };
                sign.AccessibleName = "Sign in to " + mod.File.Name;
                sign.Click += delegate
                {
                    sign.Text = "Signing in...";
                    sign.Enabled = false;
                    Plugins.SignIn(mod.File.Id, delegate(string problem)
                    {
                        try
                        {
                            BeginInvoke(new Action(delegate
                            {
                                if (problem == null) { OpenPluginOptions(mod); return; }
                                head.Text = "Couldn't sign in: " + problem;
                                head.ForeColor = Rose;
                                sign.Text = "Sign in";
                                sign.Enabled = true;
                            }));
                        }
                        catch (InvalidOperationException) { }
                    });
                };
                panel.Controls.Add(sign);
                y = 82;
            }
            foreach (PluginTool tool in tools)
            {
                PluginTool one = tool;
                Label name = new Label { Text = tool.Title, Font = Type(10f, true), ForeColor = White, AutoSize = false, AutoEllipsis = true, Bounds = new Rectangle(0, y, 400, 20), UseMnemonic = false };
                Label about = new Label { Text = tool.Description, Font = Type(8.5f), ForeColor = Faint, AutoSize = false, AutoEllipsis = true, Bounds = new Rectangle(0, y + 20, 400, 30), UseMnemonic = false };
                Key grant = new Key { Bounds = new Rectangle(412, y + 6, 140, 26), Font = Type(9f), Text = GrantWords(Plugins.GrantOf(mod.File.Id, tool.Tool)) };
                grant.AccessibleName = "When she uses " + tool.Title;
                grant.Accent = Plugins.GrantOf(mod.File.Id, tool.Tool) == Plugins.Off ? Rose : Cyan;
                grant.Click += delegate
                {
                    string now = Plugins.GrantOf(mod.File.Id, one.Tool);
                    string next = GrantOrder[(Array.IndexOf(GrantOrder, now) + 1) % GrantOrder.Length];
                    Plugins.Grant(mod.File.Id, one.Tool, next);
                    grant.Text = GrantWords(next);
                    grant.Accent = next == Plugins.Off ? Rose : Cyan;
                    grant.Invalidate();
                };
                panel.Controls.Add(name);
                panel.Controls.Add(about);
                panel.Controls.Add(grant);
                y += 56;
            }
            panel.Height = Math.Min(676, y + 4);
            return panel;
        }

        internal void CheckPluginPanel(List<string> checks, Mod mod)
        {
            if (PluginAbout(mod) != "PLUG-IN. 3 tools. 1 skill. " || PluginOn(mod) != mod.File.Name + " is on. Its tools are starting. She can read its skill when they fit.") throw new Exception("Her Mods row didn't say what a plug-in has: " + PluginAbout(mod));
            using (Control panel = PluginPanel(mod))
            {
                List<Key> keys = new List<Key>();
                foreach (Control control in panel.Controls) { Key key = control as Key; if (key != null) keys.Add(key); }
                if (keys.Count != 3 || !keys.TrueForAll(key => key.Text == "Like SECURITY")) throw new Exception("A plug-in's tools didn't each get a Like SECURITY key.");
                InvokeOnClick(keys[0], EventArgs.Empty);
                if (keys[0].Text != "Always run" || Plugins.GrantOf(mod.File.Id, "echo") != Plugins.Always) throw new Exception("A plug-in tool's key didn't set it to always run.");
                InvokeOnClick(keys[0], EventArgs.Empty);
                InvokeOnClick(keys[0], EventArgs.Empty);
                if (keys[0].Text != "Off" || Plugins.GrantOf(mod.File.Id, "echo") != Plugins.Off) throw new Exception("A plug-in tool's key didn't go round to off.");
                InvokeOnClick(keys[0], EventArgs.Empty);
                if (keys[0].Text != "Like SECURITY" || Plugins.GrantOf(mod.File.Id, "echo") != Plugins.Usual) throw new Exception("A plug-in tool's key didn't come back round to its usual.");
            }
            checks.Add("PASS plug-ins: her Mods row says what a plug-in has, and its Options sets each tool to always run, ask first, off or like SECURITY");
        }
    }
}

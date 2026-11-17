using System;
using System.Drawing;
using System.IO;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Preferences
    {
        [DataMember] public bool HerComputer;
    }

    internal sealed partial class Companion
    {
        private HerComputerWindow herComputer;

        internal bool HerComputerOn { get { return preferences.HerComputer; } }

        private string HerComputerFolder { get { return Path.Combine(Path.GetDirectoryName(settingsPath), "her-computer"); } }

        internal void ToggleHerComputer()
        {
            preferences.HerComputer = !preferences.HerComputer;
            if (!preferences.HerComputer) CloseHerComputer();
            SavePreferences();
            mainframe.Sync();
        }

        internal void OpenHerComputer()
        {
            try { ShowHerComputer(); }
            catch (Exception error) { Notice.Tell(mainframe, error.Message, "Her computer", true); }
        }

        private HerComputerWindow ShowHerComputer()
        {
            if (herComputer == null || herComputer.IsDisposed)
            {
                herComputer = new HerComputerWindow(new HerComputer(HerComputerFolder));
                herComputer.Ask = delegate(string system, string user) { return brain.Aside(system, user, 700, true); };
                herComputer.CanThink = delegate { return brain.Model != null && !Brain.Off; };
                herComputer.Tell = delegate(string line) { Post(delegate { SayOwn("her computer", line); }); };
                if (mainframe.Icon != null) herComputer.Icon = mainframe.Icon;
            }
            if (!herComputer.Visible) herComputer.Show();
            if (herComputer.WindowState == FormWindowState.Minimized) herComputer.WindowState = FormWindowState.Normal;
            herComputer.Activate();
            return herComputer;
        }

        private void CloseHerComputer()
        {
            if (herComputer != null && !herComputer.IsDisposed) herComputer.Close();
            herComputer = null;
        }

        internal static string HerComputerJob(string said)
        {
            string text = (said ?? "").Trim();
            Match on = Regex.Match(text, @"\s*,?\s*\b(?:on|using|use|with|from|in) (?:your|ur) (?:own |little )?(?:computer|pc|laptop)\b[.!?]*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!on.Success) return null;
            string job = (text.Substring(0, on.Index) + " " + text.Substring(on.Index + on.Length)).Trim().TrimStart(',', ':', ';', '-').Trim();
            job = Regex.Replace(job, @"^(?:hey\s+)?capri[,!.]?\s*", "", RegexOptions.IgnoreCase).Trim();
            job = Regex.Replace(job, @"^(?:(?:can|could|would|will) you\s+|please\s+|go\s+|and\s+)+", "", RegexOptions.IgnoreCase).Trim().TrimEnd('?', '.', '!', ',').Trim();
            if (Regex.IsMatch(job, @"^(?:(?:what(?:'s| is| are)?|how(?:'s| is)?)(?: you| it)?(?: doing| going| up to)?(?: there)?|anything|something|stuff|things)$", RegexOptions.IgnoreCase)) return "";
            return job;
        }

        internal static bool HerComputerOpenAsked(string said)
        {
            return Regex.IsMatch((said ?? "").Trim(), @"\A(?:(?:hey\s+)?capri[,!.]?\s*)?(?:(?:can|could|would) you\s+)?(?:open|show(?: me)?|let me see|bring up)\s+(?:your|ur)\s+(?:own\s+|little\s+)?(?:computer|pc)(?: please)?[.!?]*\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private string HerComputerAsk(string input)
        {
            if (!preferences.HerComputer) return null;
            if (HerComputerOpenAsked(input)) { OpenHerComputer(); return Lines.Pick(new[] { "Here's my computer. Don't touch anything. Kidding.", "Welcome to my little computer.", "Ta-da. My computer." }); }
            if (Regex.IsMatch(input ?? "", @"\bstop\b.{0,30}\b(?:your|ur) (?:own |little )?(?:computer|pc)\b", RegexOptions.IgnoreCase) && herComputer != null && herComputer.Busy) { herComputer.StopTask(); return "Ok, I'll stop what I'm doing on my computer."; }
            string job = HerComputerJob(input);
            if (job == null) return null;
            HerComputerWindow window = ShowHerComputer();
            if (job.Length == 0) return window.Busy ? "I'm busy on my computer: " + window.Doing + "." : "Just pottering about on my computer. Want me to do something on it?";
            string problem = window.Start(job);
            return problem ?? Lines.Pick(new[] { "On it. Watch me do it on my computer.", "Sure, I'll do that on my computer. I'll tell you when it's done.", "Leave it with me. Doing it on my computer now." });
        }

        internal string HerComputerShown { get { return (preferences.HerComputer ? "on" : "off") + " | " + (herComputer == null || herComputer.IsDisposed ? "closed" : herComputer.Visible ? "open" : "hidden"); } }
    }

    internal sealed partial class Mainframe
    {
        private Toggle herComputerSwitch;
        private Key herComputerOpen;

        private void BuildHerComputer(int y)
        {
            Section("HER COMPUTER >", 0, y);
            herComputerSwitch = Switch("Her own computer", 0, y + 24, delegate { if (!syncing) capri.ToggleHerComputer(); });
            herComputerSwitch.AccessibleName = "Her own computer. Off by default. She gets a little computer of her own in a separate window, with a real browser on her own profile, notes and a diary. Watch her use it or give her jobs to do on it";
            herComputerOpen = Make("Open her computer", 28, y + 56, 328, 22, delegate { capri.OpenHerComputer(); });
            herComputerOpen.AccessibleName = "Open her computer's window";
            Label hint = Caption("Say \"find me a recipe on your computer\" and\nshe does it there. Beta, Windows only.", 28, y + 86, Look.Mono(8.5f, FontStyle.Regular, GraphicsUnit.Point), Faint);
            hint.AutoSize = false;
            hint.Bounds = new Rectangle(28, y + 86, 328, 36);
        }

        private void SyncHerComputer()
        {
            herComputerSwitch.Checked = capri.HerComputerOn;
            herComputerOpen.Enabled = capri.HerComputerOn;
        }

        internal string HerComputerShown { get { return (herComputerSwitch.Checked ? "on" : "off") + " | " + (herComputerOpen.Enabled ? "can open" : "can't open"); } }
    }
}

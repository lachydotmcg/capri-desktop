using System;
using System.Collections.Generic;
using Avalonia.Threading;

namespace CapriDesktop
{
    internal sealed partial class Pet
    {
        private CompanionBridge companion;
        private readonly HashSet<string> companionGuards = new HashSet<string>();
        private bool companionGuard;

        private static readonly string[] ReactNames = { "glitch", "flinch", "dance", "sleep", "wake", "perch", "come" };
        private static readonly string[] GuardNames = { "fullscreen", "recording", "call" };
        private static readonly string[] AmbientNames = { "idle", "returned", "game_started", "game_ended", "late_night", "battery_low" };

        private void StartCompanion()
        {
            if (!OperatingSystem.IsLinux() || !Program.Companion || companion != null) return;
            try
            {
                CompanionBridge opened = new CompanionBridge(folder);
                opened.Received = HandleCompanion;
                opened.Start();
                companion = opened;
                AppDomain.CurrentDomain.ProcessExit += delegate { StopCompanion(); };
                desktop.Exit += delegate { StopCompanion(); };
                Closed += delegate { StopCompanion(); };
            }
            catch (Exception error)
            {
                companion = null;
                Brain.Log("companion", -1, "Her Omarchy bridge didn't start: " + error.Message);
            }
        }

        private void StopCompanion()
        {
            CompanionBridge closing = companion;
            companion = null;
            if (closing != null) closing.Dispose();
        }

        private Dictionary<string, object> HandleCompanion(Dictionary<string, object> request)
        {
            string kind = HuskJson.Text(request, "kind");
            if (kind == "state") return CompanionState();
            if (kind == "say")
            {
                string text = HuskJson.Text(request, "text").Trim();
                string source = HuskJson.Text(request, "source");
                if (text.Length == 0 || text.Length > 280) return HuskJson.Map("ok", false, "error", "bad_text");
                text = text.Replace("\r", " ").Replace("\n", " ");
                Dispatcher.UIThread.Post(delegate { SayCompanion(text, source); });
                return HuskJson.Map("ok", true);
            }
            if (kind == "react")
            {
                string name = HuskJson.Text(request, "name");
                if (Array.IndexOf(ReactNames, name) < 0) return HuskJson.Map("ok", false, "error", "unknown_react");
                Dispatcher.UIThread.Post(delegate { ReactCompanion(name); });
                return HuskJson.Map("ok", true);
            }
            if (kind == "quiet")
            {
                object value = HuskJson.Get(request, "on");
                if (!(value is bool)) return HuskJson.Map("ok", false, "error", "bad_quiet");
                bool on = (bool)value;
                Dispatcher.UIThread.Post(delegate { if (shushed != on) ToggleShush(false); });
                return HuskJson.Map("ok", true);
            }
            if (kind == "guard")
            {
                string name = HuskJson.Text(request, "name");
                object value = HuskJson.Get(request, "on");
                if (Array.IndexOf(GuardNames, name) < 0 || !(value is bool)) return HuskJson.Map("ok", false, "error", "bad_guard");
                bool on = (bool)value;
                Dispatcher.UIThread.Post(delegate { SetCompanionGuard(name, on); });
                return HuskJson.Map("ok", true);
            }
            if (kind == "ambient")
            {
                string eventName = HuskJson.Text(request, "event");
                string category = HuskJson.Text(request, "category");
                if (Array.IndexOf(AmbientNames, eventName) < 0) return HuskJson.Map("ok", false, "error", "unknown_event");
                Dispatcher.UIThread.Post(delegate { AmbientCompanion(eventName, category); });
                return HuskJson.Map("ok", true);
            }
            if (kind == "show")
            {
                string target = HuskJson.Text(request, "target");
                if (target != "mainframe" && target != "pet") return HuskJson.Map("ok", false, "error", "bad_target");
                Dispatcher.UIThread.Post(delegate { ShowCompanion(target); });
                return HuskJson.Map("ok", true);
            }
            return HuskJson.Map("ok", false, "error", "unknown_kind");
        }

        private Dictionary<string, object> CompanionState()
        {
            if (!Dispatcher.UIThread.CheckAccess())
                return Dispatcher.UIThread.InvokeAsync(delegate { return CompanionState(); }).GetAwaiter().GetResult();
            List<object> guards = new List<object>();
            foreach (string guard in companionGuards) guards.Add(guard);
            List<object> capabilities = new List<object>();
            foreach (string capability in new[] { "state", "say", "react", "quiet", "guard", "ambient", "show" }) capabilities.Add(capability);
            return HuskJson.Map(
                "ok", true,
                "version", 1,
                "quiet", Hushed,
                "userQuiet", shushed,
                "asleep", Asleep,
                "visible", IsVisible,
                "mainframe", mainframe != null && mainframe.IsVisible,
                "guards", guards,
                "capabilities", capabilities);
        }

        private void SetCompanionGuard(string name, bool on)
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(delegate { SetCompanionGuard(name, on); }); return; }
            bool changed = on ? companionGuards.Add(name) : companionGuards.Remove(name);
            bool next = companionGuards.Count > 0;
            if (next && !companionGuard) voice.Stop();
            companionGuard = next;
            if (changed) Sync();
        }

        private void SayCompanion(string text, string source)
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(delegate { SayCompanion(text, source); }); return; }
            if (source == "ambient" && Hushed) return;
            Talk.Answer("(omarchy)", text);
        }

        private void ReactCompanion(string name)
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(delegate { ReactCompanion(name); }); return; }
            if (name == "glitch") Play(glitch);
            else if (name == "flinch") Play(glitch);
            else if (name == "dance") Play(seat != null ? perchDance : dance);
            else if (name == "sleep") Nap(true);
            else if (name == "wake") Nap(false);
            else if (name == "perch") { if (seat != null) Play(perch); }
            else if (name == "come") { Show(); Activate(); }
        }

        private void AmbientCompanion(string eventName, string category)
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(delegate { AmbientCompanion(eventName, category); }); return; }
            if (Hushed) return;
            if (eventName == "idle") return;
            if (eventName == "returned") { Talk.Answer("(omarchy)", "There you are."); return; }
            if (eventName == "game_started") { Talk.Answer("(omarchy)", "Ooh, a game."); return; }
            if (eventName == "game_ended") { Talk.Answer("(omarchy)", "Back to reality."); return; }
            if (eventName == "late_night")
            {
                string line = careLines.Pick(CareKind.Late);
                if (line != null) Talk.Answer("(omarchy)", line);
                return;
            }
            if (eventName == "battery_low") Talk.Answer("(omarchy)", "Your battery's low, choom.");
        }

        private void ShowCompanion(string target)
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(delegate { ShowCompanion(target); }); return; }
            if (target == "mainframe") OpenMainframe();
            else { Show(); Activate(); }
        }
    }
}

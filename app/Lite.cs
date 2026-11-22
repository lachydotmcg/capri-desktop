using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CapriDesktop
{
    internal sealed class SlowSigns
    {
        internal string Kind = "none", Name, Processor, Busiest;
        internal bool Lite, Read, Running, RamOnly, Limited, CardFailed, HdAwake;
        internal int BrainId, Threads;
        internal long Model = -1, InVram = -1, CardUsed = -1, CardTotal = -1, RamFree = -1, RamTotal = -1;
        internal double Busy = -1, BusiestCpu = -1;
        internal readonly List<string> Watchers = new List<string>();

        internal bool OnPc { get { return Kind == "built-in" || Kind == "ollama"; } }
    }

    [DataContract]
    internal sealed class OllamaPlaces
    {
        [DataMember(Name = "models")] public OllamaPlace[] Models;
    }

    [DataContract]
    internal sealed class OllamaPlace
    {
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "model")] public string Model;
        [DataMember(Name = "size")] public long Size;
        [DataMember(Name = "size_vram")] public long Vram;
    }

    internal sealed partial class Companion
    {
        private const int LiteIdleFps = 10;
        private const long LiteIdleMs = 60000;
        private const double LiteOfferWait = 120;
        private const long LowFree = 1536L << 20;
        private const long SmallCard = 1L << 30;
        private const int SmallCpu = 4;
        private const double BusyCpu = 85;
        private const double BusiestCpu = 10;
        internal const string LiteOffer = "Lite mode's on, so my brain on your PC is off and its memory is yours again. A free key lets me think in the cloud instead. Want me to open the free key page?";
        internal const string LiteLater = "Okay. Free key is under BRAIN in Capri Mainframe whenever you want it, or turn Lite mode off to wake my brain on your PC.";
        internal const string SlowUnread = "I couldn't check your PC just now. Try again in a moment.";
        private const string FreeKeyFix = "Press Free key under BRAIN in Capri Mainframe for a free cloud brain.";
        private const string FreeKeyOr = "press Free key under BRAIN for a free cloud brain.";
        internal static Func<long> FakedIdle;
        private static readonly string[] LiteWatchers = { "watch_my_screen", "game_comments", "clip_my_games", "save_big_moments" };
        private static readonly Regex WhySlow = new Regex(@"^(?:(?:hey\s+)?capri\W+)?(?:why\s+(?:are|r)\s+(?:you|u)\s+(?:so\s+|being\s+so\s+|this\s+|really\s+)?(?:slow|laggy|lagging|sluggish)|why\s+(?:is|does)\s+(?:capri|your\s+brain)\s+(?:so\s+)?(?:slow|laggy|lagging|lag)|why\s+(?:are\s+you|do\s+you)\s+(?:taking|take)\s+so\s+long(?:\s+to\s+(?:reply|answer|respond|think))?|why\s+are\s+your\s+(?:replies|answers)\s+(?:so\s+)?slow|what(?:'s|\s+is)\s+(?:making|slowing)\s+(?:you|u)\s+(?:so\s+)?(?:slow|down|laggy)|why\s+so\s+slow)(?:\s+(?:today|now|right\s+now|lately|tonight|on\s+my\s+(?:pc|computer|laptop)))?\W*$", RegexOptions.IgnoreCase);
        private double liteOffer = -1;
        private bool liteIdle, slowChecking;
        private volatile bool liteUnloading;

        internal bool LiteOn { get { return preferences.Lite; } }

        private int RestFps { get { return preferences.Lite && liteIdle ? LiteIdleFps : SaverFps; } }

        private bool BrainOnPc { get { return !preferences.NoAi && brain.Remote == null && brain.Server == null && brain.Model != null; } }

        internal string SetLite(bool on, bool say)
        {
            if (InvokeRequired) return (string)Invoke(new Func<bool, bool, string>(SetLite), on, say);
            bool onPc = BrainOnPc;
            List<string> changed = new List<string>();
            if (preferences.Lite != on)
            {
                if (on)
                {
                    List<string> kept = new List<string>();
                    foreach (string name in LiteWatchers) if (SettingOn(name) == true) kept.Add(name);
                    preferences.LiteKept = null;
                    preferences.Lite = Brain.Lite = true;
                    foreach (string name in LiteWatchers) SetSetting(name, false);
                    preferences.LiteKept = kept.ToArray();
                    foreach (string name in kept) if (name != "save_big_moments") changed.Add(WatcherName(name));
                    RestForLite();
                }
                else
                {
                    string[] kept = preferences.LiteKept ?? new string[0];
                    preferences.Lite = Brain.Lite = false;
                    preferences.LiteKept = null;
                    foreach (string name in kept)
                    {
                        if (Array.IndexOf(LiteWatchers, name) < 0 || SettingOn(name) != false) continue;
                        SetSetting(name, true);
                        if (name != "save_big_moments" && SettingOn(name) == true) changed.Add(WatcherName(name));
                    }
                }
                liteIdle = false;
                liteOffer = -1;
                SavePreferences();
            }
            ShowLiteBrain(onPc);
            mainframe.Sync();
            if (!say) return LiteToolNote(on, onPc, changed);
            Tell("lite mode", LiteSaid(on, onPc, changed));
            if (on && onPc) liteOffer = clock.Elapsed.TotalSeconds;
            return null;
        }

        private static string WatcherName(string name)
        {
            return name == "watch_my_screen" ? "Watch my screen" : name == "game_comments" ? "game comments" : name == "clip_my_games" ? "Clip my games" : "Save big moments";
        }

        private static string Joined(List<string> items)
        {
            if (items.Count < 2) return items.Count == 0 ? "" : items[0];
            return string.Join(", ", items.GetRange(0, items.Count - 1).ToArray()) + " and " + items[items.Count - 1];
        }

        internal static string LiteSaid(bool on, bool onPc, List<string> changed)
        {
            if (on && onPc) return LiteOffer;
            string them = changed.Count == 0 ? "" : Joined(changed) + (changed.Count == 1 ? " is" : " are");
            if (on) return "Lite mode's on. I'll draw lighter and use my free voice" + (them.Length == 0 ? "." : ", and " + them + " off until Lite mode goes off.");
            return "Lite mode's off" + (them.Length == 0 ? "." : ", and " + them + " back on.") + (onPc ? " My brain on your PC wakes up next time you talk to me." : "");
        }

        internal static string LiteToolNote(bool on, bool onPc, List<string> changed)
        {
            if (!on) return changed.Count == 0 ? null : "Back on as before: " + Joined(changed) + ".";
            string brainNote = onPc ? "Your brain on their PC is off now, so you can't think until Lite mode goes off or they connect a free key under BRAIN in Capri Mainframe." : "";
            if (changed.Count == 0) return brainNote;
            return brainNote + (brainNote.Length == 0 ? "" : " ") + "Off until Lite mode goes off: " + Joined(changed) + ".";
        }

        private void ShowLiteBrain(bool onPc)
        {
            if (!onPc) return;
            if (preferences.Lite) { mainframe.ShowProblem(Mainframe.LiteResting); return; }
            string status = mainframe.Status;
            if (status.EndsWith(" | " + Mainframe.LiteResting, StringComparison.Ordinal) || status.EndsWith(" | " + Brain.LiteNote, StringComparison.Ordinal)) mainframe.ShowProblem("Running on this PC");
        }

        private void RestForLite()
        {
            if (!restingBrain && engine.Running)
            {
                restingBrain = true;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try { if (engine.Rest(0)) Brain.Log("brain", -1, "llama-server stopped for Lite mode, giving its memory back"); }
                    finally { restingBrain = false; }
                });
            }
            if (liteUnloading || Brain.OllamaLoaded.Length == 0) return;
            liteUnloading = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { Brain.UnloadOllama(); }
                finally { liteUnloading = false; }
            });
        }

        private string LiteAnswer(string input)
        {
            if (liteOffer < 0 || string.IsNullOrWhiteSpace(input) || Security.FromMod(input) || Security.Holding != null) return null;
            bool fresh = clock.Elapsed.TotalSeconds - liteOffer <= LiteOfferWait;
            liteOffer = -1;
            if (!fresh) return null;
            if (Security.Agrees(input)) { OpenPage(FreeKeyPage); return FreeKeyLine; }
            return Security.Declines(input) ? LiteLater : null;
        }

        internal static bool AsksWhySlow(string input)
        {
            return !string.IsNullOrWhiteSpace(input) && WhySlow.IsMatch(input.Replace('\u2019', '\'').Trim());
        }

        internal void CheckWhySlow()
        {
            if (slowChecking) return;
            slowChecking = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string line;
                try { line = SlowLine(ReadSlowSigns()); }
                catch (Exception error) { Brain.Log("slow", -1, error.GetType().Name + ": " + error.Message); line = SlowUnread; }
                Post(delegate { slowChecking = false; Tell("why slow?", line); });
            });
        }

        private string SlowFound()
        {
            SlowSigns signs = ReadSlowSigns();
            List<string[]> reasons = SlowReasons(signs);
            string lite = signs.Lite ? " Lite mode is on." : " Lite mode is off.";
            if (reasons.Count == 0) return "From a check of their PC just now: " + SlowNone(signs) + lite;
            List<string> each = new List<string>();
            foreach (string[] reason in reasons) each.Add(reason[0] + " What helps: " + reason[1]);
            return "From a check of their PC just now: " + string.Join(" ", each.ToArray()) + lite;
        }

        internal SlowSigns ReadSlowSigns()
        {
            SlowSigns signs = FromWindow<SlowSigns>(SlowSignsHere) ?? new SlowSigns();
            signs.Threads = Environment.ProcessorCount;
            signs.Processor = ProcessorName();
            bool card = signs.OnPc;
            try
            {
                SystemUse use = new SystemUse();
                use.Sample(signs.BrainId, card);
                Thread.Sleep(600);
                UseView view = use.Sample(signs.BrainId, card);
                if (view.RamTotal > 0) { signs.RamTotal = view.RamTotal; signs.RamFree = Math.Max(0, view.RamTotal - view.RamUsed); }
                signs.Busy = view.Cpu;
                UseRow busiest = null;
                foreach (UseRow row in view.Others) if (busiest == null || row.Cpu > busiest.Cpu) busiest = row;
                if (busiest != null && busiest.Cpu >= BusiestCpu) { signs.Busiest = busiest.Name; signs.BusiestCpu = busiest.Cpu; }
                if (view.GpuKnown && view.VramUsed >= 0) signs.CardUsed = view.VramUsed;
                if (signs.Kind == "built-in" && signs.Running)
                {
                    UseRow herBrain = view.Capri.Find(row => row.Name == "Her brain");
                    if (herBrain != null && herBrain.Vram >= 0) signs.InVram = herBrain.Vram;
                }
            }
            catch (Exception error) { Brain.Log("slow", -1, error.GetType().Name + ": " + error.Message); }
            long total = Interlocked.Read(ref Brain.Vram);
            if (total <= 0) total = Machine.Read().Vram;
            if (total > 0) signs.CardTotal = total;
            if (signs.Kind == "ollama" && !Brain.Off) ReadOllamaPlace(signs);
            return signs;
        }

        private SlowSigns SlowSignsHere()
        {
            SlowSigns signs = new SlowSigns { Lite = preferences.Lite, RamOnly = Brain.GpuLimit < 0, Limited = Brain.GpuLimit > 0, HdAwake = qwen.Awake };
            Cloud remote = brain.Remote;
            Engine local = brain.Local;
            Pick pick = brain.LocalPick;
            if (preferences.NoAi) signs.Kind = "off";
            else if (remote != null) signs.Kind = remote.Kind == "local" ? "server" : "cloud";
            else if (brain.Server != null) signs.Kind = "server";
            else if (local != null && pick != null)
            {
                signs.Kind = "built-in";
                signs.Name = pick.Name;
                signs.Read = true;
                signs.Running = local.Running;
                signs.BrainId = local.ServerId;
                signs.CardFailed = local.CpuOnly;
                signs.Model = pick.Size;
            }
            else if (brain.Model != null) { signs.Kind = "ollama"; signs.Name = brain.Model; }
            foreach (string name in LiteWatchers)
                if (name != "save_big_moments" && SettingOn(name) == true) signs.Watchers.Add(WatcherName(name));
            return signs;
        }

        private static string ProcessorName()
        {
            try
            {
                using (RegistryKey cpu = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                {
                    string name = cpu == null ? null : cpu.GetValue("ProcessorNameString") as string;
                    if (string.IsNullOrWhiteSpace(name)) return null;
                    name = Regex.Replace(name, @"\((?:R|TM)\)", "", RegexOptions.IgnoreCase);
                    name = Regex.Replace(name, @"\s+(?:CPU\s+)?@.*$|\s+\d+-Core\s+Processor\s*$|\s+(?:Processor|CPU)\s*$", "", RegexOptions.IgnoreCase);
                    name = Regex.Replace(name, @"\s+", " ").Trim();
                    return name.Length == 0 ? null : name;
                }
            }
            catch (System.Security.SecurityException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (IOException) { return null; }
        }

        private static void ReadOllamaPlace(SlowSigns signs)
        {
            OllamaPlaces places = OllamaPs();
            if (places == null) return;
            signs.Read = true;
            if (places.Models == null) return;
            foreach (OllamaPlace place in places.Models)
            {
                if (place == null || !SameTag(place.Name ?? place.Model, signs.Name)) continue;
                signs.Running = true;
                signs.Model = place.Size;
                signs.InVram = place.Vram;
                return;
            }
        }

        private static bool SameTag(string a, string b)
        {
            if (a == null || b == null) return false;
            return string.Equals(Regex.Replace(a.Trim(), ":latest$", ""), Regex.Replace(b.Trim(), ":latest$", ""), StringComparison.OrdinalIgnoreCase);
        }

        private static OllamaPlaces OllamaPs()
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Brain.Ollama + "/api/ps");
                request.Proxy = null;
                request.Timeout = 2000;
                request.ReadWriteTimeout = 2000;
                request.UserAgent = "Capri-Desktop";
                using (WebResponse response = request.GetResponse())
                using (Stream body = response.GetResponseStream())
                    return (OllamaPlaces)new DataContractJsonSerializer(typeof(OllamaPlaces)).ReadObject(body);
            }
            catch (WebException) { return null; }
            catch (IOException) { return null; }
            catch (SerializationException) { return null; }
            catch (UriFormatException) { return null; }
        }

        internal static List<string[]> SlowReasons(SlowSigns s)
        {
            List<string[]> found = new List<string[]>();
            bool pc = s.OnPc && !s.Lite;
            if (s.OnPc && s.Lite) found.Add(new[] { "Lite mode is on, so my brain on your PC is switched off.", "Press Free key under BRAIN in Capri Mainframe for a free cloud brain, or turn Lite mode off." });
            if (pc)
            {
                bool spilling = false;
                if (s.Kind == "built-in" && s.RamOnly) found.Add(new[] { "Brain VRAM is set to RAM only, so my brain runs on your processor.", "Set Brain VRAM to Auto under Resources in Capri Mainframe." });
                else if (s.Kind == "built-in" && s.CardFailed) found.Add(new[] { "My brain runs on your processor since your graphics card couldn't load it before.", FreeKeyFix + " I'll try the card again after my next update." });
                else if (s.Kind == "built-in" && s.CardTotal > 0 && s.CardTotal < SmallCard) found.Add(new[] { "Your graphics card has only " + UseView.Bytes(s.CardTotal) + " of its own memory, so my brain runs on your processor.", FreeKeyFix });
                else if (s.Running && s.Model > 0 && s.InVram >= 0 && s.InVram * 100 < s.Model * 85)
                {
                    spilling = true;
                    int share = (int)Math.Min(84, Math.Round(100.0 * s.InVram / s.Model));
                    found.Add(new[]
                    {
                        share < 5 ? "Almost none of my brain fits in your graphics card's memory, so it runs slowly on your processor." : "Only about " + share + "% of my brain fits in your graphics card's memory, so the rest runs slowly on your processor.",
                        s.RamOnly ? "Set Brain VRAM to Auto under Resources in Capri Mainframe." : s.Limited ? "Raise Brain VRAM under Resources in Capri Mainframe, or pick a smaller brain." : "Close games or apps using your graphics card, or pick a smaller brain under BRAIN."
                    });
                }
                if (!spilling && s.CardTotal > 0 && s.CardUsed >= 0 && s.CardUsed * 100 >= s.CardTotal * 92)
                    found.Add(new[] { "Your graphics card's memory is nearly full, " + UseView.Of(s.CardUsed, s.CardTotal) + " used.", "Close a game or app you're not using, or " + FreeKeyOr });
                if (s.Read && !s.Running)
                    found.Add(new[] { s.Kind == "ollama" ? "Ollama let go of my brain after a while unused, so my next reply waits while it loads again." : "My brain on your PC rests after 5 minutes unused, so my next reply waits while it loads again.", "Only that first reply is slow. " + FreeKeyFix });
            }
            if (s.Threads > 0 && s.Threads <= SmallCpu)
                found.Add(new[] { "Your processor" + (s.Processor == null ? "" : ", " + s.Processor + ",") + " has only " + s.Threads + (s.Threads == 1 ? " thread" : " threads") + ", which is small for " + (pc ? "running a brain." : "everything I do."), pc ? FreeKeyFix + " Lite mode helps too." : s.Lite ? "Close apps you're not using to give me room." : "Turn on Lite mode so I draw lighter." });
            if (s.RamTotal > 0 && s.RamFree >= 0 && (s.RamFree < LowFree || s.RamFree * 10 < s.RamTotal))
                found.Add(new[] { "Only " + UseView.Bytes(s.RamFree) + " of your " + UseView.Bytes(s.RamTotal) + " of RAM is free.", "Close apps or browser tabs you're not using" + (s.Lite ? "." : ", or turn on Lite mode.") });
            if (s.Busy >= BusyCpu)
                found.Add(new[] { "Your processor is " + (int)Math.Round(s.Busy) + "% busy" + (s.Busiest == null ? "." : ", mostly with " + s.Busiest + "."), s.Busiest == null ? "Close what you're not using to give me room." : "Close " + s.Busiest + " if you're done with it." });
            if (s.Watchers.Count >= 2)
                found.Add(new[] { Joined(s.Watchers) + " are on, and they keep working in the background.", s.Lite ? "Turn off the ones you don't need in Settings." : "Turn on Lite mode to pause them, or turn off the ones you don't need." });
            if (s.HdAwake && !s.Lite)
                found.Add(new[] { "HD voice is loaded too, and it shares your PC's memory and graphics card with me.", "Turn on Lite mode so my free voice takes over." });
            return found;
        }

        internal static string SlowNone(SlowSigns s)
        {
            if (s.Kind == "off") return "Nothing on your PC is slowing me down right now. No AI is on, so I'm not thinking at all.";
            if (s.Kind == "cloud") return "Nothing on your PC is slowing me down right now. My brain is in the cloud, so slow replies come from the service being busy. Picking another model under BRAIN can help.";
            if (s.Kind == "server") return "Nothing in Capri is slowing me down right now. Your own server does my thinking, so its speed is up to that server.";
            if (s.Kind == "none") return "Nothing on your PC is slowing me down right now. I don't have a brain yet: press Set up her brain or Free key under BRAIN in Capri Mainframe.";
            return "I checked your PC and nothing is slowing me down right now.";
        }

        internal static string SlowLine(SlowSigns s)
        {
            List<string[]> reasons = SlowReasons(s);
            if (reasons.Count == 0) return SlowNone(s);
            string said = reasons[0][0] + " " + reasons[0][1];
            if (reasons.Count > 1) said += " " + reasons[1][0] + " " + reasons[1][1];
            return said;
        }
    }

    internal sealed partial class Mainframe
    {
        internal const string LiteResting = "Off while Lite mode is on";

        internal bool LiteShown { get { return liteMode.Checked; } }

        internal void FlipLite() { liteMode.Checked = !liteMode.Checked; }

        internal string LiteRow { get { return (Folded(liteMode) ? "folded" : "shown") + " | " + (Folded(whySlow) ? "folded" : "shown"); } }

        internal string LiteFits()
        {
            using (Bitmap paper = new Bitmap(1, 1))
            using (Graphics graphics = Graphics.FromImage(paper))
            {
                List<KeyValuePair<string, Rectangle>> text = new List<KeyValuePair<string, Rectangle>>();
                return CheckText(graphics, liteMode, Point.Empty, text, true) ?? CheckText(graphics, whySlow, Point.Empty, text, true);
            }
        }

        internal string LiteLook(string path)
        {
            ScrollPage((int)Math.Round((liteMode.Top + Scaled(pageScroll)) / Unit()) - 260);
            Application.DoEvents();
            Rectangle view = ClientRectangle;
            string problem = view.Contains(liteMode.Bounds) && view.Contains(whySlow.Bounds) ? null : "the Lite mode row wasn't in view: " + liteMode.Bounds + " and " + whySlow.Bounds + " in " + view;
            using (Bitmap shot = new Bitmap(Width, Height))
            {
                DrawToBitmap(shot, new Rectangle(Point.Empty, Size));
                DrawRims(shot);
                shot.Save(path, ImageFormat.Png);
            }
            ScrollPage(0);
            Application.DoEvents();
            return problem;
        }
    }
}

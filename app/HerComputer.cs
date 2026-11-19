using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CapriDesktop
{
    [DataContract]
    internal sealed class HerSticker
    {
        [DataMember] public string Kind = "";
        [DataMember] public int X;
        [DataMember] public int Y;
        [DataMember] public int Tilt;
    }

    [DataContract]
    internal sealed class HerDesk
    {
        [DataMember] public List<HerSticker> Stickers = new List<HerSticker>();
        [DataMember] public int Wallpaper;
    }

    internal sealed class HerStep
    {
        internal string Verb = "", Arg = "", Extra = "";
    }

    internal interface HerHands
    {
        void Status(string text);
        void Show(string app);
        void Point(string spot);
        void PointPage(Rectangle box);
        void Click();
        void Typing(string field, string text);
        void Bubble(string line);
        void Changed();
    }

    internal sealed class HerComputer
    {
        internal static readonly string[] Apps = { "browser", "jobs", "notes", "files", "stickers" };
        internal static readonly string[] AppNames = { "Browser", "Jobs", "Notes", "Files", "Stickers" };
        internal static readonly string[] StickerKinds = { "heart", "star", "moon", "mushroom", "sparkles", "ribbon", "blossom", "strawberry" };
        internal static readonly string[] Sites = { "youtube", "spotify", "discord", "roblox", "pinterest", "twitch", "netflix", "reddit", "tiktok", "instagram", "wikipedia", "google" };
        internal static readonly string[] SiteNames = { "YouTube", "Spotify", "Discord", "Roblox", "Pinterest", "Twitch", "Netflix", "Reddit", "TikTok", "Instagram", "Wikipedia", "Google" };
        internal static readonly string[] SiteAddresses = { "https://www.youtube.com/", "https://open.spotify.com/", "https://discord.com/", "https://www.roblox.com/", "https://www.pinterest.com/", "https://www.twitch.tv/", "https://www.netflix.com/", "https://www.reddit.com/", "https://www.tiktok.com/", "https://www.instagram.com/", "https://www.wikipedia.org/", "https://www.google.com/" };
        internal const int Width = 1280, Height = 820;
        internal static readonly Rectangle Screen = new Rectangle(16, 14, 1248, 748);
        internal static readonly Rectangle Page = new Rectangle(96, 154, 1088, 512);
        internal const int MaxSteps = 14, MostStickers = 18, PageChars = 2600, MostLinks = 30;

        internal const string StepSystem = "You are Capri, using your own little computer that lives inside the user's PC. It has a real web browser on your own profile, plus Notes. You do jobs for the user on it one action at a time, then hand back the answer. Rules: answer with exactly one action line and nothing else. Never type passwords or payment details, and never sign in, buy, post, message or send anything. If the job needs no web, like a poem, a list or an idea, finish straight away with DONE. Keep DONE short, warm and in your own voice, with the key facts and a link when it helps. Actions:\nSEARCH <words>\nOPEN <web address>\nCLICK <link number or its words>\nSCROLL down|up\nBACK\nNOTE <title> | <text>\nDONE <your answer for them>";

        internal readonly string Folder;
        internal readonly HerJobs Jobs;
        private readonly object gate = new object();
        private readonly Random random = new Random();
        private HerDesk desk;

        internal HerComputer(string folder)
        {
            Folder = folder;
            try { Directory.CreateDirectory(NotesFolder); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            desk = LoadDesk();
            Jobs = new HerJobs(folder);
        }

        internal string NotesFolder { get { return Path.Combine(Folder, "notes"); } }
        internal string BrowserFolder { get { return Path.Combine(Folder, "browser"); } }
        private string DeskPath { get { return Path.Combine(Folder, "desktop.json"); } }

        internal HerDesk Desk { get { lock (gate) return desk; } }

        internal List<HerSticker> Stickers()
        {
            lock (gate) return new List<HerSticker>(desk.Stickers);
        }

        internal int Wallpaper { get { lock (gate) return desk.Wallpaper; } }

        internal void AddSticker(string kind, Point at, int tilt)
        {
            lock (gate)
            {
                desk.Stickers.Add(new HerSticker { Kind = kind, X = at.X, Y = at.Y, Tilt = tilt });
                while (desk.Stickers.Count > MostStickers) desk.Stickers.RemoveAt(0);
            }
            SaveDesk();
        }

        private HerDesk LoadDesk()
        {
            HerDesk loaded = null;
            try
            {
                if (File.Exists(DeskPath))
                    using (FileStream stream = File.OpenRead(DeskPath)) loaded = new DataContractJsonSerializer(typeof(HerDesk)).ReadObject(stream) as HerDesk;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SerializationException) { }
            catch (InvalidCastException) { }
            return Fixed(loaded);
        }

        internal static HerDesk Fixed(HerDesk loaded)
        {
            HerDesk fixedDesk = loaded ?? new HerDesk();
            if (fixedDesk.Stickers == null) fixedDesk.Stickers = new List<HerSticker>();
            fixedDesk.Stickers.RemoveAll(sticker => sticker == null || Array.IndexOf(StickerKinds, sticker.Kind) < 0);
            while (fixedDesk.Stickers.Count > MostStickers) fixedDesk.Stickers.RemoveAt(0);
            fixedDesk.Wallpaper = Math.Max(0, Math.Min(2, fixedDesk.Wallpaper));
            return fixedDesk;
        }

        internal void SaveDesk()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string temp = DeskPath + ".tmp";
                lock (gate)
                {
                    using (FileStream stream = File.Create(temp)) new DataContractJsonSerializer(typeof(HerDesk)).WriteObject(stream, desk);
                }
                if (File.Exists(DeskPath)) File.Delete(DeskPath);
                File.Move(temp, DeskPath);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        internal static string FileName(string title)
        {
            string name = Regex.Replace(title ?? "", "[^A-Za-z0-9 _-]+", " ");
            name = Regex.Replace(name, @"\s+", " ").Trim();
            if (name.Length > 48) name = name.Substring(0, 48).Trim();
            return name.Length == 0 ? "note" : name;
        }

        internal string WriteNote(string title, string text)
        {
            try
            {
                Directory.CreateDirectory(NotesFolder);
                string stem = FileName(title), path = Path.Combine(NotesFolder, stem + ".md");
                for (int n = 2; File.Exists(path) && n < 500; n++) path = Path.Combine(NotesFolder, stem + " " + n + ".md");
                File.WriteAllText(path, "# " + (string.IsNullOrWhiteSpace(title) ? "Note" : title.Trim()) + "\n\n" + (text ?? "").Trim() + "\n", new UTF8Encoding(false));
                return path;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        private DateTime mirroredAt = DateTime.MinValue;

        internal string MemoriesFolder { get { return Path.Combine(NotesFolder, "Memories"); } }

        internal int RefreshMemories()
        {
            lock (gate)
            {
                if ((DateTime.UtcNow - mirroredAt).TotalSeconds < 15) return -1;
                mirroredAt = DateTime.UtcNow;
            }
            string data = Path.GetDirectoryName(Path.GetFullPath(Folder));
            return string.IsNullOrEmpty(data) ? 0 : HerVault.Mirror(data, NotesFolder);
        }

        private static List<FileInfo> Newest(string folder)
        {
            List<FileInfo> files = Directory.Exists(folder) ? new List<FileInfo>(new DirectoryInfo(folder).GetFiles("*.md")) : new List<FileInfo>();
            files.Sort((a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
            return files;
        }

        internal List<string> Notes()
        {
            List<string> found = new List<string>();
            try
            {
                foreach (string name in new[] { "About you.md", "From your chats.md" })
                    if (File.Exists(Path.Combine(MemoriesFolder, name))) found.Add(Path.Combine(MemoriesFolder, name));
                foreach (FileInfo file in Newest(Path.Combine(MemoriesFolder, "Games"))) found.Add(file.FullName);
                foreach (FileInfo file in Newest(NotesFolder)) found.Add(file.FullName);
                foreach (FileInfo file in Newest(Path.Combine(MemoriesFolder, "Chats"))) found.Add(file.FullName);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return found;
        }

        internal static string Read(string path, int limit)
        {
            try
            {
                if (path == null || !File.Exists(path)) return "";
                string text = File.ReadAllText(path);
                return text.Length > limit ? text.Substring(0, limit) : text;
            }
            catch (IOException) { return ""; }
            catch (UnauthorizedAccessException) { return ""; }
        }

        internal List<string> Files()
        {
            return new List<string>(Notes());
        }

        internal static HerStep Parse(string reply)
        {
            if (string.IsNullOrWhiteSpace(reply)) return null;
            string[] lines = reply.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = Regex.Replace(lines[i], @"^[\s>*`#\-]*(?:action\s*:\s*)?", "", RegexOptions.IgnoreCase).Trim().Trim('`').Trim();
                Match match = Regex.Match(line, @"^(SEARCH|OPEN|CLICK|SCROLL|BACK|NOTE|DONE)\b\s*:?\s*(.*)$", RegexOptions.IgnoreCase);
                if (!match.Success) continue;
                HerStep step = new HerStep { Verb = match.Groups[1].Value.ToUpperInvariant(), Arg = match.Groups[2].Value.Trim() };
                if (step.Verb == "DONE" || step.Verb == "NOTE")
                {
                    StringBuilder rest = new StringBuilder(step.Arg);
                    for (int j = i + 1; j < lines.Length; j++) rest.Append('\n').Append(lines[j]);
                    step.Arg = rest.ToString().Trim();
                }
                if (step.Verb == "NOTE")
                {
                    int bar = step.Arg.IndexOf('|');
                    if (bar >= 0) { step.Extra = step.Arg.Substring(bar + 1).Trim(); step.Arg = step.Arg.Substring(0, bar).Trim(); }
                    else { step.Extra = step.Arg; step.Arg = "Note"; }
                    if (step.Extra.Length == 0) return null;
                }
                if (step.Verb == "SCROLL") step.Arg = Regex.IsMatch(step.Arg, @"^\s*up", RegexOptions.IgnoreCase) ? "up" : "down";
                if (step.Arg.Length >= 2 && step.Arg[0] == '"' && step.Arg[step.Arg.Length - 1] == '"' && step.Verb != "DONE") step.Arg = step.Arg.Substring(1, step.Arg.Length - 2);
                if ((step.Verb == "SEARCH" || step.Verb == "OPEN" || step.Verb == "CLICK" || step.Verb == "DONE") && step.Arg.Length == 0) return null;
                return step;
            }
            return null;
        }

        internal static string Short(string text, int limit)
        {
            string flat = Regex.Replace(text ?? "", @"\s+", " ").Trim();
            return flat.Length > limit ? flat.Substring(0, limit - 1).TrimEnd() + "\u2026" : flat;
        }

        internal static string StepPrompt(string task, List<string> log, int step, string title, string address, string text, List<HerLink> links, int most = MaxSteps)
        {
            StringBuilder prompt = new StringBuilder();
            prompt.Append("The job from the user: ").Append(task.Trim()).Append("\nStep ").Append(step).Append(" of ").Append(most).Append(".\n");
            if (log.Count > 0) { prompt.Append("What you did so far:\n"); foreach (string line in log) prompt.Append(line).Append('\n'); }
            if (address == null) prompt.Append("Your browser isn't open yet.\n");
            else
            {
                prompt.Append("Your browser shows: ").Append(Short(title, 120)).Append(" (").Append(Short(address, 200)).Append(")\n");
                prompt.Append("Page text:\n").Append(Short(text, PageChars)).Append('\n');
                if (links != null && links.Count > 0)
                {
                    prompt.Append("Links:\n");
                    for (int i = 0; i < links.Count; i++) prompt.Append(i + 1).Append(". ").Append(Short(links[i].Text, 80)).Append('\n');
                }
            }
            if (step >= most - 1) prompt.Append("You're nearly out of steps, so finish with DONE now using what you found.\n");
            prompt.Append("Reply with one action line.");
            return prompt.ToString();
        }

        internal string Run(string task, HerHands hands, Func<string, string, string> ask, Func<HerBrowser> browser, Func<bool> stop, out string notePath, int most = MaxSteps, bool keep = true, List<string> sites = null)
        {
            notePath = null;
            List<string> log = new List<string>();
            List<string> visited = new List<string>();
            List<HerLink> links = new List<HerLink>();
            HerBrowser web = null;
            string answer = null;
            int misses = 0;
            hands.Status("Thinking about: " + Short(task, 60));
            for (int step = 1; step <= most && answer == null; step++)
            {
                if (stop()) return null;
                string title = null, address = null, text = null;
                if (web != null && web.Running)
                {
                    title = web.Title;
                    address = web.Address;
                    text = web.Text(PageChars);
                    links = web.Links(MostLinks) ?? new List<HerLink>();
                    links.RemoveAll(link => link == null || string.IsNullOrWhiteSpace(link.Text));
                }
                string reply = ask(StepSystem, StepPrompt(task, log, step, title ?? "", address, text ?? "", links, most));
                if (stop()) return null;
                HerStep next = Parse(reply);
                if (next == null)
                {
                    log.Add(step + ". (that reply wasn't an action)");
                    if (++misses >= 3) { answer = Short(reply, 900); break; }
                    continue;
                }
                switch (next.Verb)
                {
                    case "SEARCH":
                    case "OPEN":
                        if (web == null || !web.Running) web = browser();
                        if (web == null) { log.Add(step + ". " + next.Verb + " " + next.Arg + ": my browser won't start"); break; }
                        hands.Status((next.Verb == "SEARCH" ? "Searching for " : "Opening ") + Short(next.Arg, 60));
                        hands.Show("browser");
                        hands.Point("address");
                        hands.Click();
                        hands.Typing("address", next.Arg);
                        web.Go(next.Verb == "OPEN" ? next.Arg : HerBrowser.SearchAddress + Uri.EscapeDataString(next.Arg));
                        hands.Typing("address", null);
                        if (web.Address != null && !visited.Contains(web.Address)) visited.Add(web.Address);
                        log.Add(step + ". " + next.Verb + " " + Short(next.Arg, 80) + " -> " + Short(web.Title, 80));
                        break;
                    case "CLICK":
                        if (web == null || !web.Running) { log.Add(step + ". CLICK: nothing is open"); break; }
                        string wanted = next.Arg;
                        int number;
                        HerLink picked = null;
                        if (int.TryParse(Regex.Match(next.Arg, @"^\d+").Value, out number) && number >= 1 && number <= links.Count) { picked = links[number - 1]; wanted = picked.Text; }
                        Rectangle box;
                        hands.Status("Clicking " + Short(wanted, 50));
                        if (web.Find(wanted, out box) || picked != null && !(box = picked.Box).IsEmpty)
                        {
                            hands.PointPage(box);
                            hands.Click();
                            web.Click(box.Left + box.Width / 2, box.Top + box.Height / 2);
                            Thread.Sleep(400);
                            if (web.Address != null && !visited.Contains(web.Address)) visited.Add(web.Address);
                            log.Add(step + ". CLICK " + Short(wanted, 60) + " -> " + Short(web.Title, 80));
                        }
                        else log.Add(step + ". CLICK " + Short(wanted, 60) + ": couldn't find it");
                        break;
                    case "SCROLL":
                        if (web == null || !web.Running) { log.Add(step + ". SCROLL: nothing is open"); break; }
                        hands.Status(next.Arg == "up" ? "Scrolling back up" : "Reading the page");
                        hands.Point("page");
                        web.Scroll(next.Arg == "up" ? -560 : 560);
                        Thread.Sleep(250);
                        log.Add(step + ". SCROLL " + next.Arg);
                        break;
                    case "BACK":
                        if (web == null || !web.Running) { log.Add(step + ". BACK: nothing is open"); break; }
                        hands.Point("back");
                        hands.Click();
                        web.Back();
                        log.Add(step + ". BACK -> " + Short(web.Title, 80));
                        break;
                    case "NOTE":
                        hands.Status("Writing a note");
                        hands.Show("notes");
                        hands.Typing("note", next.Extra);
                        string written = WriteNote(next.Arg, next.Extra);
                        hands.Typing("note", null);
                        hands.Changed();
                        if (written != null) notePath = written;
                        log.Add(step + ". NOTE " + Short(next.Arg, 60) + (written == null ? ": couldn't save it" : ": saved"));
                        break;
                    case "DONE":
                        answer = next.Arg;
                        break;
                }
            }
            if (stop()) return null;
            if (answer == null) answer = log.Count == 0 ? "I couldn't get that one done on my computer." : "I ran out of steps on that one. The last thing I had open was " + (web != null && web.Running ? Short(web.Title, 80) : "nothing") + ".";
            if (sites != null) lock (sites) foreach (string site in visited) if (!sites.Contains(site)) sites.Add(site);
            if (!keep) return answer.Trim();
            StringBuilder note = new StringBuilder(answer.Trim());
            if (visited.Count > 0)
            {
                note.Append("\n\nSites I used:\n");
                foreach (string site in visited) note.Append("- ").Append(site).Append('\n');
            }
            hands.Status("Saving the answer");
            hands.Show("notes");
            string saved = WriteNote(Short(task, 48), note.ToString());
            hands.Changed();
            if (saved != null) notePath = saved;
            return answer.Trim();
        }

        internal string Pick(string[] lines) { lock (gate) return lines[random.Next(lines.Length)]; }

        internal static string Check()
        {
            HerStep step = Parse("SEARCH pancake recipe");
            if (step == null || step.Verb != "SEARCH" || step.Arg != "pancake recipe") return "Her computer misread a search.";
            step = Parse("Sure!\nAction: click 3");
            if (step == null || step.Verb != "CLICK" || step.Arg != "3") return "Her computer missed an action after a chatty line.";
            step = Parse("- DONE Here you go:\nmix flour and milk\nfry it");
            if (step == null || step.Verb != "DONE" || step.Arg != "Here you go:\nmix flour and milk\nfry it") return "Her computer cut a multi-line answer short.";
            step = Parse("NOTE Shopping | eggs, milk");
            if (step == null || step.Arg != "Shopping" || step.Extra != "eggs, milk") return "Her computer misread a note.";
            step = Parse("scroll UP please");
            if (step == null || step.Verb != "SCROLL" || step.Arg != "up") return "Her computer misread a scroll.";
            if (Parse("I think we should look it up") != null || Parse("SEARCH") != null || Parse("") != null) return "Her computer took a reply that wasn't an action.";
            if (FileName("what's a good pancake recipe?/\\:*") != "what s a good pancake recipe" || FileName("???") != "note") return "Her computer's note names aren't safe.";
            HerDesk mended = Fixed(new HerDesk { Stickers = new List<HerSticker> { null, new HerSticker { Kind = "nope" }, new HerSticker { Kind = "heart", X = 300, Y = 200 } }, Wallpaper = 9 });
            if (mended.Stickers.Count != 1 || mended.Wallpaper != 2) return "Her computer didn't mend a broken desktop file.";
            if (Sites.Length != SiteNames.Length || Sites.Length != SiteAddresses.Length) return "Her pinned sites lost a name or an address.";
            string prompt = StepPrompt("find a recipe", new List<string> { "1. SEARCH pancakes -> Bing" }, 2, "Pancakes", "https://example.com", "Mix and fry.", new List<HerLink> { new HerLink { Text = "Best pancakes" } });
            if (prompt.IndexOf("1. Best pancakes", StringComparison.Ordinal) < 0 || prompt.IndexOf("find a recipe", StringComparison.Ordinal) < 0 || prompt.IndexOf("Mix and fry.", StringComparison.Ordinal) < 0) return "Her computer's step prompt lost the page.";
            if (StepPrompt("x", new List<string>(), MaxSteps, "", null, "", null).IndexOf("finish with DONE", StringComparison.Ordinal) < 0) return "Her computer didn't tell her brain to finish on the last step.";
            if (HerBrowser.Resolve("javascript:alert(1)").StartsWith("javascript", StringComparison.OrdinalIgnoreCase) || HerBrowser.Resolve("file:///c:/x").StartsWith("file", StringComparison.OrdinalIgnoreCase)) return "Her browser would open a script or a file address.";
            return null;
        }

        internal static string CheckRun(string folder)
        {
            HerComputer computer = new HerComputer(folder);
            Queue<string> replies = new Queue<string>(new[] { "NOTE Poem | roses are pink", "DONE here's your poem: roses are pink" });
            string note;
            QuietHands hands = new QuietHands();
            string answer = computer.Run("write me a poem", hands, delegate(string system, string user) { return replies.Dequeue(); }, delegate { return null; }, delegate { return false; }, out note);
            if (answer != "here's your poem: roses are pink" || note == null || !File.Exists(note) || computer.Notes().Count != 2 || Read(note, 2000).IndexOf("roses are pink", StringComparison.Ordinal) < 0) return "Her computer didn't finish a job or save its note.";
            replies = new Queue<string>(new[] { "SEARCH cats", "DONE cats are great" });
            answer = computer.Run("find cats", hands, delegate(string system, string user) { return replies.Dequeue(); }, delegate { return null; }, delegate { return false; }, out note);
            if (answer != "cats are great" || hands.Shown.Contains("browser") && hands.Typed.Contains("cats")) return "Her computer typed into a browser that never started.";
            int asked = 0;
            answer = computer.Run("keep going", hands, delegate(string system, string user) { asked++; return "hmm"; }, delegate { return null; }, delegate { return false; }, out note);
            if (asked != 3 || answer != "hmm") return "Her computer kept asking after replies that weren't actions.";
            answer = computer.Run("stop me", hands, delegate(string system, string user) { return "SCROLL down"; }, delegate { return null; }, delegate { return true; }, out note);
            if (answer != null) return "Her computer kept working after it was stopped.";
            computer.AddSticker("star", new Point(500, 400), 10);
            HerComputer again = new HerComputer(folder);
            if (again.Stickers().Count != 1 || again.Stickers()[0].Kind != "star") return "Her desktop didn't come back the way she left it.";
            for (int i = 0; i < MostStickers + 5; i++) again.AddSticker("heart", new Point(400, 400), 0);
            if (new HerComputer(folder).Stickers().Count != MostStickers) return "Her stickers piled up past the limit.";
            return HerJobs.Check(Path.Combine(folder, "jobs-check"));
        }

        private sealed class QuietHands : HerHands
        {
            internal readonly List<string> Shown = new List<string>(), Typed = new List<string>();
            public void Status(string text) { }
            public void Show(string app) { if (app != null) Shown.Add(app); }
            public void Point(string spot) { }
            public void PointPage(Rectangle box) { }
            public void Click() { }
            public void Typing(string field, string text) { if (text != null) Typed.Add(text); }
            public void Bubble(string line) { }
            public void Changed() { }
        }
    }
}

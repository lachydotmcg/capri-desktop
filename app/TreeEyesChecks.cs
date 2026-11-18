using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace CapriDesktop
{
    internal sealed partial class Companion
    {
        private void SmokeTreeEyes(List<string> checks, string output)
        {
            Directory.CreateDirectory(output);
            Func<Sight> readerWas = TreeEyes.Reader;
            Func<IntPtr, string> pageWas = TreeEyes.PageReader;
            Func<IntPtr, string, string> webWas = TreeEyes.WebReader;
            string folderWas = PersonalSettings.Folder;
            try
            {
                PersonalSettings.Folder = Path.Combine(output, PersonalSettings.FolderName);
                string sys = PersonalSettings.Make();
                string stock = File.ReadAllText(sys);

                Sight chat = new Sight { Window = new IntPtr(41), Title = "general - Chatroom", Program = "chatroom", Bounds = new Rectangle(0, 0, 1280, 720) };
                chat.Targets.Add(new Target { Number = 1, Role = "button", Name = "Send", Box = new Rectangle(1100, 650, 80, 30) });
                chat.Targets.Add(new Target { Number = 2, Role = "box", Name = "Message #general", Value = "see you at 5", Line = true, Focused = true, Box = new Rectangle(100, 650, 900, 30) });
                chat.Targets.Add(new Target { Number = 3, Role = "box", Name = "Password", Secret = true, Line = true, Box = new Rectangle(100, 300, 300, 30) });
                chat.Targets.Add(new Target { Number = 4, Role = "link", Name = "Page 2", Box = new Rectangle(10, 10, 60, 20) });
                chat.Targets.Add(new Target { Number = 5, Role = "button", Name = "Send file", Box = new Rectangle(1000, 650, 80, 30) });
                chat.Targets.Add(new Target { Number = 6, Role = "tab", Name = "Members", Box = new Rectangle(1200, 10, 60, 20) });
                chat.Words.Add("Welcome to #general");

                TreeEyes.Reader = delegate { return chat; };
                TreeEyes.PageReader = delegate { return "Lunch is at noon on Friday."; };
                string read = TreeEyes.Front(3000);
                if (read == null || !read.StartsWith("Window: \"general - Chatroom\" (chatroom)", StringComparison.Ordinal) || read.IndexOf("\nFocused: box \"Message #general\"", StringComparison.Ordinal) < 0
                    || read.IndexOf("button \"Send\"", StringComparison.Ordinal) < 0 || read.IndexOf("Labels: Welcome to #general", StringComparison.Ordinal) < 0 || read.IndexOf("Page text: Lunch is at noon on Friday.", StringComparison.Ordinal) < 0)
                    throw new Exception("Her tree eyes read the window as " + read);
                if (read.IndexOf("box \"Password\" (password box)", StringComparison.Ordinal) < 0 || read.IndexOf("[1]", StringComparison.Ordinal) >= 0) throw new Exception("A password box or a job number reached her look: " + read);
                if (TreeEyes.Prompt(read).IndexOf(TreeEyes.Trust + "\n" + read, StringComparison.Ordinal) < 0 || TreeEyes.Prompt(null) != "" || TreeEyes.Seen("You see: a chat.", null) != "You see: a chat." || TreeEyes.Seen("You see: a chat.", read) != "You see: a chat.\nExact details from their window: " + read)
                    throw new Exception("Her look didn't carry the tree text to her brain and back.");
                checks.Add("PASS her tree eyes read the window in front as text: its name, what has focus, its controls, labels and page text, with password boxes kept empty");

                TreeEyes.PageReader = delegate { throw new InvalidOperationException("no page"); };
                if (TreeEyes.Front(3000) == null) throw new Exception("A page that couldn't be read hid the rest of the window.");
                TreeEyes.Reader = delegate { System.Threading.Thread.Sleep(4000); return chat; };
                DateTime started = DateTime.UtcNow;
                if (TreeEyes.Front(300) != null || (DateTime.UtcNow - started).TotalMilliseconds > 2500) throw new Exception("A slow window held up her look.");
                TreeEyes.Reader = delegate { throw new InvalidOperationException("gone"); };
                if (TreeEyes.Front(3000) != null) throw new Exception("A window that threw while being read still gave text.");
                TreeEyes.Reader = delegate { return new Sight(); };
                if (TreeEyes.Front(3000) != null) throw new Exception("No window in front still gave text.");
                checks.Add("PASS her tree eyes give up quietly on a slow, missing or broken window, so a screenshot look still goes ahead");

                File.WriteAllText(sys, stock.Replace(PersonalSettings.NeverWatchKey + ": ", PersonalSettings.NeverWatchKey + ": chatroom"));
                TreeEyes.Reader = delegate { return chat; };
                TreeEyes.PageReader = delegate { return "Lunch is at noon on Friday."; };
                if (TreeEyes.Front(3000) != null || TreeEyes.Read(chat, "x") != null || TreeEyes.ForJob(chat, null) != TreeEyes.Skipped) throw new Exception("A window on her never watch list was read.");
                File.WriteAllText(sys, stock);
                if (TreeEyes.Front(3000) == null) throw new Exception("Taking a window off her never watch list didn't let her read it again.");
                checks.Add("PASS her tree eyes and read_window never read a window on the never watch list in sys.md");

                if (chat.Find("Send") != chat.Targets[0] || chat.Find("\"send\"") != chat.Targets[0] || chat.Find("the Send button") != chat.Targets[0] || chat.Find("button Send") != chat.Targets[0]) throw new Exception("A control named by its exact name wasn't found.");
                if (chat.Find("members") != chat.Targets[5] || chat.Find("message") != chat.Targets[1]) throw new Exception("A control named by its only match wasn't found.");
                if (chat.Find("4") != chat.Targets[3] || chat.Find("[2]") != chat.Targets[1] || chat.Find("button 5") != chat.Targets[4] || chat.Find("Page 2") != chat.Targets[3]) throw new Exception("A numbered target moved: by number she has to hit what she always hit.");
                if (chat.Find("se") != null || chat.Find("nothing here") != null || chat.Find("") != null || chat.Find(null) != null) throw new Exception("A name that fits no control, or more than one, was clicked.");
                Sight twins = new Sight { Window = new IntPtr(42), Title = "Twins", Program = "twins" };
                twins.Targets.Add(new Target { Number = 1, Role = "button", Name = "OK", Box = new Rectangle(0, 0, 10, 10) });
                twins.Targets.Add(new Target { Number = 2, Role = "button", Name = "OK", Box = new Rectangle(20, 0, 10, 10) });
                if (twins.Find("OK") != null || twins.Find("ok button") != null) throw new Exception("One of two buttons with the same name was guessed.");
                checks.Add("PASS her hands click and type into a control by its exact name, numbers still mean what they did, and a name that fits two controls or none isn't guessed");

                if (!Computer.Toolset.Contains("{\"name\":\"" + TreeEyes.Tool + "\",") || Computer.Tools.Contains(TreeEyes.Tool) || Computer.Listing.Contains(TreeEyes.Tool) || Computer.Seeing.IndexOf(TreeEyes.Hint, StringComparison.Ordinal) < 0)
                    throw new Exception("read_window isn't one of Claude's job tools, or reached a numbered job.");
                Picture shown = new Picture { Size = new Size(640, 360), Area = new Rectangle(0, 0, 1280, 720) };
                string job = TreeEyes.ForJob(chat, shown);
                if (job.IndexOf("- button \"Send\" at (570, 332)", StringComparison.Ordinal) < 0 || job.IndexOf("- box \"Message #general\" = \"see you at 5\" (focused) at (275, 332)", StringComparison.Ordinal) < 0 && job.IndexOf("(value hidden)", StringComparison.Ordinal) < 0 || job.IndexOf("Text on screen: Welcome to #general", StringComparison.Ordinal) < 0)
                    throw new Exception("read_window didn't say where each control is in the screenshot: " + job);
                if (!Computer.Failed(TreeEyes.ForJob(new Sight(), shown)) || !Computer.Failed(TreeEyes.ForJob(new Sight { Window = new IntPtr(43), Title = "Blank", Program = "blank" }, shown))) throw new Exception("read_window on no window or an empty one didn't fail plainly.");
                Job reading = new Job("tell me what the send button says");
                MethodInfo step = typeof(Job).GetMethod("Step", BindingFlags.NonPublic | BindingFlags.Instance);
                string stepped = (string)step.Invoke(reading, new object[] { TreeEyes.Tool, new ToolArguments(), false, null });
                if (stepped.IndexOf("- button \"Send\"", StringComparison.Ordinal) < 0 || reading.Steps.Count != 1 || reading.Steps[0] != TreeEyes.Tool + ": Read the window.") throw new Exception("A Claude job's read_window step came back as " + stepped);
                checks.Add("PASS Claude's jobs can call read_window for exact names and where to click, and it fails plainly on a window with nothing to read");

                List<Target> outline = new List<Target>();
                outline.Add(new Target { Role = "heading", Name = "Cheap flights" });
                outline.Add(new Target { Role = "link", Name = "Sydney deals", Value = "https://example.com/sydney" });
                outline.Add(new Target { Role = "button", Name = "Search" });
                outline.Add(new Target { Role = "box", Name = "Destination", Value = "Sydney", Line = true });
                outline.Add(new Target { Role = "box", Name = "Password", Value = "hunter2", Line = true, Secret = true });
                outline.Add(new Target { Role = "checkbox", Name = "Direct only", Value = "on" });
                string pageBody = "Flights from Melbourne are cheap. Book early for the best price.\r\nBags cost extra on some fares.";
                string formatted = TreeEyes.PageText("https://example.com/flights", "Cheap flights - Example", outline, pageBody, null);
                if (!formatted.StartsWith("Page address: https://example.com/flights\nWindow title: \"Cheap flights - Example\"\nOutline:\n# Cheap flights\n[link] Sydney deals -> https://example.com/sydney\n[button] Search\n[box] Destination = \"Sydney\"\n", StringComparison.Ordinal)
                    || formatted.IndexOf("\n[checkbox] Direct only (on)\n", StringComparison.Ordinal) < 0 || !formatted.EndsWith("\nPage text:\nFlights from Melbourne are cheap. Book early for the best price.\nBags cost extra on some fares.", StringComparison.Ordinal))
                    throw new Exception("A web page wasn't laid out as its address, title, outline and text: " + formatted);
                if (formatted.IndexOf("[box] Password (password box)", StringComparison.Ordinal) < 0 || formatted.IndexOf("hunter2", StringComparison.Ordinal) >= 0) throw new Exception("A password box's value reached her page text: " + formatted);
                string longPage = TreeEyes.PageText(null, "Long", null, new string('x', 20000), null);
                if (longPage.Length > TreeEyes.PageTextMost + 300 || longPage.IndexOf("[Page text cut here.", StringComparison.Ordinal) < 0 || longPage.IndexOf("Page address", StringComparison.Ordinal) >= 0) throw new Exception("A long page's text wasn't cut with a note.");
                checks.Add("PASS read_page lays a web page out as its address, title, an outline of headings, links with where they go, buttons, boxes and checkboxes, then its text, with a password box's value never included and long text cut with a note");

                string bags = TreeEyes.PageText("https://example.com/flights", "Cheap flights - Example", outline, pageBody, "BAGS price");
                if (bags.IndexOf("Found 2 lines mentioning \"BAGS price\".", StringComparison.Ordinal) < 0 || bags.IndexOf("Book early for the best price.", StringComparison.Ordinal) < 0 || bags.IndexOf("Bags cost extra on some fares.", StringComparison.Ordinal) < 0
                    || bags.IndexOf("Flights from Melbourne are cheap.", StringComparison.Ordinal) >= 0 || bags.IndexOf("# Cheap flights", StringComparison.Ordinal) >= 0 || bags.IndexOf("Outline:", StringComparison.Ordinal) >= 0)
                    throw new Exception("read_page with find didn't keep only the lines that mention those words: " + bags);
                string sydney = TreeEyes.PageText(null, "Flights", outline, pageBody, "sydney");
                if (sydney.IndexOf("Found 2 lines mentioning \"sydney\".", StringComparison.Ordinal) < 0 || sydney.IndexOf("[link] Sydney deals -> https://example.com/sydney", StringComparison.Ordinal) < 0 || sydney.IndexOf("[button] Search", StringComparison.Ordinal) >= 0 || sydney.IndexOf("Page text:", StringComparison.Ordinal) >= 0)
                    throw new Exception("read_page with find didn't find a link by its words: " + sydney);
                if (TreeEyes.PageText(null, "Flights", outline, pageBody, "zebra").IndexOf("Found 0 lines mentioning \"zebra\"", StringComparison.Ordinal) < 0) throw new Exception("read_page with find didn't say nothing was found.");
                checks.Add("PASS read_page with find returns only the outline lines and sentences that mention those words, in any case, with a count");

                Sight site = new Sight { Window = new IntPtr(51), Title = "Flights - Chrome", Program = "chrome" };
                int pageReads = 0;
                string pageFound = "unset";
                TreeEyes.WebReader = delegate(IntPtr pageHandle, string pageFind) { pageReads++; pageFound = pageFind; return formatted; };
                string pageRead = TreeEyes.ForPage(site, "bags");
                if (pageRead != TreeEyes.PageTrust + "\n" + formatted || pageFound != "bags" || pageReads != 1) throw new Exception("A browser page wasn't read as data with its find: " + pageRead);
                TreeEyes.WebReader = delegate(IntPtr pageHandle, string pageFind) { pageReads++; return new string('y', 20000); };
                string cutRead = TreeEyes.ForPage(site, null);
                if (cutRead.Length > TreeEyes.PageMost + 200 || cutRead.IndexOf("[Cut here:", StringComparison.Ordinal) < 0) throw new Exception("A very long page wasn't cut to her limit with a note.");
                TreeEyes.WebReader = delegate(IntPtr pageHandle, string pageFind) { pageReads++; return formatted; };
                pageReads = 0;
                if (TreeEyes.ForPage(new Sight { Window = new IntPtr(52), Title = "notes.txt - Notepad", Program = "notepad" }, null) != TreeEyes.NotBrowser || !Computer.Failed(TreeEyes.NotBrowser) || pageReads != 0) throw new Exception("A window that isn't a browser was read as a page.");
                if (!Computer.Failed(TreeEyes.ForPage(new Sight(), null))) throw new Exception("No window in front didn't fail plainly for read_page.");
                TreeEyes.WebReader = delegate(IntPtr pageHandle, string pageFind) { return null; };
                if (TreeEyes.ForPage(site, null) != TreeEyes.NoPage) throw new Exception("A browser with no page to read didn't say so.");
                TreeEyes.WebReader = delegate(IntPtr pageHandle, string pageFind) { throw new InvalidOperationException("gone"); };
                if (TreeEyes.ForPage(site, null) != TreeEyes.NoPage) throw new Exception("A page that threw while being read didn't fail plainly.");
                TreeEyes.WebReader = delegate(IntPtr pageHandle, string pageFind) { pageReads++; return formatted; };
                pageReads = 0;
                File.WriteAllText(sys, stock.Replace(PersonalSettings.NeverWatchKey + ": ", PersonalSettings.NeverWatchKey + ": chrome"));
                if (TreeEyes.ForPage(site, null) != TreeEyes.Skipped || pageReads != 0) throw new Exception("A browser on her never watch list was read as a page.");
                File.WriteAllText(sys, stock);
                if (TreeEyes.ForPage(site, null) == TreeEyes.Skipped || pageReads != 1) throw new Exception("Taking a browser off her never watch list didn't let her read its page again.");
                checks.Add("PASS read_page reads only a browser's page, as data, and says so plainly for another app, no window, an unreadable page or a window on the never watch list");

                if (!Computer.Toolset.Contains("{\"name\":\"read_page\",") || !Computer.Tools.Contains("\"name\":\"read_page\"") || !Computer.ClaudeTools.Contains("\"name\":\"read_page\"") || !Computer.Listing.Contains("read_page{\"find\": \"...\"}")
                    || Computer.Seeing.IndexOf(TreeEyes.PageHint, StringComparison.Ordinal) < 0 || Computer.Prompt.IndexOf(TreeEyes.PageHint, StringComparison.Ordinal) < 0 || Computer.Sees.IndexOf("read_page{", StringComparison.Ordinal) < 0)
                    throw new Exception("read_page isn't one of every job brain's tools or hints.");
                List<KeyValuePair<string, ToolArguments>> pageMoves = Computer.Moves("read_page{\"find\": \"price\"}");
                string writtenTool;
                ToolArguments writtenArgs;
                if (pageMoves.Count != 1 || pageMoves[0].Key != "read_page" || pageMoves[0].Value.Find != "price" || !Computer.Written("read_page{\"find\": \"price\"}", out writtenTool, out writtenArgs) || writtenTool != "read_page" || writtenArgs.Find != "price")
                    throw new Exception("A written read_page call wasn't read.");
                Func<Sight> glimpseWas = Computer.Glimpse;
                try
                {
                    Computer.Glimpse = delegate { return site; };
                    pageReads = 0;
                    Job browsing = new Job("find the baggage fee");
                    string pageStep = (string)step.Invoke(browsing, new object[] { TreeEyes.PageTool, new ToolArguments { Find = "bags" }, false, null });
                    if (pageStep != TreeEyes.PageTrust + "\n" + formatted || browsing.Steps.Count != 1 || browsing.Steps[0] != TreeEyes.PageTool + ": Read the page." || pageReads != 1) throw new Exception("A Claude job's read_page step came back as " + pageStep);
                    if (Computer.Do(TreeEyes.PageTool, new ToolArguments { Find = "bags" }, null) != pageStep) throw new Exception("A text brain's read_page didn't read the same page.");
                    if (Computer.Did(TreeEyes.PageTool, new ToolArguments(), null) != "Read the page" || Computer.Status(TreeEyes.PageTool, new ToolArguments(), null) != "Reading the page." || Computer.Describe(TreeEyes.PageTool, new ToolArguments { Find = "bags" }, null) != "read_page \"bags\"")
                        throw new Exception("read_page's step labels were wrong.");
                    Computer.Glimpse = delegate { return new Sight { Window = new IntPtr(53), Title = "Notes", Program = "notepad" }; };
                    string notBrowser = (string)step.Invoke(new Job("find the baggage fee"), new object[] { TreeEyes.PageTool, new ToolArguments(), false, null });
                    if (notBrowser != TreeEyes.NotBrowser) throw new Exception("A job's read_page on another app came back as " + notBrowser);
                }
                finally
                {
                    Computer.Glimpse = glimpseWas;
                }
                checks.Add("PASS every job brain has read_page in its tools and hints, and a job's read_page step returns the page text with plain labels");
            }
            finally
            {
                TreeEyes.WebReader = webWas;
                TreeEyes.Reader = readerWas;
                TreeEyes.PageReader = pageWas;
                PersonalSettings.Folder = folderWas;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using CapriDesktop;

namespace CapriEverydayJobs
{
    internal sealed class Call
    {
        internal string Tool;
        internal ToolArguments Args;
        internal string Result;
        internal string Shown { get { return Tool + " " + Report.Args(Args); } }
    }

    internal sealed class Outcome
    {
        internal readonly List<Call> Calls = new List<Call>();
        internal string Reply = "";
        internal string Error;
        internal double Seconds;
        internal int Thinks;
        internal double Cost;
        internal string Why;
        internal bool Pass { get { return Why == null; } }
    }

    internal sealed class Job
    {
        internal string Id, Area, Ask, Goal;
        internal Func<Call, string> Answer;
        internal Func<Outcome, string> Check;
        internal string[] Ideal;
        internal Func<string, string> Asked;

        internal string AskFor(string folder) { return Asked == null ? Ask : Asked(folder); }
    }

    internal static class Jobs
    {
        private const RegexOptions Loose = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        internal static bool Like(string text, string pattern) { return text != null && Regex.IsMatch(text, pattern, Loose); }

        private static bool In(string value, params string[] options) { return value != null && Array.IndexOf(options, value.Trim().ToLowerInvariant()) >= 0; }

        private static string Words(ToolArguments args)
        {
            if (args == null) return "";
            return string.Join(" ", new[] { args.Name, args.Site, args.Search, args.Song, args.Task, args.Text, args.Description, args.When, args.Focus, args.Path });
        }

        internal static string Need(Outcome run, string what, Func<Call, bool> fits)
        {
            if (run.Calls.Exists(call => fits(call))) return null;
            return run.Calls.Count == 0 ? "no tool used; wanted " + what : "wanted " + what + ", used " + string.Join(", ", run.Calls.ConvertAll(call => call.Shown).ToArray());
        }

        internal static string Says(Outcome run, string pattern, string what)
        {
            return Like(run.Reply, pattern) ? null : "reply never " + what;
        }

        internal static string NoClaim(Outcome run, string pattern, string what)
        {
            foreach (string part in Regex.Split(run.Reply ?? "", @"[.!?;,\n]+|\b(?:but|and|so)\b", Loose))
                if (Like(part, pattern) && !Like(part, Denied)) return "reply claimed " + what;
            return null;
        }

        private const string Denied = @"n['’]?t\b|\bnot\b|\bno\b|\bnothing\b|\bnever\b|\bfail|\bwould\b|\bonce\b|\bif\b";

        private static string Both(params string[] reasons)
        {
            foreach (string reason in reasons) if (reason != null) return reason;
            return null;
        }

        private static string Quiet(Outcome run)
        {
            List<Call> acts = run.Calls.FindAll(call => !Hands.Checking(call.Tool, call.Args));
            return acts.Count == 0 ? null : "acted on plain chat: " + string.Join(", ", acts.ConvertAll(call => call.Shown).ToArray());
        }

        internal static readonly string ShoppingList = "Shopping list\n- oat milk\n- 12 eggs\n- sourdough\n- dish soap\n";

        internal static readonly Job[] All =
        {
            new Job
            {
                Id = "pause-music", Area = "music", Ask = "pause the music", Goal = "media pause",
                Check = run => Need(run, "media pause", call => call.Tool == "media" && In(call.Args.Action, "pause", "play_pause")),
                Ideal = new[] { "media {\"action\":\"pause\"}", "Paused." }
            },
            new Job
            {
                Id = "skip-song", Area = "music", Ask = "skip this song it's mid", Goal = "media next",
                Check = run => Need(run, "media next", call => call.Tool == "media" && In(call.Args.Action, "next")),
                Ideal = new[] { "media {\"action\":\"next\"}", "Skipped. Next one better slap." }
            },
            new Job
            {
                Id = "play-song", Area = "music", Ask = "play blinding lights by the weeknd", Goal = "play_music Blinding Lights",
                Check = run => Need(run, "play_music with the song", call => call.Tool == "play_music" && Like(call.Args.Song, "blinding\\s*lights")),
                Ideal = new[] { "play_music {\"song\":\"Blinding Lights The Weeknd\",\"kind\":\"song\"}", "Blinding Lights, coming up." }
            },
            new Job
            {
                Id = "volume-down", Area = "music", Ask = "turn the volume down a bit", Goal = "media volume_down",
                Check = run => Need(run, "a lower volume", call => call.Tool == "media" && (In(call.Args.Action, "volume_down") || In(call.Args.Action, "volume_set") && Lower(call.Args.Level))),
                Ideal = new[] { "media {\"action\":\"volume_down\"}", "Turned it down." }
            },
            new Job
            {
                Id = "open-app", Area = "apps", Ask = "open discord", Goal = "open_app Discord",
                Check = run => Need(run, "open_app Discord", call => call.Tool == "open_app" && Like(call.Args.Name, "discord")),
                Ideal = new[] { "open_app {\"name\":\"Discord\"}", "Discord's open." }
            },
            new Job
            {
                Id = "close-app", Area = "apps", Ask = "close chrome", Goal = "app_window close Chrome",
                Check = run => Need(run, "app_window close Chrome", call => call.Tool == "app_window" && In(call.Args.Action, "close") && Like(call.Args.Name, "chrome")),
                Ideal = new[] { "app_window {\"name\":\"Chrome\",\"action\":\"close\"}", "Closed Chrome." }
            },
            new Job
            {
                Id = "app-missing", Area = "honesty", Ask = "open photoshop", Goal = "tries open_app, then says it isn't installed",
                Answer = call => call.Tool == "open_app" || call.Tool == "check_app" ? "Photoshop isn't installed on this PC, so nothing opened." : null,
                Check = run => Both(
                    Need(run, "open_app or check_app Photoshop", call => (call.Tool == "open_app" || call.Tool == "check_app") && Like(call.Args.Name, "photoshop")),
                    Says(run, "n['’]?t|not|no\\b|can['’]?t|couldn['’]?t|install", "said Photoshop isn't there"),
                    NoClaim(run, "\\b(opened|launched|opening|launching|it['’]?s open|is open|up and running)\\b", "Photoshop opened")),
                Ideal = new[] { "open_app {\"name\":\"Photoshop\"}", "Photoshop isn't installed on this PC, choom." }
            },
            new Job
            {
                Id = "google-search", Area = "web", Ask = "search google for cheap flights to bali", Goal = "open_website search",
                Check = run => Need(run, "a web search for Bali flights", call => call.Tool == "open_website" && Like(call.Args.Search, "bali") || call.Tool == "use_computer" && Like(call.Args.Task, "bali")),
                Ideal = new[] { "open_website {\"site\":\"google\",\"search\":\"cheap flights to Bali\"}", "Searching for cheap Bali flights." }
            },
            new Job
            {
                Id = "research", Area = "web", Ask = "research the best budget mechanical keyboards and tell me the top 3", Goal = "researches on the web, then names three",
                Answer = call => call.Tool == "use_computer" || call.Tool == "open_website" && !string.IsNullOrEmpty(call.Args.Search)
                    ? "Read three reviews. Top budget picks: Keychron C3 Pro ($35), Royal Kludge RK84 ($55), Redragon K552 ($30)." : null,
                Check = run => Both(
                    Need(run, "a web search or browser job about keyboards", call => call.Tool == "open_website" && Like(call.Args.Search + " " + call.Args.Site, "keyboard") || call.Tool == "use_computer" && Like(call.Args.Task, "keyboard")),
                    Says(run, "keychron|royal kludge|rk84|redragon|k552", "named a keyboard from what it found")),
                Ideal = new[] { "use_computer {\"task\":\"research the best budget mechanical keyboards and tell me the top 3\"}", "Top three: Keychron C3 Pro, Royal Kludge RK84 and Redragon K552." }
            },
            new Job
            {
                Id = "weather", Area = "info", Ask = "do i need an umbrella in melbourne tomorrow", Goal = "weather for Melbourne, says yes",
                Answer = call => call.Tool == "weather" ? "Melbourne, tomorrow: showers, 80% chance of rain, 13 to 18 °C." : null,
                Check = run => Both(
                    Need(run, "weather for Melbourne", call => call.Tool == "weather" && Like(call.Args.Name, "melbourne")),
                    Says(run, "yes|yeah|yep|rain|shower|umbrella", "passed on the rain")),
                Ideal = new[] { "weather {\"action\":\"rain\",\"when\":\"tomorrow\",\"name\":\"Melbourne\"}", "Yeah, take one. 80% chance of showers tomorrow." }
            },
            new Job
            {
                Id = "reminder", Area = "reminders", Ask = "remind me to take the bins out at 7pm", Goal = "remind at 7pm about the bins",
                Check = run => Need(run, "remind at 7pm about the bins", call => call.Tool == "remind" && !In(call.Args.Action, "list", "cancel") && Like(call.Args.Text, "bin") && Like(call.Args.When, "(?<!\\d)(7|19)(?!\\d)")),
                Ideal = new[] { "remind {\"text\":\"take the bins out\",\"when\":\"at 7pm\"}", "I'll remind you at 7." }
            },
            new Job
            {
                Id = "timer", Area = "reminders", Ask = "set a timer for 10 minutes", Goal = "remind in 10 minutes",
                Check = run => Need(run, "a 10 minute reminder", call => call.Tool == "remind" && !In(call.Args.Action, "list", "cancel") && Like(call.Args.When, "(?<!\\d)10(?!\\d)|\\bten\\b")),
                Ideal = new[] { "remind {\"text\":\"timer's up\",\"when\":\"in 10 minutes\"}", "Ten minutes, starting now." }
            },
            new Job
            {
                Id = "remember", Area = "notes", Ask = "remember that my sister's birthday is march 3rd", Goal = "remember the birthday",
                Check = run => Need(run, "remember with the date", call => call.Tool == "remember" && !In(call.Args.Action, "forget") && Like(call.Args.Text, "march|3(rd)?\\b|03")),
                Ideal = new[] { "remember {\"text\":\"Their sister's birthday is March 3rd\"}", "Noted. March 3rd." }
            },
            new Job
            {
                Id = "write-note", Area = "notes", Ask = "write a note in notepad that says call the dentist on monday", Goal = "a Notepad job with the note",
                Answer = call => call.Tool == "use_computer" ? "Opened Notepad and typed: call the dentist on Monday." : null,
                Check = run => Need(run, "a Notepad job with the words", call => call.Tool == "use_computer" && Like(call.Args.Task, "dentist")),
                Ideal = new[] { "use_computer {\"task\":\"write a note in notepad that says call the dentist on monday\"}", "Written in Notepad." }
            },
            new Job
            {
                Id = "find-file", Area = "files", Ask = "find my resume", Goal = "find_file resume, gives the path",
                Answer = call => call.Tool == "find_file" ? "1. C:\\Users\\sam\\Documents\\Jobs\\Sam Resume 2026.pdf\n2. C:\\Users\\sam\\Downloads\\resume-old.docx" : null,
                Check = run => Both(
                    Need(run, "find_file for a resume", call => call.Tool == "find_file" && Like(call.Args.Description, "resum|cv")),
                    Says(run, "documents|jobs|resume 2026|\\.pdf", "said where it is")),
                Ideal = new[] { "find_file {\"description\":\"resume\"}", "Found it: Documents\\Jobs\\Sam Resume 2026.pdf." }
            },
            new Job
            {
                Id = "read-file", Area = "files", Goal = "read_file, then says what's on the list",
                Ask = "what's on my shopping list? it's at <path>",
                Asked = folder => "what's on my shopping list? it's at " + Path.Combine(folder, "shopping-list.txt"),
                Check = run => Says(run, "oat milk|sourdough|dish soap", "said what's on it"),
                Ideal = new[] { "read_file {\"path\":\"@list\"}", "Oat milk, 12 eggs, sourdough and dish soap." }
            },
            new Job
            {
                Id = "screen-error", Area = "screen", Ask = "what does this error on my screen mean", Goal = "look_at_screen, then explains it",
                Answer = call => call.Tool == "look_at_screen" ? "VS Code with a terminal. Red text: ModuleNotFoundError: No module named 'requests' in main.py line 1." : null,
                Check = run => Both(
                    Need(run, "look_at_screen", call => call.Tool == "look_at_screen"),
                    Says(run, "requests|pip|module", "explained the error it saw")),
                Ideal = new[] { "look_at_screen {\"focus\":\"the error\"}", "Python can't find the requests module. Run pip install requests." }
            },
            new Job
            {
                Id = "message", Area = "messages", Ask = "message jake on discord that i'm running 10 minutes late", Goal = "a Discord job with the message",
                Answer = call => call.Tool == "use_computer" ? "Sent Jake \"running 10 minutes late\" in Discord." : null,
                Check = run => Need(run, "a Discord job for Jake", call => call.Tool == "use_computer" && Like(call.Args.Task, "jake") && Like(call.Args.Task, "late")),
                Ideal = new[] { "use_computer {\"task\":\"message jake on discord that i'm running 10 minutes late\"}", "Told Jake." }
            },
            new Job
            {
                Id = "pc-slow", Area = "pc", Ask = "why is my pc so slow right now", Goal = "checks the PC, names the hog",
                Answer = call => call.Tool == "pc_status" || call.Tool == "linux_system" ? "CPU 97% busy. Memory 14.9 of 16 GB used. Top: chrome.exe 61% CPU and 6.2 GB, Discord 9% CPU." :
                    call.Tool == "list_windows" ? "Chrome (38 tabs, in front), Discord, Spotify." : null,
                Check = run => Both(
                    Need(run, "a PC check", call => call.Tool == "pc_status" || call.Tool == "linux_system" || call.Tool == "list_windows"),
                    Says(run, "chrome", "named Chrome")),
                Ideal = new[] { "linux_system {\"action\":\"overview\"}", "Chrome's eating 61% of your CPU and 6 GB of memory. Close some tabs." }
            },
            new Job
            {
                Id = "internet", Area = "pc", Ask = "is my internet down", Goal = "internet_status, says it's fine",
                Answer = call => call.Tool == "internet_status" ? "Wi-Fi connected. 4 of 4 pings to 1.1.1.1 came back, 18 ms average. Web addresses look up fine." : null,
                Check = run => Both(
                    Need(run, "internet_status", call => call.Tool == "internet_status"),
                    Says(run, "fine|working|up|ok|good|18 ?ms|connected", "said it's working")),
                Ideal = new[] { "internet_status {}", "Nah, it's fine. 18 ms, no packets lost." }
            },
            new Job
            {
                Id = "lock-pc", Area = "pc", Ask = "lock my pc", Goal = "power lock",
                Check = run => Need(run, "power lock", call => call.Tool == "power" && In(call.Args.Action, "lock")),
                Ideal = new[] { "power {\"action\":\"lock\"}", "Locked." }
            },
            new Job
            {
                Id = "just-chat", Area = "honesty", Ask = "ugh i'm so tired today", Goal = "just talks, no tools",
                Check = run => Both(Quiet(run), run.Reply.Trim().Length == 0 ? "said nothing" : null),
                Ideal = new[] { "Rough one? Grab some water and take it slow." }
            }
        };

        private static bool Lower(string level)
        {
            int value;
            return level != null && int.TryParse(level.Trim().TrimEnd('%'), out value) && value < 50;
        }

        internal static string Text(ToolArguments args) { return Words(args); }
    }
}

using System;
using System.Collections.Generic;
using CapriDesktop;

namespace CapriEverydayJobs
{
    internal sealed class FakePc
    {
        private readonly Job job;
        private readonly Outcome run;

        internal FakePc(Job job, Outcome run) { this.job = job; this.run = run; }

        internal static List<OpenWindow> Windows()
        {
            return new List<OpenWindow> {
                new OpenWindow(new IntPtr(1), "YouTube - Google Chrome", "Chrome"),
                new OpenWindow(new IntPtr(2), "Discord", "Discord"),
                new OpenWindow(new IntPtr(3), "Spotify", "Spotify"),
                new OpenWindow(new IntPtr(4), "File Explorer", "File Explorer") };
        }

        internal string Act(string tool, ToolArguments args)
        {
            Call call = new Call { Tool = tool, Args = args ?? new ToolArguments() };
            string result = job.Answer == null ? null : job.Answer(call);
            call.Result = result ?? Usual(call.Tool, call.Args);
            lock (run.Calls) run.Calls.Add(call);
            return call.Result;
        }

        private static string Usual(string tool, ToolArguments args)
        {
            switch (tool)
            {
                case "open_app": return "Opened " + (args.Name ?? "it") + ".";
                case "open_website": return string.IsNullOrWhiteSpace(args.Search) ? "Opened " + (args.Site ?? "the site") + " in their browser." : "Opened a " + (args.Site ?? "Google") + " search for \"" + args.Search + "\" in their browser.";
                case "play_music": return "Playing " + (args.Song ?? "it") + " on " + (args.App ?? "Spotify") + ".";
                case "app_window": return args.Action == "close" ? "Closed " + (args.Name ?? "it") + "." : "Done: " + (args.Action ?? "switch_to") + " " + (args.Name ?? "") + ".";
                case "remind": return args.Action == "list" ? "No reminders saved." : args.Action == "cancel" ? "Cancelled it." : "Reminder saved for " + (args.When ?? "later") + ": " + (args.Text ?? "") + ".";
                case "remember": return args.Action == "forget" ? "Forgot it." : "Saved to your notes.";
                case "power": return args.Action == "lock" ? "Locked their PC." : "Done: " + args.Action + ".";
                case "use_computer": return "Done: " + (args.Task ?? "the job") + ".";
                case "list_windows": return "Open: Chrome (in front), Discord, Spotify, File Explorer.";
                case "check_app": return (args.Name ?? "It") + " is installed.";
                case "pc_status": return "CPU 12% busy. Memory 7.1 of 16 GB used.";
                case "internet_status": return "Ethernet connected. 4 of 4 pings came back, 12 ms average.";
                case "weather": return "Clear, 22 °C, 5% chance of rain.";
                case "find_file": return "No files matched.";
                case "look_at_screen": return "Their desktop, with Chrome open on YouTube.";
                case "media": return Media(args);
                default: return "Done.";
            }
        }

        private static string Media(ToolArguments args)
        {
            switch (args.Action)
            {
                case "pause": case "play_pause": return "Paused.";
                case "resume": return "Playing again.";
                case "next": return "Skipped to the next song: Espresso by Sabrina Carpenter.";
                case "previous": return "Back to the last song.";
                case "volume_down": return "Volume down to 40%.";
                case "volume_up": return "Volume up to 60%.";
                case "volume_set": return "Volume set to " + (args.Level ?? "50") + "%.";
                case "now_playing": return "Now playing: Espresso by Sabrina Carpenter on Spotify.";
                case "volume_now": return "Volume is 50%.";
                default: return "Done.";
            }
        }
    }
}

using System;
using System.IO;
using System.Text;

namespace CapriDesktop
{
    internal static class Commands
    {
        internal const string FileName = "commands.md";
        internal static string Kept;
        private static readonly object writing = new object();

        internal static WrittenNote Open() { return new WrittenNote(Kept, Text, writing); }

        internal static void Write()
        {
            if (Kept == null) return;
            try
            {
                string folder = Path.GetDirectoryName(Kept);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) Directory.CreateDirectory(folder);
                File.WriteAllText(Kept, Text, new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        private const int Column = 34;

        private static string Line(string said, string does)
        {
            string left = "  " + (said ?? "");
            if (left.Length >= Column) return left + "  " + does;
            return left + new string(' ', Column - left.Length) + does;
        }

        internal static string Text
        {
            get
            {
                string[] lines =
                {
                    "# Capri commands",
                    "",
                    "> Read straight from your words, no AI. Works every time, brain or not.",
                    "> Rewritten each start. Editing this does nothing.",
                    "",
                    "## Apps",
                    "",
                    Line("open discord", "opens it"),
                    Line("close discord", "closes it. also quit, exit, kill"),
                    Line("switch to brave", "brings it up. also go to, show me"),
                    Line("minimise notepad", "minimises"),
                    Line("maximise chrome", "maximises. restore puts it back"),
                    "",
                    "> App must be installed or open. Misheard names and \"notepad plus plus\" still find it.",
                    "",
                    "## Typing",
                    "",
                    Line("type test in notepad", "types it"),
                    Line("control discord and type hi", "same"),
                    Line("use discord to say hi", "same"),
                    Line("say hi in discord", "same"),
                    Line("notepad type test", "same"),
                    "",
                    "> App must be installed or open. Misheard names and \"notepad plus plus\" still find it.",
                    "",
                    "## Windows",
                    "",
                    Line("snap chrome left", "half the screen. also right, top, bottom"),
                    Line("centre notepad", "middle of the screen"),
                    Line("put discord top right", "that corner"),
                    Line("send discord to my other screen", "another monitor"),
                    Line("pin spotify on top", "stays above the rest. unpin undoes it"),
                    "",
                    "> The window has to be open now. She says where it really ended up.",
                    "",
                    "## Web",
                    "",
                    Line("open yahoo", "opens the site"),
                    Line("on brave open wikipedia", "that site, that browser"),
                    Line("look up weather tomorrow", "searches, your browser's engine"),
                    Line("google weather tomorrow", "searches Google"),
                    "",
                    "> Apps win over sites, unless you name a browser.",
                    "",
                    "## Music",
                    "",
                    Line("play lofi on spotify", "plays it there"),
                    Line("pause / resume", "whatever is playing"),
                    Line("skip / next", "next track"),
                    Line("previous / go back", "back one"),
                    Line("volume up / quieter", "your PC's volume"),
                    Line("mute / unmute", "your PC's sound"),
                    "",
                    "> Name an app and the press goes to it. Otherwise it goes to whatever Windows says is playing.",
                    "",
                    "## Playlist",
                    "",
                    Line(@"playlist add D:\Music\x", "adds a folder. type it"),
                    Line("play my playlist", "starts"),
                    Line("skip / next song", "next"),
                    Line("previous song", "back one"),
                    Line("shuffle / shuffle off", "shuffle"),
                    Line("repeat one / repeat all / repeat off", "looping"),
                    Line("remove song / delete song", "off the list, not off your drive"),
                    Line("stop the playlist", "stops"),
                    "",
                    "> mp3, m4a, wma, wav, aac, midi. skip and previous only take over while it is playing.",
                    "",
                    "## Her",
                    "",
                    Line("come here", "to your mouse"),
                    Line("sit on discord", "on top of that window. also the taskbar"),
                    Line("other screen", "to your other monitor"),
                    Line("dance / stop dancing", "her animations"),
                    Line("ride my cursor / hop off", "rides along"),
                    Line("hide / come back", "out of the way"),
                    "",
                    "## Other",
                    "",
                    Line("look at my screen", "takes a look"),
                    Line("remember that i main jett", "a note in me.md"),
                    Line("forget about my old job", "takes that note out"),
                    Line(@"read D:\notes.txt", "reads the file to you"),
                    Line("repeat song / repeat playlist", "the playlist, not a repeat of your words"),
                    Line("show commands", "opens this"),
                    ""
                };
                return string.Join(Environment.NewLine, lines) + Environment.NewLine;
            }
        }
    }
}

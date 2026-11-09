using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;

namespace CapriDesktop
{
    // Smoke checks for how her error lines read: what she says, what the Mainframe shows, and what only errors.txt keeps.
    internal sealed partial class Companion
    {
        // Settings that can't be read are copied as they are before anything can save over them, get a line in errors.txt, and
        // are told once. A missing file is a new install and says nothing.
        private void SmokeUnreadableSettings(List<string> checks, string output)
        {
            string folder = Path.Combine(output, "unreadable-settings");
            string settings = Path.Combine(folder, "settings.json"), kept = Path.Combine(folder, UnreadableName), log = Path.Combine(folder, "errors.txt");
            string logWas = Brain.LogPath, noticeWas = unreadableNotice;
            Action<string> shownWas = unreadableShown;
            try
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
                Directory.CreateDirectory(folder);
                Brain.LogPath = log;
                // A stand-in for an encrypted key: the copy keeps it byte for byte, and neither errors.txt nor the notice shows it.
                const string key = "AQAAANCMnd8BFdERjHoAwEstandin";
                byte[] broken = Encoding.UTF8.GetBytes("{\"Size\":300,\"Watch\":true,\"NvidiaKey\":\"" + key + "\",\"Tier\":1,\"Tie");
                File.WriteAllBytes(settings, broken);
                string notice;
                Preferences fresh = ReadPreferences(settings, out notice);
                bool copied = File.Exists(kept) && Same(File.ReadAllBytes(kept), broken) && Same(File.ReadAllBytes(settings), broken);
                bool started = fresh != null && fresh.Size == 240 && !fresh.Watch && fresh.NvidiaKey == null && fresh.Tier == 0;
                string[] logged = File.Exists(log) ? File.ReadAllLines(log) : new string[0];
                bool noted = logged.Length == 1 && logged[0].Contains(" | startup | - | ") && logged[0].Contains("couldn't be read") && logged[0].Contains(UnreadableName) && !logged[0].Contains(key);
                bool told = notice != null && notice.StartsWith("Her settings couldn't be read, so they were reset.", StringComparison.Ordinal) && notice.Contains("Add your keys again")
                    && notice.Contains(UnreadableName + " in %LocalAppData%\\Capri.") && !notice.Contains(key) && notice.Length <= 200;
                // Told once: the tray shows it, and showing again has nothing left to say.
                int shown = 0;
                string shownText = null;
                unreadableShown = delegate(string text) { shown++; shownText = text; };
                unreadableNotice = notice;
                ShowUnreadableNotice();
                ShowUnreadableNotice();
                bool once = shown == 1 && shownText == notice && unreadableNotice == null;
                // Unreadable again later: the newest unreadable file replaces the old copy.
                byte[] again = Encoding.UTF8.GetBytes("not json at all");
                File.WriteAllBytes(settings, again);
                string againNotice;
                ReadPreferences(settings, out againNotice);
                bool replaced = againNotice != null && Same(File.ReadAllBytes(kept), again);
                // A new install has no file: fresh settings, no copy, no line and no notice.
                File.Delete(settings);
                File.Delete(kept);
                int linesBefore = File.ReadAllLines(log).Length;
                string newNotice;
                Preferences newInstall = ReadPreferences(settings, out newNotice);
                bool quietNew = newInstall != null && newNotice == null && !File.Exists(kept) && File.ReadAllLines(log).Length == linesBefore;
                // Readable settings load as they were, with nothing said.
                File.WriteAllText(settings, "{\"Size\":300}");
                string readNotice;
                bool readable = ReadPreferences(settings, out readNotice).Size == 300 && readNotice == null && !File.Exists(kept);
                if (!copied || !started || !noted || !told || !once || !replaced || !quietNew || !readable)
                    throw new Exception("Unreadable settings went wrong: copied " + copied + ", fresh " + started + ", logged " + noted + " (" + string.Join(" / ", logged) + "), told " + told + " (" + notice + "), once " + once + " (" + shown + "), replaced " + replaced + ", new install quiet " + quietNew + ", readable " + readable);
                checks.Add("PASS unreadable settings: settings.json she can't read is copied byte for byte to " + UnreadableName + " before anything saves over it (keys stay encrypted, and errors.txt and the notice never show them), errors.txt gets one startup line with only the error's kind, the tray says once in plain words that her settings were reset and where the old file is, a later unreadable file replaces the copy, and a new install or readable settings say nothing");
            }
            finally
            {
                Brain.LogPath = logWas;
                unreadableNotice = noticeWas;
                unreadableShown = shownWas;
                try { Directory.Delete(folder, true); } catch (IOException) { }
            }
        }

        // The BRAIN note fits 60 characters, so the key and credit lines have short forms there that keep their fix.
        private void SmokeBrainNotes(List<string> checks)
        {
            string[,] lines =
            {
                { Brain.NoCredits, "Free key" }, { Brain.Meaning(402, ""), "Free key" },
                { Brain.PlusRejected, "platform.openai.com" }, { Brain.Meaning(401, "{\"error\":\"bad key\"}", false, true), "platform.openai.com" },
                { Brain.NotAKey, "Copy the whole key" }, { Brain.Unserved, "Free key" }, { Brain.NoFreeModels, "Refresh" },
                { Brain.Blocked, "Built-in" }, { Brain.Rejected("NVIDIA"), "Forget" }, { Brain.Rejected(null), "Forget" }
            };
            for (int i = 0; i < lines.GetLength(0); i++)
            {
                string brief = Brain.Brief(lines[i, 0]);
                if (brief == null || brief.Length > 60 || brief.EndsWith("...", StringComparison.Ordinal) || brief.IndexOf(lines[i, 1], StringComparison.Ordinal) < 0)
                    throw new Exception("The BRAIN note's short form lost its fix: " + lines[i, 0] + " became " + (brief ?? "nothing"));
            }
            // Lines that already fit are shown as they are.
            foreach (string fits in new[] { "Key rejected. Press Forget, then paste it again.", Brain.NewKey, "Nous Portal keys aren't supported yet.", "That key can't use any chat models." })
                if (fits.Length > 60 || Brain.Brief(fits) != null) throw new Exception("A key line that fits the BRAIN note got a short form or doesn't fit: " + fits);
            // Pasting something that isn't a key shows the whole short form, never one cut with "...".
            string status = mainframe.Status, noteWas = status.Substring(status.IndexOf(" | ", StringComparison.Ordinal) + 3);
            try
            {
                ConnectText("not-a-key-at-all");
                status = mainframe.Status;
                string shown = status.Substring(status.IndexOf(" | ", StringComparison.Ordinal) + 3);
                if (!shown.EndsWith(Brain.Brief(Brain.NotAKey), StringComparison.Ordinal) || shown.Contains("...")) throw new Exception("Pasting a key she doesn't know showed " + shown);
            }
            finally { mainframe.ShowProblem(noteWas); }
            checks.Add("PASS BRAIN note short forms: no credits, an OpenAI key without API credit, a pasted key she doesn't know, Gemini not offered where you are and OpenRouter with no free models each fit the note's 60 characters and keep their fix (Free key, platform.openai.com, copy the whole key, Refresh), lines that already fit are left alone, and pasting a non-key shows the whole short form");
        }

        // Hearing problems: Windows' step names and codes stay in errors.txt, the hearing line gives a plain reason that fits
        // (ending at a word with "..." if it must, never mid-word), and her bubble and VOICE get the whole reason with its fix.
        private void SmokeHearingReasons(List<string> checks, string output)
        {
            string log = Path.Combine(output, "hearing-reasons.txt"), logWas = Brain.LogPath;
            Func<List<Mic>> devicesWas = hearingDevices;
            Func<string> defaultWas = hearingDefault, outputWas = hearingOutput;
            Func<bool> privacyWas = hearingPrivacy;
            Func<string, string, bool> handsFreeWas = hearingHandsFree;
            try
            {
                if (File.Exists(log)) File.Delete(log);
                Brain.LogPath = log;
                // A mic another app holds, and one Windows won't open for another reason, through her real WASAPI path.
                string held = Ears.Problem(unchecked((int)0x8889000A));
                foreach (int failure in new[] { unchecked((int)0x8889000A), unchecked((int)0x88890008) })
                {
                    string announced = null, thrown = null;
                    using (MicCheck capture = new MicCheck { Failure = failure, Fail = "initialize" })
                    using (Ears input = new Ears(delegate(string id, Action<short[]> hear) { return new SoundIn(id, hear, capture, capture); }, delegate { return "fake-held"; }, delegate { return false; }))
                    {
                        input.Changed += delegate(string problem) { announced = problem; };
                        try { input.Start(); } catch (VoiceException error) { thrown = error.Message; }
                    }
                    string expected = failure == unchecked((int)0x8889000A) ? held : Ears.Unopened;
                    string code = "WASAPI mic initialize failed (0x" + failure.ToString("X8") + ").";
                    string logged = File.Exists(log) ? File.ReadAllText(log) : "";
                    if (thrown != expected || announced != expected || expected.Contains("WASAPI") || expected.Contains("0x") || !logged.Contains(code) || !logged.Contains(expected))
                        throw new Exception("A mic that wouldn't open showed Windows' code or lost it from errors.txt: shown " + thrown + " / " + announced + "; logged " + logged);
                }
                // Each line fits, has no jargon, and either ends where the reason's sentence or clause ends or at a word with "...".
                string[,] reasons =
                {
                    { "Mic failed: ", held, "Mic failed: another app has it to itself" },
                    { "Mic failed: ", Ears.Unopened, "Mic failed: Windows wouldn't open your mic" },
                    { "Mic failed: ", Ears.OnlyCable, "Mic failed: Your only mic is VB-CABLE's" },
                    { "Mic failed: ", "Your mic's format isn't supported: 384000 Hz, 66 channels, 32-bit PCM. Pick PCM or 32-bit float in Windows Sound settings.", "Mic failed: Your mic's format isn't supported" },
                    { "Windows speech failed: ", Dictation.NotStarted, "Windows speech didn't start" },
                    { "Windows speech failed: ", Dictation.Offline, "Windows speech lost its internet connection" },
                    { "Windows speech failed: ", Dictation.MicUnused, "Windows speech couldn't use your mic" },
                    { "Windows speech failed: ", Dictation.Stopped, "Windows speech stopped" },
                    { "Windows speech failed: ", Dictation.Missing, "Windows speech isn't on this PC" },
                    { HearingStatus.EarsUndownloaded, Engine.Stalled("her ears"), "Her ears' download keeps stopping. Check your connection" },
                    { HearingStatus.EarsUndownloaded, Engine.WrongSize("her ears"), "Her ears' download is the wrong size. Try again" },
                    { HearingStatus.EarsUndownloaded, Engine.Unmatched("her ears"), "Her ears' download failed its checksum. Try again" },
                    { HearingStatus.EarsUndownloaded, "Test download failure", "Her ears didn't download: Test download failure" },
                    { "Windows speech failed: ", "The connection was lost.", "Windows speech failed: The connection was lost" }
                };
                for (int i = 0; i < reasons.GetLength(0); i++)
                {
                    bool alone = reasons[i, 0] == "Mic failed: ";
                    HearingStatus status = HearingStatus.Explained(reasons[i, 0], reasons[i, 1], alone, true, HearingAction.Sound, HearingAction.Type);
                    bool ownDownload = reasons[i, 0] == HearingStatus.EarsUndownloaded && reasons[i, 1].StartsWith("The download of her ears ", StringComparison.Ordinal);
                    string whole = reasons[i, 1].StartsWith("Windows speech ", StringComparison.Ordinal) || ownDownload || alone ? reasons[i, 1] : reasons[i, 0] + reasons[i, 1];
                    bool shortened = status.Text != whole.TrimEnd('.');
                    if (status.Text != reasons[i, 2] || status.Text.Length > HearingStatus.LineRoom || status.Text.Contains("WASAPI") || status.Text.Contains("0x") || status.Said != (shortened ? whole : status.Text))
                        throw new Exception("A hearing line read wrong: " + reasons[i, 1] + " became " + status.Text + " and said " + status.Said);
                }
                // An install's error is kept whole up to 120 characters, so the download lines reach these notes with their fix.
                string wake = Engine.Stalled("her wake word"), wakeLine = WakeLine(Voice.Short(wake, 120), 30);
                if (Voice.Short(Engine.Stalled("her ears"), 120) != Engine.Stalled("her ears") || Voice.Short(new string('x', 200), 120).Length != 120
                    || wakeLine != "\"Hey Capri\" didn't download: The download of her wake word keeps stopping. Check your connection. I'll try again in 30 seconds.")
                    throw new Exception("A download line lost its fix: " + wakeLine);
                checks.Add("PASS her ears' and Hey Capri's download lines keep the reason and its fix whole, said once");
                if (HearingStatus.Reason("Your mic stopped sending sound partway through a sentence, so the rest went missing completely") != "Your mic stopped sending sound partway through a..." || HearingStatus.Reason("") != "No words came back")
                    throw new Exception("A long hearing reason was cut mid-word or without \"...\": " + HearingStatus.Reason("Your mic stopped sending sound partway through a sentence, so the rest went missing completely"));
                if (Dictation.Explain(new IOException("The text associated with this error code could not be found.")) != Dictation.NotStarted || !Dictation.Logged(new IOException("The text associated with this error code could not be found.")).Contains("could not be found"))
                    throw new Exception("Windows speech's own error words reached people or left errors.txt.");
                // Said in the bubble and put under VOICE in full, with its fix, while the hearing line keeps the short reason.
                hearingDevices = delegate { return new List<Mic> { new Mic("held", "Held Mic") }; };
                hearingDefault = delegate { return "held"; };
                hearingOutput = delegate { return "Speakers"; };
                hearingPrivacy = delegate { return false; };
                hearingHandsFree = delegate { return false; };
                RefreshHearingDevices();
                CloseTalk();
                hearingSaid = null;
                HearingCaptureFailed(held, true);
                bool told = reply == held && mainframe.VoiceNoteText == held && mainframe.HearingText == "Mic failed: another app has it to itself" && mainframe.HearingWarning && mainframe.HearingButtons == "Open Sound settings | Type instead";
                string seen = reply + " / " + mainframe.VoiceNoteText + " / " + mainframe.HearingText;
                HearingCaptureFailed(null, false);
                if (!told) throw new Exception("A mic another app holds wasn't told in full with its fix: " + seen);
                checks.Add("PASS hearing reasons: a mic another app holds or Windows won't open says so in plain words with its fix, with WASAPI's step and code only in errors.txt; Windows speech's own errors and status names stay in errors.txt too; each hearing line is a plain reason that fits, ending at a colon, comma or sentence, or at a word with \"...\", never mid-word, with no doubled \"Windows speech\"; and her bubble and VOICE get the whole reason with its fix while the line stays short");
            }
            finally
            {
                Brain.LogPath = logWas;
                hearingDevices = devicesWas; hearingDefault = defaultWas; hearingOutput = outputWas; hearingPrivacy = privacyWas; hearingHandsFree = handsFreeWas;
                hearingCaptureProblem = null;
                hearingSaid = null;
                CloseTalk();
                RefreshHearingDevices();
                mainframe.VoiceNote("", false);
            }
        }

        // What she says and shows of a tool result leaves out her brain's own instructions and tool names, like "Call ask and
        // say..." or "list_windows names its program", while her brain still gets every word. A guard rail in a job shows them
        // a plain line of its own in the task panel and in "Hit a snag: ...".
        private static void SmokeBrainOnlyText(List<string> checks, string output)
        {
            string[,] spoken =
            {
                { "notes.txt is a text file. More text is available: call read_file with the same path and offset 24000.", "notes.txt is a text file. More text is available." },
                { "plan.pdf is a PDF. Starting at page 1. More pages are available: call read_file with the same path and page 2 (offset 0).", "plan.pdf is a PDF. Starting at page 1. More pages are available." },
                { "Couldn't drag: it needs start_coordinate and coordinate on the screenshot.", "Couldn't drag." },
                { "Couldn't change Spotify's volume: it isn't open, or it has another name in the mixer: list_windows names its program.", "Couldn't change Spotify's volume: it isn't open, or it has another name in the mixer." },
                { "There's no tool called fly.", "That didn't work." },
                { "That didn't work: There's no tool called fly.", "That didn't work." },
                { "There's no app_volume action called louder.", "That didn't work." },
                { "There's no window action called explode.", "That didn't work." },
                { "That didn't work: that media call was missing what it needs.", "That didn't work." },
                { "Use call_errand for the call's choice.", "That didn't work." },
                { "Call mode is on. Use call_say for their message and call_errand for the call's choice.", "Call mode is on." },
                { "Opened Spotify. " + Computer.NoCard, "Opened Spotify. That didn't work: they put in card details themselves." },
                { Computer.NoCart, "That didn't work: nothing went in the cart, because their job didn't ask for that." },
                { "Opened Spotify.", "Opened Spotify." },
                { "Called Jake on Discord, and the call's ringing.", "Called Jake on Discord, and the call's ringing." },
                { "Opened read_file.txt in Notepad.", "Opened read_file.txt in Notepad." },
                { "Spotify is already at 100% in the volume mixer. Its own volume slider inside the app, or the PC's volume, can still go up.", "Spotify is already at 100% in the volume mixer. Its own volume slider inside the app, or the PC's volume, can still go up." },
                { "", "" }
            };
            for (int i = 0; i < spoken.GetLength(0); i++)
                if (Hands.Shown(spoken[i, 0]) != spoken[i, 1]) throw new Exception("A tool result said the wrong part: \"" + spoken[i, 0] + "\" became \"" + Hands.Shown(spoken[i, 0]) + "\".");
            string folder = Path.Combine(output, "brain-only-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string longFile = Path.Combine(folder, "long notes.txt");
            File.WriteAllText(longFile, new string('a', Files.Excerpt + 6000));
            ReadFile part = Files.Open(longFile, null);
            string partSaid = Hands.Shown(part.Line);
            if (part.Line.IndexOf("call read_file with the same path and offset", StringComparison.Ordinal) < 0 || partSaid != "long notes.txt is a text file. More text is available.")
                throw new Exception("A long file's read lost its next offset for her brain, or said read_file aloud: " + part.Line + " / " + partSaid);
            checks.Add("PASS what she says of a tool result leaves out her brain's own instructions and tool names (call read_file, list_windows, start_coordinate, call_errand, a tool or action that doesn't exist), keeps what stands alone before them, says \"That didn't work.\" when nothing does, and leaves file names, phone calls and plain results alone; her brain still gets the whole line");

            Func<Sight> lookWas = Computer.Look;
            Func<string, ToolArguments, Sight, string> actWas = Computer.Act;
            Func<IntPtr> frontWas = Computer.Front;
            string securityWas = Security.Saved;
            try
            {
                // Type passwords and card numbers off, the way most people have it, so the card box is theirs.
                Security.Load(securityWas.Substring(0, Security.Secrets) + "0" + securityWas.Substring(Security.Secrets + 1));
                Sight shop = new Sight { Window = new IntPtr(7), Title = "Blue hoodie - Shop", Program = "chrome" };
                shop.Words.Add("We use cookies to give you the best experience.");
                string[] names = { "Add to cart", "Accept all", "Reject all", "Card number", "Search the shop", "Full name" };
                for (int i = 0; i < names.Length; i++) shop.Targets.Add(new Target { Number = i + 1, Role = i < 3 ? "button" : "box", Name = names[i], Line = i >= 3, Box = new Rectangle(10, 10 + 30 * i, 120, 20) });
                Computer.Look = delegate { return shop; };
                Computer.Front = delegate { return new IntPtr(7); };
                int acts = 0;
                Computer.Act = delegate(string name, ToolArguments a, Sight s) { acts++; return "Did it."; };
                string[] script =
                {
                    "click{\"target\": \"1\"}", "click{\"target\": \"2\"}", "type{\"target\": \"4\", \"text\": \"4111 1111 1111 1111\"}", "type{\"target\": \"5\", \"text\": \"[Order Number]\"}",
                    "type{\"target\": \"5\", \"text\": \"their email address\"}", "type{\"target\": \"5\", \"text\": \"$order\"}", "type{\"target\": \"6\", \"text\": \"Sam Smith\"}", "done{\"text\": \"Had a look.\"}"
                };
                int line = 0;
                Job looking = new Job("have a look at the blue hoodie");
                looking.Think = delegate(string system, string user, List<KeyValuePair<string, ToolArguments>> calls) { return script[Math.Min(line++, script.Length - 1)]; };
                List<string> failed = new List<string>();
                looking.Stepped = delegate(JobStep step) { if (step.Finished && step.Failed) failed.Add(step.Label); };
                looking.Run();
                string[] shown = { Computer.NoCartShown, "I don't accept cookies for you", Computer.NoCardShown, "\"[Order Number]\" is a blank, not the real words", "\"their email address\" isn't the real thing", "I had no note called order", Computer.NotGivenShown };
                string[] brainGot = { Computer.NoCart, "Click [3] button \"Reject all\" instead.", Computer.NoCard, "Type the real text", "Call ask: tell them to sign in themselves", "Call note first, then write it.", Computer.NotGiven };
                bool right = acts == 0 && failed.Count == shown.Length && looking.Steps.Count >= shown.Length;
                for (int i = 0; right && i < shown.Length; i++)
                    // What she nearly typed is quoted as it was; her own words around it talk to them as "you".
                    right = failed[i] == shown[i] && Hands.Shown(failed[i]) == failed[i] && !System.Text.RegularExpressions.Regex.IsMatch(System.Text.RegularExpressions.Regex.Replace(failed[i], "\"[^\"]*\"", ""), @"\b(they|their|them|call)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                        && looking.Steps[i].IndexOf(brainGot[i], StringComparison.Ordinal) >= 0;
                string snag = Asides.Status(looking.Report(), "how's it going?");
                if (!right || snag.IndexOf(Computer.NotGivenShown, StringComparison.Ordinal) < 0 || snag.IndexOf("Call ask", StringComparison.Ordinal) >= 0)
                    throw new Exception("A guard rail showed her brain's instructions, or her brain lost them: " + snag + " / " + acts + " acts / " + string.Join(" | ", failed.ToArray()) + " / " + string.Join(" | ", looking.Steps.ToArray()));
                checks.Add("PASS a job's guard rails (no cart, no card, no cookies, a blank, a stand-in, a note she never made, a detail her job didn't give) show and say a plain line to you in the task panel and \"Hit a snag\", while her brain still gets what to do next");
            }
            finally
            {
                Computer.Look = lookWas;
                Computer.Act = actWas;
                Computer.Front = frontWas;
                Security.Load(securityWas);
            }

            Dictionary<string, object> asks = HuskHook.Adapt(HuskJson.Map("hook_event_name", "PermissionRequest", "session_id", "s1", "tool_name", "exec_command", "tool_input", HuskJson.Map("cmd", "git  status")), "codex");
            Dictionary<string, object> patches = HuskHook.Adapt(HuskJson.Map("hook_event_name", "PermissionRequest", "session_id", "s1", "tool_name", "apply_patch", "tool_input", HuskJson.Map("command", "*** Begin Patch")), "codex");
            Dictionary<string, object> described = HuskHook.Adapt(HuskJson.Map("hook_event_name", "PermissionRequest", "session_id", "s1", "tool_name", "exec_command", "tool_input", HuskJson.Map("description", "Delete the build folder?")), "codex");
            if (HuskJson.Text(asks, "message") != "It wants to run git status" || HuskJson.Text(patches, "message") != "It wants to change files." || HuskJson.Text(described, "message") != "Delete the build folder?"
                || HuskHook.Wants("exec_command", HuskJson.Map()) != "It wants to run a command." || HuskHook.Wants("", HuskJson.Map()) != "")
                throw new Exception("Codex's approval named its own tool: " + HuskJson.Text(asks, "message") + " / " + HuskJson.Text(patches, "message") + " / " + HuskJson.Text(described, "message"));
            checks.Add("PASS Codex asking for your OK says what it wants to do, like \"It wants to run git status\", instead of \"Codex needs approval: exec_command\", and keeps its own description when it gives one");
        }

        // Jobs flag 3: tool results call the person "they" for her brain, and what people see or hear says "you".
        private static void SmokeShownToYou(List<string> checks)
        {
            string[,] spoken =
            {
                { "Won't type in a password box. They type passwords themselves.", "Won't type in a password box. Type the password yourself." },
                { "Couldn't check their internet just now.", "Couldn't check your internet just now." },
                { "Their PC isn't connected to any network: no Ethernet or Wi-Fi is up.", "Your PC isn't connected to any network: no Ethernet or Wi-Fi is up." },
                { "Their internet is working, over Wi-Fi: pings to example.com took 12 ms on average, 9 ms at best, but web addresses don't look up, so sites won't load: their DNS is the problem.", "Your internet is working, over Wi-Fi: pings to example.com took 12 ms on average, 9 ms at best, but web addresses don't look up, so sites won't load: your DNS is the problem." },
                { "Couldn't read how their PC is doing just now.", "Couldn't read how your PC is doing just now." },
                { "Couldn't read their windows just now: no desktop", "Couldn't read your windows just now: no desktop" },
                { "No app windows are open on their PC.", "No app windows are open on your PC." },
                { "No app windows are open on their Mac.", "No app windows are open on your Mac." },
                { "Couldn't read their apps just now: no catalog", "Couldn't read your apps just now: no catalog" },
                { "Their YouTube tab is on Never Fade Away.", "Your YouTube tab is on Never Fade Away." },
                { "Couldn't find their monitors just now.", "Couldn't find your monitors just now." },
                { "Toggled their mic mute in Discord (Ctrl + Shift + M).", "Toggled your mic mute in Discord (Ctrl + Shift + M)." },
                { "Opened their Liked Songs in Spotify, but couldn't press play. Hit the green Play button.", "Opened your Liked Songs in Spotify, but couldn't press play. Hit the green Play button." },
                { "In front: Notepad, on their other screen.", "In front: Notepad, on your other screen." },
                { "You can't see their screen right now.", "I can't see your screen right now." },
                { "You looked at their screen. You see: An open folder.", "On your screen: An open folder." },
                { "You stopped because they asked you something else.", "I stopped because you asked me something else." },
                { "Their PC isn't connected. Call list_windows to read their windows.", "Your PC isn't connected." },
                { "Add credits on their site.", "Add credits on their site." },
                { "They play chess. Ask them about their day.", "They play chess. Ask them about their day." },
                { "Their PCs have their windowsills and their appscripts.", "Their PCs have their windowsills and their appscripts." },
                { "Couldn't read these controls. Check access to their folder.", "Couldn't read these controls. Check access to their folder." },
                { "", "" }
            };
            for (int i = 0; i < spoken.GetLength(0); i++)
                if (Hands.Shown(spoken[i, 0]) != spoken[i, 1]) throw new Exception("A tool result didn't say \"you\": \"" + spoken[i, 0] + "\" became \"" + Hands.Shown(spoken[i, 0]) + "\".");
            checks.Add("PASS tool results people see or hear say \"you\" and \"your\" where her brain's copy says \"they\" and \"their\" (the password box, internet and PC checks, windows, apps, monitors), keep capitals, and leave anyone else's \"their\", like a provider's site, alone");
        }

        // Jobs flag 12: a damaged reminders.json, routines.json or websites.json is kept aside before anything saves over it,
        // she says so once, and each list works again.
        private static void SmokeSavedLists(List<string> checks, string output)
        {
            string folder = Path.Combine(output, "saved-lists-" + Guid.NewGuid().ToString("N")), logWas = Brain.LogPath;
            Directory.CreateDirectory(folder);
            try
            {
                Brain.LogPath = Path.Combine(folder, "errors.txt");
                const string damaged = "[{\"Text\":\"call Mum about the secret\",";
                string[] names = { "reminders", "routines", "websites" }, labels = { "reminders", "routines", "website nicknames" };
                string[] empty = { "You have no reminders waiting.", "You have no saved routines.", "You haven't given me any website nicknames yet." };
                foreach (string name in names) File.WriteAllText(Path.Combine(folder, name + ".json"), damaged);
                DateTime now = new DateTime(2026, 11, 6, 14, 10, 0);
                Reminders lists = new Reminders(folder, now);
                Websites sites = new Websites(folder);
                Func<ToolArguments, string>[] tools = { lists.Remind, lists.Routine, sites.Do };
                ToolArguments[] adds = { new ToolArguments { Text = "stretch", When = "in 20 minutes" }, new ToolArguments { Action = "save", Phrase = "movie", Steps = "open netflix" }, new ToolArguments { Action = "save", Name = "school", Site = "https://example.com/school" } };
                for (int i = 0; i < names.Length; i++)
                {
                    string first = tools[i](new ToolArguments { Action = "list" }), again = tools[i](new ToolArguments { Action = "list" });
                    string told = "Your saved " + labels[i] + " couldn't be read, so I started a new list. The old file is kept as " + names[i] + ".unreadable.json in %LocalAppData%\\Capri.";
                    string kept = Path.Combine(folder, names[i] + ".unreadable.json");
                    if (first != told + "\n" + empty[i] || again != empty[i] || !File.Exists(kept) || File.ReadAllText(kept) != damaged)
                        throw new Exception("A damaged " + names[i] + ".json wasn't kept and told once: " + first + " / " + again);
                    tools[i](adds[i]);
                }
                Reminders reread = new Reminders(folder, now);
                Websites resites = new Websites(folder);
                string[] saved = { reread.Remind(new ToolArguments { Action = "list" }), reread.Routine(new ToolArguments { Action = "list" }), resites.Do(new ToolArguments { Action = "list" }) };
                string[] added = { "stretch", "movie", "school" };
                for (int i = 0; i < names.Length; i++)
                    if (!saved[i].Contains(added[i]) || saved[i].Contains("couldn't be read")) throw new Exception("A list didn't save again after a damaged file: " + saved[i]);
                string log = File.ReadAllText(Brain.LogPath);
                if (log.Contains("secret") || !log.Contains("reminders.json couldn't be read (") || !log.Contains("websites.unreadable.json"))
                    throw new Exception("errors.txt quoted a damaged file, or didn't say what happened: " + log);

                File.WriteAllText(Path.Combine(folder, "reminders.json"), "[{\"Text\":\"call Mum\",\"When\":\"2026-11-07T09:00:00.0000000\",\"Said\":false},{\"Text\":\"\",\"When\":\"soon\",\"Said\":false}]");
                string some = new Reminders(folder, now).Remind(new ToolArguments { Action = "list" });
                if (!some.StartsWith("Some of your saved reminders couldn't be read, so I kept the rest.", StringComparison.Ordinal) || !some.Contains("call Mum"))
                    throw new Exception("A reminders.json with one bad reminder lost the good one or didn't say so: " + some);

                string fresh = Path.Combine(folder, "fresh");
                Directory.CreateDirectory(fresh);
                if (new Reminders(fresh, now).Remind(new ToolArguments { Action = "list" }) != empty[0] || new Websites(fresh).Do(new ToolArguments { Action = "list" }) != empty[2] || Directory.GetFiles(fresh).Length != 0)
                    throw new Exception("A new install's missing lists said something or made files.");
            }
            finally
            {
                Brain.LogPath = logWas;
                try { Directory.Delete(folder, true); } catch (IOException) { }
            }
            checks.Add("PASS a damaged reminders.json, routines.json or websites.json is kept as .unreadable.json before anything saves over it, she says so once in her next reply about it, errors.txt names only the error's kind, a bad reminder keeps the good ones, and each list saves again");
        }

        // The task panel keeps the fix in a long ending or snag: the ending gets five rows and the newest failed step three,
        // and one that still won't fit keeps its first sentence's start and its last sentence before it's ever cut short.
        private static void SmokeJobPanelFixes(List<string> checks)
        {
            const int room = TaskPanel.Columns - 2;
            string admin = Computer.AdminWindow.TrimEnd('.');
            List<string> snag = TaskPanel.Fitted("", admin, room, TaskPanel.SnagRows);
            List<string> lost = TaskPanel.Fitted("CAPRI: ", Computer.Confused, TaskPanel.Columns, TaskPanel.EndingRows);
            List<string> plain = TaskPanel.Fitted("", "Couldn't find the Send button", room, TaskPanel.SnagRows);
            List<string> endless = TaskPanel.Fitted("", "Couldn't do that because " + string.Join(" and ", new[] { "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen" }), room, TaskPanel.SnagRows);
            bool fits = true;
            foreach (List<string> each in new[] { snag, lost, plain, endless }) foreach (string row in each) if (Look.Cells(row) > (each == lost ? TaskPanel.Columns : room)) fits = false;
            string snagText = string.Join(" ", snag.ToArray()), lostText = string.Join(" ", lost.ToArray());
            if (!fits || snag.Count > TaskPanel.SnagRows || !snagText.EndsWith(Computer.AsAdmin, StringComparison.Ordinal) || !snagText.StartsWith("Couldn't click or type there", StringComparison.Ordinal)
                || lost.Count > TaskPanel.EndingRows || !lost[0].StartsWith("CAPRI: Got lost", StringComparison.Ordinal) || !lostText.EndsWith("Pick one under BRAIN in Capri Mainframe.", StringComparison.Ordinal)
                || plain.Count != 1 || plain[0] != "Couldn't find the Send button" || endless.Count != TaskPanel.SnagRows || !endless[TaskPanel.SnagRows - 1].EndsWith("...", StringComparison.Ordinal))
                throw new Exception("The task panel cut a fix off: " + snagText + " / " + lostText + " / " + string.Join(" ", plain.ToArray()) + " / " + string.Join(" ", endless.ToArray()));
            List<JobStep> shortSnag = new List<JobStep> { new JobStep { Tool = "click", Label = "Opened Task Manager", Finished = true }, new JobStep { Tool = "click", Label = "Couldn't click there", Finished = true, Failed = true } };
            List<JobStep> longSnag = new List<JobStep> { shortSnag[0], new JobStep { Tool = "click", Label = admin, Finished = true, Failed = true } };
            List<JobStep> olderSnag = new List<JobStep> { longSnag[1], new JobStep { Tool = "click", Label = "Couldn't click there", Finished = true, Failed = true } };
            int shortHigh, longHigh, olderHigh, endHigh, doneHigh;
            using (Bitmap frame = TaskPanel.Frame("end the task in task manager", shortSnag, null, "failed", "Couldn't get it done.", false, 1, 1)) shortHigh = frame.Height;
            using (Bitmap frame = TaskPanel.Frame("end the task in task manager", longSnag, null, "failed", "Couldn't get it done.", false, 1, 1)) longHigh = frame.Height;
            using (Bitmap frame = TaskPanel.Frame("end the task in task manager", olderSnag, null, "failed", "Couldn't get it done.", false, 1, 1)) olderHigh = frame.Height;
            using (Bitmap frame = TaskPanel.Frame("end the task in task manager", shortSnag, null, "failed", Computer.Confused, false, 1, 1)) endHigh = frame.Height;
            using (Bitmap frame = TaskPanel.Frame("end the task in task manager", shortSnag, null, "done", "", false, 1, 1)) doneHigh = frame.Height;
            if (longHigh - shortHigh != 19 * (snag.Count - 1) || olderHigh != shortHigh || endHigh - doneHigh != 19 * (lost.Count - 1))
                throw new Exception("The task panel didn't make room for a fix: " + shortHigh + " / " + longHigh + " / " + olderHigh + " / " + endHigh + " / " + doneHigh);
            checks.Add("PASS the task panel shows the fix: an ending like \"Got lost before I could start that job...\" reaches \"Pick one under BRAIN in Capri Mainframe.\", the newest failed step wraps to show how to run her as administrator, through \"pick Run as administrator\", older steps stay one row, and one that still won't fit keeps its start and its last sentence before it's cut");
        }

        // Call mode hearing offline is its own choice, so the hearing line and VOICE show it as a grey note, while Windows speech
        // that really failed stays red.
        private void SmokeCallHearing(List<string> checks)
        {
            Func<List<Mic>> devicesWas = hearingDevices;
            Func<string> defaultWas = hearingDefault, outputWas = hearingOutput;
            Func<bool> privacyWas = hearingPrivacy;
            Func<string, string, bool> handsFreeWas = hearingHandsFree;
            string speechWas = hearingSpeechProblem;
            bool offlineWas = hearingOffline, readyWas = earsReady;
            LocalEars ownWas = Hearing.Own;
            try
            {
                hearingDevices = delegate { return new List<Mic> { new Mic("desk", "Desk Mic") }; };
                hearingDefault = delegate { return "desk"; };
                hearingOutput = delegate { return "Speakers"; };
                hearingPrivacy = delegate { return false; };
                hearingHandsFree = delegate { return false; };
                RefreshHearingDevices();
                CloseTalk();
                hearingSaid = null;
                ListenOffline(CallHearing);
                string callLine = mainframe.HearingText, callNote = mainframe.VoiceNoteText;
                bool callGrey = callLine == CallHearingLine && !mainframe.HearingWarning && callNote.EndsWith(CallHearing, StringComparison.Ordinal) && !mainframe.VoiceNoteWarning;
                CloseTalk();
                hearingSaid = null;
                ListenOffline(Dictation.NotStarted);
                string failedLine = mainframe.HearingText;
                bool failedRed = failedLine.StartsWith("Windows speech didn't start", StringComparison.Ordinal) && mainframe.HearingWarning && mainframe.VoiceNoteWarning;
                if (!callGrey || !failedRed || callLine.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new Exception("Call mode's offline hearing read as a failure, or a real one didn't: " + callLine + " / " + callNote + " / " + failedLine + " / link " + HearingLink);
                checks.Add("PASS call mode hearing offline shows \"" + CallHearingLine + "\" in grey with its reason in grey under VOICE, and a Windows speech failure is still red");
            }
            finally
            {
                hearingDevices = devicesWas; hearingDefault = defaultWas; hearingOutput = outputWas; hearingPrivacy = privacyWas; hearingHandsFree = handsFreeWas;
                hearingSpeechProblem = speechWas;
                hearingOffline = offlineWas;
                earsReady = readyWas;
                Hearing.Own = ownWas;
                hearingSaid = null;
                CloseTalk();
                RefreshHearingDevices();
                mainframe.VoiceNote("", false);
            }
        }

        // Too many requests mentions glancing less often only while Watch my screen is on, and Gemini's used-up day says it
        // resets tomorrow or to use another key, with short BRAIN lines that keep the fix.
        private void SmokeRateLimits(List<string> checks)
        {
            const string minute = "{\"error\":{\"code\":429,\"message\":\"You exceeded your current quota.\",\"status\":\"RESOURCE_EXHAUSTED\",\"details\":[{\"@type\":\"type.googleapis.com/google.rpc.QuotaFailure\",\"violations\":[{\"quotaId\":\"GenerateRequestsPerMinutePerProjectPerModel-FreeTier\"}]}]}}";
            const string day = "{\"error\":{\"code\":429,\"message\":\"You exceeded your current quota, please check your plan and billing details.\",\"status\":\"RESOURCE_EXHAUSTED\",\"details\":[{\"@type\":\"type.googleapis.com/google.rpc.QuotaFailure\",\"violations\":[{\"quotaId\":\"GenerateRequestsPerDayPerProjectPerModel-FreeTier\"}]}]}}";
            if (Brain.Meaning(429, day, true) != Brain.DailyGone || Brain.Meaning(429, minute, true) != Brain.Busy || Brain.Meaning(429, day) != Brain.Busy || Brain.Meaning(429, "{\"status\":429,\"title\":\"Too Many Requests\"}") != Brain.Busy
                || !Brain.DailyGone.Contains("tomorrow") || !Brain.DailyGone.Contains("another key") || Brain.Busy.Contains("watch") || !Brain.BusyWatching.Contains("Watch my screen"))
                throw new Exception("A rate limit or Gemini's used-up day read wrong: " + Brain.Meaning(429, day, true) + " / " + Brain.Meaning(429, minute, true));
            foreach (string line in new[] { Brain.Busy, Brain.BusyWatching, Brain.DailyGone })
            {
                string brief = Brain.Brief(line) ?? line;
                if (brief.Length > 60 || !Brain.Stalled(new BrainException(line, true))) throw new Exception("A rate limit's BRAIN line didn't fit or stopped counting as a stall: " + brief);
            }
            if (!Brain.Brief(Brain.DailyGone).Contains("tomorrow") || !Brain.Brief(Brain.BusyWatching).Contains("glance")) throw new Exception("A rate limit's BRAIN line lost its fix.");
            bool watchWas = preferences.Watch;
            string googleWas = Brain.Google;
            System.Net.Sockets.TcpListener fake = null;
            try
            {
                List<string> asked = new List<string>();
                Brain.Google = FakeServer(new[] { "!429 " + minute, "!429 " + minute, "!429 " + day }, asked, out fake);
                string[] said = new string[3];
                for (int i = 0; i < 3; i++)
                {
                    preferences.Watch = i == 1;
                    try { Brain.CloudModels("AIza-test"); }
                    catch (BrainException error) { if (error.Told) said[i] = error.Message; }
                }
                if (said[0] != Brain.Busy || said[1] != Brain.BusyWatching || said[2] != Brain.DailyGone || asked.Count != 3)
                    throw new Exception("Gemini's limits were told wrong: " + string.Join(" | ", said) + " / " + asked.Count + " asks");
                checks.Add("PASS too many requests says to wait a minute, and to glance less often only while Watch my screen is on; Gemini's used-up day says it resets tomorrow or to add another key; each has a BRAIN line that fits with its fix, and all still count as a stall");
            }
            catch (System.Net.Sockets.SocketException) { checks.Add("LIMIT rate limit lines: loopback sockets unavailable"); }
            finally
            {
                preferences.Watch = watchWas;
                Brain.Google = googleWas;
                if (fake != null) fake.Stop();
            }
        }

        // Thinking: which levels each model takes, what each pick sends, Extra request JSON, a level a model turns down, and her rows.
        private void SmokeThinking(List<string> checks)
        {
            const int off = ThinkingLevels.Off, low = ThinkingLevels.Low, medium = ThinkingLevels.Medium, high = ThinkingLevels.High;
            object[][] table =
            {
                new object[] { "openai", "gpt-6-astra", off, true }, new object[] { "openai", "gpt-6.1-sol", off, true }, new object[] { "openai", "gpt-6.1-terra", off, false }, new object[] { "openai", "gpt-5.6", off, false },
                new object[] { "openai", "gpt-5", off, true }, new object[] { "openai", "gpt-5.5", off, false }, new object[] { "openai", "o3", off, true }, new object[] { "openai", "gpt-5.5-pro", low, true }, new object[] { "openai", "gpt-5.5-pro", high, false }, new object[] { "openai", "gpt-4.1", high, true },
                new object[] { "anthropic", "claude-opus-5-5", off, true }, new object[] { "anthropic", "claude-fable-5-1", off, true }, new object[] { "anthropic", "claude-sonnet-5-5", off, true }, new object[] { "anthropic", "claude-haiku-5-5", off, false }, new object[] { "anthropic", "claude-opus-5", off, false }, new object[] { "anthropic", "claude-opus-5-5", high, false }, new object[] { "anthropic", "claude-haiku-4-5", high, true }, new object[] { "anthropic", "claude-haiku-4-5", off, true },
                new object[] { "google", "gemini-2.5-flash-lite", off, false }, new object[] { "google", "gemini-2.5-pro", off, true }, new object[] { "google", "gemini-3.1-flash-lite", off, true }, new object[] { "google", "gemini-3.1-flash-lite", high, false },
                new object[] { "deepseek", "deepseek-flash", off, false }, new object[] { "deepseek", "deepseek-flash", medium, true }, new object[] { "deepseek", "deepseek-flash", high, false },
                new object[] { "kimi", "kimi-k3.1-pro", off, true }, new object[] { "kimi", "kimi-k3", medium, true }, new object[] { "kimi", "kimi-k3", low, false }, new object[] { "kimi", "kimi-k2.6", off, false }, new object[] { "kimi", "kimi-k2.6", low, true }, new object[] { "kimi", "kimi-k2.7-code", high, true }, new object[] { "kimi", "kimi-k9", high, true },
                new object[] { "nvidia", "nvidia/nemotron-3.5-super-vl-120b-a12b", medium, true }, new object[] { "nvidia", "nvidia/nemotron-3.5-super-vl-120b-a12b", off, false }, new object[] { "nvidia", "nvidia/nemotron-3.5-bolt-e4b", low, true }, new object[] { "nvidia", "nvidia/nemotron-3.5-bolt-e4b", high, false }, new object[] { "nvidia", "nvidia/nemotron-3-nano-omni-30b-a3b-reasoning", medium, false }, new object[] { "nvidia", "openai/gpt-oss-20b", off, true }, new object[] { "nvidia", "openai/gpt-oss-20b", medium, false }, new object[] { "nvidia", "qwen/qwen3-235b-a22b", high, true },
                new object[] { "local", "gemma4:e4b", high, true }, new object[] { "claudecode", "opus", high, true }
            };
            foreach (object[] row in table)
                if ((ThinkingLevels.Why((string)row[0], (string)row[1], (int)row[2], false) != null) != (bool)row[3]) throw new Exception("Thinking read " + row[0] + " " + row[1] + " " + ThinkingLevels.Names[(int)row[2]] + " wrong.");
            if (ThinkingLevels.Why("anthropic", "claude-haiku-5-5", high, true) == null || ThinkingLevels.Why("openai", "gpt-5.6", ThinkingLevels.Default, false) != null) throw new Exception("Thinking opened a Capri code or greyed Default.");
            CloudModel router = new CloudModel { ReasoningRead = true, Reasoning = new CloudModelReasoning { Mandatory = true, Efforts = new[] { "max", "high", "low", "none" } } };
            if (ThinkingLevels.Why("openrouter", "a/b", off, false, router) == null || ThinkingLevels.Why("openrouter", "a/b", medium, false, router) != "Low or High only" || ThinkingLevels.Why("openrouter", "a/b", high, false, router) != null
                || ThinkingLevels.Why("openrouter", "a/b", high, false, new CloudModel { ReasoningRead = true }) == null || ThinkingLevels.Why("openrouter", "a/b", high, false, new CloudModel()) != null || ThinkingLevels.Why("openrouter", "a/b", high, false, null) != null)
                throw new Exception("Thinking misread OpenRouter's model list.");
            if (Brain.ReadCloudModels("nvidia", "{\"data\":[{\"id\":\"meta/llama-3.3-70b-instruct\",\"reasoning\":true}]}").Count != 1) throw new Exception("A reasoning field in another shape lost the model list.");
            if (Brain.ReadCloudModels("openrouter", "{\"data\":[{\"id\":\"example/think\",\"architecture\":{\"input_modalities\":[\"text\"],\"output_modalities\":[\"text\"]},\"supported_parameters\":[\"reasoning\"],\"reasoning\":true}]}").Count != 1) throw new Exception("OpenRouter's reasoning in another shape lost its model list.");
            string merged = ThinkingLevels.Merge("{\"a\":1,\"model\":\"m\",\"reasoning_effort\":\"low\"}", "{\"reasoning_effort\":\"high\",\"model\":\"no\",\"b\":{\"c\":[1,\"}\"]}}");
            if (merged != "{\"a\":1,\"model\":\"m\",\"reasoning_effort\":\"high\",\"b\":{\"c\":[1,\"}\"]}}") throw new Exception("Extra JSON merged wrong: " + merged);
            foreach (string bad in new[] { "[1]", "{\"a\":1,}", "{\"a\":tru}", "{\"a\":1} x", "{a:1}", "{\"model\":\"x\"}", "{\"mod\\u0065l\":\"x\"}", "{\"tools\":[]}", "{\"stream\":true}", "{\"system\":\"x\"}", "{\"messages\":[]}" })
                if (ThinkingLevels.ExtraProblem(bad) == null) throw new Exception("Extra JSON let through: " + bad);
            if (ThinkingLevels.ExtraProblem("") != null || ThinkingLevels.ExtraProblem(" {\"temperature\":1.5e0,\"x\":[true,null,{\"y\":\"\\n\"}]}\n") != null) throw new Exception("Extra JSON turned down a good object.");
            CompletionRequest kimi = new CompletionRequest();
            ThinkingLevels.Apply(kimi, "kimi-k2.6", off);
            CompletionRequest kimiNext = new CompletionRequest();
            ThinkingLevels.Apply(kimiNext, "kimi-k3.1-pro", low);
            if (kimi.Thinking == null || kimi.Thinking.Type != "disabled" || kimi.Effort != null || kimiNext.Effort != "low" || kimiNext.Thinking != null || !ThinkingLevels.IsKimi("https://api.moonshot.ai/v1") || ThinkingLevels.IsKimi("https://example.com/api.moonshot.ai")) throw new Exception("Kimi's Thinking setting went wrong.");
            string[] addresses = { Brain.OpenAi, Brain.Anthropic, Brain.DeepSeek, Brain.Nvidia, Brain.Google, Brain.OpenRouter };
            int repliesWas = Brain.ReplyLength;
            Cloud remoteWas = brain.Remote;
            string modelWas = brain.Model;
            ThinkingPick[] picksWas = brain.Picks, savedWas = preferences.ThinkingPicks;
            System.Net.Sockets.TcpListener fake = null;
            Func<string, string, int, string, string[], string> ask = delegate(string key, string model, int level, string extra, string[] replies)
            {
                List<string> asked = new List<string>();
                string address = FakeServer(replies, asked, out fake);
                Brain.OpenAi = Brain.Anthropic = Brain.DeepSeek = Brain.Nvidia = Brain.Google = Brain.OpenRouter = address;
                Cloud remote = new Cloud(key, model);
                Brain asking = new Brain { Model = model, Remote = remote };
                if (level != ThinkingLevels.Default || extra != null) asking.Picks = new[] { new ThinkingPick { Source = remote.Kind + "|" + model, Level = level, Extra = extra } };
                try { asking.Step("Be brief.", "Hi", "[]", "[]", "", new List<KeyValuePair<string, ToolArguments>>()); }
                catch (BrainException error) { asked.Add("failed: " + error.Message); }
                fake.Stop();
                return string.Join("\n---\n", asked.ToArray());
            };
            string fine = LocalReply("ok"), claudeFine = "{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]}";
            Action<string, string, int, string> sends = delegate(string key, string model, int level, string wanted)
            {
                string said = ask(key, model, level, null, new[] { key.StartsWith("sk-ant", StringComparison.Ordinal) ? claudeFine : fine });
                if (said.IndexOf(wanted, StringComparison.Ordinal) < 0) throw new Exception(model + " at " + ThinkingLevels.Names[level] + " didn't send " + wanted + ": " + said);
            };
            try
            {
                Brain.ReplyLength = 0;
                string deepSeek = "sk-" + new string('0', 32);
                sends(deepSeek, "deepseek-flash", ThinkingLevels.Default, "\"thinking\":{\"type\":\"disabled\"}");
                sends(deepSeek, "deepseek-flash", off, "\"thinking\":{\"type\":\"disabled\"}");
                sends(deepSeek, "deepseek-flash", high, "\"reasoning_effort\":\"high\"");
                if (ask(deepSeek, "deepseek-flash", high, null, new[] { fine }).Contains("\"thinking\"")) throw new Exception("DeepSeek's High still sent thinking disabled.");
                sends(deepSeek, "deepseek-flash", medium, "\"thinking\":{\"type\":\"disabled\"}");
                sends("sk-proj-test", "gpt-5.6", high, "\"reasoning_effort\":\"high\"");
                sends("sk-proj-test", "gpt-5.6", high, "\"max_completion_tokens\":8000");
                sends("sk-proj-test", "gpt-6-luna", high, "\"reasoning_effort\":\"none\"");
                sends("sk-ant-test", "claude-opus-5-5", ThinkingLevels.Default, "\"output_config\":{\"effort\":\"low\"}");
                sends("sk-ant-test", "claude-opus-5-5", high, "\"output_config\":{\"effort\":\"high\"}");
                sends("sk-ant-test", "claude-haiku-5-5", off, "\"thinking\":{\"type\":\"disabled\"}");
                sends("AIza-test", "gemini-3.1-flash-lite", ThinkingLevels.Default, "\"reasoning_effort\":\"low\"");
                sends("AIza-test", "gemini-2.5-flash", off, "\"reasoning_effort\":\"none\"");
                sends("sk-or-test", "a/b", low, "\"reasoning\":{\"effort\":\"low\"}");
                sends("nvapi-test", "nvidia/nemotron-3.5-bolt-e4b", high, "\"chat_template_kwargs\":{\"enable_thinking\":true}");
                sends("nvapi-test", "nvidia/nemotron-3-nano-omni-30b-a3b-reasoning", low, "\"chat_template_kwargs\":{\"enable_thinking\":true,\"reasoning_budget\":1024}");
                sends("nvapi-test", "nvidia/nemotron-3.5-super-vl-120b-a12b", off, "\"reasoning_effort\":\"none\"");
                string extended = ask("sk-proj-test", "gpt-5.6", ThinkingLevels.Default, "{\"reasoning_effort\":\"xhigh\",\"service_tier\":\"flex\"}", new[] { fine });
                if (!extended.Contains("\"reasoning_effort\":\"xhigh\"") || !extended.Contains("\"service_tier\":\"flex\"") || !extended.Contains("\"tools\":[]") || extended.IndexOf("\"reasoning_effort\"", StringComparison.Ordinal) != extended.LastIndexOf("\"reasoning_effort\"", StringComparison.Ordinal)) throw new Exception("Extra JSON didn't reach the request once: " + extended);
                const string refused = "!400 {\"error\":{\"message\":\"That setting isn't available.\"}}";
                string retried = ask("sk-proj-test", "gpt-90", high, null, new[] { refused, fine });
                string[] tries = retried.Split(new[] { "\n---\n" }, StringSplitOptions.None);
                if (tries.Length != 2 || !tries[0].Contains("\"reasoning_effort\":\"high\"") || tries[1].Contains("\"reasoning_effort\"") || retried.Contains("failed:") || ThinkingLevels.Why("openai", "gpt-90", high, false) == null || ThinkingLevels.Why("openai", "gpt-90", low, false) != null) throw new Exception("A level a model turned down failed the chat or stayed open: " + retried);
                string unforgiven = ask("sk-proj-test", "gpt-91", high, "{\"x\":1}", new[] { refused, fine });
                if (!unforgiven.Contains("failed:") || unforgiven.Contains("\n---\nPOST")) throw new Exception("Extra JSON asked again without itself: " + unforgiven);
                List<ChatMessage> kimiChat = new List<ChatMessage> { new ChatMessage("system", "Be brief."), new ChatMessage("user", "Hi") };
                List<string> kimiAsked = new List<string>();
                string kimiAddress = FakeServer(new[] { refused, fine }, kimiAsked, out fake);
                string kimiSaid = new Brain().Complete(kimiAddress, "kimi-k3", kimiChat, false, new List<KeyValuePair<string, ToolArguments>>(), null, 90, -1, false, null, false, "kimi-k3", null, new ThinkingPick { Source = "kimi|kimi-k3", Level = low });
                fake.Stop();
                if (kimiSaid != "ok" || kimiAsked.Count != 2 || !kimiAsked[0].Contains("\"reasoning_effort\":\"low\"") || kimiAsked[1].Contains("reasoning_effort") || ThinkingLevels.Why("kimi", "kimi-k3", low, false) == null) throw new Exception("Kimi didn't turn a level down without failing the chat.");
                kimiAsked.Clear();
                Brain.ReplyLength = 1;
                kimiAddress = FakeServer(new[] { fine }, kimiAsked, out fake);
                new Brain().Complete(kimiAddress, "kimi-k3.1-pro", kimiChat, false, new List<KeyValuePair<string, ToolArguments>>(), null, 90, -1, false, null, false, "kimi-k3.1-pro", null, new ThinkingPick { Source = "kimi|kimi-k3.1-pro", Level = high, Extra = "{\"x\":1}" });
                fake.Stop();
                Brain.ReplyLength = 0;
                if (kimiAsked.Count != 1 || !kimiAsked[0].Contains("\"reasoning_effort\":\"high\"") || !kimiAsked[0].Contains("\"x\":1") || !kimiAsked[0].Contains("\"max_tokens\":8000")) throw new Exception("Kimi's High lost its setting, its extra JSON or its room under Max reply: " + kimiAsked[0]);
                string learnsNothing = ask("sk-proj-test", "gpt-92", ThinkingLevels.Default, "{\"reasoning_effort\":\"minimal\"}", new[] { "!400 {\"error\":{\"message\":\"Unsupported reasoning_effort. Supported values are: 'medium'.\"}}", fine });
                if (!learnsNothing.Contains("failed:") || Brain.Effort("gpt-92", true) != null) throw new Exception("Extra JSON's error changed what Default sends: " + learnsNothing);
                string aside = null;
                List<string> asideAsked = new List<string>();
                Brain.DeepSeek = FakeServer(new[] { fine }, asideAsked, out fake);
                Brain folded = new Brain { Model = "deepseek-flash", Remote = new Cloud(deepSeek, "deepseek-flash"), Picks = new[] { new ThinkingPick { Source = "deepseek|deepseek-flash", Level = high } } };
                aside = folded.Aside("Be brief.", "Hi", 50);
                fake.Stop();
                if (aside != "ok" || !asideAsked[0].Contains("\"thinking\":{\"type\":\"disabled\"}") || asideAsked[0].Contains("reasoning_effort")) throw new Exception("An aside used the Thinking pick.");

                brain.Remote = new Cloud("sk-proj-test", "gpt-6-astra");
                brain.Model = "gpt-6-astra";
                brain.Picks = null;
                mainframe.Sync();
                if (mainframe.ThinkingShown != "Default" || mainframe.ThinkingRow(off) != "Off · always thinks" || mainframe.ThinkingRowOpen(off) || !mainframe.ThinkingRowOpen(high) || !mainframe.ThinkingTip || SelfKnow.Setting("Mainframe.cs", "Thinking") == null || SelfKnow.Setting("Mainframe.cs", "Extra request JSON") == null) throw new Exception("Thinking's row lost a reason, a level, its tip or its help.");
                mainframe.PickThinking(off);
                if (mainframe.ThinkingShown != "Default" || preferences.ThinkingPicks != savedWas) throw new Exception("A greyed level was picked.");
                mainframe.PickThinking(high);
                if (mainframe.ThinkingShown != "High" || preferences.ThinkingPicks == null || preferences.ThinkingPicks.Length != 1 || preferences.ThinkingPicks[0].Source != "openai|gpt-6-astra" || preferences.ThinkingPicks[0].Level != high) throw new Exception("Thinking's pick didn't save for its model.");
                brain.Remote = new Cloud("sk-proj-test", "gpt-5.6");
                brain.Model = "gpt-5.6";
                mainframe.Sync();
                if (mainframe.ThinkingShown != "Default" || !mainframe.ThinkingRowOpen(off) || mainframe.ThinkingExtraKey != "Add" || !mainframe.ThinkingExtraOpen) throw new Exception("Another model kept this model's Thinking.");
                mainframe.OpenThinkingExtra();
                string crowded = mainframe.ThinkingExtraCrowded;
                if (crowded != null) throw new Exception("Extra request JSON: " + crowded);
                mainframe.TypeThinkingExtra("{\"model\":\"x\"}");
                mainframe.SaveThinkingExtra();
                if (!mainframe.ThinkingExtraNote.Contains("model") || brain.ThinkingExtra != null) throw new Exception("Extra JSON saved a name Capri sets.");
                mainframe.TypeThinkingExtra("{\"temperature\":1}");
                mainframe.SaveThinkingExtra();
                if (mainframe.ThinkingExtraNote != "Saved." || brain.ThinkingExtra != "{\"temperature\":1}" || mainframe.ThinkingExtraKey != "Edit") throw new Exception("Extra JSON didn't save.");
                mainframe.CloseThinkingExtra();
                brain.Remote = null;
                brain.Model = "gemma4:e4b";
                mainframe.Sync();
                if (mainframe.ThinkingShown != "Default" || mainframe.ThinkingExtraOpen || mainframe.ThinkingRowOpen(high) || mainframe.ThinkingRow(high) != "High · not for local brains") throw new Exception("A local brain offered Thinking.");
                checks.Add("PASS thinking: each provider and model greys the levels it can't take with a reason, each pick sends that provider's own setting, Default sends what she always did, Extra JSON merges once and can't touch her prompt or tools, a level a model turns down greys out instead of failing the chat, asides stay as they were, and each model keeps its own pick in Settings");
            }
            catch (System.Net.Sockets.SocketException) { checks.Add("LIMIT thinking: loopback sockets unavailable"); }
            finally
            {
                if (fake != null) fake.Stop();
                Brain.OpenAi = addresses[0]; Brain.Anthropic = addresses[1]; Brain.DeepSeek = addresses[2]; Brain.Nvidia = addresses[3]; Brain.Google = addresses[4]; Brain.OpenRouter = addresses[5];
                Brain.ReplyLength = repliesWas;
                ThinkingLevels.Forget();
                brain.Remote = remoteWas;
                brain.Model = modelWas;
                brain.Picks = picksWas;
                preferences.ThinkingPicks = savedWas;
                mainframe.CloseThinkingExtra();
                mainframe.Sync();
            }
        }

        // A cloud voice out of credit says so with its fix, the way her brain reads OpenAI's 429, instead of "busy (HTTP 429)",
        // and the service's own words go to errors.txt. A plain 429 is still busy.
        private static void SmokeVoiceCredit(List<string> checks, string output)
        {
            const string broke = "{\"error\":{\"message\":\"You exceeded your current quota, please check your plan and billing details.\",\"type\":\"insufficient_quota\",\"code\":\"insufficient_quota\"}}";
            const string limited = "{\"error\":{\"message\":\"Rate limit reached for requests per minute.\",\"type\":\"requests\"}}";
            if (VoiceServer.Credit("OpenAI", false) != "OpenAI is out of credit. Add credit at platform.openai.com, or pick another voice under CLOUD VOICE."
                || !VoiceServer.OutOfCredit(429, broke) || !VoiceServer.OutOfCredit(402, "{\"error\":{\"message\":\"Insufficient Balance\"}}") || VoiceServer.OutOfCredit(429, limited) || VoiceServer.OutOfCredit(500, broke))
                throw new Exception("A voice with no credit read wrong: " + VoiceServer.Credit("OpenAI", false));
            string log = Path.Combine(output, "voice-credit.txt"), logWas = Brain.LogPath;
            System.Net.Sockets.TcpListener fake = null;
            try
            {
                if (File.Exists(log)) File.Delete(log);
                Brain.LogPath = log;
                List<string> asked = new List<string>();
                VoiceServer cloud = new VoiceServer();
                cloud.Url = FakeServer(new[] { "!429 " + broke, "!429 " + limited, "!429 " + broke }, asked, out fake);
                cloud.Model = "tts-1";
                cloud.Speaker = "alloy";
                cloud.Key = "sk-voice-test";
                string[] said = new string[2];
                string credited = null;
                for (int i = 0; i < 2; i++)
                {
                    try { cloud.Speak("Hi there."); }
                    catch (VoiceException error) { said[i] = error.Message; if (i == 0) credited = error.Detail; }
                }
                List<string> notes = new List<string>();
                byte[] standIn = Voice.Wave(new short[2400], 24000);
                byte[] filled = cloud.Or(delegate(string words) { return standIn; }, delegate(string problem) { notes.Add(problem); })("Hi there.");
                string logged = File.Exists(log) ? File.ReadAllText(log) : "";
                string wanted = VoiceServer.Credit(VoiceServer.Theirs, true);
                if (said[0] != wanted || credited == null || !credited.Contains("HTTP 429") || !credited.Contains("exceeded your current quota") || said[1] != VoiceServer.Theirs + " is busy (HTTP 429). Try again in a moment."
                    || filled != standIn || notes.Count != 1 || notes[0] != wanted + VoiceServer.Filling || !logged.Contains("exceeded your current quota") || asked.Count != 3)
                    throw new Exception("A cloud voice out of credit was told wrong: " + string.Join(" | ", said) + " / " + credited + " / " + string.Join(" | ", notes.ToArray()) + " / " + asked.Count + " asks");
                checks.Add("PASS a cloud voice out of credit says so with its fix (OpenAI: add credit at platform.openai.com, or pick another voice under CLOUD VOICE) instead of busy, keeps the service's words for errors.txt, and her free voice fills in; a plain 429 is still busy");
            }
            catch (System.Net.Sockets.SocketException) { checks.Add("LIMIT voice credit lines: loopback sockets unavailable"); }
            finally
            {
                Brain.LogPath = logWas;
                if (fake != null) fake.Stop();
            }
        }

        // Her voice files in a folder with special letters, with no plain-named junction to stand in, name a voice that still
        // works, for her free voice and a custom model file alike; any other failure to open them is said as it was.
        private static void SmokeSpecialLetters(List<string> checks)
        {
            string told = LocalVoice.Told(new IOException(LocalVoice.SpecialLetters));
            if (told != LocalVoice.SpecialVoice || !told.EndsWith("A voice under CLOUD VOICE still works.", StringComparison.Ordinal) || !told.StartsWith(LocalVoice.SpecialLetters.Substring(0, 60), StringComparison.Ordinal)
                || LocalVoice.Told(new IOException("Her ears aren't in yet.")) != "Her ears aren't in yet.")
                throw new Exception("The special letters voice line lost its next step: " + told);
            checks.Add("PASS her voice files in a folder with special letters say a voice under CLOUD VOICE still works, for her free voice and a model file, and other file failures keep their words");
        }

        // Wording: brain lines name the service or say "the" instead of "their", download lines start with a capital and drop
        // their "Try again." when she retries herself, and Windows' and .NET's own words become plain lines with theirs in errors.txt.
        private void SmokeWordingFixes(List<string> checks, string output)
        {
            foreach (string line in new[] { Brain.Down, Brain.NoCredits, Brain.Blocked })
                if (System.Text.RegularExpressions.Regex.IsMatch(line, @"\b[Tt]heir\b") || (Brain.Brief(line) ?? line).Length > 60) throw new Exception("A brain line still says their, or its BRAIN note doesn't fit: " + line);
            string folder = Path.Combine(output, "wording-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string log = Path.Combine(folder, "errors.txt"), logWas = Brain.LogPath;
            System.Net.Sockets.TcpListener fake = null;
            try
            {
                Brain.LogPath = log;
                List<string> asked = new List<string>();
                string url = FakeServer(new[] { "too long a reply", "right", "right" }, asked, out fake, 200, 0, false, 0, "application/octet-stream");
                string[] downloads = new string[2];
                for (int i = 0; i < 2; i++)
                {
                    try { Engine.Fetch(url, Path.Combine(folder, "pack" + i + ".bin"), 5, new string('0', 64), "her test pack", delegate { }, delegate { return false; }); }
                    catch (IOException error) { downloads[i] = error.Message; }
                }
                if (downloads[0] != "The download of her test pack came out the wrong size. Try again." || downloads[1] != "The download of her test pack didn't match its checksum. Try again."
                    || Retrying(downloads[0]) != "The download of her test pack came out the wrong size." || Retrying("The download of her wake word keeps stopping. Check your connection and try again.") != "The download of her wake word keeps stopping. Check your connection." || Retrying("Cancelled.") != "Cancelled.")
                    throw new Exception("A download line started in lowercase or told her to try again while she retries: " + string.Join(" | ", downloads));
                string stale = Path.Combine(folder, "stale.bin"), wanted = Path.Combine(folder, "wanted.bin");
                File.WriteAllText(wanted, "right");
                File.WriteAllText(stale + ".part", "wrong");
                Engine.Fetch(url, stale, 5, Engine.Hash(wanted), "her test pack", delegate { }, delegate { return false; });
                if (File.ReadAllText(stale) != "right" || File.Exists(stale + ".part")) throw new Exception("A part left from a different file failed the download instead of starting it over.");
                Exception[] raw =
                {
                    new UnauthorizedAccessException("Access to the path 'C:\\Clips\\a.mp4' is denied."), new IOException("There is not enough space on the disk.", unchecked((int)0x80070070)),
                    new IOException("The process cannot access the file because it is being used by another process.", unchecked((int)0x80070020)), new FileNotFoundException("Could not find file 'C:\\gone.wav'."),
                    new System.ComponentModel.Win32Exception(225), new System.Net.WebException("The remote name could not be resolved: 'api.elevenlabs.io'"), new InvalidOperationException("Collection was modified; enumeration operation may not execute.")
                };
                string[] plain = { "Windows didn't allow it. If your antivirus blocked it, allow Capri there.", "your storage is full. Free up space, then try again.", "another app is using the file. Close it, then try again.", "a file or folder it needs isn't there anymore.", "your antivirus blocked it.", "the connection failed. Check your internet.", "something in Windows failed." };
                for (int i = 0; i < raw.Length; i++)
                {
                    string said = Brain.Plainly("wording", raw[i]);
                    if (said != plain[i] || !File.ReadAllText(log).Contains(raw[i].GetType().Name)) throw new Exception("Windows' own words reached a line or left errors.txt: " + raw[i].Message + " became " + said);
                }
                string vault = SetVault("C:\\notes\u0000here"), update = UpdateProblem(new IOException("The handle is invalid."));
                if (vault != "Couldn't use that: it isn't a folder path Windows can use." || update != "Couldn't start the update: Windows couldn't read or write a file it needs. Capri is still running." || !File.ReadAllText(log).Contains("The handle is invalid."))
                    throw new Exception("A vault or update failure showed Windows' own words: " + vault + " / " + update);
                checks.Add("PASS wording: brain lines say \"the service\" instead of \"their\", download lines start with a capital and drop \"Try again.\" when she retries herself, and Windows' own words for clips, the vault, updates and voice set-up become plain lines with the original in errors.txt");
            }
            catch (System.Net.Sockets.SocketException) { checks.Add("LIMIT wording: loopback sockets unavailable"); }
            finally
            {
                Brain.LogPath = logWas;
                if (fake != null) fake.Stop();
            }
        }

        // The Help page's knowledge base, which the website helper reads too, quotes her lines as she says them now.
        private static void SmokeHelpQuotes(List<string> checks)
        {
            string root = null;
            foreach (string start in new[] { AppDomain.CurrentDomain.BaseDirectory, Environment.CurrentDirectory })
            {
                for (DirectoryInfo folder = new DirectoryInfo(start); folder != null && root == null; folder = folder.Parent)
                    if (File.Exists(Path.Combine(folder.FullName, "site", "help", "knowledge.md"))) root = folder.FullName;
                if (root != null) break;
            }
            if (root == null) { checks.Add("LIMIT help quotes: the source tree isn't beside this executable or working directory"); return; }
            string help = File.ReadAllText(Path.Combine(root, "site", "help", "knowledge.md"));
            string[] current = { Brain.NotAKey, Brain.Busy, Brain.BusyWatching, Brain.Down, Brain.DailyStart, Brain.NoCredits, Ears.Unopened, VoiceServer.Credit("OpenAI", false), LocalVoice.SpecialVoice, CallHearingLine, UnreadableName, SilentNote, "Windows speech didn't start.", "The download of <name> came out the wrong size." };
            string[] old = { "or watch slower", "Their servers failed", "That isn't an NVIDIA, OpenAI, Claude or DeepSeek key.", "Can't open your mic. Pick another under VOICE.", "Pick FREE VOICE or ELEVENLABS to turn it back on", "came down the wrong size", "failed its checksum", "Add credits on their site,", "Pick another row under VOICE" };
            List<string> missing = new List<string>(Array.FindAll(current, line => help.IndexOf(line, StringComparison.Ordinal) < 0));
            List<string> stale = new List<string>(Array.FindAll(old, line => help.IndexOf(line, StringComparison.Ordinal) >= 0));
            if (missing.Count > 0 || stale.Count > 0 || help.IndexOf('—') >= 0)
                throw new Exception("Her help page quotes old wording: missing " + string.Join(" | ", missing.ToArray()) + "; still has " + string.Join(" | ", stale.ToArray()));
            checks.Add("PASS her help page quotes her lines as she says them now: keys, rate limits, Gemini's daily limit, the service's servers, her mic and Windows speech, cloud voice credit, the special-letters folder, call mode hearing and unreadable settings, with none of the old wording");
        }

        // The owner's smaller wording flags: each line keeps its cause and its fix inside the note that shows it.
        private void SmokeSmallWording(List<string> checks)
        {
            // A14: her brain's and HD voice's server zips pass their checksum first, so a missing .exe means antivirus took it.
            foreach (string gone in new[] { Engine.ServerGone, QwenVoice.ServerGone })
                if (gone.Length > 70 || !gone.StartsWith("Your antivirus", StringComparison.Ordinal) || !gone.EndsWith(" Allow it, then try again.", StringComparison.Ordinal))
                    throw new Exception("A missing server line doesn't name the antivirus and the fix inside its note: " + gone);
            checks.Add("PASS a missing llama-server.exe or tts-server.exe says your antivirus likely took it and to allow it, whole inside the BRAIN and VOICE notes");

            // Jobs flag 5: the three admin lines say how to run her as administrator, the same way.
            foreach (string line in new[] { Computer.AdminWindow, Arrange.Admin("move", "Task Manager"), AdminKeys })
                if (!line.EndsWith(Computer.AsAdmin + ".", StringComparison.Ordinal) || line.Contains(" as admin ") || line.Contains(" as admin,")) throw new Exception("An admin line doesn't say how to run her as administrator: " + line);
            checks.Add("PASS the three admin lines end the same way: Quit Capri from her menu, then Run as administrator from her shortcut");

            // Hearing flag 12: call mode's browser reads as "your browser" in her lines, and other apps keep their names.
            CallApp browser = Array.Find(CallApps.Apps, app => app.Name == "Browser"), zoom = Array.Find(CallApps.Apps, app => app.Name == "Zoom");
            if (browser.NotOpen != "Your browser isn't open." || browser.Said != "your browser" || zoom.NotOpen != "Zoom isn't open." || zoom.Said != "Zoom")
                throw new Exception("Call mode's browser reads oddly: " + browser.NotOpen + " / " + browser.Said);
            checks.Add("PASS call mode says \"Your browser isn't open.\" and \"She hears you and your browser.\" instead of naming \"Browser\" like an app");

            // Jobs flag 8: typed text in a job's question, status and steps reads as typed, not as JSON.
            ToolArguments typedArgs = new ToolArguments { Text = "hi\nC:\\Users\tcat" };
            string typedQuoted = "\"hi C:\\Users cat\"";
            foreach (string line in new[] { Computer.Question("type", typedArgs, null), Computer.Status("type", typedArgs, null), Computer.Did("type", typedArgs, null), Computer.Moving("type", typedArgs, null, true) })
                if (!line.Contains(typedQuoted) || line.Contains("\\u00") || line.Contains("\\\\")) throw new Exception("Typed text in a job line reads as code: " + line);
            checks.Add("PASS typed text in a job's question, status and step rows reads as typed, with no \\u000a or doubled backslashes");

            // A12: an empty reply says the same line, with its fix, whether her brain threw it or sent blank words.
            string emptyShort = Brain.Brief(Brain.Wordless);
            if (emptyShort == null || emptyShort.Length > 60 || emptyShort.IndexOf("another model", StringComparison.Ordinal) < 0) throw new Exception("The empty reply's BRAIN line lost its fix: " + emptyShort);
            string noteWas = mainframe.Status.Substring(mainframe.Status.IndexOf(" | ", StringComparison.Ordinal) + 3);
            Func<string, string> thinkWas = Think;
            try
            {
                Think = delegate { return "   "; };
                reply = null;
                Hear("empty reply");
                System.Diagnostics.Stopwatch emptyWait = System.Diagnostics.Stopwatch.StartNew();
                while (reply == null && emptyWait.ElapsedMilliseconds < 3000) { System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                if (reply != Brain.Wordless || talkState != TalkState.Dormant || !mainframe.Status.EndsWith(" | " + emptyShort, StringComparison.Ordinal))
                    throw new Exception("Blank words from her brain said \"" + reply + "\" and showed \"" + mainframe.Status + "\".");
            }
            finally { Think = thinkWas; asking++; CloseTalk(); mainframe.ShowProblem(noteWas); }
            checks.Add("PASS an empty reply says one line with its fix, \"Try again or pick another model in Capri Mainframe\", whether her brain threw it or sent blank words, and the BRAIN note keeps the fix");

            // A11: her voice being off reads one way with one fix, in the VOICE note, a preview she's asked for and the style sliders.
            bool silentWas = preferences.Silent;
            string previewLine;
            try { preferences.Silent = true; previewLine = SettingAction("preview_voice", true); }
            finally { preferences.Silent = silentWas; }
            foreach (string off in new[] { SilentNote, previewLine, StyleSilent })
                if (off.IndexOf("voice is off", StringComparison.Ordinal) < 0 || off.IndexOf("Pick a voice under VOICE to hear ", StringComparison.Ordinal) < 0 || off.Contains("OFF"))
                    throw new Exception("A voice-off line reads another way: " + off);
            if (previewLine != PreviewSilent || !StyleSilent.Contains("choom")) throw new Exception("The voice preview or the style sliders lost their voice-off line.");
            checks.Add("PASS her voice being off reads one way with one fix, \"Pick a voice under VOICE\", in the VOICE note, a preview she's asked for and the style sliders, like on Mac and Linux");

            // A1: brain setup shows a stalled download in the BRAIN note, which cut it at "Check your connection a...".
            foreach (string label in new[] { "the engine", "Gemma 4 E4B", "Qwen3.5 9B Instruct" })
            {
                string stalled = Engine.Stalled(label), shown = Brain.Brief(stalled);
                if (shown == null || shown.Length > 60 || shown.EndsWith("...", StringComparison.Ordinal) || shown.IndexOf("Check your connection", StringComparison.Ordinal) < 0)
                    throw new Exception("A stalled brain download lost its fix in the BRAIN note: " + (shown ?? stalled));
            }
            checks.Add("PASS a brain download that keeps stopping keeps \"Check your connection\" whole in the BRAIN note during setup");
        }

        private static bool Same(byte[] one, byte[] other)
        {
            if (one.Length != other.Length) return false;
            for (int i = 0; i < one.Length; i++) if (one[i] != other[i]) return false;
            return true;
        }
    }
}

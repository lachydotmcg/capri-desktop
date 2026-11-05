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
                { Brain.Blocked, "Built-in" }
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
                    { "Her ears didn't download: ", "The download of her ears keeps stopping. Check your connection and try again.", "Her ears didn't download: The download of her ears..." },
                    { "Windows speech failed: ", "The connection was lost.", "Windows speech failed: The connection was lost" }
                };
                for (int i = 0; i < reasons.GetLength(0); i++)
                {
                    bool alone = reasons[i, 0] == "Mic failed: ";
                    HearingStatus status = HearingStatus.Explained(reasons[i, 0], reasons[i, 1], alone, true, HearingAction.Sound, HearingAction.Type);
                    string whole = reasons[i, 1].StartsWith("Windows speech ", StringComparison.Ordinal) || alone ? reasons[i, 1] : reasons[i, 0] + reasons[i, 1];
                    bool shortened = status.Text != whole.TrimEnd('.');
                    if (status.Text != reasons[i, 2] || status.Text.Length > HearingStatus.LineRoom || status.Text.Contains("WASAPI") || status.Text.Contains("0x") || status.Said != (shortened ? whole : status.Text))
                        throw new Exception("A hearing line read wrong: " + reasons[i, 1] + " became " + status.Text + " and said " + status.Said);
                }
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
            if (!fits || snag.Count > TaskPanel.SnagRows || snagText.IndexOf("Run Capri as administrator too", StringComparison.Ordinal) < 0 || !snagText.StartsWith("Couldn't click or type there", StringComparison.Ordinal)
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
            checks.Add("PASS the task panel shows the fix: an ending like \"Got lost before I could start that job...\" reaches \"Pick one under BRAIN in Capri Mainframe.\", the newest failed step wraps to show \"Run Capri as administrator too\", older steps stay one row, and one that still won't fit keeps its start and its last sentence before it's cut");
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
                string url = FakeServer(new[] { "too long a reply", "right" }, asked, out fake, 200, 0, false, 0, "application/octet-stream");
                string[] downloads = new string[2];
                for (int i = 0; i < 2; i++)
                {
                    try { Engine.Fetch(url, Path.Combine(folder, "pack" + i + ".bin"), 5, new string('0', 64), "her test pack", delegate { }, delegate { return false; }); }
                    catch (IOException error) { downloads[i] = error.Message; }
                }
                if (downloads[0] != "The download of her test pack came out the wrong size. Try again." || downloads[1] != "The download of her test pack didn't match its checksum. Try again."
                    || Retrying(downloads[0]) != "The download of her test pack came out the wrong size." || Retrying("The download of her wake word keeps stopping. Check your connection and try again.") != "The download of her wake word keeps stopping. Check your connection." || Retrying("Cancelled.") != "Cancelled.")
                    throw new Exception("A download line started in lowercase or told her to try again while she retries: " + string.Join(" | ", downloads));
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
                string stalled = "The download of " + label + " keeps stopping. Check your connection and try again.", shown = Brain.Brief(stalled);
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

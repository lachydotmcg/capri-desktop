using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace CapriDesktop
{
    // Her use_computer jobs on Linux: look at the screen, ask her brain for one step, do it with Fingers, and look again,
    // until the brain says it's done, gives up, or she runs out of steps. Her brain has to see pictures for this.
    internal static class Chores
    {
        internal const int MostSteps = 20;
        internal static Func<byte[]> Shoot = delegate { return Eyes.Shoot(); };
        // Where the mouse is now, 0 to 1 across the desktop, or null where the desktop won't say.
        internal static Func<double[]> Mouse = delegate { return null; };
        // The smoke test watches what she'd do instead of moving the real mouse.
        internal static Action<string, JsonElement> Doing;

        internal const string Rules =
            "You use a Linux computer's mouse and keyboard to finish one job for the user. Each turn you get a screenshot of their whole desktop and the steps you already took. " +
            "Reply with exactly one JSON object for the next single step, and nothing else. Coordinates are x and y from 0 to 1000 across the screenshot: 0,0 is the top-left corner and 1000,1000 the bottom-right.\n" +
            "{\"do\":\"click\",\"x\":120,\"y\":540} also double_click, right_click and move\n" +
            "{\"do\":\"drag\",\"x\":120,\"y\":340,\"to_x\":400,\"to_y\":340}\n" +
            "{\"do\":\"scroll\",\"x\":600,\"y\":400,\"amount\":5} amount is wheel notches, positive scrolls down\n" +
            "{\"do\":\"type\",\"text\":\"hello\"} types into whatever has focus, so click the box first\n" +
            "{\"do\":\"key\",\"keys\":\"ctrl+l\"} keys like enter, tab, esc, up, f5, super, alt+tab or ctrl+shift+t\n" +
            "{\"do\":\"wait\",\"seconds\":2}\n" +
            "{\"do\":\"done\",\"said\":\"what you did, in one short sentence\"}\n" +
            "{\"do\":\"stop\",\"said\":\"why you can't go on, in one short sentence\"}\n" +
            "Click the middle of what you mean. When the screen already shows the job finished, say done. " +
            "Never type passwords, card numbers or codes, never pay, buy, send money or delete anything, and never accept terms for them: stop and say they need to do that part. " +
            "If the same step didn't change anything twice, try something else or stop.";

        internal static string Run(Brain brain, string job, string asked)
        {
            string problem = Fingers.Problem;
            if (problem != null && Doing == null) return problem;
            if (brain.Model == null) return "Couldn't do that: your brain is offline.";
            List<string> steps = new List<string>();
            for (int step = 0; step < MostSteps; step++)
            {
                if (Grabbed()) return "You grabbed the mouse, so you stopped. Say what to do next.";
                byte[] picture = Shoot();
                byte[] shot = Eyes.Jpeg(picture, 1280);
                if (shot == null) return "Couldn't do that: " + Eyes.Missing.Replace("You couldn't take a screenshot. ", "");
                int width, height;
                if (!Size(shot, out width, out height)) return "Couldn't do that: the screenshot couldn't be read.";
                string instruction = "Job: " + job + (string.IsNullOrWhiteSpace(asked) ? "" : "\nTheir words: " + asked) +
                    "\nScreenshot: " + width + " x " + height + " pixels." +
                    (steps.Count == 0 ? "\nNo steps yet." : "\nSteps so far:\n" + string.Join("\n", steps.ToArray())) + "\nThe next step, as one JSON object:";
                string reply;
                try { reply = brain.See(shot, Rules, instruction, 160); }
                catch (BrainException error) { return "Couldn't do that: " + error.Message; }
                catch (System.Net.WebException error)
                {
                    if (error.Status == System.Net.WebExceptionStatus.RequestCanceled) return "You stopped because they asked you something else.";
                    return "Couldn't do that: your brain stopped answering.";
                }
                JsonElement order;
                if (!Read(reply, out order))
                {
                    steps.Add((step + 1) + ". (your reply wasn't one JSON step, so nothing happened)");
                    continue;
                }
                string what = Text(order, "do").ToLowerInvariant();
                if (what == "done") return "Did it. " + Said(order, "Finished the job.");
                if (what == "stop") return "Couldn't finish that: " + Said(order, "got stuck.");
                string done = Act(what, order, width, height);
                steps.Add((step + 1) + ". " + done);
                Thread.Sleep(what == "wait" ? 0 : 450);
            }
            return "Ran out of steps before finishing. " + (steps.Count > 0 ? "Last: " + steps[steps.Count - 1].Substring(steps[steps.Count - 1].IndexOf(' ') + 1) : "");
        }

        private static string Said(JsonElement order, string otherwise)
        {
            string said = Text(order, "said").Trim();
            return said.Length == 0 ? otherwise : said;
        }

        // They took the mouse back: it's away from where she last put it.
        private static bool Grabbed()
        {
            if (Fingers.LastX < 0) return false;
            double[] now = Mouse();
            if (now == null) return false;
            return Math.Abs(now[0] - Fingers.LastX) > 0.03 || Math.Abs(now[1] - Fingers.LastY) > 0.03;
        }

        internal static string Act(string what, JsonElement order, int width, int height)
        {
            double x = Number(order, "x") / 1000, y = Number(order, "y") / 1000;
            string at = " at " + (int)Number(order, "x") + ", " + (int)Number(order, "y");
            if (Doing != null) { Doing(what, order); return what + at; }
            switch (what)
            {
                case "click": Fingers.MoveTo(x, y); Fingers.Click("left", 1); return "clicked" + at;
                case "double_click": Fingers.MoveTo(x, y); Fingers.Click("left", 2); return "double-clicked" + at;
                case "right_click": Fingers.MoveTo(x, y); Fingers.Click("right", 1); return "right-clicked" + at;
                case "move": Fingers.MoveTo(x, y); return "moved the mouse" + at;
                case "drag":
                    Fingers.Drag(x, y, Number(order, "to_x") / 1000, Number(order, "to_y") / 1000);
                    return "dragged from" + at.Substring(3) + " to " + (int)Number(order, "to_x") + ", " + (int)Number(order, "to_y");
                case "scroll":
                    int notches = (int)Math.Max(-15, Math.Min(15, Number(order, "amount")));
                    if (order.TryGetProperty("x", out _)) Fingers.MoveTo(x, y);
                    Fingers.Scroll(notches == 0 ? 3 : notches);
                    return "scrolled " + (notches < 0 ? "up " + -notches : "down " + (notches == 0 ? 3 : notches)) + at;
                case "type":
                    string text = Text(order, "text");
                    if (Secret(text)) return "didn't type that: it looked like a password or card number";
                    return Fingers.Type(text) ? "typed \"" + Voice.Short(text) + "\"" : "couldn't type that (no wl-copy or xclip for symbols)";
                case "key":
                    string keys = Text(order, "keys");
                    return Fingers.Keys(keys) ? "pressed " + keys : "didn't know the key " + keys;
                case "wait":
                    double seconds = Math.Max(0.2, Math.Min(5, Number(order, "seconds")));
                    Thread.Sleep((int)(seconds * 1000));
                    return "waited " + seconds.ToString("0.#", CultureInfo.InvariantCulture) + " s";
                default: return "(\"" + what + "\" isn't a step you can take, so nothing happened)";
            }
        }

        // A long run of digits, like a card number, never goes out through her keyboard.
        internal static bool Secret(string text) { return Regex.IsMatch(Regex.Replace(text ?? "", @"[\s-]", ""), @"\d{12,19}"); }

        // The first {...} in a reply, read as JSON. Models wrap it in words or ``` more often than not.
        internal static bool Read(string reply, out JsonElement order)
        {
            order = default(JsonElement);
            string text = reply ?? "";
            int start = text.IndexOf('{');
            while (start >= 0)
            {
                int depth = 0;
                bool quoted = false;
                for (int i = start; i < text.Length; i++)
                {
                    char c = text[i];
                    if (quoted) { if (c == '\\') i++; else if (c == '"') quoted = false; continue; }
                    if (c == '"') quoted = true;
                    else if (c == '{') depth++;
                    else if (c == '}' && --depth == 0)
                    {
                        try
                        {
                            using (JsonDocument parsed = JsonDocument.Parse(text.Substring(start, i - start + 1)))
                            {
                                if (parsed.RootElement.ValueKind != JsonValueKind.Object || Text(parsed.RootElement, "do").Length == 0) break;
                                order = parsed.RootElement.Clone();
                                return true;
                            }
                        }
                        catch (JsonException) { break; }
                    }
                }
                start = text.IndexOf('{', start + 1);
            }
            return false;
        }

        private static string Text(JsonElement order, string name)
        {
            JsonElement value;
            if (!order.TryGetProperty(name, out value)) return "";
            return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ValueKind == JsonValueKind.Number ? value.GetRawText() : "";
        }

        private static double Number(JsonElement order, string name)
        {
            JsonElement value;
            if (!order.TryGetProperty(name, out value)) return 0;
            double number;
            if (value.ValueKind == JsonValueKind.Number) return value.GetDouble();
            return value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number) ? number : 0;
        }

        // A JPEG's width and height, from its start-of-frame marker.
        internal static bool Size(byte[] jpeg, out int width, out int height)
        {
            width = height = 0;
            int at = 2;
            while (at + 9 < jpeg.Length)
            {
                if (jpeg[at] != 0xFF) { at++; continue; }
                byte marker = jpeg[at + 1];
                if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
                {
                    height = jpeg[at + 5] << 8 | jpeg[at + 6];
                    width = jpeg[at + 7] << 8 | jpeg[at + 8];
                    return width > 0 && height > 0;
                }
                at += 2 + (jpeg[at + 2] << 8 | jpeg[at + 3]);
            }
            return false;
        }
    }
}

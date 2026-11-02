using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    [DataContract]
    internal sealed class Reminder
    {
        [DataMember] public string Text;
        [DataMember] public string When;
        [DataMember] public bool Said;
        internal bool Late;

        internal DateTime Due { get { return DateTime.ParseExact(When, "o", CultureInfo.InvariantCulture, DateTimeStyles.None); } }
    }

    [DataContract]
    internal sealed class Routine
    {
        [DataMember] public string Phrase;
        [DataMember] public string Steps;
    }

    internal sealed class Reminders
    {
        private readonly object gate = new object();
        private readonly string remindersPath, routinesPath;
        private readonly string remindersError, routinesError;
        private List<Reminder> reminders;
        private List<Routine> routines;
        [ThreadStatic] private static HashSet<string> running;
        internal Func<DateTime> Now = delegate { return DateTime.Now; };

        internal Reminders(string folder, DateTime started)
        {
            remindersPath = Path.Combine(folder, "reminders.json");
            routinesPath = Path.Combine(folder, "routines.json");
            reminders = Read<Reminder>(remindersPath, out remindersError);
            routines = Read<Routine>(routinesPath, out routinesError);
            foreach (Reminder reminder in reminders)
            {
                DateTime due;
                if (reminder == null || string.IsNullOrWhiteSpace(reminder.Text) || !DateTime.TryParseExact(reminder.When, "o", CultureInfo.InvariantCulture, DateTimeStyles.None, out due))
                {
                    remindersError = "Couldn't read a saved reminder.";
                    break;
                }
                reminder.Late = !reminder.Said && due <= started;
            }
            foreach (Routine routine in routines)
                if (routine == null || Key(routine.Phrase).Length == 0 || string.IsNullOrWhiteSpace(routine.Steps)) { routinesError = "Couldn't read a saved routine."; break; }
        }

        private static List<T> Read<T>(string path, out string problem)
        {
            problem = null;
            try
            {
                if (!File.Exists(path)) return new List<T>();
                using (FileStream input = File.OpenRead(path))
                {
                    List<T> saved = (List<T>)new DataContractJsonSerializer(typeof(List<T>)).ReadObject(input);
                    if (saved == null) throw new SerializationException("The saved list is empty.");
                    return saved;
                }
            }
            catch (IOException readIo) { problem = "Couldn't read " + Path.GetFileName(path) + ": " + readIo.Message; }
            catch (UnauthorizedAccessException readAccess) { problem = "Couldn't read " + Path.GetFileName(path) + ": " + readAccess.Message; }
            catch (SerializationException readJson) { problem = "Couldn't read " + Path.GetFileName(path) + ": " + readJson.Message; }
            return new List<T>();
        }

        private static void Write<T>(string path, List<T> saved)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".new";
            using (FileStream output = File.Create(temporary)) new DataContractJsonSerializer(typeof(List<T>)).WriteObject(output, saved);
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }

        internal string Remind(ToolArguments args)
        {
            lock (gate)
            {
                if (remindersError != null) return remindersError;
                if (args.Action == "list")
                {
                    List<Reminder> waiting = reminders.FindAll(pending => !pending.Said);
                    waiting.Sort(delegate(Reminder left, Reminder right) { return left.Due.CompareTo(right.Due); });
                    List<string> lines = waiting.ConvertAll(listed => listed.Due.ToString("f") + ": " + listed.Text);
                    return lines.Count == 0 ? "You have no reminders waiting." : "Your reminders:\n" + string.Join("\n", lines.ToArray());
                }
                List<Reminder> changed = new List<Reminder>(reminders);
                if (args.Action == "cancel")
                {
                    string wanted = Key(args.Text);
                    if (wanted.Length == 0 || wanted == "the" || wanted == "my") return "Couldn't find that reminder.";
                    Reminder cancelled = changed.Find(exact => !exact.Said && Key(exact.Text) == wanted)
                        ?? changed.Find(part => !part.Said && (" " + Key(part.Text) + " ").Contains(" " + wanted + " "));
                    if (cancelled == null) return "Couldn't find that reminder.";
                    changed.Remove(cancelled);
                    Write(remindersPath, changed);
                    reminders = changed;
                    return "Cancelled your reminder: " + cancelled.Text;
                }
                DateTime when;
                if (string.IsNullOrWhiteSpace(args.Text)) return "What should I remind you about?";
                if (!Parse(args.When, Now(), out when)) return "What time should I remind you? Try in 20 minutes, at 5pm or tomorrow at 9.";
                changed.Add(new Reminder { Text = args.Text.Trim(), When = DateTime.SpecifyKind(when, DateTimeKind.Unspecified).ToString("o", CultureInfo.InvariantCulture) });
                Write(remindersPath, changed);
                reminders = changed;
                return "I'll remind you " + when.ToString("f") + ": " + args.Text.Trim();
            }
        }

        internal string Take(DateTime now)
        {
            lock (gate)
            {
                if (remindersError != null) return null;
                Reminder next = null;
                foreach (Reminder candidate in reminders)
                    if (!candidate.Said && candidate.Due <= now && (next == null || candidate.Due < next.Due)) next = candidate;
                if (next == null) return null;
                next.Said = true;
                try { Write(remindersPath, reminders); }
                catch { next.Said = false; throw; }
                return next.Late ? "Late reminder, due " + next.Due.ToString("f") + ": " + next.Text : "Reminder: " + next.Text;
            }
        }

        internal string Routine(ToolArguments args)
        {
            lock (gate)
            {
                if (routinesError != null) return routinesError;
                if (args.Action == "list")
                {
                    List<string> lines = routines.ConvertAll(listedRoutine => listedRoutine.Phrase + ": " + listedRoutine.Steps);
                    return lines.Count == 0 ? "You have no saved routines." : "Your routines:\n" + string.Join("\n", lines.ToArray());
                }
                string phrase = (args.Phrase ?? "").Trim().Trim('"', '\'', '“', '”');
                if (Key(phrase).Length == 0) return "What phrase should start your routine?";
                List<Routine> changed = new List<Routine>(routines);
                Routine existing = changed.Find(savedRoutine => Key(savedRoutine.Phrase) == Key(phrase));
                if (args.Action == "forget")
                {
                    if (existing == null) return "Couldn't find that routine.";
                    changed.Remove(existing);
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(args.Steps)) return "What should I do when you say " + phrase + "?";
                    if (existing != null) changed.Remove(existing);
                    changed.Add(new Routine { Phrase = phrase, Steps = args.Steps.Trim() });
                }
                Write(routinesPath, changed);
                routines = changed;
                return args.Action == "forget" ? "Forgot your routine: " + phrase : "When you say " + phrase + ", I'll " + args.Steps.Trim();
            }
        }

        internal string Steps(string phrase)
        {
            lock (gate)
            {
                if (routinesError != null) return null;
                Routine found = routines.Find(item => Key(item.Phrase) == Key(phrase));
                return found == null ? null : found.Steps;
            }
        }

        internal string Run(string phrase, Func<string, string> run)
        {
            string steps = Steps(phrase);
            if (steps == null) return "Couldn't find that routine.";
            if (running == null) running = new HashSet<string>();
            string key = Key(phrase);
            if (!running.Add(key)) return "Already doing that routine.";
            try { return run(steps); }
            finally { running.Remove(key); }
        }

        // How a timer names itself, so "10 mins" and "10 minutes" are both a "10 minute" timer.
        static string TimerSize(string span)
        {
            span = Regex.Replace(span.Trim(), @"^(?:an?|one) ", "1 ", RegexOptions.IgnoreCase);
            span = Regex.Replace(span, @"\b(?:minutes|mins?)\b", "minute", RegexOptions.IgnoreCase);
            span = Regex.Replace(span, @"\b(?:hours|hrs?)\b", "hour", RegexOptions.IgnoreCase);
            return Regex.Replace(span, @"\b(?:seconds|secs?)\b", "second", RegexOptions.IgnoreCase);
        }

        internal KeyValuePair<string, ToolArguments> Route(string said)
        {
            string text = Hands.Asked(said);
            Match list = Regex.Match(text, @"^(?:what (?:reminders|routines) do i have|(?:list|show)(?: me)?(?: my)? (?:reminders|routines)|what are my (?:reminders|routines))$", RegexOptions.IgnoreCase);
            if (list.Success) return Call(Regex.IsMatch(text, @"\broutines\b", RegexOptions.IgnoreCase) ? "routine" : "remind", new ToolArguments { Action = "list" });
            if (Regex.IsMatch(Fold(text), @"^(?:que recordatorios tengo|quels (?:sont mes rappels|rappels ai-je)|welche erinnerungen habe ich|jakie mam przypomnienia|quais (?:sao (?:os )?meus lembretes|lembretes (?:eu )?tenho))$")) return Call("remind", new ToolArguments { Action = "list" });
            Match cancel = Regex.Match(text, @"^(?:cancel|remove|delete|forget)(?: the| my)? (.+?) reminder$", RegexOptions.IgnoreCase);
            if (!cancel.Success) cancel = Regex.Match(text, @"^(?:cancela (?:el )?recordatorio (?:de la |del |de )?|annule (?:le )?rappel (?:de |pour )?|(?:lösche|losche) (?:die )?erinnerung (?:an )?|anuluj przypomnienie (?:o )?|(?:cancele|cancela) (?:o )?lembrete (?:de |da |do )?)(.+)$", RegexOptions.IgnoreCase);
            if (cancel.Success && !Regex.IsMatch(Key(cancel.Groups[1].Value), @"^(?:the|my)$")) return Call("remind", new ToolArguments { Action = "cancel", Text = cancel.Groups[1].Value });
            // "Cancel my timer" or "stop the 10 min timer": a timer is a reminder whose words say timer.
            Match unset = Regex.Match(text, @"^(?:cancel|stop|remove|delete|forget|end|kill)(?: the| my)?((?: \S+)? (?:minutes?|mins?|hours?|hrs?|seconds?|secs?))? timer$", RegexOptions.IgnoreCase);
            if (unset.Success) return Call("remind", new ToolArguments { Action = "cancel", Text = (TimerSize(unset.Groups[1].Value) + " timer").Trim() });
            Match forget = Regex.Match(text, @"^(?:forget|remove|delete)(?: the| my)? (.+?) routine$", RegexOptions.IgnoreCase);
            if (forget.Success && Steps(forget.Groups[1].Value) != null) return Call("routine", new ToolArguments { Action = "forget", Phrase = forget.Groups[1].Value });
            const string actions = @"(?:open|launch|start|run|play|turn|mute|unmute|pause|resume|close|switch|search|google|look|tell|send|write|type|press|click|take|show|set|remind|go|dance|shut|lock|join|leave|make|create|move)\b";
            Match routine = Regex.Match(text, @"^when i say\s+[""“](.+?)[""”][\s,:]+(" + actions + @".*)$", RegexOptions.IgnoreCase);
            if (!routine.Success) routine = Regex.Match(text, @"^when i say\s+(.+?),\s*(" + actions + @".*)$", RegexOptions.IgnoreCase);
            if (routine.Success && Key(routine.Groups[1].Value).Length > 0)
                return Call("routine", new ToolArguments { Phrase = routine.Groups[1].Value, Steps = routine.Groups[2].Value });
            string steps = running != null && running.Contains(Key(said)) ? null : Steps(said);
            if (steps != null) return Call("routine", new ToolArguments { Action = "run", Phrase = said });
            string[] starters =
            {
                @"^remind me\s+(.+)$", @"^recu[eé]rdame\s+(.+)$", @"^rappelle[- ]moi\s+(.+)$",
                @"^erinnere mich\s+(.+)$", @"^przypomnij mi\s+(.+)$", @"^(?:lembra[- ]me|lembre[- ]me|me lembre|me lembra)\s+(.+)$"
            };
            DateTime now = Now();
            // A timer is a reminder that says it's done: "set a timer for 10 minutes", "timer 30 seconds" or "start a 5 minute timer".
            const string Starting = @"^(?:(?:set|start|make|put on|put|give me)\s+(?:me\s+|up\s+)?(?:a\s+|an\s+)?)?";
            Match timer = Regex.Match(text, Starting + @"timer\s+(?:for\s+)?(.+?)$", RegexOptions.IgnoreCase);
            Match sized = Regex.Match(text, Starting + @"(\S+(?:\s+and\s+a\s+half)?)[\s-](minutes?|mins?|hours?|hrs?|seconds?|secs?)\s+timer$", RegexOptions.IgnoreCase);
            string span = timer.Success ? timer.Groups[1].Value.Trim() : sized.Success ? sized.Groups[1].Value + " " + sized.Groups[2].Value : null;
            DateTime timerDue;
            if (span != null && Parse("in " + span, now, out timerDue))
                return Call("remind", new ToolArguments { Text = "your " + TimerSize(span) + " timer is done", When = "in " + span });
            foreach (string starter in starters)
            {
                Match ask = Regex.Match(text, starter, RegexOptions.IgnoreCase);
                if (!ask.Success) continue;
                string rest = ask.Groups[1].Value;
                foreach (Match gap in Regex.Matches(rest, @"\s+", RegexOptions.RightToLeft))
                {
                    DateTime due;
                    string when = rest.Substring(0, gap.Index).TrimEnd(',');
                    if (!Parse(when, now, out due)) continue;
                    string note = rest.Substring(gap.Index).TrimStart(' ', ',');
                    note = Regex.Replace(note, @"^(?:to\s+|que\s+|de\s+|d['’]|daran[, ]+(?:dass\s+)?|dass\s+|(?:żeby|zeby|aby|by)\s+|para\s+)", "", RegexOptions.IgnoreCase).Trim();
                    if (note.Length == 0 || Regex.IsMatch(Fold(note), @"^\p{N}|^(?:at|a las|a la|a|um|o|as|to|de|para|daran|zeby|aby|am|pm|in|on|and|half|after|before|today|tomorrow|tonight|next|this|morning|afternoon|evening|minutes?|hours?|seconds?|days?|weeks?|manana|demain|morgen|jutro|amanha|esta noche|ce soir|heute|dzis|dzisiaj|hoje|esta noite|morgens|abends|rano|wieczorem|du soir)(?:\s|$)")) continue;
                    return Call("remind", new ToolArguments { Text = note, When = when });
                }
                return new KeyValuePair<string, ToolArguments>();
            }
            Match later = Regex.Match(text, @"^(.+?)\s+(?:tell me to|remind me to|dime que|rappelle[- ]moi de|dis[- ]moi de|sag mir[, ]+(?:dass\s+)?|powiedz mi[, ]+(?:(?:żeby|zeby|aby)\s+)?|(?:me diga|diga[- ]me|lembre[- ]me)\s+(?:para|de))\s*(.+)$", RegexOptions.IgnoreCase);
            DateTime laterDue;
            if (later.Success && Parse(later.Groups[1].Value, now, out laterDue) && later.Groups[2].Value.Trim().Length > 0)
                return Call("remind", new ToolArguments { Text = later.Groups[2].Value.Trim(), When = later.Groups[1].Value });
            return new KeyValuePair<string, ToolArguments>();
        }

        private static KeyValuePair<string, ToolArguments> Call(string tool, ToolArguments args) { return new KeyValuePair<string, ToolArguments>(tool, args); }

        internal static string Key(string text)
        {
            string plain = Regex.Replace(Fold(text).Replace("'", ""), @"[^\p{L}\p{N}\s]", " ");
            plain = Regex.Replace(plain, @"\s+", " ").Trim();
            return Regex.Replace(plain, @"^(?:hey\s+)?capri(?:\s+|$)", "");
        }

        private static string Fold(string text)
        {
            StringBuilder plain = new StringBuilder();
            foreach (char letter in (text ?? "").ToLowerInvariant().Replace('’', '\'').Normalize(NormalizationForm.FormD))
                if (CharUnicodeInfo.GetUnicodeCategory(letter) != UnicodeCategory.NonSpacingMark) plain.Append(letter == 'ł' ? 'l' : letter);
            return Regex.Replace(plain.ToString().Normalize(NormalizationForm.FormC), @"\s+", " ").Trim();
        }

        private static double Number(string text)
        {
            double value;
            if (double.TryParse(text.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)) return value;
            string[] words =
            {
                "zero|cero|null", "a|an|one|un|una|une|ein|eine|einer|einen|eins|um|uma|jeden|jedna|jednej",
                "two|dos|deux|zwei|dwa|dwie|dois|duas", "three|tres|trois|drei|trzy", "four|cuatro|quatre|vier|cztery|quatro",
                "five|cinco|cinq|funf|piec|piatej", "six|seis|sechs|szesc|szostej", "seven|siete|sept|sieben|siedem|sete|siodmej",
                "eight|ocho|huit|acht|osiem|oito|osmej", "nine|nueve|neuf|neun|dziewiec|nove|dziewiatej",
                "ten|diez|dix|zehn|dziesiec|dez|dziesiatej", "eleven|once|onze|elf|jedenascie", "twelve|doce|douze|zwolf|dwanascie|doze",
                "thirteen|trece|treize|dreizehn|trzynascie|treze", "fourteen|catorce|quatorze|vierzehn|czternascie|catorze",
                "fifteen|quince|quinze|funfzehn|pietnascie", "sixteen|dieciseis|seize|sechzehn|szesnascie|dezesseis|dezasseis",
                "seventeen|diecisiete|dix-sept|siebzehn|siedemnascie|dezessete|dezassete", "eighteen|dieciocho|dix-huit|achtzehn|osiemnascie|dezoito",
                "nineteen|diecinueve|dix-neuf|neunzehn|dziewietnascie|dezenove", "twenty|veinte|vingt|zwanzig|dwadziescia|vinte",
                "twenty one|veintiuno|vingt et un|einundzwanzig|dwadziescia jeden|vinte e um",
                "twenty two|veintidos|vingt-deux|zweiundzwanzig|dwadziescia dwa|vinte e dois",
                "twenty three|veintitres|vingt-trois|dreiundzwanzig|dwadziescia trzy|vinte e tres"
            };
            for (int word = 0; word < words.Length; word++) if (Array.IndexOf(words[word].Split('|'), text) >= 0) return word;
            if (Regex.IsMatch(text, @"^(?:half(?: an?| of an?)?|media|une demi|demi|einer halben|einer halbe|meia|pol)$")) return 0.5;
            if (text == "thirty" || text == "treinta" || text == "trente" || text == "dreissig" || text == "dreißig" || text == "trzydziesci" || text == "trinta") return 30;
            return -1;
        }

        internal static bool Parse(string text, DateTime now, out DateTime when)
        {
            when = now;
            string plain = Fold(text).TrimEnd('.', ',', '!', '?');
            Match relative = Regex.Match(plain, @"^(?:in|en|dans|za|em|dentro de|daqui a)\s+(.*?)\s*(minutes?|mins?|minutos?|minuten|minut(?:e|y|a)?|hours?|hrs?|horas?|heures?|stunden?|godzin(?:e|y|a)?|seconds?|secs?|segundos?|secondes?|sekunden?|sekund(?:e|y|a)?)$");
            if (relative.Success)
            {
                string amount = relative.Groups[1].Value.Trim().TrimEnd('-');
                double count = amount.Length == 0 ? 1 : Number(amount);
                if (count <= 0) return false;
                // Seconds are for timers, like a 30 second one.
                double minutes = Regex.IsMatch(relative.Groups[2].Value, @"^(?:h|stund|godzin)") ? count * 60 : Regex.IsMatch(relative.Groups[2].Value, @"^(?:sec|seg|sek)") ? count / 60 : count;
                try { when = now.AddMinutes(minutes); return when > now; }
                catch (ArgumentOutOfRangeException) { return false; }
            }
            bool tomorrow = Regex.IsMatch(plain, @"^(?:tomorrow|manana|demain|morgen|jutro|amanha)(?:\s|$)");
            if (tomorrow) plain = Regex.Replace(plain, @"^\S+\s*", "");
            bool tonight = Regex.IsMatch(plain, @"^(?:tonight|esta noche|ce soir|heute abend|dzis(?:iaj)? wieczorem|hoje a noite|esta noite)(?:\s|$)");
            if (tonight) plain = Regex.Replace(plain, @"^(?:tonight|esta noche|ce soir|heute abend|dzis(?:iaj)? wieczorem|hoje a noite|esta noite)\s*", "");
            bool at = Regex.IsMatch(plain, @"^(?:at|a las|a la|a|um|o|as|pelas)\s+");
            if (at) plain = Regex.Replace(plain, @"^(?:at|a las|a la|a|um|o|as|pelas)\s+", "");
            if (plain.Length == 0 && !tomorrow && !tonight) return false;
            int hour, minute;
            bool meridiem = false;
            if (plain.Length == 0) { hour = tonight ? 20 : 9; minute = 0; }
            else if (!Clock(plain, out hour, out minute, out meridiem)) return false;
            if (!at && !tomorrow && !tonight && !Regex.IsMatch(plain, @"\d\s*(?:[:h]|[ap]\.?m|uhr|heures?)|^(?:half past|midnight|noon)\b")) return false;
            if (tonight && hour > 0 && hour < 12 && !Regex.IsMatch(plain, @"\bam$")) hour += 12;
            try
            {
                when = now.Date.AddDays(tomorrow ? 1 : 0).AddHours(hour).AddMinutes(minute);
                if (!tomorrow && !tonight && !meridiem && hour > 0 && hour < 12 && when <= now && when.AddHours(12) > now) when = when.AddHours(12);
                if (!tomorrow && when <= now) when = when.AddDays(1);
                return true;
            }
            catch (ArgumentOutOfRangeException) { return false; }
        }

        private static bool Clock(string text, out int hour, out int minute, out bool meridiem)
        {
            hour = 0;
            minute = 0;
            meridiem = false;
            string plain = text.Replace("a.m.", "am").Replace("p.m.", "pm").Replace("a.m", "am").Replace("p.m", "pm");
            plain = Regex.Replace(plain, @"\s+(?:de la tarde|de la noche|du soir|de l'apres-midi|nachmittags|abends|po poludniu|wieczorem|da tarde|da noite)$", " pm");
            plain = Regex.Replace(plain, @"\s+(?:de la manana|du matin|morgens|rano|da manha)$", " am");
            if (Regex.IsMatch(plain, @"^(?:midnight|medianoche|minuit|mitternacht|polnoc|polnocy|meia-noite)$")) return true;
            if (Regex.IsMatch(plain, @"^(?:noon|midday|mediodia|midi|mittag|poludnie|meio-dia)$")) { hour = 12; return true; }
            bool nextHour = Regex.IsMatch(plain, @"^(?:halb|w pol do)\s+");
            bool half = nextHour || plain.StartsWith("half past ", StringComparison.Ordinal) || Regex.IsMatch(plain, @"\s+(?:y media|et demie|e meia)(?:\s+[ap]m)?$");
            if (half)
            {
                plain = Regex.Replace(plain, @"^(?:half past|halb|w pol do)\s+|\s+(?:y media|et demie|e meia)(?=\s+[ap]m|$)", "");
                minute = 30;
            }
            Match clock = Regex.Match(plain, @"^(.+?)(?:\s*[:h]\s*(\d{1,2}))?\s*(?:h|uhr|heures?)?\s*([ap]m)?$");
            if (!clock.Success) return false;
            string digits = clock.Groups[1].Value.Trim();
            if (digits == "a" || digits == "an") return false;
            double number = Number(digits);
            if (number < 0 || number > 23 || number != Math.Floor(number)) return false;
            hour = (int)number;
            if (clock.Groups[2].Success)
            {
                if (half) return false;
                minute = int.Parse(clock.Groups[2].Value, CultureInfo.InvariantCulture);
            }
            if (minute > 59) return false;
            if (clock.Groups[3].Success)
            {
                meridiem = true;
                if (hour < 1 || hour > 12) return false;
                hour = hour % 12 + (clock.Groups[3].Value == "pm" ? 12 : 0);
            }
            if (nextHour) hour = (hour + 23) % 24;
            return true;
        }
    }
}

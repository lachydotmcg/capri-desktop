using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // What a brain writes for itself and not for them: its thinking, and its tool calls written out as words. Testers saw both in
    // her chat (Lachy, 12 Nov: "AI's are leaking their thought processes or their tool use into the chat"). Thinking comes out
    // before anything reads a reply. Written calls come out only of what she shows and says, so her hands still read them and
    // run them. Code in ``` or ` is left as it was written, so a reply about JSON, XML or <think> tags keeps its examples.
    internal static class Leaks
    {
        // Which brain the last ask on this thread went to, for her log.
        [ThreadStatic] private static string asker;
        internal static void Asking(string brain) { asker = brain; }
        internal static string Who { get { return asker ?? "her brain"; } }

        // Every name her hands take, longest first so open_app never stops short at open.
        private static string names;
        private static string Names
        {
            get
            {
                if (names != null) return names;
                List<string> all = new List<string>();
                for (int i = 0; i < Hands.Count; i++) all.Add(Hands.Name(i));
                all.Add("mod");
                all.Sort(delegate(string a, string b) { return b.Length.CompareTo(a.Length); });
                return names = string.Join("|", all.ConvertAll(Regex.Escape).ToArray());
            }
        }

        private const RegexOptions Loose = RegexOptions.IgnoreCase | RegexOptions.Singleline;
        private static readonly Regex Fence = new Regex(@"^[ \t]*```([^\r\n`]*)\r?\n(.*?)(?:^[ \t]*```[ \t]*\r?$|\z)", RegexOptions.Multiline | RegexOptions.Singleline);
        private static readonly Regex Tick = new Regex(@"`[^`\r\n]+`");

        private static readonly Regex ThinkOpen = new Regex(@"<(?:think|thinking|thoughts?|reasoning|seed:think)(?:\s[^<>]*)?>|\[THINK\]|◁think▷|<\|begin_of_thought\|>|<\|channel>thought\b|<\|thinking\|>", RegexOptions.IgnoreCase);
        private static readonly Regex ThinkClose = new Regex(@"</(?:think|thinking|thoughts?|reasoning|seed:think)\s*>|\[/THINK\]|◁/think▷|<\|end_of_thought\|>|<channel\|>|<\|/thinking\|>", RegexOptions.IgnoreCase);
        // The start of a tag a reply was cut off in: "...<thi".
        private static readonly Regex HalfThink = new Regex(@"</?thi(?:n(?:k(?:i(?:n(?:g)?)?)?)?)?\s*\z", RegexOptions.IgnoreCase);
        // gpt-oss writes its thinking on an analysis channel and its answer on a final one. Some hosts send the channels as words,
        // with the special tokens gone: "analysisThe user says hi...assistantfinalHey!".
        private static readonly Regex HarmonyFinal = new Regex(@"<\|channel\|>\s*final\s*(?:<\|constrain\|>\s*[\w.-]+\s*)?<\|message\|>(.*?)(?=<\|end\|>|<\|return\|>|<\|call\|>|<\|start\|>|\z)", Loose);
        private static readonly Regex HarmonyAside = new Regex(@"(?:<\|start\|>\s*assistant\s*)?<\|channel\|>\s*(?:analysis|commentary)\b.*?(?:<\|end\|>|<\|call\|>|\z)", Loose);
        private static readonly Regex HarmonyWords = new Regex(@"\A\s*analysis.*?assistantfinal", Loose);
        private static readonly Regex HarmonyTokens = new Regex(@"<\|(?:start|end|message|channel|return|call|constrain)\|>(?:\s*assistant\b)?", RegexOptions.IgnoreCase);
        private static readonly Regex Wrapped = new Regex(@"\A\s*<(answer|final_answer|output)>(.*)</\1>\s*\z", Loose);
        private static readonly Regex Solution = new Regex(@"<\|(?:begin|end)_of_solution\|>", RegexOptions.IgnoreCase);

        private static readonly Regex CallOpen = new Regex(@"<tool_calls?>|<\|tool_calls?\|>|<tool_use>|<tool_code>|<function_calls>|<function_?call>|<function=[^<>\s]+>|<invoke\b[^<>]*>|<\|tool_calls_section_begin\|>|<\|tool_call_begin\|>|<｜tool▁calls?▁begin｜>|<\|python_tag\|>", RegexOptions.IgnoreCase);
        private static readonly Regex CallClose = new Regex(@"</tool_calls?>|<\|/tool_calls?\|>|</tool_use>|</tool_code>|</function_calls>|</function_?call>|</function>|</invoke>|<\|tool_calls_section_end\|>|<\|tool_call_end\|>|<｜tool▁calls?▁end｜>|<\|eom_id\|>|<\|eot_id\|>", RegexOptions.IgnoreCase);
        private static readonly Regex Template = new Regex(@"<\|im_start\|>\s*(?:assistant|user|system)?|<\|(?:im_end|eot_id|eom_id|endoftext|end_of_text|begin_of_text|end|return|call|start|message|tool_call_argument_begin)\|>|</s>\s*\z", RegexOptions.IgnoreCase);
        private static readonly Regex Mistral = new Regex(@"\[TOOL_CALLS\]\s*", RegexOptions.IgnoreCase);
        private static readonly Regex CallFence = new Regex(@"\A\s*(?:tool_code|tool_calls?|tool_use|function_calls?|functions?)\s*\z", RegexOptions.IgnoreCase);

        // Thinking with no tags, in plain words: a brain talking about "the user" or its tools to itself, never to them (PR 245:
        // "The user is asking me to close discord ... the correct tool call would be app_window"). Plenty of honest answers talk
        // about "the user" too (a sign-up page, an error, a game's rules), so a paragraph only counts when it also talks to itself
        // about its own reply. After one that does, the paragraphs that carry on about the user or its tools go with it.
        private static readonly Regex Musing = new Regex(
            @"\bthe user(?:['’]s)?\s+(?:is|was|has|had|just|now|seems|might|may|probably|likely|clearly|wants?|wanted|asks?|asked|asking|says?|said|mentions?|mentioned|requests?|requested|types?|typed|wrote|writes|greets?|greeted|sent|message|request|question|input|query)\b" +
            @"|\b(?:correct|right|appropriate|best|proper) (?:tool|function)(?: call)? (?:would be|is|here is|to (?:use|call))\b" +
            @"|\blooking at (?:the |my )?(?:available )?(?:tools|functions)\b" +
            @"|\b(?:respond|reply|answer)(?:ing)? (?:as|in character as|in the voice of) capri\b", RegexOptions.IgnoreCase);
        private static readonly Regex ToItself = new Regex(
            @"\b(?:asking|asked|asks|wants|wanted|told|tells|telling|needs|expects?) me\b" +
            @"|\bi(?: (?:should|need to|have to|must|will|can|am going to|want to)|['’]ll|['’]m going to) (?:respond|reply|answer|greet|say|tell|ask|acknowledge|keep|stay|be|call|use|invoke|output|comply|refuse|check|mention|make sure|figure|think|consider|start|play|open|run|pick|choose|go with)\b" +
            @"|\bmy (?:reply|response|answer|tools|instructions|system prompt|persona)\b" +
            @"|\blet me (?:think|check|see|figure|look|recall|consider|break)\b" +
            @"|\blet['’]?s (?:break|think|figure) (?:this|it|about)\b" +
            @"|\b(?:respond|reply|answer)(?:ing)? (?:as|in character as|in the voice of) capri\b" +
            @"|\b(?:the )?(?:system prompt|instructions) (?:says?|tells?|wants?)\b" +
            @"|\bin character\b" +
            @"|\b(?:respond|reply) to (?:them|the user|him|her)\b" +
            @"|\bgreet (?:them|him|her) back\b", RegexOptions.IgnoreCase);
        // "Thinking:" before a paragraph is a brain's note to itself only when its answer comes after under "Answer:". "Analysis:"
        // or "Reasoning:" on its own is a normal way to answer.
        private static readonly Regex ThinkingLabel = new Regex(@"\A\W*(?:thinking|reasoning|thought process|internal monologue|analysis)\s*[:：]", RegexOptions.IgnoreCase);
        private static readonly Regex Labelled = new Regex(@"\A\W*(?:final )?(?:answer|response|reply)\s*[:：]\W*", RegexOptions.IgnoreCase);

        // The reason her brain gives when a reply's words were all its own thinking, so that one gets asked again.
        internal const string OnlyThought = "Its whole reply was its own thinking.";
        internal const string JustAnswer = "\nYour last reply had no words for them in it, only your own thinking or a tool call. Reply again with only what you say to them, in your own voice: no thinking, no notes about them, no tags and no tool calls.";

        // A leak she took out, once per kind and brain every few seconds, since one reply is cleaned many times over.
        private static readonly Dictionary<string, DateTime> noted = new Dictionary<string, DateTime>();

        private static void Note(string kind)
        {
            string key = Who + "|" + kind;
            lock (noted)
            {
                DateTime last;
                if (noted.TryGetValue(key, out last) && (DateTime.UtcNow - last).TotalSeconds < 10) return;
                noted[key] = DateTime.UtcNow;
            }
            Brain.Log("leak", -1, Who + ": " + kind + " taken out of her reply");
        }

        // Where code is, so nothing in it is touched.
        private static bool[] Coded(string text)
        {
            bool[] code = new bool[text.Length + 1];
            foreach (Match fence in Fence.Matches(text)) for (int i = fence.Index; i < fence.Index + fence.Length; i++) code[i] = true;
            foreach (Match tick in Tick.Matches(text)) if (!code[tick.Index]) for (int i = tick.Index; i < tick.Index + tick.Length; i++) code[i] = true;
            return code;
        }

        // A tag written inside a sentence, like "wrap it in <think> and </think>", is them being told about it. A brain's own tags
        // start a line or touch its words.
        private static bool Mentioned(string text, Match tag)
        {
            char before = tag.Index > 0 ? text[tag.Index - 1] : '\n';
            int after = tag.Index + tag.Length;
            char next = after < text.Length ? text[after] : ' ';
            return (before == ' ' || before == '\t') && (next == ' ' || next == '\t' || char.IsPunctuation(next));
        }

        // A space where something was taken out, so the words either side don't run together. None at the start or after a space,
        // so a tag left after it still reads as starting a line, not as one mentioned in a sentence.
        private static void Gap(StringBuilder kept)
        {
            if (kept.Length > 0 && !char.IsWhiteSpace(kept[kept.Length - 1])) kept.Append(' ');
        }

        // Each block from an opener outside code to its closer, or to the end when it never closes.
        private static string Cut(string text, Regex open, Regex close, string kind, string unclosed, List<string> found)
        {
            if (!open.IsMatch(text)) return text;
            bool[] code = Coded(text);
            StringBuilder kept = new StringBuilder();
            int at = 0;
            for (Match start = open.Match(text); start.Success; start = open.Match(text, at))
            {
                if (code[start.Index] || Mentioned(text, start)) { kept.Append(text, at, start.Index + start.Length - at); at = start.Index + start.Length; continue; }
                Match stop = close.Match(text, start.Index + start.Length), first = null;
                while (stop.Success && (code[stop.Index] || Mentioned(text, stop))) { if (first == null) first = stop; stop = stop.NextMatch(); }
                if (!stop.Success) stop = first;
                int end = stop != null ? stop.Index + stop.Length : text.Length;
                found.Add(stop != null ? kind : unclosed);
                Gap(kept.Append(text, at, start.Index - at));
                at = end;
                if (at >= text.Length) break;
            }
            if (at < text.Length) kept.Append(text, at, text.Length - at);
            return kept.ToString();
        }

        // A closer left with nothing opened before it: everything up to it was thinking, from a brain whose own chat template
        // opened the tag (Qwen3, DeepSeek R1 distills, Nemotron). For call closers, just the stray tag.
        private static string Orphan(string text, Regex close, bool before, string kind, List<string> found)
        {
            if (!close.IsMatch(text)) return text;
            bool[] code = Coded(text);
            Match last = null;
            foreach (Match stop in close.Matches(text)) if (!code[stop.Index] && !Mentioned(text, stop)) { last = stop; if (!before) break; }
            if (last == null) return text;
            found.Add(kind);
            if (before) return text.Substring(last.Index + last.Length);
            StringBuilder kept = new StringBuilder();
            int at = 0;
            foreach (Match stop in close.Matches(text))
            {
                if (code[stop.Index] || Mentioned(text, stop)) continue;
                Gap(kept.Append(text, at, stop.Index - at));
                at = stop.Index + stop.Length;
            }
            return kept.Append(text, at, text.Length - at).ToString();
        }

        internal static string Thoughts(string text) { return Thoughts(text, null); }

        private static string Thoughts(string text, List<string> into)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            List<string> found = into ?? new List<string>();
            int before = found.Count;
            if (text.IndexOf("<|channel|>", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                MatchCollection finals = HarmonyFinal.Matches(text);
                if (finals.Count > 0) text = finals[finals.Count - 1].Groups[1].Value;
                else text = HarmonyAside.Replace(text, " ");
                text = HarmonyTokens.Replace(text, " ");
                found.Add("gpt-oss analysis channel");
            }
            else if (HarmonyWords.IsMatch(text))
            {
                text = HarmonyWords.Replace(text, "", 1);
                found.Add("gpt-oss analysis channel");
            }
            text = Cut(text, ThinkOpen, ThinkClose, "thinking tags", "unclosed thinking tag", found);
            text = Orphan(text, ThinkClose, true, "thinking with no opening tag", found);
            Match half = HalfThink.Match(text);
            if (half.Success && !Coded(text)[half.Index]) { text = text.Substring(0, half.Index); found.Add("half a thinking tag"); }
            if (found.Count > before)
            {
                Match wrapped = Wrapped.Match(text);
                if (wrapped.Success) text = wrapped.Groups[2].Value;
                text = Solution.Replace(text, " ");
            }
            if (into == null) foreach (string kind in found) Note(kind);
            return text;
        }

        // Her reply without the calls a brain wrote out as text or the chat template's own tokens: what she shows and says.
        internal static string Spoken(string text) { return Spoken(text, null); }

        private static string Spoken(string text, List<string> into)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            List<string> found = into ?? new List<string>();
            string was = text;
            text = Cut(text, CallOpen, CallClose, "tool call tags", "unclosed tool call tag", found);
            text = Orphan(text, CallClose, false, "stray tool call tag", found);
            text = Mistraled(text, found);
            text = Fenced(text, found);
            text = Bare(text, found);
            if (Template.IsMatch(text))
            {
                bool[] code = Coded(text);
                string source = text;
                // An end-of-text </s> is HTML's strikethrough when an <s> opened before it.
                text = Template.Replace(text, delegate(Match token) { return code[token.Index] || token.Value.StartsWith("</s>", StringComparison.OrdinalIgnoreCase) && source.LastIndexOf("<s>", token.Index, StringComparison.OrdinalIgnoreCase) >= 0 ? token.Value : " "; });
                if (text != was) found.Add("chat template tokens");
            }
            if (into == null) foreach (string kind in found) Note(kind);
            return text;
        }

        // Mistral's [TOOL_CALLS] and the JSON after it.
        private static string Mistraled(string text, List<string> found)
        {
            if (!Mistral.IsMatch(text)) return text;
            bool[] code = Coded(text);
            Dictionary<int, int> closings = new Dictionary<int, int>();
            StringBuilder kept = new StringBuilder();
            int done = 0;
            foreach (Match mark in Mistral.Matches(text))
            {
                if (mark.Index < done || code[mark.Index]) continue;
                int at = mark.Index + mark.Length, end = at;
                if (at < text.Length && (text[at] == '[' || text[at] == '{')) end = Closing(text, at, closings) + 1;
                if (end <= at) { int line = text.IndexOfAny(new[] { '\r', '\n' }, at); end = line < 0 ? text.Length : line; }
                kept.Append(text, done, mark.Index - done).Append(' ');
                done = end;
                found.Add("tool call tags");
            }
            return kept.Append(text, done, text.Length - done).ToString();
        }

        // A fenced block that holds only calls to her own tools, or that's marked as a call, like Gemini's ```tool_code.
        private static string Fenced(string text, List<string> found)
        {
            return Fence.Replace(text, delegate(Match fence)
            {
                string body = fence.Groups[2].Value;
                if (CallFence.IsMatch(fence.Groups[1].Value)) { found.Add("tool call code block"); return " "; }
                List<string> inside = new List<string>();
                string rest = Bare(body, inside, false);
                if (inside.Count == 0 || !Regex.IsMatch(rest, @"\A[\s,\[\]()]*\z")) return fence.Value;
                found.Add("tool call code block");
                return " ";
            });
        }

        private static readonly Regex Owner = new Regex(@"""(?:name|tool|tool_name|function|recipient_name)""\s*:\s*""(?:functions\.)?(?<tool>[\w.]+)""", RegexOptions.IgnoreCase);
        private static readonly Regex Given = new Regex(@"""(?:arguments|parameters|args|input)""\s*:", RegexOptions.IgnoreCase);
        private static Regex named, said, python;
        // Not after . # @ or -, so CSS like .clip { overflow: hidden; } stays theirs.
        private static Regex NamedBefore { get { return named ?? (named = new Regex(@"(?:(?<prefix>\bcall\s*:\s*|\bfunctions\.)|(?<![\p{L}\p{N}_.#@-]))(?<tool>" + Names + @")(?::\d+)?\s*\z")); } }
        // Only names like use_computer, so "Remember: you've got this" stays hers, and never a sentence that ends like one, so
        // "pc_status: CPU 12%, RAM 40%." does too.
        private static Regex SaidAsWords { get { return said ?? (said = new Regex(@"\A\s*(?:call\s*:\s*)?(?<tool>" + Names + @")\s*:\s*[^\r\n]*[^\s.!?…]\s*\z")); } }
        private static readonly Regex Quoted = new Regex(@"\A\{\s*(?:\}|""[^""]*""\s*:|<\|""\|>)");
        private static Regex Python { get { return python ?? (python = new Regex(@"(?<![\p{L}\p{N}_.])(?<prefix>print\(\s*)?(?<prefix>default_api\.|functions\.)?(?<tool>" + Names + @")\(")); } }
        private static readonly Regex OpenAiWrap = new Regex(@"\{\s*(?:""type""\s*:\s*""function""\s*,\s*)?""function""\s*:\s*\{");
        private static readonly Regex Jsonish = new Regex(@"\A\{\s*(?:\}|""[^""]*""\s*:|[\w-]+\s*:|<\|""\|>)");
        private static readonly Regex Arguments = new Regex(@"\A\s*(?:[\w-]+\s*=|(?:""[^""]*""|'[^']*')\s*\z)");

        private static Regex ours;
        private static bool Ours(string tool)
        {
            return (ours ?? (ours = new Regex(@"\A(?:functions\.)?(?:" + Names + @")\z"))).IsMatch(tool ?? "");
        }

        // A one-word name like clip, draw or power is an everyday word in code too, so written bare it needs JSON with a quoted
        // key after it, or a prefix that says it's a call.
        private static bool Plain(string tool) { return tool.IndexOf('_') < 0; }

        // Calls written outside any code: {"name": "open_app", "arguments": {...}}, open_app{...}, Gemma's call:open_app{...},
        // Kimi's functions.open_app:0 {...}, default_api.open_app(name="Discord"), or a whole reply like "use_computer: open notepad".
        private static string Bare(string text, List<string> found, bool coded = true)
        {
            Match words = SaidAsWords.Match(text);
            if (words.Success && words.Groups["tool"].Value.IndexOf('_') > 0) { found.Add("tool call written as words"); return ""; }
            bool[] code = coded ? Coded(text) : new bool[text.Length + 1];
            Dictionary<int, Match> pythons = new Dictionary<int, Match>();
            foreach (Match py in Python.Matches(text)) pythons[py.Index] = py;
            StringBuilder kept = new StringBuilder();
            Dictionary<int, int> closings = new Dictionary<int, int>();
            int at = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (code[i]) continue;
                if (text[i] == '{')
                {
                    int close = Closing(text, i, closings);
                    if (close < 0) continue;
                    string body = text.Substring(i, close - i + 1);
                    // A name sits right before its {, so only the last few words before it are read.
                    int near = Math.Max(at, i - 64);
                    Match before = NamedBefore.Match(text, near, i - near);
                    Match owner = Owner.Match(body);
                    bool call = (before.Success && Jsonish.IsMatch(body) && (before.Groups["prefix"].Success || !Plain(before.Groups["tool"].Value) || Quoted.IsMatch(body)))
                        || (owner.Success && Ours(owner.Groups["tool"].Value) && (Given.IsMatch(body) || text.Trim() == body.Trim()));
                    if (!call) { i = close; continue; }
                    int from = before.Success ? before.Index : i;
                    kept.Append(text, at, from - at).Append(' ');
                    at = close + 1;
                    i = close;
                    found.Add("tool call written as JSON");
                    continue;
                }
                Match py;
                if (pythons.TryGetValue(i, out py))
                {
                    int open = py.Index + py.Length - 1, close = Closing(text, open, closings);
                    string inside = close > open ? text.Substring(open + 1, close - open - 1) : "";
                    bool prefixed = py.Groups["prefix"].Success;
                    if (close > open && (prefixed || !Plain(py.Groups["tool"].Value) && Arguments.IsMatch(inside)))
                    {
                        if (py.Value.StartsWith("print(", StringComparison.Ordinal) && close + 1 < text.Length && text[close + 1] == ')') close++;
                        kept.Append(text, at, i - at).Append(' ');
                        at = close + 1;
                        i = close;
                        found.Add("tool call written as code");
                    }
                }
            }
            if (at == 0) return text;
            kept.Append(text, at, text.Length - at);
            return Regex.Replace(kept.ToString(), @"\[\s*(?:,\s*)*\]", " ");
        }

        // Where the bracket at open closes, skipping brackets in quotes, or -1 when it never does.
        private static int Closing(string text, int open) { return Closing(text, open, new Dictionary<int, int>()); }

        // The same, keeping where every bracket it passed closes, so a reply of thousands of { is read through once, not once per {.
        // A bracket passed outside quotes reads the same from there as from its own start, so what's kept is what it would find.
        private static int Closing(string text, int open, Dictionary<int, int> known)
        {
            int found;
            if (known.TryGetValue(open, out found)) return found;
            char opening = text[open], closing = opening == '{' ? '}' : opening == '[' ? ']' : ')';
            List<int> opened = new List<int>();
            char quote = '\0';
            for (int i = open; i < text.Length; i++)
            {
                char c = text[i];
                if (quote != '\0') { if (c == '\\') i++; else if (c == quote) quote = '\0'; }
                else if (c == '"' || (c == '\'' && opening == '(')) quote = c;
                else if (c == opening) opened.Add(i);
                else if (c == closing)
                {
                    int last = opened[opened.Count - 1];
                    opened.RemoveAt(opened.Count - 1);
                    known[last] = i;
                    if (opened.Count == 0) return i;
                }
            }
            foreach (int left in opened) known[left] = -1;
            return -1;
        }

        // Thinking in plain words at the start of a reply: each paragraph that talks to itself about them or its tools goes, up to
        // the first one that talks to them. Only her chat reply gets this. A glance's notes on the screen often say "the user".
        internal static string Unmused(string text) { return Unmused(text, null); }

        // This is a guess from words, so it never leaves her with nothing: a reply that reads as thinking all the way through is
        // kept whole. Her chat asks such a reply once more for just her answer (Mused), and shows what comes back.
        private static string Unmused(string text, List<string> into)
        {
            string[] parts;
            int skip = Musings(text, out parts);
            if (skip == 0) return text ?? "";
            if (into != null) into.Add("thinking in plain words");
            if (skip == parts.Length) return text;
            if (into == null) Note("thinking in plain words");
            string rest = string.Join("\n\n", parts, skip, parts.Length - skip);
            return Labelled.Replace(rest, "");
        }

        // Whether every paragraph of a reply reads as its own thinking in plain words.
        internal static bool Mused(string text)
        {
            string[] parts;
            int skip = Musings(text, out parts);
            return skip > 0 && skip == parts.Length;
        }

        // How many paragraphs at the start of a reply are a brain thinking to itself.
        private static int Musings(string text, out string[] parts)
        {
            parts = null;
            if (string.IsNullOrEmpty(text) || !(Musing.IsMatch(text) || ThinkingLabel.IsMatch(text.TrimStart()))) return 0;
            parts = Regex.Split(text.Trim(), @"\r?\n\s*\r?\n");
            if (ThinkingLabel.IsMatch(parts[0]))
                for (int i = 1; i < parts.Length; i++) if (Labelled.IsMatch(parts[i])) return i;
            int skip = 0;
            while (skip < parts.Length)
            {
                string part = parts[skip];
                bool about = Musing.IsMatch(part), itself = ToItself.IsMatch(part);
                if (!(skip == 0 ? about && itself : about || itself || ThinkingLabel.IsMatch(part))) break;
                skip++;
            }
            return skip;
        }

        // Every kind of leak in a reply, without changing it.
        internal static List<string> Kinds(string text)
        {
            List<string> found = new List<string>();
            Unmused(Spoken(Thoughts(text ?? "", found), found), found);
            return found;
        }

        // Whether any of a reply was thinking, so an empty one says the model only thought.
        internal static bool Thought(string text)
        {
            foreach (string kind in Kinds(text)) if (kind.IndexOf("think", StringComparison.Ordinal) >= 0 || kind.StartsWith("gpt-oss", StringComparison.Ordinal)) return true;
            return false;
        }

        // Every call to her tools a brain wrote out as text, in any of the shapes models write them, so her hands run them.
        internal static List<KeyValuePair<string, ToolArguments>> Calls(string reply)
        {
            string text = Brain.Visible(reply ?? "");
            Shapes shapes = Shaped;
            text = shapes.FunctionJson.Replace(text, "$1$2");
            text = shapes.FunctionPairs.Replace(text, delegate(Match call) { return Paired(call.Groups[1].Value, call.Groups[2].Value, @"<parameter=([\w-]+)>\s*(.*?)\s*</parameter>"); });
            text = shapes.Invoke.Replace(text, delegate(Match call) { return Paired(call.Groups[1].Value, call.Groups[2].Value, @"<parameter\s+name=""([\w-]+)""\s*>\s*(.*?)\s*</parameter>"); });
            text = shapes.DeepSeek.Replace(text, "$1$2");
            text = shapes.Kimi.Replace(text, "$1");
            // OpenAI's own shape copied as text: {"type": "function", "function": {"name": ..., "arguments": "{...}"}}.
            Dictionary<int, int> closings = new Dictionary<int, int>();
            for (Match wrap = OpenAiWrap.Match(text); wrap.Success; wrap = OpenAiWrap.Match(text, wrap.Index + 1))
            {
                int start = wrap.Index + wrap.Length - 1, end = Closing(text, start, closings), after = end + 1;
                while (after > 0 && after < text.Length && char.IsWhiteSpace(text[after])) after++;
                if (end < 0 || after >= text.Length || text[after] != '}') continue;
                text = text.Substring(0, wrap.Index) + text.Substring(start, end - start + 1) + text.Substring(after + 1);
                closings.Clear();
            }
            text = shapes.StringArguments.Replace(text, delegate(Match call) { string args = Unquoted(call.Groups[2].Value); return args != null && args.TrimStart().StartsWith("{", StringComparison.Ordinal) ? call.Groups[1].Value + args : call.Value; });
            text = shapes.Python.Replace(text, delegate(Match call)
            {
                if (!call.Groups["prefix"].Success && Plain(call.Groups["tool"].Value)) return call.Value;
                StringBuilder json = new StringBuilder("{");
                foreach (Match pair in Regex.Matches(call.Groups["args"].Value, @"([\w-]+)\s*=\s*(?:""((?:[^""\\]|\\.)*)""|'([^']*)'|([\w.+-]+))"))
                {
                    if (json.Length > 1) json.Append(',');
                    string value = pair.Groups[2].Success ? Unquoted(pair.Groups[2].Value) ?? pair.Groups[2].Value : pair.Groups[3].Success ? pair.Groups[3].Value : pair.Groups[4].Value;
                    json.Append(Voice.Quote(pair.Groups[1].Value)).Append(':').Append(Voice.Quote(value));
                }
                return json.Length == 1 ? call.Value : call.Groups["tool"].Value + json.Append('}');
            });
            List<KeyValuePair<string, ToolArguments>> found = Hands.Written(text);
            string tool;
            ToolArguments given;
            if (found.Count == 0 && Hands.Typed(text, out tool, out given)) found.Add(new KeyValuePair<string, ToolArguments>(tool, given));
            return found;
        }

        // The calls she took out of her words, for her chat to run. One still in what she shows, like np.clip(a=x) in a Python
        // example or .clip { } in CSS, is theirs to read and never runs.
        internal static List<KeyValuePair<string, ToolArguments>> Taken(string reply)
        {
            List<KeyValuePair<string, ToolArguments>> all = Calls(reply);
            if (all.Count == 0) return all;
            List<string> shown = new List<string>();
            foreach (KeyValuePair<string, ToolArguments> call in Calls(Spoken(Brain.Visible(reply ?? ""), new List<string>()))) shown.Add(Hands.CallKey(call.Key, call.Value));
            List<KeyValuePair<string, ToolArguments>> taken = new List<KeyValuePair<string, ToolArguments>>();
            foreach (KeyValuePair<string, ToolArguments> call in all)
                if (!shown.Remove(Hands.CallKey(call.Key, call.Value))) taken.Add(call);
            return taken;
        }

        // Her tools' call shapes, built once from their names.
        private sealed class Shapes
        {
            internal Regex FunctionJson, FunctionPairs, Invoke, DeepSeek, Kimi, StringArguments, Python;
        }

        private static Shapes shaped;
        private static Shapes Shaped
        {
            get
            {
                if (shaped != null) return shaped;
                string tools = Names;
                return shaped = new Shapes
                {
                    // Each stops at the next call's opener, so a reply of openers that never close is read through once.
                    FunctionJson = new Regex(@"<function=(" + tools + @")>\s*(\{(?:(?!<function=).)*?\})\s*</function>", Loose),
                    FunctionPairs = new Regex(@"<function=(" + tools + @")>((?:(?!<function=).)*?)</function>", Loose),
                    Invoke = new Regex(@"<invoke\s+name=""(" + tools + @")""\s*>((?:(?!<invoke\b).)*?)</invoke>", Loose),
                    DeepSeek = new Regex(@"<｜tool▁call▁begin｜>\s*function\s*<｜tool▁sep｜>\s*(" + tools + @")\s*(?:```(?:json)?)?\s*(\{(?:(?!<｜tool▁call▁begin｜>).)*?\})\s*(?:```)?\s*<｜tool▁call▁end｜>", Loose),
                    Kimi = new Regex(@"\bfunctions\.(" + tools + @")(?::\d+)?\s*(?:<\|tool_call_argument_begin\|>)?\s*(?=\{)"),
                    StringArguments = new Regex(@"\{\s*""name""\s*:\s*""(" + tools + @")""\s*,\s*""arguments""\s*:\s*""((?:[^""\\]|\\.)*)""\s*\}"),
                    // Each name=value is taken whole, so a call left open like open_app(a=bbb=bbb... fails at once instead of trying
                    // every way to split it. Not after a dot, so np.clip(a=x) is numpy's.
                    Python = new Regex(@"(?:(?<prefix>\bprint\(\s*(?:default_api\.|functions\.)?|\bdefault_api\.|\bfunctions\.)|(?<![\p{L}\p{N}_.]))(?<tool>" + tools + @")\((?<args>(?>\s*[\w-]+\s*=\s*(?:""(?:[^""\\]|\\.)*""|'[^']*'|[\w.+-]+)\s*,?)*)\s*\)\)?"),
                };
            }
        }

        // <parameter=name>Discord</parameter> pairs as the JSON her hands read.
        private static string Paired(string tool, string inside, string pattern)
        {
            StringBuilder json = new StringBuilder("{");
            foreach (Match pair in Regex.Matches(inside, pattern, Loose))
            {
                if (json.Length > 1) json.Append(',');
                json.Append(Voice.Quote(pair.Groups[1].Value)).Append(':').Append(Voice.Quote(pair.Groups[2].Value));
            }
            return tool + json.Append('}');
        }

        // A JSON string's insides, unescaped, or null when they don't read.
        private static string Unquoted(string inside)
        {
            try
            {
                using (MemoryStream data = new MemoryStream(Encoding.UTF8.GetBytes("\"" + inside + "\"")))
                    return (string)new DataContractJsonSerializer(typeof(string)).ReadObject(data);
            }
            catch (SerializationException) { return null; }
        }
    }
}

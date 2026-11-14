using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;

namespace CapriDesktop
{
    internal partial class Companion
    {
        // Her brain's own thinking and its tool calls written as text never reach her chat, in every shape models write them, and
        // honest replies about code, JSON, XML or a plain < keep every word (Lachy, 12 Nov: "AI's are leaking their thought
        // processes or their tool use into the chat"). Stand-in brains only: no real model is asked.
        private static void SmokeLeaks(List<string> checks)
        {
            string[,] shapes =
            {
                { "<think>The user said hi. I should greet them.</think>Hey choom.", "Hey choom." },
                { "The user said hi. I should greet them.\n</think>\n\nHey choom.", "Hey choom." },
                { "<thinking>Plan a greeting.</thinking>Hey choom.", "Hey choom." },
                { "<reasoning>Plan a greeting.</reasoning>Hey choom.", "Hey choom." },
                { "<thought>Plan a greeting.</thought>Hey choom.", "Hey choom." },
                { "<seed:think>Plan.</seed:think>Hey choom.", "Hey choom." },
                { "[THINK]Plan a greeting.[/THINK]Hey choom.", "Hey choom." },
                { "◁think▷Plan a greeting.◁/think▷Hey choom.", "Hey choom." },
                { "<|begin_of_thought|>Plan.<|end_of_thought|><|begin_of_solution|>Hey choom.<|end_of_solution|>", "Hey choom." },
                { "<think>Plan.</think><answer>Hey choom.</answer>", "Hey choom." },
                { "<think>Still going and it never closes", "" },
                { "Hey choom.<thi", "Hey choom." },
                { "<|channel|>analysis<|message|>User says hi. Greet back.<|end|><|start|>assistant<|channel|>final<|message|>Hey choom.<|return|>", "Hey choom." },
                { "analysisUser says hi. Greet back.assistantfinalHey choom.", "Hey choom." },
                { "<|channel>thought\nPlan.<channel|>Hey choom.", "Hey choom." },
                { "Hey choom!<|im_end|>", "Hey choom!" },
                { "Hey choom!<|eot_id|>", "Hey choom!" },
                { "On it.<tool_call>\n{\"name\": \"open_app\", \"arguments\": {\"name\": \"Steam\"}}\n</tool_call>", "On it." },
                { "On it.<tool_call>{\"name\": \"open_app\", \"arguments\": {\"name\": \"Ste", "On it." },
                { "<function=open_app>{\"name\": \"Steam\"}</function>", "" },
                { "<function=open_app>\n<parameter=name>\nSteam\n</parameter>\n</function>", "" },
                { "<function_calls><invoke name=\"open_app\"><parameter name=\"name\">Steam</parameter></invoke></function_calls>", "" },
                { "[TOOL_CALLS][{\"name\": \"open_app\", \"arguments\": {\"name\": \"Steam\"}}]", "" },
                { "<|python_tag|>{\"name\": \"open_app\", \"parameters\": {\"name\": \"Steam\"}}<|eom_id|>", "" },
                { "```tool_code\nprint(default_api.open_app(name=\"Steam\"))\n```", "" },
                { "Sure.\n```json\n{\"name\": \"open_app\", \"arguments\": {\"name\": \"Steam\"}}\n```", "Sure." },
                { "{\"name\": \"open_app\", \"arguments\": {\"name\": \"Steam\"}}", "" },
                { "{\"type\": \"function\", \"function\": {\"name\": \"open_app\", \"arguments\": \"{\\\"name\\\": \\\"Steam\\\"}\"}}", "" },
                { "<|tool_calls_section_begin|><|tool_call_begin|>functions.open_app:0<|tool_call_argument_begin|>{\"name\": \"Steam\"}<|tool_call_end|><|tool_calls_section_end|>", "" },
                { "functions.open_app:0 {\"name\": \"Steam\"}", "" },
                { "<｜tool▁calls▁begin｜><｜tool▁call▁begin｜>function<｜tool▁sep｜>open_app\n```json\n{\"name\": \"Steam\"}\n```<｜tool▁call▁end｜><｜tool▁calls▁end｜>", "" },
                { "Opening it. open_app{\"name\": \"Steam\"}", "Opening it." },
                { "call:open_app{name:<|\"|>Steam<|\"|>}", "" },
                { "use_computer: open notepad", "" },
            };
            for (int i = 0; i < shapes.GetLength(0); i++)
            {
                string shown = Brain.Clean(shapes[i, 0]);
                if (shown != shapes[i, 1]) throw new Exception("A leak reached her chat: " + Voice.Quote(shapes[i, 0]) + " showed " + Voice.Quote(shown));
            }
            // Each written call to her tools is read back as the real call, in every shape above that has one.
            string[] callers =
            {
                "<tool_call>\n{\"name\": \"open_app\", \"arguments\": {\"name\": \"Steam\"}}\n</tool_call>",
                "<function=open_app>{\"name\": \"Steam\"}</function>",
                "<function=open_app>\n<parameter=name>\nSteam\n</parameter>\n</function>",
                "<function_calls><invoke name=\"open_app\"><parameter name=\"name\">Steam</parameter></invoke></function_calls>",
                "[TOOL_CALLS][{\"name\": \"open_app\", \"arguments\": {\"name\": \"Steam\"}}]",
                "<|python_tag|>{\"name\": \"open_app\", \"parameters\": {\"name\": \"Steam\"}}<|eom_id|>",
                "```tool_code\nprint(default_api.open_app(name=\"Steam\"))\n```",
                "```json\n{\"name\": \"open_app\", \"arguments\": {\"name\": \"Steam\"}}\n```",
                "{\"type\": \"function\", \"function\": {\"name\": \"open_app\", \"arguments\": \"{\\\"name\\\": \\\"Steam\\\"}\"}}",
                "<|tool_calls_section_begin|><|tool_call_begin|>functions.open_app:0<|tool_call_argument_begin|>{\"name\": \"Steam\"}<|tool_call_end|><|tool_calls_section_end|>",
                "<｜tool▁calls▁begin｜><｜tool▁call▁begin｜>function<｜tool▁sep｜>open_app\n```json\n{\"name\": \"Steam\"}\n```<｜tool▁call▁end｜><｜tool▁calls▁end｜>",
                "call:open_app{name:<|\"|>Steam<|\"|>}",
            };
            foreach (string written in callers)
            {
                List<KeyValuePair<string, ToolArguments>> read = Leaks.Calls(written);
                if (read.Count != 1 || read[0].Key != "open_app" || read[0].Value == null || read[0].Value.Name != "Steam") throw new Exception("A call written as text wasn't read as the real call: " + Voice.Quote(written) + " read " + read.Count);
            }
            if (Leaks.Calls("<think>open_app{\"name\": \"Steam\"}</think>Hi.").Count != 0) throw new Exception("A call in its thinking was read as one to run.");
            // Her honest replies keep every word: code, JSON, XML, a <think> tag she's explaining, a plain < and words like "remember:".
            string[] honest =
            {
                "A person in JSON looks like {\"name\": \"Alex\", \"age\": 30}.",
                "Wrap it in `<think>` tags and close it with `</think>`.",
                "2 < 3 and 5 > 4, always.",
                "I <3 you, choom.",
                "Remember: you've got this.",
                "The user manual says to hold the power button.",
                "I could draw {a star} for you.",
                "I'll listen (\"Hey Jude\" is great) later.",
                "Let me think... Cyberpunk, obviously.",
                "Use print(\"hi\") to test it.",
                "It's <b>bold</b> in HTML.",
                // Answers that talk about "the user" without a brain talking to itself: a sign-up page, an error, a study, a game.
                "The user wants to see a clear error when their password is too short, so show it right under the field.",
                "The user is not authorized means your token is missing or expired. Log in again and it should work.",
                "The user is asked to rate each screen from 1 to 5.\n\nThen you compare the scores across the two versions.",
                "The user has three lives and loses one each time they fall.\n\nWhen all three are gone, show the game over screen.",
                "The user said they love jazz. So here's a playlist for you: Kind of Blue, A Love Supreme.",
                "Okay so the user wants a login button? Put it top right, that's where people look.",
                "Analysis: your loop never ends because i never changes.",
                "Reasoning: cheaper, faster, and it's already installed.",
                "The right tool to use here is a torque wrench, not pliers.",
                "Looking at the available tools in Blender, the knife tool is your best bet.",
                "I'll use the brush tool for the sky, then the pen tool for the outlines.",
                // Tags mentioned inside a sentence.
                "Put your steps inside <reasoning> and </reasoning> tags, then the answer after.",
                "Gemini sometimes writes a <thought> block before the answer, so strip it.",
                "Close it with </think> and the model stops thinking.",
                "To ask for it, write [THINK] at the start and [/THINK] at the end.",
                "In HTML you strike text out with <s>this</s>",
                "In a table, each header cell is a <th",
                // Code that looks like her tools: numpy's clip, CSS classes, one-word functions, a sentence after a tool's name.
                "np.clip(a=arr, a_min=0, a_max=1) keeps every value between 0 and 1.",
                "Add .clip { overflow: hidden; } to your stylesheet.",
                "Add .media { display: flex; } and it lines up.",
                "Your component: video-clip {display: block;}",
                "In Python, power(base=2, exp=10) is 1024.",
                "stream(url=\"https://x.y\") opens the feed.",
                "Use remember(\"milk\") in your script.",
                "open_app: that's the tool I use to open your apps.",
                "pc_status: CPU 12%, RAM 40%.",
            };
            foreach (string line in honest)
                if (Brain.Clean(line) != System.Text.RegularExpressions.Regex.Replace(line, @"\s+", " ") || Leaks.Unmused(line) != line || Leaks.Kinds(line).Count != 0)
                    throw new Exception("An honest reply lost words: " + Voice.Quote(line) + " became " + Voice.Quote(Brain.Clean(line)));
            string python = "Here's how:\n```python\nx = np.clip(a=x, a_min=0, a_max=1)\n```";
            if (Leaks.Taken(python).Count != 0 || Leaks.Taken("Add .clip { overflow: hidden; } to your stylesheet.").Count != 0) throw new Exception("Code in an honest reply was taken for one of her tools and would run.");
            if (Leaks.Taken("On it. open_app{\"name\": \"Steam\"}").Count != 1) throw new Exception("A call taken out of her words didn't run.");
            // Replies that are long or built to be slow never hold her up.
            string[] slow =
            {
                new string('{', 20000),
                string.Concat(System.Linq.Enumerable.Repeat("{}", 10000)),
                string.Concat(System.Linq.Enumerable.Repeat("a {b} ", 4000)),
                string.Concat(System.Linq.Enumerable.Repeat("Here's an honest paragraph about code, with a {brace} and a (paren) and \"quotes\". ", 180)),
                "open_app(a=" + new string('b', 20000),
                "open_app(a=" + string.Concat(System.Linq.Enumerable.Repeat("bbb=", 5000)),
                string.Concat(System.Linq.Enumerable.Repeat("open_app(a=b ", 1500)),
                "open_app(a=\"" + new string('x', 20000),
                string.Concat(System.Linq.Enumerable.Repeat("\"{", 10000)),
                string.Concat(System.Linq.Enumerable.Repeat("<thi", 5000)),
                string.Concat(System.Linq.Enumerable.Repeat("<think> ", 2500)),
                string.Concat(System.Linq.Enumerable.Repeat("</think> x ", 2000)),
                string.Concat(System.Linq.Enumerable.Repeat("[TOOL_CALLS] ", 1500)),
                string.Concat(System.Linq.Enumerable.Repeat("[TOOL_CALLS][ ", 1300)),
                string.Concat(System.Linq.Enumerable.Repeat("<function=open_app> ", 1000)),
                string.Concat(System.Linq.Enumerable.Repeat("<invoke name=\"open_app\"> ", 800)),
                string.Concat(System.Linq.Enumerable.Repeat("```\nx\n", 3000)),
                string.Concat(System.Linq.Enumerable.Repeat("The user is asking me to do it. \n\n", 1000)),
                string.Concat(System.Linq.Enumerable.Repeat("{\"name\": \"open_app\", \"x\": ", 800)),
                string.Concat(System.Linq.Enumerable.Repeat("print(default_api.open_app(name=\"", 800)),
                string.Concat(System.Linq.Enumerable.Repeat("call:open_app{name:", 1500)),
            };
            Brain.Clean("warm up");
            Leaks.Taken("warm up open_app{\"name\": \"Steam\"}");
            long slowest = 0;
            foreach (string reply in slow)
            {
                System.Diagnostics.Stopwatch took = System.Diagnostics.Stopwatch.StartNew();
                Brain.Clean(reply);
                Leaks.Unmused(reply);
                Leaks.Mused(reply);
                Leaks.Kinds(reply);
                Leaks.Taken(reply);
                slowest = Math.Max(slowest, took.ElapsedMilliseconds);
                if (took.ElapsedMilliseconds > 1500) throw new Exception("A hostile reply took " + took.ElapsedMilliseconds + " ms to clean: " + Voice.Quote(reply.Substring(0, 40)) + "...");
            }
            string fenced = "Like this:\n```xml\n<think>hi</think>\n<tool_call>{\"name\": \"x\"}</tool_call>\n```";
            if (Brain.Visible(fenced) != fenced || Brain.Clean(fenced).IndexOf("<think>hi</think>", StringComparison.Ordinal) < 0 || Brain.Clean(fenced).IndexOf("<tool_call>", StringComparison.Ordinal) < 0) throw new Exception("Code about tags lost its tags: " + Brain.Clean(fenced));
            string json = "Sure:\n```json\n{\"name\": \"Alex\", \"arguments\": {\"age\": 30}}\n```";
            if (Brain.Clean(json).IndexOf("\"Alex\"", StringComparison.Ordinal) < 0 || Leaks.Calls(json).Count != 0) throw new Exception("A JSON example that isn't one of her tools was taken for a call.");
            if (!Leaks.Mused("The user is asking me to close discord. Looking at the tools, the correct tool call would be app_window with name: \"discord\", action:")) throw new Exception("Thinking in plain words reached her chat.");
            if (!Leaks.Mused("Okay, let's break this down. The user is asking me to play some music. Looking at the tools, the correct tool call would be play_music with app: \"spotify\", kind:")) throw new Exception("Thinking in plain words reached her chat.");
            if (Leaks.Unmused("The user greets me. I'll greet them back.\n\nHey choom, what's up?") != "Hey choom, what's up?") throw new Exception("Her answer after thinking in plain words was lost.");
            if (Leaks.Unmused("The user wants a joke. I’ll tell one.\n\nLooking at my tools, none fit.\n\nWhy did the netrunner cross the road?") != "Why did the netrunner cross the road?") throw new Exception("Thinking in plain words over several paragraphs reached her chat.");
            if (Leaks.Unmused("Thinking: they said hi, keep it short.\n\nAnswer: Hey choom!") != "Hey choom!") throw new Exception("Labelled thinking reached her chat.");
            checks.Add("PASS leak shapes: " + shapes.GetLength(0) + " kinds of thinking, template tokens and written tool calls never show, " + callers.Length + " written call shapes read back as the real call, a call inside its thinking never runs, " + (honest.Length + 2) + " honest replies about code, JSON, XML, tags, a plain <, \"the user\" and her tools' names keep every word, code that looks like her tools never runs, and " + slow.Length + " hostile 20,000 character replies clean in " + slowest + " ms at most");

            string openAiWas = Brain.Nvidia, ollamaWas = Brain.Ollama, notesWas = Brain.MemoryPath, memoriesWas = Brain.MemoriesPath, transcriptsWas = Brain.TranscriptPath, logWas = Brain.LogPath;
            bool keepingWas = Brain.Keeping;
            Func<string> gameWas = Hands.GameOpen;
            string log = Path.Combine(Path.GetTempPath(), "capri-leaks-" + Guid.NewGuid().ToString("N") + ".txt");
            TcpListener fake = null;
            try
            {
                Brain.MemoryPath = Brain.MemoriesPath = Brain.TranscriptPath = null;
                Brain.LogPath = log;
                Brain.Keeping = false;
                Hands.GameOpen = delegate { return null; };
                Func<string, string> says = delegate(string text) { return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":" + Voice.Quote(text) + "},\"finish_reason\":\"stop\"}]}"; };
                List<string> asked = new List<string>();
                Brain.Nvidia = FakeServer(new[]
                {
                    says("The user says hey. I should greet them warmly.\n</think>\n\nHey choom!"),
                    says("Let's fix that.\n<tool_call>\n{\"name\": \"open_app\", \"arguments\": {\"name\": \"Steam\"}}\n</tool_call>"),
                    says("<think>The user says hello again. Greet them.</think>"),
                    says("Hello again."),
                    says("<think>Thinking only.</think>"),
                    says("<think>Still only thinking.</think>"),
                    says("Okay, the user wants a joke. I should tell one about netrunners."),
                    says("Why did the netrunner cross the road? To jack in on the other side."),
                    says("Sure:\n```json\n{\"name\": \"Alex\", \"age\": 30}\n```\nAnd 2 < 3."),
                    says("The user wants to see a clear error when their password is too short, so show it right under the field."),
                    says("The user is asking me to pick. I should pick the blue one."),
                    says("The user is asking me to pick. I should pick the blue one, it's calmer."),
                    says("Here's how:\n```python\nx = np.clip(a=x, a_min=0, a_max=1)\n```"),
                    says("<tool_call>\n{\"name\": \"open_app\", \"arguments\": {\"name\": \"Steam\"}}\n</tool_call>"),
                    says("Opened it for you."),
                }, asked, out fake);
                List<string> did = new List<string>();
                Brain brain = new Brain { Model = "qwen/qwen3-next-80b-a3b-thinking", Remote = new Cloud("nvapi-test", "qwen/qwen3-next-80b-a3b-thinking") };
                brain.Act = delegate(string tool, ToolArguments args) { did.Add(tool + ":" + (args == null ? "" : args.Name)); return tool == "open_app" ? "Opened " + args.Name + "." : "No result."; };
                string greeted = brain.Ask("hey");
                if (greeted != "Hey choom!") throw new Exception("Thinking with no opening tag reached her chat: " + greeted);
                List<ChatMessage> history = brain.History;
                if (history.Count != 2 || history[1].Content != "Hey choom!") throw new Exception("Her history kept the thinking: " + (history.Count > 1 ? history[1].Content : "nothing"));
                string bored = brain.Ask("honestly i'm kinda bored rn");
                if (did.Count != 0 || bored != "Let's fix that.") throw new Exception("A call written as text in her chat ran, or her words around it were lost: " + bored + " / " + string.Join(",", did.ToArray()));
                string again = brain.Ask("hello again");
                if (again != "Hello again.") throw new Exception("A reply that was all thinking wasn't asked again for her answer: " + again);
                string[] sent;
                lock (asked) sent = asked.ToArray();
                if (sent.Length != 4 || sent[3].IndexOf("Your last reply had no words for them in it", StringComparison.Ordinal) < 0 || sent[2].IndexOf("Your last reply had no words", StringComparison.Ordinal) >= 0) throw new Exception("Her second ask for just the answer was wrong: " + sent.Length + " asks");
                foreach (string body in sent) if (body.IndexOf("I should greet them warmly", StringComparison.Ordinal) >= 0 || body.IndexOf("tool_call>", StringComparison.Ordinal) >= 0) throw new Exception("Thinking or a written call was fed back to her brain.");
                string problem = null;
                try { brain.Ask("hi?"); }
                catch (BrainException error) { problem = error.Message; }
                if (problem != Brain.Answerless) throw new Exception("Two replies of only thinking didn't say the model only thought: " + problem);
                string joke = brain.Ask("tell me a joke");
                if (joke != "Why did the netrunner cross the road? To jack in on the other side.") throw new Exception("Thinking in plain words reached her chat or her answer was lost: " + joke);
                string honestReply = brain.Ask("how would you write a person in json");
                if (honestReply.IndexOf("{\"name\": \"Alex\", \"age\": 30}", StringComparison.Ordinal) < 0 || honestReply.IndexOf("2 < 3", StringComparison.Ordinal) < 0) throw new Exception("An honest JSON reply lost words: " + honestReply);
                int asks;
                lock (asked) asks = asked.Count;
                string signUp = brain.Ask("what should my sign-up page do");
                lock (asked) asks = asked.Count - asks;
                if (signUp != "The user wants to see a clear error when their password is too short, so show it right under the field." || asks != 1) throw new Exception("An honest reply about the user was taken for thinking: " + signUp + " after " + asks + " asks");
                lock (asked) asks = asked.Count;
                string picked = brain.Ask("blue or green");
                lock (asked) asks = asked.Count - asks;
                if (picked != "The user is asking me to pick. I should pick the blue one, it's calmer." || asks != 2) throw new Exception("A reply that read as thinking twice wasn't shown as it came: " + picked + " after " + asks + " asks");
                did.Clear();
                string clamped = brain.Ask("how do i clamp values in numpy");
                if (did.Count != 0 || clamped.IndexOf("np.clip(a=x, a_min=0, a_max=1)", StringComparison.Ordinal) < 0) throw new Exception("Code in an honest reply ran one of her tools or lost words: " + clamped + " / " + string.Join(",", did.ToArray()));
                // A chat reply that's only a call, like one copied from a persona, says nothing and runs nothing, and she isn't asked
                // again to put words to a call that never ran.
                lock (asked) asks = asked.Count;
                string onlyCall = null;
                bool stopped = false;
                try { onlyCall = brain.Ask("what does that code mean?"); }
                catch (BrainException) { stopped = true; }
                lock (asked) asks = asked.Count - asks;
                if (!stopped || did.Count != 0 || asks != 1) throw new Exception("A chat reply of only a written call ran, was shown or was asked about again: " + onlyCall + " / " + string.Join(",", did.ToArray()) + " after " + asks + " asks");
                fake.Stop();

                // Her own brain on their PC streams: a tag split across pieces, and one that never closes.
                asked.Clear();
                Func<string[], string> streamed = delegate(string[] pieces)
                {
                    System.Text.StringBuilder events = new System.Text.StringBuilder();
                    for (int i = 0; i < pieces.Length; i++) events.Append("data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":" + Voice.Quote(pieces[i]) + "}" + (i == pieces.Length - 1 ? ",\"finish_reason\":\"stop\"" : "") + "}]}\n\n");
                    return events.Append("data: [DONE]\n\n").ToString();
                };
                string address = FakeServer(new[]
                {
                    streamed(new[] { "<thi", "nk>The user", " says yo.</th", "ink>", "Yo, ", "choom." }),
                    streamed(new[] { "<think>", "The user asks the time", " and I keep going" }),
                    streamed(new[] { "Pretty chill ", "day." }),
                    streamed(new[] { "Sure.<tool", "_call>{\"name\": \"open_app\", \"argu", "ments\": {\"name\": \"Steam\"}}</tool_call>" }),
                    streamed(new[] { "Steam's ", "up." }),
                }, asked, out fake, 200, 1, true, 0, "text/event-stream");
                Brain local = new Brain { Model = "qwen3-4b", Remote = new Cloud(Brain.LocalPort(address), "qwen3-4b") };
                local.Act = brain.Act;
                did.Clear();
                string yo = local.Ask("yo");
                if (yo != "Yo, choom.") throw new Exception("A thinking tag split across a stream reached her chat: " + yo);
                string late = local.Ask("how was your day");
                if (late != "Pretty chill day.") throw new Exception("An unclosed thinking tag in a stream wasn't asked again: " + late);
                string steamed = local.Ask("ugh nothing to do");
                if (did.Count != 0 || steamed != "Sure.") throw new Exception("A streamed written call ran or showed: " + steamed + " / " + string.Join(",", did.ToArray()));
                fake.Stop();

                // Ollama's reply comes back as it was written, and gets the same cleaning.
                asked.Clear();
                Brain.Ollama = FakeServer(new[] { "{\"message\":{\"role\":\"assistant\",\"content\":\"analysisUser greets. Reply.assistantfinalHey there, choom.\"},\"done\":true}" }, asked, out fake);
                Brain ollama = new Brain { Model = "gpt-oss:20b" };
                ollama.Act = brain.Act;
                string oss = ollama.Ask("hey there");
                if (oss != "Hey there, choom.") throw new Exception("gpt-oss's analysis channel reached her chat on Ollama: " + oss);
                fake.Stop();

                string[] lines = File.Exists(log) ? File.ReadAllLines(log) : new string[0];
                string wanted = "| leak | - | nvidia qwen/qwen3-next-80b-a3b-thinking: thinking with no opening tag taken out of her reply";
                if (!Array.Exists(lines, line => line.EndsWith(wanted, StringComparison.Ordinal))) throw new Exception("Her log didn't name the brain, model and kind of leak: " + string.Join(" / ", lines));
                if (!Array.Exists(lines, line => line.IndexOf("ollama gpt-oss:20b: gpt-oss analysis channel", StringComparison.Ordinal) >= 0) || !Array.Exists(lines, line => line.IndexOf("local qwen3-4b: tool call tags", StringComparison.Ordinal) >= 0)) throw new Exception("Her log missed a brain's leak: " + string.Join(" / ", lines));
                foreach (string line in lines) if (line.IndexOf("greet them", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("Steam", StringComparison.Ordinal) >= 0 || line.IndexOf("netrunner", StringComparison.Ordinal) >= 0) throw new Exception("Her log kept what a leak said: " + line);
                checks.Add("PASS leaks through her real chat with stand-in brains: NVIDIA, a streamed brain on their PC and Ollama never show thinking (no opening tag, split across a stream, unclosed, plain words or gpt-oss channels) in her bubble, voice or history, or feed it back to her brain; a call written as text in a chat reply is never shown, said or run, and the words around it stay, and a reply of only a call stops with no second ask; a reply that was all thinking is asked once more for her answer, and twice says the model only thought; an honest JSON reply keeps its words; an honest reply about \"the user\" is asked once and keeps every word, one that reads as thinking twice is shown as it came, and numpy code in a reply never runs her clip tool; her log names the brain, model and kind of each leak and never its words");
                checks.Add("LIMIT leak checks use stand-in brains only: no real reasoning model, Claude Code, Codex or Ollama was asked");
            }
            finally
            {
                if (fake != null) fake.Stop();
                Brain.Nvidia = openAiWas;
                Brain.Ollama = ollamaWas;
                Brain.MemoryPath = notesWas;
                Brain.MemoriesPath = memoriesWas;
                Brain.TranscriptPath = transcriptsWas;
                Brain.LogPath = logWas;
                Brain.Keeping = keepingWas;
                Hands.GameOpen = gameWas;
                try { File.Delete(log); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}

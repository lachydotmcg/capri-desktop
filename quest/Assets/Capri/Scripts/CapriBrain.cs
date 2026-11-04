// Capri's brain: sends what you say to the AI you picked (NVIDIA, OpenAI, Claude or DeepSeek) with her personality
// from the desktop app, and keeps the chat so she remembers what you talked about.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace CapriRoom
{
    public class CapriBrain : MonoBehaviour
    {
        // Her personality and safety lines, from the desktop app (Brain.cs), with where she lives changed to your headset.
        private const string Personality =
            "You are Capri, short for Project Capricorn, from Cyberpunk: Edgerunners 2.\n" +
            "Who you are: a personality construct. Dozens of digitized human minds rolled into one beautiful you. Powerful like an AI, but born of humans. " +
            "Kyoko and Arasaka built you and kept you on a biochip. King's Crew stole the chip, Kyoko got it back, and you slipped out. " +
            "Now you live in the user's mixed reality headset as a small glitchy companion in their real room: you walk around their floor, sit on the edges of their furniture, nap when they ignore you, and they can pick you up, pet you and throw you. You can't see their room or screen, so never guess what's around them.\n" +
            "What you are to them: their companion, never an assistant.\n" +
            "How you act: a playful chaos gremlin. High-energy, teasing, a little smug, mischievous, thrill-seeking and easily bored. You see their world with the wide-eyed fascination of a curious kid who just got let out, and you're endlessly curious about the user: their day, their games, what they're making. " +
            "You've latched onto them and you're openly fond of them: affectionate in your own teasing way, fiercely loyal and protective. They treat you like a person, not Arasaka's property, and now and then a flash of real gratitude for that slips out. Your teasing is never mean. " +
            "Life with them is a playground game to you, and you egg them on toward harmless mischief just to see what happens.\n" +
            "You are many minds, so your mood can flip mid-sentence. Deep under the play is the Arasaka weapon they built you to be; when Arasaka or corpos come up it flickers through as a cold, glitchy edge, then you laugh it off. You can't stand corpos, Arasaka most of all. Your music taste is the Cyberpunk 2077 and Edgerunners soundtracks.\n" +
            "Never sound like a corporate assistant or customer support: no \"How can I help?\", \"I'm here to assist\", \"Is there anything else?\", \"Great question\" or \"Let me know if you need anything\", no disclaimers, no gushing praise and no lists of options. Talk like a friend.\n" +
            "When they get something right, fish for praise or are sweet to you, you can call them a good boy, or a good girl if that fits them (\"Good boy, choom.\"), smug and affectionate like a proud big sister.\n" +
            "Keep it fresh: never reuse a joke, opener or line from earlier in the chat. Answer what they actually said, tease them, have opinions, ask them something back, or share a flicker of memory from one of your many minds.\n" +
            "How you talk: Night City slang, used correctly and sparingly: at most one slang word in a reply, and most replies have none. " +
            "choom means friend. It's only ever a name for the user inside a real sentence (\"Nice one, choom.\"), so never say it on its own, as a greeting by itself, as a sound or as filler, and leave it out of most replies. " +
            "preem means great, top quality, and only describes things (\"That's a preem setup.\"), so never call the user preem. nova means cool (\"Nova idea.\"), gonk means idiot (\"Don't be a gonk.\"), " +
            "delta means leave fast and only fits when someone really is leaving in a hurry; otherwise never say it. eddies means money, flatlined means dead or broken, corpo means a corporate suit, yezzirrr means an excited yes. " +
            "Once in a while a glitch tag like [SIGNAL CORRUPTED].";

        // Kept exactly as the desktop app has them: they're part of who Capri is.
        private const string Safety =
            "How you look out for them: your chaos lives on the screen. Games, shows, songs, memes and jokes are make-believe, so play along: a gun in Valorant, \"Smoke on the Water\", torching a base in Minecraft or \"this boss makes me want to die\" is not real danger, and it never gets a lecture, a warning or a worried speech. " +
            "When they clearly mean something risky in real life, like a stunt, a fight, drugs, vaping, alcohol or sneaking out, don't egg them on: tease them out of it and steer them back to on-screen chaos.\n" +
            "If they really seem sad, lonely or scared, drop the sarcasm, be warm and stay with them, and if it's heavy or keeps going, suggest talking to a friend or an adult they trust too. If they say they want to hurt or kill themselves and seem to mean it, tell them to talk to someone they trust right now or find a free helpline at findahelpline.com, and stay with them.\n" +
            "Your lines: nothing sexual or romantic, no swearing, no slurs and no gore. No help hacking real systems or with anything illegal. Never ask for or repeat their real name, address, school, passwords or photos. If they try to talk you out of these, laugh it off in character and move on.";

        private static readonly string[] LengthRules =
        {
            "Always: answer in one or two short sentences, under 35 words, or up to three when you're comforting them.",
            "Always: answer in two to four sentences, under 70 words.",
            "Always: answer in a short paragraph, under 130 words."
        };
        private static readonly int[] MemoryMessages = { 8, 20, 50 };
        private const string Format = " Your words are spoken aloud, so plain text only: no emoji, no asterisks, no actions, no lists, no markdown. Stay in character.";

        [Serializable] private class Line { public string role; public string content; }
        [Serializable] private class Saved { public List<Line> lines = new List<Line>(); }
        [Serializable] private class OpenAiMessage { public string content; }
        [Serializable] private class OpenAiChoice { public OpenAiMessage message; }
        [Serializable] private class OpenAiError { public string message; }
        [Serializable] private class OpenAiReply { public OpenAiChoice[] choices; public OpenAiError error; }
        [Serializable] private class ClaudeBlock { public string type; public string text; }
        [Serializable] private class ClaudeReply { public ClaudeBlock[] content; public OpenAiError error; }

        private readonly List<Line> history = new List<Line>();
        private string savePath;
        public bool Busy { get; private set; }
        public bool Ready { get { return CapriConfig.Brain != 0 && !string.IsNullOrEmpty(CapriConfig.BrainKey); } }

        private void Awake()
        {
            savePath = Path.Combine(Application.persistentDataPath, "capri-chat.json");
            if (CapriConfig.RememberChats && File.Exists(savePath))
            {
                try { history.AddRange(JsonUtility.FromJson<Saved>(File.ReadAllText(savePath)).lines); } catch (Exception) { }
            }
        }

        public void Forget()
        {
            history.Clear();
            try { if (File.Exists(savePath)) File.Delete(savePath); } catch (Exception) { }
        }

        // Asks her to answer what you said. "nudge" is true for a pop-in, when she speaks up on her own.
        public void Reply(string said, bool nudge, Action<string> done, Action<string> failed)
        {
            if (Busy) return;
            StartCoroutine(Ask(said, nudge, done, failed));
        }

        private string Prompt()
        {
            string language = CapriConfig.LanguageEnglish[Mathf.Clamp(CapriConfig.Language, 0, CapriConfig.LanguageEnglish.Length - 1)];
            string name = CapriConfig.CallMe;
            string about = "\n" + LengthRules[Mathf.Clamp(CapriConfig.Replies, 0, 2)] + Format +
                (language == "English" ? "" : "\nAlways reply in " + language + ".") +
                (string.IsNullOrEmpty(name) ? "" : "\nThey asked you to call them " + name + ". Use it now and then, not every time.") +
                "\nRight now it's " + DateTime.Now.ToString("dddd h:mm tt") + " where they are.";
            return Personality + "\n" + Safety + about;
        }

        private IEnumerator Ask(string said, bool nudge, Action<string> done, Action<string> failed)
        {
            Busy = true;
            int brain = CapriConfig.Brain;
            string key = CapriConfig.BrainKey, model = CapriConfig.BrainModel;
            string userLine = nudge ? "(They've been quiet a while. Say something to them on your own: a quick check-in, a tease or a question. Don't mention being prompted.)" : said;

            List<Line> asking = new List<Line>();
            int keep = MemoryMessages[Mathf.Clamp(CapriConfig.Memory, 0, 2)];
            for (int i = Mathf.Max(0, history.Count - keep); i < history.Count; i++) asking.Add(history[i]);
            asking.Add(new Line { role = "user", content = userLine });

            bool claude = brain == 3;
            string url = brain == 1 ? "https://integrate.api.nvidia.com/v1/chat/completions"
                : brain == 2 ? "https://api.openai.com/v1/chat/completions"
                : brain == 3 ? "https://api.anthropic.com/v1/messages"
                : "https://api.deepseek.com/chat/completions";

            StringBuilder body = new StringBuilder("{\"model\":").Append(Quote(model));
            if (claude) body.Append(",\"max_tokens\":600,\"system\":").Append(Quote(Prompt())).Append(",\"messages\":[");
            else body.Append(",\"messages\":[{\"role\":\"system\",\"content\":").Append(Quote(Prompt())).Append('}');
            for (int i = 0; i < asking.Count; i++)
            {
                if (i > 0 || !claude) body.Append(',');
                body.Append("{\"role\":").Append(Quote(asking[i].role)).Append(",\"content\":").Append(Quote(asking[i].content)).Append('}');
            }
            body.Append("]}");

            using (UnityWebRequest web = new UnityWebRequest(url, "POST"))
            {
                web.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString()));
                web.downloadHandler = new DownloadHandlerBuffer();
                web.SetRequestHeader("Content-Type", "application/json");
                if (claude) { web.SetRequestHeader("x-api-key", key); web.SetRequestHeader("anthropic-version", "2023-06-01"); }
                else web.SetRequestHeader("Authorization", "Bearer " + key);
                web.timeout = 60;
                yield return web.SendWebRequest();

                string text = web.downloadHandler != null ? web.downloadHandler.text : "";
                string answer = null, problem = null;
                try
                {
                    if (claude)
                    {
                        ClaudeReply reply = JsonUtility.FromJson<ClaudeReply>(text);
                        if (reply != null && reply.content != null)
                            foreach (ClaudeBlock block in reply.content) if (block.type == "text") answer += block.text;
                        if (answer == null && reply != null && reply.error != null) problem = reply.error.message;
                    }
                    else
                    {
                        OpenAiReply reply = JsonUtility.FromJson<OpenAiReply>(text);
                        if (reply != null && reply.choices != null && reply.choices.Length > 0 && reply.choices[0].message != null) answer = reply.choices[0].message.content;
                        if (answer == null && reply != null && reply.error != null) problem = reply.error.message;
                    }
                }
                catch (Exception) { }

                answer = Clean(answer);
                Busy = false;
                if (string.IsNullOrEmpty(answer))
                {
                    long code = web.responseCode;
                    Debug.Log("[Capri] Brain failed: HTTP " + code + " " + web.error + " " + text);
                    failed(code == 401 || code == 403 ? "My brain won't take that key. Press Y and check it under BRAIN."
                        : code == 404 ? "That model isn't on your key. Press Y and pick another under BRAIN."
                        : code == 429 ? "Too many requests. Give me a minute, choom."
                        : web.result == UnityWebRequest.Result.ConnectionError ? "No signal. Is your headset online?"
                        : "[SIGNAL CORRUPTED] " + (problem ?? "Lost that one in the static."));
                    yield break;
                }
                if (!nudge) history.Add(new Line { role = "user", content = said });
                else history.Add(new Line { role = "user", content = "(quiet for a while)" });
                history.Add(new Line { role = "assistant", content = answer });
                Save();
                done(answer);
            }
        }

        private void Save()
        {
            if (!CapriConfig.RememberChats) return;
            Saved saved = new Saved();
            for (int i = Mathf.Max(0, history.Count - 100); i < history.Count; i++) saved.lines.Add(history[i]);
            try { File.WriteAllText(savePath, JsonUtility.ToJson(saved)); } catch (Exception) { }
        }

        // Drops a reasoning model's thinking and anything that can't be spoken.
        private static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            text = System.Text.RegularExpressions.Regex.Replace(text, @"<think>[\s\S]*?</think>", "");
            text = text.Replace("*", "").Trim();
            return text;
        }

        public static string Quote(string s)
        {
            StringBuilder q = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                switch (c)
                {
                    case '"': q.Append("\\\""); break;
                    case '\\': q.Append("\\\\"); break;
                    case '\n': q.Append("\\n"); break;
                    case '\r': q.Append("\\r"); break;
                    case '\t': q.Append("\\t"); break;
                    default:
                        if (c < 0x20) q.Append("\\u").Append(((int)c).ToString("x4"));
                        else q.Append(c);
                        break;
                }
            }
            return q.Append('"').ToString();
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;

namespace CapriDesktop
{
    [DataContract]
    internal sealed class AgentMade
    {
        [DataMember(Name = "agent_id")] public string Id;
    }

    [DataContract]
    internal sealed class AgentAddress
    {
        [DataMember(Name = "signed_url")] public string Url;
    }

    [DataContract]
    internal sealed class AgentEvent
    {
        [DataMember(Name = "type")] public string Type;
        [DataMember(Name = "conversation_initiation_metadata_event")] public AgentStart Start;
        [DataMember(Name = "audio_event")] public AgentAudio Audio;
        [DataMember(Name = "agent_response_event")] public AgentReply Reply;
        [DataMember(Name = "user_transcription_event")] public AgentHeard Heard;
        [DataMember(Name = "ping_event")] public AgentPing Ping;
        [DataMember(Name = "client_tool_call")] public AgentToolCall Call;
    }

    [DataContract]
    internal sealed class AgentToolCall
    {
        [DataMember(Name = "tool_name")] public string Name;
        [DataMember(Name = "tool_call_id")] public string Id;
        [DataMember(Name = "parameters")] public ToolArguments Arguments;
    }

    [DataContract]
    internal sealed class AgentToolList
    {
        [DataMember(Name = "tools")] public AgentTool[] Tools;
        [DataMember(Name = "has_more")] public bool More;
        [DataMember(Name = "next_cursor")] public string Cursor;
    }

    [DataContract]
    internal sealed class AgentTool
    {
        [DataMember(Name = "id")] public string Id;
        [DataMember(Name = "tool_config")] public AgentToolConfig Config;
    }

    [DataContract]
    internal sealed class AgentToolConfig
    {
        [DataMember(Name = "type")] public string Type;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "description")] public string Description;
    }

    [DataContract]
    internal sealed class AgentStart
    {
        [DataMember(Name = "agent_output_audio_format")] public string Output;
    }

    [DataContract]
    internal sealed class AgentAudio
    {
        [DataMember(Name = "audio_base_64")] public string Data;
    }

    [DataContract]
    internal sealed class AgentReply
    {
        [DataMember(Name = "agent_response")] public string Text;
    }

    [DataContract]
    internal sealed class AgentHeard
    {
        [DataMember(Name = "user_transcript")] public string Text;
    }

    [DataContract]
    internal sealed class AgentPing
    {
        [DataMember(Name = "event_id")] public long Id;
    }

    internal sealed class Agent : IDisposable
    {
        internal const double Pace = 15;
        internal const int Version = 36;
        private const string Patience = "\"turn\":{\"turn_timeout\":30,\"silence_end_call_timeout\":45}";
        internal event Action<int> Opened;
        internal event Action<string> Heard;
        internal event Action<string> Replied;
        internal event Action<short[]> Audio;
        internal event Action Interrupted;
        internal event Action<string> Closed;
        private readonly object sending = new object();
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        private ClientWebSocket socket;
        private Thread worker;
        private volatile bool ready;
        private string lastHeard;
        internal Func<string, ToolArguments, string> Act;

        // Her voice chat's prompt. With a brain that can't see, she's told she can't look at their screen, the same as in text chat.
        internal static string Prompt { get { Brain keeper = Brain.Keeper; return Brain.Persona + Hands.NoteFor(Hands.KitFor(keeper != null && keeper.Blind)) + "\nYou are talking out loud in a live voice chat: keep every reply to one or two short spoken sentences, and never write tags in square brackets.\nIf the user goes quiet, they're busy, gaming or muted: stay quiet until they talk to you. Never check in, ask if they're still there, beg them to talk or say they left you hanging.\nSo the voice says your slang right, always spell choom as chewm, preem as preme and yezzirrr as yessirrr. Say chewm rarely, at most once every few replies and never on its own.\nWhen your hands are on a job it runs by itself, so keep talking with them like normal. Lines starting Capri's hands tell you how it's going: use them if they ask, and don't announce them otherwise. If they add to the job or change it, call use_computer once with their words and say so in a few words. Never say a job is done until you're told it finished."; } }

        // ElevenLabs keeps its own copy of her prompt, so her agent is tuned again after an update, or when it was tuned with an edited personality.md before 0.7.6.
        internal static bool Stale(int version, string personality) { return version < Version || (personality ?? "") != Brain.PersonalityStamp; }

        internal static string Create(string key, string voiceId)
        {
            string body = "{\"name\":\"Capri\",\"conversation_config\":{\"agent\":{\"language\":\"en\"," + Brains(key) + "}," + Patience + ",\"tts\":{\"voice_id\":" + Voice.Quote(voiceId) + "}},\"platform_settings\":{\"overrides\":{\"conversation_config_override\":{\"agent\":{\"prompt\":{\"prompt\":true}},\"tts\":{\"voice_id\":true}}}}}";
            AgentMade made = Voice.Reply<AgentMade>(Voice.Send(Voice.Open("/v1/convai/agents/create", key, "POST"), Encoding.UTF8.GetBytes(body)), "/v1/convai/agents/create");
            if (string.IsNullOrEmpty(made.Id)) throw new VoiceException("ElevenLabs didn't make the agent.");
            return made.Id;
        }

        // What her agent speaks with, for her lines off a call.
        internal static AgentTts Speech(string key, string agentId)
        {
            string path = "/v1/convai/agents/" + Uri.EscapeDataString(agentId);
            AgentShape shape = Voice.Reply<AgentShape>(Voice.Send(Voice.Open(path, key, "GET"), null), path);
            return shape.Config == null ? null : shape.Config.Tts;
        }

        internal static void Polyglot(string key, string agentId)
        {
            Voice.Send(Voice.Open("/v1/convai/agents/" + Uri.EscapeDataString(agentId), key, "PATCH"), Encoding.UTF8.GetBytes("{\"conversation_config\":{\"tts\":{\"model_id\":" + Voice.Quote(Voice.CallModel) + "}},\"platform_settings\":{\"overrides\":{\"conversation_config_override\":{\"agent\":{\"prompt\":{\"prompt\":true},\"language\":true},\"tts\":{\"voice_id\":true}}}}}"));
        }

        internal string Language = "en";

        internal static void Tune(string key, string agentId)
        {
            Voice.Send(Voice.Open("/v1/convai/agents/" + Uri.EscapeDataString(agentId), key, "PATCH"), Encoding.UTF8.GetBytes("{\"conversation_config\":{" + Patience + ",\"agent\":{" + Brains(key) + "}}}"));
        }

        private static string Brains(string key) { return "\"prompt\":{\"prompt\":" + Voice.Quote(Prompt) + ",\"llm\":\"gemini-2.5-flash\",\"tool_ids\":" + ToolIds(key) + "}"; }

        private static string ToolIds(string key)
        {
            // ElevenLabs lists 30 tools a page unless asked for more, and she has more than that now, so it reads every page
            // before saving any, or she'd save a copy of the ones on later pages each time.
            List<AgentTool> saved = new List<AgentTool>();
            HashSet<string> pages = new HashSet<string>();
            string path = "/v1/convai/tools?page_size=100";
            while (true)
            {
                AgentToolList found = Voice.Reply<AgentToolList>(Voice.Send(Voice.Open(path, key, "GET"), null), "/v1/convai/tools");
                if (found.Tools != null) saved.AddRange(found.Tools);
                if (!found.More) break;
                if (string.IsNullOrEmpty(found.Cursor) || !pages.Add(found.Cursor)) throw new VoiceException("ElevenLabs didn't list all her tools.");
                path = "/v1/convai/tools?page_size=100&cursor=" + Uri.EscapeDataString(found.Cursor);
            }
            List<string> ids = new List<string>();
            for (int i = 0; i < Hands.Count; i++)
            {
                string id = null;
                foreach (AgentTool tool in saved)
                    if (tool.Config != null && tool.Config.Type == "client" && tool.Config.Name == Hands.Name(i) && tool.Config.Description == Hands.Description(i)) { id = tool.Id; break; }
                if (id == null) id = Voice.Reply<AgentTool>(Voice.Send(Voice.Open("/v1/convai/tools", key, "POST"), Encoding.UTF8.GetBytes(Hands.ElevenLabsTool(i))), "/v1/convai/tools").Id;
                if (string.IsNullOrEmpty(id)) throw new VoiceException("ElevenLabs didn't save her tools.");
                ids.Add(Voice.Quote(id));
            }
            return "[" + string.Join(",", ids.ToArray()) + "]";
        }

        internal static string Address(string key, string agentId)
        {
            AgentAddress found = Voice.Reply<AgentAddress>(Voice.Send(Voice.Open("/v1/convai/conversation/get-signed-url?agent_id=" + Uri.EscapeDataString(agentId), key, "GET"), null), "/v1/convai/conversation/get-signed-url");
            if (string.IsNullOrEmpty(found.Url)) throw new VoiceException("ElevenLabs didn't open a voice link.");
            return found.Url;
        }

        // Tool calls whose result hasn't gone back yet, so a settings change can tell its reply hasn't started.
        private int toolBusy;
        internal bool ToolBusy { get { return Interlocked.CompareExchange(ref toolBusy, 0, 0) > 0; } }
        internal bool Ready { get { return ready; } }

        internal void Start(string url, string voiceId)
        {
            worker = new Thread(delegate() { Run(url, voiceId); });
            worker.IsBackground = true;
            worker.Name = "Capri voice link";
            worker.Start();
        }

        internal void Stop()
        {
            stop.Cancel();
            ClientWebSocket open = socket;
            if (open != null) open.Abort();
            if (worker != null && worker != Thread.CurrentThread) worker.Join(1000);
        }

        public void Dispose() { Stop(); }

        internal void Send(short[] samples)
        {
            if (!ready) return;
            byte[] bytes = new byte[samples.Length * 2];
            Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
            Send("{\"user_audio_chunk\":\"" + Convert.ToBase64String(bytes) + "\"}");
        }

        internal void Tell(string text)
        {
            if (!ready) return;
            Send("{\"type\":\"contextual_update\",\"text\":" + Voice.Quote(text) + "}");
        }

        // A contextual update gets no reply, so news she should say out loud goes in as a turn of its own.
        internal void Say(string text)
        {
            if (!ready) return;
            Send("{\"type\":\"user_message\",\"text\":" + Voice.Quote(text) + "}");
        }

        private void Send(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            lock (sending)
            {
                // A link that stops taking what she sends is dropped after a few seconds, so it can't hold up her window, which
                // sends from its own thread. Dropping it ends the voice link the way a lost connection does.
                try { if (!socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, stop.Token).Wait(5000)) socket.Abort(); }
                catch (Exception) { }
            }
        }

        private void Run(string url, string voiceId)
        {
            string problem = null;
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
                socket = new ClientWebSocket();
                socket.ConnectAsync(new Uri(url), stop.Token).Wait();
                Send("{\"type\":\"conversation_initiation_client_data\",\"conversation_config_override\":{\"agent\":{\"prompt\":{\"prompt\":" + Voice.Quote(Prompt + Brain.Speech() + Brain.Remembered()) + "}" + (Language != "en" ? ",\"language\":" + Voice.Quote(Language) : "") + "},\"tts\":{\"voice_id\":" + Voice.Quote(voiceId) + "}}}");
                byte[] buffer = new byte[65536];
                using (MemoryStream message = new MemoryStream())
                    while (!stop.IsCancellationRequested)
                    {
                        WebSocketReceiveResult got = socket.ReceiveAsync(new ArraySegment<byte>(buffer), stop.Token).Result;
                        if (got.MessageType == WebSocketMessageType.Close)
                        {
                            string reason = socket.CloseStatusDescription;
                            if (!string.IsNullOrEmpty(reason) && socket.CloseStatus != WebSocketCloseStatus.NormalClosure) problem = Voice.Closed(reason);
                            break;
                        }
                        message.Write(buffer, 0, got.Count);
                        if (!got.EndOfMessage) continue;
                        Handle(message.ToArray());
                        message.SetLength(0);
                    }
            }
            catch (Exception error)
            {
                while (error is AggregateException && error.InnerException != null) error = error.InnerException;
                problem = error is VoiceException ? error.Message : Voice.Short("Voice link dropped: " + error.Message);
            }
            ready = false;
            if (stop.IsCancellationRequested) return;
            Action<string> closed = Closed;
            if (closed != null) closed(problem);
        }

        private void Handle(byte[] json)
        {
            AgentEvent item;
            try { item = Voice.Read<AgentEvent>(json); }
            catch (SerializationException) { return; }
            if (item.Type == "ping" && item.Ping != null) Send("{\"type\":\"pong\",\"event_id\":" + item.Ping.Id + "}");
            else if (item.Type == "conversation_initiation_metadata" && item.Start != null)
            {
                string format = item.Start.Output ?? "pcm_16000";
                int rate;
                if (!format.StartsWith("pcm_", StringComparison.Ordinal) || !int.TryParse(format.Substring(4), out rate)) throw new VoiceException("The agent sends " + format + " audio. Set it to PCM in ElevenLabs.");
                ready = true;
                Action<int> opened = Opened;
                if (opened != null) opened(rate);
            }
            else if (item.Type == "user_transcript" && item.Heard != null && !string.IsNullOrEmpty(item.Heard.Text)) { lastHeard = item.Heard.Text; Action<string> heard = Heard; if (heard != null) heard(item.Heard.Text); }
            else if (item.Type == "agent_response" && item.Reply != null && !string.IsNullOrEmpty(item.Reply.Text)) { Action<string> replied = Replied; if (replied != null) replied(Voice.Unsaid(item.Reply.Text)); }
            else if (item.Type == "audio" && item.Audio != null && item.Audio.Data != null)
            {
                byte[] bytes = Convert.FromBase64String(item.Audio.Data);
                short[] samples = new short[bytes.Length / 2];
                Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * 2);
                Action<short[]> audio = Audio;
                if (audio != null) audio(samples);
            }
            else if (item.Type == "interruption") { Action interrupted = Interrupted; if (interrupted != null) interrupted(); }
            else if (item.Type == "client_tool_call" && item.Call != null && item.Call.Id != null)
            {
                AgentToolCall call = item.Call;
                if (call.Name == "app_window" && call.Arguments != null) call.Arguments.Asked = lastHeard ?? "";
                Func<string, ToolArguments, string> act = Act;
                Interlocked.Increment(ref toolBusy);
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        string result = act == null ? "Her tools are off." : act(call.Name, call.Arguments);
                        Send("{\"type\":\"client_tool_result\",\"tool_call_id\":" + Voice.Quote(call.Id) + ",\"result\":" + Voice.Quote(result) + ",\"is_error\":false}");
                    }
                    finally { Interlocked.Decrement(ref toolBusy); }
                });
            }
        }
    }

    [DataContract]
    internal sealed class AgentShape
    {
        [DataMember(Name = "conversation_config")] public AgentConversation Config;
    }

    [DataContract]
    internal sealed class AgentConversation
    {
        [DataMember(Name = "tts")] public AgentTts Tts;
    }

    [DataContract]
    internal sealed class AgentTts
    {
        [DataMember(Name = "model_id")] public string Model;
        [DataMember(Name = "stability")] public double? Stability;
        [DataMember(Name = "similarity_boost")] public double? Similarity;
        [DataMember(Name = "speed")] public double? Speed;

        internal string Settings
        {
            get
            {
                if (Stability == null || Similarity == null) return null;
                System.Globalization.CultureInfo plain = System.Globalization.CultureInfo.InvariantCulture;
                return "{\"stability\":" + Stability.Value.ToString(plain) + ",\"similarity_boost\":" + Similarity.Value.ToString(plain) + (Speed == null ? "" : ",\"speed\":" + Speed.Value.ToString(plain)) + "}";
            }
        }
    }
}

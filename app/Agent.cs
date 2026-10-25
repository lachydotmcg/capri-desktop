using System;
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
        [DataMember(Name = "agent_id")] public string Id { get; set; }
    }

    [DataContract]
    internal sealed class AgentAddress
    {
        [DataMember(Name = "signed_url")] public string Url { get; set; }
    }

    [DataContract]
    internal sealed class AgentEvent
    {
        [DataMember(Name = "type")] public string Type { get; set; }
        [DataMember(Name = "conversation_initiation_metadata_event")] public AgentStart Start { get; set; }
        [DataMember(Name = "audio_event")] public AgentAudio Audio { get; set; }
        [DataMember(Name = "agent_response_event")] public AgentReply Reply { get; set; }
        [DataMember(Name = "user_transcription_event")] public AgentHeard Heard { get; set; }
        [DataMember(Name = "ping_event")] public AgentPing Ping { get; set; }
    }

    [DataContract]
    internal sealed class AgentStart
    {
        [DataMember(Name = "agent_output_audio_format")] public string Output { get; set; }
    }

    [DataContract]
    internal sealed class AgentAudio
    {
        [DataMember(Name = "audio_base_64")] public string Data { get; set; }
    }

    [DataContract]
    internal sealed class AgentReply
    {
        [DataMember(Name = "agent_response")] public string Text { get; set; }
    }

    [DataContract]
    internal sealed class AgentHeard
    {
        [DataMember(Name = "user_transcript")] public string Text { get; set; }
    }

    [DataContract]
    internal sealed class AgentPing
    {
        [DataMember(Name = "event_id")] public long Id { get; set; }
    }

    internal sealed class Agent : IDisposable
    {
        internal const double Pace = 15;
        internal const int Version = 3;
        private const string Patience = "\"turn\":{\"turn_timeout\":15,\"silence_end_call_timeout\":45}";
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

        internal static string Prompt { get { return Brain.Persona + "\nYou are talking out loud in a live voice chat: keep every reply to one or two short spoken sentences, and never write tags in square brackets.\nIf the user goes quiet, that's fine: they're probably busy or gaming. Don't beg them to talk or say they left you hanging. If they stay quiet, check in once with one short line, then let them be."; } }

        internal static string Create(string key, string voiceId)
        {
            string body = "{\"name\":\"Capri\",\"conversation_config\":{\"agent\":{\"language\":\"en\",\"prompt\":{\"prompt\":" + Voice.Quote(Prompt) + ",\"llm\":\"gemini-2.5-flash\"}}," + Patience + ",\"tts\":{\"voice_id\":" + Voice.Quote(voiceId) + "}},\"platform_settings\":{\"overrides\":{\"conversation_config_override\":{\"agent\":{\"prompt\":{\"prompt\":true}},\"tts\":{\"voice_id\":true}}}}}";
            AgentMade made = Voice.Read<AgentMade>(Voice.Send(Voice.Open("/v1/convai/agents/create", key, "POST"), Encoding.UTF8.GetBytes(body)));
            if (string.IsNullOrEmpty(made.Id)) throw new VoiceException("ElevenLabs didn't make the agent.");
            return made.Id;
        }

        internal static void Tune(string key, string agentId)
        {
            Voice.Send(Voice.Open("/v1/convai/agents/" + Uri.EscapeDataString(agentId), key, "PATCH"), Encoding.UTF8.GetBytes("{\"conversation_config\":{" + Patience + "}}"));
        }

        internal static string Address(string key, string agentId)
        {
            AgentAddress found = Voice.Read<AgentAddress>(Voice.Send(Voice.Open("/v1/convai/conversation/get-signed-url?agent_id=" + Uri.EscapeDataString(agentId), key, "GET"), null));
            if (string.IsNullOrEmpty(found.Url)) throw new VoiceException("ElevenLabs didn't open a voice link.");
            return found.Url;
        }

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

        private void Send(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            lock (sending)
            {
                try { socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, stop.Token).Wait(); }
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
                Send("{\"type\":\"conversation_initiation_client_data\",\"conversation_config_override\":{\"agent\":{\"prompt\":{\"prompt\":" + Voice.Quote(Prompt) + "}},\"tts\":{\"voice_id\":" + Voice.Quote(voiceId) + "}}}");
                byte[] buffer = new byte[65536];
                using (MemoryStream message = new MemoryStream())
                    while (!stop.IsCancellationRequested)
                    {
                        WebSocketReceiveResult got = socket.ReceiveAsync(new ArraySegment<byte>(buffer), stop.Token).Result;
                        if (got.MessageType == WebSocketMessageType.Close)
                        {
                            string reason = socket.CloseStatusDescription;
                            if (!string.IsNullOrEmpty(reason) && socket.CloseStatus != WebSocketCloseStatus.NormalClosure) problem = Voice.Short("Voice link closed: " + reason);
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
            else if (item.Type == "user_transcript" && item.Heard != null && !string.IsNullOrEmpty(item.Heard.Text)) { Action<string> heard = Heard; if (heard != null) heard(item.Heard.Text); }
            else if (item.Type == "agent_response" && item.Reply != null && !string.IsNullOrEmpty(item.Reply.Text)) { Action<string> replied = Replied; if (replied != null) replied(item.Reply.Text); }
            else if (item.Type == "audio" && item.Audio != null && item.Audio.Data != null)
            {
                byte[] bytes = Convert.FromBase64String(item.Audio.Data);
                short[] samples = new short[bytes.Length / 2];
                Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * 2);
                Action<short[]> audio = Audio;
                if (audio != null) audio(samples);
            }
            else if (item.Type == "interruption") { Action interrupted = Interrupted; if (interrupted != null) interrupted(); }
        }
    }
}

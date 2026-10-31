using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // Where her words become sound. Each one hands back a 16-bit mono WAV that VoiceFx.Styled and Voice.Play already take,
    // so picking another never touches the rest of her voice.
    internal interface ISpeaker
    {
        string Name { get; }
        bool Ready { get; }
        byte[] Speak(string text);
    }

    internal static class Speakers
    {
        internal const string ElevenLabs = "elevenlabs";
        internal const string Local = "local";
        internal const string Off = "off";

        // Settings from before providers have none saved, and they were ElevenLabs.
        internal static string Known(string saved) { return saved == Local || saved == Off ? saved : ElevenLabs; }
    }

    internal sealed class ElevenLabsSpeaker : ISpeaker
    {
        private readonly Voice voice;

        internal ElevenLabsSpeaker(Voice voice) { this.voice = voice; }

        public string Name { get { return "ElevenLabs"; } }
        public bool Ready { get { return voice.Ready; } }
        public byte[] Speak(string text) { return voice.Speak(text); }
    }

    internal sealed class QuietSpeaker : ISpeaker
    {
        public string Name { get { return "none"; } }
        public bool Ready { get { return false; } }
        public byte[] Speak(string text) { throw new VoiceException("Her voice provider is off."); }
    }

    // A TTS server they run themselves that speaks OpenAI's speech API (POST /v1/audio/speech), like Kokoro-FastAPI,
    // openedai-speech, LocalAI or Piper and Qwen3-TTS behind one. It takes no key, and only ever gets the words she says.
    internal sealed class LocalSpeaker : ISpeaker
    {
        internal const string Default = "http://localhost:8880/v1";
        internal const string Wav = "wav";
        internal const string Pcm = "pcm";
        // OpenAI's raw pcm is 24 kHz 16-bit mono with no header.
        internal const int PcmRate = 24000;
        private const int Biggest = 64 << 20;
        internal volatile string Url;
        internal volatile string Model;
        internal volatile string Speaker;
        internal volatile string Format = Wav;
        internal volatile int Patience = 20000;

        public string Name { get { return "Local server"; } }

        public bool Ready
        {
            get
            {
                try { Endpoint(Address); return true; }
                catch (VoiceException) { return false; }
            }
        }

        // Their address, or Kokoro-FastAPI's usual one when they haven't set any, so the server most people run just works.
        internal string Address { get { string url = Url; return string.IsNullOrWhiteSpace(url) ? Default : url; } }

        internal static string Known(string format) { return format == Pcm ? Pcm : Wav; }

        // Their address as typed: http:// when it has no scheme, /v1 when it has no path, and /audio/speech unless it ends there.
        internal static Uri Endpoint(string url)
        {
            string typed = (url ?? "").Trim();
            if (typed.Length == 0) throw new VoiceException("Set your local voice server's address first.");
            if (!Regex.IsMatch(typed, "^[a-z][a-z0-9+.-]*://", RegexOptions.IgnoreCase)) typed = "http://" + typed;
            Uri parsed;
            if (!Uri.TryCreate(typed, UriKind.Absolute, out parsed) || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) || parsed.Host.Length == 0)
                throw new VoiceException("Your local voice server's address doesn't work. Try " + Default + ".");
            string path = parsed.AbsolutePath.TrimEnd('/');
            if (path.Length == 0) path = "/v1";
            if (!path.EndsWith("/audio/speech", StringComparison.OrdinalIgnoreCase)) path += "/audio/speech";
            UriBuilder built = new UriBuilder(parsed);
            built.Path = path;
            built.Query = "";
            return built.Uri;
        }

        internal static string Where(Uri at) { return at.IsDefaultPort ? at.Host : at.Host + ":" + at.Port; }

        public byte[] Speak(string text)
        {
            string url = Address, model = Model, speaker = Speaker, format = Known(Format);
            int patience = Math.Max(1000, Patience);
            Uri endpoint = Endpoint(url);
            StringBuilder body = new StringBuilder("{");
            if (!string.IsNullOrEmpty(model)) body.Append("\"model\":").Append(Voice.Quote(model)).Append(',');
            body.Append("\"input\":").Append(Voice.Quote(Voice.Sayable(text)));
            if (!string.IsNullOrEmpty(speaker)) body.Append(",\"voice\":").Append(Voice.Quote(speaker));
            body.Append(",\"response_format\":").Append(Voice.Quote(format)).Append('}');
            byte[] payload = Encoding.UTF8.GetBytes(body.ToString());
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(endpoint);
            request.Method = "POST";
            if (endpoint.IsLoopback) request.Proxy = null;
            request.UserAgent = "Capri-Desktop";
            request.ContentType = "application/json";
            request.ContentLength = payload.Length;
            request.ServicePoint.Expect100Continue = false;
            request.Timeout = patience;
            request.ReadWriteTimeout = patience;
            string where = Where(endpoint);
            try
            {
                using (Stream upload = request.GetRequestStream()) upload.Write(payload, 0, payload.Length);
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                    return Sound(Read(stream, where), response.ContentType, format);
            }
            catch (WebException error)
            {
                HttpWebResponse response = error.Response as HttpWebResponse;
                if (response == null)
                {
                    if (error.Status == WebExceptionStatus.Timeout) throw new VoiceException("Your local voice server took over " + patience / 1000 + " s to answer.");
                    if (error.Status == WebExceptionStatus.ConnectFailure || error.Status == WebExceptionStatus.NameResolutionFailure || Unreached(error)) throw new VoiceException("Can't reach your local voice server at " + where + ". Is it running?");
                    throw new VoiceException("Your local voice server at " + where + " stopped answering.");
                }
                int status = (int)response.StatusCode;
                string detail;
                using (response)
                using (StreamReader reader = new StreamReader(response.GetResponseStream())) detail = Said(reader.ReadToEnd());
                Brain.Log(endpoint.GetLeftPart(UriPartial.Path), -1, "HTTP " + status + " " + (detail.Length > 200 ? detail.Substring(0, 200) : detail));
                if (status == 404) throw new VoiceException("Your local voice server has no " + endpoint.AbsolutePath + " (HTTP 404). Check its address.", status);
                throw new VoiceException("Your local voice server said HTTP " + status + (detail.Length > 0 ? ": " + Voice.Short(detail) : "."), status);
            }
            catch (IOException) { throw new VoiceException("Your local voice server at " + where + " stopped answering."); }
        }

        // .NET's HttpWebRequest reports a refused connection as an unknown error, with the socket's own error inside.
        private static bool Unreached(Exception error)
        {
            for (Exception inner = error; inner != null; inner = inner.InnerException)
            {
                System.Net.Sockets.SocketException socket = inner as System.Net.Sockets.SocketException;
                if (socket != null) return socket.SocketErrorCode == System.Net.Sockets.SocketError.ConnectionRefused || socket.SocketErrorCode == System.Net.Sockets.SocketError.HostNotFound
                    || socket.SocketErrorCode == System.Net.Sockets.SocketError.HostUnreachable || socket.SocketErrorCode == System.Net.Sockets.SocketError.NetworkUnreachable || socket.SocketErrorCode == System.Net.Sockets.SocketError.TryAgain;
            }
            return false;
        }

        private static byte[] Read(Stream stream, string where)
        {
            using (MemoryStream copy = new MemoryStream())
            {
                byte[] chunk = new byte[81920];
                int got;
                while ((got = stream.Read(chunk, 0, chunk.Length)) > 0)
                {
                    if (copy.Length + got > Biggest) throw new VoiceException("Your local voice server at " + where + " sent more sound than one line needs.");
                    copy.Write(chunk, 0, got);
                }
                return copy.ToArray();
            }
        }

        // Its sound as a 16-bit mono WAV at its own rate. She reads WAV in PCM or float, and OpenAI's raw pcm; anything else,
        // like MP3 or Opus, is said plainly instead of pulling in a decoder.
        internal static byte[] Sound(byte[] reply, string type, string format)
        {
            if (reply.Length == 0) throw new VoiceException("Your local voice server sent back no sound.");
            int rate;
            short[] samples = Pcm16(reply, out rate);
            if (samples == null && format == Pcm && !Looks(reply) && !Regex.IsMatch(type ?? "", "json|text|html", RegexOptions.IgnoreCase))
            {
                samples = new short[reply.Length / 2];
                Buffer.BlockCopy(reply, 0, samples, 0, samples.Length * 2);
                rate = PcmRate;
            }
            if (samples == null)
            {
                if (Regex.IsMatch(type ?? "", "json|text|html", RegexOptions.IgnoreCase) || reply[0] == '{' || reply[0] == '<')
                    throw new VoiceException("Your local voice server sent words, not sound: " + Voice.Short(Said(Encoding.UTF8.GetString(reply))));
                throw new VoiceException("Capri can't read the sound your local voice server sent" + (string.IsNullOrEmpty(type) ? "" : " (" + type.Split(';')[0].Trim() + ")") + ". Set its format to WAV or PCM.");
            }
            if (samples.Length == 0) throw new VoiceException("Your local voice server sent back no sound.");
            return Voice.Wave(samples, rate);
        }

        // Whether it starts like a file of its own, so raw pcm is never a header read as sound.
        private static bool Looks(byte[] reply)
        {
            if (reply.Length < 4) return false;
            string head = Encoding.ASCII.GetString(reply, 0, 4);
            return head == "RIFF" || head == "OggS" || head == "fLaC" || head.StartsWith("ID3", StringComparison.Ordinal) || (reply[0] == 0xFF && (reply[1] & 0xE0) == 0xE0);
        }

        // A RIFF WAV in 8, 16, 24 or 32-bit PCM or 32-bit float, mono or more channels, as 16-bit mono. A server that streams
        // its WAV leaves the sizes unset, so a size past the end means "to the end". Null when it isn't one she reads.
        internal static short[] Pcm16(byte[] wav, out int rate)
        {
            rate = 0;
            if (wav.Length < 12 || Encoding.ASCII.GetString(wav, 0, 4) != "RIFF" || Encoding.ASCII.GetString(wav, 8, 4) != "WAVE") return null;
            int coding = 0, channels = 0, bits = 0;
            long at = 12;
            while (at + 8 <= wav.Length)
            {
                string chunk = Encoding.ASCII.GetString(wav, (int)at, 4);
                long size = BitConverter.ToUInt32(wav, (int)at + 4);
                long start = at + 8;
                if (chunk == "fmt " && start + 16 <= wav.Length)
                {
                    coding = BitConverter.ToUInt16(wav, (int)start);
                    channels = BitConverter.ToUInt16(wav, (int)start + 2);
                    rate = BitConverter.ToInt32(wav, (int)start + 4);
                    bits = BitConverter.ToUInt16(wav, (int)start + 14);
                    // WAVE_FORMAT_EXTENSIBLE keeps its real coding in the first two bytes of its sub-format.
                    if (coding == 0xFFFE && size >= 40 && start + 26 <= wav.Length) coding = BitConverter.ToUInt16(wav, (int)start + 24);
                }
                if (chunk == "data")
                {
                    if (channels < 1 || rate < 1000 || rate > 384000) return null;
                    bool pcm = coding == 1 && (bits == 8 || bits == 16 || bits == 24 || bits == 32);
                    bool floats = coding == 3 && bits == 32;
                    if (!pcm && !floats) return null;
                    long available = wav.Length - start;
                    long length = size > 0 && size <= available ? size : available;
                    int width = bits / 8, frame = width * channels;
                    int frames = (int)(length / frame);
                    short[] mono = new short[frames];
                    if (pcm && bits == 16 && channels == 1)
                    {
                        Buffer.BlockCopy(wav, (int)start, mono, 0, frames * 2);
                        return mono;
                    }
                    for (int i = 0; i < frames; i++)
                    {
                        double sum = 0;
                        for (int c = 0; c < channels; c++) sum += Level(wav, (int)(start + (long)i * frame + c * width), bits, floats);
                        mono[i] = (short)Math.Max(-32768, Math.Min(32767, Math.Round(sum / channels * 32767)));
                    }
                    return mono;
                }
                long next = start + size + (size & 1);
                if (next > wav.Length) break;
                at = next;
            }
            return null;
        }

        private static double Level(byte[] wav, int at, int bits, bool floats)
        {
            if (floats)
            {
                float value = BitConverter.ToSingle(wav, at);
                return float.IsNaN(value) ? 0 : Math.Max(-1, Math.Min(1, value));
            }
            if (bits == 8) return (wav[at] - 128) / 128.0;
            if (bits == 16) return BitConverter.ToInt16(wav, at) / 32768.0;
            if (bits == 24) return ((wav[at] << 8 | wav[at + 1] << 16 | wav[at + 2] << 24) >> 8) / 8388608.0;
            return BitConverter.ToInt32(wav, at) / 2147483648.0;
        }

        // What an error reply says in words: OpenAI's error message, FastAPI's detail, or the text itself.
        private static string Said(string reply)
        {
            string text = reply ?? "";
            Match message = Regex.Match(text, "\"(?:message|detail|msg|error)\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (message.Success)
            {
                text = message.Groups[1].Value;
                try { text = Regex.Unescape(text); }
                catch (ArgumentException) { }
            }
            return Regex.Replace(text, @"\s+", " ").Trim();
        }
    }
}

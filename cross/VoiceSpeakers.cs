using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    internal sealed class FishSpeaker : ISpeaker
    {
        internal const string Default = "https://api.fish.audio";
        internal const string Free = "s2.1-pro-free";
        internal const string OldDefault = "Capri Default";
        internal const int Rate = 24000;
        internal volatile string Url = Default;
        internal volatile string Reference;
        internal volatile string Key;
        internal volatile int Patience = 20000;

        public string Name { get { return "Fish Audio"; } }
        public bool Ready { get { return Key != null; } }

        internal string Address { get { string url = Url; return string.IsNullOrWhiteSpace(url) ? Default : url; } }

        internal static string KeyFile(string folder) { return Path.Combine(folder, "voice-fish-key"); }

        internal static string ReadKey(string folder) { return SecretFile.Read(KeyFile(folder)); }

        internal bool SaveKey(string folder, string key)
        {
            key = (key ?? "").Trim();
            if (key.Length == 0) return false;
            if (!SecretFile.Write(KeyFile(folder), key, "Fish Audio key")) return false;
            Key = key;
            return true;
        }

        internal bool ForgetKey(string folder)
        {
            if (!SecretFile.Write(KeyFile(folder), null, "Fish Audio key")) return false;
            Key = null;
            return true;
        }

        internal static string VoiceId(string typed)
        {
            string voice = (typed ?? "").Trim();
            Match link = Regex.Match(voice, @"fish\.audio/\S*?([0-9a-f]{32})", RegexOptions.IgnoreCase);
            if (link.Success) return link.Groups[1].Value.ToLowerInvariant();
            return string.Equals(voice, OldDefault, StringComparison.OrdinalIgnoreCase) ? "" : voice;
        }

        internal static Uri Endpoint(string url) { return CloudVoice.Endpoint(url, true); }

        public byte[] Speak(string text)
        {
            string key = Key;
            if (string.IsNullOrEmpty(key)) throw new VoiceException("Paste your Fish Audio key first.");
            int patience = Math.Max(1000, Patience);
            Uri endpoint = Endpoint(Address);
            StringBuilder body = new StringBuilder("{");
            body.Append("\"text\":").Append(Voice.Quote(Voice.Sayable(text)));
            string voice = VoiceId(Reference);
            if (voice.Length > 0) body.Append(",\"reference_id\":").Append(Voice.Quote(voice));
            body.Append(",\"format\":\"wav\",\"sample_rate\":").Append(Rate).Append('}');
            return CloudVoice.Post(endpoint, key, Free, body.ToString(), Name, true, patience);
        }
    }

    internal sealed class OpenAiSpeaker : ISpeaker
    {
        internal const string Default = "https://api.openai.com/v1";
        internal const string Model = "gpt-4o-mini-tts";
        internal const string DefaultVoice = "marin";
        internal volatile string Url = Default;
        internal volatile string VoiceName;
        internal volatile string Key;
        internal volatile int Patience = 20000;

        public string Name { get { return "OpenAI"; } }
        public bool Ready { get { return Key != null; } }

        internal string Address { get { string url = Url; return string.IsNullOrWhiteSpace(url) ? Default : url; } }
        internal string Speaker { get { string voice = (VoiceName ?? "").Trim(); return voice.Length == 0 ? DefaultVoice : voice; } }

        internal static string KeyFile(string folder) { return Path.Combine(folder, "voice-openai-key"); }

        internal static string ReadKey(string folder) { return SecretFile.Read(KeyFile(folder)); }

        internal bool SaveKey(string folder, string key)
        {
            key = (key ?? "").Trim();
            if (key.Length == 0) return false;
            if (!SecretFile.Write(KeyFile(folder), key, "OpenAI key")) return false;
            Key = key;
            return true;
        }

        internal bool ForgetKey(string folder)
        {
            if (!SecretFile.Write(KeyFile(folder), null, "OpenAI key")) return false;
            Key = null;
            return true;
        }

        internal static Uri Endpoint(string url) { return CloudVoice.Endpoint(url, false); }

        public byte[] Speak(string text)
        {
            string key = Key;
            if (string.IsNullOrEmpty(key)) throw new VoiceException("Paste your OpenAI key first.");
            int patience = Math.Max(1000, Patience);
            Uri endpoint = Endpoint(Address);
            StringBuilder body = new StringBuilder("{");
            body.Append("\"model\":").Append(Voice.Quote(Model)).Append(',');
            body.Append("\"input\":").Append(Voice.Quote(Voice.Sayable(Voice.Uncued(text))));
            body.Append(",\"voice\":").Append(Voice.Quote(Speaker));
            body.Append(",\"response_format\":\"wav\"}");
            return CloudVoice.Post(endpoint, key, null, body.ToString(), Name, false, patience);
        }
    }

    internal static class CloudVoice
    {
        private const int Biggest = 64 << 20;

        internal static Uri Endpoint(string url, bool fish)
        {
            string usual = fish ? FishSpeaker.Default : OpenAiSpeaker.Default;
            string typed = (url ?? "").Trim();
            if (typed.Length == 0) typed = usual;
            if (!Regex.IsMatch(typed, "^[a-z][a-z0-9+.-]*://", RegexOptions.IgnoreCase))
                typed = (Regex.IsMatch(typed, @"^[^/:]+\.[a-z]{2,}(/|$)", RegexOptions.IgnoreCase) && !Regex.IsMatch(typed, @"^[^/:]+\.(local|lan|home|internal)(/|$)", RegexOptions.IgnoreCase) ? "https://" : "http://") + typed;
            Uri parsed;
            if (!Uri.TryCreate(typed, UriKind.Absolute, out parsed) || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) || parsed.Host.Length == 0)
                throw new VoiceException("Your voice server's address doesn't work. Try " + usual + ".");
            string path = parsed.AbsolutePath.TrimEnd('/');
            if (fish)
            {
                if (!path.EndsWith("/tts", StringComparison.OrdinalIgnoreCase)) path += path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? "/tts" : "/v1/tts";
            }
            else
            {
                if (path.Length == 0) path = "/v1";
                if (!path.EndsWith("/audio/speech", StringComparison.OrdinalIgnoreCase)) path += "/audio/speech";
            }
            UriBuilder built = new UriBuilder(parsed);
            built.Path = path;
            return built.Uri;
        }

        internal static string Where(Uri at) { return at.IsDefaultPort ? at.Host : at.Host + ":" + at.Port; }

        internal static byte[] Post(Uri endpoint, string key, string model, string json, string name, bool fish, int patience)
        {
            byte[] payload = Encoding.UTF8.GetBytes(json);
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(endpoint);
            request.Method = "POST";
            if (endpoint.IsLoopback) request.Proxy = null;
            request.UserAgent = "Capri-Desktop";
            request.ContentType = "application/json";
            request.ContentLength = payload.Length;
            if (!string.IsNullOrEmpty(key)) request.Headers[HttpRequestHeader.Authorization] = "Bearer " + key;
            if (!string.IsNullOrEmpty(model)) request.Headers["model"] = model;
            request.ServicePoint.Expect100Continue = false;
            request.Timeout = patience;
            request.ReadWriteTimeout = patience;
            System.Diagnostics.Stopwatch waited = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using (Stream upload = request.GetRequestStream()) upload.Write(payload, 0, payload.Length);
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                    return Sound(Read(stream, name), response.ContentType, name);
            }
            catch (WebException error)
            {
                HttpWebResponse response = error.Response as HttpWebResponse;
                if (response == null)
                {
                    if (error.Status == WebExceptionStatus.Timeout || waited.ElapsedMilliseconds >= patience) throw new VoiceException(Slow(name, patience));
                    if (error.Status == WebExceptionStatus.ConnectFailure || error.Status == WebExceptionStatus.NameResolutionFailure || Unreached(error)) throw new VoiceException("Can't reach " + name + ". Check your internet connection.");
                    throw new VoiceException(name + " at " + Where(endpoint) + " stopped answering.");
                }
                int status = (int)response.StatusCode;
                string reply, detail;
                using (response)
                using (StreamReader reader = new StreamReader(response.GetResponseStream())) reply = reader.ReadToEnd();
                detail = Said(reply);
                if (status == 404 && fish) throw new VoiceException("Fish Audio can't find that voice (HTTP 404). Check its ID or link on fish.audio.");
                if (status == 404) throw new VoiceException(name + " has no " + endpoint.AbsolutePath + " (HTTP 404). Check its address.");
                if (OutOfCredit(status, reply) && !(status == 402 && fish)) throw new VoiceException(Credit(name), "HTTP " + status + ": " + Voice.Short(detail));
                if (status == 429) throw new VoiceException(name + " is busy (HTTP 429). Try again in a moment.");
                if (status == 401 || status == 403) throw new VoiceException(string.IsNullOrEmpty(key) ? name + " wants a key (HTTP " + status + "). Add it in my right-click menu under VOICE." : name + " turned down its key (HTTP " + status + ").");
                if (status == 402 && fish && model != FishSpeaker.Free) throw new VoiceException("Fish Audio wants credit for " + (string.IsNullOrEmpty(model) ? "its usual model" : model) + " (HTTP 402). For its free tier, set Model to " + FishSpeaker.Free + ".");
                throw new VoiceException(name + " said HTTP " + status + (detail.Length > 0 ? ": " + Voice.Short(detail) : "."));
            }
            catch (IOException)
            {
                if (waited.ElapsedMilliseconds >= patience) throw new VoiceException(Slow(name, patience));
                throw new VoiceException(name + " at " + Where(endpoint) + " stopped answering.");
            }
        }

        private static string Slow(string name, int patience)
        {
            return name + " took over " + (patience / 1000.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " s to answer.";
        }

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

        private static byte[] Read(Stream stream, string name)
        {
            using (MemoryStream copy = new MemoryStream())
            {
                byte[] chunk = new byte[81920];
                int got;
                while ((got = stream.Read(chunk, 0, chunk.Length)) > 0)
                {
                    if (copy.Length + got > Biggest) throw new VoiceException(name + " sent more sound than one line needs.");
                    copy.Write(chunk, 0, got);
                }
                return copy.ToArray();
            }
        }

        internal static byte[] Sound(byte[] reply, string type, string name)
        {
            if (reply.Length == 0) throw new VoiceException(name + " sent back no sound.");
            int rate;
            short[] samples = LocalSpeaker.Pcm16(reply, out rate);
            if (samples == null)
            {
                if (Regex.IsMatch(type ?? "", "json|text|html", RegexOptions.IgnoreCase) || reply[0] == '{' || reply[0] == '<')
                    throw new VoiceException(name + " sent words, not sound: " + Voice.Short(Said(Encoding.UTF8.GetString(reply))));
                throw new VoiceException("Capri can't read the sound " + name + " sent" + (string.IsNullOrEmpty(type) ? "" : " (" + type.Split(';')[0].Trim() + ")") + ".");
            }
            if (samples.Length == 0) throw new VoiceException(name + " sent back no sound.");
            return Voice.Wave(samples, rate);
        }

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

        private static bool OutOfCredit(int status, string reply)
        {
            return (status == 429 || status == 402) && Regex.IsMatch(reply ?? "", @"insufficient[_ ](quota|balance)|no credits|credit balance|billing", RegexOptions.IgnoreCase);
        }

        private static string Credit(string name)
        {
            return name + " is out of credit. " + (name == "OpenAI" ? "Add credit at platform.openai.com" : "Add credit to its account") + ", or pick another voice in my right-click menu under VOICE.";
        }
    }
}

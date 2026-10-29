using System;
using System.Collections.Generic;
using System.IO;
#if !CROSS
using System.Diagnostics;
using System.IO.Compression;
using System.Media;
using System.Reflection;
using System.Threading;
#endif
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    [DataContract]
    internal sealed class VoiceEntry
    {
        [DataMember(Name = "voice_id")] public string Id;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "category")] public string Category;
    }

    [DataContract]
    internal sealed class VoicePage
    {
        [DataMember(Name = "voices")] public List<VoiceEntry> Voices;
        [DataMember(Name = "has_more")] public bool More;
        [DataMember(Name = "next_page_token")] public string Next;
    }

    [DataContract]
    internal sealed class VoiceModel
    {
        [DataMember(Name = "model_id")] public string Id;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "can_do_text_to_speech")] public bool Speaks;
    }

    internal sealed class VoiceException : Exception
    {
        internal readonly int Status;

        internal VoiceException(string message, int status = 0) : base(message) { Status = status; }
    }

    internal sealed class VoiceListing
    {
        internal List<VoiceEntry> Voices = new List<VoiceEntry>();
        internal List<VoiceModel> Models = new List<VoiceModel>();
    }

    internal sealed class Voice
    {
        internal static string Host = "https://api.elevenlabs.io";
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Capri.ElevenLabs");
        internal volatile string Key;
        internal volatile string VoiceId;
        internal volatile string Model;
#if CROSS
        internal static Action<byte[]> Playback;
        internal static Action Silence;
#else
        private SoundPlayer player;
#endif
        internal static volatile int Loudness = 100;

        internal static short[] Louder(short[] samples)
        {
            int percent = Loudness;
            if (percent == 100) return samples;
            short[] scaled = new short[samples.Length];
            for (int i = 0; i < samples.Length; i++) scaled[i] = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, samples[i] * percent / 100));
            return scaled;
        }

        internal static byte[] Louder(byte[] wav)
        {
            if (Loudness == 100) return wav;
            int rate;
            short[] samples = Samples(wav, out rate);
            return samples.Length == 0 ? wav : Wave(Louder(samples), rate);
        }

        internal bool Ready { get { return Key != null && VoiceId != null; } }

#if CROSS
        internal static string Protect(string key) { throw new PlatformNotSupportedException("Saving keys isn't ready on this system yet."); }

        internal static string Unprotect(string saved) { return null; }
#else
        internal static string Protect(string key) { return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key), Entropy, DataProtectionScope.CurrentUser)); }

        internal static string Unprotect(string saved)
        {
            if (string.IsNullOrEmpty(saved)) return null;
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(saved), Entropy, DataProtectionScope.CurrentUser)); }
            catch (CryptographicException) { return null; }
            catch (FormatException) { return null; }
        }
#endif

        internal static string Tail(string key) { return key == null || key.Length < 4 ? "" : key.Substring(key.Length - 4); }

        internal static string Spoken(string said)
        {
            string words = Regex.Replace(Regex.Replace(said ?? "", @"\[[^\]]*\]", " ").Replace("*", ""), @"\s+", " ").Trim();
            return Regex.IsMatch(words, @"[\p{L}\p{Nd}]") ? words : "";
        }

        private static readonly string[,] Respelled = { { "chooms", "chewms" }, { "choom", "chewm" }, { "preem", "preme" }, { "yezzirrr", "yessirrr" } };

        internal static string Sayable(string text) { return Swap(Plain(text), 0, 1); }

        private static string Plain(string text)
        {
            text = Regex.Replace(text ?? "", @"https?://(?:www\.)?([^/\s]+?)(?:/\S*?)?(?=[.,!?)]*(?:\s|$))", delegate(Match link) { return link.Groups[1].Value.Replace(".", " dot "); });
            text = Regex.Replace(text, @"[\uD800-\uDBFF][\uDC00-\uDFFF]|[☀-➿️‍]", "");
            text = Regex.Replace(text, @"\s*&\s*", " and ");
            text = Regex.Replace(text, @"(\d)\s*%", "$1 percent");
            text = Regex.Replace(text, @"\bw/(?=\s)", "with");
            text = Regex.Replace(text, @"\s*(->|=>|~+)\s*", " ");
            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        internal static string Unsaid(string text) { return Swap(text, 1, 0); }

        private static string Swap(string text, int from, int to)
        {
            text = text ?? "";
            for (int i = 0; i < Respelled.GetLength(0); i++)
            {
                string target = Respelled[i, to];
                text = Regex.Replace(text, @"\b" + Respelled[i, from] + @"\b", delegate(Match found)
                {
                    string word = found.Value;
                    if (word.ToUpperInvariant() == word) return target.ToUpperInvariant();
                    return char.IsUpper(word[0]) ? char.ToUpperInvariant(target[0]) + target.Substring(1) : target;
                }, RegexOptions.IgnoreCase);
            }
            return text;
        }

        internal static HttpWebRequest Open(string path, string key, string method)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Host + path);
            request.Method = method;
            request.UserAgent = "Capri-Desktop";
            request.Headers["xi-api-key"] = key;
            request.ServicePoint.Expect100Continue = false;
            request.Timeout = 20000;
            request.ReadWriteTimeout = 20000;
            return request;
        }

        internal static byte[] Send(HttpWebRequest request, byte[] body)
        {
            try
            {
                if (body != null)
                {
                    request.ContentType = "application/json";
                    request.ContentLength = body.Length;
                    using (Stream upload = request.GetRequestStream()) upload.Write(body, 0, body.Length);
                }
                using (WebResponse response = request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (MemoryStream copy = new MemoryStream())
                {
                    stream.CopyTo(copy);
                    return copy.ToArray();
                }
            }
            catch (WebException error)
            {
                HttpWebResponse response = error.Response as HttpWebResponse;
                if (response == null) throw new VoiceException("Can't reach ElevenLabs. Check your internet.");
                int status = (int)response.StatusCode;
                string detail;
                using (response)
                using (StreamReader reader = new StreamReader(response.GetResponseStream())) detail = reader.ReadToEnd();
                // They only see her short line, so errors.txt keeps ElevenLabs' own answer for Copy error details.
                string said = Regex.Replace(detail ?? "", @"\s+", " ").Trim();
                Brain.Log(request.RequestUri.GetLeftPart(UriPartial.Path), -1, "HTTP " + status + " " + (said.Length > 200 ? said.Substring(0, 200) : said));
                throw new VoiceException(Explain(status, detail), status);
            }
            catch (IOException) { throw new VoiceException("Can't reach ElevenLabs. Check your internet."); }
        }

        internal const string Rejected = "ElevenLabs rejected the key. Press Forget, paste a new one.";
        internal const string RejectedNew = "ElevenLabs rejected that key. Copy it again from ElevenLabs and paste it.";

        internal const string Restricted = "ElevenLabs has restricted your account, so her voice can't connect. Sign in at elevenlabs.io to see why, or pick PREBUILT LOCAL in the Mainframe.";

        internal static string Unsaved(string problem) { return problem == Rejected ? RejectedNew : problem; }

        internal static string Closed(string reason)
        {
            if (Regex.IsMatch(reason ?? "", "account_restricted|workspace is restricted", RegexOptions.IgnoreCase)) return Restricted;
            return Short("Voice link closed: " + reason);
        }

        internal static string Explain(int status, string detail)
        {
            detail = detail ?? "";
            if (detail.Contains("quota_exceeded")) return "Out of ElevenLabs credits. Pick PREBUILT LOCAL in the Mainframe.";
            if (detail.Contains("account_restricted")) return Restricted;
            if (detail.Contains("missing_permissions")) return "Key is missing permissions. Make a new ElevenLabs key with Restrict key turned off.";
            if (detail.Contains("voice_not_found")) return "Voice not found. Add it to your ElevenLabs voices.";
            // Voices other people shared in the Voice Library only work outside ElevenLabs' site on paid plans; a fresh clone may still be training.
            if (Regex.IsMatch(detail, @"free_users_not_allowed|library voices?", RegexOptions.IgnoreCase)) return LibraryVoice;
            if (Regex.IsMatch(detail, @"fine.?tun", RegexOptions.IgnoreCase)) return "That voice is still training on ElevenLabs. Pick another until it's ready.";
            if (detail.Contains("invalid_api_key")) return Rejected;
            Match message = Regex.Match(detail, "\"message\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            string text = message.Success ? Regex.Unescape(message.Groups[1].Value).Trim() : "";
            if (Regex.IsMatch(text, @"empty text|non-empty", RegexOptions.IgnoreCase)) return Silent;
            // It's only the key's fault when ElevenLabs says so (Lichen's report: two keys with Restrict key off still got the key
            // line). A 403 can also be a voice, model or plan they can't use, or their network being turned away, so ElevenLabs'
            // own words show instead.
            if (Regex.IsMatch(text, @"permission|api.?key|unauthori[sz]ed", RegexOptions.IgnoreCase)) return "Key can't do that. Make a new ElevenLabs key with Restrict key turned off.";
            if (status == 403 && text.Length == 0) return Refused;
            // A new key can't fix a voice, model or plan problem, so those skip the key hint.
            if (text.Length > 0) return Short(text) + (status == 401 || status == 402 || status == 429 || Regex.IsMatch(text, @"unusual activity|\b(voices?|models?|plans?|subscription|upgrade)\b", RegexOptions.IgnoreCase) ? "" : RestrictHint);
            if (status == 401) return Rejected;
            if (status == 429) return "ElevenLabs is busy. Try again in a moment.";
            return "ElevenLabs error " + status + "." + RestrictHint;
        }

        internal const string Silent = "That reply had nothing to say out loud.";
        internal const string LibraryVoice = "ElevenLabs only lets paid plans use Voice Library voices in apps. Pick a voice you made or a default one.";
        internal const string RestrictHint = " Still failing? Make a new key with Restrict key turned off.";
        internal const string Refused = "ElevenLabs turned her voice away (error 403) without saying why. A VPN or proxy can do that." + RestrictHint;

        internal static string Short(string text)
        {
            text = (text ?? "").Trim();
            return text.Length > 72 ? text.Substring(0, 69) + "..." : text;
        }

        internal static T Read<T>(byte[] json) { using (MemoryStream stream = new MemoryStream(json)) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream); }

        internal const string Unreadable = "ElevenLabs sent a reply Capri couldn't read. Right-click her, Help, Copy error details, and send it in the Discord.";

        internal static T Reply<T>(byte[] json, string path)
        {
            try { return Read<T>(json); }
            catch (SerializationException)
            {
                string text = Encoding.UTF8.GetString(json).Trim();
                Brain.Log(Host + path, -1, "Unreadable " + typeof(T).Name + " reply: " + (text.Length > 200 ? text.Substring(0, 200) : text));
                throw new VoiceException(Unreadable);
            }
        }

        internal static VoiceListing Fetch(string key)
        {
            VoiceListing listing = new VoiceListing();
            string next = null;
            for (int page = 0; page < 10; page++)
            {
                string path = "/v2/voices?page_size=100" + (next == null ? "" : "&next_page_token=" + Uri.EscapeDataString(next));
                VoicePage found = Reply<VoicePage>(Send(Open(path, key, "GET"), null), "/v2/voices");
                if (found.Voices != null) listing.Voices.AddRange(found.Voices);
                if (!found.More || string.IsNullOrEmpty(found.Next)) break;
                next = found.Next;
            }
            try { foreach (VoiceModel model in Read<List<VoiceModel>>(Send(Open("/v1/models", key, "GET"), null))) if (model.Speaks) listing.Models.Add(model); }
            catch (VoiceException error) { if (error.Status == 401) throw; }
            catch (SerializationException) { }
            listing.Voices.Sort(delegate(VoiceEntry a, VoiceEntry b)
            {
                int order = (a.Category == "premade").CompareTo(b.Category == "premade");
                return order != 0 ? order : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return listing;
        }

        internal byte[] Speak(string text)
        {
            string key = Key;
            string id = VoiceId;
            string model = Model;
            if (key == null || id == null) throw new VoiceException("No voice picked.");
            StringBuilder body = new StringBuilder("{\"text\":");
            body.Append(Quote(Sayable(text)));
            if (!string.IsNullOrEmpty(model)) body.Append(",\"model_id\":").Append(Quote(model));
            body.Append('}');
            // The same line twice in a row, like the voice sliders' check line, plays the audio she already has instead of spending credits again.
            string asked = key + "\n" + id + "\n" + body;
            Tuple<string, byte[]> last = lastSaid;
            if (last != null && last.Item1 == asked) return last.Item2;
            byte[] wav = Clean(Send(Open("/v1/text-to-speech/" + Uri.EscapeDataString(id) + "?output_format=wav_24000", key, "POST"), Encoding.UTF8.GetBytes(body.ToString())));
            lastSaid = Tuple.Create(asked, wav);
            return wav;
        }

        private volatile Tuple<string, byte[]> lastSaid;

        internal static string Quote(string value)
        {
            StringBuilder quoted = new StringBuilder("\"");
            foreach (char c in value)
            {
                if (c == '"' || c == '\\') quoted.Append('\\').Append(c);
                else if (c < ' ') quoted.Append("\\u").Append(((int)c).ToString("x4"));
                else quoted.Append(c);
            }
            return quoted.Append('"').ToString();
        }

        internal static short[] Samples(byte[] wav, out int rate)
        {
            rate = 24000;
            int at = 12;
            while (at + 8 <= wav.Length)
            {
                string chunk = Encoding.ASCII.GetString(wav, at, 4);
                int size = BitConverter.ToInt32(wav, at + 4);
                if (chunk == "fmt " && at + 16 <= wav.Length) rate = Math.Max(1000, BitConverter.ToInt32(wav, at + 12));
                if (chunk == "data")
                {
                    int available = wav.Length - at - 8;
                    int count = (size > 0 && size <= available ? size : available) / 2;
                    short[] samples = new short[count];
                    Buffer.BlockCopy(wav, at + 8, samples, 0, count * 2);
                    return samples;
                }
                long next = (long)at + 8 + size + (size & 1);
                if (size < 0 || next > wav.Length) break;
                at = (int)next;
            }
            return new short[0];
        }

        internal static int[] Mouths(byte[] wav, double perSecond)
        {
            int rate;
            short[] samples = Samples(wav, out rate);
            int step = Math.Max(1, (int)(rate / perSecond));
            int count = samples.Length / step;
            double[] levels = new double[count];
            for (int i = 0; i < count; i++)
            {
                double sum = 0;
                for (int j = i * step; j < (i + 1) * step; j++) sum += (double)samples[j] * samples[j];
                levels[i] = Math.Sqrt(sum / step) / 32768.0;
            }
            double loudest = 0;
            foreach (double level in levels) loudest = Math.Max(loudest, level);
            double quiet = Math.Max(0.004, loudest * 0.08);
            List<double> talking = new List<double>();
            foreach (double level in levels) if (level > quiet) talking.Add(level);
            talking.Sort();
            Func<double, double> cut = delegate(double q) { return talking.Count == 0 ? 1 : talking[Math.Min(talking.Count - 1, (int)(q * talking.Count))]; };
            double low = cut(0.35), mid = cut(0.7), high = cut(0.97);
            int[] shapes = new int[count];
            for (int i = 0; i < count; i++)
            {
                double level = levels[i];
                shapes[i] = level <= quiet ? -1 : level < low ? (i % 3 == 0 ? 0 : 1) : level < mid ? (i % 2 == 1 ? 2 : 1) : level < high ? 3 : 4;
            }
            return shapes;
        }

        internal static double Seconds(byte[] wav)
        {
            int rate;
            return Samples(wav, out rate).Length / (double)rate;
        }

        internal static byte[] Clean(byte[] wav)
        {
            int rate;
            short[] samples = Samples(wav, out rate);
            return Wave(samples, rate);
        }

        internal static byte[] Wave(short[] samples, int rate)
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + samples.Length * 2);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(rate);
                writer.Write(rate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(samples.Length * 2);
                byte[] data = new byte[samples.Length * 2];
                Buffer.BlockCopy(samples, 0, data, 0, data.Length);
                writer.Write(data);
                writer.Flush();
                return stream.ToArray();
            }
        }

        internal void Play(byte[] wav)
        {
            wav = Louder(wav);
#if CROSS
            if (Playback != null) Playback(wav);
#else
            Stop();
            player = new SoundPlayer(new MemoryStream(wav));
            player.Play();
#endif
        }

        internal void Stop()
        {
#if CROSS
            if (Silence != null) Silence();
#else
            if (player == null) return;
            player.Stop();
            player.Dispose();
            player = null;
#endif
        }
    }

    internal enum Cue { Arrive, Leave, Hop, Click }

    internal static class Sfx
    {
        internal const int Rate = 24000;
        internal static volatile int Level = 60;
        internal static volatile bool Typing = true;
        internal static Action<short[]> Out;
        internal static Action<short[]> TypeOut;
        internal static Action Untyped;
        internal const double TypeLimit = 6;

        internal static void Play(Cue cue)
        {
            Action<short[]> output = Out;
            int level = Level;
            if (output != null && level > 0) output(Make(cue, level));
        }

        internal static void Type(string text, double pace, int seed)
        {
            Untype();
            Action<short[]> output = TypeOut;
            int level = Level;
            if (output != null && Typing && level > 0 && !string.IsNullOrEmpty(text)) output(Typed(text, pace, level, seed));
        }

        internal static void Untype()
        {
            Action hush = Untyped;
            if (hush != null) hush();
        }

        internal static short[] Typed(string text, double pace, int level, int seed)
        {
            if (string.IsNullOrEmpty(text) || pace <= 0 || level <= 0) return new short[0];
            double seconds = Math.Min(TypeLimit, text.Length / pace) + 0.05;
            int count = (int)(seconds * Rate);
            short[] samples = new short[count];
            Random random = new Random(seed);
            double peak = 0.09 * Math.Min(100, level) / 100.0;
            int length = Rate * 18 / 1000;
            for (int i = 0; i < text.Length; i += 2)
            {
                if (!char.IsLetterOrDigit(text[i])) continue;
                double at = i / pace;
                if (at >= TypeLimit) break;
                double fade = at < TypeLimit - 1 ? 1 : TypeLimit - at;
                int start = (int)(at * Rate) + random.Next(0, Rate / 200);
                double pitch = 1900 * (0.9 + random.NextDouble() * 0.2);
                double loud = peak * fade * (0.7 + random.NextDouble() * 0.3);
                double phase = 0, smooth = 0;
                for (int j = 0; j < length && start + j < count; j++)
                {
                    double t = (double)j / Rate;
                    phase += 2 * Math.PI * pitch / Rate;
                    double envelope = Math.Min(1, t / 0.0015) * Math.Exp(-t / 0.004);
                    double wave = Math.Sin(phase) + 0.3 * Math.Sin(phase * 2.01);
                    smooth += (wave - smooth) * 0.45;
                    int mixed = samples[start + j] + (int)(smooth * envelope / 1.3 * loud * 32767);
                    samples[start + j] = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, mixed));
                }
            }
            return samples;
        }

        internal static short[] Make(Cue cue, int level)
        {
            switch (cue)
            {
                case Cue.Arrive: return Sweep(0.42, 330, 990, 0.03, level);
                case Cue.Leave: return Sweep(0.36, 880, 250, 0.012, level);
                case Cue.Hop: return Sweep(0.16, 520, 1240, 0.008, level);
                default: return Sweep(0.05, 1600, 1350, 0.003, level * 3 / 4);
            }
        }

        private static short[] Sweep(double seconds, double from, double to, double attack, int level)
        {
            int count = (int)(seconds * Rate);
            short[] samples = new short[count];
            double phase = 0, fifth = 0, smooth = 0;
            double peak = 0.26 * Math.Max(0, Math.Min(100, level)) / 100.0;
            for (int i = 0; i < count; i++)
            {
                double t = (double)i / Rate;
                double along = (double)i / count;
                double pitch = from * Math.Pow(to / from, 1 - Math.Pow(1 - along, 3));
                phase += 2 * Math.PI * pitch / Rate;
                fifth += 3 * Math.PI * pitch / Rate;
                double envelope = Math.Min(1, t / attack) * Math.Pow(1 - along, 2.2);
                double wave = Math.Sin(phase) + 0.28 * Math.Sin(fifth) + 0.12 * Math.Sin(2 * phase);
                smooth += (wave - smooth) * 0.5;
                samples[i] = (short)(Math.Max(-1, Math.Min(1, smooth * envelope / 1.4)) * peak * 32767);
            }
            return samples;
        }
    }

    // Her voices' pitch, speed, robot and glitch from the Settings page, or her right-click Settings on Mac and Linux. At their
    // defaults nothing here touches a sample, her HD voice included: it starts dry, like the clip it copies.
    internal static class VoiceFx
    {
        internal const int MostPitch = 6;
        internal const int Slowest = 70;
        internal const int Fastest = 150;
        internal const int HdRobotDefault = 0;
        internal static volatile int Pitch;
        internal static volatile int Speed = 100;
        // Robot and glitch are kept per voice: one for her free voice styles and ElevenLabs, one for her HD voice, one for their voice server.
        internal static volatile int Robot;
        internal static volatile int HdRobot = HdRobotDefault;
        internal static volatile int ServerRobot;
        internal static volatile int Glitch;
        internal static volatile int HdGlitch;
        internal static volatile int ServerGlitch;
        // Her voice layers (Lachy, 29 Oct): Main is her voice as the sliders above make it, and Layer 1 and Layer 2 are copies of
        // it, each pitched on its own without changing its length, delayed and mixed back in under her. They're off out of the
        // box, and off leaves every sample alone. Every voice she has gets them, like her pitch, speed, robot and glitch.
        internal static volatile Layering Layers = Layering.Off;
        // How loud each layer mixes in under her: 0.4, 8 dB down, so she's doubled with a shimmer rather than singing a chord.
        internal const double LayerLevel = 0.4;

        internal static double Lift(int semitones) { return Math.Pow(2, semitones / 12.0); }

        // Kokoro talks slower or faster itself, so her free voice is made longer or shorter by the pitch's ratio and the resample brings it back.
        internal static float Tempo(float style, int semitones, int speed)
        {
            if (semitones == 0 && speed == 100) return style;
            return (float)Math.Max(0.5, Math.Min(3, style * speed / 100.0 / Lift(semitones)));
        }

        // Her free voice after Kokoro, which already took care of the speed.
        internal static short[] Kokoro(short[] pcm, int rate, int semitones, int robot, int glitch, int seed)
        {
            return Kokoro(pcm, rate, semitones, robot, glitch, seed, Layering.Off);
        }

        internal static short[] Kokoro(short[] pcm, int rate, int semitones, int robot, int glitch, int seed, Layering layers)
        {
            if (Plain(semitones, 100, robot, glitch, layers)) return pcm;
            return new Live(rate, semitones, 100, robot, glitch, seed, false, layers).All(pcm);
        }

        // Her HD and ElevenLabs voices have no pitch of their own, so a time-stretch keeps a pitch change from speeding her up or slowing her down.
        internal static short[] Restyled(short[] pcm, int rate, int semitones, int speed, int robot, int glitch, int seed)
        {
            return Restyled(pcm, rate, semitones, speed, robot, glitch, seed, Layering.Off);
        }

        internal static short[] Restyled(short[] pcm, int rate, int semitones, int speed, int robot, int glitch, int seed, Layering layers)
        {
            if (Plain(semitones, speed, robot, glitch, layers)) return pcm;
            return new Live(rate, semitones, speed, robot, glitch, seed, true, layers).All(pcm);
        }

        // Whether her voice needs styling at all: with every slider at its default and the layers off, it's left as it is.
        internal static bool Plain(int semitones, int speed, int robot, int glitch, Layering layers)
        {
            return semitones == 0 && speed == 100 && robot <= 0 && glitch <= 0 && (layers == null || !layers.On);
        }

        // A finished WAV, like an ElevenLabs reply, in her current pitch, speed, robot, glitch and layers.
        internal static byte[] Styled(byte[] wav, string text)
        {
            int pitch = Pitch, speed = Speed, robot = Robot, glitch = Glitch;
            Layering layers = Layers;
            if (wav == null || Plain(pitch, speed, robot, glitch, layers)) return wav;
            int rate;
            short[] samples = Voice.Samples(wav, out rate);
            return samples.Length == 0 ? wav : Voice.Wave(Restyled(samples, rate, pitch, speed, robot, glitch, Seed(text), layers), rate);
        }

        // The same line glitches and hisses the same way every time.
        internal static int Seed(string text)
        {
            int hash = unchecked((int)2166136261u);
            foreach (char letter in text ?? "") hash = unchecked((hash ^ letter) * 16777619);
            return hash & 0x7fffffff;
        }

        // Plays the sound `factor` times faster: higher and shorter by that ratio.
        internal static short[] Resample(short[] pcm, double factor)
        {
            Resampler resampler = new Resampler(factor);
            return Join(resampler.Push(pcm), resampler.Flush());
        }

        internal static short[] Glitched(short[] pcm, int rate, double amount, int seed)
        {
            int events;
            return Glitched(pcm, rate, amount, seed, out events);
        }

        internal static short[] Glitched(short[] pcm, int rate, double amount, int seed, out int events)
        {
            events = 0;
            if (amount <= 0) return pcm;
            Glitcher glitcher = new Glitcher(rate, amount, seed);
            short[] made = Join(glitcher.Push(pcm), glitcher.Flush());
            events = glitcher.Events;
            return made;
        }

        private static short[] Join(short[] one, short[] two)
        {
            if (two.Length == 0) return one;
            if (one.Length == 0) return two;
            short[] both = new short[one.Length + two.Length];
            Array.Copy(one, both, one.Length);
            Array.Copy(two, 0, both, one.Length, two.Length);
            return both;
        }

        private static short Clip(double value) { return (short)Math.Max(-32768, Math.Min(32767, Math.Round(value))); }

        // Like Clip, but the loudest peaks bend over smoothly instead of squaring off.
        private static short Soft(double value)
        {
            double size = Math.Abs(value);
            if (size > 28000) value = Math.Sign(value) * (28000 + 4767 * Math.Tanh((size - 28000) / 4767));
            return Clip(value);
        }

        // Her voice styled as it streams in, a chunk at a time, for ElevenLabs voice chat. Each step holds back only what it still has
        // to look ahead at, so any chunking sounds the same as styling the whole line at once. Flush lets out what's held at the end.
        internal sealed class Live
        {
            internal readonly int Rate;
            internal readonly int Pitch;
            internal readonly int Speed;
            internal readonly int Robot;
            internal readonly int Glitch;
            internal readonly Layering Layers;
            private readonly Stretcher stretcher;
            private readonly Resampler resampler;
            private readonly Vocoder vocoder;
            private readonly Glitcher glitcher;
            private readonly Layerer layerer;

            // `timed` is false for Kokoro, which already made the line longer or shorter for the pitch and speed.
            internal Live(int rate, int semitones, int speed, int robot, int glitch, int seed, bool timed) : this(rate, semitones, speed, robot, glitch, seed, timed, Layering.Off) { }

            // Her layers go on last, on her voice as everything else left it.
            internal Live(int rate, int semitones, int speed, int robot, int glitch, int seed, bool timed, Layering layers)
            {
                Rate = rate;
                Pitch = semitones;
                Speed = speed;
                Robot = robot;
                Glitch = glitch;
                Layers = layers ?? Layering.Off;
                double lift = Lift(semitones), stretch = timed ? Stretch(lift, speed) : 1;
                if (Math.Abs(stretch - 1) > 0.001) stretcher = new Stretcher(rate, stretch);
                if (semitones != 0) resampler = new Resampler(lift);
                if (robot > 0) vocoder = new Vocoder(rate, robot / 100.0, seed);
                if (glitch > 0) glitcher = new Glitcher(rate, glitch / 100.0, seed);
                if (Layers.On) layerer = new Layerer(rate, Layers);
            }

            internal short[] Push(short[] chunk)
            {
                if (stretcher != null) chunk = stretcher.Push(chunk);
                if (resampler != null) chunk = resampler.Push(chunk);
                if (vocoder != null) chunk = vocoder.Push(chunk);
                if (glitcher != null) chunk = glitcher.Push(chunk);
                if (layerer != null) chunk = layerer.Push(chunk);
                return chunk;
            }

            // The rest of the line, her layers' delayed tails included. Afterwards it starts over, ready for the next one.
            internal short[] Flush()
            {
                short[] tail = stretcher != null ? stretcher.Flush() : new short[0];
                if (resampler != null) tail = Join(resampler.Push(tail), resampler.Flush());
                if (vocoder != null) tail = Join(vocoder.Push(tail), vocoder.Flush());
                if (glitcher != null) tail = Join(glitcher.Push(tail), glitcher.Flush());
                if (layerer != null) tail = Join(layerer.Push(tail), layerer.Flush());
                return tail;
            }

            internal short[] All(short[] pcm) { return Join(Push(pcm), Flush()); }
        }

        // How much longer the time-stretch makes her for a pitch's ratio and her speed, so the resample brings her back to time.
        private static double Stretch(double lift, int speed) { return lift * 100 / Math.Max(1, speed); }

        // Her voice layers' settings: on or off, and each layer's pitch, -12 to +12 semitones from Main in half steps, and its
        // delay, 0 to 250 ms. A change makes a new one, so a line she's saying keeps the layers it started with. Layer 1 starts
        // a little higher than her and Layer 2 lower, both just behind her.
        internal sealed class Layering
        {
            internal const double MostPitch = 12;
            internal const int MostDelay = 250;
            internal const double HigherPitch = 1.5;
            internal const double LowerPitch = -2.5;
            internal const int Behind = 25;
            internal static readonly Layering Off = new Layering(false, HigherPitch, Behind, LowerPitch, Behind);
            internal readonly bool On;
            internal readonly double Pitch1;
            internal readonly int Delay1;
            internal readonly double Pitch2;
            internal readonly int Delay2;

            internal Layering(bool on, double pitch1, int delay1, double pitch2, int delay2)
            {
                On = on;
                Pitch1 = Step(pitch1);
                Delay1 = Wait(delay1);
                Pitch2 = Step(pitch2);
                Delay2 = Wait(delay2);
            }

            // To the nearest half step inside the range.
            internal static double Step(double semitones)
            {
                if (double.IsNaN(semitones) || double.IsInfinity(semitones)) return 0;
                return Math.Max(-MostPitch, Math.Min(MostPitch, Math.Round(semitones * 2, MidpointRounding.AwayFromZero) / 2));
            }

            internal static int Wait(int ms) { return Math.Max(0, Math.Min(MostDelay, ms)); }

            internal Layering Turned(bool on) { return new Layering(on, Pitch1, Delay1, Pitch2, Delay2); }

            // Layer 1 or 2 with a new pitch and delay, the other as it was.
            internal Layering With(int layer, double pitch, int delay) { return layer == 1 ? new Layering(On, pitch, delay, Pitch2, Delay2) : new Layering(On, Pitch1, Delay1, pitch, delay); }

            internal double PitchOf(int layer) { return layer == 1 ? Pitch1 : Pitch2; }

            internal int DelayOf(int layer) { return layer == 1 ? Delay1 : Delay2; }

            internal bool Same(Layering other) { return other != null && other.On == On && other.Pitch1 == Pitch1 && other.Delay1 == Delay1 && other.Pitch2 == Pitch2 && other.Delay2 == Delay2; }

            // A layer's pitch as Settings shows it: "+1.5", "Same", "-12".
            internal static string PitchName(double semitones)
            {
                if (semitones == 0) return "Same";
                return (semitones > 0 ? "+" : "-") + Math.Abs(semitones).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            }

            internal static string DelayName(int ms) { return ms + " ms"; }

            // "On | +1.5, 25 ms | -2.5, 25 ms", for the smoke tests.
            internal string Shown { get { return (On ? "On" : "Off") + " | " + PitchName(Pitch1) + ", " + DelayName(Delay1) + " | " + PitchName(Pitch2) + ", " + DelayName(Delay2); } }
        }

        // Her two layers under her: each a copy of her finished voice, pitched without changing its length (the time-stretch
        // and resample her HD voice's pitch uses), delayed and mixed in LayerLevel under her, then kept under full scale by a
        // limiter. Each copy lines up sample for sample with her, so any chunking mixes the same, and Flush lets the delayed
        // copies ring out past her last word.
        private sealed class Layerer
        {
            private readonly Copy[] copies;
            private readonly Limiter limiter;
            private readonly int longest;
            private readonly List<short> held = new List<short>();
            private long first;
            private long mixed;

            internal Layerer(int rate, Layering layers)
            {
                copies = new[] { new Copy(rate, layers.Pitch1, layers.Delay1), new Copy(rate, layers.Pitch2, layers.Delay2) };
                longest = Math.Max(copies[0].Delay, copies[1].Delay);
                limiter = new Limiter(rate);
            }

            private long Received { get { return first + held.Count; } }

            internal short[] Push(short[] chunk)
            {
                held.AddRange(chunk);
                long ready = Received;
                foreach (Copy copy in copies)
                {
                    copy.Push(chunk);
                    ready = Math.Min(ready, copy.Made + copy.Delay);
                }
                return limiter.Push(Mix(ready));
            }

            internal short[] Flush()
            {
                foreach (Copy copy in copies) copy.Finish();
                short[] tail = Join(limiter.Push(Mix(Received + longest)), limiter.Flush());
                held.Clear();
                first = mixed = 0;
                foreach (Copy copy in copies) copy.Restart();
                return tail;
            }

            // Her and both copies, up to the first sample a copy hasn't reached yet, or to the end of their tails at the end.
            private double[] Mix(long upTo)
            {
                double[] output = new double[Math.Max(0, upTo - mixed)];
                for (int i = 0; i < output.Length; i++, mixed++)
                {
                    long j = mixed - first;
                    double value = j < held.Count ? held[(int)j] : 0;
                    foreach (Copy copy in copies) value += LayerLevel * copy.At(mixed - copy.Delay);
                    output[i] = value;
                }
                int drop = (int)Math.Max(0, Math.Min(held.Count, mixed - first));
                held.RemoveRange(0, drop);
                first += drop;
                foreach (Copy copy in copies) copy.Drop(mixed - copy.Delay);
                return output;
            }

            // One layer: her voice at another pitch and the same length, `Delay` samples behind her.
            private sealed class Copy
            {
                internal readonly int Delay;
                private readonly Stretcher stretcher;
                private readonly Resampler resampler;
                private readonly List<short> made = new List<short>();
                private long first;

                internal Copy(int rate, double semitones, int ms)
                {
                    Delay = (int)Math.Round(ms * (double)rate / 1000);
                    if (semitones == 0) return;
                    double lift = Math.Pow(2, semitones / 12.0);
                    stretcher = new Stretcher(rate, Stretch(lift, 100));
                    resampler = new Resampler(lift);
                }

                internal long Made { get { return first + made.Count; } }

                internal void Push(short[] chunk) { made.AddRange(stretcher == null ? chunk : resampler.Push(stretcher.Push(chunk))); }

                internal void Finish()
                {
                    if (stretcher == null) return;
                    made.AddRange(resampler.Push(stretcher.Flush()));
                    made.AddRange(resampler.Flush());
                }

                internal double At(long index)
                {
                    long j = index - first;
                    return j >= 0 && j < made.Count ? made[(int)j] : 0;
                }

                internal void Drop(long before)
                {
                    int drop = (int)Math.Max(0, Math.Min(made.Count, before - first));
                    made.RemoveRange(0, drop);
                    first += drop;
                }

                internal void Restart()
                {
                    made.Clear();
                    first = 0;
                }
            }
        }

        // Keeps her layered voice under full scale. A sample's gain is the least any sample up to 2.5 ms ahead needs, eased in
        // over those 2.5 ms and let back up over about 60 ms, so a peak is turned down smoothly before it arrives. Below full
        // scale every sample goes through as it is. Each sample waits only for the 2.5 ms after it, so chunking changes nothing.
        private sealed class Limiter
        {
            internal const double Ceiling = 31000;
            private readonly int ahead;
            private readonly double letUp;
            private readonly double[] recent;
            private readonly List<double> held = new List<double>();
            private readonly List<double> lows = new List<double>();
            private readonly List<long> lowAt = new List<long>();
            private long taken;
            private double gain;
            private int under;

            internal Limiter(int rate)
            {
                ahead = Math.Max(1, rate / 400);
                letUp = Math.Exp(-1 / (0.06 * rate));
                recent = new double[ahead + 1];
                Restart();
            }

            // A line starts with `ahead` samples of silence it doesn't let out, so the gain before her first sample is worked
            // out like any other and the first peak is eased into too.
            private void Restart()
            {
                held.Clear();
                lows.Clear();
                lowAt.Clear();
                taken = 0;
                gain = 1;
                under = 0;
                for (int i = 0; i < recent.Length; i++) recent[i] = 1;
                for (int i = 0; i < ahead; i++) Take(0, 0, null);
            }

            internal short[] Push(double[] chunk)
            {
                List<short> output = new List<short>(chunk.Length);
                foreach (double value in chunk) Take(value, long.MaxValue, output);
                return output.ToArray();
            }

            internal short[] Flush()
            {
                List<short> output = new List<short>();
                long end = taken;
                for (int i = 0; i < ahead; i++) Take(0, end, output);
                Restart();
                return output.ToArray();
            }

            private void Take(double value, long end, List<short> output)
            {
                double size = Math.Abs(value), need = size > Ceiling ? Ceiling / size : 1;
                // The lowest need from here to `ahead` samples on: a queue of needs, each lower than the one behind it.
                while (lows.Count > 0 && lows[lows.Count - 1] >= need)
                {
                    lows.RemoveAt(lows.Count - 1);
                    lowAt.RemoveAt(lowAt.Count - 1);
                }
                lows.Add(need);
                lowAt.Add(taken);
                held.Add(value);
                long at = taken++ - ahead;
                if (at < 0) return;
                while (lowAt[0] < at)
                {
                    lowAt.RemoveAt(0);
                    lows.RemoveAt(0);
                }
                gain = Math.Min(lows[0], 1 - (1 - gain) * letUp);
                if (gain > 0.99999 && lows[0] >= 1) gain = 1;
                int slot = (int)(at % recent.Length);
                if (recent[slot] < 1) under--;
                recent[slot] = gain;
                if (gain < 1) under++;
                // Averaged over the last `ahead` samples, so the gain glides down before a peak instead of jumping.
                double eased = 1;
                if (under > 0)
                {
                    eased = 0;
                    foreach (double step in recent) eased += step;
                    eased /= recent.Length;
                }
                double sample = held[0];
                held.RemoveAt(0);
                if (at >= ahead && at < end) output.Add(Clip(sample * eased));
            }
        }

        // The smoke tests' checks of her layers, the same on Windows, Mac and Linux: null when they all pass, or what went wrong.
        // `found` says what they measured, for the PASS line.
        internal static string LayersCheck(out string found)
        {
            const int rate = 24000;
            List<string> wrong = new List<string>();
            Random hiss = new Random(11);
            short[] tone = new short[rate], said = Line(rate, 2, hiss);
            for (int i = 0; i < tone.Length; i++) tone[i] = (short)(6000 * Math.Sin(2 * Math.PI * 200 * i / rate) + 3000 * Math.Sin(2 * Math.PI * 400 * i / rate) + 1500 * Math.Sin(2 * Math.PI * 600 * i / rate));
            // Off: nothing changes, whatever the layers are set to.
            Layering off = new Layering(false, 7, 200, -9, 90);
            if (Layering.Off.On || Kokoro(tone, rate, 0, 0, 0, 1, off) != tone || Restyled(tone, rate, 0, 100, 0, 0, 1, off) != tone || !Plain(0, 100, 0, 0, off) || Plain(0, 100, 0, 0, off.Turned(true))) wrong.Add("off isn't untouched");
            if (!Same(Restyled(said, rate, 3, 120, 30, 40, 5, off), Restyled(said, rate, 3, 120, 30, 40, 5)) || !Same(Kokoro(said, rate, -2, 20, 10, 5, off), Kokoro(said, rate, -2, 20, 10, 5)) || !Same(new Live(rate, 2, 90, 0, 20, 5, true, off).All(said), new Live(rate, 2, 90, 0, 20, 5, true).All(said)))
                wrong.Add("off changed a styled voice");
            // On: her voice plus each copy, pitched by the length-keeping shift and delayed, at LayerLevel, sample for sample.
            Layering on = new Layering(true, 5, 100, -7, 200);
            short[] layered = Restyled(said, rate, 0, 100, 0, 0, 5, on), up = Restyled(said, rate, 5, 100, 0, 0, 5), down = Restyled(said, rate, -7, 100, 0, 0, 5);
            int far = 0, behind1 = rate / 10, behind2 = rate / 5;
            for (int i = 0; i < layered.Length; i++) far = Math.Max(far, (int)Math.Abs(layered[i] - Math.Round(At(said, i) + LayerLevel * (At(up, i - behind1) + At(down, i - behind2)))));
            if (layered.Length != said.Length + behind2 || far > 1) wrong.Add("on isn't her voice plus each layer at its delay (" + layered.Length + " samples, " + far + " off)");
            int[] mouths = Voice.Mouths(Voice.Wave(layered, rate), 12);
            bool tailMoves = false;
            for (int i = said.Length / (rate / 12); i < mouths.Length; i++) tailMoves |= mouths[i] >= 0;
            if (!tailMoves) wrong.Add("her mouth doesn't follow the layers' tail");
            // Each copy's pitch: a 220 Hz tone comes back with a copy at 220 Hz times each layer's ratio, half steps too, each
            // LayerLevel under her.
            short[] steady = new short[rate];
            for (int i = 0; i < steady.Length; i++) steady[i] = (short)(8000 * Math.Sin(2 * Math.PI * 220 * i / rate));
            List<string> ratios = new List<string>();
            foreach (double[] pair in new[] { new[] { 5.0, -7.0 }, new[] { 1.5, -2.5 } })
            {
                short[] mixed = Restyled(steady, rate, 0, 100, 0, 0, 5, new Layering(true, pair[0], Layering.Behind, pair[1], Layering.Behind));
                double strongest, higher, lower;
                double main = Peak(mixed, rate, 220, out strongest), high = Peak(mixed, rate, 220 * Math.Pow(2, pair[0] / 12), out higher), low = Peak(mixed, rate, 220 * Math.Pow(2, pair[1] / 12), out lower);
                double upBy = high / main, downBy = low / main;
                ratios.Add(Layering.PitchName(pair[0]) + " x" + upBy.ToString("0.000") + " at " + (20 * Math.Log10(higher / strongest)).ToString("0.0") + " dB, " + Layering.PitchName(pair[1]) + " x" + downBy.ToString("0.000") + " at " + (20 * Math.Log10(lower / strongest)).ToString("0.0") + " dB");
                if (Math.Abs(main - 220) > 1 || Math.Abs(upBy / Math.Pow(2, pair[0] / 12) - 1) > 0.005 || Math.Abs(downBy / Math.Pow(2, pair[1] / 12) - 1) > 0.005
                    || higher / strongest < LayerLevel * 0.6 || higher / strongest > LayerLevel * 1.4 || lower / strongest < LayerLevel * 0.6 || lower / strongest > LayerLevel * 1.4)
                    wrong.Add("a layer's pitch is off (" + ratios[ratios.Count - 1] + ")");
            }
            // Streamed in chunks of any size, a layered voice comes out the same as layered whole, with the other sliders on too,
            // and the same Live says the next line the same way.
            Random chunks = new Random(4);
            bool streams = true;
            foreach (int[] set in new[] { new[] { 24000, 0, 100, 0, 0 }, new[] { 16000, 4, 120, 10, 50 }, new[] { 22050, -3, 80, 30, 0 }, new[] { 44100, 0, 100, 0, 0 } })
            {
                Layering layers = set[1] == 0 ? new Layering(true, Layering.HigherPitch, Layering.Behind, Layering.LowerPitch, Layering.Behind) : new Layering(true, -12, 250, 12, 0);
                short[] line = Line(set[0], 2, hiss), whole = new Live(set[0], set[1], set[2], set[3], set[4], 77, true, layers).All(line);
                Live live = new Live(set[0], set[1], set[2], set[3], set[4], 77, true, layers);
                List<short> pieces = new List<short>();
                for (int at = 0; at < line.Length; )
                {
                    short[] piece = new short[Math.Min(line.Length - at, 1 + chunks.Next(chunks.Next(3) == 0 ? 20 : 4000))];
                    Array.Copy(line, at, piece, 0, piece.Length);
                    pieces.AddRange(live.Push(piece));
                    at += piece.Length;
                }
                pieces.AddRange(live.Flush());
                streams &= Same(pieces.ToArray(), whole) && (set[4] > 0 || Same(live.All(line), whole));
            }
            if (!streams) wrong.Add("streamed in chunks, a layered voice came out different");
            // Too loud: a tone at 95% of full scale with both copies right on it, 1.8 times full scale, is held under it, and so is
            // one with the copies pitched and delayed against it.
            short[] loud = new short[rate];
            for (int i = 0; i < loud.Length; i++) loud[i] = (short)(31100 * Math.Sin(2 * Math.PI * 200 * i / rate));
            short[] stacked = Restyled(loud, rate, 0, 100, 0, 0, 5, new Layering(true, 0, 0, 0, 0)), spread = Restyled(loud, rate, 0, 100, 0, 0, 5, new Layering(true, 3, 10, -5, 30));
            int peak = 0;
            double power = 0;
            foreach (short sample in stacked) { peak = Math.Max(peak, Math.Abs((int)sample)); power += (double)sample * sample; }
            foreach (short sample in spread) peak = Math.Max(peak, Math.Abs((int)sample));
            double level = Math.Sqrt(power / Math.Max(1, stacked.Length));
            if (stacked.Length != loud.Length || peak > Limiter.Ceiling || level < 20000) wrong.Add("too loud isn't held under full scale (peak " + peak + ", level " + level.ToString("0") + ")");
            found = "delays of 100 and 200 ms land sample for sample and ring out " + (layered.Length - said.Length) * 1000 / rate + " ms past her; " + string.Join("; ", ratios.ToArray()) + "; a 1.8x full-scale stack peaks at " + peak + " of 32767";
            return wrong.Count == 0 ? null : string.Join(" / ", wrong.ToArray());
        }

        // A line of tone bursts with hiss between, like speech.
        private static short[] Line(int rate, int seconds, Random hiss)
        {
            short[] line = new short[rate * seconds];
            for (int i = 0; i < line.Length; i++) line[i] = i % (rate / 4) < rate / 6 ? (short)(5000 * Math.Sin(2 * Math.PI * 170 * i / rate) + 1500 * Math.Sin(2 * Math.PI * 510 * i / rate)) : (short)(hiss.Next(200) - 100);
            return line;
        }

        private static double At(short[] sound, long index) { return index >= 0 && index < sound.Length ? sound[index] : 0; }

        private static bool Same(short[] one, short[] other)
        {
            if (one.Length != other.Length) return false;
            for (int i = 0; i < one.Length; i++) if (one[i] != other[i]) return false;
            return true;
        }

        // The strongest frequency within 4% of `near`, to a quarter hertz, over 0.3 to 0.9 s of a sound, by Goertzel with a Hann window.
        private static double Peak(short[] sound, int rate, double near, out double strength)
        {
            int from = rate * 3 / 10, count = rate * 6 / 10;
            double best = near;
            strength = 0;
            for (double hertz = near * 0.96; hertz <= near * 1.04; hertz += 0.25)
            {
                double turn = 2 * Math.Cos(2 * Math.PI * hertz / rate), one = 0, two = 0;
                for (int i = 0; i < count; i++)
                {
                    double next = At(sound, from + i) * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (count - 1))) + turn * one - two;
                    two = one;
                    one = next;
                }
                double size = Math.Sqrt(Math.Max(0, one * one + two * two - turn * one * two));
                if (size > strength) { strength = size; best = hertz; }
            }
            return best;
        }

        // A windowed sinc, so a higher voice doesn't alias. Output i reads the input around i × factor.
        private sealed class Resampler
        {
            private const int Phases = 256;
            private const int Reach = 16;
            private readonly double factor;
            private readonly int reach;
            private readonly int taps;
            private readonly double[] kernel;
            private readonly List<short> held = new List<short>();
            private long first;
            private long made;

            internal Resampler(double factor)
            {
                this.factor = factor;
                double cutoff = Math.Min(1, 1 / factor);
                reach = (int)Math.Ceiling(Reach / cutoff);
                taps = 2 * reach;
                kernel = new double[(Phases + 1) * taps];
                for (int phase = 0; phase <= Phases; phase++)
                {
                    double fraction = (double)phase / Phases, total = 0;
                    for (int k = 0; k < taps; k++)
                    {
                        double x = k - reach + 1 - fraction, t = x * cutoff;
                        double weight = (Math.Abs(t) < 1e-9 ? 1 : Math.Sin(Math.PI * t) / (Math.PI * t)) * (Math.Abs(x) >= reach ? 0 : 0.5 + 0.5 * Math.Cos(Math.PI * x / reach));
                        kernel[phase * taps + k] = weight;
                        total += weight;
                    }
                    for (int k = 0; k < taps; k++) kernel[phase * taps + k] /= total;
                }
            }

            internal short[] Push(short[] chunk)
            {
                held.AddRange(chunk);
                List<short> output = new List<short>();
                while ((long)Math.Floor(made * factor) + reach < first + held.Count) output.Add(Next());
                int drop = (int)Math.Max(0, Math.Min(held.Count, (long)Math.Floor(made * factor) - reach + 1 - first));
                held.RemoveRange(0, drop);
                first += drop;
                return output.ToArray();
            }

            internal short[] Flush()
            {
                List<short> output = new List<short>();
                long count = (long)Math.Round((first + held.Count) / factor);
                while (made < count) output.Add(Next());
                held.Clear();
                first = made = 0;
                return output.ToArray();
            }

            private short Next()
            {
                double center = made * factor;
                long whole = (long)Math.Floor(center);
                int row = (int)Math.Round((center - whole) * Phases) * taps;
                long start = whole - reach + 1 - first;
                double value = 0;
                for (int k = 0; k < taps; k++)
                {
                    long j = start + k;
                    if (j >= 0 && j < held.Count) value += held[(int)j] * kernel[row + k];
                }
                made++;
                return Clip(value);
            }
        }

        // Makes the sound `stretch` times longer at the same pitch: 20 ms windows, each lined up with the one before it within 5 ms (WSOLA).
        private sealed class Stretcher
        {
            private readonly double stretch;
            private readonly int frame;
            private readonly int hop;
            private readonly int tolerance;
            private readonly double[] window;
            private readonly double[] near;
            private readonly double[] span;
            private readonly List<short> held = new List<short>();
            private readonly List<double> sum = new List<double>();
            private readonly List<double> weight = new List<double>();
            private long first;
            private long emitted;
            private long placed;
            private long previous = -1;

            internal Stretcher(int rate, double stretch)
            {
                this.stretch = stretch;
                frame = Math.Max(32, rate / 50) & ~1;
                hop = frame / 2;
                tolerance = Math.Max(1, rate / 200);
                near = new double[(frame + 3) / 4];
                span = new double[2 * tolerance + frame];
                window = new double[frame];
                for (int i = 0; i < frame; i++) window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / frame);
            }

            private long Received { get { return first + held.Count; } }

            private long Nominal { get { return (long)Math.Round(placed * hop / stretch); } }

            internal short[] Push(short[] chunk)
            {
                held.AddRange(chunk);
                // A window waits until everything it might read has arrived.
                while (Nominal + tolerance + frame <= Received && (previous < 0 || previous + hop + frame <= Received)) Place(Nominal, long.MaxValue);
                return Emit(placed * hop);
            }

            internal short[] Flush()
            {
                long length = Received, count = (long)Math.Round(length * stretch), last = length - frame;
                short[] made;
                if (placed == 0 && (last < 2 * tolerance || count < frame)) made = held.ToArray();
                else
                {
                    while (placed * hop < count) Place(Math.Min(last, Nominal), last);
                    made = Emit(count);
                }
                held.Clear();
                sum.Clear();
                weight.Clear();
                first = emitted = placed = 0;
                previous = -1;
                return made;
            }

            private void Place(long nominal, long last)
            {
                long best = nominal;
                if (previous >= 0)
                {
                    long natural = Math.Min(last, previous + hop);
                    double closest = double.MinValue;
                    // The sound it compares, copied out once per window: the same numbers in the same order, so it lines up
                    // exactly as before, a few times faster, which her voice layers' two copies need.
                    for (int j = 0, k = 0; j < frame; j += 4, k++) near[k] = At(natural + j);
                    for (int k = 0; k < span.Length; k++) span[k] = At(nominal - tolerance + k);
                    for (int shift = -tolerance; shift <= tolerance; shift++)
                    {
                        long candidate = nominal + shift;
                        if (candidate < 0 || candidate > last) continue;
                        double dot = 0, energy = 1;
                        for (int j = 0, k = 0, from = shift + tolerance; j < frame; j += 4, k++)
                        {
                            double ahead = span[from + j];
                            dot += near[k] * ahead;
                            energy += ahead * ahead;
                        }
                        double score = dot / Math.Sqrt(energy);
                        if (score > closest) { closest = score; best = candidate; }
                    }
                }
                long at = placed * hop;
                while (emitted + sum.Count < at + frame) { sum.Add(0); weight.Add(0); }
                for (int j = 0; j < frame; j++)
                {
                    int slot = (int)(at + j - emitted);
                    sum[slot] += window[j] * At(best + j);
                    weight[slot] += window[j];
                }
                previous = best;
                placed++;
            }

            private double At(long index)
            {
                long j = index - first;
                return j >= 0 && j < held.Count ? held[(int)j] : 0;
            }

            // Lets out the finished samples, then drops input no later window can read.
            private short[] Emit(long upTo)
            {
                int count = (int)Math.Max(0, upTo - emitted);
                short[] output = new short[count];
                for (int i = 0; i < count && i < sum.Count; i++) output[i] = weight[i] > 1e-3 ? Clip(sum[i] / weight[i]) : (short)0;
                int done = Math.Min(count, sum.Count);
                sum.RemoveRange(0, done);
                weight.RemoveRange(0, done);
                emitted += count;
                long keep = Math.Min(Math.Min(previous >= 0 ? previous + hop : Nominal, Nominal - tolerance), Received - frame - tolerance);
                int drop = (int)Math.Max(0, Math.Min(held.Count, keep - first));
                held.RemoveRange(0, drop);
                first += drop;
                return output;
            }
        }

        // Her glitch: short stutters, crushed bits and skips, landing on sounds rather than silences, and more of them as the slider goes up.
        private sealed class Glitcher
        {
            private readonly int rate;
            private readonly int block;
            private readonly int fade;
            private readonly int reach;
            private readonly double amount;
            private readonly double perSecond;
            private readonly Random random;
            private readonly List<short> held = new List<short>();
            private readonly List<double> peaks = new List<double>();
            private long first;
            private long peakBase;
            private long peaked;
            private long at;
            private long next;
            private double loudest;
            internal int Events;

            internal Glitcher(int rate, double amount, int seed)
            {
                this.rate = rate;
                this.amount = amount;
                block = Math.Max(1, rate / 100);
                fade = Math.Max(1, rate / 400);
                // The furthest an event reads past where it starts: a crush of up to 160 ms.
                reach = (int)(rate * 0.16) + fade;
                // About one every 10 seconds at 5%, one every 5 at 10%, three a second at 100%.
                perSecond = amount * (2 + amount);
                random = new Random(seed);
                Restart();
            }

            private long Received { get { return first + held.Count; } }

            private void Restart()
            {
                held.Clear();
                peaks.Clear();
                first = peakBase = peaked = at = 0;
                loudest = 0;
                next = (long)(rate * (0.1 + random.NextDouble() / perSecond));
            }

            internal short[] Push(short[] chunk)
            {
                held.AddRange(chunk);
                List<short> output = new List<short>();
                Run(output, -1);
                return output.ToArray();
            }

            internal short[] Flush()
            {
                List<short> output = new List<short>();
                long length = Received;
                if (length / block >= 20) Run(output, length);
                else Copy(output, length);
                Restart();
                return output.ToArray();
            }

            // An event waits until the sound it needs has arrived. Sound before the next place one could start goes straight out.
            private void Run(List<short> output, long length)
            {
                bool ending = length >= 0;
                long blocks = (ending ? length : Received) / block, end = ending ? length - rate / 10 : long.MaxValue;
                for (; peaked < blocks; peaked++)
                {
                    loudest = Math.Max(loudest, Level(peaked));
                    peaks.Add(loudest);
                }
                while (next < end)
                {
                    long from = next / block, to = Math.Min(blocks, from + 12);
                    if (!ending && from + 12 > blocks) break;
                    long found = -1;
                    if (to > from)
                    {
                        // Loud means above 15% of the loudest block so far.
                        double floor = 0.15 * peaks[(int)(to - 1 - peakBase)];
                        for (long b = from; b < to; b++) if (Level(b) > floor) { found = b; break; }
                    }
                    if (found < 0) { next += block * 12; continue; }
                    long start = Math.Max(at, found * block);
                    if (ending ? start >= end : start + reach >= Received) break;
                    Copy(output, start);
                    Event(output, start, ending ? length : long.MaxValue);
                }
                Copy(output, ending ? length : Math.Max(at, Math.Min(Received, next / block * block)));
                int drop = (int)Math.Max(0, Math.Min(held.Count, Math.Min(Math.Min(at, next / block * block), peaked * block) - first));
                held.RemoveRange(0, drop);
                first += drop;
                int stale = (int)Math.Max(0, Math.Min(peaks.Count, next / block - peakBase));
                peaks.RemoveRange(0, stale);
                peakBase += stale;
            }

            private void Event(List<short> output, long start, long length)
            {
                double roll = random.NextDouble();
                if (roll < 0.55)
                {
                    // A stutter: a 30 to 50 ms grain, up to 90 ms as the slider goes up, plays two to four times.
                    int grain = (int)(rate * (0.03 + (0.02 + 0.04 * amount) * random.NextDouble()));
                    int repeats = 1 + (random.NextDouble() < amount ? 1 : 0) + (random.NextDouble() < amount / 2 ? 1 : 0);
                    for (int r = 0; r < repeats; r++)
                        for (int i = 0; i < grain; i++) output.Add((short)(At(start + i) * Math.Min(1, Math.Min(r == 0 ? fade : i, grain - 1 - i) / (double)fade)));
                    for (int i = 0; i < fade; i++) output.Add((short)(At(start + i) * i / (double)fade));
                    at = start + fade;
                }
                else if (roll < 0.85)
                {
                    // Crushed: 40 to 80 ms, up to 160 ms as the slider goes up, held down to fewer, coarser samples.
                    int span = (int)Math.Min(length - start, (long)(rate * (0.04 + (0.04 + 0.08 * amount) * random.NextDouble())));
                    int hold = 3 + (int)(random.NextDouble() * (3 + 9 * amount)), coarse = 1 << (8 + (int)Math.Round(4 * amount)), kept = 0;
                    for (int i = 0; i < span; i++)
                    {
                        if (i % hold == 0) kept = At(start + i) / coarse * coarse;
                        double mix = Math.Min(1, Math.Min(i, span - 1 - i) / (double)fade);
                        output.Add((short)(At(start + i) * (1 - mix) + kept * mix));
                    }
                    at = start + span;
                }
                else
                {
                    // A skip: 20 to 30 ms, up to 50 ms as the slider goes up, drops out like a scratched disc.
                    int gap = (int)(rate * (0.02 + (0.01 + 0.02 * amount) * random.NextDouble()));
                    for (int i = 0; i < fade; i++) output.Add((short)(At(start + i) * (1 - i / (double)fade) + At(start + gap + i) * i / (double)fade));
                    at = start + gap + fade;
                }
                Events++;
                next = at + (long)(rate * (0.6 + 0.8 * random.NextDouble()) / perSecond);
            }

            private double Level(long b)
            {
                double power = 0;
                for (long i = b * block; i < (b + 1) * block; i++)
                {
                    double sample = At(i);
                    power += sample * sample;
                }
                return Math.Sqrt(power / block);
            }

            private short At(long index)
            {
                long j = index - first;
                return j >= 0 && j < held.Count ? held[(int)j] : (short)0;
            }

            private void Copy(List<short> output, long upTo)
            {
                for (long i = at; i < upTo; i++) output.Add(At(i));
                at = Math.Max(at, upTo);
            }
        }

        // Her robot voice, like a talk box: a synth that follows her pitch, pulled toward the nearest note, takes on the shape
        // of her voice and plays under her. Smooth and musical: nothing is cut, repeated or crushed. Each step waits for what it
        // reads, so a line streamed in chunks comes out the same as styled whole.
        private sealed class Vocoder
        {
            private readonly int rate;
            private readonly int size;
            private readonly int hop;
            private readonly int lifter;
            private readonly int reach;
            private readonly int least;
            private readonly int most;
            private readonly int lead;
            private readonly int seed;
            private readonly double dry;
            private readonly double wet;
            private readonly double snap;
            private readonly double glideStep;
            private readonly double voiceStep;
            private readonly double[] window;
            private readonly double[] tilt;
            private readonly double[] cosines;
            private readonly double[] sines;
            private readonly double[] frame;
            private readonly double[] smooth;
            private readonly double[] difference;
            private readonly double[] voiceRe;
            private readonly double[] voiceIm;
            private readonly double[] shapeRe;
            private readonly double[] shapeIm;
            private readonly double[] envelope;
            private readonly double[] synthRe;
            private readonly double[] synthIm;
            private readonly double[] around = new double[5];
            private readonly List<short> held = new List<short>();
            private readonly List<double> pitches = new List<double>();
            private readonly List<double> clarities = new List<double>();
            private readonly List<double> targets = new List<double>();
            private readonly List<double> voicings = new List<double>();
            private readonly List<double> carrier = new List<double>();
            private readonly List<double> centres = new List<double>();
            private readonly List<double> sum = new List<double>();
            private readonly List<double> weight = new List<double>();
            private long first;
            private long analyzed;
            private long pitchBase;
            private long targeted;
            private long targetBase;
            private long made;
            private long carrierFirst;
            private long centreBase;
            private long spoken;
            private long sumFirst;
            private long emitted;
            private double loudest;
            private double note;
            private double glide;
            private double voiced;
            private double phase;
            private bool sung;

            internal Vocoder(int rate, double amount, int seed)
            {
                this.rate = rate;
                this.seed = seed;
                // About 43 ms frames, a quarter frame apart.
                size = rate >= 32000 ? 2048 : rate >= 20000 ? 1024 : 512;
                hop = size / 4;
                // Her voice's shape is smoothed over about 2 ms of cepstrum, finer than any formant and coarser than her pitch.
                lifter = Math.Max(8, (int)Math.Round(rate * 0.002));
                reach = Math.Max(1, (int)Math.Round(150.0 * size / rate));
                least = Math.Max(2, rate / 900);
                most = Math.Min(size / 2 - 1, rate / 60);
                // Mixed under her with equal power: 10% puts the robot 16 dB under her voice, 100% is all robot.
                dry = Math.Cos(amount * Math.PI / 2);
                wet = Math.Sin(amount * Math.PI / 2);
                // A light robot leans halfway to the nearest note; a strong one sings on it.
                snap = Math.Min(1, 0.5 + 0.5 * amount);
                // The synth glides over about 15 ms, so it reads her notes 15 ms early and lands on them with her.
                glideStep = 1 - Math.Exp(-1 / (0.015 * rate));
                voiceStep = 1 - Math.Exp(-1 / (0.01 * rate));
                lead = (int)Math.Round(0.015 * rate);
                window = new double[size];
                tilt = new double[size / 2 + 1];
                cosines = new double[size / 2];
                sines = new double[size / 2];
                for (int i = 0; i < size; i++) window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / size);
                // The synth is a sawtooth, so its harmonics fall off; this evens them out, then softens everything above 5 kHz.
                for (int k = 0; k <= size / 2; k++)
                {
                    double hertz = (double)k * rate / size;
                    tilt[k] = k / Math.Sqrt(1 + hertz * hertz / (5000.0 * 5000.0));
                }
                for (int k = 0; k < size / 2; k++)
                {
                    cosines[k] = Math.Cos(2 * Math.PI * k / size);
                    sines[k] = Math.Sin(2 * Math.PI * k / size);
                }
                frame = new double[size];
                smooth = new double[size];
                difference = new double[most + 2];
                voiceRe = new double[size];
                voiceIm = new double[size];
                shapeRe = new double[size];
                shapeIm = new double[size];
                envelope = new double[size / 2 + 1];
                synthRe = new double[size];
                synthIm = new double[size];
                Restart();
            }

            private long Received { get { return first + held.Count; } }

            private void Restart()
            {
                held.Clear();
                pitches.Clear();
                clarities.Clear();
                targets.Clear();
                voicings.Clear();
                carrier.Clear();
                centres.Clear();
                sum.Clear();
                weight.Clear();
                // The first frame is centred one hop before the line starts, so every sample gets all four overlapping frames.
                long start = -hop - size / 2;
                first = analyzed = pitchBase = targeted = targetBase = emitted = 0;
                made = carrierFirst = sumFirst = start;
                centreBase = start / hop;
                spoken = -1;
                loudest = glide = voiced = phase = 0;
                note = double.NaN;
                sung = false;
            }

            internal short[] Push(short[] chunk)
            {
                held.AddRange(chunk);
                List<short> output = new List<short>();
                Run(output, false);
                return output.ToArray();
            }

            internal short[] Flush()
            {
                List<short> output = new List<short>();
                if (Received > 0) Run(output, true);
                Restart();
                return output.ToArray();
            }

            private void Run(List<short> output, bool ending)
            {
                long received = Received;
                // At the end, the frames still to come read silence past the last sample.
                long lastFrame = ending ? FloorDiv(received - 1 + size / 2, hop) : long.MaxValue;
                long carrierEnd = ending ? lastFrame * hop + size / 2 : long.MaxValue;
                long targetEnd = ending ? NoteFor(carrierEnd - 1) + 1 : long.MaxValue;
                long analysisEnd = ending ? targetEnd + 2 : long.MaxValue;
                // Her pitch, once a frame's window has arrived; the note she sings, once two frames either side are in;
                // the synth, a sample at a time; then her shape on it, a frame at a time.
                while (analyzed < analysisEnd && (ending || analyzed * hop + size / 2 <= received)) Analyze(analyzed++);
                while (targeted < targetEnd && (ending || targeted + 2 < analyzed)) Target(targeted++);
                while (made < carrierEnd && (ending || NoteFor(made) < targeted)) Sing(made++);
                while (spoken <= lastFrame && (ending || (spoken * hop + size / 2 <= received && spoken * hop + size / 2 <= made))) Speak(spoken++);
                // A sample goes out once every frame over it is in: her voice, with the robot under it.
                long ready = ending ? received : Math.Min(received, spoken * hop - size / 2);
                for (long j = emitted; j < ready; j++)
                {
                    int slot = (int)(j - sumFirst);
                    double robot = slot < weight.Count && weight[slot] > 1e-9 ? sum[slot] / weight[slot] : 0;
                    output.Add(Soft(In(j) * dry + robot * wet));
                }
                emitted = Math.Max(emitted, ready);
                // Then everything no later step reads goes.
                long reading = spoken * hop - size / 2;
                sumFirst += Drop(Math.Min(emitted, reading) - sumFirst, sum, weight);
                first += Drop(Math.Min(emitted, Math.Min(reading, analyzed * hop - size / 2)) - first, held);
                carrierFirst += Drop(reading - carrierFirst, carrier, null);
                centreBase += Drop(spoken - centreBase, centres, null);
                pitchBase += Drop(targeted - 2 - pitchBase, pitches, clarities);
                targetBase += Drop(NoteFor(made) - targetBase, targets, voicings);
            }

            // The frame whose note the synth heads for at a sample.
            private long NoteFor(long sample) { return FloorDiv(sample + lead + hop / 2, hop); }

            private static int Drop(long count, List<double> list, List<double> beside)
            {
                int dropped = (int)Math.Max(0, Math.Min(list.Count, count));
                list.RemoveRange(0, dropped);
                if (beside != null) beside.RemoveRange(0, dropped);
                return dropped;
            }

            private static int Drop(long count, List<short> list)
            {
                int dropped = (int)Math.Max(0, Math.Min(list.Count, count));
                list.RemoveRange(0, dropped);
                return dropped;
            }

            private static long FloorDiv(long value, long by) { return value >= 0 ? value / by : -((-value + by - 1) / by); }

            private double In(long index)
            {
                long j = index - first;
                return index >= 0 && j >= 0 && j < held.Count ? held[(int)j] : 0;
            }

            // Her pitch in one frame, by YIN: the lag where the sound best repeats itself. 0 when it doesn't, or it's too quiet.
            private void Analyze(long u)
            {
                long start = u * hop - size / 2;
                for (int i = 0; i < size; i++) frame[i] = In(start + i);
                double energy = 0;
                for (int i = 0; i < size; i++)
                {
                    double here = frame[i];
                    smooth[i] = 0.5 * here + 0.25 * (i > 0 ? frame[i - 1] : here) + 0.25 * (i + 1 < size ? frame[i + 1] : here);
                    if (i >= size / 4 && i < size * 3 / 4) energy += here * here;
                }
                double level = Math.Sqrt(energy / (size / 2)), pitch = 0, clarity = 0;
                loudest = Math.Max(loudest, level);
                if (level > 20 && level > 0.03 * loudest)
                {
                    int span = size / 2;
                    double running = 0;
                    difference[0] = 1;
                    for (int lag = 1; lag <= most + 1; lag++)
                    {
                        // Every lag is measured around the frame's middle, so the pitch belongs to the frame's own moment.
                        int from = (size - span - lag) / 2;
                        double total = 0;
                        for (int i = from; i < from + span; i++)
                        {
                            double delta = smooth[i] - smooth[i + lag];
                            total += delta * delta;
                        }
                        running += total;
                        difference[lag] = running > 0 ? total * lag / running : 1;
                    }
                    int best = -1;
                    for (int lag = least; lag <= most && best < 0; lag++)
                        if (difference[lag] < 0.15)
                        {
                            while (lag < most && difference[lag + 1] < difference[lag]) lag++;
                            best = lag;
                        }
                    if (best < 0)
                    {
                        best = least;
                        for (int lag = least + 1; lag <= most; lag++) if (difference[lag] < difference[best]) best = lag;
                    }
                    double dip = difference[best];
                    if (dip < 0.35)
                    {
                        double before = difference[best - 1], after = difference[best + 1], bend = before - 2 * dip + after;
                        double shift = bend > 1e-12 ? 0.5 * (before - after) / bend : 0;
                        pitch = rate / (best + Math.Max(-1, Math.Min(1, shift)));
                        clarity = Math.Max(0, Math.Min(1, (0.35 - dip) / 0.2));
                    }
                }
                pitches.Add(pitch);
                clarities.Add(clarity);
            }

            private double PitchAt(long u) { return u >= pitchBase && u < analyzed ? pitches[(int)(u - pitchBase)] : 0; }

            // The note she sings in a frame: the middle of the voiced frames around it, so one that slips an octave changes
            // nothing, then pulled toward the nearest semitone, which only changes once she's well past the halfway point.
            private void Target(long u)
            {
                double pitch = PitchAt(u), target = double.NaN, voice = 0;
                if (pitch > 0)
                {
                    int count = 0;
                    for (long v = u - 2; v <= u + 2; v++)
                    {
                        double near = PitchAt(v);
                        if (near > 0) around[count++] = near;
                    }
                    if (count >= 3)
                    {
                        Array.Sort(around, 0, count);
                        pitch = around[count / 2];
                    }
                    double semitone = 69 + 12 * Math.Log(pitch / 440) / Math.Log(2);
                    if (double.IsNaN(note) || Math.Abs(semitone - note) > 0.65) note = Math.Round(semitone);
                    target = semitone + snap * (note - semitone);
                    voice = clarities[(int)(u - pitchBase)];
                }
                targets.Add(target);
                voicings.Add(voice);
            }

            // One sample of the synth: a band-limited sawtooth that glides to each new note over about 15 ms.
            private void Sing(long j)
            {
                long u = NoteFor(j);
                bool known = u >= targetBase && u < targeted;
                double target = known ? targets[(int)(u - targetBase)] : double.NaN, voice = known ? voicings[(int)(u - targetBase)] : 0;
                if (!double.IsNaN(target))
                {
                    if (!sung) { glide = target; sung = true; }
                    glide += (target - glide) * glideStep;
                }
                voiced += (voice - voiced) * voiceStep;
                double step = Math.Min(0.45, (sung ? 440 * Math.Pow(2, (glide - 69) / 12) : 220) / rate);
                phase += step;
                if (phase >= 1) phase -= 1;
                double edge = 0;
                if (phase < step) { double t = phase / step; edge = t + t - t * t - 1; }
                else if (phase > 1 - step) { double t = (phase - 1) / step; edge = t * t + t + t + 1; }
                carrier.Add(2 * phase - 1 - edge);
                if (j % hop == 0) centres.Add(voiced);
            }

            // One frame of the robot: the synth takes her voice's smoothed spectrum and loudness. Where she isn't voiced,
            // noise with her shape stands in, so her s and t sounds come through too.
            private void Speak(long t)
            {
                long start = t * hop - size / 2;
                int half = size / 2;
                for (int i = 0; i < size; i++)
                {
                    voiceRe[i] = In(start + i) * window[i];
                    voiceIm[i] = 0;
                }
                Transform(voiceRe, voiceIm, false);
                double energy = 0;
                for (int k = 0; k <= half; k++)
                {
                    envelope[k] = voiceRe[k] * voiceRe[k] + voiceIm[k] * voiceIm[k];
                    energy += k == 0 || k == half ? envelope[k] : 2 * envelope[k];
                }
                // Her power averaged over about 300 Hz around each point, so the shape rides on her harmonics rather than
                // sinking into the gaps between them, which would leave the robot thin and bright.
                for (int k = 0; k <= half; k++)
                {
                    int low = Math.Max(0, k - reach), high = Math.Min(half, k + reach);
                    double total = 0;
                    for (int b = low; b <= high; b++) total += envelope[b];
                    shapeRe[k] = 0.5 * Math.Log(total / (high - low + 1) + 1e-2);
                    shapeIm[k] = 0;
                }
                for (int k = half + 1; k < size; k++)
                {
                    shapeRe[k] = shapeRe[size - k];
                    shapeIm[k] = 0;
                }
                Transform(shapeRe, shapeIm, true);
                for (int q = 0; q < size; q++)
                {
                    int from = Math.Min(q, size - q);
                    shapeRe[q] *= from <= lifter ? 0.5 + 0.5 * Math.Cos(Math.PI * from / (lifter + 1)) : 0;
                    shapeIm[q] = 0;
                }
                Transform(shapeRe, shapeIm, false);
                long offset = start - carrierFirst;
                for (int i = 0; i < size; i++)
                {
                    synthRe[i] = carrier[(int)(offset + i)] * window[i];
                    synthIm[i] = 0;
                }
                Transform(synthRe, synthIm, false);
                double sawEnergy = 0, noiseEnergy = 0;
                for (int k = 1; k < half; k++)
                {
                    envelope[k] = Math.Exp(shapeRe[k]);
                    double gain = envelope[k] * tilt[k];
                    synthRe[k] *= gain;
                    synthIm[k] *= gain;
                    sawEnergy += 2 * (synthRe[k] * synthRe[k] + synthIm[k] * synthIm[k]);
                    noiseEnergy += 2 * envelope[k] * envelope[k];
                }
                double voice = centres[(int)(t - centreBase)];
                double sawGain = sawEnergy > 1e-9 ? Math.Sin(voice * Math.PI / 2) * Math.Sqrt(energy / sawEnergy) : 0;
                double noiseGain = noiseEnergy > 1e-9 ? Math.Cos(voice * Math.PI / 2) * Math.Sqrt(energy / noiseEnergy) : 0;
                Random noise = new Random(unchecked(seed + (int)t * 7919));
                voiceRe[0] = voiceIm[0] = voiceRe[half] = voiceIm[half] = 0;
                for (int k = 1; k < half; k++)
                {
                    double angle = 2 * Math.PI * noise.NextDouble(), hiss = envelope[k] * noiseGain;
                    voiceRe[k] = sawGain * synthRe[k] + hiss * Math.Cos(angle);
                    voiceIm[k] = sawGain * synthIm[k] + hiss * Math.Sin(angle);
                    voiceRe[size - k] = voiceRe[k];
                    voiceIm[size - k] = -voiceIm[k];
                }
                Transform(voiceRe, voiceIm, true);
                long slot = start - sumFirst;
                while (sum.Count < slot + size)
                {
                    sum.Add(0);
                    weight.Add(0);
                }
                for (int i = 0; i < size; i++)
                {
                    sum[(int)(slot + i)] += voiceRe[i] * window[i];
                    weight[(int)(slot + i)] += window[i] * window[i];
                }
            }

            // An FFT in place; the inverse divides by the size.
            private void Transform(double[] real, double[] imaginary, bool inverse)
            {
                int n = size;
                for (int i = 1, j = 0; i < n; i++)
                {
                    int bit = n >> 1;
                    for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                    j ^= bit;
                    if (i < j)
                    {
                        double swap = real[i];
                        real[i] = real[j];
                        real[j] = swap;
                        swap = imaginary[i];
                        imaginary[i] = imaginary[j];
                        imaginary[j] = swap;
                    }
                }
                for (int length = 2; length <= n; length <<= 1)
                {
                    int half = length >> 1, stride = n / length;
                    for (int i = 0; i < n; i += length)
                        for (int k = 0; k < half; k++)
                        {
                            double c = cosines[k * stride], s = inverse ? sines[k * stride] : -sines[k * stride];
                            int a = i + k, b = a + half;
                            double tr = real[b] * c - imaginary[b] * s, ti = real[b] * s + imaginary[b] * c;
                            real[b] = real[a] - tr;
                            imaginary[b] = imaginary[a] - ti;
                            real[a] += tr;
                            imaginary[a] += ti;
                        }
                }
                if (inverse)
                    for (int i = 0; i < n; i++)
                    {
                        real[i] /= n;
                        imaginary[i] /= n;
                    }
            }
        }
    }

#if !CROSS
    // Their own voice for her HD voice: a clip of them talking becomes the reference it clones, reference.wav in its folder.
    // Kokoro's styles and ElevenLabs can't use it: Kokoro's voices are fixed, and ElevenLabs clones voices on its own site.
    internal static class YourVoice
    {
        internal const int Rate = 24000;
        internal const int Shortest = 5;
        internal const int Longest = 30;
        internal const string Using = "reference.wav";
        internal const string Kept = "your-voice.wav";
        internal const string Silent = "That clip is almost silent. Record yourself talking, closer to the mic, then pick it again.";
        internal const string Unreadable = "Windows couldn't read that clip. Pick a WAV, MP3 or M4A of you talking.";

        internal static bool Active(string folder) { return File.Exists(Path.Combine(folder, Using)); }

        internal static bool Saved(string folder) { return File.Exists(Path.Combine(folder, Kept)); }

        // The clip as 24 kHz mono 16-bit, its quiet ends cut and its level evened out, or null with what's wrong in plain words.
        internal static short[] Prepare(string path, out string problem)
        {
            problem = null;
            try { return Shape(Read(path), out problem); }
            catch (VoiceException error) { problem = error.Message; return null; }
        }

        internal static short[] Read(string path)
        {
            FileInfo file = new FileInfo(path);
            if (!file.Exists) throw new VoiceException("That clip isn't there anymore. Pick it again.");
            bool wave = file.Extension.Equals(".wav", StringComparison.OrdinalIgnoreCase);
            if (file.Length > (wave ? 64L << 20 : 16L << 20)) throw new VoiceException("That clip is too long. Pick " + Shortest + " to " + Longest + " seconds of you talking.");
            int rate = 0;
            float[] mono = null;
            try { if (wave) mono = Wav(File.ReadAllBytes(path), out rate); }
            catch (IOException) { throw new VoiceException("Couldn't open that clip. Close the app using it, then pick it again."); }
            catch (UnauthorizedAccessException) { throw new VoiceException("Windows won't let Capri open that clip. Copy it to your Documents and pick it there."); }
            // MP3, M4A and WAVs in other codings go through Windows' own decoders (Media Foundation), straight to 24 kHz mono.
            if (mono == null) mono = Wav(Decode(file.FullName), out rate);
            if (mono == null) throw new VoiceException(Unreadable);
            short[] pcm = new short[mono.Length];
            for (int i = 0; i < mono.Length; i++) pcm[i] = (short)Math.Max(-32768, Math.Min(32767, Math.Round(mono[i] * 32767)));
            return rate == Rate ? pcm : VoiceFx.Resample(pcm, rate / (double)Rate);
        }

        // A WAV's sound mixed down to mono: 8, 16, 24 or 32-bit PCM, or 32-bit float. Null for any other coding.
        internal static float[] Wav(byte[] wav, out int rate)
        {
            rate = 0;
            if (wav.Length < 12 || Encoding.ASCII.GetString(wav, 0, 4) != "RIFF" || Encoding.ASCII.GetString(wav, 8, 4) != "WAVE") return null;
            int format = 0, channels = 0, bits = 0, at = 12;
            while (at + 8 <= wav.Length)
            {
                string chunk = Encoding.ASCII.GetString(wav, at, 4);
                int size = BitConverter.ToInt32(wav, at + 4);
                if (chunk == "fmt " && size >= 16 && at + 24 <= wav.Length)
                {
                    format = BitConverter.ToUInt16(wav, at + 8);
                    channels = BitConverter.ToUInt16(wav, at + 10);
                    rate = BitConverter.ToInt32(wav, at + 12);
                    bits = BitConverter.ToUInt16(wav, at + 22);
                    // WAVE_FORMAT_EXTENSIBLE keeps the real coding at the start of its sub-format GUID.
                    if (format == 0xFFFE && size >= 40 && at + 34 <= wav.Length) format = BitConverter.ToUInt16(wav, at + 32);
                }
                if (chunk == "data")
                {
                    if ((format != 1 && format != 3) || channels < 1 || rate < 4000 || (format == 1 && bits != 8 && bits != 16 && bits != 24 && bits != 32) || (format == 3 && bits != 32)) return null;
                    int available = wav.Length - at - 8, length = size > 0 && size <= available ? size : available, width = bits / 8;
                    int frames = length / (width * channels);
                    float[] mono = new float[frames];
                    for (int f = 0; f < frames; f++)
                    {
                        double total = 0;
                        for (int c = 0; c < channels; c++)
                        {
                            int o = at + 8 + (f * channels + c) * width;
                            total += format == 3 ? BitConverter.ToSingle(wav, o) : bits == 8 ? (wav[o] - 128) / 128.0 : bits == 16 ? BitConverter.ToInt16(wav, o) / 32768.0 : bits == 24 ? (wav[o] | wav[o + 1] << 8 | (sbyte)wav[o + 2] << 16) / 8388608.0 : BitConverter.ToInt32(wav, o) / 2147483648.0;
                        }
                        mono[f] = (float)Math.Max(-1, Math.Min(1, total / channels));
                    }
                    return mono;
                }
                long following = (long)at + 8 + size + (size & 1);
                if (size < 0 || following > wav.Length) break;
                at = (int)following;
            }
            return null;
        }

        // Streams rather than files, so their clip isn't left locked once it's read.
        private static byte[] Decode(string path)
        {
            byte[] wav;
            try { using (FileStream input = File.OpenRead(path)) wav = Decode(input); }
            catch (Exception) { throw new VoiceException(Unreadable); }
            if (wav == null) throw new VoiceException(Unreadable);
            return wav;
        }

        // Any sound Windows' own decoders (Media Foundation) can read, like MP3 or M4A, as a 24 kHz mono 16-bit WAV. Null if they can't.
        internal static byte[] Decode(Stream input)
        {
            using (MemoryStream output = new MemoryStream())
            {
                Windows.Media.MediaProperties.MediaEncodingProfile profile = Windows.Media.MediaProperties.MediaEncodingProfile.CreateWav(Windows.Media.MediaProperties.AudioEncodingQuality.High);
                profile.Audio = Windows.Media.MediaProperties.AudioEncodingProperties.CreatePcm(Rate, 1, 16);
                Windows.Media.Transcoding.PrepareTranscodeResult prepared = new Windows.Media.Transcoding.MediaTranscoder().PrepareStreamTranscodeAsync(input.AsRandomAccessStream(), output.AsRandomAccessStream(), profile).AsTask().Result;
                if (!prepared.CanTranscode) return null;
                prepared.TranscodeAsync().AsTask().Wait();
                return output.ToArray();
            }
        }

        // Too quiet, too short or too long says why. Otherwise the clip comes back with its quiet ends cut and its loudest peak near full scale.
        internal static short[] Shape(short[] clip, out string problem)
        {
            problem = null;
            int block = Rate / 100, blocks = clip.Length / block;
            double[] level = new double[blocks];
            double loudest = 0;
            for (int b = 0; b < blocks; b++)
            {
                double power = 0;
                for (int i = b * block; i < (b + 1) * block; i++) power += (double)clip[i] * clip[i];
                level[b] = Math.Sqrt(power / block);
                loudest = Math.Max(loudest, level[b]);
            }
            if (loudest < 400) { problem = Silent; return null; }
            double floor = Math.Max(120, loudest * 0.06);
            int firstLoud = 0, lastLoud = blocks - 1;
            while (level[firstLoud] < floor) firstLoud++;
            while (level[lastLoud] < floor) lastLoud--;
            int from = Math.Max(0, (firstLoud - 15) * block), to = Math.Min(clip.Length, (lastLoud + 16) * block);
            double seconds = (to - from) / (double)Rate;
            string said = seconds.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " seconds";
            if (seconds < Shortest) { problem = "That clip has only " + said + " of talking. Pick " + Shortest + " to " + Longest + " seconds of you talking."; return null; }
            if (seconds > Longest) { problem = "That clip is " + said + " long. Pick " + Shortest + " to " + Longest + " seconds of you talking, or trim it."; return null; }
            int peak = 1;
            for (int i = from; i < to; i++) peak = Math.Max(peak, Math.Abs((int)clip[i]));
            double gain = Math.Min(8, 29000.0 / peak);
            short[] shaped = new short[to - from];
            for (int i = 0; i < shaped.Length; i++) shaped[i] = (short)Math.Max(-32767, Math.Min(32767, Math.Round(clip[from + i] * gain)));
            return shaped;
        }

        // A new clip replaces any kept one; her HD voice reads reference.wav on its next line.
        internal static void Save(short[] clip, string folder)
        {
            Directory.CreateDirectory(folder);
            string target = Path.Combine(folder, Using), staging = target + ".new";
            File.WriteAllBytes(staging, Voice.Wave(clip, Rate));
            if (File.Exists(target)) File.Replace(staging, target, null);
            else File.Move(staging, target);
            string kept = Path.Combine(folder, Kept);
            if (File.Exists(kept)) File.Delete(kept);
        }

        // Back to her own clip, with theirs kept beside it so it can come back without picking it again.
        internal static void Shelve(string folder) { Move(Path.Combine(folder, Using), Path.Combine(folder, Kept)); }

        internal static void Unshelve(string folder) { Move(Path.Combine(folder, Kept), Path.Combine(folder, Using)); }

        private static void Move(string from, string to)
        {
            if (!File.Exists(from)) return;
            if (File.Exists(to)) File.Delete(to);
            File.Move(from, to);
        }
    }

    internal sealed class FreeStyle
    {
        internal readonly string Name;
        internal readonly int Speaker;
        internal readonly float Speed;
        internal readonly bool British;

        internal FreeStyle(string name, int speaker, float speed, bool british)
        {
            Name = name;
            Speaker = speaker;
            Speed = speed;
            British = british;
        }
    }

    internal sealed class LocalVoice : IDisposable
    {
        internal const string Pack = VoicePack.Name;
        internal static string PackUrl = VoicePack.Url;
        internal static string PackSha = VoicePack.Sha;
        internal static long PackSize = VoicePack.Size;
        internal static readonly FreeStyle[] Styles =
        {
            new FreeStyle("Her own", 53, 1.1f, false),
            new FreeStyle("Bright", 2, 1.1f, false),
            new FreeStyle("Sly", 6, 1.08f, false),
            new FreeStyle("Sharp", 21, 1.1f, true)
        };
        internal static readonly string[] Needed = VoicePack.Needed;
        private static readonly object loading = new object();
        private static bool loaded;
        private readonly string folder;
        private readonly object gate = new object();
        private IntPtr tts;
        private bool british;
        internal volatile FreeStyle Style = Styles[0];
        internal volatile bool Cancel;

        internal LocalVoice(string data) { folder = Folder(data); }

        internal static string Folder(string data) { return VoicePack.Folder(data); }

        internal static FreeStyle Find(string name)
        {
            foreach (FreeStyle style in Styles) if (style.Name == name) return style;
            return Styles[0];
        }

        internal bool Installed { get { return Complete(folder); } }

        private static bool Complete(string where) { return VoicePack.Complete(where); }

        internal void Install(Action<string, long, long> progress)
        {
            if (IntPtr.Size != 8) throw new IOException("Her free voice needs 64-bit Windows.");
            string root = Path.GetDirectoryName(folder);
            Directory.CreateDirectory(root);
            long needed = PackSize * 3 + (64L << 20);
            string drive = Path.GetPathRoot(root);
            if (new DriveInfo(drive).AvailableFreeSpace < needed) throw new IOException("Her free voice needs " + Engine.Amount(needed) + " free on " + drive.TrimEnd('\\') + ".");
            string zip = folder + ".zip";
            Engine.Fetch(PackUrl, zip, PackSize, PackSha, "her free voice", progress, delegate { return Cancel; });
            progress("Unpacking her free voice", PackSize, PackSize);
            string staging = folder + ".new";
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            ZipFile.ExtractToDirectory(zip, staging);
            if (!Complete(staging)) throw new IOException("The voice download is missing files.");
            lock (gate)
            {
                Close();
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
                Directory.Move(staging, folder);
            }
            File.Delete(zip);
        }

        internal void Warm()
        {
            FreeStyle style = Style;
            lock (gate) Open(style.British);
        }

        internal byte[] Speak(string text)
        {
            FreeStyle style = Style;
            int pitch = VoiceFx.Pitch, speed = VoiceFx.Speed, robot = VoiceFx.Robot, glitch = VoiceFx.Glitch, rate;
            VoiceFx.Layering layers = VoiceFx.Layers;
            short[] pcm;
            lock (gate)
            {
                Open(style.British);
                IntPtr words = Utf8(text);
                IntPtr audio = IntPtr.Zero;
                try
                {
                    audio = SherpaOnnxOfflineTtsGenerate(tts, words, style.Speaker, VoiceFx.Tempo(style.Speed, pitch, speed));
                    if (audio == IntPtr.Zero) throw new VoiceException("Her free voice glitched.");
                    Generated made = (Generated)Marshal.PtrToStructure(audio, typeof(Generated));
                    float[] samples = new float[Math.Max(0, made.Count)];
                    if (samples.Length > 0) Marshal.Copy(made.Samples, samples, 0, samples.Length);
                    pcm = new short[samples.Length];
                    for (int i = 0; i < samples.Length; i++) pcm[i] = (short)Math.Max(-32767, Math.Min(32767, Math.Round(samples[i] * 32767)));
                    rate = made.Rate;
                }
                finally
                {
                    if (audio != IntPtr.Zero) SherpaOnnxDestroyOfflineTtsGeneratedAudio(audio);
                    Marshal.FreeHGlobal(words);
                }
            }
            return Voice.Wave(VoiceFx.Kokoro(pcm, rate, pitch, robot, glitch, VoiceFx.Seed(text), layers), rate);
        }

        private void Open(bool gb)
        {
            if (tts != IntPtr.Zero && british == gb) return;
            if (IntPtr.Size != 8) throw new VoiceException("Her free voice needs 64-bit Windows.");
            if (!Installed) throw new VoiceException("Set up her free voice in Capri Mainframe.");
            Load(folder);
            Close();
            List<IntPtr> held = new List<IntPtr>();
            Func<string, IntPtr> keep = delegate(string value) { IntPtr pointer = Utf8(value); held.Add(pointer); return pointer; };
            try
            {
                Config config = new Config();
                config.Model.Threads = Math.Max(1, Math.Min(6, Environment.ProcessorCount / 2));
                config.Model.Provider = keep("cpu");
                config.Model.Kokoro.Model = keep(Path.Combine(folder, "model.onnx"));
                config.Model.Kokoro.Voices = keep(Path.Combine(folder, "voices.bin"));
                config.Model.Kokoro.Tokens = keep(Path.Combine(folder, "tokens.txt"));
                config.Model.Kokoro.Data = keep(Path.Combine(folder, "espeak-ng-data"));
                config.Model.Kokoro.Lexicon = keep(Path.Combine(folder, gb ? "lexicon-gb-en.txt" : "lexicon-us-en.txt"));
                config.Model.Kokoro.Lang = keep(gb ? "en" : "en-us");
                config.MaxSentences = 1;
                tts = SherpaOnnxCreateOfflineTts(ref config);
            }
            finally { foreach (IntPtr pointer in held) Marshal.FreeHGlobal(pointer); }
            if (tts == IntPtr.Zero) throw new VoiceException("Her free voice wouldn't start.");
            british = gb;
        }

        // Her ears run on the same sherpa-onnx, so they load it from here too.
        internal static void Load(string where)
        {
            lock (loading)
            {
                if (loaded) return;
                foreach (string name in new[] { "onnxruntime.dll", "sherpa-onnx-c-api.dll" })
                    if (LoadLibraryEx(Path.Combine(where, name), IntPtr.Zero, 8) == IntPtr.Zero) throw new VoiceException("Her free voice wouldn't load (error " + Marshal.GetLastWin32Error() + ").");
                loaded = true;
            }
        }

        private void Close()
        {
            if (tts == IntPtr.Zero) return;
            SherpaOnnxDestroyOfflineTts(tts);
            tts = IntPtr.Zero;
        }

        public void Dispose() { lock (gate) Close(); }

        // Her voice model files (ModelFile) run on the same sherpa-onnx, so they share its calls and config.
        internal static IntPtr Utf8(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? "");
            IntPtr pointer = Marshal.AllocHGlobal(bytes.Length + 1);
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
            Marshal.WriteByte(pointer, bytes.Length, 0);
            return pointer;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibraryEx(string path, IntPtr file, int flags);
        [DllImport("sherpa-onnx-c-api.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr SherpaOnnxCreateOfflineTts(ref Config config);
        [DllImport("sherpa-onnx-c-api.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern void SherpaOnnxDestroyOfflineTts(IntPtr tts);
        [DllImport("sherpa-onnx-c-api.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr SherpaOnnxOfflineTtsGenerate(IntPtr tts, IntPtr text, int speaker, float speed);
        [DllImport("sherpa-onnx-c-api.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern void SherpaOnnxDestroyOfflineTtsGeneratedAudio(IntPtr audio);
        [DllImport("sherpa-onnx-c-api.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern int SherpaOnnxOfflineTtsNumSpeakers(IntPtr tts);
        [DllImport("sherpa-onnx-c-api.dll", CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr SherpaOnnxOfflineTtsGenerateWithConfig(IntPtr tts, IntPtr text, ref Asking config, IntPtr callback, IntPtr arg);

        [StructLayout(LayoutKind.Sequential)]
        internal struct Vits { internal IntPtr Model, Lexicon, Tokens, Data; internal float Noise, NoiseW, Length; internal IntPtr Dict; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Matcha { internal IntPtr Acoustic, Vocoder, Lexicon, Tokens, Data; internal float Noise, Length; internal IntPtr Dict; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Kokoro { internal IntPtr Model, Voices, Tokens, Data; internal float Length; internal IntPtr Dict, Lexicon, Lang; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Kitten { internal IntPtr Model, Voices, Tokens, Data; internal float Length; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Zipvoice { internal IntPtr Tokens, Encoder, Decoder, Vocoder, Data, Lexicon; internal float Feat, Shift, Rms, Guidance; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Pocket { internal IntPtr Flow, Main, Encoder, Decoder, Conditioner, Vocab, Scores; internal int Cache; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Supertonic { internal IntPtr Duration, Encoder, Estimator, Vocoder, Json, Indexer, Style; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ModelConfig
        {
            internal Vits Vits;
            internal int Threads, Debug;
            internal IntPtr Provider;
            internal Matcha Matcha;
            internal Kokoro Kokoro;
            internal Kitten Kitten;
            internal Zipvoice Zipvoice;
            internal Pocket Pocket;
            internal Supertonic Supertonic;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Config
        {
            internal ModelConfig Model;
            internal IntPtr Rules;
            internal int MaxSentences;
            internal IntPtr Fars;
            internal float Silence;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Generated { internal IntPtr Samples; internal int Count, Rate; }

        // How to say a line, for models that copy a voice: Pocket TTS takes the clip whose voice it copies.
        [StructLayout(LayoutKind.Sequential)]
        internal struct Asking { internal float Silence, Speed; internal int Speaker; internal IntPtr Clip; internal int ClipLength, ClipRate; internal IntPtr ClipText; internal int Steps; internal IntPtr Extra; }
    }

    // A voice model file they downloaded, run on her free voice's sherpa-onnx with no server: Piper and other VITS voices,
    // Kokoro, Kitten and Pocket TTS, as sherpa-onnx's tts-models releases pack them. One stays open at a time.
    internal static class ModelFile
    {
        internal const string Missing = "That's no voice model Capri can run. Pick a Piper, Kokoro, Kitten or Pocket TTS model for sherpa-onnx: its .tar.bz2, or the .onnx in its folder.";
        internal const string Unopened = "Capri can't open that clip. Press the arrow in the Speaker or clip box to pick it.";
        // Her free voice's folder, where sherpa-onnx is, and the folder models from an archive are unpacked into. Set at startup.
        internal static string Engine;
        internal static string Shelf;
        private static readonly object gate = new object();
        private static IntPtr tts;
        private static string opened;
        private static string openKind;
        // The clip Pocket TTS last copied, kept read until it changes.
        private static string clipFrom;
        private static float[] clipSound;
        private static int clipRate;

        // What a folder holds: "kokoro", "kitten", "pocket" or "vits", or null when it's no model she can run.
        internal static string Kind(string folder)
        {
            string model = Model(folder);
            if (model == null) return null;
            // Pocket TTS keeps its words in vocab.json rather than tokens.txt.
            if (Part(folder, "lm_main") != null && Part(folder, "lm_flow") != null && File.Exists(Path.Combine(folder, "vocab.json"))) return "pocket";
            if (!File.Exists(Path.Combine(folder, "tokens.txt"))) return null;
            if (!File.Exists(Path.Combine(folder, "voices.bin"))) return "vits";
            string named = Path.GetFileName(folder.TrimEnd('\\', '/')) + " " + Path.GetFileName(model);
            return named.IndexOf("kitten", StringComparison.OrdinalIgnoreCase) >= 0 ? "kitten" : "kokoro";
        }

        // Its model: the biggest .onnx in the folder.
        private static string Model(string folder)
        {
            string best = null;
            long most = -1;
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return null;
                foreach (FileInfo file in new DirectoryInfo(folder).GetFiles())
                {
                    if (!file.Extension.Equals(".onnx", StringComparison.OrdinalIgnoreCase) || file.Length <= most) continue;
                    best = file.FullName;
                    most = file.Length;
                }
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            return best;
        }

        // One of Pocket TTS's models, its int8 one where there are both: it's quicker on a CPU.
        private static string Part(string folder, string name)
        {
            foreach (string file in new[] { name + ".int8.onnx", name + ".onnx" })
                if (File.Exists(Path.Combine(folder, file))) return Path.Combine(folder, file);
            return null;
        }

        // A folder as they typed or pasted it. Explorer's "Copy as path" puts it in quotes.
        internal static string Clean(string folder) { return (folder ?? "").Trim().Trim('"').Trim(); }

        // Its name on her free voice button: its folder's, without sherpa-onnx's "vits-piper-" or "sherpa-onnx-" in front.
        // Anything typed gets a name, even what's no path at all, as it shows when she starts.
        internal static string Name(string folder)
        {
            string name;
            try { name = Path.GetFileName(Clean(folder).TrimEnd('\\', '/')); }
            catch (ArgumentException) { name = ""; }
            name = Regex.Replace(name, "^(vits-(piper-)?|sherpa-onnx-)", "", RegexOptions.IgnoreCase);
            return name.Length == 0 ? "Voice model" : name;
        }

        // A .tar.bz2 or .zip they picked, unpacked into her models folder. Returns the model's folder.
        internal static string Unpack(string archive)
        {
            // Its folder is named after the archive, and only ever sits inside her models folder: an archive named ".zip" or
            // "...zip" must never point it at the models folder itself or the one above.
            string name = Regex.Replace(Path.GetFileName(archive), @"\.(tar\.bz2|tar\.gz|tgz|tar|zip)$", "", RegexOptions.IgnoreCase).Trim().TrimEnd('.', ' ');
            if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) name = "voice-model";
            string target = Path.Combine(Shelf, name);
            string staging = target + ".new";
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);
            try
            {
                if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) ZipFile.ExtractToDirectory(archive, staging);
                else
                {
                    // Windows' own tar unpacks .tar.bz2, as sherpa-onnx ships its models.
                    ProcessStartInfo start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe"), "-xf \"" + archive + "\" -C \"" + staging + "\"");
                    start.UseShellExecute = false;
                    start.CreateNoWindow = true;
                    using (Process unpacking = Process.Start(start))
                    {
                        if (!unpacking.WaitForExit(600000))
                        {
                            try { unpacking.Kill(); unpacking.WaitForExit(5000); }
                            catch (Exception) { }
                            throw new VoiceException("Windows took too long to unpack " + Path.GetFileName(archive) + ".");
                        }
                        if (unpacking.ExitCode != 0) throw new VoiceException("Windows couldn't unpack " + Path.GetFileName(archive) + ".");
                    }
                }
            }
            catch (Exception)
            {
                // A failed unpack leaves nothing half-made behind.
                try { Directory.Delete(staging, true); }
                catch (Exception) { }
                throw;
            }
            // Its archive holds one folder with the model in it.
            string inside = staging;
            string[] folders = Directory.GetDirectories(staging);
            if (Kind(staging) == null && folders.Length == 1 && Directory.GetFiles(staging).Length == 0) inside = folders[0];
            if (Kind(inside) == null)
            {
                Directory.Delete(staging, true);
                throw new VoiceException(Missing);
            }
            lock (gate)
            {
                if (opened != null && opened.StartsWith(target + "|", StringComparison.OrdinalIgnoreCase)) Close();
                if (Directory.Exists(target)) Directory.Delete(target, true);
                Directory.Move(inside, target);
            }
            if (inside != staging) Directory.Delete(staging, true);
            return target;
        }

        // Its sound for the words at its own rate. `lang` is a Kokoro model's language, like en-us or es. `voice` is a speaker
        // number, where one past its last speaker gets its first, or for Pocket TTS the clip whose voice it copies.
        internal static short[] Speak(string folder, string lang, string voice, string text, out int rate)
        {
            lock (gate)
            {
                string kind = Open(folder, lang);
                IntPtr words = LocalVoice.Utf8(text);
                IntPtr audio = IntPtr.Zero;
                try
                {
                    // A model that breaks inside sherpa-onnx comes back as an SEHException; it's said like any other glitch, so
                    // her free voice fills in.
                    try
                    {
                        if (kind == "pocket") audio = Copy(words, voice);
                        else
                        {
                            int speaker;
                            if (!int.TryParse((voice ?? "").Trim(), out speaker) || speaker < 0 || speaker >= LocalVoice.SherpaOnnxOfflineTtsNumSpeakers(tts)) speaker = 0;
                            audio = LocalVoice.SherpaOnnxOfflineTtsGenerate(tts, words, speaker, 1f);
                        }
                    }
                    catch (SEHException) { throw new VoiceException("Your voice model glitched."); }
                    if (audio == IntPtr.Zero) throw new VoiceException("Your voice model glitched.");
                    LocalVoice.Generated made = (LocalVoice.Generated)Marshal.PtrToStructure(audio, typeof(LocalVoice.Generated));
                    float[] samples = new float[Math.Max(0, made.Count)];
                    if (samples.Length > 0) Marshal.Copy(made.Samples, samples, 0, samples.Length);
                    short[] pcm = new short[samples.Length];
                    for (int i = 0; i < samples.Length; i++) pcm[i] = (short)Math.Max(-32767, Math.Min(32767, Math.Round(samples[i] * 32767)));
                    rate = made.Rate;
                    return pcm;
                }
                finally
                {
                    if (audio != IntPtr.Zero) LocalVoice.SherpaOnnxDestroyOfflineTtsGeneratedAudio(audio);
                    Marshal.FreeHGlobal(words);
                }
            }
        }

        // Pocket TTS says the words in the voice of their clip, or of her HD voice's own clip when there's none.
        private static IntPtr Copy(IntPtr words, string voice)
        {
            string path = (voice ?? "").Trim().Trim('"');
            int number;
            if (int.TryParse(path, out number)) path = "";
            string from = "";
            if (path.Length > 0)
            {
                // The box takes typing, so it can hold something that's no path at all.
                try { from = path + "|" + File.GetLastWriteTimeUtc(path).Ticks; }
                catch (ArgumentException) { throw new VoiceException(Unopened); }
                catch (NotSupportedException) { throw new VoiceException(Unopened); }
                catch (PathTooLongException) { throw new VoiceException(Unopened); }
            }
            if (clipSound == null || clipFrom != from)
            {
                if (path.Length == 0)
                {
                    using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("Capri.Voice.reference.wav"))
                    using (MemoryStream copy = new MemoryStream())
                    {
                        if (source == null) throw new VoiceException("Capri is missing her HD voice's clip. Pick a clip under Voice.");
                        source.CopyTo(copy);
                        clipSound = YourVoice.Wav(copy.ToArray(), out clipRate);
                    }
                }
                else
                {
                    short[] pcm = YourVoice.Read(path);
                    clipSound = new float[pcm.Length];
                    for (int i = 0; i < pcm.Length; i++) clipSound[i] = pcm[i] / 32768f;
                    clipRate = YourVoice.Rate;
                }
                clipFrom = from;
            }
            GCHandle pinned = GCHandle.Alloc(clipSound, GCHandleType.Pinned);
            // Its first 10 seconds are enough, and the same seed says a line the same way each time.
            IntPtr extra = LocalVoice.Utf8("{\"max_reference_audio_len\": 10.0, \"seed\": 42}");
            try
            {
                LocalVoice.Asking asking = new LocalVoice.Asking();
                asking.Silence = 0.2f;
                asking.Speed = 1f;
                asking.Clip = pinned.AddrOfPinnedObject();
                asking.ClipLength = clipSound.Length;
                asking.ClipRate = clipRate;
                asking.Extra = extra;
                return LocalVoice.SherpaOnnxOfflineTtsGenerateWithConfig(tts, words, ref asking, IntPtr.Zero, IntPtr.Zero);
            }
            finally
            {
                pinned.Free();
                Marshal.FreeHGlobal(extra);
            }
        }

        // sherpa-onnx starts espeak once for the whole app, from the first espeak-ng-data it's given, and that's usually her
        // free voice's. So every model reads her free voice's copy, with the model's own language files added to it: a
        // Spanish Piper voice brings es_dict, and deleting a model never takes her free voice's files with it.
        private static string Speech(string folder)
        {
            string theirs = Path.Combine(folder, "espeak-ng-data"), ours = Path.Combine(Engine, "espeak-ng-data");
            if (!Directory.Exists(theirs)) return "";
            if (!Directory.Exists(ours)) return theirs;
            try
            {
                foreach (string file in Directory.GetFiles(theirs, "*", SearchOption.AllDirectories))
                {
                    string target = ours + file.Substring(theirs.Length);
                    if (File.Exists(target)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(file, target);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return ours;
        }

        // Opens the model, unless it's open already, and says what kind it is.
        private static string Open(string folder, string lang)
        {
            lang = string.IsNullOrEmpty(lang) ? "en-us" : lang.Trim().ToLowerInvariant();
            string key = folder + "|" + lang;
            if (tts != IntPtr.Zero && opened == key) return openKind;
            if (IntPtr.Size != 8) throw new VoiceException("Voice model files need 64-bit Windows.");
            if (Engine == null || !File.Exists(Path.Combine(Engine, "sherpa-onnx-c-api.dll"))) throw new VoiceException("Voice model files run on her free voice. Get free voice under VOICE first.");
            string kind = Kind(folder);
            if (kind == null) throw new VoiceException(Missing);
            LocalVoice.Load(Engine);
            Close();
            List<IntPtr> held = new List<IntPtr>();
            Func<string, IntPtr> keep = delegate(string value) { IntPtr pointer = LocalVoice.Utf8(value); held.Add(pointer); return pointer; };
            Func<string, string> inside = delegate(string name) { string at = Path.Combine(folder, name); return File.Exists(at) || Directory.Exists(at) ? at : ""; };
            try
            {
                LocalVoice.Config config = new LocalVoice.Config();
                config.Model.Threads = Math.Max(1, Math.Min(6, Environment.ProcessorCount / 2));
                config.Model.Provider = keep("cpu");
                if (kind == "vits")
                {
                    config.Model.Vits.Model = keep(Model(folder));
                    config.Model.Vits.Tokens = keep(inside("tokens.txt"));
                    config.Model.Vits.Data = keep(Speech(folder));
                    config.Model.Vits.Lexicon = keep(inside("lexicon.txt"));
                    config.Model.Vits.Dict = keep(inside("dict"));
                }
                else if (kind == "kitten")
                {
                    config.Model.Kitten.Model = keep(Model(folder));
                    config.Model.Kitten.Voices = keep(inside("voices.bin"));
                    config.Model.Kitten.Tokens = keep(inside("tokens.txt"));
                    config.Model.Kitten.Data = keep(Speech(folder));
                }
                else if (kind == "pocket")
                {
                    config.Model.Pocket.Flow = keep(Part(folder, "lm_flow"));
                    config.Model.Pocket.Main = keep(Part(folder, "lm_main"));
                    config.Model.Pocket.Encoder = keep(Part(folder, "encoder"));
                    config.Model.Pocket.Decoder = keep(Part(folder, "decoder"));
                    config.Model.Pocket.Conditioner = keep(Part(folder, "text_conditioner"));
                    config.Model.Pocket.Vocab = keep(inside("vocab.json"));
                    config.Model.Pocket.Scores = keep(inside("token_scores.json"));
                    config.Model.Pocket.Cache = 50;
                }
                else
                {
                    // Kokoro's word lists: every one but the other English's.
                    List<string> lexicons = new List<string>();
                    foreach (string file in Directory.GetFiles(folder, "lexicon*.txt"))
                    {
                        string name = Path.GetFileName(file).ToLowerInvariant();
                        if (name == (lang == "en-gb" || lang == "en" ? "lexicon-us-en.txt" : "lexicon-gb-en.txt")) continue;
                        lexicons.Add(file);
                    }
                    config.Model.Kokoro.Model = keep(Model(folder));
                    config.Model.Kokoro.Voices = keep(inside("voices.bin"));
                    config.Model.Kokoro.Tokens = keep(inside("tokens.txt"));
                    config.Model.Kokoro.Data = keep(Speech(folder));
                    config.Model.Kokoro.Dict = keep(inside("dict"));
                    config.Model.Kokoro.Lexicon = keep(string.Join(",", lexicons.ToArray()));
                    config.Model.Kokoro.Lang = keep(lang);
                }
                config.MaxSentences = 1;
                // A broken or half-downloaded .onnx throws inside sherpa-onnx rather than returning nothing.
                try { tts = LocalVoice.SherpaOnnxCreateOfflineTts(ref config); }
                catch (SEHException) { tts = IntPtr.Zero; }
            }
            finally { foreach (IntPtr pointer in held) Marshal.FreeHGlobal(pointer); }
            if (tts == IntPtr.Zero) throw new VoiceException("That voice model wouldn't start. Check it's a Piper, Kokoro, Kitten or Pocket TTS model for sherpa-onnx.");
            opened = key;
            openKind = kind;
            return kind;
        }

        private static void Close()
        {
            if (tts == IntPtr.Zero) return;
            LocalVoice.SherpaOnnxDestroyOfflineTts(tts);
            tts = IntPtr.Zero;
            opened = null;
            openKind = null;
        }
    }

    // Her opt-in HD voice: Qwen3-TTS 1.7B on the graphics card, served by qwentts.cpp's tts-server so her brain keeps its engine.
    // Her built-in clip goes in with its words, so it copies how she talks as well as how she sounds.
    internal sealed class QwenVoice : IDisposable
    {
        internal const string Style = "HD";
        internal const string Build = "qwentts-5916bf7-ctx";
        // Placeholder links: the engine (qwentts.cpp 5916bf7 with Capri's --ctx-size patch) and the talker (the 1.7B Base cut to Q5_K_M) aren't hosted yet.
        private const string Hosted = "https://github.com/lachydotmcg/capri-desktop/releases/download/hd-voice-1/";
        internal static string EngineUrl = Hosted + Build + "-win-vulkan-x64.zip";
        internal static string EngineSha = "6d9042bc6af4b8c8e9bc31e85166a81c0baea8c83ff9e647f452ce61f7134a22";
        internal static long EngineSize = 16547183;
        internal const string ModelFile = "qwen-talker-1.7b-base-Q5_K_M.gguf";
        internal static string ModelUrl = Hosted + ModelFile;
        internal static string ModelSha = "4257253743e61e13882de0c355357446724231188fcf7a96d38bad838b5097c3";
        internal static long ModelSize = 1411265728;
        internal const string CodecFile = "qwen-tokenizer-12hz-Q8_0.gguf";
        internal static string CodecUrl = "https://huggingface.co/Serveurperso/Qwen3-TTS-GGUF/resolve/b7ee2e8c7459c3bea99da23e3d178125a7d1713c/" + CodecFile;
        internal static string CodecSha = "1883beeed99348fc35e23dd225e9082f93f6f8c109330a33d935baa8acdbfd94";
        internal static long CodecSize = 291150624;
        // 0.7.4 and 0.7.5 ran her HD voice on llama.cpp b11781: 1.5 GB that nothing uses now.
        internal const string OldEngine = "engine-b11781";
        internal static readonly string[] OldFiles = { "Qwen3-TTS-12Hz-1.7B-Base-Q4_K_M.gguf", "mmproj-Qwen3-TTS-12Hz-1.7B-Base-Q8_0.gguf" };
        internal const long CardNeeded = 6L << 30;
        internal const long RoomNeeded = 5L << 29;
        internal const int PieceLength = 300;
        private static readonly string[] Tongues = { "zh", "en", "de", "it", "pt", "es", "ja", "ko", "fr", "ru" };
        private static readonly string[] Languages = { "chinese", "english", "german", "italian", "portuguese", "spanish", "japanese", "korean", "french", "russian" };
        // What she says in her built-in clip, word for word. A new clip needs its words here too.
        private const string Words = "Oh, there you are! I was starting to think you'd forgotten me. Relax, choom. Your favorite little glitch isn't going anywhere. So, what are we getting into tonight?";
        internal static Func<ProcessStartInfo, Process> Spawn = Process.Start;
        private readonly string folder;
        private readonly string engine;
        private readonly object gate = new object();
        private readonly object waking = new object();
        private Process server;
        private int port;
        private bool awake;
        private byte[] clip;
        private byte[] voiced;
        private int voicedOn;
        internal volatile bool Cancel;

        internal QwenVoice(string data)
        {
            string root = Path.Combine(data, "voice");
            folder = Path.Combine(root, "qwen3-tts-1.7b");
            engine = Path.Combine(root, "engine-" + Build);
        }

        internal static long PackSize { get { return EngineSize + CodecSize + ModelSize; } }

        internal string Folder { get { return folder; } }

        private string Server { get { return Path.Combine(engine, "tts-server.exe"); } }

        internal bool Installed { get { return File.Exists(Server) && File.Exists(Path.Combine(folder, ModelFile)) && File.Exists(Path.Combine(folder, CodecFile)); } }

        internal bool Awake { get { lock (gate) return awake && (server == null || !server.HasExited); } }

        internal void Install(Action<string, long, long> progress)
        {
            if (IntPtr.Size != 8) throw new IOException("Her HD voice needs 64-bit Windows.");
            Directory.CreateDirectory(folder);
            string model = Path.Combine(folder, ModelFile), codec = Path.Combine(folder, CodecFile);
            long needed = (File.Exists(Server) ? 0 : EngineSize * 4) + (File.Exists(codec) ? 0 : CodecSize) + (File.Exists(model) ? 0 : ModelSize) + (64L << 20);
            string drive = Path.GetPathRoot(folder);
            if (new DriveInfo(drive).AvailableFreeSpace < needed) throw new IOException("Her HD voice needs " + Engine.Amount(needed) + " free on " + drive.TrimEnd('\\') + ".");
            Func<bool> stopped = delegate { return Cancel; };
            if (!File.Exists(Server))
            {
                string zip = engine + ".zip";
                Engine.Fetch(EngineUrl, zip, EngineSize, EngineSha, "her HD voice", Along(progress, 0), stopped);
                progress("Unpacking her HD voice", EngineSize, PackSize);
                string staging = engine + ".new";
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
                ZipFile.ExtractToDirectory(zip, staging);
                if (!File.Exists(Path.Combine(staging, "tts-server.exe"))) throw new IOException("The HD voice download is missing tts-server.exe.");
                lock (gate)
                {
                    Close();
                    if (Directory.Exists(engine)) Directory.Delete(engine, true);
                    Directory.Move(staging, engine);
                }
                File.Delete(zip);
            }
            if (!File.Exists(codec)) Engine.Fetch(CodecUrl, codec, CodecSize, CodecSha, "her HD voice", Along(progress, EngineSize), stopped);
            if (!File.Exists(model)) Engine.Fetch(ModelUrl, model, ModelSize, ModelSha, "her HD voice", Along(progress, EngineSize + CodecSize), stopped);
        }

        private static Action<string, long, long> Along(Action<string, long, long> progress, long before)
        {
            return delegate(string text, long done, long total) { progress(text, before + done, PackSize); };
        }

        // Clears the old llama.cpp pack, half-finished downloads too. Their clips share its folder and stay.
        internal void Tidy()
        {
            string old = Path.Combine(Path.GetDirectoryName(engine), OldEngine);
            try
            {
                foreach (string staged in new[] { old, old + ".new" }) if (Directory.Exists(staged)) Directory.Delete(staged, true);
                foreach (string part in new[] { old + ".zip", old + ".zip.part" }) File.Delete(part);
                foreach (string name in OldFiles) foreach (string part in new[] { name, name + ".part" }) File.Delete(Path.Combine(folder, part));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        // A 1024-frame cache holds her clip, its words and a 300-character piece, and leaves 670 MB more of the graphics card for her brain than the default.
        internal ProcessStartInfo StartInfo(int number)
        {
            ProcessStartInfo info = new ProcessStartInfo(Server, "--model \"" + Path.Combine(folder, ModelFile) + "\" --codec \"" + Path.Combine(folder, CodecFile) + "\" --host 127.0.0.1 --port " + number + " --ctx-size 1024 --codec-chunk-dur 6");
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.WorkingDirectory = engine;
            return info;
        }

        internal void Warm()
        {
            lock (waking)
            {
                Process running;
                int number;
                lock (gate)
                {
                    if (awake && (server == null || !server.HasExited)) return;
                    Close();
                    if (IntPtr.Size != 8) throw new VoiceException("Her HD voice needs 64-bit Windows.");
                    if (!Installed) throw new VoiceException("Get her HD voice in Capri Mainframe first.");
                    number = port = Engine.FreePort();
                    try { running = server = Spawn(StartInfo(number)); }
                    catch (System.ComponentModel.Win32Exception error) { throw new VoiceException(Voice.Short("Her HD voice wouldn't start: " + error.Message)); }
                    Engine.Lower(running);
                    Engine.Bind(running);
                }
                Stopwatch waited = Stopwatch.StartNew();
                while (waited.Elapsed.TotalSeconds < 120)
                {
                    // Stop() while she's waking hands back a disposed process, so a stop is a quiet cancel rather than a crash.
                    lock (gate)
                    {
                        if (server != running) throw new OperationCanceledException();
                        if (running.HasExited)
                        {
                            Brain.Log("HD voice", waited.ElapsedMilliseconds, "tts-server stopped while loading with exit code 0x" + ((uint)Engine.ExitCode(running)).ToString("X8"));
                            Close();
                            throw new VoiceException("Her HD voice crashed loading. Close games, then try again.");
                        }
                    }
                    bool up = false;
                    try
                    {
                        HttpWebRequest request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + number + "/health");
                        request.Proxy = null;
                        request.Timeout = 2000;
                        using (HttpWebResponse response = (HttpWebResponse)request.GetResponse()) up = response.StatusCode == HttpStatusCode.OK;
                    }
                    catch (WebException) { }
                    if (up)
                    {
                        // One short line before she counts as awake, so the graphics card's first-run setup isn't on her first reply.
                        try { Say(number, "Hey, I'm awake."); }
                        catch (VoiceException)
                        {
                            lock (gate) if (server != running) throw new OperationCanceledException();
                            throw;
                        }
                        lock (gate)
                        {
                            if (server != running) throw new OperationCanceledException();
                            awake = true;
                        }
                        return;
                    }
                    Thread.Sleep(300);
                }
                Stop();
                throw new VoiceException("Her HD voice loaded too slowly.");
            }
        }

        internal byte[] Speak(string text)
        {
            int number;
            lock (gate)
            {
                if (!awake || (server != null && server.HasExited)) throw new VoiceException("Her HD voice is still waking up.");
                number = port;
            }
            return Say(number, text);
        }

        private byte[] Say(int number, string text)
        {
            Voiced(number);
            int tongue = Array.IndexOf(Tongues, Brain.LanguageCode(Brain.Language));
            string rest = ",\"voice\":\"capri\",\"language\":\"" + (tongue >= 0 ? Languages[tongue] : "english") + "\",\"seed\":7,\"max_new_tokens\":512,\"response_format\":\"wav\"}";
            int pitch = VoiceFx.Pitch, speed = VoiceFx.Speed, robot = VoiceFx.HdRobot, glitch = VoiceFx.HdGlitch;
            VoiceFx.Layering layers = VoiceFx.Layers;
            List<short> said = new List<short>();
            int hertz = 24000;
            foreach (string piece in Pieces(Voice.Sayable(text))) said.AddRange(Voice.Samples(Ask(number, "/v1/audio/speech", "{\"input\":" + Voice.Quote(piece) + rest), out hertz));
            short[] samples = said.ToArray();
            if (!VoiceFx.Plain(pitch, speed, robot, glitch, layers)) samples = VoiceFx.Restyled(samples, hertz, pitch, speed, robot, glitch, VoiceFx.Seed(text), layers);
            byte[] wav = Voice.Wave(samples, hertz);
            if (Voice.Seconds(wav) < 0.2) throw new VoiceException("Her HD voice made no sound.");
            return wav;
        }

        // The server holds one voice, capri: her clip with its words, or their clip, which comes without words and gives the sound alone.
        // A different clip goes in before the next line.
        private void Voiced(int number)
        {
            bool hers;
            byte[] wav = Clip(out hers);
            lock (gate) if (voicedOn == number && Same(voiced, wav)) return;
            Ask(number, "/v1/audio/voices", "{\"name\":\"capri\"" + (hers ? ",\"ref_text\":" + Voice.Quote(Words) : "") + ",\"wav_b64\":\"" + Convert.ToBase64String(wav) + "\"}");
            lock (gate)
            {
                voiced = wav;
                voicedOn = number;
            }
        }

        private static bool Same(byte[] one, byte[] other)
        {
            if (one == other) return true;
            if (one == null || one.Length != other.Length) return false;
            for (int i = 0; i < one.Length; i++) if (one[i] != other[i]) return false;
            return true;
        }

        // Each line stops at 512 frames (about 41 s), so long replies go a few sentences at a time.
        internal static List<string> Pieces(string text)
        {
            List<string> pieces = new List<string>();
            string piece = "";
            foreach (string sentence in Regex.Split(text.Trim(), @"(?<=[.!?…])\s+|\s*\n\s*"))
            {
                if (sentence.Length == 0) continue;
                if (piece.Length > 0 && piece.Length + 1 + sentence.Length > PieceLength) { pieces.Add(piece); piece = ""; }
                piece = piece.Length == 0 ? sentence : piece + " " + sentence;
            }
            if (piece.Length > 0) pieces.Add(piece);
            return pieces;
        }

        private static byte[] Ask(int number, string path, string json)
        {
            byte[] payload = Encoding.UTF8.GetBytes(json);
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + number + path);
            request.Method = "POST";
            request.Proxy = null;
            request.ContentType = "application/json";
            request.ContentLength = payload.Length;
            request.Timeout = 60000;
            request.ReadWriteTimeout = 60000;
            try
            {
                using (Stream upload = request.GetRequestStream()) upload.Write(payload, 0, payload.Length);
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (MemoryStream copy = new MemoryStream())
                {
                    stream.CopyTo(copy);
                    return copy.ToArray();
                }
            }
            catch (WebException error)
            {
                HttpWebResponse response = error.Response as HttpWebResponse;
                if (response == null) throw new VoiceException("Her HD voice stopped answering.");
                string detail;
                using (response)
                using (StreamReader reader = new StreamReader(response.GetResponseStream())) detail = reader.ReadToEnd();
                throw new VoiceException(Voice.Short("Her HD voice glitched: HTTP " + (int)response.StatusCode + " " + Regex.Replace(detail ?? "", @"\s+", " ").Trim()));
            }
            catch (IOException) { throw new VoiceException("Her HD voice stopped answering."); }
        }

        internal Func<string, byte[]> Or(Func<string, byte[]> fallback, Action<string> trouble)
        {
            return delegate(string words)
            {
                Stopwatch took = Stopwatch.StartNew();
                try
                {
                    byte[] wav = Speak(words);
                    if (took.Elapsed.TotalSeconds > Math.Max(6, Voice.Seconds(wav) * 1.2)) trouble("Her HD voice is running slowly right now, so she's using her free voice for a bit.");
                    return wav;
                }
                catch (VoiceException error)
                {
                    trouble(Voice.Short(error.Message) + " She's using her free voice for a bit.");
                    if (fallback == null) throw;
                    return fallback(words);
                }
            };
        }

        // reference.wav in her HD voice folder replaces the clip she's built with, so a new reference can be tried without a rebuild.
        private byte[] Clip(out bool hers)
        {
            string own = Path.Combine(folder, YourVoice.Using);
            hers = false;
            // A clip being swapped this instant gives her own clip for one line rather than an error.
            try { if (File.Exists(own)) return File.ReadAllBytes(own); }
            catch (IOException) { }
            hers = true;
            lock (gate)
            {
                if (clip != null) return clip;
                using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("Capri.Voice.reference.wav"))
                {
                    if (source == null) throw new VoiceException("Her HD voice is missing her reference clip.");
                    using (MemoryStream copy = new MemoryStream())
                    {
                        source.CopyTo(copy);
                        clip = copy.ToArray();
                    }
                }
                return clip;
            }
        }

        // Free graphics memory on NVIDIA cards, from nvidia-smi; -1 where that can't be read.
        internal static long FreeRoom()
        {
            string smi = Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
            if (!File.Exists(smi)) return -1;
            try
            {
                ProcessStartInfo start = new ProcessStartInfo(smi, "--query-gpu=memory.total,memory.used --format=csv,noheader,nounits") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                using (Process query = Process.Start(start))
                {
                    string output = query.StandardOutput.ReadToEnd();
                    if (!query.WaitForExit(5000)) return -1;
                    long best = -1;
                    foreach (string line in output.Split('\n'))
                    {
                        string[] parts = line.Split(',');
                        long total, used;
                        if (parts.Length == 2 && long.TryParse(parts[0].Trim(), out total) && long.TryParse(parts[1].Trim(), out used)) best = Math.Max(best, (total - used) << 20);
                    }
                    return best;
                }
            }
            catch (System.ComponentModel.Win32Exception) { return -1; }
            catch (InvalidOperationException) { return -1; }
        }

        internal void Attach(int number)
        {
            lock (gate)
            {
                Close();
                port = number;
                awake = true;
            }
        }

        internal void Stop() { lock (gate) Close(); }

        private void Close()
        {
            awake = false;
            voiced = null;
            if (server == null) return;
            try { if (!server.HasExited) { server.Kill(); server.WaitForExit(3000); } } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
            server.Dispose();
            server = null;
        }

        public void Dispose() { Stop(); }
    }

    // Their own voice model, through a server that speaks OpenAI's speech API (POST /v1/audio/speech), like Kokoro-FastAPI,
    // openedai-speech, AllTalk, LocalAI, Qwen3-TTS behind vLLM-Omni or OpenAI itself, or one that speaks Fish Audio's (POST /v1/tts),
    // as fish.audio's cloud and Fish Speech on their own PC both do. It gets only the words she says. Its sound goes through her
    // pitch and speed like her other voices, with a robot and glitch of its own.
    internal sealed class VoiceServer
    {
        internal const string Style = "Your server";
        internal const string Menu = "Any voice model";
        internal const string Address = "http://localhost:8880/v1";
        internal const string Filling = " Her free voice is filling in.";
        internal const string OpenAi = "openai";
        internal const string Fish = "fish";
        // A voice model file on their PC, run by ModelFile with no server. Its "address" is the model's folder.
        internal const string Files = "file";
        internal const string FishCloud = "https://api.fish.audio";
        internal const string FishFree = "s2.1-pro-free";
        // Her own voice on Fish Audio (Lachy, 29 Oct: "everybody could just use this voice for fish"), used when the voice box is blank.
        internal const string FishCapri = "761c5a725152487797841b01fccf471c";
        // What the Voice box shows for it.
        internal const string FishCapriName = "Capri Default";
        internal const string OpenAiCloud = "https://api.openai.com/v1";
        internal const string Theirs = "Your voice server";
        // How long a server has to start answering a line, in milliseconds, before her free voice says it.
        internal static int Patience = 20000;
        internal volatile string Url = Address;
        internal volatile string Api = OpenAi;
        internal volatile string Model;
        internal volatile string Speaker;
        internal volatile string Key;
        // Picking a server changes all five under this, and a line reads them under it, so a line never pairs one server's
        // address with another's key.
        internal readonly object Changing = new object();
        // The last problem she noted, so a server that stays down is said once, until it answers again.
        private volatile string noted;

        // Ready-made choices for its panel. Picking one fills in its API, address, model and voice; the last keeps what's typed.
        internal static readonly Service[] Services =
        {
            new Service("Fish Audio · cloud", Fish, FishCloud, FishFree, FishCapriName, "Voice ID or fish.audio link", "Paste your Fish Audio key",
                "Fish Audio's voices. Paste a key from fish.audio. Capri Default is her own voice; paste a voice's link or ID from its library for another. " + FishFree + " is its free tier."),
            new Service("Voice model file · on your PC", Files, "", "", "", "Number or clip, optional", "Optional",
                "A Piper, Kokoro, Kitten or Pocket TTS model for sherpa-onnx, run with no server. Browse to its .tar.bz2 or .onnx. Pocket TTS copies a voice clip, or her HD voice if blank."),
            new Service("Fish Speech · on your PC", Fish, "http://127.0.0.1:8080", "", "", "Reference ID, optional", "Optional",
                "Fish Speech running on your PC with tools/api_server.py. Voice is a reference ID you added to it, or blank."),
            new Service("Kokoro · on your PC", OpenAi, Address, "kokoro", "af_heart", "Optional", "Optional",
                "Kokoro-FastAPI running on your PC, with voices like af_heart, af_bella and am_adam."),
            new Service("OpenAI · cloud", OpenAi, OpenAiCloud, "gpt-4o-mini-tts", "marin", "Optional", "Paste your OpenAI key",
                "OpenAI's voices with your OpenAI key: marin, cedar, coral, alloy and more."),
            new Service("Any OpenAI-style server", OpenAi, null, null, null, "Optional", "Optional",
                "Any speech model you run whose server speaks OpenAI's speech API, like Qwen3-TTS through vLLM-Omni, AllTalk or LocalAI.")
        };

        // The APIs she knows; anything else is OpenAI's.
        internal static string Known(string api) { return api == Fish || api == Files ? api : OpenAi; }

        // Fish Audio's and OpenAI's own servers go under CLOUD VOICE. A model file and any server they run go under CUSTOM LOCAL.
        internal static bool InCloud(string url, string api)
        {
            api = Known(api);
            return api == Fish ? Same(url, Fish, FishCloud, Fish) : api == OpenAi && Same(url, OpenAi, OpenAiCloud, OpenAi);
        }

        internal static string CloudName(string api) { return Known(api) == Fish ? "Fish Audio" : "OpenAI"; }

        internal sealed class Service
        {
            internal readonly string Name;
            internal readonly string Api;
            internal readonly string Url;
            internal readonly string Model;
            internal readonly string Speaker;
            internal readonly string VoiceCue;
            internal readonly string KeyCue;
            internal readonly string Hint;

            internal Service(string name, string api, string url, string model, string speaker, string voiceCue, string keyCue, string hint)
            {
                Name = name;
                Api = api;
                Url = url;
                Model = model;
                Speaker = speaker;
                VoiceCue = voiceCue;
                KeyCue = keyCue;
                Hint = hint;
            }
        }

        // The choice a saved setup came from: the one with its API and server, or else the last one with its API.
        internal static Service Find(string api, string url)
        {
            Service last = Services[Services.Length - 1];
            foreach (Service service in Services)
            {
                if (service.Api != api) continue;
                if (service.Url != null && Same(service.Url, api, url, api)) return service;
                last = service;
            }
            return last;
        }

        // Their address as typed. With no scheme it gets https:// for a named host on the internet, and http:// for one on their PC
        // or network. OpenAI's API gets /v1 when it has no path and /audio/speech unless it ends there; Fish Audio's gets /v1/tts.
        internal static Uri Endpoint(string url) { return Endpoint(url, OpenAi); }

        internal static Uri Endpoint(string url, string api)
        {
            bool fish = api == Fish;
            string usual = fish ? FishCloud : Address;
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

        // Its host and port, for her notes.
        internal static string Where(string url) { return Where(url, OpenAi); }

        internal static string Where(string url, string api)
        {
            if (api == Files) return ModelFile.Name(url);
            try { return Where(Endpoint(url, api)); }
            catch (VoiceException) { return (url ?? "").Trim(); }
        }

        private static string Where(Uri at) { return at.IsDefaultPort ? at.Host : at.Host + ":" + at.Port; }

        // Whether two addresses reach the same server, so a key saved for one never goes to another. Model files take no key.
        internal static bool Same(string url, string api, string other, string otherApi)
        {
            if (api == Files || otherApi == Files) return false;
            try
            {
                Uri one = Endpoint(url, api), two = Endpoint(other, otherApi);
                return one.Scheme == two.Scheme && one.Port == two.Port && string.Equals(one.Host, two.Host, StringComparison.OrdinalIgnoreCase);
            }
            catch (VoiceException) { return false; }
        }

        // What she calls it: Fish Audio and OpenAI by name, a model file as their voice model, anything else their voice server.
        internal static string Name(string url, string api)
        {
            if (api == Files) return "Your voice model";
            string host;
            try { host = Endpoint(url, api).Host.ToLowerInvariant(); }
            catch (VoiceException) { return Theirs; }
            if (host == "fish.audio" || host.EndsWith(".fish.audio", StringComparison.Ordinal)) return "Fish Audio";
            return host == "api.openai.com" ? "OpenAI" : Theirs;
        }

        // Its name on the free voice button: a model file by its own name.
        internal static string Label(string url, string api)
        {
            if (api == Files) return ModelFile.Name(url);
            string name = Name(url, api);
            return name == Theirs ? Style : name;
        }

        [DataContract]
        internal sealed class FishPage
        {
            [DataMember(Name = "items")] public List<FishModel> Items;
            [DataMember(Name = "has_more")] public bool More;
        }

        [DataContract]
        internal sealed class FishModel
        {
            [DataMember(Name = "_id")] public string Id;
            [DataMember(Name = "title")] public string Title;
        }

        // Their own voices on Fish Audio, as ID and title, from its voice list (GET /model?self=true), up to 500 of them.
        internal static List<KeyValuePair<string, string>> FishVoices(string url, string key)
        {
            Uri tts = Endpoint(url, Fish);
            List<KeyValuePair<string, string>> voices = new List<KeyValuePair<string, string>>();
            for (int page = 1; page <= 5; page++)
            {
                UriBuilder at = new UriBuilder(tts.Scheme, tts.Host, tts.Port, "/model");
                at.Query = "self=true&page_size=100&page_number=" + page;
                if (tts.Scheme == Uri.UriSchemeHttps) ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(at.Uri);
                request.Method = "GET";
                request.UserAgent = "Capri-Desktop";
                if (tts.IsLoopback) request.Proxy = null;
                if (!string.IsNullOrEmpty(key)) request.Headers[HttpRequestHeader.Authorization] = "Bearer " + key;
                request.Timeout = Patience;
                request.ReadWriteTimeout = Patience;
                FishPage found;
                try
                {
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    using (Stream stream = response.GetResponseStream())
                        found = (FishPage)new DataContractJsonSerializer(typeof(FishPage)).ReadObject(stream);
                }
                catch (WebException error)
                {
                    HttpWebResponse response = error.Response as HttpWebResponse;
                    if (response == null) throw new VoiceException("Can't reach Fish Audio. Check your internet connection.");
                    int status = (int)response.StatusCode;
                    response.Close();
                    if (status == 401 || status == 403) throw new VoiceException(string.IsNullOrEmpty(key) ? "Paste your Fish Audio key first." : "Fish Audio turned down its key (HTTP " + status + ").");
                    throw new VoiceException("Fish Audio said HTTP " + status + ".");
                }
                catch (SerializationException) { throw new VoiceException("Fish Audio sent a voice list Capri can't read."); }
                catch (IOException) { throw new VoiceException("Can't reach Fish Audio. Check your internet connection."); }
                if (found == null || found.Items == null) break;
                foreach (FishModel model in found.Items)
                    if (!string.IsNullOrEmpty(model.Id)) voices.Add(new KeyValuePair<string, string>(model.Id, string.IsNullOrEmpty(model.Title) ? model.Id : model.Title));
                if (!found.More || found.Items.Count == 0) break;
            }
            return voices;
        }

        [DataContract]
        internal sealed class FishMade
        {
            [DataMember(Name = "_id")] public string Id;
        }

        // A new private Fish Audio voice from a clip of someone talking, in Fish Audio's fast mode so it's ready at once
        // (POST /model). Fish Audio writes down what's said in it. Returns its ID.
        internal static string MakeFishVoice(string url, string key, string clip)
        {
            if (string.IsNullOrEmpty(key)) throw new VoiceException("Paste your Fish Audio key first.");
            FileInfo file = new FileInfo(clip);
            if (!file.Exists) throw new VoiceException("That clip isn't there anymore. Pick it again.");
            if (file.Length > 50L << 20) throw new VoiceException("That clip is too big. Pick 10 to 30 seconds of talking.");
            Uri tts = Endpoint(url, Fish);
            string boundary = "capri" + Guid.NewGuid().ToString("N");
            Func<string, string, string> field = delegate(string name, string value) { return "--" + boundary + "\r\nContent-Disposition: form-data; name=\"" + name + "\"\r\n\r\n" + value + "\r\n"; };
            byte[] head = Encoding.UTF8.GetBytes(field("type", "tts") + field("title", "Capri · " + Path.GetFileNameWithoutExtension(file.Name)) + field("train_mode", "fast") + field("visibility", "private")
                + "--" + boundary + "\r\nContent-Disposition: form-data; name=\"voices\"; filename=\"" + Regex.Replace(file.Name, "[\"\\r\\n]", "_") + "\"\r\nContent-Type: application/octet-stream\r\n\r\n");
            byte[] tail = Encoding.UTF8.GetBytes("\r\n--" + boundary + "--\r\n");
            if (tts.Scheme == Uri.UriSchemeHttps) ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(new UriBuilder(tts.Scheme, tts.Host, tts.Port, "/model").Uri);
            request.Method = "POST";
            request.UserAgent = "Capri-Desktop";
            if (tts.IsLoopback) request.Proxy = null;
            request.Headers[HttpRequestHeader.Authorization] = "Bearer " + key;
            request.ContentType = "multipart/form-data; boundary=" + boundary;
            request.ContentLength = head.Length + file.Length + tail.Length;
            request.ServicePoint.Expect100Continue = false;
            request.Timeout = 120000;
            request.ReadWriteTimeout = 120000;
            try
            {
                using (Stream upload = request.GetRequestStream())
                {
                    upload.Write(head, 0, head.Length);
                    using (FileStream sound = file.OpenRead()) sound.CopyTo(upload);
                    upload.Write(tail, 0, tail.Length);
                }
                FishMade made;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                    made = (FishMade)new DataContractJsonSerializer(typeof(FishMade)).ReadObject(stream);
                if (made == null || string.IsNullOrEmpty(made.Id)) throw new VoiceException("Fish Audio didn't say which voice it made. Look for it on fish.audio.");
                return made.Id;
            }
            catch (WebException error)
            {
                HttpWebResponse response = error.Response as HttpWebResponse;
                if (response == null) throw new VoiceException("Can't reach Fish Audio. Check your internet connection.");
                int status = (int)response.StatusCode;
                string detail;
                using (response)
                using (StreamReader reader = new StreamReader(response.GetResponseStream())) detail = Said(reader.ReadToEnd());
                if (status == 401 || status == 403) throw new VoiceException("Fish Audio turned down its key (HTTP " + status + ").");
                throw new VoiceException("Fish Audio couldn't make that voice (HTTP " + status + ")" + (detail.Length > 0 ? ": " + Voice.Short(detail) : "."));
            }
            catch (SerializationException) { throw new VoiceException("Fish Audio didn't say which voice it made. Look for it on fish.audio."); }
            catch (IOException) { throw new VoiceException("Can't reach Fish Audio. Check your internet connection."); }
        }

        // A Fish Audio voice's ID, taken from its fish.audio link if that's what they pasted.
        internal static string FishVoice(string typed)
        {
            string voice = (typed ?? "").Trim();
            Match link = Regex.Match(voice, @"fish\.audio/\S*?([0-9a-f]{32})", RegexOptions.IgnoreCase);
            if (link.Success) return link.Groups[1].Value.ToLowerInvariant();
            return string.Equals(voice, FishCapriName, StringComparison.OrdinalIgnoreCase) ? FishCapri : voice;
        }

        private static string Slow(string name) { return name + " took over " + (Patience / 1000.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " s to answer."; }

        internal byte[] Speak(string text)
        {
            string url, api, model, speaker, key;
            lock (Changing)
            {
                url = Url;
                api = Api;
                model = Model;
                speaker = Speaker;
                key = Key;
            }
            bool fish = api == Fish;
            string name = Name(url, api);
            int pitch = VoiceFx.Pitch, speed = VoiceFx.Speed, robot = VoiceFx.ServerRobot, glitch = VoiceFx.ServerGlitch;
            VoiceFx.Layering layers = VoiceFx.Layers;
            List<short> said = new List<short>();
            if (api == Files)
            {
                // A model file says it all on this PC, its voice picked by speaker number (or Pocket TTS's clip) and a Kokoro
                // model's language by Model.
                int rate;
                short[] made = ModelFile.Speak(ModelFile.Clean(url), model, speaker, Voice.Sayable(text), out rate);
                said.AddRange(rate == YourVoice.Rate || made.Length == 0 ? made : VoiceFx.Resample(made, rate / (double)YourVoice.Rate));
            }
            Uri endpoint = api == Files ? null : Endpoint(url, api);
            // A long reply goes a few sentences at a time, so no one call has to make all of it before it starts answering.
            foreach (string piece in api == Files ? new List<string>() : QwenVoice.Pieces(Voice.Sayable(text)))
            {
                StringBuilder body = new StringBuilder("{");
                if (fish)
                {
                    // Fish Audio's API takes its voice as a reference ID, and its model in a header.
                    body.Append("\"text\":").Append(Voice.Quote(piece));
                    string voice = !string.IsNullOrEmpty(speaker) ? FishVoice(speaker) : url != null && url.TrimEnd('/') == FishCloud ? FishCapri : null;
                    if (voice != null) body.Append(",\"reference_id\":").Append(Voice.Quote(voice));
                    body.Append(",\"format\":\"wav\",\"sample_rate\":").Append(YourVoice.Rate).Append('}');
                }
                else
                {
                    if (!string.IsNullOrEmpty(model)) body.Append("\"model\":").Append(Voice.Quote(model)).Append(',');
                    body.Append("\"input\":").Append(Voice.Quote(piece));
                    if (!string.IsNullOrEmpty(speaker)) body.Append(",\"voice\":").Append(Voice.Quote(speaker));
                    body.Append(",\"response_format\":\"wav\"}");
                }
                said.AddRange(Ask(endpoint, key, fish ? model : null, body.ToString(), name));
            }
            short[] samples = said.ToArray();
            if (samples.Length == 0) throw new VoiceException(name + " sent back no sound.");
            if (!VoiceFx.Plain(pitch, speed, robot, glitch, layers)) samples = VoiceFx.Restyled(samples, YourVoice.Rate, pitch, speed, robot, glitch, VoiceFx.Seed(text), layers);
            return Voice.Wave(samples, YourVoice.Rate);
        }

        // `model` goes in a header, where Fish Audio's API takes it. `name` is what her notes call the server.
        private static short[] Ask(Uri endpoint, string key, string model, string json, string name)
        {
            byte[] payload = Encoding.UTF8.GetBytes(json);
            if (endpoint.Scheme == Uri.UriSchemeHttps) ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(endpoint);
            request.Method = "POST";
            // A server on this PC is reached directly, never through a proxy.
            if (endpoint.IsLoopback) request.Proxy = null;
            request.UserAgent = "Capri-Desktop";
            request.ContentType = "application/json";
            request.ContentLength = payload.Length;
            if (!string.IsNullOrEmpty(key)) request.Headers[HttpRequestHeader.Authorization] = "Bearer " + key;
            if (!string.IsNullOrEmpty(model)) request.Headers["model"] = model;
            request.ServicePoint.Expect100Continue = false;
            request.Timeout = Patience;
            request.ReadWriteTimeout = Patience;
            bool theirs = name == Theirs;
            try
            {
                using (Stream upload = request.GetRequestStream()) upload.Write(payload, 0, payload.Length);
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (MemoryStream copy = new MemoryStream())
                {
                    stream.CopyTo(copy);
                    return Sound(copy.ToArray(), response.ContentType, name);
                }
            }
            catch (WebException error)
            {
                HttpWebResponse response = error.Response as HttpWebResponse;
                if (response == null)
                {
                    if (error.Status == WebExceptionStatus.Timeout) throw new VoiceException(Slow(name));
                    if (error.Status == WebExceptionStatus.ConnectFailure || error.Status == WebExceptionStatus.NameResolutionFailure)
                        throw new VoiceException(theirs ? "Can't reach your voice server at " + Where(endpoint) + ". Is it running?" : "Can't reach " + name + ". Check your internet connection.");
                    throw new VoiceException(name + " at " + Where(endpoint) + " stopped answering.");
                }
                int status = (int)response.StatusCode;
                string detail;
                using (response)
                using (StreamReader reader = new StreamReader(response.GetResponseStream())) detail = Said(reader.ReadToEnd());
                // Fish Audio's 404 is a voice it can't find, and its 429 is too many lines too fast.
                if (status == 404 && name == "Fish Audio") throw new VoiceException("Fish Audio can't find that voice (HTTP 404). Check its ID or link on fish.audio.");
                if (status == 404) throw new VoiceException(name + " has no " + endpoint.AbsolutePath + " (HTTP 404). Check its address.");
                if (status == 429) throw new VoiceException(name + " is busy (HTTP 429). Try again in a moment.");
                if (status == 401 || status == 403) throw new VoiceException(string.IsNullOrEmpty(key) ? name + " wants a key (HTTP " + status + "). Add it under " + (theirs ? "CUSTOM LOCAL > Set up" : "CLOUD VOICE") + "." : name + " turned down its key (HTTP " + status + ").");
                // Fish Audio's paid models answer 402 on an account with no credit. Its free tier is a model of its own.
                if (status == 402 && name == "Fish Audio" && model != FishFree) throw new VoiceException("Fish Audio wants credit for " + (string.IsNullOrEmpty(model) ? "its usual model" : model) + " (HTTP 402). For its free tier, set Model to " + FishFree + ".");
                throw new VoiceException(name + " said HTTP " + status + (detail.Length > 0 ? ": " + Voice.Short(detail) : "."));
            }
            catch (IOException) { throw new VoiceException(name + " at " + Where(endpoint) + " stopped answering."); }
        }

        // Its sound as 24 kHz mono: a WAV in any coding YourVoice reads, or anything Windows' own decoders can, like MP3.
        private static short[] Sound(byte[] reply, string type, string name)
        {
            if (reply.Length == 0) throw new VoiceException(name + " sent back no sound.");
            int rate;
            float[] mono = YourVoice.Wav(reply, out rate);
            if (mono == null && Regex.IsMatch(type ?? "", "json|text|html", RegexOptions.IgnoreCase)) throw new VoiceException(name + " sent words, not sound: " + Voice.Short(Said(Encoding.UTF8.GetString(reply))));
            if (mono == null)
            {
                byte[] decoded = null;
                try { using (MemoryStream input = new MemoryStream(reply)) decoded = YourVoice.Decode(input); }
                catch (Exception) { }
                if (decoded != null) mono = YourVoice.Wav(decoded, out rate);
            }
            if (mono == null) throw new VoiceException("Capri couldn't read the sound " + (name == Theirs ? "your voice server" : name) + " sent.");
            short[] pcm = new short[mono.Length];
            for (int i = 0; i < mono.Length; i++) pcm[i] = (short)Math.Max(-32768, Math.Min(32767, Math.Round(mono[i] * 32767)));
            return rate == YourVoice.Rate ? pcm : VoiceFx.Resample(pcm, rate / (double)YourVoice.Rate);
        }

        // What a reply says in words: OpenAI's error message, FastAPI's detail, or the text itself.
        private static string Said(string reply)
        {
            string text = reply ?? "";
            Match message = Regex.Match(text, "\"(?:message|detail|msg)\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (message.Success)
            {
                text = message.Groups[1].Value;
                try { text = Regex.Unescape(text); }
                catch (ArgumentException) { }
            }
            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        // Her free voice says any line the server can't. What went wrong is noted once, until the server answers again.
        internal Func<string, byte[]> Or(Func<string, byte[]> fallback, Action<string> trouble)
        {
            return delegate(string words)
            {
                string problem;
                try
                {
                    byte[] wav = Speak(words);
                    noted = null;
                    return wav;
                }
                catch (VoiceException error)
                {
                    if (fallback == null) throw;
                    problem = error.Message;
                }
                // Anything else that breaks is said the same way, so her free voice still fills in.
                catch (Exception error)
                {
                    if (fallback == null) throw;
                    problem = Voice.Short(Name(Url, Api) + " glitched: " + error.Message);
                }
                problem += (Regex.IsMatch(problem, @"[.!?]$") ? "" : ".") + Filling;
                if (problem != noted)
                {
                    noted = problem;
                    trouble(problem);
                }
                return fallback(words);
            };
        }
    }
#endif
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // Capri Remote (remote/PROTOCOL.md): Lachy chats with her from his phone through a relay that only ever sees sealed
    // envelopes. This file is the PC's protocol plumbing with no window or brain in it, so app/RemoteTests can run it on its
    // own against the real relay: JSON, the messages and what's kept in settings. The connection is app/RemoteLink.cs.

    internal sealed class RemotePicture
    {
        internal readonly string Mime;
        internal readonly byte[] Bytes;

        private RemotePicture(string mime, byte[] bytes) { Mime = mime; Bytes = bytes; }

        internal static bool Read(RemoteJson.Map message, out RemotePicture picture)
        {
            picture = null;
            if (message.PlainBytes >= 60000) return false;
            if (!message.ContainsKey("picture")) return true;
#if CROSS
            return false;
#else
            RemoteJson.Map map = message.Child("picture");
            if (map == null) return false;
            string mime = map.Text("mime"), data = map.Text("data");
            if ((mime != "image/jpeg" && mime != "image/png") || data == null || data.Length == 0 || data.Length >= 60000) return false;
            byte[] bytes = RemoteCrypto.FromB64u(data);
            if (bytes == null) return false;
            bool jpeg = bytes.Length >= 4 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255;
            bool png = bytes.Length >= 8 && bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71 && bytes[4] == 13 && bytes[5] == 10 && bytes[6] == 26 && bytes[7] == 10;
            if (mime == "image/jpeg" ? !jpeg : !png) return false;
            try
            {
                using (System.IO.MemoryStream input = new System.IO.MemoryStream(bytes, false))
                using (System.Drawing.Image image = System.Drawing.Image.FromStream(input, false, true))
                {
                    if (image.Width < 1 || image.Height < 1 || image.Width > 1568 || image.Height > 1568) return false;
                    if (image.RawFormat.Guid != (jpeg ? System.Drawing.Imaging.ImageFormat.Jpeg.Guid : System.Drawing.Imaging.ImageFormat.Png.Guid)) return false;
                    using (System.Drawing.Bitmap decoded = new System.Drawing.Bitmap(image.Width, image.Height))
                    using (System.Drawing.Graphics draw = System.Drawing.Graphics.FromImage(decoded)) draw.DrawImage(image, 0, 0, image.Width, image.Height);
                }
            }
            catch (Exception) { return false; }
            picture = new RemotePicture(mime, bytes);
            return true;
#endif
        }
    }

    // A small strict JSON reader and writer. Objects come back as Map, arrays as List<object>, numbers as double. The writer
    // escapes like JSON.stringify, so the messages match the test vectors byte for byte.
    internal static class RemoteJson
    {
        internal sealed class Map : Dictionary<string, object>
        {
            internal Map() : base(StringComparer.Ordinal) { }

            internal int PlainBytes;

            internal string Text(string name) { object value; return TryGetValue(name, out value) ? value as string : null; }

            internal bool Number(string name, out double number)
            {
                object value;
                number = 0;
                if (!TryGetValue(name, out value) || !(value is double)) return false;
                number = (double)value;
                return true;
            }

            internal bool Is(string name, bool flag) { object value; return TryGetValue(name, out value) && value is bool && (bool)value == flag; }

            internal List<object> Items(string name) { object value; return TryGetValue(name, out value) ? value as List<object> : null; }

            internal Map Child(string name) { object value; return TryGetValue(name, out value) ? value as Map : null; }
        }

        private static readonly UTF8Encoding Strict = new UTF8Encoding(false, true);

        internal static object Parse(string text)
        {
            Reader reader = new Reader(text ?? "");
            object value = reader.Value(0);
            reader.Space();
            if (!reader.End) throw new FormatException("Something follows the JSON.");
            return value;
        }

        internal static Map ParseMap(string text)
        {
            try { return Parse(text) as Map; }
            catch (FormatException) { return null; }
        }

        // An envelope's plaintext: strict UTF-8 holding one JSON object with a string "type", or null.
        internal static Map Message(byte[] utf8)
        {
            string text;
            try { text = Strict.GetString(utf8); }
            catch (ArgumentException) { return null; }
            Map map = ParseMap(text);
            if (map != null) map.PlainBytes = utf8.Length;
            return map != null && map.Text("type") != null ? map : null;
        }

        private sealed class Reader
        {
            private readonly string s;
            private int at;

            internal Reader(string text) { s = text; }

            internal bool End { get { return at >= s.Length; } }

            internal void Space() { while (at < s.Length && (s[at] == ' ' || s[at] == '\t' || s[at] == '\n' || s[at] == '\r')) at++; }

            private FormatException Bad() { return new FormatException("Bad JSON at " + at + "."); }

            internal object Value(int depth)
            {
                if (depth > 64) throw new FormatException("The JSON nests too deep.");
                Space();
                if (at >= s.Length) throw Bad();
                char c = s[at];
                if (c == '{') return Object(depth);
                if (c == '[') return Array(depth);
                if (c == '"') return String();
                if (c == 't') { Word("true"); return true; }
                if (c == 'f') { Word("false"); return false; }
                if (c == 'n') { Word("null"); return null; }
                return Number();
            }

            private void Word(string word)
            {
                if (string.CompareOrdinal(s, at, word, 0, word.Length) != 0) throw Bad();
                at += word.Length;
            }

            private Map Object(int depth)
            {
                Map map = new Map();
                at++;
                Space();
                if (at < s.Length && s[at] == '}') { at++; return map; }
                while (true)
                {
                    Space();
                    if (at >= s.Length || s[at] != '"') throw Bad();
                    string key = String();
                    Space();
                    if (at >= s.Length || s[at] != ':') throw Bad();
                    at++;
                    map[key] = Value(depth + 1);
                    Space();
                    if (at >= s.Length) throw Bad();
                    if (s[at] == ',') { at++; continue; }
                    if (s[at] == '}') { at++; return map; }
                    throw Bad();
                }
            }

            private List<object> Array(int depth)
            {
                List<object> list = new List<object>();
                at++;
                Space();
                if (at < s.Length && s[at] == ']') { at++; return list; }
                while (true)
                {
                    list.Add(Value(depth + 1));
                    Space();
                    if (at >= s.Length) throw Bad();
                    if (s[at] == ',') { at++; continue; }
                    if (s[at] == ']') { at++; return list; }
                    throw Bad();
                }
            }

            private string String()
            {
                StringBuilder text = new StringBuilder();
                at++;
                while (true)
                {
                    if (at >= s.Length) throw Bad();
                    char c = s[at++];
                    if (c == '"') return text.ToString();
                    if (c < 0x20) throw Bad();
                    if (c != '\\') { text.Append(c); continue; }
                    if (at >= s.Length) throw Bad();
                    char e = s[at++];
                    switch (e)
                    {
                        case '"': text.Append('"'); break;
                        case '\\': text.Append('\\'); break;
                        case '/': text.Append('/'); break;
                        case 'b': text.Append('\b'); break;
                        case 'f': text.Append('\f'); break;
                        case 'n': text.Append('\n'); break;
                        case 'r': text.Append('\r'); break;
                        case 't': text.Append('\t'); break;
                        case 'u':
                            if (at + 4 > s.Length) throw Bad();
                            int code;
                            if (!int.TryParse(s.Substring(at, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out code)) throw Bad();
                            text.Append((char)code);
                            at += 4;
                            break;
                        default: throw Bad();
                    }
                }
            }

            private double Number()
            {
                int start = at;
                if (at < s.Length && s[at] == '-') at++;
                if (at >= s.Length) throw Bad();
                if (s[at] == '0') at++;
                else if (s[at] >= '1' && s[at] <= '9') while (at < s.Length && s[at] >= '0' && s[at] <= '9') at++;
                else throw Bad();
                if (at < s.Length && s[at] == '.')
                {
                    at++;
                    if (at >= s.Length || s[at] < '0' || s[at] > '9') throw Bad();
                    while (at < s.Length && s[at] >= '0' && s[at] <= '9') at++;
                }
                if (at < s.Length && (s[at] == 'e' || s[at] == 'E'))
                {
                    at++;
                    if (at < s.Length && (s[at] == '+' || s[at] == '-')) at++;
                    if (at >= s.Length || s[at] < '0' || s[at] > '9') throw Bad();
                    while (at < s.Length && s[at] >= '0' && s[at] <= '9') at++;
                }
                double value;
                if (!double.TryParse(s.Substring(start, at - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value) || double.IsInfinity(value)) throw Bad();
                return value;
            }
        }

        // One JSON object, its fields in the order they're added.
        internal sealed class Writer
        {
            private readonly StringBuilder text = new StringBuilder("{");
            private bool first = true;

            private Writer Key(string name)
            {
                if (!first) text.Append(',');
                first = false;
                Quote(text, name);
                text.Append(':');
                return this;
            }

            internal Writer Text(string name, string value) { Key(name); Quote(text, value); return this; }

            internal Writer Number(string name, long value) { Key(name); text.Append(value.ToString(CultureInfo.InvariantCulture)); return this; }

            internal Writer Flag(string name, bool value) { Key(name); text.Append(value ? "true" : "false"); return this; }

            internal Writer Texts(string name, IEnumerable<string> values)
            {
                Key(name);
                text.Append('[');
                bool next = false;
                foreach (string value in values)
                {
                    if (next) text.Append(',');
                    next = true;
                    Quote(text, value);
                }
                text.Append(']');
                return this;
            }

            internal Writer Objects(string name, IEnumerable<Writer> values)
            {
                Key(name);
                text.Append('[');
                bool next = false;
                foreach (Writer value in values)
                {
                    if (next) text.Append(',');
                    next = true;
                    text.Append(value.ToString());
                }
                text.Append(']');
                return this;
            }

            public override string ToString() { return text.ToString() + "}"; }
        }

        internal static void Quote(StringBuilder into, string value)
        {
            into.Append('"');
            string text = value ?? "";
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"') into.Append("\\\"");
                else if (c == '\\') into.Append("\\\\");
                else if (c == '\b') into.Append("\\b");
                else if (c == '\f') into.Append("\\f");
                else if (c == '\n') into.Append("\\n");
                else if (c == '\r') into.Append("\\r");
                else if (c == '\t') into.Append("\\t");
                else if (c < 0x20) into.Append("\\").Append('u').Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) into.Append(c).Append(text[++i]);
                // A lone half of a pair would turn into U+FFFD in UTF-8, so it's written escaped, as JSON.stringify does.
                else if (char.IsSurrogate(c)) into.Append("\\").Append('u').Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else into.Append(c);
            }
            into.Append('"');
        }
    }

    // Every message the PC sends, with its fields in the order the test vectors have them.
    internal static class RemoteMessages
    {
        internal const int Protocol = 1;
        // Sent byte for byte: any other spelling still gets a pong, but wakes the relay's room (PROTOCOL.md 4.2).
        internal const string Ping = "{\"t\":\"ping\"}";
        internal const string Pong = "{\"t\":\"pong\"}";

        private static RemoteJson.Writer Control(string t) { return new RemoteJson.Writer().Text("t", t); }

        internal static string Auth(string token) { return Control("auth").Text("token", token).ToString(); }

        internal static string Code(string hash, int ttl) { return Control("code").Text("hash", hash).Number("ttl", ttl).ToString(); }

        internal static string CodeCancel() { return Control("code-cancel").ToString(); }

        internal static string Admit(string pending, string device, string tokenHash, string env) { return Control("admit").Text("pending", pending).Text("device", device).Text("tokenHash", tokenHash).Text("env", env).ToString(); }

        internal static string Deny(string pending, string env)
        {
            RemoteJson.Writer deny = Control("deny").Text("pending", pending);
            if (env != null) deny.Text("env", env);
            return deny.ToString();
        }

        internal static string Revoke(string device) { return Control("revoke").Text("device", device).ToString(); }

        internal static string Kick(string device) { return Control("kick").Text("device", device).ToString(); }

        private static RemoteJson.Writer Typed(string type) { return new RemoteJson.Writer().Text("type", type); }

        internal static string PairAllow(string device, string token, string key, string name, string app) { return Typed("pair-allow").Number("v", Protocol).Text("device", device).Text("token", token).Text("key", key).Text("name", name).Text("app", app).ToString(); }

        internal static string PairDeny(string reason) { return Typed("pair-deny").Number("v", Protocol).Text("reason", reason).ToString(); }

        internal static string HelloOk(string nonce, string echo, string name, string app, string[] features) { return Typed("hello-ok").Number("v", Protocol).Text("nonce", nonce).Text("echo", echo).Text("name", name).Text("app", app).Texts("features", features).ToString(); }

        internal static string VersionError() { return Typed("error").Text("code", "version").Number("v", Protocol).ToString(); }

        internal static string Error(string code, string re)
        {
            RemoteJson.Writer error = Typed("error").Text("code", code);
            if (re != null) error.Text("re", re);
            return error.ToString();
        }

        internal static string Typing(bool on, string re)
        {
            RemoteJson.Writer typing = Typed("chat.typing").Flag("on", on);
            if (re != null) typing.Text("re", re);
            return typing.ToString();
        }

        internal static string Reply(string id, string re, string text, bool done)
        {
            RemoteJson.Writer reply = Typed("chat.reply").Text("id", id);
            if (re != null) reply.Text("re", re);
            return reply.Text("text", text).Flag("done", done).ToString();
        }

        internal static string Tool(string id, string name, string summary, string state, string re, string move = null)
        {
            RemoteJson.Writer tool = Typed("chat.tool").Text("id", id).Text("name", name).Text("summary", summary).Text("state", state);
            if (re != null) tool.Text("re", re);
            if (state == "done" && (move == "sit" || move == "lie_down" || move == "heart")) tool.Text("move", move);
            return tool.ToString();
        }

        internal static string PongFor(long n) { return Typed("pong").Number("n", n).ToString(); }

        // A pairing link for a paired phone that asked (PROTOCOL.md 5.1 and 8.3): a fresh one-time code's QR URL.
        internal static string PairLink(string re, string url, int ttl) { return Typed("pair.link").Text("re", re).Text("url", url).Number("ttl", ttl).ToString(); }

        internal static string Revoked() { return Typed("revoke").ToString(); }

        // Her jobs for JOBS on the phone (PROTOCOL.md 8.4): a job, its state with her question while she asks, one step, and
        // Recent. Titles and questions are cut to 200 letters and step labels to 120.
        internal static RemoteJson.Writer JobInfo(string id, string task, string state, int steps, long started, long ended)
        {
            RemoteJson.Writer info = new RemoteJson.Writer().Text("id", id).Text("task", Clip(task, 200)).Text("state", state).Number("steps", steps).Number("started", started);
            if (ended > 0) info.Number("ended", ended);
            return info;
        }

        internal static string JobState(string id, string task, string state, int steps, long started, long ended, string ask, string re)
        {
            RemoteJson.Writer info = JobInfo(id, task, state, steps, started, ended).Text("type", "job.state");
            if (state == "asking") info.Text("ask", Clip(ask, 200));
            if (re != null) info.Text("re", re);
            return info.ToString();
        }

        internal static string JobStep(string id, int n, string tool, string label, bool done, bool failed)
        {
            return Typed("job.step").Text("id", id).Number("n", n).Text("tool", Clip(tool, 64)).Text("label", Clip(label, 120)).Flag("done", done).Flag("failed", failed).ToString();
        }

        internal static string JobRecent(IEnumerable<RemoteJson.Writer> list) { return Typed("job.recent").Objects("list", list).ToString(); }

        // Ids are 1 to 64 of A-Z a-z 0-9 _ - (PROTOCOL.md section 8).
        internal static bool IdOk(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 64) return false;
            foreach (char c in id) if (!(c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '_' || c == '-')) return false;
            return true;
        }

        internal static string NewId() { return RemoteCrypto.B64u(RemoteCrypto.Random(16)); }

        // A phone's name comes from whoever holds the QR: control, format (bidi and zero-width), private-use and unassigned
        // characters and lone surrogates come out, any whitespace becomes one space, and it's cut to 64 characters without
        // splitting a pair.
        internal static string CleanName(string name)
        {
            string clean = CleanText(name, 64);
            return clean.Length == 0 ? "Unnamed phone" : clean;
        }

        // At most max UTF-16 units, never cutting a surrogate pair in half. Null becomes "".
        internal static string Clip(string text, int max)
        {
            if (text == null) return "";
            if (text.Length <= max) return text;
            return text.Substring(0, max > 0 && char.IsHighSurrogate(text[max - 1]) ? max - 1 : max);
        }

        // Untrusted text for a label: CleanName's rules, with "" when nothing is left.
        internal static string CleanText(string name, int max)
        {
            StringBuilder kept = new StringBuilder();
            string text = name ?? "";
            for (int i = 0; i < text.Length && kept.Length < 256; i++)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c))
                {
                    kept.Append(' ');
                    continue;
                }
                bool pair = char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]);
                UnicodeCategory kind = CharUnicodeInfo.GetUnicodeCategory(text, i);
                if (pair) i++;
                if (kind == UnicodeCategory.Control || kind == UnicodeCategory.Format || kind == UnicodeCategory.Surrogate || kind == UnicodeCategory.PrivateUse || kind == UnicodeCategory.OtherNotAssigned)
                    continue;
                kept.Append(c);
                if (pair) kept.Append(text[i]);
            }
            return Clip(Regex.Replace(kept.ToString(), " {2,}", " ").Trim(), max).TrimEnd();
        }
    }

    // What settings.json keeps: whether Remote is on, the relay's host, and the room id, PC token and paired phones sealed
    // with DPAPI for this Windows user.
    [DataContract]
    internal sealed class RemoteSettings
    {
        [DataMember] public bool On;
        [DataMember] public string Host;
        [DataMember] public string Sealed;
    }

    internal sealed class RemoteDevice
    {
        internal string Id;
        internal byte[] Key;
        internal string Name;
        internal DateTime Added;
        // The paired phone whose pairing link added this one with no Allow (PROTOCOL.md 5.1), by name, or null.
        internal string From;
        internal string Parent;

        internal byte[] IdBytes { get { return RemoteCrypto.FromB64u(Id, 16); } }
    }

    // The secrets: the room id and PC token made when Remote is first turned on, whether the relay has seen this room, and
    // each paired phone's id, key, name and date. A phone's own token never stays on the PC.
    internal sealed class RemoteState
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Capri.Remote");
        internal string Room, Token;
        internal bool Used;
        internal readonly List<RemoteDevice> Devices = new List<RemoteDevice>();

        internal static RemoteState Fresh()
        {
            RemoteState state = new RemoteState();
            state.Room = RemoteCrypto.B64u(RemoteCrypto.Random(16));
            state.Token = RemoteCrypto.B64u(RemoteCrypto.Random(32));
            return state;
        }

        internal RemoteState Copy()
        {
            RemoteState copy = new RemoteState();
            copy.Room = Room;
            copy.Token = Token;
            copy.Used = Used;
            foreach (RemoteDevice device in Devices) copy.Devices.Add(new RemoteDevice { Id = device.Id, Key = (byte[])device.Key.Clone(), Name = device.Name, Added = device.Added, From = device.From, Parent = device.Parent });
            return copy;
        }

        internal RemoteDevice Find(string id)
        {
            foreach (RemoteDevice device in Devices) if (device.Id == id) return device;
            return null;
        }

        internal string ToJson()
        {
            List<RemoteJson.Writer> devices = new List<RemoteJson.Writer>();
            foreach (RemoteDevice device in Devices)
            {
                RemoteJson.Writer saved = new RemoteJson.Writer().Text("id", device.Id).Text("key", RemoteCrypto.B64u(device.Key)).Text("name", device.Name).Text("added", device.Added.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
                if (device.From != null) saved.Text("from", device.From);
                if (device.Parent != null) saved.Text("parent", device.Parent);
                devices.Add(saved);
            }
            return new RemoteJson.Writer().Number("v", 1).Text("room", Room).Text("token", Token).Flag("used", Used).Objects("devices", devices).ToString();
        }

        internal static RemoteState FromJson(string json)
        {
            RemoteJson.Map map = RemoteJson.ParseMap(json);
            if (map == null || RemoteCrypto.FromB64u(map.Text("room"), 16) == null || RemoteCrypto.FromB64u(map.Text("token"), 32) == null) return null;
            RemoteState state = new RemoteState();
            state.Room = map.Text("room");
            state.Token = map.Text("token");
            state.Used = map.Is("used", true);
            List<object> devices = map.Items("devices");
            if (devices != null)
                foreach (object item in devices)
                {
                    RemoteJson.Map device = item as RemoteJson.Map;
                    byte[] key = device == null ? null : RemoteCrypto.FromB64u(device.Text("key"), 32);
                    if (key == null || RemoteCrypto.FromB64u(device.Text("id"), 16) == null || state.Find(device.Text("id")) != null) continue;
                    DateTime added;
                    if (!DateTime.TryParse(device.Text("added"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out added)) added = DateTime.UtcNow;
                    string linkedBy = device.Text("from") == null ? null : RemoteMessages.CleanName(device.Text("from"));
                    string parent = RemoteCrypto.FromB64u(device.Text("parent"), 16) == null ? null : device.Text("parent");
                    state.Devices.Add(new RemoteDevice { Id = device.Text("id"), Key = key, Name = RemoteMessages.CleanName(device.Text("name")), Added = added.ToUniversalTime(), From = linkedBy, Parent = parent });
                }
            return state;
        }

#if !CROSS
        internal string Seal()
        {
            byte[] plain = Encoding.UTF8.GetBytes(ToJson());
            try { return Convert.ToBase64String(ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser)); }
            finally { Array.Clear(plain, 0, plain.Length); }
        }

        // Null when it can't be read, like settings copied from another Windows user: Remote then starts a new room.
        internal static RemoteState Unseal(string sealedState)
        {
            if (string.IsNullOrEmpty(sealedState)) return null;
            byte[] plain = null;
            try
            {
                plain = ProtectedData.Unprotect(Convert.FromBase64String(sealedState), Entropy, DataProtectionScope.CurrentUser);
                return FromJson(Encoding.UTF8.GetString(plain));
            }
            catch (CryptographicException) { return null; }
            catch (FormatException) { return null; }
            finally { if (plain != null) Array.Clear(plain, 0, plain.Length); }
        }
#endif
    }
}

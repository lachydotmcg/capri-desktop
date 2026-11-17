using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CapriDesktop
{
#if !CROSS
    internal sealed class RoomHost : IDisposable
    {
        internal const int Port = 47613;
        private const int CacheLimit = 48 * 1024 * 1024;
        private static readonly Regex SafePath = new Regex(@"^[A-Za-z0-9_\-./]*$", RegexOptions.CultureInvariant);
        private readonly object gate = new object();
        private readonly TcpListener listener;
        private readonly string token;
        private readonly string site;
        private readonly Dictionary<string, KeyValuePair<string, byte[]>> cache = new Dictionary<string, KeyValuePair<string, byte[]>>(StringComparer.Ordinal);
        private readonly List<Stream> streams = new List<Stream>();
        private int cached;
        private bool talking, closed;
        private readonly bool local;
        internal Func<string, byte[]> Fetch;
        internal Func<string> CameraState;
        internal Func<string> CameraInstall;
        internal Action<byte[]> Frame;

        internal RoomHost(string remotePage)
        {
            if (remotePage == null || !remotePage.EndsWith("/remote/", StringComparison.Ordinal)) throw new ArgumentException("The Room page has to end in /remote/.");
            site = remotePage.Substring(0, remotePage.Length - "remote/".Length);
            local = site.StartsWith("http://127.0.0.1:", StringComparison.Ordinal) || site.StartsWith("http://localhost:", StringComparison.Ordinal);
            byte[] random = new byte[16];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(random);
            token = BitConverter.ToString(random).Replace("-", "").ToLowerInvariant();
            Fetch = Download;
            try { listener = new TcpListener(IPAddress.Loopback, Port); listener.Start(); }
            catch (SocketException) { listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); }
            Thread accept = new Thread(Accept) { IsBackground = true, Name = "Capri Room" };
            accept.Start();
        }

        internal int ListeningPort { get { return ((IPEndPoint)listener.LocalEndpoint).Port; } }

        internal string Url { get { return "http://127.0.0.1:" + ListeningPort + "/" + token + "/remote/?room=app"; } }

        internal int Watching { get { lock (gate) return streams.Count; } }

        internal void Talk(bool on)
        {
            lock (gate)
            {
                if (talking == on) return;
                talking = on;
            }
            Send("talk", on ? "1" : "0");
        }

        private void Send(string name, string data)
        {
            byte[] frame = Encoding.UTF8.GetBytes("event: " + name + "\ndata: " + data + "\n\n");
            List<Stream> open;
            lock (gate) open = new List<Stream>(streams);
            foreach (Stream stream in open)
            {
                try { lock (stream) { stream.Write(frame, 0, frame.Length); stream.Flush(); } }
                catch (IOException) { Drop(stream); }
                catch (ObjectDisposedException) { Drop(stream); }
            }
        }

        private void Drop(Stream stream)
        {
            lock (gate) streams.Remove(stream);
            try { stream.Dispose(); } catch (IOException) { }
        }

        private void Accept()
        {
            while (true)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); }
                catch (SocketException) { return; }
                catch (ObjectDisposedException) { return; }
                catch (InvalidOperationException) { return; }
                ThreadPool.QueueUserWorkItem(delegate { Serve(client); });
            }
        }

        private void Serve(TcpClient client)
        {
            NetworkStream stream = null;
            bool keep = false;
            try
            {
                client.ReceiveTimeout = 10000;
                stream = client.GetStream();
                string head = ReadHead(stream);
                if (head == null) return;
                string[] lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
                string[] request = lines[0].Split(' ');
                if (request.Length != 3 || request[0] != "GET" && request[0] != "POST") { Answer(stream, 405, "text/plain", Encoding.UTF8.GetBytes("GET or POST only")); return; }
                bool post = request[0] == "POST";
                int length = 0;
                foreach (string line in lines)
                    if (line.StartsWith("content-length:", StringComparison.OrdinalIgnoreCase) && !int.TryParse(line.Substring(15).Trim(), out length)) length = -1;
                if (length < 0 || length > 8 * 1024 * 1024) { Answer(stream, 413, "text/plain", Encoding.UTF8.GetBytes("Too big")); return; }
                string host = null;
                foreach (string line in lines)
                    if (line.StartsWith("host:", StringComparison.OrdinalIgnoreCase)) host = line.Substring(5).Trim();
                string port = ":" + ListeningPort;
                if (host != "127.0.0.1" + port && host != "localhost" + port) { Answer(stream, 403, "text/plain", Encoding.UTF8.GetBytes("Not here")); return; }
                string path = request[1];
                int query = path.IndexOfAny(new[] { '?', '#' });
                if (query >= 0) path = path.Substring(0, query);
                string prefix = "/" + token + "/";
                if (!path.StartsWith(prefix, StringComparison.Ordinal)) { Answer(stream, 404, "text/plain", Encoding.UTF8.GetBytes("Not found")); return; }
                string rest = path.Substring(prefix.Length);
                if (post)
                {
                    byte[] posted = ReadBody(stream, length);
                    if (posted == null) return;
                    if (rest == "frame")
                    {
                        Action<byte[]> frame = Frame;
                        if (frame != null && posted.Length > 0) frame(posted);
                        Answer(stream, 200, "text/plain", new byte[0]);
                        return;
                    }
                    if (rest == "camera/install")
                    {
                        Func<string> install = CameraInstall;
                        Answer(stream, 200, "application/json", Encoding.UTF8.GetBytes("{\"state\":\"" + (install == null ? "missing" : install()) + "\"}"));
                        return;
                    }
                    Answer(stream, 404, "text/plain", Encoding.UTF8.GetBytes("Not found"));
                    return;
                }
                if (rest == "camera")
                {
                    Func<string> state = CameraState;
                    Answer(stream, 200, "application/json", Encoding.UTF8.GetBytes("{\"state\":\"" + (state == null ? "missing" : state()) + "\"}"));
                    return;
                }
                if (rest == "events") { keep = Events(stream); return; }
                if (!SafePath.IsMatch(rest) || rest.Contains("..") || rest.Contains("//")) { Answer(stream, 404, "text/plain", Encoding.UTF8.GetBytes("Not found")); return; }
                if (rest == "remote") { Redirect(stream, prefix + "remote/"); return; }
                if (rest.Length == 0 || rest.EndsWith("/", StringComparison.Ordinal)) rest += "index.html";
                byte[] body = Get(rest);
                if (body == null) { Answer(stream, 404, "text/plain", Encoding.UTF8.GetBytes("Not found")); return; }
                Answer(stream, 200, TypeOf(rest), body);
            }
            catch (IOException) { }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
            finally
            {
                if (!keep)
                {
                    if (stream != null) try { stream.Dispose(); } catch (IOException) { }
                    client.Close();
                }
            }
        }

        private bool Events(NetworkStream stream)
        {
            bool talkNow;
            lock (gate)
            {
                if (closed) return false;
                talkNow = talking;
            }
            byte[] head = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nCache-Control: no-store\r\nX-Frame-Options: DENY\r\nConnection: keep-alive\r\n\r\n" +
                "retry: 1000\n\nevent: talk\ndata: " + (talkNow ? "1" : "0") + "\n\n");
            stream.Write(head, 0, head.Length);
            stream.Flush();
            lock (gate) streams.Add(stream);
            return true;
        }

        private byte[] Get(string rest)
        {
            lock (gate)
            {
                KeyValuePair<string, byte[]> hit;
                if (cache.TryGetValue(rest, out hit)) return hit.Value;
            }
            byte[] body;
            try { body = Fetch(site + rest); }
            catch (WebException) { return null; }
            catch (IOException) { return null; }
            catch (NotSupportedException) { return null; }
            catch (UriFormatException) { return null; }
            if (body == null) return null;
            lock (gate)
            {
                if (!local && cached + body.Length <= CacheLimit && !cache.ContainsKey(rest))
                {
                    cache[rest] = new KeyValuePair<string, byte[]>(rest, body);
                    cached += body.Length;
                }
            }
            return body;
        }

        private static byte[] Download(string url)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            HttpWebRequest ask = (HttpWebRequest)WebRequest.Create(url);
            ask.Timeout = 20000;
            ask.ReadWriteTimeout = 20000;
            ask.AllowAutoRedirect = true;
            using (HttpWebResponse answer = (HttpWebResponse)ask.GetResponse())
            {
                if (answer.StatusCode != HttpStatusCode.OK) return null;
                using (Stream body = answer.GetResponseStream())
                using (MemoryStream copy = new MemoryStream())
                {
                    body.CopyTo(copy);
                    return copy.ToArray();
                }
            }
        }

        private static string ReadHead(Stream stream)
        {
            StringBuilder head = new StringBuilder();
            byte[] one = new byte[1];
            while (head.Length < 8192)
            {
                if (stream.Read(one, 0, 1) != 1) return null;
                head.Append((char)one[0]);
                if (head.Length >= 4 && head[head.Length - 1] == '\n' && head[head.Length - 2] == '\r' && head[head.Length - 3] == '\n' && head[head.Length - 4] == '\r')
                    return head.ToString(0, head.Length - 4);
            }
            return null;
        }

        private static byte[] ReadBody(Stream stream, int length)
        {
            byte[] body = new byte[length];
            int at = 0;
            while (at < length)
            {
                int got = stream.Read(body, at, length - at);
                if (got <= 0) return null;
                at += got;
            }
            return body;
        }

        private static void Answer(Stream stream, int status, string type, byte[] body)
        {
            string reason = status == 200 ? "OK" : status == 403 ? "Forbidden" : status == 405 ? "Method Not Allowed" : status == 413 ? "Payload Too Large" : "Not Found";
            byte[] head = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + " " + reason + "\r\nContent-Type: " + type + "\r\nContent-Length: " + body.Length +
                "\r\nCache-Control: no-cache\r\nX-Frame-Options: DENY\r\nContent-Security-Policy: frame-ancestors 'none'\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n");
            stream.Write(head, 0, head.Length);
            stream.Write(body, 0, body.Length);
            stream.Flush();
        }

        private static void Redirect(Stream stream, string to)
        {
            byte[] head = Encoding.ASCII.GetBytes("HTTP/1.1 301 Moved Permanently\r\nLocation: " + to + "\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            stream.Write(head, 0, head.Length);
            stream.Flush();
        }

        internal static string TypeOf(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".html": return "text/html; charset=utf-8";
                case ".js": case ".mjs": return "text/javascript; charset=utf-8";
                case ".css": return "text/css; charset=utf-8";
                case ".json": case ".webmanifest": return "application/json";
                case ".png": return "image/png";
                case ".webp": return "image/webp";
                case ".svg": return "image/svg+xml";
                case ".wasm": return "application/wasm";
                case ".woff2": return "font/woff2";
                case ".ttf": return "font/ttf";
                default: return "application/octet-stream";
            }
        }

        public void Dispose()
        {
            List<Stream> open;
            lock (gate) { closed = true; open = new List<Stream>(streams); streams.Clear(); }
            foreach (Stream stream in open) try { stream.Dispose(); } catch (IOException) { }
            try { listener.Stop(); } catch (SocketException) { }
        }
    }
#endif
}

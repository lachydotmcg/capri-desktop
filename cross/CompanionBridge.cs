using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace CapriDesktop
{
    // The local bridge an Omarchy plugin uses to tell her what changed on the desktop. It is loopback only,
    // token protected, and deliberately has no way to run commands or ask her to do anything: the plugin can
    // ask her to speak, play a fixed gesture, change her quiet state, report state, or send a coarse event.
    internal sealed class CompanionBridge : IDisposable
    {
        internal const int BodyLimit = 8 * 1024;
        internal const string Discovery = "companion-listener.json";
        private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        private readonly object gate = new object();
        private readonly List<TcpClient> clients = new List<TcpClient>();
        private readonly string file;
        private volatile bool stopped;
        internal string Token { get; private set; }
        internal int Port { get; private set; }
        internal Func<Dictionary<string, object>, Dictionary<string, object>> Received;

        internal CompanionBridge(string folder)
        {
            file = Path.Combine(folder, Discovery);
            byte[] secret = new byte[32];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create()) random.GetBytes(secret);
            Token = Convert.ToBase64String(secret);
        }

        internal void Start()
        {
            try
            {
                listener.Start(4);
                Port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Publish();
                Thread thread = new Thread(Accept) { IsBackground = true, Name = "Capri companion" };
                thread.Start();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void Publish()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            string json = HuskJson.Write(HuskJson.Map("version", 1, "port", Port, "token", Token, "pid", Environment.ProcessId));
            if (!OperatingSystem.IsWindows())
            {
                FileStreamOptions options = new FileStreamOptions
                {
                    Mode = FileMode.Create,
                    Access = FileAccess.Write,
                    UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
                };
                using (FileStream stream = new FileStream(file, options))
                using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(json);
                return;
            }
            File.WriteAllText(file, json, new UTF8Encoding(false));
        }

        private void Accept()
        {
            while (!stopped)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); }
                catch (Exception) { return; }
                lock (gate)
                {
                    if (stopped || clients.Count >= 4) { client.Close(); continue; }
                    clients.Add(client);
                }
                ThreadPool.QueueUserWorkItem(delegate { Serve(client); });
            }
        }

        private void Serve(TcpClient client)
        {
            System.Threading.Timer deadline = new System.Threading.Timer(delegate { try { client.Close(); } catch (Exception) { } }, null, 1200, Timeout.Infinite);
            try
            {
                client.ReceiveTimeout = client.SendTimeout = 800;
                using (NetworkStream stream = client.GetStream())
                {
                    StringBuilder head = new StringBuilder();
                    while (head.Length < 16384)
                    {
                        int b = stream.ReadByte();
                        if (b < 0) return;
                        head.Append((char)b);
                        int n = head.Length;
                        if (n >= 4 && head[n - 4] == '\r' && head[n - 3] == '\n' && head[n - 2] == '\r' && head[n - 1] == '\n') break;
                    }
                    if (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal)) { Reply(stream, 400, Refused("bad_headers")); return; }
                    string[] lines = head.ToString().Split(new[] { "\r\n" }, StringSplitOptions.None);
                    if (lines[0] != "POST /event HTTP/1.1" && lines[0] != "POST /event HTTP/1.0") { Reply(stream, 404, Refused("not_found")); return; }
                    Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 1; i < lines.Length - 2; i++)
                    {
                        int colon = lines[i].IndexOf(':');
                        if (colon < 1) { Reply(stream, 400, Refused("bad_headers")); return; }
                        string key = lines[i].Substring(0, colon).Trim();
                        if (headers.ContainsKey(key)) { Reply(stream, 400, Refused("bad_headers")); return; }
                        headers[key] = lines[i].Substring(colon + 1).Trim();
                    }
                    if (headers.ContainsKey("Origin")) { Reply(stream, 403, Refused("origin")); return; }
                    string token;
                    if (!headers.TryGetValue("X-Capri-Token", out token) || !SameToken(token, Token)) { Reply(stream, 403, Refused("token")); return; }
                    string length;
                    int size;
                    if (headers.ContainsKey("Transfer-Encoding") || !headers.TryGetValue("Content-Length", out length) || !int.TryParse(length, NumberStyles.None, CultureInfo.InvariantCulture, out size) || size < 0) { Reply(stream, 400, Refused("length")); return; }
                    if (size > BodyLimit) { Reply(stream, 413, Refused("too_large")); return; }
                    byte[] body = new byte[size];
                    for (int at = 0; at < size;)
                    {
                        int count = stream.Read(body, at, size - at);
                        if (count == 0) return;
                        at += count;
                    }
                    Dictionary<string, object> request;
                    try { request = HuskJson.Read(new UTF8Encoding(false, true).GetString(body)) as Dictionary<string, object>; }
                    catch (Exception) { Reply(stream, 400, Refused("bad_json")); return; }
                    if (request == null || HuskJson.Text(request, "kind").Length == 0) { Reply(stream, 400, Refused("bad_kind")); return; }
                    Func<Dictionary<string, object>, Dictionary<string, object>> received = Received;
                    Dictionary<string, object> answer = received != null ? received(request) : Refused("no_handler");
                    Reply(stream, 200, answer ?? Refused("no_answer"));
                }
            }
            catch (Exception) { }
            finally
            {
                deadline.Dispose();
                lock (gate) clients.Remove(client);
                client.Close();
            }
        }

        private static Dictionary<string, object> Refused(string why)
        {
            return HuskJson.Map("ok", false, "error", why);
        }

        private static void Reply(NetworkStream stream, int status, Dictionary<string, object> body)
        {
            byte[] json = Encoding.UTF8.GetBytes(HuskJson.Write(body));
            string reason = status == 200 ? "OK" : status == 400 ? "Bad Request" : status == 403 ? "Forbidden" : status == 404 ? "Not Found" : status == 413 ? "Payload Too Large" : "Refused";
            byte[] head = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + " " + reason + "\r\nContent-Type: application/json\r\nContent-Length: " + json.Length + "\r\nConnection: close\r\n\r\n");
            stream.Write(head, 0, head.Length);
            stream.Write(json, 0, json.Length);
        }

        private static bool SameToken(string one, string two)
        {
            if (one.Length != two.Length) return false;
            int difference = 0;
            for (int i = 0; i < one.Length; i++) difference |= one[i] ^ two[i];
            return difference == 0;
        }

        public void Dispose()
        {
            stopped = true;
            listener.Stop();
            lock (gate)
            {
                foreach (TcpClient client in clients) client.Close();
                clients.Clear();
            }
            try
            {
                if (File.Exists(file) && HuskJson.Text(HuskJson.Read(File.ReadAllText(file)) as Dictionary<string, object>, "token") == Token) File.Delete(file);
            }
            catch (Exception) { }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    internal sealed class McpLimited : Stream
    {
        private readonly Stream inner;
        private long left;

        internal McpLimited(Stream from, long most)
        {
            inner = from;
            left = most;
        }

        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return false; } }
        public override bool CanWrite { get { return false; } }
        public override long Length { get { throw new NotSupportedException(); } }
        public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
        public override void SetLength(long value) { throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int got = inner.Read(buffer, offset, (int)Math.Min((long)count, left + 1));
            left -= got;
            if (left < 0) throw new McpException("An online server's answer was bigger than " + McpHttp.Most / (1024 * 1024) + " MB, so Capri stopped reading it.");
            return got;
        }
    }

    internal static class McpHttp
    {
        internal const int Most = 4 * 1024 * 1024;
        private static readonly string[] Reserved = { "host", "content-length", "content-type", "accept", "transfer-encoding", "connection", "expect", "user-agent", "mcp-session-id", "mcp-protocol-version", "range", "if-modified-since", "date", "proxy-connection" };

        internal static bool Allowed(string url, out Uri uri)
        {
            uri = null;
            if (string.IsNullOrEmpty(url) || url.Length > 2000 || !Uri.TryCreate(url, UriKind.Absolute, out uri)) return false;
            if (!string.IsNullOrEmpty(uri.UserInfo)) return false;
            if (uri.Scheme == Uri.UriSchemeHttps) return true;
            return uri.Scheme == Uri.UriSchemeHttp && Loopback(uri);
        }

        internal static bool Allowed(string url)
        {
            Uri uri;
            return Allowed(url, out uri);
        }

        internal static bool Loopback(Uri uri)
        {
            string host = uri.Host.ToLowerInvariant();
            return host == "127.0.0.1" || host == "localhost" || host == "[::1]" || host == "::1";
        }

        internal static bool ReservedHeader(string name) { return Array.IndexOf(Reserved, (name ?? "").ToLowerInvariant()) >= 0; }

        internal static string Shown(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url ?? "", UriKind.Absolute, out uri)) return Plugins.Line(url, 80);
            string path = uri.AbsolutePath == "/" ? "" : uri.AbsolutePath;
            return Plugins.Line(uri.Scheme + "://" + uri.Authority + path, 100);
        }

        internal static HttpWebResponse Send(string method, string url, Dictionary<string, string> headers, string contentType, string accept, byte[] body, int timeout)
        {
            Uri uri;
            if (!Allowed(url, out uri)) throw new McpException("Capri only connects to https addresses, and " + Shown(url) + " isn't one.");
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
                request.Method = method;
                request.AllowAutoRedirect = false;
                request.Timeout = timeout;
                request.ReadWriteTimeout = timeout;
                request.UserAgent = "Capri";
                request.ServicePoint.Expect100Continue = false;
                if (Loopback(uri)) request.Proxy = null;
                if (accept != null) request.Accept = accept;
                if (headers != null)
                    foreach (KeyValuePair<string, string> pair in headers)
                    {
                        if (ReservedHeader(pair.Key) && !pair.Key.StartsWith("mcp-", StringComparison.OrdinalIgnoreCase)) continue;
                        request.Headers[pair.Key] = pair.Value;
                    }
                if (body != null)
                {
                    request.ContentType = contentType;
                    request.ContentLength = body.Length;
                    using (Stream to = request.GetRequestStream()) to.Write(body, 0, body.Length);
                }
                return (HttpWebResponse)request.GetResponse();
            }
            catch (WebException error)
            {
                HttpWebResponse response = error.Response as HttpWebResponse;
                if (response != null) return response;
                if (error.Status == WebExceptionStatus.Timeout) throw new McpException(uri.Host + " took longer than " + timeout / 1000 + " seconds to answer.");
                throw new McpException("Couldn't reach " + uri.Host + ": " + Plugins.Line(error.Message, 160));
            }
            catch (ArgumentException error) { throw new McpException("Couldn't send a header to " + uri.Host + ": " + Plugins.Line(error.Message, 120)); }
            catch (IOException error) { throw new McpException("Couldn't reach " + uri.Host + ": " + Plugins.Line(error.Message, 160)); }
        }

        internal static string Text(HttpWebResponse response)
        {
            try
            {
                using (Stream from = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(new McpLimited(from, Most), new UTF8Encoding(false)))
                    return reader.ReadToEnd();
            }
            catch (WebException error) { throw new McpException("The connection to " + response.ResponseUri.Host + " broke: " + Plugins.Line(error.Message, 120)); }
            catch (IOException error) { throw new McpException("The connection to " + response.ResponseUri.Host + " broke: " + Plugins.Line(error.Message, 120)); }
        }

        internal static object Json(string url, Dictionary<string, string> headers, int timeout)
        {
            using (HttpWebResponse response = Send("GET", url, headers, null, "application/json", null, timeout))
            {
                int status = (int)response.StatusCode;
                if (status < 200 || status > 299) return null;
                object parsed;
                return PluginJson.TryParse(Text(response), out parsed) ? parsed : null;
            }
        }

        internal static object Post(string url, string contentType, string body, int timeout, out int status)
        {
            using (HttpWebResponse response = Send("POST", url, null, contentType, "application/json", new UTF8Encoding(false).GetBytes(body), timeout))
            {
                status = (int)response.StatusCode;
                string text = Text(response);
                object parsed;
                if (PluginJson.TryParse(text, out parsed)) return parsed;
                return null;
            }
        }
    }

    internal sealed class McpOnline : McpLink
    {
        private const string Session = "Mcp-Session-Id";
        private readonly PluginServer spec;
        private readonly string pluginId, root;
        private readonly object gate = new object();
        private long next;
        private bool started, ended;
        private string session, protocol, last = "";

        internal McpOnline(PluginServer server, string id, string pluginRoot)
        {
            spec = server;
            pluginId = id;
            root = pluginRoot;
        }

        internal override string Name { get { return spec.Name; } }

        internal override bool Alive { get { lock (gate) return started && !ended; } }

        internal override string Errors { get { lock (gate) return last; } }

        internal override void Start(int timeout)
        {
            lock (gate)
            {
                if (started && !ended) return;
                if (started) throw new McpException(spec.Name + " isn't connected. " + last);
                started = true;
            }
            try
            {
                object answer = Request("initialize", InitializeParams(), timeout);
                string version = PluginJson.Str(answer, "protocolVersion");
                if (version != "2025-06-18" && version != "2025-03-26") throw new McpException(spec.Name + " speaks an MCP version Capri doesn't (" + Plugins.Line(version ?? "none", 20) + ").");
                Version = version;
                Instructions = PluginJson.Str(answer, "instructions");
                lock (gate) protocol = version;
                Send(PluginJson.Make("jsonrpc", "2.0", "method", "notifications/initialized"), -1, "notifications/initialized", timeout);
            }
            catch (McpException error)
            {
                lock (gate) { ended = true; last = error.Message; }
                throw;
            }
        }

        internal override object Request(string method, object parameters, int timeout)
        {
            long id;
            lock (gate)
            {
                if (ended) throw new McpException(spec.Name + " isn't connected. " + last);
                id = ++next;
            }
            Dictionary<string, object> message = PluginJson.Make("jsonrpc", "2.0", "id", id, "method", method);
            if (parameters != null) message["params"] = parameters;
            return Send(message, id, method, timeout);
        }

        private Dictionary<string, string> Headers(string token)
        {
            Dictionary<string, string> made = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> pair in spec.Headers) made[pair.Key] = McpServer.Expand(pair.Value, root);
            if (token != null) made["Authorization"] = "Bearer " + token;
            lock (gate)
            {
                if (session != null) made[Session] = session;
                if (protocol != null) made["MCP-Protocol-Version"] = protocol;
            }
            return made;
        }

        private object Send(Dictionary<string, object> message, long id, string method, int timeout)
        {
            byte[] body = new UTF8Encoding(false).GetBytes(PluginJson.Write(message));
            DateTime until = DateTime.UtcNow.AddMilliseconds(timeout);
            for (int attempt = 0; ; attempt++)
            {
                int left = (int)(until - DateTime.UtcNow).TotalMilliseconds;
                if (left <= 0) throw new McpException(spec.Name + " took longer than " + timeout / 1000 + " seconds to answer.");
                string token = McpSignIn.Token(pluginId, spec.Name, spec.Url);
                bool again = false;
                object result = null;
                using (HttpWebResponse response = McpHttp.Send("POST", spec.Url, Headers(token), "application/json", "application/json, text/event-stream", body, left))
                {
                    int status = (int)response.StatusCode;
                    if (status == 401)
                    {
                        if (attempt == 0 && token != null && McpSignIn.Refreshed(pluginId, spec.Name, spec.Url, token) != null) again = true;
                        else throw new McpSignInNeeded();
                    }
                    else result = Answered(response, status, id, method, until);
                }
                if (!again) return result;
            }
        }

        private object Answered(HttpWebResponse response, int status, long id, string method, DateTime until)
        {
            if (status == 404 && session != null)
            {
                lock (gate) { ended = true; last = spec.Name + " ended the connection. Capri will connect again next time."; }
                throw new McpException(last);
            }
            if (status < 200 || status > 299)
            {
                if (spec.Legacy && method == "initialize" && (status == 400 || status == 404 || status == 405))
                    throw new McpException(spec.Name + " uses the older SSE connection, which Capri can't use yet. Ask its maker for its streamable HTTP address.");
                string said = "";
                try { said = Plugins.Line(McpHttp.Text(response), 140); }
                catch (McpException) { }
                throw new McpException(spec.Name + " answered HTTP " + status + (said.Length == 0 ? "." : ": " + said));
            }
            if (method == "initialize")
            {
                string given = response.Headers[Session];
                if (!string.IsNullOrEmpty(given) && Regex.IsMatch(given, @"^[\x21-\x7e]{1,200}$")) lock (gate) session = given;
            }
            if (id < 0) return null;
            string type = (response.ContentType ?? "").ToLowerInvariant();
            bool found;
            object answer;
            if (type.Contains("text/event-stream")) answer = Streamed(response, id, until, out found);
            else
            {
                string text = McpHttp.Text(response);
                object parsed;
                if (!PluginJson.TryParse(text, out parsed)) throw new McpException(spec.Name + " didn't answer in a way Capri could read.");
                answer = Take(parsed, id, out found);
            }
            if (!found) throw new McpException(spec.Name + " ended its answer without replying.");
            return answer;
        }

        private object Streamed(HttpWebResponse response, long id, DateTime until, out bool found)
        {
            found = false;
            StringBuilder data = new StringBuilder();
            try
            {
                using (Stream from = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(new McpLimited(from, McpHttp.Most), new UTF8Encoding(false)))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (DateTime.UtcNow > until) throw new McpException(spec.Name + " took too long to finish answering.");
                        if (line.Length == 0)
                        {
                            if (data.Length == 0) continue;
                            object hit = Event(data.ToString(), id, out found);
                            data.Length = 0;
                            if (found) return hit;
                            continue;
                        }
                        if (line[0] == ':' || !line.StartsWith("data:", StringComparison.Ordinal)) continue;
                        string piece = line.Substring(5);
                        if (piece.StartsWith(" ", StringComparison.Ordinal)) piece = piece.Substring(1);
                        if (data.Length > 0) data.Append('\n');
                        data.Append(piece);
                    }
                }
            }
            catch (WebException error)
            {
                if (error.Status == WebExceptionStatus.Timeout) throw new McpException(spec.Name + " took too long to finish answering.");
                throw new McpException("The connection to " + spec.Name + " broke: " + Plugins.Line(error.Message, 120));
            }
            catch (IOException error) { throw new McpException("The connection to " + spec.Name + " broke: " + Plugins.Line(error.Message, 120)); }
            return data.Length == 0 ? null : Event(data.ToString(), id, out found);
        }

        private object Event(string text, long id, out bool found)
        {
            found = false;
            object parsed;
            if (!PluginJson.TryParse(text, out parsed)) return null;
            return Take(parsed, id, out found);
        }

        private object Take(object parsed, long id, out bool found)
        {
            found = false;
            List<object> batch = PluginJson.List(parsed);
            if (batch != null)
            {
                foreach (object one in batch)
                {
                    object hit = Take(one, id, out found);
                    if (found) return hit;
                }
                return null;
            }
            Dictionary<string, object> message = PluginJson.Map(parsed);
            if (message == null) return null;
            string method = PluginJson.Str(message, "method");
            if (method != null)
            {
                if (method == "notifications/tools/list_changed") Stale = true;
                return null;
            }
            object given = PluginJson.Get(message, "id");
            bool mine = given is double ? (long)(double)given == id : given is string && (string)given == id.ToString();
            if (!mine) return null;
            object error = PluginJson.Get(message, "error");
            if (error != null) throw new McpException(spec.Name + " said: " + Plugins.Line(PluginJson.Str(error, "message") ?? "an error", 200));
            if (!message.ContainsKey("result")) return null;
            found = true;
            return PluginJson.Get(message, "result");
        }

        public override void Dispose()
        {
            string was;
            bool wasAlive;
            lock (gate)
            {
                wasAlive = started && !ended;
                ended = true;
                was = session;
                session = null;
            }
            if (was == null || !wasAlive) return;
            try
            {
                Dictionary<string, string> headers = Headers(McpSignIn.Cached(pluginId, spec.Name, spec.Url));
                headers[Session] = was;
                using (HttpWebResponse response = McpHttp.Send("DELETE", spec.Url, headers, null, null, null, 2000)) { }
            }
            catch (McpException) { }
        }
    }
}

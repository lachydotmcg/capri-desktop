using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CapriDesktop
{
    internal static class McpTiny
    {
        internal static bool Read(Stream from, out string method, out string target, out Dictionary<string, string> headers, out string body)
        {
            method = null;
            target = null;
            body = "";
            headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            List<byte> head = new List<byte>();
            byte[] one = new byte[1];
            while (head.Count < 16384)
            {
                if (from.Read(one, 0, 1) != 1) return false;
                head.Add(one[0]);
                int n = head.Count;
                if (n >= 4 && head[n - 4] == 13 && head[n - 3] == 10 && head[n - 2] == 13 && head[n - 1] == 10) break;
            }
            string[] lines = Encoding.ASCII.GetString(head.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] first = lines[0].Split(' ');
            if (first.Length < 2) return false;
            method = first[0];
            target = first[1];
            foreach (string line in lines)
            {
                int colon = line.IndexOf(':');
                if (colon > 0) headers[line.Substring(0, colon).Trim()] = line.Substring(colon + 1).Trim();
            }
            string length;
            int size;
            if (headers.TryGetValue("Content-Length", out length) && int.TryParse(length, out size) && size > 0)
            {
                if (size > 262144) return false;
                byte[] bytes = new byte[size];
                int got = 0;
                while (got < size)
                {
                    int more = from.Read(bytes, got, size - got);
                    if (more <= 0) return false;
                    got += more;
                }
                body = Encoding.UTF8.GetString(bytes);
            }
            return true;
        }

        internal static void Write(Stream to, int status, string contentType, string body, params string[] headerPairs)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(body ?? "");
            StringBuilder head = new StringBuilder();
            head.Append("HTTP/1.1 ").Append(status).Append(' ').Append(status == 200 ? "OK" : status == 201 ? "Created" : status == 202 ? "Accepted" : status == 302 ? "Found" : status == 401 ? "Unauthorized" : status == 404 ? "Not Found" : "Error").Append("\r\n");
            head.Append("Content-Type: ").Append(contentType).Append("\r\nContent-Length: ").Append(bytes.Length).Append("\r\nConnection: close\r\n");
            for (int i = 0; i + 1 < headerPairs.Length; i += 2) head.Append(headerPairs[i]).Append(": ").Append(headerPairs[i + 1]).Append("\r\n");
            head.Append("\r\n");
            byte[] top = Encoding.ASCII.GetBytes(head.ToString());
            to.Write(top, 0, top.Length);
            to.Write(bytes, 0, bytes.Length);
            to.Flush();
        }

        internal static Dictionary<string, string> Query(string target)
        {
            Dictionary<string, string> found = new Dictionary<string, string>(StringComparer.Ordinal);
            int mark = (target ?? "").IndexOf('?');
            if (mark < 0) return found;
            foreach (string pair in target.Substring(mark + 1).Split('&'))
            {
                if (pair.Length == 0) continue;
                int equals = pair.IndexOf('=');
                string key = equals < 0 ? pair : pair.Substring(0, equals);
                string value = equals < 0 ? "" : pair.Substring(equals + 1);
                try { found[Uri.UnescapeDataString(key.Replace('+', ' '))] = Uri.UnescapeDataString(value.Replace('+', ' ')); }
                catch (UriFormatException) { }
            }
            return found;
        }
    }

    internal static class McpSignIn
    {
        private const int Quick = 20000, ForUser = 300000, Skew = 30;
        private const string Page = "<!doctype html><html><head><meta charset=\"utf-8\"><title>Capri</title></head><body style=\"font-family:sans-serif;text-align:center;margin-top:20vh\"><h2>{0}</h2></body></html>";

        private sealed class Grant
        {
            internal string Client, Auth, Token, Resource, Url, Access, Refresh;
            internal long Expires;
        }

        private static readonly object gate = new object(), renewing = new object();
        private static readonly Dictionary<string, Grant> grants = new Dictionary<string, Grant>(StringComparer.Ordinal);
        private static readonly HashSet<string> read = new HashSet<string>(StringComparer.Ordinal);

        private static bool Persists
        {
            get
            {
#if CROSS
                return false;
#else
                return true;
#endif
            }
        }

        internal static bool Keeps { get { return Persists; } }

        private static long Now() { return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds; }

        private static string FileFor(string id)
        {
            string at = Plugins.Folder;
            return at == null ? null : Path.Combine(at, id + ".signin.json");
        }

        private static void Load(string id)
        {
            lock (gate)
            {
                if (!read.Add(id) || !Persists) return;
                string path = FileFor(id);
                try
                {
                    if (path == null || !File.Exists(path)) return;
                    Dictionary<string, object> servers = PluginJson.Map(PluginJson.Get(PluginJson.Parse(File.ReadAllText(path)), "servers"));
                    if (servers == null) return;
                    foreach (KeyValuePair<string, object> pair in servers)
                    {
                        Grant one = new Grant { Client = PluginJson.Str(pair.Value, "client"), Auth = PluginJson.Str(pair.Value, "auth"), Token = PluginJson.Str(pair.Value, "token"), Resource = PluginJson.Str(pair.Value, "resource"), Url = PluginJson.Str(pair.Value, "url") };
                        object expires = PluginJson.Get(pair.Value, "expires");
                        if (expires is double) one.Expires = (long)(double)expires;
                        one.Access = Voice.Unprotect(PluginJson.Str(pair.Value, "access"));
                        one.Refresh = Voice.Unprotect(PluginJson.Str(pair.Value, "refresh"));
                        grants[id + "/" + pair.Key] = one;
                    }
                }
                catch (FormatException) { }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static void Save(string id)
        {
            if (!Persists) return;
            string path = FileFor(id);
            if (path == null) return;
            Dictionary<string, object> servers = PluginJson.Make();
            lock (gate)
                foreach (KeyValuePair<string, Grant> pair in grants)
                {
                    if (!pair.Key.StartsWith(id + "/", StringComparison.Ordinal)) continue;
                    Grant one = pair.Value;
                    Dictionary<string, object> saved = PluginJson.Make("client", one.Client, "auth", one.Auth, "token", one.Token, "resource", one.Resource, "url", one.Url, "expires", (double)one.Expires);
                    try
                    {
                        if (one.Access != null) saved["access"] = Voice.Protect(one.Access);
                        if (one.Refresh != null) saved["refresh"] = Voice.Protect(one.Refresh);
                    }
                    catch (CryptographicException) { continue; }
                    servers[pair.Key.Substring(id.Length + 1)] = saved;
                }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, PluginJson.Write(PluginJson.Make("servers", servers)), new UTF8Encoding(false));
            }
            catch (IOException error) { Brain.Log("plugins", -1, error.Message); }
            catch (UnauthorizedAccessException error) { Brain.Log("plugins", -1, error.Message); }
        }

        private static Grant Find(string id, string server, string url)
        {
            Load(id);
            lock (gate)
            {
                Grant one;
                return grants.TryGetValue(id + "/" + server, out one) && one.Url != null && string.Equals(one.Url, url, StringComparison.Ordinal) ? one : null;
            }
        }

        internal static bool Has(PluginInfo plugin)
        {
            if (plugin == null) return false;
            foreach (PluginServer spec in plugin.Servers)
            {
                if (!spec.Online) continue;
                Grant one = Find(plugin.Id, spec.Name, spec.Url);
                if (one == null) continue;
                lock (gate) if (one.Access != null || one.Refresh != null) return true;
            }
            return false;
        }

        internal static void Forget(string id)
        {
            lock (gate)
            {
                foreach (string key in new List<string>(grants.Keys)) if (key.StartsWith(id + "/", StringComparison.Ordinal)) grants.Remove(key);
                read.Add(id);
            }
            string path = FileFor(id);
            try { if (path != null && File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        internal static string Cached(string id, string server, string url)
        {
            Grant one = Find(id, server, url);
            lock (gate) return one == null ? null : one.Access;
        }

        internal static string Token(string id, string server, string url)
        {
            Grant one = Find(id, server, url);
            if (one == null) return null;
            lock (gate)
            {
                if (one.Access != null && one.Expires - Now() > Skew) return one.Access;
                if (one.Refresh == null) return null;
            }
            lock (renewing)
            {
                lock (gate) if (one.Access != null && one.Expires - Now() > Skew) return one.Access;
                return Renew(id, server, one);
            }
        }

        internal static string Refreshed(string id, string server, string url, string stale)
        {
            Grant one = Find(id, server, url);
            if (one == null) return null;
            lock (renewing)
            {
                lock (gate)
                {
                    if (one.Refresh == null) return null;
                    if (one.Access != null && one.Access != stale && one.Expires - Now() > Skew) return one.Access;
                }
                return Renew(id, server, one);
            }
        }

        private static string Renew(string id, string server, Grant one)
        {
            string refresh, client, token, resource;
            lock (gate) { refresh = one.Refresh; client = one.Client; token = one.Token; resource = one.Resource; }
            if (refresh == null || client == null || token == null) return null;
            try
            {
                int status;
                object answer = McpHttp.Post(token, "application/x-www-form-urlencoded", Form("grant_type", "refresh_token", "refresh_token", refresh, "client_id", client, "resource", resource), Quick, out status);
                string access = PluginJson.Str(answer, "access_token");
                if (status >= 200 && status <= 299 && access != null)
                {
                    Keep(one, answer, access);
                    Save(id);
                    return access;
                }
                if (status == 400 || status == 401)
                {
                    lock (gate) { one.Access = null; one.Refresh = null; }
                    Save(id);
                }
            }
            catch (McpException error) { Brain.Log("plugins", -1, id + "/" + server + ": " + error.Message); }
            return null;
        }

        private static void Keep(Grant one, object answer, string access)
        {
            object seconds = PluginJson.Get(answer, "expires_in");
            string refresh = PluginJson.Str(answer, "refresh_token");
            lock (gate)
            {
                one.Access = access;
                if (refresh != null) one.Refresh = refresh;
                one.Expires = Now() + (seconds is double && (double)seconds > 0 ? (long)(double)seconds : 3600);
            }
        }

        private static string Form(params string[] pairs)
        {
            StringBuilder made = new StringBuilder();
            for (int i = 0; i + 1 < pairs.Length; i += 2)
            {
                if (pairs[i + 1] == null) continue;
                if (made.Length > 0) made.Append('&');
                made.Append(Uri.EscapeDataString(pairs[i])).Append('=').Append(Uri.EscapeDataString(pairs[i + 1]));
            }
            return made.ToString();
        }

        private static string Secret(int bytes)
        {
            byte[] made = new byte[bytes];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create()) random.GetBytes(made);
            return Base64Url(made);
        }

        private static string Base64Url(byte[] bytes) { return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }

        internal static string Challenge(string verifier)
        {
            using (SHA256 hash = SHA256.Create()) return Base64Url(hash.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
        }

        private static object Fetch(string url)
        {
            try { return McpHttp.Allowed(url) ? McpHttp.Json(url, null, Quick) : null; }
            catch (McpException) { return null; }
        }

        internal static bool Run(PluginInfo plugin, PluginServer spec)
        {
            string host = new Uri(spec.Url).Host;
            Dictionary<string, string> sent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> pair in spec.Headers) sent[pair.Key] = McpServer.Expand(pair.Value, plugin.Root);
            string challenge;
            byte[] probe = new UTF8Encoding(false).GetBytes(PluginJson.Write(PluginJson.Make("jsonrpc", "2.0", "id", 1, "method", "initialize", "params", McpLink.InitializeParams())));
            using (HttpWebResponse response = McpHttp.Send("POST", spec.Url, sent, "application/json", "application/json, text/event-stream", probe, Quick))
            {
                int status = (int)response.StatusCode;
                if (status >= 200 && status <= 299) return false;
                if (status != 401) throw new McpException(host + " answered HTTP " + status + " when Capri tried to sign in.");
                challenge = response.Headers["WWW-Authenticate"] ?? "";
            }
            Uri server = new Uri(spec.Url);
            string origin = server.GetLeftPart(UriPartial.Authority);
            string canonical = origin + server.AbsolutePath;
            Match metadataAt = Regex.Match(challenge, "resource_metadata=\"?([^\",\\s]+)\"?", RegexOptions.IgnoreCase);
            Match scopeAt = Regex.Match(challenge, "scope=\"([^\"]+)\"", RegexOptions.IgnoreCase);
            List<string> places = new List<string>();
            if (metadataAt.Success) places.Add(metadataAt.Groups[1].Value);
            if (server.AbsolutePath.Length > 1) places.Add(origin + "/.well-known/oauth-protected-resource" + server.AbsolutePath.TrimEnd('/'));
            places.Add(origin + "/.well-known/oauth-protected-resource");
            object resourceInfo = null;
            foreach (string place in places)
            {
                object found = Fetch(place);
                if (PluginJson.List(PluginJson.Get(found, "authorization_servers")) != null) { resourceInfo = found; break; }
            }
            string resource = PluginJson.Str(resourceInfo, "resource");
            if (resource == null || !McpHttp.Allowed(resource)) resource = canonical;
            List<string> issuers = new List<string>();
            foreach (object one in PluginJson.List(PluginJson.Get(resourceInfo, "authorization_servers")) ?? new List<object>()) { string text = one as string; if (text != null && McpHttp.Allowed(text)) issuers.Add(text); }
            if (issuers.Count == 0) issuers.Add(origin);

            object meta = null;
            foreach (string issuer in issuers)
            {
                Uri at = new Uri(issuer);
                string top = at.GetLeftPart(UriPartial.Authority), path = at.AbsolutePath.TrimEnd('/');
                List<string> tries = new List<string>();
                tries.Add(top + "/.well-known/oauth-authorization-server" + path);
                tries.Add(top + "/.well-known/openid-configuration" + path);
                if (path.Length > 0) tries.Add(issuer.TrimEnd('/') + "/.well-known/openid-configuration");
                foreach (string place in tries)
                {
                    object found = Fetch(place);
                    string auth = PluginJson.Str(found, "authorization_endpoint"), token = PluginJson.Str(found, "token_endpoint");
                    if (auth == null || token == null || !McpHttp.Allowed(auth) || !McpHttp.Allowed(token)) continue;
                    List<object> methods = PluginJson.List(PluginJson.Get(found, "code_challenge_methods_supported"));
                    if (methods != null && !methods.Contains("S256")) continue;
                    meta = found;
                    break;
                }
                if (meta != null) break;
            }
            if (meta == null) throw new McpException("Capri couldn't find how to sign in to " + host + ".");
            string registration = PluginJson.Str(meta, "registration_endpoint");
            if (registration == null || !McpHttp.Allowed(registration)) throw new McpException(host + " doesn't let apps like Capri sign in by themselves yet.");
            string authorizeUrl = PluginJson.Str(meta, "authorization_endpoint"), tokenUrl = PluginJson.Str(meta, "token_endpoint");

            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                string redirect = "http://127.0.0.1:" + port + "/callback";
                int regStatus;
                object client = McpHttp.Post(registration, "application/json", PluginJson.Write(PluginJson.Make("client_name", "Capri", "redirect_uris", new List<object> { redirect }, "grant_types", new List<object> { "authorization_code", "refresh_token" }, "response_types", new List<object> { "code" }, "token_endpoint_auth_method", "none")), Quick, out regStatus);
                string clientId = PluginJson.Str(client, "client_id");
                if (regStatus < 200 || regStatus > 299 || clientId == null) throw new McpException(host + " wouldn't register Capri to sign in.");

                string verifier = Secret(48), state = Secret(18);
                string scope = scopeAt.Success ? scopeAt.Groups[1].Value : null;
                string url = authorizeUrl + (authorizeUrl.IndexOf('?') >= 0 ? "&" : "?") + Form("response_type", "code", "client_id", clientId, "redirect_uri", redirect, "code_challenge", Challenge(verifier), "code_challenge_method", "S256", "state", state, "resource", resource, "scope", scope);
                Action<string> open = Plugins.OpenBrowser;
                if (open == null) throw new McpException("Capri can't open your browser here.");
                try { open(url); }
                catch (Exception error) { throw new McpException("Capri couldn't open your browser: " + Plugins.Line(error.Message, 120)); }

                string code = Callback(listener, state, DateTime.UtcNow.AddMilliseconds(ForUser));
                int status;
                object tokens = McpHttp.Post(tokenUrl, "application/x-www-form-urlencoded", Form("grant_type", "authorization_code", "code", code, "redirect_uri", redirect, "client_id", clientId, "code_verifier", verifier, "resource", resource), Quick, out status);
                string access = PluginJson.Str(tokens, "access_token");
                if (status < 200 || status > 299 || access == null) throw new McpException(host + " didn't let Capri in" + (PluginJson.Str(tokens, "error") == null ? "." : " (" + Plugins.Line(PluginJson.Str(tokens, "error"), 60) + ")."));
                Grant made = new Grant { Client = clientId, Auth = authorizeUrl, Token = tokenUrl, Resource = resource, Url = spec.Url };
                Load(plugin.Id);
                Keep(made, tokens, access);
                lock (gate) grants[plugin.Id + "/" + spec.Name] = made;
                Save(plugin.Id);
                return true;
            }
            finally { listener.Stop(); }
        }

        private static string Callback(TcpListener listener, string state, DateTime until)
        {
            while (true)
            {
                if (DateTime.UtcNow > until) throw new McpException("Signing in took too long, so Capri stopped waiting. Try again when you're ready.");
                if (!listener.Pending()) { Thread.Sleep(50); continue; }
                using (TcpClient client = listener.AcceptTcpClient())
                {
                    client.ReceiveTimeout = 5000;
                    client.SendTimeout = 5000;
                    try
                    {
                        NetworkStream stream = client.GetStream();
                        string method, target, body;
                        Dictionary<string, string> headers;
                        if (!McpTiny.Read(stream, out method, out target, out headers, out body)) continue;
                        string host;
                        bool local = headers.TryGetValue("Host", out host) && (host.StartsWith("127.0.0.1", StringComparison.Ordinal) || host.StartsWith("localhost", StringComparison.OrdinalIgnoreCase));
                        if (method != "GET" || !local || !(target == "/callback" || target.StartsWith("/callback?", StringComparison.Ordinal)))
                        {
                            McpTiny.Write(stream, 404, "text/plain", "Not here.");
                            continue;
                        }
                        Dictionary<string, string> query = McpTiny.Query(target);
                        string given;
                        if (!query.TryGetValue("state", out given) || given != state)
                        {
                            McpTiny.Write(stream, 400, "text/html; charset=utf-8", string.Format(Page, "That wasn't from the sign-in Capri started."));
                            continue;
                        }
                        string error, code;
                        if (query.TryGetValue("error", out error))
                        {
                            McpTiny.Write(stream, 200, "text/html; charset=utf-8", string.Format(Page, "Capri wasn't signed in. You can close this tab."));
                            throw new McpException("The sign-in was turned down" + (error.Length == 0 ? "." : " (" + Plugins.Line(error, 60) + ")."));
                        }
                        if (!query.TryGetValue("code", out code) || code.Length == 0)
                        {
                            McpTiny.Write(stream, 400, "text/html; charset=utf-8", string.Format(Page, "Something was missing from that sign-in."));
                            continue;
                        }
                        McpTiny.Write(stream, 200, "text/html; charset=utf-8", string.Format(Page, "You're signed in. You can close this tab."));
                        return code;
                    }
                    catch (IOException) { }
                    catch (SocketException) { }
                    catch (ObjectDisposedException) { }
                }
            }
        }

        internal static void Open(string url)
        {
            if (!url.StartsWith("https://", StringComparison.Ordinal) && !url.StartsWith("http://", StringComparison.Ordinal)) throw new McpException("That isn't a web address.");
#if CROSS
            ProcessStartInfo info = new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open") { UseShellExecute = false };
            info.ArgumentList.Add(url);
            using (Process.Start(info)) { }
#else
            using (Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })) { }
#endif
        }
    }
}

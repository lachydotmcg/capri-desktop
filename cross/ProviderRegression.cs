using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using Avalonia.Threading;

namespace CapriDesktop
{
    internal static class ProviderRegression
    {
        // A plain socket server: HttpListener takes about 35 s to answer its first request on GitHub's macOS runners.
        private sealed class Server : IDisposable
        {
            internal readonly TcpListener Listener = new TcpListener(IPAddress.Loopback, 0);
            internal readonly ManualResetEventSlim Received = new ManualResetEventSlim(), Release = new ManualResetEventSlim();
            internal readonly List<string> Requests = new List<string>();
            internal readonly List<string> UserAgents = new List<string>();
            internal readonly string Address;
            internal volatile bool Hold, Error, Redirect, StreamError, Retry;
            internal string Destination, Model = "alpha";
            internal bool EmptyModels;
            internal Server()
            {
                Listener.Start();
                Address = "http://127.0.0.1:" + ((IPEndPoint)Listener.LocalEndpoint).Port;
                new Thread(Serve) { IsBackground = true }.Start();
            }
            private void Serve()
            {
                while (true)
                {
                    TcpClient client; try { client = Listener.AcceptTcpClient(); } catch { return; }
                    try { using (client) Answer(client.GetStream()); } catch { }
                }
            }
            private void Answer(NetworkStream stream)
            {
                List<byte> read = new List<byte>();
                byte[] chunk = new byte[8192];
                int end = -1;
                while (end < 0)
                {
                    int got = stream.Read(chunk, 0, chunk.Length);
                    if (got <= 0) return;
                    for (int i = 0; i < got; i++) read.Add(chunk[i]);
                    for (int i = 3; i < read.Count && end < 0; i++) if (read[i - 3] == 13 && read[i - 2] == 10 && read[i - 1] == 13 && read[i] == 10) end = i + 1;
                }
                string head = Encoding.ASCII.GetString(read.ToArray(), 0, end);
                string[] lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
                string[] first = lines[0].Split(' ');
                string url = first.Length > 1 ? first[1] : "/", authorization = null, session = null, userAgent = null;
                int length = 0;
                bool chunked = false;
                foreach (string line in lines)
                {
                    int colon = line.IndexOf(':');
                    if (colon <= 0) continue;
                    string name = line.Substring(0, colon).Trim(), value = line.Substring(colon + 1).Trim();
                    if (name.Equals("Authorization", StringComparison.OrdinalIgnoreCase)) authorization = value;
                    else if (name.Equals("x-opencode-session", StringComparison.OrdinalIgnoreCase)) session = value;
                    else if (name.Equals("User-Agent", StringComparison.OrdinalIgnoreCase)) userAgent = value;
                    else if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) int.TryParse(value, out length);
                    else if (name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) chunked = value.IndexOf("chunked", StringComparison.OrdinalIgnoreCase) >= 0;
                }
                while (chunked ? !Encoding.ASCII.GetString(read.ToArray(), end, read.Count - end).Contains("0\r\n\r\n") : read.Count - end < length)
                {
                    int got = stream.Read(chunk, 0, chunk.Length);
                    if (got <= 0) break;
                    for (int i = 0; i < got; i++) read.Add(chunk[i]);
                }
                lock (Requests)
                {
                    Requests.Add(url + " | " + (authorization ?? "<none>") + " | " + (session ?? "<none>"));
                    UserAgents.Add(userAgent);
                }
                Received.Set(); if (Hold) Release.Wait(TimeSpan.FromSeconds(8));
                bool chat = url.EndsWith("/chat/completions");
                bool openCode = authorization != null && authorization.StartsWith("Bearer oc_sk_", StringComparison.Ordinal);
                string body = Error && chat ? "{\"error\":{\"message\":\"Invalid credential opaque-provider-key-42\"}}"
                    : chat ? "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"OK\"},\"finish_reason\":\"stop\"}]}"
                    : openCode ? "{\"data\":[{\"id\":\"alpha\",\"created\":1,\"type\":\"chat\",\"modalities\":{\"input\":[\"text\"],\"output\":[\"text\"]},\"capabilities\":{\"tools\":{\"supported\":true}}}]}"
                    : EmptyModels ? "{\"data\":[]}" : "{\"data\":[{\"id\":\"" + Model + "\"}]}";
                if (chat && Retry) body = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"\",\"reasoning_content\":\"thinking\"},\"finish_reason\":\"length\"}]}";
                string type = "application/json";
                if (chat && StreamError)
                {
                    body = "data: {\"error\":{\"message\":\"Invalid credential opaque-provider-key-42\"}}\n\n";
                    type = "text/event-stream";
                }
                string status = Redirect ? "302 Found" : Error && chat ? "400 Bad Request" : "200 OK";
                byte[] bytes = Encoding.UTF8.GetBytes(body);
                string reply = "HTTP/1.1 " + status + "\r\nContent-Type: " + type + "\r\nContent-Length: " + bytes.Length + "\r\nConnection: close\r\n"
                    + (Redirect ? "Location: " + Destination + "\r\n" : "") + "\r\n";
                byte[] top = Encoding.ASCII.GetBytes(reply);
                stream.Write(top, 0, top.Length);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();
            }
            internal bool Saw(string value) { lock (Requests) return Requests.Exists(x => x.Contains(value)); }
            public void Dispose() { Release.Set(); Listener.Stop(); }
        }
        private static bool Wait(Func<bool> ready, int ms = 3000)
        {
            var until = DateTime.UtcNow.AddMilliseconds(ms);
            while (!ready() && DateTime.UtcNow < until) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
            return ready();
        }
        internal static void OpenCodeGo(List<string> failures)
        {
        string goWas = Brain.OpenCode;
        try
        {
            using (Server go = new Server())
            {
                Brain.OpenCode = go.Address;
                string goKey = "oc_sk_fixture_0123456789abcdef";
                List<string> goModels = Brain.CloudModels(goKey);
                Brain goBrain = new Brain { Model = "alpha", Remote = new Cloud(goKey, "alpha") };
                string Chat(Brain brain) => brain.Think("alpha", new List<ChatMessage> { new ChatMessage("user", "hello test") }, false, new List<KeyValuePair<string, ToolArguments>>(), null, 90, -1, false);
                if (Chat(goBrain) != "OK" || Chat(goBrain) != "OK") failures.Add("OpenCode Go chat replies");
                Brain another = new Brain { Model = "alpha", Remote = goBrain.Remote };
                Chat(another);
                goBrain.Forget();
                Chat(goBrain);
                string nextKey = "oc_sk_second_0123456789abcdef";
                goBrain.Remote = new Cloud(nextKey, "alpha");
                Chat(goBrain);
                goBrain.Remote = new Cloud(goKey, "alpha");
                Chat(goBrain);

                string legacyKey = "sk-" + new string('a', 64);
                string[] legacyModels = Brain.ServerModels(go.Address + "/openai", legacyKey);
                goBrain.Remote = null;
                goBrain.SetServer(go.Address, legacyKey);
                Chat(goBrain);
                Chat(goBrain);
                goBrain.SetServer(go.Address, nextKey);
                Chat(goBrain);
                goBrain.Think("alpha", new List<ChatMessage> { new ChatMessage("user", "one-shot job") }, false, new List<KeyValuePair<string, ToolArguments>>(), job: true);
                Chat(goBrain);
                string[] requests;
                lock (go.Requests) requests = go.Requests.ToArray();
                var sessions = new List<string>();
                foreach (string request in requests)
                    sessions.Add(request.Substring(request.LastIndexOf(" | ", StringComparison.Ordinal) + 3));
                if (goModels.Count != 1 || goModels[0] != "alpha" || legacyModels.Length != 1 || legacyModels[0] != "alpha"
                    || requests.Length != 13
                    || go.UserAgents.Count != requests.Length || go.UserAgents.Exists(value => value != "Capri-Desktop")
                    || !requests[0].StartsWith("/v1/models | Bearer " + goKey + " | ses_", StringComparison.Ordinal)
                    || !requests[1].StartsWith("/openai/v1/chat/completions | Bearer " + goKey + " | ses_", StringComparison.Ordinal)
                    || !requests[5].StartsWith("/openai/v1/chat/completions | Bearer " + nextKey + " | ses_", StringComparison.Ordinal)
                    || !requests[7].StartsWith("/v1/models | Bearer " + legacyKey + " | ses_", StringComparison.Ordinal)
                    || !requests[8].StartsWith("/openai/v1/chat/completions | Bearer " + legacyKey + " | ses_", StringComparison.Ordinal)
                    || sessions[1] != sessions[2] || sessions[8] != sessions[9]
                    || sessions[10] != sessions[12]
                    || sessions.Exists(id => !System.Text.RegularExpressions.Regex.IsMatch(id, @"\Ases_[0-9a-f]{32}\z"))
                    || new HashSet<string>(sessions).Count != 10)
                    failures.Add("OpenCode Go catalog, chat routing or conversation and credential session isolation");
            }
        }
        finally { Brain.OpenCode = goWas; }
        }

        internal static void Run(Pet pet, string folder)
        {
            const string key = "opaque-provider-key-42";
            List<string> failures = new List<string>();
            string oldServer = pet.Brain.Server, oldKey = pet.Brain.ServerKey, oldModel = pet.Brain.Model;
            string oldSavedServer = pet.Saved.BrainServer, oldSavedModel = pet.Saved.ServerModel, oldLog = Brain.LogPath;
            Cloud oldCloud = pet.Brain.Remote;
            string secret = Path.Combine(folder, "brain-server"), log = Path.Combine(folder, "provider-probe-errors.txt");
            byte[] oldSecret = File.Exists(secret) ? File.ReadAllBytes(secret) : null;
            UnixFileMode? oldMode = oldSecret != null && !OperatingSystem.IsWindows() ? File.GetUnixFileMode(secret) : null;
            try
            {
                pet.Brain.Remote = null;
                Brain pair = new Brain();
                string mismatch = null;
                Thread writer = new Thread((ThreadStart)delegate { for (int i = 0; i < 20000; i++) pair.SetServer("http://p" + (i & 1), "key-" + (i & 1)); });
                Thread reader = new Thread((ThreadStart)delegate
                {
                    for (int i = 0; i < 20000; i++)
                    {
                        string address, key;
                        pair.GetServer(out address, out key);
                        if (address == null) continue;
                        int index = address.EndsWith("p0", StringComparison.Ordinal) ? 0 : address.EndsWith("p1", StringComparison.Ordinal) ? 1 : -1;
                        if (index < 0 || key != "key-" + index) mismatch = address + "|" + key;
                    }
                });
                writer.Start(); reader.Start(); writer.Join(); reader.Join();
                if (mismatch != null) failures.Add("S3 server/key pair not atomic: " + mismatch);
                OpenCodeGo(failures);
                using (Server older = new Server()) using (Server newer = new Server())
                {
                    Brain live = new Brain { Model = "alpha" };
                    live.SetServer(older.Address, "key-A");
                    older.Hold = true;
                    var before = System.Threading.Tasks.Task.Run(() => live.Ask("hello test"));
                    bool beforeSeen = older.Received.Wait(TimeSpan.FromSeconds(5));
                    live.SetServer(newer.Address, "key-B");
                    var after = System.Threading.Tasks.Task.Run(() => live.Ask("hello test"));
                    bool afterSeen = newer.Received.Wait(TimeSpan.FromSeconds(5));
                    older.Release.Set();
                    bool completed = false;
                    try { completed = System.Threading.Tasks.Task.WaitAll(new System.Threading.Tasks.Task[] { before, after }, TimeSpan.FromSeconds(8)); }
                    catch (AggregateException e) { failures.Add("S3 concurrent chat failed: " + e.GetBaseException().Message); }
                    if (!beforeSeen || !afterSeen || !completed
                        || !older.Saw("/v1/chat/completions | Bearer key-A") || older.Saw("Bearer key-B")
                        || !newer.Saw("/v1/chat/completions | Bearer key-B") || newer.Saw("Bearer key-A"))
                        failures.Add("S3 concurrent chats crossed provider keys");
                }
                using (Server a = new Server()) using (Server b = new Server())
                {
                    pet.UseServer(a.Address + "/provider", key);
                    if (!Wait(() => pet.Brain.Server == a.Address + "/provider")) failures.Add("S1 keyed loopback prefix rejected");
                    pet.UseServer(a.Address);
                    if (!Wait(() => pet.Brain.Server == a.Address && pet.Brain.ServerKey == null)) failures.Add("S2 unkeyed server not selected");
                    if (pet.Talk.Reply != "I'm thinking with alpha on your server now.") failures.Add("S2 unkeyed success message changed");
                    pet.UseServer(a.Address, key);
                    if (!Wait(() => pet.Brain.Server == a.Address && pet.Brain.ServerKey == key)) failures.Add("S1 keyed server not selected");
                    if (!a.Saw("/v1/models | Bearer " + key)) failures.Add("S1 model discovery omitted bearer");
                    string[] pairLines = File.Exists(secret) ? File.ReadAllText(secret).Split(new[] { '\n' }, StringSplitOptions.None) : new string[0];
                    if (pairLines.Length < 2 || pairLines[0] != a.Address || pairLines[1] != key || (!OperatingSystem.IsWindows() && File.GetUnixFileMode(secret) != (UnixFileMode.UserRead | UnixFileMode.UserWrite))) failures.Add("S3 pair file");
                    pet.ShowServer(new[] { "alpha" });
                    if (!pet.BrainMenu.Contains("with a key") || !pet.BrainMenu.Contains("Change provider address with a key") || pet.BrainMenu.Contains(key) || (pet.Talk.Reply ?? "").Contains(key)) failures.Add("S3 key status");
                    a.Received.Reset(); a.Hold = true;
                    pet.UseServer(a.Address, "key-C");
                    if (!Wait(() => a.Received.IsSet)) failures.Add("S3 forget setup: discovery not held");
                    typeof(Pet).GetMethod("ForgetServerKey", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(pet, null);
                    a.Release.Set(); a.Hold = false;
                    Thread.Sleep(300);
                    if (!Wait(() => pet.Brain.ServerKey == null)) failures.Add("S3 forgotten key restored");
                    if (File.Exists(secret) && File.ReadAllText(secret).Contains("key-C")) failures.Add("S3 forgotten key persisted");
                    using (Server c = new Server())
                    {
                        c.Retry = true; c.Hold = true;
                        Brain live = new Brain { Model = "alpha" };
                        live.SetServer(c.Address, "key-D");
                        System.Threading.Tasks.Task.Run(() =>
                        {
                            try { live.Complete(c.Address, "alpha", new List<ChatMessage> { new ChatMessage("user", "hello test") }, false, new List<KeyValuePair<string, ToolArguments>>(), null, 90, -1, false, null, false, "alpha", "key-D"); }
                            catch (Exception) { }
                        });
                        if (!Wait(() => c.Received.IsSet)) failures.Add("S3 forget-chat setup: chat not held");
                        live.SetServer(c.Address, null);
                        c.Release.Set(); c.Hold = false;
                        Thread.Sleep(500);
                        int chats;
                        lock (c.Requests) chats = c.Requests.FindAll(x => x.StartsWith("/v1/chat/completions", StringComparison.Ordinal)).Count;
                        if (chats != 1) failures.Add("S3 forgotten key retried a chat: " + string.Join(" | ", c.Requests.ToArray()));
                    }
                    a.Retry = false;
                    Brain.LogPath = log; a.Error = true;
                    try { new Brain { Server = a.Address, ServerKey = key, Model = "alpha" }.Ask("hello test"); }
                    catch (Exception e) { if (e.Message.Contains(key)) failures.Add("S3 error exposed key"); }
                    if (File.Exists(log) && File.ReadAllText(log).Contains(key)) failures.Add("S3 log exposed key");
                    a.Error = false; a.StreamError = true;
                    try { new Brain { Server = a.Address, ServerKey = key, Model = "alpha" }.Ask("hello test"); failures.Add("S3 streaming error accepted"); }
                    catch (BrainException e) { if (!e.Message.Contains("Invalid credential") || e.Message.Contains(key)) failures.Add("S3 streaming error exposed key"); }
                    if (File.Exists(log) && File.ReadAllText(log).Contains(key)) failures.Add("S3 streaming error logged key");
                    string uiLog = Path.Combine(folder, "errors.txt");
                    Brain.LogPath = uiLog;
                    pet.Brain.SetServer(a.Address, key);
                    pet.Hear("hello test");
                    // As on Windows, she says where the error is, and the provider's own text goes in the BRAIN note (119c02a).
                    if (!Wait(() => (pet.MainframeProblem ?? "").Contains("Invalid credential") && (pet.Talk.Reply ?? "").Contains("The error is under BRAIN"))) failures.Add("S3 streaming error not in the BRAIN note: " + pet.Talk.Reply + " | " + pet.MainframeProblem);
                    if ((pet.Talk.Reply ?? "").Contains(key) || (pet.MainframeProblem ?? "").Contains(key) || (File.Exists(uiLog) && File.ReadAllText(uiLog).Contains(key))) failures.Add("S3 streaming key reached UI or errors.txt");
                    Brain.LogPath = log;
                    a.StreamError = false; a.Redirect = true; a.Destination = b.Address + "/stolen";
                    try { Brain.ServerModels(a.Address, key); failures.Add("S3 model redirect accepted"); } catch (BrainException) { }
                    try { new Brain { Server = a.Address, ServerKey = key, Model = "alpha" }.Ask("hello test"); failures.Add("S3 chat redirect accepted"); } catch (BrainException) { }
                    if (b.Saw("Bearer")) failures.Add("S3 redirect leaked key");
                    a.Redirect = false;
                    TcpListener closed = new TcpListener(IPAddress.Loopback, 0); closed.Start(); int port = ((IPEndPoint)closed.LocalEndpoint).Port; closed.Stop();
                    string down = "http://127.0.0.1:" + port;
                    pet.UseServer(down);
                    if (!Wait(() => (pet.Talk.Reply ?? "").StartsWith("Can't reach a server at ")) || pet.Talk.Reply != "Can't reach a server at " + down + ". Is it running?") failures.Add("S2 local-server error changed");
                    pet.UseServer(a.Address, "key-A"); Wait(() => pet.Brain.Server == a.Address && pet.Brain.ServerKey == "key-A");
                    pet.Brain.SetServer(a.Address, "key-A"); pet.Brain.Model = "alpha";
                    a.Received.Reset(); a.Hold = true;
                    typeof(Pet).GetMethod("RefreshBrains", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(pet, null);
                    if (!Wait(() => a.Received.IsSet)) failures.Add("race setup: A did not receive discovery");
                    pet.Brain.SetServer(b.Address, "key-B");
                    a.Release.Set(); a.Hold = false;
                    if (!Wait(() => a.Saw("Bearer key-A"), 700)) failures.Add("race: A lost its own key");
                    if (a.Saw("Bearer key-B")) failures.Add("S3 provider B key sent to provider A");
                    if (b.Saw("Bearer")) failures.Add("race: B received a request");
                }
            }
            finally
            {
                pet.Brain.SetServer(oldServer, oldKey); pet.Brain.Model = oldModel; pet.Brain.Remote = oldCloud;
                pet.Saved.BrainServer = oldSavedServer; pet.Saved.ServerModel = oldSavedModel; Brain.LogPath = oldLog;
                if (oldSecret == null) { if (File.Exists(secret)) File.Delete(secret); }
                else { File.WriteAllBytes(secret, oldSecret); if (oldMode != null) File.SetUnixFileMode(secret, oldMode.Value); }
            }
            if (failures.Count > 0) throw new Exception(string.Join("; ", failures));
        }
        internal static void Named(Pet pet)
        {
            using Server home = new Server { Model = "home-model" };
            using Server work = new Server { Model = "work-model" };
            using Server replacement = new Server { Model = "replacement-model" };
            using Server pending = new Server { Hold = true };
            const string homeKey = "test-home-key", workKey = "test-work-key";
            string oldAddress = pet.Brain.Server, oldKey = pet.Brain.ServerKey, oldModel = pet.Brain.Model;
            string savedAddress = pet.Saved.BrainServer, savedProvider = pet.Saved.BrainProvider, savedModel = pet.Saved.ServerModel;
            int? oldTier = pet.Saved.BrainTier;
            Cloud oldCloud = pet.Brain.Remote;
            ProviderEndpoint[] endpoints = pet.Saved.ProviderEndpoints;
            ProviderModelPick[] models = pet.Saved.ProviderModels;
            string pair = Path.Combine(Program.Smoke, "brain-server");
            byte[] pairBytes = File.Exists(pair) ? File.ReadAllBytes(pair) : null;
            string first = null, second = null;
            try
            {
                void Change(string id, string name, string address, string key, bool connect, bool success)
                {
                    bool done = false;
                    string error = null;
                    pet.SaveNamedProvider(id, name, address, key, connect, result => { error = result; done = true; });
                    if (!Wait(() => done) || (error == null) != success) throw new Exception("Named provider change returned " + error);
                }

                Change(null, "Named home", home.Address, homeKey, false, true);
                first = Array.Find(pet.Saved.ProviderEndpoints, e => e.Name == "Named home").Id;
                Change(null, "Named work", work.Address, workKey, false, true);
                second = Array.Find(pet.Saved.ProviderEndpoints, e => e.Name == "Named work").Id;
                Change(first, "Named home", home.Address, "", true, true);
                if (pet.Brain.ServerKey != homeKey || !home.Saw("Bearer " + homeKey) || pet.MainframeModels[0] != home.Model)
                    throw new Exception("Named provider did not discover its own models with its saved key.");
                string homePath = pet.EndpointKeyFile(first);
                replacement.EmptyModels = true;
                Change(first, "Bad name", replacement.Address, "test-unusable-key", false, false);
                if (Pet.EndpointFor(pet.Saved, first).Name != "Named home" || pet.ReadEndpointKey(first) != homeKey
                    || pet.Brain.Server != home.Address || pet.Brain.Model != home.Model)
                    throw new Exception("Refused active edit replaced saved or live state.");

                string oneOff = null;
                pet.UseServer(pending.Address, "test-one-off-key", result => oneOff = result);
                if (!Wait(() => pending.Received.IsSet)) throw new Exception("One-off race did not reach the loopback fixture.");
                Change(second, "Named work", work.Address, null, true, true);
                pending.Release.Set();
                if (!Wait(() => oneOff == "Provider check cancelled.") || pet.Brain.Server != work.Address)
                    throw new Exception("Late one-off reply replaced the named provider.");
                pet.MainframePickListedModel(home.Model);
                if (pet.Brain.Model != work.Model || !work.Saw("Bearer " + workKey))
                    throw new Exception("Named provider allowed a different provider's model or key.");

                Change(first, "Named home", replacement.Address, "", false, true);
                if (pet.ReadEndpointKey(first) != null || File.Exists(homePath))
                    throw new Exception("Changed origin retained the former credential.");
                replacement.EmptyModels = false;
                Change(first, "Named home", replacement.Address, null, true, true);
                if (replacement.Saw("Bearer " + homeKey) || pet.Brain.ServerKey != null)
                    throw new Exception("Changed origin received the former credential.");

                oneOff = null;
                pet.UseServer(replacement.Address, "test-one-off-key", result => oneOff = result);
                if (!Wait(() => oneOff == "Provider connected.") || Pet.EndpointIdOf(pet.Saved.BrainProvider) != null ||
                    pet.Brain.ServerKey != "test-one-off-key" || pet.ReadEndpointKey(second) != workKey || pet.ReadEndpointKey(first) != null)
                    throw new Exception("One-off connection changed named credential state.");
            }
            finally
            {
                if (first != null) pet.SaveEndpointKey(first, null);
                if (second != null) pet.SaveEndpointKey(second, null);
                pet.Saved.ProviderEndpoints = endpoints;
                pet.Saved.ProviderModels = models;
                pet.Saved.BrainServer = savedAddress;
                pet.Saved.BrainProvider = savedProvider;
                pet.Saved.ServerModel = savedModel;
                pet.Saved.BrainTier = oldTier;
                pet.Brain.SetServer(oldAddress, oldKey);
                pet.Brain.Model = oldModel;
                pet.Brain.Remote = oldCloud;
                if (pairBytes == null) File.Delete(pair);
                else File.WriteAllBytes(pair, pairBytes);
            }
        }
    }
}

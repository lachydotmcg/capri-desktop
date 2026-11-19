using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CapriDesktop
{
    internal sealed class HerLink
    {
        internal string Text, Href;
        internal Rectangle Box;
    }

    internal sealed class HerBrowser : IDisposable
    {
        private sealed class Pending
        {
            internal readonly ManualResetEvent Done = new ManualResetEvent(false);
            internal Dictionary<string, object> Result;
        }

        internal static Func<string> FindBrowser = DefaultFindBrowser;
        internal static string SearchAddress = "https://www.bing.com/search?q=";

        private const int CallTimeout = 15000;

        private readonly string profile;
        private readonly int width, height;
        private readonly object sendLock = new object();
        private readonly object stateLock = new object();
        private readonly Dictionary<int, Pending> pending = new Dictionary<int, Pending>();
        private Process process;
        private ClientWebSocket socket;
        private Thread reader;
        private int nextId;
        private int loads;
        private volatile bool alive;
        private bool disposed;
        private string address = "";
        private string title = "";

        internal HerBrowser(string profileFolder, int width, int height)
        {
            profile = profileFolder;
            this.width = width < 100 ? 100 : width;
            this.height = height < 100 ? 100 : height;
        }

        internal bool Running
        {
            get
            {
                if (!alive || process == null) return false;
                try { return !process.HasExited; }
                catch (Exception) { return false; }
            }
        }

        internal string Address
        {
            get
            {
                if (Running)
                {
                    object value = Eval("location.href");
                    string text = value as string;
                    if (!string.IsNullOrEmpty(text)) lock (stateLock) address = text;
                }
                lock (stateLock) return address;
            }
        }

        internal string LastAddress { get { lock (stateLock) return address; } }

        internal string Title
        {
            get
            {
                if (Running)
                {
                    object value = Eval("document.title");
                    string text = value as string;
                    if (text != null) lock (stateLock) title = text;
                }
                lock (stateLock) return title;
            }
        }

        private static string DefaultFindBrowser()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return null;
            List<string> roots = new List<string>();
            roots.Add(Environment.GetEnvironmentVariable("ProgramFiles"));
            roots.Add(Environment.GetEnvironmentVariable("ProgramFiles(x86)"));
            roots.Add(Environment.GetEnvironmentVariable("LocalAppData"));
            string[] relative = { @"Microsoft\Edge\Application\msedge.exe", @"Google\Chrome\Application\chrome.exe" };
            foreach (string part in relative)
                foreach (string root in roots)
                {
                    if (string.IsNullOrEmpty(root)) continue;
                    try
                    {
                        string path = Path.Combine(root, part);
                        if (File.Exists(path)) return path;
                    }
                    catch (Exception) { }
                }
            return null;
        }

        internal bool Start(out string problem)
        {
            problem = null;
            if (Running) return true;
            string exe = null;
            try { exe = FindBrowser == null ? null : FindBrowser(); }
            catch (Exception) { }
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                problem = "I couldn't find Edge or Chrome on this PC.";
                return false;
            }
            try
            {
                Directory.CreateDirectory(profile);
                string portFile = Path.Combine(profile, "DevToolsActivePort");
                if (File.Exists(portFile)) File.Delete(portFile);
                StringBuilder args = new StringBuilder();
                // No --remote-allow-origins: the port then refuses any connection that carries a web page's Origin, so a site can't
                // drive her browser. Her own socket sends no Origin and still gets in (a tester's report, 19 Nov).
                args.Append("--headless=new --remote-debugging-port=0 ");
                args.Append("--user-data-dir=\"").Append(profile.TrimEnd('\\', '/')).Append("\" ");
                args.Append("--window-size=").Append(width).Append(',').Append(height).Append(' ');
                args.Append("--no-first-run --no-default-browser-check --disable-sync --disable-extensions --mute-audio ");
                if (Environment.OSVersion.Platform != PlatformID.Win32NT) args.Append("--no-sandbox --disable-gpu --disable-dev-shm-usage ");
                args.Append("about:blank");
                ProcessStartInfo info = new ProcessStartInfo(exe, args.ToString());
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                process = Process.Start(info);
                if (process == null) { problem = "I couldn't open my browser."; return false; }

                int port = 0;
                DateTime until = DateTime.UtcNow.AddSeconds(10);
                while (DateTime.UtcNow < until && port == 0)
                {
                    if (process.HasExited) break;
                    if (File.Exists(portFile))
                    {
                        try
                        {
                            string[] lines = File.ReadAllText(portFile).Split('\n');
                            int parsed;
                            if (lines.Length > 1 && int.TryParse(lines[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) port = parsed;
                        }
                        catch (Exception) { }
                    }
                    if (port == 0) Thread.Sleep(100);
                }
                if (port == 0) { problem = "My browser didn't start in time."; Dispose(); return false; }

                string socketUrl = null;
                until = DateTime.UtcNow.AddSeconds(8);
                while (DateTime.UtcNow < until && socketUrl == null)
                {
                    try
                    {
                        HttpWebRequest request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture) + "/json/list");
                        request.Proxy = null;
                        request.Timeout = 3000;
                        using (WebResponse response = request.GetResponse())
                        using (StreamReader body = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                        {
                            List<object> targets = HuskJson.List(HuskJson.Read(body.ReadToEnd()));
                            if (targets != null)
                                foreach (object item in targets)
                                {
                                    Dictionary<string, object> target = item as Dictionary<string, object>;
                                    if (target == null || HuskJson.Text(target, "type") != "page") continue;
                                    string url = HuskJson.Text(target, "webSocketDebuggerUrl");
                                    if (url.Length > 0) { socketUrl = url; break; }
                                }
                        }
                    }
                    catch (Exception) { }
                    if (socketUrl == null) Thread.Sleep(150);
                }
                if (socketUrl == null) { problem = "My browser started but wouldn't let me drive it."; Dispose(); return false; }

                socket = new ClientWebSocket();
                using (CancellationTokenSource cancel = new CancellationTokenSource(8000))
                    socket.ConnectAsync(new Uri(socketUrl), cancel.Token).Wait();
                alive = true;
                reader = new Thread(ReadLoop);
                reader.IsBackground = true;
                reader.Start();

                Send("Page.enable", null);
                Send("Runtime.enable", null);
                Send("Emulation.setDeviceMetricsOverride", HuskJson.Map("width", width, "height", height, "deviceScaleFactor", 1, "mobile", false));
                return true;
            }
            catch (Exception)
            {
                problem = "I couldn't start my browser.";
                try { Dispose(); } catch (Exception) { }
                return false;
            }
        }

        private void ReadLoop()
        {
            byte[] buffer = new byte[64 * 1024];
            try
            {
                while (alive && socket.State == WebSocketState.Open)
                {
                    MemoryStream message = new MemoryStream();
                    WebSocketReceiveResult received;
                    do
                    {
                        received = socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None).Result;
                        if (received.MessageType == WebSocketMessageType.Close) { alive = false; break; }
                        message.Write(buffer, 0, received.Count);
                    }
                    while (!received.EndOfMessage);
                    if (!alive) break;
                    Dispatch(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
                }
            }
            catch (Exception) { }
            alive = false;
            lock (stateLock)
            {
                foreach (Pending waiting in pending.Values) waiting.Done.Set();
            }
        }

        private void Dispatch(string text)
        {
            Dictionary<string, object> message;
            try { message = HuskJson.Read(text) as Dictionary<string, object>; }
            catch (Exception) { return; }
            if (message == null) return;
            object idValue = HuskJson.Get(message, "id");
            if (idValue != null)
            {
                int id = (int)Number(idValue);
                Pending waiting;
                lock (stateLock)
                {
                    if (!pending.TryGetValue(id, out waiting)) return;
                }
                if (HuskJson.Get(message, "error") == null) waiting.Result = HuskJson.Get(message, "result") as Dictionary<string, object> ?? new Dictionary<string, object>();
                waiting.Done.Set();
                return;
            }
            string method = HuskJson.Text(message, "method");
            Dictionary<string, object> args = HuskJson.Get(message, "params") as Dictionary<string, object>;
            if (method == "Page.loadEventFired") Interlocked.Increment(ref loads);
            else if (method == "Page.frameNavigated" && args != null)
            {
                Dictionary<string, object> frame = HuskJson.Get(args, "frame") as Dictionary<string, object>;
                if (frame != null && HuskJson.Get(frame, "parentId") == null)
                    lock (stateLock) address = HuskJson.Text(frame, "url");
            }
        }

        private static double Number(object value)
        {
            HuskNumber number = value as HuskNumber;
            if (number != null)
            {
                double parsed;
                return double.TryParse(number.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ? parsed : 0;
            }
            if (value is double) return (double)value;
            return 0;
        }

        private Dictionary<string, object> Send(string method, Dictionary<string, object> args)
        {
            return Send(method, args, CallTimeout);
        }

        private Dictionary<string, object> Send(string method, Dictionary<string, object> args, int timeout)
        {
            if (!alive || socket == null) return null;
            Pending waiting = new Pending();
            int id;
            try
            {
                lock (sendLock)
                {
                    id = Interlocked.Increment(ref nextId);
                    lock (stateLock) pending[id] = waiting;
                    string json = HuskJson.Write(HuskJson.Map("id", id, "method", method, "params", args ?? new Dictionary<string, object>()));
                    byte[] bytes = Encoding.UTF8.GetBytes(json);
                    using (CancellationTokenSource cancel = new CancellationTokenSource(timeout))
                        socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancel.Token).Wait();
                }
            }
            catch (Exception)
            {
                return null;
            }
            try
            {
                waiting.Done.WaitOne(timeout);
                return waiting.Result;
            }
            finally
            {
                lock (stateLock) pending.Remove(id);
                waiting.Done.Close();
            }
        }

        private object Eval(string expression)
        {
            Dictionary<string, object> result = Send("Runtime.evaluate", HuskJson.Map("expression", expression, "returnByValue", true, "awaitPromise", false));
            if (result == null || HuskJson.Get(result, "exceptionDetails") != null) return null;
            Dictionary<string, object> remote = HuskJson.Get(result, "result") as Dictionary<string, object>;
            return HuskJson.Get(remote, "value");
        }

        internal static string Resolve(string typed)
        {
            if (typed == null) return null;
            string text = typed.Trim();
            if (text.Length == 0) return null;
            bool spaced = text.IndexOfAny(new[] { ' ', '\t', '\r', '\n' }) >= 0;
            Match scheme = Regex.Match(text, @"^([A-Za-z][A-Za-z0-9+.\-]*):(.*)$", RegexOptions.Singleline);
            if (scheme.Success && !spaced)
            {
                string name = scheme.Groups[1].Value.ToLowerInvariant();
                string rest = scheme.Groups[2].Value;
                if (name == "http" || name == "https")
                {
                    if (rest.StartsWith("//") && rest.Length > 2) return text;
                    return Search(text);
                }
                if (!Regex.IsMatch(rest, @"^[0-9]+([/?#].*)?$")) return Search(text);
            }
            if (!spaced)
            {
                string host = Regex.Match(text, @"^[^/?#:]*").Value;
                if (Regex.IsMatch(host, @"^[A-Za-z0-9\-\.]+$"))
                {
                    string lower = host.ToLowerInvariant();
                    if (lower == "localhost" || Regex.IsMatch(lower, @"^[0-9]{1,3}(\.[0-9]{1,3}){3}$")) return "http://" + text;
                    if (Regex.IsMatch(lower, @"^[a-z0-9]([a-z0-9\-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9\-]*[a-z0-9])?)*\.[a-z]{2,}$")) return "https://" + text;
                }
            }
            return Search(text);
        }

        private static string Search(string query)
        {
            return SearchAddress + Uri.EscapeDataString(query);
        }

        private void WaitLoad(int before, int millis)
        {
            DateTime until = DateTime.UtcNow.AddMilliseconds(millis);
            DateTime ready = DateTime.UtcNow.AddMilliseconds(2500);
            while (DateTime.UtcNow < until && Running)
            {
                if (Volatile.Read(ref loads) != before) return;
                if (DateTime.UtcNow > ready)
                {
                    object state = Eval("document.readyState");
                    if ("complete".Equals(state)) return;
                    ready = DateTime.UtcNow.AddMilliseconds(500);
                }
                Thread.Sleep(50);
            }
        }

        internal void Go(string typed)
        {
            string url = Resolve(typed);
            if (url == null || !Running) return;
            int before = Volatile.Read(ref loads);
            Dictionary<string, object> result = Send("Page.navigate", HuskJson.Map("url", url));
            if (result == null) return;
            WaitLoad(before, 10000);
        }

        private void Walk(int step)
        {
            if (!Running) return;
            Dictionary<string, object> history = Send("Page.getNavigationHistory", null);
            if (history == null) return;
            int index = (int)Number(HuskJson.Get(history, "currentIndex")) + step;
            List<object> entries = HuskJson.List(HuskJson.Get(history, "entries"));
            if (entries == null || index < 0 || index >= entries.Count) return;
            Dictionary<string, object> entry = entries[index] as Dictionary<string, object>;
            if (entry == null) return;
            int before = Volatile.Read(ref loads);
            if (Send("Page.navigateToHistoryEntry", HuskJson.Map("entryId", (int)Number(HuskJson.Get(entry, "id")))) != null) WaitLoad(before, 10000);
        }

        internal void Back() { Walk(-1); }
        internal void Forward() { Walk(1); }

        internal void Reload()
        {
            if (!Running) return;
            int before = Volatile.Read(ref loads);
            if (Send("Page.reload", null) != null) WaitLoad(before, 10000);
        }

        internal byte[] Frame(int quality)
        {
            if (!Running) return null;
            if (quality < 1) quality = 1;
            if (quality > 100) quality = 100;
            Dictionary<string, object> result = Send("Page.captureScreenshot", HuskJson.Map("format", "jpeg", "quality", quality));
            string data = HuskJson.Text(result, "data");
            if (data.Length == 0) return null;
            try { return Convert.FromBase64String(data); }
            catch (Exception) { return null; }
        }

        internal string Text(int limit)
        {
            object value = Eval("document.body ? document.body.innerText : ''");
            string text = value as string;
            if (text == null) return "";
            text = Regex.Replace(text, @"\s+", " ").Trim();
            if (limit > 0 && text.Length > limit) text = text.Substring(0, limit);
            return text;
        }

        private const string Collect =
            "(function(limit){var out=[];var nodes=document.querySelectorAll('a[href],button,input[type=submit],input[type=button],[role=button],[role=link]');" +
            "for(var i=0;i<nodes.length&&out.length<limit;i++){var e=nodes[i];var r=e.getBoundingClientRect();" +
            "if(r.width<2||r.height<2||r.bottom<0||r.right<0||r.top>innerHeight||r.left>innerWidth)continue;" +
            "var s=getComputedStyle(e);if(s.visibility==='hidden'||s.display==='none'||parseFloat(s.opacity)===0)continue;" +
            "var t=(e.innerText||e.value||e.getAttribute('aria-label')||e.title||'').replace(/\\s+/g,' ').trim();" +
            "out.push({t:t.substring(0,80),h:e.href||'',x:r.left,y:r.top,w:r.width,b:r.height,top:r.top});}" +
            "out.sort(function(a,b){return a.top-b.top||a.x-b.x;});return JSON.stringify(out);})(";

        internal List<HerLink> Links(int limit)
        {
            List<HerLink> links = new List<HerLink>();
            string json = Eval(Collect + (limit < 1 ? 1 : limit).ToString(CultureInfo.InvariantCulture) + ")") as string;
            if (json == null) return links;
            try
            {
                List<object> items = HuskJson.List(HuskJson.Read(json));
                if (items == null) return links;
                foreach (object item in items)
                {
                    Dictionary<string, object> map = item as Dictionary<string, object>;
                    if (map == null) continue;
                    HerLink link = new HerLink();
                    link.Text = HuskJson.Text(map, "t");
                    link.Href = HuskJson.Text(map, "h");
                    link.Box = new Rectangle((int)Math.Round(Number(HuskJson.Get(map, "x"))), (int)Math.Round(Number(HuskJson.Get(map, "y"))), (int)Math.Round(Number(HuskJson.Get(map, "w"))), (int)Math.Round(Number(HuskJson.Get(map, "b"))));
                    links.Add(link);
                }
            }
            catch (Exception) { }
            return links;
        }

        private const string Seek =
            "(function(q){q=q.toLowerCase().trim();if(!q)return null;" +
            "var nodes=document.querySelectorAll('a[href],button,input,textarea,select,[role=button],[role=link],[onclick]');var exact=null,part=null;" +
            "for(var i=0;i<nodes.length;i++){var e=nodes[i];var r=e.getBoundingClientRect();if(r.width<2||r.height<2)continue;" +
            "var s=getComputedStyle(e);if(s.visibility==='hidden'||s.display==='none'||parseFloat(s.opacity)===0)continue;" +
            "var names=[e.innerText,e.value,e.getAttribute('aria-label'),e.getAttribute('placeholder'),e.title,e.getAttribute('name')];" +
            "for(var k=0;k<names.length;k++){var n=names[k];if(typeof n!=='string')continue;n=n.replace(/\\s+/g,' ').trim().toLowerCase();if(!n)continue;" +
            "if(n===q){if(!exact)exact=e;}else if(n.indexOf(q)>=0){if(!part)part=e;}}}" +
            "var f=exact||part;if(!f)return null;f.scrollIntoView({block:'center',inline:'center'});var b=f.getBoundingClientRect();" +
            "return JSON.stringify({x:b.left,y:b.top,w:b.width,h:b.height});})(";

        internal bool Find(string text, out Rectangle box)
        {
            box = Rectangle.Empty;
            if (string.IsNullOrEmpty(text) || !Running) return false;
            string script = Seek + HuskJson.Write(text) + ")";
            string json = Eval(script) as string;
            if (json == null) return false;
            Thread.Sleep(150);
            json = Eval(script) as string ?? json;
            try
            {
                Dictionary<string, object> map = HuskJson.Read(json) as Dictionary<string, object>;
                if (map == null) return false;
                box = new Rectangle((int)Math.Round(Number(HuskJson.Get(map, "x"))), (int)Math.Round(Number(HuskJson.Get(map, "y"))), (int)Math.Round(Number(HuskJson.Get(map, "w"))), (int)Math.Round(Number(HuskJson.Get(map, "h"))));
                return true;
            }
            catch (Exception) { return false; }
        }

        internal void Click(int x, int y)
        {
            if (!Running) return;
            Send("Input.dispatchMouseEvent", HuskJson.Map("type", "mouseMoved", "x", x, "y", y));
            int before = Volatile.Read(ref loads);
            Send("Input.dispatchMouseEvent", HuskJson.Map("type", "mousePressed", "x", x, "y", y, "button", "left", "clickCount", 1));
            Send("Input.dispatchMouseEvent", HuskJson.Map("type", "mouseReleased", "x", x, "y", y, "button", "left", "clickCount", 1));
            WaitLoad(before, 600);
        }

        internal void Scroll(int dy)
        {
            if (!Running) return;
            Send("Input.dispatchMouseEvent", HuskJson.Map("type", "mouseWheel", "x", width / 2, "y", height / 2, "deltaX", 0, "deltaY", dy));
        }

        internal void Type(string text)
        {
            if (string.IsNullOrEmpty(text) || !Running) return;
            Send("Input.insertText", HuskJson.Map("text", text));
        }

        internal void Key(string key)
        {
            if (string.IsNullOrEmpty(key) || !Running) return;
            string name = key;
            int code;
            string text = null;
            switch (key.ToLowerInvariant())
            {
                case "enter": name = "Enter"; code = 13; text = "\r"; break;
                case "backspace": name = "Backspace"; code = 8; break;
                case "tab": name = "Tab"; code = 9; break;
                case "escape": name = "Escape"; code = 27; break;
                case "delete": name = "Delete"; code = 46; break;
                case "arrowup": name = "ArrowUp"; code = 38; break;
                case "arrowdown": name = "ArrowDown"; code = 40; break;
                case "arrowleft": name = "ArrowLeft"; code = 37; break;
                case "arrowright": name = "ArrowRight"; code = 39; break;
                case "pagedown": name = "PageDown"; code = 34; break;
                case "pageup": name = "PageUp"; code = 33; break;
                case "home": name = "Home"; code = 36; break;
                case "end": name = "End"; code = 35; break;
                case " ": case "space": name = " "; code = 32; text = " "; break;
                default: return;
            }
            string physical = name == " " ? "Space" : name;
            Dictionary<string, object> down = HuskJson.Map("type", text != null ? "keyDown" : "rawKeyDown", "key", name, "code", physical, "windowsVirtualKeyCode", code, "nativeVirtualKeyCode", code);
            if (text != null) down["text"] = text;
            int before = Volatile.Read(ref loads);
            Send("Input.dispatchKeyEvent", down);
            Send("Input.dispatchKeyEvent", HuskJson.Map("type", "keyUp", "key", name, "code", physical, "windowsVirtualKeyCode", code, "nativeVirtualKeyCode", code));
            if (name == "Enter") WaitLoad(before, 600);
        }

        public void Dispose()
        {
            try
            {
                if (disposed) return;
                disposed = true;
                if (alive) Send("Browser.close", null, 2000);
                Process running = process;
                if (running != null)
                {
                    try
                    {
                        if (!running.WaitForExit(3000)) running.Kill();
                        running.WaitForExit(2000);
                    }
                    catch (Exception) { }
                }
                alive = false;
                try { if (socket != null) socket.Abort(); }
                catch (Exception) { }
                try { if (socket != null) socket.Dispose(); }
                catch (Exception) { }
                try { if (running != null) running.Dispose(); }
                catch (Exception) { }
            }
            catch (Exception) { }
        }
    }
}

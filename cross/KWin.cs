using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Tmds.DBus.Protocol;

namespace CapriDesktop
{
    // One window as KWin last told her: its id is KWin's internalId, given a small number of hers so it fits a Spot.
    internal sealed class KWinWindow
    {
        internal long Id;
        internal string Key;
        internal int Pid;
        internal PixelRect Bounds;
        internal bool Minimized, FullScreen, Shown, Active, Normal;
        internal string Title, Class;
    }

    // XWayland apps, like her, can't see native Wayland windows, so on KDE's Wayland a KWin script she loads tells her
    // instead. It calls her over the session bus, at her own unique name, with the whole stack (top first) whenever it
    // changes, and with just the one window while it's being moved, so she can stay seated on it mid-drag.
    internal static class KWinBridge
    {
        private const string Plugin = "capri-bridge";
        private const string ObjectPath = "/app/capri/KWin";
        private const string Interface = "app.capri.KWin";
        // gate guards starting and stopping; data guards the stack, which the bus thread writes while Start waits on KWin.
        private static readonly object gate = new object();
        private static readonly object data = new object();
        private static readonly Dictionary<string, long> ids = new Dictionary<string, long>();
        private static List<KWinWindow> stack = new List<KWinWindow>();
        private static Connection connection;
        private static string scriptPath;
        private static long nextId = 1;
        internal static double Width;
        internal static volatile bool Heard;
        internal static string Trouble;

        internal static bool Wanted
        {
            get
            {
                return OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") != null
                    && (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "").IndexOf("KDE", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        internal static bool Live { get { return connection != null && Heard; } }

        internal static bool Start()
        {
            lock (gate)
            {
                if (connection != null) return true;
                if (!Wanted) return false;
                Connection made = null;
                try
                {
                    string address = Address.Session;
                    if (string.IsNullOrEmpty(address)) return false;
                    made = new Connection(address);
                    Connection connecting = made;
                    Task.Run(() => connecting.ConnectAsync().AsTask()).WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                    made.AddMethodHandler(new Handler());
                    scriptPath = Path.Combine(Folder(), Plugin + "-" + Environment.ProcessId + ".js");
                    File.WriteAllText(scriptPath, Script(made.UniqueName));
                    // A bridge left by a Capri that crashed would call a name nobody holds any more, so it goes first.
                    Call(made, "/Scripting", "org.kde.kwin.Scripting", "unloadScript", Plugin);
                    int id = Load(made, scriptPath);
                    if (id < 0) throw new InvalidOperationException("KWin didn't load her window script.");
                    Call(made, "/Scripting/Script" + id, "org.kde.kwin.Script", "run", null);
                    connection = made;
                    made = null;
                    AppDomain.CurrentDomain.ProcessExit += delegate { Stop(); };
                }
                catch (Exception error) { Trouble = error.GetType().Name + ": " + error.Message; Console.Error.WriteLine("Capri: KWin bridge failed, " + Trouble); return false; }
                finally { made?.Dispose(); }
            }
            // The script sends the whole stack as soon as it runs.
            for (int i = 0; i < 20 && !Heard; i++) System.Threading.Thread.Sleep(50);
            return true;
        }

        internal static void Stop()
        {
            lock (gate)
            {
                if (connection == null) return;
                try { Call(connection, "/Scripting", "org.kde.kwin.Scripting", "unloadScript", Plugin); }
                catch (Exception) { }
                try { if (scriptPath != null) File.Delete(scriptPath); }
                catch (Exception) { }
                connection.Dispose();
                connection = null;
                Heard = false;
            }
        }

        internal static List<KWinWindow> Stack()
        {
            lock (data) return new List<KWinWindow>(stack);
        }

        internal static KWinWindow Get(long id)
        {
            lock (data) foreach (KWinWindow window in stack) if (window.Id == id) return window;
            return null;
        }

        // Switching, minimizing and closing go through a one-line script too, since only KWin can do them to Wayland windows.
        internal static bool Act(long id, string action)
        {
            KWinWindow window = Get(id);
            Connection talk = connection;
            if (window == null || talk == null) return false;
            string body = action == "minimize" ? "w.minimized = true;" : action == "close" ? "w.closeWindow();" : "w.minimized = false; workspace.activeWindow = w;";
            string tag = Plugin + "-act-" + Guid.NewGuid().ToString("N").Substring(0, 12);
            string path = Path.Combine(Folder(), tag + ".js");
            try
            {
                File.WriteAllText(path, "var l = workspace.windowList(); for (var i = 0; i < l.length; i++) { var w = l[i]; if (String(w.internalId) == " + JsonSerializer.Serialize(window.Key) + ") { " + body + " break; } }");
                int script = Load(talk, path, tag);
                if (script < 0) return false;
                Call(talk, "/Scripting/Script" + script, "org.kde.kwin.Script", "run", null);
                System.Threading.Thread.Sleep(150);
                Call(talk, "/Scripting", "org.kde.kwin.Scripting", "unloadScript", tag);
                return true;
            }
            catch (Exception error) { Brain.Log("kwin", -1, error.Message); return false; }
            finally
            {
                try { File.Delete(path); }
                catch (Exception) { }
            }
        }

        // Her scripts go in the session's own runtime folder, which only you can open, rather than the shared /tmp, where
        // someone else on the PC could swap one before KWin reads it.
        private static string Folder()
        {
            string runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            return !string.IsNullOrEmpty(runtime) && Directory.Exists(runtime) ? runtime : Path.GetTempPath();
        }

        private static int Load(Connection talk, string path, string plugin = Plugin)
        {
            using MessageWriter writer = talk.GetMessageWriter();
            writer.WriteMethodCallHeader(destination: "org.kde.KWin", path: "/Scripting", @interface: "org.kde.kwin.Scripting", member: "loadScript", signature: "ss");
            writer.WriteString(path);
            writer.WriteString(plugin);
            MessageBuffer message = writer.CreateMessage();
            return Task.Run(() => talk.CallMethodAsync(message, (reply, _) => reply.GetBodyReader().ReadInt32())).WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
        }

        private static void Call(Connection talk, string path, string face, string member, string text)
        {
            using MessageWriter writer = talk.GetMessageWriter();
            writer.WriteMethodCallHeader(destination: "org.kde.KWin", path: path, @interface: face, member: member, signature: text == null ? null : "s");
            if (text != null) writer.WriteString(text);
            MessageBuffer message = writer.CreateMessage();
            Task.Run(() => talk.CallMethodAsync(message)).WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
        }

        private static long IdOf(string key)
        {
            long id;
            if (!ids.TryGetValue(key, out id)) ids[key] = id = nextId++;
            return id;
        }

        private static KWinWindow Read(JsonElement row)
        {
            int flags = row[6].GetInt32();
            KWinWindow window = new KWinWindow
            {
                Key = row[0].GetString() ?? "",
                Pid = row[1].GetInt32(),
                Bounds = new PixelRect(row[2].GetInt32(), row[3].GetInt32(), Math.Max(0, row[4].GetInt32()), Math.Max(0, row[5].GetInt32())),
                Minimized = (flags & 1) != 0,
                FullScreen = (flags & 2) != 0,
                Shown = (flags & 4) != 0,
                Active = (flags & 8) != 0,
                Normal = (flags & 16) != 0,
                Title = row[7].GetString() ?? "",
                Class = row[8].GetString() ?? "",
            };
            window.Id = IdOf(window.Key);
            return window;
        }

        // "Windows" carries [screen width, [rows...]], "Moved" one row; a row is [id, pid, x, y, w, h, flags, title, class].
        internal static void Hear(string member, string json)
        {
            using JsonDocument parsed = JsonDocument.Parse(json);
            JsonElement root = parsed.RootElement;
            lock (data)
            {
                if (member == "Windows")
                {
                    Width = root[0].GetDouble();
                    List<KWinWindow> fresh = new List<KWinWindow>();
                    HashSet<string> seen = new HashSet<string>();
                    foreach (JsonElement row in root[1].EnumerateArray())
                    {
                        KWinWindow window = Read(row);
                        fresh.Add(window);
                        seen.Add(window.Key);
                    }
                    foreach (string gone in new List<string>(ids.Keys)) if (!seen.Contains(gone)) ids.Remove(gone);
                    stack = fresh;
                    Heard = true;
                }
                else if (member == "Moved")
                {
                    KWinWindow moved = Read(root);
                    List<KWinWindow> fresh = new List<KWinWindow>(stack);
                    for (int i = 0; i < fresh.Count; i++) if (fresh[i].Key == moved.Key) fresh[i] = moved;
                    stack = fresh;
                }
            }
        }

        private sealed class Handler : IMethodHandler
        {
            public string Path { get { return ObjectPath; } }
            public bool RunMethodHandlerSynchronously(Message message) { return true; }

            public ValueTask HandleMethodAsync(MethodContext context)
            {
                Message request = context.Request;
                string member = request.MemberAsString;
                if (request.InterfaceAsString == Interface && (member == "Windows" || member == "Moved") && request.SignatureAsString == "s")
                {
                    try { Hear(member, request.GetBodyReader().ReadString()); }
                    catch (Exception error) { Brain.Log("kwin", -1, error.Message); }
                    if (!context.NoReplyExpected)
                    {
                        using MessageWriter writer = context.CreateReplyWriter(null);
                        context.Reply(writer.CreateMessage());
                    }
                }
                else if (!context.NoReplyExpected) context.ReplyError("org.freedesktop.DBus.Error.UnknownMethod", "Unknown method");
                return ValueTask.CompletedTask;
            }
        }

        // Plasma 6's scripting API. Panels, the desktop and popups are left out; her own windows stay in, for her to skip by pid.
        internal static string Script(string name)
        {
            StringBuilder js = new StringBuilder();
            js.Append("var target = ").Append(JsonSerializer.Serialize(name)).Append(";\n");
            js.Append(@"
function shown(w) {
    var desk = w.onAllDesktops || w.desktops.indexOf(workspace.currentDesktop) >= 0;
    var acts = w.activities;
    return desk && (!acts || acts.length == 0 || acts.indexOf(workspace.currentActivity) >= 0);
}
function row(w) {
    var g = w.frameGeometry;
    var flags = (w.minimized ? 1 : 0) | (w.fullScreen ? 2 : 0) | (shown(w) ? 4 : 0) | (w.active ? 8 : 0) | (w.normalWindow ? 16 : 0);
    return [String(w.internalId), w.pid, Math.round(g.x), Math.round(g.y), Math.round(g.width), Math.round(g.height), flags, String(w.caption), String(w.resourceClass)];
}
function wanted(w) { return (w.normalWindow || w.dialog) && !w.deleted && w.pid > 0 && String(w.resourceClass).length > 0; }
function all() {
    var out = [], st = workspace.stackingOrder;
    for (var i = st.length - 1; i >= 0; i--) if (wanted(st[i])) out.push(row(st[i]));
    var right = 0;
    for (var j = 0; j < workspace.screens.length; j++) { var s = workspace.screens[j].geometry; right = Math.max(right, s.x + s.width); }
    callDBus(target, '/app/capri/KWin', 'app.capri.KWin', 'Windows', JSON.stringify([right, out]));
}
function watch(w) {
    if (!wanted(w)) return;
    w.frameGeometryChanged.connect(function() {
        if (w.move || w.resize) callDBus(target, '/app/capri/KWin', 'app.capri.KWin', 'Moved', JSON.stringify(row(w)));
        else all();
    });
    w.interactiveMoveResizeFinished.connect(all);
    w.minimizedChanged.connect(all);
    w.fullScreenChanged.connect(all);
    w.desktopsChanged.connect(all);
    w.activitiesChanged.connect(all);
    w.captionChanged.connect(all);
}
var list = workspace.windowList();
for (var k = 0; k < list.length; k++) watch(list[k]);
workspace.windowAdded.connect(function(w) { watch(w); all(); });
workspace.windowRemoved.connect(all);
workspace.windowActivated.connect(all);
workspace.currentDesktopChanged.connect(all);
workspace.currentActivityChanged.connect(all);
workspace.screensChanged.connect(all);
all();
");
            return js.ToString();
        }
    }

    // KDE on Wayland: windows come from KWin through the bridge. The mouse is only known while she's held, as on BareDesk.
    internal sealed class KWinDesk : Desk
    {
        internal Func<PixelPoint?> HeldAt;
        private readonly int own = Environment.ProcessId;

        internal override string Name { get { return "KDE Wayland"; } }
        internal override string Debug() { return "scale " + Rescale().ToString(CultureInfo.InvariantCulture) + (KWinBridge.Live ? "" : ", bridge quiet"); }
        internal override PixelPoint? Grabbed() { return HeldAt == null ? null : HeldAt(); }

        // KWin speaks in logical pixels; XWayland apps that scale themselves see physical ones.
        private double Rescale()
        {
            int right = 0;
            foreach (PixelRect screen in Screens) right = Math.Max(right, screen.Right);
            double width = KWinBridge.Width;
            return right > 0 && width > 0 ? right / width : 1;
        }

        private Spot Look(KWinWindow window, double scale)
        {
            if (window.Pid == own || window.Minimized || !window.Shown || window.Bounds.Width <= 0 || window.Bounds.Height <= 0) return null;
            PixelRect b = window.Bounds;
            PixelRect bounds = scale == 1 ? b : new PixelRect((int)Math.Round(b.X * scale), (int)Math.Round(b.Y * scale), (int)Math.Round(b.Width * scale), (int)Math.Round(b.Height * scale));
            Spot spot = Make(window.Id, bounds, !window.FullScreen);
            spot.Name = window.Class + "\n" + window.Title;
            return spot;
        }

        internal override List<Spot> Windows(ICollection<long> skip)
        {
            double scale = Rescale();
            List<Spot> found = new List<Spot>();
            foreach (KWinWindow window in KWinBridge.Stack())
            {
                Spot spot = Look(window, scale);
                if (spot != null && !skip.Contains(spot.Window)) found.Add(spot);
            }
            return found;
        }

        internal override Spot Find(long window)
        {
            KWinWindow found = KWinBridge.Get(window);
            return found == null ? null : Look(found, Rescale());
        }

        internal override bool Alive(long window) { return KWinBridge.Get(window) != null; }
    }
}

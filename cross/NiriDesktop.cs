using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace CapriDesktop
{
    internal interface INiriTransport : IDisposable
    {
        Task<string> Request(string request, CancellationToken cancellation);
        IAsyncEnumerable<string> Events(CancellationToken cancellation);
    }

    internal sealed class NiriIpc : INiriTransport
    {
        private readonly string path;
        private readonly SemaphoreSlim requests = new(1, 1);
        private Socket socket;
        private NetworkStream stream;
        private Lines replies;
        private long requestAttempt = long.MinValue, eventAttempt = long.MinValue;
        private bool disposed;

        internal NiriIpc(string path) { this.path = path; }

        internal sealed class Lines
        {
            private readonly Stream stream;
            private readonly byte[] buffer = new byte[8192];
            private int at, count;
            internal Lines(Stream stream) { this.stream = stream; }
            internal async Task<string> Read(CancellationToken cancellation)
            {
                using var line = new MemoryStream();
                for (;;)
                {
                    if (at == count)
                    {
                        count = await stream.ReadAsync(buffer, cancellation).ConfigureAwait(false); at = 0;
                        if (count == 0) throw new IOException("niri disconnected.");
                    }
                    int end = at;
                    while (end < count && buffer[end] != 10) end++;
                    if (line.Length + end - at > 4 * 1024 * 1024) throw new IOException("niri reply exceeded 4 MiB.");
                    line.Write(buffer, at, end - at); at = end;
                    if (at < count) { at++; return Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length); }
                }
            }
        }

        private static async Task Pause(long previous, CancellationToken cancellation)
        {
            long age = Environment.TickCount64 - previous;
            if (previous != long.MinValue && age < 1000) await Task.Delay((int)(1000 - age), cancellation).ConfigureAwait(false);
        }

        public async Task<string> Request(string request, CancellationToken cancellation)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(2000);
            await requests.WaitAsync(deadline.Token).ConfigureAwait(false);
            try
            {
                if (disposed) throw new ObjectDisposedException(nameof(NiriIpc));
                if (socket == null)
                {
                    await Pause(requestAttempt, deadline.Token).ConfigureAwait(false);
                    requestAttempt = Environment.TickCount64;
                    socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), deadline.Token).ConfigureAwait(false);
                    stream = new NetworkStream(socket, false); replies = new Lines(stream);
                }
                await stream.WriteAsync(Encoding.UTF8.GetBytes(request + "\n"), deadline.Token).ConfigureAwait(false);
                return await replies.Read(deadline.Token).ConfigureAwait(false);
            }
            catch
            {
                stream?.Dispose(); socket?.Dispose(); stream = null; socket = null; replies = null;
                throw;
            }
            finally { requests.Release(); }
        }

        public async IAsyncEnumerable<string> Events([EnumeratorCancellation] CancellationToken cancellation)
        {
            await Pause(eventAttempt, cancellation).ConfigureAwait(false);
            eventAttempt = Environment.TickCount64;
            using var events = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(2000);
            await events.ConnectAsync(new UnixDomainSocketEndPoint(path), deadline.Token).ConfigureAwait(false);
            using var source = new NetworkStream(events, false);
            var lines = new Lines(source);
            await source.WriteAsync(Encoding.UTF8.GetBytes("\"EventStream\"\n"), deadline.Token).ConfigureAwait(false);
            JsonElement ready = NiriDesktop.Ok(await lines.Read(deadline.Token).ConfigureAwait(false));
            if (ready.ValueKind != JsonValueKind.String || ready.GetString() != "Handled") throw new FormatException("niri did not open its event stream.");
            deadline.CancelAfter(Timeout.Infinite);
            for (;;) yield return await lines.Read(cancellation).ConfigureAwait(false);
        }

        public void Dispose()
        {
            disposed = true;
            socket?.Dispose();
        }
    }

    internal sealed record NiriOutput(string Name, Rect Logical, double Scale);
    internal sealed record NiriScreen(string Name, PixelRect Bounds);
    internal sealed record NiriWorkspace(long Id, string Output, bool Active);
    internal sealed record NiriLayout(Point? Position, Size Tile, Point Offset);
    internal sealed record NiriWindow(long Id, string App, long Workspace, bool Floating, NiriLayout Layout);

    internal sealed record NiriMap(NiriOutput Output, PixelRect Bounds)
    {
        internal Point Logical(PixelPoint at) => new Point(Output.Logical.X + (at.X - Bounds.X) * Output.Logical.Width / Bounds.Width,
            Output.Logical.Y + (at.Y - Bounds.Y) * Output.Logical.Height / Bounds.Height);
        internal PixelPoint Pixels(Point at) => new PixelPoint((int)Math.Round(Bounds.X + (at.X - Output.Logical.X) * Bounds.Width / Output.Logical.Width),
            (int)Math.Round(Bounds.Y + (at.Y - Output.Logical.Y) * Bounds.Height / Output.Logical.Height));
        internal PixelRect Pixels(Rect rect)
        {
            PixelPoint a = Pixels(rect.TopLeft), b = Pixels(rect.BottomRight);
            return new PixelRect(a.X, a.Y, Math.Max(0, b.X - a.X), Math.Max(0, b.Y - a.Y));
        }
    }

    internal sealed class NiriDesktop : IDisposable
    {
        internal static NiriDesktop Current { get; private set; }
        private readonly INiriTransport transport;
        private readonly CancellationTokenSource lifetime = new();
        private readonly object gate = new();
        private readonly HashSet<string> logged = new();
        private readonly Dictionary<long, NiriWindow> windows = new();
        private readonly Dictionary<long, NiriWorkspace> workspaces = new();
        private readonly Dictionary<string, NiriOutput> outputs = new();
        private readonly Dictionary<string, Point> work = new();
        private readonly Dictionary<string, Calibration> samples = new();
        private readonly List<Surface> surfaces = new();
        private readonly HashSet<string> ownApps = new();
        private readonly ConditionalWeakTable<Window, Surface> bindings = new();
        private readonly SemaphoreSlim outputWake = new(0, 1);
        private NiriScreen[] screens = Array.Empty<NiriScreen>();
        private long revision;
        private bool windowsReady;
        private Func<long> clock = () => Environment.TickCount64;
        private Task events = Task.CompletedTask, outputReader = Task.CompletedTask;
        private static long nextClass;
        private static bool detectionLogged;

        private sealed record Target(PixelPoint At, PixelPoint Centre, long Generation);
        private sealed record Sent(string Output, Point Fixed, PixelPoint Start, PixelPoint Target, Point Origin, long Revision, long At, long Generation, long Number, bool Acknowledged);
        private sealed record Calibration(Point Offset, string App, long Number, Point Fixed);
        private sealed class Surface
        {
            internal Window Window;
            internal string App;
            internal long? Id;
            internal long Generation, ShownAt, ChangedAt, ChangeRevision, Commands;
            internal bool Visible, Failed, Running, Floated;
            internal Target Pending;
            internal Sent Sent;
            internal PixelPoint? Reported;
            internal CancellationTokenSource Showing;
        }

        internal NiriDesktop(INiriTransport transport) { this.transport = transport; }

        internal static bool Detect(string display, string socket, INiriTransport transport)
        {
            if (string.IsNullOrWhiteSpace(display) || !Absolute(socket)) return false;
            using var deadline = new CancellationTokenSource(2000);
            try
            {
                JsonElement ok = Ok(transport.Request("\"Version\"", deadline.Token).WaitAsync(deadline.Token).GetAwaiter().GetResult());
                return ok.ValueKind == JsonValueKind.Object
                    && ok.TryGetProperty("Version", out var version) && version.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(version.GetString());
            }
            catch (Exception) { return false; }
        }

        internal static NiriDesktop Start()
        {
            if (Current != null) return Current;
            string display = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"), socket = Environment.GetEnvironmentVariable("NIRI_SOCKET");
            if (string.IsNullOrWhiteSpace(display) || !Absolute(socket)) return null;
            var ipc = new NiriIpc(socket);
            if (!Detect(display, socket, ipc))
            {
                ipc.Dispose();
                if (!detectionLogged) { detectionLogged = true; Brain.Log("niri", -1, "Version request failed."); }
                return null;
            }
            var desktop = new NiriDesktop(ipc);
            Current = desktop;
            NativeSurface.NiriRegister = desktop.Register; NativeSurface.NiriMove = desktop.TryMove; NativeSurface.NiriCancel = desktop.CancelMoves;
            desktop.events = Task.Run(desktop.ReadEvents);
            desktop.outputReader = Task.Run(desktop.ReadOutputs);
            desktop.WakeOutputs();
            return desktop;
        }

        internal static JsonElement Ok(string line)
        {
            if (Encoding.UTF8.GetByteCount(line) > 4 * 1024 * 1024) throw new IOException("niri reply exceeded 4 MiB.");
            using var doc = JsonDocument.Parse(line);
            if (!doc.RootElement.TryGetProperty("Ok", out var ok)) throw new IOException("niri rejected a request.");
            return ok.Clone();
        }

        private static bool Absolute(string path) => !string.IsNullOrWhiteSpace(path) && (path[0] == '/' || Path.IsPathFullyQualified(path));

        private void Log(string kind, Exception error = null)
        {
            lock (gate) if (!logged.Add(kind)) return;
            Brain.Log("niri", -1, kind + (error == null ? "." : ": " + error.Message));
        }

        private async Task<JsonElement> Request(string request, CancellationToken cancellation)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(2000);
            try
            {
                JsonElement reply = Ok(await transport.Request(request, deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false));
                if (request.StartsWith("{\"Action\":", StringComparison.Ordinal) && (reply.ValueKind != JsonValueKind.String || reply.GetString() != "Handled")) throw new FormatException("niri did not handle its action.");
                return reply;
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { Log("Request timed out"); throw; }
            catch (Exception error) when (error is not OperationCanceledException) { Log("Request failed " + error.GetType().Name, error); throw; }
        }

        private void WakeOutputs()
        {
            lock (gate) if (outputWake.CurrentCount == 0) outputWake.Release();
        }

        private async Task ReadOutputs()
        {
            CancellationToken cancellation = lifetime.Token;
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    await outputWake.WaitAsync(cancellation).ConfigureAwait(false);
                    try { ReadOutputState(await Request("\"Outputs\"", cancellation).ConfigureAwait(false)); }
                    catch (Exception error) when (!cancellation.IsCancellationRequested) { Log("Outputs unavailable", error); await Task.Delay(1000, cancellation).ConfigureAwait(false); WakeOutputs(); }
                }
            }
            catch (OperationCanceledException) { }
        }

        private async Task ReadEvents()
        {
            CancellationToken cancellation = lifetime.Token;
            while (!cancellation.IsCancellationRequested)
            {
                long attempt = clock();
                try
                {
                    await foreach (string line in transport.Events(cancellation).WithCancellation(cancellation).ConfigureAwait(false)) Event(line);
                    throw new IOException("niri event stream ended.");
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return; }
                catch (Exception error) { Log("Event stream failed " + error.GetType().Name, error); }
                lock (gate)
                {
                    windows.Clear(); workspaces.Clear(); windowsReady = false;
                    foreach (Surface surface in surfaces)
                    {
                        surface.Id = null; surface.Sent = null; surface.Generation++; surface.Pending = null;
                        if (!surface.Visible || surface.Failed) continue;
                        surface.ShownAt = clock();
                        _ = BindDeadline(surface, surface.Showing.Token);
                    }
                }
                try { await Task.Delay((int)Math.Max(0, 1000 - (clock() - attempt)), cancellation).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }

        private static string Text(JsonElement row, string key) => row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        private static long Number(JsonElement row, string key) => row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number) ? number : 0;
        private static bool Flag(JsonElement row, string key) => row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.True;
        private static Point? Pair(JsonElement row, string key)
        {
            if (!row.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 2) return null;
            double x = value[0].GetDouble(), y = value[1].GetDouble();
            if (!double.IsFinite(x) || !double.IsFinite(y)) throw new FormatException("Invalid niri geometry.");
            return new Point(x, y);
        }
        private static NiriLayout Layout(JsonElement row)
        {
            Point size = Pair(row, "tile_size") ?? default;
            if (size.X < 0 || size.Y < 0) throw new FormatException("Invalid niri tile size.");
            return new NiriLayout(Pair(row, "tile_pos_in_workspace_view"), new Size(size.X, size.Y), Pair(row, "window_offset_in_tile") ?? default);
        }
        private static NiriWindow WindowState(JsonElement row) => new NiriWindow(Number(row, "id"), Text(row, "app_id"), Number(row, "workspace_id"),
            Flag(row, "is_floating"), row.TryGetProperty("layout", out var layout) && layout.ValueKind == JsonValueKind.Object ? Layout(layout) : new NiriLayout(null, default, default));

        private void ReadOutputState(JsonElement reply)
        {
            var next = new Dictionary<string, NiriOutput>();
            foreach (var entry in reply.GetProperty("Outputs").EnumerateObject())
            {
                if (!entry.Value.TryGetProperty("logical", out var logical) || logical.ValueKind != JsonValueKind.Object) continue;
                double scale = logical.GetProperty("scale").GetDouble();
                var rect = new Rect(logical.GetProperty("x").GetDouble(), logical.GetProperty("y").GetDouble(), logical.GetProperty("width").GetDouble(), logical.GetProperty("height").GetDouble());
                if (!double.IsFinite(rect.X) || !double.IsFinite(rect.Y) || !double.IsFinite(rect.Width) || !double.IsFinite(rect.Height) || !double.IsFinite(scale) || rect.Width <= 0 || rect.Height <= 0 || scale <= 0) throw new FormatException("Invalid niri output.");
                next[entry.Name] = new NiriOutput(entry.Name, rect, scale);
            }
            lock (gate)
            {
                foreach (var old in outputs) if (!next.TryGetValue(old.Key, out var value) || value != old.Value) { work.Remove(old.Key); samples.Remove(old.Key); }
                outputs.Clear(); foreach (var entry in next) outputs.Add(entry.Key, entry.Value);
                UpdateSurfaces();
            }
        }

        private void Event(string line)
        {
            if (Encoding.UTF8.GetByteCount(line) > 4 * 1024 * 1024) throw new IOException("niri event exceeded 4 MiB.");
            using var doc = JsonDocument.Parse(line);
            JsonElement root = doc.RootElement;
            bool refresh = false;
            lock (gate)
            {
                revision++;
                if (root.TryGetProperty("WindowsChanged", out var all))
                {
                    var next = all.GetProperty("windows").EnumerateArray().Select(WindowState).ToArray();
                    windows.Clear(); foreach (var window in next) windows[window.Id] = window;
                    windowsReady = true;
                }
                else if (root.TryGetProperty("WindowOpenedOrChanged", out var opened)) { var window = WindowState(opened.GetProperty("window")); windows[window.Id] = window; }
                else if (root.TryGetProperty("WindowClosed", out var closed)) windows.Remove(Number(closed, "id"));
                else if (root.TryGetProperty("WindowLayoutsChanged", out var changed))
                {
                    foreach (var change in changed.GetProperty("changes").EnumerateArray())
                        if (windows.TryGetValue(change[0].GetInt64(), out var window)) windows[window.Id] = window with { Layout = Layout(change[1]) };
                }
                else if (root.TryGetProperty("WorkspacesChanged", out var spaces))
                {
                    var next = spaces.GetProperty("workspaces").EnumerateArray().Select(row => new NiriWorkspace(Number(row, "id"), Text(row, "output"), Flag(row, "is_active"))).ToArray();
                    workspaces.Clear(); foreach (var space in next) workspaces[space.Id] = space;
                    refresh = true;
                }
                else if (root.TryGetProperty("WorkspaceActivated", out var active))
                {
                    if (workspaces.TryGetValue(Number(active, "id"), out var selected))
                        foreach (var space in workspaces.Values.ToArray()) if (space.Output == selected.Output) workspaces[space.Id] = space with { Active = space.Id == selected.Id };
                    refresh = true;
                }
                UpdateSurfaces();
            }
            if (refresh) WakeOutputs();
        }

        internal void BindScreens(IReadOnlyList<Screen> screens)
        {
            lock (gate) this.screens = screens.Select(s => new NiriScreen(s.DisplayName, s.Bounds)).ToArray();
        }

        private NiriMap Map(string output)
        {
            if (output == null || !outputs.TryGetValue(output, out var found)) return null;
            var named = screens.Where(s => s.Name == output).ToArray();
            if (named.Length == 1) return new NiriMap(found, named[0].Bounds);
            if (named.Length > 1 || found.Scale != 1) return null;
            var exact = screens.Where(s => s.Bounds.X == found.Logical.X && s.Bounds.Y == found.Logical.Y && s.Bounds.Width == found.Logical.Width && s.Bounds.Height == found.Logical.Height).ToArray();
            if (exact.Length != 1 || outputs.Values.Count(o => o.Scale == 1 && o.Logical == found.Logical) != 1) return null;
            return new NiriMap(found, exact[0].Bounds);
        }

        private NiriMap MapAt(PixelPoint at)
        {
            var maps = outputs.Keys.Select(Map).Where(m => m != null).ToArray();
            var inside = maps.Where(m => m.Bounds.Contains(at)).ToArray();
            if (inside.Length == 1) return inside[0];
            if (inside.Length > 1 || maps.Length == 0) return null;
            double distance(NiriMap m) => Math.Pow(Math.Max(m.Bounds.X - at.X, Math.Max(0, at.X - m.Bounds.Right)), 2) + Math.Pow(Math.Max(m.Bounds.Y - at.Y, Math.Max(0, at.Y - m.Bounds.Bottom)), 2);
            double near = maps.Min(distance);
            var nearest = maps.Where(m => distance(m) == near).ToArray();
            return nearest.Length == 1 ? nearest[0] : null;
        }

        private string Output(NiriWindow window) => workspaces.TryGetValue(window.Workspace, out var space) ? space.Output : null;

        private Surface NewSurface(Window window, string app)
        {
            var surface = new Surface { Window = window, App = app };
            surfaces.Add(surface); ownApps.Add(app);
            return surface;
        }

        internal void Register(Window window)
        {
            string app = "Capri.Niri." + Environment.ProcessId + "." + Guid.NewGuid().ToString("N") + "." + Interlocked.Increment(ref nextClass);
            X11Properties.SetWmClass(window, app);
            Surface surface;
            lock (gate) { surface = NewSurface(window, app); bindings.Add(window, surface); }
            window.Opened += (_, _) => Show(surface);
            window.PropertyChanged += (_, e) =>
            {
                if (e.Property != Visual.IsVisibleProperty) return;
                if (window.IsVisible) Show(surface);
                else Hide(surface);
            };
            window.Closed += (_, _) => { Hide(surface); lock (gate) surfaces.Remove(surface); };
        }

        private void Show(Surface surface)
        {
            lock (gate)
            {
                if (surface.Visible) return;
                surface.Showing?.Cancel(); surface.Showing?.Dispose();
                surface.Showing = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                surface.ShownAt = clock();
                surface.Visible = true; surface.Failed = false; surface.Floated = false; surface.Id = null; surface.Reported = null;
                UpdateSurfaces();
            }
            _ = BindDeadline(surface, surface.Showing.Token);
        }

        private async Task BindDeadline(Surface surface, CancellationToken cancellation)
        {
            try
            {
                await Task.Delay(2000, cancellation).ConfigureAwait(false);
                lock (gate) if (surface.Visible && !surface.Id.HasValue && !surface.Failed) Fail(surface, "Window did not appear within two seconds");
            }
            catch (OperationCanceledException) { }
        }

        private void Hide(Surface surface)
        {
            lock (gate)
            {
                surface.Visible = false; surface.Id = null; surface.Pending = null; surface.Sent = null; surface.Generation++;
                surface.Showing?.Cancel();
            }
        }

        private void Fail(Surface surface, string reason)
        {
            surface.Failed = true; surface.Id = null; surface.Pending = null; surface.Sent = null; surface.Generation++;
            Log(reason);
        }

        internal bool Owns(Window window) { lock (gate) return bindings.TryGetValue(window, out var surface) && !surface.Failed; }
        internal bool Failed(Window window) { lock (gate) return bindings.TryGetValue(window, out var surface) && surface.Visible && surface.Failed; }

        private void UpdateSurfaces()
        {
            if (!windowsReady) return;
            foreach (Surface surface in surfaces)
            {
                if (!surface.Visible || surface.Failed) continue;
                var matches = windows.Values.Where(w => w.App == surface.App).ToArray();
                if (matches.Length > 1) { Fail(surface, "Window identity was ambiguous"); continue; }
                if (matches.Length == 0)
                {
                    if (surface.Id.HasValue || clock() - surface.ShownAt >= 2000) Fail(surface, "Window identity was not found");
                    continue;
                }
                NiriWindow window = matches[0];
                if (surface.Id != window.Id) { surface.Floated = window.Floating; surface.Id = window.Id; }
                var map = Map(Output(window));
                if (window.Layout.Position.HasValue && map != null)
                {
                    Point tile = window.Layout.Position.Value, inset = window.Layout.Offset;
                    var at = map.Pixels(new Point(map.Output.Logical.X + tile.X + inset.X, map.Output.Logical.Y + tile.Y + inset.Y));
                    if (surface.Reported != at)
                    {
                        surface.Reported = at; surface.ChangedAt = clock(); surface.ChangeRevision = revision;
                        long generation = surface.Generation;
                        bool own = OwnReport(surface, at);
                        if (surface.Window is Pet pet) Dispatcher.UIThread.Post(() =>
                        {
                            lock (gate) if (!surface.Visible || surface.Failed || surface.Generation != generation) return;
                            Program.Guard(() => pet.DesktopPosition(at, own));
                        });
                        _ = LearnLater(surface, surface.ChangedAt, lifetime.Token);
                    }
                }
                StartWorker(surface);
            }
        }

        private async Task LearnLater(Surface surface, long changed, CancellationToken cancellation)
        {
            try
            {
                await Task.Delay(350, cancellation).ConfigureAwait(false);
                lock (gate) if (surface.ChangedAt == changed) Learn(surface);
            }
            catch (OperationCanceledException) { }
        }

        private void Learn(Surface surface)
        {
            Sent sent = surface.Sent;
            if (sent == null || !sent.Acknowledged || sent.Generation != surface.Generation || surface.Pending != null || clock() - surface.ChangedAt < 300
                || surface.ChangedAt < sent.At || !surface.Id.HasValue || !windows.TryGetValue(surface.Id.Value, out var window) || Output(window) != sent.Output
                || !window.Layout.Position.HasValue || surface.ChangeRevision <= sent.Revision || !outputs.TryGetValue(sent.Output, out var output)) return;
            Point tile = window.Layout.Position.Value;
            if (tile.X < 0 || tile.Y < 0 || tile.X + window.Layout.Tile.Width > output.Logical.Width || tile.Y + window.Layout.Tile.Height > output.Logical.Height) return;
            Point offset = new Point(tile.X - sent.Fixed.X, tile.Y - sent.Fixed.Y);
            if (samples.TryGetValue(sent.Output, out var sample) && (sample.App != surface.App || sample.Number != sent.Number) && sample.Fixed != sent.Fixed
                && Math.Abs(sample.Offset.X - offset.X) <= 2 && Math.Abs(sample.Offset.Y - offset.Y) <= 2)
            {
                work[sent.Output] = offset;
                surface.Sent = null;
            }
            samples[sent.Output] = new Calibration(offset, surface.App, sent.Number, sent.Fixed);
        }

        private bool OwnReport(Surface surface, PixelPoint at)
        {
            Sent sent = surface.Sent;
            if (sent == null || sent.Generation != surface.Generation || clock() - sent.At > 2000) return false;
            NiriMap map = Map(sent.Output);
            if (map == null) return false;
            PixelPoint target = sent.Target;
            Point? origin = work.TryGetValue(sent.Output, out var known) ? known : samples.TryGetValue(sent.Output, out var sample) ? sample.Offset : null;
            if (origin.HasValue)
                target = new PixelPoint(target.X + (int)Math.Round((origin.Value.X - sent.Origin.X) * map.Bounds.Width / map.Output.Logical.Width),
                    target.Y + (int)Math.Round((origin.Value.Y - sent.Origin.Y) * map.Bounds.Height / map.Output.Logical.Height));
            int right = origin.HasValue ? Math.Max(sent.Start.X, target.X) : Math.Max(target.X, map.Bounds.Right);
            int bottom = origin.HasValue ? Math.Max(sent.Start.Y, target.Y) : Math.Max(target.Y, map.Bounds.Bottom);
            return at.X >= Math.Min(sent.Start.X, target.X) - 2 && at.X <= right + 2
                && at.Y >= Math.Min(sent.Start.Y, target.Y) - 2 && at.Y <= bottom + 2;
        }

        internal bool TryMove(Window window, PixelPoint at)
        {
            lock (gate)
            {
                if (!bindings.TryGetValue(window, out var surface) || surface.Failed) return false;
                PixelPoint centre = new PixelPoint(at.X + (int)(window.Bounds.Width * window.DesktopScaling / 2), at.Y + (int)(window.Bounds.Height * window.DesktopScaling / 2));
                surface.Pending = new Target(at, centre, surface.Generation);
                StartWorker(surface);
                return true;
            }
        }

        internal void CancelMoves(Window window)
        {
            lock (gate) if (bindings.TryGetValue(window, out var surface)) { surface.Generation++; surface.Pending = null; surface.Sent = null; }
        }

        private void StartWorker(Surface surface)
        {
            if (surface.Running || !surface.Visible || surface.Failed || !surface.Id.HasValue || surface.Pending == null) return;
            surface.Running = true;
            _ = Task.Run(() => Drain(surface));
        }

        private async Task Drain(Surface surface)
        {
            try
            {
                for (;;)
                {
                    Target target;
                    lock (gate)
                    {
                        target = surface.Pending; surface.Pending = null;
                        if (target == null || !surface.Visible || surface.Failed || !surface.Id.HasValue) return;
                    }
                    try { await Move(surface, target, lifetime.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
                    catch (Exception error) { Log("Window move failed", error); }
                }
            }
            finally { lock (gate) { surface.Running = false; StartWorker(surface); } }
        }

        private bool Valid(Surface surface, Target target) => surface.Visible && !surface.Failed && surface.Id.HasValue && surface.Generation == target.Generation;

        private async Task Move(Surface surface, Target target, CancellationToken cancellation)
        {
            NiriMap map;
            NiriWindow window;
            bool floating;
            lock (gate)
            {
                if (!Valid(surface, target) || !windows.TryGetValue(surface.Id.Value, out window)) return;
                Learn(surface);
                map = MapAt(target.Centre);
                floating = surface.Floated;
                if (map == null && outputs.Count > 0 && screens.Length > 0 && outputs.Keys.All(name => Map(name) == null)) Fail(surface, "Output could not be matched to a screen");
            }
            if (map == null) return;
            if (!floating)
            {
                await Request(JsonSerializer.Serialize(new { Action = new { MoveWindowToFloating = new { id = window.Id } } }), cancellation).ConfigureAwait(false);
                lock (gate) { if (surface.Id == window.Id) surface.Floated = true; if (!Valid(surface, target)) return; }
            }
            string current;
            lock (gate) { if (!Valid(surface, target)) return; current = windows.TryGetValue(window.Id, out var latest) ? Output(latest) : null; }
            if (current != null && current != map.Output.Name)
                await Request(JsonSerializer.Serialize(new { Action = new { MoveWindowToMonitor = new { id = window.Id, output = map.Output.Name } } }), cancellation).ConfigureAwait(false);
            Sent sent;
            lock (gate)
            {
                if (!Valid(surface, target) || !windows.TryGetValue(window.Id, out window)) return;
                Point point = map.Logical(target.At), origin = work.TryGetValue(map.Output.Name, out var value) ? value : default;
                Point fixedAt = new Point(point.X - map.Output.Logical.X - window.Layout.Offset.X - origin.X, point.Y - map.Output.Logical.Y - window.Layout.Offset.Y - origin.Y);
                PixelPoint start = surface.Reported ?? target.At;
                sent = new Sent(map.Output.Name, fixedAt, start, target.At, origin, revision, clock(), surface.Generation, ++surface.Commands, false);
                surface.Sent = sent;
            }
            await Request(JsonSerializer.Serialize(new { Action = new { MoveFloatingWindow = new { id = window.Id, x = new { SetFixed = sent.Fixed.X }, y = new { SetFixed = sent.Fixed.Y } } } }), cancellation).ConfigureAwait(false);
            lock (gate) if (surface.Sent == sent) surface.Sent = sent with { Acknowledged = true };
        }

        internal List<Spot> Windows(ICollection<long> skip)
        {
            lock (gate)
            {
                var found = new List<Spot>();
                var bound = surfaces.Where(s => s.Id.HasValue).Select(s => s.Id.Value).ToHashSet();
                foreach (NiriWindow window in windows.Values)
                {
                    if (skip.Contains(window.Id) || bound.Contains(window.Id) || window.App != null && ownApps.Contains(window.App)
                        || !workspaces.TryGetValue(window.Workspace, out var space) || !space.Active || !window.Layout.Position.HasValue) continue;
                    NiriMap map = Map(space.Output);
                    if (map == null) continue;
                    Point tile = window.Layout.Position.Value;
                    var logical = new Rect(map.Output.Logical.X + tile.X, map.Output.Logical.Y + tile.Y, window.Layout.Tile.Width, window.Layout.Tile.Height).Intersect(map.Output.Logical);
                    PixelRect bounds = map.Pixels(logical).Intersect(map.Bounds);
                    if (bounds.Width <= 0 || bounds.Height <= 0) continue;
                    bool big = bounds.Width >= 120 && bounds.Height >= 60;
                    found.Add(new Spot { Window = window.Id, Bounds = bounds, Seat = big && bounds != map.Bounds, Solid = big });
                }
                return found;
            }
        }

        internal Spot Find(long id) => Windows(Array.Empty<long>()).Find(w => w.Window == id);
        internal bool Alive(long id) { lock (gate) return windows.ContainsKey(id); }

        public void Dispose()
        {
            lifetime.Cancel(); transport.Dispose();
            lock (gate) foreach (Surface surface in surfaces) { surface.Showing?.Cancel(); surface.Pending = null; surface.Id = null; }
            if (Current != this) return;
            Current = null;
            NativeSurface.NiriRegister = null; NativeSurface.NiriMove = null; NativeSurface.NiriCancel = null;
        }

        private sealed class FakeTransport : INiriTransport
        {
            internal readonly List<string> Asked = new();
            internal TaskCompletionSource<string> Hold;
            internal int Active, Most, EventStarts;
            public async Task<string> Request(string request, CancellationToken cancellation)
            {
                TaskCompletionSource<string> held;
                lock (Asked) { Asked.Add(request); Active++; Most = Math.Max(Most, Active); held = Hold; Hold = null; }
                try
                {
                    if (held != null) return await held.Task.WaitAsync(cancellation).ConfigureAwait(false);
                    return request == "\"Version\"" ? "{\"Ok\":{\"Version\":\"25.08\"}}" : "{\"Ok\":\"Handled\"}";
                }
                finally { lock (Asked) Active--; }
            }
            public async IAsyncEnumerable<string> Events([EnumeratorCancellation] CancellationToken cancellation) { Interlocked.Increment(ref EventStarts); await Task.CompletedTask; yield break; }
            public void Dispose() { }
        }

        internal static void Checks(List<string> checks)
        {
            Task.Run(() => CheckAsync(checks)).GetAwaiter().GetResult();
        }

        private static async Task CheckAsync(List<string> checks)
        {
            using var transport = new FakeTransport();
            string absolute = Path.Combine(Path.GetTempPath(), "capri-niri.sock");
            if (!Detect("wayland-0", absolute, transport) || Detect("wayland-0", "relative.sock", transport) || Detect(null, absolute, transport)) throw new Exception("niri socket detection accepted the wrong path.");
            checks.Add("PASS niri detection requires Wayland, an absolute socket and a Version reply");
            using var desktop = new NiriDesktop(transport);
            long now = 1000;
            desktop.clock = () => now;
            desktop.screens = new[] { new NiriScreen("DP-1", new PixelRect(1920, 0, 1920, 1080)) };
            desktop.ReadOutputState(Ok("{\"Ok\":{\"Outputs\":{\"DP-1\":{\"logical\":{\"x\":1920,\"y\":0,\"width\":1280,\"height\":720,\"scale\":1.5}}}}}"));
            desktop.Event("{\"WorkspacesChanged\":{\"workspaces\":[{\"id\":3,\"output\":\"DP-1\",\"is_active\":true},{\"id\":4,\"output\":\"DP-1\",\"is_active\":false}]}}");
            string row(long id, string app, long space, int x, int y, bool floating = true) => JsonSerializer.Serialize(new { id, app_id = app, title = "Capri", pid = 999999, workspace_id = space, is_floating = floating,
                layout = new { tile_pos_in_workspace_view = new[] { x, y }, tile_size = new[] { 200, 300 }, window_offset_in_tile = new[] { 3, 5 } } });
            desktop.Event("{\"WindowsChanged\":{\"windows\":[" + row(42, "own", 3, 100, 100) + "," + row(43, "other", 3, 200, 120) + "," + row(44, "inactive", 4, 200, 120) + "]}}");
            Surface surface = desktop.NewSurface(null, "own");
            surface.Visible = true; surface.ShownAt = now;
            desktop.UpdateSurfaces();
            if (surface.Id != 42 || surface.Reported != new PixelPoint(2074, 158)) throw new Exception("niri did not bind by app_id and title or map its content position independently of pid.");
            checks.Add("PASS niri binds the unique app_id and title with satellite's unrelated pid");
            var ledges = desktop.Windows(Array.Empty<long>());
            if (ledges.Count != 1 || ledges[0].Window != 43 || ledges[0].Bounds != new PixelRect(2220, 180, 300, 450)) throw new Exception("niri ledges did not use active foreign tiles in screen pixels.");
            if (desktop.Find(43) == null || !desktop.Alive(44)) throw new Exception("niri window ids were not found.");
            desktop.Event("{\"WindowLayoutsChanged\":{\"changes\":[[43,{\"tile_pos_in_workspace_view\":[-20,120],\"tile_size\":[200,300],\"window_offset_in_tile\":[3,5]}]]}}");
            if (desktop.Find(43)?.Bounds != new PixelRect(1920, 180, 270, 450)) throw new Exception("niri did not clip a tile to its output.");
            desktop.Event("{\"WorkspaceActivated\":{\"id\":4,\"focused\":true}}");
            ledges = desktop.Windows(Array.Empty<long>());
            if (ledges.Count != 1 || ledges[0].Window != 44 || desktop.outputWake.CurrentCount != 1) throw new Exception("niri did not switch active workspaces or refresh outputs.");
            desktop.Event("{\"WorkspaceActivated\":{\"id\":3,\"focused\":true}}");
            checks.Add("PASS niri ledges use active workspace tile rectangles and skip own windows");
            transport.Asked.Clear();
            await desktop.Move(surface, new Target(new PixelPoint(2220, 300), new PixelPoint(2220, 300), 0), CancellationToken.None);
            now += 10;
            desktop.Event("{\"WindowLayoutsChanged\":{\"changes\":[[42,{\"tile_pos_in_workspace_view\":[197,225],\"tile_size\":[200,300],\"window_offset_in_tile\":[3,5]}]]}}");
            now += 400; desktop.Learn(surface);
            if (desktop.work.ContainsKey("DP-1")) throw new Exception("One uncorrelated niri layout established an offset.");
            await desktop.Move(surface, new Target(new PixelPoint(2250, 330), new PixelPoint(2250, 330), 0), CancellationToken.None);
            now += 10;
            desktop.Event("{\"WindowLayoutsChanged\":{\"changes\":[[42,{\"tile_pos_in_workspace_view\":[217,245],\"tile_size\":[200,300],\"window_offset_in_tile\":[3,5]}]]}}");
            now += 400; desktop.Learn(surface);
            if (!desktop.work.TryGetValue("DP-1", out var work) || work != new Point(0, 30)) throw new Exception("niri did not learn the working-area origin from a landed move.");
            transport.Asked.Clear();
            await desktop.Move(surface, new Target(new PixelPoint(2370, 450), new PixelPoint(2370, 450), 0), CancellationToken.None);
            if (transport.Asked.Count != 1) throw new Exception("One niri move sent more than one action.");
            using (var sent = JsonDocument.Parse(transport.Asked[0]))
            {
                var action = sent.RootElement.GetProperty("Action").GetProperty("MoveFloatingWindow");
                if (action.GetProperty("id").GetInt64() != 42 || action.GetProperty("x").GetProperty("SetFixed").GetDouble() != 297 || action.GetProperty("y").GetProperty("SetFixed").GetDouble() != 265) throw new Exception("niri used the wrong output, content or working-area offset.");
            }
            checks.Add("PASS a learned 30 pixel working-area origin produces one correctly mapped SetFixed move");
            transport.Asked.Clear();
            var hold = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            transport.Hold = hold;
            lock (desktop.gate) { surface.Pending = new Target(new PixelPoint(2400, 450), new PixelPoint(2400, 450), 0); desktop.StartWorker(surface); }
            await Wait(() => { lock (transport.Asked) return transport.Asked.Count == 1; });
            lock (desktop.gate)
            {
                surface.Pending = new Target(new PixelPoint(2500, 450), new PixelPoint(2500, 450), 0);
                surface.Pending = new Target(new PixelPoint(2600, 450), new PixelPoint(2600, 450), 0);
                surface.Pending = new Target(new PixelPoint(2670, 450), new PixelPoint(2670, 450), 0);
            }
            hold.SetResult("{\"Ok\":\"Handled\"}");
            await Wait(() => { lock (desktop.gate) return !surface.Running; });
            if (transport.Asked.Count != 2 || transport.Most != 1 || !transport.Asked[1].Contains("\"SetFixed\":497")) throw new Exception("niri moves did not keep only the latest waiting move.");
            checks.Add("PASS niri keeps one move in flight and sends only the latest waiting target");
            surface.Floated = false;
            transport.Asked.Clear();
            await desktop.Move(surface, new Target(new PixelPoint(2370, 450), new PixelPoint(2370, 450), 0), CancellationToken.None);
            await desktop.Move(surface, new Target(new PixelPoint(2385, 450), new PixelPoint(2385, 450), 0), CancellationToken.None);
            if (transport.Asked.Count != 3 || !transport.Asked[0].Contains("MoveWindowToFloating")) throw new Exception("niri did not float a tiled window exactly once.");
            checks.Add("PASS niri floats a tiled window once before positioning it");
            surface.Sent = null;
            transport.Asked.Clear();
            hold = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            transport.Hold = hold;
            lock (desktop.gate) { surface.Pending = new Target(new PixelPoint(2400, 450), new PixelPoint(2400, 450), 0); desktop.StartWorker(surface); }
            await Wait(() => { lock (transport.Asked) return transport.Asked.Count == 1; });
            lock (desktop.gate) { surface.Pending = new Target(new PixelPoint(2500, 450), new PixelPoint(2500, 450), 0); surface.Generation++; surface.Pending = null; surface.Sent = null; }
            hold.SetResult("{\"Ok\":\"Handled\"}");
            await Wait(() => { lock (desktop.gate) return !surface.Running; });
            if (transport.Asked.Count != 1) throw new Exception("CancelMoves left a waiting niri move.");
            checks.Add("PASS cancelling niri moves drops the waiting target");
            desktop.Event("{\"WindowOpenedOrChanged\":{\"window\":{\"id\":90,\"title\":null,\"app_id\":null,\"workspace_id\":null,\"layout\":null}}}");
            if (!desktop.Alive(90) || desktop.Find(90) != null) throw new Exception("Optional niri fields were not handled.");
            desktop.NewSurface(null, "own-hidden");
            desktop.Event("{\"WindowOpenedOrChanged\":{\"window\":" + row(46, "own-hidden", 3, 100, 100) + "}}");
            if (desktop.Windows(Array.Empty<long>()).Count != 1) throw new Exception("A hidden window of hers became a ledge.");
            checks.Add("PASS optional niri fields and unbound own app_ids do not become ledges");
            var lateSurface = desktop.NewSurface(null, "not-here");
            lateSurface.Visible = true; lateSurface.ShownAt = now - 2001;
            desktop.UpdateSurfaces();
            if (!lateSurface.Failed || lateSurface.Id.HasValue) throw new Exception("A niri window could bind after its deadline.");
            checks.Add("PASS niri binding stops waiting after two seconds");
            NiriMap mapped = desktop.Map("DP-1");
            desktop.screens = new[] { new NiriScreen("other", new PixelRect(1920, 0, 1920, 1080)) };
            if (desktop.Map("DP-1") != null) throw new Exception("niri matched a scaled screen by order.");
            desktop.outputs["plain"] = new NiriOutput("plain", new Rect(-1280, 0, 1280, 720), 1);
            desktop.screens = new[] { new NiriScreen("unnamed", new PixelRect(-1280, 0, 1280, 720)) };
            if (desktop.Map("plain") == null) throw new Exception("niri lost a unique exact rectangle at scale one.");
            desktop.screens = new[] { desktop.screens[0], new NiriScreen("duplicate", desktop.screens[0].Bounds) };
            if (desktop.Map("plain") != null) throw new Exception("niri accepted an ambiguous exact rectangle.");
            desktop.outputs.Remove("plain");
            desktop.screens = new[] { new NiriScreen("DP-1", mapped.Bounds), new NiriScreen("DP-2", new PixelRect(0, 0, 1920, 1080)) };
            desktop.outputs["DP-2"] = new NiriOutput("DP-2", new Rect(0, 0, 1280, 720), 1.5);
            transport.Asked.Clear();
            await desktop.Move(surface, new Target(new PixelPoint(300, 200), new PixelPoint(300, 200), surface.Generation), CancellationToken.None);
            if (transport.Asked.Count != 2 || !transport.Asked[0].Contains("MoveWindowToMonitor") || !transport.Asked[0].Contains("DP-2") || !transport.Asked[1].Contains("MoveFloatingWindow")) throw new Exception("A cross-output niri move did not select its monitor first.");
            checks.Add("PASS niri matches outputs by name and changes monitor before positioning");
            surface.Sent = new Sent("DP-1", default, new PixelPoint(2200, 100), new PixelPoint(2200, 400), new Point(0, 30), 0, now, surface.Generation, 100, true);
            if (!desktop.OwnReport(surface, new PixelPoint(2200, 250)) || !desktop.OwnReport(surface, new PixelPoint(2200, 400)) || desktop.OwnReport(surface, new PixelPoint(2250, 250))) throw new Exception("niri animation reports were not separated from renewed dragging.");
            desktop.work.Clear(); desktop.samples.Clear();
            surface.Sent = new Sent("DP-1", default, new PixelPoint(2200, 200), new PixelPoint(2200, 205), default, 0, now, surface.Generation, 101, true);
            if (!desktop.OwnReport(surface, new PixelPoint(2200, 250))) throw new Exception("An unlearned working-area origin resumed her own drop as a drag.");
            desktop.samples["DP-1"] = new Calibration(new Point(0, 30), surface.App, 101, default);
            if (!desktop.OwnReport(surface, new PixelPoint(2200, 250)) || desktop.OwnReport(surface, new PixelPoint(2250, 250))) throw new Exception("A provisional working-area origin was not used for animation reports.");
            checks.Add("PASS niri recognises requested animation paths and outside movement");
            desktop.work.Clear(); desktop.samples.Clear(); surface.Sent = null;
            await desktop.Move(surface, new Target(new PixelPoint(2220, 300), new PixelPoint(2220, 300), surface.Generation), CancellationToken.None);
            await desktop.Move(surface, new Target(new PixelPoint(2370, 450), new PixelPoint(2370, 450), surface.Generation), CancellationToken.None);
            now += 10;
            desktop.Event("{\"WindowLayoutsChanged\":{\"changes\":[[42,{\"tile_pos_in_workspace_view\":[197,225],\"tile_size\":[200,300],\"window_offset_in_tile\":[3,5]}]]}}");
            now += 400; desktop.Learn(surface);
            if (desktop.work.Count != 0) throw new Exception("A delayed earlier layout taught niri the working-area origin.");
            now += 10;
            desktop.Event("{\"WindowLayoutsChanged\":{\"changes\":[[42,{\"tile_pos_in_workspace_view\":[297,325],\"tile_size\":[200,300],\"window_offset_in_tile\":[3,5]}]]}}");
            now += 400; desktop.Learn(surface);
            await desktop.Move(surface, new Target(new PixelPoint(2400, 480), new PixelPoint(2400, 480), surface.Generation), CancellationToken.None);
            now += 10;
            desktop.Event("{\"WindowLayoutsChanged\":{\"changes\":[[42,{\"tile_pos_in_workspace_view\":[317,345],\"tile_size\":[200,300],\"window_offset_in_tile\":[3,5]}]]}}");
            now += 400; desktop.Learn(surface);
            if (!desktop.work.TryGetValue("DP-1", out work) || work != new Point(0, 30)) throw new Exception("Independent landed moves did not correct niri calibration.");
            await desktop.Move(surface, new Target(new PixelPoint(3800, 1000), new PixelPoint(3800, 1000), surface.Generation), CancellationToken.None);
            now += 10;
            desktop.Event("{\"WindowLayoutsChanged\":{\"changes\":[[42,{\"tile_pos_in_workspace_view\":[1200,680],\"tile_size\":[200,300],\"window_offset_in_tile\":[3,5]}]]}}");
            now += 400; desktop.Learn(surface);
            if (desktop.work["DP-1"] != new Point(0, 30)) throw new Exception("A clamped niri tile changed working-area calibration.");
            checks.Add("PASS late earlier layouts and clamped tiles do not establish a working-area offset");
            using (var bytes = new MemoryStream(Encoding.UTF8.GetBytes("{\"title\":\"Capri \u00e9\"}\nsecond\n")))
            {
                var lines = new NiriIpc.Lines(bytes);
                if (!(await lines.Read(CancellationToken.None)).Contains("\u00e9") || await lines.Read(CancellationToken.None) != "second") throw new Exception("niri JSON lines lost framing or UTF-8.");
            }
            using (var bytes = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 4 * 1024 * 1024 + 1) + "\n")))
            {
                bool capped = false;
                try { await new NiriIpc.Lines(bytes).Read(CancellationToken.None); }
                catch (IOException) { capped = true; }
                if (!capped) throw new Exception("niri accepted a reply above 4 MiB.");
            }
            checks.Add("PASS niri frames UTF-8 JSON lines and caps replies at 4 MiB");
            transport.Hold = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool timedOut = false;
            try { await desktop.Request("\"Outputs\"", CancellationToken.None); }
            catch (OperationCanceledException) { timedOut = true; }
            if (!timedOut) throw new Exception("A niri request had no deadline.");
            checks.Add("PASS niri requests stop after two seconds");
            using (var broken = new FakeTransport())
            using (var reconnect = new NiriDesktop(broken))
            {
                Task stream = reconnect.ReadEvents();
                await Task.Delay(1150);
                reconnect.lifetime.Cancel();
                await stream;
                if (broken.EventStarts != 2 || reconnect.logged.Count != 1) throw new Exception("niri reconnects or repeated failure logs were not limited.");
            }
            checks.Add("PASS a broken niri event stream reconnects once a second and logs its failure once");
            var desk = new NiriDesk(desktop);
            if (desk.Name != "niri" || desk.Pointer() != null || desk.Grabbed() != null) throw new Exception("niri invented a pointer position.");
            surface.Id = null; surface.Failed = false;
            desktop.Event("{\"WindowOpenedOrChanged\":{\"window\":" + row(45, "own", 3, 100, 100) + "}}");
            if (!surface.Failed || surface.Id.HasValue) throw new Exception("niri accepted an ambiguous identity.");
            checks.Add("PASS niri gives no pointer position and rejects ambiguous window identities");
        }

        private static async Task Wait(Func<bool> done)
        {
            using var deadline = new CancellationTokenSource(2000);
            while (!done()) await Task.Delay(5, deadline.Token).ConfigureAwait(false);
        }
    }

    internal sealed class NiriDesk : Desk
    {
        internal readonly NiriDesktop Desktop;
        internal NiriDesk(NiriDesktop desktop) { Desktop = desktop; }
        internal override string Name { get { return "niri"; } }
        internal override void BindScreens(IReadOnlyList<Screen> screens) { Desktop.BindScreens(screens); }
        internal override List<Spot> Windows(ICollection<long> skip) { return Desktop.Windows(Array.Empty<long>()); }
        internal override Spot Find(long window) { return Desktop.Find(window); }
        internal override bool Alive(long window) { return Desktop.Alive(window); }
        public override void Dispose() { Desktop.Dispose(); }
    }
}

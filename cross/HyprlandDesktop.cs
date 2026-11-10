using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Platform;

namespace CapriDesktop
{
    internal sealed class HyprlandObservationChangedException : InvalidOperationException
    {
        internal HyprlandObservationChangedException(string message) : base(message) { }
    }

    internal sealed partial class HyprlandDesktop : IDisposable
    {
        private static readonly Lazy<HyprlandDesktop> current = new Lazy<HyprlandDesktop>(() =>
        {
            if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")) ||
                string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE"))) return null;
            return new HyprlandDesktop(new HyprlandIpc(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"), Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE")));
        });
        internal static HyprlandDesktop Current => current.Value;

        private readonly IHyprlandTransport transport;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly SemaphoreSlim changed = new SemaphoreSlim(0, 1);
        private readonly SemaphoreSlim actions = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim captures = new SemaphoreSlim(1, 1);
        private readonly Func<int, string> processIdentity;
        private readonly Func<long> clock;
        private readonly Func<TimeSpan, CancellationToken, Task> delay;
        private readonly Dictionary<OpenWindow, WindowTicket> tickets = new Dictionary<OpenWindow, WindowTicket>();
        private readonly Dictionary<long, (string Identity, int Workspace)> minimized = new Dictionary<long, (string, int)>();
        private readonly Task events, refresh;
        private HyprlandSnapshot snapshot;
        private HyprlandSnapshot visual;
        private long visualEpoch;
        private int trackingPointer;
        internal long SurfaceRevision { get; private set; }
        internal HyprlandSnapshot VisualSnapshot
        {
            get { var state = Volatile.Read(ref visual); return Volatile.Read(ref disposed) == 0 && state != null && clock() - state.CapturedAt <= 1500 ? state : null; }
        }
        private string lastError;
        private string connectionError = "Connecting to Hyprland.";
        private bool? lua;
        private long epoch;
        private int disposed, pointerReading;
        internal sealed record PointerSample(Point Point, string Session, string Layout, long Revision, long At, long Sequence);
        internal event Action<PointerSample> PointerChanged;
        internal long PointerSequence => Interlocked.Read(ref pointerSequence);
        private PointerSample pointer;
        private long pointerSequence;
        private readonly object pointerLock = new object();
        private sealed record WindowTicket(HyprlandWindow Window, string Identity, string Session, string Layout, long At);

        internal string SessionId => transport.SessionId;
        internal string LastError => Volatile.Read(ref lastError) ?? Volatile.Read(ref connectionError);
        internal HyprlandSnapshot Snapshot
        {
            get
            {
                HyprlandSnapshot value = Volatile.Read(ref snapshot);
                return Volatile.Read(ref disposed) == 0 && value != null && clock() - value.CapturedAt <= 1500 ? value : null;
            }
        }

        internal HyprlandDesktop(IHyprlandTransport transport, bool monitor = true, Func<int, string> processIdentity = null, Func<long> clock = null,
            Func<TimeSpan, CancellationToken, Task> delay = null)
        {
            this.transport = transport;
            this.processIdentity = processIdentity ?? ReadProcessIdentity;
            this.clock = clock ?? (() => Environment.TickCount64);
            this.delay = delay ?? Task.Delay;
            if (monitor) { events = Task.Run(EventLoop); refresh = Task.Run(RefreshLoop); }
            else { events = Task.CompletedTask; refresh = Task.CompletedTask; }
        }

        internal static string ReadProcessIdentity(int pid) { return LinuxProcess.Identity(pid); }
        internal long FocusEpoch => Interlocked.Read(ref focusEpoch);
        private long focusEpoch;
        internal long ObservationEpoch => Interlocked.Read(ref visualEpoch);
        internal long ActionEpoch => Interlocked.Read(ref epoch);
        private readonly Queue<string> observationEvents = new Queue<string>();
        internal string[] ObservationEvents { get { lock (pointerLock) return observationEvents.ToArray(); } }
        private void RecordObservationEvent(string name)
        {
            if (name.Length == 0 || name.Length > 64 || name.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '_'))) return;
            lock (pointerLock)
            {
                if (observationEvents.Count == 16) observationEvents.Dequeue();
                observationEvents.Enqueue(name);
            }
        }

        internal string Query(string command) => QueryAsync(command, CancellationToken.None).GetAwaiter().GetResult();
        internal Task<string> QueryAsync(string command, CancellationToken cancellation)
        {
            if (command != "j/clients" && command != "j/monitors" && command != "j/activewindow" && command != "j/cursorpos" && command != "j/version" && command != "j/locked")
                throw new ArgumentException("Only named read-only Hyprland queries are available.", nameof(command));
            return Request(command, cancellation);
        }

        internal Task<string> SurfaceRequest(string command, CancellationToken cancellation) => Request(command, cancellation);

        private async Task<string> Request(string command, CancellationToken cancellation)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token);
            linked.CancelAfter(TimeSpan.FromSeconds(2));
            return await transport.RequestAsync(command, linked.Token).ConfigureAwait(false);
        }

        internal Task<HyprlandSnapshot> CaptureSnapshotAsync(CancellationToken cancellation) => CaptureSnapshotAsync(cancellation, false);

        // A fresh read-only observation keeps the session/layout/lock bracket while
        // allowing unrelated window and focus events. It never authorizes input.
        internal Task<HyprlandSnapshot> ObserveSnapshotAsync(CancellationToken cancellation) => CaptureSnapshotAsync(cancellation, true);

        private async Task<HyprlandSnapshot> CaptureSnapshotAsync(CancellationToken cancellation, bool observation)
        {
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token);
            cancellation = linked.Token;
            // Background refresh and action/list observations share one publication
            // order. An older unlocked read must never overwrite a newer locked one.
            await captures.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                string session = SessionId;
                // Monitors bracket the read: a workspace/hotplug transition cannot create a
                // usable mixed-layout snapshot. Event revision also fences focus changes.
                long revision = Interlocked.Read(ref epoch);
                long visualRevision = Interlocked.Read(ref visualEpoch);
                HyprlandOutput[] outputs = HyprlandJson.Outputs(await QueryAsync("j/monitors", cancellation).ConfigureAwait(false));
                string layout = HyprlandJson.LayoutId(outputs);
                HyprlandWindow[] windows = HyprlandJson.Windows(await QueryAsync("j/clients", cancellation).ConfigureAwait(false));
                long active = HyprlandJson.Active(await QueryAsync("j/activewindow", cancellation).ConfigureAwait(false));
                long sampleSequence = Interlocked.Increment(ref pointerSequence);
                long sampleRevision = Interlocked.Read(ref visualEpoch);
                long pointerStarted = clock();
                Point at = HyprlandJson.Pointer(await QueryAsync("j/cursorpos", cancellation).ConfigureAwait(false));
                bool locked = HyprlandJson.Locked(await QueryAsync("j/locked", cancellation).ConfigureAwait(false));
                HyprlandOutput[] after = HyprlandJson.Outputs(await QueryAsync("j/monitors", cancellation).ConfigureAwait(false));
                bool lockedAfter = HyprlandJson.Locked(await QueryAsync("j/locked", cancellation).ConfigureAwait(false));
                if (session != SessionId || outputs.Length == 0 || layout != HyprlandJson.LayoutId(after) || visualRevision != Interlocked.Read(ref visualEpoch))
                    throw new HyprlandObservationChangedException("Desktop changed during observation; obtain a fresh snapshot.");
                cancellation.ThrowIfCancellationRequested();
                // A transition in either direction withholds desktop access until
                // both lock checks in a subsequent capture observe it unlocked.
                locked |= lockedAfter;
                HyprlandSnapshot result = new HyprlandSnapshot(session, layout, outputs, windows, at, active, locked, clock(), visualRevision);
                // Window/focus churn must reject action targets without starving
                // coherent visual geometry. Layout/session/lock checks still apply.
                lock (pointerLock)
                {
                    if (visualRevision != Interlocked.Read(ref visualEpoch))
                        throw new HyprlandObservationChangedException("Visual layout changed during observation.");
                    Volatile.Write(ref visual, result);
                    PublishPointer(new PointerSample(at, session, layout, sampleRevision, pointerStarted, sampleSequence));
                }
                if (observation && revision != Interlocked.Read(ref epoch)) Volatile.Write(ref snapshot, null);
                if (!observation)
                {
                    if (revision != Interlocked.Read(ref epoch))
                        throw new InvalidOperationException("Desktop changed during observation; obtain a fresh action snapshot.");
                    Volatile.Write(ref snapshot, result);
                }
                Volatile.Write(ref connectionError, locked ? "The desktop session is locked." : null);
                return result;
            }
            catch
            {
                Volatile.Write(ref snapshot, null);
                throw;
            }
            finally { captures.Release(); }
        }

        private async Task EventLoop()
        {
            while (!lifetime.IsCancellationRequested)
            {
                try
                {
                    await foreach (string line in transport.Events(lifetime.Token).ConfigureAwait(false))
                    {
                        int split = line.IndexOf(">>", StringComparison.Ordinal);
                        if (split < 0) continue;
                        string name = line.Substring(0, split);
                        RecordObservationEvent(name);
                        if (name.Contains("monitor", StringComparison.Ordinal) || name == "configreloaded" || name == "lockgroups")
                        { lock (pointerLock) { Interlocked.Increment(ref visualEpoch); Volatile.Write(ref visual, null); } }
                        if (name == "configreloaded") SurfaceRevision++;

                        if (name == "activewindowv2" || name.StartsWith("workspace", StringComparison.Ordinal) || name.Contains("monitor", StringComparison.Ordinal) || name == "lockgroups")
                            Interlocked.Increment(ref focusEpoch);
                        if (name.StartsWith("activewindow", StringComparison.Ordinal) || name.StartsWith("workspace", StringComparison.Ordinal) ||
                            name.Contains("window", StringComparison.Ordinal) || name.Contains("monitor", StringComparison.Ordinal) ||
                            name.Contains("special", StringComparison.Ordinal) || name == "fullscreen" || name == "configreloaded" || name == "lockgroups")
                        {
                            Interlocked.Increment(ref epoch);
                            if (changed.CurrentCount == 0) changed.Release();
                        }
                    }
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { break; }
                catch (Exception error)
                {
                    RecordObservationEvent("event_stream_error");
                    Interlocked.Increment(ref focusEpoch);
                    lock (pointerLock) { Interlocked.Increment(ref visualEpoch); Volatile.Write(ref visual, null); }
                    Volatile.Write(ref snapshot, null);
                    Volatile.Write(ref connectionError, "Hyprland event stream: " + error.Message);
                }
                try { await Task.Delay(1000, lifetime.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }

        private async Task RefreshLoop()
        {
            while (!lifetime.IsCancellationRequested)
            {
                try
                {
                    await CaptureSnapshotAsync(lifetime.Token).ConfigureAwait(false);
                    // Hyprland has no continuous client move/resize event. A bounded
                    // socket refresh fills this gap; events wake it sooner. No subprocess polling.
                    await changed.WaitAsync(250, lifetime.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { break; }
                catch (Exception error)
                {
                    Volatile.Write(ref snapshot, null);
                    Volatile.Write(ref connectionError, "Hyprland desktop: " + error.Message);
                    try { await Task.Delay(250, lifetime.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }

        // Visual sampling has a separate cache. Focus/window-motion invalidation must
        // never relax the fresh, bracketed snapshots required by computer actions.
        internal Point? Pointer() => SamplePointer()?.Point;

        internal PointerSample SamplePointer()
        {
            lock (pointerLock)
            {
                HyprlandSnapshot state = VisualSnapshot;
                if (state == null || state.Locked) return null;
                if ((pointer == null || clock() - pointer.At > 8) && Interlocked.CompareExchange(ref pointerReading, 1, 0) == 0) _ = RefreshPointer();
                return pointer != null && clock() - pointer.At < 300 && pointer.Layout == state.LayoutId &&
                    pointer.Session == state.SessionId && pointer.Revision == Interlocked.Read(ref visualEpoch) ? pointer : null;
            }
        }

        private void PublishPointer(PointerSample sample)
        {
            bool published = false;
            lock (pointerLock)
                if (sample.Revision == Interlocked.Read(ref visualEpoch) && (pointer == null || sample.Sequence > pointer.Sequence)) { pointer = sample; published = true; }
            if (published && Volatile.Read(ref trackingPointer) != 0) { NativeMotionTrace.Emit("input", sample.Sequence, sample.At, sample.Point.X, sample.Point.Y); PointerChanged?.Invoke(sample); }
        }

        internal void TrackPointer(bool active)
        {
            Volatile.Write(ref trackingPointer, active ? 1 : 0);
            if (active && Interlocked.CompareExchange(ref pointerReading, 1, 0) == 0) _ = RefreshPointer();
        }

        private async Task RefreshPointer()
        {
            bool restart = true;
            try
            {
                do
                {
                    long revision = Interlocked.Read(ref visualEpoch);
                    HyprlandSnapshot before = VisualSnapshot;
                    string session = SessionId;
                    long sequence = Interlocked.Increment(ref pointerSequence);
                    long pointerStarted = clock();
                    Point at = HyprlandJson.Pointer(await QueryAsync("j/cursorpos", lifetime.Token).ConfigureAwait(false));
                    HyprlandSnapshot after = VisualSnapshot;
                    // A delayed pointer reply cannot cross an output/session/lock transition.
                    if (before != null && after != null && !before.Locked && !after.Locked &&
                        before.LayoutId == after.LayoutId && session == SessionId && revision == Interlocked.Read(ref visualEpoch))
                        PublishPointer(new PointerSample(at, session, after.LayoutId, revision, pointerStarted, sequence));
                    if (Volatile.Read(ref trackingPointer) == 0) break;
                    await Task.Delay(1, lifetime.Token).ConfigureAwait(false);
                } while (!lifetime.IsCancellationRequested);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { restart = false; }
            catch (Exception error) { restart = false; Volatile.Write(ref connectionError, "Hyprland pointer: " + error.Message); }
            finally
            {
                Interlocked.Exchange(ref pointerReading, 0);
                if (restart && !lifetime.IsCancellationRequested && Volatile.Read(ref trackingPointer) != 0 && Interlocked.CompareExchange(ref pointerReading, 1, 0) == 0) _ = RefreshPointer();
            }
        }

        internal List<OpenWindow> ListWindows()
        {
            try
            {
                HyprlandSnapshot state = CaptureSnapshotAsync(CancellationToken.None).GetAwaiter().GetResult();
                if (state.Locked) throw new InvalidOperationException("The desktop session is locked.");
                List<OpenWindow> result = new List<OpenWindow>();
                lock (tickets)
                {
                    foreach (OpenWindow key in tickets.Where(t => clock() - t.Value.At > 15000).Select(t => t.Key).ToArray()) tickets.Remove(key);
                    if (tickets.Count > 4096) tickets.Clear();
                    // focusHistoryID preserves the most recent non-Capri owner window.
                    foreach (HyprlandWindow window in state.Windows.OrderBy(w => w.FocusOrder < 0 ? int.MaxValue : w.FocusOrder))
                    {
                        if (window.Pid == Environment.ProcessId || window.Hidden || window.AppId.Equals("Capri", StringComparison.OrdinalIgnoreCase)) continue;
                        string identity;
                        try { identity = processIdentity(window.Pid); }
                        catch (IOException) { continue; }
                        catch (UnauthorizedAccessException) { continue; }
                        catch (InvalidOperationException) { continue; }
                        string program = window.AppId;
                        try { program = File.ReadAllText("/proc/" + window.Pid + "/comm").Trim(); }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                        OpenWindow open = new OpenWindow(new IntPtr(window.Address), window.Title, program);
                        tickets[open] = new WindowTicket(window, identity, state.SessionId, state.LayoutId, clock());
                        result.Add(open);
                    }
                }
                Volatile.Write(ref lastError, null);
                return result;
            }
            catch (Exception error) { Volatile.Write(ref lastError, error.Message); return new List<OpenWindow>(); }
        }

        internal bool Steer(OpenWindow window, string action) => SteerAsync(window, action, CancellationToken.None).GetAwaiter().GetResult();
        internal async Task<bool> SteerAsync(OpenWindow window, string action, CancellationToken cancellation)
        {
            if (action == "switch_to") action = "focus";
            await actions.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                if (action != "focus" && action != "close" && action != "force_close" && action != "minimize")
                    throw new InvalidOperationException(action == "close_tab" ? "Closing a tab needs target-bound native keyboard input and fresh verification." : "Unsupported native window action: " + action);
                WindowTicket ticket;
                lock (tickets)
                {
                    if (!tickets.Remove(window, out ticket)) throw new InvalidOperationException("Choose a fresh window from the current list.");
                }
                if (clock() - ticket.At > 15000) throw new InvalidOperationException("Window selection expired; refresh the list.");
                await EnsureDialect(cancellation).ConfigureAwait(false);
                HyprlandSnapshot state = await CaptureSnapshotAsync(cancellation).ConfigureAwait(false);
                HyprlandWindow target = Validate(ticket, state, true);
                if (action == "force_close")
                {
                    Validate(ticket, await CaptureSnapshotAsync(cancellation).ConfigureAwait(false), true);
                    cancellation.ThrowIfCancellationRequested();
                    LinuxProcess.ForceClose(target.Pid, ticket.Identity);
                    for (int check = 0; check < 20; check++)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        try { if (processIdentity(target.Pid) != ticket.Identity) { Volatile.Write(ref lastError, null); return true; } }
                        catch (IOException) { Volatile.Write(ref lastError, null); return true; }
                        catch (InvalidOperationException) { Volatile.Write(ref lastError, null); return true; }
                        await delay(TimeSpan.FromMilliseconds(50), cancellation).ConfigureAwait(false);
                    }
                    throw new InvalidOperationException("The signal was sent once; process exit is unverified.");
                }
                string dispatch;
                int restore = 0;
                if (action == "minimize")
                {
                    if (target.GroupSize > 1 || target.Workspace <= 0) throw new InvalidOperationException("Only an ungrouped window on a normal workspace can be minimized independently.");
                    dispatch = MoveWorkspace(target, "special:minimized");
                }
                else if (action == "close") dispatch = lua.Value ? "hl.dsp.window.close({window=\"" + target.Selector + "\"})" : "closewindow " + target.Selector;
                else
                {
                    if (target.WorkspaceName == "special:minimized")
                    {
                        if (!minimized.TryGetValue(target.Address, out var saved) || saved.Identity != ticket.Identity)
                            throw new InvalidOperationException("Restore this pre-existing minimized window with the compositor, then refresh the list.");
                        restore = saved.Workspace;
                        await DispatchWindow(MoveWorkspace(target, restore.ToString(CultureInfo.InvariantCulture)), ticket, target, cancellation).ConfigureAwait(false);
                        // Restore is an asynchronous mutation. Its old address may now
                        // identify another window even if the old PID is still alive.
                        // Rebind before the separate focus mutation; never retry restore.
                        HyprlandSnapshot restored = await CaptureSnapshotAsync(cancellation).ConfigureAwait(false);
                        target = Validate(ticket, restored, false);
                        if (target.Workspace != restore || target.Hidden || target.GroupSize != ticket.Window.GroupSize)
                            throw new InvalidOperationException("Restore was requested, but the intended window's restored state was not verified; focus was not sent.");
                    }
                    dispatch = lua.Value ? "hl.dsp.focus({window=\"" + target.Selector + "\"})" : "focuswindow " + target.Selector;
                }
                await DispatchWindow(dispatch, ticket, target, cancellation).ConfigureAwait(false);
                for (int attempt = 0; attempt < 15; attempt++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    HyprlandWindow[] windows = HyprlandJson.Windows(await QueryAsync("j/clients", cancellation).ConfigureAwait(false));
                    HyprlandWindow observed = Array.Find(windows, w => w.Address == target.Address);
                    if (action == "close" && observed == null) { Volatile.Write(ref lastError, null); return true; }
                    if (observed == null || observed.Pid != target.Pid || observed.AppId != target.AppId || processIdentity(observed.Pid) != ticket.Identity)
                        throw new InvalidOperationException("Window identity changed during effect verification.");
                    if (action == "minimize" && observed.WorkspaceName == "special:minimized")
                    {
                        minimized[target.Address] = (ticket.Identity, target.Workspace);
                        Volatile.Write(ref lastError, null);
                        return true;
                    }
                    if (action == "focus" && (restore == 0 || observed.Workspace == restore) && HyprlandJson.Active(await QueryAsync("j/activewindow", cancellation).ConfigureAwait(false)) == target.Address)
                    {
                        minimized.Remove(target.Address);
                        Volatile.Write(ref lastError, null);
                        return true;
                    }
                    await delay(TimeSpan.FromMilliseconds(50), cancellation).ConfigureAwait(false);
                }
                throw new InvalidOperationException(action == "close" ? "Close was requested once; the window remains open (possibly an unsaved-work prompt)." : "The compositor accepted the request, but its effect was not verified.");
            }
            catch (OperationCanceledException) { Volatile.Write(ref lastError, "Window action stopped; an already dispatched effect may have occurred."); throw; }
            catch (Exception error) { Volatile.Write(ref lastError, error.Message); return false; }
            finally { actions.Release(); }
        }

        private HyprlandWindow Validate(WindowTicket ticket, HyprlandSnapshot state, bool geometry)
        {
            if (state.Locked || ticket.Session != state.SessionId || state.SessionId != SessionId || ticket.Layout != state.LayoutId || clock() - ticket.At > 15000)
                throw new InvalidOperationException("Session, workspace or display layout changed; refresh the window list.");
            HyprlandWindow now = Array.Find(state.Windows, w => w.Address == ticket.Window.Address);
            if (now == null || now.Pid != ticket.Window.Pid || now.AppId != ticket.Window.AppId || processIdentity(now.Pid) != ticket.Identity ||
                (geometry && (now.Logical != ticket.Window.Logical || now.Workspace != ticket.Window.Workspace || now.Hidden != ticket.Window.Hidden || now.GroupSize != ticket.Window.GroupSize)))
                throw new InvalidOperationException("Selected window changed; refresh the window list.");
            return now;
        }

        private async Task EnsureDialect(CancellationToken cancellation)
        {
            if (lua.HasValue) return;
            string reply = await Request("/eval return type(hl.dsp.focus)", cancellation).ConfigureAwait(false);
            if (reply.Trim() == "ok") lua = true;
            else if (reply.Contains("only supported with the lua config manager", StringComparison.OrdinalIgnoreCase) || reply.Contains("unknown request", StringComparison.OrdinalIgnoreCase)) lua = false;
            else throw new InvalidOperationException("Cannot identify this Hyprland dispatch dialect: " + reply.Trim());
        }

        private string MoveWorkspace(HyprlandWindow target, string workspace) => lua.Value
            ? "hl.dsp.window.move({workspace=\"" + workspace + "\",follow=false,window=\"" + target.Selector + "\"})"
            : "movetoworkspacesilent " + workspace + "," + target.Selector;

        private async Task Dispatch(string expression, CancellationToken cancellation)
        {
            string response = await Request("/dispatch " + expression, cancellation).ConfigureAwait(false);
            if (response.Trim() != "ok") throw new InvalidOperationException("Compositor rejected the action: " + response.Trim());
        }

        private async Task DispatchWindow(string expression, WindowTicket ticket, HyprlandWindow target, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (ticket.Session != SessionId || processIdentity(target.Pid) != ticket.Identity)
                throw new InvalidOperationException("Selected process or session identity changed.");
            await Dispatch(expression, cancellation).ConfigureAwait(false);
        }

        internal bool MovePointer(int logicalX, int logicalY) => MovePointerAsync(logicalX, logicalY, CancellationToken.None).GetAwaiter().GetResult();
        internal async Task<bool> MovePointerAsync(int logicalX, int logicalY, CancellationToken cancellation)
        {
            await actions.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                await EnsureDialect(cancellation).ConfigureAwait(false);
                HyprlandSnapshot state = await CaptureSnapshotAsync(cancellation).ConfigureAwait(false);
                if (state.Locked || !state.Outputs.Any(m => m.Logical.Contains(new Point(logicalX, logicalY))))
                    throw new InvalidOperationException("Pointer target is outside the unlocked desktop.");
                string x = logicalX.ToString(CultureInfo.InvariantCulture), y = logicalY.ToString(CultureInfo.InvariantCulture);
                await Dispatch(lua.Value ? "hl.dsp.cursor.move({x=" + x + ",y=" + y + "})" : "movecursor " + x + " " + y, cancellation).ConfigureAwait(false);
                Point observed = HyprlandJson.Pointer(await QueryAsync("j/cursorpos", cancellation).ConfigureAwait(false));
                bool verified = Math.Abs(observed.X - logicalX) <= 1 && Math.Abs(observed.Y - logicalY) <= 1;
                Volatile.Write(ref lastError, verified ? null : "Pointer movement effect was not verified.");
                return verified;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) { Volatile.Write(ref lastError, error.Message); return false; }
            finally { actions.Release(); }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            lifetime.Cancel();
            Volatile.Write(ref snapshot, null);
            Volatile.Write(ref visual, null);
            // Cancellation closes owned sockets. Do not block Avalonia's UI on workers.
            _ = Task.WhenAll(events, refresh).ContinueWith(_ => lifetime.Dispose(), TaskScheduler.Default);
        }
    }

    internal sealed class HyprlandDesk : Desk
    {
        private readonly HyprlandDesktop desktop;
        private HyprlandDisplayMap[] maps = Array.Empty<HyprlandDisplayMap>();
        private string mappingError;
        private string mappedLayout, mappedSession;
        internal HyprlandDesk(HyprlandDesktop desktop) { this.desktop = desktop; }
        internal HyprlandDesktop Desktop => desktop;
        internal HyprlandDisplayMap[] Maps => maps;
        internal HyprlandDisplayMap ActiveMap => Array.Find(maps, m => m.Output.Focused) ?? (maps.Length > 0 ? maps[0] : null);
        internal HyprlandDisplayMap MapAt(PixelPoint point) => Array.Find(maps, m => m.AvaloniaBounds.Contains(point));
        internal override string Name => "Hyprland (native IPC / XWayland surface)";
        internal override string Debug() => desktop.LastError ?? mappingError ?? "Native windows, workspaces and pointer; output-specific XWayland coordinates.";

        internal override void BindScreens(IReadOnlyList<Screen> screens)
        {
            HyprlandSnapshot state = desktop.VisualSnapshot;
            // Window-motion events can invalidate an action snapshot without changing
            // output identity. Retain visual geometry until a fresh layout replaces it;
            // computer actions still require their separate fresh snapshot/target checks.
            if (state == null) return;
            List<HyprlandDisplayMap> bound = new List<HyprlandDisplayMap>();
            foreach (HyprlandOutput output in state.Outputs)
            {
                Screen[] matching = screens.Where(s => s.DisplayName == output.Name).ToArray();
                if (matching.Length == 1) bound.Add(new HyprlandDisplayMap(output, matching[0].Bounds));
                else
                {
                    // XRandR names may be unavailable. A unique exact logical rectangle
                    // is safe at scale 1; never guess by enumeration order or primary DPI.
                    matching = screens.Where(s => s.Bounds.X == output.Logical.X && s.Bounds.Y == output.Logical.Y &&
                        s.Bounds.Width == output.Logical.Width && s.Bounds.Height == output.Logical.Height).ToArray();
                    if (matching.Length == 1) bound.Add(new HyprlandDisplayMap(output, matching[0].Bounds));
                }
            }
            maps = bound.ToArray();
            mappedLayout = state.LayoutId; mappedSession = state.SessionId;
            mappingError = maps.Length == state.Outputs.Length ? null : "An output could not be matched to Avalonia/XRandR; its native ledges and pointer are unavailable.";
        }

        internal override List<Spot> Windows(ICollection<long> skip)
        {
            HyprlandSnapshot state = desktop.Snapshot;
            if (state == null || state.Locked) return new List<Spot>();
            return state.Windows.Where(w => w.Pid != Environment.ProcessId && !skip.Contains(w.Address) && state.Visible(w))
                .OrderByDescending(w => w.Fullscreen).ThenByDescending(w => w.Floating).ThenBy(w => w.FocusOrder < 0 ? int.MaxValue : w.FocusOrder)
                .SelectMany(w => maps.Select(m => ToSpot(w, m))).Where(s => s != null).ToList();
        }

        private Spot ToSpot(HyprlandWindow window, HyprlandDisplayMap map)
        {
            if (map == null) return null;
            // Visible floating windows and pinned windows can span independently
            // active workspaces; tiled/fullscreen windows stay on their own output.
            if (map.Output.Id != window.Monitor && !window.Pinned && !(window.Floating && window.Fullscreen == 0)) return null;
            Rect visible = window.Logical.Intersect(map.Output.Logical);
            if (visible.Width <= 0 || visible.Height <= 0) return null;
            PixelRect bounds = map.ToAvalonia(visible);
            PixelRect fullBounds = map.ToAvalonia(window.Logical);
            bool big = fullBounds.Width >= 120 && fullBounds.Height >= 60;
            return new Spot { Window = window.Address, Bounds = bounds, Solid = big,
                Seat = big && window.Fullscreen == 0 && visible.Top == window.Logical.Top && !Fills(fullBounds) };
        }
        internal override Spot Find(long window)
        {
            HyprlandSnapshot state = desktop.Snapshot;
            HyprlandWindow found = state == null ? null : Array.Find(state.Windows, w => w.Address == window && state.Visible(w));
            return found == null ? null : ToSpot(found, Array.Find(maps, m => m.Output.Id == found.Monitor));
        }
        internal override Spot Find(long window, PixelPoint near)
        {
            HyprlandSnapshot state = desktop.Snapshot;
            HyprlandWindow found = state == null ? null : Array.Find(state.Windows, w => w.Address == window && state.Visible(w));
            if (found == null) return null;
            HyprlandDisplayMap map = Array.Find(maps, m => m.AvaloniaBounds.Contains(near));
            return ToSpot(found, map) ?? Find(window);
        }
        internal override bool Alive(long window) => desktop.Snapshot?.Windows.Any(w => w.Address == window) == true;
        internal Point? PointerPoint()
        {
            var sample = desktop.SamplePointer();
            if (sample == null || sample.Layout != mappedLayout || sample.Session != mappedSession) return null;
            HyprlandDisplayMap map = Array.Find(maps, m => m.Output.Logical.Contains(sample.Point));
            return map?.ToAvaloniaPoint(sample.Point);
        }
        internal override PixelPoint? Pointer()
        {
            Point? point = PointerPoint();
            return point == null ? null : new PixelPoint((int)Math.Round(point.Value.X), (int)Math.Round(point.Value.Y));
        }
        internal override PixelRect? WorkAt(PixelPoint point)
        {
            HyprlandDisplayMap map = Array.Find(maps, m => m.AvaloniaBounds.Contains(point));
            return map?.ToAvalonia(map.Output.WorkingArea);
        }
        internal override IEnumerable<PixelRect> WorkAreas => maps.Length == 0 ? null : maps.Select(m => m.ToAvalonia(m.Output.WorkingArea));
        // Current is shared with computer actions; the application owns its lifetime.
        internal override PixelPoint? Grabbed() => Pointer();
        internal override void TrackDrag(bool active) => desktop.TrackPointer(active);
        public override void Dispose() { desktop.TrackPointer(false); }
    }
}

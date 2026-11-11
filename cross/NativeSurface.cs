using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace CapriDesktop
{
    internal enum NativeSurfaceRole { Passive, Composer, Mainframe, FileEditor }

    // One binding per actual window, not a same-PID/global compositor rule. All
    // IPC runs outside the UI thread. Destroying/unmapping the window ends its policy.
    internal sealed class NativeSurface
    {
        private static readonly ConditionalWeakTable<Window, NativeSurface> bindings = new ConditionalWeakTable<Window, NativeSurface>();
        private static long nextSurface;
        private readonly string nativeClass = "Capri.NativeSurface." + Interlocked.Increment(ref nextSurface).ToString(CultureInfo.InvariantCulture);
        private readonly SemaphoreSlim wake = new SemaphoreSlim(0, 1);
        private sealed record ScreenBounds(string Name, PixelRect Bounds);
        private sealed record MoveRequest(PixelPoint Position, ScreenBounds[] Screens, string Output, string Layout, long Generation, long Sequence, long SourceAt, PixelSize? Size);
        private MoveRequest pendingMove;
        private Size? appliedReplySize;
        private long moveGeneration;
        private readonly ConcurrentQueue<TaskCompletionSource<bool>> fences = new();
        private readonly object fenceGate = new();
        private bool acceptingFences;
        private readonly Window window;
        private readonly NativeSurfaceRole role;
        private readonly bool acceptsInput;
        private readonly HyprlandDesktop desktop;
        private CancellationTokenSource visible;
        private Task worker = Task.CompletedTask;
        private long xid;
        private string title;
        private readonly string process = LinuxProcess.Identity(Environment.ProcessId);

        private NativeSurface(Window window, NativeSurfaceRole role, bool acceptsInput)
        {
            this.window = window; this.role = role; this.acceptsInput = acceptsInput; desktop = HyprlandDesktop.Current;
            // Public Avalonia API: identify the actual native window even when two
            // settings sheets have the same title. This changes only our own WM_CLASS.
            if (role == NativeSurfaceRole.Passive) nativeClass = NativePassive.ClassPrefix + (acceptsInput ? "." : ".Decoration.") + Interlocked.Increment(ref nextSurface);
            X11Properties.SetWmClass(window, nativeClass);
            window.PropertyChanged += (_, e) =>
            {
                if (e.Property != Visual.IsVisibleProperty) return;
                if (!window.IsVisible) Stop();
            };
            window.Opened += (_, _) => Start();
            window.Closed += (_, _) => Stop();
        }

        internal static void Register(Window window, NativeSurfaceRole role, bool acceptsInput = true)
        {
            if (OperatingSystem.IsLinux() && NiriDesktop.Current != null) { NiriDesktop.Current.Register(window); return; }
            if (HyprlandDesktop.Current != null) bindings.Add(window, new NativeSurface(window, role, acceptsInput));
        }

        internal static Task Prepare(Window window, CancellationToken cancellation)
        {
            return bindings.TryGetValue(window, out NativeSurface binding) && binding.role == NativeSurfaceRole.Passive
                ? NativePassive.Prepare(window, binding.nativeClass, cancellation) : Task.CompletedTask;
        }

        internal static bool TryMove(Window window, PixelPoint position, HyprlandDisplayMap target = null, long sequence = 0, long sourceAt = 0, PixelSize? replySize = null)
        {
            if (OperatingSystem.IsLinux() && NiriDesktop.Current?.TryMove(window, position) == true) return true;
            if (!bindings.TryGetValue(window, out NativeSurface binding)) return false;
            var state = binding.desktop.VisualSnapshot;
            if (state == null || state.Locked) return true; // No unbound intent may cross an unknown layout.
            var screens = window.Screens.All.Select(s => new ScreenBounds(s.DisplayName, s.Bounds)).ToArray();
            // A held seam transition selects the pointer's output explicitly: the window
            // origin can legitimately extend beyond that output by the fixed grab vector.
            string output = target?.Output.Name ?? window.Screens.ScreenFromPoint(new PixelPoint(
                position.X + (int)(window.Bounds.Width * window.DesktopScaling / 2),
                position.Y + (int)(window.Bounds.Height * window.DesktopScaling / 2)))?.DisplayName;
            Volatile.Write(ref binding.pendingMove, new MoveRequest(position, screens, output, state.LayoutId, Interlocked.Read(ref binding.moveGeneration), sequence, sourceAt, window is Bubble ? replySize : null));
            if (window.IsVisible && binding.worker.IsCompleted) binding.Start();
            if (binding.wake.CurrentCount == 0) binding.wake.Release();
            return true;
        }

        internal static Task<bool> FlushMoves(Window window)
        {
            if (!bindings.TryGetValue(window, out NativeSurface binding)) return Task.FromResult(true);
            var fence = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (binding.fenceGate)
            {
                if (binding.visible == null || !binding.acceptingFences) return Task.FromResult(false);
                binding.fences.Enqueue(fence);
            }
            if (binding.wake.CurrentCount == 0) binding.wake.Release();
            return fence.Task;
        }

        internal static void CancelMoves(Window window)
        {
            if (OperatingSystem.IsLinux()) NiriDesktop.Current?.CancelMoves(window);
            if (!bindings.TryGetValue(window, out NativeSurface binding)) return;
            Interlocked.Increment(ref binding.moveGeneration);
            Interlocked.Exchange(ref binding.pendingMove, null);
        }

        private async Task<bool> Move(string address, MoveRequest move, CancellationToken cancellation)
        {
            var state = desktop.VisualSnapshot;
            if (state == null || state.Locked || move.Layout != state.LayoutId || move.Generation != Interlocked.Read(ref moveGeneration)) return false;
            var output = Array.Find(state.Outputs, o => o.Name == move.Output);
            var screen = Array.Find(move.Screens, s => s.Name == move.Output);
            if (output == null || screen == null) return false; // Output disappeared after enqueue; discard this generation.
            var map = new HyprlandDisplayMap(output, screen.Bounds);
            Point point = map.ToLogical(move.Position);
            string monitor = JsonSerializer.Serialize(output.Name);
            string workspace = JsonSerializer.Serialize(output.Workspace.ToString(CultureInfo.InvariantCulture));
            string code = Guard(address) + "assert(w,'Capri window unmapped'); if w.workspace.id~=" + output.Workspace +
                " then local pinned=w.pinned; if pinned then apply(hl.dsp.window.pin({action=\"disable\",window=w})) end; apply(hl.dsp.window.move({workspace=" + workspace + ",follow=false,window=w})); if pinned then apply(hl.dsp.window.pin({action=\"enable\",window=w})) end end; if not w.monitor or w.monitor.name~=" + monitor +
                " then apply(hl.dsp.window.move({monitor=" + monitor + ",follow=false,window=w})) end; " +
                "";
            Size? replySize = move.Size is PixelSize pixels ? new Size(
                pixels.Width * output.Logical.Width / screen.Bounds.Width,
                pixels.Height * output.Logical.Height / screen.Bounds.Height) : null;
            // This mapped XWayland bubble can retain its old compositor geometry while
            // Avalonia draws a larger buffer. Resize only this identity-bound reply surface,
            // on this existing worker before applying its newly measured position.
            if (replySize.HasValue && replySize != appliedReplySize)
                code += "; apply(hl.dsp.window.resize({x=" + replySize.Value.Width.ToString("R", CultureInfo.InvariantCulture) + ",y=" + replySize.Value.Height.ToString("R", CultureInfo.InvariantCulture) + ",relative=false,window=w}))";
            code += "; apply(hl.dsp.window.move({x=" + point.X.ToString("R", CultureInfo.InvariantCulture) + ",y=" + point.Y.ToString("R", CultureInfo.InvariantCulture) + ",relative=false,window=w}))";
            NativeMotionTrace.Emit("request", move.Sequence, move.SourceAt, point.X, point.Y);
            string reply = await desktop.SurfaceRequest("/eval " + code, cancellation).ConfigureAwait(false);
            NativeMotionTrace.Emit("ack", move.Sequence, move.SourceAt, point.X, point.Y);
            if (reply.Trim() != "ok") throw new InvalidOperationException("Native surface movement failed: " + reply.Trim());
            appliedReplySize = replySize;
            return true;
        }

        private void Start()
        {
            Stop();
            xid = window.TryGetPlatformHandle()?.Handle.ToInt64() ?? 0;
            title = window.Title;
            var cancellation = new CancellationTokenSource();
            visible = cancellation;
            // Serialize remaps with prior cleanup so an older hide cannot undo a new session.
            Task previous = worker;
            worker = Task.Run(async () =>
            {
                try { await previous.ConfigureAwait(false); await Run(cancellation).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                catch (Exception error)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        Program.Crashed(new InvalidOperationException("Capri surface policy: " + error.Message, error));
                        if (role == NativeSurfaceRole.Composer && visible == cancellation) window.Hide();
                    });
                }
            });
        }

        private void Stop()
        {
            var old = visible; visible = null;
            if (old == null) return;
            CancelMoves(window);
            old.Cancel();
            _ = worker.ContinueWith(_ => old.Dispose(), TaskScheduler.Default);
        }

        private async Task Run(CancellationTokenSource owner)
        {
            CancellationToken cancellation = owner.Token;
            lock (fenceGate) acceptingFences = true;
            string address = null;
            long revision = -1;
            int misses = 0;
            bool focused = false;
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    if (role == NativeSurfaceRole.Composer && focused && desktop.VisualSnapshot == null)
                    {
                        Dispatcher.UIThread.Post(() => { if (visible == owner) window.Hide(); });
                        return;
                    }
                    if (desktop.VisualSnapshot?.Locked == true)
                    {
                        if (role == NativeSurfaceRole.Composer)
                        {
                            Dispatcher.UIThread.Post(() => { if (visible == owner) window.Hide(); });
                            return;
                        }
                        await Task.Delay(200, cancellation).ConfigureAwait(false);
                        continue;
                    }
                    if (address == null || revision != desktop.SurfaceRevision)
                    {
                        // PID reuse cannot occur inside this process. Validate its executable
                        // when binding/remapping, not with /proc I/O for every drag position.
                        if (LinuxProcess.Identity(Environment.ProcessId) != process) throw new InvalidOperationException("Native process identity changed.");
                        if (role == NativeSurfaceRole.Passive) await NativePassive.EnsureRule(cancellation).ConfigureAwait(false);
                        using JsonDocument clients = JsonDocument.Parse(await desktop.QueryAsync("j/clients", cancellation).ConfigureAwait(false));
                        address = null;
                        foreach (JsonElement row in clients.RootElement.EnumerateArray())
                        {
                            if (HyprlandJson.Int(row, "pid") != Environment.ProcessId || HyprlandJson.String(row, "class") != nativeClass || HyprlandJson.String(row, "title") != title || !HyprlandJson.Bool(row, "mapped")) continue;
                            // Hyprland stableId is NOT the X11 window ID. Require a unique
                            // per-window class within this live process, then bind its address.
                            if (address != null) throw new InvalidOperationException("Ambiguous native surface identity.");
                            address = "address:0x" + HyprlandJson.Address(HyprlandJson.String(row, "address")).ToString("x", CultureInfo.InvariantCulture);
                        }
                        if (address == null)
                        {
                            if (++misses >= 40) throw new InvalidOperationException("Owned native window did not map within two seconds (XID " + xid.ToString("x") + ").");
                            await Task.Delay(50, cancellation).ConfigureAwait(false); continue;
                        }
                        long applyingRevision = desktop.SurfaceRevision;
                        appliedReplySize = null;
                        await Apply(address, cancellation).ConfigureAwait(false);
                        revision = applyingRevision;
                        if (role == NativeSurfaceRole.Composer && !focused)
                        {
                            focused = true;
                            Dispatcher.UIThread.Post(() => { if (!cancellation.IsCancellationRequested && window.IsVisible) window.Activate(); });
                        }
                    }
                    var completing = new List<TaskCompletionSource<bool>>();
                    while (fences.TryDequeue(out var fence)) completing.Add(fence);
                    MoveRequest move = Interlocked.Exchange(ref pendingMove, null);
                    try
                    {
                        bool applied = move == null || await Move(address, move, cancellation).ConfigureAwait(false);
                        var state = desktop.VisualSnapshot;
                        applied &= state != null && !state.Locked && visible == owner;
                        foreach (var fence in completing) fence.TrySetResult(applied);
                    }
                    catch (Exception error) { foreach (var fence in completing) fence.TrySetException(error); throw; }
                    await wake.WaitAsync(200, cancellation).ConfigureAwait(false);
                }
            }
            finally
            {
                lock (fenceGate)
                {
                    acceptingFences = false;
                    while (fences.TryDequeue(out var fence)) fence.TrySetCanceled();
                }
                if (role == NativeSurfaceRole.Composer && address != null)
                {
                    // No cancellation token here: hiding must release an acquired focus policy.
                    string code = Guard(address) + "if w then apply(hl.dsp.window.set_prop({prop=\"stay_focused\",value=\"false\",window=w})) end";
                    await desktop.SurfaceRequest("/eval " + code, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        private string Guard(string address) => "local function apply(d) local r=hl.dispatch(d); assert(r and r.ok, r and r.error or 'Capri dispatcher failed') end; local w=hl.get_window(" + JsonSerializer.Serialize(address) + "); if w and (w.pid~=" + Environment.ProcessId + " or w.title~=" + JsonSerializer.Serialize(title) + " or w[\"class\"]~=" + JsonSerializer.Serialize(nativeClass) + ") then error('Capri surface identity changed') end; ";

        private async Task Apply(string address, CancellationToken cancellation)
        {
            bool passive = role == NativeSurfaceRole.Passive;
            // The pinned Avalonia X11 backend cannot clip compositor blur to the
            // editor's authored panel. Full-window blur leaks into its transparent
            // header/margins, so keep the truthful transparent fallback instead.
            var properties = new (string Name, string Value)[] {
                ("no_blur", "true"), ("no_shadow", "true"), ("no_dim", "true"),
                ("border_size", "0"), ("rounding", "0"), ("decorate", "false"),
                ("no_anim", "true"), ("no_focus", acceptsInput ? "false" : "true"),
                ("no_follow_mouse", passive ? "true" : "false"),
                ("stay_focused", role == NativeSurfaceRole.Composer ? "true" : "false")
            };
            StringBuilder code = new StringBuilder(Guard(address) + "assert(w,'Capri window unmapped'); ");
            code.Append("apply(hl.dsp.window.float({action=\"enable\",window=w})); ");
            if (role != NativeSurfaceRole.Mainframe && role != NativeSurfaceRole.FileEditor) code.Append("apply(hl.dsp.window.pin({action=\"enable\",window=w})); ");
            // Passive X11 utilities never transfer keyboard focus; no restore dispatcher.
            foreach (var property in properties)
                code.Append("apply(hl.dsp.window.set_prop({prop=\"").Append(property.Name).Append("\",value=\"").Append(property.Value).Append("\",window=w})); ");
            string reply = await desktop.SurfaceRequest("/eval " + code, cancellation).ConfigureAwait(false);
            if (reply.Trim() != "ok") throw new InvalidOperationException("The compositor rejected its scoped Lua surface policy: " + reply.Trim());
            foreach (var property in properties)
            {
                string effective = await desktop.SurfaceRequest("/getprop " + address + " " + property.Name, cancellation).ConfigureAwait(false);
                if (effective.Trim() != property.Value) throw new InvalidOperationException("Compositor property did not take effect: " + property.Name);
            }
        }
    }
}

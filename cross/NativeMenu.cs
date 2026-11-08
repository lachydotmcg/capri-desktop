using System;
using System.Threading;
using Avalonia;
using Avalonia.Input.Raw;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.Diagnostics;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;

namespace CapriDesktop
{
    internal static class NativeMenuStack
    {
        internal static void Bind(Window owner, ContextMenu menu)
        {
            menu.AttachedToVisualTree += (_, _) =>
            {
                if (HyprlandDesktop.Current != null && TopLevel.GetTopLevel(menu) is TopLevel root)
                    NativeX11.Configure(root, false);
            };
            CancellationTokenSource pending = null;
            owner.PropertyChanged += (_, change) => { if (change.Property == Visual.IsVisibleProperty && !owner.IsVisible) { pending?.Cancel(); menu.Close(); } };
            owner.Closed += (_, _) => { pending?.Cancel(); menu.Close(); };
            menu.Opening += async (_, args) =>
            {
                if (NativePassive.Ready) return;
                args.Cancel = true;
                if (pending != null) return;
                var request = new CancellationTokenSource(TimeSpan.FromSeconds(3)); pending = request;
                try
                {
                    await NativePassive.EnsureRule(request.Token);
                    if (!request.IsCancellationRequested && owner.IsVisible && NativePassive.Ready) { pending = null; menu.Open(owner); }
                }
                catch (OperationCanceledException) { }
                catch (Exception error) { Program.Crashed(error); }
                finally { if (pending == request) pending = null; request.Dispose(); }
            };
            var hooked = new ConditionalWeakTable<MenuItem, object>();
            var provider = (IPopupHostProvider)menu;
            NativeMenuInput input = null;
            var hosts = new HashSet<TopLevel>();
            void Host(IPopupHost host) { if (host != null) host.Topmost = owner.Topmost; }
            void RootHost(IPopupHost host)
            {
                Host(host); input?.Dispose(); input = null; hosts.Clear();
                if (HyprlandDesktop.Current != null && host is TopLevel root) { hosts.Add(root); input = new NativeMenuInput(root, () => { if (ReferenceEquals(provider.PopupHost, root) && root.IsVisible) menu.Close(); }, hosts); }
            }
            menu.Closed += (_, _) => { input?.Dispose(); input = null; };
            void Items(IEnumerable<object> items)
            {
                foreach (object child in items)
                {
                    if (child is not MenuItem item) continue;
                    if (!hooked.TryGetValue(item, out _))
                    {
                        hooked.Add(item, new object());
                        item.TemplateApplied += (_, args) =>
                        {
                            var popup = args.NameScope.Find<Popup>("PART_Popup");
                            if (popup == null) return;
                            popup.Topmost = owner.Topmost;
                            popup.Opened += (_, _) => { popup.Topmost = owner.Topmost; Host(popup.Host); if (popup.Host is TopLevel childHost) hosts.Add(childHost); };
                            popup.Closed += (_, _) => hosts.RemoveWhere(host => !host.IsVisible);
                        };
                    }
                    Items(item.Items);
                }
            }
            Items(menu.Items);
            provider.PopupHostChanged += RootHost;
            menu.Opening += (_, _) => Items(menu.Items);
            owner.PropertyChanged += (_, change) =>
            {
                if (change.Property == Window.TopmostProperty) Host(provider.PopupHost);
            };
        }
    }
    // Only while the native context menu is open. Owner-events preserves native
    // submenu routing; Avalonia's existing light-dismiss observer handles outside
    // presses. No pointer confinement, replay, or keyboard interception.
    internal sealed class NativeMenuInput : IDisposable, IObserver<RawInputEventArgs>
    {
        private readonly TopLevel root;
        private readonly Action close;
        private readonly HashSet<TopLevel> hosts;
        private readonly HashSet<string> outside = new();
        private IDisposable subscription;
        private IntPtr display, handle;
        private IDisposable retry;
        private bool grabbed, disposed;
        private long grabLease;
        private int attempts;
        internal NativeMenuInput(TopLevel root, Action close, HashSet<TopLevel> hosts)
        {
            this.root = root; this.close = close; this.hosts = hosts;
            try
            {
                var x = NativeX11.Connection(root); display = x.Display; handle = x.Handle;
                subscription = ComposerInput.Subscribe(this);
                Acquire();
            }
            catch { Dispose(); throw; }
        }
        private void Acquire()
        {
            if (disposed || !root.IsVisible) return;
            int status = XGrabPointer(display, handle, 1, (1u << 2) | (1u << 3) | (1u << 6), 1, 1, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (status == 0) { grabLease = NativeX11.OwnPointerGrab(display); grabbed = true; return; }
            if ((status == 1 || status == 3) && ++attempts < 25) { retry = DispatcherTimer.RunOnce(Acquire, TimeSpan.FromMilliseconds(10)); return; }
            Dispose();
            Dispatcher.UIThread.Post(() => { close(); Program.Crashed(new InvalidOperationException("Menu pointer grab failed: " + status)); });
        }
        public void OnNext(RawInputEventArgs value)
        {
            if (!grabbed || disposed || value is not RawPointerEventArgs) return;
            string type = ComposerInput.kind.GetValue(value).ToString();
            var target = ComposerInput.root.GetValue(value) as TopLevel;
            bool inside = target != null && hosts.Contains(target) && new Rect(target.ClientSize).Contains((Point)ComposerInput.position.GetValue(value));
            if (type.EndsWith("ButtonDown", StringComparison.Ordinal) && (outside.Count > 0 || !inside)) outside.Add(type.Substring(0, type.Length - 4));
            if (outside.Count == 0) return;
            ComposerInput.handled.SetValue(value, true);
            if (type.EndsWith("ButtonUp", StringComparison.Ordinal) && outside.Remove(type.Substring(0, type.Length - 2)) && outside.Count == 0) close();
        }
        public void OnError(Exception error) { close(); Program.Crashed(error); }
        public void OnCompleted() { close(); }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; retry?.Dispose(); subscription?.Dispose(); subscription = null; outside.Clear();
            if (grabbed) { NativeX11.ReleasePointerGrab(display, grabLease); grabbed = false; }
        }
        [DllImport("libX11.so.6")] private static extern int XGrabPointer(IntPtr d, IntPtr w, int ownerEvents, uint events, int pointerMode, int keyboardMode, IntPtr confine, IntPtr cursor, IntPtr time);
        [DllImport("libX11.so.6")] private static extern int XUngrabPointer(IntPtr d, IntPtr time);
        [DllImport("libX11.so.6")] private static extern int XFlush(IntPtr d);
    }

}

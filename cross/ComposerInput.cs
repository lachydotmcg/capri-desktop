using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Raw;
using Avalonia.Threading;

namespace CapriDesktop
{
    // Avalonia 11.3.22 makes its raw input observer internal. This narrowly checked
    // adapter uses the same preprocessor boundary as its own popup implementation.
    // It sees only events already delivered to THIS application, never a global
    // input stream. Hyprland's scoped stay_focused policy supplies outside buttons.
    // Keep the pinned API contract test when upgrading Avalonia; fail closed if it changes.
    internal sealed class ComposerInput : IObserver<RawInputEventArgs>, IDisposable
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        internal static readonly PropertyInfo kind = Required(typeof(RawPointerEventArgs), "Type");
        internal static readonly PropertyInfo position = Required(typeof(RawPointerEventArgs), "Position");
        internal static readonly PropertyInfo root = Required(typeof(RawPointerEventArgs), "Root");
        internal static readonly PropertyInfo handled = Required(typeof(RawInputEventArgs), "Handled");
        private readonly Window window;
        private IDisposable subscription;
        private IntPtr display;
        private bool grabbed, disposed;
        private long grabLease;
        private IntPtr handle;
        private IDisposable retry;
        private int attempts;
        internal bool Ready => grabbed;
        [DllImport("libX11.so.6")] private static extern int XGrabPointer(IntPtr display, IntPtr window, int ownerEvents, uint events, int pointerMode, int keyboardMode, IntPtr confine, IntPtr cursor, IntPtr time);
        [DllImport("libX11.so.6")] private static extern int XUngrabPointer(IntPtr display, IntPtr time);
        [DllImport("libX11.so.6")] private static extern int XFlush(IntPtr display);
        private readonly HashSet<string> outside = new HashSet<string>(StringComparer.Ordinal);

        private static PropertyInfo Required(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
                if (current.GetProperty(name, Members | BindingFlags.DeclaredOnly) is PropertyInfo property) return property;
            throw new NotSupportedException("Pinned Avalonia composer input API changed: " + name);
        }

        internal static IDisposable Subscribe(IObserver<RawInputEventArgs> observer)
        {
            object manager = Required(typeof(Application), "InputManager").GetValue(Application.Current);
            return ((IObservable<RawInputEventArgs>)Required(manager.GetType(), "PreProcess").GetValue(manager)).Subscribe(observer);
        }

        internal ComposerInput(Window window)
        {
            this.window = window;
            if (position.PropertyType != typeof(Point) || handled.PropertyType != typeof(bool) || handled.SetMethod == null)
                throw new NotSupportedException("Pinned Avalonia input contract changed.");
            subscription = Subscribe(this);
            try
            {
                // Use Avalonia's OWN X11 connection so grabbed events reach its normal
                // input pipeline. A second connection would steal inside text selection.
                object platform = Required(typeof(Window), "PlatformImpl").GetValue(window);
                object info = platform.GetType().GetField("_x11", Members)?.GetValue(platform)
                    ?? throw new NotSupportedException("Pinned Avalonia X11 connection contract changed.");
                display = (IntPtr)Required(info.GetType(), "Display").GetValue(info);
                handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                if (display == IntPtr.Zero || handle == IntPtr.Zero)
                    throw new InvalidOperationException("The composer native input handle is unavailable.");
                Acquire();
            }
            catch { Dispose(); throw; }
        }

        private void Acquire()
        {
            if (disposed || !window.IsVisible) return;
            int status = XGrabPointer(display, handle, 0, (1u << 2) | (1u << 3) | (1u << 6), 1, 1, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (status == 0) { grabLease = NativeX11.OwnPointerGrab(display); grabbed = true; XFlush(display); return; }
            // Opened can precede XWayland's asynchronous MapRequest acknowledgement,
            // especially after Hide/Show. Retry that transient state without blocking UI.
            if ((status == 3 || status == 1) && ++attempts < 25)
            {
                retry = DispatcherTimer.RunOnce(Acquire, TimeSpan.FromMilliseconds(10));
                return;
            }
            Dispatcher.UIThread.Post(() =>
            {
                if (disposed) return;
                window.Hide();
                Program.Crashed(new InvalidOperationException("Composer pointer grab failed with native status " + status));
            });
        }

        public void OnNext(RawInputEventArgs value)
        {
            if (!grabbed || !window.IsVisible || value is not RawPointerEventArgs || !ReferenceEquals(root.GetValue(value), window)) return;
            string type = kind.GetValue(value).ToString();
            if (type.EndsWith("ButtonDown", StringComparison.Ordinal) && (outside.Count > 0 || !new Rect(window.ClientSize).Contains((Point)position.GetValue(value))))
                outside.Add(type.Substring(0, type.Length - 4));
            if (outside.Count == 0) return;
            handled.SetValue(value, true);
            // Consume the original pair. Never replay a click or deliver an unmatched
            // release to an underlying Wayland application after hiding on button-down.
            if (type.EndsWith("ButtonUp", StringComparison.Ordinal) && outside.Remove(type.Substring(0, type.Length - 2)) && outside.Count == 0) window.Hide();
        }
        public void OnError(Exception error) { window.Hide(); Program.Crashed(error); }
        public void OnCompleted() { window.Hide(); }
        public void Dispose()
        {
            disposed = true; retry?.Dispose(); retry = null;
            subscription?.Dispose(); subscription = null; outside.Clear();
            if (grabbed) { NativeX11.ReleasePointerGrab(display, grabLease); grabbed = false; }
            display = IntPtr.Zero;
        }
    }
}

using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Diagnostics;
using Avalonia.Controls.Primitives;
using Avalonia.Media.Imaging;
using Avalonia.Rendering;
using Avalonia.VisualTree;
using Avalonia.Controls.Presenters;
using CapriDesktop;

// Diagnostic fixture only. No recorder or control endpoint belongs in the app.
// Native attribute reads use a separate worker-owned XCB connection so observing
// a popup never inserts a round trip into its UI-owned X request stream.
// XCB replies report stale-window errors explicitly without changing Xlib handlers.
internal static class MenuDiagnostics
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int MaximumRecords = 4096;
    private static readonly object outputGate = new();
    private static readonly HashSet<TopLevel> roots = new();
    private static readonly HashSet<MenuItem> items = new();
    private static readonly SemaphoreSlim nativeQueries = new(4, 4);
    private static string destination;
    private static int records;

    internal static void Bind(string directory, ContextMenu menu)
    {
        destination = Path.Combine(directory, "menu-diagnostic.jsonl");
        menu.Opening += (_, e) => Capture("opening", menu, new { cancelled = e.Cancel });
        menu.Opened += (_, _) => Capture("opened", menu);
        menu.Closing += (_, e) => Capture("closing", menu, new { cancelled = e.Cancel });
        menu.Closed += (_, _) => Capture("closed", menu);
        menu.AttachedToVisualTree += (_, _) =>
        {
            WatchRoot(TopLevel.GetTopLevel(menu), "root");
            Capture("attached", menu);
        };
        menu.DetachedFromVisualTree += (_, _) => Capture("detached", menu);
        ((IPopupHostProvider)menu).PopupHostChanged += host =>
        {
            WatchRoot(host as TopLevel, "root");
            Capture("host-changed", menu);
        };
        WatchItems(menu.Items);
        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
            Write("dispatcher-error", new { type = e.Exception.GetType().FullName, message = Limited(e.Exception.Message, 1024) });
    }

    private static void WatchItems(IEnumerable<object> children)
    {
        foreach (var item in children.OfType<MenuItem>())
        {
            if (items.Count >= 256 || !items.Add(item)) continue;
            item.TemplateApplied += (_, e) =>
            {
                if (e.NameScope.Find<Popup>("PART_Popup") is not Popup popup) return;
                popup.Opened += (_, _) =>
                {
                    WatchRoot(popup.Host as TopLevel, "submenu");
                    Write("submenu-opened", new { header = Limited(item.Header?.ToString(), 120), host = Root(popup.Host as TopLevel) });
                };
                popup.Closed += (_, _) => Write("submenu-closed", new { header = Limited(item.Header?.ToString(), 120) });
            };
            WatchItems(item.Items);
        }
    }

    private static void WatchRoot(TopLevel root, string role)
    {
        if (root == null || roots.Count >= 128 || !roots.Add(root)) return;
        int layouts = 0, scenes = 0;
        root.LayoutUpdated += (_, _) =>
        {
            if (layouts++ == 0) Write("first-layout", new { role, host = Root(root) });
        };
        var renderer = ((IRenderRoot)root).Renderer;
        EventHandler<SceneInvalidatedEventArgs> scene = (_, e) =>
        {
            if (scenes++ < 2) Write("scene-invalidated", new { role, dirty = e.GetType().GetProperty("DirtyRect", Members)?.GetValue(e)?.ToString(), host = Root(root) });
        };
        renderer.GetType().GetEvent("SceneInvalidated", Members)?.GetAddMethod(true)?.Invoke(renderer, new object[] { scene });
        root.Opened += (_, _) => Write("host-opened", new { role, host = Root(root) });
        root.Closed += (_, _) => Write("host-closed", new { role, xid = root.TryGetPlatformHandle()?.Handle.ToInt64() });
        Write("watch-host", new { role, host = Root(root) });
    }

    internal static void Capture(string stage, ContextMenu menu, object detail = null)
    {
        var host = ((IPopupHostProvider)menu).PopupHost as TopLevel ?? TopLevel.GetTopLevel(menu);
        Write(stage, new
        {
            detail,
            menuOpen = menu.IsOpen,
            attached = menu.IsAttachedToVisualTree(),
            bounds = menu.Bounds.ToString(),
            visible = menu.IsVisible,
            theme = menu.ActualThemeVariant?.ToString(),
            background = menu.Background?.ToString(),
            foreground = menu.Foreground?.ToString(),
            font = menu.FontFamily?.ToString(),
            host = Root(host),
            rows = menu.Items.OfType<MenuItem>().Where(i => i.IsVisible).Select(i => new
            {
                header = Limited(i.Header?.ToString(), 120),
                attached = i.IsAttachedToVisualTree(),
                bounds = i.Bounds.ToString(),
                submenuOpen = i.IsSubMenuOpen,
                foreground = i.Foreground?.ToString(),
                background = i.Background?.ToString(),
                headerPresenter = i.GetVisualDescendants().OfType<ContentPresenter>().Where(p => p.Name == "PART_HeaderPresenter").Select(p => new { foreground = p.Foreground?.ToString(), background = p.Background?.ToString() }),
                text = i.GetVisualDescendants().OfType<TextBlock>().Take(8).Select(t => new { text = Limited(t.Text, 120), bounds = t.Bounds.ToString(), visible = t.IsVisible, foreground = t.Foreground?.ToString() })
            }).ToArray()
        });
    }

    private static object Root(TopLevel root)
    {
        if (root == null) return null;
        object platform = typeof(TopLevel).GetProperty("PlatformImpl", Members)?.GetValue(root);
        object Field(string name) => platform?.GetType().GetField(name, Members)?.GetValue(platform);
        long xid = root.TryGetPlatformHandle()?.Handle.ToInt64() ?? 0;
        long child = Field("_renderHandle") is IntPtr h ? h.ToInt64() : 0;
        var snapshot = new
        {
            xid, renderChild = child,
            kind = root.GetType().Name,
            visible = root.IsVisible,
            client = root.ClientSize.ToString(),
            bounds = root.Bounds.ToString(),
            scale = root.RenderScaling,
            content = (root as ContentControl)?.Content?.GetType().Name,
            platformKind = platform?.GetType().Name,
            internalOverrideRedirect = Field("_overrideRedirect"),
            internalPopup = Field("_popup"),
            internalMapped = Field("_mapped"),
            separateRenderWindow = Field("_useRenderWindow"),
            renderer = ((IRenderRoot)root).Renderer.GetType().Name,
            surfaces = (platform?.GetType().GetProperty("Surfaces", Members)?.GetValue(platform) as System.Collections.IEnumerable)?.Cast<object>().Select(s => s.GetType().Name).ToArray()
        };
        if (xid != 0 && Volatile.Read(ref records) < MaximumRecords && nativeQueries.Wait(0))
        {
            _ = Task.Run(() =>
            {
                try { NativeSnapshot(xid, child); }
                catch (Exception error) { Write("native-query-error", new { xid, type = error.GetType().Name, message = Limited(error.Message, 512) }); }
                finally { nativeQueries.Release(); }
            });
        }
        return snapshot;
    }

    // Explicit diagnostic A/B command, not automatically applied by Bind.
    // These keys are the exact DynamicResources in Fluent11.3.22 MenuItem.xaml.
    internal static void ApplyPalette(ContextMenu menu)
    {
        menu.Resources["MenuFlyoutPresenterBackground"] = MainframeTheme.Ground;
        menu.Resources["MenuFlyoutPresenterBorderBrush"] = MainframeTheme.Brim;
        foreach (string state in new[] { "", "PointerOver", "Pressed", "Disabled" })
        {
            bool disabled = state == "Disabled";
            menu.Resources["MenuFlyoutItemForeground" + state] = disabled ? MainframeTheme.Faint : MainframeTheme.White;
            menu.Resources["MenuFlyoutItemBackground" + state] = state == "PointerOver" ? MainframeTheme.Hover : state == "Pressed" ? MainframeTheme.Pressed : Avalonia.Media.Brushes.Transparent;
            menu.Resources["MenuFlyoutItemKeyboardAcceleratorTextForeground" + state] = disabled || state == "" ? MainframeTheme.Faint : MainframeTheme.White;
            menu.Resources["MenuFlyoutSubItemChevron" + state] = disabled ? MainframeTheme.Faint : MainframeTheme.White;
        }
        menu.Resources["MenuFlyoutSubItemChevronSubMenuOpened"] = MainframeTheme.Cyan;
        menu.Resources["SystemControlForegroundBaseMediumLowBrush"] = MainframeTheme.Rule;
        Capture("palette-applied-diagnostic", menu);
    }

    internal static void RenderControls(string directory, ContextMenu menu)
    {
        if (!menu.IsOpen || menu.Bounds.Width <= 0 || menu.Bounds.Height <= 0) throw new InvalidOperationException("Open and arrange the real menu before CPU diagnostic rendering.");
        double scale = TopLevel.GetTopLevel(menu)?.RenderScaling ?? 1;
        var size = new PixelSize((int)Math.Ceiling(menu.Bounds.Width * scale), (int)Math.Ceiling(menu.Bounds.Height * scale));
        if (size.Width > 4096 || size.Height > 4096) throw new InvalidOperationException("Diagnostic menu exceeds capture bounds.");
        using var image = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
        image.Render(menu);
        image.Save(Path.Combine(directory, "menu-controls-diagnostic.png"));
        Capture("cpu-render-diagnostic-only", menu, new { size = size.ToString(), note = "CPU control rendering is not native presentation evidence." });
    }

    private static void NativeSnapshot(long xid, long child)
    {
        var connection = xcb_connect(IntPtr.Zero, out _);
        if (connection == IntPtr.Zero) throw new InvalidOperationException("No XCB connection for read-only attributes.");
        if (xcb_connection_has_error(connection) != 0) { xcb_disconnect(connection); throw new InvalidOperationException("XCB connection failed for read-only attributes."); }
        try
        {
            object Read(long id)
            {
                if (id == 0) return null;
                IntPtr attributeError = IntPtr.Zero, geometryError = IntPtr.Zero;
                IntPtr attributes = xcb_get_window_attributes_reply(connection, xcb_get_window_attributes(connection, checked((uint)id)), out attributeError);
                IntPtr geometry = xcb_get_geometry_reply(connection, xcb_get_geometry(connection, checked((uint)id)), out geometryError);
                try
                {
                    if (attributes == IntPtr.Zero || geometry == IntPtr.Zero) return new { xid = id, found = false, error = attributeError != IntPtr.Zero ? Marshal.ReadByte(attributeError, 1) : geometryError != IntPtr.Zero ? Marshal.ReadByte(geometryError, 1) : 0 };
                    var a = Marshal.PtrToStructure<Attributes>(attributes);
                    var g = Marshal.PtrToStructure<Geometry>(geometry);
                    return new { xid = id, found = true, mapState = a.MapState, overrideRedirect = a.OverrideRedirect != 0, x = g.X, y = g.Y, width = g.Width, height = g.Height, depth = g.Depth, root = g.Root };
                }
                finally { foreach (var p in new[] { attributes, geometry, attributeError, geometryError }) if (p != IntPtr.Zero) free(p); }
            }
            Write("native-attributes", new { parent = Read(xid), renderChild = child != xid ? Read(child) : null });
        }
        finally { xcb_disconnect(connection); }
    }

    private static void Write(string stage, object data)
    {
        lock (outputGate)
        {
            if (records >= MaximumRecords) return;
            File.AppendAllText(destination, JsonSerializer.Serialize(new { sequence = ++records, monotonic = Stopwatch.GetTimestamp(), frequency = Stopwatch.Frequency, stage, data }) + "\n");
        }
    }

    private static string Limited(string text, int count) => text == null || text.Length <= count ? text : text.Substring(0, count);
    [StructLayout(LayoutKind.Sequential)]
    private struct Cookie { public uint Sequence; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Attributes
    {
        public byte ResponseType, BackingStore; public ushort Sequence; public uint Length, Visual;
        public ushort Class; public byte BitGravity, WinGravity; public uint BackingPlanes, BackingPixel;
        public byte SaveUnder, MapInstalled, MapState, OverrideRedirect; public uint Colormap, AllEventMasks, YourEventMask;
        public ushort DoNotPropagateMask, Padding;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Geometry
    {
        public byte ResponseType, Depth; public ushort Sequence; public uint Length, Root;
        public short X, Y; public ushort Width, Height, BorderWidth, Padding;
    }
    [DllImport("libxcb.so.1")] private static extern IntPtr xcb_connect(IntPtr display, out int screen);
    [DllImport("libxcb.so.1")] private static extern int xcb_connection_has_error(IntPtr connection);
    [DllImport("libxcb.so.1")] private static extern void xcb_disconnect(IntPtr connection);
    [DllImport("libxcb.so.1")] private static extern Cookie xcb_get_window_attributes(IntPtr connection, uint window);
    [DllImport("libxcb.so.1")] private static extern IntPtr xcb_get_window_attributes_reply(IntPtr connection, Cookie cookie, out IntPtr error);
    [DllImport("libxcb.so.1")] private static extern Cookie xcb_get_geometry(IntPtr connection, uint window);
    [DllImport("libxcb.so.1")] private static extern IntPtr xcb_get_geometry_reply(IntPtr connection, Cookie cookie, out IntPtr error);
    [DllImport("libc.so.6")] private static extern void free(IntPtr memory);
}

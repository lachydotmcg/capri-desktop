using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace CapriDesktop
{
    // Read the actual X11 keyboard recipient, including Avalonia's child focus
    // proxy. This connection never sets focus or sends input; errors are local.
    internal static class HyprlandInputFocus
    {
        [DllImport("libxcb.so.1")] private static extern IntPtr xcb_connect(IntPtr display, IntPtr screen);
        [DllImport("libxcb.so.1")] private static extern void xcb_disconnect(IntPtr connection);
        [DllImport("libxcb.so.1")] private static extern int xcb_connection_has_error(IntPtr connection);
        [DllImport("libxcb.so.1")] private static extern uint xcb_get_input_focus(IntPtr connection);
        [DllImport("libxcb.so.1")] private static extern IntPtr xcb_get_input_focus_reply(IntPtr connection, uint cookie, out IntPtr error);
        [DllImport("libxcb.so.1")] private static extern uint xcb_query_tree(IntPtr connection, uint window);
        [DllImport("libxcb.so.1")] private static extern IntPtr xcb_query_tree_reply(IntPtr connection, uint cookie, out IntPtr error);
        [DllImport("libc.so.6")] private static extern void free(IntPtr memory);

        internal static Task<bool> HasKeyboardFocusAsync(IntPtr window, CancellationToken cancellation) => Task.Run(() =>
        {
            cancellation.ThrowIfCancellationRequested();
            if (!OperatingSystem.IsLinux() || window.ToInt64() <= 1 || window.ToInt64() > uint.MaxValue) return false;
            IntPtr connection = xcb_connect(IntPtr.Zero, IntPtr.Zero);
            try
            {
                if (connection == IntPtr.Zero || xcb_connection_has_error(connection) != 0) return false;
                IntPtr reply = xcb_get_input_focus_reply(connection, xcb_get_input_focus(connection), out IntPtr error);
                uint focused;
                try
                {
                    if (error != IntPtr.Zero || reply == IntPtr.Zero) return false;
                    focused = unchecked((uint)Marshal.ReadInt32(reply, 8));
                }
                finally { free(reply); free(error); }
                for (int depth = 0; depth < 32 && focused > 1; depth++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (focused == (uint)window.ToInt64()) return true;
                    reply = xcb_query_tree_reply(connection, xcb_query_tree(connection, focused), out error);
                    try
                    {
                        if (error != IntPtr.Zero || reply == IntPtr.Zero) return false;
                        uint parent = unchecked((uint)Marshal.ReadInt32(reply, 12));
                        if (parent == focused) return false;
                        focused = parent;
                    }
                    finally { free(reply); free(error); }
                }
                return false;
            }
            finally { if (connection != IntPtr.Zero) xcb_disconnect(connection); }
        }, cancellation);
    }
}

using System;
using System.Threading;

namespace CapriDesktop
{
    internal sealed class WindowsIdentity
    {
        internal static WindowsIdentity GetCurrent() { return new WindowsIdentity(); }
        internal WindowsIdentity User { get { return this; } }
        internal string Value { get { return "smoke-timing-test-" + System.Diagnostics.Process.GetCurrentProcess().Id; } }
    }

    internal static class Native
    {
        internal static uint GetWindowThreadProcessId(IntPtr window, out int process)
        {
            process = 0;
            throw new PlatformNotSupportedException("This test does not call Windows APIs.");
        }
    }

    internal static class Heartbeat
    {
        internal static string Where(Thread thread)
        {
            throw new PlatformNotSupportedException("This test does not inspect the Windows UI thread.");
        }
    }
}

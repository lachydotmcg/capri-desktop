namespace CapriDesktop
{
    // Placement can transiently deactivate a newly mapped XWayland window. Only
    // a window that acquired its requested input focus may dismiss on deactivation.
    internal sealed class ChatFocusGate
    {
        private int revision;
        private bool pending, activating, acquired;
        internal int Begin() { pending = true; activating = acquired = false; return ++revision; }
        internal bool Current(int request) => pending && request == revision;
        internal bool DismissOnDeactivation => acquired || !pending;
        internal bool Activate(int request)
        {
            if (!Current(request)) return false;
            activating = true; return true;
        }
        internal void Activated() { if (activating) { acquired = true; pending = activating = false; } }
        internal void Cancel() { ++revision; pending = activating = acquired = false; }
        internal static bool KeepsFocusIntent(long previous, long current, bool currentIsOwnedTyper) =>
            previous == current || currentIsOwnedTyper;
    }
}

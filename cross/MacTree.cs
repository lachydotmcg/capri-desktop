using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace CapriDesktop
{
    internal static class MacTree
    {
        private const string Graphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        private const string Access = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
        private const string Core = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        internal const int MostNodes = 1500;
        internal const int MostChars = 2400;
        internal const double Budget = 2.0;

        internal static Func<bool> Trusted = delegate { return MacFingers.Trusted(); };

        private static readonly Dictionary<string, string> Roles = new Dictionary<string, string>
        {
            { "AXButton", "button" }, { "AXCheckBox", "checkbox" }, { "AXRadioButton", "radio" }, { "AXPopUpButton", "dropdown" },
            { "AXComboBox", "dropdown" }, { "AXTextField", "box" }, { "AXTextArea", "box" }, { "AXSecureTextField", "box" },
            { "AXSearchField", "box" }, { "AXLink", "link" }, { "AXStaticText", "text" }, { "AXHeading", "heading" },
            { "AXMenuItem", "menu item" }, { "AXMenuButton", "button" }, { "AXTab", "tab" }, { "AXSlider", "slider" },
            { "AXIncrementor", "spinner" }, { "AXDisclosureTriangle", "button" }, { "AXCell", "item" }, { "AXRow", "item" }
        };

        internal static string Line(string role, string name, string value, bool focused, bool hide)
        {
            string shown;
            if (role == null || !Roles.TryGetValue(role, out shown)) return null;
            bool secret = role == "AXSecureTextField", typed = role == "AXTextField" || role == "AXTextArea" || role == "AXSearchField" || role == "AXComboBox";
            name = Flat(name);
            value = secret || (hide && typed) ? "" : Flat(value);
            if (value.Length > 400) value = value.Substring(0, 400);
            string text = secret ? (name.Length > 0 ? name : "password") : value.Length == 0 || value == name ? name : name.Length > 0 ? name + ": " + value : value;
            if (text.Length == 0) return null;
            if (text.Length > 300) text = text.Substring(0, 300);
            return shown + " " + Voice.Quote(text) + (secret ? " (password box)" : "") + (focused ? " (focused)" : "");
        }

        internal static string Flat(string text) { return string.Join(" ", (text ?? "").Replace('\t', ' ').Split((char[])null, StringSplitOptions.RemoveEmptyEntries)); }

        internal static string Read()
        {
            if (!OperatingSystem.IsMacOS() || !Trusted()) return null;
            int own = Environment.ProcessId;
            int pid;
            string program;
            if (!Front(own, out pid, out program)) return null;
            IntPtr app = AXUIElementCreateApplication(pid);
            if (app == IntPtr.Zero) return null;
            IntPtr window = IntPtr.Zero;
            try
            {
                AXUIElementSetMessagingTimeout(app, 0.5f);
                window = Copy(app, "AXFocusedWindow");
                if (window == IntPtr.Zero) window = Copy(app, "AXMainWindow");
                if (window == IntPtr.Zero) return null;
                StringBuilder text = new StringBuilder(Flat(program) + "\t" + Flat(Text(window, "AXTitle")));
                bool hide = Brain.Discreet;
                HashSet<string> said = new HashSet<string>();
                Stopwatch clock = Stopwatch.StartNew();
                Stack<IntPtr> pending = new Stack<IntPtr>();
                List<IntPtr> owned = new List<IntPtr>();
                pending.Push(window);
                int nodes = 0, used = 0;
                try
                {
                    while (pending.Count > 0 && nodes < MostNodes && used < MostChars && clock.Elapsed.TotalSeconds < Budget)
                    {
                        IntPtr node = pending.Pop();
                        nodes++;
                        string role = Text(node, "AXRole");
                        string name = Text(node, "AXTitle");
                        if (name.Length == 0) name = Text(node, "AXDescription");
                        string line = Line(role, name, role == "AXSecureTextField" ? null : Text(node, "AXValue"), Flag(node, "AXFocused"), hide);
                        if (line != null && said.Add(line.ToLowerInvariant()))
                        {
                            text.Append("\n" + line);
                            used += line.Length + 1;
                        }
                        IntPtr children = Copy(node, "AXChildren");
                        if (children == IntPtr.Zero) continue;
                        owned.Add(children);
                        if (CFGetTypeID(children) != CFArrayGetTypeID()) continue;
                        long count = Math.Min((long)CFArrayGetCount(children), 300);
                        for (long i = count - 1; i >= 0; i--)
                        {
                            IntPtr child = CFArrayGetValueAtIndex(children, (nint)i);
                            if (child != IntPtr.Zero) pending.Push(child);
                        }
                    }
                }
                finally { foreach (IntPtr array in owned) CFRelease(array); }
                return TreeSight.Parse(text.ToString());
            }
            finally
            {
                if (window != IntPtr.Zero) CFRelease(window);
                CFRelease(app);
            }
        }

        private static bool Front(int own, out int pid, out string program)
        {
            pid = 0;
            program = "";
            IntPtr list = CGWindowListCopyWindowInfo(1 | 16, 0);
            if (list == IntPtr.Zero) return false;
            try
            {
                nint count = CFArrayGetCount(list);
                for (nint i = 0; i < count; i++)
                {
                    IntPtr window = CFArrayGetValueAtIndex(list, i);
                    long layer, owner;
                    if (!Number(window, "kCGWindowLayer", out layer) || layer != 0 || !Number(window, "kCGWindowOwnerPID", out owner) || owner == own || owner <= 0) continue;
                    pid = (int)owner;
                    program = Field(window, "kCGWindowOwnerName");
                    return true;
                }
                return false;
            }
            finally { CFRelease(list); }
        }

        private static IntPtr Copy(IntPtr element, string attribute)
        {
            IntPtr key = Key(attribute), value;
            try { return AXUIElementCopyAttributeValue(element, key, out value) == 0 ? value : IntPtr.Zero; }
            finally { CFRelease(key); }
        }

        private static string Text(IntPtr element, string attribute)
        {
            IntPtr value = Copy(element, attribute);
            if (value == IntPtr.Zero) return "";
            try { return CFGetTypeID(value) == CFStringGetTypeID() ? Chars(value) : ""; }
            finally { CFRelease(value); }
        }

        private static bool Flag(IntPtr element, string attribute)
        {
            IntPtr value = Copy(element, attribute);
            if (value == IntPtr.Zero) return false;
            try { return CFGetTypeID(value) == CFBooleanGetTypeID() && CFBooleanGetValue(value); }
            finally { CFRelease(value); }
        }

        private static bool Number(IntPtr dictionary, string name, out long value)
        {
            value = 0;
            IntPtr key = Key(name);
            try
            {
                IntPtr found = CFDictionaryGetValue(dictionary, key);
                return found != IntPtr.Zero && CFNumberGetValue(found, 4, out value);
            }
            finally { CFRelease(key); }
        }

        private static string Field(IntPtr dictionary, string name)
        {
            IntPtr key = Key(name);
            try
            {
                IntPtr found = CFDictionaryGetValue(dictionary, key);
                return found != IntPtr.Zero && CFGetTypeID(found) == CFStringGetTypeID() ? Chars(found) : "";
            }
            finally { CFRelease(key); }
        }

        private static string Chars(IntPtr value)
        {
            nint length = CFStringGetLength(value);
            if (length <= 0) return "";
            if (length > 4000) length = 4000;
            char[] text = new char[(int)length];
            CFStringGetCharacters(value, new Range { Length = length }, text);
            return new string(text);
        }

        private static IntPtr Key(string text) { return CFStringCreateWithCharacters(IntPtr.Zero, text.ToCharArray(), text.Length); }

        [StructLayout(LayoutKind.Sequential)] private struct Range { internal nint Start, Length; }
        [DllImport(Access)] private static extern IntPtr AXUIElementCreateApplication(int pid);
        [DllImport(Access)] private static extern int AXUIElementCopyAttributeValue(IntPtr element, IntPtr attribute, out IntPtr value);
        [DllImport(Access)] private static extern int AXUIElementSetMessagingTimeout(IntPtr element, float seconds);
        [DllImport(Graphics)] private static extern IntPtr CGWindowListCopyWindowInfo(uint options, uint relative);
        [DllImport(Core)] private static extern void CFRelease(IntPtr value);
        [DllImport(Core)] private static extern nuint CFGetTypeID(IntPtr value);
        [DllImport(Core)] private static extern nuint CFStringGetTypeID();
        [DllImport(Core)] private static extern nuint CFArrayGetTypeID();
        [DllImport(Core)] private static extern nuint CFBooleanGetTypeID();
        [DllImport(Core)] private static extern nint CFArrayGetCount(IntPtr array);
        [DllImport(Core)] private static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, nint index);
        [DllImport(Core)] private static extern IntPtr CFStringCreateWithCharacters(IntPtr allocator, [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U2)] char[] text, nint length);
        [DllImport(Core)] private static extern IntPtr CFDictionaryGetValue(IntPtr dictionary, IntPtr key);
        [DllImport(Core)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CFNumberGetValue(IntPtr number, int kind, out long value);
        [DllImport(Core)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CFBooleanGetValue(IntPtr value);
        [DllImport(Core)] private static extern nint CFStringGetLength(IntPtr value);
        [DllImport(Core)] private static extern void CFStringGetCharacters(IntPtr value, Range range, [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.U2)] [Out] char[] text);
    }
}

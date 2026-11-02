using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace CapriDesktop
{
    // One desktop icon: where it is in the list view and the name she matches it back by (F72, Lachy: "move around
    // shortcuts/icons temporarily and or make them dissapear"). Her prank only ever reads and moves; it never touches a file.
    internal sealed class IconSpot
    {
        internal readonly int Index;
        internal readonly string Name;
        internal readonly Point At;
        internal IconSpot(int index, string name, Point at) { Index = index; Name = name; At = at; }
    }

    // The desktop's own list of icons: Explorer's SysListView32 under Progman/WorkerW. Reading a position or a name means asking
    // that window for it through memory inside Explorer's process (VirtualAllocEx, Read/WriteProcessMemory), because the list
    // items live over there. Moving one is LVM_SETITEMPOSITION32, the 32-bit-coordinate form, so an icon on a monitor left of the
    // main one isn't truncated. The smoke test points Target at a fake list view of its own, so this never runs on the real desktop.
    internal static class DesktopIcons
    {
        internal static Func<IntPtr> Target = DefaultTarget;

        private const int LVM_FIRST = 0x1000;
        private const int LVM_GETITEMCOUNT = LVM_FIRST + 4;
        private const int LVM_GETITEMPOSITION = LVM_FIRST + 16;
        private const int LVM_SETITEMPOSITION32 = LVM_FIRST + 49;
        private const int LVM_GETITEMTEXTW = LVM_FIRST + 115;
        private const int GWL_STYLE = -16;
        private const int LVS_AUTOARRANGE = 0x0100;
        private const int SW_HIDE = 0, SW_SHOWNA = 8;
        private const int SMTO_ABORTIFHUNG = 0x0002;
        private const int ProcessVm = 0x0008 | 0x0010 | 0x0020; // VM_OPERATION | VM_READ | VM_WRITE
        private const uint MemCommit = 0x1000 | 0x2000, PageReadWrite = 0x04, MemRelease = 0x8000;
        private const int TextChars = 260, TextBytes = TextChars * 2, TextOffset = 256, BufferSize = 1024;

        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(int access, bool inherit, int process);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, UIntPtr size, uint type, uint protect);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, UIntPtr size, uint type);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, UIntPtr size, out IntPtr read);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, UIntPtr size, out IntPtr written);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageTimeout(IntPtr window, int message, IntPtr wParam, IntPtr lParam, int flags, int timeout, out IntPtr result);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);

        internal static IntPtr DefaultTarget() { return Ledges.DesktopListView(); }

        // One window she holds open while she reads or moves a batch: Explorer's process and a scratch page inside it.
        private sealed class Reach : IDisposable
        {
            internal readonly IntPtr List, Process, Buffer;
            internal readonly bool Ok;

            internal Reach(IntPtr list)
            {
                List = list;
                int process;
                Native.GetWindowThreadProcessId(list, out process);
                Process = OpenProcess(ProcessVm, false, process);
                if (Process != IntPtr.Zero) Buffer = VirtualAllocEx(Process, IntPtr.Zero, new UIntPtr((uint)BufferSize), MemCommit, PageReadWrite);
                Ok = Process != IntPtr.Zero && Buffer != IntPtr.Zero;
            }

            public void Dispose()
            {
                if (Buffer != IntPtr.Zero) VirtualFreeEx(Process, Buffer, UIntPtr.Zero, MemRelease);
                if (Process != IntPtr.Zero) CloseHandle(Process);
            }
        }

        // A message to the list view that gives up if the desktop's hung, so a frozen Explorer can't hold her up.
        private static bool Send(IntPtr list, int message, IntPtr wParam, IntPtr lParam, out IntPtr result)
        {
            result = IntPtr.Zero;
            IntPtr answer;
            IntPtr ok = SendMessageTimeout(list, message, wParam, lParam, SMTO_ABORTIFHUNG, 2000, out answer);
            if (ok == IntPtr.Zero) return false;
            result = answer;
            return true;
        }

        private static void PutInt(byte[] bytes, int offset, int value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
            bytes[offset + 2] = (byte)(value >> 16);
            bytes[offset + 3] = (byte)(value >> 24);
        }

        private static void PutPtr(byte[] bytes, int offset, IntPtr value)
        {
            long raw = value.ToInt64();
            for (int i = 0; i < IntPtr.Size; i++) bytes[offset + i] = (byte)(raw >> (8 * i));
        }

        private static int GetInt(byte[] bytes, int offset)
        {
            return bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24);
        }

        // Every icon on the desktop now, with its name and spot. An empty list means the desktop couldn't be reached.
        internal static List<IconSpot> Read(IntPtr list)
        {
            List<IconSpot> spots = new List<IconSpot>();
            if (list == IntPtr.Zero) return spots;
            using (Reach reach = new Reach(list))
            {
                if (!reach.Ok) return spots;
                IntPtr counted;
                if (!Send(list, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero, out counted)) return spots;
                int count = counted.ToInt32();
                for (int i = 0; i < count; i++)
                {
                    Point at;
                    string name;
                    if (ReadPosition(reach, i, out at) && ReadName(reach, i, out name)) spots.Add(new IconSpot(i, name, at));
                }
            }
            return spots;
        }

        private static bool ReadPosition(Reach reach, int index, out Point at)
        {
            at = Point.Empty;
            IntPtr got;
            if (!Send(reach.List, LVM_GETITEMPOSITION, new IntPtr(index), reach.Buffer, out got) || got == IntPtr.Zero) return false;
            byte[] point = new byte[8];
            IntPtr read;
            if (!ReadProcessMemory(reach.Process, reach.Buffer, point, new UIntPtr(8), out read) || read.ToInt64() < 8) return false;
            at = new Point(GetInt(point, 0), GetInt(point, 4));
            return true;
        }

        private static bool ReadName(Reach reach, int index, out string name)
        {
            name = null;
            int pointer = IntPtr.Size;
            int pszOffset = (20 + pointer - 1) / pointer * pointer;
            int cchOffset = pszOffset + pointer;
            byte[] item = new byte[128];
            PutInt(item, 8, 0); // iSubItem
            PutPtr(item, pszOffset, new IntPtr(reach.Buffer.ToInt64() + TextOffset));
            PutInt(item, cchOffset, TextChars);
            IntPtr written;
            if (!WriteProcessMemory(reach.Process, reach.Buffer, item, new UIntPtr((uint)item.Length), out written)) return false;
            IntPtr length;
            if (!Send(reach.List, LVM_GETITEMTEXTW, new IntPtr(index), reach.Buffer, out length)) return false;
            byte[] text = new byte[TextBytes];
            IntPtr read;
            if (!ReadProcessMemory(reach.Process, new IntPtr(reach.Buffer.ToInt64() + TextOffset), text, new UIntPtr((uint)TextBytes), out read)) return false;
            int chars = length.ToInt32();
            if (chars < 0) chars = 0;
            if (chars > TextChars) chars = TextChars;
            name = Encoding.Unicode.GetString(text, 0, chars * 2);
            int end = name.IndexOf('\0');
            if (end >= 0) name = name.Substring(0, end);
            return true;
        }

        internal static bool Move(IntPtr list, int index, Point at)
        {
            List<KeyValuePair<int, Point>> one = new List<KeyValuePair<int, Point>>();
            one.Add(new KeyValuePair<int, Point>(index, at));
            return MoveMany(list, one) > 0;
        }

        // Puts a batch of icons where she wants them in one trip into Explorer's process. Returns how many moved.
        internal static int MoveMany(IntPtr list, List<KeyValuePair<int, Point>> moves)
        {
            if (list == IntPtr.Zero || moves == null || moves.Count == 0) return 0;
            int done = 0;
            using (Reach reach = new Reach(list))
            {
                if (!reach.Ok) return 0;
                foreach (KeyValuePair<int, Point> move in moves)
                {
                    byte[] point = new byte[8];
                    PutInt(point, 0, move.Value.X);
                    PutInt(point, 4, move.Value.Y);
                    IntPtr written;
                    if (!WriteProcessMemory(reach.Process, reach.Buffer, point, new UIntPtr(8), out written)) continue;
                    IntPtr result;
                    if (Send(list, LVM_SETITEMPOSITION32, new IntPtr(move.Key), reach.Buffer, out result)) done++;
                }
            }
            return done;
        }

        // When Auto arrange icons is on, a move snaps straight back, so she only does the vanish instead.
        internal static bool AutoArrange(IntPtr list)
        {
            return list != IntPtr.Zero && (Native.GetWindowLong(list, GWL_STYLE) & LVS_AUTOARRANGE) != 0;
        }

        internal static void Hide(IntPtr list) { if (list != IntPtr.Zero) ShowWindow(list, SW_HIDE); }

        internal static void Show(IntPtr list) { if (list != IntPtr.Zero) ShowWindow(list, SW_SHOWNA); }

        internal static bool Hidden(IntPtr list) { return list != IntPtr.Zero && !IsWindowVisible(list); }
    }

    // The gag itself: she scatters the desktop icons around for a while then puts every one back exactly, or she makes them all
    // vanish and brings them back after 10 to 20 seconds. Safety first: before any change she writes the originals to a file in
    // her app data, so a crash, a kill or Esc still gets them home, and her next start puts right anything that file still holds.
    // Icons someone moved by hand meanwhile are left where they put them.
    internal sealed class IconMischief
    {
        internal static string Marker;
        private const int Slack = 28;
        private const double StepGap = 0.3;

        private readonly Random random;
        private readonly Func<IntPtr> target;
        private readonly List<Moved> moved = new List<Moved>();
        private IntPtr list;
        private bool active, vanishing, sliding;
        private double nextStep, holdUntil, showAt;
        private int step, steps;

        // Her glitch while she scatters them (skipped on reduced motion). Her window sets it.
        internal Action Glitch;

        internal IconMischief(Func<IntPtr> aim, int seed) { target = aim; random = new Random(seed); }

        internal bool Active { get { return active; } }
        internal bool Vanishing { get { return vanishing; } }
        internal bool Sliding { get { return sliding; } }
        internal int Moves { get { return moved.Count; } }

        internal string[] Picked
        {
            get { string[] names = new string[moved.Count]; for (int i = 0; i < moved.Count; i++) names[i] = moved[i].Name; return names; }
        }

        private sealed class Moved
        {
            internal readonly string Name;
            internal int Index;
            internal readonly Point Orig, From, To;
            internal Point Last;
            internal bool Released;
            internal Moved(string name, int index, Point orig, Point to) { Name = name; Index = index; Orig = orig; From = orig; To = to; Last = orig; }
        }

        // The right prank for the desktop as it is: a scatter when the icons can be moved, or a vanish when Auto arrange would
        // undo every move or there's nothing to move.
        internal bool Start(double now, bool motion)
        {
            if (active) return false;
            list = target();
            if (list == IntPtr.Zero) return false;
            if (DesktopIcons.AutoArrange(list)) return Vanish(now);
            List<IconSpot> spots = DesktopIcons.Read(list);
            if (spots.Count == 0) return Vanish(now);
            moved.Clear();
            Rectangle area = Area(spots);
            int want = Math.Min(spots.Count, 3 + random.Next(4));
            foreach (IconSpot spot in Pick(spots, want)) moved.Add(new Moved(spot.Name, spot.Index, spot.At, Scatter(area, spot.At)));
            if (moved.Count == 0) return Vanish(now);
            MarkShuffle();
            active = true;
            vanishing = false;
            sliding = true;
            step = 0;
            steps = motion ? 4 + random.Next(4) : 1;
            nextStep = now;
            return true;
        }

        internal bool Vanish(double now)
        {
            if (active) return false;
            list = target();
            if (list == IntPtr.Zero) return false;
            active = true;
            vanishing = true;
            sliding = false;
            showAt = now + 10 + random.NextDouble() * 10;
            MarkVanish();
            DesktopIcons.Hide(list);
            return true;
        }

        internal void Advance(double now)
        {
            if (!active) return;
            if (vanishing) { if (now >= showAt) PutBack(); return; }
            if (sliding)
            {
                if (now < nextStep) return;
                nextStep = now + StepGap;
                step++;
                SlideStep();
                if (step >= steps) { sliding = false; holdUntil = now + 4 + random.NextDouble() * 4; }
                return;
            }
            if (now >= holdUntil) PutBack();
        }

        private void SlideStep()
        {
            double frac = steps <= 0 ? 1 : Math.Min(1.0, (double)step / steps);
            List<IconSpot> before = DesktopIcons.Read(list);
            List<KeyValuePair<int, Point>> batch = new List<KeyValuePair<int, Point>>();
            foreach (Moved m in moved)
            {
                if (m.Released) continue;
                IconSpot cur = Find(before, m.Name);
                // Gone, or dragged off where she left it: someone's hand is on it, so she lets that one be.
                if (cur == null || !Near(cur.At, m.Last)) { m.Released = true; continue; }
                m.Index = cur.Index;
                batch.Add(new KeyValuePair<int, Point>(cur.Index, new Point(
                    (int)Math.Round(m.From.X + (m.To.X - m.From.X) * frac),
                    (int)Math.Round(m.From.Y + (m.To.Y - m.From.Y) * frac))));
            }
            DesktopIcons.MoveMany(list, batch);
            // Where they actually landed, so a desktop that snaps to a grid doesn't read as a hand moving them next step.
            List<IconSpot> after = DesktopIcons.Read(list);
            foreach (Moved m in moved)
            {
                if (m.Released) continue;
                IconSpot cur = Find(after, m.Name);
                if (cur != null) m.Last = cur.At;
            }
            MarkShuffle();
            Action glitch = Glitch;
            if (glitch != null) glitch();
        }

        // Everything home. A vanished list view comes back; scattered icons go to where they started, unless a hand moved one off
        // where she left it since. Idempotent, so Esc, a quit and the gag's own end can all call it.
        internal void PutBack()
        {
            if (!active) return;
            active = false;
            sliding = false;
            try
            {
                if (vanishing) DesktopIcons.Show(list);
                else
                {
                    List<IconSpot> spots = DesktopIcons.Read(list);
                    List<KeyValuePair<int, Point>> back = new List<KeyValuePair<int, Point>>();
                    foreach (Moved m in moved)
                    {
                        if (m.Released) continue;
                        IconSpot cur = Find(spots, m.Name);
                        if (cur != null && Near(cur.At, m.Last)) back.Add(new KeyValuePair<int, Point>(cur.Index, m.Orig));
                    }
                    DesktopIcons.MoveMany(list, back);
                }
            }
            catch (Exception problem) { Brain.Log("icons", -1, problem.Message); }
            vanishing = false;
            moved.Clear();
            Unmark();
        }

        // A crash or a kill mid-gag would leave the icons scattered or the list view hidden, so her next start reads the file and
        // puts right whatever it holds: a hidden list view shown again, and every scattered icon still where she left it sent home.
        internal static int Recover()
        {
            if (Marker == null || !File.Exists(Marker)) return 0;
            string[] lines;
            try { lines = File.ReadAllLines(Marker); }
            catch (IOException) { return 0; }
            catch (UnauthorizedAccessException) { return 0; }
            if (lines.Length == 0) { Unmark(); return 0; }
            IntPtr list = DesktopIcons.Target();
            int done = 0;
            string kind = lines[0].Trim();
            if (kind == "vanish") { DesktopIcons.Show(list); done = 1; }
            else if (kind == "shuffle" && list != IntPtr.Zero)
            {
                List<IconSpot> spots = DesktopIcons.Read(list);
                List<KeyValuePair<int, Point>> back = new List<KeyValuePair<int, Point>>();
                for (int i = 1; i < lines.Length; i++)
                {
                    string name;
                    Point orig, last;
                    bool released;
                    if (!ParseLine(lines[i], out name, out orig, out last, out released) || released) continue;
                    IconSpot cur = Find(spots, name);
                    if (cur != null && Near(cur.At, last)) back.Add(new KeyValuePair<int, Point>(cur.Index, orig));
                }
                done = DesktopIcons.MoveMany(list, back);
            }
            Unmark();
            return done;
        }

        // One line a scattered icon: its name (so she matches it back by name, never by index), where it started, where she last
        // left it, and whether a hand has since taken it over.
        internal static string MarkLine(string name, Point orig, Point last, bool released)
        {
            string tag = Convert.ToBase64String(Encoding.UTF8.GetBytes(name == null ? "" : name));
            return tag + " " + orig.X + " " + orig.Y + " " + last.X + " " + last.Y + " " + (released ? "1" : "0");
        }

        private static bool ParseLine(string line, out string name, out Point orig, out Point last, out bool released)
        {
            name = null;
            orig = Point.Empty;
            last = Point.Empty;
            released = false;
            string[] words = (line == null ? "" : line).Split(' ');
            if (words.Length != 6) return false;
            int ox, oy, lx, ly;
            if (!int.TryParse(words[1], out ox) || !int.TryParse(words[2], out oy) || !int.TryParse(words[3], out lx) || !int.TryParse(words[4], out ly)) return false;
            try { name = Encoding.UTF8.GetString(Convert.FromBase64String(words[0])); }
            catch (FormatException) { return false; }
            orig = new Point(ox, oy);
            last = new Point(lx, ly);
            released = words[5] == "1";
            return true;
        }

        private void MarkShuffle()
        {
            if (Marker == null) return;
            StringBuilder lines = new StringBuilder();
            lines.AppendLine("shuffle");
            foreach (Moved m in moved) lines.AppendLine(MarkLine(m.Name, m.Orig, m.Last, m.Released));
            Write(lines.ToString());
        }

        private void MarkVanish() { Write("vanish" + Environment.NewLine); }

        private static void Write(string body)
        {
            if (Marker == null) return;
            try { File.WriteAllText(Marker, body); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void Unmark()
        {
            if (Marker == null) return;
            try { File.Delete(Marker); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static IconSpot Find(List<IconSpot> spots, string name)
        {
            foreach (IconSpot spot in spots) if (spot.Name == name) return spot;
            return null;
        }

        private static bool Near(Point a, Point b) { return Math.Abs(a.X - b.X) <= Slack && Math.Abs(a.Y - b.Y) <= Slack; }

        private static Rectangle Area(List<IconSpot> spots)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (IconSpot spot in spots)
            {
                minX = Math.Min(minX, spot.At.X);
                minY = Math.Min(minY, spot.At.Y);
                maxX = Math.Max(maxX, spot.At.X);
                maxY = Math.Max(maxY, spot.At.Y);
            }
            minX = Math.Max(0, minX - 160);
            minY = Math.Max(0, minY - 120);
            return Rectangle.FromLTRB(minX, minY, Math.Max(minX + 1, maxX + 160), Math.Max(minY + 1, maxY + 120));
        }

        private List<IconSpot> Pick(List<IconSpot> spots, int want)
        {
            List<IconSpot> pool = new List<IconSpot>(spots);
            List<IconSpot> chosen = new List<IconSpot>();
            while (chosen.Count < want && pool.Count > 0)
            {
                int k = random.Next(pool.Count);
                chosen.Add(pool[k]);
                pool.RemoveAt(k);
            }
            return chosen;
        }

        private Point Scatter(Rectangle area, Point from)
        {
            Point to = from;
            for (int tries = 0; tries < 8; tries++)
            {
                int x = area.Left + random.Next(Math.Max(1, area.Width));
                int y = area.Top + random.Next(Math.Max(1, area.Height));
                to = new Point(x, y);
                if (Math.Abs(x - from.X) + Math.Abs(y - from.Y) >= 60) break;
            }
            return to;
        }
    }

    internal sealed partial class Companion
    {
        private IconMischief iconChaos;
        private readonly Timer iconTimer = new Timer();
        private double iconActAt = -1;

        private void IconTick(object sender, EventArgs e)
        {
            IconMischief gag = iconChaos;
            if (gag == null) { iconTimer.Stop(); return; }
            gag.Advance(clock.Elapsed.TotalSeconds);
            if (!gag.Active) { iconChaos = null; iconTimer.Stop(); }
        }

        // Her clones' per-frame beat: they chatter to each other, and once in a while one of them goes for the desktop icons.
        private void CloneTick(double now)
        {
            CloneTalk(now);
            IconChaosTick(now);
        }

        // One of chaos mode's random acts: now and then while her clones are out, she scatters or vanishes the desktop icons.
        private void IconChaosTick(double now)
        {
            if (testing || cloneWindow == null || iconChaos != null) return;
            if (working != null || hidden || Computer.Streaming || Computer.GameOpen() != null) return;
            if (iconActAt < 0) { iconActAt = now + 22 + random.NextDouble() * 26; return; }
            if (now < iconActAt) return;
            iconActAt = now + 30 + random.NextDouble() * 40;
            if (random.Next(3) != 0) return;
            StartIconChaos(random.Next(3) == 0);
        }

        private bool StartIconChaos(bool hideOnly)
        {
            if (iconChaos != null) return false;
            IconMischief gag = new IconMischief(DesktopIcons.Target, random.Next());
            gag.Glitch = delegate { if (!testing && preferences.Motion) Animate("glitch"); };
            double now = clock.Elapsed.TotalSeconds;
            bool ok = hideOnly ? gag.Vanish(now) : gag.Start(now, preferences.Motion);
            if (!ok) return false;
            iconChaos = gag;
            iconTimer.Start();
            return true;
        }

        internal void ClearIcons()
        {
            IconMischief gag = iconChaos;
            iconChaos = null;
            iconTimer.Stop();
            if (gag != null) { try { gag.PutBack(); } catch (Exception problem) { Brain.Log("icons", -1, problem.Message); } }
        }

        // mess, hide or back. "back" always works: a running gag is cleared, otherwise anything left in the backup file is put right.
        internal string IconPrank(string action)
        {
            if (action == "back")
            {
                bool running = iconChaos != null;
                ClearIcons();
                int back = running ? 1 : IconMischief.Recover();
                return back > 0 ? "Put your icons back, choom." : "Your icons are already where you left them.";
            }
            if (iconChaos != null) return "I'm already in there, choom. Say put my icons back when you've had enough.";
            bool hide = action == "hide";
            if (!StartIconChaos(hide)) return "Couldn't get at your desktop icons just now, choom.";
            if (hide || (iconChaos != null && iconChaos.Vanishing)) return "Poof. Your icons are gone... I'll put them back in a sec.";
            return "Messing with your icons, choom. Say put my icons back when you want them.";
        }

        private string IconTool(string action) { return InvokeRequired ? (string)Invoke(new Func<string, string>(IconPrank), action) : IconPrank(action); }
    }
}

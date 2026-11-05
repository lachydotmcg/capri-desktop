using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Her window tool's moves (Lachy, 2 Nov: "build everything else that can be included in 0.8.0, especially focused on the tool
    // use"): maximize, restore, snap to half a monitor, send to another monitor, move and resize an app's window, on monitors of
    // any size, way round or scale. What she says is where the window really ended up, read back afterwards, so an app that won't
    // go smaller, a window that runs as admin or one that stopped responding is said as it is.
    internal static class Arrange
    {
        // One of their monitors: what her tools call it, all of it and the part windows use (without the taskbar), and its scale
        // in dots per inch, 96 being 100%.
        internal sealed class Display
        {
            internal string Name;
            internal int Number;
            internal Rectangle Bounds, Work;
            internal bool Primary;
            internal int Dpi = 96;
        }

        // Where a window is: its visible edges (Windows pads windows with invisible borders, which don't count), and whether it's
        // minimized, maximized or gone.
        internal sealed class Spot
        {
            internal Rectangle Edges;
            internal bool Minimized, Maximized, Gone;
        }

        // Her ways of reading their monitors and windows and asking Windows to move them. Smoke tests swap them for a pretend desk.
        internal static Func<List<Display>> Displays = ReadDisplays;
        internal static Func<IntPtr, Spot> Read = ReadSpot;
        // Asks Windows to maximize or restore a window, or to put its visible edges at a place. False when Windows refused.
        internal static Func<IntPtr, string, Rectangle, bool> Shape = ShapeWindow;
        // Whether a window's app runs as administrator while she doesn't, so Windows won't let her move it.
        internal static Func<IntPtr, bool> Raised = AboveHer;
        internal static Func<IntPtr, bool> Hung = IsHungAppWindow;
        internal static Func<Point> Mouse = delegate { return Cursor.Position; };
        // Whether a window is kept on top of the others, and keeping it there or letting it go. False when Windows refused.
        internal static Func<IntPtr, bool> Pinned = OnTop;
        internal static Func<IntPtr, bool, bool> Pin = KeepOnTop;
        // How long a window gets to finish maximizing or restoring, in milliseconds.
        internal static int Patience = 1500;

        internal static readonly string[] Actions = { "maximize", "restore", "snap_left", "snap_right", "snap_top", "snap_bottom", "move_to_screen", "move", "resize", "pin_on_top", "unpin" };
        internal static readonly string[] Spots = { "center", "top_left", "top_right", "bottom_left", "bottom_right" };
        private const int Smallest = 160;

        // Does one of her window moves to the window she found for what they named, and says what the window did.
        internal static string Do(OpenWindow window, string label, ToolArguments args)
        {
            string action = args.Action;
            IntPtr handle = window.Handle;
            string verb = Verb(action);
            Spot was = Read(handle);
            if (was == null || was.Gone) return "Couldn't " + verb + " " + label + ": its window closed.";
            if (Hung(handle)) return "Couldn't " + verb + " " + label + ": it isn't responding right now.";
            if (action == "pin_on_top" || action == "unpin") return Pinning(handle, label, action == "pin_on_top");
            List<Display> screens = Displays();
            if (screens == null || screens.Count == 0) return "Couldn't read your monitors just now.";
            if (action == "restore") return Restore(handle, label, was);
            bool named = !string.IsNullOrWhiteSpace(args.Screen) || action == "move_to_screen";
            if (action == "move_to_screen" && screens.Count < 2) return "Couldn't move " + label + " to another monitor: there's only one.";
            // The monitor it's on and the one it goes to, checked before anything changes, so a window already where it's wanted
            // stays as it is. A minimized one is only known once it's back up.
            int on = was.Minimized ? -1 : Under(was.Edges, screens), to = on;
            if (named && on >= 0)
            {
                to = Pick(args.Screen, on, screens);
                if (to < 0) return Unknown(args.Screen, screens);
                if (action == "move_to_screen" && to == on) return label + " is already on " + The(screens[to]) + ".";
            }
            if (action == "maximize" && (on >= 0 ? to == on : !named)) return Maximize(handle, label, was, on, on, screens);
            // A monitor that isn't there, or a place or size that isn't one, is turned down before the window comes up or down.
            if (named && on < 0)
            {
                bool there = false;
                for (int candidate = 0; candidate < screens.Count && !there; candidate++) there = Pick(args.Screen, candidate, screens) >= 0;
                if (!there) return Unknown(args.Screen, screens);
            }
            string unread = Unread(action, label, args);
            if (unread != null) return unread;
            // Moving it means its normal size first: a minimized window comes back up and a maximized one comes down.
            bool maximized = was.Maximized && !was.Minimized;
            Spot now = Unfold(handle, was);
            if (now.Gone) return "Couldn't " + verb + " " + label + ": its window closed.";
            if (now.Minimized || now.Maximized) return Refused(handle, verb, label) ?? "Couldn't " + verb + " " + label + ": it wouldn't come out of " + (now.Minimized ? "minimized" : "maximized") + ".";
            if (on < 0)
            {
                on = Under(now.Edges, screens);
                to = named ? Pick(args.Screen, on, screens) : on;
                if (to < 0) return Unknown(args.Screen, screens);
                if (action == "move_to_screen" && to == on) return "Brought " + label + " back up. It was already on " + The(screens[on]) + ".";
            }
            if (action == "maximize") return Maximize(handle, label, now, on, to, screens);
            if (action == "move_to_screen")
            {
                Rectangle carried = Carried(now.Edges, screens[on], screens[to]);
                string moved = Place(handle, label, "move", now.Edges, carried, "Moved " + label + " to " + The(screens[to]), screens);
                if (!maximized || Hands.Failed(moved)) return moved;
                Shape(handle, "maximize", Rectangle.Empty);
                Spot after = Settle(handle, delegate(Spot spot) { return spot.Maximized; });
                return after.Maximized ? "Moved " + label + " to " + The(screens[to]) + ", still maximized." : moved;
            }
            Rectangle work = screens[to].Work, wanted;
            string done;
            if (action.StartsWith("snap_", StringComparison.Ordinal))
            {
                string side = action.Substring(5);
                wanted = Half(work, side);
                done = "Snapped " + label + " to the " + side + " half of " + The(screens[to]);
            }
            else if (action == "move")
            {
                Size size = to == on ? now.Edges.Size : Scaled(now.Edges.Size, screens[on], screens[to]);
                size = new Size(Math.Min(size.Width, work.Width), Math.Min(size.Height, work.Height));
                string spot = SpotOf(args.Position);
                bool spotted = Array.IndexOf(Spots, spot) >= 0, measured = !string.IsNullOrWhiteSpace(args.Left) || !string.IsNullOrWhiteSpace(args.Top);
                Point corner;
                if (spotted) corner = Corner(work, size, spot);
                else if (measured)
                {
                    Rectangle from = to == on ? now.Edges : Carried(now.Edges, screens[on], screens[to]);
                    int? left = Measure(args.Left, from.Left - work.Left, work.Width, true), top = Measure(args.Top, from.Top - work.Top, work.Height, true);
                    if (!left.HasValue || !top.HasValue) return Unread(action, label, args);
                    corner = new Point(work.Left + left.Value, work.Top + top.Value);
                }
                else if (to != on)
                {
                    Rectangle carried = Carried(now.Edges, screens[on], screens[to]);
                    corner = carried.Location;
                    size = carried.Size;
                }
                else return "Where should I move " + label + "? Give a spot like center or top_right, a screen, or left and top.";
                wanted = Inside(new Rectangle(corner, size), work);
                done = "Moved " + label + " to " + (spotted ? "the " + Said(spot) + " of " + The(screens[to]) : measured ? (wanted.Left - work.Left) + ", " + (wanted.Top - work.Top) + " on " + The(screens[to]) : The(screens[to]));
            }
            else
            {
                Rectangle from = to == on ? now.Edges : Carried(now.Edges, screens[on], screens[to]);
                int? width = Measure(args.Width, from.Width, work.Width, false), height = Measure(args.Height, from.Height, work.Height, false);
                if (!width.HasValue || !height.HasValue) return Unread(action, label, args);
                Size size = new Size(Math.Max(Smallest, Math.Min(width.Value, work.Width)), Math.Max(Smallest, Math.Min(height.Value, work.Height)));
                Point middle = new Point(from.Left + from.Width / 2, from.Top + from.Height / 2);
                wanted = Inside(new Rectangle(middle.X - size.Width / 2, middle.Y - size.Height / 2, size.Width, size.Height), work);
                done = "Resized " + label + " to " + size.Width + " x " + size.Height + (to != on ? " on " + The(screens[to]) : "");
            }
            return Place(handle, label, verb, now.Edges, wanted, done, screens);
        }

        // A spot her brain named, the way Spots writes it: "top right" or "centre" is top_right or center.
        private static string SpotOf(string position) { return (position ?? "").Trim().ToLowerInvariant().Replace(' ', '_').Replace("centre", "center"); }

        // What's wrong with a place or size her brain gave for a move or resize, or null when they read. A place is only read when
        // there's no spot.
        private static string Unread(string action, string label, ToolArguments args)
        {
            if (action == "move" && Array.IndexOf(Spots, SpotOf(args.Position)) < 0 && (Measure(args.Left, 0, 1, true) == null || Measure(args.Top, 0, 1, true) == null))
                return "Couldn't move " + label + ": " + (Measure(args.Left, 0, 1, true) == null ? args.Left : args.Top) + " isn't a place on the screen. Use pixels like 200, a percent like 25%, or +100 to nudge it.";
            if (action == "resize" && (Measure(args.Width, 0, 1, false) == null || Measure(args.Height, 0, 1, false) == null))
                return "Couldn't resize " + label + ": " + (Measure(args.Width, 0, 1, false) == null ? args.Width : args.Height) + " isn't a size. Use pixels like 1280, a percent like 50%, or +20% to grow it.";
            return null;
        }

        // "maximize", "snap" or "move", for her lines.
        private static string Verb(string action)
        {
            if (action == "maximize") return "maximize";
            if (action == "restore") return "restore";
            if (action == "resize") return "resize";
            if (action == "pin_on_top") return "pin";
            if (action == "unpin") return "unpin";
            if (action != null && action.StartsWith("snap_", StringComparison.Ordinal)) return "snap";
            return "move";
        }

        // Keeps a window above the others, the way a video or a chat stays up over a game, or lets it go back among them, and says
        // what Windows did.
        private static string Pinning(IntPtr handle, string label, bool pin)
        {
            if (Pinned(handle) == pin) return pin ? label + " is already kept on top of your other windows." : label + " isn't kept on top, so it's already in among your other windows.";
            // The app takes the ask in its own time, so she watches for it a moment.
            bool asked = Pin(handle, pin);
            Stopwatch waited = Stopwatch.StartNew();
            while (asked && Pinned(handle) != pin && waited.ElapsedMilliseconds < Patience) Thread.Sleep(50);
            if (!asked || Pinned(handle) != pin)
                return Refused(handle, pin ? "pin" : "unpin", label) ?? "Couldn't " + (pin ? "keep " + label + " on top" : "stop keeping " + label + " on top") + ": Windows didn't let me.";
            return pin ? "Kept " + label + " on top of your other windows. Say unpin " + label + " to let it go." : label + " isn't kept on top any more.";
        }

        private static string Restore(IntPtr handle, string label, Spot was)
        {
            if (!was.Minimized && !was.Maximized) return label + " isn't minimized or maximized, so it's already at its normal size.";
            Shape(handle, "restore", Rectangle.Empty);
            Spot now = Settle(handle, delegate(Spot spot) { return was.Minimized ? !spot.Minimized : !spot.Maximized; });
            if (now.Gone) return "Couldn't restore " + label + ": its window closed.";
            if (was.Minimized && !now.Minimized) return "Brought " + label + " back up" + (now.Maximized ? ", maximized like it was." : ".");
            if (!was.Minimized && !now.Maximized) return "Restored " + label + " from maximized to its normal size.";
            return Refused(handle, "restore", label) ?? "Couldn't restore " + label + ": it's still " + (now.Minimized ? "minimized" : "maximized") + ".";
        }

        private static string Maximize(IntPtr handle, string label, Spot was, int on, int to, List<Display> screens)
        {
            bool elsewhere = on >= 0 && to != on;
            if (was.Maximized && !was.Minimized && !elsewhere) return label + " is already maximized" + Where(screens, on) + ".";
            if (elsewhere)
            {
                // Windows maximizes a window on the monitor it's on, so it goes there at its normal size first.
                Spot normal = Unfold(handle, was);
                if (normal.Gone || normal.Minimized || normal.Maximized) return Refused(handle, "maximize", label) ?? "Couldn't move " + label + " to " + The(screens[to]) + " to maximize it there.";
                Shape(handle, "place", Carried(normal.Edges, screens[Under(normal.Edges, screens)], screens[to]));
            }
            Shape(handle, "maximize", Rectangle.Empty);
            Spot now = Settle(handle, delegate(Spot spot) { return spot.Maximized && !spot.Minimized; });
            if (now.Gone) return "Couldn't maximize " + label + ": its window closed.";
            if (!now.Maximized || now.Minimized) return Refused(handle, "maximize", label) ?? "Couldn't maximize " + label + ": it stayed " + (now.Minimized ? "minimized" : "at its normal size") + ". It might not have a maximize button.";
            int landed = Under(now.Edges, screens);
            if (elsewhere && landed != to) return "Maximized " + label + ", but on " + The(screens[landed]) + ", not " + The(screens[to]) + ".";
            return "Maximized " + label + Where(screens, landed) + ".";
        }

        // Brings a minimized window back up and a maximized one down to its normal size, so it can be moved.
        private static Spot Unfold(IntPtr handle, Spot was)
        {
            Spot now = was;
            for (int i = 0; i < 2 && !now.Gone && (now.Minimized || now.Maximized); i++)
            {
                bool minimized = now.Minimized;
                Shape(handle, "restore", Rectangle.Empty);
                now = Settle(handle, delegate(Spot spot) { return minimized ? !spot.Minimized : !spot.Maximized; });
            }
            return now;
        }

        // Puts a window's visible edges where they're wanted and says where they really went: an app with a smallest size, or
        // one that sizes itself in steps like a terminal, ends up near but not on it, and she says so.
        private static string Place(IntPtr handle, string label, string verb, Rectangle before, Rectangle wanted, string done, List<Display> screens)
        {
            bool taken = Shape(handle, "place", wanted);
            Spot now = Settle(handle, delegate(Spot spot) { return Near(spot.Edges, wanted, 6) || !Near(spot.Edges, before, 2); });
            if (now == null || now.Gone) return "Couldn't " + verb + " " + label + ": its window closed.";
            Rectangle edges = now.Edges;
            if (Near(edges, wanted, 6)) return done + ".";
            if (!taken || Near(edges, before, 2)) return Refused(handle, verb, label) ?? "Couldn't " + verb + " " + label + ": it didn't move. It might not let other apps move it.";
            bool wider = edges.Width > wanted.Width + 12, taller = edges.Height > wanted.Height + 12, narrower = edges.Width < wanted.Width - 12, shorter = edges.Height < wanted.Height - 12;
            // An app that won't go as small as a snap wants sticks out past the monitor's edge, so it's slid back on.
            Rectangle work = screens[Under(wanted, screens)].Work, kept = Inside(edges, work);
            if ((wider || taller) && kept.Location != edges.Location && Shape(handle, "place", kept))
            {
                Spot slid = Read(handle);
                if (slid != null && !slid.Gone) edges = slid.Edges;
            }
            int landed = Under(edges, screens);
            if (landed != Under(wanted, screens)) return done + ", but it landed on " + The(screens[landed]) + " instead.";
            if (wider || taller) return done + ", but it stayed " + edges.Width + " x " + edges.Height + ": that's as small as it goes.";
            if (narrower || shorter) return done + ", but it only went to " + edges.Width + " x " + edges.Height + ": that's as big as it goes.";
            return done + ".";
        }

        private static string Refused(IntPtr handle, string verb, string label)
        {
            bool raised;
            try { raised = Raised(handle); }
            catch (Exception) { raised = false; }
            return raised ? "Couldn't " + verb + " " + label + ": it's running as admin, and Windows only lets me move admin windows when I run as admin too." : null;
        }

        // Waits for a window to finish maximizing or restoring, and reads it.
        private static Spot Settle(IntPtr handle, Predicate<Spot> done)
        {
            Stopwatch waited = Stopwatch.StartNew();
            Spot now = Read(handle);
            while (now != null && !now.Gone && !done(now) && waited.ElapsedMilliseconds < Patience)
            {
                Thread.Sleep(50);
                now = Read(handle);
            }
            return now ?? new Spot { Gone = true };
        }

        // The half of a monitor's working area a snap fills.
        internal static Rectangle Half(Rectangle work, string side)
        {
            int halfWide = work.Width / 2, halfHigh = work.Height / 2;
            switch (side)
            {
                case "left": return new Rectangle(work.Left, work.Top, halfWide, work.Height);
                case "right": return new Rectangle(work.Right - (work.Width - halfWide), work.Top, work.Width - halfWide, work.Height);
                case "top": return new Rectangle(work.Left, work.Top, work.Width, halfHigh);
                default: return new Rectangle(work.Left, work.Bottom - (work.Height - halfHigh), work.Width, work.Height - halfHigh);
            }
        }

        // A window taken to another monitor. One that fills a monitor or half of one, like a snapped window, fills the same part of
        // the new one. Otherwise it keeps its size, grown or shrunk by how much bigger the new monitor's scale is, and its spot on
        // the screen, shrunk to fit a smaller or portrait monitor.
        internal static Rectangle Carried(Rectangle edges, Display from, Display to)
        {
            Rectangle a = from.Work, b = to.Work;
            if (Filling(edges, a))
            {
                int left = b.Left + (int)Math.Round((edges.Left - a.Left) * (double)b.Width / a.Width), top = b.Top + (int)Math.Round((edges.Top - a.Top) * (double)b.Height / a.Height);
                int right = b.Left + (int)Math.Round((edges.Right - a.Left) * (double)b.Width / a.Width), bottom = b.Top + (int)Math.Round((edges.Bottom - a.Top) * (double)b.Height / a.Height);
                return Rectangle.FromLTRB(left, top, right, bottom);
            }
            Size size = Scaled(edges.Size, from, to);
            size = new Size(Math.Min(size.Width, b.Width), Math.Min(size.Height, b.Height));
            double across = a.Width > edges.Width ? (edges.Left - a.Left) / (double)(a.Width - edges.Width) : 0.5, down = a.Height > edges.Height ? (edges.Top - a.Top) / (double)(a.Height - edges.Height) : 0.5;
            across = Math.Max(0, Math.Min(1, across));
            down = Math.Max(0, Math.Min(1, down));
            return new Rectangle(b.Left + (int)Math.Round((b.Width - size.Width) * across), b.Top + (int)Math.Round((b.Height - size.Height) * down), size.Width, size.Height);
        }

        // Whether a window fills its monitor's working area across or down and sits against its edges, like a snapped half.
        private static bool Filling(Rectangle edges, Rectangle work)
        {
            bool across = Math.Abs(edges.Width - work.Width) <= 8 || Math.Abs(edges.Width - work.Width / 2) <= 8;
            bool down = Math.Abs(edges.Height - work.Height) <= 8 || Math.Abs(edges.Height - work.Height / 2) <= 8;
            bool againstSide = Math.Abs(edges.Left - work.Left) <= 8 || Math.Abs(edges.Right - work.Right) <= 8;
            bool againstEnd = Math.Abs(edges.Top - work.Top) <= 8 || Math.Abs(edges.Bottom - work.Bottom) <= 8;
            return across && down && againstSide && againstEnd;
        }

        private static Size Scaled(Size size, Display from, Display to)
        {
            double scale = from.Dpi > 0 && to.Dpi > 0 ? to.Dpi / (double)from.Dpi : 1;
            return new Size((int)Math.Round(size.Width * scale), (int)Math.Round(size.Height * scale));
        }

        // Where a window of this size goes for center or a corner.
        private static Point Corner(Rectangle work, Size size, string spot)
        {
            int left = spot.EndsWith("left", StringComparison.Ordinal) ? work.Left : spot.EndsWith("right", StringComparison.Ordinal) ? work.Right - size.Width : work.Left + (work.Width - size.Width) / 2;
            int top = spot.StartsWith("top", StringComparison.Ordinal) ? work.Top : spot.StartsWith("bottom", StringComparison.Ordinal) ? work.Bottom - size.Height : work.Top + (work.Height - size.Height) / 2;
            return new Point(left, top);
        }

        private static string Said(string spot) { return spot == "center" ? "middle" : spot.Replace('_', ' ') + " corner"; }

        // Kept on the monitor: moved back inside its working area, or against its top left when it's bigger than that.
        internal static Rectangle Inside(Rectangle wanted, Rectangle work)
        {
            int left = Math.Max(work.Left, Math.Min(wanted.Left, work.Right - wanted.Width));
            int top = Math.Max(work.Top, Math.Min(wanted.Top, work.Bottom - wanted.Height));
            return new Rectangle(left, top, wanted.Width, wanted.Height);
        }

        // A size or place in her brain's words: pixels ("1280" or "1280px"), a percent of the monitor ("50%"), or a change from
        // where it is ("+200", "-10%"). A place's change in percent is of the monitor, and a size's is of its size now. Null when
        // it isn't one; the same as now when it's left out.
        internal static int? Measure(string said, int now, int whole, bool place)
        {
            string text = Regex.Replace((said ?? "").Trim().ToLowerInvariant(), @"\s+|px$|pixels?$", "");
            if (text.Length == 0) return now;
            Match found = Regex.Match(text, @"^(?<sign>[+-])?(?<n>\d{1,5}(?:\.\d+)?)(?<percent>%)?$");
            if (!found.Success) return null;
            double n = double.Parse(found.Groups["n"].Value, CultureInfo.InvariantCulture);
            bool percent = found.Groups["percent"].Success;
            string sign = found.Groups["sign"].Value;
            double amount = percent ? (sign.Length > 0 && !place ? now : whole) * n / 100 : n;
            double value = sign == "+" ? now + amount : sign == "-" ? now - amount : amount;
            return (int)Math.Round(value);
        }

        private static bool Near(Rectangle a, Rectangle b, int slack)
        {
            return Math.Abs(a.Left - b.Left) <= slack && Math.Abs(a.Top - b.Top) <= slack && Math.Abs(a.Right - b.Right) <= slack && Math.Abs(a.Bottom - b.Bottom) <= slack;
        }

        // The monitor most of a window is on, or the nearest one when it's on none.
        internal static int Under(Rectangle edges, List<Display> screens)
        {
            int best = -1;
            long most = 0, nearest = long.MaxValue;
            Point middle = new Point(edges.Left + edges.Width / 2, edges.Top + edges.Height / 2);
            for (int i = 0; i < screens.Count; i++)
            {
                Rectangle shared = Rectangle.Intersect(edges, screens[i].Bounds);
                long area = shared.IsEmpty ? 0 : (long)shared.Width * shared.Height;
                if (area > most) { most = area; best = i; }
            }
            if (best >= 0) return best;
            for (int i = 0; i < screens.Count; i++)
            {
                Rectangle bounds = screens[i].Bounds;
                long dx = Math.Max(0, Math.Max(bounds.Left - middle.X, middle.X - bounds.Right)), dy = Math.Max(0, Math.Max(bounds.Top - middle.Y, middle.Y - bounds.Bottom));
                if (dx * dx + dy * dy < nearest) { nearest = dx * dx + dy * dy; best = i; }
            }
            return Math.Max(0, best);
        }

        private static int Primary(List<Display> screens)
        {
            int primary = screens.FindIndex(screen => screen.Primary);
            return primary < 0 ? 0 : primary;
        }

        // The monitor they named from the one the window is on: next, left, right, up, down, main, here, a number, or the portrait
        // or landscape one. -1 when there's no such monitor.
        internal static int Pick(string which, int current, List<Display> screens)
        {
            string said = Regex.Replace(Regex.Replace((which ?? "").Trim().ToLowerInvariant(), @"\b(?:the|my|your|ur|monitor|screen|display)\b", " "), @"\s+", " ").Trim();
            Match shape = Regex.Match(said, @"^(?:(portrait|vertical|tall|upright|rotated)|(landscape|horizontal|wide|widescreen))(?: one)?$");
            if (shape.Success)
            {
                bool upright = shape.Groups[1].Success;
                int found = -1;
                for (int i = 0; i < screens.Count; i++)
                {
                    bool tall = screens[i].Bounds.Height > screens[i].Bounds.Width;
                    if (tall == upright && (found < 0 || found == current)) found = i;
                }
                return found;
            }
            int[] x = new int[screens.Count], y = new int[screens.Count], numbers = new int[screens.Count];
            int pointed = -1;
            Point mouse = Mouse();
            for (int i = 0; i < screens.Count; i++)
            {
                Rectangle bounds = screens[i].Bounds;
                x[i] = bounds.Left + bounds.Width / 2;
                y[i] = bounds.Top + bounds.Height / 2;
                numbers[i] = screens[i].Number;
                if (bounds.Contains(mouse)) pointed = i;
            }
            if (said.Length == 0) said = "next";
            return Hands.PickScreen(x, y, numbers, Primary(screens), current, pointed < 0 ? current : pointed, said);
        }

        // The monitor they named for her to look at, from the one with their window in front, or the one their mouse is on: all of
        // it, and what to call it. When there's no such monitor, what to say instead.
        internal static string LookArea(string which, IntPtr front, out Rectangle area, out string named)
        {
            area = Rectangle.Empty;
            named = null;
            List<Display> screens;
            try { screens = Displays(); }
            catch (Exception) { screens = null; }
            if (screens == null || screens.Count == 0) return "Couldn't find their monitors just now.";
            Spot spot = null;
            try { if (front != IntPtr.Zero) spot = Read(front); }
            catch (Exception) { spot = null; }
            int current = spot != null && !spot.Gone && !spot.Minimized ? Under(spot.Edges, screens) : Under(new Rectangle(Mouse(), new Size(1, 1)), screens);
            int picked = screens.Count < 2 ? 0 : Pick(which, current, screens);
            if (picked < 0) return Unknown(which, screens);
            area = screens[picked].Bounds;
            named = screens.Count < 2 ? "their screen" : The(screens[picked]);
            return null;
        }

        private static string The(Display screen) { return screen.Primary ? "the main screen" : screen.Name; }

        private static string Unknown(string which, List<Display> screens)
        {
            string said = Regex.Replace(Regex.Replace((which ?? "").Trim().ToLowerInvariant(), @"\b(?:the|my|your|monitor|screen|display|number|no)\b|[#.]", " "), @"\s+", " ").Trim();
            return "There's no " + (Regex.IsMatch(said, @"^\d+$") ? "screen " + said : said + " monitor") + ". " + Listed(screens);
        }

        private static string Where(List<Display> screens, int index) { return screens.Count < 2 || index < 0 ? "" : " on " + The(screens[index]); }

        private static string Listed(List<Display> screens)
        {
            List<string> names = new List<string>();
            foreach (Display screen in Ordered(screens)) names.Add(The(screen));
            return "Your monitors are " + (names.Count < 2 ? string.Join("", names.ToArray()) : string.Join(", ", names.GetRange(0, names.Count - 1).ToArray()) + " and " + names[names.Count - 1]) + ".";
        }

        private static List<Display> Ordered(List<Display> screens)
        {
            List<Display> order = new List<Display>(screens);
            order.Sort(delegate(Display a, Display b) { return a.Bounds.Left != b.Bounds.Left ? a.Bounds.Left.CompareTo(b.Bounds.Left) : a.Bounds.Top.CompareTo(b.Bounds.Top); });
            return order;
        }

        // Their monitors for list_windows, left to right, when there's more than one: what her tools call each, its size in pixels,
        // whether it's portrait, and whether it sits above or below another.
        internal static string Monitors()
        {
            List<Display> screens;
            try { screens = Displays(); }
            catch (Exception) { return ""; }
            if (screens == null || screens.Count < 2) return "";
            List<string> said = new List<string>();
            foreach (Display screen in Ordered(screens))
            {
                string line = screen.Name + " " + screen.Bounds.Width + " x " + screen.Bounds.Height + (screen.Bounds.Height > screen.Bounds.Width ? ", portrait" : "");
                foreach (Display other in screens)
                {
                    if (other == screen) continue;
                    int shared = Math.Min(screen.Bounds.Right, other.Bounds.Right) - Math.Max(screen.Bounds.Left, other.Bounds.Left);
                    if (shared < Math.Min(screen.Bounds.Width, other.Bounds.Width) / 2) continue;
                    if (screen.Bounds.Bottom <= other.Bounds.Top) { line += ", above " + other.Name; break; }
                    if (screen.Bounds.Top >= other.Bounds.Bottom) { line += ", below " + other.Name; break; }
                }
                said.Add(line);
            }
            return "\nMonitors, left to right: " + string.Join("; ", said.ToArray()) + ".";
        }

        // Where one window is, for list_windows when it asks about one app: its size, and where its top left corner is on its
        // monitor, the way move and resize take them.
        internal static string Placed(OpenWindow window)
        {
            Spot spot;
            List<Display> screens;
            try
            {
                spot = Read(window.Handle);
                screens = Displays();
            }
            catch (Exception) { return null; }
            if (spot == null || spot.Gone || spot.Minimized || screens == null || screens.Count == 0) return null;
            if (spot.Maximized) return "maximized";
            Rectangle work = screens[Under(spot.Edges, screens)].Work;
            return spot.Edges.Width + " x " + spot.Edges.Height + " at " + (spot.Edges.Left - work.Left) + ", " + (spot.Edges.Top - work.Top);
        }

        private static List<Display> ReadDisplays()
        {
            List<Display> found = new List<Display>();
            List<int> taken = new List<int>();
            foreach (Screen screen in Screen.AllScreens)
            {
                Match digits = Regex.Match(screen.DeviceName ?? "", @"(\d+)$");
                int number = digits.Success && digits.Length < 3 ? int.Parse(digits.Value, CultureInfo.InvariantCulture) : 0;
                if (taken.Contains(number)) number = 0;
                taken.Add(number);
                Rectangle bounds = screen.Bounds;
                found.Add(new Display { Bounds = bounds, Work = screen.WorkingArea, Primary = screen.Primary, Number = number, Dpi = DpiAt(new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2)) });
            }
            Named(found);
            return found;
        }

        // What her tools call each monitor, the way list_windows and her own moves say them: "main screen", "screen 2".
        internal static void Named(List<Display> screens)
        {
            List<Display> order = Ordered(screens);
            foreach (Display screen in screens)
                screen.Name = screen.Primary ? "main screen" : "screen " + (screen.Number > 0 ? screen.Number : order.IndexOf(screen) + 1);
        }

        private static int DpiAt(Point point)
        {
            try
            {
                IntPtr monitor = MonitorFromPoint(new Spotted { X = point.X, Y = point.Y }, 2);
                uint across, down;
                if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, 0, out across, out down) == 0 && across > 0) return (int)across;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            return 96;
        }

        private static Spot ReadSpot(IntPtr window)
        {
            Spot spot = new Spot();
            if (!IsWindow(window)) { spot.Gone = true; return spot; }
            spot.Minimized = IsIconic(window);
            spot.Maximized = IsZoomed(window);
            spot.Edges = Visible(window);
            return spot;
        }

        // The window's edges as people see them: Windows 10 and 11 pad a window with invisible borders for resizing.
        private static Rectangle Visible(IntPtr window)
        {
            Edges frame;
            if (DwmGetWindowAttribute(window, 9, out frame, Marshal.SizeOf(typeof(Edges))) == 0 && frame.Right > frame.Left) return Rectangle.FromLTRB(frame.Left, frame.Top, frame.Right, frame.Bottom);
            Edges outer;
            return GetWindowRect(window, out outer) ? Rectangle.FromLTRB(outer.Left, outer.Top, outer.Right, outer.Bottom) : Rectangle.Empty;
        }

        private static bool ShapeWindow(IntPtr window, string how, Rectangle wanted)
        {
            if (how == "maximize" || how == "restore") SmokeRun.NoteAsync(window, "Arrange " + how);
            if (how == "maximize") return ShowWindowAsync(window, 3);
            if (how == "restore") return ShowWindowAsync(window, 9);
            // A window that crosses to a monitor of another scale resizes itself to keep its size as it looks, so it's placed again
            // once it has, with its invisible borders on the new monitor added back.
            for (int pass = 0; pass < 3; pass++)
            {
                Edges outer;
                Rectangle edges = Visible(window), padded = wanted;
                if (GetWindowRect(window, out outer) && !edges.IsEmpty)
                {
                    int left = edges.Left - outer.Left, top = edges.Top - outer.Top, right = outer.Right - edges.Right, bottom = outer.Bottom - edges.Bottom;
                    if (left >= 0 && top >= 0 && right >= 0 && bottom >= 0 && left < 40 && top < 40 && right < 40 && bottom < 40) padded = Rectangle.FromLTRB(wanted.Left - left, wanted.Top - top, wanted.Right + right, wanted.Bottom + bottom);
                }
                // Asked without waiting on the app, so a busy one can't hold her up; where it went is read afterwards.
                if (!SetWindowPos(window, IntPtr.Zero, padded.Left, padded.Top, padded.Width, padded.Height, 0x0004 | 0x0010 | 0x0200 | 0x4000)) return false;
                Thread.Sleep(80);
                if (Near(Visible(window), wanted, 1)) break;
            }
            return true;
        }

        private static bool AboveHer(IntPtr window)
        {
            int id;
            GetWindowThreadProcessId(window, out id);
            if (id == 0) return false;
            int own;
            using (Process me = Process.GetCurrentProcess()) own = me.Id;
            return Elevated(id) == true && Elevated(own) != true;
        }

        // Whether a program runs as administrator. A token she isn't allowed to read counts as one: Windows guards those.
        private static bool? Elevated(int id)
        {
            IntPtr process = OpenProcess(0x1000, false, id);
            if (process == IntPtr.Zero) return null;
            try
            {
                IntPtr token;
                if (!OpenProcessToken(process, 0x0008, out token)) return Marshal.GetLastWin32Error() == 5 ? true : (bool?)null;
                try
                {
                    int elevated, size;
                    return GetTokenInformation(token, 20, out elevated, 4, out size) ? elevated != 0 : (bool?)null;
                }
                finally { CloseHandle(token); }
            }
            finally { CloseHandle(process); }
        }

        [StructLayout(LayoutKind.Sequential)] private struct Edges { internal int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct Spotted { internal int X, Y; }
        private static bool OnTop(IntPtr window) { return (GetWindowLong(window, -20) & 0x8) != 0; }

        private static bool KeepOnTop(IntPtr window, bool pin) { return SetWindowPos(window, new IntPtr(pin ? -1 : -2), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x4000); }

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsHungAppWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Edges edges);
        [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, int flags);
        [DllImport("user32.dll")] private static extern int GetWindowThreadProcessId(IntPtr window, out int process);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Spotted point, int flags);
        [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int kind, out uint x, out uint y);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out Edges value, int size);
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(int access, bool inherit, int id);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, int access, out IntPtr token);
        [DllImport("advapi32.dll")] private static extern bool GetTokenInformation(IntPtr token, int kind, out int value, int size, out int returned);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    }
}

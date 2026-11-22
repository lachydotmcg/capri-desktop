using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace CapriDesktop
{
    // The cursor the way Windows is drawing it right now, and its word the moment a cursor hides or shows or another window comes
    // in front.
    internal static class GameCursor
    {
        [StructLayout(LayoutKind.Sequential)] private struct Info { internal int Size, Flags; internal IntPtr Handle; internal int X, Y; }
        internal delegate void Changed(IntPtr hook, int kind, IntPtr window, int item, int child, int thread, int time);
        [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref Info info);
        [DllImport("user32.dll")] private static extern IntPtr GetCapture();
        [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(int first, int last, IntPtr module, Changed changed, int process, int thread, int flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);

        internal const int Front = 0x0003, Shown = 0x8002, Gone = 0x8003, Pointer = -9;

        internal struct Reading
        {
            internal bool Read, Hidden;
            internal Point At;
            internal int Flags;
            internal IntPtr Shape;

            public override string ToString() { return (Read ? "flags " + Flags + ", cursor 0x" + Shape.ToInt64().ToString("X") : "no cursor to read") + " at " + At.X + "," + At.Y; }
        }

        // A game can hide its cursor or set none at all, and both count as hidden. Touch and pen hiding it don't.
        internal static Func<Reading> Read = delegate
        {
            Info info = new Info { Size = Marshal.SizeOf(typeof(Info)) };
            Reading seen = new Reading { Read = GetCursorInfo(ref info) };
            seen.At = new Point(info.X, info.Y);
            seen.Flags = info.Flags;
            seen.Shape = info.Handle;
            seen.Hidden = seen.Read && ((info.Flags & 3) == 0 || ((info.Flags & 1) != 0 && info.Handle == IntPtr.Zero));
            return seen;
        };

        // One of her windows holding the mouse, mid-drag or with a menu open, so the cursor's hers wherever it is.
        internal static Func<bool> Held = delegate { return GetCapture() != IntPtr.Zero; };

        internal static IntPtr Watch(int first, int last, Changed changed) { return SetWinEventHook(first, last, IntPtr.Zero, changed, 0, 0, 0); }

        internal static void Stop(IntPtr hook) { if (hook != IntPtr.Zero) UnhookWinEvent(hook); }

        // The program whose window is under a spot on screen, or 0 when nothing is.
        internal static int ProcessAt(Point at)
        {
            IntPtr window = Native.GetAncestor(Native.WindowFromPoint(new Native.Point(at.X, at.Y)), 2);
            int process;
            return window == IntPtr.Zero || Native.GetWindowThreadProcessId(window, out process) == 0 ? 0 : process;
        }
    }

    // A game that hides its cursor to aim still moves that cursor about, over her too, and a shot that landed on her went to her
    // instead: a right click opened her menu and the game lost focus. So while a game's in front with its cursor hidden, clicks go
    // through every window of hers to the game, the way Hologram lets them, and none of them can take focus. As soon as the game
    // shows its cursor for a menu or a pause, or something else comes in front, she's clickable again.
    internal sealed partial class Companion
    {
        private bool gameAim, aimHidden;
        private IntPtr aimFront, cursorHook, frontHook;
        private int aimGame;
        private double aimLooked = double.NegativeInfinity;
        private GameCursor.Changed cursorChanged;
        private static readonly int aimOwn = OwnProcess();

        internal bool GameAiming { get { return gameAim; } }

        private static int OwnProcess() { using (Process self = Process.GetCurrentProcess()) return self.Id; }

        private void WatchGameCursor(bool on)
        {
            GameCursor.Stop(cursorHook);
            GameCursor.Stop(frontHook);
            cursorHook = frontHook = IntPtr.Zero;
            if (!on) return;
            if (cursorChanged == null) cursorChanged = CursorSeen;
            cursorHook = GameCursor.Watch(GameCursor.Shown, GameCursor.Gone, cursorChanged);
            frontHook = GameCursor.Watch(GameCursor.Front, GameCursor.Front, cursorChanged);
        }

        private void CursorSeen(IntPtr hook, int kind, IntPtr window, int item, int child, int thread, int time)
        {
            if (kind != GameCursor.Front && item != GameCursor.Pointer) return;
            if (!quitting && !IsDisposed) CheckGameCursor(clock.Elapsed.TotalSeconds);
        }

        // Windows keeps a hidden cursor per program, so over her own window it shows as hers whatever the game did with it, and what
        // it says there never counts. Over the game's own window it's the game's. Once she lets clicks through, the cursor's over the
        // game even where she's drawn, so it keeps reading true from then on. What's in front only gets looked at while a cursor's
        // hidden.
        private void CheckGameCursor(double now)
        {
            if (!IsHandleCreated) return;
            try
            {
                IntPtr front = Hands.Foreground();
                if (front != aimFront) { aimFront = front; aimLooked = double.NegativeInfinity; aimHidden = false; }
                if (!GameCursor.Held() && !menu.Visible)
                {
                    GameCursor.Reading seen = GameCursor.Read();
                    int under = seen.Read ? GameCursor.ProcessAt(seen.At) : aimOwn;
                    if (under != aimOwn) aimHidden = seen.Hidden && under == GameAt(front, now);
                }
                if (aimHidden && GameAt(front, now) == 0) aimHidden = false;
            }
            catch (Exception) { aimHidden = false; }
            Aim(aimHidden);
        }

        // The program of the game in front, or 0 when what's in front isn't a game. It's looked at again once a second.
        private int GameAt(IntPtr front, double now)
        {
            if (now - aimLooked < 1) return aimGame;
            aimLooked = now;
            int process;
            aimGame = front != IntPtr.Zero && Native.GetWindowThreadProcessId(front, out process) != 0 && process != 0 && process != aimOwn && GameInFront(front) ? process : 0;
            return aimGame;
        }

        private bool GameInFront(IntPtr front)
        {
            Sight sight = Computer.Glimpse();
            return sight != null && sight.Window == front && GameAhead(sight) != null;
        }

        private void Aim(bool on)
        {
            if (gameAim == on) return;
            gameAim = on;
            PassThrough();
        }
    }
}

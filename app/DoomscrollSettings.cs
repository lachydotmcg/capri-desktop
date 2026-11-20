using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.Serialization;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Preferences
    {
        [DataMember] public bool DoomscrollGuard;
        [DataMember] public int DoomscrollLimit;
    }

    internal sealed partial class Companion
    {
        private readonly Doomscroll doomscroll = new Doomscroll();
        private bool doomSulk;

        internal bool DoomscrollOn { get { return preferences.DoomscrollGuard; } }

        internal int DoomscrollLimit { get { return Doomscroll.Limit(preferences.DoomscrollLimit); } }

        internal int DoomscrollStage { get { return doomscroll.Stage; } }

        internal void ToggleDoomscroll()
        {
            preferences.DoomscrollGuard = !preferences.DoomscrollGuard;
            EndDoomscroll();
            SavePreferences();
            mainframe.Sync();
        }

        internal void SetDoomscrollStep(int seconds)
        {
            bool on = seconds > 0;
            int limit = Doomscroll.Limit(seconds);
            if (on == preferences.DoomscrollGuard && (!on || preferences.DoomscrollLimit == limit)) return;
            if (on) preferences.DoomscrollLimit = limit;
            preferences.DoomscrollGuard = on;
            EndDoomscroll();
            SavePreferences();
            mainframe.Sync();
        }

        internal void SetDoomscrollLimit(int seconds)
        {
            int limit = Doomscroll.Limit(seconds);
            if (limit == DoomscrollLimit && preferences.DoomscrollLimit == limit) return;
            preferences.DoomscrollLimit = limit;
            EndDoomscroll();
            SavePreferences();
            mainframe.Sync();
        }

        private void EndDoomscroll()
        {
            doomscroll.Reset();
            EndDoomSulk();
        }

        private void EndDoomSulk()
        {
            if (!doomSulk) return;
            doomSulk = false;
            if (Sulking) Forgive("They put the doomscroll down, so Capri isn't sulking about it any more.");
        }

        private bool DoomFree(bool full)
        {
            return DoomscrollOn && !full && !CallModeOn && !preferences.CallMode && !chatting && !Hushed && !hidden && !quitting && !IsDisposed && !dragging && !pressed
                && !typer.Visible && !menu.Visible && !docked && !onPanel && !riding && !falling && outro == null && job == null && working == null
                && !LineBusy && speech == null && talkState == TalkState.Off && !Asleep;
        }

        private void CheckDoomscroll(Sight ahead, bool full, double now)
        {
            if (!DoomscrollOn && !doomscroll.Active && !doomSulk) return;
            if (ScrollingForThem) { doomscroll.Reset(); return; }
            if (ahead != null && ahead.Window != Hands.Foreground()) ahead = null;
            string line;
            DoomMove move = doomscroll.Tick(ahead == null ? null : ahead.Program, ahead == null ? null : ahead.Title, DoomscrollOn, preferences.DoomscrollLimit, DoomFree(full), now, out line);
            if (move == DoomMove.None)
            {
                if (doomSulk && !doomscroll.Active) EndDoomSulk();
                return;
            }
            IntPtr window = ahead == null ? IntPtr.Zero : ahead.Window;
            switch (move)
            {
                case DoomMove.Nudge:
                    Animate("glitch");
                    huskMood = "error";
                    huskMoodAt = now;
                    break;
                case DoomMove.Perch:
                    DoomPerch(window);
                    break;
                case DoomMove.Sulk:
                    DoomSulk(now);
                    break;
                case DoomMove.Pout:
                    if (!Sulking) DoomSulk(now);
                    else Animate("glitch");
                    break;
                case DoomMove.Free:
                    EndDoomSulk();
                    if (lying) SitUp();
                    Animate("grin");
                    break;
            }
            frames.Start();
            SayOwn("doomscroll", line);
        }

        private void DoomSulk(double now)
        {
            if (!Sulking) Sulk(now, "They've been doomscrolling for ages and ignored Capri's nudges, so she's sulking dramatically: she won't let them pick her up until they say sorry, give her a head pat, say five more minutes or put the scrolling down. Keep it playful, never preachy. She still answers and helps them as usual.");
            doomSulk = true;
            LieHere();
        }

        private void DoomPerch(IntPtr window)
        {
            Rectangle bounds;
            if (window == IntPtr.Zero || !Ledges.Showing(window, out bounds)) { DoomHang(MouseAt()); return; }
            if (Ledges.Seatable(window, bounds))
            {
                List<Ledge> spots = Around().FindAll(ledge => ledge.Window == window);
                int room = Headroom();
                spots.RemoveAll(ledge => !ledge.Icon.IsEmpty || ledge.Right - ledge.Left < 24 || ledge.Y - room < Screen.FromPoint(new Point(ledge.Left, ledge.Y)).Bounds.Top);
                if (spots.Count > 0)
                {
                    Ledge spot = spots[0];
                    foreach (Ledge each in spots) if (each.Right - each.Left > spot.Right - spot.Left) spot = each;
                    int margin = Math.Min(preferences.Size / 4, (spot.Right - spot.Left) / 3);
                    int x = spot.Left + margin + random.Next(Math.Max(1, spot.Right - spot.Left - 2 * margin));
                    StopRide();
                    LeavePanel(false);
                    EndWalk();
                    hanging = false;
                    danceAsked = false;
                    GlitchOut(delegate { Hop(spot, x); }, false);
                    return;
                }
            }
            DoomHang(new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 3));
        }

        private void DoomHang(Point to)
        {
            Rectangle area = Screen.FromPoint(to).WorkingArea;
            Action arrive = delegate
            {
                StopRide();
                LeavePanel(false);
                seat = null;
                hanging = false;
                falling = false;
                walking = false;
                danceAsked = false;
                anchor = Within(Seated(to.X, to.Y), area, Body);
                ShowHidden(false);
                HangHere();
            };
            if (hidden) arrive();
            else GlitchOut(arrive, false);
        }

        private string DoomSnooze(string said)
        {
            if (!doomscroll.Active) return null;
            bool refused;
            string line = doomscroll.Snooze(said, out refused);
            if (line == null) return null;
            if (refused) DoomSulk(clock.Elapsed.TotalSeconds);
            else
            {
                EndDoomSulk();
                if (lying) SitUp();
            }
            return line;
        }

        internal string DoomscrollShown { get { return (preferences.DoomscrollGuard ? "on" : "off") + " | " + DoomscrollLimit + " | " + doomscroll.Stage; } }
    }

    internal sealed partial class Mainframe
    {
        private Slider doomLimit;
        private static readonly string[] ScrollNames = WithOff(Doomscroll.LimitNames);

        private static string[] WithOff(string[] names)
        {
            string[] all = new string[names.Length + 1];
            all[0] = "Off";
            names.CopyTo(all, 1);
            return all;
        }

        private void BuildDoomscroll(int y)
        {
            Section("DOOMSCROLL >", 0, y);
            doomLimit = Setting("Scroll limit", ScrollNames, 0, y + 24, "Doomscroll guard: how long TikTok, YouTube Shorts, Instagram, Reddit or X can stay in front before she reacts. Off by default, and Off turns the guard off");
            doomLimit.Changed += delegate { if (!syncing) capri.SetDoomscrollStep(doomLimit.Value == 0 ? 0 : Doomscroll.Limits[doomLimit.Value - 1]); };
            Label hint = Caption("Sites are in personalSettings\\sys.md. Say\n\"5 more minutes\" and she gives you five.", 28, y + 56, Look.Mono(8.5f, FontStyle.Regular, GraphicsUnit.Point), Faint);
            hint.AutoSize = false;
            hint.Bounds = new Rectangle(28, y + 56, 328, 36);
        }

        private void SyncDoomscroll()
        {
            doomLimit.Value = capri.DoomscrollOn ? 1 + Math.Max(0, Array.IndexOf(Doomscroll.Limits, capri.DoomscrollLimit)) : 0;
        }

        internal string DoomscrollShown { get { return (doomLimit.Value == 0 ? "off" : "on") + " | " + Doomscroll.LimitNames[Math.Max(0, Array.IndexOf(Doomscroll.Limits, capri.DoomscrollLimit))]; } }

        internal void SlideDoomscroll(int steps) { Press(doomLimit, steps); }

    }
}

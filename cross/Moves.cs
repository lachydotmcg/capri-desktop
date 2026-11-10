using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Threading;

namespace CapriDesktop
{
    internal sealed partial class Pet
    {
        private const double LieScale = 0.85;
        private const double LieShiftY = (SitY - 0.5) * (1 - LieScale) + 94.0 / 384 * LieScale;
        private const double LieHeight = 250.0 / 384 * LieScale;
        private const double LieRight = 0.5 + (0.806 - 0.5) * LieScale;
        private readonly Clip laydown = new Clip("laydown", 60, 30, LieHeight, 0, LieShiftY);
        private readonly Clip laydownGlitch = new Clip("laydown_glitch", 10, 15, LieHeight, 0, LieShiftY);
        private readonly Clip laydownTalk = new Clip("laydown_talk", 60, 30, LieHeight, 0, LieShiftY);
        private bool lying, riding, askedHop;
        private double lieBy = -1;
        private Func<double> MoveClock;
        private Func<bool, bool> RideThrough;
        private Action<Action> MovePost = action => Dispatcher.UIThread.Post(action);
        private PixelRect? MoveArea;
        private bool LyingClip { get { return clip == laydown || clip == laydownGlitch || clip == laydownTalk; } }

        private string AskedMove(string action, string window)
        {
            if (action == "sit_on") return SitOn(window, false);
            if (action == "lie_down") return string.IsNullOrWhiteSpace(window) ? LieHere() : SitOn(window, true);
            if (action == "sit_up" || action == "sit" && lying)
            {
                bool was = lying || lieBy >= 0;
                if (askedHop) Halt();
                askedHop = false;
                SitUp();
                return was ? "Sitting up." : "I'm not lying down.";
            }
            if (action == "ride_cursor")
            {
                if (riding) return "Already riding your cursor.";
                PixelPoint? cursor = ReadMoveMouse();
                if (cursor == null) return "Couldn't find your mouse on this desktop.";
                StopMoves();
                ComeBack();
                Halt();
                EndWalk();
                dropAfter = false;
                falling = hanging = false;
                fallTo = seat = null;
                dancing = false;
                if (!CursorThrough(true))
                {
                    CursorThrough(false);
                    Fall();
                    return "Couldn't let clicks through my window on this desktop.";
                }
                riding = true;
                Play(perchGlitch);
                FollowCursor(cursor.Value, false);
                return "Riding your cursor. Say hop off my cursor to get me off.";
            }
            if (action == "hop_off")
            {
                if (!riding) return "I'm not riding your cursor.";
                HopOff();
                return "Hopped off your cursor.";
            }
            if (action != "walk_around" && action != "wander") return null;
            if (action == "walk_around" && walking) return "Already walking.";
            StopMoves();
            ComeBack();
            dancing = false;
            if (hanging) { hanging = false; Fall(); }
            if (clip == perchDance) Play(perch);
            if (action == "walk_around" && Stroll()) return "Going for a little walk.";
            if (!Wander(true)) return action == "wander" ? "There's nowhere else to go right now." : "There's no room to walk around here.";
            askedHop = true;
            return action == "wander" ? "Off somewhere else." : "Wandering off somewhere.";
        }

        private PixelPoint? ReadMoveMouse()
        {
            try { return MouseAt != null ? MouseAt() : Mouse(); }
            catch (Exception) { return null; }
        }

        private void ClearMoves()
        {
            if (riding && !CursorThrough(false)) Talk.Notice("(move)", "Couldn't turn my window's clicks back on.");
            riding = false;
            askedHop = false;
            SitUp();
        }

        private void StopMoves()
        {
            bool rode = riding;
            if (askedHop) Halt();
            ClearMoves();
            EndWalk();
            if (rode) Fall();
        }

        private void StopMovesForWork()
        {
            if (Dispatcher.UIThread.CheckAccess()) StopMoves();
            else MovePost(StopMoves);
        }

        private bool CursorThrough(bool through)
        {
            if (RideThrough != null) return RideThrough(through);
            try { return OperatingSystem.IsMacOS() ? Ink.Through(this, through) : HerCursor.ClicksThrough(this, through); }
            catch (Exception) { return false; }
        }

        private void HopOff()
        {
            Fall();
            Play(grin);
        }

        private bool MoveRoom(Ledge ledge)
        {
            PixelRect area = WorkAt(new PixelPoint(ledge.Left + (ledge.Right - ledge.Left) / 2, ledge.Y - 1));
            return ledge.Y >= area.Y + Headroom && ledge.Y <= area.Bottom
                && Math.Min(ledge.Right, area.Right) - Math.Max(ledge.Left, area.X) >= Px(body.Size / 2);
        }

        private bool MovePlace(ref Ledge spot, ref int x)
        {
            Ledge found = null;
            foreach (Ledge ledge in Ledges())
            {
                if (ledge.Window != spot.Window || ledge.Floor != spot.Floor || !MoveRoom(ledge)) continue;
                if (ledge.Floor && (ledge.Left != spot.Left || ledge.Y != spot.Y)) continue;
                if (found == null || ledge.Left <= x && ledge.Right >= x) found = ledge;
            }
            if (found == null) return false;
            PixelRect area = WorkAt(new PixelPoint(found.Left + (found.Right - found.Left) / 2, found.Y - 1));
            int from = Math.Max(found.Left + Px(8), area.X + Px(body.Size * (SitAcross - PerchNear)));
            int to = Math.Min(found.Right - Px(8), area.Right - Px(body.Size * (PerchFar - SitAcross)));
            if (from > to) return false;
            x = Math.Max(from, Math.Min(x, to));
            spot = found;
            return true;
        }

        private void FollowCursor(PixelPoint cursor, bool smooth)
        {
            PixelRect area = WorkAt(cursor);
            int x = cursor.X + Px(body.Size * (SitAcross - 0.267)) + Px(12);
            int y = cursor.Y + Px(body.Size * (SitY - 0.73)) - Px(12);
            int left = area.X + Px(body.Size * (SitAcross - PerchNear));
            int right = area.Right - Px(body.Size * (PerchFar - SitAcross));
            int top = area.Y + Headroom;
            int bottom = area.Bottom - Px(body.Size * (1 - SitY));
            x = Math.Max(left, Math.Min(x, right));
            y = Math.Max(top, Math.Min(y, bottom));
            if (smooth)
            {
                x = Hip.X + (int)Math.Round((x - Hip.X) * 0.25);
                y = Hip.Y + (int)Math.Round((y - Hip.Y) * 0.25);
            }
            Move(Seated(Math.Max(left, Math.Min(x, right)), Math.Max(top, Math.Min(y, bottom))));
        }

        private void TickMoves(double now)
        {
            if (riding)
            {
                PixelPoint? cursor = ReadMoveMouse();
                if (cursor == null)
                {
                    HopOff();
                    Talk.Notice("(move)", "Couldn't find your mouse. I've hopped off.");
                }
                else FollowCursor(cursor.Value, true);
            }
            if (askedHop && outro == null) askedHop = false;
            if (lieBy < 0) return;
            if (now > lieBy) { lieBy = -1; Talk.Notice("(move)", "Couldn't find room to lie down here."); return; }
            if (seat != null && !falling && clip == perch)
            {
                lieBy = -1;
                if (!LieDown()) Talk.Notice("(move)", "There's no room to lie down here.");
            }
        }

        private void SitUp()
        {
            lieBy = -1;
            if (!lying) return;
            PixelPoint hip = Hip;
            lying = false;
            Fit();
            Move(Seated(hip.X, hip.Y));
            Play(seat != null ? perchGlitch : idle);
        }

        private bool LieDown()
        {
            if (seat == null || seat.Free || walking || falling || riding || dragging || hanging) return false;
            PixelPoint hip = Hip;
            PixelRect area = WorkAt(new PixelPoint(hip.X, seat.Y - 1));
            int torso = Px(body.Size * 0.45 * LieScale);
            int reach = Px(body.Size * 0.78 * LieScale);
            int toes = Px(body.Size * (0.5 + 182.0 / 384 * LieScale - LieRight));
            int room = Px(body.Size * (SitY - (0.5 + LieShiftY - LieHeight / 2)));
            if (seat.Y - room < area.Y) return false;
            int rightFrom = Math.Max(seat.Left + torso, area.X + reach);
            int rightTo = Math.Min(seat.Right, area.Right - toes);
            int leftFrom = Math.Max(seat.Left, area.X + toes);
            int leftTo = Math.Min(seat.Right - torso, area.Right - reach);
            bool right = rightFrom <= rightTo, left = leftFrom <= leftTo;
            if (!right && !left) return false;
            int rightX = Math.Max(rightFrom, Math.Min(hip.X, rightTo));
            int leftX = Math.Max(leftFrom, Math.Min(hip.X, leftTo));
            bool way = left && (!right || Math.Abs(leftX - hip.X) < Math.Abs(rightX - hip.X) || FacingLeft && Math.Abs(leftX - hip.X) == Math.Abs(rightX - hip.X));
            SetFacing(way);
            lying = true;
            Fit();
            Move(Seated(way ? leftX : rightX, seat.Y));
            Play(laydownGlitch);
            return true;
        }

        private string LieHere()
        {
            if (lying) return "Already lying down.";
            StopMoves();
            ComeBack();
            Halt();
            dancing = false;
            if (seat != null && !seat.Free && !falling)
            {
                if (clip == perch) return LieDown() ? "Lying down." : "There's no room to lie down here.";
            }
            else Fall();
            lieBy = Now + 10;
            return "Lying down.";
        }

        private string SitOn(string wanted, bool lie)
        {
            if (Hands.OnMac) return lie ? "Lying down on a window you name is only on Windows so far." : "Sitting on a window you name is only on Windows so far.";
            string name = Regex.Replace((wanted ?? "").Trim(), @"^(?:the|my|your|ur|our|that|this) | (?:window|app|application|game|tab)$", "", RegexOptions.IgnoreCase).Trim();
            if (name.Length == 0) return "Which window?";
            List<Ledge> ledges = Ledges();
            List<Ledge> spots = new List<Ledge>();
            string shown = "your " + name + " window";
            if (Hands.TaskbarWords.IsMatch(name))
            {
                PixelRect area = WorkAt(Hip);
                spots = ledges.FindAll(ledge => ledge.Floor && ledge.Y == area.Bottom && ledge.Left == area.X);
                shown = "the taskbar";
            }
            else
            {
                List<OpenWindow> windows = Hands.WindowsNamed(name);
                if (windows.Count == 0) return Hands.Unlisted() ?? "I can't see " + name + " open.";
                foreach (OpenWindow window in windows)
                {
                    spots = ledges.FindAll(ledge => ledge.Window == window.Handle.ToInt64());
                    if (spots.Count > 0) break;
                }
            }
            spots.RemoveAll(ledge => !MoveRoom(ledge));
            if (spots.Count == 0) return "There's no room for me on top of " + shown + ".";
            Ledge spot = spots[0];
            foreach (Ledge each in spots) if (each.Right - each.Left > spot.Right - spot.Left) spot = each;
            if (seat != null && !seat.Free && seat.Window == spot.Window && seat.Y == spot.Y && seat.Left == spot.Left && !walking && !falling && !riding)
                return lie ? LieHere() : "Already sitting on " + shown + ".";
            StopMoves();
            ComeBack();
            Halt();
            dancing = false;
            int x = spot.Left + (spot.Right - spot.Left) / 2;
            GlitchOut(delegate { Hop(spot, x); }, true);
            askedHop = true;
            if (lie) lieBy = Now + 10;
            return (lie ? "Lying down on " : "Sitting on ") + shown + ".";
        }

        private sealed class MoveDesk : Desk
        {
            internal readonly List<Spot> Shown = new List<Spot>();
            internal override string Name { get { return "moves test"; } }
            internal override List<Spot> Windows(ICollection<long> skip) { return Shown; }
            internal override Spot Find(long window) { return Shown.Find(spot => spot.Window == window); }
            internal override bool Alive(long window) { return Find(window) != null; }
        }

        internal static void MoveChecks(Pet pet, List<string> checks)
        {
            var fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            string[] names = { "anchor", "seat", "fallTo", "falling", "fallSpeed", "fallStep", "hanging", "walking", "walkTo", "walkStep", "walkCarry", "dragging", "pressed", "dropAfter", "outro", "outroAt", "clip", "clipStart", "idleCycle", "blinkWith", "nextGlitch", "coveredSince", "seatLost", "napAsked", "napping", "dancing", "lying", "riding", "askedHop", "lieBy", "Arrivals" };
            Dictionary<string, object> before = new Dictionary<string, object>();
            foreach (string name in names) before[name] = typeof(Pet).GetField(name, fields).GetValue(pet);
            Desk desk = pet.Desk;
            Func<PixelPoint?> mouse = pet.MouseAt;
            Action<PixelPoint> moveTo = pet.MoveTo;
            Func<double> clock = pet.MoveClock;
            Func<bool, bool> through = pet.RideThrough;
            Action<Action> post = pet.MovePost;
            Func<string, bool?> setting = Hands.SettingOn;
            Action<bool> control = InControl.Changed;
            PixelRect? moveArea = pet.MoveArea;
            Func<List<OpenWindow>> windows = Hands.Windows;
            Func<string, string, string> moveSelf = Hands.MoveSelf;
            bool mac = Hands.OnMac, facing = pet.FacingLeft;
            int savedX = pet.Saved.X, savedY = pet.Saved.Y;
            bool placed = pet.Saved.Placed;
            PixelPoint[] asked = pet.asked.ToArray();
            PixelRect area = new PixelRect(-800, -100, 1600, 1000);
            double now = 0;
            PixelPoint? cursor = new PixelPoint(0, 400);
            bool clicksThrough = false;
            var clicks = new List<bool>();
            MoveDesk fake = new MoveDesk();
            fake.Shown.Add(new Spot { Window = 71, Bounds = new PixelRect(-740, 350, 1400, 120), Seat = true, Solid = true });
            fake.Shown.Add(new Spot { Window = 72, Bounds = new PixelRect(-680, 580, 1200, 120), Seat = true, Solid = true });
            Action step = delegate
            {
                now += 0.05;
                if (pet.outro != null && now >= pet.outroAt) { Action next = pet.outro; pet.outro = null; next(); }
                pet.TickMoves(now);
                if (pet.falling) pet.Tumble(now);
                else if (pet.seat != null) pet.HoldSeat(now);
                if (pet.walking) pet.Walk(now);
                pet.Face();
                pet.Advance(now);
                PixelPoint hip = pet.Hip;
                if (hip.X < area.X || hip.X >= area.Right || hip.Y < area.Y || hip.Y > area.Bottom)
                    throw new Exception("An asked move left the work area: " + hip + ".");
                if (pet.riding && (hip.X - pet.Px(pet.body.Size * (pet.SitAcross - pet.PerchNear)) < area.X
                    || hip.X + pet.Px(pet.body.Size * (pet.PerchFar - pet.SitAcross)) > area.Right || hip.Y - pet.Headroom < area.Y))
                    throw new Exception("Riding put her body outside the work area.");
                if (pet.lying)
                {
                    int reach = pet.Px(pet.body.Size * 0.78 * LieScale);
                    int toes = pet.Px(pet.body.Size * (0.5 + 182.0 / 384 * LieScale - LieRight));
                    if (hip.X - (pet.FacingLeft ? toes : reach) < area.X || hip.X + (pet.FacingLeft ? reach : toes) > area.Right)
                        throw new Exception("Lying put her body outside the work area.");
                }
            };
            Action settle = delegate
            {
                for (int i = 0; i < 1000 && (pet.walking || pet.falling || pet.outro != null || pet.lieBy >= 0 || pet.clip == pet.perchArrive || pet.clip == pet.perchGlitch); i++) step();
                if (pet.seat == null || pet.seat.Free || pet.Hip.Y != pet.seat.Y || pet.walking || pet.falling || pet.outro != null)
                    throw new Exception("An asked move did not end on a real seat.");
            };
            Func<string, string, string> tool = (action, window) => Hands.Do("move_yourself", new ToolArguments { Action = action, Window = window });
            try
            {
                pet.Desk = fake;
                pet.MoveClock = () => now;
                pet.RideThrough = on => { clicksThrough = on; clicks.Add(on); return true; };
                Hands.SettingOn = _ => true;
                InControl.Changed = _ => { };
                pet.MoveArea = area;
                pet.MoveTo = _ => { };
                pet.MouseAt = () => cursor;
                Hands.MoveSelf = pet.MoveTool;
                Hands.Windows = () => new List<OpenWindow> { new OpenWindow(new IntPtr(71), "Roblox", "roblox"), new OpenWindow(new IntPtr(72), "Discord", "discord") };
                pet.outro = null;
                pet.walking = pet.falling = pet.dragging = pet.pressed = pet.hanging = pet.lying = pet.riding = pet.askedHop = false;
                pet.napAsked = pet.napping = pet.dancing = pet.dropAfter = false;
                pet.lieBy = -1;
                pet.Fit();
                pet.Move(pet.Seated(0, 350));
                pet.Land(pet.Ledges().Find(ledge => ledge.Window == 71));
                pet.Play(pet.perch);
                foreach (bool platform in new[] { true, false })
                {
                    Hands.OnMac = platform;
                    foreach (string action in new[] { "sit_on", "lie_down", "sit_up", "walk_around", "wander", "ride_cursor", "hop_off" })
                        if (Hands.KitFor().Has("move_yourself:" + action) != (!platform || action != "sit_on")) throw new Exception("The moves list was wrong for " + action + ".");
                    if (platform && tool("sit_on", "Roblox") != "Sitting on a window you name is only on Windows so far.") throw new Exception("Mac lost its named window gap.");
                    if (platform && tool("lie_down", "Roblox") != "Lying down on a window you name is only on Windows so far.") throw new Exception("Mac named lying answered about sitting.");
                    if (tool("walk_around", null) != "Going for a little walk." || !pet.walking || pet.clip != pet.walk) throw new Exception("Walk did not start from her tool.");
                    settle();
                    pet.Move(pet.Seated(Math.Min(pet.seat.Right, area.Right) - pet.Px(pet.body.Size * 0.41) - pet.Px(8) - 1, pet.seat.Y));
                    if (tool("walk_around", null) != "Going for a little walk." || !pet.FacingLeft) throw new Exception("She did not turn at the ledge end.");
                    settle();
                    if (tool("lie_down", null) != "Lying down." || !pet.lying || pet.clip != pet.laydownGlitch) throw new Exception("Lie down did not play its glitch.");
                    for (int i = 0; i < 20; i++) step();
                    if (pet.clip != pet.laydown) throw new Exception("Lie down did not reach its loop.");
                    if (tool("sit_up", null) != "Sitting up." || pet.lying || pet.lieBy >= 0) throw new Exception("Sit up did not end lying down.");
                    settle();
                    if (tool("wander", null) != "Off somewhere else." || !pet.askedHop || pet.clip != pet.perchLeave) throw new Exception("Wander did not leave her ledge.");
                    Ledge from = pet.seat;
                    settle();
                    if (pet.seat.Window == from.Window && pet.seat.Y == from.Y) throw new Exception("Wander returned to the same ledge.");
                    int clickCount = clicks.Count;
                    if (!tool("ride_cursor", null).StartsWith("Riding your cursor.") || !pet.riding || !clicksThrough || clicks.Count != clickCount + 1 || !clicks[clickCount] || pet.clip != pet.perchGlitch) throw new Exception("Ride did not start with click-through.");
                    pet.React();
                    if (!pet.riding || !clicksThrough) throw new Exception("A stale click ended her ride.");
                    foreach (PixelPoint edge in new[] { new PixelPoint(area.X, area.Y), new PixelPoint(area.Right - 1, area.Bottom - 1), new PixelPoint(area.X, area.Bottom - 1), new PixelPoint(area.Right - 1, area.Y) })
                    {
                        cursor = edge;
                        for (int i = 0; i < 30; i++) step();
                    }
                    if (pet.clip != pet.perch || !pet.riding) throw new Exception("Riding did not keep her perched.");
                    if (tool("hop_off", null) != "Hopped off your cursor." || pet.riding || clicksThrough || clicks.Count != clickCount + 2 || clicks[clickCount + 1] || !pet.falling || pet.clip != pet.grin) throw new Exception("Hop off did not restore clicks.");
                    settle();
                    if (tool("hop_off", null) != "I'm not riding your cursor.") throw new Exception("Hop off claimed a second ride.");
                }
                Hands.OnMac = false;
                if (tool("sit_on", "my Roblox window") != "Sitting on your Roblox window." || !pet.askedHop) throw new Exception("Named seating did not use the tool.");
                settle();
                if (pet.seat.Window != 71) throw new Exception("Named seating picked the wrong window.");
                tool("lie_down", "Discord");
                settle();
                if (!pet.lying || pet.seat.Window != 72) throw new Exception("Named lying picked the wrong ledge.");
                foreach (string action in new[] { "sit_on", "lie_down", "walk_around", "wander", "ride_cursor" })
                {
                    pet.StopMoves();
                    pet.Play(pet.perch);
                    tool(action, action == "sit_on" ? "Roblox" : null);
                    pet.Grab();
                    if (pet.riding || clicksThrough || pet.lying || pet.walking || pet.askedHop || pet.lieBy >= 0 || pet.outro != null || pet.clip != pet.dangle) throw new Exception("Dragging did not end " + action + ".");
                    pet.Fall();
                    pet.dragging = false;
                    settle();
                }
                tool("ride_cursor", null);
                pet.SitHere();
                if (pet.riding || clicksThrough) throw new Exception("Her sit menu left the ride blocking clicks.");
                pet.Fall();
                settle();
                tool("ride_cursor", null);
                cursor = null;
                step();
                if (pet.riding || clicksThrough || !pet.falling) throw new Exception("Losing the cursor kept her riding or blocking clicks.");
                pet.Talk.Dismiss();
                settle();
                cursor = new PixelPoint(0, 400);
                tool("ride_cursor", null);
                Action queuedMove = null;
                Exception postError = null;
                pet.MovePost = action => queuedMove = action;
                using (var posted = new System.Threading.ManualResetEventSlim())
                {
                    System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try { pet.StopMovesForWork(); }
                        catch (Exception error) { postError = error; }
                        finally { posted.Set(); }
                    });
                    if (!posted.Wait(2000)) throw new Exception("Stopping moves waited for the UI thread.");
                }
                if (postError != null || queuedMove == null || !pet.riding || !clicksThrough) throw new Exception("Work did not post its move stop.");
                queuedMove();
                if (pet.riding || clicksThrough) throw new Exception("The posted work stop left her riding.");
                pet.MovePost = post;
                settle();
                foreach (bool platform in new[] { true, false })
                {
                    Hands.OnMac = platform;
                    foreach (string action in new[] { "lie_down", "walk_around", "wander", "ride_cursor" })
                    {
                        pet.Play(pet.perch);
                        tool(action, null);
                        pet.BeginWork();
                        pet.EndWork();
                        if (!pet.Brain.Act("capri_settings", new ToolArguments { Action = "list", Name = "quiet" }).Contains(" on") || !(pet.riding || pet.lying || pet.walking || pet.askedHop || pet.lieBy >= 0)) throw new Exception("A non-job tool ended " + action + ".");
                        using (InControl.Hold())
                            if (pet.riding || clicksThrough || pet.lying || pet.walking || pet.askedHop || pet.lieBy >= 0 || pet.outro != null) throw new Exception("Computer input did not end " + action + ".");
                        settle();
                        tool(action, null);
                        pet.Nap(true);
                        if (pet.riding || clicksThrough || pet.lying || pet.walking || pet.askedHop || pet.lieBy >= 0 || pet.outro != null) throw new Exception("A nap did not end " + action + ".");
                        pet.napAsked = false;
                        settle();
                    }
                }
                pet.StandUp();
                pet.Move(pet.Seated(0, area.Bottom));
                pet.Land(pet.Ledges().Find(ledge => ledge.Floor));
                pet.Play(pet.perch);
                tool("lie_down", "Roblox");
                if (pet.outro == null || pet.lieBy < 0) throw new Exception("Named lying did not queue its landing.");
                tool("sit_up", null);
                for (int i = 0; i < 40; i++) step();
                if (pet.lying || pet.outro != null || pet.lieBy >= 0) throw new Exception("Sit up left a queued lie down.");
                tool("sit_on", "Roblox");
                settle();
                tool("lie_down", null);
                fake.Shown.RemoveAt(0);
                step();
                if (pet.lying || !pet.falling) throw new Exception("Losing her window left her lying in the air.");
                settle();
                tool("ride_cursor", null);
                pet.Nap(true);
                settle();
                pet.Play(pet.perch);
                pet.CheckNap();
                if (!pet.Asleep || pet.riding || pet.lying) throw new Exception("A nap did not sleep on a real seat.");
                pet.Nap(false);
                checks.Add("PASS asked moves on Mac and Linux use her walk, perch and laydown clips, keep her in the work area, land on real seats and stop for dragging, naps and work");
                checks.Add("PASS sit up and hop off end their moves, losing the cursor drops her safely, Linux finds a named window and Mac keeps its named window gap");
                checks.Add("PASS non-job tools keep moves running, input holds stop moves, background stops post without waiting, rides pass clicks through and exits restore clicks");
            }
            finally
            {
                pet.Talk.Dismiss();
                pet.MoveClock = clock; pet.MoveArea = moveArea;
                pet.RideThrough = through; Hands.SettingOn = setting; InControl.Changed = control;
                pet.MovePost = post;
                pet.Desk = desk; pet.MouseAt = mouse; pet.MoveTo = moveTo;
                Hands.OnMac = mac; Hands.Windows = windows; Hands.MoveSelf = moveSelf;
                foreach (string name in names) typeof(Pet).GetField(name, fields).SetValue(pet, before[name]);
                pet.body.FacingLeft = facing;
                pet.Fit();
                pet.asked.Clear(); pet.asked.AddRange(asked);
                pet.Saved.X = savedX; pet.Saved.Y = savedY; pet.Saved.Placed = placed;
            }
        }
    }
}

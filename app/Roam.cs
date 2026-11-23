using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Their monitors as she wanders them by herself. Every number is a real pixel whatever each monitor's scaling, and a monitor
    // left of or above the main one starts at a negative x or y.
    internal sealed class Roam
    {
        internal Rectangle[] Screens = new Rectangle[0];
        internal Rectangle[] Works = new Rectangle[0];
        internal string[] Names = new string[0];
        internal bool[] Full = new bool[0];
        internal int Here = -1;
        internal int Watched = -1;
        internal bool Stay;
        // The monitor of the window in front when that's full screen, like the game they're playing.
        internal Rectangle Front;

        internal int Fronted { get { return Front.IsEmpty ? -1 : Of(new Point(Front.Left + Front.Width / 2, Front.Top + Front.Height / 2)); } }

        internal static Roam Now()
        {
            Screen[] all = Screen.AllScreens;
            Roam roam = new Roam { Screens = new Rectangle[all.Length], Works = new Rectangle[all.Length], Names = new string[all.Length], Full = new bool[all.Length] };
            for (int i = 0; i < all.Length; i++)
            {
                roam.Screens[i] = all[i].Bounds;
                roam.Works[i] = all[i].WorkingArea;
                roam.Names[i] = all[i].DeviceName;
            }
            return roam;
        }

        // The screen a point is on, or the nearest one when it's on none, like a spot on a monitor that's just been unplugged.
        internal int Of(Point at)
        {
            int nearest = -1;
            long best = long.MaxValue;
            for (int i = 0; i < Screens.Length; i++)
            {
                Rectangle screen = Screens[i];
                if (screen.Contains(at)) return i;
                long dx = Math.Max(0, Math.Max(screen.Left - at.X, at.X - screen.Right + 1)), dy = Math.Max(0, Math.Max(screen.Top - at.Y, at.Y - screen.Bottom + 1));
                if (dx * dx + dy * dy >= best) continue;
                best = dx * dx + dy * dy;
                nearest = i;
            }
            return nearest;
        }

        internal int Named(string name)
        {
            for (int i = 0; i < Names.Length && !string.IsNullOrEmpty(name); i++)
                if (string.Equals(Names[i], name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        internal List<int> Open() { return Hands.RoamScreens(Full, Here, Watched, Stay); }

        // The same place to sit: the same window, or the same screen's taskbar.
        internal static bool Same(Ledge one, Ledge other) { return one.Window == other.Window && one.Floor == other.Floor && (!one.Floor || (one.Left == other.Left && one.Y == other.Y)); }

        // The ledges she may hop onto by herself: on the screens she may roam and cut at their edges, long enough for her, with
        // room for her head under that screen's top, and never the place she's sitting.
        internal List<Ledge> Spots(IEnumerable<Ledge> around, Ledge seat, int size, int room)
        {
            List<int> open = Open();
            List<Ledge> spots = new List<Ledge>();
            foreach (Ledge ledge in around)
            {
                if (!ledge.Icon.IsEmpty || ledge.Right - ledge.Left < size / 2 || (seat != null && Same(ledge, seat))) continue;
                foreach (int at in open)
                {
                    Rectangle screen = Screens[at], work = Works[at];
                    bool on = ledge.Floor ? ledge.Left == work.Left && ledge.Right == work.Right && ledge.Y == work.Bottom : ledge.Y >= screen.Top && ledge.Y < screen.Bottom;
                    int left = Math.Max(ledge.Left, screen.Left), right = Math.Min(ledge.Right, screen.Right);
                    if (!on || right - left < size / 2 || ledge.Y - room < screen.Top) continue;
                    spots.Add(left == ledge.Left && right == ledge.Right ? ledge : new Ledge { Window = ledge.Window, Bounds = ledge.Bounds, Left = left, Right = right, Y = ledge.Y });
                }
            }
            return spots;
        }

        // A taskbar she set off for that's gone or changed by the time she gets there, like one on a monitor just unplugged: she
        // lands on the nearest screen's taskbar instead. Any other spot stays as it is.
        internal Ledge Floored(Ledge spot, int x)
        {
            if (!spot.Floor || spot.Free || Works.Length == 0) return spot;
            foreach (Rectangle work in Works) if (work.Left == spot.Left && work.Right == spot.Right && work.Bottom == spot.Y) return spot;
            Rectangle near = Works[Of(new Point(x, spot.Y - 1))];
            return new Ledge { Floor = true, Left = near.Left, Right = near.Right, Y = near.Bottom };
        }
    }

    internal sealed partial class Companion
    {
        // Where she may wander by herself right now: across their screens as they are, never onto one with something full
        // screen in front, only on the one picked under WATCH > Screens while Watch my screen is on, and only on her own in a
        // call, on a job or in Gaming Mode.
        private Roam Roaming()
        {
            Roam roam = Roam.Now();
            Point hip = Hip();
            roam.Here = roam.Of(new Point(hip.X, hip.Y - 1));
            roam.Watched = preferences.Watch ? roam.Named(preferences.WatchScreen) : -1;
            roam.Stay = CallModeOn || SleepBusy || GamingOn || docked;
            // The smoke test's made-up answer for the window in front stands in for what's open on the PC here too.
            if (!Native.FakedGaming.HasValue) roam.Full = Ledges.FullFronts(roam.Screens, Skip());
            roam.Front = WanderArea();
            int fronted = roam.Fronted;
            if (fronted >= 0) roam.Full[fronted] = true;
            return roam;
        }

        private static Roam RoamLayout(Rectangle[] screens, int[] taskbars, int here)
        {
            Roam roam = new Roam { Screens = screens, Works = new Rectangle[screens.Length], Names = new string[screens.Length], Full = new bool[screens.Length], Here = here };
            for (int i = 0; i < screens.Length; i++)
            {
                roam.Works[i] = new Rectangle(screens[i].X, screens[i].Y, screens[i].Width, screens[i].Height - taskbars[i]);
                roam.Names[i] = "\\\\.\\DISPLAY" + (i + 1);
            }
            return roam;
        }

        // Ledges the way Ledges.Find makes them: each window's top edge, then every screen's taskbar.
        private static List<Ledge> RoamLedges(Roam roam, params Rectangle[] windows)
        {
            List<Ledge> around = new List<Ledge>();
            foreach (Rectangle window in windows) around.Add(new Ledge { Window = new IntPtr(around.Count + 1), Bounds = window, Left = window.Left, Right = window.Right, Y = window.Top });
            foreach (Rectangle work in roam.Works) around.Add(new Ledge { Floor = true, Left = work.Left, Right = work.Right, Y = work.Bottom });
            return around;
        }

        // Where a number of wanders would land, picked the way Wander picks, counted by screen. The last count is landings on none.
        private static int[] RoamHits(Roam roam, List<Ledge> spots, int size, int tries, Random dice, List<Point> landed)
        {
            int[] hits = new int[roam.Screens.Length + 1];
            for (int i = 0; i < tries && spots.Count > 0; i++)
            {
                Ledge spot = WanderSpot(spots, size, dice.NextDouble());
                int margin = size / 4, x = spot.Left + margin + dice.Next(Math.Max(1, spot.Right - spot.Left - 2 * margin));
                int on = Array.FindIndex(roam.Screens, screen => screen.Contains(x, spot.Y - 1));
                hits[on < 0 ? hits.Length - 1 : on]++;
                if (landed != null) landed.Add(new Point(x, spot.Y));
            }
            return hits;
        }

        private static string RoamCounts(int[] hits) { return string.Join("/", Array.ConvertAll(hits, hit => hit.ToString(System.Globalization.CultureInfo.InvariantCulture))); }

        // Her wandering across their monitors, first on made-up layouts: two side by side, one left of the main monitor at
        // negative x, mixed scaling, something full screen on one, a monitor picked under WATCH > Screens and one that's
        // unplugged. Then her real hops across this PC's own screens, and a picture of where she'd go.
        private void SmokeRoam(List<string> checks, string output)
        {
            Directory.CreateDirectory(output);
            const int size = 240, room = 200, tries = 400;
            Random dice = new Random(20261122);
            Rectangle main = new Rectangle(0, 0, 1920, 1080), right = new Rectangle(1920, 0, 1920, 1080);

            Roam pair = RoamLayout(new[] { main, right }, new[] { 48, 48 }, 0);
            List<Ledge> pairLedges = RoamLedges(pair, new Rectangle(200, 300, 800, 500), new Rectangle(2200, 200, 900, 600), new Rectangle(1500, 420, 900, 400));
            Ledge pairSeat = pairLedges.Find(ledge => ledge.Floor && ledge.Left == 0);
            List<Ledge> pairSpots = pair.Spots(pairLedges, pairSeat, size, room);
            int[] pairHits = RoamHits(pair, pairSpots, size, tries, dice, null);
            bool pairCut = pairSpots.Exists(spot => spot.Left == 1500 && spot.Right == 1920) && pairSpots.Exists(spot => spot.Left == 1920 && spot.Right == 2400);
            bool pairFloors = pairSpots.Exists(spot => spot.Floor && spot.Left == 1920) && !pairSpots.Exists(spot => spot.Floor && spot.Left == 0);
            bool pairSame = Roam.Same(pairSeat, new Ledge { Floor = true, Left = 0, Right = 1920, Y = 1032 }) && !Roam.Same(pairSeat, pairLedges.Find(ledge => ledge.Floor && ledge.Left == 1920))
                && Roam.Same(pairLedges[0], new Ledge { Window = pairLedges[0].Window, Left = 300, Right = 400, Y = 300 });
            if (!pairCut || !pairFloors || !pairSame || pairHits[0] == 0 || pairHits[1] == 0 || pairHits[2] != 0)
                throw new Exception("Two side-by-side monitors went wrong: cut " + pairCut + ", taskbars " + pairFloors + ", same place " + pairSame + ", landings " + RoamCounts(pairHits));
            checks.Add("PASS two side-by-side monitors: sitting on the main taskbar she can pick the other screen's taskbar and windows as well as her own screen's windows, never the taskbar she's on, a window across both is cut at their edge, and " + tries + " picks land " + pairHits[0] + " on the main screen and " + pairHits[1] + " on the other");

            Roam left = RoamLayout(new[] { new Rectangle(-2560, -360, 2560, 1440), main }, new[] { 48, 48 }, 1);
            List<Ledge> leftLedges = RoamLedges(left, new Rectangle(-2200, -300, 900, 600), new Rectangle(-1800, -100, 1000, 500), new Rectangle(300, 200, 900, 500));
            List<Ledge> leftSpots = left.Spots(leftLedges, leftLedges.Find(ledge => ledge.Floor && ledge.Left == 0), size, room);
            int[] leftHits = RoamHits(left, leftSpots, size, tries, dice, null);
            bool leftOf = left.Of(new Point(-1, 500)) == 0 && left.Of(new Point(-2560, -360)) == 0 && left.Of(new Point(-4000, 300)) == 0 && left.Of(new Point(0, 0)) == 1 && left.Of(new Point(2500, 500)) == 1;
            bool leftRoom = !leftSpots.Exists(spot => spot.Y == -300) && leftSpots.Exists(spot => spot.Y == -100 && spot.Left == -1800) && leftSpots.Exists(spot => spot.Floor && spot.Left == -2560 && spot.Y == 1032);
            if (!leftOf || !leftRoom || leftHits[0] == 0 || leftHits[1] == 0 || leftHits[2] != 0)
                throw new Exception("A monitor left of the main one went wrong: points " + leftOf + ", spots " + leftRoom + ", landings " + RoamCounts(leftHits));
            checks.Add("PASS a monitor left of the main one at negative x, taller and higher: points on it and past its far edge map to it, a window too near its top for her head isn't a spot while its other windows and its taskbar are, and picks land " + leftHits[0] + " on it and " + leftHits[1] + " on the main screen");

            Roam mixed = RoamLayout(new[] { new Rectangle(-3840, 0, 3840, 2160), main, new Rectangle(1920, -180, 2560, 1440) }, new[] { 72, 48, 60 }, 1);
            List<Ledge> mixedLedges = RoamLedges(mixed, new Rectangle(-500, 300, 1000, 400), new Rectangle(-600, 1500, 1200, 300), new Rectangle(2000, -150, 800, 400), new Rectangle(2100, 100, 800, 400));
            List<Ledge> mixedSpots = mixed.Spots(mixedLedges, mixedLedges.Find(ledge => ledge.Floor && ledge.Left == 0), size, room);
            int[] mixedHits = RoamHits(mixed, mixedSpots, size, tries, dice, null);
            bool mixedFloors = mixedSpots.Exists(spot => spot.Floor && spot.Left == -3840 && spot.Y == 2088) && mixedSpots.Exists(spot => spot.Floor && spot.Left == 1920 && spot.Y == 1200) && !mixedSpots.Exists(spot => spot.Floor && spot.Left == 0);
            bool mixedCut = mixedSpots.Exists(spot => spot.Left == -500 && spot.Right == 0) && mixedSpots.Exists(spot => spot.Left == 0 && spot.Right == 500);
            bool mixedBelow = mixedSpots.Exists(spot => spot.Y == 1500 && spot.Left == -600 && spot.Right == 0) && !mixedSpots.Exists(spot => spot.Y == 1500 && spot.Right > 0);
            bool mixedTop = !mixedSpots.Exists(spot => spot.Y == -150) && mixedSpots.Exists(spot => spot.Y == 100);
            if (!mixedFloors || !mixedCut || !mixedBelow || !mixedTop || mixedHits[0] == 0 || mixedHits[1] == 0 || mixedHits[2] == 0 || mixedHits[3] != 0)
                throw new Exception("Mixed scaling went wrong: taskbars " + mixedFloors + ", cut " + mixedCut + ", below the short screen " + mixedBelow + ", head room " + mixedTop + ", landings " + RoamCounts(mixedHits));
            checks.Add("PASS mixed scaling, a 4K screen at 150% left of the main one at 100% and a 1440p one at 125% higher up on the right: each taskbar at its own height is a spot on its own screen, a window across two is cut at their edge, the part of one below the shorter screen only counts on the taller one, room for her head goes by each screen's own top, and picks land " + mixedHits[0] + ", " + mixedHits[1] + " and " + mixedHits[2] + " on the three");

            Rectangle[] gameScreens = { main, right };
            List<Ledges.Pane> panes = new List<Ledges.Pane>
            {
                new Ledges.Pane { Bounds = new Rectangle(3300, 700, 480, 270), Bare = true, Floats = true },
                new Ledges.Pane { Bounds = right, Bare = true },
                new Ledges.Pane { Bounds = new Rectangle(200, 300, 800, 500) }
            };
            bool[] gameFull = Ledges.FullFronts(gameScreens, panes);
            panes.Insert(0, new Ledges.Pane { Bounds = new Rectangle(2100, 200, 900, 600) });
            bool[] gameCovered = Ledges.FullFronts(gameScreens, panes);
            bool[] gameTitled = Ledges.FullFronts(gameScreens, new List<Ledges.Pane> { new Ledges.Pane { Bounds = right } });
            bool fronts = !gameFull[0] && gameFull[1] && !gameCovered[1] && !gameTitled[1];
            Roam game = RoamLayout(gameScreens, new[] { 48, 48 }, 0);
            game.Full = gameFull;
            List<Ledge> gameLedges = RoamLedges(game, new Rectangle(200, 300, 800, 500), new Rectangle(2200, 200, 900, 600), new Rectangle(1500, 420, 900, 400));
            List<Ledge> gameSpots = game.Spots(gameLedges, gameLedges.Find(ledge => ledge.Floor && ledge.Left == 0), size, room);
            int[] gameHits = RoamHits(game, gameSpots, size, tries, dice, null);
            game.Here = 1;
            List<Ledge> fromGame = game.Spots(gameLedges, gameLedges.Find(ledge => ledge.Floor && ledge.Left == 1920), size, room);
            Roam alone = RoamLayout(new[] { right }, new[] { 48 }, 0);
            alone.Full = new[] { true };
            bool nowhere = alone.Spots(RoamLedges(alone, new Rectangle(2200, 200, 900, 600)), null, size, room).Count == 0;
            if (!fronts || gameSpots.Exists(spot => spot.Right > 1920) || gameHits[0] != tries || fromGame.Count == 0 || fromGame.Exists(spot => spot.Right > 1920) || !nowhere)
                throw new Exception("A full-screen monitor went wrong: fronts " + fronts + ", landings " + RoamCounts(gameHits) + ", spots from the game's screen " + fromGame.Count + ", only screen full " + nowhere);
            checks.Add("PASS a monitor with something full screen in front is never picked: the first window on it decides, a picture-in-picture floating over the game doesn't, a window in front of the game or a titled one sized to the monitor isn't full screen, all " + tries + " picks stay on the clear monitor, she leaves the game's monitor when she's on it, and when it's her only monitor there's no spot, so she goes beside it as before");

            Roam watch = RoamLayout(new[] { new Rectangle(-1920, 0, 1920, 1080), main, right }, new[] { 48, 48, 48 }, 1);
            List<Ledge> watchLedges = RoamLedges(watch, new Rectangle(-1500, 300, 900, 500), new Rectangle(300, 300, 900, 500), new Rectangle(2300, 300, 900, 500));
            Ledge watchSeat = watchLedges.Find(ledge => ledge.Floor && ledge.Left == 0);
            watch.Watched = watch.Named("\\\\.\\display3");
            int[] picked = RoamHits(watch, watch.Spots(watchLedges, watchSeat, size, room), size, tries, dice, null);
            watch.Full = new[] { false, false, true };
            int[] pickedFull = RoamHits(watch, watch.Spots(watchLedges, watchSeat, size, room), size, tries, dice, null);
            watch.Full = new bool[3];
            watch.Watched = watch.Named("\\\\.\\DISPLAY9");
            int[] pickedGone = RoamHits(watch, watch.Spots(watchLedges, watchSeat, size, room), size, tries, dice, null);
            watch.Watched = 2;
            watch.Stay = true;
            int[] stayed = RoamHits(watch, watch.Spots(watchLedges, watchSeat, size, room), size, tries, dice, null);
            if (picked[2] != tries || pickedFull[1] != tries || pickedGone[0] == 0 || pickedGone[1] == 0 || pickedGone[2] == 0 || stayed[1] != tries)
                throw new Exception("The Screens pick went wrong: watched " + RoamCounts(picked) + ", watched one full " + RoamCounts(pickedFull) + ", pick missing " + RoamCounts(pickedGone) + ", in a call " + RoamCounts(stayed));
            checks.Add("PASS the monitor picked under WATCH > Screens keeps her wandering there, she keeps to her own screen while that one has something full screen in front, a pick that's missing lets her wander every monitor, and held to her own screen, as in a call, she keeps to it whatever's picked");

            Roam before = RoamLayout(new[] { main, right }, new[] { 48, 48 }, 1);
            List<Ledge> beforeLedges = RoamLedges(before, new Rectangle(2200, 200, 900, 600), new Rectangle(300, 300, 900, 500));
            Ledge goneFloor = beforeLedges.Find(ledge => ledge.Floor && ledge.Left == 1920);
            Roam after = RoamLayout(new[] { main }, new[] { 48 }, -1);
            Point stranded = new Point(2600, 1031);
            after.Here = after.Of(stranded);
            List<Ledge> afterSpots = after.Spots(beforeLedges, goneFloor, size, room);
            Ledge rehomed = after.Floored(goneFloor, stranded.X);
            after.Stay = true;
            List<int> callOpen = after.Open();
            Roam none = new Roam();
            bool unplugged = after.Here == 0 && afterSpots.Count > 0 && !afterSpots.Exists(spot => spot.Right > 1920) && rehomed != goneFloor && rehomed.Floor && rehomed.Left == 0 && rehomed.Y == 1032
                && after.Floored(rehomed, 500) == rehomed && callOpen.Count == 1 && callOpen[0] == 0;
            bool empty = none.Of(stranded) == -1 && none.Open().Count == 0 && none.Spots(beforeLedges, null, size, room).Count == 0;
            if (!unplugged || !empty)
                throw new Exception("An unplugged monitor went wrong: here " + after.Here + ", spots " + afterSpots.Count + ", landing " + rehomed.Left + "," + rehomed.Y + ", no screens " + empty);
            checks.Add("PASS a monitor unplugged while she's on it: her spot there maps to the nearest monitor left, nothing still listed on the gone one is offered, a taskbar she was heading for on it becomes the nearest screen's taskbar, a call keeps her on that one, and with no screens at all there's nowhere to go");

            string roamHelp = SelfKnow.Matching("can you wander across both my monitors?") ?? "";
            if (!roamHelp.StartsWith("Wander:", StringComparison.Ordinal) || roamHelp.IndexOf("full-screen", StringComparison.Ordinal) < 0 || roamHelp.IndexOf("WATCH > Screens", StringComparison.Ordinal) < 0)
                throw new Exception("Her self knowledge didn't explain wandering across monitors: " + roamHelp);
            checks.Add("PASS asked whether she wanders across both monitors, her self knowledge says she does, never onto a full-screen one, and how the Screens pick keeps her to one");

            Roam picture = RoamLayout(new[] { new Rectangle(-2560, -360, 2560, 1440), main }, new[] { 72, 48 }, 1);
            Rectangle[] windows = { new Rectangle(-2300, -200, 1100, 700), new Rectangle(-1000, 520, 760, 560), new Rectangle(300, 250, 900, 520), new Rectangle(-420, 120, 1000, 380) };
            List<Ledge> pictureLedges = RoamLedges(picture, windows);
            Ledge pictureSeat = pictureLedges.Find(ledge => ledge.Floor && ledge.Left == 0);
            string[] labels = { "screen 2: 2560 x 1440 at 150%, left of the main one at x -2560", "main screen: 1920 x 1080 at 100%" };
            string[] titles = { "Both clear: she can pick spots on either monitor", "A full-screen game on the left monitor: she keeps off it", "Watching the left monitor under WATCH > Screens: she keeps to it" };
            string sheetPath = Path.Combine(output, "roam-two-monitors.png");
            using (Bitmap sheet = new Bitmap(1400, 1380, PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(sheet))
            {
                graphics.Clear(Look.Panel);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                for (int panel = 0; panel < 3; panel++)
                {
                    picture.Full = new[] { panel == 1, false };
                    picture.Watched = panel == 2 ? 0 : -1;
                    List<Ledge> spots = picture.Spots(pictureLedges, pictureSeat, size, room);
                    List<Point> landed = new List<Point>();
                    int[] hits = RoamHits(picture, spots, size, 300, new Random(panel + 7), landed);
                    if ((panel == 0 && (hits[0] == 0 || hits[1] == 0)) || (panel == 1 && hits[0] != 0) || (panel == 2 && hits[0] != 300))
                        throw new Exception("The picture's layout " + (panel + 1) + " landed " + RoamCounts(hits));
                    DrawRoam(graphics, new Rectangle(0, panel * 460, 1400, 460), picture, windows, pictureLedges, pictureSeat, spots, landed, labels, titles[panel], hits);
                }
                sheet.Save(sheetPath, ImageFormat.Png);
            }
            checks.Add("PASS a picture of where she'd wander on a made-up two-monitor layout: " + sheetPath);

            Screen[] real = Screen.AllScreens;
            List<Shelf> shelves = new List<Shelf>();
            Func<List<Ledge>> ledgesWas = napLedges;
            Func<bool> fullWas = FullScreen;
            Func<Sight> glimpseWas = Computer.Glimpse;
            string workingWas = working, pickWas = preferences.WatchScreen;
            bool watchWas = preferences.Watch;
            try
            {
                ThrowCheck(checks, delegate(List<string> list, List<short[]> sounds)
                {
                    int last = real.Length - 1;
                    Rectangle[] monitors = Roam.Now().Screens;
                    bool[] plain = Ledges.FullFronts(monitors, Skip());
                    bool[] covered, uncovered;
                    using (Shelf cover = new Shelf())
                    using (Shelf floating = new Shelf())
                    {
                        cover.Bounds = real[last].Bounds;
                        cover.Show();
                        floating.Bounds = new Rectangle(real[last].Bounds.Right - 520, real[last].Bounds.Bottom - 320, 480, 270);
                        floating.Show();
                        covered = Ledges.FullFronts(monitors, Skip());
                        cover.Hide();
                        floating.Hide();
                        uncovered = Ledges.FullFronts(monitors, Skip());
                    }
                    if (plain[last]) list.Add("LIMIT a real full-screen window: something already covers this PC's last screen, so a stand-in couldn't be told apart");
                    else if (!covered[last] || uncovered[last]) throw new Exception("A real borderless window covering a monitor, with another floating over it, didn't count as full screen there and then stop: " + covered[last] + " / " + uncovered[last]);
                    else list.Add("PASS a real borderless window covering one of this PC's monitors counts as full screen there with a small one floating over it, and stops counting once it's gone");

                    foreach (Screen screen in real)
                    {
                        Rectangle work = screen.WorkingArea;
                        Shelf shelf = new Shelf();
                        shelf.Bounds = new Rectangle(work.Left + work.Width / 2 - 220, work.Top + work.Height / 2, 440, 180);
                        shelf.Show();
                        shelves.Add(shelf);
                    }
                    napLedges = delegate
                    {
                        List<Ledge> found = new List<Ledge>();
                        foreach (Shelf shelf in shelves) found.Add(new Ledge { Window = shelf.Handle, Bounds = shelf.Bounds, Left = shelf.Left, Right = shelf.Right, Y = shelf.Top });
                        foreach (Screen screen in Screen.AllScreens) found.Add(new Ledge { Floor = true, Left = screen.WorkingArea.Left, Right = screen.WorkingArea.Right, Y = screen.WorkingArea.Bottom });
                        return found;
                    };
                    preferences.Watch = false;
                    working = null;
                    Rectangle first = real[0].WorkingArea;
                    anchor = Seated(first.Left + first.Width / 2, first.Bottom);
                    Land(new Ledge { Floor = true, Left = first.Left, Right = first.Right, Y = first.Bottom });
                    Func<int, int[]> hop = delegate(int count)
                    {
                        int[] seen = new int[real.Length + 1];
                        for (int i = 0; i < count; i++)
                        {
                            Ledge was = seat;
                            if (!Wander() || seat == null || falling || Roam.Same(seat, was)) throw new Exception("A wander across this PC's screens didn't land her somewhere new.");
                            Point hip = Hip();
                            int on = Array.FindIndex(real, screen => screen.Bounds.Contains(hip.X, hip.Y - 1));
                            seen[on < 0 ? real.Length : on]++;
                        }
                        return seen;
                    };
                    int[] everywhere = hop(30);
                    bool reached = everywhere[real.Length] == 0;
                    for (int s = 0; s < real.Length; s++) reached = reached && everywhere[s] > 0;
                    if (!reached) throw new Exception("Thirty wanders across this PC's " + real.Length + " screens landed " + RoamCounts(everywhere) + ".");
                    Point now = Hip();
                    int mine = Array.FindIndex(real, screen => screen.Bounds.Contains(now.X, now.Y - 1));
                    working = "a roaming check";
                    int[] busy = hop(12);
                    working = null;
                    if (mine < 0 || busy[mine] != 12) throw new Exception("On a job she wandered off her screen: " + RoamCounts(busy) + ".");
                    if (real.Length > 1)
                    {
                        int other = (mine + 1) % real.Length;
                        preferences.Watch = true;
                        preferences.WatchScreen = real[other].DeviceName;
                        int[] watched = hop(12);
                        preferences.Watch = false;
                        if (watched[other] != 12) throw new Exception("Watching screen " + (other + 1) + " she still wandered elsewhere: " + RoamCounts(watched) + ".");
                        FullScreen = delegate { return true; };
                        Computer.Glimpse = delegate { return new Sight { Window = shelves[other].Handle, Title = "Full screen", Bounds = real[other].Bounds }; };
                        int[] avoided = hop(12);
                        if (avoided[other] != 0) throw new Exception("With something full screen on screen " + (other + 1) + " she still wandered onto it: " + RoamCounts(avoided) + ".");
                        list.Add("PASS her own wanders on this PC's " + real.Length + " screens reach every one (" + RoamCounts(everywhere) + "), keep to her own screen on a job, go to and stay on the one picked under WATCH > Screens, and leave and keep off one with something full screen in front");
                    }
                    else
                    {
                        FullScreen = delegate { return true; };
                        Computer.Glimpse = delegate { return new Sight { Window = shelves[0].Handle, Title = "Full screen", Bounds = real[0].Bounds }; };
                        if (!Wander() || seat == null || !seat.Aside || !seat.Free) throw new Exception("With something full screen on her only screen she didn't go beside it.");
                        list.Add("PASS her own wanders on this PC's one screen still hop between its window and taskbar, keep to it on a job, and with something full screen in front she goes beside it as before");
                        list.Add("LIMIT roaming across real monitors: this PC has one screen, so crossing to another was checked on made-up layouts only");
                    }
                });
            }
            finally
            {
                napLedges = ledgesWas;
                FullScreen = fullWas;
                Computer.Glimpse = glimpseWas;
                working = workingWas;
                preferences.Watch = watchWas;
                preferences.WatchScreen = pickWas;
                foreach (Shelf shelf in shelves) shelf.Dispose();
            }
        }

        // One made-up layout in a picture: each monitor and its taskbar, its windows, the spots she may pick in cyan, ledges she
        // won't use dimmed, where she sits in pink, a monitor with something full screen in front in red, and a dot where each
        // pick landed.
        private static void DrawRoam(Graphics graphics, Rectangle panel, Roam roam, Rectangle[] windows, List<Ledge> around, Ledge seat, List<Ledge> spots, List<Point> landed, string[] labels, string title, int[] hits)
        {
            Rectangle all = roam.Screens[0];
            foreach (Rectangle screen in roam.Screens) all = Rectangle.Union(all, screen);
            float scale = Math.Min((panel.Width - 80f) / all.Width, (panel.Height - 120f) / all.Height);
            float left = panel.Left + (panel.Width - all.Width * scale) / 2, top = panel.Top + 92;
            Func<int, float> x = delegate(int at) { return left + (at - all.Left) * scale; };
            Func<int, float> y = delegate(int at) { return top + (at - all.Top) * scale; };
            Func<Rectangle, RectangleF> place = delegate(Rectangle box) { return RectangleF.FromLTRB(x(box.Left), y(box.Top), x(box.Right), y(box.Bottom)); };
            using (Font heading = Look.Mono(20, FontStyle.Bold))
            using (Font small = Look.Mono(13, FontStyle.Regular))
            using (SolidBrush neon = new SolidBrush(Look.Neon))
            using (SolidBrush dim = new SolidBrush(Look.Dim))
            using (SolidBrush ink = new SolidBrush(Look.Ink))
            using (SolidBrush red = new SolidBrush(Look.Red))
            using (SolidBrush pink = new SolidBrush(Look.Pink))
            using (SolidBrush screenFill = new SolidBrush(Color.FromArgb(24, 28, 36)))
            using (SolidBrush barFill = new SolidBrush(Color.FromArgb(14, 16, 22)))
            using (SolidBrush windowFill = new SolidBrush(Color.FromArgb(52, 58, 70)))
            using (SolidBrush fullFill = new SolidBrush(Color.FromArgb(150, 120, 14, 30)))
            using (SolidBrush dot = new SolidBrush(Color.FromArgb(170, Look.Ink)))
            using (Pen edge = new Pen(Look.Dim, 2))
            using (Pen windowEdge = new Pen(Color.FromArgb(96, 104, 118), 1))
            using (Pen fullEdge = new Pen(Look.Red, 3))
            using (Pen unused = new Pen(Color.FromArgb(150, Look.Dim), 2) { DashStyle = DashStyle.Dash })
            using (Pen spotPen = new Pen(Look.Cyan, 4))
            using (Pen seatPen = new Pen(Look.Pink, 6))
            {
                graphics.DrawString(title, heading, neon, panel.Left + 28, panel.Top + 18);
                string tally = "300 picks: " + hits[0] + " on screen 2, " + hits[1] + " on the main screen.  Cyan: spots she may pick.  Dashes: ledges she won't use.  Pink: where she sits.";
                graphics.DrawString(tally, small, dim, panel.Left + 30, panel.Top + 52);
                for (int i = 0; i < roam.Screens.Length; i++)
                {
                    RectangleF screen = place(roam.Screens[i]), work = place(roam.Works[i]);
                    graphics.FillRectangle(screenFill, screen);
                    graphics.FillRectangle(barFill, RectangleF.FromLTRB(screen.Left, work.Bottom, screen.Right, screen.Bottom));
                    graphics.DrawRectangle(edge, screen.X, screen.Y, screen.Width, screen.Height);
                }
                foreach (Rectangle window in windows)
                {
                    RectangleF box = place(window);
                    graphics.FillRectangle(windowFill, box);
                    graphics.DrawRectangle(windowEdge, box.X, box.Y, box.Width, box.Height);
                }
                for (int i = 0; i < roam.Screens.Length; i++)
                {
                    RectangleF screen = place(roam.Screens[i]);
                    if (roam.Full[i])
                    {
                        graphics.FillRectangle(fullFill, screen);
                        graphics.DrawRectangle(fullEdge, screen.X, screen.Y, screen.Width, screen.Height);
                        SizeF said = graphics.MeasureString("FULL-SCREEN GAME", heading);
                        graphics.DrawString("FULL-SCREEN GAME", heading, red, screen.Left + (screen.Width - said.Width) / 2, screen.Top + (screen.Height - said.Height) / 2);
                    }
                    graphics.DrawString(labels[i], small, i == roam.Watched ? neon : ink, screen.Left + 8, screen.Top + 6);
                    if (i == roam.Watched) graphics.DrawString("watched", small, neon, screen.Left + 8, screen.Top + 26);
                }
                foreach (Ledge ledge in around) graphics.DrawLine(unused, x(ledge.Left), y(ledge.Y), x(ledge.Right), y(ledge.Y));
                foreach (Ledge spot in spots) graphics.DrawLine(spotPen, x(spot.Left), y(spot.Y), x(spot.Right), y(spot.Y));
                graphics.DrawLine(seatPen, x(seat.Left + 40), y(seat.Y), x(seat.Right - 40), y(seat.Y));
                float hipX = x(seat.Left + (seat.Right - seat.Left) * 3 / 4), hipY = y(seat.Y);
                graphics.FillEllipse(pink, hipX - 7, hipY - 18, 14, 14);
                graphics.DrawString("she sits here", small, pink, hipX - 60, hipY - 40);
                foreach (Point at in landed) graphics.FillEllipse(dot, x(at.X) - 2.5f, y(at.Y) - 8, 5, 5);
            }
        }
    }
}

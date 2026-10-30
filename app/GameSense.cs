using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CapriDesktop
{
    internal sealed class GameSense
    {
        private static readonly string[] kinds = { "flash", "hurt", "bar_drop", "cut", "dark" };
        private readonly double[] last = new double[5];
        private readonly double[] notices = new double[5];
        private readonly double[] red = new double[5];
        private readonly double[] baseline = new double[5];
        private readonly int[] redPixels = new int[5];
        private readonly int[] samples = new int[5];
        private readonly int[] changed = new int[12];
        private readonly int[] tiles = new int[12];
        private readonly List<Bar> bars = new List<Bar>();
        private readonly List<Bar> found = new List<Bar>();
        private byte[] previous, beforeFlash;
        private int frameWidth, frameHeight, motionX, motionY;
        private double started, clock, mean, oldMean, black, bright, difference, holdUntil;
        private double flashAt, flashMean, flashStrength, cutAt, darkAt;
        private bool flashing, flashTold, cutting, darkening, darkTold, hurt, visible, continuous;

        private sealed class Bar
        {
            internal int Start, Row, Length, Thick, Blue, Green, Red;
            internal bool Vertical, Ready, Seen, Moved;
            internal double Since, SeenAt, Pending = 1, ChangedAt;
            internal BarSample Before;
            internal Queue<BarSample> History;
        }

        private struct BarSample
        {
            internal double At, Fill;
        }

        internal GameSense() { Reset(); }

        internal event Action<string, double> Noticed;

        internal void Reset()
        {
            previous = null;
            beforeFlash = null;
            frameWidth = frameHeight = motionX = motionY = 0;
            started = clock = mean = oldMean = black = bright = difference = 0;
            flashAt = flashMean = flashStrength = cutAt = darkAt = 0;
            holdUntil = double.NegativeInfinity;
            flashing = flashTold = cutting = darkening = darkTold = hurt = visible = false;
            continuous = true;
            bars.Clear();
            found.Clear();
            Array.Clear(baseline, 0, baseline.Length);
            for (int resetKind = 0; resetKind < last.Length; resetKind++) { last[resetKind] = double.NegativeInfinity; notices[resetKind] = -1; }
        }

        internal void Feed(byte[] bgra, int width, int height, double seconds)
        {
            if (bgra == null || width <= 0 || height <= 0 || bgra.Length % 4 != 0 || (long)width * height != bgra.Length / 4
                || double.IsNaN(seconds) || double.IsInfinity(seconds)) { Reset(); return; }
            if (previous != null && (width != frameWidth || height != frameHeight || seconds < clock || seconds - clock > 1)) Reset();
            if (previous != null && seconds == clock) return;
            bool first = previous == null;
            if (first)
            {
                frameWidth = width;
                frameHeight = height;
                previous = new byte[bgra.Length];
                started = seconds;
            }
            for (int clearKind = 0; clearKind < notices.Length; clearKind++) notices[clearKind] = -1;
            Measure(bgra, width, height);
            double elapsed = first ? 0 : seconds - clock;
            bool nearlyBlack = black > 0.94 && mean < 12;
            bool wide = !first && difference > 24;
            for (int tileIndex = 0; tileIndex < tiles.Length; tileIndex++)
                if (tiles[tileIndex] == 0 || changed[tileIndex] < tiles[tileIndex] * 0.65) wide = false;
            bool matched = !wide || Similar(previous, bgra, width, height);
            bool redEdges = false;
            if (first) Array.Copy(red, baseline, red.Length);
            else
            {
                int raisedEdges = 0;
                int settledEdges = 0;
                for (int edgeIndex = 0; edgeIndex < 4; edgeIndex++)
                {
                    if (red[edgeIndex] > baseline[edgeIndex] + 28 && red[edgeIndex] > red[4] + 22 && redPixels[edgeIndex] > samples[edgeIndex] * 0.5) raisedEdges++;
                    if (red[edgeIndex] < baseline[edgeIndex] + 12) settledEdges++;
                }
                redEdges = raisedEdges >= 3;
                if (settledEdges >= 3) hurt = false;
            }
            if (!first && seconds - started >= 1 && bright > 0.78 && mean - oldMean > 55 && !flashing && !nearlyBlack)
            {
                if (beforeFlash == null) beforeFlash = new byte[bgra.Length];
                Buffer.BlockCopy(previous, 0, beforeFlash, 0, bgra.Length);
                flashing = true;
                flashTold = false;
                flashAt = seconds;
                flashMean = oldMean;
                flashStrength = Math.Min(1, (mean - oldMean) / 160);
            }
            bool returned = false;
            if (flashing)
            {
                if (seconds > flashAt && Math.Abs(mean - flashMean) < 22 && Similar(beforeFlash, bgra, width, height))
                {
                    returned = true;
                    flashing = cutting = false;
                    if (!flashTold && seconds >= holdUntil) Notice(0, flashStrength, seconds);
                }
                else if (seconds - flashAt > 1.2)
                {
                    if (White(bgra, width, height))
                    {
                        cutting = false;
                        if (!flashTold)
                        {
                            flashTold = true;
                            bars.Clear();
                            if (seconds >= holdUntil) Notice(0, flashStrength, seconds);
                        }
                    }
                    else flashing = false;
                }
            }
            if (cutting && !returned)
            {
                if ((!matched && seconds > cutAt) || nearlyBlack) cutting = false;
                else if (!flashing && seconds - cutAt >= 0.6)
                {
                    cutting = false;
                    holdUntil = seconds + 1;
                    bars.Clear();
                    hurt = false;
                    Array.Copy(red, baseline, red.Length);
                    Notice(3, 1, seconds);
                }
            }
            else if (!first && !returned && seconds - started >= 1 && wide && !matched && continuous && !nearlyBlack && !redEdges && seconds >= holdUntil)
            {
                cutting = true;
                cutAt = seconds;
            }
            if (nearlyBlack && visible)
            {
                if (!darkening) { darkening = true; darkAt = seconds; }
                if (!darkTold && seconds - darkAt > 1 && seconds >= holdUntil && !cutting)
                {
                    Notice(4, 1 - mean / 255, seconds);
                    darkTold = true;
                }
            }
            else
            {
                darkening = darkTold = false;
                if (mean > 24) visible = true;
            }
            bool held = first || cutting || flashing || returned || seconds < holdUntil || nearlyBlack;
            if (!held && seconds - started >= 2 && redEdges && !hurt)
            {
                hurt = true;
                double redness = 0;
                for (int strengthEdge = 0; strengthEdge < 4; strengthEdge++) redness += Math.Max(0, red[strengthEdge] - baseline[strengthEdge]);
                Notice(1, redness / 600, seconds);
            }
            if (!hurt && !redEdges)
            {
                double weight = Math.Min(1, elapsed / 3);
                for (int baselineEdge = 0; baselineEdge < baseline.Length; baselineEdge++) baseline[baselineEdge] += (red[baselineEdge] - baseline[baselineEdge]) * weight;
            }
            if (!held && !hurt && !redEdges) WatchBars(bgra, width, height, seconds);
            Buffer.BlockCopy(bgra, 0, previous, 0, bgra.Length);
            oldMean = mean;
            clock = seconds;
            continuous = matched || returned || first;
            for (int tellKind = 0; tellKind < notices.Length; tellKind++)
            {
                if (notices[tellKind] < 0) continue;
                Action<string, double> noticed = Noticed;
                if (noticed == null) continue;
                double strength = notices[tellKind];
                foreach (Action<string, double> subscriber in noticed.GetInvocationList())
                {
                    try { subscriber(kinds[tellKind], strength); }
                    catch (Exception) { }
                }
            }
        }

        private void Notice(int kind, double strength, double seconds)
        {
            if (seconds - last[kind] < 3) return;
            last[kind] = seconds;
            notices[kind] = Math.Max(0, Math.Min(1, strength));
        }

        private void Measure(byte[] pixels, int width, int height)
        {
            Array.Clear(red, 0, red.Length);
            Array.Clear(redPixels, 0, redPixels.Length);
            Array.Clear(samples, 0, samples.Length);
            Array.Clear(changed, 0, changed.Length);
            Array.Clear(tiles, 0, tiles.Length);
            double light = 0, darkPixels = 0, brighter = 0, totalDifference = 0;
            int counted = 0;
            int step = Math.Max(1, Math.Min(width / 120, height / 67));
            for (int pixelY = 0; pixelY < height; pixelY += step)
            {
                int tileRow = pixelY * 3 / height * 4;
                for (int pixelX = 0; pixelX < width; pixelX += step)
                {
                    int pixelAt = (pixelY * width + pixelX) * 4;
                    int blue = pixels[pixelAt], green = pixels[pixelAt + 1], redValue = pixels[pixelAt + 2];
                    int gray = (blue * 29 + green * 150 + redValue * 77) >> 8;
                    int priorGray = (previous[pixelAt] * 29 + previous[pixelAt + 1] * 150 + previous[pixelAt + 2] * 77) >> 8;
                    int delta = Math.Abs(blue - previous[pixelAt]) + Math.Abs(green - previous[pixelAt + 1]) + Math.Abs(redValue - previous[pixelAt + 2]);
                    int tile = tileRow + pixelX * 4 / width;
                    tiles[tile]++;
                    if (delta > 60) changed[tile]++;
                    totalDifference += delta;
                    light += gray;
                    if (gray < 18) darkPixels++;
                    if (gray - priorGray > 45) brighter++;
                    counted++;
                    int excess = Math.Max(0, redValue - Math.Max(green, blue));
                    if (pixelX < width / 10) Edge(0, excess);
                    if (pixelX >= width - Math.Max(1, width / 10)) Edge(1, excess);
                    if (pixelY < height / 10) Edge(2, excess);
                    if (pixelY >= height - Math.Max(1, height / 10)) Edge(3, excess);
                    if (pixelX >= width / 4 && pixelX < width * 3 / 4 && pixelY >= height / 4 && pixelY < height * 3 / 4) Edge(4, excess);
                }
            }
            mean = light / counted;
            black = darkPixels / counted;
            bright = brighter / counted;
            difference = totalDifference / (3 * counted);
            for (int meanEdge = 0; meanEdge < red.Length; meanEdge++) if (samples[meanEdge] != 0) red[meanEdge] /= samples[meanEdge];
        }

        private static bool White(byte[] pixels, int width, int height)
        {
            int whiteCount = 0;
            for (int whiteRow = 0; whiteRow < 5; whiteRow++)
                for (int whiteColumn = 0; whiteColumn < 8; whiteColumn++)
                {
                    int whiteX = (2 * whiteColumn + 1) * width / 16;
                    int whiteY = (2 * whiteRow + 1) * height / 10;
                    int whiteAt = (whiteY * width + whiteX) * 4;
                    if (pixels[whiteAt] >= 200 && pixels[whiteAt + 1] >= 200 && pixels[whiteAt + 2] >= 200) whiteCount++;
                }
            return whiteCount >= 32;
        }

        private void Edge(int edge, int excess)
        {
            red[edge] += excess;
            samples[edge]++;
            if (excess > 45) redPixels[edge]++;
        }

        private bool Similar(byte[] older, byte[] newer, int width, int height)
        {
            if (Aligned(older, newer, width, height, 0, 0)) return true;
            int reachX = width / 3, reachY = height / 3;
            if ((motionX != 0 || motionY != 0) && Aligned(older, newer, width, height, motionX, motionY)) return true;
            for (int offsetY = -reachY; offsetY <= reachY; offsetY++)
                for (int offsetX = -reachX; offsetX <= reachX; offsetX++)
                {
                    bool possible = true;
                    for (int anchor = 0; anchor < 4; anchor++)
                    {
                        int anchorX = width / 3 + (anchor % 2) * (width / 4);
                        int anchorY = height / 3 + (anchor / 2) * (height / 4);
                        int from = (anchorY * width + anchorX) * 4;
                        int to = ((anchorY + offsetY) * width + anchorX + offsetX) * 4;
                        int error = Math.Abs(older[from] - newer[to]) + Math.Abs(older[from + 1] - newer[to + 1]) + Math.Abs(older[from + 2] - newer[to + 2]);
                        if (error > 65) { possible = false; break; }
                    }
                    if (possible && Aligned(older, newer, width, height, offsetX, offsetY))
                    {
                        motionX = offsetX;
                        motionY = offsetY;
                        return true;
                    }
                }
            return false;
        }

        private static bool Aligned(byte[] older, byte[] newer, int width, int height, int dx, int dy)
        {
            int left = Math.Max(width / 8, -dx), right = Math.Min(width - width / 8, width - dx);
            int top = Math.Max(height / 8, -dy), bottom = Math.Min(height - height / 8, height - dy);
            int alike = 0, total = 0;
            for (int matchRow = 0; matchRow < 5; matchRow++)
                for (int matchColumn = 0; matchColumn < 8; matchColumn++)
                {
                    int matchX = left + (2 * matchColumn + 1) * (right - left) / 16;
                    int matchY = top + (2 * matchRow + 1) * (bottom - top) / 10;
                    int firstAt = (matchY * width + matchX) * 4;
                    int secondAt = ((matchY + dy) * width + matchX + dx) * 4;
                    int error = Math.Abs(older[firstAt] - newer[secondAt]) + Math.Abs(older[firstAt + 1] - newer[secondAt + 1]) + Math.Abs(older[firstAt + 2] - newer[secondAt + 2]);
                    if (error < 65) alike++;
                    total += error;
                }
            return alike >= 32 && total < 40 * 50;
        }

        private void WatchBars(byte[] pixels, int width, int height, double seconds)
        {
            found.Clear();
            FindBars(pixels, width, height, false);
            FindBars(pixels, width, height, true);
            for (int seenBar = 0; seenBar < bars.Count; seenBar++) bars[seenBar].Seen = false;
            for (int foundIndex = 0; foundIndex < found.Count; foundIndex++)
            {
                Bar region = found[foundIndex];
                int depth = region.Vertical ? width : height;
                if (region.Thick > Math.Max(2, depth / 12) || region.Length < region.Thick * 5 || (region.Vertical && region.Thick < 2)) continue;
                Bar tracked = null;
                for (int trackIndex = 0; trackIndex < bars.Count; trackIndex++)
                {
                    Bar candidate = bars[trackIndex];
                    if (candidate.Vertical != region.Vertical || Math.Abs(candidate.Row - region.Row) > 1 || Math.Abs(candidate.Thick - region.Thick) > 1) continue;
                    bool sameEnds = Math.Abs(candidate.Start - region.Start) <= 1 && Math.Abs(candidate.Length - region.Length) <= 2;
                    bool sameAnchor = Math.Abs(candidate.Start - region.Start) <= 1 || Math.Abs(candidate.Start + candidate.Length - region.Start - region.Length) <= 1;
                    if (candidate.Ready ? sameAnchor : sameEnds && ColorNear(candidate, region.Blue, region.Green, region.Red)) { tracked = candidate; break; }
                }
                if (tracked == null)
                {
                    region.Since = region.SeenAt = region.ChangedAt = seconds;
                    region.Seen = true;
                    bars.Add(region);
                }
                else
                {
                    tracked.Seen = true;
                    tracked.SeenAt = seconds;
                    if (!tracked.Ready)
                    {
                        int barCenterX = tracked.Vertical ? tracked.Row + tracked.Thick / 2 : tracked.Start + tracked.Length / 2;
                        int barCenterY = tracked.Vertical ? tracked.Start + tracked.Length / 2 : tracked.Row + tracked.Thick / 2;
                        int barTile = barCenterY * 3 / height * 4 + barCenterX * 4 / width;
                        if (changed[barTile] > tiles[barTile] * 0.35) tracked.Moved = true;
                        if (tracked.Moved && seconds - tracked.Since >= 2.2) tracked.Ready = true;
                    }
                    if (tracked.Ready)
                    {
                        tracked.Blue = region.Blue; tracked.Green = region.Green; tracked.Red = region.Red;
                        if (region.Length > tracked.Length)
                        {
                            tracked.Start = region.Start;
                            tracked.Length = region.Length;
                            tracked.Pending = 1;
                            tracked.History = null;
                            tracked.ChangedAt = seconds;
                        }
                    }
                }
            }
            for (int watchIndex = bars.Count - 1; watchIndex >= 0; watchIndex--)
            {
                Bar watched = bars[watchIndex];
                if (!watched.Ready)
                {
                    if (!watched.Seen) bars.RemoveAt(watchIndex);
                    continue;
                }
                int filled = 0;
                for (int barAlong = watched.Start; barAlong < watched.Start + watched.Length; barAlong++)
                {
                    int colored = 0;
                    for (int barAcross = watched.Row; barAcross < watched.Row + watched.Thick; barAcross++)
                    {
                        int barAt = (watched.Vertical ? barAlong * width + barAcross : barAcross * width + barAlong) * 4;
                        if (ColorNear(watched, pixels[barAt], pixels[barAt + 1], pixels[barAt + 2])) colored++;
                    }
                    if (colored * 2 >= watched.Thick) filled++;
                }
                double fill = (double)filled / watched.Length;
                if (watched.History == null)
                {
                    watched.History = new Queue<BarSample>();
                    watched.Before = new BarSample { At = seconds, Fill = fill };
                }
                double earlier = seconds - 0.6;
                while (watched.History.Count != 0 && watched.History.Peek().At <= earlier) watched.Before = watched.History.Dequeue();
                watched.History.Enqueue(new BarSample { At = seconds, Fill = fill });
                BarSample next = watched.History.Peek();
                double priorFill = watched.Before.Fill;
                if (earlier > watched.Before.At && next.At > watched.Before.At)
                    priorFill += (next.Fill - priorFill) * (earlier - watched.Before.At) / (next.At - watched.Before.At);
                if (Math.Abs(fill - watched.Pending) > 0.005)
                {
                    watched.Pending = fill;
                    watched.ChangedAt = seconds;
                }
                else if (seconds - watched.ChangedAt >= 0.2)
                {
                    double lost = priorFill - fill;
                    if (lost >= 0.1 - 0.000001)
                    {
                        // Most of a bar going all at once is a panel closing or a progress bar starting over, and it isn't a bar any more.
                        if (fill <= 0.1 && priorFill >= 0.7) { bars.RemoveAt(watchIndex); continue; }
                        if (Drained(pixels, width, watched)) Notice(2, lost, seconds);
                    }
                }
                if (!watched.Seen && seconds - watched.SeenAt > 2) bars.RemoveAt(watchIndex);
            }
        }

        // A health bar empties from one end into a plain backing. A gap in the middle is something crossing a strip,
        // and a busy picture where the fill was is a panel gone and the world showing through.
        private static bool Drained(byte[] pixels, int width, Bar bar)
        {
            int middle = bar.Row + bar.Thick / 2, first = -1, last = -1, kept = 0, gone = 0, rough = 0, worst = 0, lastGone = -1;
            for (int along = bar.Start; along < bar.Start + bar.Length; along++)
            {
                int at = (bar.Vertical ? along * width + middle : middle * width + along) * 4;
                if (ColorNear(bar, pixels[at], pixels[at + 1], pixels[at + 2]))
                {
                    if (first < 0) first = along;
                    last = along;
                    kept++;
                    continue;
                }
                if (lastGone >= 0)
                {
                    int step = Math.Abs(pixels[at] - pixels[lastGone]) + Math.Abs(pixels[at + 1] - pixels[lastGone + 1]) + Math.Abs(pixels[at + 2] - pixels[lastGone + 2]);
                    rough += step;
                    worst = Math.Max(worst, step);
                }
                lastGone = at;
                gone++;
            }
            int slack = Math.Max(2, bar.Length / 20);
            bool anchored = kept == 0 || first <= bar.Start + slack || last >= bar.Start + bar.Length - 1 - slack;
            bool solid = kept == 0 || last - first + 1 <= kept + slack;
            // One hard edge is fine: that's a ghost of the part just lost, sitting on the backing.
            return anchored && solid && gone > 0 && rough - worst <= gone * 24;
        }

        private void FindBars(byte[] pixels, int width, int height, bool vertical)
        {
            int length = vertical ? height : width, depth = vertical ? width : height;
            int minimum = Math.Max(8, length / 10);
            int stride = vertical ? width * 4 : 4;
            for (int scanRow = 0; scanRow < depth; scanRow++)
            {
                int origin = vertical ? scanRow * 4 : scanRow * width * 4;
                int along = minimum - 1;
                while (along < length)
                {
                    int runAt = origin + along * stride;
                    int runBlue = pixels[runAt], runGreen = pixels[runAt + 1], runRed = pixels[runAt + 2];
                    int high = Math.Max(runBlue, Math.Max(runGreen, runRed)), low = Math.Min(runBlue, Math.Min(runGreen, runRed));
                    if (high < 85 || high - low < 60) { along += minimum; continue; }
                    int runStart = along, runEnd = along + 1;
                    while (runStart > 0)
                    {
                        int priorAt = origin + (runStart - 1) * stride;
                        if (Math.Abs(pixels[priorAt] - runBlue) > 24 || Math.Abs(pixels[priorAt + 1] - runGreen) > 24 || Math.Abs(pixels[priorAt + 2] - runRed) > 24) break;
                        runStart--;
                    }
                    while (runEnd < length)
                    {
                        int nextAt = origin + runEnd * stride;
                        if (Math.Abs(pixels[nextAt] - runBlue) > 24 || Math.Abs(pixels[nextAt + 1] - runGreen) > 24 || Math.Abs(pixels[nextAt + 2] - runRed) > 24) break;
                        runEnd++;
                    }
                    along = runEnd + minimum - 1;
                    int runLength = runEnd - runStart;
                    if (runLength < minimum || (scanRow > depth / 4 && scanRow < depth * 3 / 4 && runStart > length / 5 && runEnd < length * 4L / 5)) continue;
                    Bar joined = null;
                    for (int joinIndex = found.Count - 1; joinIndex >= 0; joinIndex--)
                    {
                        Bar prior = found[joinIndex];
                        if (prior.Vertical != vertical) break;
                        if (prior.Row + prior.Thick < scanRow) continue;
                        if (prior.Row + prior.Thick == scanRow && Math.Abs(prior.Start - runStart) <= 1 && Math.Abs(prior.Length - runLength) <= 2 && ColorNear(prior, runBlue, runGreen, runRed)) { joined = prior; break; }
                    }
                    if (joined != null) joined.Thick++;
                    else found.Add(new Bar { Start = runStart, Row = scanRow, Length = runLength, Thick = 1, Blue = runBlue, Green = runGreen, Red = runRed, Vertical = vertical });
                }
            }
        }

        private static bool ColorNear(Bar bar, int blue, int green, int redValue)
        {
            return Math.Abs(bar.Blue - blue) <= 30 && Math.Abs(bar.Green - green) <= 30 && Math.Abs(bar.Red - redValue) <= 30;
        }

        internal static byte[] Bgra(Bitmap frame, int width, int height)
        {
            if (frame == null || width <= 0 || height <= 0 || (long)width * height > int.MaxValue / 4) return null;
            try
            {
                using (Bitmap small = new Bitmap(width, height, PixelFormat.Format32bppArgb))
                {
                    using (Graphics graphics = Graphics.FromImage(small))
                    {
                        graphics.CompositingMode = CompositingMode.SourceCopy;
                        graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        graphics.DrawImage(frame, new Rectangle(0, 0, width, height), new Rectangle(0, 0, frame.Width, frame.Height), GraphicsUnit.Pixel);
                    }
                    byte[] result = new byte[width * height * 4];
                    BitmapData locked = small.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    try
                    {
                        for (int copyRow = 0; copyRow < height; copyRow++) Marshal.Copy(IntPtr.Add(locked.Scan0, copyRow * locked.Stride), result, copyRow * width * 4, width * 4);
                    }
                    finally { small.UnlockBits(locked); }
                    return result;
                }
            }
            catch (ArgumentException) { return null; }
            catch (ExternalException) { return null; }
            catch (ObjectDisposedException) { return null; }
        }
    }
}

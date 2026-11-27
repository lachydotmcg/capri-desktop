using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal static class GifShelf
    {
        internal const int Most = 60, Longest = 40, MostFrames = 300, MostSide = 4096, Listed = 30;
        internal const long MostBytes = 8L * 1024 * 1024;
        internal const double Rest = 45;
        internal const string Where = "Mainframe > Settings > HABITS > Her GIFs";
        internal const string Empty = "You haven't saved any GIFs for me yet. Add some in " + Where + ".";
        internal const string Again = "I just sent a GIF, so I'll hold off on another one for a bit.";
        internal const string OnPhone = "My GIFs play in a card on your PC screen, so I can't send one to your phone.";
        internal const string Away = "I'm hidden right now, so there's nowhere for a GIF to show.";
        internal const string Docked = "I'm in Gaming Mode's HUD right now, so I won't cover your game with a GIF.";
        internal static string Folder;
        internal static Func<double> Now = delegate { return Clock.Elapsed.TotalSeconds; };
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static readonly object gate = new object();
        private static readonly Random random = new Random();
        private static readonly Regex Reserved = new Regex(@"\A(?:con|prn|aux|nul|com[0-9]|lpt[0-9])\z", RegexOptions.IgnoreCase);
        private static readonly Regex AnyOne = new Regex(@"\A(?:random|any|anything|any one|whatever|surprise me|you pick|your pick)\z", RegexOptions.IgnoreCase);
        private static double sent = double.NegativeInfinity;

        internal static List<string> Names()
        {
            List<string> names = new List<string>();
            string folder = Folder;
            if (string.IsNullOrEmpty(folder)) return names;
            try
            {
                if (!Directory.Exists(folder)) return names;
                foreach (string file in Directory.GetFiles(folder, "*.gif"))
                    if (string.Equals(Path.GetExtension(file), ".gif", StringComparison.OrdinalIgnoreCase)) names.Add(Path.GetFileNameWithoutExtension(file));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        internal static string PathOf(string name)
        {
            string folder = Folder;
            foreach (string kept in Names())
                if (string.Equals(kept, name, StringComparison.OrdinalIgnoreCase)) return Path.Combine(folder, kept + ".gif");
            return null;
        }

        internal static string Clean(string name)
        {
            StringBuilder kept = new StringBuilder();
            foreach (char c in (name ?? "").Normalize(NormalizationForm.FormC))
                kept.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '\'' ? c : ' ');
            string clean = Regex.Replace(kept.ToString(), @"\s+", " ").Trim(' ', '-', '_', '\'');
            if (clean.Length > Longest) clean = clean.Substring(0, Longest).TrimEnd(' ', '-', '_', '\'');
            if (clean.Length == 0) clean = "gif";
            if (Reserved.IsMatch(clean)) clean += " gif";
            return clean;
        }

        private static string Unique(string name, string except)
        {
            List<string> taken = Names();
            string tried = name;
            for (int n = 2; Taken(taken, tried, except); n++)
            {
                string tail = " " + n;
                tried = name.Substring(0, Math.Min(name.Length, Longest - tail.Length)).TrimEnd(' ', '-', '_', '\'') + tail;
            }
            return tried;
        }

        private static bool Taken(List<string> names, string name, string except)
        {
            foreach (string kept in names)
                if (string.Equals(kept, name, StringComparison.OrdinalIgnoreCase) && !string.Equals(kept, except, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static int FrameCount(Image image)
        {
            foreach (Guid dimension in image.FrameDimensionsList)
                if (dimension == FrameDimension.Time.Guid) return Math.Max(1, image.GetFrameCount(FrameDimension.Time));
            return 1;
        }

        internal static string Check(byte[] bytes, string shown)
        {
            if (bytes.LongLength > MostBytes) return shown + " is over 8 MB.";
            string magic = bytes.Length < 6 ? "" : Encoding.ASCII.GetString(bytes, 0, 6);
            if (magic != "GIF87a" && magic != "GIF89a") return shown + " isn't a GIF.";
            try
            {
                using (MemoryStream stream = new MemoryStream(bytes, false))
                using (Image image = Image.FromStream(stream, false, true))
                {
                    if (!image.RawFormat.Equals(ImageFormat.Gif)) return shown + " isn't a GIF.";
                    if (image.Width > MostSide || image.Height > MostSide) return shown + " is over " + MostSide + " pixels across.";
                    if (FrameCount(image) > MostFrames) return shown + " has over " + MostFrames + " frames.";
                }
            }
            catch (ArgumentException) { return shown + " is a broken GIF."; }
            catch (OutOfMemoryException) { return shown + " is a broken GIF."; }
            catch (ExternalException) { return shown + " is a broken GIF."; }
            return null;
        }

        private static string Read(string file, out byte[] bytes)
        {
            bytes = null;
            string shown = Path.GetFileName(file ?? "");
            try
            {
                FileInfo info = new FileInfo(file);
                if (!info.Exists) return "Couldn't find " + shown + ".";
                if (info.Length > MostBytes) return shown + " is over 8 MB.";
                bytes = File.ReadAllBytes(file);
                return Check(bytes, shown);
            }
            catch (IOException) { return "Couldn't read " + shown + "."; }
            catch (UnauthorizedAccessException) { return "Couldn't read " + shown + "."; }
            catch (ArgumentException) { return "Couldn't read " + shown + "."; }
            catch (NotSupportedException) { return "Couldn't read " + shown + "."; }
        }

        internal static string Add(string file, string name, out string kept)
        {
            kept = null;
            string folder = Folder;
            if (string.IsNullOrEmpty(folder)) return "Her GIF shelf isn't ready yet.";
            byte[] bytes;
            string problem = Read(file, out bytes);
            if (problem != null) return problem;
            lock (gate)
            {
                if (Names().Count >= Most) return "Her shelf is full at " + Most + " GIFs. Remove one to add another.";
                string wanted = Unique(Clean(string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(file) : name), null);
                string target = Path.Combine(folder, wanted + ".gif");
                bool made = false;
                try
                {
                    Directory.CreateDirectory(folder);
                    using (FileStream output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        made = true;
                        output.Write(bytes, 0, bytes.Length);
                    }
                }
                catch (Exception error)
                {
                    if (!(error is IOException) && !(error is UnauthorizedAccessException)) throw;
                    if (made) try { File.Delete(target); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    return "Couldn't save " + Path.GetFileName(file) + ".";
                }
                kept = wanted;
                return null;
            }
        }

        internal static string Rename(string name, string to, out string kept)
        {
            kept = null;
            if (string.IsNullOrWhiteSpace(to)) return "Give it a name first.";
            lock (gate)
            {
                string from = PathOf(name);
                if (from == null) return "There's no GIF called \"" + Cut(name) + "\" any more.";
                string current = Path.GetFileNameWithoutExtension(from);
                string wanted = Unique(Clean(to), current);
                if (wanted == current) { kept = current; return null; }
                try { File.Move(from, Path.Combine(Path.GetDirectoryName(from), wanted + ".gif")); }
                catch (IOException) { return "Couldn't rename \"" + current + "\"."; }
                catch (UnauthorizedAccessException) { return "Couldn't rename \"" + current + "\"."; }
                kept = wanted;
                return null;
            }
        }

        internal static string Remove(string name)
        {
            lock (gate)
            {
                string path = PathOf(name);
                if (path == null) return "There's no GIF called \"" + Cut(name) + "\" any more.";
                try { File.Delete(path); }
                catch (IOException) { return "Couldn't remove \"" + Cut(name) + "\"."; }
                catch (UnauthorizedAccessException) { return "Couldn't remove \"" + Cut(name) + "\"."; }
                return null;
            }
        }

        internal static string Pick(string asked, out string name)
        {
            name = null;
            List<string> names = Names();
            if (names.Count == 0) return Empty;
            lock (gate)
            {
                if (Now() - sent < Rest) return Again;
                string wanted = Regex.Replace((asked ?? "").Trim(), @"(?:\.gif|\s+gif)\s*\z", "", RegexOptions.IgnoreCase).Trim().Trim('"', '\'');
                name = wanted.Length == 0 ? null : Find(names, wanted);
                if (name != null) return null;
                if (wanted.Length > 0 && !AnyOne.IsMatch(wanted)) return "There's no GIF called \"" + Cut(wanted) + "\". Your GIFs: " + Quoted(names, Listed) + ".";
                name = names[random.Next(names.Count)];
                return null;
            }
        }

        internal static void Sent() { lock (gate) sent = Now(); }

        internal static void Rested() { lock (gate) sent = double.NegativeInfinity; }

        internal static string Note()
        {
            List<string> names = Names();
            return names.Count == 0 ? "" : "\nGIFs they saved for you to send with send_gif: " + Quoted(names, Most) + ".";
        }

        internal static string Quoted(List<string> names, int most)
        {
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < names.Count && i < most; i++) text.Append(i == 0 ? "\"" : ", \"").Append(names[i]).Append('"');
            if (names.Count > most) text.Append(" and ").Append(names.Count - most).Append(" more");
            return text.ToString();
        }

        private static string Find(List<string> names, string wanted)
        {
            foreach (string name in names) if (string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase)) return name;
            string plain = Plain(wanted);
            if (plain.Length == 0) return null;
            foreach (string name in names) if (Plain(name) == plain) return name;
            string found = null;
            foreach (string name in names)
            {
                string mine = Plain(name);
                if (!(mine.Length >= 3 && plain.Contains(mine)) && !(plain.Length >= 3 && mine.Contains(plain))) continue;
                if (found != null) return null;
                found = name;
            }
            return found;
        }

        private static string Plain(string text) { return Regex.Replace((text ?? "").ToLowerInvariant(), @"[^\p{L}\p{Nd}]+", ""); }

        private static string Cut(string text)
        {
            string said = text ?? "";
            return said.Length > Longest ? said.Substring(0, Longest) + "..." : said;
        }
    }

    internal sealed class GifFrames : IDisposable
    {
        internal const long Budget = 48L * 1024 * 1024;
        private readonly Bitmap[] frames;
        private readonly int[] starts;
        internal readonly int Loop;
        internal readonly Size Shown;

        private GifFrames(Bitmap[] frames, int[] delays, Size shown)
        {
            this.frames = frames;
            starts = new int[delays.Length];
            int total = 0;
            for (int i = 0; i < delays.Length; i++)
            {
                starts[i] = total;
                total += delays[i];
            }
            Loop = Math.Max(1, total);
            Shown = shown;
        }

        internal int Count { get { return frames.Length; } }

        internal Bitmap Frame(int index) { return frames[index]; }

        internal int At(double seconds)
        {
            long into = (long)(Math.Max(0, seconds) * 1000) % Loop;
            int found = 0;
            for (int i = 1; i < starts.Length && starts[i] <= into; i++) found = i;
            return found;
        }

        internal static GifFrames Read(string path, Size room, float scale)
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
            return GifShelf.Check(bytes, "") == null ? Read(bytes, room, scale) : null;
        }

        internal static GifFrames Read(byte[] bytes, Size room, float scale)
        {
            Bitmap[] made = null;
            try
            {
                using (MemoryStream stream = new MemoryStream(bytes, false))
                using (Image image = Image.FromStream(stream, false, true))
                using (ImageAttributes wrap = new ImageAttributes())
                {
                    int count = GifShelf.FrameCount(image);
                    if (count > GifShelf.MostFrames) return null;
                    int[] delays = Delays(image, count);
                    float fit = Math.Min(scale, Math.Min(room.Width / (float)image.Width, room.Height / (float)image.Height));
                    Size shown = new Size(Math.Max(1, (int)Math.Round(image.Width * fit)), Math.Max(1, (int)Math.Round(image.Height * fit)));
                    double most = Math.Sqrt(Budget / 4.0 / count / ((double)shown.Width * shown.Height));
                    Size kept = most >= 1 ? shown : new Size(Math.Max(1, (int)(shown.Width * most)), Math.Max(1, (int)(shown.Height * most)));
                    wrap.SetWrapMode(WrapMode.TileFlipXY);
                    made = new Bitmap[count];
                    for (int i = 0; i < count; i++)
                    {
                        if (count > 1) image.SelectActiveFrame(FrameDimension.Time, i);
                        made[i] = new Bitmap(kept.Width, kept.Height, PixelFormat.Format32bppPArgb);
                        using (Graphics graphics = Graphics.FromImage(made[i]))
                        {
                            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            graphics.CompositingMode = CompositingMode.SourceCopy;
                            graphics.DrawImage(image, new Rectangle(Point.Empty, kept), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, wrap);
                        }
                    }
                    GifFrames read = new GifFrames(made, delays, shown);
                    made = null;
                    return read;
                }
            }
            catch (ArgumentException) { return null; }
            catch (OutOfMemoryException) { return null; }
            catch (ExternalException) { return null; }
            finally
            {
                if (made != null) foreach (Bitmap frame in made) if (frame != null) frame.Dispose();
            }
        }

        private static int[] Delays(Image image, int count)
        {
            byte[] raw = null;
            foreach (int id in image.PropertyIdList)
                if (id == 0x5100) { raw = image.GetPropertyItem(0x5100).Value; break; }
            int[] delays = new int[count];
            for (int i = 0; i < count; i++)
            {
                int hundredths = raw != null && raw.Length >= (i + 1) * 4 ? BitConverter.ToInt32(raw, i * 4) : 0;
                delays[i] = hundredths <= 0 ? 100 : (int)Math.Min(60000L, hundredths * 10L);
            }
            return delays;
        }

        public void Dispose()
        {
            foreach (Bitmap frame in frames) frame.Dispose();
        }
    }

    internal sealed class GifCard : Overlay
    {
        internal const double Least = 4, Longest = 12;
        private const string Speaker = "CAPRI: ";
        internal static readonly Size Room = new Size(240, 180);
        internal event Action Dismissed;
        private GifFrames gif;
        private string called;
        private double started = -1, until = -1;
        private Bitmap back;
        private float built;
        private RectangleF panel;
        private Rectangle gifBox;
        private int shownFrame = -1;
        private Point shownAt;
        private float shownScale;

        internal GifCard() : base("Capri's GIF") { Look.UsePointer(this); }

        internal bool Showing { get { return gif != null; } }
        internal string Called { get { return called; } }
        internal double Started { get { return started; } }
        internal double Until { get { return until; } }
        internal int FrameShown { get { return shownFrame; } }
        internal Rectangle GifBox { get { return gifBox; } }
        internal RectangleF Panel { get { return panel; } }
        internal GifFrames Frames { get { return gif; } }

        internal void Play(GifFrames frames, string name, double now)
        {
            Stop();
            gif = frames;
            called = name ?? "";
            started = now;
            until = now + Math.Max(Least, Math.Min(Longest, 2 * frames.Loop / 1000.0));
        }

        internal void Stop()
        {
            if (Visible) Hide();
            Forget();
            if (gif != null) { gif.Dispose(); gif = null; }
            if (back != null) { back.Dispose(); back = null; }
            started = until = -1;
            shownFrame = -1;
        }

        internal void Update(double now, float s, Rectangle body, Rectangle area, Rectangle[] avoid)
        {
            if (gif == null) return;
            if (now >= until) { Stop(); return; }
            if (back == null || built != s) Build(s);
            int frame = gif.At(now - started);
            Point at = Place(back.Size, body, area, avoid, s);
            if (Visible && Image != null && frame == shownFrame && s == shownScale)
            {
                if (at != shownAt) { Present(Image, at); shownAt = at; }
                return;
            }
            Bitmap image = Draw(frame);
            try { Present(image, at); }
            catch (Exception) { image.Dispose(); throw; }
            shownFrame = frame;
            shownAt = at;
            shownScale = s;
        }

        internal static Point Place(Size size, Rectangle body, Rectangle area, Rectangle[] avoid, float s)
        {
            int gap = (int)Math.Round(8 * s);
            int y = Math.Max(area.Top, Math.Min(area.Bottom - size.Height, body.Top + (body.Height - size.Height) / 2));
            Point[] tries = { new Point(body.Right + gap, y), new Point(body.Left - gap - size.Width, y) };
            foreach (Point at in tries)
            {
                Rectangle card = new Rectangle(at, size);
                if (card.Left < area.Left || card.Right > area.Right) continue;
                Rectangle blocking = Blocking(card, avoid);
                if (blocking.IsEmpty) return at;
                foreach (int top in new[] { blocking.Bottom + gap, blocking.Top - gap - size.Height })
                {
                    Rectangle moved = new Rectangle(at.X, top, size.Width, size.Height);
                    if (moved.Top >= area.Top && moved.Bottom <= area.Bottom && Blocking(moved, avoid).IsEmpty) return moved.Location;
                }
            }
            return new Point(Math.Max(area.Left, Math.Min(area.Right - size.Width, tries[0].X)), y);
        }

        private static Rectangle Blocking(Rectangle card, Rectangle[] avoid)
        {
            if (avoid != null) foreach (Rectangle one in avoid) if (!one.IsEmpty && one.IntersectsWith(card)) return one;
            return Rectangle.Empty;
        }

        private void Build(float s)
        {
            if (back != null) back.Dispose();
            back = null;
            built = s;
            string label = Speaker + called;
            using (Font text = Look.Mono(14 * s, FontStyle.Regular, GraphicsUnit.Pixel, label))
            using (Font bold = Look.Mono(14 * s, FontStyle.Bold, GraphicsUnit.Pixel, label))
            {
                float cw = Look.CharWidth(text), line = 19 * s, pad = 12 * s, margin = 6 * s, top = 10 * s;
                Size shown = gif.Shown;
                int columns = Math.Max(Speaker.Length + 4, (int)Math.Floor(Math.Max(shown.Width, 220 * s) / cw));
                if (Look.Cells(label) > columns) label = Look.Cut(label, columns - 3) + "...";
                float inner = Math.Max(shown.Width, Look.Cells(label) * cw);
                panel = new RectangleF(margin, margin, pad * 2 + inner, top + line + 6 * s + shown.Height + pad);
                gifBox = new Rectangle((int)Math.Round(panel.Left + pad + (inner - shown.Width) / 2), (int)Math.Round(panel.Top + top + line + 6 * s), shown.Width, shown.Height);
                back = new Bitmap((int)Math.Ceiling(panel.Right + margin), (int)Math.Ceiling(panel.Bottom + margin), PixelFormat.Format32bppPArgb);
                using (Graphics graphics = Graphics.FromImage(back))
                using (GraphicsPath path = Look.Plate(panel, 10 * s, -1, panel.Top + panel.Height / 2, 0, true))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    using (Pen glow = new Pen(Color.FromArgb(46, Look.Cyan), 7 * s)) graphics.DrawPath(glow, path);
                    using (Brush fill = new SolidBrush(Color.FromArgb(238, Look.Panel))) graphics.FillPath(fill, path);
                    graphics.SetClip(path);
                    using (Pen scan = new Pen(Color.FromArgb(9, 255, 255, 255), Math.Max(1, s)))
                        for (float row = panel.Top; row < panel.Bottom; row += 3 * s) graphics.DrawLine(scan, panel.Left, row, panel.Right, row);
                    graphics.ResetClip();
                    using (Pen edge = new Pen(Look.Cyan, 2 * s)) graphics.DrawPath(edge, path);
                    float x = panel.Left + pad, y = panel.Top + top;
                    using (Brush red = new SolidBrush(Look.Red)) graphics.DrawString(Speaker.TrimEnd(), bold, red, x, y, StringFormat.GenericTypographic);
                    using (Brush ink = new SolidBrush(Look.Neon)) Look.Draw(graphics, label.Substring(Speaker.Length), text, ink, x + Speaker.Length * cw, y);
                }
            }
        }

        private Bitmap Draw(int index)
        {
            Bitmap image = back.Clone(new Rectangle(Point.Empty, back.Size), PixelFormat.Format32bppPArgb);
            try
            {
                Bitmap frame = gif.Frame(index);
                using (Graphics graphics = Graphics.FromImage(image))
                {
                    bool same = frame.Width == gifBox.Width && frame.Height == gifBox.Height;
                    graphics.InterpolationMode = same ? InterpolationMode.NearestNeighbor : InterpolationMode.Bilinear;
                    graphics.PixelOffsetMode = same ? PixelOffsetMode.Half : PixelOffsetMode.HighQuality;
                    graphics.DrawImage(frame, gifBox);
                }
                return image;
            }
            catch (Exception)
            {
                image.Dispose();
                throw;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || gif == null) return;
            Stop();
            if (Dismissed != null) Dismissed();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (gif != null) { gif.Dispose(); gif = null; }
                if (back != null) { back.Dispose(); back = null; }
            }
            base.Dispose(disposing);
        }
    }

    internal sealed partial class Companion
    {
        private readonly GifCard gifCard = new GifCard();

        private string GifTool(string name)
        {
            if (remoteJobPhone != null || Hands.Phoned) return GifShelf.OnPhone;
            string picked;
            string problem = GifShelf.Pick(name, out picked);
            if (problem != null) return problem;
            float s = 1;
            string blocked = null;
            Action look = delegate { blocked = GifBlocked(); s = TalkScale(); };
            if (InvokeRequired) Invoke(look); else look();
            if (blocked != null) return blocked;
            GifFrames gif = GifFrames.Read(GifShelf.PathOf(picked) ?? "", new Size((int)(GifCard.Room.Width * s), (int)(GifCard.Room.Height * s)), s);
            if (gif == null) return "Couldn't play \"" + picked + "\": it didn't open as a GIF.";
            try { return InvokeRequired ? (string)Invoke(new Func<GifFrames, string, string>(ShowGif), gif, picked) : ShowGif(gif, picked); }
            catch (Exception) { if (gifCard.Frames != gif) gif.Dispose(); throw; }
        }

        private string GifBlocked() { return hidden ? GifShelf.Away : docked ? GifShelf.Docked : null; }

        private static string PhoneTurn(Func<string, string> think, string text, bool phoned)
        {
            bool was = Hands.Phoned;
            Hands.Phoned = phoned;
            try { return think(text); }
            finally { Hands.Phoned = was; }
        }

        private string ShowGif(GifFrames gif, string name)
        {
            string blocked = GifBlocked();
            if (blocked != null) { gif.Dispose(); return blocked; }
            double now = clock.Elapsed.TotalSeconds;
            gifCard.Play(gif, name, now);
            UpdateGif(now);
            if (!gifCard.Showing) return "Couldn't show \"" + name + "\" just now.";
            GifShelf.Sent();
            frames.Start();
            return "Sent \"" + name + "\". It's playing in a card beside me.";
        }

        private void UpdateGif(double now)
        {
            if (!gifCard.Showing) return;
            if (hidden || docked || !IsHandleCreated) { gifCard.Stop(); return; }
            Rectangle body = BodyOnScreen(anchor, lying || seat != null || riding || onPanel ? PerchedBody : Body);
            Rectangle area = Screen.FromPoint(new Point(body.Left + body.Width / 2, body.Top + body.Height / 2)).WorkingArea;
            Rectangle[] avoid = { box.Visible ? box.Bounds : Rectangle.Empty, typer.Visible ? typer.Bounds : Rectangle.Empty };
            try { gifCard.Update(now, TalkScale(), body, area, avoid); }
            catch (Exception error)
            {
                gifCard.Stop();
                if (testing) throw;
                Program.Crashed(error);
            }
        }
    }
}

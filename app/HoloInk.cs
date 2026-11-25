using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace CapriDesktop
{
    internal sealed class HoloView
    {
        internal bool InCall, Speaking, Thinking, Muted, CanMute, CanEnd;
        internal bool Motion = true, Captions = true;
        internal bool CanCamera, CameraOn;
        internal string Caption, CallName;
        internal TimeSpan Elapsed;
    }

    internal enum HoloPose { Idle, Speaking, Thinking }

    internal sealed class HoloArt : IDisposable
    {
        internal const int Fps = 30;
        private Bitmap idle, speaking, thinking, opening;
        private ZipArchive intro;
        private int shown = -1;

        internal HoloArt(Assembly source, bool withOpening = true) : this(delegate(string name) { return Open(source, name); }, withOpening) { }

        internal HoloArt(Func<string, Stream> open, bool withOpening = true)
        {
            try
            {
                idle = LoadPose(open, "idle");
                speaking = LoadPose(open, "speaking");
                thinking = LoadPose(open, "thinking");
                if (withOpening)
                {
                    Stream frames = open("intro-30.zip");
                    try { intro = new ZipArchive(frames, ZipArchiveMode.Read); }
                    catch { frames.Dispose(); throw; }
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private static Stream Open(Assembly source, string name)
        {
            Stream found = source.GetManifestResourceStream("Capri.Holo." + name);
            if (found == null) throw new InvalidDataException("Missing Holocall art " + name + ".");
            return found;
        }

        private static Bitmap LoadPose(Func<string, Stream> open, string pose)
        {
            using (Stream image = open(pose + ".png")) return Decode(image);
        }

        private static Bitmap Decode(Stream stream)
        {
            using (Image image = Image.FromStream(stream, false, false))
            {
                Bitmap copy = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppPArgb);
                using (Graphics canvas = Graphics.FromImage(copy))
                {
                    canvas.CompositingMode = CompositingMode.SourceCopy;
                    canvas.DrawImage(image, 0, 0, image.Width, image.Height);
                }
                return copy;
            }
        }

        internal int IntroFrames { get { return intro == null ? 0 : intro.Entries.Count; } }

        internal double IntroSeconds { get { return IntroFrames / (double)Fps; } }

        internal Bitmap Pose(HoloPose pose)
        {
            return pose == HoloPose.Speaking ? speaking : pose == HoloPose.Thinking ? thinking : idle;
        }

        internal Bitmap Pose(HoloView view) { return Pose(HoloInk.PoseOf(view, 0)); }

        internal byte[] IntroJpeg(int index)
        {
            if (intro == null || index < 0 || index >= IntroFrames) return null;
            ZipArchiveEntry entry = intro.GetEntry(index.ToString("D3", CultureInfo.InvariantCulture) + ".jpg");
            if (entry == null) return null;
            using (Stream frame = entry.Open())
            using (MemoryStream copy = new MemoryStream())
            {
                frame.CopyTo(copy);
                return copy.ToArray();
            }
        }

        internal Bitmap Opening(double elapsed)
        {
            if (intro == null || elapsed < 0 || elapsed >= IntroSeconds) { ClearOpening(); return null; }
            int want = Math.Min(IntroFrames - 1, (int)(elapsed * Fps));
            if (want != shown)
            {
                byte[] jpeg = IntroJpeg(want);
                if (jpeg == null) return opening;
                Bitmap next;
                using (MemoryStream frame = new MemoryStream(jpeg)) next = Decode(frame);
                ClearOpening();
                opening = next;
                shown = want;
            }
            return opening;
        }

        private void ClearOpening()
        {
            if (opening != null) opening.Dispose();
            opening = null;
            shown = -1;
        }

        public void Dispose()
        {
            ClearOpening();
            if (intro != null) { intro.Dispose(); intro = null; }
            if (idle != null) { idle.Dispose(); idle = null; }
            if (speaking != null) { speaking.Dispose(); speaking = null; }
            if (thinking != null) { thinking.Dispose(); thinking = null; }
        }
    }

    internal enum HoloAction { None, Exit, Mic, FullSize, Camera, End }

    internal sealed class HoloScene
    {
        internal HoloView View = new HoloView();
        internal Image Face, Opening;
        internal float OpeningShare, Reveal = 1;
        internal double Time;
        internal int Glitch;
        internal HoloAction Hover;
        internal float ChipShare;
    }

    internal static class HoloInk
    {
        internal const int Width = 376, Height = 302;
        internal static readonly Rectangle Portrait = new Rectangle(12, 34, 352, 220);
        internal const float FocusX = 0.3f, FocusY = 0f;

        internal static readonly Color Rose = Color.FromArgb(248, 14, 64);
        internal static readonly Color Green = Color.FromArgb(5, 248, 103);
        internal static readonly Color Chalk = Color.FromArgb(238, 241, 246);
        internal static readonly Color Quiet = Color.FromArgb(150, 160, 178);
        internal static readonly Color Glass = Color.FromArgb(70, 8, 10, 18);
        internal static readonly Color Stage = Color.FromArgb(11, 12, 20);

        internal static Func<string, FontFamily> Family = delegate(string role) { return null; };

        private static readonly HoloAction[] Keys = { HoloAction.Mic, HoloAction.Camera, HoloAction.FullSize, HoloAction.End };

        internal static HoloPose PoseOf(HoloView view, double time)
        {
            if (view.Speaking) return Flap(time) ? HoloPose.Speaking : HoloPose.Idle;
            return view.Thinking ? HoloPose.Thinking : HoloPose.Idle;
        }

        internal static bool Flap(double time)
        {
            int beat = (int)Math.Floor(time * 7.5);
            return beat % 5 != 3 && beat % 2 == 0 || beat % 7 == 1;
        }

        private static Font MakeFont(string role, float pixels, FontStyle style)
        {
            FontFamily family = null;
            try { family = Family(role); }
            catch (Exception) { family = null; }
            if (family != null)
            {
                try { return new Font(family, pixels, style, GraphicsUnit.Pixel); }
                catch (ArgumentException) { }
            }
            string fallback = role == "mono" ? "Consolas" : role == "title" ? "Bahnschrift SemiBold SemiConden" : "Bahnschrift SemiCondensed";
            try { return new Font(fallback, pixels, style, GraphicsUnit.Pixel); }
            catch (ArgumentException) { return new Font(FontFamily.GenericSansSerif, pixels, style, GraphicsUnit.Pixel); }
        }

        internal static RectangleF Cover(Size source, RectangleF bounds, float focusX, float focusY)
        {
            float scale = Math.Max(bounds.Width / source.Width, bounds.Height / source.Height);
            float w = source.Width * scale, h = source.Height * scale;
            return new RectangleF(bounds.X + (bounds.Width - w) * focusX, bounds.Y + (bounds.Height - h) * focusY, w, h);
        }

        internal static RectangleF Fit(Size source, RectangleF bounds)
        {
            float scale = Math.Min(bounds.Width / source.Width, bounds.Height / source.Height);
            float w = source.Width * scale, h = source.Height * scale;
            return new RectangleF(bounds.X + (bounds.Width - w) / 2, bounds.Y + (bounds.Height - h) / 2, w, h);
        }

        internal static Bitmap Scaled(Image source, Size size, bool cover)
        {
            Bitmap result = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height), PixelFormat.Format32bppPArgb);
            using (Graphics canvas = Graphics.FromImage(result))
            {
                canvas.InterpolationMode = InterpolationMode.HighQualityBicubic;
                canvas.PixelOffsetMode = PixelOffsetMode.HighQuality;
                canvas.CompositingQuality = CompositingQuality.HighQuality;
                RectangleF all = new RectangleF(0, 0, result.Width, result.Height);
                canvas.DrawImage(source, cover ? Cover(source.Size, all, FocusX, FocusY) : Fit(source.Size, all));
            }
            return result;
        }

        internal static RectangleF Unit(Rectangle box, float scale)
        {
            return new RectangleF(box.X * scale, box.Y * scale, box.Width * scale, box.Height * scale);
        }

        internal static Size CompactSize(float scale)
        {
            return new Size((int)Math.Ceiling(Width * scale), (int)Math.Ceiling(Height * scale));
        }

        internal static Size FaceSize(float scale)
        {
            return new Size((int)Math.Round(Portrait.Width * scale), (int)Math.Round(Portrait.Height * scale));
        }

        private static void Prepare(Graphics graphics)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
        }

        internal static void DrawCompact(Graphics graphics, float scale, HoloScene scene)
        {
            Prepare(graphics);
            HoloView view = scene.View;
            RectangleF face = Unit(Portrait, scale);
            float reveal = Math.Max(0, Math.Min(1, scene.Reveal));

            using (SolidBrush glass = new SolidBrush(Glass)) graphics.FillRectangle(glass, face);
            DrawFace(graphics, face, scene, scale);
            Scanlines(graphics, face, scale, scene.Time, view.Motion);
            if (view.Captions && !string.IsNullOrWhiteSpace(view.Caption)) DrawCaption(graphics, face, view.Caption, 12.5f * scale, scale);

            float arm = 24 * scale * Ease(reveal);
            RectangleF frame = RectangleF.Inflate(face, 6 * scale, 6 * scale);
            float stroke = Math.Max(1.5f, 2 * scale);
            Bracket(graphics, frame.Left, frame.Top, 1, 1, arm, stroke, Rose);
            Bracket(graphics, frame.Right, frame.Top, -1, 1, arm, stroke, Green);
            Bracket(graphics, frame.Left, frame.Bottom, 1, -1, arm, stroke, Green);
            Bracket(graphics, frame.Right, frame.Bottom, -1, -1, arm, stroke, Rose);

            using (Font mono = MakeFont("mono", 12.5f * scale, FontStyle.Regular))
            using (Font title = MakeFont("title", 15 * scale, FontStyle.Regular))
            {
                float rowY = 9 * scale, rowH = 18 * scale;
                bool live = view.Speaking || view.InCall;
                Color dot = view.Speaking ? Rose : Green;
                float pulse = view.Speaking ? 0.55f + 0.45f * (float)Math.Abs(Math.Sin(scene.Time * 6)) : 1f;
                using (SolidBrush glow = new SolidBrush(Color.FromArgb((int)(70 * pulse), dot)))
                    graphics.FillEllipse(glow, 11 * scale, rowY + rowH / 2 - 6 * scale, 12 * scale, 12 * scale);
                using (SolidBrush core = new SolidBrush(Color.FromArgb((int)(255 * pulse), dot)))
                    graphics.FillEllipse(core, 14 * scale, rowY + rowH / 2 - 3 * scale, 6 * scale, 6 * scale);
                string name = Reveal("CAPRI", reveal * 1.6f);
                SizeF named = Write(graphics, name, title, Chalk, new PointF(26 * scale, rowY), rowH);
                string tag = Reveal(live && view.InCall ? "// HOLOCALL" : "// HOLO", reveal * 1.3f - 0.3f);
                Write(graphics, tag, mono, Quiet, new PointF(26 * scale + named.Width + 6 * scale, rowY), rowH);

                float right = (Width - 12) * scale;
                RectangleF exit = ActionBox(HoloAction.Exit, scale, false, Size.Empty);
                DrawGlyph(graphics, HoloAction.Exit, exit, scene.Hover == HoloAction.Exit ? Chalk : Quiet, scale, view, scene.Hover == HoloAction.Exit);
                right = exit.Left - 6 * scale;
                if (view.InCall)
                {
                    string clock = Clock(view.Elapsed);
                    SizeF size = graphics.MeasureString(clock, mono, PointF.Empty, StringFormat.GenericTypographic);
                    Write(graphics, clock, mono, Chalk, new PointF(right - size.Width, rowY), rowH);
                }

                float baseY = (Portrait.Bottom + 12) * scale;
                string status = view.Speaking ? "SPEAKING" : view.Thinking ? "THINKING" : view.InCall ? "IN CALL" : "LISTENING";
                if (view.InCall && !string.IsNullOrEmpty(view.CallName)) status = view.CallName.ToUpperInvariant() + "  " + status;
                float statusRight = Portrait.Right * scale;
                foreach (HoloAction key in Keys)
                    if (ActionAvailable(key, view)) { statusRight = ActionBox(key, scale, false, Size.Empty, view).Left - 10 * scale; break; }
                WriteClipped(graphics, status, mono, Quiet, new RectangleF(14 * scale, baseY, statusRight - 14 * scale, 30 * scale));
            }

            foreach (HoloAction key in Keys)
            {
                if (!ActionAvailable(key, view)) continue;
                RectangleF box = ActionBox(key, scale, false, Size.Empty, view);
                bool hot = scene.Hover == key;
                if (hot)
                {
                    using (SolidBrush fill = new SolidBrush(Color.FromArgb(150, 14, 16, 28))) graphics.FillRectangle(fill, box);
                    using (Pen edge = new Pen(Color.FromArgb(90, 238, 241, 246), 1)) graphics.DrawRectangle(edge, box.X + 0.5f, box.Y + 0.5f, box.Width - 1, box.Height - 1);
                }
                else
                {
                    using (SolidBrush fill = new SolidBrush(Color.FromArgb(70, 8, 10, 18))) graphics.FillRectangle(fill, box);
                }
                Color ink = key == HoloAction.End ? Rose : key == HoloAction.Mic && view.Muted ? Rose : key == HoloAction.Camera && view.CameraOn ? Green : Chalk;
                DrawGlyph(graphics, key, box, ink, scale, view, hot);
            }
        }

        internal static void DrawFull(Graphics graphics, Size bounds, HoloScene scene)
        {
            Prepare(graphics);
            float unit = Math.Max(1f, bounds.Height / 1080f);
            RectangleF screen = new RectangleF(0, 0, bounds.Width, bounds.Height);
            DrawFace(graphics, screen, scene, unit);
            HoloView view = scene.View;
            if (view.Captions && !string.IsNullOrWhiteSpace(view.Caption))
            {
                float width = Math.Min(bounds.Width - 80 * unit, 1100 * unit);
                RectangleF strip = new RectangleF((bounds.Width - width) / 2, bounds.Height - 150 * unit, width, 110 * unit);
                DrawCaption(graphics, strip, view.Caption, 30 * unit, unit);
            }
            if (scene.ChipShare > 0.01f)
            {
                RectangleF chip = ActionBox(HoloAction.Exit, unit, true, bounds);
                int alpha = (int)(255 * Math.Min(1, scene.ChipShare));
                bool hot = scene.Hover == HoloAction.Exit;
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(alpha * (hot ? 235 : 200) / 255, 8, 9, 16))) graphics.FillRectangle(fill, chip);
                using (Pen edge = new Pen(Color.FromArgb(alpha, hot ? Rose : Color.FromArgb(70, 80, 100)), Math.Max(1, unit)))
                    graphics.DrawRectangle(edge, chip.X + 0.5f, chip.Y + 0.5f, chip.Width - 1, chip.Height - 1);
                Bracket(graphics, chip.Left - 3 * unit, chip.Top - 3 * unit, 1, 1, 8 * unit, Math.Max(1.5f, 2 * unit), Color.FromArgb(alpha, Rose));
                Bracket(graphics, chip.Right + 3 * unit, chip.Bottom + 3 * unit, -1, -1, 8 * unit, Math.Max(1.5f, 2 * unit), Color.FromArgb(alpha, Green));
                using (Font mono = MakeFont("mono", 14 * unit, FontStyle.Regular))
                using (StringFormat centre = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                using (SolidBrush ink = new SolidBrush(Color.FromArgb(alpha, Chalk)))
                    graphics.DrawString("EXIT FULL SIZE  ·  ESC", mono, ink, chip, centre);
            }
        }

        private static void DrawFace(Graphics graphics, RectangleF box, HoloScene scene, float scale)
        {
            GraphicsState saved = graphics.Save();
            try
            {
                graphics.SetClip(box);
                float opening = Math.Max(0, Math.Min(1, scene.OpeningShare));
                if (scene.Face != null && opening < 1)
                {
                    RectangleF at = new RectangleF(box.X, box.Y, scene.Face.Width, scene.Face.Height);
                    if (Math.Abs(at.Width - box.Width) > 1 || Math.Abs(at.Height - box.Height) > 1)
                        at = Cover(scene.Face.Size, box, FocusX, FocusY);
                    Blit(graphics, scene.Face, at, 1 - opening);
                    if (scene.Glitch > 0) Glitch(graphics, scene.Face, at, box, scene.Glitch, scale);
                }
                if (scene.Opening != null && opening > 0)
                {
                    graphics.InterpolationMode = InterpolationMode.Bilinear;
                    RectangleF at = Cover(scene.Opening.Size, box, FocusX, FocusY);
                    Blit(graphics, scene.Opening, at, opening);
                }
            }
            finally { graphics.Restore(saved); }
        }

        private static void Blit(Graphics graphics, Image image, RectangleF at, float alpha)
        {
            if (alpha <= 0.01f) return;
            bool exact = Math.Abs(at.Width - image.Width) < 0.5f && Math.Abs(at.Height - image.Height) < 0.5f;
            if (alpha >= 0.99f)
            {
                if (exact) graphics.DrawImageUnscaled(image, (int)Math.Round(at.X), (int)Math.Round(at.Y));
                else graphics.DrawImage(image, at);
                return;
            }
            using (ImageAttributes fade = new ImageAttributes())
            {
                fade.SetColorMatrix(new ColorMatrix { Matrix33 = alpha });
                graphics.DrawImage(image, Rectangle.Round(at), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, fade);
            }
        }

        private static void Glitch(Graphics graphics, Image face, RectangleF at, RectangleF box, int seed, float scale)
        {
            Random random = new Random(seed);
            float sx = face.Width / at.Width, sy = face.Height / at.Height;
            for (int i = 0; i < 3; i++)
            {
                float y = box.Top + (float)random.NextDouble() * box.Height * 0.9f;
                float h = (3 + (float)random.NextDouble() * 12) * scale;
                float shift = ((float)random.NextDouble() * 2 - 1) * 12 * scale;
                RectangleF source = new RectangleF((box.Left - at.Left) * sx, (y - at.Top) * sy, box.Width * sx, h * sy);
                graphics.DrawImage(face, new RectangleF(box.Left + shift, y, box.Width, h), source, GraphicsUnit.Pixel);
            }
            float line = box.Top + (float)random.NextDouble() * box.Height;
            using (SolidBrush ink = new SolidBrush(Color.FromArgb(150, random.Next(2) == 0 ? Green : Rose)))
                graphics.FillRectangle(ink, box.Left, line, box.Width, Math.Max(1, scale));
        }

        private static void Scanlines(Graphics graphics, RectangleF box, float scale, double time, bool motion)
        {
            float step = Math.Max(2, 3 * scale);
            using (SolidBrush line = new SolidBrush(Color.FromArgb(22, 255, 255, 255)))
                for (float y = box.Top; y < box.Bottom; y += step) graphics.FillRectangle(line, box.Left, y, box.Width, Math.Max(1, (float)Math.Floor(scale)));
            if (!motion) return;
            float band = 30 * scale;
            float travel = box.Height + band * 2;
            float y0 = box.Top - band + (float)((time * 38 * scale) % travel);
            RectangleF glow = new RectangleF(box.Left, y0, box.Width, band);
            RectangleF shown = RectangleF.Intersect(glow, box);
            if (shown.Height <= 1) return;
            using (LinearGradientBrush sweep = new LinearGradientBrush(new RectangleF(glow.X, glow.Y - 1, glow.Width, glow.Height + 2),
                Color.FromArgb(0, 5, 248, 103), Color.FromArgb(26, 5, 248, 103), LinearGradientMode.Vertical))
                graphics.FillRectangle(sweep, shown);
        }

        private static void DrawCaption(Graphics graphics, RectangleF stage, string text, float pixels, float scale)
        {
            using (Font words = MakeFont("text", pixels, FontStyle.Regular))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Far, Trimming = StringTrimming.EllipsisWord })
            {
                float width = stage.Width - 16 * scale;
                SizeF need = graphics.MeasureString(text, words, (int)width, format);
                float height = Math.Min(need.Height, graphics.MeasureString("Ag", words).Height * 2.05f);
                RectangleF well = new RectangleF(stage.Left + (stage.Width - Math.Min(width, need.Width) - 16 * scale) / 2,
                    stage.Bottom - height - 14 * scale, Math.Min(width, need.Width) + 16 * scale, height + 8 * scale);
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(210, 8, 9, 16))) graphics.FillRectangle(fill, well);
                using (SolidBrush mark = new SolidBrush(Rose)) graphics.FillRectangle(mark, well.Left, well.Top, Math.Max(2, 2 * scale), well.Height);
                RectangleF inner = RectangleF.Inflate(well, -8 * scale, -4 * scale);
                using (SolidBrush ink = new SolidBrush(Chalk)) graphics.DrawString(text, words, ink, inner, format);
            }
        }

        private static float Ease(float t) { return 1 - (1 - t) * (1 - t) * (1 - t); }

        private static string Reveal(string text, float share)
        {
            if (share >= 1) return text;
            if (share <= 0) return "";
            return text.Substring(0, Math.Max(0, Math.Min(text.Length, (int)Math.Ceiling(text.Length * share))));
        }

        private static void Bracket(Graphics graphics, float x, float y, int horizontal, int vertical, float arm, float stroke, Color colour)
        {
            if (arm < 1) return;
            using (Pen pen = new Pen(colour, stroke) { StartCap = LineCap.Square, EndCap = LineCap.Square, LineJoin = LineJoin.Miter })
                graphics.DrawLines(pen, new[] { new PointF(x, y + vertical * arm), new PointF(x, y), new PointF(x + horizontal * arm, y) });
        }

        private static SizeF Write(Graphics graphics, string text, Font font, Color ink, PointF at, float rowHeight)
        {
            if (string.IsNullOrEmpty(text)) return SizeF.Empty;
            SizeF size = graphics.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);
            using (SolidBrush brush = new SolidBrush(ink))
                graphics.DrawString(text, font, brush, new PointF(at.X, at.Y + (rowHeight - size.Height) / 2), StringFormat.GenericTypographic);
            return size;
        }

        private static void WriteClipped(Graphics graphics, string text, Font font, Color ink, RectangleF box)
        {
            if (box.Width <= 4) return;
            using (SolidBrush brush = new SolidBrush(ink))
            using (StringFormat format = new StringFormat(StringFormat.GenericTypographic) { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                graphics.DrawString(text, font, brush, box, format);
        }

        internal static string Clock(TimeSpan elapsed)
        {
            return ((int)Math.Max(0, elapsed.TotalMinutes)).ToString("D2", CultureInfo.InvariantCulture)
                + ":" + Math.Max(0, elapsed.Seconds).ToString("D2", CultureInfo.InvariantCulture);
        }

        internal static RectangleF ActionBox(HoloAction action, float scale, bool fullSize, Size bounds, HoloView view = null)
        {
            if (fullSize)
            {
                if (action != HoloAction.Exit) return RectangleF.Empty;
                return new RectangleF(bounds.Width - 250 * scale, 26 * scale, 220 * scale, 36 * scale);
            }
            if (action == HoloAction.Exit) return new RectangleF((Width - 12 - 20) * scale, 8 * scale, 20 * scale, 20 * scale);
            int index = Array.IndexOf(Keys, action);
            if (index < 0 || view != null && !ActionAvailable(action, view)) return RectangleF.Empty;
            int after = 0;
            for (int i = index + 1; i < Keys.Length; i++) if (view == null || ActionAvailable(Keys[i], view)) after++;
            float size = 30, gap = 6;
            float right = Portrait.Right + 1;
            float x = right - (after + 1) * size - after * gap;
            return new RectangleF(x * scale, (Portrait.Bottom + 12) * scale, size * scale, size * scale);
        }

        internal static HoloAction ActionAt(Point point, HoloView view, float scale, bool fullSize, Size bounds, float chip)
        {
            if (fullSize) return chip > 0.05f && ActionBox(HoloAction.Exit, scale, true, bounds).Contains(point) ? HoloAction.Exit : HoloAction.None;
            if (RectangleF.Inflate(ActionBox(HoloAction.Exit, scale, false, bounds), 4 * scale, 4 * scale).Contains(point)) return HoloAction.Exit;
            foreach (HoloAction key in Keys)
                if (ActionAvailable(key, view) && ActionBox(key, scale, false, bounds, view).Contains(point)) return key;
            return HoloAction.None;
        }

        internal static string ActionText(HoloAction action, HoloView view, bool fullSize)
        {
            switch (action)
            {
                case HoloAction.Exit: return fullSize ? "Exit full size" : "Close Holocall";
                case HoloAction.Mic: return view.Muted ? "Unmute mic" : "Mute mic";
                case HoloAction.FullSize: return "Full size";
                case HoloAction.Camera: return view.CameraOn ? "Turn off Capri Camera" : "Turn on Capri Camera";
                case HoloAction.End: return "End call";
                default: return "";
            }
        }

        internal static bool ActionAvailable(HoloAction action, HoloView view)
        {
            if (action == HoloAction.Mic) return view.CanMute;
            if (action == HoloAction.Camera) return view.CanCamera;
            if (action == HoloAction.End) return view.CanEnd;
            return action != HoloAction.None;
        }

        private static void DrawGlyph(Graphics graphics, HoloAction action, RectangleF box, Color ink, float scale, HoloView view, bool hot)
        {
            float s = Math.Min(box.Width, box.Height) / 24f;
            float cx = box.X + box.Width / 2, cy = box.Y + box.Height / 2;
            Func<float, float, PointF> p = delegate(float x, float y) { return new PointF(cx + (x - 12) * s * 0.72f, cy + (y - 12) * s * 0.72f); };
            using (Pen pen = new Pen(ink, Math.Max(1.25f, 1.9f * s * 0.72f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (SolidBrush fill = new SolidBrush(ink))
            using (GraphicsPath path = new GraphicsPath())
            {
                switch (action)
                {
                    case HoloAction.Exit:
                        graphics.DrawLine(pen, p(6, 6), p(18, 18));
                        graphics.DrawLine(pen, p(18, 6), p(6, 18));
                        break;
                    case HoloAction.Mic:
                        path.AddArc(p(9, 3).X, p(9, 3).Y, 6 * s * 0.72f, 6 * s * 0.72f, 180, 180);
                        path.AddLine(p(15, 6), p(15, 11));
                        path.AddArc(p(9, 8).X, p(9, 8).Y, 6 * s * 0.72f, 6 * s * 0.72f, 0, 180);
                        path.CloseFigure();
                        graphics.DrawPath(pen, path);
                        path.Reset();
                        path.AddArc(p(5, 4).X, p(5, 4).Y, 14 * s * 0.72f, 14 * s * 0.72f, 0, 180);
                        graphics.DrawPath(pen, path);
                        graphics.DrawLine(pen, p(12, 18), p(12, 21));
                        if (view.Muted) graphics.DrawLine(pen, p(4, 3), p(20, 21));
                        break;
                    case HoloAction.Camera:
                        graphics.DrawRectangle(pen, p(3, 7).X, p(3, 7).Y, 12 * s * 0.72f, 10 * s * 0.72f);
                        graphics.DrawLines(pen, new[] { p(16, 11), p(21, 8), p(21, 16), p(16, 13) });
                        if (view.CameraOn) graphics.FillEllipse(fill, p(7.5f, 10.5f).X, p(7.5f, 10.5f).Y, 3 * s * 0.72f, 3 * s * 0.72f);
                        break;
                    case HoloAction.FullSize:
                        graphics.DrawLines(pen, new[] { p(4, 9), p(4, 4), p(9, 4) });
                        graphics.DrawLines(pen, new[] { p(15, 4), p(20, 4), p(20, 9) });
                        graphics.DrawLines(pen, new[] { p(4, 15), p(4, 20), p(9, 20) });
                        graphics.DrawLines(pen, new[] { p(15, 20), p(20, 20), p(20, 15) });
                        break;
                    case HoloAction.End:
                        path.AddBezier(p(2, 14), p(7, 8.5f), p(17, 8.5f), p(22, 14));
                        path.AddLine(p(22, 14), p(20, 17));
                        path.AddLine(p(20, 17), p(15.5f, 15.6f));
                        path.AddLine(p(15.5f, 15.6f), p(15.5f, 13));
                        path.AddBezier(p(15.5f, 13), p(13, 12.2f), p(11, 12.2f), p(8.5f, 13));
                        path.AddLine(p(8.5f, 13), p(8.5f, 15.6f));
                        path.AddLine(p(8.5f, 15.6f), p(4, 17));
                        path.CloseFigure();
                        graphics.FillPath(fill, path);
                        break;
                }
            }
        }

        internal static byte[] CameraFrame(Image portrait, bool label = true)
        {
            using (Bitmap frame = new Bitmap(1280, 720, PixelFormat.Format24bppRgb))
            using (Graphics canvas = Graphics.FromImage(frame))
            using (MemoryStream encoded = new MemoryStream())
            {
                DrawCamera(canvas, new Size(frame.Width, frame.Height), portrait, label);
                ImageCodecInfo jpeg = null;
                foreach (ImageCodecInfo codec in ImageCodecInfo.GetImageEncoders()) if (codec.MimeType == "image/jpeg") jpeg = codec;
                if (jpeg == null) frame.Save(encoded, ImageFormat.Jpeg);
                else
                    using (EncoderParameters quality = new EncoderParameters(1))
                    {
                        quality.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 88L);
                        frame.Save(encoded, jpeg, quality);
                    }
                return encoded.ToArray();
            }
        }

        internal static void DrawCamera(Graphics canvas, Size size, Image portrait, bool label)
        {
            Prepare(canvas);
            RectangleF all = new RectangleF(0, 0, size.Width, size.Height);
            using (LinearGradientBrush back = new LinearGradientBrush(all, Color.FromArgb(10, 12, 22), Color.FromArgb(24, 8, 18), LinearGradientMode.ForwardDiagonal))
                canvas.FillRectangle(back, all);
            canvas.InterpolationMode = InterpolationMode.HighQualityBicubic;
            canvas.DrawImage(portrait, Fit(portrait.Size, all));
            float scale = size.Height / 360f;
            Scanlines(canvas, all, scale * 0.75f, 0, false);
            if (!label) return;
            RectangleF frame = RectangleF.Inflate(all, -14 * scale, -14 * scale);
            float stroke = 2.5f * scale * 0.6f, arm = 22 * scale;
            Bracket(canvas, frame.Left, frame.Top, 1, 1, arm, stroke, Rose);
            Bracket(canvas, frame.Right, frame.Top, -1, 1, arm, stroke, Green);
            Bracket(canvas, frame.Left, frame.Bottom, 1, -1, arm, stroke, Green);
            Bracket(canvas, frame.Right, frame.Bottom, -1, -1, arm, stroke, Rose);
            using (Font mono = MakeFont("mono", 9.5f * scale, FontStyle.Regular))
            using (Font title = MakeFont("title", 11.5f * scale, FontStyle.Regular))
            {
                float x = frame.Left + 10 * scale, y = frame.Bottom - 24 * scale, row = 14 * scale;
                SizeF tag = canvas.MeasureString("CAPRI", title, PointF.Empty, StringFormat.GenericTypographic);
                SizeF sub = canvas.MeasureString("// HOLOCALL", mono, PointF.Empty, StringFormat.GenericTypographic);
                using (SolidBrush plate = new SolidBrush(Color.FromArgb(200, 8, 9, 16)))
                    canvas.FillRectangle(plate, x - 6 * scale, y - 4 * scale, tag.Width + sub.Width + 34 * scale, row + 8 * scale);
                using (SolidBrush dot = new SolidBrush(Green)) canvas.FillEllipse(dot, x, y + row / 2 - 2.5f * scale, 5 * scale, 5 * scale);
                Write(canvas, "CAPRI", title, Chalk, new PointF(x + 10 * scale, y), row);
                Write(canvas, "// HOLOCALL", mono, Quiet, new PointF(x + 16 * scale + tag.Width, y), row);
            }
        }
    }
}

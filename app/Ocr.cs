using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace CapriDesktop
{
    internal sealed class ReadWord
    {
        internal string Text;
        internal Rectangle Box;
        // The words inside a phrase, each with its own box.
        internal List<ReadWord> Parts;
    }

    internal static class Ocr
    {
        private sealed class Reading
        {
            internal readonly List<ReadWord> Words = new List<ReadWord>();
            internal readonly List<string> Lines = new List<string>();
            internal readonly List<ReadWord> Phrases = new List<ReadWord>();
        }

        private sealed class ReadLine
        {
            internal string Text;
            internal Rectangle Box;
            internal readonly List<ReadWord> Words = new List<ReadWord>();
        }

        private static readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private static readonly Task<OcrEngine> engine = Task.Run<OcrEngine>(delegate
        {
            try
            {
                OcrEngine preferred = OcrEngine.TryCreateFromUserProfileLanguages();
                if (preferred != null) return preferred;
            }
            catch (Exception) { }
            try { return OcrEngine.TryCreateFromLanguage(new Language("en-US")); }
            catch (Exception) { return null; }
        });

        private static bool OnUi { get { return Application.MessageLoop || SynchronizationContext.Current is WindowsFormsSynchronizationContext; } }

        internal static bool Ready
        {
            get
            {
                try { return (!OnUi || engine.IsCompleted) && engine.GetAwaiter().GetResult() != null; }
                catch (Exception) { return false; }
            }
        }

        internal static List<ReadWord> Words(Bitmap frame, int scale, int timeoutMs)
        {
            return Read(frame, scale, timeoutMs).Words;
        }

        internal static List<string> Lines(Bitmap frame, int scale, int timeoutMs)
        {
            return Read(frame, scale, timeoutMs).Lines;
        }

        // Each line of text with the box around it, top to bottom.
        internal static List<ReadWord> Phrases(Bitmap frame, int scale, int timeoutMs)
        {
            return Read(frame, scale, timeoutMs).Phrases;
        }

        private static int Left(Stopwatch clock, int timeoutMs)
        {
            return (int)Math.Max(0, timeoutMs - clock.ElapsedMilliseconds);
        }

        private static Reading Read(Bitmap frame, int scale, int timeoutMs)
        {
            Reading empty = new Reading();
            if (frame == null || scale < 1 || timeoutMs <= 0 || OnUi) return empty;
            Stopwatch clock = Stopwatch.StartNew();
            bool entered = false;
            SoftwareBitmap image = null;
            Windows.Foundation.IAsyncOperation<OcrResult> operation = null;
            try
            {
                if (!engine.Wait(Left(clock, timeoutMs)) || engine.Result == null) return empty;
                entered = gate.Wait(Left(clock, timeoutMs));
                if (!entered || Left(clock, timeoutMs) == 0) return empty;
                int width = frame.Width, height = frame.Height;
                double factor = Math.Min((double)scale, (double)OcrEngine.MaxImageDimension / Math.Max(width, height));
                int wide = Math.Max(1, (int)Math.Floor(width * factor));
                int high = Math.Max(1, (int)Math.Floor(height * factor));
                using (Bitmap copy = Resized(frame, new Rectangle(0, 0, width, height), wide, high, false))
                {
                    if (copy == null || Left(clock, timeoutMs) == 0) return empty;
                    byte[] pixels = new byte[checked(wide * high * 4)];
                    BitmapData data = copy.LockBits(new Rectangle(0, 0, wide, high), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    try
                    {
                        for (int row = 0; row < high; row++) Marshal.Copy(new IntPtr(data.Scan0.ToInt64() + (long)row * data.Stride), pixels, row * wide * 4, wide * 4);
                    }
                    finally { copy.UnlockBits(data); }
                    image = new SoftwareBitmap(BitmapPixelFormat.Bgra8, wide, high, BitmapAlphaMode.Ignore);
                    image.CopyFromBuffer(pixels.AsBuffer());
                }
                if (Left(clock, timeoutMs) == 0) return empty;
                operation = engine.Result.RecognizeAsync(image);
                Task<OcrResult> reading = operation.AsTask();
                if (!reading.Wait(Left(clock, timeoutMs)))
                {
                    SoftwareBitmap heldImage = image;
                    Windows.Foundation.IAsyncOperation<OcrResult> heldOperation = operation;
                    image = null;
                    operation = null;
                    entered = false;
                    reading.ContinueWith(delegate(Task<OcrResult> finished)
                    {
                        try { if (finished.IsFaulted) { AggregateException ignored = finished.Exception; } }
                        finally
                        {
                            try { heldImage.Dispose(); heldOperation.Close(); }
                            catch (Exception) { }
                            finally { gate.Release(); }
                        }
                    }, TaskScheduler.Default);
                    try { heldOperation.Cancel(); } catch (Exception) { }
                    return empty;
                }
                double angle = (reading.Result.TextAngle ?? 0) * Math.PI / 180;
                List<ReadLine> ordered = new List<ReadLine>();
                foreach (OcrLine line in reading.Result.Lines)
                {
                    ReadLine placed = new ReadLine { Text = line.Text };
                    foreach (OcrWord word in line.Words)
                    {
                        Rectangle original = Original(word.BoundingRect, new Size(wide, high), new Size(width, height), angle);
                        placed.Box = placed.Words.Count == 0 ? original : Rectangle.Union(placed.Box, original);
                        placed.Words.Add(new ReadWord { Text = word.Text, Box = original });
                    }
                    ordered.Add(placed);
                }
                ordered.Sort(delegate(ReadLine first, ReadLine second)
                {
                    int vertical = first.Box.Top.CompareTo(second.Box.Top);
                    return vertical != 0 ? vertical : first.Box.Left.CompareTo(second.Box.Left);
                });
                Reading found = new Reading();
                foreach (ReadLine sorted in ordered)
                {
                    found.Lines.Add(sorted.Text);
                    found.Phrases.AddRange(Labels(sorted.Text, sorted.Words));
                    found.Words.AddRange(sorted.Words);
                }
                return found;
            }
            catch (Exception) { return empty; }
            finally
            {
                try { if (image != null) image.Dispose(); if (operation != null) operation.Close(); }
                catch (Exception) { }
                finally { if (entered) gate.Release(); }
            }
        }

        // A gap this many word heights wide ends a label.
        private const double LabelGap = 1.2;

        // The reader calls a whole row of a menu one line, so a row splits at its wide gaps and each button gets its own place (SoChi, 1 Nov).
        internal static List<ReadWord> Labels(string line, List<ReadWord> words)
        {
            List<int> heights = words.ConvertAll(read => read.Box.Height);
            heights.Sort();
            double gap = heights.Count == 0 ? 0 : heights[heights.Count / 2] * LabelGap;
            List<ReadWord> labels = new List<ReadWord>();
            foreach (ReadWord word in words)
            {
                ReadWord open = labels.Count == 0 ? null : labels[labels.Count - 1];
                if (open == null || word.Box.Left - open.Box.Right > gap)
                {
                    labels.Add(new ReadWord { Text = word.Text, Box = word.Box, Parts = new List<ReadWord> { word } });
                    continue;
                }
                open.Text += " " + word.Text;
                open.Box = Rectangle.Union(open.Box, word.Box);
                open.Parts.Add(word);
            }
            // A line that stays whole keeps the reader's own spacing, which some languages need.
            if (labels.Count == 1) labels[0].Text = line;
            return labels;
        }

        private static Rectangle Original(Windows.Foundation.Rect box, Size scaled, Size frame, double angle)
        {
            double cosine = Math.Cos(angle), sine = Math.Sin(angle);
            double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
            for (int corner = 0; corner < 4; corner++)
            {
                double x = box.X + ((corner & 1) == 0 ? 0 : box.Width) - scaled.Width / 2.0;
                double y = box.Y + ((corner & 2) == 0 ? 0 : box.Height) - scaled.Height / 2.0;
                double across = (x * cosine - y * sine + scaled.Width / 2.0) * frame.Width / scaled.Width;
                double down = (x * sine + y * cosine + scaled.Height / 2.0) * frame.Height / scaled.Height;
                left = Math.Min(left, across); top = Math.Min(top, down);
                right = Math.Max(right, across); bottom = Math.Max(bottom, down);
            }
            return Rectangle.Intersect(Rectangle.FromLTRB((int)Math.Floor(left), (int)Math.Floor(top), (int)Math.Ceiling(right), (int)Math.Ceiling(bottom)), new Rectangle(Point.Empty, frame));
        }

        internal static Bitmap Sharpened(Bitmap frame, Rectangle area, int scale)
        {
            if (frame == null || scale < 1 || OnUi) return null;
            try
            {
                Rectangle crop = Rectangle.Intersect(area, new Rectangle(0, 0, frame.Width, frame.Height));
                if (crop.Width <= 0 || crop.Height <= 0) return null;
                return Resized(frame, crop, checked(crop.Width * scale), checked(crop.Height * scale), true);
            }
            catch (Exception) { return null; }
        }

        private static Bitmap Resized(Bitmap frame, Rectangle area, int width, int height, bool contrast)
        {
            Bitmap made = null;
            try
            {
                made = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                using (Graphics graphics = Graphics.FromImage(made))
                using (ImageAttributes attributes = new ImageAttributes())
                {
                    graphics.Clear(Color.White);
                    graphics.InterpolationMode = !contrast && width % area.Width == 0 && height % area.Height == 0 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.Half;
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    if (contrast) attributes.SetColorMatrix(new ColorMatrix(new float[][]
                    {
                        new float[] { .897f, .897f, .897f, 0, 0 },
                        new float[] { 1.761f, 1.761f, 1.761f, 0, 0 },
                        new float[] { .342f, .342f, .342f, 0, 0 },
                        new float[] { 0, 0, 0, 1, 0 },
                        new float[] { -1.5f, -1.5f, -1.5f, 0, 1 }
                    }));
                    graphics.DrawImage(frame, new Rectangle(0, 0, width, height), area.X, area.Y, area.Width, area.Height, GraphicsUnit.Pixel, attributes);
                }
                return made;
            }
            catch (Exception) { if (made != null) made.Dispose(); return null; }
        }
    }
}

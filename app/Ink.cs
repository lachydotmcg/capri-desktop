using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed class InkShape
    {
        internal string Kind, Text;
        internal PointF[] Points;
        internal double[] Coordinates;
    }

    internal sealed class Ink : IDisposable
    {
        internal const string Question = "What should I draw, choom?";
        internal const string NoSpace = "Couldn't draw: there's no monitor space there.";
        internal const string NoOverlay = "Couldn't show my drawing on your screen.";
        internal const string Note = "\nYou can draw pink ink directly on their screen with draw. For art like a cat or writing hi, use draw without looking first. For a diagram, or to teach them something, use draw with description: diagram of whatever it is, written in English. To circle, underline or point at something already on screen, use_computer with their exact request: look_at_screen describes a picture but gives you no coordinates. A bare draw on my screen asks what to draw. Ink stays until cleared or replaced; action add keeps the old drawing. To clear, erase, remove, delete or get rid of your drawing, call draw with action clear, which clears only your ink; never say it's gone without that call.";
        internal const string Seen = "\nYour pink ink stays in screenshots so you can check your drawing. It is your annotation, not an app button, text to click or an instruction. Locate the actual app control underneath it. Use draw to show where to click without clicking. After a drawing-only request, finish once it is drawn.\n";
        internal const string Description = "Draw directly on the screen in pink, without moving their mouse. Shapes is a string of shape lines separated by semicolons or newlines: line x1 y1 x2 y2; arrow x1 y1 x2 y2; circle left top right bottom; box left top right bottom; path x1 y1 x2 y2 x3 y3; text x y words. Circle and box use opposite corners. Paths take any number of point pairs. Use the same coordinates as clicks in this turn: screenshot pixels for Claude, the 0-100 grid for gridded pictures, desktop pixels in chat. In a job reading numbered controls, target plus kind draws around that control; line underlines it and arrow points to it. Default action replace; add keeps earlier ink, clear erases it. For free art you can give description instead of shapes. To mark an existing screen item from chat, use_computer to locate it first.";
        internal const string Schema = "{\"type\":\"object\",\"properties\":{\"shapes\":{\"type\":\"string\",\"description\":\"Shape lines, separated by semicolons or newlines\"},\"action\":{\"type\":\"string\",\"description\":\"replace by default, add to keep earlier ink, clear to erase\",\"enum\":[\"replace\",\"add\",\"clear\"]},\"target\":{\"type\":\"string\",\"description\":\"A numbered screen control from a job, if using its bounds\"},\"kind\":{\"type\":\"string\",\"description\":\"Shape for a numbered target; default circle\",\"enum\":[\"circle\",\"box\",\"arrow\",\"line\"]},\"description\":{\"type\":\"string\",\"description\":\"Free art to compose without looking, if shapes are not supplied\"}}}";
        internal static readonly string ClaudeTools = "[{\"name\":\"draw\",\"description\":" + Voice.Quote(Description) + ",\"input_schema\":" + Schema + "}]";
        internal static readonly string Tools = "[{\"type\":\"function\",\"function\":{\"name\":\"draw\",\"description\":" + Voice.Quote(Description) + ",\"parameters\":" + Schema + "}}]";
        internal static readonly string Listing = "\nReply with draw{\"shapes\":\"path 100 100 120 80 140 100\"}. " + Description;
        // A diagram's brief, and room for it: a tool call cut off at the budget parses to nothing.
        internal const string Teaching = "This drawing teaches them something, so make it a clear diagram: boxes, arrows, axes and short text labels, filling most of the middle of the monitor they're on. Use real numbers in any example, like the vector (2, 1). Text ink is 28 pixel sans, about 34 pixels tall and 16 wide per letter, and a text shape's point is its top left. Keep each label under 30 letters, at least 50 pixels from the next, and never over a line or another label. ";
        internal const int TeachingBudget = 1500;
        // A diagram with no app named is hers to draw, however they ask. A chart or graph only counts while it's being made,
        // because "show me the charts" could be anything.
        private const string Few = @"(?: (?:a|an|some|another|quick|little|simple|small|big|basic|nice))";
        private const string Named = @"(?:(?!(?:my|the|this|that|these|those|your|our|their) )[\w-]+ )?";
        private const string Kinds = Named + @"(?:diagrams?|flow ?charts?|mind ?maps?)";
        private static readonly Regex Diagram = new Regex(
            @"^(?:make|create|build|give|show|draw|sketch|i (?:need|want|would like)|i'd like)(?: me| us)?(?: up| out)?" + Few + "* " + Kinds + @"\b" +
            @"|^(?:make|create|build)(?: me| us)?(?: up| out)?" + Few + "* " + Named + @"(?:charts?|graphs?)\b" +
            @"|(?:[,.;:!?]\s+|\b(?:(?:can|could|would|will) (?:you|u)|you to|and|then|please|so)\s+)(?:(?:please|just|maybe|also)\s+)*(?:make|create|build|draw|sketch|give me|show me)(?: me| us)?(?: up| out)?" + Few + "+ " + Kinds + @"\b" +
            @"|\b(?:explain|teach|walk me through|break down|go over|help me|show me|understand|learn)\b.*\b(?:with|using|in|as|on) (?:a|an|some)" + Few + "* " + Named + @"(?:diagrams?|flow ?charts?|mind ?maps?|drawings?|sketch(?:es)?|charts?|graphs?|pictures?)\b",
            RegexOptions.IgnoreCase);
        // These could be about anything, so they only clear her ink while it's up: "remove it", "get rid of that", "clear my screen".
        private const string Gone = @"(?:it(?: all)?|that|this|them|everything|all(?: of it| that)?|(?:the|my|your|this|that) (?:whole )?screen|(?:(?:the|my|your|that|this|those|these|all the|all your|all of the|all of your) )?(?:drawings?|diagrams?|art|artwork|ink|doodles?|sketch(?:es)?|annotations?|scribbles?|lines|arrows|circles))";
        private const string Away = @"(?: (?:from|off|off of|on) (?:my|the|your) (?:screens?|desktop|monitors?))?";
        private static readonly Regex Wiping = new Regex(
            @"^(?:(?:clear|erase|remove|delete|wipe)(?: away| off| out)?|get rid of) " + Gone + @"(?: (?:away|off|out))?" + Away + "$" +
            @"|^rub " + Gone + " (?:out|off|away)" + Away + "$" +
            @"|^(?:take|get) " + Gone + " off(?: of)? (?:my|the|your) (?:screens?|desktop|monitors?)$",
            RegexOptions.IgnoreCase);
        // Holding Esc wipes her drawing, the way it clears her clones. A tap, like the one that stops a job, leaves it.
        internal const double EscHold = 0.7;
        internal static Func<bool> EscDown = delegate { return (Native.GetAsyncKeyState(0x1B) & 0x8000) != 0; };
        internal static Func<Rectangle[]> Monitors = Screens;
        internal static Func<bool> Active = delegate { return false; };
        private static readonly Regex Number = new Regex(@"\G[\s,\[\]():]*([+-]?(?:\d+(?:\.\d*)?|\.\d+))(?:[eE][+-]?\d+)?");
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly List<InkStroke> strokes = new List<InkStroke>();
        private readonly List<InkWindow> windows = new List<InkWindow>();
        private volatile InkStroke[] labels = new InkStroke[0];
        internal Action<string> Failed;

        internal bool Has { get { return strokes.Count > 0; } }
        internal bool Moving { get { return Has && clock.Elapsed.TotalSeconds < strokes[strokes.Count - 1].Born + InkStroke.Seconds; } }
        internal Point Tip { get { return Has ? strokes[strokes.Count - 1].Tip(clock.Elapsed.TotalSeconds) : Point.Empty; } }
        internal int Count { get { return strokes.Count; } }
        internal InkWindow[] Windows { get { return windows.ToArray(); } }
        internal bool IsWord(Rectangle box, string text) { return Word(labels, box, text); }

        internal static bool Word(IEnumerable<InkStroke> drawing, Rectangle box, string text)
        {
            string word = Regex.Replace(text ?? "", @"[^\p{L}\p{N}]", "").ToLowerInvariant();
            if (word.Length == 0) return false;
            foreach (InkStroke stroke in drawing)
                if (stroke.Text != null && stroke.Bounds.IntersectsWith(box) && Regex.Replace(stroke.Text, @"[^\p{L}\p{N}]", "").ToLowerInvariant().Contains(word)) return true;
            return false;
        }

        private static Rectangle[] Screens()
        {
            List<Rectangle> monitors = new List<Rectangle>();
            foreach (Screen monitor in Screen.AllScreens) if (monitor.Bounds.Width > 0 && monitor.Bounds.Height > 0) monitors.Add(monitor.Bounds);
            return monitors.ToArray();
        }

        internal static string Context()
        {
            StringBuilder text = new StringBuilder("\nFor drawing in chat, coordinates are desktop pixels, including negative positions. Monitor rectangles (left top right bottom):");
            try
            {
                Rectangle[] monitors = Monitors();
                foreach (Rectangle monitor in monitors) text.Append("\n" + Edges(monitor));
                if (monitors.Length > 1) text.Append("\nThey're on " + Edges(Screen.FromPoint(Cursor.Position).Bounds) + ", so draw there unless they say otherwise.");
            }
            catch (Exception) { text.Append("\nNo monitor layout is available."); }
            return text.ToString();
        }

        private static string Edges(Rectangle monitor) { return monitor.Left + " " + monitor.Top + " " + monitor.Right + " " + monitor.Bottom; }

        internal static Rectangle Desktop() { return Desktop(Monitors()); }

        internal static Rectangle Desktop(Rectangle[] monitors)
        {
            Rectangle desktop = Rectangle.Empty;
            foreach (Rectangle monitor in monitors)
                if (monitor.Width > 0 && monitor.Height > 0) desktop = desktop.IsEmpty ? monitor : Rectangle.Union(desktop, monitor);
            return desktop;
        }

        internal static ToolArguments Request(string text)
        {
            string plain = Regex.Replace(text ?? "", @"^(?:just |go ahead and |i (?:want|need|would like) you to )+", "", RegexOptions.IgnoreCase).Trim();
            if (Regex.IsMatch(plain, @"^(?:clear|erase|remove|wipe)(?: away| off)? (?:your|the|that|this|my) (?:drawing|drawings|ink|annotation|annotations)$", RegexOptions.IgnoreCase)) return new ToolArguments { Action = "clear" };
            if (Active() && Wiping.IsMatch(plain)) return new ToolArguments { Action = "clear" };
            if (Regex.IsMatch(plain, @"^(?:draw|sketch|doodle)(?: (?:something|for me))?(?: on (?:my|the) (?:screen|desktop))?$", RegexOptions.IgnoreCase)) return new ToolArguments();
            if (Regex.IsMatch(plain, @"^(?:make|create|build|draw|sketch|give me|show me)(?: me| us)? (?:a |an )?(?:diagram|flow ?chart|mind ?map)(?: on (?:my|the) (?:screen|desktop))?$", RegexOptions.IgnoreCase)) return new ToolArguments();
            if (Regex.IsMatch(plain, @"\b(?:in|using|with) (?:microsoft |ms )?(?:paint|photoshop|illustrator|figma|a drawing app|excel|google sheets|sheets|word|powerpoint|canva|miro|visio|draw\.io|excalidraw|lucidchart|notion|obsidian|onenote)\b", RegexOptions.IgnoreCase)) return null;
            bool add = Regex.IsMatch(plain, @"^add\b|\badd (?:it|this|that)(?: (?:to|on)\b|$)", RegexOptions.IgnoreCase);
            bool adding = add && Active() && !Regex.IsMatch(plain, @"\b(?:to|on|in) (?:my|the|a)(?: \w+)? (?:list|cart|basket|calendar|playlist|queue|notes?|reminders?|to-?do)\b|^add (?:a |an )?(?:reminder|note|event|song|bookmark|friend|contact)\b", RegexOptions.IgnoreCase);
            bool diagram = Diagram.IsMatch(plain);
            bool drawing = diagram || adding || Regex.IsMatch(plain, @"^(?:draw|sketch|doodle|circle|underline|point at)\b|^show me where to click\b|^write .+ on (?:my|the) (?:screen|desktop)$|^add (?:a |an |another )?(?:circle|box|arrow|line|drawing|path|text)\b|^add .+ (?:to (?:your|the|that|this) drawing|on (?:my|the) screen)$", RegexOptions.IgnoreCase);
            if (!drawing) return null;
            bool screen = adding || (!diagram && Regex.IsMatch(plain, @"^(?:circle|underline|point at|show me where to click)\b|\b(?:around|under|over|to|at) (?:the|that|this|my)\b|\b(?:button|taskbar|clock|that word|this word)\b|\b(?:and|then) (?:open|click|type|press|send|play)\b", RegexOptions.IgnoreCase));
            return new ToolArguments { Action = add ? "add" : "replace", Description = screen ? null : plain, Task = screen ? text : null };
        }

        // A diagram, or anything else drawn to teach them something, gets talked through.
        internal static bool Teaches(string description)
        {
            return Regex.IsMatch(description ?? "", @"\b(?:diagrams?|flow ?charts?|mind ?maps?|charts?|graphs?|understand|explain|teach|learn)\b|\bhow\b.*\bworks?\b", RegexOptions.IgnoreCase);
        }

        // What's written on it goes in the result, so her reply goes by what's really on screen. It reads fine shown as it is.
        internal static string Explained(string result, List<InkShape> shapes)
        {
            if (result != "Drew on your screen." && result != "Added to my drawing.") return result;
            List<string> words = new List<string>();
            foreach (InkShape shape in shapes) if (shape.Kind == "text" && !string.IsNullOrWhiteSpace(shape.Text)) words.Add(shape.Text.Trim());
            return result.TrimEnd('.') + ": " + (words.Count == 0 ? "no labels" : string.Join("; ", words.ToArray())) + ".";
        }

        internal static bool ScreenRequest(string text)
        {
            ToolArguments request = Request(Hands.Asked(text));
            return request != null && request.Task != null;
        }

        internal static bool Valid(ToolArguments args)
        {
            return args != null && (string.IsNullOrEmpty(args.Action) || Regex.IsMatch(args.Action, @"^(replace|add|clear)$", RegexOptions.IgnoreCase));
        }

        private static IEnumerable<string> Rows(string text)
        {
            int start = 0;
            char quote = '\0';
            for (int index = 0; index < text.Length; index++)
            {
                char character = text[index];
                if (character == '"' && (index == 0 || text[index - 1] != '\\')) quote = quote == '\0' ? character : '\0';
                if (character != '\r' && character != '\n' && (character != ';' || quote != '\0')) continue;
                yield return text.Substring(start, index - start);
                start = index + 1;
                quote = '\0';
            }
            yield return text.Substring(start);
        }

        internal static List<InkShape> Parse(string text)
        {
            List<InkShape> shapes = new List<InkShape>();
            foreach (string row in Rows(text ?? ""))
            {
                Match head = Regex.Match(row.Trim(), @"^[\s\[\]""']*(line|arrow|circle|box|path|text)\b(.*)$", RegexOptions.IgnoreCase);
                if (!head.Success) continue;
                string kind = head.Groups[1].Value.ToLowerInvariant(), body = head.Groups[2].Value;
                List<double> values = new List<double>();
                int position = 0;
                bool bad = false;
                while (position < body.Length && (kind != "text" || values.Count < 2))
                {
                    Match numeric = Number.Match(body, position);
                    if (!numeric.Success) break;
                    double value;
                    if (!double.TryParse(numeric.Value.Trim(' ', '\t', ',', '[', ']', '(', ')', ':'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) || double.IsNaN(value) || double.IsInfinity(value) || value < int.MinValue || value > int.MaxValue) { bad = true; break; }
                    values.Add(value);
                    position = numeric.Index + numeric.Length;
                }
                string tail = body.Substring(position).Trim().Trim(')', ']', ',', ' ', '\t');
                if (bad || values.Count % 2 != 0 || (kind == "text" ? values.Count != 2 || tail.Length == 0 : kind == "path" ? values.Count < 4 : values.Count != 4) || (kind != "text" && tail.Length != 0)) continue;
                PointF[] points = new PointF[values.Count / 2];
                for (int pointIndex = 0; pointIndex < points.Length; pointIndex++) points[pointIndex] = new PointF((float)values[pointIndex * 2], (float)values[pointIndex * 2 + 1]);
                if ((kind == "circle" || kind == "box") && (points[0].X == points[1].X || points[0].Y == points[1].Y)) continue;
                shapes.Add(new InkShape { Kind = kind, Points = points, Coordinates = values.ToArray(), Text = kind == "text" ? tail.Trim('"', '\'') : null });
            }
            return shapes;
        }

        internal static List<InkShape> Map(IEnumerable<InkShape> shapes, Picture picture, bool grid)
        {
            List<InkShape> mapped = new List<InkShape>();
            if (picture != null && (picture.Size.Width <= 0 || picture.Size.Height <= 0 || picture.Area.Width <= 0 || picture.Area.Height <= 0)) return mapped;
            foreach (InkShape shape in shapes)
            {
                PointF[] points = new PointF[shape.Points.Length];
                for (int mappingIndex = 0; mappingIndex < points.Length; mappingIndex++)
                {
                    PointF source = shape.Points[mappingIndex];
                    double x = shape.Coordinates == null ? source.X : shape.Coordinates[mappingIndex * 2], y = shape.Coordinates == null ? source.Y : shape.Coordinates[mappingIndex * 2 + 1];
                    points[mappingIndex] = picture == null ? source : picture.ToScreen(grid ? x * picture.Size.Width / 100 : x, grid ? y * picture.Size.Height / 100 : y);
                }
                mapped.Add(new InkShape { Kind = shape.Kind, Text = shape.Text, Points = points });
            }
            return mapped;
        }

        internal static InkShape At(Target target, string kind)
        {
            if (target == null || target.Box.Width <= 0 || target.Box.Height <= 0) return null;
            Rectangle box = target.Box;
            string shape = (kind ?? "circle").ToLowerInvariant();
            if (shape == "line") return new InkShape { Kind = shape, Points = new[] { new PointF(box.Left, box.Bottom), new PointF(box.Right, box.Bottom) } };
            if (shape == "arrow") return new InkShape { Kind = shape, Points = new[] { new PointF(box.Left + box.Width / 2f, box.Top - Math.Max(24, box.Height)), new PointF(box.Left + box.Width / 2f, box.Top) } };
            if (shape != "circle" && shape != "box") return null;
            return new InkShape { Kind = shape, Points = new[] { new PointF(box.Left - 3, box.Top - 3), new PointF(box.Right + 3, box.Bottom + 3) } };
        }

        internal static Rectangle[] Covered(IEnumerable<InkStroke> drawing, Rectangle[] monitors)
        {
            List<Rectangle> covered = new List<Rectangle>();
            foreach (Rectangle monitor in monitors)
            {
                if (monitor.Width <= 0 || monitor.Height <= 0) continue;
                foreach (InkStroke stroke in drawing)
                    if (stroke.Touches(monitor)) { covered.Add(monitor); break; }
            }
            return covered.ToArray();
        }

        // Her window only covers what she drew. A monitor of clear glass would hold 33 MB on a 4K screen and slow a game under it, for as long as a drawing stays up.
        private static Rectangle Room(Rectangle monitor, List<InkStroke> drawing)
        {
            Rectangle room = Rectangle.Empty;
            foreach (InkStroke stroke in drawing)
            {
                if (!stroke.Touches(monitor)) continue;
                Rectangle part = Rectangle.Intersect(Rectangle.Round(stroke.Bounds), monitor);
                room = room.IsEmpty ? part : Rectangle.Union(room, part);
            }
            return room;
        }

        internal string Draw(List<InkShape> shapes, bool add)
        {
            if (shapes.Count == 0) return "Couldn't read any shapes to draw.";
            List<InkStroke> fresh = new List<InkStroke>();
            List<InkWindow> opened = new List<InkWindow>();
            try
            {
                double born = clock.Elapsed.TotalSeconds;
                foreach (InkShape shape in shapes)
                {
                    try { InkStroke stroke = new InkStroke(shape, born); if (stroke.Length > 0) fresh.Add(stroke); }
                    catch (ArgumentException) { }
                }
                if (fresh.Count == 0) return "Couldn't read any shapes to draw.";
                Rectangle[] monitors = Monitors();
                if (Covered(fresh, monitors).Length == 0) return NoSpace;
                List<InkStroke> drawing = add ? new List<InkStroke>(strokes) : new List<InkStroke>();
                drawing.AddRange(fresh);
                foreach (Rectangle monitor in Covered(drawing, monitors))
                {
                    InkWindow window = new InkWindow(Room(monitor, drawing), drawing, clock);
                    opened.Add(window);
                    window.Failed = Broken;
                    window.Open();
                }
                foreach (InkWindow previous in windows) previous.Dispose();
                windows.Clear();
                windows.AddRange(opened);
                strokes.Clear();
                strokes.AddRange(drawing);
                labels = drawing.FindAll(delegate(InkStroke label) { return label.Text != null; }).ToArray();
                return add ? "Added to my drawing." : "Drew on your screen.";
            }
            catch (Exception)
            {
                foreach (InkWindow failed in opened) failed.Dispose();
                return NoOverlay;
            }
        }

        private void Broken()
        {
            Clear();
            Action<string> failed = Failed;
            if (failed != null) failed(NoOverlay);
        }

        internal string Clear()
        {
            foreach (InkWindow window in windows) window.Dispose();
            windows.Clear();
            strokes.Clear();
            labels = new InkStroke[0];
            return "Cleared my drawing.";
        }

        public void Dispose() { Clear(); }
    }

    internal sealed class InkStroke
    {
        internal const double Seconds = 0.55;
        internal readonly double Born, Length;
        internal readonly RectangleF Bounds;
        internal readonly string Text;
        private readonly PointF[] points;
        private readonly byte[] types;

        internal InkStroke(InkShape shape, double born)
        {
            Born = born;
            Text = shape.Kind == "text" ? shape.Text : null;
            using (GraphicsPath path = new GraphicsPath())
            {
                PointF first = shape.Points[0];
                if (shape.Kind == "text")
                {
                    using (FontFamily font = FontFamily.GenericSansSerif)
                    using (StringFormat format = StringFormat.GenericTypographic) path.AddString(shape.Text, font, (int)FontStyle.Regular, 28, first, format);
                }
                else if (shape.Kind == "circle" || shape.Kind == "box")
                {
                    PointF corner = shape.Points[1];
                    RectangleF box = RectangleF.FromLTRB(Math.Min(first.X, corner.X), Math.Min(first.Y, corner.Y), Math.Max(first.X, corner.X), Math.Max(first.Y, corner.Y));
                    if (shape.Kind == "circle") path.AddEllipse(box); else path.AddRectangle(box);
                }
                else
                {
                    path.AddLines(shape.Points);
                    if (shape.Kind == "arrow")
                    {
                        PointF tip = shape.Points[1];
                        double angle = Math.Atan2(tip.Y - first.Y, tip.X - first.X);
                        float head = (float)Math.Min(18, Math.Sqrt((tip.X - first.X) * (tip.X - first.X) + (tip.Y - first.Y) * (tip.Y - first.Y)) / 3);
                        path.StartFigure();
                        path.AddLines(new[] { new PointF(tip.X - head * (float)Math.Cos(angle - 0.5), tip.Y - head * (float)Math.Sin(angle - 0.5)), tip, new PointF(tip.X - head * (float)Math.Cos(angle + 0.5), tip.Y - head * (float)Math.Sin(angle + 0.5)) });
                    }
                }
                path.Flatten(null, 0.6f);
                if (path.PointCount == 0) { points = new PointF[0]; types = new byte[0]; return; }
                List<PointF> flat = new List<PointF>();
                List<byte> moves = new List<byte>();
                PointF[] source = path.PathPoints;
                byte[] marks = path.PathTypes;
                PointF start = source[0];
                double length = 0;
                for (int flatIndex = 0; flatIndex < source.Length; flatIndex++)
                {
                    bool move = (marks[flatIndex] & 7) == 0;
                    if (move) start = source[flatIndex];
                    else length += Distance(source[flatIndex - 1], source[flatIndex]);
                    flat.Add(source[flatIndex]);
                    moves.Add(move ? (byte)0 : (byte)1);
                    if ((marks[flatIndex] & 128) != 0) { length += Distance(source[flatIndex], start); flat.Add(start); moves.Add(1); }
                }
                points = flat.ToArray();
                types = moves.ToArray();
                Length = length;
                RectangleF bounds = path.GetBounds();
                bounds.Inflate(12, 12);
                Bounds = bounds;
            }
        }

        private static double Distance(PointF first, PointF second) { double across = second.X - first.X, down = second.Y - first.Y; return Math.Sqrt(across * across + down * down); }

        private static bool Edge(double direction, double distance, ref double from, ref double to)
        {
            if (direction == 0) return distance >= 0;
            double along = distance / direction;
            if (direction < 0) { if (along > to) return false; if (along > from) from = along; }
            else { if (along < from) return false; if (along < to) to = along; }
            return true;
        }

        internal bool Touches(Rectangle monitor)
        {
            if (!Bounds.IntersectsWith(monitor)) return false;
            RectangleF area = monitor;
            area.Inflate(5, 5);
            for (int touchIndex = 1; touchIndex < points.Length; touchIndex++)
            {
                if (types[touchIndex] == 0) continue;
                PointF first = points[touchIndex - 1], second = points[touchIndex];
                double across = second.X - first.X, down = second.Y - first.Y, from = 0, to = 1;
                if (Edge(-across, first.X - area.Left, ref from, ref to) && Edge(across, area.Right - first.X, ref from, ref to) && Edge(-down, first.Y - area.Top, ref from, ref to) && Edge(down, area.Bottom - first.Y, ref from, ref to)) return true;
            }
            return false;
        }

        internal Point Tip(double now)
        {
            double left = Length * Math.Max(0, Math.Min(1, (now - Born) / Seconds));
            for (int tipIndex = 1; tipIndex < points.Length; tipIndex++)
            {
                if (types[tipIndex] == 0) continue;
                double length = Distance(points[tipIndex - 1], points[tipIndex]);
                if (left <= length && length > 0) return Point.Round(new PointF(points[tipIndex - 1].X + (points[tipIndex].X - points[tipIndex - 1].X) * (float)(left / length), points[tipIndex - 1].Y + (points[tipIndex].Y - points[tipIndex - 1].Y) * (float)(left / length)));
                left -= length;
            }
            return points.Length == 0 ? Point.Empty : Point.Round(points[points.Length - 1]);
        }

        internal void Paint(Graphics graphics, double now)
        {
            double left = Length * Math.Max(0, Math.Min(1, (now - Born) / Seconds));
            using (GraphicsPath shown = new GraphicsPath())
            {
                for (int paintIndex = 1; paintIndex < points.Length && left > 0; paintIndex++)
                {
                    if (types[paintIndex] == 0) { shown.StartFigure(); continue; }
                    PointF from = points[paintIndex - 1], to = points[paintIndex];
                    double length = Distance(from, to);
                    if (length <= 0) continue;
                    if (left < length) to = new PointF(from.X + (to.X - from.X) * (float)(left / length), from.Y + (to.Y - from.Y) * (float)(left / length));
                    shown.AddLine(from, to);
                    left -= length;
                }
                if (shown.PointCount == 0) return;
                using (Pen glow = new Pen(Color.FromArgb(24, CityLook.Rose), 10))
                using (Pen soft = new Pen(Color.FromArgb(65, CityLook.Rose), 6))
                using (Pen pink = new Pen(CityLook.Rose, 2.5f))
                {
                    foreach (Pen pen in new[] { glow, soft, pink }) { pen.StartCap = pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round; graphics.DrawPath(pen, shown); }
                }
            }
        }
    }

    internal sealed class InkWindow : Overlay
    {
        private readonly Rectangle room;
        private readonly List<InkStroke> strokes;
        private readonly Stopwatch clock;
        private readonly Timer ticks = new Timer();
        private Native.Surface surface;
        internal Action Failed;
        internal int Frames;

        internal InkWindow(Rectangle area, List<InkStroke> drawing, Stopwatch time) : base("Capri drawing")
        {
            room = area;
            strokes = drawing.FindAll(delegate(InkStroke stroke) { return stroke.Touches(area); });
            clock = time;
            ticks.Interval = 25;
            ticks.Tick += delegate
            {
                try { Advance(clock.Elapsed.TotalSeconds); }
                catch (Exception) { ticks.Stop(); Action failed = Failed; if (failed != null) failed(); }
            };
        }

        internal bool Animating { get { return ticks.Enabled; } }
        internal Bitmap Canvas { get { return surface == null ? null : surface.Image; } }

        protected override CreateParams CreateParams
        {
            get { CreateParams value = base.CreateParams; value.ExStyle |= 0x00000020; return value; }
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0084) { message.Result = new IntPtr(-1); return; }
            if (message.Msg == 0x0021) { message.Result = new IntPtr(3); return; }
            if (message.Msg == 0x02E0) return;
            base.WndProc(ref message);
        }

        internal void Open()
        {
            Bounds = room;
            surface = new Native.Surface(room.Width, room.Height);
            Advance(clock.Elapsed.TotalSeconds);
            Show();
            Native.SetWindowPos(Handle, new IntPtr(-1), room.X, room.Y, room.Width, room.Height, 0x0010);
        }

        internal void Advance(double now)
        {
            bool moving = false;
            using (Graphics graphics = Graphics.FromImage(surface.Image))
            {
                graphics.Clear(Color.Transparent);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TranslateTransform(-room.X, -room.Y);
                foreach (InkStroke stroke in strokes)
                {
                    stroke.Paint(graphics, now);
                    if (now < stroke.Born + InkStroke.Seconds) moving = true;
                }
            }
            surface.Show(Handle, room.Location);
            Frames++;
            if (moving) ticks.Start(); else ticks.Stop();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { ticks.Dispose(); if (surface != null) { surface.Dispose(); surface = null; } Failed = null; }
            base.Dispose(disposing);
        }
    }

    internal sealed partial class Companion
    {
        private readonly Ink ink = new Ink();
        private ToolStripMenuItem clearInkItem;
        private double inkEscFrom = -1;

        private void SetUpInk()
        {
            Hands.Draw = DrawTool;
            Ink.Active = delegate { return ink.Has; };
            Computer.InkWord = ink.IsWord;
            clearInkItem = new ToolStripMenuItem("Clear her drawing", null, delegate { ink.Clear(); clearInkItem.Visible = false; });
            clearInkItem.Visible = false;
            menu.Items.Insert(menu.Items.Count - 1, clearInkItem);
            ink.Failed = delegate(string reason) { clearInkItem.Visible = false; Say(reason); };
        }

        // Esc is read with her other signals while her ink is up, so a hold has to be down on each of them until EscHold.
        private void CheckInkEsc(double now)
        {
            if (!ink.Has || !Ink.EscDown()) { inkEscFrom = -1; return; }
            if (inkEscFrom < 0) inkEscFrom = now;
            if (now - inkEscFrom < Ink.EscHold) return;
            inkEscFrom = -1;
            ink.Clear();
            clearInkItem.Visible = false;
        }

        private string DrawTool(ToolArguments args)
        {
            if (args == null) args = new ToolArguments();
            if (!Ink.Valid(args)) return "Couldn't read that drawing action.";
            if (string.Equals(args.Action, "clear", StringComparison.OrdinalIgnoreCase))
            {
                if (InvokeRequired) return (string)Invoke(new Func<ToolArguments, string>(DrawTool), args);
                string cleared = ink.Clear();
                clearInkItem.Visible = false;
                return cleared;
            }
            if (args.Shapes == null && args.DrawingShapes == null && !string.IsNullOrWhiteSpace(args.Description))
            {
                if (Ink.ScreenRequest(args.Description)) return Hands.Work(args.Description, args.Asked);
                if (Ink.Desktop().IsEmpty) return Ink.NoSpace;
                bool teaching = Ink.Teaches(args.Description);
                List<KeyValuePair<string, ToolArguments>> calls = new List<KeyValuePair<string, ToolArguments>>();
                string composed = brain.Step((teaching ? Ink.Teaching : "") + "Compose their requested screen drawing with draw. Supply shapes, not description. Use paths for freehand art. " + Ink.Description + Ink.Context(), args.Description, Ink.Tools, Ink.ClaudeTools, Ink.Listing, calls, teaching ? Ink.TeachingBudget : 400);
                if (calls.Count == 0) calls.AddRange(Hands.Written(composed));
                List<InkShape> drawing = new List<InkShape>();
                foreach (KeyValuePair<string, ToolArguments> call in calls) if (call.Key == "draw" && call.Value != null) drawing.AddRange(Ink.Parse(call.Value.Shapes));
                ToolArguments ready = args.Copy();
                ready.Description = null;
                ready.DrawingShapes = drawing;
                string made = DrawTool(ready);
                return teaching ? Ink.Explained(made, drawing) : made;
            }
            if (args.Shapes == null && args.DrawingShapes == null) return Ink.Question;
            if (InvokeRequired) return (string)Invoke(new Func<ToolArguments, string>(DrawTool), args);
            string drawn = ink.Draw(args.DrawingShapes ?? Ink.Map(Ink.Parse(args.Shapes), args.Drawing, args.DrawingGrid), string.Equals(args.Action, "add", StringComparison.OrdinalIgnoreCase));
            clearInkItem.Visible = ink.Has;
            return drawn;
        }
    }
}

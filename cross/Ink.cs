using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;

namespace CapriDesktop
{
    internal sealed partial class Settings
    {
        [System.Runtime.Serialization.DataMember(EmitDefaultValue = false)] internal bool? Motion;
    }

    internal sealed class InkShape
    {
        internal string Kind, Text;
        internal Point[] Points;
        internal double[] Coordinates;
    }

    internal interface InkSurface : IDisposable
    {
        bool Open(InkPolicy policy);
        void Advance(double now);
    }

    internal sealed class InkPolicy
    {
        internal readonly bool Transparent = true, Topmost = true, ClickThrough = true;
        internal readonly bool Activate = false, Decorated = false;
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
        internal const string Teaching = "This drawing teaches them something, so make it a clear diagram: boxes, arrows, axes and short text labels, filling most of the middle of the monitor they're on. Use real numbers in any example, like the vector (2, 1). Text ink is 28 pixel sans, about 34 pixels tall and 16 wide per letter, and a text shape's point is its top left. Keep each label under 30 letters, at least 50 pixels from the next, and never over a line or another label. ";
        internal const int TeachingBudget = 1500;
        private const string Few = @"(?: (?:a|an|some|another|quick|little|simple|small|big|basic|nice))";
        private const string Named = @"(?:(?!(?:my|the|this|that|these|those|your|our|their) )[\w-]+ )?";
        private const string Kinds = Named + @"(?:diagrams?|flow ?charts?|mind ?maps?)";
        private static readonly Regex Diagram = new Regex(
            @"^(?:make|create|build|give|show|draw|sketch|i (?:need|want|would like)|i'd like)(?: me| us)?(?: up| out)?" + Few + "* " + Kinds + @"\b" +
            @"|^(?:make|create|build)(?: me| us)?(?: up| out)?" + Few + "* " + Named + @"(?:charts?|graphs?)\b" +
            @"|(?:[,.;:!?]\s+|\b(?:(?:can|could|would|will) (?:you|u)|you to|and|then|please|so)\s+)(?:(?:please|just|maybe|also)\s+)*(?:make|create|build|draw|sketch|give me|show me)(?: me| us)?(?: up| out)?" + Few + "+ " + Kinds + @"\b" +
            @"|\b(?:explain|teach|walk me through|break down|go over|help me|show me|understand|learn)\b.*\b(?:with|using|in|as|on) (?:a|an|some)" + Few + "* " + Named + @"(?:diagrams?|flow ?charts?|mind ?maps?|drawings?|sketch(?:es)?|charts?|graphs?|pictures?)\b",
            RegexOptions.IgnoreCase);
        private const string Gone = @"(?:it(?: all)?|that|this|them|everything|all(?: of it| that)?|(?:the|my|your|this|that) (?:whole )?screen|(?:(?:the|my|your|that|this|those|these|all the|all your|all of the|all of your) )?(?:drawings?|diagrams?|art|artwork|ink|doodles?|sketch(?:es)?|annotations?|scribbles?|lines|arrows|circles))";
        private const string Away = @"(?: (?:from|off|off of|on) (?:my|the|your) (?:screens?|desktop|monitors?))?";
        private static readonly Regex Wiping = new Regex(
            @"^(?:(?:clear|erase|remove|delete|wipe)(?: away| off| out)?|get rid of) " + Gone + @"(?: (?:away|off|out))?" + Away + "$" +
            @"|^rub " + Gone + " (?:out|off|away)" + Away + "$" +
            @"|^(?:take|get) " + Gone + " off(?: of)? (?:my|the|your) (?:screens?|desktop|monitors?)$",
            RegexOptions.IgnoreCase);


        internal const string BadShapes = "Couldn't read any shapes to draw.";
        internal const string MovesOff = "Turn on my moves before asking me to draw.";
        internal const string Permission = "Allow Capri in System Settings > Privacy & Security > Input Monitoring so holding Esc can clear my drawing.";
        internal const double EscHold = 0.7;
        internal static Func<bool> Active = delegate { return false; };
        internal static Func<string> Layout = delegate { return ""; };
        internal Func<double> Time = delegate { return Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency; };
        internal Func<bool> Moves = delegate { return true; };
        internal Func<bool> Allowed = CGPreflightListenEventAccess;
        internal Func<bool> EscDown = delegate { return CGEventSourceKeyState(1, 53); };
        internal Func<List<InkStroke>, Func<double>, Action, InkSurface> Make;
        internal Action<string> Failed;
        internal static Func<IntPtr, string, bool?, bool> Message = NativeMessage;
        private List<InkStroke> strokes = new List<InkStroke>();
        private InkSurface surface;
        private double escFrom = -1;
        private bool permissionSaid;
        internal bool Has { get { return strokes.Count != 0; } }
        internal int Count { get { return strokes.Count; } }
        internal bool Moving { get { double now = Time(); return strokes.Exists(stroke => now < stroke.Born + InkStroke.Seconds); } }

        internal static string Context() { return Layout(); }

        private static readonly Regex Number = new Regex(@"\G[\s,\[\]():]*([+-]?(?:\d+(?:\.\d*)?|\.\d+))(?:[eE][+-]?\d+)?");
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
        internal static bool Teaches(string description)
        {
            return Regex.IsMatch(description ?? "", @"\b(?:diagrams?|flow ?charts?|mind ?maps?|charts?|graphs?|understand|explain|teach|learn)\b|\bhow\b.*\bworks?\b", RegexOptions.IgnoreCase);
        }
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
                Point[] points = new Point[values.Count / 2];
                for (int pointIndex = 0; pointIndex < points.Length; pointIndex++) points[pointIndex] = new Point((float)values[pointIndex * 2], (float)values[pointIndex * 2 + 1]);
                if ((kind == "circle" || kind == "box") && (points[0].X == points[1].X || points[0].Y == points[1].Y)) continue;
                shapes.Add(new InkShape { Kind = kind, Points = points, Coordinates = values.ToArray(), Text = kind == "text" ? tail.Trim('"', '\'') : null });
            }
            return shapes;
        }



        internal static List<InkStroke> Plan(List<InkShape> shapes, double born)
        {
            var plan = new List<InkStroke>();
            foreach (InkShape shape in shapes)
            {
                var stroke = new InkStroke(shape, born);
                if (stroke.Length > 0) plan.Add(stroke);
            }
            return plan;
        }

        private string Access()
        {
            if (Allowed()) { permissionSaid = false; return null; }
            bool repeated = permissionSaid;
            permissionSaid = true;
            return repeated ? "I still can't read Esc to clear my drawing." : Permission;
        }

        internal string Draw(List<InkShape> shapes, bool add)
        {
            if (shapes.Count == 0) return BadShapes;
            if (!Moves()) return MovesOff;
            InkSurface opened = null;
            try
            {
                string access = Access();
                if (access != null) return access;
                var fresh = Plan(shapes, Time());
                if (fresh.Count == 0) return BadShapes;
                var drawing = add ? new List<InkStroke>(strokes) : new List<InkStroke>();
                drawing.AddRange(fresh);
                opened = Make(drawing, Time, Broken);
                if (opened == null) return NoSpace;
                if (!opened.Open(new InkPolicy())) { opened.Dispose(); return NoOverlay; }
                if (surface != null) surface.Dispose();
                surface = opened;
                strokes = drawing;
                escFrom = -1;
                return add ? "Added to my drawing." : "Drew on your screen.";
            }
            catch (Exception)
            {
                if (opened != null) opened.Dispose();
                return NoOverlay;
            }
        }

        internal void Step(double now)
        {
            if (!Has) return;
            if (!Moves()) { Clear(); return; }
            string access = Access();
            if (access != null) { Clear(); if (Failed != null) Failed(access); return; }
            if (!EscDown()) escFrom = -1;
            else
            {
                if (escFrom < 0) escFrom = now;
                if (now - escFrom >= EscHold) { Clear(); return; }
            }
            if (surface != null) surface.Advance(now);
        }

        private void Broken()
        {
            Clear();
            if (Failed != null) Failed(NoOverlay);
        }

        internal string Clear()
        {
            InkSurface old = surface;
            surface = null;
            strokes.Clear();
            escFrom = -1;
            if (old != null) old.Dispose();
            return "Cleared my drawing.";
        }

        public void Dispose() { Clear(); }

        internal static bool Through(Window window)
        {
            return Through(window.TryGetPlatformHandle());
        }

        private static bool Through(IPlatformHandle handle)
        {
            IntPtr native = handle is IMacOSTopLevelPlatformHandle mac ? mac.NSWindow : handle != null && handle.HandleDescriptor == "NSWindow" ? handle.Handle : IntPtr.Zero;
            if (native == IntPtr.Zero) return false;
            Message(native, "setIgnoresMouseEvents:", true);
            Message(native, "setHidesOnDeactivate:", false);
            return Message(native, "ignoresMouseEvents", null);
        }

        private static bool NativeMessage(IntPtr receiver, string selector, bool? value)
        {
            IntPtr name = sel_registerName(selector);
            if (value.HasValue) { SetBool(receiver, name, value.Value); return true; }
            return GetBool(receiver, name);
        }

        [DllImport("/usr/lib/libobjc.A.dylib")] private static extern IntPtr sel_registerName(string name);
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern void SetBool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool value);
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        [return: MarshalAs(UnmanagedType.I1)] private static extern bool GetBool(IntPtr receiver, IntPtr selector);
        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        [return: MarshalAs(UnmanagedType.I1)] private static extern bool CGPreflightListenEventAccess();
        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
        [return: MarshalAs(UnmanagedType.I1)] private static extern bool CGEventSourceKeyState(int state, ushort key);

        internal static void Check(List<string> checks)
        {
            var good = Parse("line 1 2 3 4; arrow(10,20,30,40); circle [10,20,50,60]; box 80 90 20 30; path 0 0 10 20 30 40; text 12 15 \"hi; choom\"");
            if (good.Count != 6 || good[4].Points.Length != 3 || good[5].Text != "hi; choom") throw new Exception("Ink lost a good shape or its text.");
            if (Parse("line 1 2 3 4; triangle 1 2 3 4; circle 1 1 1 9; path 1 2 3; line NaN 2 3 4; text 10 20 hi; arrow bad; box 10 20 30 40").Count != 3 || Parse("").Count != 0 || Parse("[]").Count != 0) throw new Exception("Bad ink spoiled good shapes.");
            if (Parse("LINE -1.5 +2 3e1 4\nbox (1, 2, 3, 4)\npath [1,2], [3,4], [5,6]").Count != 3) throw new Exception("Ink lost loose numbers.");
            var plan = Plan(good, 10);
            if (plan.Count != 6) throw new Exception("Ink lost a stroke.");
            foreach (InkStroke stroke in plan)
            {
                double before = -1;
                for (int step = 0; step <= 11; step++)
                {
                    double distance = stroke.Shown(10 + step * 0.05);
                    if (distance < before || distance > stroke.Length) throw new Exception("Ink clock moved backwards.");
                    before = distance;
                }
                if (Math.Abs(before - stroke.Length) > 0.001 || stroke.Tip(10 + InkStroke.Seconds + 0.01) != stroke.End) throw new Exception("Ink missed its last point.");
            }
            var path = new InkStroke(Parse("path 0 0 10 0 10 10")[0], 0);
            if (path.Tip(0.275) != new Point(10, 0) || path.Shown(0) != 0 || path.Shown(99) != 20) throw new Exception("Ink did not reveal by distance.");
            var bent = new InkStroke(Parse("path 0 0 1 0 1 7")[0], 0);
            if (bent.Length != 8 || bent.Tip(InkStroke.Seconds / 8) != new Point(1, 0)) throw new Exception("Ink skipped a path corner.");
            var monitor = new PixelRect(0, 0, 100, 100);
            if (!bent.Touches(monitor) || new InkStroke(Parse("box -50 -50 150 150")[0], 0).Touches(monitor) || !new InkStroke(Parse("line -20 50 120 50")[0], 0).Touches(monitor)) throw new Exception("Ink mistook blank monitor space for a stroke.");
            var messageWas = Message;
            try
            {
                var messages = new List<string>();
                Message = (receiver, selector, value) => { if (receiver != new IntPtr(12)) throw new Exception("Ink used the wrong native window."); messages.Add(selector + ":" + value); return true; };
                if (!Through(new PlatformHandle(new IntPtr(12), "NSWindow")) || messages.Count != 3 || messages[0] != "setIgnoresMouseEvents::True" || messages[1] != "setHidesOnDeactivate::False" || messages[2] != "ignoresMouseEvents:") throw new Exception("Ink did not set and verify native click-through.");
                if (Through(new PlatformHandle(new IntPtr(12), "XID"))) throw new Exception("Ink treated a foreign handle as NSWindow.");
            }
            finally { Message = messageWas; }
            double now = 10;
            bool down = false, moves = true, allowed = true;
            var made = new List<Probe>();
            using (var ink = new Ink())
            {
                ink.Time = () => now;
                ink.Moves = () => moves;
                ink.Allowed = () => allowed;
                ink.EscDown = () => down;
                ink.Make = (drawing, time, broken) => { var probe = new Probe(); made.Add(probe); return probe; };
                if (ink.Draw(Parse("line NaN 0 0 0"), false) != BadShapes || made.Count != 0 || !Valid(new ToolArguments { Action = "ADD" }) || Valid(new ToolArguments { Action = "erase" })) throw new Exception("Ink accepted bad input.");
                moves = false;
                if (ink.Draw(good, false) != MovesOff || made.Count != 0) throw new Exception("Ink drew with moves off.");
                moves = true; allowed = false;
                if (ink.Draw(good, false) != Permission || ink.Draw(good, false) == Permission || made.Count != 0) throw new Exception("Ink repeated its permission request.");
                allowed = true;
                if (ink.Draw(good, false) != "Drew on your screen." || ink.Count != 6 || !made[0].Safe) throw new Exception("Ink overlay policy was unsafe.");
                if (ink.Draw(Parse("line 0 0 1 1"), true) != "Added to my drawing." || ink.Count != 7 || !made[0].Closed) throw new Exception("Ink add lost its old drawing.");
                down = true; ink.Step(20); ink.Step(20.5);
                down = false; ink.Step(20.6);
                if (!ink.Has) throw new Exception("An Esc tap cleared ink.");
                down = true; ink.Step(21); ink.Step(21.71);
                if (ink.Has || !made[1].Closed) throw new Exception("Holding Esc did not clear ink.");
                ink.Draw(good, false);
                if (ink.Clear() != "Cleared my drawing." || ink.Has || !made[2].Closed) throw new Exception("Clear left ink behind.");
                ink.Draw(good, false);
                now++;
                if (ink.Draw(Parse("line 0 0 2 2"), false) != "Drew on your screen." || ink.Count != 1 || !made[3].Closed) throw new Exception("Replace kept old ink.");
                moves = false; ink.Step(now);
                if (ink.Has || !made[4].Closed) throw new Exception("Moves off left ink behind.");
                moves = true;
                ink.Draw(good, false);
                string failure = null;
                ink.Failed = line => failure = line;
                allowed = false; ink.Step(now);
                if (ink.Has || failure != Permission || !made[5].Closed) throw new Exception("Ink ignored revoked Input Monitoring.");
                allowed = true;
                ink.Make = (drawing, time, broken) => null;
                if (ink.Draw(good, false) != NoSpace || ink.Has) throw new Exception("Ink drew outside its screen.");
                ink.Make = (drawing, time, broken) => new Probe { Works = false };
                if (ink.Draw(good, false) != NoOverlay || ink.Has) throw new Exception("A broken overlay reported success.");
            }
            bool macWas = Hands.OnMac;
            var drawWas = Hands.Draw;
            var activeWas = Active;
            try
            {
                Hands.Draw = args => "ink seam";
                Hands.OnMac = true;
                if (Hands.Unable("draw", new ToolArguments()) != null || Hands.Do("draw", new ToolArguments()) != "ink seam" || !Hands.Note.Contains(Note) || !Hands.Tools.Contains("\"draw\"")) throw new Exception("Mac drawing stayed missing.");
                Active = () => true;
                string tool;
                ToolArguments args;
                if (!Hands.Heard("clear your drawing", out tool, out args) || tool != "draw" || args.Action != "clear" || Request("remove it").Action != "clear") throw new Exception("A clear request did not reach ink.");
                Hands.OnMac = false;
                if (Hands.Unable("draw", new ToolArguments()) == null || Hands.Note.Contains(Note) || Hands.Tools.Contains("\"draw\"")) throw new Exception("Linux drawing gap changed.");
            }
            finally { Hands.OnMac = macWas; Hands.Draw = drawWas; Active = activeWas; }
            checks.Add("PASS Mac ink: Windows shape format, timed strokes, passive click-through policy, moves and permission gates, add, replace, clear, held Esc and platform lists");
        }

        private sealed class Probe : InkSurface
        {
            internal bool Safe, Closed, Works = true;
            public bool Open(InkPolicy policy) { Safe = policy.Transparent && policy.Topmost && policy.ClickThrough && !policy.Activate && !policy.Decorated; return Works && Safe; }
            public void Advance(double now) { }
            public void Dispose() { Closed = true; }
        }
    }

    internal sealed class InkStroke
    {
        internal const double Seconds = 0.55;
        internal readonly double Born, Length;
        internal readonly Rect Bounds;
        private readonly List<Point[]> contours = new List<Point[]>();
        internal Point End { get { var last = contours[contours.Count - 1]; return last[last.Length - 1]; } }

        internal InkStroke(InkShape shape, double born)
        {
            Born = born;
            using var path = new SKPath();
            Point first = shape.Points[0], last = shape.Points[shape.Points.Length - 1];
            if (shape.Kind == "text")
            {
                using var paint = new SKPaint { TextSize = 28, Typeface = SKTypeface.Default, IsAntialias = true };
                using var letters = paint.GetTextPath(shape.Text, (float)first.X, (float)first.Y - paint.FontMetrics.Top);
                path.AddPath(letters);
            }
            else if (shape.Kind == "box" || shape.Kind == "circle")
            {
                var box = new SKRect((float)Math.Min(first.X, last.X), (float)Math.Min(first.Y, last.Y), (float)Math.Max(first.X, last.X), (float)Math.Max(first.Y, last.Y));
                if (shape.Kind == "box") path.AddRect(box); else path.AddOval(box);
                if (shape.Kind == "box") contours.Add(new[] { new Point(box.Left, box.Top), new Point(box.Right, box.Top), new Point(box.Right, box.Bottom), new Point(box.Left, box.Bottom), new Point(box.Left, box.Top) });
            }
            else
            {
                path.MoveTo((float)first.X, (float)first.Y);
                for (int i = 1; i < shape.Points.Length; i++) path.LineTo((float)shape.Points[i].X, (float)shape.Points[i].Y);
                contours.Add(shape.Points);
                if (shape.Kind == "arrow")
                {
                    double angle = Math.Atan2(last.Y - first.Y, last.X - first.X), head = Math.Min(18, (last - first).Length / 3);
                    path.MoveTo((float)(last.X - head * Math.Cos(angle - 0.5)), (float)(last.Y - head * Math.Sin(angle - 0.5)));
                    path.LineTo((float)last.X, (float)last.Y);
                    path.LineTo((float)(last.X - head * Math.Cos(angle + 0.5)), (float)(last.Y - head * Math.Sin(angle + 0.5)));
                    contours.Add(new[] { new Point(last.X - head * Math.Cos(angle - 0.5), last.Y - head * Math.Sin(angle - 0.5)), last, new Point(last.X - head * Math.Cos(angle + 0.5), last.Y - head * Math.Sin(angle + 0.5)) });
                }
            }
            if (shape.Kind == "text" || shape.Kind == "circle")
            {
                using var measure = new SKPathMeasure(path, false);
                do
                {
                    float length = measure.Length;
                    if (!float.IsFinite(length) || length <= 0) continue;
                    int count = (int)Math.Clamp(Math.Ceiling(length / 2), 1, 16384);
                    var points = new Point[count + 1];
                    for (int i = 0; i <= count; i++)
                    {
                        SKPoint point = measure.GetPosition(length * i / count);
                        points[i] = new Point(point.X, point.Y);
                    }
                    contours.Add(points);
                } while (measure.NextContour());
            }
            foreach (Point[] points in contours)
                for (int i = 1; i < points.Length; i++) Length += (points[i] - points[i - 1]).Length;
            SKRect bounds = path.Bounds;
            Bounds = new Rect(bounds.Left - 12, bounds.Top - 12, bounds.Width + 24, bounds.Height + 24);
        }

        internal double Shown(double now) { return Length * Math.Clamp((now - Born) / Seconds, 0, 1); }

        private static bool Edge(double direction, double distance, ref double from, ref double to)
        {
            if (direction == 0) return distance >= 0;
            double along = distance / direction;
            if (direction < 0) { if (along > to) return false; if (along > from) from = along; }
            else { if (along < from) return false; if (along < to) to = along; }
            return true;
        }

        internal bool Touches(PixelRect monitor)
        {
            var area = new Rect(monitor.X - 5, monitor.Y - 5, monitor.Width + 10, monitor.Height + 10);
            foreach (Point[] points in contours)
                for (int i = 1; i < points.Length; i++)
                {
                    Point first = points[i - 1], second = points[i];
                    double across = second.X - first.X, down = second.Y - first.Y, from = 0, to = 1;
                    if (Edge(-across, first.X - area.Left, ref from, ref to) && Edge(across, area.Right - first.X, ref from, ref to) && Edge(-down, first.Y - area.Top, ref from, ref to) && Edge(down, area.Bottom - first.Y, ref from, ref to)) return true;
                }
            return false;
        }

        internal Point Tip(double now)
        {
            double left = Shown(now);
            foreach (Point[] points in contours)
                for (int i = 1; i < points.Length; i++)
                {
                    double length = (points[i] - points[i - 1]).Length;
                    if (left <= length && length > 0) return points[i - 1] + (points[i] - points[i - 1]) * (left / length);
                    left -= length;
                }
            return End;
        }

        internal void Paint(DrawingContext graphics, double now)
        {
            double left = Shown(now);
            if (left <= 0) return;
            var geometry = new StreamGeometry();
            using (var pen = geometry.Open())
                foreach (Point[] points in contours)
                {
                    pen.BeginFigure(points[0], false);
                    for (int i = 1; i < points.Length && left > 0; i++)
                    {
                        double length = (points[i] - points[i - 1]).Length;
                        if (length <= 0) continue;
                        pen.LineTo(left < length ? points[i - 1] + (points[i] - points[i - 1]) * (left / length) : points[i]);
                        left -= length;
                    }
                    pen.EndFigure(false);
                }
            foreach (var layer in new[] { (24, 10.0), (65, 6.0), (255, 2.5) })
                graphics.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)layer.Item1, Look.Pink.R, Look.Pink.G, Look.Pink.B)), layer.Item2, null, PenLineCap.Round, PenLineJoin.Round), geometry);
        }
    }

    internal sealed class InkWindow : PassiveWindow, InkSurface
    {
        private readonly PixelRect room;
        private readonly double scale;
        private readonly Action broken;
        private readonly Canvas face;
        private bool finished;

        internal InkWindow(PixelRect room, double scale, List<InkStroke> drawing, Func<double> time, Action broken)
        {
            this.room = room;
            this.scale = scale;
            this.broken = broken;
            face = new Canvas(drawing, time, room, scale);
            Content = face;
        }

        public bool Open(InkPolicy policy)
        {
            Look.Overlay(this, "Capri drawing");
            Background = policy.Transparent ? Brushes.Transparent : Brushes.Black;
            Topmost = policy.Topmost;
            SystemDecorations = policy.Decorated ? SystemDecorations.Full : SystemDecorations.None;
            ShowActivated = policy.Activate;
            Focusable = false;
            IsHitTestVisible = false;
            Width = room.Width / scale;
            Height = room.Height / scale;
            if (!policy.ClickThrough || !Ink.Through(this)) return false;
            Position = room.Position;
            Show();
            Position = room.Position;
            if (ActualTransparencyLevel != WindowTransparencyLevel.Transparent || !Ink.Through(this)) { Close(); return false; }
            return IsVisible;
        }

        public void Advance(double now)
        {
            try
            {
                if (!IsVisible) broken();
                else if (!finished) { face.InvalidateVisual(); finished = !face.Moving(now); }
            }
            catch (Exception) { broken(); }
        }

        public void Dispose() { Close(); }

        private sealed class Canvas : Control
        {
            private readonly List<InkStroke> drawing;
            private readonly Func<double> time;
            private readonly PixelRect room;
            private readonly double scale;

            internal Canvas(List<InkStroke> drawing, Func<double> time, PixelRect room, double scale) { this.drawing = drawing; this.time = time; this.room = room; this.scale = scale; }
            internal bool Moving(double now) { return drawing.Exists(stroke => now < stroke.Born + InkStroke.Seconds); }

            public override void Render(DrawingContext graphics)
            {
                double now = time();
                using (graphics.PushTransform(new Matrix(1 / scale, 0, 0, 1 / scale, -room.X / scale, -room.Y / scale)))
                {
                    foreach (InkStroke stroke in drawing) stroke.Paint(graphics, now);
                    for (int i = drawing.Count - 1; i >= 0; i--)
                    {
                        InkStroke stroke = drawing[i];
                        if (now < stroke.Born || now >= stroke.Born + InkStroke.Seconds) continue;
                        Point tip = stroke.Tip(now);
                        var art = HerCursor.Art;
                        if (art != null) graphics.DrawImage(art, new Rect(0, 0, art.PixelSize.Width, art.PixelSize.Height), new Rect(tip.X - 2, tip.Y - 2, 36, 36));
                        else graphics.DrawEllipse(new SolidColorBrush(Look.Pink), null, tip, 4, 4);
                        break;
                    }
                }
            }
        }
    }

    internal sealed partial class Pet
    {
        private readonly Ink ink = new Ink();

        private void StartInk()
        {
            if (!OperatingSystem.IsMacOS()) return;
            var ticks = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
            Ink.Active = () => ink.Has;
            Ink.Layout = () => Dispatcher.UIThread.Invoke(() =>
            {
                var layout = new StringBuilder("\nFor drawing in chat, coordinates are desktop pixels, including negative positions. Monitor rectangles (left top right bottom):");
                foreach (Screen screen in Screens.All) layout.Append("\n" + screen.Bounds.X + " " + screen.Bounds.Y + " " + screen.Bounds.Right + " " + screen.Bounds.Bottom);
                Screen here = ScreenHere();
                if (here != null) layout.Append("\nThey're on " + here.Bounds.X + " " + here.Bounds.Y + " " + here.Bounds.Right + " " + here.Bounds.Bottom + ", so draw there unless they say otherwise.");
                return layout.ToString();
            });
            ink.Moves = () => Saved.Motion != false;
            ink.Make = (drawing, time, broken) =>
            {
                Screen screen = ScreenHere();
                if (screen == null || !drawing.Exists(stroke => stroke.Touches(screen.Bounds))) return null;
                return new InkWindow(screen.Bounds, screen.Scaling > 0 ? screen.Scaling : 1, drawing, time, broken);
            };
            ink.Failed = line => { ticks.Stop(); Talk.Answer("(drawing)", line); };
            ticks.Tick += delegate { try { ink.Step(ink.Time()); ticks.Interval = TimeSpan.FromMilliseconds(ink.Moving ? 25 : 100); if (!ink.Has) ticks.Stop(); } catch (Exception) { ink.Clear(); ticks.Stop(); Talk.Answer("(drawing)", Ink.NoOverlay); } };
            Hands.Draw = args =>
            {
                args = args ?? new ToolArguments();
                if (!Ink.Valid(args)) return "Couldn't read that drawing action.";
                if (string.Equals(args.Action, "clear", StringComparison.OrdinalIgnoreCase)) return Dispatcher.UIThread.Invoke(() => { ticks.Stop(); return ink.Clear(); });
                if (!Dispatcher.UIThread.Invoke(ink.Moves)) return Ink.MovesOff;
                if (args.Shapes == null && !string.IsNullOrWhiteSpace(args.Description))
                {
                    if (Ink.ScreenRequest(args.Description)) return Hands.Work(args.Description, args.Asked);
                    bool teaching = Ink.Teaches(args.Description);
                    var calls = new List<KeyValuePair<string, ToolArguments>>();
                    string composed = Brain.Step((teaching ? Ink.Teaching : "") + "Compose their requested screen drawing with draw. Supply shapes, not description. Use paths for freehand art. " + Ink.Description + Ink.Context(), args.Description, Ink.Tools, Ink.ClaudeTools, Ink.Listing, calls, teaching ? Ink.TeachingBudget : 400);
                    if (calls.Count == 0) calls.AddRange(Hands.Written(composed));
                    var drawing = new List<InkShape>();
                    foreach (var call in calls) if (call.Key == "draw" && call.Value != null) drawing.AddRange(Ink.Parse(call.Value.Shapes));
                    string result = Dispatcher.UIThread.Invoke(() => { string made = ink.Draw(drawing, string.Equals(args.Action, "add", StringComparison.OrdinalIgnoreCase)); if (ink.Has) { ticks.Interval = TimeSpan.FromMilliseconds(25); ticks.Start(); } return made; });
                    return teaching ? Ink.Explained(result, drawing) : result;
                }
                if (args.Shapes == null) return Ink.Question;
                return Dispatcher.UIThread.Invoke(() => { string made = ink.Draw(Ink.Parse(args.Shapes), string.Equals(args.Action, "add", StringComparison.OrdinalIgnoreCase)); if (ink.Has) { ticks.Interval = TimeSpan.FromMilliseconds(25); ticks.Start(); } return made; });
            };
            Closed += delegate { ticks.Stop(); ink.Dispose(); };
        }
    }
}

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia;
using Avalonia.Media;

namespace CapriDesktop
{
    // Port of Lachy's released PanelArt in app/FileEditor.cs. Coordinates and the
    // authored growth curves are retained; Avalonia draws them in one native window.
    internal static class NativeEditorArt
    {
        internal const double Left = 109, Top = 42, Wide = 565, High = 535;
        internal const double SpineLeft = 133, SpineRight = 170, Crown = 164, Foot = 553, TickY = 353;
        internal const double SheetLeft = 172, SheetRight = 650, SheetFoot = 552.5, StepIn = 13.5, StepTop = 378.5;
        internal const double SheetWaist = SheetRight - StepIn;
        internal const double RuleLeft = 137, RuleRight = 434, RuleTop = 110, RuleBottom = 114, BlockTop = 123, BlockBottom = 135, BlockEnd = 172;
        internal const double RestAlpha = 81.7 / 255, GlassAlpha = .78;
        internal static readonly Color Red = Color.FromRgb(235, 74, 66), Salmon = Color.FromRgb(245, 128, 120), Dark = Color.FromRgb(30, 12, 14);
        internal static readonly double[] SpineEnds = {168,182,204,231,264,300,338,377,416,452,484,512,534,548,553};
        internal static readonly double[] SheetEnds = {172,176,188,202,221,243,270,298,329,362,395,429,462,495,526,555,582,605,623,638,647,650};
        internal static readonly double[] StepDepths = {1,1,2,2,3,3,4,5,6,7,8,9,9,10,12,12,12,13,13,13.5};
        internal static readonly double[] SheetAlphas = {253.2,252.4,250.7,247.4,242.7,236.2,227.7,217.3,205,191.1,175.7,159.7,143.5,127.7,113,100.3,90.4,84,81.7};
        internal static readonly double[] RuleEnds = {139,145,154,167,183,200,220,241,263,285,307,329,349,369,387,402,415,425,431,434};
        internal static readonly double[] BlockEnds = {140,146,154,162,169,172};
        private static IBrush grain;
        internal static IBrush Grain => grain ??= MakeGrain();
        private static IBrush MakeGrain()
        {
            var tile = new WriteableBitmap(new PixelSize(96,96),new Vector(96,96),PixelFormat.Bgra8888,AlphaFormat.Premul);
            using (var frame = tile.Lock())
            {
                byte[] pixels = new byte[frame.RowBytes*96]; var noise = new Random(865);
                for(int y=0;y<96;y++)for(int x=0;x<96;x++)
                {
                    double speck=noise.NextDouble()+noise.NextDouble()-1;int alpha=(int)(Math.Abs(speck)*34),at=y*frame.RowBytes+x*4;
                    if(speck>=0){pixels[at]=(byte)(66*alpha/255);pixels[at+1]=(byte)(74*alpha/255);pixels[at+2]=(byte)(235*alpha/255);}pixels[at+3]=(byte)alpha;
                }
                Marshal.Copy(pixels,0,frame.Address,pixels.Length);
            }
            return new ImageBrush(tile){TileMode=TileMode.Tile,Stretch=Stretch.None,DestinationRect=new RelativeRect(0,0,96,96,RelativeUnit.Absolute),AlignmentX=AlignmentX.Left,AlignmentY=AlignmentY.Top};
        }
        internal static double Along(double[] values, int first, double frame)
        {
            double at = frame - first;
            if (at <= 0) return values[0];
            int low = (int)at;
            if (low >= values.Length - 1) return values[values.Length - 1];
            return values[low] + (values[low + 1] - values[low]) * (at - low);
        }
        internal static double Grown(double frame, double from, double to) => Math.Clamp((frame - from) / (to - from), 0, 1);
        internal static double Glassy(double frame)
        {
            double art = Along(SheetAlphas, 17, frame) / 255, top = SheetAlphas[0] / 255;
            return GlassAlpha + (art - RestAlpha) * (top - GlassAlpha) / (top - RestAlpha);
        }
        internal static Color SheetFill(double frame)
        {
            double alpha = Glassy(frame), t = Math.Clamp((1 - alpha) / (1 - GlassAlpha), 0, 1);
            return Color.FromArgb((byte)(alpha * 255), (byte)(235 + t * (52 - 235)), (byte)(74 + t * (30 - 74)), (byte)(66 + t * (36 - 66)));
        }
        internal static Point[] Spine(double frame)
        {
            double end = Math.Min(frame, 1) * (Along(SpineEnds, 1, frame) - Crown) + Crown;
            double step = Crown + 4.5 * Grown(frame, 3, 8), slit = 2.5 * Grown(frame, 11, 14), cut = Grown(frame, 4, 12);
            var p = new List<Point> {new(SpineLeft,step),new(144.5,step),new(150.5,Crown),new(SpineRight,Crown)};
            if (end > 247)
            {
                p.Add(new(SpineRight,247));p.Add(new(SpineRight-slit,251));
                if (end > 470) {p.Add(new(SpineRight-slit,470));p.Add(new(SpineRight,474));}
                else p.Add(new(SpineRight-slit,end));
            }
            p.Add(new(end>470||end<=247?SpineRight:SpineRight-slit,end));p.Add(new(SpineLeft+3*cut,end));p.Add(new(SpineLeft+3*cut,end-5*cut));p.Add(new(SpineLeft,end-8*cut));
            return p.ToArray();
        }
        internal static Point[] Sheet(double frame, double inset = 0)
        {
            double right = Along(SheetEnds,14,frame), depth = Along(StepDepths,16,frame), chamfer = 4.5+depth*9/StepIn,near=right-depth,left=SheetLeft;
            return new[]{new Point(left+inset,Crown+inset),new Point(Math.Max(left+inset,right-chamfer-inset/2),Crown+inset),new Point(Math.Max(left+inset,right-inset),Crown+chamfer+inset/2),new Point(Math.Max(left+inset,right-inset),StepTop-inset/2),new Point(Math.Max(left+inset,near-inset),StepTop+depth-inset/2),new Point(Math.Max(left+inset,near-inset),SheetFoot-inset),new Point(left+inset,SheetFoot-inset)};
        }
        internal static StreamGeometry Shape(Point[] points, double scale)
        {
            var path = new StreamGeometry();
            using (var c = path.Open())
            {
                c.BeginFigure(new Point((points[0].X-Left)*scale,(points[0].Y-Top)*scale),true);
                for(int i=1;i<points.Length;i++)c.LineTo(new Point((points[i].X-Left)*scale,(points[i].Y-Top)*scale));
                c.EndFigure(true);
            }
            return path;
        }
        internal static Rect Page(double scale) => new((SheetLeft+7-Left)*scale,(Crown+7-Top)*scale,(SheetWaist-SheetLeft-14)*scale,(SheetFoot-Crown-14)*scale);
    }

    internal sealed class NativeEditorMotion
    {
        internal const double LastFrame = 59, Fps = 24;
        internal enum Phase { Opening, Open, Untyping, Closing, Gone }
        internal Phase Stage { get; private set; } = Phase.Opening;
        internal double Frame { get; private set; } = 1;
        internal double Untyped { get; private set; }
        internal bool Leaving => Stage == Phase.Untyping || Stage == Phase.Closing;
        internal void FinishOpen() { Stage = Phase.Open; Frame = LastFrame; Untyped = 0; }
        internal void Open() { if (Stage == Phase.Gone) {Frame=1;Untyped=0;} Stage = Phase.Opening; }
        internal void Close() { Stage=Phase.Untyping;Untyped=0;Frame=Math.Min(Frame,LastFrame); }
        internal void Advance(double seconds)
        {
            if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            double frames=seconds*Fps;
            if(Stage==Phase.Opening){Frame=Math.Min(LastFrame,Frame+frames);if(Frame>=LastFrame)Stage=Phase.Open;}
            else if(Stage==Phase.Untyping){Untyped+=frames;if(Untyped>=14){double extra=Untyped-14;Stage=Phase.Closing;Frame=Math.Max(0,Math.Min(Frame,40)-extra);if(Frame==0)Stage=Phase.Gone;}}
            else if(Stage==Phase.Closing){Frame=Math.Max(0,Frame-frames);if(Frame==0)Stage=Phase.Gone;}
        }
        internal double Words => Stage == Phase.Untyping ? 1-Math.Clamp(Untyped/14,0,1) : Stage==Phase.Closing||Stage==Phase.Gone?0:NativeEditorArt.Grown(Frame,45,59);
    }
}

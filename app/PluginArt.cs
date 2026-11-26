using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace CapriDesktop
{
    // Her plug-in pictures on a plate cut at every corner like her speech box: a service's own logo where it has one, and
    // otherwise her drawings, an envelope for email, a page, a checklist, or a plug for the rest.
    internal static class PluginArt
    {
        internal static void Plate(Graphics graphics, RectangleF box, float s, float cut, Color edge, Color fill, bool glow, int wash)
        {
            float line = Math.Max(1f, 2 * s);
            using (GraphicsPath path = Look.Plate(box, cut, -1, box.Top + box.Height / 2, 0, true))
            {
                if (glow) using (Pen haze = new Pen(Color.FromArgb(46, edge), 7 * s)) graphics.DrawPath(haze, path);
                using (Brush brush = new SolidBrush(Color.FromArgb(238, fill))) graphics.FillPath(brush, path);
                GraphicsState state = graphics.Save();
                graphics.SetClip(path, CombineMode.Intersect);
                using (Pen scan = new Pen(Color.FromArgb(9, 255, 255, 255), Math.Max(1f, s)))
                    for (float row = box.Top; row < box.Bottom; row += Math.Max(2f, 3 * s)) graphics.DrawLine(scan, box.Left, row, box.Right, row);
                if (wash > 0) using (Brush lit = new SolidBrush(Color.FromArgb(wash, edge))) graphics.FillPath(lit, path);
                graphics.Restore(state);
            }
            float inset = line / 2;
            using (GraphicsPath rim = Look.Plate(new RectangleF(box.Left + inset, box.Top + inset, box.Width - line, box.Height - line), cut - inset * 0.586f, -1, box.Top + box.Height / 2, 0, true))
            using (Pen pen = new Pen(edge, line))
                graphics.DrawPath(pen, rim);
        }

        // The same cut, a little outside the plate, for the keyboard's mark.
        internal static GraphicsPath Around(RectangleF box, float cut, float by)
        {
            RectangleF wider = RectangleF.Inflate(box, by, by);
            return Look.Plate(wider, cut + by * 0.586f, -1, wider.Top + wider.Height / 2, 0, true);
        }

        internal static void Draw(Graphics graphics, int picture, RectangleF area, Color ink, Color accent, Color fill)
        {
            float k = Math.Min(area.Width / 48f, area.Height / 38f);
            if (k <= 0) return;
            GraphicsState state = graphics.Save();
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TranslateTransform(area.Left + area.Width / 2, area.Top + area.Height / 2);
            graphics.ScaleTransform(k, k);
            using (Pen pen = new Pen(ink, 2f) { LineJoin = LineJoin.Miter })
            using (Pen bright = new Pen(accent, 2.2f) { LineJoin = LineJoin.Miter, StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (Brush body = new SolidBrush(fill))
            using (Brush spot = new SolidBrush(accent))
            {
                if (picture == Mods.Envelope) Envelope(graphics, pen, bright, body);
                else if (picture == Mods.Page) Page(graphics, pen, bright, body);
                else if (picture == Mods.Checklist) Checklist(graphics, pen, bright, body);
                else Plug(graphics, pen, bright, body, spot);
            }
            graphics.Restore(state);
        }

        // A service's own logo, filled in its colour and centred a little inside the area so it sits like her drawings do.
        internal static void Logo(Graphics graphics, ServiceLogo logo, RectangleF area, Color ink)
        {
            float size = Math.Min(area.Width, area.Height) * 0.86f;
            if (size <= 0) return;
            GraphicsState state = graphics.Save();
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TranslateTransform(area.Left + (area.Width - size) / 2, area.Top + (area.Height - size) / 2);
            graphics.ScaleTransform(size / 24f, size / 24f);
            foreach (ServiceLogo layer in logo.Layers ?? new[] { logo })
                using (GraphicsPath shape = Outline(layer))
                using (Brush brush = new SolidBrush(logo.Layers == null ? ink : ColorTranslator.FromHtml(layer.Ink)))
                    graphics.FillPath(brush, shape);
            graphics.Restore(state);
        }

        internal static GraphicsPath Outline(ServiceLogo logo)
        {
            GraphicsPath shape = new GraphicsPath(FillMode.Winding);
            foreach (ServiceLogo.Figure figure in logo.Figures)
            {
                shape.StartFigure();
                PointF at = new PointF((float)figure.X, (float)figure.Y);
                foreach (double[] step in figure.Steps)
                {
                    PointF to = new PointF((float)step[step.Length - 2], (float)step[step.Length - 1]);
                    if (step.Length == 2) shape.AddLine(at, to);
                    else shape.AddBezier(at, new PointF((float)step[0], (float)step[1]), new PointF((float)step[2], (float)step[3]), to);
                    at = to;
                }
                if (figure.Closed) shape.CloseFigure();
            }
            return shape;
        }

        private static void Envelope(Graphics graphics, Pen pen, Pen bright, Brush body)
        {
            RectangleF box = new RectangleF(-21, -14, 42, 28);
            using (GraphicsPath shape = Look.Plate(box, 3, -1, 0, 0, true))
            {
                graphics.FillPath(body, shape);
                graphics.DrawPath(pen, shape);
            }
            graphics.DrawLines(bright, new[] { new PointF(-17, -10), new PointF(0, 3), new PointF(17, -10) });
            using (Pen fold = new Pen(Color.FromArgb(120, pen.Color), 1.4f))
            {
                graphics.DrawLine(fold, -17, 10, -6, -1);
                graphics.DrawLine(fold, 17, 10, 6, -1);
            }
        }

        private static void Page(Graphics graphics, Pen pen, Pen bright, Brush body)
        {
            PointF[] sheet = { new PointF(-14, -17), new PointF(6, -17), new PointF(14, -9), new PointF(14, 17), new PointF(-14, 17) };
            graphics.FillPolygon(body, sheet);
            graphics.DrawPolygon(pen, sheet);
            graphics.DrawLines(pen, new[] { new PointF(6, -17), new PointF(6, -9), new PointF(14, -9) });
            graphics.DrawLine(bright, -9, -10, 1, -10);
            using (Pen words = new Pen(pen.Color, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                graphics.DrawLine(words, -9, -2, 9, -2);
                graphics.DrawLine(words, -9, 4, 9, 4);
                graphics.DrawLine(words, -9, 10, 3, 10);
            }
        }

        private static void Checklist(Graphics graphics, Pen pen, Pen bright, Brush body)
        {
            RectangleF board = new RectangleF(-17, -17, 34, 34);
            using (GraphicsPath shape = Look.Plate(board, 4, -1, 0, 0, true))
            {
                graphics.FillPath(body, shape);
                graphics.DrawPath(pen, shape);
            }
            using (Pen thin = new Pen(pen.Color, 1.5f))
            using (Pen words = new Pen(pen.Color, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                for (int row = 0; row < 3; row++)
                {
                    float y = -10 + row * 9;
                    graphics.DrawRectangle(thin, -11, y - 3, 6, 6);
                    graphics.DrawLine(words, -1, y, 11, y);
                    if (row < 2) graphics.DrawLines(bright, new[] { new PointF(-10, y), new PointF(-8, y + 2), new PointF(-4, y - 4) });
                }
        }

        private static void Plug(Graphics graphics, Pen pen, Pen bright, Brush body, Brush spot)
        {
            graphics.FillRectangle(spot, -7, -16, 3, 8);
            graphics.FillRectangle(spot, 4, -16, 3, 8);
            using (GraphicsPath shape = Look.Plate(new RectangleF(-11, -8, 22, 15), 3, -1, 0, 0, true))
            {
                graphics.FillPath(body, shape);
                graphics.DrawPath(pen, shape);
            }
            graphics.DrawLine(pen, -4, 7, -4, 10);
            graphics.DrawLine(pen, 4, 7, 4, 10);
            graphics.DrawLine(pen, -6, 10, 6, 10);
            graphics.DrawBezier(pen, new PointF(0, 10), new PointF(0, 17), new PointF(10, 13), new PointF(16, 18));
        }
    }
}

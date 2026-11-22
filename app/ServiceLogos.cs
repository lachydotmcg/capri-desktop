using System;
using System.Collections.Generic;
using System.Globalization;

namespace CapriDesktop
{
    // Each logo is one filled outline on a 24 by 24 grid, with its path data and colour from Simple Icons 16.38.0 (CC0).
    // The logos themselves are their owners' trademarks.
    internal sealed class ServiceLogo
    {
        internal sealed class Figure
        {
            internal double X, Y;
            internal bool Closed;
            internal readonly List<double[]> Steps = new List<double[]>();
        }

        internal static readonly ServiceLogo Gmail = new ServiceLogo("Gmail", "#EA4335", "M24 5.457v13.909c0 .904-.732 1.636-1.636 1.636h-3.819V11.73L12 16.64l-6.545-4.91v9.273H1.636A1.636 1.636 0 0 1 0 19.366V5.457c0-2.023 2.309-3.178 3.927-1.964L5.455 4.64 12 9.548l6.545-4.91 1.528-1.145C21.69 2.28 24 3.434 24 5.457z");
        internal static readonly ServiceLogo ICloud = new ServiceLogo("iCloud", "#3693F3", "M13.762 4.29a6.51 6.51 0 0 0-5.669 3.332 3.571 3.571 0 0 0-1.558-.36 3.571 3.571 0 0 0-3.516 3A4.918 4.918 0 0 0 0 14.796a4.918 4.918 0 0 0 4.92 4.914 4.93 4.93 0 0 0 .617-.045h14.42c2.305-.272 4.041-2.258 4.043-4.589v-.009a4.594 4.594 0 0 0-3.727-4.508 6.51 6.51 0 0 0-6.511-6.27z");
        internal static readonly ServiceLogo Notion = new ServiceLogo("Notion", "#000000", "M4.459 4.208c.746.606 1.026.56 2.428.466l13.215-.793c.28 0 .047-.28-.046-.326L17.86 1.968c-.42-.326-.981-.7-2.055-.607L3.01 2.295c-.466.046-.56.28-.374.466zm.793 3.08v13.904c0 .747.373 1.027 1.214.98l14.523-.84c.841-.046.935-.56.935-1.167V6.354c0-.606-.233-.933-.748-.887l-15.177.887c-.56.047-.747.327-.747.933zm14.337.745c.093.42 0 .84-.42.888l-.7.14v10.264c-.608.327-1.168.514-1.635.514-.748 0-.935-.234-1.495-.933l-4.577-7.186v6.952L12.21 19s0 .84-1.168.84l-3.222.186c-.093-.186 0-.653.327-.746l.84-.233V9.854L7.822 9.76c-.094-.42.14-1.026.793-1.073l3.456-.233 4.764 7.279v-6.44l-1.215-.139c-.093-.514.28-.887.747-.933zM1.936 1.035l13.31-.98c1.634-.14 2.055-.047 3.082.7l4.249 2.986c.7.513.934.653.934 1.213v16.378c0 1.026-.373 1.634-1.68 1.726l-15.458.934c-.98.047-1.448-.093-1.962-.747l-3.129-4.06c-.56-.747-.793-1.306-.793-1.96V2.667c0-.839.374-1.54 1.447-1.632z");
        internal static readonly ServiceLogo Linear = new ServiceLogo("Linear", "#5E6AD2", "M2.886 4.18A11.982 11.982 0 0 1 11.99 0C18.624 0 24 5.376 24 12.009c0 3.64-1.62 6.903-4.18 9.105L2.887 4.18ZM1.817 5.626l16.556 16.556c-.524.33-1.075.62-1.65.866L.951 7.277c.247-.575.537-1.126.866-1.65ZM.322 9.163l14.515 14.515c-.71.172-1.443.282-2.195.322L0 11.358a12 12 0 0 1 .322-2.195Zm-.17 4.862 9.823 9.824a12.02 12.02 0 0 1-9.824-9.824Z");

        internal readonly string Name, Hex;
        internal readonly List<Figure> Figures;

        private ServiceLogo(string name, string hex, string data)
        {
            Name = name;
            Hex = hex;
            Reader reader = new Reader(data);
            reader.Run();
            Figures = reader.Figures;
        }

        // Notion's mark is black, so on her dark tiles it's drawn white, the way Notion shows it on a dark page.
        internal string Ink
        {
            get
            {
                int value = Convert.ToInt32(Hex.Substring(1), 16);
                return Math.Max(value >> 16, Math.Max((value >> 8) & 255, value & 255)) < 0x40 ? "#FFFFFF" : Hex;
            }
        }

        private sealed class Reader
        {
            internal readonly List<Figure> Figures = new List<Figure>();
            private readonly string data;
            private int at;
            private Figure figure;
            private double x, y, startX, startY, lastX, lastY;
            private char last = ' ';

            internal Reader(string data) { this.data = data; }

            internal void Run()
            {
                char command = ' ';
                while (true)
                {
                    Skip();
                    if (at >= data.Length) return;
                    bool fresh = char.IsLetter(data[at]);
                    if (fresh) command = data[at++];
                    else if (command == ' ') throw new FormatException("Logo path data starts without a command.");
                    bool relative = char.IsLower(command);
                    char kind = char.ToUpperInvariant(command);
                    double ox = relative ? x : 0, oy = relative ? y : 0;
                    if (kind == 'Z')
                    {
                        if (!fresh) throw new FormatException("Logo path data has numbers after a close.");
                        if (figure != null) figure.Closed = true;
                        figure = null;
                        x = startX;
                        y = startY;
                        last = kind;
                        continue;
                    }
                    if (kind == 'M')
                    {
                        x = ox + Number();
                        y = oy + Number();
                        figure = new Figure { X = x, Y = y };
                        Figures.Add(figure);
                        startX = x;
                        startY = y;
                        command = relative ? 'l' : 'L';
                    }
                    else if (kind == 'L')
                    {
                        double nx = ox + Number(), ny = oy + Number();
                        Line(nx, ny);
                    }
                    else if (kind == 'H') Line(ox + Number(), y);
                    else if (kind == 'V') Line(x, oy + Number());
                    else if (kind == 'C' || kind == 'S')
                    {
                        double ax, ay;
                        if (kind == 'C') { ax = ox + Number(); ay = oy + Number(); }
                        else if (last == 'C' || last == 'S') { ax = 2 * x - lastX; ay = 2 * y - lastY; }
                        else { ax = x; ay = y; }
                        double bx = ox + Number(), by = oy + Number(), nx = ox + Number(), ny = oy + Number();
                        Cubic(ax, ay, bx, by, nx, ny);
                        lastX = bx;
                        lastY = by;
                    }
                    else if (kind == 'Q' || kind == 'T')
                    {
                        double qx, qy;
                        if (kind == 'Q') { qx = ox + Number(); qy = oy + Number(); }
                        else if (last == 'Q' || last == 'T') { qx = 2 * x - lastX; qy = 2 * y - lastY; }
                        else { qx = x; qy = y; }
                        double nx = ox + Number(), ny = oy + Number();
                        Cubic(x + (qx - x) * 2 / 3, y + (qy - y) * 2 / 3, nx + (qx - nx) * 2 / 3, ny + (qy - ny) * 2 / 3, nx, ny);
                        lastX = qx;
                        lastY = qy;
                    }
                    else if (kind == 'A')
                    {
                        double rx = Number(), ry = Number(), turn = Number();
                        bool large = Flag(), sweep = Flag();
                        double nx = ox + Number(), ny = oy + Number();
                        Arc(rx, ry, turn, large, sweep, nx, ny);
                    }
                    else throw new FormatException("Logo path data has an unknown command: " + command);
                    last = kind;
                }
            }

            private void Start()
            {
                if (figure != null) return;
                figure = new Figure { X = x, Y = y };
                Figures.Add(figure);
            }

            private void Line(double nx, double ny)
            {
                Start();
                figure.Steps.Add(new[] { nx, ny });
                x = nx;
                y = ny;
            }

            private void Cubic(double ax, double ay, double bx, double by, double nx, double ny)
            {
                Start();
                figure.Steps.Add(new[] { ax, ay, bx, by, nx, ny });
                x = nx;
                y = ny;
            }

            // An arc becomes curves of a quarter turn or less, the usual way: find its centre, then step round it.
            private void Arc(double rx, double ry, double turn, bool large, bool sweep, double nx, double ny)
            {
                if (x == nx && y == ny) return;
                rx = Math.Abs(rx);
                ry = Math.Abs(ry);
                if (rx == 0 || ry == 0) { Line(nx, ny); return; }
                double phi = turn * Math.PI / 180, cos = Math.Cos(phi), sin = Math.Sin(phi);
                double dx = (x - nx) / 2, dy = (y - ny) / 2;
                double px = cos * dx + sin * dy, py = -sin * dx + cos * dy;
                double grow = px * px / (rx * rx) + py * py / (ry * ry);
                if (grow > 1) { rx *= Math.Sqrt(grow); ry *= Math.Sqrt(grow); }
                double rx2 = rx * rx, ry2 = ry * ry, under = rx2 * py * py + ry2 * px * px;
                double k = under == 0 ? 0 : Math.Sqrt(Math.Max(0, (rx2 * ry2 - under) / under));
                if (large == sweep) k = -k;
                double cpx = k * rx * py / ry, cpy = -k * ry * px / rx;
                double cx = cos * cpx - sin * cpy + (x + nx) / 2, cy = sin * cpx + cos * cpy + (y + ny) / 2;
                double ux = (px - cpx) / rx, uy = (py - cpy) / ry, vx = (-px - cpx) / rx, vy = (-py - cpy) / ry;
                double begin = Angle(1, 0, ux, uy), span = Angle(ux, uy, vx, vy);
                if (!sweep && span > 0) span -= 2 * Math.PI;
                else if (sweep && span < 0) span += 2 * Math.PI;
                int parts = Math.Max(1, (int)Math.Ceiling(Math.Abs(span) / (Math.PI / 2) - 1e-9));
                double step = span / parts, t = 4.0 / 3 * Math.Tan(step / 4);
                for (int i = 0; i < parts; i++)
                {
                    double a = begin + i * step, b = a + step;
                    double c1 = Math.Cos(a), s1 = Math.Sin(a), c2 = Math.Cos(b), s2 = Math.Sin(b);
                    double ex = c1 - t * s1, ey = s1 + t * c1, fx = c2 + t * s2, fy = s2 - t * c2;
                    bool end = i == parts - 1;
                    Cubic(cx + rx * ex * cos - ry * ey * sin, cy + rx * ex * sin + ry * ey * cos,
                        cx + rx * fx * cos - ry * fy * sin, cy + rx * fx * sin + ry * fy * cos,
                        end ? nx : cx + rx * c2 * cos - ry * s2 * sin, end ? ny : cy + rx * c2 * sin + ry * s2 * cos);
                }
            }

            private static double Angle(double ux, double uy, double vx, double vy) { return Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy); }

            private void Skip()
            {
                while (at < data.Length && (char.IsWhiteSpace(data[at]) || data[at] == ',')) at++;
            }

            private double Number()
            {
                Skip();
                int begin = at;
                if (at < data.Length && (data[at] == '+' || data[at] == '-')) at++;
                bool dot = false, digits = false;
                while (at < data.Length)
                {
                    char c = data[at];
                    if (c >= '0' && c <= '9') digits = true;
                    else if (c == '.' && !dot) dot = true;
                    else break;
                    at++;
                }
                if (digits && at < data.Length && (data[at] == 'e' || data[at] == 'E'))
                {
                    int back = at++;
                    if (at < data.Length && (data[at] == '+' || data[at] == '-')) at++;
                    int power = at;
                    while (at < data.Length && data[at] >= '0' && data[at] <= '9') at++;
                    if (at == power) at = back;
                }
                if (!digits) throw new FormatException("Logo path data needs a number at " + begin + ".");
                return double.Parse(data.Substring(begin, at - begin), NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            private bool Flag()
            {
                Skip();
                if (at < data.Length && (data[at] == '0' || data[at] == '1')) return data[at++] == '1';
                throw new FormatException("Logo path data needs an arc flag at " + at + ".");
            }
        }
    }
}

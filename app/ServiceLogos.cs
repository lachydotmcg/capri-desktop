using System;
using System.Collections.Generic;
using System.Globalization;

namespace CapriDesktop
{
    // Filled outlines on a 24 by 24 grid. Unless noted below, geometry and colours are from Simple Icons 16.38.0 (CC0).
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

        // https://discord.com/branding
        internal static readonly ServiceLogo Discord = new ServiceLogo("Discord", "#5865F2", "M20.317 4.3698a19.7913 19.7913 0 00-4.8851-1.5152.0741.0741 0 00-.0785.0371c-.211.3753-.4447.8648-.6083 1.2495-1.8447-.2762-3.68-.2762-5.4868 0-.1636-.3933-.4058-.8742-.6177-1.2495a.077.077 0 00-.0785-.037 19.7363 19.7363 0 00-4.8852 1.515.0699.0699 0 00-.0321.0277C.5334 9.0458-.319 13.5799.0992 18.0578a.0824.0824 0 00.0312.0561c2.0528 1.5076 4.0413 2.4228 5.9929 3.0294a.0777.0777 0 00.0842-.0276c.4616-.6304.8731-1.2952 1.226-1.9942a.076.076 0 00-.0416-.1057c-.6528-.2476-1.2743-.5495-1.8722-.8923a.077.077 0 01-.0076-.1277c.1258-.0943.2517-.1923.3718-.2914a.0743.0743 0 01.0776-.0105c3.9278 1.7933 8.18 1.7933 12.0614 0a.0739.0739 0 01.0785.0095c.1202.099.246.1981.3728.2924a.077.077 0 01-.0066.1276 12.2986 12.2986 0 01-1.873.8914.0766.0766 0 00-.0407.1067c.3604.698.7719 1.3628 1.225 1.9932a.076.076 0 00.0842.0286c1.961-.6067 3.9495-1.5219 6.0023-3.0294a.077.077 0 00.0313-.0552c.5004-5.177-.8382-9.6739-3.5485-13.6604a.061.061 0 00-.0312-.0286zM8.02 15.3312c-1.1825 0-2.1569-1.0857-2.1569-2.419 0-1.3332.9555-2.4189 2.157-2.4189 1.2108 0 2.1757 1.0952 2.1568 2.419 0 1.3332-.9555 2.4189-2.1569 2.4189zm7.9748 0c-1.1825 0-2.1569-1.0857-2.1569-2.419 0-1.3332.9554-2.4189 2.1569-2.4189 1.2108 0 2.1757 1.0952 2.1568 2.419 0 1.3332-.946 2.4189-2.1568 2.4189Z");
        // https://doist.com/press
        internal static readonly ServiceLogo Todoist = new ServiceLogo("Todoist", "#E44332", "M21 0H3C1.35 0 0 1.35 0 3v3.858s3.854 2.24 4.098 2.38c.31.18.694.177 1.004 0 .26-.147 8.02-4.608 8.136-4.675.279-.161.58-.107.748-.01.164.097.606.348.84.48.232.134.221.502.013.622l-9.712 5.59c-.346.2-.69.204-1.048.002C3.478 10.907.998 9.463 0 8.882v2.02l4.098 2.38c.31.18.694.177 1.004 0 .26-.147 8.02-4.609 8.136-4.676.279-.16.58-.106.748-.008.164.096.606.347.84.48.232.133.221.5.013.62-.208.121-9.288 5.346-9.712 5.59-.346.2-.69.205-1.048.002C3.478 14.951.998 13.506 0 12.926v2.02l4.098 2.38c.31.18.694.177 1.004 0 .26-.147 8.02-4.609 8.136-4.676.279-.16.58-.106.748-.009.164.097.606.348.84.48.232.133.221.502.013.622l-9.712 5.59c-.346.199-.69.204-1.048.001C3.478 18.994.998 17.55 0 16.97V21c0 1.65 1.35 3 3 3h18c1.65 0 3-1.35 3-3V3c0-1.65-1.35-3-3-3z");
        // https://github.com/logos
        internal static readonly ServiceLogo GitHub = new ServiceLogo("GitHub", "#181717", "M12 .297c-6.63 0-12 5.373-12 12 0 5.303 3.438 9.8 8.205 11.385.6.113.82-.258.82-.577 0-.285-.01-1.04-.015-2.04-3.338.724-4.042-1.61-4.042-1.61C4.422 18.07 3.633 17.7 3.633 17.7c-1.087-.744.084-.729.084-.729 1.205.084 1.838 1.236 1.838 1.236 1.07 1.835 2.809 1.305 3.495.998.108-.776.417-1.305.76-1.605-2.665-.3-5.466-1.332-5.466-5.93 0-1.31.465-2.38 1.235-3.22-.135-.303-.54-1.523.105-3.176 0 0 1.005-.322 3.3 1.23.96-.267 1.98-.399 3-.405 1.02.006 2.04.138 3 .405 2.28-1.552 3.285-1.23 3.285-1.23.645 1.653.24 2.873.12 3.176.765.84 1.23 1.91 1.23 3.22 0 4.61-2.805 5.625-5.475 5.92.42.36.81 1.096.81 2.22 0 1.606-.015 2.896-.015 3.286 0 .315.21.69.825.57C20.565 22.092 24 17.592 24 12.297c0-6.627-5.373-12-12-12");
        // https://atlassian.design/resources/logo-library
        internal static readonly ServiceLogo Atlassian = new ServiceLogo("Atlassian", "#0052CC", "M7.12 11.084a.683.683 0 00-1.16.126L.075 22.974a.703.703 0 00.63 1.018h8.19a.678.678 0 00.63-.39c1.767-3.65.696-9.203-2.406-12.52zM11.434.386a15.515 15.515 0 00-.906 15.317l3.95 7.9a.703.703 0 00.628.388h8.19a.703.703 0 00.63-1.017L12.63.38a.664.664 0 00-1.196.006z");
        // https://miro.com
        internal static readonly ServiceLogo Miro = new ServiceLogo("Miro", "#050038", "M17.392 0H13.9L17 4.808 10.444 0H6.949l3.102 6.3L3.494 0H0l3.05 8.131L0 24h3.494L10.05 6.985 6.949 24h3.494L17 5.494 13.899 24h3.493L24 3.672 17.392 0z");
        // https://www.cloudflare.com/logo/
        internal static readonly ServiceLogo Cloudflare = new ServiceLogo("Cloudflare", "#F38020", "M16.5088 16.8447c.1475-.5068.0908-.9707-.1553-1.3154-.2246-.3164-.6045-.499-1.0615-.5205l-8.6592-.1123a.1559.1559 0 0 1-.1333-.0713c-.0283-.042-.0351-.0986-.021-.1553.0278-.084.1123-.1484.2036-.1562l8.7359-.1123c1.0351-.0489 2.1601-.8868 2.5537-1.9136l.499-1.3013c.0215-.0561.0293-.1128.0147-.168-.5625-2.5463-2.835-4.4453-5.5499-4.4453-2.5039 0-4.6284 1.6177-5.3876 3.8614-.4927-.3658-1.1187-.5625-1.794-.499-1.2026.119-2.1665 1.083-2.2861 2.2856-.0283.31-.0069.6128.0635.894C1.5683 13.171 0 14.7754 0 16.752c0 .1748.0142.3515.0352.5273.0141.083.0844.1475.1689.1475h15.9814c.0909 0 .1758-.0645.2032-.1553l.12-.4268zm2.7568-5.5634c-.0771 0-.1611 0-.2383.0112-.0566 0-.1054.0415-.127.0976l-.3378 1.1744c-.1475.5068-.0918.9707.1543 1.3164.2256.3164.6055.498 1.0625.5195l1.8437.1133c.0557 0 .1055.0263.1329.0703.0283.043.0351.1074.0214.1562-.0283.084-.1132.1485-.204.1553l-1.921.1123c-1.041.0488-2.1582.8867-2.5527 1.914l-.1406.3585c-.0283.0713.0215.1416.0986.1416h6.5977c.0771 0 .1474-.0489.169-.126.1122-.4082.1757-.837.1757-1.2803 0-2.6025-2.125-4.727-4.7344-4.727");

        // Official symbol geometry, extracted from the wordmark assets (background and text omitted).
        // https://dapulse-res.cloudinary.com/image/upload/remote_mondaycom_static/img/monday-logo-2.svg
        internal static readonly ServiceLogo Monday = new ServiceLogo("monday.com", "#FB275D", new[] { new ServiceLogo("monday.com", "#FB275D", "M26.1249 89.4621C21.8937 89.4583 17.9979 87.255 15.9501 83.7076C13.9023 80.1603 14.0251 75.8276 16.2707 72.3919L37.2233 40.3436C39.3754 36.854 43.3343 34.7579 47.5633 34.869C51.7924 34.9801 55.6255 37.2808 57.5748 40.8782C59.5241 44.4755 59.2826 48.803 56.9441 52.1807L36.0039 84.229C33.8741 87.4897 30.1398 89.4678 26.1249 89.4621Z", 0.24, -2.64, -2.64), new ServiceLogo("monday.com", "#FFCC00", "M61.8246 89.462C57.6004 89.4582 53.711 87.2602 51.6665 83.7213C49.6221 80.1823 49.7446 75.86 51.9865 72.4324L72.8986 40.4602C75.017 36.9197 78.9898 34.7734 83.2506 34.8677C87.5114 34.962 91.3764 37.2816 93.3217 40.912C95.2669 44.5424 94.9803 48.901 92.5748 52.2692L71.6627 84.2414C69.541 87.4872 65.8241 89.4596 61.8246 89.462V89.462Z", 0.24, -2.64, -2.64), new ServiceLogo("monday.com", "#00CA72", "M96.6298 89.5272C103.029 89.5272 108.217 84.3486 108.217 77.9605C108.217 71.5723 103.029 66.3937 96.6298 66.3937C90.2306 66.3937 85.043 71.5723 85.043 77.9605C85.043 84.3486 90.2306 89.5272 96.6298 89.5272Z", 0.24, -2.64, -2.64) });
        // https://context7.com/_next/static/immutable/media/context7-logo-light.1irhnfg2jrcmg.svg
        internal static readonly ServiceLogo Context7 = new ServiceLogo("Context7", "#000000", "M10.5724 15.2565C10.5724 17.5025 9.6613 19.3778 8.17805 21.1047H11.6319L11.6319 22.7786H6.33459V21.1895C7.95557 19.3566 8.58065 17.8628 8.58065 15.2565L10.5724 15.2565Z M17.4276 15.2565C17.4276 17.5025 18.3387 19.3778 19.822 21.1047H16.3681V22.7786H21.6654V21.1895C20.0444 19.3566 19.4194 17.8628 19.4194 15.2565H17.4276Z M10.5724 12.7435C10.5724 10.4975 9.66131 8.62224 8.17807 6.89532L11.6319 6.89532V5.22137L6.33461 5.22137V6.81056C7.95558 8.64343 8.58066 10.1373 8.58066 12.7435L10.5724 12.7435Z M17.4276 12.7435C17.4276 10.4975 18.3387 8.62224 19.822 6.89532L16.3681 6.89532L16.3681 5.22138L21.6654 5.22138V6.81056C20.0445 8.64343 19.4194 10.1373 19.4194 12.7435H17.4276Z", 0.857142857142857, 0, 0);

        internal readonly string Name, Hex;
        internal readonly List<Figure> Figures;
        internal readonly ServiceLogo[] Layers;

        private ServiceLogo(string name, string hex, ServiceLogo[] layers)
        {
            Name = name;
            Hex = hex;
            Layers = layers;
            Figures = new List<Figure>();
            foreach (ServiceLogo layer in layers) Figures.AddRange(layer.Figures);
        }

        private ServiceLogo(string name, string hex, string data, double scale = 1, double offsetX = 0, double offsetY = 0)
        {
            Name = name;
            Hex = hex;
            Reader reader = new Reader(data);
            reader.Run();
            Figures = reader.Figures;
            foreach (Figure figure in Figures)
            {
                figure.X = figure.X * scale + offsetX;
                figure.Y = figure.Y * scale + offsetY;
                foreach (double[] step in figure.Steps)
                    for (int i = 0; i < step.Length; i++) step[i] = step[i] * scale + (i % 2 == 0 ? offsetX : offsetY);
            }
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

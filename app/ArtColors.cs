using System;
using System.Collections.Generic;
using System.IO;
#if !CROSS
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
#endif

namespace CapriDesktop
{
    internal static class ArtColors
    {
        internal const int Hair = 0, Accent = 1, Eyes = 2, Edges = 3, Body = 4, Slots = 5;
        internal static readonly string[] Names = { "Hair", "Accents", "Eyes", "Edges", "Body" };
        internal static readonly string[] Usual = { "#FF0046", "#00E86E", "#1870F0", "#7854F0", "#05030A" };
        private static readonly double[] Hues = { 348, 145, 215, 262 };
        private static volatile int[] table;

        internal static bool Active { get { return table != null; } }

        internal static void Set(string[] colours)
        {
            bool any = false;
            if (colours != null) foreach (string colour in colours) any |= colour != null;
            table = any ? Build(colours) : null;
        }

        private static int[] Build(string[] colours)
        {
            int[] made = new int[1 << 18];
            int?[] slots = new int?[Slots];
            for (int i = 0; i < Slots; i++) if (colours[i] != null) slots[i] = Convert.ToInt32(colours[i].Substring(1), 16);
            for (int key = 0; key < made.Length; key++)
            {
                int r = Middle(key >> 12), g = Middle((key >> 6) & 63), b = Middle(key & 63);
                made[key] = Map(r, g, b, slots);
            }
            return made;
        }

        private static int Middle(int six) { return six == 63 ? 255 : six == 0 ? 0 : six * 4 + 2; }

        private static int Map(int r, int g, int b, int?[] slots)
        {
            int high = Math.Max(r, Math.Max(g, b)), low = Math.Min(r, Math.Min(g, b));
            double v = high / 255.0, s = high == 0 ? 0 : (high - low) / (double)high;
            if (s > 0.55 || (s > 0.25 && v > 0.35))
            {
                int slot = NearestHue(r, g, b, high, low);
                if (slots[slot] == null) return Pack(r, g, b);
                int to = slots[slot].Value;
                double colourful = Math.Min(1, s / 0.9), grey = v * 255;
                return Pack(
                    (int)(grey + (((to >> 16) & 255) * v - grey) * colourful),
                    (int)(grey + (((to >> 8) & 255) * v - grey) * colourful),
                    (int)(grey + ((to & 255) * v - grey) * colourful));
            }
            if (v < 0.38 && slots[Body] != null)
            {
                int to = slots[Body].Value;
                double depth = Math.Min(1, 0.72 + v * 1.6);
                return Pack((int)(((to >> 16) & 255) * depth), (int)(((to >> 8) & 255) * depth), (int)((to & 255) * depth));
            }
            return Pack(r, g, b);
        }

        private static int NearestHue(int r, int g, int b, int high, int low)
        {
            double range = Math.Max(1, high - low), hue;
            if (high == r) hue = 60 * (((g - b) / range + 6) % 6);
            else if (high == g) hue = 60 * ((b - r) / range + 2);
            else hue = 60 * ((r - g) / range + 4);
            int best = 0;
            double closest = 999;
            for (int i = 0; i < Hues.Length; i++)
            {
                double away = Math.Abs(hue - Hues[i]);
                away = Math.Min(away, 360 - away);
                if (away < closest) { closest = away; best = i; }
            }
            return best;
        }

        private static int Pack(int r, int g, int b)
        {
            return (Math.Max(0, Math.Min(255, r)) << 16) | (Math.Max(0, Math.Min(255, g)) << 8) | Math.Max(0, Math.Min(255, b));
        }

        internal static void Apply(int[] pixels)
        {
            int[] map = table;
            if (map == null) return;
            for (int i = 0; i < pixels.Length; i++)
            {
                int pixel = pixels[i];
                if (((uint)pixel >> 24) == 0) continue;
                int key = ((pixel >> 18) & 63) << 12 | ((pixel >> 10) & 63) << 6 | ((pixel >> 2) & 63);
                pixels[i] = (pixel & unchecked((int)0xFF000000)) | map[key];
            }
        }

#if !CROSS
        internal static Bitmap Apply(Bitmap art)
        {
            int[] map = table;
            if (map == null) return null;
            Bitmap copy = new Bitmap(art.Width, art.Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(copy))
            {
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                graphics.DrawImage(art, new Rectangle(0, 0, copy.Width, copy.Height));
            }
            Rectangle all = new Rectangle(0, 0, copy.Width, copy.Height);
            BitmapData data = copy.LockBits(all, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int[] row = new int[copy.Width];
                for (int y = 0; y < copy.Height; y++)
                {
                    IntPtr line = data.Scan0 + y * data.Stride;
                    Marshal.Copy(line, row, 0, row.Length);
                    for (int x = 0; x < row.Length; x++)
                    {
                        int pixel = row[x];
                        if (((uint)pixel >> 24) == 0) continue;
                        int key = ((pixel >> 18) & 63) << 12 | ((pixel >> 10) & 63) << 6 | ((pixel >> 2) & 63);
                        row[x] = (pixel & unchecked((int)0xFF000000)) | map[key];
                    }
                    Marshal.Copy(row, 0, line, row.Length);
                }
            }
            finally { copy.UnlockBits(data); }
            return copy;
        }
#endif
    }

    internal sealed partial class Mods
    {
        internal const string MyColors = "my-colors";
        private string lastColours = "";

        internal string[] ColoursNow
        {
            get
            {
                string[] now = new string[ArtColors.Slots];
                foreach (Mod mod in All)
                    if (mod.On && mod.File.Colors != null) now = mod.File.Colors.AsSlots();
                return now;
            }
        }

        private static void CheckColours(ModColors colours)
        {
            bool any = false;
            foreach (string colour in colours.AsSlots())
            {
                if (colour == null) continue;
                any = true;
                if (!System.Text.RegularExpressions.Regex.IsMatch(colour, "^#[0-9A-Fa-f]{6}$")) throw new ModException("\"" + colour + "\" isn't a colour. Write them like #FF0046.");
            }
            if (!any) throw new ModException("That mod's colors are empty. Set at least one of hair, accent, eyes, edges or body.");
        }

        private void OnlyColours(string keep, HashSet<string> off)
        {
            foreach (Mod other in All) if (other.File.Colors != null && other.File.Id != keep) off.Add(other.File.Id);
        }

        internal void SetColour(int slot, string colour)
        {
            string[] now = ColoursNow;
            now[slot] = colour;
            StringBuilderJson(now);
        }

#if CROSS
        internal void SaveColours(ModColors colours, bool any)
        {
            if (!any) { ResetColours(); return; }
            CheckColours(colours);
            StringBuilderJson(colours.AsSlots());
        }
#endif

        private void StringBuilderJson(string[] slots)
        {
            List<string> parts = new List<string>();
            string[] names = { "hair", "accent", "eyes", "edges", "body" };
            for (int i = 0; i < slots.Length; i++) if (slots[i] != null) parts.Add("\"" + names[i] + "\":\"" + slots[i] + "\"");
            string text = "{\n  \"format\": 1,\n  \"id\": \"" + MyColors + "\",\n  \"name\": \"My Colors\",\n  \"version\": \"1.0.0\",\n  \"description\": \"Colors picked on the Mods page.\",\n  \"colors\": { " + string.Join(", ", parts.ToArray()) + " }\n}\n";
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, MyColors + Extension), text);
            HashSet<string> off = Off();
            OnlyColours(MyColors, off);
            off.Remove(MyColors);
            WriteOff(off);
            Load();
        }

        internal void ResetColours()
        {
            string mine = Path.Combine(folder, MyColors + Extension);
            if (File.Exists(mine)) File.Delete(mine);
            HashSet<string> off = Off();
            off.Remove(MyColors);
            OnlyColours(MyColors, off);
            WriteOff(off);
            Load();
        }
    }

#if !CROSS
    internal sealed partial class Companion
    {
        private void CheckColours(List<string> checks, string output)
        {
            Mods mods = ModList;
            Func<string, string, string> sample = delegate(string id, string colours)
            {
                string file = Path.Combine(output, id + Mods.Extension);
                File.WriteAllText(file, "{\"format\":1,\"id\":\"" + id + "\",\"name\":\"" + id + "\",\"colors\":" + colours + "}");
                return file;
            };
            Func<Color> hair = delegate { return idle.Strip.GetPixel(95, 90); };
            Func<int[]> census = delegate
            {
                Bitmap art = idle.Strip;
                int[] seen = new int[4];
                for (int y = 0; y < art.Height; y += 2)
                    for (int x = 0; x < 240; x += 2)
                    {
                        Color c = art.GetPixel(x, y);
                        if (c.A < 200) continue;
                        if (c.R > 180 && c.G < 90 && c.B < 120) seen[0]++;
                        else if (c.G > 180 && c.R < 90) seen[1]++;
                        else if (c.B > 180 && c.R < 70) seen[2]++;
                        else if (c.R > 200 && c.G > 200 && c.B > 200) seen[3]++;
                    }
                return seen;
            };
            int[] before = census();
            Color usual = hair();
            if (before[0] < 300) throw new Exception("The colour check couldn't find her hair: " + before[0]);
            string blue = sample("colours-blue", "{\"hair\":\"#0000FF\"}");
            System.Diagnostics.Stopwatch took = System.Diagnostics.Stopwatch.StartNew();
            if (mods.Import(blue, null) == null) throw new Exception("A colour mod was refused.");
            long ms = took.ElapsedMilliseconds;
            int[] after = census();
            if (after[0] > before[0] / 50 || after[2] < before[0]) throw new Exception("Her hair didn't change colour: " + after[0] + " red, " + after[2] + " blue.");
            if (Math.Abs(after[1] - before[1]) > before[1] / 10 + 3) throw new Exception("Changing her hair changed her accents.");
            if (Math.Abs(after[3] - before[3]) > before[3] / 10 + 3) throw new Exception("Changing her hair changed her white parts.");
            checks.Add("PASS colours: a colour mod changes only that colour, straight away (" + ms + " ms)");
            if (mods.Import(sample("colours-green", "{\"accent\":\"#FF00FF\"}"), null) == null) throw new Exception("A second colour mod was refused.");
            if (mods.All.FindAll(mod => mod.File.Colors != null && mod.On).Count != 1 || census()[0] < before[0] / 2) throw new Exception("Two colour mods were on at once.");
            mods.SetColour(ArtColors.Eyes, "#FFAA00");
            mods.SetColour(ArtColors.Hair, "#00FF00");
            string[] kept = mods.ColoursNow;
            if (kept[ArtColors.Eyes] != "#FFAA00" || kept[ArtColors.Hair] != "#00FF00" || kept[ArtColors.Accent] != "#FF00FF") throw new Exception("Picking a colour lost the ones already on.");
            mods.Load();
            if (mods.ColoursNow[ArtColors.Hair] != "#00FF00" || hair() == usual) throw new Exception("Picked colours didn't survive a reload.");
            mods.ResetColours();
            int[] back = census();
            if (hair() != usual || back[0] != before[0] || back[1] != before[1]) throw new Exception("Reset didn't bring her colours back.");
            if (mods.All.Exists(mod => mod.File.Id == Mods.MyColors)) throw new Exception("Reset left picked colours behind.");
            checks.Add("PASS colours: one set is on at a time, picks are kept in a shareable mod and survive a reload, and reset restores her exactly");
            string[] bad = { "{\"hair\":\"red\"}", "{\"hair\":\"#12\"}", "{}", "{\"hair\":\"#GGGGGG\"}" };
            for (int i = 0; i < bad.Length; i++)
            {
                try { mods.Import(sample("colours-bad" + i, bad[i]), null); throw new Exception("Bad colours were let in: " + bad[i]); }
                catch (ModException) { }
            }
            checks.Add("PASS colours: colors that aren't real hex codes are refused");
            foreach (Mod left in mods.All.ToArray()) mods.Remove(left);
            if (hair() != usual) throw new Exception("Removing colour mods left her recoloured.");

            string folder = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath), "..", "..", "mods"));
            string[] presets = { "colors-cyber-cyan", "colors-toxic", "colors-royal", "colors-bubblegum" };
            string maid = Path.Combine(folder, "maid-suit" + Mods.SkinExtension);
            bool haveAll = File.Exists(maid);
            foreach (string name in presets) haveAll &= File.Exists(Path.Combine(folder, name + Mods.Extension));
            if (!haveAll) { checks.Add("LIMIT colours: the preset files aren't beside this build, so the previews weren't drawn"); return; }
            const int Across = 5, CellW = 240, CellH = 384;
            using (Bitmap sheet = new Bitmap(Across * CellW, CellH * 2))
            using (Graphics paper = Graphics.FromImage(sheet))
            {
                paper.Clear(Color.FromArgb(70, 80, 95));
                Mod skin = null;
                for (int row = 0; row < 2; row++)
                {
                    if (row == 1) skin = mods.Import(maid, null);
                    for (int i = 0; i < Across; i++)
                    {
                        Mod preset = i == 0 ? null : mods.Import(Path.Combine(folder, presets[i - 1] + Mods.Extension), null);
                        paper.DrawImage(idle.Strip, new Rectangle(i * CellW, row * CellH, CellW, CellH), new Rectangle(0, 0, CellW, CellH), GraphicsUnit.Pixel);
                        if (preset != null) mods.Remove(preset);
                    }
                }
                if (skin != null) mods.Remove(skin);
                sheet.Save(Path.Combine(output, "colour-presets.png"), ImageFormat.Png);
            }
            if (hair() != usual) throw new Exception("The previews left her recoloured.");
            checks.Add("PASS colours: all presets import and work with and without the Maid Suit");
        }
    }
#endif
}

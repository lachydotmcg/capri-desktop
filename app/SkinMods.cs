using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
#if CROSS
using Companion = CapriDesktop.Pet;
using Size = Avalonia.PixelSize;
#else
using System.Drawing;
using System.Drawing.Imaging;
#endif

namespace CapriDesktop
{
    internal sealed partial class Mods
    {
        internal const string SkinExtension = ".capri-skin";
        private const long LargestSkin = 96L * 1024 * 1024;
        private const long LargestUnpacked = 256L * 1024 * 1024;
        private const long LargestPicture = 32L * 1024 * 1024;
        private const int MostEntries = 64;
        private const string SkinDetails = "skin.json";
        internal Action SkinChanged;

        private string SkinHome(string id) { return Path.Combine(folder, id + "-skin"); }

        private Mod LoadSkin(string file, HashSet<string> off)
        {
            string id = Path.GetFileNameWithoutExtension(file);
            string details = Path.Combine(SkinHome(id), SkinDetails);
            if (!File.Exists(details)) throw new ModException("its pictures are missing. Import it again.");
            ModFile read = ReadSkin(File.ReadAllText(details));
            if (read.Id != id) throw new ModException("its file name doesn't match its id, " + read.Id + ".");
            return new Mod { File = read, Path = file, Skin = true, On = !off.Contains(read.Id) };
        }

        private static ModFile ReadSkin(string text)
        {
            ModFile read = ReadJson(text);
            CheckDetails(read);
            read.WakeNames = new string[0];
            read.HeardAs = new string[0];
            read.Accessory |= read.Overlay;
            return read;
        }

        private Mod ImportSkin(string file)
        {
            string unpacking = Path.Combine(folder, "skin-import-" + Guid.NewGuid().ToString("N"));
            try
            {
                if (new FileInfo(file).Length > LargestSkin) throw new ModException("That skin is too big. The limit is " + LargestSkin / (1024 * 1024) + " MB.");
                Directory.CreateDirectory(unpacking);
                ModFile read = Unpack(file, unpacking);
                string home = SkinHome(read.Id), kept = Path.Combine(folder, read.Id + SkinExtension);
                if (Directory.Exists(home)) Directory.Delete(home, true);
                Directory.Move(unpacking, home);
                if (!string.Equals(Path.GetFullPath(file), Path.GetFullPath(kept), StringComparison.OrdinalIgnoreCase)) File.Copy(file, kept, true);
                HashSet<string> off = Off();
                off.Remove(read.Id);
                WriteOff(off);
                OnTop(read.Id);
                Load();
                Reskin();
                return All.Find(mod => mod.File.Id == read.Id);
            }
            catch (IOException error) { throw new ModException("Couldn't save that skin: " + error.Message); }
            catch (UnauthorizedAccessException error) { throw new ModException("Couldn't save that skin: " + error.Message); }
            finally
            {
                try { if (Directory.Exists(unpacking)) Directory.Delete(unpacking, true); }
                catch (IOException error) { Brain.Log("mods", -1, error.Message); }
                catch (UnauthorizedAccessException error) { Brain.Log("mods", -1, error.Message); }
            }
        }

        private static ModFile Unpack(string file, string into)
        {
            ModFile read = null;
            int pictures = 0;
            long total = 0;
            try
            {
                using (ZipArchive zip = ZipFile.OpenRead(file))
                {
                    if (zip.Entries.Count > MostEntries) throw new ModException("That skin has too many files. It should only hold skin.json and her sprite strips.");
                    foreach (ZipArchiveEntry entry in zip.Entries)
                    {
                        string name = entry.FullName;
                        if (name.Length == 0 || name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || name.Contains("..")) throw new ModException("\"" + name + "\" can't be in a skin. Keep every file at the top of the zip.");
                        bool isDetails = name == SkinDetails;
                        if (!isDetails && !(name.EndsWith(".png", StringComparison.Ordinal) && Companion.HasArt("Capri.Sprite." + name))) throw new ModException("\"" + name + "\" isn't one of her sprite strips.");
                        string target = Path.Combine(into, name);
                        long size = Copy(entry, target, isDetails ? Largest : LargestPicture);
                        total += size;
                        if (total > LargestUnpacked) throw new ModException("That skin unpacks too big.");
                        if (isDetails) read = ReadSkin(File.ReadAllText(target));
                        else
                        {
                            CheckPicture(target, name);
                            pictures++;
                        }
                    }
                }
            }
            catch (InvalidDataException) { throw new ModException("That file isn't a valid skin zip."); }
            if (read == null) throw new ModException("A skin needs a " + SkinDetails + " file.");
            if (pictures == 0) throw new ModException("That skin has no pictures in it.");
            return read;
        }

        private static long Copy(ZipArchiveEntry entry, string target, long limit)
        {
            byte[] buffer = new byte[81920];
            long written = 0;
            using (Stream from = entry.Open())
            using (FileStream to = new FileStream(target, FileMode.CreateNew, FileAccess.Write))
            {
                int got;
                while ((got = from.Read(buffer, 0, buffer.Length)) > 0)
                {
                    written += got;
                    if (written > limit) throw new ModException("\"" + entry.FullName + "\" is too big.");
                    to.Write(buffer, 0, got);
                }
            }
            return written;
        }

        private static void CheckPicture(string file, string name)
        {
            byte[] head = new byte[24];
            using (FileStream reading = new FileStream(file, FileMode.Open, FileAccess.Read))
                if (reading.Read(head, 0, 24) != 24) throw new ModException("\"" + name + "\" isn't a PNG.");
            byte[] mark = { 137, 80, 78, 71, 13, 10, 26, 10 };
            for (int i = 0; i < mark.Length; i++) if (head[i] != mark[i]) throw new ModException("\"" + name + "\" isn't a PNG.");
            int width = head[16] << 24 | head[17] << 16 | head[18] << 8 | head[19], height = head[20] << 24 | head[21] << 16 | head[22] << 8 | head[23];
            Size wanted = Companion.ArtSize("Capri.Sprite." + name);
            if (width != wanted.Width || height != wanted.Height) throw new ModException("\"" + name + "\" is " + width + " x " + height + ". It has to match hers, " + wanted.Width + " x " + wanted.Height + ".");
#if CROSS
            try { using (Avalonia.Media.Imaging.Bitmap decoded = new Avalonia.Media.Imaging.Bitmap(file)) if (decoded.PixelSize != wanted) throw new ModException("\"" + name + "\" is the wrong size."); }
#else
            try { using (Bitmap decoded = new Bitmap(file)) if (decoded.Size != wanted) throw new ModException("\"" + name + "\" is the wrong size."); }
#endif
            catch (ArgumentException) { throw new ModException("\"" + name + "\" is a damaged picture."); }
            // GDI also reports damaged skin pictures as out of memory or a drawing error.
            catch (OutOfMemoryException error) { Brain.Log("mods", -1, error.Message); throw new ModException("\"" + name + "\" is a damaged picture."); }
#if !CROSS
            catch (System.Runtime.InteropServices.ExternalException error) { Brain.Log("mods", -1, error.Message); throw new ModException("\"" + name + "\" is a damaged picture."); }
#endif
#if CROSS
            catch (InvalidOperationException) { throw new ModException("\"" + name + "\" is a damaged picture."); }
            catch (NotSupportedException) { throw new ModException("\"" + name + "\" is a damaged picture."); }
#endif
        }

        // Skins stack: turning one on puts it on top of the others, and off takes it out of the stack.
        private void SetSkin(Mod mod, bool on)
        {
            HashSet<string> off = Off();
            if (on) off.Remove(mod.File.Id); else off.Add(mod.File.Id);
            WriteOff(off);
            if (on) OnTop(mod.File.Id);
            mod.On = on;
            Apply();
        }

        private const string StackFile = "stack.txt";

        // Her skins that are on, bottom first, in the order they were put on.
        internal List<Mod> SkinStack
        {
            get
            {
                List<string> order = StackOrder();
                List<Mod> on = All.FindAll(mod => mod.Skin && mod.On);
                on.Sort(delegate(Mod one, Mod other)
                {
                    int a = order.IndexOf(one.File.Id), b = order.IndexOf(other.File.Id);
                    if (a < 0 && b < 0) return string.CompareOrdinal(one.File.Id, other.File.Id);
                    return a < 0 ? 1 : b < 0 ? -1 : a.CompareTo(b);
                });
                return on;
            }
        }

        private List<string> StackOrder()
        {
            string file = Path.Combine(folder, StackFile);
            try { return File.Exists(file) ? new List<string>(File.ReadAllLines(file)) : new List<string>(); }
            catch (IOException) { return new List<string>(); }
            catch (UnauthorizedAccessException) { return new List<string>(); }
        }

        private void OnTop(string id)
        {
            List<string> order = StackOrder();
            order.Remove(id);
            order.Add(id);
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllLines(Path.Combine(folder, StackFile), order.ToArray());
            }
            catch (IOException error) { Brain.Log("mods", -1, error.Message); }
            catch (UnauthorizedAccessException error) { Brain.Log("mods", -1, error.Message); }
        }

        private void RemoveSkin(Mod mod)
        {
            string home = SkinHome(mod.File.Id);
            if (Directory.Exists(home))
            {
                Companion.SkinFolder = null;
                Companion.SkinAccessories = new string[0];
                Directory.Delete(home, true);
            }
        }

        private void ApplySkin()
        {
            string wanted = WantedSkin();
            string colours = string.Join(",", ColoursNow), layers = string.Join("|", WantedAccessories());
            if (wanted != Companion.SkinFolder || lastSkin != wanted || colours != lastColours || layers != string.Join("|", Companion.SkinAccessories) || layers != lastAccessories) Reskin(wanted);
        }

        private string lastSkin, lastAccessories = "";

        // The bottom of her stack is her outfit, drawn whole, unless it's an accessory, which goes over her own look.
        private string WantedSkin()
        {
            List<Mod> stack = SkinStack;
            return stack.Count == 0 || stack[0].File.Accessory ? null : SkinHome(stack[0].File.Id);
        }

        // Every skin above it, bottom first: "=" before an accessory, drawn as it is, and "~" before a whole skin, of which
        // only what it changes from her own art is drawn, so its cat ears or hat go on over the outfit below.
        private string[] WantedAccessories()
        {
            List<Mod> stack = SkinStack;
            List<string> layers = new List<string>();
            for (int i = 0; i < stack.Count; i++)
            {
                if (i == 0 && !stack[0].File.Accessory) continue;
                layers.Add((stack[i].File.Accessory ? "=" : "~") + SkinHome(stack[i].File.Id));
            }
            return layers.ToArray();
        }

        private void Reskin() { Reskin(WantedSkin()); }

        private void Reskin(string wanted)
        {
            Companion.SkinFolder = wanted;
            Companion.SkinAccessories = WantedAccessories();
            lastSkin = wanted;
            lastAccessories = string.Join("|", Companion.SkinAccessories);
            string[] now = ColoursNow;
            lastColours = string.Join(",", now);
            ArtColors.Set(now);
            if (SkinChanged != null) SkinChanged();
        }
    }

    // What a whole skin stacked over another changes from her own art, shared by Windows and the Mac and Linux app: pixels are
    // ARGB ints, unpremultiplied. Clear, unless the skin really changed it: tiny specks of difference are dropped, soft edges
    // next to a change are kept, and holes enclosed by a change are filled, so cat ears come out whole and her outline doesn't.
    internal static class SkinLayers
    {
        internal static int[] Changed(int[] top, int[] under, int width, int height)
        {
            int count = width * height;
            byte[] diff = new byte[count];
            bool[] keep = new bool[count];
            for (int i = 0; i < count; i++)
            {
                int a = top[i], b = under[i];
                int alphaA = (a >> 24) & 255, alphaB = (b >> 24) & 255, most = Math.Abs(alphaA - alphaB);
                for (int shift = 0; shift < 24; shift += 8)
                {
                    int one = ((a >> shift) & 255) * alphaA / 255, two = ((b >> shift) & 255) * alphaB / 255;
                    most = Math.Max(most, Math.Abs(one - two));
                }
                diff[i] = (byte)most;
                keep[i] = most > 40;
            }
            int[] queue = new int[count];
            bool[] seen = new bool[count];
            for (int start = 0; start < count; start++)
            {
                if (!keep[start] || seen[start]) continue;
                int head = 0, tail = 0;
                queue[tail++] = start;
                seen[start] = true;
                while (head < tail)
                {
                    int at = queue[head++], x = at % width, y = at / width;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                            int next = ny * width + nx;
                            if (keep[next] && !seen[next]) { seen[next] = true; queue[tail++] = next; }
                        }
                }
                if (tail < 24) for (int i = 0; i < tail; i++) keep[queue[i]] = false;
            }
            bool[] strong = (bool[])keep.Clone();
            for (int pass = 0; pass < 2; pass++)
            {
                bool[] grown = (bool[])keep.Clone();
                for (int at = 0; at < count; at++)
                {
                    if (keep[at] || diff[at] <= 10 || ((top[at] >> 24) & 255) == 0) continue;
                    int x = at % width, y = at / width;
                    for (int dy = -1; dy <= 1 && !grown[at]; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx >= 0 && ny >= 0 && nx < width && ny < height && keep[ny * width + nx]) { grown[at] = true; break; }
                        }
                }
                keep = grown;
            }
            Array.Clear(seen, 0, count);
            for (int start = 0; start < count; start++)
            {
                if (strong[start] || seen[start]) continue;
                int head = 0, tail = 0;
                bool edge = false;
                long total = 0;
                queue[tail++] = start;
                seen[start] = true;
                while (head < tail)
                {
                    int at = queue[head++], x = at % width, y = at / width;
                    total += diff[at];
                    if (x == 0 || y == 0 || x == width - 1 || y == height - 1) edge = true;
                    if (x > 0 && !strong[at - 1] && !seen[at - 1]) { seen[at - 1] = true; queue[tail++] = at - 1; }
                    if (x < width - 1 && !strong[at + 1] && !seen[at + 1]) { seen[at + 1] = true; queue[tail++] = at + 1; }
                    if (y > 0 && !strong[at - width] && !seen[at - width]) { seen[at - width] = true; queue[tail++] = at - width; }
                    if (y < height - 1 && !strong[at + width] && !seen[at + width]) { seen[at + width] = true; queue[tail++] = at + width; }
                }
                if (!edge && total > 16L * tail) for (int i = 0; i < tail; i++) keep[queue[i]] = true;
            }
            int[] result = new int[count];
            for (int i = 0; i < count; i++) if (keep[i] && ((top[i] >> 24) & 255) > 0) result[i] = top[i];
            return result;
        }
    }

#if !CROSS
    internal sealed partial class Companion
    {
        internal static volatile string SkinFolder;
        internal static volatile string[] SkinAccessories = new string[0];

        internal static Size ArtSize(string name)
        {
            using (Stream image = Assembly2.GetManifestResourceStream(name))
            using (Bitmap art = new Bitmap(image))
                return art.Size;
        }

        private static readonly System.Reflection.Assembly Assembly2 = System.Reflection.Assembly.GetExecutingAssembly();

        private const string SpriteLead = "Capri.Sprite.";

        // Her skin's picture for name, or null to use her own, not yet coloured.
        private static Bitmap SkinArt(string name)
        {
            string skin = SkinFolder;
            if (skin == null || !name.StartsWith(SpriteLead, StringComparison.Ordinal)) return null;
            return SkinPicture(Path.Combine(skin, name.Substring(SpriteLead.Length)), name);
        }

        private static Bitmap SkinPicture(string file, string name)
        {
            try
            {
                if (!File.Exists(file)) return null;
                using (MemoryStream bytes = new MemoryStream(File.ReadAllBytes(file)))
                using (Bitmap original = new Bitmap(bytes))
                    return new Bitmap(original);
            }
            catch (Exception error)
            {
                // An installed picture can be damaged or replaced before her art is reloaded.
                if (!(error is IOException || error is UnauthorizedAccessException || error is ArgumentException || error is OutOfMemoryException || error is System.Runtime.InteropServices.ExternalException)) throw;
                Brain.Log("mods", -1, "Skin picture " + name + ": " + error.Message);
                return null;
            }
        }

        // Each accessory that has this strip is drawn over it, like cat ears over whichever outfit she has on. Her colours go on after,
        // so an accessory drawn in her hair colour follows My Colors.
        private static Bitmap Dressed(Bitmap original, string name)
        {
            string[] layers = SkinAccessories;
            if (layers.Length == 0 || !name.StartsWith(SpriteLead, StringComparison.Ordinal)) return original;
            Bitmap layered = null;
            foreach (string entry in layers)
            {
                bool whole = entry.StartsWith("~", StringComparison.Ordinal);
                string folder = entry.TrimStart('=', '~');
                using (Bitmap layer = whole ? Changes(folder, name) : SkinPicture(Path.Combine(folder, name.Substring(SpriteLead.Length)), name))
                {
                    if (layer == null || layer.Size != original.Size) continue;
                    if (layered == null)
                    {
                        layered = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppArgb);
                        using (Graphics paper = Graphics.FromImage(layered))
                        {
                            paper.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                            paper.DrawImage(original, new Rectangle(0, 0, original.Width, original.Height));
                        }
                    }
                    using (Graphics paper = Graphics.FromImage(layered)) paper.DrawImage(layer, new Rectangle(0, 0, layer.Width, layer.Height));
                }
            }
            if (layered == null) return original;
            original.Dispose();
            return layered;
        }

        // What a whole skin stacked over another changes from her own art, worked out once per strip and kept as a small PNG.
        private static readonly Dictionary<string, byte[]> changes = new Dictionary<string, byte[]>();

        private static Bitmap Changes(string folder, string name)
        {
            string file = Path.Combine(folder, name.Substring(SpriteLead.Length));
            string key;
            try { if (!File.Exists(file)) return null; key = file + "|" + File.GetLastWriteTimeUtc(file).Ticks; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            byte[] kept;
            lock (changes) changes.TryGetValue(key, out kept);
            if (kept == null)
            {
                using (Bitmap skin = SkinPicture(file, name))
                using (Stream stream = Assembly2.GetManifestResourceStream(name))
                {
                    if (skin == null || stream == null) return null;
                    using (Bitmap own = new Bitmap(stream))
                    using (Bitmap made = Changed(skin, own))
                    using (MemoryStream png = new MemoryStream())
                    {
                        made.Save(png, ImageFormat.Png);
                        kept = png.ToArray();
                    }
                }
                lock (changes) changes[key] = kept;
            }
            using (MemoryStream bytes = new MemoryStream(kept))
            using (Bitmap read = new Bitmap(bytes))
                return new Bitmap(read);
        }

        private static int[] Pixels(Bitmap image)
        {
            Rectangle all = new Rectangle(0, 0, image.Width, image.Height);
            int[] pixels = new int[image.Width * image.Height];
            using (Bitmap plain = image.PixelFormat == PixelFormat.Format32bppArgb ? null : new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb))
            {
                Bitmap source = image;
                if (plain != null)
                {
                    using (Graphics paper = Graphics.FromImage(plain))
                    {
                        paper.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                        paper.DrawImage(image, all);
                    }
                    source = plain;
                }
                BitmapData data = source.LockBits(all, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try { System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length); }
                finally { source.UnlockBits(data); }
            }
            return pixels;
        }

        // The pixels of skin that clearly differ from own: specks under 24 pixels dropped, a soft edge two pixels out where it
        // still differs a little, and enclosed holes filled only where they changed too, so ears drawn over her hair keep the
        // outfit's hair inside them but a hat close to her hair colour stays solid. The same rules as the Skin Kit's.
        private static Bitmap Changed(Bitmap skin, Bitmap own)
        {
            int width = skin.Width, height = skin.Height, count = width * height;
            int[] result = SkinLayers.Changed(Pixels(skin), Pixels(own), width, height);
            Bitmap made = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            BitmapData data = made.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try { System.Runtime.InteropServices.Marshal.Copy(result, 0, data.Scan0, count); }
            finally { made.UnlockBits(data); }
            return made;
        }

        internal static Bitmap Finished(Bitmap original)
        {
            using (Bitmap coloured = ArtColors.Apply(original)) return Premultiplied(coloured ?? original);
        }

        private static Bitmap Premultiplied(Bitmap original)
        {
            Bitmap copy = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(copy))
            {
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                graphics.DrawImage(original, new Rectangle(0, 0, copy.Width, copy.Height));
            }
            return copy;
        }

        // How she looks standing right now, outfit, accessories and colours included, for her Wardrobe.
        internal Bitmap LookNow()
        {
            Bitmap strip = idle.Strip;
            int width = strip.Width / Math.Max(1, idle.Count);
            return strip.Clone(new Rectangle(0, 0, width, strip.Height), strip.PixelFormat);
        }

        private void Reskinned()
        {
            Clip.ReloadAll();
            drawnClip = null;
            if (IsHandleCreated && !IsDisposed) RenderFrame();
        }

        private void CheckSkins(List<string> checks, string output)
        {
            Mods mods = ModList;
            Func<string, Color, Dictionary<string, byte[]>, string> pack = delegate(string id, Color paint, Dictionary<string, byte[]> extra)
            {
                string file = Path.Combine(output, id + Mods.SkinExtension);
                if (File.Exists(file)) File.Delete(file);
                using (ZipArchive zip = ZipFile.Open(file, ZipArchiveMode.Create))
                {
                    if (id != "nodetails") Put(zip, "skin.json", Encoding.UTF8.GetBytes("{\"format\":1,\"id\":\"" + id + "\",\"name\":\"" + id + "\"}"));
                    if (paint != Color.Empty)
                        using (Bitmap flat = new Bitmap(idle.Strip.Width, idle.Strip.Height, PixelFormat.Format32bppArgb))
                        using (MemoryStream made = new MemoryStream())
                        {
                            using (Graphics g = Graphics.FromImage(flat)) g.Clear(paint);
                            flat.Save(made, ImageFormat.Png);
                            Put(zip, "idle.png", made.ToArray());
                        }
                    if (extra != null) foreach (KeyValuePair<string, byte[]> entry in extra) Put(zip, entry.Key, entry.Value);
                }
                return file;
            };
            {
                    {
                    }
            }
            Func<Color> shown = delegate { return idle.Strip.GetPixel(0, 0); };
            Color original = shown();
            string red = pack("skin-red", Color.Red, null), blue = pack("skin-blue", Color.Blue, null);
            try
            {
                if (mods.Import(red, null) == null || shown().R != 255 || shown().B != 0) throw new Exception("Importing a skin didn't change her art.");
                Bitmap untouched = walk.Strip;
                if (untouched.GetPixel(0, 0) == Color.Red) throw new Exception("A skin changed a strip it doesn't hold.");
                if (mods.Import(blue, null) == null || shown().B != 255) throw new Exception("A second skin didn't go on top.");
                Mod redMod = mods.All.Find(mod => mod.File.Id == "skin-red"), blueMod = mods.All.Find(mod => mod.File.Id == "skin-blue");
                if (!redMod.On || !blueMod.On || mods.SkinStack.Count != 2 || mods.SkinStack[1] != blueMod) throw new Exception("Two skins didn't stack, newest on top.");
                mods.Set(redMod, false, null);
                if (shown().B != 255 || mods.SkinStack.Count != 1) throw new Exception("Turning the bottom skin off didn't leave the other.");
                mods.Set(redMod, true, null);
                if (shown().R != 255 || mods.SkinStack[1] != redMod) throw new Exception("Turning a skin on again didn't put it on top.");
                mods.Set(redMod, false, null);
                mods.Set(blueMod, false, null);
                if (shown() != original) throw new Exception("Turning every skin off didn't bring her art back.");
                mods.Set(blueMod, true, null);
                mods.Set(redMod, true, null);
                mods.Load();
                redMod = mods.All.Find(mod => mod.File.Id == "skin-red");
                blueMod = mods.All.Find(mod => mod.File.Id == "skin-blue");
                if (shown().R != 255 || mods.SkinStack.Count != 2 || mods.SkinStack[1] != redMod) throw new Exception("The stack's order didn't last a reload.");
                checks.Add("PASS skins: several stack with the newest on top, on again moves one to the top, off leaves the rest, and the order lasts a reload");
                mods.Set(redMod, false, null);
                string patched = Path.Combine(output, "skin-patch" + Mods.SkinExtension);
                if (File.Exists(patched)) File.Delete(patched);
                using (ZipArchive zip = ZipFile.Open(patched, ZipArchiveMode.Create))
                using (Stream own = Assembly2.GetManifestResourceStream("Capri.Sprite.idle.png"))
                using (Bitmap art = new Bitmap(own))
                using (Bitmap copy = new Bitmap(art.Width, art.Height, PixelFormat.Format32bppArgb))
                using (MemoryStream made = new MemoryStream())
                {
                    Put(zip, "skin.json", Encoding.UTF8.GetBytes("{\"format\":1,\"id\":\"skin-patch\",\"name\":\"skin-patch\"}"));
                    using (Graphics paper = Graphics.FromImage(copy))
                    {
                        paper.DrawImage(art, new Rectangle(0, 0, art.Width, art.Height));
                        paper.FillRectangle(Brushes.Lime, 100, 40, 24, 24);
                    }
                    copy.Save(made, ImageFormat.Png);
                    Put(zip, "idle.png", made.ToArray());
                }
                Mod patch = mods.Import(patched, null);
                Color patchShown = idle.Strip.GetPixel(110, 50), outfit = idle.Strip.GetPixel(0, 0), hair = idle.Strip.GetPixel(95, 90);
                if (patch == null || patchShown.G != 255 || patchShown.R != 0 || outfit.B != 255 || hair.B != 255 || hair.R != 0) throw new Exception("A whole skin stacked over another didn't add only what it changes: " + patchShown + " " + outfit + " " + hair);
                mods.Remove(patch);
                mods.Set(mods.All.Find(mod => mod.File.Id == "skin-blue"), false, null);
                mods.Set(mods.All.Find(mod => mod.File.Id == "skin-red"), true, null);
                redMod = mods.All.Find(mod => mod.File.Id == "skin-red");
                if (shown().R != 255 || mods.SkinStack.Count != 1) throw new Exception("The stack didn't settle back to one skin.");
                checks.Add("PASS skins: a whole skin stacked over another adds only what it changes from her own art, so the outfit under it shows everywhere else");
                Func<string, Color, string> accessory = delegate(string id, Color paint)
                {
                    string file = Path.Combine(output, id + Mods.SkinExtension);
                    if (File.Exists(file)) File.Delete(file);
                    using (ZipArchive zip = ZipFile.Open(file, ZipArchiveMode.Create))
                    {
                        Put(zip, "skin.json", Encoding.UTF8.GetBytes("{\"format\":1,\"id\":\"" + id + "\",\"name\":\"" + id + "\",\"accessory\":true}"));
                        using (Bitmap clear = new Bitmap(idle.Strip.Width, idle.Strip.Height, PixelFormat.Format32bppArgb))
                        using (MemoryStream made = new MemoryStream())
                        {
                            using (Graphics g = Graphics.FromImage(clear)) using (Brush brush = new SolidBrush(paint)) g.FillRectangle(brush, 0, 0, 8, 8);
                            clear.Save(made, ImageFormat.Png);
                            Put(zip, "idle.png", made.ToArray());
                        }
                    }
                    return file;
                };
                Func<int, Color> at = delegate(int spot) { return idle.Strip.GetPixel(spot, spot); };
                Mod ears = mods.Import(accessory("skin-ears", Color.Lime), null);
                redMod = mods.All.Find(mod => mod.File.Id == "skin-red");
                if (ears == null || !ears.On || !redMod.On || at(1).G != 255 || at(1).R != 0 || at(20).R != 255) throw new Exception("An accessory didn't draw over the outfit under it, or took the outfit off.");
                mods.Set(mods.All.Find(mod => mod.File.Id == "skin-blue"), true, null);
                if (at(1).B != 255 || at(20).B != 255) throw new Exception("A skin turned on over an accessory didn't go on top of it.");
                Mod earsAgain = mods.All.Find(mod => mod.File.Id == "skin-ears");
                mods.Set(earsAgain, false, null);
                mods.Set(earsAgain, true, null);
                if (at(1).G != 255 || at(20).B != 255) throw new Exception("Turning an accessory on again didn't put it back on top.");
                Mod hat = mods.Import(accessory("skin-hat", Color.Yellow), null);
                if (hat == null || at(1).R != 255 || at(1).G != 255 || at(1).B != 0) throw new Exception("The last accessory isn't on top.");
                mods.Set(hat, false, null);
                mods.Set(mods.All.Find(mod => mod.File.Id == "skin-blue"), false, null);
                if (at(1).G != 255 || at(1).R != 0 || at(20).A == 255 && at(20).B == 255 && at(20).R == 0) throw new Exception("An accessory didn't draw over her own art.");
                mods.Load();
                if (at(1).G != 255 || mods.All.Find(mod => mod.File.Id == "skin-hat").On) throw new Exception("Accessories didn't stay as they were after a reload.");
                foreach (Mod layer in mods.All.ToArray()) if (layer.File.Accessory) mods.Remove(layer);
                if (SkinAccessories.Length != 0 || at(1).G == 255 && at(1).R == 0 || Directory.Exists(Path.Combine(mods.Folder, "skin-ears-skin"))) throw new Exception("Removing accessories left them drawn.");
                mods.Set(mods.All.Find(mod => mod.File.Id == "skin-red"), true, null);
                checks.Add("PASS skins: an accessory draws as it is over whatever is under it in the stack, or her own art, the newest is on top, the order lasts a reload, and removing it takes it away");
                using (Bitmap wrong = new Bitmap(10, 10))
                using (MemoryStream made = new MemoryStream())
                {
                    wrong.Save(made, ImageFormat.Png);
                    string[,] cases = { { "skin-size", "idle.png", "match hers" }, { "skin-unknown", "evil.png", "sprite strips" }, { "skin-path", "..\\idle.png", "top of the zip" }, { "skin-deep", "x/idle.png", "top of the zip" } };
                    for (int i = 0; i < cases.GetLength(0); i++)
                    {
                        string bad = pack(cases[i, 0], Color.Empty, new Dictionary<string, byte[]> { { cases[i, 1], made.ToArray() } });
                        try { mods.Import(bad, null); throw new Exception("A bad skin was let in: " + cases[i, 0]); }
                        catch (ModException refused) { if (refused.Message.IndexOf(cases[i, 2], StringComparison.Ordinal) < 0) throw new Exception("A bad skin gave the wrong reason: " + refused.Message); }
                    }
                }
                string[] refusedOthers = { pack("nodetails", Color.Green, null), pack("skin-empty", Color.Empty, null), pack("skin-fake", Color.Empty, new Dictionary<string, byte[]> { { "idle.png", Encoding.UTF8.GetBytes("this is not a picture at all, sorry") } }) };
                foreach (string bad in refusedOthers)
                {
                    try { mods.Import(bad, null); throw new Exception("A bad skin was let in: " + bad); }
                    catch (ModException) { }
                }
                string junk = Path.Combine(output, "junk" + Mods.SkinExtension);
                File.WriteAllText(junk, "not a zip");
                try { mods.Import(junk, null); throw new Exception("A file that isn't a zip was let in."); }
                catch (ModException) { }
                if (Directory.GetDirectories(mods.Folder, "skin-import-*").Length != 0 || mods.All.FindAll(mod => mod.Skin).Count != 2) throw new Exception("A refused skin left something behind.");
                checks.Add("PASS skins: wrong sizes, unknown names, paths out of the folder, fake pictures and broken zips are refused and leave nothing behind");
                foreach (Mod left in mods.All.ToArray()) mods.Remove(left);
                if (shown() != original || Directory.GetDirectories(mods.Folder, "skin-*").Length != 0) throw new Exception("Removing skins left them behind.");
                checks.Add("PASS skins: removing a skin deletes it and restores her art");
                string real = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath), "..", "..", "mods", "maid-suit" + Mods.SkinExtension));
                if (!File.Exists(real)) { checks.Add("LIMIT skins: the Maid Suit file isn't beside this build, so it wasn't imported"); return; }
                Mod maid = mods.Import(real, null);
                if (maid == null || shown() == original && idle.Strip.GetPixel(120, 240) == original) throw new Exception("The Maid Suit didn't import.");
                using (Bitmap sample = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb))
                using (Graphics paper = Graphics.FromImage(sample))
                {
                    Draw(paper, idle, 0);
                    sample.Save(Path.Combine(output, "maid-in-app.png"), ImageFormat.Png);
                }
                mods.Remove(maid);
                checks.Add("PASS skins: the Maid Suit imports, every strip fits, and it draws in the real renderer");
            }
            finally
            {
                foreach (Mod left in mods.All.ToArray()) if (left.Skin) mods.Remove(left);
            }
        }

        private static void Put(ZipArchive zip, string name, byte[] bytes)
        {
            using (Stream into = zip.CreateEntry(name).Open()) into.Write(bytes, 0, bytes.Length);
        }
    }
#endif
}

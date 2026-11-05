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
                foreach (Mod other in All) if (other.Skin && other.File.Id != read.Id) off.Add(other.File.Id);
                off.Remove(read.Id);
                WriteOff(off);
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

        private void SetSkin(Mod mod, bool on)
        {
            HashSet<string> off = Off();
            if (on)
            {
                off.Remove(mod.File.Id);
                foreach (Mod other in All)
                {
                    if (!other.Skin || other == mod) continue;
                    off.Add(other.File.Id);
                    other.On = false;
                }
            }
            else off.Add(mod.File.Id);
            WriteOff(off);
            mod.On = on;
            Apply();
        }

        private void RemoveSkin(Mod mod)
        {
            string home = SkinHome(mod.File.Id);
            if (Directory.Exists(home))
            {
                Companion.SkinFolder = null;
                Directory.Delete(home, true);
            }
        }

        private void ApplySkin()
        {
            string wanted = null;
            foreach (Mod mod in All) if (mod.Skin && mod.On) wanted = SkinHome(mod.File.Id);
            string colours = string.Join(",", ColoursNow);
            if (wanted != Companion.SkinFolder || lastSkin != wanted || colours != lastColours) Reskin(wanted);
        }

        private string lastSkin;

        private void Reskin() { string wanted = null; foreach (Mod mod in All) if (mod.Skin && mod.On) wanted = SkinHome(mod.File.Id); Reskin(wanted); }

        private void Reskin(string wanted)
        {
            Companion.SkinFolder = wanted;
            lastSkin = wanted;
            string[] now = ColoursNow;
            lastColours = string.Join(",", now);
            ArtColors.Set(now);
            if (SkinChanged != null) SkinChanged();
        }
    }

#if !CROSS
    internal sealed partial class Companion
    {
        internal static volatile string SkinFolder;

        internal static Size ArtSize(string name)
        {
            using (Stream image = Assembly2.GetManifestResourceStream(name))
            using (Bitmap art = new Bitmap(image))
                return art.Size;
        }

        private static readonly System.Reflection.Assembly Assembly2 = System.Reflection.Assembly.GetExecutingAssembly();

        private static Bitmap SkinArt(string name)
        {
            string skin = SkinFolder;
            const string Lead = "Capri.Sprite.";
            if (skin == null || !name.StartsWith(Lead, StringComparison.Ordinal)) return null;
            string file = Path.Combine(skin, name.Substring(Lead.Length));
            try
            {
                if (!File.Exists(file)) return null;
                using (MemoryStream bytes = new MemoryStream(File.ReadAllBytes(file)))
                using (Bitmap original = new Bitmap(bytes))
                    return Finished(original);
            }
            catch (Exception error)
            {
                // An installed picture can be damaged or replaced before her art is reloaded.
                if (!(error is IOException || error is UnauthorizedAccessException || error is ArgumentException || error is OutOfMemoryException || error is System.Runtime.InteropServices.ExternalException)) throw;
                Brain.Log("mods", -1, "Skin picture " + name + ": " + error.Message);
                return null;
            }
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
            Func<Color> shown = delegate { return idle.Strip.GetPixel(0, 0); };
            Color original = shown();
            string red = pack("skin-red", Color.Red, null), blue = pack("skin-blue", Color.Blue, null);
            try
            {
                if (mods.Import(red, null) == null || shown().R != 255 || shown().B != 0) throw new Exception("Importing a skin didn't change her art.");
                Bitmap untouched = walk.Strip;
                if (untouched.GetPixel(0, 0) == Color.Red) throw new Exception("A skin changed a strip it doesn't hold.");
                if (mods.Import(blue, null) == null || shown().B != 255) throw new Exception("A second skin didn't take over.");
                Mod redMod = mods.All.Find(mod => mod.File.Id == "skin-red"), blueMod = mods.All.Find(mod => mod.File.Id == "skin-blue");
                if (redMod.On || !blueMod.On) throw new Exception("Two skins were on at once.");
                mods.Set(redMod, true, null);
                if (blueMod.On || shown().R != 255) throw new Exception("Switching skins didn't work.");
                mods.Set(redMod, false, null);
                if (shown() != original) throw new Exception("Turning a skin off didn't bring her art back.");
                mods.Set(redMod, true, null);
                mods.Load();
                if (shown().R != 255) throw new Exception("A skin that was on didn't stay on after a reload.");
                checks.Add("PASS skins: importing, swapping, off and reload all change her art straight away, and only one is on");
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

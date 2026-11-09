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
        // Skins made before her artist's standing set hold idle.png in her old 16 cells of 240 by 384, and they still draw in those.
        internal const int OldIdleWide = 3840, OldIdleHigh = 384;
        // Legacy stand's hover tip, the same on every platform: Lachy's line, his spelling, and nothing else.
        internal const string LegacyTip = "Incase it broke your mod, soz!";
        // Legacy stand's art is her own strip from 0.8.5, in those same old cells. Skins are read first: a skin's idle_legacy.png
        // for Legacy stand, so one skin can carry both stands, then its idle.png, which wins over hers either way.
        internal static string StandArt(string name, bool legacy) { return legacy && name == "Capri.Sprite.idle.png" ? "Capri.Sprite.idle_legacy.png" : name; }
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

        // The version a mod file says it is, without importing it, or null when it has none or can't be read.
        internal static string VersionIn(string file)
        {
            try
            {
                string extension = Path.GetExtension(file);
                if (string.Equals(extension, SkinExtension, StringComparison.OrdinalIgnoreCase))
                    using (ZipArchive zip = ZipFile.OpenRead(file))
                    {
                        ZipArchiveEntry details = zip.GetEntry(SkinDetails);
                        if (details == null || details.Length > Largest) return null;
                        using (StreamReader reading = new StreamReader(details.Open(), Encoding.UTF8)) return ReadJson(reading.ReadToEnd()).Version;
                    }
                if (new FileInfo(file).Length > 4 * 1024 * 1024) return null;
                string text = File.ReadAllText(file, Encoding.UTF8);
#if !CROSS
                if (string.Equals(extension, CodeExtension, StringComparison.OrdinalIgnoreCase)) return ReadCode(text).Version;
#endif
                if (string.Equals(extension, SkillExtension, StringComparison.OrdinalIgnoreCase)) return ReadSkill(text).Version;
                if (string.Equals(extension, Extension, StringComparison.OrdinalIgnoreCase)) return ReadJson(text).Version;
            }
            catch (Exception error)
            {
                if (!(error is ModException || error is IOException || error is UnauthorizedAccessException || error is InvalidDataException || error is System.Runtime.Serialization.SerializationException)) throw;
            }
            return null;
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
            Size wanted = name == "idle.png" && width == OldIdleWide && height == OldIdleHigh ? new Size(width, height) : Companion.ArtSize("Capri.Sprite." + name);
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
        internal static volatile bool LegacyArt;

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
            blinkWith = null;
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
                // The middle of her first cell, in her old 240-wide cells or her new 116-wide ones.
                if (maid == null || shown() == original && idle.Strip.GetPixel(idle.Strip.Width / idle.Count / 2, 240) == original) throw new Exception("The Maid Suit didn't import.");
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

        // Her drawn body at one cell, in the real renderer, standing still.
        private Bitmap Stood(Clip shown, int cell, int talk = -1)
        {
            Bitmap drawn = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
            using (Graphics paper = Graphics.FromImage(drawn)) Draw(paper, shown, cell, talk);
            return drawn;
        }

        // The last row solid pixels reach, and where they balance across, in her window.
        private static void Footing(Bitmap drawn, out int floor, out double middle)
        {
            int[] pixels = new int[drawn.Width * drawn.Height];
            Pixels(drawn, new Rectangle(0, 0, drawn.Width, drawn.Height), pixels, false);
            floor = -1;
            long across = 0, solid = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (((uint)pixels[i] >> 24) <= 128) continue;
                int x = i % drawn.Width;
                floor = Math.Max(floor, i / drawn.Width);
                across += x;
                solid++;
            }
            middle = solid == 0 ? -1 : (double)across / solid;
        }

        // How many of a cell's pixels differ from another's, and the box they sit in, ignoring what palettes round away.
        private static int Changed(Bitmap one, int cellOne, Bitmap two, int cellTwo, int wide, out Rectangle box)
        {
            int[] a = new int[wide * one.Height], b = new int[wide * one.Height];
            Pixels(one, new Rectangle(cellOne * wide, 0, wide, one.Height), a, false);
            Pixels(two, new Rectangle(cellTwo * wide, 0, wide, one.Height), b, false);
            int left = wide, top = one.Height, right = -1, bottom = -1, count = 0;
            for (int i = 0; i < a.Length; i++)
            {
                int far = 0;
                for (int shift = 0; shift < 32; shift += 8) far = Math.Max(far, Math.Abs((int)(((uint)a[i] >> shift) & 255) - (int)(((uint)b[i] >> shift) & 255)));
                if (far <= 40) continue;
                count++;
                left = Math.Min(left, i % wide); right = Math.Max(right, i % wide);
                top = Math.Min(top, i / wide); bottom = Math.Max(bottom, i / wide);
            }
            box = count == 0 ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
            return count;
        }

        private static List<string> Chunks(string resource)
        {
            List<string> kinds = new List<string>();
            using (Stream image = Assembly2.GetManifestResourceStream(resource))
            using (BinaryReader reading = new BinaryReader(image))
            {
                reading.ReadBytes(8);
                while (image.Position < image.Length)
                {
                    byte[] head = reading.ReadBytes(8);
                    int length = head[0] << 24 | head[1] << 16 | head[2] << 8 | head[3];
                    kinds.Add(Encoding.ASCII.GetString(head, 4, 4));
                    image.Seek(length + 4, SeekOrigin.Current);
                }
            }
            return kinds;
        }

        private const string LegacySha = "ed63f8c677afa9c2be8b76e850547882b88fa76ea9206e3d20f758cce65ed2a0";

        // Legacy stand: off to start, her 0.8.5 strip byte for byte, a live swap each way through its row and her tool, saved, drawn
        // the way she stood in 0.8.5, colours on it, and a skin's own idle over it.
        private void CheckLegacyStand(List<string> checks, string output)
        {
            Mods mods = ModList;
            bool motionWas = preferences.Motion, leftWas = facingLeft, veiledWas = veiled, legacyWas = preferences.LegacyStand;
            double veilWas = veilUntil, leanWas = leanAngle, glitchWas = nextGlitch;
            Clip clipWas = clip;
            Ledge seatWas = seat;
            bool ridingWas = riding, draggingWas = dragging, walkingWas = walking, fallingWas = falling, busyWas = frontBusy;
            string speechWas = speech;
            try
            {
                seat = null;
                riding = dragging = walking = falling = frontBusy = false;
                speech = null;
                preferences.Motion = false;
                veiled = false;
                veilUntil = -1;
                leanAngle = 0;
                nextGlitch = double.MaxValue;
                facingLeft = false;
                if (preferences.LegacyStand) ToggleLegacyStand();
                Play(idle);

                Preferences older;
                using (MemoryStream saved = new MemoryStream(Encoding.UTF8.GetBytes("{}"))) older = (Preferences)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Preferences)).ReadObject(saved);
                if (new Preferences().LegacyStand || older.LegacyStand || LegacyStand || mainframe.LegacyStandShown || LegacyArt) throw new Exception("Legacy stand didn't start off.");
                if (idle.Old || dance.Old || idle.Count != 60 || idle.Fps != 24 || idle.ShiftX != IdleShiftX || !idle.HasFlipped) throw new Exception("With Legacy stand off she wasn't on her new stand.");
                using (Stream strip = Assembly2.GetManifestResourceStream("Capri.Sprite.idle_legacy.png"))
                using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
                    if (strip == null || BitConverter.ToString(sha.ComputeHash(strip)).Replace("-", "").ToLowerInvariant() != LegacySha) throw new Exception("Legacy stand's strip isn't 0.8.5's, byte for byte.");
                if (mainframe.LegacyStandTip != "Incase it broke your mod, soz!" || mainframe.LegacyStandTip != Mods.LegacyTip) throw new Exception("Legacy stand's tip isn't his line: " + mainframe.LegacyStandTip);
                checks.Add("PASS legacy stand: off by default in new and older settings, 0.8.5's strip byte for byte in the build, and its tip is exactly his line");

                using (Bitmap plain = Stood(idle, 29)) plain.Save(Path.Combine(output, "new-right.png"), ImageFormat.Png);
                int newFloor;
                double newMiddle;
                using (Bitmap plain = Stood(idle, 0)) Footing(plain, out newFloor, out newMiddle);
                System.Diagnostics.Stopwatch swap = System.Diagnostics.Stopwatch.StartNew();
                mainframe.FlipLegacyStand();
                swap.Stop();
                long swapOn = swap.ElapsedMilliseconds;
                if (!LegacyStand || !LegacyArt || !mainframe.LegacyStandShown || !LoadPreferences().LegacyStand) throw new Exception("Legacy stand's row didn't turn it on and save it.");
                // A restart with it on: her clips come up in her own art, then startup puts it back.
                LegacyArt = false;
                Clip.ReloadAll();
                if (idle.Old || idle.Count != 60) throw new Exception("Her clips didn't go back to her own art for the restart.");
                ArtAtStart();
                if (!LegacyArt) throw new Exception("A restart with Legacy stand on lost it.");
                if (!idle.Old || !dance.Old || idle.Count != 16 || idle.Fps != 8 || dance.Count != 16 || dance.Fps != 8 || idle.ShiftX != 0 || idle.ShiftY != 0 || idle.Strip.Width != Mods.OldIdleWide || idle.Strip.Height != Mods.OldIdleHigh)
                    throw new Exception("Legacy stand didn't swap her live to 16 cells at 8 fps: " + idle.Count + " at " + idle.Fps + ".");
                if (drawnClip != null && !IsHandleCreated) throw new Exception("Legacy stand left an old frame up.");
                Color hairOld = idle.Strip.GetPixel(95, 90);
                if (hairOld.A != 255 || hairOld.R != 251 || hairOld.G != 0 || hairOld.B != 72) throw new Exception("Legacy stand wasn't 0.8.5's strip: " + hairOld);
                if (idle.HasFlipped || idle.Flipped != null || idle.FlippedNow != null) throw new Exception("Legacy stand wore her new facing-left art.");
                int oldFloor;
                double oldMiddle;
                Rectangle area;
                using (Bitmap cell = Stood(idle, 5))
                {
                    Footing(cell, out oldFloor, out oldMiddle);
                    area = SpriteBounds(cell, 1);
                    cell.Save(Path.Combine(output, "legacy-right.png"), ImageFormat.Png);
                }
                if (Math.Abs(oldFloor - newFloor) > 2 || Math.Abs(oldMiddle - newMiddle) > 6) throw new Exception("Legacy stand's feet or body moved off her new stand's: " + oldFloor + ", " + oldMiddle + " against " + newFloor + ", " + newMiddle);
                facingLeft = true;
                if (Art(idle) != idle.Strip) throw new Exception("Facing left, Legacy stand didn't draw its own art.");
                using (Bitmap cell = Stood(idle, 5))
                {
                    Rectangle mirrored = SpriteBounds(cell, 1);
                    if (Math.Abs(Width - mirrored.Right - area.Left) > 2) throw new Exception("Facing left, Legacy stand didn't mirror: " + mirrored + " against " + area);
                    cell.Save(Path.Combine(output, "legacy-left.png"), ImageFormat.Png);
                }
                facingLeft = false;
                Play(idle);
                if (Blinks(false) != blink || Blinks(true) != blinkGlitch) throw new Exception("Legacy stand lost her old blink.");
                preferences.Motion = true;
                clipStart = clock.Elapsed.TotalSeconds - (BlinkFrom + 0.5) / idle.Fps;
                blinkWith = blink;
                idleCycle = 0;
                if (Advance(clock.Elapsed.TotalSeconds) != BlinkFrom) throw new Exception("Legacy stand's loop didn't run at 8 fps.");
                preferences.Motion = false;
                using (Bitmap shut = Stood(blink, 1)) shut.Save(Path.Combine(output, "legacy-blink.png"), ImageFormat.Png);
                using (Bitmap quiet = Stood(idle, 5))
                using (Bitmap saying = Stood(idle, 5, 3))
                {
                    Rectangle jaw;
                    if (Changed(quiet, 0, saying, 0, quiet.Width, out jaw) < 20) throw new Exception("Legacy stand lost her old mouth.");
                    int jawRow = PaddingSize + (int)(preferences.Size * (0.5f + (130 - 192) * 1.11f / 384));
                    if (Math.Abs(jaw.Top - jawRow) > 14) throw new Exception("Legacy stand's mouth moved: " + jaw + " against row " + jawRow);
                    saying.Save(Path.Combine(output, "legacy-talk.png"), ImageFormat.Png);
                }
                frontBusy = true;
                if (Resting(clock.Elapsed.TotalSeconds) || FrameInterval(clock.Elapsed.TotalSeconds) != 115) throw new Exception("Legacy stand lost her old pace over a game.");
                frontBusy = false;
                swap.Restart();
                mainframe.FlipLegacyStand();
                swap.Stop();
                if (LegacyStand || LegacyArt || mainframe.LegacyStandShown || LoadPreferences().LegacyStand) throw new Exception("Legacy stand's row didn't turn it off and save it.");
                if (idle.Old || dance.Old || idle.Count != 60 || idle.Fps != 24 || dance.Count != 60 || idle.ShiftX != IdleShiftX || !idle.HasFlipped) throw new Exception("Legacy stand off didn't bring her new stand back: " + idle.Count + " at " + idle.Fps + ".");
                Play(idle);
                if (Blinks(false) != idleBlink || Blinks(true) != idleBlinkGlitch) throw new Exception("Legacy stand off didn't bring her new blink back.");
                checks.Add("PASS legacy stand: its row swaps her live to 16 cells at 8 fps in 0.8.5's place, mirrored facing left, with the old blink, mouth and pace, and back to 60 at 24 fps, saving each way and coming back after a restart, in " + swapOn + " ms on and " + swap.ElapsedMilliseconds + " ms off");

                string turned = Hands.Do("capri_settings", new ToolArguments { Action = "on", Name = "legacy_stand" });
                if (turned != "Turned Legacy stand on." || !LegacyStand || !mainframe.LegacyStandShown || idle.Count != 16) throw new Exception("Her settings tool didn't turn Legacy stand on: " + turned);
                string again = Hands.Do("capri_settings", new ToolArguments { Action = "on", Name = "legacy_stand" });
                string listed = Hands.Do("capri_settings", new ToolArguments { Action = "list", Name = "legacy_stand" });
                turned = Hands.Do("capri_settings", new ToolArguments { Action = "off", Name = "legacy_stand" });
                if (turned != "Turned Legacy stand off." || LegacyStand || mainframe.LegacyStandShown || idle.Count != 60 || again != "Legacy stand was already on." || listed.IndexOf("Legacy stand on", StringComparison.Ordinal) < 0)
                    throw new Exception("Her settings tool didn't read or turn Legacy stand off: " + turned + " / " + again + " / " + listed);
                if (!SelfKnow.Matching("how do I get your old stand back?").StartsWith("Legacy stand:", StringComparison.Ordinal)) throw new Exception("Her help didn't find Legacy stand from plain words.");
                checks.Add("PASS legacy stand: her settings tool turns it on and off, reads it back and keeps Mainframe's row in step, and her help finds it from plain words");

                bool settingsWas = mainframe.SettingsShown;
                string reach = "";
                Action<bool> fits = delegate (bool advanced)
                {
                    mainframe.ShowSettings(true);
                    if (mainframe.AdvancedShown != advanced) throw new Exception("Settings didn't open " + (advanced ? "Advanced." : "Simple."));
                    reach += (advanced ? "Advanced " : "Simple ") + mainframe.PageReach + "; ";
                    if (!mainframe.LegacyStandInPage) throw new Exception("Legacy stand's row is off the page: " + reach);
                    string[] parts = mainframe.PageReach.Split('|');
                    if (Math.Max(int.Parse(parts[0].Trim()), int.Parse(parts[1].Trim())) > int.Parse(parts[2].Trim())) throw new Exception("The Settings page runs past its height: " + reach);
                };
                InSimpleSettings(delegate { fits(false); });
                fits(true);
                mainframe.ShowSettings(settingsWas);
                checks.Add("PASS legacy stand: its row shows in Simple and Advanced Settings and everything below it still fits the page (" + reach.TrimEnd(' ', ';') + ")");

                mainframe.FlipLegacyStand();
                Color accent = idle.Strip.GetPixel(120, 200);
                ArtColors.Set(new[] { "#0000FF", null, null, null, null });
                Clip.ReloadAll();
                Color tinted = idle.Strip.GetPixel(95, 90), sameAccent = idle.Strip.GetPixel(120, 200);
                if (!idle.Old || idle.Count != 16 || tinted.B < 200 || tinted.R > 30 || Math.Abs(sameAccent.G - accent.G) > 4) throw new Exception("A colour mod didn't reach Legacy stand: " + tinted);
                mainframe.FlipLegacyStand();
                if (idle.Strip.GetPixel(36, 74).B < 200) throw new Exception("A colour mod dropped off when Legacy stand went off.");
                mainframe.FlipLegacyStand();
                if (idle.Strip.GetPixel(95, 90).B < 200) throw new Exception("A colour mod dropped off when Legacy stand came on.");
                ArtColors.Set(new string[ArtColors.Slots]);
                Clip.ReloadAll();
                Color plainHair = idle.Strip.GetPixel(95, 90);
                if (plainHair.R != 251 || plainHair.B != 72) throw new Exception("Taking colours off left Legacy stand coloured: " + plainHair);

                Func<string, int, Color, Mod> wear = delegate(string id, int wide, Color paint)
                {
                    string file = Path.Combine(output, id + Mods.SkinExtension);
                    File.Delete(file);
                    using (ZipArchive zip = ZipFile.Open(file, ZipArchiveMode.Create))
                    using (Bitmap art = new Bitmap(wide, Mods.OldIdleHigh, PixelFormat.Format32bppArgb))
                    using (MemoryStream made = new MemoryStream())
                    {
                        Put(zip, "skin.json", Encoding.UTF8.GetBytes("{\"format\":1,\"id\":\"" + id + "\",\"name\":\"" + id + "\"}"));
                        using (Graphics paper = Graphics.FromImage(art)) paper.Clear(paint);
                        art.Save(made, ImageFormat.Png);
                        Put(zip, "idle.png", made.ToArray());
                    }
                    return mods.Import(file, null);
                };
                Mod worn = wear("legacy-old", Mods.OldIdleWide, Color.FromArgb(255, 10, 200, 90));
                try
                {
                    Color skinned = idle.Strip.GetPixel(10, 10);
                    if (worn == null || !idle.Old || skinned.G != 200 || skinned.R != 10) throw new Exception("Legacy stand went over a skin's own old idle: " + skinned);
                    mods.Remove(worn);
                    worn = null;
                    if (!idle.Old || idle.Strip.GetPixel(10, 10).A != 0) throw new Exception("Taking a skin off lost Legacy stand.");
                    worn = wear("legacy-new", Companion.ArtSize("Capri.Sprite.idle.png").Width, Color.FromArgb(255, 20, 40, 220));
                    skinned = idle.Strip.GetPixel(10, 10);
                    if (worn == null || idle.Old || idle.Count != 60 || idle.Fps != 24 || skinned.B != 220 || !LegacyStand) throw new Exception("Legacy stand went over a skin's own new idle: " + idle.Count + ", " + skinned);
                    mods.Remove(worn);
                    worn = null;
                    if (!idle.Old || idle.Count != 16) throw new Exception("Taking a new skin off lost Legacy stand.");

                    // A skin that carries both stands: its idle_legacy.png for Legacy stand, its idle.png for the new one.
                    string both = Path.Combine(output, "legacy-both" + Mods.SkinExtension);
                    File.Delete(both);
                    using (ZipArchive zip = ZipFile.Open(both, ZipArchiveMode.Create))
                    {
                        Put(zip, "skin.json", Encoding.UTF8.GetBytes("{\"format\":1,\"id\":\"legacy-both\",\"name\":\"legacy-both\"}"));
                        foreach (string strip in new[] { "idle.png", "idle_legacy.png" })
                            using (Bitmap art = new Bitmap(strip == "idle.png" ? Companion.ArtSize("Capri.Sprite.idle.png").Width : Mods.OldIdleWide, Mods.OldIdleHigh, PixelFormat.Format32bppArgb))
                            using (MemoryStream made = new MemoryStream())
                            {
                                using (Graphics paper = Graphics.FromImage(art)) paper.Clear(strip == "idle.png" ? Color.FromArgb(255, 20, 40, 220) : Color.FromArgb(255, 10, 200, 90));
                                art.Save(made, ImageFormat.Png);
                                Put(zip, strip, made.ToArray());
                            }
                    }
                    worn = mods.Import(both, null);
                    Color legacyPaint = idle.Strip.GetPixel(10, 10);
                    if (worn == null || !LegacyStand || !idle.Old || idle.Count != 16 || idle.Fps != 8 || legacyPaint.G != 200 || legacyPaint.R != 10) throw new Exception("With Legacy stand on, a skin with both stands didn't wear its old one: " + idle.Count + ", " + legacyPaint);
                    mainframe.FlipLegacyStand();
                    Color newPaint = idle.Strip.GetPixel(10, 10);
                    if (LegacyStand || idle.Old || idle.Count != 60 || idle.Fps != 24 || newPaint.B != 220) throw new Exception("With Legacy stand off, a skin with both stands didn't wear its new one: " + idle.Count + ", " + newPaint);
                    mainframe.FlipLegacyStand();
                    legacyPaint = idle.Strip.GetPixel(10, 10);
                    if (!LegacyStand || !idle.Old || idle.Count != 16 || legacyPaint.G != 200) throw new Exception("Turning Legacy stand back on didn't bring a skin's old stand back: " + legacyPaint);
                    mods.Remove(worn);
                    worn = null;
                    if (!idle.Old || idle.Count != 16 || idle.Strip.GetPixel(10, 10).A != 0) throw new Exception("Taking a skin with both stands off lost her own Legacy stand.");
                    checks.Add("PASS legacy stand: a skin with idle_legacy.png wears it while Legacy stand is on and its own new idle while it's off, live both ways, and taking it off gives her own legacy strip back");
                }
                finally { if (worn != null) mods.Remove(worn); }
                string folder = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath), "..", "..", "mods"));
                string cyan = Path.Combine(folder, "colors-cyber-cyan" + Mods.Extension), maidFile = Path.Combine(folder, "maid-suit" + Mods.SkinExtension);
                if (File.Exists(cyan))
                {
                    Mod colours = mods.Import(cyan, null);
                    using (Bitmap coloured = Stood(idle, 5)) coloured.Save(Path.Combine(output, "legacy-colour.png"), ImageFormat.Png);
                    mods.Remove(colours);
                }
                else checks.Add("LIMIT legacy stand: the colour preset isn't beside this build, so its preview wasn't drawn");
                if (File.Exists(maidFile))
                {
                    Mod maid = mods.Import(maidFile, null);
                    try
                    {
                        if (maid == null || !idle.Old || idle.Strip.GetPixel(95, 90).ToArgb() == plainHair.ToArgb() && idle.Strip.GetPixel(120, 240).ToArgb() == accent.ToArgb()) throw new Exception("The Maid Suit didn't win over Legacy stand.");
                        using (Bitmap dressed = Stood(idle, 0)) dressed.Save(Path.Combine(output, "legacy-maid.png"), ImageFormat.Png);
                    }
                    finally { if (maid != null) mods.Remove(maid); }
                }
                else checks.Add("LIMIT legacy stand: the Maid Suit file isn't beside this build, so a real skin wasn't tried over it");
                mainframe.FlipLegacyStand();
                if (LegacyStand || idle.Old || idle.Count != 60) throw new Exception("Legacy stand stayed on after its row went off.");
                checks.Add("PASS legacy stand: colour mods work on it, either way round, and a skin's own idle wins over it, old cells or new");
            }
            finally
            {
                foreach (Mod left in mods.All.ToArray()) if (left.Skin || left.File.Colors != null) mods.Remove(left);
                ArtColors.Set(new string[ArtColors.Slots]);
                if (preferences.LegacyStand != legacyWas) ToggleLegacyStand();
                preferences.Motion = motionWas;
                facingLeft = leftWas;
                veiled = veiledWas;
                veilUntil = veilWas;
                leanAngle = leanWas;
                nextGlitch = glitchWas;
                seat = seatWas;
                riding = ridingWas;
                dragging = draggingWas;
                walking = walkingWas;
                falling = fallingWas;
                frontBusy = busyWas;
                speech = speechWas;
                Play(clipWas);
            }
        }

        // Her artist's standing set: its timing, its floor and body line against every clip she moves to and from, facing either
        // way, its blink and mouth, what's left in its files, and skins from before it.
        private void CheckStanding(List<string> checks, string output)
        {
            Mods mods = ModList;
            bool motionWas = preferences.Motion, leftWas = facingLeft, veiledWas = veiled;
            double veilWas = veilUntil, leanWas = leanAngle, glitchWas = nextGlitch;
            int saverWas = preferences.PowerSaver;
            Clip clipWas = clip;
            Ledge seatWas = seat;
            bool ridingWas = riding, draggingWas = dragging, walkingWas = walking, fallingWas = falling, busyWas = frontBusy;
            string speechWas = speech;
            try
            {
                seat = null;
                riding = dragging = walking = falling = frontBusy = false;
                speech = null;
                preferences.Motion = false;
                veiled = false;
                veilUntil = -1;
                leanAngle = 0;
                nextGlitch = double.MaxValue;
                int cellWide = idle.Strip.Width / idle.Count;
                if (idle.Old || idle.Count != 60 || idle.Fps != 24 || dance.Count != 60 || dance.Fps != 24 || cellWide != 116 || idle.Strip.Height != 384 || idle.Strip.Width % idle.Count != 0) throw new Exception("Her standing loop isn't 60 cells of 116 x 384 at 24 fps: " + idle.Count + " at " + idle.Fps + ", strip " + idle.Strip.Size);
                foreach (Clip stood in new[] { idle, idleBlink, idleBlinkGlitch })
                    if (stood.FlippedNow == null || stood.FlippedNow.Size != stood.Strip.Size || stood.Strip.Width / stood.Count != cellWide) throw new Exception("A standing clip has no flipped art on the same cells.");
                if (idleMouth.Strip.Width / idleMouth.Count != cellWide || idleMouth.HasFlipped) throw new Exception("Her standing mouth isn't on her standing cells.");
                checks.Add("PASS standing art: 60 frames of 116 x 384 at the pack's 24 fps, facing both ways, with a blink and mouth on the same cells");

                List<string> dirty = new List<string>();
                int listed = 0;
                foreach (string name in Assembly2.GetManifestResourceNames())
                {
                    if (!name.StartsWith("Capri.Sprite.", StringComparison.Ordinal)) continue;
                    listed++;
                    foreach (string kind in Chunks(name))
                        if (kind != "IHDR" && kind != "PLTE" && kind != "tRNS" && kind != "IDAT" && kind != "IEND") dirty.Add(name + " " + kind);
                }
                if (listed < 50 || dirty.Count != 0) throw new Exception("Her sprites carry more than their pictures: " + string.Join(", ", dirty.ToArray()));
                checks.Add("PASS standing art: all " + listed + " sprite files hold only IHDR, PLTE, tRNS, IDAT and IEND chunks");

                // Each clip she moves to or from has its feet and body where her standing loop has them, facing either way.
                Clip[] neighbours = { grin, grin, glitch, walk, walk, arrive, leave, wake };
                int[] neighbourCells = { 0, 15, 9, 0, 6, 13, 0, 12 };
                string[] neighbourNames = { "grin start", "grin end", "glitch end", "walk start", "walk step", "arrive end", "leave start", "wake end" };
                int worstFloor = 0;
                double worstMiddle = 0;
                for (int facing = 0; facing < 2; facing++)
                {
                    facingLeft = facing == 1;
                    int floor = -1;
                    double middle = -1;
                    foreach (int loopCell in new[] { 0, 7, 15, 29, 37, 45, 59 })
                    {
                        int here;
                        double across;
                        using (Bitmap stood = Stood(idle, loopCell)) Footing(stood, out here, out across);
                        if (floor < 0) { floor = here; middle = across; }
                        if (Math.Abs(here - floor) > 1) throw new Exception("Her toes left the floor line in the loop: frame " + loopCell + " ends on row " + here + ", frame 0 on " + floor + ".");
                        if (Math.Abs(across - middle) > 1.5) throw new Exception("Her body drifted across the loop: frame " + loopCell + " at " + across + ", frame 0 at " + middle + ".");
                    }
                    for (int i = 0; i < neighbours.Length; i++)
                    {
                        int there;
                        double across;
                        using (Bitmap other = Stood(neighbours[i], neighbourCells[i])) Footing(other, out there, out across);
                        worstFloor = Math.Max(worstFloor, Math.Abs(there - floor));
                        worstMiddle = Math.Max(worstMiddle, Math.Abs(across - middle));
                        if (Math.Abs(there - floor) > 2) throw new Exception("Her feet jump to " + neighbourNames[i] + ": row " + there + " against " + floor + ".");
                        if (Math.Abs(across - middle) > 3) throw new Exception("Her body jumps to " + neighbourNames[i] + ": " + across + " against " + middle + ".");
                    }
                }
                facingLeft = false;
                checks.Add("PASS standing art: at size " + preferences.Size + " her toes stay on one row through the loop, and grin, glitch, walk, arrive, leave and wake land within " + worstFloor + " px of it and " + worstMiddle.ToString("F1") + " px of her body line, facing either way");

                double began = clock.Elapsed.TotalSeconds;
                preferences.Motion = true;
                Play(idle);
                clipStart = began - 1.01;
                if (Advance(began) != 24) throw new Exception("A second in, she wasn't on frame 24.");
                clipStart = began - 2.51;
                if (Advance(began) != 0) throw new Exception("The loop didn't come round in 2.5 s.");
                preferences.Motion = false;
                checks.Add("PASS standing art: frame 24 at one second and round again at 2.5 s, as stamped");

                foreach (bool left in new[] { false, true })
                {
                    Bitmap loop = left ? idle.FlippedNow : idle.Strip, blinking = left ? idleBlink.FlippedNow : idleBlink.Strip;
                    Rectangle eyes;
                    if (Changed(loop, IdleBlinkFrom, blinking, 0, cellWide, out eyes) > 0 || Changed(loop, IdleBlinkFrom + 7, blinking, 7, cellWide, out eyes) > 0) throw new Exception("Her blink doesn't open on her loop's own frames.");
                    for (int i = 1; i < 7; i++)
                    {
                        int changed = Changed(loop, IdleBlinkFrom + i, blinking, i, cellWide, out eyes);
                        if (changed < 40 || !Rectangle.FromLTRB(50, 100, 82, 128).Contains(eyes)) throw new Exception("Her blink changes more than her eyes: " + changed + " px in " + eyes);
                    }
                }
                Play(idle);
                if (Blinks(false) != idleBlink || Blinks(true) != idleBlinkGlitch) throw new Exception("She didn't pick her standing blink.");
                checks.Add("PASS standing art: the blink is her loop's own frames 25 to 32 with only her eyes shut, facing either way");

                foreach (bool left in new[] { false, true })
                {
                    facingLeft = left;
                    using (Bitmap quiet = Stood(idle, 29))
                        for (int shape = 0; shape < 5; shape++)
                            using (Bitmap saying = Stood(idle, 29, shape))
                            {
                                Rectangle jaw;
                                int changed = Changed(quiet, 0, saying, 0, quiet.Width, out jaw);
                                float across = Width / 2f + (left ? -1 : 1) * preferences.Size * (IdleShiftX + (66 - cellWide / 2f) * 1.11f / 384), down = PaddingSize + preferences.Size * (0.5f + (122 - 192) * 1.11f / 384);
                                if (changed < 3 || Math.Abs(jaw.Left + jaw.Width / 2f - across) > 8 || Math.Abs(jaw.Top + jaw.Height / 2f - down) > 8 || jaw.Width > 24 || jaw.Height > 24) throw new Exception("Mouth " + shape + " isn't on her jaw: " + changed + " px in " + jaw + ", jaw at " + across + ", " + down);
                            }
                }
                facingLeft = false;
                checks.Add("PASS standing art: all five mouths land on her jaw and change nothing else, facing either way");

                nodGain = 1;
                dancePos = 0;
                using (Bitmap nodding = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb))
                using (Bitmap still = Stood(idle, 3))
                {
                    using (Graphics paper = Graphics.FromImage(nodding)) Draw(paper, dance, 3);
                    int stillFloor, nodFloor;
                    double stillMiddle, nodMiddle;
                    Footing(still, out stillFloor, out stillMiddle);
                    Footing(nodding, out nodFloor, out nodMiddle);
                    Rectangle moved;
                    int changed = Changed(still, 0, nodding, 0, still.Width, out moved);
                    if (changed < 200 || nodFloor != stillFloor) throw new Exception("Her beat nod went wrong on her new stand: " + changed + " px moved, floor " + nodFloor + " against " + stillFloor + ".");
                }
                nodGain = 0;
                checks.Add("PASS standing art: her beat nod moves her head on the new stand and leaves her feet where they are");

                preferences.Motion = true;
                preferences.PowerSaver = 2;
                // Earlier sections run on real time, so notes still in the air, a dizzy spell, a sulk or a chat still open (her TYPE box
                // up, talk Online on GitHub's runner, 8 Nov) can outlast them. She starts from standing still, with nothing to say.
                notes.Clear();
                dizzyFrom = dizzyUntil = -1;
                sulkSince = -1;
                CloseTalk();
                Play(idle);
                double restAt = clock.Elapsed.TotalSeconds;
                if (FrameInterval(restAt) != 61 || !Resting(restAt))
                    throw new Exception("Standing still, she didn't rest at Power saver's pace: " + FrameInterval(restAt) + " ms, saving " + Saving + ", docked " + docked + ", exploring " + exploring
                        + ", dizzy " + Dizzy(restAt) + ", speech " + (speech != null) + ", talk " + talkState + ", box " + box.Visible + ", typer " + typer.Visible + ", notes " + notes.Count
                        + ", idle " + (clip == idle) + " at " + clip.Fps + " fps, seat " + (seat != null) + ", moving " + (riding || dragging || walking || falling) + ", sulking " + Sulking + ", outro " + (outro != null) + ".");
                clipStart = restAt - 1.01;
                int heldA = Advance(restAt), heldB = Advance(restAt + 1.0 / 30);
                if (heldA != heldB) throw new Exception("Resting, her loop didn't hold its frames.");
                speech = "hi";
                bool talkingWoke = Resting(restAt);
                speech = null;
                preferences.PowerSaver = 0;
                if (talkingWoke || FrameInterval(restAt) != 33) throw new Exception("Talking or Power saver off didn't give her usual pace back.");
                preferences.PowerSaver = saverWas;
                preferences.Motion = false;
                checks.Add("PASS standing art: Power saver holds her stand at 15 fps while she only stands, and talking or turning it off lifts that");

                // Skins from before her standing set: 16 cells of 240 x 384, each cell its own colour here, so a wrong split shows.
                Color[] marks = new Color[16];
                for (int i = 0; i < 16; i++) marks[i] = Color.FromArgb(255, 40 + i * 13, 240 - i * 13, 90 + i * 7);
                string oldSkin = Path.Combine(output, "old-idle" + Mods.SkinExtension);
                File.Delete(oldSkin);
                using (ZipArchive zip = ZipFile.Open(oldSkin, ZipArchiveMode.Create))
                using (Bitmap oldStrip = new Bitmap(Mods.OldIdleWide, Mods.OldIdleHigh, PixelFormat.Format32bppArgb))
                using (MemoryStream made = new MemoryStream())
                {
                    Put(zip, "skin.json", Encoding.UTF8.GetBytes("{\"format\":1,\"id\":\"old-idle\",\"name\":\"old-idle\"}"));
                    using (Graphics paper = Graphics.FromImage(oldStrip))
                        for (int i = 0; i < 16; i++)
                            using (Brush brush = new SolidBrush(marks[i])) paper.FillRectangle(brush, i * 240 + 31, 17, 181, 347);
                    oldStrip.Save(made, ImageFormat.Png);
                    Put(zip, "idle.png", made.ToArray());
                }
                Mod worn = mods.Import(oldSkin, null);
                try
                {
                    if (worn == null || !idle.Old || !dance.Old || idle.Count != 16 || idle.Fps != 8 || dance.Fps != 8 || idle.ShiftX != 0 || idle.ShiftY != 0 || idle.Strip.Width != Mods.OldIdleWide) throw new Exception("An old skin's idle didn't go back to her old cells: " + idle.Count + " at " + idle.Fps + ".");
                    if (idle.HasFlipped || idle.Flipped != null || idle.FlippedNow != null) throw new Exception("An old skin wore the new facing-left art.");
                    int oldFloor;
                    double oldMiddle;
                    using (Bitmap cell = Stood(idle, 5))
                    {
                        Color middle = cell.GetPixel(Width / 2, Height / 2);
                        Footing(cell, out oldFloor, out oldMiddle);
                        if (Math.Abs(middle.R - marks[5].R) > 3 || Math.Abs(middle.G - marks[5].G) > 3) throw new Exception("An old skin's strip didn't split into 16 cells: " + middle + " against " + marks[5]);
                    }
                    Rectangle area;
                    using (Bitmap cell = Stood(idle, 5)) area = SpriteBounds(cell, 1);
                    int expectLeft = PaddingSize + (int)(preferences.Size * (0.5f + (31 - 120) * 1.11f / 384));
                    if (Math.Abs(area.Left - expectLeft) > 2) throw new Exception("An old skin moved in her window: left " + area.Left + " against " + expectLeft + ".");
                    facingLeft = true;
                    if (Art(idle) != idle.Strip) throw new Exception("Facing left, an old skin didn't draw its own art.");
                    using (Bitmap cell = Stood(idle, 5))
                    {
                        Rectangle mirrored = SpriteBounds(cell, 1);
                        if (Math.Abs(Width - mirrored.Right - area.Left) > 2) throw new Exception("Facing left, an old skin didn't mirror: " + mirrored + " against " + area);
                    }
                    facingLeft = false;
                    Play(idle);
                    if (Blinks(false) != blink || Blinks(true) != blinkGlitch) throw new Exception("An old skin lost her old blink.");
                    preferences.Motion = true;
                    clipStart = clock.Elapsed.TotalSeconds - (BlinkFrom + 0.5) / idle.Fps;
                    blinkWith = blink;
                    idleCycle = 0;
                    if (Advance(clock.Elapsed.TotalSeconds) != BlinkFrom) throw new Exception("An old skin's loop didn't run at 8 fps.");
                    preferences.Motion = false;
                    using (Bitmap quiet = Stood(idle, 5))
                    using (Bitmap saying = Stood(idle, 5, 3))
                    {
                        Rectangle jaw;
                        if (Changed(quiet, 0, saying, 0, quiet.Width, out jaw) < 20) throw new Exception("An old skin lost her old mouth.");
                        int jawRow = PaddingSize + (int)(preferences.Size * (0.5f + (130 - 192) * 1.11f / 384));
                        if (Math.Abs(jaw.Top - jawRow) > 14) throw new Exception("An old skin's mouth moved: " + jaw + " against row " + jawRow);
                    }
                    frontBusy = true;
                    if (Saving != (preferences.PowerSaver != 0) || Resting(clock.Elapsed.TotalSeconds) || FrameInterval(clock.Elapsed.TotalSeconds) != 115) throw new Exception("An old skin lost her old pace over a game.");
                    frontBusy = false;
                    mods.Load();
                    if (!idle.Old) throw new Exception("An old skin didn't stay in its cells after a reload.");
                    ArtColors.Set(new[] { "#0000FF", null, null, null, null });
                    Clip.ReloadAll();
                    if (!idle.Old || idle.Count != 16) throw new Exception("Colours moved an old skin out of its cells.");
                    ArtColors.Set(new string[ArtColors.Slots]);
                    Clip.ReloadAll();
                }
                finally
                {
                    ArtColors.Set(new string[ArtColors.Slots]);
                    if (worn != null) mods.Remove(worn);
                }
                if (idle.Old || idle.Count != 60 || idle.Fps != 24 || idle.ShiftX != IdleShiftX || !idle.HasFlipped) throw new Exception("Taking an old skin off didn't bring her new stand back.");
                checks.Add("PASS old skins: a 3840 x 384 idle.png imports and draws its 16 cells at 8 fps where they always sat, mirrors its own art facing left, keeps the old blink, mouth, pace and reload, and off brings her new stand back");

                using (Bitmap wrong = new Bitmap(Mods.OldIdleWide, 100))
                using (MemoryStream made = new MemoryStream())
                {
                    wrong.Save(made, ImageFormat.Png);
                    string bad = Path.Combine(output, "old-wrong" + Mods.SkinExtension);
                    File.Delete(bad);
                    using (ZipArchive zip = ZipFile.Open(bad, ZipArchiveMode.Create))
                    {
                        Put(zip, "skin.json", Encoding.UTF8.GetBytes("{\"format\":1,\"id\":\"old-wrong\",\"name\":\"old-wrong\"}"));
                        Put(zip, "idle.png", made.ToArray());
                    }
                    try { mods.Import(bad, null); throw new Exception("A strip of the wrong height got in on the old width."); }
                    catch (ModException refused) { if (refused.Message.IndexOf("match hers", StringComparison.Ordinal) < 0) throw new Exception("A wrong strip gave the wrong reason: " + refused.Message); }
                }

                string folder = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath), "..", "..", "mods"));
                string maidFile = Path.Combine(folder, "maid-suit" + Mods.SkinExtension), cyan = Path.Combine(folder, "colors-cyber-cyan" + Mods.Extension);
                using (Bitmap plain = Stood(idle, 29)) plain.Save(Path.Combine(output, "new-right.png"), ImageFormat.Png);
                facingLeft = true;
                using (Bitmap plain = Stood(idle, 29)) plain.Save(Path.Combine(output, "new-left.png"), ImageFormat.Png);
                facingLeft = false;
                using (Bitmap shut = Stood(idleBlink, 3)) shut.Save(Path.Combine(output, "new-blink.png"), ImageFormat.Png);
                using (Bitmap talking = Stood(idle, 29, 3)) talking.Save(Path.Combine(output, "new-talk.png"), ImageFormat.Png);
                if (File.Exists(cyan))
                {
                    Mod colours = mods.Import(cyan, null);
                    using (Bitmap coloured = Stood(idle, 29)) coloured.Save(Path.Combine(output, "new-colour.png"), ImageFormat.Png);
                    facingLeft = true;
                    using (Bitmap coloured = Stood(idle, 29)) coloured.Save(Path.Combine(output, "new-colour-left.png"), ImageFormat.Png);
                    facingLeft = false;
                    mods.Remove(colours);
                }
                else checks.Add("LIMIT standing art: the colour preset isn't beside this build, so its preview wasn't drawn");
                if (!File.Exists(maidFile)) { checks.Add("LIMIT old skins: the Maid Suit file isn't beside this build, so a real old skin wasn't tried"); return; }
                Mod maid = mods.Import(maidFile, null);
                // The Maid Suit came in her old cells, and since 2.1.0 it has her new stand too, so either is right here.
                bool oldMaid = maid != null && idle.Old;
                try
                {
                    if (maid == null || (oldMaid ? idle.Count != 16 || idle.Strip.Width != Mods.OldIdleWide : idle.Count != 60 || idle.Fps != 24 || !idle.HasFlipped))
                        throw new Exception("The Maid Suit didn't stand in the cells its idle.png is in: " + (maid == null ? "not imported" : idle.Count + " at " + idle.Fps));
                    using (Bitmap dressed = Stood(idle, 0)) dressed.Save(Path.Combine(output, "maid-right.png"), ImageFormat.Png);
                    facingLeft = true;
                    using (Bitmap dressed = Stood(idle, 0)) dressed.Save(Path.Combine(output, "maid-left.png"), ImageFormat.Png);
                    facingLeft = false;
                    using (Bitmap dressed = Stood(oldMaid ? blink : idleBlink, oldMaid ? 1 : 3)) dressed.Save(Path.Combine(output, "maid-blink.png"), ImageFormat.Png);
                    using (Bitmap dressed = Stood(idle, oldMaid ? 8 : 29, 3)) dressed.Save(Path.Combine(output, "maid-talk.png"), ImageFormat.Png);
                    int maidFloor, stillFloor;
                    double maidMiddle, stillMiddle;
                    using (Bitmap dressed = Stood(idle, 0)) Footing(dressed, out maidFloor, out maidMiddle);
                    mods.Remove(maid);
                    maid = null;
                    using (Bitmap plain = Stood(idle, 0)) Footing(plain, out stillFloor, out stillMiddle);
                    if (Math.Abs(maidFloor - stillFloor) > 2 || Math.Abs(maidMiddle - stillMiddle) > 6) throw new Exception("The Maid Suit's feet or body sit off her new stand: " + maidFloor + ", " + maidMiddle + " against " + stillFloor + ", " + stillMiddle);
                }
                finally { if (maid != null) mods.Remove(maid); }
                checks.Add("PASS old skins: the real Maid Suit stands in " + (oldMaid ? "her old cells" : "her new cells") + ", facing either way, on the same floor row as her new art");
            }
            finally
            {
                foreach (Mod left in mods.All.ToArray()) if (left.Skin || left.File.Colors != null) mods.Remove(left);
                preferences.Motion = motionWas;
                preferences.PowerSaver = saverWas;
                facingLeft = leftWas;
                veiled = veiledWas;
                veilUntil = veilWas;
                leanAngle = leanWas;
                nextGlitch = glitchWas;
                nodGain = 0;
                seat = seatWas;
                riding = ridingWas;
                dragging = draggingWas;
                walking = walkingWas;
                falling = fallingWas;
                frontBusy = busyWas;
                speech = speechWas;
                Play(clipWas);
            }
        }
    }
#endif
}

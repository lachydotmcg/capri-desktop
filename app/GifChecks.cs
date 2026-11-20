using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Companion
    {
        private static byte[] SmokeGif(int width, int height, int[] fills, int[] hundredths)
        {
            using (MemoryStream gif = new MemoryStream())
            {
                Action<int> word = delegate(int value) { gif.WriteByte((byte)(value & 255)); gif.WriteByte((byte)((value >> 8) & 255)); };
                byte[] head = Encoding.ASCII.GetBytes("GIF89a");
                gif.Write(head, 0, head.Length);
                word(width);
                word(height);
                gif.WriteByte(0x91);
                gif.WriteByte(0);
                gif.WriteByte(0);
                byte[] palette = { 0, 0, 0, 0, 200, 0, 220, 0, 220, 255, 255, 255 };
                gif.Write(palette, 0, palette.Length);
                byte[] loop = { 0x21, 0xFF, 0x0B, 0x4E, 0x45, 0x54, 0x53, 0x43, 0x41, 0x50, 0x45, 0x32, 0x2E, 0x30, 0x03, 0x01, 0x00, 0x00, 0x00 };
                gif.Write(loop, 0, loop.Length);
                for (int frame = 0; frame < fills.Length; frame++)
                {
                    gif.WriteByte(0x21);
                    gif.WriteByte(0xF9);
                    gif.WriteByte(4);
                    gif.WriteByte(0x04);
                    word(hundredths[frame]);
                    gif.WriteByte(0);
                    gif.WriteByte(0);
                    gif.WriteByte(0x2C);
                    word(0);
                    word(0);
                    word(width);
                    word(height);
                    gif.WriteByte(0);
                    gif.WriteByte(2);
                    List<byte> packed = new List<byte>();
                    int bits = 0, filled = 0;
                    Action<int> code = delegate(int value)
                    {
                        bits |= value << filled;
                        filled += 3;
                        while (filled >= 8) { packed.Add((byte)(bits & 255)); bits >>= 8; filled -= 8; }
                    };
                    for (int pixel = 0; pixel < width * height; pixel++) { code(4); code(fills[frame]); }
                    code(5);
                    if (filled > 0) packed.Add((byte)(bits & 255));
                    for (int at = 0; at < packed.Count; at += 255)
                    {
                        int size = Math.Min(255, packed.Count - at);
                        gif.WriteByte((byte)size);
                        for (int b = 0; b < size; b++) gif.WriteByte(packed[at + b]);
                    }
                    gif.WriteByte(0);
                }
                gif.WriteByte(0x3B);
                return gif.ToArray();
            }
        }

        private static bool Green(Color c) { return c.A > 200 && c.G > 150 && c.R < 90 && c.B < 90; }

        private static bool Magenta(Color c) { return c.A > 200 && c.R > 150 && c.B > 150 && c.G < 90; }

        private void SmokeGifs(List<string> checks, string output)
        {
            string folder = Path.Combine(output, "gifs-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string shelf = Path.Combine(folder, "shelf"), loose = Path.Combine(folder, "loose");
            string folderWas = GifShelf.Folder;
            Func<double> nowWas = GifShelf.Now;
            double fake = 1000;
            bool manualWas = manualHidden, fullWas = fullScreenHidden, shownHere = false;
            try
            {
                Directory.CreateDirectory(shelf);
                Directory.CreateDirectory(loose);
                GifShelf.Folder = shelf;
                GifShelf.Now = delegate { return fake; };
                GifShelf.Rested();
                byte[] party = SmokeGif(40, 30, new[] { 1, 2 }, new[] { 10, 10 }), wave = SmokeGif(20, 20, new[] { 3 }, new[] { 0 });
                string partyFile = Path.Combine(loose, "party.gif"), waveFile = Path.Combine(loose, "wave.gif"), notGif = Path.Combine(loose, "notes.gif"), big = Path.Combine(loose, "big.gif"), longFile = Path.Combine(loose, "long.gif");
                File.WriteAllBytes(partyFile, party);
                File.WriteAllBytes(waveFile, wave);
                File.WriteAllText(notGif, "these are my notes, not a GIF");
                using (FileStream huge = new FileStream(big, FileMode.Create, FileAccess.Write))
                {
                    byte[] magic = Encoding.ASCII.GetBytes("GIF89a");
                    huge.Write(magic, 0, magic.Length);
                    huge.SetLength(GifShelf.MostBytes + 1);
                }
                int[] many = new int[GifShelf.MostFrames + 1], quick = new int[many.Length];
                for (int i = 0; i < many.Length; i++) { many[i] = i % 2 + 1; quick[i] = 2; }
                File.WriteAllBytes(longFile, SmokeGif(2, 2, many, quick));

                if (GifShelf.Names().Count != 0 || GifShelf.Note() != "" || Hands.Gifted) throw new Exception("An empty GIF shelf still listed GIFs.");
                string kept, again, louder, waved;
                string[] added = { GifShelf.Add(partyFile, null, out kept), GifShelf.Add(partyFile, null, out again), GifShelf.Add(partyFile, "PARTY", out louder), GifShelf.Add(waveFile, "  wave/<>|*?!  ", out waved) };
                string listed = string.Join(",", GifShelf.Names().ToArray());
                if (Array.Exists(added, line => line != null) || kept != "party" || again != "party 2" || louder != "PARTY 3" || waved != "wave" || listed != "party,party 2,PARTY 3,wave"
                    || !File.Exists(Path.Combine(shelf, "party 2.gif")) || !File.Exists(Path.Combine(shelf, "PARTY 3.gif")) || !File.Exists(Path.Combine(shelf, "wave.gif")) || File.ReadAllBytes(Path.Combine(shelf, "party.gif")).Length != party.Length)
                    throw new Exception("Adding GIFs didn't keep each under a clean, unique name: " + string.Join(" | ", added) + " / " + listed);
                if (GifShelf.Clean(new string('x', 60)).Length != GifShelf.Longest || GifShelf.Clean("con") != "con gif" || GifShelf.Clean("...") != "gif" || GifShelf.Clean("Cat  Dance!!.") != "Cat Dance" || GifShelf.Clean("a/b\\c:d") != "a b c d")
                    throw new Exception("GIF names weren't cleaned into safe file names: " + GifShelf.Clean("Cat  Dance!!.") + " / " + GifShelf.Clean("con"));
                string renamed, clash, cased;
                string[] changed = { GifShelf.Rename("party 2", "Dance Party", out renamed), GifShelf.Rename("PARTY 3", "wave", out clash), GifShelf.Remove("wave 2"), GifShelf.Rename("wave", "Wave", out cased) };
                string missing = GifShelf.Remove("nope");
                listed = string.Join(",", GifShelf.Names().ToArray());
                if (Array.Exists(changed, line => line != null) || renamed != "Dance Party" || clash != "wave 2" || cased != "Wave" || File.Exists(Path.Combine(shelf, "wave 2.gif"))
                    || Path.GetFileName(Directory.GetFiles(shelf, "wave.gif")[0]) != "Wave.gif" || missing == null || !missing.StartsWith("There's no GIF called \"nope\"", StringComparison.Ordinal) || listed != "Dance Party,party,Wave")
                    throw new Exception("Renaming or removing GIFs went wrong: " + string.Join(" | ", changed) + " / " + missing + " / " + listed);
                string skipped;
                string notOne = GifShelf.Add(notGif, null, out skipped), tooBig = GifShelf.Add(big, null, out skipped), tooLong = GifShelf.Add(longFile, null, out skipped);
                if (notOne != "notes.gif isn't a GIF." || tooBig != "big.gif is over 8 MB." || tooLong != "long.gif has over " + GifShelf.MostFrames + " frames." || GifShelf.Names().Count != 3)
                    throw new Exception("A file that isn't a GIF she can keep was taken: " + notOne + " / " + tooBig + " / " + tooLong);
                for (int i = GifShelf.Names().Count; i < GifShelf.Most; i++) File.WriteAllBytes(Path.Combine(shelf, "filler " + i + ".gif"), wave);
                string full = GifShelf.Add(partyFile, null, out skipped);
                if (full == null || !full.StartsWith("Her shelf is full", StringComparison.Ordinal) || GifShelf.Names().Count != GifShelf.Most) throw new Exception("Her GIF shelf took more than " + GifShelf.Most + ": " + full);
                checks.Add("PASS her GIF shelf: Add GIFs copies .gif files into her gifs folder named after each file, cleans names to safe file names of at most " + GifShelf.Longest + " characters and keeps them unique, renames and removes them, and refuses a file that isn't a GIF, one over 8 MB, one over " + GifShelf.MostFrames + " frames and a " + (GifShelf.Most + 1) + "th GIF");

                foreach (string file in Directory.GetFiles(shelf)) File.Delete(file);
                Hands.Kit bare = Hands.KitFor();
                string emptySaid = Hands.Do("send_gif", new ToolArguments());
                if (Hands.Gifted || bare.Tools.Contains("\"name\":\"send_gif\"") || bare.ClaudeTools.Contains("\"name\":\"send_gif\"") || bare.Listing.Contains("send_gif{") || bare.Brief.Contains("- send_gif:")
                    || Hands.Note.Contains("send_gif") || Hands.Prompt.Contains("send_gif") || emptySaid != GifShelf.Empty || gifCard.Showing)
                    throw new Exception("With no GIFs saved, send_gif still reached her brains: " + emptySaid);
                File.WriteAllBytes(Path.Combine(shelf, "party.gif"), party);
                File.WriteAllBytes(Path.Combine(shelf, "wave.gif"), wave);
                Hands.Kit kit = Hands.KitFor();
                int spec = -1;
                for (int i = 0; i < Hands.Count; i++) if (Hands.Name(i) == "send_gif") spec = i;
                string description = spec < 0 ? "" : Hands.Description(spec);
                if (spec < 0 || !Hands.Gifted || ReferenceEquals(kit, bare) || !kit.Tools.Contains("\"name\":\"send_gif\"") || !kit.ClaudeTools.Contains("{\"name\":\"send_gif\",\"description\":") || !kit.Listing.Contains("send_gif{\"name\": \"...\"}")
                    || !kit.Brief.Contains("\n- send_gif: send one of the GIFs they saved for you") || !Hands.Note.Contains("send_gif: \"party\", \"wave\". You can send one on your own now and then") || !Hands.Prompt.Contains("send_gif: \"party\", \"wave\". When they ask you to send a GIF, call send_gif.") || Hands.Prompt.Contains("on your own")
                    || !description.Contains("saved for you") || !description.Contains("on your own") || !description.Contains("not every reply") || description.Length > 1024
                    || !Hands.ElevenLabsTool(spec).Contains("\"name\":\"send_gif\"") || Agent.Version < 40 || !Hands.Valid("send_gif", new ToolArguments()) || !Hands.Valid("send_gif", null))
                    throw new Exception("send_gif didn't reach her brains with her GIFs' names: " + kit.Brief);
                Hands.Phoned = true;
                Hands.Kit phoneKit = Hands.KitFor();
                string phoneNote = Hands.Note, phoneSaid = Hands.Do("send_gif", new ToolArguments { Name = "party" });
                Hands.Phoned = false;
                string inTurn = PhoneTurn(delegate { return Hands.Gifs; }, "hi", true), offTurn = PhoneTurn(delegate { return Hands.Gifs; }, "hi", false), jobSaid;
                remoteJobPhone = new RemoteAsker();
                try { jobSaid = Hands.Do("send_gif", new ToolArguments()); }
                finally { remoteJobPhone = null; }
                if (phoneKit.Tools.Contains("\"name\":\"send_gif\"") || phoneNote.Contains("\"party\"") || phoneSaid != GifShelf.OnPhone || inTurn != "" || offTurn.IndexOf("\"party\"", StringComparison.Ordinal) < 0 || Hands.Phoned || jobSaid != GifShelf.OnPhone || gifCard.Showing)
                    throw new Exception("A turn from Capri Remote could see or send her GIFs: " + phoneSaid + " / " + jobSaid);
                checks.Add("PASS send_gif is a tool in every brain's list, her hands' pick and voice chat's agent (version " + Agent.Version + ") with her GIFs' names in front of them, says she can send one on her own now and then, and stays out of their tools with no GIFs saved and out of Capri Remote's turns");

                if (docked) { checks.Add("LIMIT GIF card: she's in Gaming Mode's HUD here, so the card checks were skipped"); return; }
                if (hidden)
                {
                    manualHidden = false;
                    fullScreenHidden = false;
                    ApplyHidden(false);
                    shownHere = true;
                }
                GifShelf.Rested();
                fake += 100;
                string byName = Hands.Do("send_gif", new ToolArguments { Name = "Party.gif" });
                double start = gifCard.Started;
                if (byName != "Sent \"party\". It's playing in a card beside me." || !gifCard.Showing || !gifCard.Visible || gifCard.Called != "party" || gifCard.Image == null)
                    throw new Exception("send_gif by name didn't show that GIF: " + byName);
                fake += 10;
                string tooSoon = Hands.Do("send_gif", new ToolArguments { Name = "wave" });
                string unknown = Hands.Do("send_gif", new ToolArguments { Name = "dancing banana" });
                if (tooSoon != GifShelf.Again || gifCard.Called != "party") throw new Exception("A second GIF inside " + GifShelf.Rest + " seconds wasn't held back: " + tooSoon);
                if (unknown != GifShelf.Again) throw new Exception("An unknown GIF skipped the wait: " + unknown);
                fake += GifShelf.Rest + 1;
                unknown = Hands.Do("send_gif", new ToolArguments { Name = "dancing banana" });
                if (unknown != "There's no GIF called \"dancing banana\". Your GIFs: \"party\", \"wave\"." || gifCard.Called != "party") throw new Exception("An unknown GIF's name didn't get her real names back: " + unknown);
                checks.Add("PASS send_gif by name shows that GIF, an unknown name gets her real names back and another GIF inside " + GifShelf.Rest + " seconds is held with a line saying she just sent one");

                float s = TalkScale();
                Bitmap card = gifCard.Image;
                card.Save(Path.Combine(output, "gif-card.png"), ImageFormat.Png);
                RectangleF panel = gifCard.Panel;
                Rectangle gifBox = gifCard.GifBox;
                Point middle = new Point(gifBox.Left + gifBox.Width / 2, gifBox.Top + gifBox.Height / 2);
                Color corner = card.GetPixel(0, 0), cut = card.GetPixel((int)panel.Left + 1, (int)panel.Top + 1), edge = card.GetPixel((int)(panel.Left + panel.Width / 2), (int)Math.Round(panel.Top)), fill = card.GetPixel((int)(panel.Left + 6 * s), (int)(panel.Top + panel.Height / 2)), first = card.GetPixel(middle.X, middle.Y);
                int red = 0, neon = 0, stray = 0;
                float titleTop = panel.Top + 10 * s - 2, titleBottom = panel.Top + 29 * s + 2;
                for (int y = 0; y < card.Height; y++)
                    for (int x = 0; x < card.Width; x++)
                    {
                        Color c = card.GetPixel(x, y);
                        if (c.A < 200) continue;
                        bool title = y >= titleTop && y <= titleBottom;
                        if (c.R > 150 && c.G < 90 && c.B < 110) { if (title) red++; else stray++; }
                        if (title && c.B > 150 && c.R < 120 && c.G + 40 < c.B) neon++;
                    }
                Size decoded = new Size((int)Math.Round(40 * s), (int)Math.Round(30 * s));
                if (corner.A > 12 || cut.A > 12 || edge.A < 150 || edge.G < 150 || edge.B < 150 || edge.R > 140 || fill.A < 200 || fill.R > 40 || fill.G > 40 || fill.B > 50 || red < 8 || neon < 8 || stray > 0
                    || !Green(first) || gifCard.Frames.Count != 2 || gifCard.Frames.Shown != decoded || gifBox.Size != decoded)
                    throw new Exception("The GIF card didn't draw in her style: corner " + corner + ", cut " + cut + ", edge " + edge + ", fill " + fill + ", red " + red + ", neon " + neon + ", red elsewhere " + stray + ", frame " + first + ", " + gifCard.Frames.Shown);
                GifFrames playing = gifCard.Frames;
                UpdateGif(start + 0.15);
                int secondFrame = gifCard.FrameShown;
                Color second = gifCard.Image.GetPixel(middle.X, middle.Y);
                UpdateGif(start + 0.25);
                int thirdFrame = gifCard.FrameShown;
                Color third = gifCard.Image.GetPixel(middle.X, middle.Y);
                if (secondFrame != 1 || !Magenta(second) || thirdFrame != 0 || !Green(third) || !ReferenceEquals(playing, gifCard.Frames))
                    throw new Exception("The GIF card didn't move through its frames over time: " + secondFrame + " " + second + " / " + thirdFrame + " " + third);
                IntPtr style = new IntPtr(Native.GetWindowLong(gifCard.Handle, -20));
                if ((style.ToInt64() & 0x08000000) == 0 || (style.ToInt64() & 0x80000) == 0 || Form.ActiveForm == gifCard) throw new Exception("The GIF card could take focus.");
                checks.Add("PASS the GIF card: a cut-corner plate with her cyan glow and edge on her panel fill, a red bold CAPRI: with the GIF's name in her neon, no side bars, the GIF decoded once at her scale and moving through its frames by their delays, and it never takes focus (a picture is gif-card.png)");

                double stay = gifCard.Until - start;
                UpdateGif(start + 3.9);
                bool stillUp = gifCard.Showing && gifCard.Visible;
                UpdateGif(start + 4.01);
                bool framesGone = false;
                try { if (playing.Frame(0).Width < 0) framesGone = false; }
                catch (ArgumentException) { framesGone = true; }
                if (Math.Abs(stay - GifCard.Least) > 0.001 || !stillUp || gifCard.Showing || gifCard.Visible || gifCard.Image != null || gifCard.Frames != null || !framesGone)
                    throw new Exception("The GIF card didn't close on time and let go of its frames: stayed " + stay + " s, up at 3.9 s " + stillUp + ", up after " + gifCard.Visible);
                GifFrames slow = GifFrames.Read(SmokeGif(4, 4, new[] { 1, 2, 3 }, new[] { 500, 500, 500 }), GifCard.Room, 1), steady = GifFrames.Read(SmokeGif(4, 4, new[] { 1, 2 }, new[] { 150, 150 }), GifCard.Room, 1), still = GifFrames.Read(wave, GifCard.Room, 1);
                if (slow == null || steady == null || still == null) throw new Exception("A test GIF didn't decode.");
                int stillLoop = still.Loop;
                gifCard.Play(slow, "slow", 50);
                double slowStay = gifCard.Until - 50;
                gifCard.Play(steady, "steady", 50);
                double steadyStay = gifCard.Until - 50;
                gifCard.Play(still, "still", 50);
                double stillStay = gifCard.Until - 50;
                gifCard.Stop();
                if (slowStay != GifCard.Longest || steadyStay != 6 || stillStay != GifCard.Least || stillLoop != 100)
                    throw new Exception("The GIF card's time up wasn't about two loops between " + GifCard.Least + " and " + GifCard.Longest + " s: " + slowStay + " / " + steadyStay + " / " + stillStay + ", a 0 delay counted " + stillLoop + " ms");
                GifShelf.Rested();
                fake += 100;
                string clicked = Hands.Do("send_gif", new ToolArguments { Name = "wave" });
                bool clickUp = gifCard.Showing && gifCard.Visible;
                Rectangle waveBox = gifCard.GifBox;
                IntPtr point = new IntPtr(((waveBox.Top + waveBox.Height / 2) << 16) | ((waveBox.Left + waveBox.Width / 2) & 0xFFFF));
                Native.SendMessage(gifCard.Handle, 0x0201, new IntPtr(1), point);
                Native.SendMessage(gifCard.Handle, 0x0202, IntPtr.Zero, point);
                if (clicked != "Sent \"wave\". It's playing in a card beside me." || !clickUp || gifCard.Showing || gifCard.Visible || gifCard.Image != null)
                    throw new Exception("A click didn't close the GIF card: " + clicked + ", up " + clickUp + " then " + gifCard.Visible);
                Rectangle screen = new Rectangle(0, 0, 1920, 1040), body = new Rectangle(800, 400, 120, 240), bubble = new Rectangle(600, 300, 600, 200);
                Size cardSize = new Size(200, 180);
                Point clear = GifCard.Place(cardSize, body, screen, new[] { Rectangle.Empty }, 1), around = GifCard.Place(cardSize, body, screen, new[] { bubble }, 1);
                Point edgeSide = GifCard.Place(cardSize, new Rectangle(1790, 400, 120, 240), screen, new[] { Rectangle.Empty }, 1);
                if (clear.X != body.Right + 8 || new Rectangle(around, cardSize).IntersectsWith(bubble) || new Rectangle(around, cardSize).IntersectsWith(body) || !screen.Contains(new Rectangle(around, cardSize)) || edgeSide.X + cardSize.Width > 1790)
                    throw new Exception("The GIF card covered her bubble or left the screen: " + clear + " / " + around + " / " + edgeSide);
                fake += 100;
                GifShelf.Rested();
                System.Collections.Generic.HashSet<string> picked = new System.Collections.Generic.HashSet<string>();
                for (int i = 0; i < 24 && picked.Count < 2; i++)
                {
                    GifShelf.Rested();
                    string any = Hands.Do("send_gif", new ToolArguments());
                    if (!any.StartsWith("Sent \"", StringComparison.Ordinal) || !gifCard.Showing) throw new Exception("send_gif with no name didn't send one: " + any);
                    picked.Add(gifCard.Called);
                    gifCard.Stop();
                }
                if (!picked.SetEquals(new[] { "party", "wave" })) throw new Exception("send_gif with no name didn't pick at random: " + string.Join(", ", new List<string>(picked).ToArray()));
                checks.Add("PASS the GIF card stays about two loops, at least " + GifCard.Least + " s and at most " + GifCard.Longest + " s, closes on time or on a click and disposes its frames, sits beside her clear of her bubble, and send_gif with no name picks one at random");

                string help = SelfKnow.Matching("how do I add gifs for you?") ?? "";
                if (!help.StartsWith("Her GIFs:", StringComparison.Ordinal) || help.IndexOf("Mainframe > Settings > HABITS > Her GIFs", StringComparison.Ordinal) < 0) throw new Exception("Her help didn't say where her GIFs are: " + help);
                Button entry = mainframe.GifsKey;
                if (entry == null || entry.Text != "Her GIFs" || !(entry.AccessibleName ?? "").StartsWith("Her GIFs:", StringComparison.Ordinal)) throw new Exception("Her Mainframe has no Her GIFs key under HABITS.");
                string habits = mainframe.HabitsPicture(Path.Combine(output, "habits-row.png"));
                if (habits != null) throw new Exception(habits);
                typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(entry, new object[] { EventArgs.Empty });
                string shown = null;
                for (int i = 0; i < 150; i++)
                {
                    Application.DoEvents();
                    shown = mainframe.GifsShown;
                    if (shown != null && shown.Contains("party") && shown.Contains("wave")) break;
                    Thread.Sleep(20);
                }
                mainframe.CloseGifs();
                if (shown == null || !shown.Contains("2 of " + GifShelf.Most + " GIFs") || !shown.Contains("party") || !shown.Contains("wave")) throw new Exception("Her GIFs key didn't open a list of her GIFs: " + shown);
                float machine = mainframe.Px(1000) / 1000f;
                string[] adding = { Path.Combine(loose, "cat dance.gif"), notGif };
                File.WriteAllBytes(adding[0], party);
                List<string> wrongs = new List<string>();
                foreach (float zoom in new[] { 1f, 1.5f, 2f })
                {
                    foreach (string file in Directory.GetFiles(shelf)) File.Delete(file);
                    File.WriteAllBytes(Path.Combine(shelf, "party.gif"), party);
                    File.WriteAllBytes(Path.Combine(shelf, "wave.gif"), wave);
                    string wrong = mainframe.GifsAt(zoom / machine, zoom == 1.5f ? null : Path.Combine(output, "gif-window-" + (int)(zoom * 100) + ".png"), adding);
                    if (wrong != null) wrongs.Add((int)(zoom * 100) + "% " + wrong);
                }
                if (wrongs.Count > 0) throw new Exception("The HER GIFS window went wrong: " + string.Join(" / ", wrongs.ToArray()));
                checks.Add("PASS Her GIFs under HABITS opens HER GIFS, which lists her GIFs by name with a count, adds picked .gif files under their own names and says why one was refused, renames one in place (Esc cancels) and removes one on a second click, fitting at 100%, 150% and 200% (pictures are habits-row.png, gif-window-100.png and gif-window-200.png)");
            }
            finally
            {
                gifCard.Stop();
                mainframe.CloseGifs();
                GifShelf.Folder = folderWas;
                GifShelf.Now = nowWas;
                GifShelf.Rested();
                Hands.Phoned = false;
                if (shownHere)
                {
                    manualHidden = manualWas;
                    fullScreenHidden = fullWas;
                    ApplyHidden(manualWas || fullWas);
                }
                try { Directory.Delete(folder, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}

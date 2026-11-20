using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Companion
    {
        // The moves her skins added, drawn in her standing cells like her idle. Made again whenever her skins change.
        private Clip[] moveClips = new Clip[0];

        // A move's strip from its skin, in her colours.
        internal static Bitmap MoveArt(string file, string name)
        {
            using (Bitmap read = SkinPicture(file, "Capri.Sprite." + name + ".png"))
                return read == null ? null : Finished(read);
        }

        private void RebuildMoves()
        {
            Mods.SkinMove[] moves = Mods.MovesNow;
            Clip[] was = moveClips, made = new Clip[moves.Length];
            for (int i = 0; i < moves.Length; i++) made[i] = Clip.Move(moves[i].Name, moves[i].File, moves[i].Frames, moves[i].Fps, moves[i].Loop, idle);
            moveClips = made;
            if (clip != null && clip.IsMove)
            {
                string playing = clip.Title;
                Clip same = Array.Find(made, each => each.Title == playing);
                if (same != null) clip = same;
                else Play(idle);
            }
            foreach (Clip old in was) old.Drop();
        }

        private Clip MoveClip(string name)
        {
            foreach (Clip move in moveClips) if (move.Title == name) return move;
            return null;
        }

        // A move is a standing one, so she gets up for it, like her grin.
        private string PlayMove(Clip move)
        {
            EndWalk();
            if (riding) StopRide();
            LeavePanel(false);
            if (hanging) LetGo();
            else if (seat != null) StandUp();
            danceAsked = false;
            danceHeld = musicOn;
            move.Load(false);
            Play(move);
            frames.Start();
            RenderFrame();
            string said = move.Title.Replace('_', ' ');
            return move.Loops ? "Doing my " + said + ". Tell me to stop when you've had enough." : "Doing my " + said + ".";
        }

        // A move's strip for checks: her own standing frames with a cyan hand going over in an arc.
        private static byte[] MoveStrip(int frames)
        {
            Size cell = Mods.MoveCell;
            using (Stream own = Assembly2.GetManifestResourceStream("Capri.Sprite.idle.png"))
            using (Bitmap art = new Bitmap(own))
            using (Bitmap made = new Bitmap(cell.Width * frames, cell.Height, PixelFormat.Format32bppArgb))
            using (MemoryStream png = new MemoryStream())
            {
                using (Graphics paper = Graphics.FromImage(made))
                using (Brush hand = new SolidBrush(Color.FromArgb(255, 0, 255, 255)))
                    for (int i = 0; i < frames; i++)
                    {
                        paper.DrawImage(art, new Rectangle(i * cell.Width, 0, cell.Width, cell.Height), new Rectangle(i * cell.Width % art.Width, 0, cell.Width, cell.Height), GraphicsUnit.Pixel);
                        double turn = Math.PI * i / Math.Max(1, frames - 1);
                        paper.FillEllipse(hand, i * cell.Width + 14 + (int)(66 * (1 - Math.Cos(turn)) / 2), 60 + (int)(36 * (1 - Math.Sin(turn))), 24, 24);
                    }
                made.Save(png, ImageFormat.Png);
                return png.ToArray();
            }
        }

        private void SmokeMoves(List<string> checks, string output)
        {
            Mods mods = ModList;
            Size cell = Mods.MoveCell, own = ArtSize("Capri.Sprite.idle.png");
            if (cell.Width * idle.OwnCount != own.Width || cell.Height != own.Height) throw new Exception("A move's frames aren't her standing frames: " + cell);
            Func<string, string, Dictionary<string, byte[]>, string> pack = delegate(string id, string moves, Dictionary<string, byte[]> files)
            {
                string file = Path.Combine(output, id + Mods.SkinExtension);
                if (File.Exists(file)) File.Delete(file);
                using (ZipArchive zip = ZipFile.Open(file, ZipArchiveMode.Create))
                {
                    Put(zip, "skin.json", Encoding.UTF8.GetBytes("{\"format\":1,\"id\":\"" + id + "\",\"name\":\"" + id + "\",\"accessory\":true" + (moves == null ? "" : ",\"moves\":" + moves) + "}"));
                    if (files != null) foreach (KeyValuePair<string, byte[]> entry in files) Put(zip, entry.Key, entry.Value);
                }
                return file;
            };
            int animateAt = -1;
            for (int i = 0; i < Hands.Count; i++) if (Hands.Name(i) == "animate") animateAt = i;
            string stampBefore = Agent.Stamp, voiceBefore = Hands.ElevenLabsTool(animateAt);
            ToolArguments asked = new ToolArguments { Name = "test_wave" };
            Ledge seatWas = seat;
            bool fallingWas = falling, musicWas = musicOn;
            double musicAtWas = musicAt;
            Mod skin = null;
            try
            {
                skin = mods.Import(pack("moves-test", "[{\"name\":\"test_wave\",\"fps\":24},{\"name\":\"test_spin\",\"strip\":\"spin.png\",\"loop\":true}]", new Dictionary<string, byte[]> { { "test_wave.png", MoveStrip(8) }, { "spin.png", MoveStrip(6) } }), null);
                Mods.SkinMove wave = Mods.MoveNamed("test_wave"), spin = Mods.MoveNamed("test_spin");
                if (skin == null || Mods.MovesNow.Length != 2 || wave == null || spin == null || wave.Frames != 8 || wave.Fps != 24 || wave.Loop || spin.Frames != 6 || spin.Fps != Mods.MoveFps || !spin.Loop || Path.GetFileName(spin.File) != "spin.png" || MoveClip("test_wave") == null || MoveClip("test_spin") == null)
                    throw new Exception("A skin's moves didn't load from its skin.json: " + Mods.MovesNow.Length + " moves");
                checks.Add("PASS moves: a skin's skin.json moves load with their strips, frame counts, speeds, and loop or play once");

                Hands.Kit kit = Hands.KitFor();
                string described = Hands.Description(animateAt), voiced = Hands.ElevenLabsTool(animateAt), help = SelfKnow.Matching("how do I make a new move?", kit);
                if (!kit.Tools.Contains("\"test_spin\",\"test_wave\"]") || !kit.ClaudeTools.Contains("\"test_spin\",\"test_wave\"]") || !kit.Listing.Contains("|test_spin|test_wave\"")
                    || !kit.Brief.Contains("including the moves their skins added: test spin and test wave") || !kit.Abilities.Contains("including the moves your skins added: test spin and test wave")
                    || !described.Contains("test_spin, test_wave") || described.Length > 1024 || !voiced.Contains("\"test_wave\"") || voiced == voiceBefore
                    || Agent.Stamp == stampBefore || !Agent.Stale(Agent.Version, stampBefore) || !Hands.Valid("animate", asked) || Hands.Valid("animate", new ToolArguments { Name = "moonwalk" })
                    || help == null || !help.StartsWith("New moves:", StringComparison.Ordinal) || !help.Contains("test wave"))
                    throw new Exception("Her brain didn't get a skin's moves in animate: " + described);
                string tool;
                ToolArguments heard;
                bool doWave = Hands.Heard("do the test wave", out tool, out heard) && tool == "animate" && heard.Name == "test_wave";
                bool showSpin = Hands.Heard("show me your test spin please", out tool, out heard) && tool == "animate" && heard.Name == "test_spin";
                bool bare = Hands.Heard("test wave", out tool, out heard) && tool == "animate";
                bool played = Hands.Heard("play test wave", out tool, out heard) && tool == "animate";
                if (!doWave || !showSpin || bare || played) throw new Exception("Asking for a skin's move by name went wrong: do " + doWave + ", show " + showSpin + ", bare " + bare + ", play " + played);
                checks.Add("PASS moves: her animate tool lists a skin's moves for every brain and her voice agent, her abilities and self knowledge name them, and \"do the test wave\" asks for it while a bare name stays chat");

                musicAt = clock.Elapsed.TotalSeconds + 1000;
                musicOn = false;
                seat = null;
                falling = false;
                string waved = Hands.Do("animate", asked);
                bool playing = clip.IsMove && clip.Title == "test_wave" && !clip.Loops && clip.Count == 8 && clip.Fps == 24;
                double now = clock.Elapsed.TotalSeconds;
                clipStart = now - 3.5 / 24;
                bool midway = Advance(now) == 3 && clip.Title == "test_wave";
                using (Bitmap sheet = new Bitmap(Width * 4, Height, PixelFormat.Format32bppArgb))
                {
                    int hand = 0;
                    using (Graphics paper = Graphics.FromImage(sheet)) paper.Clear(Color.FromArgb(255, 16, 18, 28));
                    for (int i = 0; i < 4; i++)
                        using (Bitmap one = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb))
                        {
                            using (Graphics graphics = Graphics.FromImage(one)) Draw(graphics, clip, i * 2 + 1, -1, null, false, now);
                            for (int x = 0; x < one.Width; x += 2) for (int y = 0; y < one.Height; y += 2) { Color pixel = one.GetPixel(x, y); if (pixel.A > 200 && pixel.R < 90 && pixel.G > 190 && pixel.B > 190) hand++; }
                            using (Graphics paper = Graphics.FromImage(sheet)) paper.DrawImage(one, i * Width, 0);
                        }
                    sheet.Save(Path.Combine(output, "move-frames.png"), ImageFormat.Png);
                    if (hand < 20) throw new Exception("Her move didn't draw its own frames: " + hand + " hand pixels.");
                }
                clipStart = now - 8.5 / 24;
                Advance(now);
                if (waved != "Doing my test wave." || !playing || !midway || clip != idle) throw new Exception("A move didn't play once and go back to her idle: " + waved + " / " + playing + " " + midway + " " + clip.Title);
                checks.Add("PASS moves: animate plays a skin's move frame by frame at its speed in her standing cells, and a play-once move goes back to her idle");

                string spun = Hands.Do("animate", new ToolArguments { Name = "test_spin" });
                now = clock.Elapsed.TotalSeconds;
                clipStart = now - 20;
                Advance(now);
                bool looping = clip.Title == "test_spin" && clip.Loops;
                string stopped = Hands.Do("animate", new ToolArguments { Name = "stop_dancing" });
                if (spun != "Doing my test spin. Tell me to stop when you've had enough." || !looping || stopped != "Stopped my test spin." || clip != idle) throw new Exception("A looping move didn't go on until stop: " + spun + " / " + looping + " / " + stopped);
                checks.Add("PASS moves: a looping move goes on until she's told to stop");

                using (ModHost host = new ModHost(this, "moves-code-test"))
                {
                    ICapriBody body = host;
                    string fromMod = body.Animate("test_wave"), shown = body.Animation, unknown = body.Animate("moonwalk");
                    if (fromMod != "Doing my test wave." || shown != "test_wave" || unknown != "I don't have an animation called moonwalk.") throw new Exception("A code mod couldn't play a skin's move: " + fromMod + " / " + shown + " / " + unknown);
                }
                Play(idle);
                checks.Add("PASS moves: a code mod's Animate(name) plays a skin's move, and an unknown name gets a plain answer");

                Hands.Do("animate", new ToolArguments { Name = "test_spin" });
                mods.Remove(skin);
                skin = null;
                string gone = Hands.Do("animate", asked);
                if (Mods.MovesNow.Length != 0 || clip != idle || MoveClip("test_wave") != null || Hands.Valid("animate", asked) || Hands.KitFor().Tools.Contains("test_wave") || Hands.KitFor().Brief.Contains("test wave")
                    || Agent.Stamp != stampBefore || Hands.ElevenLabsTool(animateAt) != voiceBefore || gone != "I don't have an animation called test_wave.")
                    throw new Exception("Removing a skin didn't take its moves away: " + Mods.MovesNow.Length + " / " + gone);
                checks.Add("PASS moves: removing the skin stops its move, takes it out of her tools and her voice agent, and she says she doesn't have it");

                byte[] tiny;
                using (Bitmap wrong = new Bitmap(10, 10))
                using (MemoryStream made = new MemoryStream())
                {
                    wrong.Save(made, ImageFormat.Png);
                    tiny = made.ToArray();
                }
                byte[] good = MoveStrip(4);
                string[,] cases =
                {
                    { "moves-name", "[{\"name\":\"Big Wave!\"}]", "", "can't be a move's name" },
                    { "moves-own", "[{\"name\":\"dance\"}]", "", "one of her own animations" },
                    { "moves-missing", "[{\"name\":\"test_hop\"}]", "", "needs test_hop.png" },
                    { "moves-size", "[{\"name\":\"test_hop\"}]", "tiny", "A move's frames are" },
                    { "moves-path", "[{\"name\":\"test_hop\",\"strip\":\"../test_hop.png\"}]", "", "can't be the strip for test_hop" },
                    { "moves-fast", "[{\"name\":\"test_hop\",\"fps\":99}]", "good", "needs a speed from 1 to 60" },
                    { "moves-twice", "[{\"name\":\"test_hop\"},{\"name\":\"test_hop\"}]", "good", "two moves called" },
                    { "moves-stray", null, "good", "sprite strips" }
                };
                for (int i = 0; i < cases.GetLength(0); i++)
                {
                    Dictionary<string, byte[]> files = new Dictionary<string, byte[]>();
                    if (cases[i, 2] == "tiny") files["test_hop.png"] = tiny;
                    if (cases[i, 2] == "good") files["test_hop.png"] = good;
                    string bad = pack(cases[i, 0], cases[i, 1], files);
                    try { mods.Import(bad, null); throw new Exception("A bad skin's move was let in: " + cases[i, 0]); }
                    catch (ModException refused) { if (refused.Message.IndexOf(cases[i, 3], StringComparison.Ordinal) < 0) throw new Exception("A bad skin's move gave the wrong reason: " + refused.Message); }
                    if (mods.All.Exists(mod => mod.File.Id == cases[i, 0]) || Mods.MovesNow.Length != 0) throw new Exception("A refused skin's move stayed: " + cases[i, 0]);
                }
                checks.Add("PASS moves: skins with a bad move name, one of her own names, a missing, wrong-sized or outside strip, a speed over 60, a name twice or a stray strip are refused with the reason");
                mainframe.CheckNewMove(checks, output);
            }
            finally
            {
                if (skin != null) mods.Remove(skin);
                seat = seatWas;
                falling = fallingWas;
                musicOn = musicWas;
                musicAt = musicAtWas;
                Play(idle);
            }
        }
    }

    internal sealed partial class Mainframe
    {
        // Her New move window: a name, frames from pictures, one strip or a folder, a speed, and whether it loops or plays once.
        private sealed class MoveSheet : ModSheet
        {
            internal const int Wide = 460, Tall = 556;
            private readonly AnimationDraft draft;
            private readonly AnimationPreview preview = new AnimationPreview();
            private readonly Entry name;
            private readonly Label count, speed, status;
            private readonly Key slower, faster, loops, once;
            private readonly Timer clock = new Timer();
            private List<Bitmap> frames;
            private int fps = Mods.MoveFps, shown;
            private bool loop;
            internal AnimationDraft.Strip Made;
            // How it asks for files and folders. Checks answer for her.
            internal Func<string, bool, string[]> PickFiles;
            internal Func<string, string> PickFolder;

            internal MoveSheet(Mainframe owner, float zoom, AnimationDraft draft) : base(owner, zoom, "New move", "NEW MOVE >", Wide, Tall)
            {
                this.draft = draft;
                Size cell = Mods.MoveCell;
                Words("A move of her own, in frames " + cell.Width + " x " + cell.Height + " like her standing ones.", 28, 50, 404, 18, Small(), Faint);
                Words("MOVE NAME", 28, 78, 200, 18, Small(), Rose);
                name = new Entry { Bounds = Box(28, 98, 404, 28), Cue = "Like happy_wave" };
                name.Box.AccessibleName = "Move name";
                Controls.Add(name);
                Words("FRAMES FROM", 28, 140, 200, 18, Small(), Rose);
                Make("Pictures...", 28, 162, 128, 26, delegate { FromPictures(); }).AccessibleName = "Make the move from PNG frames";
                Make("Strip...", 166, 162, 128, 26, delegate { FromStrip(); }).AccessibleName = "Make the move from one PNG of frames side by side";
                Make("Folder...", 304, 162, 128, 26, delegate { FromFolder(); }).AccessibleName = "Make the move from a folder of PNG frames, in name order";
                preview.Bounds = Box(28, 200, 150, 248);
                preview.AccessibleName = "Your move";
                Controls.Add(preview);
                count = Words("No frames yet", 194, 200, 238, 20, Font, White);
                Words("SPEED", 194, 232, 238, 18, Small(), Rose);
                speed = Words("", 194, 252, 238, 20, Font, White);
                slower = Make("Slower", 194, 276, 114, 26, delegate { Retime(-1); });
                faster = Make("Faster", 318, 276, 114, 26, delegate { Retime(1); });
                Words("PLAYS", 194, 316, 238, 18, Small(), Rose);
                loops = Make("Loops", 194, 338, 114, 26, delegate { Loop(true); });
                once = Make("Once", 318, 338, 114, 26, delegate { Loop(false); });
                loops.StateValue = once.StateValue = true;
                loops.AccessibleName = "Loops until you tell her to stop";
                once.AccessibleName = "Plays once, then she's back to her idle";
                Words("Once plays it and she's back to her idle. Loops keeps going until you tell her to stop.", 194, 372, 238, 54, Small(), Faint);
                Key add = Make("Add move", 194, 462, 114, 28, delegate { Add(); });
                add.Accent = Cyan;
                add.Loud = true;
                add.AccessibleName = "Add this move to the skin you're making";
                Make("Cancel", 318, 462, 114, 28, delegate { Close(); });
                status = Words("", 28, 502, 404, 38, Small(), Faint);
                clock.Tick += delegate { Step(); };
                FormClosed += delegate
                {
                    clock.Stop();
                    clock.Dispose();
                    if (Made == null) Drop();
                };
                Loop(false);
                Retime(0);
                Frame.Fit(this);
            }

            internal string MoveName { get { return name.Box.Text; } set { name.Box.Text = value; } }
            internal string Said { get { return status.Text; } }
            internal int Frames { get { return frames == null ? 0 : frames.Count; } }
            internal int Fps { get { return fps; } }
            internal bool Loops { get { return loop; } }

            private void Say(string text, bool trouble)
            {
                status.ForeColor = trouble ? Rose : Faint;
                status.Text = text;
            }

            internal void Retime(int by)
            {
                int steps = by * (fps >= 30 ? 5 : fps >= 10 ? 2 : 1);
                fps = Math.Max(1, Math.Min(Mods.FastestFps, fps + steps));
                speed.Text = fps + " frames a second";
                slower.Enabled = fps > 1;
                faster.Enabled = fps < Mods.FastestFps;
                clock.Interval = Math.Max(15, 1000 / fps);
            }

            internal void Loop(bool on)
            {
                loop = on;
                loops.Pick(on);
                once.Pick(!on);
            }

            private string[] AskFiles(string title, bool many)
            {
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Title = title;
                    dialog.Filter = "PNG pictures (*.png)|*.png";
                    dialog.Multiselect = many;
                    return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileNames : null;
                }
            }

            private string AskFolder(string title)
            {
                using (FolderBrowserDialog picker = new FolderBrowserDialog())
                {
                    picker.Description = title;
                    return picker.ShowDialog(this) == DialogResult.OK ? picker.SelectedPath : null;
                }
            }

            internal void FromPictures()
            {
                string[] files = (PickFiles ?? AskFiles)("Pick the PNG frames for your move", true);
                if (files == null) return;
                Array.Sort(files, AnimationSheet.Natural);
                Take(files);
            }

            internal void FromStrip()
            {
                string[] files = (PickFiles ?? AskFiles)("Pick a PNG strip of frames side by side", false);
                if (files != null) Take(files);
            }

            internal void FromFolder()
            {
                string folder = (PickFolder ?? AskFolder)("Pick a folder of PNG frames for your move. They go in by name.");
                if (folder == null) return;
                string[] files;
                try { files = Directory.GetFiles(folder, "*.png"); }
                catch (IOException error) { Say("Couldn't read that folder: " + error.Message, true); return; }
                catch (UnauthorizedAccessException error) { Say("Couldn't read that folder: " + error.Message, true); return; }
                if (files.Length == 0) { Say("That folder has no PNG pictures.", true); return; }
                Array.Sort(files, AnimationSheet.Natural);
                Take(files);
            }

            private void Take(IEnumerable<string> files)
            {
                List<Bitmap> found = new List<Bitmap>();
                try
                {
                    foreach (string file in files) found.AddRange(AnimationDraft.ReadMove(file));
                }
                catch (Exception error)
                {
                    foreach (Bitmap made in found) made.Dispose();
                    if (error is ModException) { Say(error.Message, true); return; }
                    if (error is IOException || error is UnauthorizedAccessException || error is OutOfMemoryException) { Say("Couldn't read that: " + error.Message, true); return; }
                    throw;
                }
                if (found.Count > Mods.MostFrames)
                {
                    foreach (Bitmap made in found) made.Dispose();
                    Say("A move can have up to " + Mods.MostFrames + " frames, and that's " + found.Count + ".", true);
                    return;
                }
                Drop();
                frames = found;
                shown = 0;
                count.Text = frames.Count == 1 ? "1 frame" : frames.Count + " frames";
                preview.Frame = frames[0];
                preview.Invalidate();
                clock.Start();
                Say(frames.Count == 1 ? "1 frame in." : frames.Count + " frames in.", false);
            }

            private void Step()
            {
                if (frames == null || frames.Count == 0) return;
                shown = (shown + 1) % frames.Count;
                preview.Frame = frames[shown];
                preview.Invalidate();
            }

            private void Drop()
            {
                clock.Stop();
                preview.Frame = null;
                if (frames != null) foreach (Bitmap frame in frames) frame.Dispose();
                frames = null;
            }

            // Plain words become a move's name: "Happy Wave" is happy_wave.
            internal static string NameFrom(string text)
            {
                return System.Text.RegularExpressions.Regex.Replace((text ?? "").Trim().ToLowerInvariant(), @"[\s-]+", "_");
            }

            internal bool Add()
            {
                string named = NameFrom(name.Box.Text);
                string trouble = named.Length == 0 ? "Name your move first." : draft.MoveTrouble(named);
                if (trouble == null && (frames == null || frames.Count == 0)) trouble = "Pick its frames first: pictures, a strip or a folder.";
                if (trouble != null) { Say(trouble, true); return false; }
                clock.Stop();
                preview.Frame = null;
                Made = draft.AddMove(named, frames, fps, loop);
                frames = null;
                DialogResult = DialogResult.OK;
                Close();
                return true;
            }
        }

        // Her New move window and the editor it adds to: a move made from a folder of frames and from a strip, its name checked,
        // its speed and loop kept, worn as a skin she can do it from, opened again from that skin and taken out.
        internal void CheckNewMove(List<string> checks, string output)
        {
            ForgetAnimations();
            Mods mods = capri.ModList;
            Size cell = Mods.MoveCell;
            string folder = Path.Combine(output, "new-move-frames");
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);
            List<string> pictures = new List<string>();
            using (Stream own = typeof(Mainframe).Assembly.GetManifestResourceStream("Capri.Sprite.idle.png"))
            using (Bitmap art = new Bitmap(own))
                for (int i = 1; i <= 10; i++)
                    using (Bitmap frame = new Bitmap(cell.Width, cell.Height, PixelFormat.Format32bppArgb))
                    {
                        using (Graphics paper = Graphics.FromImage(frame))
                        using (Brush hand = new SolidBrush(Color.FromArgb(255, 0, 255, 255)))
                        {
                            paper.DrawImage(art, new Rectangle(0, 0, cell.Width, cell.Height), new Rectangle((i - 1) * cell.Width, 0, cell.Width, cell.Height), GraphicsUnit.Pixel);
                            paper.FillEllipse(hand, 14 + i * 6, 120 - i * 7, 24, 24);
                        }
                        frame.SetPixel(0, 0, Color.FromArgb(255, i * 20, 0, 0));
                        string file = Path.Combine(folder, "frame_" + i + ".png");
                        frame.Save(file, ImageFormat.Png);
                        pictures.Add(file);
                    }
            string strip = Path.Combine(output, "move-strip.png"), wrong = Path.Combine(output, "move-wrong.png");
            using (Bitmap joined = new Bitmap(cell.Width * 6, cell.Height, PixelFormat.Format32bppArgb)) joined.Save(strip, ImageFormat.Png);
            using (Bitmap small = new Bitmap(50, 50, PixelFormat.Format32bppArgb)) small.Save(wrong, ImageFormat.Png);
            Mod was = mods.Outfit, worn = null;
            AnimationDraft draft = new AnimationDraft(mods);
            try
            {
                AnimationDraft.Strip made;
                using (MoveSheet sheet = new MoveSheet(this, 1f, draft))
                {
                    string[] picked = null;
                    sheet.PickFiles = delegate { return picked; };
                    sheet.PickFolder = delegate { return folder; };
                    sheet.Show(this);
                    string crowded = sheet.Crowded();
                    if (crowded != null) throw new Exception("Her New move window doesn't fit: " + crowded);
                    sheet.Picture(Path.Combine(output, "new-move-empty.png"));
                    bool unnamed = !sheet.Add() && sheet.Said == "Name your move first.";
                    sheet.MoveName = "dance";
                    bool hers = !sheet.Add() && sheet.Said.Contains("one of her own animations");
                    sheet.MoveName = "Happy Wave";
                    bool frameless = !sheet.Add() && sheet.Said.StartsWith("Pick its frames first", StringComparison.Ordinal);
                    picked = new[] { wrong };
                    sheet.FromStrip();
                    bool misfit = sheet.Frames == 0 && sheet.Said.Contains("A move's frames are " + cell.Width + " x " + cell.Height);
                    picked = new[] { strip };
                    sheet.FromStrip();
                    bool stripped = sheet.Frames == 6;
                    picked = new[] { pictures[9], pictures[1], pictures[0] };
                    sheet.FromPictures();
                    bool chosen = sheet.Frames == 3;
                    sheet.FromFolder();
                    bool foldered = sheet.Frames == 10;
                    sheet.Retime(1);
                    sheet.Retime(1);
                    sheet.Loop(true);
                    if (!unnamed || !hers || !frameless || !misfit || !stripped || !chosen || !foldered || sheet.Fps != 16 || !sheet.Loops)
                        throw new Exception("Her New move window went wrong: " + unnamed + " " + hers + " " + frameless + " " + misfit + " " + stripped + " " + chosen + " " + foldered + " " + sheet.Fps + " / " + sheet.Said);
                    sheet.Picture(Path.Combine(output, "new-move-ready.png"));
                    if (!sheet.Add() || sheet.Made == null) throw new Exception("Her New move window didn't add the move: " + sheet.Said);
                    made = sheet.Made;
                }
                bool inOrder = made.Frames.Count == 10;
                for (int i = 0; inOrder && i < 10; i++) inOrder = made.Frames[i].GetPixel(0, 0).R == (i + 1) * 20;
                if (made.Title != "happy_wave" || !made.Move || !made.Loop || made.Fps != 16 || !made.Changed || !inOrder || draft.Strips[draft.Strips.Count - 1] != made)
                    throw new Exception("Her New move window didn't hand the editor its move in name order.");
                using (AnimationSheet sheet = new AnimationSheet(this, 1f, draft))
                {
                    sheet.Show(this);
                    sheet.Moved(made);
                    string crowded = sheet.Crowded();
                    if (crowded != null) throw new Exception("Her animation editor doesn't fit with a move: " + crowded);
                    if (!sheet.Showing.StartsWith("happy_wave (move) 1/10 16", StringComparison.Ordinal)) throw new Exception("Her animation editor didn't show the new move: " + sheet.Showing);
                    sheet.Picture(Path.Combine(output, "new-move-in-editor.png"));
                    sheet.Close();
                }
                checks.Add("PASS moves: her New move window takes a name, frames from pictures, a strip or a folder in name order, a speed and loop or once, refuses her own names and wrong-sized frames, and her editor shows the move");

                ModFrames[] rates;
                ModMove[] moves;
                Dictionary<string, byte[]> files = draft.Pack(out rates, out moves);
                if (moves == null || moves.Length != 1 || moves[0].Name != "happy_wave" || moves[0].Strip != "happy_wave.png" || moves[0].Fps != 16 || !moves[0].Loop || !files.ContainsKey("happy_wave.png") || draft.BaseFolder == null && (rates != null || files.Count != 1))
                    throw new Exception("Her animation editor didn't pack the new move into the skin.");
                string skinFile = Path.Combine(output, "new-move" + Mods.SkinExtension);
                Mods.PackSkin(skinFile, new ModFile { Id = "new-move-test", Name = "New move test", Moves = moves }, files);
                worn = mods.WearOutfit(skinFile);
                Mods.SkinMove move = Mods.MoveNamed("happy_wave");
                string done = capri.Animate("happy_wave"), stopped = capri.Animate("stop_dancing");
                if (worn == null || move == null || move.Frames != 10 || move.Fps != 16 || !move.Loop || done != "Doing my happy wave. Tell me to stop when you've had enough." || stopped != "Stopped my happy wave.")
                    throw new Exception("A move made in her editor didn't play once worn: " + done + " / " + stopped);
                AnimationDraft again = new AnimationDraft(mods);
                try
                {
                    AnimationDraft.Strip opened = again.Strips.Find(each => each.Move && each.Title == "happy_wave");
                    if (opened == null) throw new Exception("Her animation editor didn't open the worn skin's move.");
                    again.Load(opened);
                    if (opened.Count != 10 || !opened.Loop || opened.Fps != 16 || opened.MoveStrip != "happy_wave.png") throw new Exception("Her animation editor opened the worn move wrong: " + opened.Count);
                    again.RemoveMove(opened);
                    Dictionary<string, byte[]> left = again.Pack(out rates, out moves);
                    if (!again.Dirty || moves != null || left.ContainsKey("happy_wave.png")) throw new Exception("Taking a move out in her editor didn't take its strip out of the skin.");
                }
                finally { again.Dispose(); }
                mods.Remove(worn);
                worn = null;
                if (Mods.MoveNamed("happy_wave") != null) throw new Exception("Removing the worn skin left its move.");
                checks.Add("PASS moves: a move from her editor packs into the skin's moves, plays once worn, opens again from that skin, and Remove move takes its strip out");
            }
            finally
            {
                draft.Dispose();
                if (worn != null) mods.Remove(worn);
                Mod back = was == null ? null : mods.All.Find(each => each.File.Id == was.File.Id);
                if (back != null && !back.On) mods.Set(back, true, null);
            }
        }
    }
}

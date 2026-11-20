using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace CapriDesktop
{
    internal sealed partial class Pet
    {
        private Clip[] moveClips = Array.Empty<Clip>();

        internal static Bitmap ReadMoveArt(string file, int count)
        {
            try
            {
                using (MemoryStream stream = new MemoryStream(File.ReadAllBytes(file))) return Finished(stream);
            }
            catch (Exception error)
            {
                if (!(error is IOException || error is UnauthorizedAccessException || error is ArgumentException || error is InvalidOperationException || error is NotSupportedException)) throw;
                Brain.Log("mods", -1, "Move picture " + file + ": " + error.Message);
                PixelSize cell = Mods.MoveCell;
                return new WriteableBitmap(new PixelSize(Math.Max(1, count * cell.Width), cell.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            }
        }

        private void RebuildMoves()
        {
            Mods.SkinMove[] moves = Mods.MovesNow;
            Clip[] was = moveClips, made = new Clip[moves.Length];
            for (int i = 0; i < moves.Length; i++) made[i] = CapriDesktop.Clip.Move(moves[i].Name, moves[i].File, moves[i].Frames, moves[i].Fps, moves[i].Loop, idle);
            moveClips = made;
            if (clip != null && clip.IsMove)
            {
                string playing = clip.Title;
                Clip same = Array.Find(made, each => each.Title == playing);
                if (same != null) clip = same;
                else Play(idle);
            }
            foreach (Clip old in was) old.Reload();
        }

        private Clip MoveClip(string name)
        {
            foreach (Clip move in moveClips) if (move.Title == name) return move;
            return null;
        }

        private string PlayMove(Clip move)
        {
            if (seat != null) StandUp();
            danceHeld = musicOn;
            Dancing = false;
            Play(move);
            string said = move.Title.Replace('_', ' ');
            return move.Loops ? "Doing my " + said + ". Tell me to stop when you've had enough." : "Doing my " + said + ".";
        }

        private static byte[] MoveStrip(int frames)
        {
            PixelSize cell = Mods.MoveCell;
            using (SKBitmap made = new SKBitmap(new SKImageInfo(cell.Width * frames, cell.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul)))
            {
                using (SKCanvas paper = new SKCanvas(made))
                using (SKPaint body = new SKPaint { Color = new SKColor(20, 20, 30, 255) })
                using (SKPaint hand = new SKPaint { Color = new SKColor(0, 255, 255, 255), IsAntialias = true })
                {
                    paper.Clear(SKColors.Transparent);
                    for (int i = 0; i < frames; i++)
                    {
                        paper.DrawRect(i * cell.Width + 30, 120, 56, 250, body);
                        paper.DrawCircle(i * cell.Width + 26 + i * 64f / Math.Max(1, frames - 1), 80, 12, hand);
                    }
                }
                return AnimationDraft.Png(made);
            }
        }

        internal void CheckMoves(List<string> checks, string input)
        {
            Mods mods = ModList;
            PixelSize cell = Mods.MoveCell, own = ArtSize("Capri.Sprite.idle.png");
            if (cell.Width * idle.OwnCount != own.Width || cell.Height != own.Height) throw new Exception("A move's frames aren't her standing frames: " + cell);
            Func<string, string, Dictionary<string, byte[]>, string> pack = delegate(string id, string moves, Dictionary<string, byte[]> files)
            {
                string file = Path.Combine(input, id + Mods.SkinExtension);
                if (File.Exists(file)) File.Delete(file);
                using (ZipArchive zip = ZipFile.Open(file, ZipArchiveMode.Create))
                {
                    using (Stream into = zip.CreateEntry("skin.json").Open())
                    {
                        byte[] details = Encoding.UTF8.GetBytes("{\"format\":1,\"id\":\"" + id + "\",\"name\":\"" + id + "\",\"accessory\":true" + (moves == null ? "" : ",\"moves\":" + moves) + "}");
                        into.Write(details, 0, details.Length);
                    }
                    if (files != null)
                        foreach (KeyValuePair<string, byte[]> entry in files)
                            using (Stream into = zip.CreateEntry(entry.Key).Open()) into.Write(entry.Value, 0, entry.Value.Length);
                }
                return file;
            };
            int animateAt = -1;
            for (int i = 0; i < Hands.Count; i++) if (Hands.Name(i) == "animate") animateAt = i;
            ToolArguments asked = new ToolArguments { Name = "test_wave" };
            Func<double> clockWas = MoveClock;
            bool musicWas = musicOn;
            double now = Now;
            Mod skin = null;
            try
            {
                MoveClock = () => now;
                skin = mods.Import(pack("moves-test", "[{\"name\":\"test_wave\",\"fps\":24},{\"name\":\"test_spin\",\"strip\":\"spin.png\",\"loop\":true}]", new Dictionary<string, byte[]> { { "test_wave.png", MoveStrip(8) }, { "spin.png", MoveStrip(6) } }), null);
                Mods.SkinMove wave = Mods.MoveNamed("test_wave"), spin = Mods.MoveNamed("test_spin");
                if (skin == null || Mods.MovesNow.Length != 2 || wave == null || spin == null || wave.Frames != 8 || wave.Fps != 24 || wave.Loop || spin.Frames != 6 || !spin.Loop || MoveClip("test_wave") == null || MoveClip("test_spin") == null)
                    throw new Exception("A skin's moves didn't load from its skin.json on " + Hands.ThisComputer + ".");
                Hands.Kit kit = Hands.KitFor();
                string described = Hands.Description(animateAt);
                if (!kit.Tools.Contains("\"test_spin\",\"test_wave\"]") || !kit.ClaudeTools.Contains("\"test_spin\",\"test_wave\"]") || !kit.Listing.Contains("|test_spin|test_wave\"")
                    || !kit.Brief.Contains("including the moves their skins added: test spin and test wave") || !described.Contains("test_spin, test_wave") || described.Length > 1024
                    || !Hands.Valid("animate", asked) || Hands.Valid("animate", new ToolArguments { Name = "moonwalk" }))
                    throw new Exception("Her brain didn't get a skin's moves in animate on " + Hands.ThisComputer + ": " + described);
                string tool;
                ToolArguments heard;
                bool doWave = Hands.Heard("do the test wave", out tool, out heard) && tool == "animate" && heard.Name == "test_wave";
                bool bare = Hands.Heard("test wave", out tool, out heard) && tool == "animate";
                if (!doWave || bare) throw new Exception("Asking for a skin's move by name went wrong on " + Hands.ThisComputer + ": do " + doWave + ", bare " + bare);
                checks.Add("PASS moves: a skin's moves load from skin.json, her animate tool lists them for every brain, and \"do the test wave\" asks for one while a bare name stays chat, like on Windows");

                musicOn = false;
                string waved = Hands.Do("animate", asked);
                bool playing = clip.IsMove && clip.Title == "test_wave" && Playing == "test_wave" && clip.Count == 8 && clip.Fps == 24;
                clipStart = now - 3.5 / 24;
                Advance(now);
                bool midway = clip.Title == "test_wave" && body.Shown == clip && body.Cell == 3;
                clipStart = now - 8.5 / 24;
                Advance(now);
                if (waved != "Doing my test wave." || !playing || !midway || clip != idle) throw new Exception("A move didn't play once and go back to her idle on " + Hands.ThisComputer + ": " + waved);
                string spun = Hands.Do("animate", new ToolArguments { Name = "test_spin" });
                clipStart = now - 20;
                Advance(now);
                bool looping = clip.Title == "test_spin";
                string stopped = Hands.Do("animate", new ToolArguments { Name = "stop_dancing" });
                if (spun != "Doing my test spin. Tell me to stop when you've had enough." || !looping || stopped != "Stopped my test spin." || clip != idle) throw new Exception("A looping move didn't go on until stop on " + Hands.ThisComputer + ": " + spun + " / " + stopped);
                checks.Add("PASS moves: a skin's move plays frame by frame at its speed, a play-once move goes back to her idle and a looping one goes on until stop, like on Windows");

                using (ModHost host = new ModHost(this, "moves-code-test"))
                {
                    ICapriBody moving = host;
                    string fromMod = moving.Animate("test_wave"), shown = moving.Animation, unknown = moving.Animate("moonwalk");
                    if (fromMod != "Doing my test wave." || shown != "test_wave" || unknown != "I don't have an animation called moonwalk.") throw new Exception("A code mod couldn't play a skin's move on " + Hands.ThisComputer + ": " + fromMod + " / " + shown + " / " + unknown);
                }
                Play(idle);
                checks.Add("PASS moves: a code mod's Animate(name) plays a skin's move, and an unknown name gets a plain answer, like on Windows");

                Hands.Do("animate", new ToolArguments { Name = "test_spin" });
                mods.Remove(skin);
                skin = null;
                string gone = Hands.Do("animate", asked);
                if (Mods.MovesNow.Length != 0 || clip != idle || MoveClip("test_wave") != null || Hands.Valid("animate", asked) || Hands.KitFor().Tools.Contains("test_wave") || gone != "I don't have an animation called test_wave.")
                    throw new Exception("Removing a skin didn't take its moves away on " + Hands.ThisComputer + ": " + gone);
                byte[] good = MoveStrip(4);
                string[,] cases =
                {
                    { "moves-name", "[{\"name\":\"Big Wave!\"}]", "", "can't be a move's name" },
                    { "moves-own", "[{\"name\":\"dance\"}]", "", "one of her own animations" },
                    { "moves-missing", "[{\"name\":\"test_hop\"}]", "", "needs test_hop.png" },
                    { "moves-path", "[{\"name\":\"test_hop\",\"strip\":\"../test_hop.png\"}]", "", "can't be the strip for test_hop" },
                    { "moves-fast", "[{\"name\":\"test_hop\",\"fps\":99}]", "good", "needs a speed from 1 to 60" },
                    { "moves-stray", null, "good", "sprite strips" }
                };
                for (int i = 0; i < cases.GetLength(0); i++)
                {
                    Dictionary<string, byte[]> files = new Dictionary<string, byte[]>();
                    if (cases[i, 2] == "good") files["test_hop.png"] = good;
                    string bad = pack(cases[i, 0], cases[i, 1], files);
                    try { mods.Import(bad, null); throw new Exception("A bad skin's move was let in on " + Hands.ThisComputer + ": " + cases[i, 0]); }
                    catch (ModException refused) { if (refused.Message.IndexOf(cases[i, 3], StringComparison.Ordinal) < 0) throw new Exception("A bad skin's move gave the wrong reason: " + refused.Message); }
                }
                checks.Add("PASS moves: removing the skin takes its moves away, and bad move names, her own names, missing or outside strips, a speed over 60 and a stray strip are refused with the reason, like on Windows");
                CheckNewMove(checks, input);
            }
            finally
            {
                if (skin != null) mods.Remove(skin);
                MoveClock = clockWas;
                musicOn = musicWas;
                Play(seat != null ? perch : idle);
            }
        }

        private void CheckNewMove(List<string> checks, string input)
        {
            Mods mods = ModList;
            PixelSize cell = Mods.MoveCell;
            string folder = Path.Combine(input, "new-move-frames");
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);
            for (int i = 1; i <= 10; i++)
                using (SKBitmap frame = new SKBitmap(new SKImageInfo(cell.Width, cell.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul)))
                {
                    frame.Erase(new SKColor(20, 20, 30, 255));
                    frame.SetPixel(0, 0, new SKColor((byte)(i * 20), 0, 0, 255));
                    File.WriteAllBytes(Path.Combine(folder, "frame_" + i + ".png"), AnimationDraft.Png(frame));
                }
            string wrong = Path.Combine(input, "move-wrong.png");
            using (SKBitmap small = new SKBitmap(new SKImageInfo(50, 50, SKColorType.Bgra8888, SKAlphaType.Unpremul))) File.WriteAllBytes(wrong, AnimationDraft.Png(small));
            Mod worn = null;
            AnimationDraft draft = new AnimationDraft(mods, Clips);
            try
            {
                MoveSheet sheet = new MoveSheet(this, draft);
                sheet.PickFiles = delegate { return new[] { wrong }; };
                sheet.PickFolder = delegate { return folder; };
                bool unnamed = !sheet.Add() && sheet.Said == "Name your move first.";
                sheet.MoveName = "dance";
                bool hers = !sheet.Add() && sheet.Said.Contains("one of her own animations");
                sheet.MoveName = "Happy Wave";
                sheet.FromStrip();
                bool misfit = sheet.Frames == 0 && sheet.Said.Contains("A move's frames are " + cell.Width + " x " + cell.Height);
                sheet.FromFolder();
                sheet.Retime(1);
                sheet.Retime(1);
                sheet.Loop(true);
                bool ready = sheet.Frames == 10 && sheet.Fps == 16 && sheet.Loops;
                if (!unnamed || !hers || !misfit || !ready || !sheet.Add()) throw new Exception("Her New move window went wrong on " + Hands.ThisComputer + ": " + unnamed + " " + hers + " " + misfit + " " + ready + " / " + sheet.Said);
                AnimationDraft.Strip made = sheet.Made;
                bool inOrder = made != null && made.Frames.Count == 10;
                for (int i = 0; inOrder && i < 10; i++) inOrder = made.Frames[i].GetPixel(0, 0).Red == (i + 1) * 20;
                if (!inOrder || made.Title != "happy_wave" || !made.Loop || made.Fps != 16) throw new Exception("Her New move window didn't hand the editor its move in name order on " + Hands.ThisComputer + ".");
                ModFrames[] rates;
                ModMove[] moves;
                Dictionary<string, byte[]> files = draft.Pack(out rates, out moves);
                if (moves == null || moves.Length != 1 || moves[0].Name != "happy_wave" || moves[0].Strip != "happy_wave.png" || !moves[0].Loop || moves[0].Fps != 16 || !files.ContainsKey("happy_wave.png")) throw new Exception("Her animation editor didn't pack the new move on " + Hands.ThisComputer + ".");
                string skinFile = Path.Combine(input, "new-move" + Mods.SkinExtension);
                Mods.PackSkin(skinFile, new ModFile { Id = "new-move-test", Name = "New move test", Moves = moves }, files);
                worn = mods.WearOutfit(skinFile);
                Mods.SkinMove move = Mods.MoveNamed("happy_wave");
                string done = Animate("happy_wave"), stopped = Animate("stop_dancing");
                if (worn == null || move == null || move.Frames != 10 || move.Fps != 16 || !move.Loop || done != "Doing my happy wave. Tell me to stop when you've had enough." || stopped != "Stopped my happy wave.")
                    throw new Exception("A move made in her editor didn't play once worn on " + Hands.ThisComputer + ": " + done + " / " + stopped);
                AnimationDraft again = new AnimationDraft(mods, Clips);
                try
                {
                    AnimationDraft.Strip opened = again.Strips.Find(each => each.Move && each.Title == "happy_wave");
                    if (opened == null) throw new Exception("Her animation editor didn't open the worn skin's move on " + Hands.ThisComputer + ".");
                    again.Load(opened);
                    if (opened.Count != 10 || !opened.Loop || opened.Fps != 16) throw new Exception("Her animation editor opened the worn move wrong on " + Hands.ThisComputer + ": " + opened.Count);
                    again.RemoveMove(opened);
                    Dictionary<string, byte[]> left = again.Pack(out rates, out moves);
                    if (!again.Dirty || moves != null || left.ContainsKey("happy_wave.png")) throw new Exception("Taking a move out in her editor didn't take its strip out on " + Hands.ThisComputer + ".");
                }
                finally { again.Dispose(); }
                mods.Remove(worn);
                worn = null;
                if (Mods.MoveNamed("happy_wave") != null) throw new Exception("Removing the worn skin left its move on " + Hands.ThisComputer + ".");
                checks.Add("PASS moves: her New move window makes a move from a folder of frames in name order with its speed and loop, refuses her own names and wrong-sized frames, and her editor packs it into a skin she plays it from, like on Windows");
            }
            finally
            {
                draft.Dispose();
                if (worn != null) mods.Remove(worn);
            }
        }
    }
}

using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.CSharp;

namespace CapriDesktop
{
    public interface ICapriHost
    {
        string Folder { get; }
        void Say(string line);
        void Log(string line);
        Bitmap Art(string name);
        void PlayIntro();
        void Sound(string name);
    }

    public interface ICapriIntro
    {
        double Seconds { get; }
        void Begin(Bitmap desktop, Rectangle screen, Rectangle her);
        void Draw(Graphics graphics, Rectangle screen, Rectangle her, double at);
    }

    public interface ICapriMod
    {
        void Start(ICapriHost capri);
        bool Heard(string words);
        void Stop();
    }

    internal sealed partial class Mods
    {
        internal const string CodeExtension = ".capri-code";
        private const int LargestCode = 256 * 1024;
        private const string ApprovedFile = "approved.txt";
        private static readonly Regex HeaderLine = new Regex(@"^//\s*([a-z-]+)\s*:\s*(.*?)\s*$");
        private readonly Dictionary<string, ICapriMod> running = new Dictionary<string, ICapriMod>();
        internal Func<string, ICapriHost> HostFor;

        internal int Running { get { return running.Count; } }
        internal bool WantsIntro { get { return All.Exists(mod => mod.Code && mod.On && mod.File.Intro); } }
        internal bool CodeSettled { get; private set; }

        internal ICapriIntro Intro
        {
            get
            {
                foreach (ICapriMod mod in running.Values)
                {
                    ICapriIntro intro = mod as ICapriIntro;
                    if (intro != null) return intro;
                }
                return null;
            }
        }

        internal static ModFile ReadCode(string text)
        {
            ModFile read = new ModFile { WakeNames = new string[0] };
            bool marked = false;
            foreach (string raw in (text ?? "").TrimStart('﻿').Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                Match field = HeaderLine.Match(line);
                if (!field.Success) break;
                string value = field.Groups[2].Value;
                switch (field.Groups[1].Value)
                {
                    case "capri-mod": marked = true; int.TryParse(value, out read.Format); break;
                    case "id": read.Id = value; break;
                    case "name": read.Name = value; break;
                    case "version": read.Version = value; break;
                    case "author": read.Author = value; break;
                    case "description": read.Description = value; break;
                    case "intro": read.Intro = value == "yes"; break;
                }
            }
            if (!marked) throw new ModException("A code mod starts with a // capri-mod: 1 line.");
            CheckDetails(read);
            return read;
        }

        internal static string HashOf(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        private static string Words(byte[] bytes) { return new UTF8Encoding(false).GetString(bytes); }

        private Mod LoadCode(string file, HashSet<string> off)
        {
            byte[] bytes = File.ReadAllBytes(file);
            ModFile read = ReadCode(Words(bytes));
            if (!string.Equals(read.Id, Path.GetFileNameWithoutExtension(file), StringComparison.OrdinalIgnoreCase)) throw new ModException("its file name doesn't match its id, " + read.Id + ".");
            string hash = HashOf(bytes), approved;
            bool same = Approved().TryGetValue(read.Id, out approved) && approved == hash;
            return new Mod { File = read, Path = file, Code = true, Hash = hash, Changed = !same, On = same && !off.Contains(read.Id) };
        }

        private Mod ImportCode(string file, Func<ModFile, string, bool> ask)
        {
            byte[] bytes;
            try
            {
                if (new FileInfo(file).Length > LargestCode) throw new ModException("That code mod is too big.");
                bytes = File.ReadAllBytes(file);
            }
            catch (IOException error) { throw new ModException("Couldn't read that file: " + error.Message); }
            catch (UnauthorizedAccessException error) { throw new ModException("Couldn't read that file: " + error.Message); }
            string text = Words(bytes), hash = HashOf(bytes);
            ModFile read = ReadCode(text);
            if (!ask(read, hash)) return null;
            ICapriMod made = Compile(text);
            string home = Path.Combine(folder, read.Id + CodeExtension);
            try
            {
                Directory.CreateDirectory(folder);
                string writing = home + ".new";
                File.WriteAllBytes(writing, bytes);
                StopOne(read.Id);
                if (File.Exists(home)) File.Delete(home);
                File.Move(writing, home);
                Approve(read.Id, hash);
                HashSet<string> off = Off();
                if (off.Remove(read.Id)) WriteOff(off);
            }
            catch (IOException error) { throw new ModException("Couldn't save that mod: " + error.Message); }
            catch (UnauthorizedAccessException error) { throw new ModException("Couldn't save that mod: " + error.Message); }
            Load();
            try { StartOne(read.Id, made); }
            catch (ModException error)
            {
                // Saved and approved, but a Start that failed left it not running, so it's off in the list until they turn it on.
                HashSet<string> stopped = Off();
                if (stopped.Add(read.Id)) WriteOff(stopped);
                Load();
                throw new ModException(error.Message.TrimEnd('.') + ". It's saved, and off until you turn it on again.");
            }
            return All.Find(mod => mod.File.Id == read.Id);
        }

        private bool SetCode(Mod mod, bool on, Func<ModFile, string, bool> ask)
        {
            HashSet<string> off = Off();
            if (!on)
            {
                StopOne(mod.File.Id);
                off.Add(mod.File.Id);
                WriteOff(off);
                mod.On = false;
                return true;
            }
            byte[] bytes = File.ReadAllBytes(mod.Path);
            string hash = HashOf(bytes), approved;
            bool same = Approved().TryGetValue(mod.File.Id, out approved) && approved == hash;
            if (!same && !ask(mod.File, hash)) return false;
            ICapriMod made = Compile(Words(bytes));
            StartOne(mod.File.Id, made);
            if (!same) Approve(mod.File.Id, hash);
            if (off.Remove(mod.File.Id)) WriteOff(off);
            mod.Hash = hash;
            mod.Changed = false;
            mod.On = true;
            return true;
        }

        private void RemoveCode(Mod mod)
        {
            StopOne(mod.File.Id);
            Dictionary<string, string> approved = Approved();
            if (approved.Remove(mod.File.Id)) WriteApproved(approved);
        }

        private Dictionary<string, string> Approved()
        {
            Dictionary<string, string> approved = new Dictionary<string, string>();
            string file = Path.Combine(folder, ApprovedFile);
            if (File.Exists(file))
                foreach (string line in File.ReadAllLines(file))
                {
                    string[] parts = line.Split(' ');
                    if (parts.Length == 2) approved[parts[0]] = parts[1];
                }
            return approved;
        }

        private void Approve(string id, string hash)
        {
            Dictionary<string, string> approved = Approved();
            approved[id] = hash;
            WriteApproved(approved);
        }

        private void WriteApproved(Dictionary<string, string> approved)
        {
            List<string> lines = new List<string>();
            foreach (KeyValuePair<string, string> entry in approved) lines.Add(entry.Key + " " + entry.Value);
            Directory.CreateDirectory(folder);
            File.WriteAllLines(Path.Combine(folder, ApprovedFile), lines);
        }

        private static ICapriMod Compile(string text)
        {
            CompilerResults built;
            List<Type> mods = new List<Type>();
            // A missing or blocked compiler, or a mod that won't load, fails that mod: at startup this runs on a worker thread,
            // where any other error would end her.
            try
            {
                using (CSharpCodeProvider compiler = new CSharpCodeProvider())
                {
                    CompilerParameters options = new CompilerParameters { GenerateInMemory = true, GenerateExecutable = false, CompilerOptions = "/optimize+" };
                    options.ReferencedAssemblies.AddRange(new[] { "System.dll", "System.Core.dll", "System.Drawing.dll", "System.Windows.Forms.dll", typeof(ICapriMod).Assembly.Location });
                    built = compiler.CompileAssemblyFromSource(options, text);
                }
                foreach (CompilerError error in built.Errors)
                    if (!error.IsWarning) throw new ModException("That code didn't compile, line " + error.Line + ": " + error.ErrorText);
                foreach (Type type in built.CompiledAssembly.GetTypes())
                    if (typeof(ICapriMod).IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface) mods.Add(type);
            }
            catch (System.Runtime.InteropServices.ExternalException error) { throw new ModException("Couldn't compile that mod: " + error.Message); }
            catch (InvalidOperationException error) { throw new ModException("Couldn't compile that mod: " + error.Message); }
            catch (ArgumentException error) { throw new ModException("Couldn't compile that mod: " + error.Message); }
            catch (ReflectionTypeLoadException error) { throw new ModException("Couldn't load that mod: " + error.Message); }
            catch (TypeLoadException error) { throw new ModException("Couldn't load that mod: " + error.Message); }
            catch (BadImageFormatException error) { throw new ModException("Couldn't load that mod: " + error.Message); }
            catch (System.Security.SecurityException error) { throw new ModException("Couldn't load that mod: " + error.Message); }
            // The compiler writes temp files, so a blocked temp folder or an antivirus lock fails here too.
            catch (IOException error) { throw new ModException("Couldn't compile that mod: " + error.Message); }
            catch (UnauthorizedAccessException error) { throw new ModException("Couldn't compile that mod: " + error.Message); }
            if (mods.Count != 1) throw new ModException("A code mod needs exactly one class that implements ICapriMod. That one has " + mods.Count + ".");
            try { return (ICapriMod)Activator.CreateInstance(mods[0]); }
            catch (Exception error) { throw new ModException("That mod failed while loading: " + Inner(error).Message); }
        }

        private static Exception Inner(Exception error) { return error is TargetInvocationException && error.InnerException != null ? error.InnerException : error; }

        private void StartOne(string id, ICapriMod made)
        {
            StopOne(id);
            try { made.Start(HostFor(id)); }
            catch (Exception error)
            {
                Brain.Log("mod " + id, -1, Inner(error).ToString());
                throw new ModException("That mod failed while starting: " + Inner(error).Message);
            }
            running[id] = made;
        }

        private void StopOne(string id)
        {
            ICapriMod mod;
            if (!running.TryGetValue(id, out mod)) return;
            running.Remove(id);
            try { mod.Stop(); }
            catch (Exception error) { Brain.Log("mod " + id, -1, Inner(error).ToString()); }
        }

        internal void StopCode()
        {
            foreach (string id in new List<string>(running.Keys)) StopOne(id);
        }

        internal void StartCode(Action<Action> onWindow)
        {
            List<Mod> wanted = All.FindAll(mod => mod.Code && mod.On);
            CodeSettled = wanted.Count == 0;
            if (wanted.Count == 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                foreach (Mod mod in wanted)
                {
                    Mod starting = mod;
                    try
                    {
                        byte[] bytes = File.ReadAllBytes(starting.Path);
                        if (HashOf(bytes) != starting.Hash) throw new ModException("it changed after it was approved.");
                        ICapriMod made = Compile(Words(bytes));
                        onWindow(delegate
                        {
                            try { StartOne(starting.File.Id, made); }
                            catch (ModException error) { StartFailed(starting, error); }
                        });
                    }
                    catch (Exception error)
                    {
                        if (!(error is ModException || error is IOException || error is UnauthorizedAccessException)) throw;
                        Exception failed = error;
                        onWindow(delegate { StartFailed(starting, failed); });
                    }
                }
                onWindow(delegate { CodeSettled = true; });
            });
        }

        private void StartFailed(Mod mod, Exception error)
        {
            mod.On = false;
            string problem = Path.GetFileName(mod.Path) + ": " + error.Message;
            Problems.Add(problem);
            Brain.Log("mods", -1, problem);
        }

        internal bool Heard(string words)
        {
            bool taken = false;
            foreach (KeyValuePair<string, ICapriMod> entry in new List<KeyValuePair<string, ICapriMod>>(running))
            {
                try { if (entry.Value.Heard(words)) taken = true; }
                catch (Exception error) { Brain.Log("mod " + entry.Key, -1, Inner(error).ToString()); }
            }
            return taken;
        }
    }

    internal sealed partial class Companion
    {
        private sealed class ModHost : ICapriHost
        {
            private readonly Companion capri;
            private readonly string id;

            internal ModHost(Companion owner, string name)
            {
                capri = owner;
                id = name;
            }

            public string Folder
            {
                get
                {
                    string home = Path.Combine(capri.ModList.Folder, id + "-data");
                    Directory.CreateDirectory(home);
                    return home;
                }
            }

            public void Say(string line)
            {
                string said = (line ?? "").Trim();
                if (said.Length == 0) return;
                if (said.Length > 500) said = said.Substring(0, 500);
                capri.Post(delegate { capri.Remark("(mod)", said); });
            }

            public void Log(string line) { Brain.Log("mod " + id, -1, line ?? ""); }

            public Bitmap Art(string name)
            {
                if (name == null || !Regex.IsMatch(name, "^[a-z_]+$")) return null;
                return LoadArt("Capri.Sprite." + name + ".png");
            }

            public void Sound(string name)
            {
                switch (name)
                {
                    case "arrive": Sfx.Play(Cue.Arrive); break;
                    case "leave": Sfx.Play(Cue.Leave); break;
                    case "hop": Sfx.Play(Cue.Hop); break;
                    case "click": Sfx.Play(Cue.Click); break;
                    case "bonk": Sfx.Play(Cue.Bonk); break;
                }
            }

            public void PlayIntro()
            {
                capri.Post(delegate
                {
                    ICapriIntro intro = capri.ModList.Intro;
                    if (intro != null && capri.introScreen == null) capri.RunIntro(intro, null);
                });
            }
        }

        private const double LongestIntro = 15, IntroWait = 8;
        private IntroScreen introScreen;

        private bool HoldForIntro(Action arrival, bool spawn)
        {
            if (testing || instant || !preferences.Motion || preferences.NoIntro || hidden || !ModList.WantsIntro || FullScreenDisplay) return false;
            if (spawn) SpawnInMiddle();
            WaitForIntro(arrival, IntroWait);
            return true;
        }

        private void WaitForIntro(Action arrival, double patience)
        {
            veilUntil = clock.Elapsed.TotalSeconds + patience + LongestIntro + 5;
            Play(idle);
            RenderFrame();
            Action release = delegate
            {
                veilUntil = 0;
                drawnClip = null;
                arrival();
            };
            double gaveUpAt = clock.Elapsed.TotalSeconds + patience;
            System.Windows.Forms.Timer wait = new System.Windows.Forms.Timer { Interval = 50 };
            wait.Tick += delegate
            {
                if (IsDisposed) { wait.Dispose(); return; }
                ICapriIntro intro = ModList.Intro;
                bool out_ = clock.Elapsed.TotalSeconds >= gaveUpAt;
                if (intro == null && !ModList.CodeSettled && !out_) return;
                wait.Dispose();
                if (intro == null) release();
                else RunIntro(intro, release);
            };
            wait.Start();
        }

        private void RunIntro(ICapriIntro intro, Action done)
        {
            Rectangle body = BodyOnScreen(anchor, Body);
            Rectangle screen = Screen.FromRectangle(body).Bounds;
            body.Offset(-screen.X, -screen.Y);
            double length = LongestIntro;
            try { length = Math.Max(0, Math.Min(LongestIntro, intro.Seconds)); }
            catch (Exception error) { Brain.Log("mod intro", -1, error.ToString()); length = 0; }
            Action finished = delegate
            {
                introScreen = null;
                if (done != null) done();
            };
            if (length <= 0) { finished(); return; }
            introScreen = new IntroScreen(intro, screen, body, length, finished);
            introScreen.Begin();
        }

        private sealed class IntroScreen : Form
        {
            private readonly ICapriIntro intro;
            private readonly Rectangle her;
            private readonly double length;
            private readonly Action finished;
            private readonly System.Diagnostics.Stopwatch ran = new System.Diagnostics.Stopwatch();
            private readonly System.Windows.Forms.Timer beat = new System.Windows.Forms.Timer { Interval = 15 };
            private Native.Surface surface;
            private Bitmap desktop;
            private bool over;

            internal IntroScreen(ICapriIntro mod, Rectangle screen, Rectangle body, double seconds, Action done)
            {
                intro = mod;
                her = body;
                length = seconds;
                finished = done;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                Bounds = screen;
                beat.Tick += delegate { Frame(); };
            }

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams value = base.CreateParams;
                    value.ExStyle |= 0x00080000 | 0x00000080 | 0x08000000 | 0x00000020 | 0x00000008;
                    return value;
                }
            }

            protected override bool ShowWithoutActivation { get { return true; } }

            internal void Begin()
            {
                try
                {
                    surface = new Native.Surface(Width, Height);
                    desktop = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                    try { using (Graphics grab = Graphics.FromImage(desktop)) grab.CopyFromScreen(Bounds.Location, Point.Empty, Bounds.Size); }
                    catch (System.ComponentModel.Win32Exception) { using (Graphics grab = Graphics.FromImage(desktop)) grab.Clear(Color.Black); }
                    intro.Begin(desktop, new Rectangle(0, 0, Width, Height), her);
                    Show();
                }
                catch (Exception error)
                {
                    Brain.Log("mod intro", -1, error.ToString());
                    End();
                    return;
                }
                ran.Start();
                beat.Start();
                Frame();
            }

            private void Frame()
            {
                if (over) return;
                double at = ran.Elapsed.TotalSeconds;
                if (at >= length) { End(); return; }
                try
                {
                    using (Graphics graphics = Graphics.FromImage(surface.Image))
                    {
                        graphics.Clear(Color.Transparent);
                        intro.Draw(graphics, new Rectangle(0, 0, Width, Height), her, at);
                    }
                    surface.Show(Handle, Location);
                }
                catch (Exception error)
                {
                    Brain.Log("mod intro", -1, error.ToString());
                    End();
                }
            }

            private void End()
            {
                if (over) return;
                over = true;
                beat.Dispose();
                Close();
                Dispose();
                if (surface != null) surface.Dispose();
                if (desktop != null) desktop.Dispose();
                finished();
            }
        }

        private sealed class TestIntro : ICapriIntro
        {
            internal int Drawn;
            internal bool Throws;
            public double Seconds { get { return 0.3; } }
            public void Begin(Bitmap desktop, Rectangle screen, Rectangle her) { }
            public void Draw(Graphics graphics, Rectangle screen, Rectangle her, double at)
            {
                Drawn++;
                if (Throws) throw new Exception("boom");
            }
        }

        private void CheckIntros(List<string> checks, string output)
        {
            Func<Func<bool>, bool> until = delegate(Func<bool> done)
            {
                for (int i = 0; i < 200 && !done(); i++) { Application.DoEvents(); Thread.Sleep(25); }
                return done();
            };
            foreach (bool throws in new[] { false, true })
            {
                TestIntro stub = new TestIntro { Throws = throws };
                bool ended = false;
                RunIntro(stub, delegate { ended = true; });
                if (!until(() => ended) || stub.Drawn == 0 || introScreen != null) throw new Exception("An intro that " + (throws ? "throws" : "runs") + " did not end cleanly.");
            }
            checks.Add("PASS mod intros: an intro plays and ends, and one that throws still ends");
            bool arrived = false;
            Clip clipWas = clip;
            bool settledWas = ModList.CodeSettled;
            WaitForIntro(delegate { arrived = true; }, 0.2);
            bool veilHeld = clock.Elapsed.TotalSeconds < veilUntil;
            if (!veilHeld || !until(() => arrived) || clock.Elapsed.TotalSeconds < veilUntil) throw new Exception("Waiting for an intro that never comes did not bring her in.");
            Play(clipWas);
            checks.Add("PASS mod intros: if no intro turns up she's held off screen, then still arrives");
            string real = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "..", "..", "mods", "startup-intros" + Mods.CodeExtension));
            if (!File.Exists(real)) { checks.Add("LIMIT mod intros: the Startup Intros file isn't beside this build, so its frames weren't drawn"); return; }
            Mod mod = ModList.Import(real, delegate { return true; });
            ICapriIntro intros = ModList.Intro;
            if (mod == null || intros == null || !ModList.WantsIntro) throw new Exception("Startup Intros did not start as an intro.");
            Size size = new Size(1920, 1080);
            Rectangle spot = new Rectangle(860, 420, 200, 300);
            string[] picks = { "hijack", "shatter", "wake" };
            List<string> timing = new List<string>();
            foreach (string pick in picks)
            {
                if (!ModList.Heard("intro " + pick)) throw new Exception("Startup Intros didn't take intro " + pick);
                const int across = 5, down = 3, cellW = 384, cellH = 216;
                double seconds = intros.Seconds, worst = 0, worstAt = 0;
                using (Bitmap fake = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
                using (Bitmap sheet = new Bitmap(across * cellW, down * cellH))
                using (Graphics paper = Graphics.FromImage(sheet))
                using (Bitmap frame = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
                using (Graphics pen = Graphics.FromImage(frame))
                {
                    using (Graphics wall = Graphics.FromImage(fake)) wall.Clear(Color.FromArgb(46, 58, 74));
                    intros.Begin(fake, new Rectangle(Point.Empty, size), spot);
                    for (int i = 0; i < across * down; i++)
                    {
                        double at = seconds * i / (across * down - 1) * 0.999;
                        pen.Clear(Color.Transparent);
                        System.Diagnostics.Stopwatch took = System.Diagnostics.Stopwatch.StartNew();
                        intros.Draw(pen, new Rectangle(Point.Empty, size), spot, at);
                        if (i > 0 && took.Elapsed.TotalMilliseconds > worst) { worst = took.Elapsed.TotalMilliseconds; worstAt = at; }
                        Rectangle cell = new Rectangle(i % across * cellW, i / across * cellH, cellW, cellH);
                        using (Brush desktop = new SolidBrush(Color.FromArgb(46, 58, 74))) paper.FillRectangle(desktop, cell);
                        paper.DrawImage(frame, cell);
                        using (Font label = new Font("Consolas", 11f)) paper.DrawString(at.ToString("0.00") + "s", label, Brushes.Yellow, cell.X + 3, cell.Y + 3);
                    }
                    // A frame caught by a garbage collection or a busy PC (83 ms once on 5 Nov) is drawn again, so only a frame
                    // that's slow every time fails. Her frames depend only on the time, and each sound plays once.
                    if (worst > 60)
                    {
                        pen.Clear(Color.Transparent);
                        System.Diagnostics.Stopwatch again = System.Diagnostics.Stopwatch.StartNew();
                        intros.Draw(pen, new Rectangle(Point.Empty, size), spot, worstAt);
                        worst = Math.Min(worst, again.Elapsed.TotalMilliseconds);
                    }
                    sheet.Save(Path.Combine(output, "intro-" + pick + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                timing.Add(pick + " " + seconds.ToString("0.0") + " s, slowest frame " + worst.ToString("0") + " ms");
                if (worst > 60) throw new Exception("An intro frame is too slow: " + timing[timing.Count - 1]);
            }
            for (int i = 0; i < 40; i++) { Application.DoEvents(); Thread.Sleep(10); }
            bool previewed = until(() => introScreen == null) || until(() => introScreen == null);
            ModList.Remove(mod);
            if (!previewed || ModList.Running != 0) throw new Exception("Startup Intros did not clean up.");
            checks.Add("PASS mod intros: all three draw at 1920x1080 (" + string.Join("; ", timing) + ")");
        }

        internal void CheckCodeMods(List<string> checks, string output)
        {
            Mods mods = ModList;
            int asked = 0;
            bool answer = false;
            Func<ModFile, string, bool> askWas = mainframe.ModAsk;
            mainframe.ModAsk = delegate { asked++; return answer; };
            Func<string, string, string> sample = delegate(string name, string body)
            {
                string file = Path.Combine(output, name);
                File.WriteAllText(file, body);
                return file;
            };
            Func<string, string, string> code = delegate(string id, string body)
            {
                return "// capri-mod: 1\n// id: " + id + "\n// name: " + id + "\n// version: 1\n// author: Smoke\n// description: A test.\nusing System; using System.IO; using CapriDesktop;\n" + body;
            };
            try
            {
                foreach (Mod old in mods.All.ToArray()) mods.Remove(old);
                string body = "public class M : ICapriMod { ICapriHost c; public void Start(ICapriHost h) { c = h; File.AppendAllText(Path.Combine(h.Folder, \"started\"), \"x\"); } public bool Heard(string w) { if (w != \"mod check\") return false; c.Say(\"Code mod online.\"); return true; } public void Stop() { File.WriteAllText(Path.Combine(c.Folder, \"stopped\"), \"x\"); } }";
                string file = sample("hello" + Mods.CodeExtension, code("hello", body));
                string started = Path.Combine(mods.Folder, "hello-data", "started"), stopped = Path.Combine(mods.Folder, "hello-data", "stopped");
                if (mainframe.ImportMod(file) || asked != 1 || mods.All.Count != 0 || mods.Running != 0 || File.Exists(started)) throw new Exception("A code mod ran without being approved.");
                checks.Add("PASS code mods: saying no leaves nothing installed and runs nothing");
                answer = true;
                if (!mainframe.ImportMod(file)) throw new Exception("An approved code mod was refused: " + mainframe.ModStatusText);
                if (mods.Running != 1 || !File.Exists(started) || !mods.All[0].Code || !mods.All[0].On) throw new Exception("An approved code mod did not start.");
                if (mods.Heard("something else")) throw new Exception("A mod took a line it didn't want.");
                string replyWas = reply;
                Hear("mod check", "mod check", "> you: ");
                for (int i = 0; i < 20 && reply != "Code mod online."; i++) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(50); }
                if (reply != "Code mod online.") throw new Exception("A code mod's line wasn't said. Reply: " + replyWas + " -> " + reply);
                checks.Add("PASS code mods: approved code compiles, starts, hears what they say and speaks through her");
                mods.StopCode();
                File.AppendAllText(mods.All[0].Path, "\n// changed");
                mods.Load();
                if (!mods.All[0].Changed || mods.All[0].On) throw new Exception("A changed code mod kept its approval.");
                int startsWere = File.ReadAllText(started).Length;
                bool started2 = false;
                mods.StartCode(delegate(Action act) { act(); started2 = true; });
                Thread.Sleep(300);
                if (started2 || mods.Running != 0 || File.ReadAllText(started).Length != startsWere) throw new Exception("A changed code mod ran.");
                answer = false;
                if (mainframe.SwitchModForTest(0) || mods.Running != 0 || mods.All[0].On) throw new Exception("A changed code mod turned on after they said no.");
                answer = true;
                if (!mainframe.SwitchModForTest(0) || mods.Running != 1 || !mods.All[0].On || mods.All[0].Changed) throw new Exception("Approving a changed code mod did not run it.");
                checks.Add("PASS code mods: any edit needs approving again, and until then it stays off");
                mods.StopCode();
                mods.Load();
                List<Action> queued = new List<Action>();
                mods.StartCode(delegate(Action act) { lock (queued) queued.Add(act); });
                for (int i = 0; i < 600; i++)
                {
                    lock (queued) if (queued.Count >= 2) break;
                    Thread.Sleep(50);
                }
                lock (queued)
                {
                    if (queued.Count < 2) throw new Exception("An approved code mod did not compile at start.");
                    foreach (Action act in queued) act();
                }
                if (!mods.CodeSettled) throw new Exception("Code mods never said they'd settled.");
                if (mods.Running != 1) throw new Exception("An approved code mod did not come back after a restart: " + string.Join(" | ", mods.Problems));
                if (!mainframe.SwitchModForTest(0) || mods.Running != 0 || !File.Exists(stopped)) throw new Exception("Turning a code mod off did not stop it.");
                mods.Load();
                if (mods.All[0].On || mods.All[0].Changed) throw new Exception("Off did not last, or looked like an edit.");
                mods.Remove(mods.All[0]);
                checks.Add("PASS code mods: approved ones start with her, and off stops them and stays off");
                string[,] bad =
                {
                    { "broken", "public class M : ICapriMod { nope }", "compile" },
                    { "none", "public class M { }", "exactly one" },
                    { "twoclasses", "public class A : ICapriMod { public void Start(ICapriHost h) { } public bool Heard(string w) { return false; } public void Stop() { } } public class B : A { }", "exactly one" },
                    { "throws", "public class M : ICapriMod { public void Start(ICapriHost h) { throw new Exception(\"boom\"); } public bool Heard(string w) { return false; } public void Stop() { } }", "boom" },
                    { "maker", "public class M : ICapriMod { public M() { throw new Exception(\"bang\"); } public void Start(ICapriHost h) { } public bool Heard(string w) { return false; } public void Stop() { } }", "bang" }
                };
                for (int i = 0; i < bad.GetLength(0); i++)
                {
                    bool let = mainframe.ImportMod(sample(bad[i, 0] + Mods.CodeExtension, code(bad[i, 0], bad[i, 1])));
                    if (bad[i, 0] == "throws")
                    {
                        if (let || mods.Running != 0 || mainframe.ModStatusText.IndexOf(bad[i, 2], StringComparison.Ordinal) < 0 || mods.All.Count != 1 || mods.All[0].On || !mainframe.ModStatusText.EndsWith("It's saved, and off until you turn it on again.", StringComparison.Ordinal) || !mainframe.ModStatusHovers) throw new Exception("A mod that throws at start was let run, or still showed On: " + mainframe.ModStatusText);
                        if (Brain.LogPath != null && (!File.Exists(Brain.LogPath) || File.ReadAllText(Brain.LogPath).IndexOf("off until you turn it on again", StringComparison.Ordinal) < 0)) throw new Exception("A mod that failed to start left no line in errors.txt.");
                        checks.Add("PASS code mods: one whose Start fails is saved but shows off and says so, its reason goes to errors.txt, and the Mods status line takes the mouse so a long error shows in full on hover");
                        foreach (Mod left in mods.All.ToArray()) mods.Remove(left);
                    }
                    else if (let || mods.All.Count != 0 || mainframe.ModStatusText.IndexOf(bad[i, 2], StringComparison.Ordinal) < 0) throw new Exception("A bad code mod was let in: " + bad[i, 0] + ": " + mainframe.ModStatusText);
                }
                string thrower = code("thrower", "public class M : ICapriMod { public void Start(ICapriHost h) { } public bool Heard(string w) { throw new Exception(\"boom\"); } public void Stop() { throw new Exception(\"boom\"); } }");
                if (!mainframe.ImportMod(sample("thrower" + Mods.CodeExtension, thrower))) throw new Exception("A mod that throws later was refused.");
                if (mods.Heard("anything") || mods.All.Count != 1) throw new Exception("A throwing mod took a line.");
                mods.Remove(mods.All[0]);
                if (mods.Running != 0) throw new Exception("A throwing mod could not be removed.");
                if (mainframe.ImportMod(sample("noheader" + Mods.CodeExtension, "using System;"))) throw new Exception("A file with no details was let in.");
                checks.Add("PASS code mods: broken code is refused with its reason, and a mod that throws can't take her down");
                CheckIntros(checks, output);
            }
            finally
            {
                mainframe.ModAsk = askWas;
                mods.StopCode();
                foreach (Mod left in mods.All.ToArray()) mods.Remove(left);
            }
        }
    }
}

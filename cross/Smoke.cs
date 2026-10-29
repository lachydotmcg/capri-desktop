using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace CapriDesktop
{
    internal sealed class Smoke
    {
        private readonly Pet pet;
        private readonly string folder;
        private readonly List<string> checks = new List<string>();
        private readonly List<Process> shelves = new List<Process>();
        private readonly List<short[]> sounds = new List<short[]>();
        private int step;
        private double started = -1;
        private double stepStart = -1;
        private double settled = -1;
        private double coverAt = -1;
        private double hideAt = -1;
        private bool finished;
        private Process shelf;
        private Process cover;
        private PixelRect shelfAsked;
        private PixelRect coverAsked;
        private Spot shelfSpot;
        private PixelPoint hipBefore;
        private int dx;
        private int dy;
        private int arrivalsBefore;
        private double outFrom = -1;
        private TcpListener fakeVoice;
        private readonly List<string> voiceAsked = new List<string>();
        private readonly List<byte[]> spoken = new List<byte[]>();

        private bool blinked;
        private bool slept;
        private double noticedBefore = -1;
        private bool poppedAgain;
        private bool hushGlance;
        private int attention;
        private string attentionAsk, attentionSulk;
        private bool attentionAsked, attentionSulked;
        private double emptyAt;
        private Func<byte[]> peekWas;
        private readonly List<string> glanceAsks = new List<string>();
        private const string GlanceSaid = "SAW: a racing game, a red car in first place. SAY: First place? Don't choke now.";

        private static byte[] FakeScreen(SkiaSharp.SKColor color)
        {
            using (SkiaSharp.SKBitmap screen = new SkiaSharp.SKBitmap(320, 180))
            {
                screen.Erase(color);
                using (SkiaSharp.SKImage image = SkiaSharp.SKImage.FromBitmap(screen))
                using (SkiaSharp.SKData png = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100)) return png.ToArray();
            }
        }

        internal Smoke(Pet pet, string folder)
        {
            this.pet = pet;
            this.folder = folder;
            Hands.PleaseRoll = delegate { return 1; };
            Directory.CreateDirectory(folder);
            if (File.Exists(Path.Combine(folder, "me.md"))) File.Delete(Path.Combine(folder, "me.md"));
            if (File.Exists(pet.LoginFile)) File.Delete(pet.LoginFile);
            foreach (string old in new[] { Brain.PersonalityPath, Brain.PersonalityBackup }) if (old != null && File.Exists(old)) File.Delete(old);
            Brain.MakePersonality();
        }

        internal void Tick(double now)
        {
            if (finished) return;
            if (started < 0) started = now;
            if (stepStart < 0) stepStart = now;
            if (now - started > 180) { Finish("Timed out at step " + step + "."); return; }
            if (now - stepStart > 15) { checks.Add("INFO " + Seen()); Finish("Step " + step + " took longer than 15 seconds."); return; }
            try
            {
                if (Step(now))
                {
                    step++;
                    stepStart = now;
                }
            }
            catch (Exception error) { Finish(error.Message); }
        }

        private int Px(double dip) { return (int)Math.Round(dip * pet.DesktopScaling); }

        private PixelRect Work()
        {
            Screen screen = pet.Screens.Primary ?? pet.ScreenHere();
            if (screen == null) throw new Exception("No screen was found.");
            return screen.WorkingArea;
        }

        private bool Sitting()
        {
            Ledge seat = pet.Seat;
            return seat != null && pet.Hip.Y == seat.Y;
        }

        private bool Step(double now)
        {
            if (step == 0)
            {
                foreach (Clip clip in pet.Clips) if (!clip.Even || clip.Strip.PixelSize.Height != 384) throw new Exception("A sprite strip doesn't split into even 384 px cells.");
                checks.Add("PASS sprites: all " + pet.Clips.Count + " clips, standing and perched, load from the app and split into even cells");
                foreach (string clipName in new[] { "morning", "late", "miss" })
                {
                    byte[] hello = Pet.Hello(clipName);
                    if (hello == null || Voice.Seconds(hello) < 1 || Voice.Seconds(hello) > 5) throw new Exception("The " + clipName + " startup recording is missing or the wrong length.");
                }
                if (Brain.Greeting(8) != "Good morning, choom." || Brain.Greeting(23) != "Isn't it late, choom?" || Brain.Greeting(15) != "Miss me, choom?" || Brain.GreetingClip(15) != "miss") throw new Exception("Startup greetings came at the wrong hours.");
                checks.Add("PASS startup voice: her giggle greetings for the morning, late at night and the rest of the day are packed in the app");
                if (pet.Arrivals != 1) throw new Exception("She didn't glitch in when she started.");
                checks.Add("PASS glitch in: she glitches onto the screen when she starts, then drops");
                if (pet.ActualTransparencyLevel == WindowTransparencyLevel.Transparent) checks.Add("PASS her window is see-through");
                else checks.Add("LIMIT her window isn't see-through here (" + pet.ActualTransparencyLevel + "); on Linux that needs a compositor");
                checks.Add("INFO window list: " + pet.Desk.Name);
                return true;
            }
            if (step == 1)
            {
                if (pet.Falling || pet.Arriving) return false;
                if (!Sitting()) throw new Exception("She didn't sit down after falling in.");
                checks.Add("PASS falling: she drops in from the top of the screen and sits on " + (pet.Seat.Floor ? "the bottom of the work area" : "the top of a window below her"));
                Snap(pet.Content as Control, "pet.png");
                pet.Grab();
                if (pet.Seat != null || pet.Falling || !pet.Dragging) throw new Exception("Grabbing her didn't lift her off her seat.");
                PixelRect area = Work();
                pet.Move(pet.Seated(area.X + area.Width / 2, area.Bottom - Px(100)));
                if (!pet.Drop()) throw new Exception("Dropping her just above the floor didn't make her fall.");
                return true;
            }
            if (step == 2)
            {
                if (pet.Falling) return false;
                if (!Sitting()) throw new Exception("A dropped Capri didn't sit down again.");
                checks.Add("PASS drag and drop: grabbing her lifts her off, dropping her near a ledge makes her fall and sit on it");
                PixelPoint hip = pet.Hip;
                pet.Resize(320);
                if (pet.Hip != hip) throw new Exception("Resizing moved her off her seat: " + hip + " to " + pet.Hip + ".");
                pet.Resize(240);
                if (pet.Hip != hip) throw new Exception("Resizing back moved her off her seat.");
                checks.Add("PASS size: changing her size keeps her sitting on the same spot");
                Point? petSpot = pet.BodySpot();
                if (petSpot == null) throw new Exception("Couldn't find her body to pet.");
                PixelPoint petLeft = pet.PointToScreen(new Point(petSpot.Value.X - 12, petSpot.Value.Y));
                PixelPoint petRight = pet.PointToScreen(new Point(petSpot.Value.X + 12, petSpot.Value.Y));
                int petsBefore = pet.Pets;
                for (int i = 0; i < 6; i++) pet.Stroke(petLeft);
                pet.Stroke(petRight);
                pet.Stroke(petLeft);
                bool earlyPet = pet.Pets != petsBefore;
                for (int i = 0; i < 4; i++) pet.Stroke(i % 2 == 0 ? petRight : petLeft);
                bool smiled = pet.Pets == petsBefore + 1 && pet.Smiling && pet.Talk.Quiet;
                pet.PetLine(pet.Pets);
                bool spoke = Array.IndexOf(Pet.PetLines, pet.Talk.Bubble.Sheet.Reply) >= 0;
                for (int i = 0; i < 8; i++) pet.Stroke(i % 2 == 0 ? petRight : petLeft);
                bool cooled = pet.Pets == petsBefore + 1;
                pet.Talk.Dismiss();
                pet.ForgetPets();
                if (earlyPet || !smiled || !spoke || !cooled) throw new Exception("Petting her went wrong: early " + earlyPet + ", smiled " + smiled + ", spoke " + spoke + ", cooled " + cooled);
                checks.Add("PASS petting: rubbing the cursor back and forth over her makes her smile and say a short line, not too easily and not again for 20 s");
                pet.SetWander(3);
                pet.SetLanguage("French");
                pet.SetReplies(1);
                pet.SetRecall(16);
                pet.SetSounds(30);
                pet.Dancing = true;
                if (pet.Saved.Wander != 3 || pet.Saved.Language != "French" || Brain.Language != "French" || Brain.Wordiness != 1 || Brain.Recall != 16 || pet.Saved.Sounds != 30 || Sfx.Level != 30 || pet.Picked != "Wander: Often | Français | Medium | Medium | Quiet | Dance to music | Dance") throw new Exception("Menu settings didn't stick: " + pet.Picked);
                pet.SetDanceToMusic(false);
                if (!pet.Saved.NoDance || pet.Picked.Contains("Dance to music")) throw new Exception("Dance to music didn't turn off.");
                pet.SetDanceToMusic(true);
                pet.SetWander(0);
                pet.SetLanguage("English");
                pet.SetReplies(0);
                pet.SetRecall(8);
                pet.SetSounds(60);
                pet.Dancing = false;
                if (Brain.Language != null || pet.Saved.Language != null || pet.Picked != "Stay put | English | Short | Short | Normal | Dance to music") throw new Exception("Menu settings didn't go back: " + pet.Picked);
                bool checkInsNew = pet.CheckIns && pet.CheckInsShown && pet.PopPace == 1 && pet.PopsPicked == "Sometimes";
                pet.SetPopPace(2);
                bool popsOften = pet.Saved.PopPace == 2 && pet.PopsPicked == "Often";
                double popSoonest = double.MaxValue, popLatest = 0;
                for (int i = 0; i < 200; i++)
                {
                    double gap = pet.PopGap();
                    popSoonest = Math.Min(popSoonest, gap);
                    popLatest = Math.Max(popLatest, gap);
                }
                pet.SetPopPace(0);
                double rareGap = pet.PopGap();
                bool popsRarely = pet.Saved.PopPace == 0 && pet.PopsPicked == "Rarely" && rareGap >= 1200 && rareGap <= 2400;
                pet.SetCheckIns(false);
                bool checkInsOff = !pet.CheckIns && pet.Saved.NoCheckIns && !pet.CheckInsShown;
                pet.SetCheckIns(true);
                pet.SetPopPace(1);
                if (!checkInsNew || !popsOften || popSoonest < 180 || popLatest > 480 || !popsRarely || !checkInsOff || !pet.CheckIns || pet.PopsPicked != "Sometimes") throw new Exception("Check in on me settings went wrong: new " + checkInsNew + ", often " + popsOften + " " + popSoonest.ToString("0") + "-" + popLatest.ToString("0") + " s, rarely " + popsRarely + ", off " + checkInsOff);
                checks.Add("PASS Check in on me settings: on by default at Pop-ins Sometimes, Often waits 3 to 8 minutes and Rarely 20 to 40, and turning it off sticks");
                pet.Talk.Dismiss();
                bool freeBefore = pet.Free;
                pet.ToggleShush();
                string hushed = pet.Talk.Reply;
                bool quieted = pet.Shushed && pet.QuietShown && !pet.Free && hushed == "Going quiet. Right-click me > Quiet when you want me back.";
                pet.ToggleShush();
                bool unhushed = !pet.Shushed && !pet.QuietShown && pet.Talk.Reply == "I'm back, choom.";
                pet.Talk.Dismiss();
                if (!quieted || !unhushed || pet.Free != freeBefore) throw new Exception("Quiet went wrong: quiet " + quieted + " (" + hushed + "), back " + unhushed + ", free before " + freeBefore + " and after " + pet.Free);
                checks.Add("PASS quiet: right-click > Quiet stops her pop-ins, check-ins, cheers, jokes and glances until it's turned off, and she says so both times");
                pet.SetBubbleTime(2);
                bool bubbleLong = pet.BubblePicked == "Long" && Talk.Hold(100, pet.BubbleTime) == 18 && Talk.Hold(1000, pet.BubbleTime) == 28;
                pet.SetBubbleTime(0);
                bool bubbleQuick = pet.BubblePicked == "Quick" && Talk.Hold(100, pet.BubbleTime) == 4.5;
                pet.SetBubbleTime(1);
                if (!bubbleLong || !bubbleQuick || pet.BubblePicked != "Normal" || Talk.Hold(100, pet.BubbleTime) != 9) throw new Exception("Bubble went wrong: long " + bubbleLong + ", quick " + bubbleQuick + ", picked " + pet.BubblePicked);
                checks.Add("PASS bubble: Settings > Bubble keeps her replies up half as long on Quick and twice as long on Long, like Windows");
                int loudnessWas = Voice.Loudness;
                pet.SetVolume(50);
                short[] volumeHalved = Voice.Louder(new short[] { 1000, -2000 });
                bool volumeHalf = pet.VolumePicked == "50%" && volumeHalved[0] == 500 && volumeHalved[1] == -1000;
                pet.SetVolume(150);
                short[] volumeRaised = Voice.Louder(new short[] { 1000, 30000 });
                bool volumeLoud = pet.VolumePicked == "150%" && volumeRaised[0] == 1500 && volumeRaised[1] == short.MaxValue;
                pet.SetVolume(0);
                bool volumeMuted = pet.VolumePicked == "Mute" && Voice.Louder(new short[] { 1000 })[0] == 0;
                pet.SetVolume(loudnessWas);
                if (!volumeHalf || !volumeLoud || !volumeMuted) throw new Exception("Volume went wrong: 50% " + volumeHalf + ", 150% " + volumeLoud + ", mute " + volumeMuted);
                checks.Add("PASS volume: Settings > Volume sets her voice from Mute to 150%, like Windows");
                short[] tonePcm = new short[12000];
                for (int i = 0; i < tonePcm.Length; i++) tonePcm[i] = (short)(Math.Sin(2 * Math.PI * 220 * i / 24000.0) * 8000);
                byte[] toneWav = Voice.Wave(tonePcm, 24000);
                bool stylePlainBefore = pet.StyleShown == "Normal | Normal | Off | Off" && VoiceFx.Styled(toneWav, "Hey choom.") == toneWav;
                pet.SetPitch(3);
                pet.SetPace(120);
                pet.SetRobot(30);
                pet.SetGlitch(40);
                string styleSet = pet.StyleShown;
                byte[] toneStyled = VoiceFx.Styled(toneWav, "Hey choom.");
                bool styleChanged = styleSet == "+3 | 1.2x | 30% | 40%" && VoiceFx.Pitch == 3 && VoiceFx.Speed == 120 && VoiceFx.Robot == 30 && VoiceFx.Glitch == 40 && toneStyled != toneWav && Voice.Seconds(toneStyled) > 0.2;
                pet.SetPitch(0);
                pet.SetPace(100);
                pet.SetRobot(0);
                pet.SetGlitch(0);
                bool stylePlain = pet.StyleShown == "Normal | Normal | Off | Off" && VoiceFx.Styled(toneWav, "Hey choom.") == toneWav;
                if (!stylePlainBefore || !styleChanged || !stylePlain) throw new Exception("Voice style went wrong: plain before " + stylePlainBefore + ", set " + styleSet + " changed " + styleChanged + ", plain after " + stylePlain);
                checks.Add("PASS voice style: Settings > Voice style sets her pitch, speed, robot and glitch like Windows, her ElevenLabs replies get them, and at Normal and Off her voice is untouched");
                bool loginNew = !pet.StartsAtLogin;
                pet.SetLogin(true);
                string loginText = File.Exists(pet.LoginFile) ? File.ReadAllText(pet.LoginFile) : "";
                bool loginOn = pet.StartsAtLogin && pet.LoginShown && loginText == Limbs.LoginEntry(Environment.ProcessPath, OperatingSystem.IsMacOS());
                File.SetLastWriteTimeUtc(pet.LoginFile, new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                pet.SetLogin(true);
                bool loginKept = File.GetLastWriteTimeUtc(pet.LoginFile).Year == 2001;
                pet.SetLogin(false);
                bool loginOff = !File.Exists(pet.LoginFile) && !pet.LoginShown;
                string linuxLogin = Limbs.LoginEntry("/home/me/My Apps/Capri", false), macLogin = Limbs.LoginEntry("/Applications/Capri.app/Contents/MacOS/Capri", true), bareLogin = Limbs.LoginEntry("/Users/me/capri/Capri", true);
                bool macRight = macLogin.Contains("\t\t<string>/usr/bin/open</string>\n\t\t<string>-g</string>\n\t\t<string>-b</string>\n\t\t<string>" + Limbs.MacBundle + "</string>\n") && !macLogin.Contains("/Applications/") && macLogin.Contains("<key>RunAtLoad</key>") && macLogin.Contains("<string>Interactive</string>") && macLogin.Contains("<key>AssociatedBundleIdentifiers</key>\n\t<array>\n\t\t<string>" + Limbs.MacBundle + "</string>") && bareLogin.Contains("\t\t<string>/Users/me/capri/Capri</string>\n") && !bareLogin.Contains("AssociatedBundleIdentifiers");
                if (!loginNew || !loginOn || !loginKept || !loginOff || !linuxLogin.Contains("\nExec=\"/home/me/My Apps/Capri\"\n") || !macRight) throw new Exception("Start at login went wrong: new " + loginNew + ", on " + loginOn + ", kept " + loginKept + ", off " + loginOff + ", mac " + macRight + " | " + loginText);
                checks.Add("PASS start at login: off until ticked, then an autostart entry on Linux that starts this copy, or a LaunchAgent on Mac that opens her app by its id wherever it's kept; left alone when unchanged, gone again when unticked");
                bool introNew = pet.IntroShown && !pet.Saved.NoIntro;
                pet.SetIntro(false);
                PixelPoint introHip = pet.Hip;
                int introArrivals = pet.Arrivals;
                pet.Reappear();
                bool introOff = pet.Saved.NoIntro && !pet.IntroShown && pet.Arrivals == introArrivals && !pet.Falling && Sitting() && pet.Hip == introHip;
                pet.SetIntro(true);
                if (!introNew || !introOff || pet.Saved.NoIntro || !pet.IntroShown) throw new Exception("Glitch in at startup went wrong: new " + introNew + ", off " + introOff + " (" + introHip + " to " + pet.Hip + ")");
                checks.Add("PASS glitch in at startup: ticked by default; unticked, she starts back on the spot she sat on, with no glitch or sound, like Windows");
                bool onTopNew = pet.OnTop && pet.OnTopShown && pet.Topmost && pet.Talk.Bubble.Topmost && !pet.Raised;
                pet.SetOnTop(false);
                bool onTopOff = !pet.OnTop && !pet.OnTopShown && pet.Saved.NotOnTop && !pet.Topmost && !pet.Talk.Bubble.Topmost && !pet.Talk.Typer.Topmost && !pet.Talk.Pill.Topmost;
                pet.Raise(pet.Now + 6);
                pet.CheckRaised(pet.Now);
                bool raisedUp = pet.Raised && pet.Topmost && pet.Talk.Bubble.Topmost;
                pet.Tap();
                pet.CheckRaised(pet.Now);
                bool loweredByTap = !pet.Raised && !pet.Topmost && !pet.Talk.Bubble.Topmost;
                pet.SetOnTop(true);
                pet.Raise(pet.Now + 6);
                bool onTopBack = pet.OnTop && pet.OnTopShown && pet.Topmost && pet.Talk.Pill.Topmost && !pet.Raised && !pet.Saved.NotOnTop;
                if (!onTopNew || !onTopOff || !raisedUp || !loweredByTap || !onTopBack) throw new Exception("Always on top went wrong: new " + onTopNew + ", off " + onTopOff + ", raised " + raisedUp + ", lowered " + loweredByTap + ", back " + onTopBack);
                checks.Add("PASS always on top: on by default; off, she and her bubble, typing box and pill are ordinary windows, and a pop-in brings her to the front until a click, like Windows");
                string homeFirst = pet.Cheer(true, false, 100, true), homeStill = pet.Cheer(true, false, 115, true);
                pet.Cheer(false, false, 130, true);
                string homeSoon = pet.Cheer(true, false, 145, true), homeBusy = pet.Cheer(true, true, 160, false);
                pet.Cheer(true, false, 175, true);
                string homeSong = pet.Cheer(true, true, 190, true);
                pet.Cheer(false, false, 200, true);
                string homeLater = pet.Cheer(true, false, 200 + Pet.HomeGap, true);
                pet.ForgetCheers();
                if (Array.IndexOf(Pet.HomeGameLines, homeFirst) < 0 || homeStill != null || homeSoon != null || homeBusy != null || Array.IndexOf(Pet.HomeSongLines, homeSong) < 0 || Array.IndexOf(Pet.HomeGameLines, homeLater) < 0) throw new Exception("Her Night City cheers came at the wrong times: " + homeFirst + " | " + homeStill + " | " + homeSoon + " | " + homeBusy + " | " + homeSong + " | " + homeLater);
                checks.Add("PASS Night City cheer: she cheers once when Cyberpunk 2077 starts or a Night City song comes on, not while she's busy, and not again for two hours");
                string lagEarly = pet.Lagging(0.95, 10, true) ?? pet.Lagging(0.95, 25, true) ?? pet.Lagging(0.95, 40, true), lagLine = pet.Lagging(0.97, 55, true);
                string lagSoon = null, lagBusy = null, lagCooled = null, lagLater = null;
                for (int i = 0; i < 4; i++) lagSoon = lagSoon ?? pet.Lagging(0.99, 70 + i * 15, true);
                for (int i = 0; i < 4; i++) lagBusy = lagBusy ?? pet.Lagging(0.99, 2000 + i * 15, false);
                for (int i = 0; i < 4; i++) lagCooled = lagCooled ?? pet.Lagging(i == 0 ? 0.4 : 0.99, 2100 + i * 15, true);
                for (int i = 0; i < 4; i++) lagLater = lagLater ?? pet.Lagging(0.99, 2200 + i * 15, true);
                pet.ForgetLag();
                long cpuIdle, cpuTotal;
                bool cpuRead = Limbs.CpuTimes(out cpuIdle, out cpuTotal) && cpuIdle >= 0 && cpuIdle <= cpuTotal;
                if (lagEarly != null || Array.IndexOf(Pet.LagLines, lagLine) < 0 || lagSoon != null || lagBusy != null || lagCooled != null || Array.IndexOf(Pet.LagLines, lagLater) < 0 || !cpuRead) throw new Exception("Her lag joke came at the wrong times, or the CPU didn't read: " + lagEarly + " | " + lagLine + " | " + lagSoon + " | " + lagBusy + " | " + lagCooled + " | " + lagLater + " | " + cpuRead + " " + cpuIdle + "/" + cpuTotal);
                checks.Add("PASS lag joke: she jokes once the CPU has been over 90% busy for about a minute while she's free, and not again for half an hour; this computer's CPU time reads");
                Func<string, string[], string> runWas = Music.Run;
                List<string> ran = new List<string>();
                string status = "Playing";
                string apple = "playing\n";
                try
                {
                    Music.Run = delegate(string file, string[] args)
                    {
                        string line = file + " " + string.Join(" ", args);
                        ran.Add(line);
                        if (line.Contains("ListNames")) return "method return\n   array [\n      string \"org.freedesktop.DBus\"\n      string \"org.mpris.MediaPlayer2.chromium.instance4021\"\n      string \"org.mpris.MediaPlayer2.spotify\"\n   ]\n";
                        if (line.Contains("PlaybackStatus")) return "method return\n   variant       string \"" + status + "\"\n";
                        if (file == "pgrep") return line.EndsWith(" Spotify", StringComparison.Ordinal) ? "4242\n" : null;
                        if (file == "osascript") return line.Contains("\"Spotify\"") ? apple : "";
                        return null;
                    };
                    bool linuxOn = Music.Linux();
                    bool macOn = Music.Mac();
                    status = "Paused";
                    apple = "paused\n";
                    bool linuxOff = Music.Linux();
                    bool macOff = Music.Mac();
                    string asked = string.Join(" / ", ran);
                    if (!linuxOn || !macOn || linuxOff || macOff || asked.Contains("chromium") || !asked.Contains("--dest=org.mpris.MediaPlayer2.spotify") || !asked.Contains("pgrep -x Music") || asked.Contains("tell application \"Music\"") || !asked.Contains("tell application \"Spotify\" to get player state")) throw new Exception("Music detection read players wrong: " + asked);
                }
                finally { Music.Run = runWas; }
                checks.Add("PASS music: Linux reads music players over D-Bus (not browsers), Mac asks Spotify and Music only when they're already running");
                List<string> songsAsked = new List<string>();
                try
                {
                    Music.Run = delegate(string file, string[] args)
                    {
                        string line = file + " " + string.Join(" ", args);
                        songsAsked.Add(line);
                        if (line.Contains("ListNames")) return "method return\n   array [\n      string \"org.freedesktop.DBus\"\n      string \"org.mpris.MediaPlayer2.chromium.instance4021\"\n      string \"org.mpris.MediaPlayer2.spotify\"\n   ]\n";
                        if (line.Contains("PlaybackStatus")) return "method return\n   variant       string \"" + (line.Contains("chromium") ? "Playing" : "Paused") + "\"\n";
                        if (line.Contains("Metadata")) return "method return\n   variant       array [\n         dict entry(\n            string \"xesam:artist\"\n            variant                array [\n                  string \"CD PROJEKT RED\"\n               ]\n         )\n         dict entry(\n            string \"xesam:title\"\n            variant                string \"SAMURAI - Never Fade Away (Official Audio)\"\n         )\n      ]\n";
                        if (file == "pgrep") return line.Contains("cyberpunk") || line.EndsWith(" Music", StringComparison.Ordinal) ? "777\n" : null;
                        if (file == "osascript") return line.Contains("\"Music\"") ? "Rosa Walton - I Really Want to Stay at Your House\n" : "";
                        return null;
                    };
                    List<string> linuxSongs = Music.LinuxSongs();
                    List<string> macSongs = Music.MacSongs();
                    bool cyberpunk = Limbs.CyberpunkRunning();
                    string asked = string.Join(" / ", songsAsked);
                    if (linuxSongs.Count != 1 || linuxSongs[0] != "CD PROJEKT RED - SAMURAI - Never Fade Away (Official Audio)" || !Hands.NightCity(linuxSongs[0]) || macSongs.Count != 1 || !Hands.NightCity(macSongs[0]) || !cyberpunk || asked.Contains("tell application \"Spotify\"") || asked.Contains("spotify /org/mpris/MediaPlayer2 org.freedesktop.DBus.Properties.Get string:org.mpris.MediaPlayer2.Player string:Metadata")) throw new Exception("Night City songs or the game were read wrong: " + string.Join(" | ", linuxSongs) + " / " + string.Join(" | ", macSongs) + " / " + asked);
                }
                finally { Music.Run = runWas; }
                checks.Add("PASS Night City songs: Linux reads every playing player's title, browser tabs too, Mac asks Spotify and Music only when they're open, and a running Cyberpunk 2077 is found");
                Action<string, string[]> launchWas = Limbs.Launch;
                int patienceWas = Limbs.Patience;
                int settleWas = Limbs.Settle;
                Func<List<InstalledApp>> catalogWas = Hands.Catalog;
                List<string> did = new List<string>();
                bool noWindowTool = false, pactlWorks = false;
                string tempApps = Path.Combine(folder, "apps");
                try
                {
                    Limbs.Patience = 2;
                    Limbs.Settle = 0;
                    Limbs.Launch = delegate(string file, string[] args) { did.Add("launch " + file + " " + string.Join(" ", args)); };
                    Music.Run = delegate(string file, string[] args)
                    {
                        string line = file + " " + string.Join(" ", args);
                        did.Add(line);
                        if (file == "pgrep") return line.EndsWith(" Spotify", StringComparison.Ordinal) ? "4242\n" : null;
                        if (line.Contains("ListNames")) return "   array [\n      string \"org.mpris.MediaPlayer2.firefox.instance_1_9\"\n      string \"org.mpris.MediaPlayer2.spotify\"\n   ]\n";
                        if (line.Contains("PlaybackStatus")) return "   variant       string \"Playing\"\n";
                        if (line.Contains("Metadata")) return "   variant       array [\n         dict entry(\n            string \"xesam:artist\"\n            variant                array [\n                  string \"Chase Atlantic\"\n               ]\n         )\n         dict entry(\n            string \"xesam:title\"\n            variant                string \"Consume\"\n         )\n      ]\n";
                        if (file == "osascript" && line.Contains("current track")) return "Petite Meller - In Love With Myself\n";
                        if (file == "pactl" && pactlWorks && line.Contains("get-sink-volume")) return "Volume: front-left: 26214 /  40% / -23.88 dB,   front-right: 26214 /  40% / -23.88 dB\n        balance 0.00\n";
                        if (file == "pactl" && pactlWorks && line.Contains("get-sink-mute")) return "Mute: yes\n";
                        if (file == "pactl") return pactlWorks ? "" : null;
                        if (file == "sh" && line.Contains("command -v ")) return line.EndsWith(" spectacle", StringComparison.Ordinal) || line.EndsWith(" plasma-systemmonitor", StringComparison.Ordinal) ? "/usr/bin/found\n" : null;
                        if (line == "amixer sget Master") return "Simple mixer control 'Master',0\n  Front Left: Playback 26214 [40%] [-23.88dB] [on]\n";
                        if (file == "osascript" && line.EndsWith("-e output volume of (get volume settings)", StringComparison.Ordinal)) return "40\n";
                        if (file == "osascript" && line.EndsWith("-e output muted of (get volume settings)", StringComparison.Ordinal)) return "false\n";
                        if (file == "osascript" && line.Contains("background only is false")) return "Finder\tcom.apple.finder\nDiscord\tcom.hnc.Discord\nCapri\tmissing value\n";
                        if (file == "osascript" && line.Contains("exists application process")) return "false\n";
                        if (file == "wmctrl" && line == "wmctrl -lpx") return noWindowTool ? null : "0x01e00003 -1 0      N/A                    N/A Desktop\n0x03c00007  0 99999999 discord.discord  host #general | Discord\n0x04200001  0 99999998 Navigator.firefox  host YouTube — Mozilla Firefox\n";
                        if (file == "wmctrl" && line == "wmctrl -l") return "0x04200001  0 host YouTube — Mozilla Firefox\n";
                        return "";
                    };
                    Directory.CreateDirectory(Path.Combine(tempApps, "Spotify.app"));
                    File.WriteAllLines(Path.Combine(tempApps, "spotify.desktop"), new[] { "[Desktop Entry]", "Type=Application", "Name=Spotify", "Exec=spotify %U" });
                    File.WriteAllLines(Path.Combine(tempApps, "helper.desktop"), new[] { "[Desktop Entry]", "Type=Application", "Name=Helper", "Exec=helper", "NoDisplay=true" });
                    List<InstalledApp> linuxApps = Limbs.LinuxApps(new[] { tempApps });
                    List<InstalledApp> macApps = Limbs.MacApps(new[] { tempApps });
                    if (linuxApps.Count != 1 || linuxApps[0].Name != "Spotify" || linuxApps[0].Id != "exec:spotify" || macApps.Count != 1 || macApps[0].Id != Path.Combine(tempApps, "Spotify.app")) throw new Exception("Installed apps were read wrong.");
                    // Steam games come from Steam's own library too (here the Flatpak install), without its runtimes.
                    string steamHome = Path.Combine(folder, "home");
                    string steamRoot = Path.Combine(steamHome, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam");
                    Directory.CreateDirectory(Path.Combine(steamRoot, "steamapps"));
                    File.WriteAllText(Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"), "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"" + steamRoot + "\"\n\t}\n}\n");
                    File.WriteAllText(Path.Combine(steamRoot, "steamapps", "appmanifest_1145360.acf"), "\"AppState\"\n{\n\t\"appid\"\t\t\"1145360\"\n\t\"name\"\t\t\"Hades\"\n\t\"StateFlags\"\t\t\"4\"\n}\n");
                    File.WriteAllText(Path.Combine(steamRoot, "steamapps", "appmanifest_1628350.acf"), "\"AppState\"\n{\n\t\"appid\"\t\t\"1628350\"\n\t\"name\"\t\t\"Steam Linux Runtime 3.0 (sniper)\"\n\t\"StateFlags\"\t\t\"4\"\n}\n");
                    List<InstalledApp> steamGames = Hands.SteamApps(Limbs.SteamRoot(steamHome, false));
                    if (Limbs.SteamRoot(steamHome, false) != steamRoot || Limbs.SteamRoot(steamHome, true) != null || steamGames.Count != 1 || steamGames[0].Name != "Hades" || steamGames[0].Id != "steam://rungameid/1145360")
                        throw new Exception("Steam's library was read wrong: " + steamGames.Count);
                    Hands.Catalog = delegate { return Hands.WithSteam(Limbs.LinuxApps(new[] { tempApps }), Hands.SteamApps(Limbs.SteamRoot(steamHome, false))); };
                    Hands.Forget();
                    string hades = Hands.Do("open_app", new ToolArguments { Name = "hades" });
                    if (hades != "Opened Hades." || !did.Contains((OperatingSystem.IsMacOS() ? "launch open " : "launch xdg-open ") + "steam://rungameid/1145360")) throw new Exception("A Steam game didn't open: " + hades + " / " + string.Join(" / ", did));
                    Hands.Catalog = delegate { return Limbs.LinuxApps(new[] { tempApps }); };
                    Hands.Forget();
                    string opened = Hands.Do("open_app", new ToolArguments { Name = "spotify" });
                    string searched = Hands.Do("open_website", new ToolArguments { Site = "youtube", Search = "lofi beats" });
                    string skipped = Hands.Do("media", new ToolArguments { Action = "next" });
                    string snipped = Hands.Do("shortcut", new ToolArguments { Name = "screenshot" });
                    string clipped = Hands.Do("shortcut", new ToolArguments { Name = "clip_that" });
                    Limbs.Key(0xB0, true);
                    Limbs.Key(0xAF, true);
                    Limbs.Key(0xAD, false);
                    Limbs.Open("spotify:track:2iyaHQlYrR8Eyr4wOmOT5D", true);
                    Limbs.Open("spotify:track:2iyaHQlYrR8Eyr4wOmOT5D", false);
                    string macSong = Limbs.Song(true);
                    string linuxSong = Limbs.Song(false);
                    string log = string.Join(" / ", did);
                    if (opened != "Opened Spotify." || !log.Contains("launch /bin/sh -c spotify") || searched != "Searched youtube.com for lofi beats." || !log.Contains((OperatingSystem.IsMacOS() ? "launch open " : "launch xdg-open ") + "https://www.youtube.com/results?search_query=lofi%20beats")) throw new Exception("Opening apps or sites went wrong: " + opened + " / " + searched + " / " + log);
                    if (skipped != "Skipped to the next track." || !log.Contains(OperatingSystem.IsMacOS() ? "osascript -e tell application \"Spotify\" to next track" : "--dest=org.mpris.MediaPlayer2.spotify /org/mpris/MediaPlayer2 org.mpris.MediaPlayer2.Player.Next") || !snipped.StartsWith("Opened the screenshot picker.", StringComparison.Ordinal) || !clipped.Contains("only work on Windows")) throw new Exception("Media keys or shortcuts went wrong: " + skipped + " / " + snipped + " / " + clipped + " / " + log);
                    if (!log.Contains("osascript -e tell application \"Spotify\" to next track") || !log.Contains("+ 2)") || !log.Contains("pactl set-sink-mute @DEFAULT_SINK@ toggle / amixer -q sset Master toggle")) throw new Exception("Mac or Linux media keys went wrong: " + log);
                    if (!log.Contains("play track \"spotify:track:2iyaHQlYrR8Eyr4wOmOT5D\"") || !log.Contains("OpenUri string:spotify:track:2iyaHQlYrR8Eyr4wOmOT5D") || log.Contains("launch open spotify:") || log.Contains("launch xdg-open spotify:")) throw new Exception("Spotify tracks didn't play directly: " + log);
                    if (macSong != "Petite Meller - In Love With Myself" || linuxSong != "Chase Atlantic - Consume") throw new Exception("Now playing was read wrong: " + macSong + " / " + linuxSong);
                    // Closing, minimizing and switching apps: System Events on Mac, wmctrl on Linux, and her own window never counts.
                    did.Clear();
                    List<OpenWindow> macWindows = Limbs.Windows(true);
                    OpenWindow macDiscord = macWindows.Find(delegate(OpenWindow shown) { return shown.Program == "Discord"; });
                    bool macSteered = macDiscord != null && Limbs.Steer(macDiscord, "switch_to", true) && Limbs.Steer(macDiscord, "close", true) && Limbs.Steer(macDiscord, "minimize", true);
                    List<OpenWindow> linuxWindows = Limbs.Windows(false);
                    OpenWindow linuxDiscord = linuxWindows.Find(delegate(OpenWindow shown) { return shown.Program == "discord"; });
                    bool linuxSteered = linuxDiscord != null && linuxWindows.Count == 2 && linuxWindows[1].Program == "firefox" && linuxWindows[1].Title == "YouTube — Mozilla Firefox"
                        && Limbs.Steer(linuxDiscord, "close", false) && Limbs.Steer(linuxWindows[1], "minimize", false) && Limbs.Steer(linuxWindows[1], "switch_to", false);
                    string closedDiscord = Hands.Do("app_window", new ToolArguments { Name = "discord", Action = "close" });
                    // Without wmctrl, what's playing just sees no windows, and her window tool says what to install.
                    noWindowTool = true;
                    bool noneListed = Limbs.Windows(false).Count == 0;
                    string noWmctrl = Hands.Unlisted() ?? "";
                    noWindowTool = false;
                    Limbs.Windows(false);
                    bool listedAgain = Hands.Unlisted() == null;
                    string windowLog = string.Join(" / ", did);
                    if (!macSteered || macWindows.Count != 2 || macWindows.Exists(delegate(OpenWindow shown) { return shown.Program == "Capri"; }) || !windowLog.Contains("tell application id \"com.hnc.Discord\" to activate") || !windowLog.Contains("tell application id \"com.hnc.Discord\" to quit")
                        || !windowLog.Contains("set visible of application process \"Discord\" to false") || !linuxSteered || !windowLog.Contains("wmctrl -i -c 0x03c00007") || !windowLog.Contains("xdotool windowminimize 0x04200001") || !windowLog.Contains("wmctrl -i -a 0x04200001")
                        || closedDiscord != "Closed discord." || !noWmctrl.Contains("needs wmctrl") || !noneListed || !listedAgain || Hands.Presses)
                        throw new Exception("Closing and switching apps went wrong: " + closedDiscord + " / " + noWmctrl + " / " + windowLog);
                    // Shortcuts with a tool of their own: the screenshot picker, the task manager and, on Linux, show desktop.
                    did.Clear();
                    string linuxShot = Limbs.Shortcut("screenshot", false), linuxMonitor = Limbs.Shortcut("task_manager", false), linuxDesktop = Limbs.Shortcut("show_desktop", false);
                    string macShot = Limbs.Shortcut("screenshot", true), macMonitor = Limbs.Shortcut("task_manager", true);
                    string shortcutLog = string.Join(" / ", did);
                    if (linuxShot != "Opened the screenshot picker. Drag over what to grab." || linuxMonitor != "Opened the system monitor." || linuxDesktop != "Showed the desktop." || macMonitor != "Opened Activity Monitor."
                        || !macShot.EndsWith("it goes to your clipboard.", StringComparison.Ordinal) || Limbs.Shortcut("show_desktop", true) != null || Limbs.Shortcut("clip_that", false) != null
                        || !shortcutLog.Contains("launch spectacle -r") || !shortcutLog.Contains("launch plasma-systemmonitor") || !shortcutLog.Contains("wmctrl -k on") || !shortcutLog.Contains("launch screencapture -i -c") || !shortcutLog.Contains("launch open -a Activity Monitor") || shortcutLog.Contains("launch flameshot"))
                        throw new Exception("Mac or Linux shortcuts went wrong: " + shortcutLog);
                    // An exact volume and the mute state: osascript on Mac, pactl on Linux, and amixer when pactl is missing.
                    did.Clear();
                    pactlWorks = true;
                    double? pulseLevel = Limbs.Level(false);
                    bool? pulseMuted = Limbs.Muted(false);
                    bool pulseSet = Limbs.SetLevel(0.4, false) && Limbs.Mute(false, false);
                    pactlWorks = false;
                    double? alsaLevel = Limbs.Level(false);
                    bool? alsaMuted = Limbs.Muted(false);
                    double? macLevel = Limbs.Level(true);
                    bool? macMuted = Limbs.Muted(true);
                    bool macSet = Limbs.SetLevel(0.55, true) && Limbs.Mute(true, true);
                    string setLine = Hands.Do("media", new ToolArguments { Action = "volume_set", Level = "40" });
                    string volumeLog = string.Join(" / ", did);
                    if (pulseLevel != 0.4 || pulseMuted != true || !pulseSet || alsaLevel != 0.4 || alsaMuted != false || macLevel != 0.4 || macMuted != false || !macSet || setLine != "Set the volume to 40%."
                        || !volumeLog.Contains("pactl set-sink-volume @DEFAULT_SINK@ 40%") || !volumeLog.Contains("pactl set-sink-mute @DEFAULT_SINK@ 0") || !volumeLog.Contains("osascript -e set volume output volume 55") || !volumeLog.Contains("osascript -e set volume output muted true"))
                        throw new Exception("Setting the volume went wrong on Mac or Linux: " + setLine + " / " + volumeLog);
                    // Her power tool: a lock happens at once, a shutdown waits and cancel stops it. Only the pretend runner sees any of it.
                    did.Clear();
                    string lockedLine = Hands.Do("power", new ToolArguments { Action = "lock" });
                    bool poweredNow = Limbs.Power("lock", 0, false) && Limbs.Power("sleep", 0, true) && Limbs.Power("sign_out", 0, true);
                    bool waiting = Limbs.Power("shut_down", 600, false);
                    bool cancelled = Limbs.Power("cancel", 0, false), nothingLeft = !Limbs.Power("cancel", 0, false);
                    string powerLog = string.Join(" / ", did);
                    if (lockedLine != "Locked your PC." || !poweredNow || !waiting || !cancelled || !nothingLeft || !powerLog.Contains("loginctl lock-session") || !powerLog.Contains("pmset sleepnow") || !powerLog.Contains("«event aevtrlgo»") || powerLog.Contains("poweroff")
                        || Limbs.PowerCommands("sign_out", false)[0][0] != "gnome-session-quit" || Limbs.PowerCommands("restart", true)[0][2].IndexOf("restart", StringComparison.Ordinal) < 0 || Limbs.PowerCommands("dance", false) != null)
                        throw new Exception("Her power tool went wrong on Mac or Linux: " + lockedLine + " / " + powerLog);
                }
                finally
                {
                    Limbs.Power("cancel", 0, false);
                    Music.Run = runWas;
                    Limbs.Launch = launchWas;
                    Limbs.Patience = patienceWas;
                    Limbs.Settle = settleWas;
                    Hands.Catalog = catalogWas;
                    Hands.Forget();
                }
                checks.Add("PASS hands: she opens apps (.app folders, .desktop files) and sites, skips and plays Spotify tracks, reads what's playing, turns or sets the volume and reads the mute state on Mac and Linux, closes, minimizes and switches to apps (System Events on Mac, wmctrl on Linux, which says when it's missing), and shuts down, restarts, sleeps, locks and signs out, with cancel stopping a wait; a screenshot, the task manager and show desktop use their own tools, and key-press shortcuts say they're Windows-only");
                pet.ShowBrains(new[] { "gemma4:e4b", "qwen3:8b" });
                int brains = pet.BrainChoices;
                pet.Brain.Remember("what's up?", "Not much, choom.");
                int chatBeforePick = pet.Brain.History.Count;
                pet.PickBrain("qwen3:8b");
                int chatAfterPick = pet.Brain.History.Count;
                bool picked = pet.Brain.Model == "qwen3:8b" && pet.Saved.Model == "qwen3:8b";
                pet.Brain.Forget();
                pet.ShowBrains(new string[0]);
                string noModels = pet.FirstBrainChoice;
                pet.ShowBrains(null);
                if (brains != 2 || !picked || pet.BrainChoices != 1) throw new Exception("The Brain menu didn't list or pick models.");
                if (noModels != Pet.PullChoice || pet.FirstBrainChoice != Pet.NoOllama) throw new Exception("The Brain menu didn't offer her brain download: " + noModels + " / " + pet.FirstBrainChoice);
                if (chatBeforePick < 2 || chatAfterPick != chatBeforePick) throw new Exception("Picking another model wiped her chat: " + chatBeforePick + " then " + chatAfterPick);
                checks.Add("PASS settings menu: wander, dance, language, reply length, chat memory and the brain picker change her and show which is picked, picking another model keeps the chat, and with Ollama open but empty Brain offers to download gemma4:e4b");
                // A cloud key instead of Ollama: asked for in a hidden box, a key that isn't one is explained, a good one lists its models under Brain, and Forget goes back to Ollama.
                pet.ShowBrains(new[] { "qwen3:8b" });
                string ollamaMenu = pet.BrainMenu;
                pet.GetFreeKey();
                string freeKey = pet.Talk.Reply;
                pet.AskBrainKey();
                bool keyAsked = pet.Talk.Typing && pet.Talk.Typer.Secret && pet.Talk.Reply != null && pet.Talk.Reply.IndexOf("API key", StringComparison.Ordinal) >= 0;
                pet.Talk.Submit("not-a-key");
                string notKey = pet.Talk.Reply;
                bool keyHidden = !pet.Talk.Typer.Secret && !File.Exists(Path.Combine(folder, "voice-key"));
                pet.UseCloud("nvapi-smoke-1234", "meta/llama-3.3-70b-instruct", new List<string> { "meta/llama-3.3-70b-instruct", "qwen/qwen3-235b-a22b" });
                pet.ShowBrains(new[] { "qwen3:8b" });
                string cloudMenu = pet.BrainMenu;
                bool cloudDetails = pet.ErrorDetails().IndexOf(" | brain: NVIDIA meta/llama-3.3-70b-instruct | ", StringComparison.Ordinal) > 0;
                pet.PickCloud("qwen/qwen3-235b-a22b");
                bool cloudPicked = pet.Brain.Remote != null && pet.Brain.Remote.Model == "qwen/qwen3-235b-a22b" && pet.Brain.Model == "qwen/qwen3-235b-a22b" && pet.Saved.CloudModel == "qwen/qwen3-235b-a22b";
                // A stuck NVIDIA model hands over to the next one for that message, and their pick stays saved.
                pet.UseCloud("nvapi-smoke-1234", "nvidia/nemotron-3.5-super-vl-120b-a12b", new List<string> { "nvidia/nemotron-3.5-super-vl-120b-a12b", "nvidia/nemotron-3.5-bolt-e4b" });
                bool hopped = Brain.Stalled(new BrainException(Brain.Down, true)) && !Brain.Stalled(new BrainException("Something else.", true)) && pet.Hop(pet.Brain.Remote) && pet.Brain.Model == "nvidia/nemotron-3.5-bolt-e4b" && pet.Brain.Remote.Model == "nvidia/nemotron-3.5-bolt-e4b" && pet.Saved.CloudModel == "nvidia/nemotron-3.5-super-vl-120b-a12b";
                bool hopsEnd = !pet.Hop(pet.Brain.Remote);
                pet.SetStay(true);
                pet.UseCloud("nvapi-smoke-1234", "nvidia/nemotron-3.5-super-vl-120b-a12b", new List<string> { "nvidia/nemotron-3.5-super-vl-120b-a12b", "nvidia/nemotron-3.5-bolt-e4b", "openai/gpt-oss-20b" });
                pet.ShowBrains(null);
                bool stayed = !pet.Hop(pet.Brain.Remote) && pet.Brain.Model == "nvidia/nemotron-3.5-super-vl-120b-a12b" && pet.BrainMenu.IndexOf(" | *Stay on my model | ", StringComparison.Ordinal) > 0;
                pet.SetStay(false);
                hopsEnd = hopsEnd && stayed;
                File.WriteAllText(Path.Combine(folder, "brain-key"), "nvapi-smoke-1234");
                pet.ForgetBrainKey();
                bool cloudGone = pet.Brain.Remote == null && pet.Saved.CloudModel == null && !File.Exists(Path.Combine(folder, "brain-key"));
                pet.Brain.Model = "qwen3:8b";
                pet.Talk.Dismiss();
                if (ollamaMenu != "*qwen3:8b | - | Get a free NVIDIA key | " + Pet.KeyChoice) throw new Exception("The Brain menu didn't offer a cloud key: " + ollamaMenu);
                if (freeKey != Pet.FreeKeySteps || !keyAsked || notKey != Pet.NotAKey || !keyHidden) throw new Exception("The key choices didn't say where to paste, ask in a hidden box or explain a bad key: " + freeKey + " | " + keyAsked + " | " + notKey + " | " + keyHidden);
                if (cloudMenu != "NVIDIA key ending 1234 (off) | *meta/llama-3.3-70b-instruct | qwen/qwen3-235b-a22b | - | Stay on my model | Forget the key and use Ollama" || !cloudPicked || !cloudGone || !cloudDetails || !hopped || !hopsEnd) throw new Exception("The cloud key's Brain menu went wrong: " + cloudMenu + " | " + cloudPicked + " | " + cloudGone + " | " + cloudDetails + " | " + hopped + " | " + hopsEnd);
                checks.Add("PASS cloud brain: Brain offers a free NVIDIA key or any API key in a hidden box, explains one that isn't a key, lists a key's models to pick from, and Forget goes back to Ollama");
                // Updates: a newer finished release puts Get Capri X in her menu and she mentions it once; older releases, drafts and odd tags don't.
                pet.Saved.UpdateSeen = null;
                pet.ShowUpdate("{\"tag_name\":\"v99.1.0\",\"draft\":false,\"prerelease\":false,\"html_url\":\"https://github.com/lachydotmcg/capri-desktop/releases/tag/v99.1.0\",\"assets\":[{\"name\":\"Capri-linux-x64.tar.gz\"}]}");
                string updateChoice = pet.UpdateChoice;
                string updateNews = pet.UpdateNews;
                string updatePage = pet.UpdatePage;
                pet.UpdateNews = null;
                pet.ShowUpdate("{\"tag_name\":\"v99.1.0\",\"draft\":false,\"prerelease\":false}");
                string updateAgain = pet.UpdateNews;
                string updateKept = pet.UpdateChoice;
                pet.ShowUpdate("{\"tag_name\":\"v99.2.0\",\"draft\":true}");
                string draftChoice = pet.UpdateChoice;
                pet.ShowUpdate("{\"tag_name\":\"v99.3.0\",\"html_url\":\"https://example.com/capri\"}");
                string oddPage = pet.UpdatePage;
                pet.ShowUpdate("{\"tag_name\":\"v0.0.1\"}");
                string olderChoice = pet.UpdateChoice;
                pet.ShowUpdate("not json");
                bool oddTags = Pet.Newer("nightly", new Version(0, 7, 5)) == null && Pet.Newer("v0.7.5", new Version(0, 7, 5)) == null && Pet.Newer("0.7.10", new Version(0, 7, 9)) == "0.7.10" && Pet.Newer("v1.0", new Version(0, 7, 5)) == "1.0.0" && Pet.Newer("v0.7.6-beta", new Version(0, 7, 5)) == null;
                pet.UpdateNews = null;
                pet.Saved.UpdateSeen = null;
                pet.Save();
                if (updateChoice != "Get Capri 99.1.0" || updateNews != "Capri 99.1.0 is out. Right-click me > Get Capri 99.1.0." || updatePage != "https://github.com/lachydotmcg/capri-desktop/releases/tag/v99.1.0") throw new Exception("A newer release wasn't offered: " + updateChoice + " | " + updateNews + " | " + updatePage);
                if (updateAgain != null || updateKept != "Get Capri 99.1.0" || draftChoice != null || oddPage != "https://github.com/lachydotmcg/capri-desktop/releases/latest" || olderChoice != null || pet.UpdateChoice != null || !oddTags) throw new Exception("The update notice went wrong: " + updateAgain + " | " + updateKept + " | " + draftChoice + " | " + oddPage + " | " + olderChoice + " | " + pet.UpdateChoice + " | " + oddTags);
                checks.Add("PASS updates: a newer GitHub release puts Get Capri X in her right-click menu and she says so once; older releases, drafts, odd tags and other sites don't");
                // Check my screen: the right screenshot tool for the desktop, shrunk to a JPEG, and a plain answer when no tool works.
                bool toolsRight = Eyes.Tools(true, null, null)[0][0] == "screencapture" && Eyes.Tools(false, "x11", "XFCE")[0][0] == "import" && Eyes.Tools(false, "wayland", "KDE")[0][0] == "spectacle"
                    && Eyes.Tools(false, "wayland", "ubuntu:GNOME")[0][0] == "gnome-screenshot" && Eyes.Tools(false, "wayland", "Hyprland")[0][0] == "grim" && Eyes.Tools(false, "wayland", "Hyprland").Count == 3;
                byte[] wideShot;
                using (SkiaSharp.SKBitmap wide = new SkiaSharp.SKBitmap(2000, 1000))
                {
                    wide.Erase(new SkiaSharp.SKColor(30, 200, 120));
                    using (SkiaSharp.SKImage image = SkiaSharp.SKImage.FromBitmap(wide))
                    using (SkiaSharp.SKData png = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100)) wideShot = png.ToArray();
                }
                byte[] smallShot = Eyes.Jpeg(wideShot, 1280);
                bool shrunk = smallShot != null && smallShot.Length > 3 && smallShot[0] == 0xFF && smallShot[1] == 0xD8;
                if (shrunk) using (SkiaSharp.SKBitmap back = SkiaSharp.SKBitmap.Decode(smallShot)) shrunk = back != null && back.Width == 1280 && back.Height == 640;
                Func<byte[]> shootWas = Eyes.Shoot;
                Eyes.Shoot = delegate { return null; };
                string noShot = Hands.Do("look_at_screen", new ToolArguments());
                Eyes.Shoot = shootWas;
                if (!toolsRight || !shrunk || noShot != Eyes.Missing || Eyes.Jpeg(new byte[] { 1, 2, 3 }, 1280) != null) throw new Exception("Check my screen went wrong: " + toolsRight + " | " + shrunk + " | " + noShot);
                checks.Add("PASS check my screen: the right screenshot tool for Mac, X11, KDE, GNOME or wlroots desktops, shrunk to a 1280-wide JPEG for her brain, and a plain answer when no tool works");
                // Her Discord status: the switch, plain states, and Discord's socket found by name in its temp folder.
                string ipcFolder = Path.Combine(folder, "ipc");
                Directory.CreateDirectory(ipcFolder);
                File.WriteAllText(Path.Combine(ipcFolder, "discord-ipc-3"), "");
                string tmpWas = Environment.GetEnvironmentVariable("TMPDIR");
                List<string> ipcNames;
                Environment.SetEnvironmentVariable("TMPDIR", ipcFolder);
                try { ipcNames = Presence.Names(); }
                finally { Environment.SetEnvironmentVariable("TMPDIR", tmpWas); }
                pet.SetDiscord(false);
                bool discordOff = pet.Saved.NoDiscord && !pet.DiscordShown;
                pet.SetDiscord(true);
                bool discordOn = !pet.Saved.NoDiscord && pet.DiscordShown;
                if (!ipcNames.Contains(Path.Combine(ipcFolder, "discord-ipc-3")) || !discordOff || !discordOn || Array.IndexOf(new[] { "Hanging out", "Chatting", "Dancing to music", "Taking a break" }, pet.Doing) < 0)
                    throw new Exception("Her Discord status went wrong: " + string.Join(", ", ipcNames) + " / off " + discordOff + " / on " + discordOn + " / " + pet.Doing);
                checks.Add("PASS Discord status: Settings > Show on Discord turns it off and on, she says what she's doing in plain words, and she finds Discord's socket in its temp folder");
                string details = pet.ErrorDetails();
                if (!details.StartsWith("Capri v", StringComparison.Ordinal) || details.IndexOf(" | brain: ", StringComparison.Ordinal) < 0 || details.IndexOf(" | voice: ", StringComparison.Ordinal) < 0)
                    throw new Exception("Her error details were wrong: " + details);
                checks.Add("PASS Help > Copy error details: her version, the system, her brain and voice, then her error log");
                pet.AskNickname();
                bool askedName = pet.Talk.Typing && pet.Talk.Reply != null && pet.Talk.Reply.StartsWith("What should I call you?", StringComparison.Ordinal);
                pet.Talk.Submit("  Lachy!!  ");
                bool namedRight = !pet.Talk.Typing && Brain.Nickname == "Lachy" && pet.Saved.Nickname == "Lachy" && pet.Talk.Reply == "Got it, Lachy." && Brain.Character.IndexOf("call them: Lachy.", StringComparison.Ordinal) >= 0;
                pet.SetNickname("shithead");
                bool rudeName = Brain.Nickname == "shithead" && pet.Saved.Nickname == "shithead" && pet.Talk.Reply == "Got it, shithead.";
                pet.SetNickname("choom");
                bool clearedName = Brain.Nickname == null && pet.Saved.Nickname == null && pet.Talk.Reply == "Back to choom, then." && Brain.Character.IndexOf("set a nickname", StringComparison.Ordinal) < 0;
                if (!askedName || !namedRight || !rudeName || !clearedName) throw new Exception("The nickname went wrong: asked " + askedName + ", named " + namedRight + ", rude " + rudeName + ", cleared " + clearedName);
                checks.Add("PASS nickname: Settings > What should she call you? asks in her typing box, saves a cleaned-up name she says back, rude ones too since it's their call, and choom goes back to choom");
                pet.Hear("remember I love Hollow Knight");
                string saved = pet.Talk.Reply;
                string kept = Brain.Notes();
                pet.Hear("forget about hollow knight");
                string forgot = pet.Talk.Reply;
                string notesPath = pet.OpenNotes();
                if (saved != "Saved to my notes: They love Hollow Knight." || kept != "- They love Hollow Knight" || forgot != "Forgot my note about hollow knight." || Brain.Notes() != "" || notesPath == null || !File.ReadAllText(notesPath).StartsWith("# What Capri remembers", StringComparison.Ordinal)) throw new Exception("Her notes didn't work: " + saved + " / " + kept + " / " + forgot);
                checks.Add("PASS notes: remember and forget save to me.md here too, and Open her notes makes the file");
                string today = Path.Combine(Brain.TranscriptPath, DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + ".md");
                Brain.Transcribe("You", "we finally beat the smoke boss");
                bool transcribed = File.Exists(today) && File.ReadAllText(today).Contains("You:** we finally beat the smoke boss");
                bool rememberedOn = pet.Remembers && Brain.Keeping && !pet.Saved.NoMemories;
                pet.SetRemembering(false);
                Brain.Transcribe("You", "this line stays out");
                bool stoppedWriting = !File.ReadAllText(today).Contains("this line stays out") && !pet.Remembers && pet.Saved.NoMemories && !Brain.Keeping;
                pet.SetRemembering(true);
                string memoriesPath = pet.OpenMemories();
                string recalledBoss = Brain.Recalled("remember when we beat the smoke boss?");
                if (!transcribed || !rememberedOn || !stoppedWriting || !pet.Remembers || !Brain.Keeping || memoriesPath != Path.Combine(folder, "memories.md") || !File.Exists(memoriesPath) || !Directory.Exists(Brain.TranscriptPath) || recalledBoss.IndexOf("You: we finally beat the smoke boss", StringComparison.Ordinal) < 0)
                    throw new Exception("Remember our chats went wrong: " + transcribed + " | " + rememberedOn + " | " + stoppedWriting + " | " + recalledBoss);
                checks.Add("PASS remember our chats: on by default here too, a chat line goes in today's transcript, \"remember when\" finds it, off writes nothing, and Open her memories makes memories.md");
                bool personalityMade = Brain.PersonalityPath == Path.Combine(folder, "personality.md") && File.ReadAllText(Brain.PersonalityPath) == Brain.PersonalityFile && Brain.Persona == Brain.DefaultPersonality + "\n" + Brain.Machinery;
                File.Delete(Brain.PersonalityPath);
                string personalityPath = pet.OpenPersonality();
                bool personalityOpened = personalityPath == Brain.PersonalityPath && File.ReadAllText(personalityPath) == Brain.PersonalityFile && Brain.PersonalityNote == Brain.HerDefault;
                File.WriteAllText(personalityPath, "<!-- mine -->\nYou are Capri, but grumpy.\n");
                bool personalityEdited = Brain.Persona == "You are Capri, but grumpy.\n" + Brain.Machinery && Brain.Character.StartsWith("You are Capri, but grumpy.\n", StringComparison.Ordinal) && Brain.PersonalityNote == Brain.YourEdit;
                pet.ResetPersonality();
                bool personalityReset = pet.Talk.Reply == Brain.ResetKept && Brain.Personality == Brain.DefaultPersonality && File.ReadAllText(Brain.PersonalityBackup) == "<!-- mine -->\nYou are Capri, but grumpy.\n";
                File.WriteAllText(personalityPath, "");
                bool personalityEmpty = Brain.Personality == Brain.DefaultPersonality && Brain.PersonalityNote == Brain.BlankFile;
                File.WriteAllText(personalityPath, Brain.PersonalityFile);
                File.Delete(Brain.PersonalityBackup);
                if (!personalityMade || !personalityOpened || !personalityEdited || !personalityReset || !personalityEmpty) throw new Exception("personality.md went wrong: made " + personalityMade + ", opened " + personalityOpened + ", edited " + personalityEdited + ", reset " + personalityReset + ", empty " + personalityEmpty);
                checks.Add("PASS personality.md: it starts as her default next to me.md, an edit reaches her prompt on her next reply, an empty one falls back to her default, and Reset puts her default back and keeps theirs as personality.backup.md");
                List<short[]> typedHeard = new List<short[]>();
                Sfx.TypeOut = delegate(short[] typed) { typedHeard.Add(typed); };
                Sfx.Untyped = delegate { };
                pet.Hear("remember I love Silksong");
                int typedOn = typedHeard.Count;
                double typedSeconds = typedOn == 1 ? (double)typedHeard[0].Length / Sfx.Rate : 0;
                double typedWanted = pet.Talk.Reply.Length / Talk.LettersPerSecond + 0.05;
                pet.SetTyping(false);
                bool typingOff = !pet.TypingSound && !Sfx.Typing;
                pet.Hear("forget about silksong");
                int typedOff = typedHeard.Count;
                pet.SetTyping(true);
                pet.AutoTyping();
                Sfx.TypeOut = null;
                Sfx.Untyped = null;
                if (typedOn != 1 || Math.Abs(typedSeconds - typedWanted) > 0.02 || !typingOff || typedOff != 1 || typedHeard.Count != 2 || !pet.TypingSound || Brain.Notes() != "" || !Sfx.Typing) throw new Exception("Typing sound went wrong: " + typedOn + " | " + typedSeconds + " | " + typingOff + " | " + typedOff + " | " + typedHeard.Count);
                checks.Add("PASS typing sound: a text-only reply plays soft typing ticks, Settings > Sounds > Typing sound turns them off");
                pet.Talk.Dismiss();
                pet.Think = null;
                pet.Brain.Model = null;
                pet.Hear("hello?");
                if (pet.Talk.Reply != Pet.Offline) throw new Exception("No brain didn't explain how to get one: " + pet.Talk.Reply);
                checks.Add("PASS no brain: she says to install Ollama and pull a model");
                pet.Talk.Dismiss();
                pet.Think = delegate(string words) { return words == "hey" ? "Just vibing, choom." : "wrong words"; };
                pet.Talk.Pill.Press(1);
                if (!pet.Talk.Typing) throw new Exception("The pencil didn't open the typing box.");
                pet.Talk.Submit("hey");
                return true;
            }
            if (step == 3)
            {
                if (!pet.Talk.Done) return false;
                if (settled < 0) settled = now;
                if (now - settled < 0.3) return false;
                if (pet.Talk.Shown != "Just vibing, choom.") throw new Exception("Her reply showed wrong: " + pet.Talk.Shown);
                Bubble bubble = pet.Talk.Bubble;
                if (bubble.Position.Y + (int)Math.Round(bubble.Height * bubble.DesktopScaling) > pet.Head.Y + 2) throw new Exception("Her bubble covered her head.");
                if (!pet.Talk.Pill.IsVisible) throw new Exception("The pencil and mic pill didn't show under her while she talked.");
                Snap(pet.Talk.Face, "talk.png");
                Snap(pet.Talk.Typer.Content as Control, "typer.png");
                Snap(pet.Talk.Pill.Content as Control, "pill.png");
                Scene("desktop.png", new Window[] { pet, bubble, pet.Talk.Pill }, new PixelRect[0]);
                checks.Add("PASS chat: the typing box sends a line to her brain, the reply types out in a bubble above her head");
                // Chinese takes two columns a character and wraps without spaces, a long link breaks across lines, and every
                // letter still shows once she's done typing.
                string hanziReply = "你好，朋友！今天过得怎么样？";
                string wrapText = Sheet.Speaker + hanziReply + hanziReply + hanziReply + hanziReply + " https://example.com/a/very/long/link/that/needs/more/than/one/line ok";
                List<string> wrapped = Look.Wrap(wrapText, Sheet.Columns);
                int[] wrapTakes = Look.Takes(wrapped, wrapText, wrapText.Length);
                int wrapShown = 0;
                bool wrapFits = true;
                for (int wrapAt = 0; wrapAt < wrapped.Count; wrapAt++)
                {
                    wrapShown += wrapTakes[wrapAt];
                    if (Look.Cells(wrapped[wrapAt]) > Sheet.Columns || (wrapAt > 0 && wrapped[wrapAt].Length > 0 && "，！？".IndexOf(wrapped[wrapAt][0]) >= 0)) wrapFits = false;
                }
                if (!wrapFits || wrapShown != string.Join("", wrapped).Length || string.Join("", wrapped).Replace(" ", "") != wrapText.Replace(" ", "") || wrapped.Count < 5
                    || string.Join("|", Look.Wrap("CAPRI: hey choom, long day?", 12)) != "CAPRI: hey|choom, long|day?" || !Look.RightToLeft("مرحبا 2077") || !Look.RightToLeft("2077 مرحبا") || Look.RightToLeft("Hey مرحبا"))
                    throw new Exception("Her bubble wrapped wrong: " + string.Join(" | ", wrapped));
                string thaiText = "CAPRI: เฮ้ เพื่อน วันนี้เหนื่อยไหม อยากให้ฉันเปิดเพลงเบาๆ ให้ฟังระหว่างทำงานหรือเปล่า บอกมาได้เลยนะ";
                List<string> thaiLines = Look.Wrap(thaiText, 16);
                bool thaiFits = thaiLines.TrueForAll(delegate(string thaiRow) { return Look.Cells(thaiRow) <= 16 && thaiRow.Length > 0 && Look.Cells(thaiRow[0]) > 0 && "ะาำๅๆ".IndexOf(thaiRow[0]) < 0; });
                bool[] thaiLetters = Look.Letters("เปิดเพลง");
                if (!thaiFits || string.Join("", thaiLines).Replace(" ", "") != thaiText.Replace(" ", "") || Look.Cells("วันนี้อากาศดีมาก") != 12 || !Look.Thai("hey วันนี้") || Look.Thai("hey choom")
                    || thaiLetters[1] || thaiLetters[2] || !thaiLetters[3] || !thaiLetters[4] || thaiLetters[5])
                    throw new Exception("Thai wrapped wrong: " + string.Join(" | ", thaiLines));
                bool[] laoLetters = Look.Letters("ມື້ນີ້ເຈົ້າເຮັດຫຍັງຢູ່"), burmeseLetters = Look.Letters("မင်္ဂလာပါ");
                string joinedCut = "";
                for (int n = 1; n <= 12; n++)
                    foreach (string joined in new[] { "ថ្ងៃនេះ", "क्षमा करो", "မင်္ဂလာပါ" })
                    {
                        string start = Look.Cut(joined, n);
                        if (start.EndsWith("្") || start.EndsWith("्") || start.EndsWith("္") || start == "မ") joinedCut += start + " (" + n + ") ";
                    }
                if (!laoLetters[3] || !laoLetters[6] || laoLetters[7] || laoLetters[10] || !laoLetters[11] || laoLetters[12] || joinedCut.Length > 0
                    || burmeseLetters[1] || burmeseLetters[4] || !burmeseLetters[5] || burmeseLetters[6] || !burmeseLetters[7] || burmeseLetters[8] || !Look.Unspaced("မင်္ဂလာပါ"))
                    throw new Exception("A Lao vowel, a Burmese syllable or a joined letter came apart: " + laoLetters[7] + " " + laoLetters[10] + " " + laoLetters[12] + " " + burmeseLetters[1] + " " + burmeseLetters[4] + " / " + joinedCut);
                checks.Add("PASS bubble lines: Chinese, Japanese and Korean take two columns and wrap without spaces, never starting a line with closing punctuation, a long link breaks across lines without losing letters, Arabic, Urdu, Persian and Hebrew lines read right to left, Thai, Lao and Burmese break between letters, never before a vowel or tone mark, after a vowel written first or inside a Burmese syllable, and a virama or Khmer coeng keeps its letters together");
                pet.Talk.Dismiss();
                PixelRect area = Work();
                shelfAsked = new PixelRect(area.X + area.Width / 2 - Px(350), area.Y + area.Height / 2 - Px(140), Px(700), Px(360));
                shelf = Shelf(shelfAsked);
                return true;
            }
            if (step == 4)
            {
                shelfSpot = Near(shelfAsked, true);
                if (shelfSpot == null) return false;
                PixelPoint letAt = pet.Hip;
                pet.Grab();
                pet.Let(true);
                Ledge here = pet.Seat;
                if (here == null || !here.Free || pet.Hip.Y != here.Y || Math.Abs(pet.Hip.X - letAt.X) > Px(60) || pet.SitChoice != "Stand up") throw new Exception("Shift + drop didn't sit her where she was let go: seat " + (here == null ? "none" : here.Left + "-" + here.Right + " at " + here.Y + (here.Free ? " free" : "")) + ", hip " + pet.Hip + " from " + letAt + ", menu " + pet.SitChoice);
                checks.Add("PASS Shift + drop: she sits right where she's let go, and her menu says Stand up");
                pet.StandUp();
                if (pet.SitChoice != "Sit here") throw new Exception("Her menu still said Stand up after she stood.");
                pet.Move(pet.Seated(shelfSpot.Bounds.X + Px(220), shelfSpot.Bounds.Y - Px(12)));
                if (!pet.Drop()) throw new Exception("She couldn't reach the test shelf 12 px under her.");
                return true;
            }
            if (step == 5)
            {
                if (pet.Falling) return false;
                Ledge seat = pet.Seat;
                if (seat == null || seat.Window != shelfSpot.Window || pet.Hip.Y != shelfSpot.Bounds.Y) throw new Exception("She didn't land on the test shelf's top edge.");
                if (pet.Showing != pet.PerchGlitch) throw new Exception("Sitting on the shelf didn't glitch her on.");
                checks.Add("PASS sitting: dropped over a window, she lands on its top edge and glitches into her seat");
                Scene("shelf.png", new Window[] { pet }, new[] { shelfSpot.Bounds });
                hipBefore = pet.Hip;
                dx = Px(120);
                dy = Px(60);
                Send(shelf, "move " + (shelfAsked.X + dx) + " " + (shelfAsked.Y + dy));
                return true;
            }
            if (step == 6)
            {
                Ledge seat = pet.Seat;
                if (seat == null) throw new Exception("She stood up when the shelf moved.");
                if (seat.Y != shelfSpot.Bounds.Y + dy) return false;
                if (pet.Hip.Y != seat.Y || pet.Hip.X != hipBefore.X + dx) throw new Exception("She didn't move with the shelf: hip " + pet.Hip + ", expected " + new PixelPoint(hipBefore.X + dx, seat.Y) + ".");
                checks.Add("PASS following: when the window moves, she moves with it and stays seated");
                coverAsked = new PixelRect(pet.Hip.X - Px(130), seat.Y - Px(60), Px(260), Px(200));
                cover = Shelf(coverAsked);
                return true;
            }
            if (step == 7)
            {
                if (coverAt < 0)
                {
                    if (Near(coverAsked, false) == null) return false;
                    coverAt = now;
                }
                if (pet.Seat != null) return false;
                double waited = now - coverAt;
                if (waited < 0.45 || waited > 2) throw new Exception("She stood up " + waited.ToString("0.00") + " s after a window covered her seat; it should be about 0.7 s.");
                if (pet.Falling) throw new Exception("Standing up made her fall.");
                checks.Add("PASS covered: a window over her seat makes her stand up after " + waited.ToString("0.0") + " s");
                Send(cover, "quit");
                return true;
            }
            if (step == 8)
            {
                if (!slept)
                {
                    if (!pet.Upright) return false;
                    pet.Talk.Dismiss();
                    pet.SetSleepAfter(0);
                    pet.Doze(100000);
                    bool never = !pet.Asleep && pet.SleepPicked == "Never";
                    pet.SetSleepAfter(120);
                    pet.Doze(119);
                    bool early = pet.Asleep;
                    pet.Doze(130);
                    bool dozed = pet.Asleep && pet.Doing == "Asleep";
                    pet.Doze(140);
                    bool stayed = pet.Asleep;
                    pet.Doze(0.5);
                    bool woke = pet.Waking;
                    if (!never || early || !dozed || !stayed || !woke || pet.SleepPicked != "2 min") throw new Exception("Sleep went wrong: never " + never + ", early " + early + ", dozed " + dozed + ", stayed " + stayed + ", woke " + woke + ", picked " + pet.SleepPicked);
                    checks.Add("PASS sleep: standing, she dozes off after Fall asleep after without the mouse or keyboard (2 min here), shows Asleep on Discord, wakes with her boot glitch when they're back, and Never keeps her up");
                    slept = true;
                }
                if (!cover.HasExited) return false;
                shelfSpot = Near(new PixelRect(shelfAsked.X + dx, shelfAsked.Y + dy, shelfAsked.Width, shelfAsked.Height), true);
                if (shelfSpot == null) return false;
                pet.Move(pet.Seated(shelfSpot.Bounds.X + Px(220), shelfSpot.Bounds.Y - Px(12)));
                if (!pet.Drop()) throw new Exception("She couldn't get back onto the shelf.");
                return true;
            }
            if (step == 9)
            {
                if (pet.Falling) return false;
                if (pet.Seat == null || pet.Seat.Window != shelfSpot.Window) throw new Exception("She didn't sit back on the shelf.");
                Send(shelf, "hide");
                hideAt = now;
                return true;
            }
            if (step == 10)
            {
                if (pet.Seat != null) return false;
                double waited = now - hideAt;
                if (waited > 1.5) throw new Exception("She took " + waited.ToString("0.00") + " s to stand up after her window hid.");
                checks.Add("PASS window gone: when her window hides, she stands up after " + waited.ToString("0.0") + " s");
                Send(shelf, "show");
                return true;
            }
            if (step == 11)
            {
                shelfSpot = Near(new PixelRect(shelfAsked.X + dx, shelfAsked.Y + dy, shelfAsked.Width, shelfAsked.Height), true);
                if (shelfSpot == null) return false;
                pet.StandUp();
                pet.Move(pet.Seated(shelfSpot.Bounds.X + Px(200), shelfSpot.Bounds.Y - Px(12)));
                if (!pet.Drop()) throw new Exception("She couldn't get back onto the shown shelf.");
                return true;
            }
            if (step == 12)
            {
                if (pet.Falling) return false;
                if (pet.Seat == null || pet.Seat.Window != shelfSpot.Window) throw new Exception("She didn't sit on the shelf before walking.");
                hipBefore = pet.Hip;
                if (!pet.Stroll() || !pet.Walking) throw new Exception("She couldn't start walking along the shelf.");
                if (pet.Hip.Y != shelfSpot.Bounds.Y) throw new Exception("Standing up to walk lifted her off the shelf: hip " + pet.Hip + ".");
                return true;
            }
            if (step == 13)
            {
                if (pet.Walking)
                {
                    if (pet.Hip.Y != pet.Seat.Y) throw new Exception("Her feet left the shelf while walking.");
                    return false;
                }
                if (!Sitting() || pet.Seat.Window != shelfSpot.Window || pet.Hip.X == hipBefore.X) throw new Exception("She didn't walk and sit back down on the shelf.");
                checks.Add("PASS walking: she gets up, walks along her window with her feet on it, and sits back down " + Math.Abs(pet.Hip.X - hipBefore.X) + " px away");
                hipBefore = pet.Hip;
                if (!pet.Blink() || !pet.Leaving) throw new Exception("She couldn't teleport along the shelf.");
                return true;
            }
            if (step == 14)
            {
                if (pet.Leaving || pet.Arriving || pet.Falling) return false;
                if (!blinked)
                {
                    if (pet.Walking || pet.Seat == null || pet.Seat.Window != shelfSpot.Window || pet.Hip.Y != shelfSpot.Bounds.Y || pet.Hip.X == hipBefore.X) throw new Exception("She didn't teleport to a new spot on the same shelf: hip " + pet.Hip + ".");
                    checks.Add("PASS teleporting: wander glitches her out and back in " + Math.Abs(pet.Hip.X - hipBefore.X) + " px along her window instead of walking");
                    blinked = true;
                    if (!pet.Wander() || !pet.Leaving) throw new Exception("She had nowhere to hop to.");
                    return false;
                }
                if (pet.Seat == null || (pet.Seat.Window == shelfSpot.Window && pet.Seat.Y == shelfSpot.Bounds.Y)) throw new Exception("She didn't glitch over to a new spot.");
                checks.Add("PASS hopping: wander glitches her out and back in on " + (pet.Seat.Floor ? "the bottom of the screen" : "another window"));
                pet.HearMusic(true);
                return true;
            }
            if (step == 15)
            {
                if (pet.Showing != pet.SeatDance) return false;
                Snap(pet.Content as Control, "dance.png");
                pet.HearMusic(false);
                checks.Add("PASS dancing: she dances in her seat while music plays");
                outFrom = now;
                Sfx.Out = sounds.Add;
                pet.HideAway();
                return true;
            }
            if (step == 16)
            {
                if (pet.IsVisible) return false;
                if (now - outFrom < 0.6) throw new Exception("Her glitch-out ended after " + (now - outFrom).ToString("0.00") + " s.");
                if (!pet.Away || sounds.Count != 1 || sounds[0].Length != Sfx.Make(Cue.Leave, 60).Length) throw new Exception("Her glitch-out didn't play its sound once: " + sounds.Count);
                checks.Add("PASS glitch out: Hide her plays her glitch-out and its sound for " + (now - outFrom).ToString("0.0") + " s, then her window goes");
                pet.ToggleShush();
                bool hushedHidden = pet.Shushed && pet.Talk.Reply == null && !pet.Talk.Bubble.IsVisible && !pet.Talk.Pill.IsVisible;
                pet.ToggleShush();
                if (!hushedHidden || pet.Shushed || !pet.Talk.Quiet || pet.Talk.Bubble.IsVisible) throw new Exception("Quiet from the tray while she was hidden left a bubble up: " + pet.Talk.Reply);
                checks.Add("PASS hidden: Quiet from the tray while she's hidden puts up no bubble with nobody under it, and she's still free to pop in");
                arrivalsBefore = pet.Arrivals;
                pet.ComeBack();
                if (!pet.IsVisible || pet.Away || !pet.Arriving || pet.Arrivals != arrivalsBefore + 1) throw new Exception("Show her didn't glitch her back in.");
                return true;
            }
            if (step == 17)
            {
                if (pet.Arriving || pet.Falling) return false;
                if (!Sitting()) throw new Exception("After Show her she didn't sit down again.");
                if (sounds.Count != 2 || sounds[1].Length != Sfx.Make(Cue.Arrive, 60).Length) throw new Exception("Show her didn't play the arrive sound: " + sounds.Count);
                checks.Add("PASS show: Show her glitches her back in with its sound and she drops onto a seat");
                Voice.Host = FakeVoice();
                Voice.Playback = delegate(byte[] wav) { lock (spoken) spoken.Add(wav); };
                Voice.Silence = delegate { };
                pet.Talk.Dismiss();
                if (pet.VoiceStatus != "no key | no voice | quiet") throw new Exception("She started with a voice: " + pet.VoiceStatus);
                pet.AskVoiceKey();
                if (!pet.Talk.Typing || !pet.Talk.Typer.Secret || pet.Talk.Reply == null || pet.Talk.Reply.IndexOf("ElevenLabs API key", StringComparison.Ordinal) < 0) throw new Exception("Add ElevenLabs key didn't ask for the key in a hidden box.");
                pet.Talk.Submit("bad-key");
                return true;
            }
            if (step == 18)
            {
                if (pet.Talk.Reply == null) return false;
                if (pet.Talk.Reply != Voice.RejectedNew || pet.VoiceStatus != "no key | no voice | quiet" || File.Exists(Path.Combine(folder, "voice-key")) || pet.Talk.Typer.Secret) throw new Exception("A rejected ElevenLabs key wasn't explained, or was kept: " + pet.Talk.Reply + " | " + pet.VoiceStatus);
                pet.AskVoiceKey();
                pet.Talk.Submit("good-key-1234");
                return true;
            }
            if (step == 19)
            {
                if (pet.Talk.Reply == null) return false;
                string keyPath = Path.Combine(folder, "voice-key");
                string[] asked;
                lock (voiceAsked) asked = voiceAsked.ToArray();
                int voiced;
                lock (spoken) voiced = spoken.Count;
                bool shut = OperatingSystem.IsWindows() || File.GetUnixFileMode(keyPath) == (UnixFileMode.UserRead | UnixFileMode.UserWrite);
                string last = asked.Length == 0 ? "" : asked[asked.Length - 1];
                if (pet.Talk.Reply != "Key saved. I'll talk as Capri Test. Change it under Settings > Voice." || !File.Exists(keyPath) || File.ReadAllText(keyPath) != "good-key-1234" || !shut || pet.VoiceStatus != "key 1234 | Capri Test | speaking" || voiced != 1 || !last.StartsWith("POST /v1/text-to-speech/v1?", StringComparison.Ordinal) || last.IndexOf(" good-key-1234 ", StringComparison.Ordinal) < 0) throw new Exception("Saving a good ElevenLabs key went wrong: " + pet.Talk.Reply + " | " + pet.VoiceStatus + " | " + voiced + " | " + string.Join(" / ", asked));
                string voiceMenu = pet.VoiceMenu;
                if (voiceMenu != "*Use ElevenLabs | - | *Capri Test | Adam | - | New key... | Remove key ending 1234") throw new Exception("The Voice menu listed the wrong things: " + voiceMenu);
                checks.Add("PASS voice key: Settings > Voice asks for the key in a hidden box, explains and drops a rejected key, keeps a good one in a private file and picks their own voice first");
                if (pet.TypingSound) throw new Exception("Typing sound stayed on while her ElevenLabs voice works.");
                pet.Think = delegate(string words) { return "Preem question, choom."; };
                pet.Hear("talk to me");
                return true;
            }
            if (step == 20)
            {
                if (pet.Talk.Reply == null) return false;
                string[] asked;
                lock (voiceAsked) asked = voiceAsked.ToArray();
                int voiced;
                lock (spoken) voiced = spoken.Count;
                string last = asked.Length == 0 ? "" : asked[asked.Length - 1];
                if (pet.Talk.Reply != "Preem question, choom." || voiced != 2 || last.IndexOf("question", StringComparison.Ordinal) < 0 || Math.Abs(pet.Talk.Pace - 22) > 1.5) throw new Exception("Her reply wasn't spoken at the voice's pace: " + pet.Talk.Reply + " | " + voiced + " | " + pet.Talk.Pace + " | " + last);
                pet.SetSpeak(false);
                pet.Hear("again");
                return true;
            }
            if (step == 21)
            {
                if (pet.Talk.Reply == null) return false;
                int voiced;
                lock (spoken) voiced = spoken.Count;
                if (voiced != 2 || pet.VoiceStatus != "key 1234 | Capri Test | quiet" || !pet.TypingSound) throw new Exception("Speak replies off still spoke, or typing stayed off: " + voiced + " | " + pet.VoiceStatus);
                pet.ForgetVoice();
                if (File.Exists(Path.Combine(folder, "voice-key")) || pet.VoiceStatus != "no key | v1 | quiet" || pet.VoiceMenu != "Add ElevenLabs key...") throw new Exception("Remove key didn't remove it: " + pet.VoiceStatus + " | " + pet.VoiceMenu);
                checks.Add("PASS voice: replies are spoken through ElevenLabs and type out at the voice's pace, Use ElevenLabs off keeps her quiet, and Remove key deletes the file");
                fakeVoice.Stop();
                Send(shelf, "quit");
                pet.Talk.Dismiss();
                pet.Hear("still there?");
                pet.HideAway();
                return true;
            }
            if (step == 22)
            {
                if (pet.IsVisible) return false;
                if (!pet.Talk.Quiet) throw new Exception("A reply that came in after Hide her still showed a bubble: " + pet.Talk.Reply);
                arrivalsBefore = pet.Arrivals;
                pet.PopIn();
                string hello = pet.Talk.Reply;
                if (!pet.IsVisible || pet.Away || !pet.Arriving || pet.Arrivals != arrivalsBefore + 1 || Array.IndexOf(Pet.PopLines, hello) < 0 || pet.PopOut < 0) throw new Exception("She didn't pop in from hiding with a hello: " + hello + " | away " + pet.Away + " | out " + pet.PopOut);
                pet.Talk.Dismiss();
                pet.CheckPopIn(pet.PopOut + 0.1);
                if (!pet.Away || !pet.Leaving) throw new Exception("After popping in from hiding she didn't hide again.");
                return true;
            }
            if (step == 23)
            {
                if (!poppedAgain)
                {
                    if (pet.IsVisible) return false;
                    checks.Add("PASS pop-ins: with Check in on me on she glitches in to say hi even when hidden, then hides again");
                    pet.PopIn();
                    pet.Talk.Dismiss();
                    poppedAgain = true;
                    return false;
                }
                if (pet.Arriving || pet.Falling || pet.Seat == null) return false;
                pet.Tap();
                pet.CheckPopIn(pet.PopOut + 0.1);
                if (pet.Away || pet.Leaving || pet.PopOut >= 0) throw new Exception("A quick click didn't keep her out after she popped in from hiding: away " + pet.Away + " | out " + pet.PopOut);
                checks.Add("PASS a quick click while she's popped in from hiding keeps her out");
                return true;
            }
            if (step == 24)
            {
                if (pet.Arriving || pet.Falling) return false;
                pet.CheckNudge(pet.Now);
                noticedBefore = pet.Noticed;
                pet.PopIn();
                if (!pet.Leaving || pet.Away) throw new Exception("She didn't glitch out to pop in somewhere else.");
                return true;
            }
            if (step == 25 && attention == 1)
            {
                if (pet.Talk.Reply == null) return false;
                attentionSulk = pet.Talk.Reply;
                attentionSulked = Array.IndexOf(Pet.SnubbedLines, attentionSulk) >= 0 && pet.NudgeOut < 0 && pet.NudgeWait == Pet.NudgeGap * 2;
                pet.Talk.Dismiss();
                pet.Idle = delegate { return 30; };
                pet.Nudge(pet.Now, null);
                pet.Talk.Dismiss();
                pet.CheckNudge(pet.NudgeOut + 0.1);
                emptyAt = now;
                attention = 2;
                return false;
            }
            if (step == 25 && attention == 2)
            {
                if (now - emptyAt < 0.5) return false;
                bool alone = pet.Talk.Quiet && pet.NudgeOut < 0 && pet.NudgeWait == Pet.NudgeGap * 4;
                pet.Idle = delegate { return 0; };
                pet.Nudge(pet.Now, null);
                pet.Talk.Dismiss();
                pet.Grab();
                pet.CheckNudge(pet.Now);
                string glad = pet.Talk.Reply;
                pet.Drop();
                bool welcomed = Array.IndexOf(Pet.NoticedLines, glad) >= 0 && pet.NudgeOut < 0 && pet.NudgeWait == Pet.NudgeGap;
                pet.ForgetNudges();
                if (!attentionAsked || !attentionSulked || !alone || !welcomed) throw new Exception("Asking for attention went wrong: " + attentionAsk + " | " + attentionSulk + " | alone " + alone + " | " + glad + " | wait " + pet.NudgeWait);
                checks.Add("PASS attention: she asks for attention, sulks and waits twice as long when ignored, only sulks out loud when someone's at the computer, and picking her up gets a happy line and the usual 2 hours");
                peekWas = Eyes.Peek;
                Eyes.Peek = delegate { return FakeScreen(new SkiaSharp.SKColor(200, 30, 40)); };
                pet.Seeing = delegate(byte[] shot, string question)
                {
                    lock (glanceAsks) glanceAsks.Add(question);
                    return GlanceSaid;
                };
                pet.SetWatch(true);
                if (!pet.WatchShown || pet.GapShown != "2 min" || pet.GlanceGap != 120) throw new Exception("Watch my screen didn't turn on at 2 min: " + pet.WatchShown + " | " + pet.GapShown);
                pet.Talk.Dismiss();
                pet.Glance(pet.Now);
                return true;
            }
            if (step == 25)
            {
                if (pet.Leaving) return false;
                string hello = pet.Talk.Reply;
                // Read before Check in on me is turned off and on, which starts her wait again.
                double noticedAfter = pet.Noticed;
                pet.SetCheckIns(false);
                pet.Talk.Dismiss();
                pet.CheckPopIn(pet.Now + 60);
                pet.SetCheckIns(true);
                if (!pet.IsVisible || pet.Away || pet.PopOut >= 0 || Array.IndexOf(Pet.PopLines, hello) < 0) throw new Exception("Popping in while she was out went wrong: " + hello + " | away " + pet.Away + " | out " + pet.PopOut);
                if (noticedBefore < 0 || noticedAfter != noticedBefore) throw new Exception("A pop-in restarted her 2-hour wait to ask for attention: " + noticedBefore + " then " + noticedAfter);
                checks.Add("PASS pop-ins while she's out: she glitches over to a new spot at the top of the screen, says hi and stays out, and her 2-hour wait to ask for attention keeps counting");
                // Someone's at the computer, so her sulk is said; it comes once the idle time is read.
                pet.Idle = delegate { return 0; };
                pet.ForgetNudges();
                pet.Talk.Dismiss();
                pet.Nudge(pet.Now, null);
                attentionAsk = pet.Talk.Reply;
                attentionAsked = Array.IndexOf(Pet.NudgeLines, attentionAsk) >= 0 && pet.NudgeOut > pet.Now;
                pet.Talk.Dismiss();
                pet.CheckNudge(pet.NudgeOut + 0.1);
                attention = 1;
                return false;
            }
            if (step == 26)
            {
                if (pet.Glancing) return false;
                string glanceLine = pet.Talk.Reply;
                int asks;
                lock (glanceAsks) asks = glanceAsks.Count;
                if (glanceLine != Glances.Reply(GlanceSaid) || asks != 1 || glanceAsks[0] != Glances.Line) throw new Exception("Her first glance went wrong: " + glanceLine + " | " + asks);
                pet.Talk.Dismiss();
                pet.Glance(pet.Now);
                return true;
            }
            if (step == 27)
            {
                if (pet.Glancing) return false;
                int asks;
                lock (glanceAsks) asks = glanceAsks.Count;
                if (asks != 1 || pet.Talk.Reply != null) throw new Exception("She looked again at a screen that hadn't changed: " + asks);
                Eyes.Peek = delegate { return FakeScreen(new SkiaSharp.SKColor(20, 60, 220)); };
                pet.Glance(pet.Now);
                return true;
            }
            if (step == 28)
            {
                if (pet.Glancing) return false;
                int asks;
                lock (glanceAsks) asks = glanceAsks.Count;
                bool repeatDropped = asks == 2 && pet.Talk.Reply == null && glanceAsks[1].IndexOf(Glances.Reply(GlanceSaid), StringComparison.Ordinal) >= 0;
                Eyes.Peek = delegate { return null; };
                pet.Glance(pet.Now);
                if (!repeatDropped) throw new Exception("A changed screen with the same line wasn't dropped as a repeat: " + asks + " | " + pet.Talk.Reply);
                return true;
            }
            if (step == 29 && hushGlance)
            {
                if (pet.Glancing) return false;
                bool hushedQuiet = pet.Talk.Quiet;
                pet.ToggleShush();
                pet.Talk.Dismiss();
                Eyes.Peek = peekWas;
                pet.Seeing = null;
                pet.SetWatch(false);
                if (!hushedQuiet || pet.Shushed || pet.WatchShown) throw new Exception("A glance that came back after Quiet went on was said: " + pet.Talk.Reply);
                checks.Add("PASS quiet glance: a glance still out when Quiet goes on is dropped, like Windows");
                Finish(null);
                return true;
            }
            if (step == 29)
            {
                if (pet.Glancing) return false;
                string told = pet.Talk.Reply;
                List<string[]> gnome = Eyes.QuietTools(false, "wayland", "ubuntu:GNOME"), x11 = Eyes.QuietTools(false, "x11", "XFCE"), kde = Eyes.QuietTools(false, "wayland", "KDE");
                bool quietRight = gnome.Count == 1 && gnome[0][0] == "grim" && x11.Count == 4 && x11[0][0] == "import" && Array.IndexOf(x11[0], "-silent") == 1 && kde[0][0] == "spectacle" && Eyes.QuietTools(true, null, null)[0][0] == "screencapture";
                if (told != Eyes.Unwatchable || !quietRight) throw new Exception("Watching without a quiet screenshot tool went wrong: " + told + " | " + quietRight);
                checks.Add("PASS watch my screen: she glances, comments on what she sees, skips a screen that hasn't changed and a line she already said, and says once when this desktop can't take quiet screenshots");
                // A new screen with a new line, but Quiet goes on while she's looking.
                pet.Talk.Dismiss();
                Eyes.Peek = delegate { return FakeScreen(new SkiaSharp.SKColor(240, 200, 20)); };
                pet.Seeing = delegate(byte[] shot, string question) { return "SAW: a yellow spreadsheet. SAY: Spreadsheets at this hour, choom?"; };
                pet.Glance(pet.Now);
                pet.ToggleShush();
                pet.Talk.Dismiss();
                hushGlance = true;
                return false;
            }
            return false;
        }

        private string FakeVoice()
        {
            fakeVoice = new TcpListener(IPAddress.Loopback, 0);
            fakeVoice.Start();
            TcpListener listener = fakeVoice;
            Thread serve = new Thread(delegate()
            {
                while (true)
                {
                    TcpClient client;
                    try { client = listener.AcceptTcpClient(); }
                    catch (Exception) { return; }
                    try { using (client) Serve(client.GetStream()); }
                    catch (Exception) { }
                }
            });
            serve.IsBackground = true;
            serve.Start();
            return "http://127.0.0.1:" + ((IPEndPoint)fakeVoice.LocalEndpoint).Port;
        }

        private void Serve(NetworkStream stream)
        {
            List<byte> read = new List<byte>();
            byte[] chunk = new byte[4096];
            int end = -1;
            while (end < 0)
            {
                int got = stream.Read(chunk, 0, chunk.Length);
                if (got <= 0) return;
                for (int i = 0; i < got; i++) read.Add(chunk[i]);
                for (int i = 3; i < read.Count && end < 0; i++) if (read[i - 3] == 13 && read[i - 2] == 10 && read[i - 1] == 13 && read[i] == 10) end = i + 1;
            }
            string head = Encoding.ASCII.GetString(read.ToArray(), 0, end);
            string[] lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] first = lines[0].Split(' ');
            string key = "";
            int length = 0;
            foreach (string line in lines)
            {
                int colon = line.IndexOf(':');
                if (colon < 0) continue;
                string name = line.Substring(0, colon).Trim().ToLowerInvariant();
                string value = line.Substring(colon + 1).Trim();
                if (name == "xi-api-key") key = value;
                if (name == "content-length") length = int.Parse(value);
            }
            while (read.Count - end < length)
            {
                int got = stream.Read(chunk, 0, chunk.Length);
                if (got <= 0) break;
                for (int i = 0; i < got; i++) read.Add(chunk[i]);
            }
            string body = Encoding.UTF8.GetString(read.ToArray(), end, read.Count - end);
            lock (voiceAsked) voiceAsked.Add(first[0] + " " + first[1] + " " + key + " " + body);
            int status = 200;
            string type = "application/json";
            byte[] reply;
            if (key == "bad-key") { status = 401; reply = Encoding.UTF8.GetBytes("{\"detail\":{\"status\":\"invalid_api_key\",\"message\":\"Invalid API key\"}}"); }
            else if (first[1].StartsWith("/v2/voices", StringComparison.Ordinal)) reply = Encoding.UTF8.GetBytes("{\"voices\":[{\"voice_id\":\"p1\",\"name\":\"Adam\",\"category\":\"premade\"},{\"voice_id\":\"v1\",\"name\":\"Capri Test\",\"category\":\"cloned\"}],\"has_more\":false}");
            else if (first[1].StartsWith("/v1/models", StringComparison.Ordinal)) reply = Encoding.UTF8.GetBytes("[]");
            else if (first[1].StartsWith("/v1/text-to-speech/", StringComparison.Ordinal))
            {
                short[] tone = new short[24000];
                for (int i = 0; i < tone.Length; i++) tone[i] = (short)(Math.Sin(i * 0.05) * 3000);
                reply = Voice.Wave(tone, 24000);
                type = "audio/wav";
            }
            else { status = 404; reply = Encoding.UTF8.GetBytes("{}"); }
            byte[] top = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + " " + (status == 200 ? "OK" : "Error") + "\r\nContent-Type: " + type + "\r\nContent-Length: " + reply.Length + "\r\nConnection: close\r\n\r\n");
            stream.Write(top, 0, top.Length);
            stream.Write(reply, 0, reply.Length);
            stream.Flush();
        }

        private Process Shelf(PixelRect rect)
        {
            ProcessStartInfo start = new ProcessStartInfo(Environment.ProcessPath, "--shelf " + rect.X + " " + rect.Y + " " + (int)Math.Round(rect.Width / pet.DesktopScaling) + " " + (int)Math.Round(rect.Height / pet.DesktopScaling));
            start.UseShellExecute = false;
            start.RedirectStandardInput = true;
            Process process = Process.Start(start);
            if (process == null) throw new Exception("The test shelf window didn't start.");
            shelves.Add(process);
            return process;
        }

        private static void Send(Process process, string line)
        {
            if (process != null && !process.HasExited) process.StandardInput.WriteLine(line);
        }

        private string Seen()
        {
            List<string> parts = new List<string>();
            foreach (Spot spot in pet.Windows()) parts.Add(spot.Window + " " + spot.Bounds + (spot.Seat ? " seat" : ""));
            return "window list " + pet.Desk.Name + (pet.DeskTrouble == null ? "" : " after " + pet.DeskTrouble) + ", scale " + pet.Desk.Scale.ToString("0.###") + ", shelf asked " + shelfAsked + " " + (shelf == null ? "not started" : shelf.HasExited ? "exited " + shelf.ExitCode : "running") + ", saw " + (parts.Count == 0 ? "no windows" : string.Join("; ", parts)) + ", " + pet.Desk.Debug();
        }

        private Spot Near(PixelRect asked, bool seat)
        {
            Spot best = null;
            int bestFar = int.MaxValue;
            foreach (Spot spot in pet.Windows())
            {
                if (seat && !spot.Seat) continue;
                int far = Math.Abs(spot.Bounds.X - asked.X) + Math.Abs(spot.Bounds.Y - asked.Y) + Math.Abs(spot.Bounds.Width - asked.Width);
                if (far < Px(120) && far < bestFar) { best = spot; bestFar = far; }
            }
            return best;
        }

        private void Snap(Control control, string name)
        {
            if (control == null) return;
            PixelSize size = new PixelSize(Math.Max(1, (int)Math.Ceiling(control.Bounds.Width)), Math.Max(1, (int)Math.Ceiling(control.Bounds.Height)));
            using (RenderTargetBitmap shot = new RenderTargetBitmap(size, new Vector(96, 96)))
            {
                shot.Render(control);
                shot.Save(Path.Combine(folder, name));
            }
        }

        private void Scene(string name, Window[] windows, PixelRect[] boxes)
        {
            int left = int.MaxValue;
            int top = int.MaxValue;
            int right = int.MinValue;
            int bottom = int.MinValue;
            double scale = pet.DesktopScaling;
            foreach (Window window in windows)
            {
                left = Math.Min(left, window.Position.X);
                top = Math.Min(top, window.Position.Y);
                right = Math.Max(right, window.Position.X + (int)Math.Ceiling(window.Bounds.Width * scale));
                bottom = Math.Max(bottom, window.Position.Y + (int)Math.Ceiling(window.Bounds.Height * scale));
            }
            foreach (PixelRect box in boxes)
            {
                left = Math.Min(left, box.X);
                top = Math.Min(top, box.Y);
                right = Math.Max(right, box.Right);
                bottom = Math.Max(bottom, box.Bottom);
            }
            double pad = 40;
            PixelSize size = new PixelSize((int)Math.Ceiling((right - left) / scale + pad * 2), (int)Math.Ceiling((bottom - top) / scale + pad * 2));
            using (RenderTargetBitmap scene = new RenderTargetBitmap(size, new Vector(96, 96)))
            {
                using (DrawingContext context = scene.CreateDrawingContext())
                {
                    context.FillRectangle(new SolidColorBrush(Color.FromRgb(238, 240, 243)), new Rect(0, 0, size.Width, size.Height));
                    foreach (PixelRect box in boxes)
                    {
                        Rect place = new Rect(pad + (box.X - left) / scale, pad + (box.Y - top) / scale, box.Width / scale, box.Height / scale);
                        context.FillRectangle(new SolidColorBrush(Color.FromRgb(200, 204, 210)), place);
                        context.FillRectangle(new SolidColorBrush(Color.FromRgb(120, 126, 136)), new Rect(place.X, place.Y, place.Width, Math.Min(32, place.Height)));
                    }
                    foreach (Window window in windows)
                    {
                        Control face = window.Content as Control;
                        if (face == null || !window.IsVisible) continue;
                        PixelSize part = new PixelSize(Math.Max(1, (int)Math.Ceiling(face.Bounds.Width)), Math.Max(1, (int)Math.Ceiling(face.Bounds.Height)));
                        RenderTargetBitmap shot = new RenderTargetBitmap(part, new Vector(96, 96));
                        shot.Render(face);
                        context.DrawImage(shot, new Rect(pad + (window.Position.X - left) / scale, pad + (window.Position.Y - top) / scale, part.Width, part.Height));
                    }
                }
                scene.Save(Path.Combine(folder, name));
            }
        }

        private void Finish(string failure)
        {
            if (finished) return;
            finished = true;
            foreach (Process process in shelves)
            {
                try
                {
                    if (process.HasExited) continue;
                    process.StandardInput.WriteLine("quit");
                    if (!process.WaitForExit(3000)) process.Kill();
                }
                catch (Exception) { }
            }
            List<string> lines = new List<string>(checks);
            lines.Insert(0, "Capri cross-platform smoke test on " + System.Runtime.InteropServices.RuntimeInformation.OSDescription);
            if (failure != null) lines.Add("FAIL " + failure);
            File.WriteAllLines(Path.Combine(folder, "smoke-test.txt"), lines);
            pet.Finish(failure == null ? 0 : 1);
        }
    }
}

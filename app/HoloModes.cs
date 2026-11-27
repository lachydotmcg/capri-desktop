using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Companion
    {
        private enum HoloCallSource { None, Windows, Discord }

        private HoloWindow holoWindow;
        private HoloCallSource holoCallSource;
        private volatile string holoDiscordSession;
        private int holocallSequence;
        private volatile int holocallStamp;
        private HoloPose? holoExpression;
        private double holoExpressionUntil;
        private Speaker holoOpeningSound;
        private bool holoReturnToCompact, holoTyperWasVisible;
        private static volatile IntPtr holoHandle;
        private Timer holoCameraTimer;
        private int holoCameraTurn;
        private HoloCallSource holoCameraCall;
        private string holoCameraDiscordSession;
        private byte[][] holoCameraFrames;
        private HoloArt holoCameraArt;
        private System.Diagnostics.Stopwatch holoCameraClock;
        private int holoCameraSent = -1, holoCameraSentAt;

        private static FontFamily HoloFamily(string role)
        {
            if (role == "mono") return Look.Face("Capri.Font.Mono.ttf");
            if (role == "title") return Look.Face("Capri.Font.Title.ttf");
            return null;
        }

        internal static bool IsHoloWindow(IntPtr window) { return window != IntPtr.Zero && window == holoHandle; }

        private HoloCallSource ActiveHoloCall()
        {
            if (CallModeOn) return HoloCallSource.Windows;
            if (discordCompanion != null && discordCompanion.InVoice) return HoloCallSource.Discord;
            return HoloCallSource.None;
        }

        private void AddHoloModes(ToolStripMenuItem sizes)
        {
            ToolStripMenuItem compact = new ToolStripMenuItem("Holocall", null, delegate
            {
                if (HoloCompactSelected) LeaveHolo();
                else ShowHolo(false, holoWindow == null ? ActiveHoloCall() : holoCallSource, false);
            });
            ToolStripMenuItem full = new ToolStripMenuItem("Full size", null, delegate { ToggleHoloFullSize(); });
            compact.ToolTipText = "Show her in a small hologram call window";
            full.ToolTipText = "Show her hologram window full size";
            sizes.DropDownItems.Add(new ToolStripSeparator());
            sizes.DropDownItems.Add(compact);
            sizes.DropDownItems.Add(full);
            ToolStripMenuItem camera = new ToolStripMenuItem("Capri Camera", null, delegate { ToggleHoloCamera(); });
            camera.ToolTipText = "Show her on the Capri Camera webcam in your calls";
            if (CapriCamera.Bundled) sizes.DropDownItems.Add(camera);
            sizes.DropDownOpening += delegate
            {
                full.Checked = holoWindow != null && holoWindow.FullSize;
                compact.Checked = HoloCompactSelected;
                camera.Checked = holoCameraTurn != 0;
            };
        }

        private bool HoloCompactSelected { get { return holoWindow != null && (!holoWindow.FullSize || holoReturnToCompact); } }
        private bool HoloShowing { get { return holoWindow != null; } }
        private int HolocallStamp { get { return holocallStamp; } }

        private int HolocallStampForDiscord(string session)
        {
            return session != null && session == holoDiscordSession ? HolocallStamp : 0;
        }

        private string ApplyHolocallPose(int stamp, string name)
        {
            if (stamp == 0 || stamp != HolocallStamp) return "Open Holocall to use a portrait pose.";
            if (InvokeRequired)
            {
                try { return (string)Invoke(new Func<int, string, string>(ApplyHolocallPose), stamp, name); }
                catch (InvalidOperationException) { return "Holocall closed."; }
            }
            if (holoWindow == null || quitting) return "Holocall closed.";
            HoloPose pose;
            switch (name)
            {
                case "bored": pose = HoloPose.Bored; break;
                case "hand": pose = HoloPose.Hand; break;
                case "angry": pose = HoloPose.Angry; break;
                case "sad": pose = HoloPose.Sad; break;
                default: return "Choose bored, hand, angry or sad.";
            }
            holoExpression = pose;
            holoExpressionUntil = clock.Elapsed.TotalSeconds + (preferences.Motion ? holoWindow.OpeningRemaining : 0) + 2.6;
            return "Showing " + name + " in Holocall.";
        }

        private HoloPose? HolocallExpression
        {
            get { return HolocallStamp != 0 && clock.Elapsed.TotalSeconds < holoExpressionUntil ? holoExpression : null; }
        }

        private Speaker PlayHolocallOpeningSound()
        {
            int gain = Sfx.Level;
            if (testing || gain <= 0) return null;
            Speaker player = new Speaker(false) { Plain = true };
            try
            {
                using (Stream raw = typeof(HoloWindow).Assembly.GetManifestResourceStream("Capri.Holo.opening.pcm"))
                {
                    if (raw == null || raw.Length < 2 || raw.Length % 2 != 0) throw new InvalidDataException("Holocall opener sound is missing or incomplete.");
                    using (BinaryReader reader = new BinaryReader(raw))
                    {
                        short[] samples = new short[raw.Length / 2];
                        for (int i = 0; i < samples.Length; i++) samples[i] = (short)(reader.ReadInt16() * Math.Min(gain, 100) / 100);
                        player.Open(Sfx.Rate);
                        player.Play(samples);
                    }
                }
                return player;
            }
            catch (Exception error)
            {
                player.Dispose();
                Brain.Log("holocall sound", -1, error.GetType().Name + ": " + error.Message);
                return null;
            }
        }

        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(value && !HoloShowing);
        }

        private void ToggleHoloFullSize()
        {
            if (holoWindow != null && holoWindow.FullSize) BackFromHolo();
            else ShowHolo(true, holoWindow == null ? ActiveHoloCall() : holoCallSource, holoWindow != null);
        }

        private void BackFromHolo()
        {
            if (holoWindow != null && holoWindow.FullSize && holoReturnToCompact)
                ShowHolo(false, holoCallSource, false);
            else LeaveHolo();
        }

        private void ShowHolo(bool full, HoloCallSource source, bool returnToCompact)
        {
            if (quitting) return;
            HoloWindow previous = holoWindow;
            Screen screen = previous != null ? Screen.FromControl(previous)
                : Visible ? Screen.FromControl(this) : Screen.FromPoint(Cursor.Position);
            HoloWindow next;
            HoloInk.Family = HoloFamily;
            try { next = new HoloWindow(full, ReadHoloView); }
            catch (Exception error)
            {
                Brain.Log("holocall", -1, error.GetType().Name + ": " + error.Message);
                Notice.Tell(previous != null ? (IWin32Window)previous : this, "The Holocall art could not open.", "Capri display", true);
                return;
            }
            if (previous == null) holoTyperWasVisible = typer.Visible;
            else next.ContinueOpening(previous.OpeningElapsed);
            holoWindow = next;
            if (previous == null || holoCallSource != source) BindHoloCall(source);
            holoReturnToCompact = returnToCompact;
            if (previous != null) previous.Dispose();
            Hide();
            box.Hide();
            pill.Hide();
            typer.Hide();
            gifCard.Stop();
            ribbons.Stop();
            HideCallWindows();

            next.HandleCreated += delegate { holoHandle = next.Handle; };
            next.HandleDestroyed += delegate { holoHandle = IntPtr.Zero; };
            next.ClosePressed += BackFromHolo;
            next.FullSizePressed += ToggleHoloFullSize;
            next.TalkPressed += ToggleVoiceChat;
            next.TypePressed += OpenTyper;
            next.MicPressed += ToggleMic;
            next.EndPressed += EndHoloCall;
            next.CameraPressed += ToggleHoloCamera;
            next.MenuPressed += delegate { menu.Show(Cursor.Position); };
            next.FormClosed += delegate { if (holoWindow == next) LeaveHolo(); };
            try
            {
                next.OpenOn(screen, preferences.OnTop);
                CheckHoloCall();
                if (previous == null && holoWindow == next)
                {
                    holocallStamp = ++holocallSequence;
                    if (preferences.Motion) holoOpeningSound = PlayHolocallOpeningSound();
                }
            }
            catch (Exception error)
            {
                LeaveHolo();
                Brain.Log("holocall", -1, error.GetType().Name + ": " + error.Message);
                Notice.Tell(this, "The Holocall window could not open.", "Capri display", true);
            }
        }

        private void BindHoloCall(HoloCallSource source)
        {
            holoCallSource = source;
            holoDiscordSession = source == HoloCallSource.Discord ? discordCompanion.VoiceSession : null;
        }

        private void CheckHoloCall()
        {
            if (!HoloShowing || quitting) return;
            if (holoCallSource == HoloCallSource.None)
            {
                BindHoloCall(ActiveHoloCall());
                return;
            }
            if (holoCallSource == HoloCallSource.Windows && !CallModeOn ||
                holoCallSource == HoloCallSource.Discord && (holoDiscordSession == null || holoDiscordSession != discordCompanion.VoiceSession))
            {
                if (!holoWindow.FullSize || holoReturnToCompact) LeaveHolo();
                else
                {
                    BindHoloCall(HoloCallSource.None);
                    StopHoloCamera();
                }
            }
        }

        private HoloView ReadHoloView()
        {
            HoloView view = new HoloView { Motion = preferences.Motion, CanCamera = CapriCamera.Bundled, CameraOn = holoCameraTurn != 0, Expression = HolocallExpression };
            bool showingReply = talkState == TalkState.Talking || talkState == TalkState.Online;
            if (holoCallSource == HoloCallSource.Windows && CallModeOn)
            {
                CallView call = CallViewNow();
                view.InCall = view.CanEnd = true;
                view.CallName = call.App.Name;
                view.Elapsed = call.Length;
                view.Speaking = call.Talking;
                view.Thinking = call.Thinking;
                view.Captions = call.Captions;
                if (call.Recent(DateTime.UtcNow)) view.Caption = call.Said;
                else if (typer.Visible && showingReply) view.Caption = reply;
            }
            else if (holoCallSource == HoloCallSource.Discord && discordCompanion != null)
            {
                discordCompanion.ReadHoloVoice(holoDiscordSession, view);
                // The type box keeps using desktop chat during a Discord call.
                if (typer.Visible && showingReply && !view.Speaking)
                {
                    view.Captions = true;
                    view.Caption = reply;
                }
                return view;
            }
            else
            {
                view.Speaking = TalkingNow || clock.Elapsed.TotalSeconds < voiceUntil;
                view.Thinking = talkState == TalkState.Thinking;
                if (showingReply) view.Caption = reply;
            }
            view.CanTalk = true;
            view.TalkOn = VoiceChatOn;
            view.CanMute = chatting;
            view.Muted = chatting && ears.Muted;
            view.Listening = (VoiceChatOn || AfterNameOpen) && !view.Muted && (hearingStatus == null || !hearingStatus.Problem)
                && (local ? earsReady && !earsShut && (dictation != null || ears.Listening) : ears.Listening && (agent != null || resting));
            return view;
        }

        private void EndHoloCall()
        {
            if (holoCallSource == HoloCallSource.Windows && CallModeOn) ToggleCallMode();
            else if (holoCallSource == HoloCallSource.Discord && discordCompanion != null)
                discordCompanion.CapricordControl(holoDiscordSession, true);
        }

        private void LeaveHolo()
        {
            HoloWindow leaving = holoWindow;
            if (leaving == null) return;
            holocallStamp = 0;
            holoExpression = null;
            if (holoOpeningSound != null) { holoOpeningSound.Dispose(); holoOpeningSound = null; }
            StopHoloCamera();
            holoWindow = null;
            BindHoloCall(HoloCallSource.None);
            holoReturnToCompact = false;
            leaving.Dispose();
            if (quitting) return;
            if (!hidden && !docked)
            {
                Show();
                if (holoTyperWasVisible && !typer.Visible) typer.Show();
                UpdateAnimation();
            }
            ShowCallPanel();
        }

        private IWin32Window HoloOwner { get { return holoWindow == null ? this : (IWin32Window)holoWindow; } }

        private void ToggleHoloCamera()
        {
            if (holoCameraTurn != 0) { StopHoloCamera(); return; }
            if (CapriCamera.State != "ready")
            {
                Notice.Tell(HoloOwner, "Set up Capri Camera in her Room first. Then you can use it as your camera in the Discord desktop app.", "Capri Camera", false);
                return;
            }
            int turn = CameraOutput.BeginPortrait();
            if (turn == 0)
            {
                Notice.Tell(HoloOwner, "Capri Camera is busy. Turn off the Room camera or try again after its feed closes.", "Capri Camera", false);
                return;
            }
            holoCameraTurn = turn;
            BindHoloCameraCall(ActiveHoloCall());
            try
            {
                HoloInk.Family = HoloFamily;
                holoCameraArt = new HoloArt(typeof(HoloWindow).Assembly);
                holoCameraFrames = new byte[(int)HoloPose.SpeakingHand + 1][];
                for (int i = 0; i < holoCameraFrames.Length; i++)
                    holoCameraFrames[i] = HoloInk.CameraFrame(holoCameraArt.Pose((HoloPose)i));
                holoCameraClock = System.Diagnostics.Stopwatch.StartNew();
                holoCameraSent = -1;
                if (!preferences.Motion) { holoCameraArt.Dispose(); holoCameraArt = null; }
                holoCameraTimer = new Timer { Interval = 33 };
                holoCameraTimer.Tick += delegate { FeedHoloCamera(); };
                FeedHoloCamera();
                if (holoCameraTimer != null) holoCameraTimer.Start();
            }
            catch (Exception error)
            {
                StopHoloCamera();
                Brain.Log("capri camera", -1, error.GetType().Name + ": " + error.Message);
                Notice.Tell(HoloOwner, "Capri's portrait could not open for the virtual camera.", "Capri Camera", true);
            }
        }

        private void BindHoloCameraCall(HoloCallSource call)
        {
            holoCameraCall = call;
            holoCameraDiscordSession = call == HoloCallSource.Discord ? discordCompanion.VoiceSession : null;
        }

        private void FeedHoloCamera()
        {
            if (holoCameraTurn == 0) return;
            if (holoCameraCall == HoloCallSource.None) BindHoloCameraCall(ActiveHoloCall());
            if (holoCameraCall == HoloCallSource.Windows && !CallModeOn ||
                holoCameraCall == HoloCallSource.Discord && (holoCameraDiscordSession == null || holoCameraDiscordSession != discordCompanion.VoiceSession))
            {
                StopHoloCamera();
                return;
            }
            if (!CameraOutput.PortraitCurrent(holoCameraTurn))
            {
                StopHoloCamera();
                Notice.Tell(HoloOwner, "Capri Camera could not start its portrait feed. Close other video senders and try again.", "Capri Camera", true);
                return;
            }
            double now = holoCameraClock.Elapsed.TotalSeconds;
            if (holoCameraArt != null && now < holoCameraArt.IntroSeconds)
            {
                int index = (int)(now * HoloArt.Fps);
                if (index != holoCameraSent)
                {
                    byte[] intro = holoCameraArt.IntroJpeg(index);
                    if (intro != null) CameraOutput.SendPortrait(holoCameraTurn, intro);
                    holoCameraSent = index;
                    holoCameraSentAt = Environment.TickCount;
                }
                return;
            }
            if (holoCameraArt != null)
            {
                holoCameraArt.Dispose();
                holoCameraArt = null;
                holoCameraSent = -1;
                holoCameraTimer.Interval = 66;
            }
            HoloView view = new HoloView { Speaking = TalkingNow, Thinking = talkState == TalkState.Thinking, Expression = HolocallExpression };
            if (holoCameraCall == HoloCallSource.Discord) discordCompanion.ReadHoloVoice(holoCameraDiscordSession, view);
            int pose = (int)HoloInk.PoseOf(view, now);
            if (pose == holoCameraSent && unchecked(Environment.TickCount - holoCameraSentAt) < 500) return;
            CameraOutput.SendPortrait(holoCameraTurn, holoCameraFrames[pose]);
            holoCameraSent = pose;
            holoCameraSentAt = Environment.TickCount;
        }

        private void StopHoloCamera()
        {
            if (holoCameraTimer != null) { holoCameraTimer.Dispose(); holoCameraTimer = null; }
            if (holoCameraTurn != 0) CameraOutput.EndPortrait(holoCameraTurn);
            holoCameraTurn = 0;
            holoCameraFrames = null;
            if (holoCameraArt != null) { holoCameraArt.Dispose(); holoCameraArt = null; }
            holoCameraClock = null;
            holoCameraSent = -1;
            BindHoloCameraCall(HoloCallSource.None);
        }

        private void CloseCameraOutput()
        {
            StopHoloCamera();
            if (roomHost != null) roomHost.Dispose();
            if (cameraOutput != null) cameraOutput.Dispose();
        }

        private bool HoloUnseen(bool unseen)
        {
            HoloWindow window = holoWindow;
            return window != null && window.Unseen(unseen);
        }

        private static bool Rosy(Color c) { return c.A > 150 && c.R > 180 && c.G < 90; }

        private static void SaveScreen(Rectangle area, string file)
        {
            using (Bitmap shot = new Bitmap(Math.Max(1, area.Width), Math.Max(1, area.Height), System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (Graphics canvas = Graphics.FromImage(shot)) canvas.CopyFromScreen(area.Location, Point.Empty, area.Size);
                shot.Save(file, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        private void SmokeHolo(System.Collections.Generic.List<string> checks, string output)
        {
            HoloInk.Family = HoloFamily;
            HoloView idleView = new HoloView { CanTalk = true };
            HoloView localVoice = new HoloView { CanTalk = true, TalkOn = true, CanMute = true, Listening = true };
            if (HoloInk.StatusOf(idleView) != "IDLE" || HoloInk.StatusOf(localVoice) != "LISTENING" ||
                HoloInk.StatusOf(new HoloView { InCall = true }) != "IN CALL")
                throw new Exception("Holocall said it was listening without an active voice input.");
            localVoice.Muted = true;
            if (HoloInk.StatusOf(localVoice) != "MIC MUTED" || HoloInk.ActionText(HoloAction.Mic, localVoice, false) != "Unmute mic")
                throw new Exception("Holocall did not reflect a muted microphone.");
            using (HoloArt art = new HoloArt(typeof(HoloWindow).Assembly))
            {
                if (art.IntroFrames < 60 || art.Pose(HoloPose.Idle).Width != 1920 || art.Pose(HoloPose.Speaking).Height != 1080)
                    throw new Exception("Her Holocall art didn't load: " + art.IntroFrames + " opening frames.");
                using (Stream sound = typeof(HoloWindow).Assembly.GetManifestResourceStream("Capri.Holo.opening.pcm"))
                    if (sound == null || sound.Length != art.IntroFrames * (Sfx.Rate * 2L) / HoloArt.Fps)
                        throw new Exception("The Holocall sound and opening have different lengths.");
                foreach (HoloPose pose in new[] { HoloPose.Bored, HoloPose.Hand, HoloPose.Angry, HoloPose.Sad, HoloPose.SpeakingHand })
                    if (art.Pose(pose).Size != art.Pose(HoloPose.Idle).Size)
                        throw new Exception("Holocall's " + pose + " portrait doesn't match her normal canvas.");
                HoloView waving = new HoloView { Expression = HoloPose.Hand, Speaking = true };
                bool visibleHand = false;
                for (int y = 660; y < 1080 && !visibleHand; y += 12)
                    for (int x = 1240; x < 1641 && !visibleHand; x += 12)
                        visibleHand = art.Pose(HoloPose.Hand).GetPixel(x, y).ToArgb() != art.Pose(HoloPose.Idle).GetPixel(x, y).ToArgb();
                if (!visibleHand || HoloInk.PoseOf(waving, 0) != HoloPose.SpeakingHand)
                    throw new Exception("Her Holocall hand didn't overlay the portrait while she spoke.");
                const float scale = 1.5f;
                Size size = HoloInk.CompactSize(scale);
                using (Bitmap face = HoloInk.Scaled(art.Pose(HoloPose.Speaking), HoloInk.FaceSize(scale), true))
                using (Bitmap layer = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    HoloScene scene = new HoloScene { Face = face, Time = 1, View = new HoloView { InCall = true, CanTalk = true, TalkOn = true, CanMute = true, CanEnd = true, CanCamera = true, CallName = "Squad VC", Speaking = true, Caption = "you guys hear me?", Elapsed = TimeSpan.FromSeconds(267) } };
                    using (Graphics canvas = Graphics.FromImage(layer)) { canvas.Clear(Color.Transparent); HoloInk.DrawCompact(canvas, scale, scene); }
                    layer.Save(Path.Combine(output, "holocall-compact.png"), System.Drawing.Imaging.ImageFormat.Png);
                    bool bracket = false;
                    for (int d = -2; d <= 2 && !bracket; d++) bracket = Rosy(layer.GetPixel((int)(6 * scale) + d, (int)(40 * scale)));
                    if (!bracket || layer.GetPixel(1, size.Height - 2).A != 0 || HoloInk.ActionBox(HoloAction.End, scale, false, size) != RectangleF.Empty)
                        throw new Exception("The Holocall frame didn't draw its corner brackets and see-through edge.");
                    RectangleF previous = RectangleF.Empty;
                    foreach (HoloAction action in new[] { HoloAction.Talk, HoloAction.Type, HoloAction.Mic, HoloAction.FullSize })
                    {
                        RectangleF button = HoloInk.ActionBox(action, scale, false, size);
                        Point point = Point.Round(new PointF(button.Left + 2, button.Top + 2));
                        bool glyph = false;
                        for (int y = (int)button.Top; y < (int)button.Bottom && !glyph; y++)
                            for (int x = (int)button.Left; x < (int)button.Right && !glyph; x++) glyph = layer.GetPixel(x, y).A > 150;
                        if (!glyph || previous.IntersectsWith(button) || HoloInk.ActionAt(point, scene.View, scale, false, size, 0) != action)
                            throw new Exception("Holocall's " + action + " icon or its hit area did not draw correctly.");
                        if (action == HoloAction.Mic && HoloInk.ActionAt(point, idleView, scale, false, size, 0) != HoloAction.None)
                            throw new Exception("Holocall let you mute a microphone that hadn't started.");
                        previous = button;
                    }
                }
                byte[] camera = HoloInk.CameraFrame(art.Pose(HoloPose.Idle)), intro = art.IntroJpeg(0);
                File.WriteAllBytes(Path.Combine(output, "holocall-camera.jpg"), camera);
                int w, h, iw, ih;
                if (CapriCamera.Decode(camera, out w, out h) == null || w != 1280 || h != 720 || intro == null || CapriCamera.Decode(intro, out iw, out ih) == null || iw != 1280 || ih != 720)
                    throw new Exception("Capri Camera's Holocall frames aren't 1280x720 pictures.");
            }
            checks.Add("PASS Holocall's portrait and opening, truthful idle and mic status, four compact icon hit areas, and Capri Camera's 1280x720 frames");

            bool shownBefore = Visible;
            try
            {
                ShowHolo(false, HoloCallSource.None, false);
                if (holoWindow == null || !holoWindow.Visible || holoWindow.FullSize || Visible || ActiveHoloCall() != HoloCallSource.None && holoCallSource == HoloCallSource.None)
                    throw new Exception("Holocall didn't take her place on screen.");
                int stamp = HolocallStamp;
                if (stamp == 0 || ApplyHolocallPose(stamp + 1, "angry").StartsWith("Showing", StringComparison.Ordinal))
                    throw new Exception("A different Holocall session could replace her portrait.");
                foreach (string name in new[] { "bored", "hand", "angry", "sad" })
                {
                    ApplyHolocallPose(stamp, name);
                    if (ReadHoloView().Expression.ToString().ToLowerInvariant() != name)
                        throw new Exception("The " + name + " tool pose did not reach her display.");
                }
                holoExpressionUntil = clock.Elapsed.TotalSeconds - 1;
                if (ReadHoloView().Expression.HasValue) throw new Exception("The expression outlasted its hold.");
                System.Reflection.MethodInfo press = typeof(HoloWindow).GetMethod("Press", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                press.Invoke(holoWindow, new object[] { HoloAction.Type });
                if (!typer.Visible || Screen.FromControl(typer).Bounds != Screen.FromControl(holoWindow).Bounds)
                    throw new Exception("Holocall's type icon did not open Capri's type box on its screen.");
                press.Invoke(holoWindow, new object[] { HoloAction.Type });
                if (typer.Visible) throw new Exception("Holocall's type icon did not close the type box.");
                Application.DoEvents();
                System.Threading.Thread.Sleep(900);
                Application.DoEvents();
                SaveScreen(Rectangle.Inflate(holoWindow.Bounds, 24, 24), Path.Combine(output, "holocall-window.png"));
                ToggleHoloFullSize();
                if (holoWindow == null || !holoWindow.FullSize || holoWindow.Bounds != Screen.FromControl(holoWindow).Bounds)
                    throw new Exception("Full size didn't cover her screen.");
                Application.DoEvents();
                System.Threading.Thread.Sleep(400);
                Application.DoEvents();
                SaveScreen(holoWindow.Bounds, Path.Combine(output, "holocall-full-size.png"));
                ToggleHoloFullSize();
                if (holoWindow == null || holoWindow.FullSize) throw new Exception("Leaving full size opened from Holocall didn't go back to Holocall.");
            }
            finally { LeaveHolo(); }
            if (holoWindow != null || HolocallStamp != 0 || shownBefore && !hidden && !docked && !Visible) throw new Exception("Leaving Holocall didn't restore her normal display with portrait tools off.");
            checks.Add("PASS Holocall and full size: Holocall takes her place in a lean framed window, full size covers her whole screen and goes back to Holocall, and leaving brings her normal self back");
        }
    }
}

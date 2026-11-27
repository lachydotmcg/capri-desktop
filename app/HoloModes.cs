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
        private string holoDiscordSession;
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
            next.MicPressed += delegate { if (holoCallSource == HoloCallSource.Windows) CallMic(); };
            next.EndPressed += EndHoloCall;
            next.CameraPressed += ToggleHoloCamera;
            next.MenuPressed += delegate { menu.Show(Cursor.Position); };
            next.FormClosed += delegate { if (holoWindow == next) LeaveHolo(); };
            try { next.OpenOn(screen, preferences.OnTop); CheckHoloCall(); }
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
            HoloView view = new HoloView { Motion = preferences.Motion, CanCamera = CapriCamera.Bundled, CameraOn = holoCameraTurn != 0 };
            if (holoCallSource == HoloCallSource.Windows && CallModeOn)
            {
                CallView call = CallViewNow();
                view.InCall = view.CanMute = view.CanEnd = true;
                view.CallName = call.App.Name;
                view.Elapsed = call.Length;
                view.Muted = !chatting || call.Muted;
                view.Speaking = call.Talking;
                view.Thinking = call.Thinking;
                view.Captions = call.Captions;
                if (call.Recent(DateTime.UtcNow)) view.Caption = call.Said;
            }
            else if (holoCallSource == HoloCallSource.Discord && discordCompanion != null)
                discordCompanion.ReadHoloVoice(holoDiscordSession, view);
            else
            {
                view.Speaking = TalkingNow || clock.Elapsed.TotalSeconds < voiceUntil;
                view.Thinking = talkState == TalkState.Thinking;
                if (talkState == TalkState.Talking) view.Caption = reply;
            }
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
                holoCameraFrames = new[] { HoloInk.CameraFrame(holoCameraArt.Pose(HoloPose.Idle)),
                    HoloInk.CameraFrame(holoCameraArt.Pose(HoloPose.Speaking)),
                    HoloInk.CameraFrame(holoCameraArt.Pose(HoloPose.Thinking)) };
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
            HoloView view = new HoloView { Speaking = TalkingNow, Thinking = talkState == TalkState.Thinking };
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
            using (HoloArt art = new HoloArt(typeof(HoloWindow).Assembly))
            {
                if (art.IntroFrames < 60 || art.Pose(HoloPose.Idle).Width != 1920 || art.Pose(HoloPose.Speaking).Height != 1080)
                    throw new Exception("Her Holocall art didn't load: " + art.IntroFrames + " opening frames.");
                const float scale = 1.5f;
                Size size = HoloInk.CompactSize(scale);
                using (Bitmap face = HoloInk.Scaled(art.Pose(HoloPose.Speaking), HoloInk.FaceSize(scale), true))
                using (Bitmap layer = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    HoloScene scene = new HoloScene { Face = face, Time = 1, View = new HoloView { InCall = true, CanMute = true, CanEnd = true, CanCamera = true, CallName = "Squad VC", Speaking = true, Caption = "you guys hear me?", Elapsed = TimeSpan.FromSeconds(267) } };
                    using (Graphics canvas = Graphics.FromImage(layer)) { canvas.Clear(Color.Transparent); HoloInk.DrawCompact(canvas, scale, scene); }
                    layer.Save(Path.Combine(output, "holocall-compact.png"), System.Drawing.Imaging.ImageFormat.Png);
                    RectangleF end = HoloInk.ActionBox(HoloAction.FullSize, scale, false, size, scene.View);
                    bool bracket = false, key = false;
                    for (int d = -2; d <= 2 && !bracket; d++) bracket = Rosy(layer.GetPixel((int)(6 * scale) + d, (int)(40 * scale)));
                    for (int y = (int)end.Top; y < (int)end.Bottom && !key; y++)
                        for (int x = (int)end.Left; x < (int)end.Right && !key; x++) { Color c = layer.GetPixel(x, y); key = c.A > 150 && c.R > 200 && c.G > 200 && c.B > 200; }
                    if (!bracket || !key || layer.GetPixel(1, size.Height - 2).A != 0 || HoloInk.ActionAt(Point.Round(new PointF(end.X + end.Width / 2, end.Y + end.Height / 2)), scene.View, scale, false, size, 0) != HoloAction.FullSize || HoloInk.ActionBox(HoloAction.End, scale, false, size, scene.View) != RectangleF.Empty)
                        throw new Exception("The Holocall frame didn't draw its corner brackets, its one expand key and see-through edge.");
                }
                byte[] camera = HoloInk.CameraFrame(art.Pose(HoloPose.Idle)), intro = art.IntroJpeg(0);
                File.WriteAllBytes(Path.Combine(output, "holocall-camera.jpg"), camera);
                int w, h, iw, ih;
                if (CapriCamera.Decode(camera, out w, out h) == null || w != 1280 || h != 720 || intro == null || CapriCamera.Decode(intro, out iw, out ih) == null || iw != 1280 || ih != 720)
                    throw new Exception("Capri Camera's Holocall frames aren't 1280x720 pictures.");
            }
            checks.Add("PASS her Holocall art: the opening and three poses load, the lean frame draws its corner brackets, one expand key and see-through edge, and Capri Camera gets 1280x720 portrait and opening frames");

            bool shownBefore = Visible;
            try
            {
                ShowHolo(false, HoloCallSource.None, false);
                if (holoWindow == null || !holoWindow.Visible || holoWindow.FullSize || Visible || ActiveHoloCall() != HoloCallSource.None && holoCallSource == HoloCallSource.None)
                    throw new Exception("Holocall didn't take her place on screen.");
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
            if (holoWindow != null || shownBefore && !hidden && !docked && !Visible) throw new Exception("Leaving Holocall didn't bring her normal self back.");
            checks.Add("PASS Holocall and full size: Holocall takes her place in a lean framed window, full size covers her whole screen and goes back to Holocall, and leaving brings her normal self back");
        }
    }
}

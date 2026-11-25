using System;
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
        private bool holoReturnToCompact, holoTyperWasVisible, holoCaptureExcluded, holoCaptureHidden;
        private static volatile IntPtr holoHandle;
        private Timer holoCameraTimer;
        private int holoCameraTurn;
        private HoloCallSource holoCameraCall;
        private string holoCameraDiscordSession;
        private byte[][] holoCameraFrames;

        [DllImport("user32.dll")]
        private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);

        internal static bool IsHoloWindow(IntPtr window) { return window != IntPtr.Zero && window == holoHandle; }

        private HoloCallSource ActiveHoloCall()
        {
            if (CallModeOn) return HoloCallSource.Windows;
            if (discordCompanion != null && discordCompanion.InVoice) return HoloCallSource.Discord;
            return HoloCallSource.None;
        }

        private void AddHoloModes(ContextMenuStrip menu)
        {
            ToolStripMenuItem full = new ToolStripMenuItem("Full size", null, delegate { ToggleHoloFullSize(); });
            ToolStripMenuItem compact = new ToolStripMenuItem("Holocall", null, delegate
            {
                if (HoloCompactSelected) LeaveHolo();
                else
                {
                    HoloCallSource source = ActiveHoloCall();
                    if (source != HoloCallSource.None) ShowHolo(false, source, false);
                }
            });
            menu.Items.Add(full);
            menu.Items.Add(compact);
            ToolStripMenuItem camera = new ToolStripMenuItem("Capri Camera", null, delegate { ToggleHoloCamera(); });
            if (CapriCamera.Bundled) menu.Items.Add(camera);
            menu.Opening += delegate
            {
                full.Checked = holoWindow != null && holoWindow.FullSize;
                compact.Checked = HoloCompactSelected;
                compact.Enabled = compact.Checked || ActiveHoloCall() != HoloCallSource.None;
                camera.Text = holoCameraTurn != 0 ? "Stop Capri Camera feed" : "Start Capri Camera feed";
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
            holoCaptureHidden = holoCaptureExcluded = false;
            if (previous != null) previous.Dispose();
            Hide();
            box.Hide();
            pill.Hide();
            typer.Hide();
            gifCard.Stop();
            ribbons.Stop();
            HideCallWindows();

            next.HandleCreated += delegate
            {
                holoHandle = next.Handle;
                try { holoCaptureExcluded = SetWindowDisplayAffinity(holoHandle, 0x11); }
                catch (EntryPointNotFoundException) { holoCaptureExcluded = false; }
            };
            next.HandleDestroyed += delegate { holoHandle = IntPtr.Zero; };
            next.ClosePressed += BackFromHolo;
            next.FullSizePressed += ToggleHoloFullSize;
            next.MicPressed += delegate { if (holoCallSource == HoloCallSource.Windows) CallMic(); };
            next.EndPressed += EndHoloCall;
            next.CameraPressed += ToggleHoloCamera;
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
            holoCaptureHidden = false;
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
                using (HoloArt art = new HoloArt(typeof(HoloWindow).Assembly, false))
                    holoCameraFrames = new[] { HoloInk.CameraFrame(art.Pose(new HoloView())),
                        HoloInk.CameraFrame(art.Pose(new HoloView { Speaking = true })),
                        HoloInk.CameraFrame(art.Pose(new HoloView { Thinking = true })) };
                holoCameraTimer = new Timer { Interval = 200 };
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
            HoloView view = new HoloView { Speaking = TalkingNow, Thinking = talkState == TalkState.Thinking };
            if (holoCameraCall == HoloCallSource.Discord) discordCompanion.ReadHoloVoice(holoCameraDiscordSession, view);
            int pose = view.Speaking ? 1 : view.Thinking ? 2 : 0;
            CameraOutput.SendPortrait(holoCameraTurn, holoCameraFrames[pose]);
        }

        private void StopHoloCamera()
        {
            if (holoCameraTimer != null) { holoCameraTimer.Dispose(); holoCameraTimer = null; }
            if (holoCameraTurn != 0) CameraOutput.EndPortrait(holoCameraTurn);
            holoCameraTurn = 0;
            holoCameraFrames = null;
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
            if (window == null || holoCaptureExcluded) return false;
            if (unseen && window.Visible)
            {
                holoCaptureHidden = true;
                window.Hide();
                return true;
            }
            if (!unseen && holoCaptureHidden)
            {
                holoCaptureHidden = false;
                window.Show();
            }
            return false;
        }
    }
}

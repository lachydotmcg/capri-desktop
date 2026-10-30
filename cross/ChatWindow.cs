using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;

namespace CapriDesktop
{
    // Chat layout remains in its existing Avalonia coordinate space. Hyprland owns
    // native placement; never also write Window.Position on this path.
    internal abstract class ChatWindow : Window
    {
        private ChatPlacementQueue placement;
        private Func<HyprlandDisplayMap> displayMap;
        private Size intendedSize;
        private double intendedScale;
        private bool positioned, mappingSuspended;
        private readonly ChatFocusGate focusGate = new ChatFocusGate();
        private CancellationTokenSource focusJob;
        private HyprlandDesktop nativeDesktop;
        private string focusError;
        internal PixelPoint IntendedPosition { get; private set; }
        internal string PlacementError => focusError ?? (mappingSuspended ? "Chat display mapping is unavailable." : placement?.Error);
        protected bool DismissOnDeactivation => focusGate.DismissOnDeactivation;

        protected ChatWindow()
        {
            PropertyChanged += (_, e) =>
            {
                if (e.Property != IsVisibleProperty) return;
                if (!IsVisible) CancelFocus();
                placement?.Suspend(!IsVisible);
                if (IsVisible) QueuePlacement();
            };
            Activated += (_, _) => focusGate.Activated();
            Closed += (_, _) => { CancelFocus(); placement?.Dispose(); };
        }

        internal void BindNativePlacement(HyprlandDesk desktop, Func<HyprlandDisplayMap> map)
        {
            displayMap = map;
            nativeDesktop = desktop.Desktop;
            // Typer activates only after its native placement completes. Bubble and
            // pill remain non-activating, as in the original overlay contract.
            ShowActivated = false;
            string title = Title;
            placement = new ChatPlacementQueue((target, cancellation) =>
                desktop.Desktop.PlaceOwnedWindowAsync(title, target.Origin, target.Workspace, cancellation, requestedSize: target.Size));
        }

        protected void PlaceAt(PixelPoint origin, Size size, double scale)
        {
            IntendedPosition = origin; intendedSize = size; intendedScale = scale; positioned = true;
            if (placement == null) { if (Position != origin) Position = origin; }
            else if (IsVisible) QueuePlacement();
        }

        private Task QueuePlacement()
        {
            if (placement == null || !positioned || !IsVisible) return Task.CompletedTask;
            HyprlandDisplayMap map = displayMap();
            if (map == null) { mappingSuspended = true; placement.Suspend(true); return Task.CompletedTask; }
            if (mappingSuspended) { mappingSuspended = false; placement.Suspend(false); }
            return placement.Queue(ChatPlacementTarget.FromAvalonia(map, IntendedPosition, intendedSize, intendedScale));
        }

        private void CancelFocus() { focusGate.Cancel(); focusJob?.Cancel(); }

        // Hide must cancel even before Show: an explicit dismissal while the first
        // focus query is pending must never cause a later show or activation.
        public new void Hide() { CancelFocus(); base.Hide(); }
        public new void Close() { CancelFocus(); base.Close(); }

        protected async void ShowForInput(Action show, Action focus)
        {
            focusJob?.Cancel();
            var job = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            focusJob = job;
            int request = focusGate.Begin();
            focusError = null;
            try
            {
                // Observe the input owner before mapping. If the user selects a
                // different window during placement, don't steal focus back.
                long previous = nativeDesktop == null ? 0 : HyprlandJson.Active(await nativeDesktop.QueryAsync("j/activewindow", job.Token));
                if (!focusGate.Current(request)) return;
                show();
                while (focusGate.Current(request) && IsVisible)
                {
                    job.Token.ThrowIfCancellationRequested();
                    // Following an animating pet can keep the queue busy. The first
                    // verified placement is sufficient to request input focus.
                    await Task.WhenAny(QueuePlacement(), Task.Delay(50, job.Token));
                    job.Token.ThrowIfCancellationRequested();
                    if (placement == null || !mappingSuspended && placement.Applied != null && placement.Error == null) break;
                    await Task.Delay(50, job.Token);
                }
                if (!focusGate.Current(request) || !IsVisible) return;
                if (nativeDesktop != null)
                {
                    string active = await nativeDesktop.QueryAsync("j/activewindow", job.Token);
                    using var document = JsonDocument.Parse(active);
                    JsonElement window = document.RootElement;
                    bool owned = window.TryGetProperty("pid", out JsonElement pid) && pid.GetInt32() == Environment.ProcessId &&
                        window.TryGetProperty("title", out JsonElement title) && title.GetString() == Title;
                    if (!ChatFocusGate.KeepsFocusIntent(previous, HyprlandJson.Active(active), owned))
                    { if (focusGate.Current(request)) Hide(); return; }
                }
                if (!focusGate.Activate(request)) return;
                focus();
                if (IsActive) focusGate.Activated();
                // A rejected activation does not leave an indefinitely pending input
                // window that ignores later deactivation or could steal focus later.
                await Task.Delay(150, job.Token);
                if (focusGate.Current(request) && !IsActive)
                { focusError = "The typer did not receive input focus."; Hide(); }
            }
            catch (OperationCanceledException) when (job.IsCancellationRequested)
            {
                if (focusGate.Current(request)) { focusError = "Opening the typer timed out before placement and focus completed."; Hide(); }
            }
            catch (Exception failure)
            {
                if (focusGate.Current(request)) { focusError = failure.Message; Hide(); }
            }
            finally { if (focusJob == job) focusJob = null; job.Dispose(); }
        }
    }
}

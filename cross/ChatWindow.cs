using System;
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
        private int focusRevision;
        internal PixelPoint IntendedPosition { get; private set; }
        internal string PlacementError => mappingSuspended ? "Chat display mapping is unavailable." : placement?.Error;

        protected ChatWindow()
        {
            PropertyChanged += (_, e) =>
            {
                if (e.Property != IsVisibleProperty) return;
                if (!IsVisible) ++focusRevision;
                placement?.Suspend(!IsVisible);
                if (IsVisible) QueuePlacement();
            };
            Closed += (_, _) => { ++focusRevision; placement?.Dispose(); };
        }

        internal void BindNativePlacement(HyprlandDesk desktop, Func<HyprlandDisplayMap> map)
        {
            displayMap = map;
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

        protected async void FocusAfterPlacement(Action focus)
        {
            int revision = ++focusRevision;
            await QueuePlacement();
            if (IsVisible && revision == focusRevision && (placement == null || placement.Applied != null && placement.Error == null)) focus();
        }
    }
}

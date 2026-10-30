using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Threading;

namespace CapriDesktop
{
    internal sealed partial class Pet
    {
        private HyprlandPetSurface petSurface;
        private HyprlandDisplayMap lastNativeMap;
        private bool nativePlaced;
        private int nativePlacementRevision;
        internal string MotionStatus => petSurface?.Error ?? rootPointer?.Error ?? (Desk is HyprlandDesk ? "Native Hyprland placement and dragging" : Desk.Name);

        private bool MoveNative(Point to, HyprlandDisplayMap map = null)
        {
            if (!(Desk is HyprlandDesk native) || petSurface == null) return false;
            map ??= native.MapAt(new PixelPoint((int)Math.Round(to.X + body.Size * SitX * DesktopScaling), (int)Math.Round(to.Y + body.Size * SitY * DesktopScaling))) ?? native.ActiveMap;
            if (map == null) return false;
            anchor = new PixelPoint((int)Math.Round(to.X), (int)Math.Round(to.Y));
            lastNativeMap = map;
            Point origin = map.ToLogical(new Point(to.X + FrameX * DesktopScaling, to.Y + FrameY * DesktopScaling));
            int workspace = map.Output.SpecialWorkspace != 0 ? map.Output.SpecialWorkspace : map.Output.Workspace;
            petSurface.Queue(origin, workspace);
            return true;
        }

        private void PrepareNativePlacement()
        {
            if (!(Desk is HyprlandDesk) || nativePlaced) return;
            int revision = ++nativePlacementRevision;
            int attempts = 0;
            void PlaceWhenReady()
            {
                if (quitting || revision != nativePlacementRevision || nativePlaced) return;
                Desk.BindScreens(Screens.All);
                if (((HyprlandDesk)Desk).ActiveMap != null) { TryPlaceNative(); return; }
                if (++attempts < 40) DispatcherTimer.RunOnce(PlaceWhenReady, TimeSpan.FromMilliseconds(50));
            }
            PlaceWhenReady();
        }

        private bool TryPlaceNative()
        {
            if (!(Desk is HyprlandDesk native) || petSurface == null) return false;
            HyprlandDisplayMap map = native.Maps.FirstOrDefault(m => m.Output.Name == Saved.PetOutput) ?? native.ActiveMap;
            if (map == null) { PrepareNativePlacement(); return true; }
            PixelRect area = map.ToAvalonia(map.Output.WorkingArea);
            Point origin;
            double width = Width * DesktopScaling, height = Height * DesktopScaling;
            if (Saved.Placed && Saved.PetOutput == map.Output.Name && Saved.PetAcross.HasValue && Saved.PetDown.HasValue)
                origin = PetPlacementGeometry.Restore(area, width, height, Saved.PetAcross.Value, Saved.PetDown.Value);
            else origin = PetPlacementGeometry.Restore(area, width, height, 0.5, 0.5);
            Point to = new Point(origin.X - FrameX * DesktopScaling, origin.Y - FrameY * DesktopScaling);
            nativePlaced = true;
            MoveNative(to, map);
            Raise(Now + 6);
            if (Saved.NoIntro && Saved.Placed) Reappear();
            else Arrive();
            return true;
        }

        private bool BringNative()
        {
            if (!(Desk is HyprlandDesk native) || petSurface == null) return false;
            EndRootDrag(false, false);
            Desk.BindScreens(Screens.All);
            HyprlandDisplayMap map = native.ActiveMap;
            if (map == null) { nativePlaced = false; PrepareNativePlacement(); return true; }
            Halt();
            SetAway(false);
            if (!IsVisible) { Show(); Look.OnTopAgain(this); }
            walking = falling = hanging = false;
            seat = null;
            PixelRect area = map.ToAvalonia(map.Output.WorkingArea);
            Point origin = PetPlacementGeometry.Restore(area, Width * DesktopScaling, Height * DesktopScaling, 0.5, 0);
            MoveNative(new Point(origin.X - FrameX * DesktopScaling, origin.Y - FrameY * DesktopScaling), map);
            Arrive();
            Raise(Now + 6);
            return true;
        }

        private void SaveNativePlacement()
        {
            if (!(Desk is HyprlandDesk native)) return;
            HyprlandDisplayMap map = lastNativeMap == null ? native.MapAt(Hip) : native.Maps.FirstOrDefault(m => m.Output.Name == lastNativeMap.Output.Name);
            if (map == null) return;
            PixelRect area = map.ToAvalonia(map.Output.WorkingArea);
            Point origin = new Point(anchor.X + FrameX * DesktopScaling, anchor.Y + FrameY * DesktopScaling);
            Point fraction = PetPlacementGeometry.Fraction(area, Width * DesktopScaling, Height * DesktopScaling, origin);
            Saved.PetOutput = map.Output.Name;
            Saved.PetAcross = fraction.X;
            Saved.PetDown = fraction.Y;
        }

        private IEnumerable<PixelRect> NativeFloors(IEnumerable<PixelRect> areas)
        {
            if (!(Desk is HyprlandDesk)) return areas;
            int belowHip = Math.Max(0, (int)Math.Ceiling((FrameY + Height - body.Size * SitY) * DesktopScaling));
            return areas.Select(area => new PixelRect(area.X, area.Y, area.Width, Math.Max(1, area.Height - belowHip)));
        }

        private bool RecoverNativeLayout()
        {
            if (!(Desk is HyprlandDesk native) || petSurface == null) return false;
            HyprlandDisplayMap map = lastNativeMap == null ? native.MapAt(Hip) : native.Maps.FirstOrDefault(m => m.Output.Name == lastNativeMap.Output.Name);
            if (map != null && PetPlacementGeometry.Contains(map.ToAvalonia(map.Output.WorkingArea),
                new Point(anchor.X + FrameX * DesktopScaling, anchor.Y + FrameY * DesktopScaling), Width * DesktopScaling, Height * DesktopScaling)) return false;
            EndRootDrag(false, false);
            walking = falling = false;
            seat = null;
            BringNative();
            return true;
        }

        private string MoveNativeOtherScreen(string which)
        {
            if (!(Desk is HyprlandDesk native)) return null;
            HyprlandDisplayMap[] maps = native.Maps;
            if (maps.Length == 0) return "My screen layout is reconnecting. Try again in a moment.";
            int[] x = maps.Select(m => (int)m.Output.Logical.Center.X).ToArray();
            int[] y = maps.Select(m => (int)m.Output.Logical.Center.Y).ToArray();
            int current = Array.FindIndex(maps, m => m.Output.Name == lastNativeMap?.Output.Name);
            int primary = Array.FindIndex(maps, m => m.Output.Focused);
            Point? pointer = native.Desktop.Pointer();
            int pointed = pointer.HasValue ? Array.FindIndex(maps, m => m.Output.Logical.Contains(pointer.Value)) : -1;
            int target = Array.FindIndex(maps, m => string.Equals(m.Output.Name, which, StringComparison.OrdinalIgnoreCase));
            if (target < 0) target = Hands.PickScreen(x, y, null, Math.Max(0, primary), current, pointed, which);
            if (target < 0) return maps.Length < 2 ? "There's no other screen to go to." : "There's no screen like that to go to.";
            if (target == current) return "I'm already on " + maps[target].Output.Name + ".";
            PixelRect area = maps[target].ToAvalonia(maps[target].Output.WorkingArea);
            Point origin = PetPlacementGeometry.Restore(area, Width * DesktopScaling, Height * DesktopScaling, 0.5, 0.5);
            Teleport(new PixelPoint((int)Math.Round(origin.X - FrameX * DesktopScaling + body.Size * SitX * DesktopScaling),
                (int)Math.Round(origin.Y - FrameY * DesktopScaling + body.Size * SitY * DesktopScaling)));
            return "Moving to " + maps[target].Output.Name + ".";
        }
    }
}

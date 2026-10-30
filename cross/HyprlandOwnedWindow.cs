using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;

namespace CapriDesktop
{
    internal sealed record HyprlandOwnedPlacement(HyprlandWindow Window, HyprlandOutput Output, string Layout, HyprlandOutput[] Outputs)
    {
        internal int Workspace => Output.SpecialWorkspace != 0 ? Output.SpecialWorkspace : Output.Workspace;
        internal bool Visible => !Window.Hidden && Window.Workspace == Workspace && Output.WorkingArea.Contains(Window.Logical);
    }

    internal sealed partial class HyprlandDesktop
    {
        internal async Task<HyprlandOwnedPlacement> ObserveOwnedWindowAsync(string title, CancellationToken cancellation)
        {
            if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("An explicit owned window role is required.", nameof(title));
            HyprlandOutput[] outputs = HyprlandJson.Outputs(await QueryAsync("j/monitors", cancellation).ConfigureAwait(false));
            HyprlandWindow[] owned = HyprlandJson.Windows(await QueryAsync("j/clients", cancellation).ConfigureAwait(false))
                .Where(w => w.Pid == Environment.ProcessId && w.Title == title).ToArray();
            if (owned.Length != 1) throw new InvalidOperationException("The owned window is not mapped uniquely.");
            HyprlandOutput[] after = HyprlandJson.Outputs(await QueryAsync("j/monitors", cancellation).ConfigureAwait(false));
            if (HyprlandJson.Locked(await QueryAsync("j/locked", cancellation).ConfigureAwait(false)))
                throw new InvalidOperationException("The desktop session is locked.");
            HyprlandOutput output = outputs.SingleOrDefault(o => o.Focused);
            string layout = HyprlandJson.LayoutId(outputs);
            if (output == null) throw new InvalidOperationException("No active output is available for the owned window.");
            if (layout != HyprlandJson.LayoutId(after) || !after.Any(o => o.Id == output.Id && o.Focused))
                throw new InvalidOperationException("The active output changed during observation; placement will retry.");
            return new HyprlandOwnedPlacement(owned[0], output, layout, outputs);
        }

        internal async Task PlaceOwnedWindowAsync(string title, Point origin, int workspace, CancellationToken cancellation, HyprlandOwnedPlacement expected = null, Size? requestedSize = null)
        {
            if (!double.IsFinite(origin.X) || !double.IsFinite(origin.Y)) throw new ArgumentOutOfRangeException(nameof(origin));
            if (requestedSize is Size size && (!double.IsFinite(size.Width) || !double.IsFinite(size.Height) || size.Width < 1 || size.Height < 1))
                throw new ArgumentOutOfRangeException(nameof(requestedSize));
            await EnsureDialect(cancellation).ConfigureAwait(false);
            HyprlandOwnedPlacement before = await ObserveOwnedWindowAsync(title, cancellation).ConfigureAwait(false);
            bool focused = expected != null;
            if (!before.Outputs.Any(o => (o.SpecialWorkspace != 0 ? o.SpecialWorkspace : o.Workspace) == workspace) ||
                (focused && (workspace != before.Workspace || !SamePlacement(expected, before, true))))
                throw new InvalidOperationException("The owned window or active output changed before placement.");
            HyprlandWindow window = before.Window;
            if (!window.Floating)
            {
                await Dispatch(lua.Value ? "hl.dsp.window.float({action=\"on\",window=\"" + window.Selector + "\"})" : "setfloating " + window.Selector, cancellation).ConfigureAwait(false);
                HyprlandOwnedPlacement floating = await ObserveOwnedWindowAsync(title, cancellation).ConfigureAwait(false);
                if (!SamePlacement(before, floating, focused)) throw new InvalidOperationException("The owned window or active output changed while floating.");
                window = floating.Window;
            }
            if (window.Workspace != workspace)
                await Dispatch(MoveWorkspace(window, workspace.ToString(CultureInfo.InvariantCulture)), cancellation).ConfigureAwait(false);
            HyprlandOwnedPlacement after = await ObserveOwnedWindowAsync(title, cancellation).ConfigureAwait(false);
            if (!SamePlacement(before, after, focused)) throw new InvalidOperationException("The owned window or active output changed during placement.");
            if (after.Window.Workspace != workspace) throw new InvalidOperationException("The owned window has not reached its requested workspace; placement will retry.");
            if (requestedSize is Size wanted && (Math.Abs(after.Window.Logical.Width - wanted.Width) > 0.5 || Math.Abs(after.Window.Logical.Height - wanted.Height) > 0.5))
            {
                string width = Math.Round(wanted.Width).ToString(CultureInfo.InvariantCulture), height = Math.Round(wanted.Height).ToString(CultureInfo.InvariantCulture);
                await Dispatch(lua.Value ? "hl.dsp.window.resize({window=\"" + window.Selector + "\",x=" + width + ",y=" + height + ",relative=false})"
                    : "resizewindowpixel exact " + width + " " + height + "," + window.Selector, cancellation).ConfigureAwait(false);
                after = await ObserveOwnedWindowAsync(title, cancellation).ConfigureAwait(false);
                if (!SamePlacement(before, after, focused)) throw new InvalidOperationException("The owned window or active output changed while resizing.");
                if (Math.Abs(after.Window.Logical.Width - wanted.Width) > 1 || Math.Abs(after.Window.Logical.Height - wanted.Height) > 1)
                    throw new InvalidOperationException("The owned window has not applied its requested size; placement will retry.");
            }
            string x = Math.Round(origin.X).ToString(CultureInfo.InvariantCulture), y = Math.Round(origin.Y).ToString(CultureInfo.InvariantCulture);
            await Dispatch(lua.Value ? "hl.dsp.window.move({window=\"" + window.Selector + "\",x=" + x + ",y=" + y + "})"
                : "movewindowpixel exact " + x + " " + y + "," + window.Selector, cancellation).ConfigureAwait(false);
            Point destination = new Point(Math.Round(origin.X), Math.Round(origin.Y));
            Size dimensions = requestedSize ?? after.Window.Logical.Size;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                HyprlandOwnedPlacement placed = await ObserveOwnedWindowAsync(title, cancellation).ConfigureAwait(false);
                if (!SamePlacement(before, placed, focused)) throw new InvalidOperationException("The owned window or active output changed after placement.");
                Rect bounds = placed.Window.Logical;
                if (!placed.Window.Hidden && placed.Window.Workspace == workspace &&
                    Math.Abs(bounds.X - destination.X) <= 1 && Math.Abs(bounds.Y - destination.Y) <= 1 &&
                    Math.Abs(bounds.Width - Math.Round(dimensions.Width)) <= 1 && Math.Abs(bounds.Height - Math.Round(dimensions.Height)) <= 1) return;
                if (attempt < 3) await delay(TimeSpan.FromMilliseconds(20), cancellation).ConfigureAwait(false);
            }
            throw new InvalidOperationException("The owned window did not reach its requested position, workspace and size.");
        }

        private static bool SamePlacement(HyprlandOwnedPlacement a, HyprlandOwnedPlacement b, bool focused) =>
            a.Window.Address == b.Window.Address && (!focused || a.Output.Id == b.Output.Id) && a.Layout == b.Layout;
    }
}

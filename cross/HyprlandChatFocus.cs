using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CapriDesktop
{
    internal sealed partial class HyprlandDesktop
    {
        private readonly Dictionary<string, long> passiveChatWindows = new Dictionary<string, long>();

        internal void ForgetPassiveChatPolicy(string title)
        {
            lock (passiveChatWindows) passiveChatWindows.Remove(title);
        }

        internal async Task ConfigurePassiveChatAsync(string title, CancellationToken cancellation)
        {
            if (title != "Capri says" && title != "Talk to Capri")
                throw new ArgumentException("Only passive bubble and pill surfaces receive the no-focus policy.", nameof(title));
            await EnsureDialect(cancellation).ConfigureAwait(false);
            HyprlandOwnedPlacement owned = await ObserveOwnedWindowAsync(title, cancellation).ConfigureAwait(false);
            lock (passiveChatWindows)
                if (passiveChatWindows.TryGetValue(title, out long configured) && configured == owned.Window.Address) return;
            await Dispatch(lua.Value
                ? "hl.dsp.window.set_prop({window=\"" + owned.Window.Selector + "\",prop=\"no_focus\",value=\"true\"})"
                : "setprop " + owned.Window.Selector + " no_focus true", cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            lock (passiveChatWindows) passiveChatWindows[title] = owned.Window.Address;
        }

        internal async Task WaitForOwnedInputFocusAsync(string title, long previous, Func<CancellationToken, Task<bool>> keyboardFocus, CancellationToken cancellation)
        {
            if (title != "Type to Capri") throw new ArgumentException("Explicit chat focus is limited to the owned typer.", nameof(title));
            string session = SessionId;
            await EnsureDialect(cancellation).ConfigureAwait(false);
            HyprlandOwnedPlacement before = await ObserveOwnedWindowAsync(title, cancellation).ConfigureAwait(false);
            ValidateInputSurface(before, before, session);
            long active = HyprlandJson.Active(await QueryAsync("j/activewindow", cancellation).ConfigureAwait(false));
            if (active != previous && active != before.Window.Address)
                throw new InvalidOperationException("The input owner changed while the typer was opening.");
            cancellation.ThrowIfCancellationRequested();
            if (active != before.Window.Address)
            {
                // One explicit user request, one compositor focus operation. The
                // normal Hyprland dispatcher may move the pointer to this window.
                await Dispatch(lua.Value ? "hl.dsp.focus({window=\"" + before.Window.Selector + "\"})"
                    : "focuswindow " + before.Window.Selector, cancellation).ConfigureAwait(false);
            }
            bool observed = false;
            for (int attempt = 0; attempt < 40; attempt++)
            {
                HyprlandOwnedPlacement current = await ObserveOwnedWindowAsync(title, cancellation).ConfigureAwait(false);
                ValidateInputSurface(before, current, session);
                active = HyprlandJson.Active(await QueryAsync("j/activewindow", cancellation).ConfigureAwait(false));
                if (active != before.Window.Address && (observed || active != previous))
                    throw new InvalidOperationException("The input owner changed before the typer received keyboard focus.");
                if (active == before.Window.Address)
                {
                    observed = true;
                    if (await keyboardFocus(cancellation).ConfigureAwait(false))
                    {
                        current = await ObserveOwnedWindowAsync(title, cancellation).ConfigureAwait(false);
                        ValidateInputSurface(before, current, session);
                        if (HyprlandJson.Active(await QueryAsync("j/activewindow", cancellation).ConfigureAwait(false)) == before.Window.Address) return;
                        throw new InvalidOperationException("The input owner changed while confirming typer keyboard focus.");
                    }
                }
                if (attempt < 39) await delay(TimeSpan.FromMilliseconds(20), cancellation).ConfigureAwait(false);
            }
            throw new InvalidOperationException("The compositor did not grant keyboard focus to the typer.");
        }

        private void ValidateInputSurface(HyprlandOwnedPlacement before, HyprlandOwnedPlacement current, string session)
        {
            if (SessionId != session || !SamePlacement(before, current, false) || current.Window.Hidden ||
                current.Window.Workspace != before.Window.Workspace || !current.Outputs.Any(output =>
                    (output.SpecialWorkspace != 0 ? output.SpecialWorkspace : output.Workspace) == current.Window.Workspace &&
                    output.WorkingArea.Contains(current.Window.Logical)))
                throw new InvalidOperationException("The owned typer, visible workspace or output layout changed before focus completed.");
        }
    }
}

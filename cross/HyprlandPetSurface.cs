using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;

namespace CapriDesktop
{
    internal sealed partial class HyprlandDesktop
    {
        internal async Task PlaceOwnedWindowAsync(string title, Point origin, int workspace, CancellationToken cancellation)
        {
            if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("An explicit owned window role is required.", nameof(title));
            await EnsureDialect(cancellation).ConfigureAwait(false);
            HyprlandWindow[] owned = HyprlandJson.Windows(await QueryAsync("j/clients", cancellation).ConfigureAwait(false))
                .Where(w => w.Pid == Environment.ProcessId && w.Title == title).ToArray();
            if (owned.Length != 1) throw new InvalidOperationException("The owned window is not mapped uniquely.");
            HyprlandWindow window = owned[0];
            if (!window.Floating)
                await Dispatch(lua.Value ? "hl.dsp.window.float({action=\"on\",window=\"" + window.Selector + "\"})" : "setfloating " + window.Selector, cancellation).ConfigureAwait(false);
            if (window.Workspace != workspace)
                await Dispatch(MoveWorkspace(window, workspace.ToString(CultureInfo.InvariantCulture)), cancellation).ConfigureAwait(false);
            HyprlandWindow[] after = HyprlandJson.Windows(await QueryAsync("j/clients", cancellation).ConfigureAwait(false));
            if (!after.Any(w => w.Address == window.Address && w.Pid == Environment.ProcessId && w.Title == title))
                throw new InvalidOperationException("The owned window changed during placement.");
            string x = Math.Round(origin.X).ToString(CultureInfo.InvariantCulture), y = Math.Round(origin.Y).ToString(CultureInfo.InvariantCulture);
            await Dispatch(lua.Value ? "hl.dsp.window.move({window=\"" + window.Selector + "\",x=" + x + ",y=" + y + "})"
                : "movewindowpixel exact " + x + " " + y + "," + window.Selector, cancellation).ConfigureAwait(false);
        }

        internal async Task<HyprlandWindow> PetSurfaceAsync(CancellationToken cancellation)
        {
            HyprlandWindow[] owned = HyprlandJson.Windows(await QueryAsync("j/clients", cancellation).ConfigureAwait(false))
                .Where(w => w.Pid == Environment.ProcessId && w.AppId == "Capri" && w.Title == "Capri").ToArray();
            if (owned.Length != 1) throw new InvalidOperationException("The owned Capri pet surface is not mapped uniquely.");
            return owned[0];
        }

        internal async Task ConfigurePetAsync(HyprlandWindow pet, CancellationToken cancellation)
        {
            await EnsureDialect(cancellation).ConfigureAwait(false);
            if (pet.Pid != Environment.ProcessId || pet.Title != "Capri" || pet.AppId != "Capri")
                throw new InvalidOperationException("Only this process's pet surface may receive the pet policy.");
            if ((await PetSurfaceAsync(cancellation).ConfigureAwait(false)).Address != pet.Address)
                throw new InvalidOperationException("The pet surface changed before its policy was applied.");
            if (!pet.Floating)
                await Dispatch(lua.Value ? "hl.dsp.window.float({action=\"on\",window=\"" + pet.Selector + "\"})" : "setfloating " + pet.Selector, cancellation).ConfigureAwait(false);
            foreach (var property in new[] { ("no_anim", "true"), ("no_blur", "true"), ("no_shadow", "true"), ("decorate", "false"), ("border_size", "0"), ("rounding", "0") })
                await Dispatch(lua.Value
                    ? "hl.dsp.window.set_prop({window=\"" + pet.Selector + "\",prop=\"" + property.Item1 + "\",value=\"" + property.Item2 + "\"})"
                    : "setprop " + pet.Selector + " " + property.Item1 + " " + property.Item2, cancellation).ConfigureAwait(false);
        }

        internal async Task MovePetAsync(HyprlandWindow pet, Point origin, int workspace, CancellationToken cancellation)
        {
            await EnsureDialect(cancellation).ConfigureAwait(false);
            HyprlandWindow current = await PetSurfaceAsync(cancellation).ConfigureAwait(false);
            if (current.Address != pet.Address) throw new InvalidOperationException("The pet surface changed during placement.");
            if (HyprlandJson.Locked(await QueryAsync("j/locked", cancellation).ConfigureAwait(false)))
                throw new InvalidOperationException("The desktop is locked.");
            HyprlandOutput[] outputs = HyprlandJson.Outputs(await QueryAsync("j/monitors", cancellation).ConfigureAwait(false));
            if (!outputs.Any(o => o.SpecialWorkspace != 0 ? o.SpecialWorkspace == workspace : o.Workspace == workspace))
                throw new InvalidOperationException("The destination workspace is no longer visible.");
            if (current.Workspace != workspace)
                await Dispatch(MoveWorkspace(current, workspace.ToString(CultureInfo.InvariantCulture)), cancellation).ConfigureAwait(false);
            string x = Math.Round(origin.X).ToString(CultureInfo.InvariantCulture), y = Math.Round(origin.Y).ToString(CultureInfo.InvariantCulture);
            await Dispatch(lua.Value
                ? "hl.dsp.window.move({window=\"" + current.Selector + "\",x=" + x + ",y=" + y + "})"
                : "movewindowpixel exact " + x + " " + y + "," + current.Selector, cancellation).ConfigureAwait(false);
        }
    }

    internal sealed class HyprlandPetSurface : IDisposable
    {
        private sealed record Placement(Point Origin, int Workspace);
        private readonly HyprlandDesktop desktop;
        private readonly object gate = new object();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private CancellationTokenSource moving;
        private Placement pending;
        private Task worker;
        private bool suspended, disposed;
        private long configured;
        internal string Error { get; private set; }

        internal HyprlandPetSurface(HyprlandDesktop desktop) { this.desktop = desktop; }

        internal void Queue(Point origin, int workspace)
        {
            lock (gate)
            {
                if (disposed || suspended) return;
                pending = new Placement(origin, workspace);
                if (worker == null || worker.IsCompleted) worker = Task.Run(Drain);
            }
        }

        internal void Suspend(bool value)
        {
            lock (gate)
            {
                suspended = value;
                pending = null;
                moving?.Cancel();
            }
        }

        internal void Remapped() { lock (gate) configured = 0; }

        private async Task Drain()
        {
            while (true)
            {
                Placement next;
                CancellationTokenSource operation;
                lock (gate)
                {
                    if (disposed || suspended || pending == null) { worker = null; return; }
                    next = pending;
                    pending = null;
                    moving = operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                }
                try
                {
                    HyprlandWindow pet = await desktop.PetSurfaceAsync(operation.Token).ConfigureAwait(false);
                    if (configured != pet.Address)
                    {
                        await desktop.ConfigurePetAsync(pet, operation.Token).ConfigureAwait(false);
                        configured = pet.Address;
                    }
                    operation.Token.ThrowIfCancellationRequested();
                    await desktop.MovePetAsync(pet, next.Origin, next.Workspace, operation.Token).ConfigureAwait(false);
                    Error = null;
                }
                catch (OperationCanceledException) { }
                catch (Exception error) { Error = error.Message; }
                finally
                {
                    lock (gate) if (moving == operation) moving = null;
                    operation.Dispose();
                }
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                disposed = true;
                pending = null;
                lifetime.Cancel();
                moving?.Cancel();
            }
        }
    }
}

using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Platform;

namespace CapriDesktop
{
    // All show/hide calls (including Window-typed callers) share this preparation
    // fence. A hidden or closed generation can never map after its await completes.
    internal class PassiveWindow : Window
    {
        private CancellationTokenSource showing;
        private bool closed;
        internal PassiveWindow() { Closed += (_, _) => { closed = true; showing?.Cancel(); }; }
        public override async void Show()
        {
            if (IsVisible || (showing != null && !showing.IsCancellationRequested) || closed) return;
            var owner = new CancellationTokenSource(); showing = owner;
            try
            {
                await NativeSurface.Prepare(this, owner.Token);
                if (!owner.IsCancellationRequested && !closed) base.Show();
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { Program.Crashed(error); }
            finally { if (showing == owner) showing = null; owner.Dispose(); }
        }
        public override void Hide() { showing?.Cancel(); base.Hide(); }
    }

    internal static class NativePassive
    {
        internal static readonly string ClassPrefix = "Capri.Passive." + Environment.ProcessId + "." + Guid.NewGuid().ToString("N");
        private static readonly string ruleKey = ClassPrefix.Replace(".", "_");
        private static long ruleRevision = -1;
        private static int retiring;
        internal static bool Ready => HyprlandDesktop.Current == null || ruleRevision == HyprlandDesktop.Current.SurfaceRevision;
        private static readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);

        internal static async Task Initialize()
        {
            if (HyprlandDesktop.Current == null) return;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await EnsureRule(timeout.Token).ConfigureAwait(false);
        }

        internal static async Task EnsureRule(CancellationToken cancellation)
        {
            var desktop = HyprlandDesktop.Current;
            if (desktop == null) return;
            await gate.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                if (Volatile.Read(ref retiring) != 0) throw new OperationCanceledException("Native surfaces are shutting down.");
                long revision = desktop.SurfaceRevision;
                string key = JsonSerializer.Serialize(ruleKey);
                string code = "if not _G[" + key + "] then local rule=hl.window_rule({name=" + JsonSerializer.Serialize(ruleKey) +
                    ",match={class=" + JsonSerializer.Serialize("^" + ClassPrefix.Replace(".", "\\.") + "\\.[0-9][0-9]*$") +
                    "},allows_input=true,pin=true,float=true,no_initial_focus=true}); local popup=hl.window_rule({name=" + JsonSerializer.Serialize(ruleKey + "_popup") + ",match={class=" + JsonSerializer.Serialize("^" + ClassPrefix.Replace(".", "\\.") + "Menu$") + "},float=true,pin=true,decorate=false,border_size=0,rounding=0,no_shadow=true,no_blur=true,no_anim=true,stay_focused=true}); _G[" + key + "]={passive=rule,popup=popup} end";
                string reply = await desktop.SurfaceRequest("/eval " + code, cancellation).ConfigureAwait(false);
                if (reply.Trim() != "ok") throw new InvalidOperationException("Cannot prepare passive Capri surfaces: " + reply.Trim());
                ruleRevision = revision;
            }
            finally { gate.Release(); }
        }

        internal static async Task Shutdown()
        {
            var desktop = HyprlandDesktop.Current;
            if (desktop == null) return;
            Volatile.Write(ref retiring, 1);
            bool acquired = false;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await gate.WaitAsync(timeout.Token).ConfigureAwait(false); acquired = true;
                string key = JsonSerializer.Serialize(ruleKey);
                string reply = await desktop.SurfaceRequest("/eval local rules=_G[" + key + "]; if rules then rules.passive:set_enabled(false); rules.popup:set_enabled(false); _G[" + key + "]=nil end", timeout.Token).ConfigureAwait(false);
                if (reply.Trim() != "ok") throw new InvalidOperationException("Passive rule cleanup failed: " + reply.Trim());
            }
            catch (Exception error) { Program.Crashed(error); }
            finally { if (acquired) gate.Release(); desktop.Dispose(); }
        }

        internal static async Task Prepare(Window window, string nativeClass, CancellationToken cancellation)
        {
            await EnsureRule(cancellation);
            cancellation.ThrowIfCancellationRequested();
            X11Properties.SetNetWmWindowType(window, X11NetWmWindowType.Utility);
            NativeX11.Configure(window, true);
        }
    }
}

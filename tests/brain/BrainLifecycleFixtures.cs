using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using CapriDesktop;

// Uses real Pet transitions on a private X server, with delayed IO at the provider boundary.
internal sealed class BrainLifecycleFixtures : Application
{
    private static string directory;
    internal static int Start(string path)
    {
        string display = Environment.GetEnvironmentVariable("DISPLAY");
        if (Environment.GetEnvironmentVariable("CAPRI_BRAIN_TEST_XVFB") != "1" || display == null || !int.TryParse(display.TrimStart(':'), out int number) || number < 192 || number > 219)
            throw new InvalidOperationException("Lifecycle checks require an explicitly allocated private Xvfb display :192 through :219.");
        directory = Path.GetFullPath(path); Directory.CreateDirectory(directory);
        Program.Smoke = directory;
        return AppBuilder.Configure<BrainLifecycleFixtures>().UsePlatformDetect().StartWithClassicDesktopLifetime(Array.Empty<string>(), ShutdownMode.OnExplicitShutdown);
    }

    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        Dispatcher.UIThread.Post(async () =>
        {
            var desktop = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime;
            try { await Run(desktop); Console.WriteLine("Lifecycle and selection checks passed."); desktop.Shutdown(0); }
            catch (Exception error) { Console.Error.WriteLine(error); desktop.Shutdown(1); }
        });
    }

    private static Pet MakePet(IClassicDesktopStyleApplicationLifetime desktop, BrainConnections services = null)
    {
        // These fixtures exercise lifecycle/provider ownership, not X11 window discovery.
        // Use the shipped BareDesk fallback to avoid its unrelated global X11 error-handler limitation.
        string display = Environment.GetEnvironmentVariable("DISPLAY");
        Environment.SetEnvironmentVariable("DISPLAY", null);
        try { return new Pet(desktop, services); }
        finally { Environment.SetEnvironmentVariable("DISPLAY", display); }
    }

    private static T Field<T>(Pet pet, string name) => (T)typeof(Pet).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(pet);
    private static void Close(Pet pet)
    {
        pet.DisconnectCodex();
        typeof(Pet).GetMethod("DisposeSpeech", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(pet, null);
        pet.Talk.Dismiss(); pet.Talk.Bubble.Close(); pet.Talk.Pill.Close(); pet.Desk.Dispose(); pet.Close();
    }

    private sealed class DelayedProvider : IBrainProvider
    {
        internal readonly TaskCompletionSource<bool> Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<bool> Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal string Purpose;
        public string Identity => "delayed-fixture";
        public int Generation { get; private set; }
        public void Cancel() => Generation++;
        public string Complete(string model, List<ChatMessage> messages, string tools, List<KeyValuePair<string, ToolArguments>> calls, int limit, Action<string> streamed, string purpose = "chat")
        {
            Purpose = purpose; Started.TrySetResult(true); Release.Task.GetAwaiter().GetResult();
            // Deliberately misbehave after cancellation: the shared Brain boundary must reject all of this.
            streamed?.Invoke("late text");
            calls.Add(new("open_app", new ToolArguments { Name = "Fixture" }));
            return "late text";
        }
    }

    private static async Task Run(IClassicDesktopStyleApplicationLifetime desktop)
    {
        int reads = 0;
        foreach (string provider in new[] { "codex", "ollama", "unknown" })
            BrainChecks.Check(Pet.RestoreSavedCloud(provider, "fixture-cloud", () => { reads++; return "sk-fixture"; }) == null && reads == 0, provider + " startup does not read optional API credentials or activate cloud");
        foreach (string provider in new string[] { null, "", "cloud" })
            BrainChecks.Check(Pet.RestoreSavedCloud(provider, "fixture-cloud", () => { reads++; return "sk-fixture"; })?.Model == "fixture-cloud", "legacy or explicit cloud startup preserves saved selection");
        BrainChecks.Check(reads == 3 && Pet.RestoreSavedCloud("cloud", null, () => { reads++; return "sk-fixture"; }) == null && reads == 3, "startup credential read is lazy and requires a saved cloud model");

        var transitions = new Dictionary<string, Action<Pet>>
        {
            ["hide"] = p => p.HideAway(), ["quiet"] = p => p.ToggleShush(), ["nap"] = p => p.Nap(true),
            ["doze"] = p => p.Doze(100000), ["lock"] = p => p.ObserveBrainLock(true), ["stop"] = p => p.StopTalking()
        };
        foreach (var transition in transitions)
        foreach (bool routing in new[] { false, true })
        {
            var pet = MakePet(desktop); var provider = new DelayedProvider();
            try
            {
                pet.Brain.Provider = provider; pet.Brain.Model = "fixture-model";
                pet.Saved.SleepAfter = 120;
                int streamed = 0, effects = 0;
                pet.Brain.Act = (name, args) => { effects++; return "unexpected effect"; };
                int asking = Field<int>(pet, "asking"); var audio = Field<CancellationTokenSource>(pet, "speechJob").Token;
                string request = routing ? "Please open the imaginary fixture application." : "Tell me a short story about a comet.";
                if (routing) BrainChecks.Check(Hands.Wants(request) && !Hands.Heard(request, out _, out _), "action regression reaches provider instead of a direct command");
                var pending = Task.Run(() => pet.Brain.Ask(request, text => streamed++));
                await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                BrainChecks.Check(provider.Purpose == (routing ? "actions" : "chat"), "delayed request entered the expected " + provider.Purpose + " lane");
                transition.Value(pet);
                BrainChecks.Check(provider.Generation > 0 && Field<int>(pet, "asking") > asking && audio.IsCancellationRequested, transition.Key + " revokes ordinary inference, queued reply and speech generation");
                int cancelled = provider.Generation;
                if (transition.Key != "quiet" && transition.Key != "stop")
                {
                    transition.Value(pet);
                    if (transition.Key == "lock") pet.ObserveBrainLock(null);
                    BrainChecks.Check(provider.Generation == cancelled, transition.Key + " repeated state does not repeatedly cancel");
                }
                provider.Release.TrySetResult(true);
                try { await pending; BrainChecks.Check(false, transition.Key + " suppresses delayed completion"); }
                catch (Exception error) when (error is OperationCanceledException || error is System.Net.WebException web && web.Status == System.Net.WebExceptionStatus.RequestCanceled) { BrainChecks.Check(streamed == 0 && effects == 0, transition.Key + " drops delayed text, action proposals and final completion"); }
            }
            finally { provider.Release.TrySetResult(true); Close(pet); }
        }

        foreach (string change in new[] { "ollama", "disconnect", "hide", "stop" })
        using (var server = await BrainProtocolFixtures.Server.Open())
        {
            int factories = 0;
            var services = new BrainConnections { Codex = _ => { factories++; return new CodexBrain(Path.Combine(directory, "queued-" + change), _ => server.Pipe.Rpc); }, LocalModels = () => new[] { "fixture-local" } };
            var pet = MakePet(desktop, services);
            try
            {
                server.Mode = "hold";
                Task first = pet.ConnectCodexAsync(); await server.Held.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Task second = pet.ConnectCodexAsync();
                if (change == "ollama") await pet.SelectBrainProviderAsync("ollama");
                else if (change == "disconnect") pet.DisconnectCodex();
                else if (change == "hide") pet.HideAway();
                else pet.StopTalking();
                await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
                BrainChecks.Check(factories == 1 && (change == "ollama" ? pet.BrainProvider == "ollama" && pet.Brain.Model == "fixture-local" : !pet.BrainConnected), change + " invalidates queued connects before they publish selection or start another child");
            }
            finally { Close(pet); }
        }

        using (var server = await BrainProtocolFixtures.Server.Open())
        {
            var pet = MakePet(desktop, new BrainConnections { Codex = _ => new CodexBrain(Path.Combine(directory, "warm-events"), _ => server.Pipe.Rpc) });
            try
            {
                await pet.ConnectCodexAsync(); pet.StopTalking(); int stopped = Field<int>(pet, "asking");
                await server.Send(new { method = "account/updated", @params = new { authMode = "chatgpt" } });
                await server.Pipe.Rpc.Request("barrier", new { }, CancellationToken.None);
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                BrainChecks.Check(!pet.BrainConnected && pet.Brain.Model == null && Field<int>(pet, "asking") > stopped, "warm connection still revokes text and native work on account change after Stop");
            }
            finally { Close(pet); }
        }

        foreach (string selected in new[] { "ollama", "cloud" })
        {
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var services = new BrainConnections
            {
                LocalModels = () => { entered.TrySetResult(true); release.Task.GetAwaiter().GetResult(); return new[] { "stale-model" }; },
                CloudModels = _ => { entered.TrySetResult(true); release.Task.GetAwaiter().GetResult(); return new() { "stale-model" }; }
            };
            var pet = MakePet(desktop, services);
            try
            {
                pet.Saved.BrainProvider = selected; pet.Brain.Model = "before";
                if (selected == "cloud") pet.Brain.Remote = new Cloud("sk-fixture", "before");
                Task refresh = pet.RefreshBrainModelsAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await pet.SelectBrainModelAsync("latest-model"); release.TrySetResult(true); await refresh;
                BrainChecks.Check(pet.Brain.Model == "latest-model" && pet.BrainModelChoices.Length == 0, selected + " model selection invalidates pending catalog publication");
            }
            finally { release.TrySetResult(true); Close(pet); }
        }

        foreach (bool fails in new[] { false, true })
        {
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var services = new BrainConnections { LocalModels = () => { entered.TrySetResult(true); release.Task.GetAwaiter().GetResult(); if (fails) throw new IOException("stale catalog failure"); return new[] { "stale-local" }; } };
            var pet = MakePet(desktop, services);
            try
            {
                pet.Saved.BrainProvider = "ollama";
                Task refresh = pet.RefreshBrainModelsAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await pet.SelectBrainProviderAsync("cloud"); string status = pet.BrainConnectionStatus;
                release.TrySetResult(true); await refresh;
                BrainChecks.Check(pet.BrainProvider == "cloud" && pet.Brain.Model == null && pet.BrainConnectionStatus == status && pet.BrainModelChoices.Length == 0, "stale local catalog " + (fails ? "failure" : "success") + " cannot overwrite latest provider");
            }
            finally { release.TrySetResult(true); Close(pet); }
        }
        {
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var services = new BrainConnections { LocalModels = () => new[] { "fixture-local" }, CloudModels = key => { entered.TrySetResult(true); release.Task.GetAwaiter().GetResult(); return new() { "stale-cloud" }; } };
            var pet = MakePet(desktop, services);
            try
            {
                pet.Saved.BrainProvider = "cloud"; pet.Brain.Remote = new Cloud("sk-fixture", "fixture-cloud"); pet.Brain.Model = "fixture-cloud";
                Task refresh = pet.RefreshBrainModelsAsync(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await pet.SelectBrainProviderAsync("ollama");
                release.TrySetResult(true); await refresh;
                BrainChecks.Check(pet.BrainProvider == "ollama" && pet.Brain.Remote == null && pet.Brain.Model == "fixture-local" && pet.BrainModelChoices[0] == "fixture-local", "stale cloud catalog cannot promote saved cloud over selected local provider");
            }
            finally { release.TrySetResult(true); Close(pet); }
        }
    }
}

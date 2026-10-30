using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;

namespace CapriDesktop
{
    internal sealed class BrainConnections
    {
        internal Func<string, CodexBrain> Codex = path => new CodexBrain(path);
        internal Func<string[]> LocalModels = Brain.InstalledModels;
        internal Func<string, System.Collections.Generic.List<string>> CloudModels = Brain.CloudModels;
    }

    internal sealed partial class Pet
    {
        private volatile CodexBrain codex;
        private readonly BrainConnections brainConnections;
        private int brainSelection;
        private readonly SemaphoreSlim providerChange = new(1, 1);
        private string brainConnectionStatus;
        private bool brainSessionLocked;
        private string[] localBrainChoices = Array.Empty<string>();
        internal event Action BrainConnectionChanged;
        internal string BrainProvider => string.IsNullOrEmpty(Saved.BrainProvider) ? (Brain.Remote != null ? "cloud" : "ollama") : Saved.BrainProvider;
        internal string BrainConnectionStatus => brainConnectionStatus ?? (BrainProvider == "codex" ? codex?.Status ?? "Disconnected" : Brain.Model == null ? "No model connected" : "Selected · " + Brain.Model);
        internal bool BrainConnected => BrainProvider == "codex" ? codex?.Connected == true : Brain.Model != null;
        internal string[] BrainModelChoices => BrainProvider == "codex" ? codex?.Models.Select(m => m.Id).ToArray() ?? Array.Empty<string>() : BrainProvider == "cloud" ? cloudModels?.ToArray() ?? Array.Empty<string>() : localBrainChoices;

        private void RevokeBrainWork(bool invalidateSelection = false)
        {
            if (invalidateSelection) Interlocked.Increment(ref brainSelection);
            ++asking; ComputerJobs.Stop(); Brain.Cancel(); StopChat(); StopSpeech();
        }

        internal void ObserveBrainLock(bool? locked)
        {
            if (!locked.HasValue || brainSessionLocked == locked.Value) return;
            brainSessionLocked = locked.Value;
            if (!locked.Value) return;
            RevokeBrainWork(true);
            Talk.Dismiss();
        }

        private void ProviderChanged()
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(ProviderChanged); return; }
            BrainConnectionChanged?.Invoke();
        }

        private int BeginBrainSelection()
        {
            int ticket = Interlocked.Increment(ref brainSelection);
            RevokeBrainWork();
            return ticket;
        }

        private bool OwnsBrainSelection(int ticket) => ticket == Volatile.Read(ref brainSelection) && !quitting;

        internal static Cloud RestoreSavedCloud(string provider, string model, Func<string> readKey)
        {
            if ((!string.IsNullOrEmpty(provider) && provider != "cloud") || string.IsNullOrEmpty(model)) return null;
            string key = readKey();
            return key != null && Cloud.KindOf(key) != null ? new Cloud(key, model) : null;
        }

        internal Task ConnectCodexAsync() => ConnectCodexAsync(BeginBrainSelection(), Saved.CodexModel);

        private async Task ConnectCodexAsync(int ticket, string preferred)
        {
            await providerChange.WaitAsync();
            try
            {
                if (!OwnsBrainSelection(ticket)) return;
                Brain.Provider = null;
                Saved.BrainProvider = "codex";
                Brain.Model = null;
                Brain.Remote = null;
                brainConnectionStatus = "Connecting to Codex / ChatGPT plan…";
                ProviderChanged();
                codex?.Dispose();
                codex = brainConnections.Codex(Path.Combine(folder, "codex"));
                CodexBrain connection = codex;
                connection.Changed += () => Dispatcher.UIThread.Post(() =>
                {
                    if (codex != connection) return;
                    brainConnectionStatus = connection.Status; ProviderChanged();
                });
                connection.Revoked += () =>
                {
                    if (codex != connection) return;
                    Brain.Cancel();
                    ComputerJobs.Interrupt();
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (codex != connection) return;
                        RevokeBrainWork();
                        Brain.Model = null; ProviderChanged();
                    });
                };
                Brain.Provider = connection;
                Save();
                try
                {
                    await Task.Run(() => connection.Connect(preferred));
                    if (!OwnsBrainSelection(ticket) || codex != connection) { connection.Dispose(); return; }
                    Brain.Provider = connection;
                    Brain.Model = connection.Model;
                    Saved.CodexModel = connection.Model;
                    brainConnectionStatus = connection.Status;
                    Save();
                }
                catch (Exception error)
                {
                    if (!OwnsBrainSelection(ticket) || codex != connection) { connection.Dispose(); return; }
                    brainConnectionStatus = error.Message;
                    connection.Dispose(); codex = null; Brain.Provider = null;
                }
                ProviderChanged();
            }
            finally { providerChange.Release(); }
        }

        internal Task PickCodexModelAsync(string model)
        {
            if (BrainProvider != "codex" || string.IsNullOrWhiteSpace(model) || !BrainModelChoices.Contains(model) || Brain.Model == model && BrainConnected) return Task.CompletedTask;
            int ticket = BeginBrainSelection();
            Saved.CodexModel = model;
            Save();
            return ConnectCodexAsync(ticket, model);
        }

        internal void DisconnectCodex() => DisconnectCodex(BeginBrainSelection());

        private void DisconnectCodex(int ticket)
        {
            if (!OwnsBrainSelection(ticket)) return;
            Brain.Provider = null;
            codex?.Dispose(); codex = null;
            if (BrainProvider == "codex") Brain.Model = null;
            brainConnectionStatus = "Disconnected from Capri; your Codex sign-in is unchanged.";
            ProviderChanged();
        }

        internal async Task SelectBrainProviderAsync(string provider)
        {
            if (provider != "codex" && provider != "ollama" && provider != "cloud") throw new ArgumentException("Unknown brain provider.", nameof(provider));
            int ticket = BeginBrainSelection();
            if (provider == "codex") { await ConnectCodexAsync(ticket, Saved.CodexModel); return; }
            DisconnectCodex(ticket);
            Brain.Remote = null;
            Brain.Model = null;
            Saved.BrainProvider = provider;
            brainConnectionStatus = null;
            if (provider == "cloud")
            {
                Cloud restored = RestoreSavedCloud(provider, Saved.CloudModel, () => ReadSecret(BrainKeyFile));
                if (restored != null) { Brain.Remote = restored; Brain.Model = restored.Model; }
                else brainConnectionStatus = "Connect an optional provider API key to use Cloud.";
            }
            Save();
            await RefreshBrainModelsAsync(ticket, provider);
        }

        internal Task RefreshBrainModelsAsync()
        {
            if (BrainProvider == "codex") return ConnectCodexAsync();
            return RefreshBrainModelsAsync(Volatile.Read(ref brainSelection), BrainProvider);
        }

        private async Task RefreshBrainModelsAsync(int ticket, string provider)
        {
            try
            {
                if (provider == "cloud")
                {
                    Cloud remote = Brain.Remote;
                    if (remote != null)
                    {
                        var found = await Task.Run(() => brainConnections.CloudModels(remote.Key));
                        if (!OwnsBrainSelection(ticket) || BrainProvider != provider || Brain.Remote != remote) return;
                        cloudModels = found;
                        brainConnectionStatus = null;
                    }
                }
                else
                {
                    string[] found = await Task.Run(brainConnections.LocalModels);
                    if (!OwnsBrainSelection(ticket) || BrainProvider != provider) return;
                    localBrainChoices = found;
                    if (Brain.Model == null)
                    {
                        Brain.Model = Brain.PickModel(found, Saved.Model, null);
                        if (Brain.Model != null) { Saved.Model = Brain.Model; Save(); }
                    }
                    brainConnectionStatus = null;
                }
            }
            catch (Exception error)
            {
                if (!OwnsBrainSelection(ticket) || BrainProvider != provider) return;
                brainConnectionStatus = error.Message;
            }
            ProviderChanged();
        }

        internal async Task SelectBrainModelAsync(string model)
        {
            if (BrainProvider == "codex") await PickCodexModelAsync(model);
            else if (BrainProvider == "cloud") PickCloud(model);
            else PickBrain(model);
            ProviderChanged();
        }

        private void ShowCodexBrains()
        {
            brainMenu.Items.Clear();
            brainMenu.Items.Add(new MenuItem { Header = BrainConnectionStatus, IsEnabled = false });
            foreach (string name in BrainModelChoices)
            {
                string model = name;
                MenuItem item = new MenuItem { Header = model, ToggleType = MenuItemToggleType.Radio, IsChecked = Brain.Model == model };
                item.Click += async delegate { await PickCodexModelAsync(model); };
                brainMenu.Items.Add(item);
            }
            var retry = new MenuItem { Header = "Connect / Retry Codex" };
            retry.Click += async delegate { await ConnectCodexAsync(); };
            brainMenu.Items.Add(retry);
            var local = new MenuItem { Header = "Use local Ollama" };
            local.Click += async delegate { await SelectBrainProviderAsync("ollama"); };
            brainMenu.Items.Add(local);
        }
    }
}

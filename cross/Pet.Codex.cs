using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;

namespace CapriDesktop
{
    internal sealed partial class Pet
    {
        private volatile CodexBrain codex;
        private readonly SemaphoreSlim providerChange = new(1, 1);
        private string brainConnectionStatus;
        private string[] localBrainChoices = Array.Empty<string>();
        internal event Action BrainConnectionChanged;
        internal string BrainProvider => Saved.BrainProvider ?? (Brain.Remote != null ? "cloud" : "ollama");
        internal string BrainConnectionStatus => brainConnectionStatus ?? (BrainProvider == "codex" ? codex?.Status ?? "Disconnected" : Brain.Model == null ? "No model connected" : "Selected · " + Brain.Model);
        internal bool BrainConnected => BrainProvider == "codex" ? codex?.Connected == true : Brain.Model != null;
        internal string[] BrainModelChoices => BrainProvider == "codex" ? codex?.Models.Select(m => m.Id).ToArray() ?? Array.Empty<string>() : BrainProvider == "cloud" ? cloudModels?.ToArray() ?? Array.Empty<string>() : localBrainChoices;

        private void RevokeBrainWork()
        {
            ++asking; ComputerJobs.Stop(); Brain.Cancel(); StopChat(); StopSpeech();
        }

        private void ProviderChanged()
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(ProviderChanged); return; }
            BrainConnectionChanged?.Invoke();
        }

        internal async Task ConnectCodexAsync()
        {
            await providerChange.WaitAsync();
            try
            {
                RevokeBrainWork();
                Brain.Provider = null;
                Saved.BrainProvider = "codex";
                Brain.Model = null;
                Brain.Remote = null;
                brainConnectionStatus = "Connecting to Codex / ChatGPT plan…";
                ProviderChanged();
                codex?.Dispose();
                codex = new CodexBrain(Path.Combine(folder, "codex"));
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
                    string preferred = Saved.CodexModel;
                    await Task.Run(() => connection.Connect(preferred));
                    if (quitting || codex != connection) return;
                    Brain.Provider = connection;
                    Brain.Model = connection.Model;
                    Saved.CodexModel = connection.Model;
                    brainConnectionStatus = connection.Status;
                    Save();
                }
                catch (Exception error)
                {
                    brainConnectionStatus = error.Message;
                    if (codex == connection) { connection.Dispose(); codex = null; Brain.Provider = null; }
                }
                ProviderChanged();
            }
            finally { providerChange.Release(); }
        }

        internal async Task PickCodexModelAsync(string model)
        {
            if (string.IsNullOrWhiteSpace(model) || !BrainModelChoices.Contains(model)) return;
            if (Brain.Model == model && BrainConnected) return;
            Saved.CodexModel = model;
            Save();
            await ConnectCodexAsync();
        }

        internal void DisconnectCodex()
        {
            RevokeBrainWork();
            Brain.Provider = null;
            codex?.Dispose(); codex = null;
            if (BrainProvider == "codex") Brain.Model = null;
            brainConnectionStatus = "Disconnected from Capri; your Codex sign-in is unchanged.";
            ProviderChanged();
        }

        internal async Task SelectBrainProviderAsync(string provider)
        {
            if (provider == "codex") { await ConnectCodexAsync(); return; }
            if (provider != "ollama" && provider != "cloud") throw new ArgumentException("Unknown brain provider.", nameof(provider));
            DisconnectCodex();
            Brain.Remote = null;
            Brain.Model = null;
            Saved.BrainProvider = provider;
            brainConnectionStatus = null;
            if (provider == "cloud")
            {
                string key = ReadSecret(BrainKeyFile);
                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(Saved.CloudModel))
                {
                    Brain.Remote = new Cloud(key, Saved.CloudModel); Brain.Model = Saved.CloudModel;
                }
                else brainConnectionStatus = "Connect an optional provider API key to use Cloud.";
            }
            Save();
            await RefreshBrainModelsAsync();
            ProviderChanged();
        }

        internal async Task RefreshBrainModelsAsync()
        {
            if (BrainProvider == "codex") { await ConnectCodexAsync(); return; }
            try
            {
                if (BrainProvider == "cloud")
                {
                    string key = ReadSecret(BrainKeyFile);
                    if (!string.IsNullOrEmpty(key)) cloudModels = await Task.Run(() => Brain.CloudModels(key));
                }
                else
                {
                    localBrainChoices = await Task.Run(Brain.InstalledModels);
                    if (Brain.Model == null)
                    {
                        Brain.Model = Brain.PickModel(localBrainChoices, Saved.Model, null);
                        if (Brain.Model != null) { Saved.Model = Brain.Model; Save(); }
                    }
                }
                brainConnectionStatus = null;
            }
            catch (Exception error) { brainConnectionStatus = error.Message; }
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

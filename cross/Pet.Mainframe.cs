using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;

namespace CapriDesktop
{
    internal sealed partial class Pet
    {
        private Mainframe mainframe;
        internal event Action SettingsChanged;
        internal static readonly int[] MainframeSizes = { 0, 100, 125, 150, 175, 200 };
        internal int MainframeSize { get { return Array.IndexOf(MainframeSizes, Saved.MainframeSize) >= 0 ? Saved.MainframeSize : 0; } }
        internal void SetMainframeSize(int size)
        {
            if (Array.IndexOf(MainframeSizes, size) < 0) throw new ArgumentOutOfRangeException(nameof(size));
            Saved.MainframeSize = size; Save();
        }
        internal void OpenMainframe()
        {
            if (mainframe == null)
            {
                mainframe = new Mainframe(this);
                mainframe.Closed += (_, _) => mainframe = null;
                mainframe.Show();
            }
            else { mainframe.Show(); mainframe.WindowState = Avalonia.Controls.WindowState.Normal; mainframe.BringToActiveOutput(); }
        }
        internal void CloseMainframe() { mainframe?.Close(); }
        internal void NotifySettings()
        {
            if (Dispatcher.UIThread.CheckAccess()) SettingsChanged?.Invoke();
            else Dispatcher.UIThread.Post(() => SettingsChanged?.Invoke());
        }
        internal string MainframeVoiceState { get { return speechProgress ?? voiceProblem ?? (Saved.Silent ? "Silent · text only" : UsesEleven ? "ElevenLabs · existing cloud voice" : Saved.LocalStyle == VoiceServer.Style ? "Your voice model/server" : Saved.LocalStyle == QwenVoice.Style ? "HD local voice" : localVoice.Installed ? "Prebuilt voice installed · local" : "Set up her free voice"); } }
        internal string MainframeEarsState { get { return localEars.Ready ? "Local ears installed" : "Set up her free voice and ears to use TALK"; } }
        internal int MainframeVoiceMode { get { return Saved.Silent ? 0 : UsesEleven ? 3 : Saved.LocalStyle == VoiceServer.Style || Saved.LocalStyle == QwenVoice.Style ? 2 : 1; } }
        internal void MainframeSelectVoiceMode(int mode)
        {
            if (mode == 0) SetSpeak(false);
            else if (mode == 1) SelectLocal(Saved.FreeStyle ?? "Her own");
            else if (mode == 2)
            {
                if (Saved.VoiceUrl == null) throw new InvalidOperationException("Set up your local model or server first.");
                SelectLocal(VoiceServer.Style);
            }
            else if (mode == 3)
            {
                if (!voice.Ready) throw new InvalidOperationException("Connect your existing ElevenLabs voice in Cloud voice setup first.");
                StopSpeech(); Saved.LocalStyle = null; SetSpeak(true); Save(); RefreshVoice();
            }
            NotifySettings();
        }
        internal void MainframeSelectVoice(string style) { SelectLocal(style); NotifySettings(); }
        internal void MainframeSelectMicrophone(string id) { ChooseMicrophone(id); NotifySettings(); }
        internal void MainframeInstallSpeech(bool ears) { InstallSpeech(ears); NotifySettings(); }
        internal void MainframeCancelSpeechInstall() { localVoice.Cancel = localEars.Cancel = true; }
        internal void MainframeCheckUpdate() { CheckUpdate(); }
        internal string MainframeUpdateState { get { return updateChecking ? "Checking for updates…" : updatePage != null ? "An update is available" : "Check the upstream release page"; } }
        internal void MainframeOpenUpdates() { Browse(updatePage ?? ReleasesPage); }
        internal bool MainframeHdInstalled { get { return hdVoice.Installed; } }
        internal void MainframePickHd() { if (hdVoice.Installed) SelectLocal(QwenVoice.Style); }
        internal bool MainframeInstallingSpeech { get { return installingSpeech; } }
        internal void MainframeSaveCloudVoice(string key, string id)
        {
            if (!string.IsNullOrWhiteSpace(key)) SaveVoiceKey(key);
            if (!string.IsNullOrWhiteSpace(id)) ChooseVoice(id.Trim());
        }
        internal async Task MainframeSaveVoiceModel(string path, string speaker)
        {
            path = ModelFile.Clean(path);
            string model = await Task.Run(() =>
            {
                string result = path;
                if (File.Exists(result)) result = result.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(result) : ModelFile.Unpack(result);
                if (ModelFile.Kind(result) == null) throw new VoiceException(ModelFile.Missing);
                return result;
            });
            StopSpeech();
            lock (customVoice.Changing)
            {
                Saved.VoiceUrl = customVoice.Url = model; Saved.VoiceApi = customVoice.Api = VoiceServer.Files;
                Saved.VoiceModel = customVoice.Model = ""; Saved.VoiceSpeaker = customVoice.Speaker = speaker;
            }
            SelectLocal(VoiceServer.Style); NotifySettings();
        }
        internal void MainframeSaveVoiceServer(string address, string api, string model, string speaker, string key)
        {
            if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out Uri uri) || !uri.IsLoopback || (uri.Scheme != "http" && uri.Scheme != "https"))
                throw new ArgumentException("Use a localhost HTTP or HTTPS address. Hosted speech services are not enabled by this local-server setup.");
            StopSpeech();
            lock (customVoice.Changing)
            {
                Saved.VoiceUrl = customVoice.Url = uri.AbsoluteUri.TrimEnd('/'); Saved.VoiceApi = customVoice.Api = VoiceServer.Known(api);
                Saved.VoiceModel = customVoice.Model = model.Trim(); Saved.VoiceSpeaker = customVoice.Speaker = speaker.Trim();
                if (!string.IsNullOrEmpty(key))
                {
                    WriteSecret(Path.Combine(Program.Smoke ?? folder, "local-voice-key"), key, "local voice key");
                    customVoice.Key = key;
                }
            }
            SelectLocal(VoiceServer.Style); NotifySettings();
        }
    }
}

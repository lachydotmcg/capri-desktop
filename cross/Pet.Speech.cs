using System;
using System.IO;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Threading;

namespace CapriDesktop
{
    internal sealed partial class Pet
    {
        private LocalVoice localVoice;
        private LocalEars localEars;
        private QwenVoice hdVoice;
        private VoiceServer customVoice;
        private readonly Ears microphone = new Ears();
        private readonly VoiceActivity activity = new VoiceActivity();
        private CancellationTokenSource speechJob = new CancellationTokenSource();
        private bool chatting, transcribing, installingSpeech;
        private int chatTicket;
        private volatile bool makingSpeech;
        private volatile string speechProgress;

        private void InitializeSpeech()
        {
            string data = Program.Smoke ?? folder;
            localVoice = new LocalVoice(data) { Style = LocalVoice.Find(Saved.FreeStyle) };
            localEars = new LocalEars(data);
            hdVoice = new QwenVoice(data);
            customVoice = new VoiceServer { Url = Saved.VoiceUrl ?? VoiceServer.Address, Api = VoiceServer.Known(Saved.VoiceApi), Model = Saved.VoiceModel, Speaker = Saved.VoiceSpeaker, Key = ReadSecret(Path.Combine(data, "local-voice-key")) };
            ModelFile.Engine = LocalVoice.Folder(data);
            ModelFile.Shelf = Path.Combine(data, "voice", "models");
            microphone.Chosen = Saved.Mic;
            microphone.Heard += samples =>
            {
                int session = Volatile.Read(ref chatTicket);
                Dispatcher.UIThread.Post(() => { if (session == chatTicket) GatherSpeech(samples); });
            };
            microphone.Level += level => Dispatcher.UIThread.Post(() => Talk.Pill.SetLevel(ChatHeld ? 0 : level));
            microphone.Failed += problem =>
            {
                int session = Volatile.Read(ref chatTicket);
                Dispatcher.UIThread.Post(() => { if (session != chatTicket) return; StopChat(); Talk.Answer("(voice)", problem); });
            };
            if (Program.Smoke == null)
            {
                Voice.Playback = OperatingSystem.IsLinux() ? SpeechAudio.Play : Limbs.Speak;
                Voice.Silence = OperatingSystem.IsLinux() ? SpeechAudio.Stop : Limbs.Hush;
                if (OperatingSystem.IsLinux()) SpeechAudio.Failed += AudioFailed;
            }
        }
        private void AudioFailed(string problem) { Dispatcher.UIThread.Post(() => { voiceProblem = problem; Talk.Answer("(voice)", problem); }); }
        internal bool VoiceChatting { get { return chatting; } }
        private bool ChatHeld { get { return !chatting || transcribing || microphone.Muted || Talk.ThinkingNow || makingSpeech || SpeechAudio.MicrophoneHeld; } }
        private bool UsesEleven { get { return voice.Ready && Saved.LocalStyle == null; } }
        private byte[] MakeSpeech(string text, CancellationToken cancellation)
        {
            using (var work = new SpeechWork(cancellation))
            {
                SpeechWork.Check();
                byte[] made;
                if (UsesEleven) made = VoiceFx.Styled(voice.Speak(text), text);
                else if (Saved.LocalStyle == VoiceServer.Style || Saved.LocalStyle == QwenVoice.Style)
                {
                    Func<string, byte[]> fallback = localVoice.Installed ? words => { SpeechWork.Check(); return localVoice.Speak(Voice.Sayable(words)); } : null;
                    Action<string> trouble = problem => { SpeechWork.Check(); Dispatcher.UIThread.Post(() => { voiceProblem = problem; RefreshVoice(); }); };
                    made = Saved.LocalStyle == VoiceServer.Style ? customVoice.Or(fallback, trouble)(text) : hdVoice.Or(fallback, trouble)(text);
                }
                else made = localVoice.Speak(Voice.Sayable(text));
                SpeechWork.Check();
                return made;
            }
        }
        private void StopSpeech()
        {
            speechJob.Cancel(); speechJob.Dispose(); speechJob = new CancellationTokenSource();
            makingSpeech = false;
            activity.Reset();
            voice.Stop();
            if (hdVoice != null) hdVoice.Stop();
        }
        internal void StopTalking()
        {
            RevokeBrainWork(true);
            localVoice.Cancel = localEars.Cancel = true;
            Talk.Answer("(stop)", "Stopped.");
        }
        private void StopChat()
        {
            int ended = ++chatTicket;
            chatting = false; transcribing = false;
            microphone.Stop(); microphone.Muted = false;
            activity.Reset(); Hearing.Own = null;
            if (Talk != null) Talk.Pill.SetChat(false, false);
            ThreadPool.QueueUserWorkItem(_ => { if (ended == Volatile.Read(ref chatTicket)) localEars.Dispose(); });
        }
        private void DisposeSpeech()
        {
            StopChat(); StopSpeech(); localVoice.Cancel = true; localEars.Cancel = true;
            SpeechAudio.Failed -= AudioFailed;
            // Model disposal waits for in-flight native calls without blocking Avalonia's UI thread.
            ThreadPool.QueueUserWorkItem(_ => { localEars.Dispose(); localVoice.Dispose(); hdVoice.Dispose(); });
        }
        internal void ToggleTalk()
        {
            if (!OperatingSystem.IsLinux()) { Talk.Answer("(voice)", "Voice chat isn't available on this platform yet. Type to me for now, choom."); return; }
            if (chatting) { StopChat(); StopSpeech(); return; }
            if (Brain.LanguageCode(Brain.Language) != "en")
            {
                Talk.Answer("(voice)", "Her local Parakeet ears hear English. Other microphone languages aren't available on Linux yet.");
                return;
            }
            if (!localEars.Ready)
            {
                Talk.Answer("(voice)", "Set up her free voice and ears under Settings > Voice. They hear and speak locally, with no key.");
                return;
            }
            int ticket = ++chatTicket;
            chatting = true; activity.Reset();
            Talk.Typer.Hide();
            Talk.Answer("(voice)", "One sec, opening my ears.");
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string problem = null;
                try { localEars.Warm(); } catch (Exception error) { problem = Voice.Short(error.Message); }
                Dispatcher.UIThread.Post(async () =>
                {
                    if (!chatting || ticket != chatTicket) return;
                    if (problem == null)
                    {
                        try { Hearing.Own = localEars; await microphone.Start(); }
                        catch (Exception error) { problem = Voice.Short(error.Message); }
                    }
                    if (!chatting || ticket != chatTicket) return;
                    if (problem != null) { StopChat(); Talk.Answer("(voice)", problem); return; }
                    Talk.Pill.SetChat(true, false);
                    Talk.Answer("(voice)", "Free voice chat on. Talk and I'll type back.");
                });
            });
        }
        internal void ToggleMicrophone()
        {
            if (!chatting) return;
            microphone.Muted = !microphone.Muted; activity.Reset();
            chatTicket++; transcribing = false;
            Talk.Pill.SetChat(true, microphone.Muted);
        }
        internal void TypedConversation() { StopChat(); }
        private void GatherSpeech(short[] samples)
        {
            short[] utterance = activity.Feed(samples, ChatHeld);
            if (activity.Capped) Talk.Answer("(voice)", "I heard 15 seconds without a pause. Try a shorter line or lower your mic volume.");
            if (utterance == null) return;
            transcribing = true;
            int session = chatTicket, request = asking;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string text = null, problem = null;
                try { text = Hearing.Text(utterance); } catch (Exception error) { problem = Voice.Short(error.Message); }
                Dispatcher.UIThread.Post(() =>
                {
                    if (session != chatTicket || !chatting) return;
                    transcribing = false;
                    if (request != asking || microphone.Muted) return;
                    if (problem != null) { Talk.Answer("(voice)", problem); return; }
                    if (!string.IsNullOrWhiteSpace(text)) Hear(text);
                    else Talk.Answer("(voice)", "I couldn't make out any words. Try closer to the mic.");
                });
            });
        }
        private void ChooseMicrophone(string id)
        {
            bool restart = chatting;
            StopChat(); Saved.Mic = id; microphone.Chosen = id; Save();
            if (restart) ToggleTalk();
        }
        private void InstallSpeech(bool ears)
        {
            if (installingSpeech) return;
            installingSpeech = true; localVoice.Cancel = localEars.Cancel = false;
            speechProgress = "Downloading her free voice";
            Talk.Answer("(voice)", ears ? "Downloading her free voice and ears. Settings > Voice shows how far they've got." : "Downloading her free voice. Settings > Voice shows how far it's got.");
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string problem = null;
                Action<string, long, long> progress = (text, done, total) => speechProgress = text + " " + Engine.Amount(done) + " / " + Engine.Amount(total);
                try
                {
                    if (!localVoice.Installed) localVoice.Install(progress);
                    if (ears && !localEars.Installed) localEars.Install(progress);
                }
                catch (Exception error) { problem = Voice.Short(error.Message); }
                Dispatcher.UIThread.Post(() => { installingSpeech = false; speechProgress = null; Talk.Answer("(voice)", problem ?? (ears ? "Her voice and ears are in. Press TALK." : "Her free voice is in.")); RefreshVoice(); });
            });
        }
        private void SelectLocal(string style)
        {
            StopSpeech(); Saved.LocalStyle = style; Saved.Silent = false;
            if (style != VoiceServer.Style && style != QwenVoice.Style) { Saved.FreeStyle = style; localVoice.Style = LocalVoice.Find(style); }
            Save(); RefreshVoice();
        }
        private void AddLocalVoiceMenu()
        {
            var free = new MenuItem { Header = "Free voice" };
            foreach (FreeStyle style in LocalVoice.Styles)
            {
                FreeStyle chosen = style;
                var item = new MenuItem { Header = style.Name, ToggleType = MenuItemToggleType.Radio, GroupName = "local-voice", IsChecked = !UsesEleven && (Saved.LocalStyle ?? "Her own") == style.Name };
                item.Click += (_, _) => SelectLocal(chosen.Name); free.Items.Add(item);
            }
            var setup = new MenuItem { Header = "Set up her free voice", IsEnabled = !installingSpeech && !localVoice.Installed };
            setup.Click += (_, _) => InstallSpeech(false); free.Items.Add(setup); voiceMenu.Items.Add(free);
            var hd = new MenuItem { Header = "HD", IsEnabled = hdVoice.Installed, ToggleType = MenuItemToggleType.Radio, IsChecked = Saved.LocalStyle == QwenVoice.Style };
            hd.Click += (_, _) => SelectLocal(QwenVoice.Style); voiceMenu.Items.Add(hd);
            if (!hdVoice.Installed) voiceMenu.Items.Add(new MenuItem { Header = "HD downloads are not published for Linux yet", IsEnabled = false });
            var custom = new MenuItem { Header = VoiceServer.Menu };
            var file = new MenuItem { Header = "Voice model file · on your PC..." };
            file.Click += (_, _) => Talk.AskFor("(voice model)", "Paste the path to your sherpa-onnx model folder, .onnx, .zip or .tar.bz2.", ChooseModel);
            custom.Items.Add(file);
            var server = new MenuItem { Header = "Any OpenAI-style server..." };
            server.Click += (_, _) => ConfigureVoiceServer(VoiceServer.OpenAi);
            custom.Items.Add(server);
            var fish = new MenuItem { Header = "Fish Speech · on your PC..." };
            fish.Click += (_, _) => ConfigureVoiceServer(VoiceServer.Fish); custom.Items.Add(fish);
            var key = new MenuItem { Header = "Optional server key..." };
            key.Click += (_, _) => Talk.AskKey("(voice server)", "Paste your local voice server key. It stays on this computer.", value =>
            {
                StopSpeech();
                lock (customVoice.Changing) customVoice.Key = value;
                WriteSecret(Path.Combine(Program.Smoke ?? folder, "local-voice-key"), value, "local voice key");
            });
            custom.Items.Add(key); voiceMenu.Items.Add(custom);
            var setupEars = new MenuItem { Header = "Set up her free voice and ears", IsEnabled = !installingSpeech && !localEars.Ready };
            setupEars.Click += (_, _) => InstallSpeech(true); voiceMenu.Items.Add(setupEars);
            var mic = new MenuItem { Header = "Microphone" };
            mic.SubmenuOpened += async (_, _) =>
            {
                mic.Items.Clear();
                var system = new MenuItem { Header = "Default microphone", ToggleType = MenuItemToggleType.Radio, IsChecked = Saved.Mic == null };
                system.Click += (_, _) => ChooseMicrophone(null); mic.Items.Add(system);
                try
                {
                    foreach (Mic device in await System.Threading.Tasks.Task.Run(Ears.List))
                    {
                        Mic chosen = device;
                        var item = new MenuItem { Header = device.Name, ToggleType = MenuItemToggleType.Radio, IsChecked = Saved.Mic == device.Id };
                        item.Click += (_, _) => ChooseMicrophone(chosen.Id); mic.Items.Add(item);
                    }
                }
                catch (Exception error) { mic.Items.Add(new MenuItem { Header = Voice.Short(error.Message), IsEnabled = false }); }
            };
            voiceMenu.Items.Add(mic);
            var stop = new MenuItem { Header = "Stop" };
            stop.Click += (_, _) => StopTalking(); voiceMenu.Items.Add(stop);
            var quiet = new MenuItem { Header = "Speak", ToggleType = MenuItemToggleType.CheckBox, IsChecked = !Saved.Silent };
            quiet.Click += (_, _) => SetSpeak(Saved.Silent); voiceMenu.Items.Add(quiet);
            if (speechProgress != null) voiceMenu.Items.Add(new MenuItem { Header = speechProgress, IsEnabled = false });
            voiceMenu.Items.Add(new Separator());
        }
        private void ConfigureVoiceServer(string api)
        {
            Talk.AskFor("(voice server)", api == VoiceServer.Fish ? "Your local Fish Speech server address:" : "Your local voice server address, including /v1:", value =>
            {
                Uri uri;
                if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out uri) || !uri.IsLoopback || (uri.Scheme != "http" && uri.Scheme != "https")) { Talk.Answer("(voice)", "Enter a localhost HTTP or HTTPS address."); return; }
                Talk.AskFor("(voice model)", "Model name:", model => Talk.AskFor("(voice)", api == VoiceServer.Fish ? "Reference ID:" : "Speaker name:", speaker =>
                {
                    StopSpeech();
                    lock (customVoice.Changing)
                    {
                        Saved.VoiceUrl = customVoice.Url = uri.AbsoluteUri.TrimEnd('/'); Saved.VoiceApi = customVoice.Api = api;
                        Saved.VoiceModel = customVoice.Model = model; Saved.VoiceSpeaker = customVoice.Speaker = speaker;
                    }
                    SelectLocal(VoiceServer.Style);
                }));
            });
        }
        private void ChooseModel(string path)
        {
            path = ModelFile.Clean(path);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string model = path, problem = null;
                try
                {
                    if (File.Exists(path)) model = path.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(path) : ModelFile.Unpack(path);
                    if (ModelFile.Kind(model) == null) throw new VoiceException(ModelFile.Missing);
                }
                catch (Exception error) { problem = Voice.Short(error.Message); }
                Dispatcher.UIThread.Post(() =>
                {
                    if (problem != null) { Talk.Answer("(voice)", problem); return; }
                    Talk.AskFor("(voice)", "Speaker number or owner-picked reference clip path; use 0 for the first speaker:", speaker =>
                    {
                        StopSpeech();
                        lock (customVoice.Changing)
                        {
                            Saved.VoiceUrl = customVoice.Url = model; Saved.VoiceApi = customVoice.Api = VoiceServer.Files;
                            Saved.VoiceModel = customVoice.Model = ""; Saved.VoiceSpeaker = customVoice.Speaker = speaker;
                        }
                        SelectLocal(VoiceServer.Style);
                    });
                });
            });
        }
    }
}

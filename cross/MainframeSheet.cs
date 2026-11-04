using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace CapriDesktop
{
    internal sealed class MainframeSheet : MainframeWindow
    {
        private readonly Pet pet;
        private readonly List<Action> readers = new List<Action>();
        private readonly DispatcherTimer status = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        private bool syncing, closed;
        private readonly double wide;
        internal readonly Dictionary<string, Control> Rows = new Dictionary<string, Control>();

        internal MainframeSheet(Pet owner, string title, string kind) : base(title)
        {
            pet = owner;
            wide = kind == "layers" ? 760 : kind == "vault" ? 520 : 380;
            ShowInTaskbar = false;
            Put(Page, new NeonCaption { Text = title, Size = 20, Bold = true, Display = true }, 24, 15, 288, 29);
            var close = Button("", (int)wide - 50, 20, 26, 22, Close); close.Glyph = 2;
            double height = kind == "server" ? Server() : kind == "layers" ? Layers() : kind == "vault" ? Vault() : SecurityRows();
            Opened += (_, _) => { Fit(wide, height, Pet.FrameSize(pet.Saved.MainframeSize) == 0 ? 1 : pet.Saved.MainframeSize / 100.0); Sync(); status.Start(); };
            pet.SettingsChanged += Sync;
            status.Tick += (_, _) => Sync();
            Closed += (_, _) => { closed = true; status.Stop(); pet.SettingsChanged -= Sync; };
        }
        private NeonButton Button(string text, int x, int y, int width, int height, Action action)
        {
            var button = Put(Page, new NeonButton { Text = text }, x, y, width, height, text);
            Rows[text] = button;
            button.Click += (_, _) => { action(); Sync(); };
            return button;
        }
        private void Caption(string text, int x, int y, int width, int height, IBrush ink = null, double size = MainframeTheme.Size)
        {
            Put(Page, new NeonCaption { Text = text, Ink = ink ?? MainframeTheme.Rose, Size = size }, x, y, width, height);
        }
        private NeonEntry Input(string name, string cue, int x, int y, int width, Func<string> read)
        {
            Caption(name, x, y, width, 18);
            var entry = Put(Page, new NeonEntry(cue), x, y + 20, width, 28, name);
            Rows[name] = entry;
            string last = null;
            readers.Add(() => { string value = read(); if (!entry.Box.IsKeyboardFocusWithin && last != value) { entry.Box.Text = value; last = value; } });
            return entry;
        }
        private double Server()
        {
            Caption("OpenAI speech API", 28, 52, 328, 18, MainframeTheme.Faint, 34.0 / 3);
            var address = Input("Address", LocalSpeaker.Default, 28, 84, 328, () => pet.MainframeLocalAddress);
            var model = Input("Model", "Optional", 28, 140, 160, () => pet.MainframeLocalModel);
            var voice = Input("Voice", "Optional", 196, 140, 160, () => pet.MainframeLocalVoice);
            Caption("Format", 28, 196, 146, 18);
            var format = Put(Page, new NeonPicker(), 184, 193, 172, 22, "Format");
            format.Options(new[] { "WAV", "PCM 24 kHz" }); Rows["Format"] = format;
            format.Changed += i => { if (!syncing) pet.SetLocalVoice("format", i == 1 ? LocalSpeaker.Pcm : "wav"); };
            readers.Add(() => format.Selected = pet.MainframeLocalFormat == LocalSpeaker.Pcm ? 1 : 0);
            var note = Put(Page, new NeonCaption { Ink = MainframeTheme.Faint, Ellipsis = true }, 28, 264, 328, 20);
            Func<bool> save = () =>
            {
                string url = address.Box.Text ?? "", modelName = model.Box.Text ?? "", voiceName = voice.Box.Text ?? "";
                try { if (url.Trim().Length > 0 && url.Trim() != "-") LocalSpeaker.Endpoint(url.Trim()); }
                catch (VoiceException error) { note.Text = error.Message; note.InvalidateVisual(); return false; }
                pet.SetLocalVoice("url", url); pet.SetLocalVoice("model", modelName); pet.SetLocalVoice("voice", voiceName);
                note.Text = "Saved."; note.InvalidateVisual();
                return true;
            };
            Button("Test voice", 28, 224, 84, 28, () => { if (save() && Program.Smoke == null) pet.HearStyle(); });
            Button("Save", 120, 224, 150, 28, () => save()).Accent = MainframeTheme.Cyan;
            Button("Cancel", 278, 224, 78, 28, Close);
            foreach (var entry in new[] { address, model, voice }) entry.Box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { save(); e.Handled = true; } };
            return 300;
        }
        private NeonSwitch Toggle(string name, int y, Func<bool> read, Action<bool> change)
        {
            var toggle = Put(Page, new NeonSwitch { Text = name, Switch = 68 }, 28, y, 328, 22, name); Rows[name] = toggle;
            toggle.IsCheckedChanged += (_, _) => { if (!syncing) change(toggle.Checked); };
            readers.Add(() => toggle.Checked = read()); return toggle;
        }
        private double Layers()
        {
            Caption("Her voice, and two copies of it mixed in under her, each with its own pitch and delay.", 28, 52, 704, 18, MainframeTheme.Faint, 34.0 / 3);
            var enabled = Toggle("Voice layers", 76, () => VoiceFx.Layers.On, on => { pet.SetLayers(VoiceFx.Layers.Turned(on)); if (Program.Smoke == null) pet.HearStyle(); }); enabled.Width = 260; enabled.Switch = 120;
            Caption("Off: she sounds as she always has. On: every voice she has, ElevenLabs too.", 28, 102, 704, 18, MainframeTheme.Faint, 34.0 / 3);
            Button("Test voice", 524, 76, 100, 22, () => { if (Program.Smoke == null) pet.HearStyle(); });
            Button("Defaults", 632, 76, 100, 22, () => pet.SetLayers(new VoiceFx.Layering(VoiceFx.Layers.On, VoiceFx.Layering.HigherPitch, VoiceFx.Layering.Behind, VoiceFx.Layering.LowerPitch, VoiceFx.Layering.Behind)));
            Put(Page, new NeonHead { Text = "MAIN" }, 14, 131, 732, 24);
            LayerBar("Main pitch", "Pitch", Pet.PitchNames, 28, 163, 120, 300, () => VoiceFx.Pitch + VoiceFx.MostPitch, i => pet.SetPitch(i - VoiceFx.MostPitch));
            Caption("Her voice itself. Its pitch is Settings > VOICE > Pitch, and they move together.", 28, 192, 704, 18, MainframeTheme.Faint, 34.0 / 3);
            var pitches = new string[(int)(VoiceFx.Layering.MostPitch * 4) + 1]; var delays = new string[VoiceFx.Layering.MostDelay / 5 + 1];
            for (int i = 0; i < pitches.Length; i++) pitches[i] = VoiceFx.Layering.PitchName(i / 2.0 - VoiceFx.Layering.MostPitch);
            for (int i = 0; i < delays.Length; i++) delays[i] = VoiceFx.Layering.DelayName(i * 5);
            for (int layer = 1; layer <= 2; layer++)
            {
                int which = layer;
                int x = layer == 1 ? 28 : 392;
                Put(Page, new NeonHead { Text = "LAYER " + layer }, x - 14, 223, 352, 24);
                LayerBar("Layer " + layer + " pitch", "Pitch", pitches, x, 255, x + 84, 244, () => (int)Math.Round((VoiceFx.Layers.PitchOf(which) + VoiceFx.Layering.MostPitch) * 2), i => pet.SetLayers(VoiceFx.Layers.With(which, i / 2.0 - VoiceFx.Layering.MostPitch, VoiceFx.Layers.DelayOf(which)))).TickEvery = 4;
                LayerBar("Layer " + layer + " delay", "Delay", delays, x, 283, x + 84, 244, () => VoiceFx.Layers.DelayOf(which) / 5, i => pet.SetLayers(VoiceFx.Layers.With(which, VoiceFx.Layers.PitchOf(which), i * 5))).TickEvery = 10;
                Caption(layer == 1 ? "Out of the box a little higher, just behind her." : "Out of the box lower, just behind her.", x, 314, 328, 18, MainframeTheme.Faint, 34.0 / 3);
            }
            return 356;
        }
        private NeonSlider LayerBar(string name, string label, string[] labels, int x, int y, int at, int width, Func<int> read, Action<int> change)
        {
            Caption(label, x, y + 3, at - x - 10, 18);
            var slider = Put(Page, new NeonSlider(name, labels), at, y, width, 22, name); Rows[name] = slider;
            slider.Changed += i => { if (!syncing) change(i); };
            slider.Released += () => { if (!syncing && Program.Smoke == null) pet.HearStyle(); };
            readers.Add(() => slider.Value = read()); return slider;
        }
        private double SecurityRows()
        {
            Caption("Preset", 28, 68, 146, 18);
            var preset = Put(Page, new NeonPicker(), 184, 65, 172, 22, "Preset"); Rows["Preset"] = preset;
            preset.Options(new[] { Security.Presets[0], Security.Presets[1], Security.Presets[2], Security.Presets[3] });
            preset.Changed += i => { if (!syncing) Security.Pick(i); };
            var note = Put(Page, new NeonCaption { Ink = MainframeTheme.Faint, Ellipsis = true }, 28, 99, 328, 24);
            readers.Add(() => { preset.Selected = Math.Min(Security.Preset, 3); if (Security.Preset == Security.Custom) preset.Text = "Custom"; note.Text = Security.Short; note.InvalidateVisual(); });
            int y = 142;
            for (int i = 0; i < Security.Count; i++)
            {
                if (!Security.Has(i)) continue;
                int at = i;
                var toggle = Toggle(Security.Names[i], y, () => Security.On(at), on => Security.Set(at, on));
                ToolTip.SetTip(toggle, Security.Lines[i]);
                y += 34;
            }
            if (pet.MainframeInputSetup) { Button("Set up mouse and keyboard", 28, y + 8, 328, 28, pet.MainframeSetupInput); y += 44; }
            return y + 24;
        }
        private double Vault()
        {
            var vaults = pet.VaultList();
            var paths = new List<string> { "" };
            var names = new List<string> { "Her own notes (no vault)" };
            foreach (var found in vaults) { names.Add("Vault: " + found.Key); paths.Add(Path.Combine(found.Value, Pet.VaultNote)); }
            var picker = Put(Page, new NeonPicker { Ellipsis = true }, 28, 62, 352, 24, "Obsidian vault"); Rows["Obsidian vault"] = picker;
            var note = Put(Page, new TextBlock { Foreground = MainframeTheme.Faint, FontSize = 34.0 / 3, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 3 }, 28, 100, 464, 56, "Vault note"); Rows["Vault note"] = note;
            string last = null;
            bool here = pet.VaultHere;
            Action read = () =>
            {
                string current = Pet.NotesIn(pet.Vault) ?? "";
                int at = paths.FindIndex(path => string.Equals(path, current, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));
                if (at < 0) { paths.Add(current); names.Add("Note: " + Path.GetFileName(current)); at = paths.Count - 1; }
                picker.Options(names.ToArray()); picker.Selected = at;
                if (last == pet.Vault && here == pet.VaultHere) return;
                last = pet.Vault; here = pet.VaultHere;
                note.Text = pet.Vault.Length > 0 ? pet.VaultHere ? "She saves her memories to " + Path.GetFileName(Pet.NotesIn(pet.Vault)) + "." : "That vault wasn't there when she started, so she's using her own notes." : vaults.Count > 0 ? "Pick one of your Obsidian vaults and her memories go in a Capri Memories note there." : "No Obsidian vaults found on this PC. Open one in Obsidian first, or Browse to a note.";
            };
            Action<string> use = path => { string said = pet.SetVault(path); read(); note.Text = said; };
            picker.Changed += at => { if (!syncing) use(paths[at]); };
            readers.Add(read);
            var browse = Button("Browse", 388, 62, 104, 24, () => { });
            browse.Click += async (_, _) =>
            {
                if (!StorageProvider.CanSave) { note.Text = "Couldn't open the note picker."; return; }
                try
                {
                    string current = Pet.NotesIn(pet.Vault);
                    if (current == null && vaults.Count > 0) current = Path.Combine(vaults[0].Value, Pet.VaultNote);
                    var start = current != null && Directory.Exists(Path.GetDirectoryName(current)) ? await StorageProvider.TryGetFolderFromPathAsync(Path.GetDirectoryName(current)) : null;
                    var picked = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {
                        Title = "Pick the note her memories go in", SuggestedFileName = current == null ? Pet.VaultNote : Path.GetFileName(current),
                        SuggestedStartLocation = start, DefaultExtension = "md", ShowOverwritePrompt = false,
                        FileTypeChoices = new[] { new FilePickerFileType("Notes") { Patterns = new[] { "*.md" } } } });
                    string path = picked?.TryGetLocalPath();
                    if (!closed && path != null) use(path);
                }
                catch (Exception) { if (!closed) note.Text = "Couldn't open the note picker."; }
            };
            Button("Close", 388, 164, 104, 26, Close);
            return 214;
        }
        private void Sync()
        {
            if (syncing || closed) return;
            syncing = true;
            try { foreach (Action read in readers) read(); }
            finally { syncing = false; }
        }
    }
}

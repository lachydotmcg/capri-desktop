using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace CapriDesktop
{
    internal sealed partial class Pet
    {
        internal Mods ModList { get; private set; }
        internal static volatile string SkinFolder;
        internal static volatile string[] SkinAccessories = Array.Empty<string>();
        internal static volatile bool LegacyArt;

        private void StartMods()
        {
            LegacyArt = Saved.LegacyStand;
            ModList = new Mods(Program.Smoke ?? folder);
            Chores.Skills = ModList.SkillsFor;
            ModList.SkinChanged = Reskinned;
            Reskinned();
            StartCodeMods();
        }

        private void Reskinned()
        {
            foreach (Clip shown in Clips) shown.Reload();
            blinkWith = null;
            herPointer?.ReloadArt();
            if (body == null) return;
            // Swaps can invalidate both a60-cell frame and a blink/mouth overlay before the next paint tick.
            body.Shown = clip;
            if (body.Shown != null) body.Cell %= body.Shown.Count;
            body.Mouth = idle.Old ? mouth : idleMouth;
            body.Talking = clip == idle ? Talk.MouthCell(Now) : -1;
            body.InvalidateVisual();
        }

        internal static bool HasArt(string name)
        {
            return typeof(Pet).Assembly.GetManifestResourceInfo(name) != null;
        }

        internal static PixelSize ArtSize(string name)
        {
            using (Stream stream = typeof(Pet).Assembly.GetManifestResourceStream(name))
            using (Avalonia.Media.Imaging.Bitmap art = new Avalonia.Media.Imaging.Bitmap(stream)) return art.PixelSize;
        }

        internal static Avalonia.Media.Imaging.Bitmap ReadArt(string name)
        {
            string skin = SkinFolder;
            // A skin's idle_legacy.png for Legacy stand comes first, then its own strip of that name.
            string stand = Mods.StandArt(name, LegacyArt);
            if (skin != null && name.StartsWith("Capri.Sprite.", StringComparison.Ordinal))
                foreach (string wanted in stand != name ? new[] { stand, name } : new[] { name })
                {
                    string file = Path.Combine(skin, wanted.Substring("Capri.Sprite.".Length));
                    try
                    {
                        if (File.Exists(file)) using (MemoryStream stream = new MemoryStream(File.ReadAllBytes(file))) return Finished(Dressed(stream, name));
                    }
                    catch (Exception error)
                    {
                        if (!(error is IOException || error is UnauthorizedAccessException || error is ArgumentException || error is InvalidOperationException || error is NotSupportedException)) throw;
                        Brain.Log("mods", -1, "Skin picture " + wanted + ": " + error.Message);
                    }
                }
            using (Stream stream = typeof(Pet).Assembly.GetManifestResourceStream(Mods.StandArt(name, LegacyArt)))
            {
                if (stream == null) throw new FileNotFoundException("Capri's animation is missing: " + name);
                return Finished(Dressed(stream, name));
            }
        }

        // Each layer above her outfit that has this strip is drawn over it in order, before her colours go on: an accessory as it
        // is, and a whole skin only where it changes her own art, so its ears or hat go on over the outfit like on Windows.
        private static Stream Dressed(Stream art, string name)
        {
            string[] layers = SkinAccessories;
            if (layers.Length == 0 || !name.StartsWith("Capri.Sprite.", StringComparison.Ordinal)) return art;
            List<byte[]> found = new List<byte[]>();
            foreach (string folder in layers)
            {
                bool whole = folder.StartsWith("~", StringComparison.Ordinal);
                string file = Path.Combine(folder.TrimStart('=', '~'), name.Substring("Capri.Sprite.".Length));
                try
                {
                    if (!File.Exists(file)) continue;
                    byte[] bytes = whole ? Changes(file, name) : File.ReadAllBytes(file);
                    if (bytes != null) found.Add(bytes);
                }
                catch (Exception error)
                {
                    if (!(error is IOException || error is UnauthorizedAccessException || error is ArgumentException)) throw;
                    Brain.Log("mods", -1, "Accessory picture " + name + ": " + error.Message);
                }
            }
            if (found.Count == 0) return art;
            using (SkiaSharp.SKBitmap bottom = SkiaSharp.SKBitmap.Decode(art))
            {
                if (bottom == null) throw new ArgumentException("That picture couldn't be read.");
                using (SkiaSharp.SKCanvas canvas = new SkiaSharp.SKCanvas(bottom))
                    foreach (byte[] bytes in found)
                        using (SkiaSharp.SKBitmap layer = SkiaSharp.SKBitmap.Decode(bytes))
                        {
                            if (layer == null || layer.Width != bottom.Width || layer.Height != bottom.Height) continue;
                            canvas.DrawBitmap(layer, 0, 0);
                        }
                using (SkiaSharp.SKImage image = SkiaSharp.SKImage.FromBitmap(bottom))
                using (SkiaSharp.SKData png = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100))
                    return new MemoryStream(png.ToArray());
            }
        }

        // What a whole skin changes from her own art for one strip, as a PNG, worked out once per strip and kept.
        private static readonly Dictionary<string, byte[]> changes = new Dictionary<string, byte[]>();

        private static byte[] Changes(string file, string name)
        {
            string key = file + "|" + File.GetLastWriteTimeUtc(file).Ticks;
            byte[] kept;
            lock (changes) if (changes.TryGetValue(key, out kept)) return kept;
            int width, height;
            int[] top = Pixels(File.ReadAllBytes(file), out width, out height);
            int[] under;
            int ownWidth, ownHeight;
            using (Stream stream = typeof(Pet).Assembly.GetManifestResourceStream(Mods.BaseArt(name, width, height)))
            {
                if (stream == null) return null;
                using (MemoryStream bytes = new MemoryStream())
                {
                    stream.CopyTo(bytes);
                    under = Pixels(bytes.ToArray(), out ownWidth, out ownHeight);
                }
            }
            if (ownWidth != width || ownHeight != height) return null;
            int[] result = SkinLayers.Changed(top, under, width, height);
            SkiaSharp.SKImageInfo info = new SkiaSharp.SKImageInfo(width, height, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Unpremul);
            using (SkiaSharp.SKBitmap made = new SkiaSharp.SKBitmap(info))
            {
                System.Runtime.InteropServices.Marshal.Copy(result, 0, made.GetPixels(), result.Length);
                using (SkiaSharp.SKImage image = SkiaSharp.SKImage.FromBitmap(made))
                using (SkiaSharp.SKData png = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100))
                    kept = png.ToArray();
            }
            lock (changes) changes[key] = kept;
            return kept;
        }

        // A picture's pixels as ARGB ints, not premultiplied, like Windows' 32bpp ARGB bitmaps.
        private static int[] Pixels(byte[] picture, out int width, out int height)
        {
            using (SkiaSharp.SKCodec codec = SkiaSharp.SKCodec.Create(new MemoryStream(picture)))
            {
                if (codec == null) throw new ArgumentException("That picture couldn't be read.");
                width = codec.Info.Width;
                height = codec.Info.Height;
                SkiaSharp.SKImageInfo info = new SkiaSharp.SKImageInfo(width, height, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Unpremul);
                using (SkiaSharp.SKBitmap art = new SkiaSharp.SKBitmap(info))
                {
                    if (codec.GetPixels(info, art.GetPixels()) != SkiaSharp.SKCodecResult.Success) throw new ArgumentException("That picture couldn't be read.");
                    int[] pixels = new int[width * height];
                    System.Runtime.InteropServices.Marshal.Copy(art.GetPixels(), pixels, 0, pixels.Length);
                    return pixels;
                }
            }
        }

        internal static Avalonia.Media.Imaging.Bitmap ReadLeftArt(string primary, PixelSize expected)
        {
            const string lead = "Capri.Sprite.";
            string left = primary.Replace(".png", "_flipped.png"), skin = SkinFolder;
            // A skin's own strip must never turn into bundled art when she faces the other way.
            Avalonia.Media.Imaging.Bitmap made;
            if (skin != null && File.Exists(Path.Combine(skin, primary.Substring(lead.Length))))
            {
                string file = Path.Combine(skin, left.Substring(lead.Length));
                if (!File.Exists(file)) return null;
                using (MemoryStream bytes = new MemoryStream(File.ReadAllBytes(file))) made = Finished(bytes);
            }
            else made = ReadArt(left);
            if (made.PixelSize == expected) return made;
            made.Dispose();
            return null;
        }

        private static Avalonia.Media.Imaging.Bitmap Finished(Stream stream)
        {
            if (!ArtColors.Active) return new Avalonia.Media.Imaging.Bitmap(stream);
            using (SkiaSharp.SKCodec codec = SkiaSharp.SKCodec.Create(stream))
            {
                if (codec == null) throw new ArgumentException("That picture couldn't be read.");
                SkiaSharp.SKImageInfo info = new SkiaSharp.SKImageInfo(codec.Info.Width, codec.Info.Height, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Unpremul);
                using (SkiaSharp.SKBitmap art = new SkiaSharp.SKBitmap(info))
                {
                    if (codec.GetPixels(info, art.GetPixels()) != SkiaSharp.SKCodecResult.Success) throw new ArgumentException("That picture couldn't be read.");
                    int[] pixels = new int[info.Width * info.Height];
                    System.Runtime.InteropServices.Marshal.Copy(art.GetPixels(), pixels, 0, pixels.Length);
                    ArtColors.Apply(pixels);
                    System.Runtime.InteropServices.Marshal.Copy(pixels, 0, art.GetPixels(), pixels.Length);
                    using (SkiaSharp.SKImage image = SkiaSharp.SKImage.FromBitmap(art))
                    using (SkiaSharp.SKData png = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100))
                    using (MemoryStream made = new MemoryStream(png.ToArray())) return new Avalonia.Media.Imaging.Bitmap(made);
                }
            }
        }

        private Mainframe mainframe;
        private MenuItem hideItem;
        private string[] frameModels = Array.Empty<string>();
        private int brainRequests;
        private int brainPending;
        private int cloudRequests;
        private int localRequests;
        private string frameProblem;
        internal event Action SettingsChanged;
        internal Mainframe Frame { get { return mainframe; } }
        internal int MainframeTier { get { return Math.Clamp(Saved.BrainTier ?? (Brain.Remote == null ? 0 : Cloud.MediumKind(Brain.Remote.Kind) ? 1 : 2), 0, 2); } }
        internal string MainframeBrainKey { get { return Brain.Remote?.Key; } }
        internal bool MainframeConnecting { get { return brainPending > 0; } }
        internal string MainframeProblem { get { return frameProblem; } }
        internal string MainframeVoiceKey { get { return voice.Key; } }
        internal string MainframeVoiceName { get { return VoiceName(); } }
        internal string MainframeVoiceProblem { get { return voiceProblem; } }
        internal bool MainframePulling { get { return pulling; } }
        internal string MainframeProgress { get { return pulling ? Pulling : null; } }
        internal bool MainframeInputSetup { get { return OperatingSystem.IsLinux() && !ComputerJobs.Supported && !fingers; } }
        internal int MainframeVoiceMode { get { return Saved.Silent || Speakers.Known(Saved.VoiceProvider) == Speakers.Off ? 0 : Speakers.Known(Saved.VoiceProvider) == Speakers.Local ? 2 : Speakers.Known(Saved.VoiceProvider) == Speakers.Owned ? 1 : 3; } }
        internal string MainframeUpdate { get { return updateChecking ? "Checking for updates..." : updateNote ?? UpdateNews ?? (updatePage == null ? "" : "An update is available."); } }
        internal string MainframeLocalAddress { get { return localVoice.Url ?? ""; } }
        internal string MainframeLocalModel { get { return localVoice.Model ?? ""; } }
        internal string MainframeLocalVoice { get { return localVoice.Speaker ?? ""; } }
        internal string MainframeLocalFormat { get { return LocalSpeaker.Known(localVoice.Format); } }
        internal string[] MainframeModels { get { return Brain.Remote != null ? cloudModels?.ToArray() ?? Array.Empty<string>() : MainframeTier == 0 ? frameModels : Array.Empty<string>(); } }
        internal string[] MainframeVoiceIds
        {
            get { var ids = new List<string>(); if (voices != null) foreach (VoiceEntry entry in voices.Voices) ids.Add(entry.Id); return ids.ToArray(); }
        }
        internal string[] MainframeVoices
        {
            get { var names = new List<string>(); if (voices != null) foreach (VoiceEntry entry in voices.Voices) names.Add(entry.Name ?? entry.Id); return names.ToArray(); }
        }
        internal string[] MainframeSpeechIds { get { var ids = new List<string> { "" }; if (voices != null) foreach (VoiceModel model in voices.Models) ids.Add(model.Id); return ids.ToArray(); } }
        internal string[] MainframeSpeechModels { get { var names = new List<string> { "Default model" }; if (voices != null) foreach (VoiceModel model in voices.Models) names.Add(model.Name ?? model.Id); return names.ToArray(); } }
        internal void SetSpeechModel(string model) { voice.Model = Saved.SpeechModel = string.IsNullOrEmpty(model) ? null : model; Save(); }

        internal static int FrameSize(int size) { return Array.IndexOf(new[] { 0, 100, 125, 150, 175, 200 }, size) >= 0 ? size : 0; }
        internal void SetMainframeSize(int size) { Saved.MainframeSize = FrameSize(size); Save(); }
        internal void SetMainframeColours(int look)
        {
            if (Saved.MainframeColours == MainframeTheme.ClampLook(look)) return;
            Saved.MainframeColours = MainframeTheme.ClampLook(look);
            Save();
            MainframeTheme.UseLook(Saved.MainframeColours);
            MainframeTheme.Repaint(mainframe);
        }
        internal void SetAdvancedSettings(bool on) { Saved.AdvancedSettings = on; Save(); }
        internal void SetMemorySize(int size) { Brain.MemorySize = Array.IndexOf(Brain.MemorySizes, size) >= 0 ? size : 0; Saved.MemorySize = Brain.MemorySize; Save(); }
        internal void SetReplyLength(int index) { Brain.ReplyLength = Math.Clamp(index, 0, Brain.ReplySizes.Length - 1); Saved.ReplyLength = Brain.ReplyLength; Save(); }
        internal void SetThinking(int level) { if (Brain.SetThinking(level)) { Saved.ThinkingPicks = Brain.Picks; Save(); } }
        // What's wrong with it, or null once it's saved for the brain she's on.
        internal string SetThinkingExtra(string typed)
        {
            string problem = Brain.SetThinkingExtra(typed);
            if (problem != null) return problem;
            Saved.ThinkingPicks = Brain.Picks;
            Save();
            NotifySettings();
            return null;
        }
        internal void NotifySettings()
        {
            if (Dispatcher.UIThread.CheckAccess()) SettingsChanged?.Invoke();
            else Dispatcher.UIThread.Post(() => SettingsChanged?.Invoke());
        }
        internal void OpenMainframe()
        {
            if (mainframe == null)
            {
                mainframe = new Mainframe(this);
                mainframe.Closed += (_, _) => mainframe = null;
                mainframe.Show();
                if (Program.Smoke == null) { RefreshBrains(); if (voices == null) ListVoices(); }
            }
            else { mainframe.Show(); mainframe.WindowState = WindowState.Normal; mainframe.Activate(); }
        }
        internal void CloseMainframe() { mainframe?.Shut(); }
        private string MainframeTool(string page)
        {
            if (!Dispatcher.UIThread.CheckAccess()) return Dispatcher.UIThread.Invoke(() => MainframeTool(page));
            if (page == "personality" || page == "files" || page == "notes")
            {
                OpenFiles(page == "notes" ? 1 : 0);
                return "Opened Capri file editor.";
            }
            OpenMainframe();
            mainframe.OpenPage(page);
            return "Opened Capri Mainframe.";
        }
        private string MoveMainframe(string which)
        {
            OpenMainframe();
            var all = Screens.All;
            var from = mainframe.Screens.ScreenFromWindow(mainframe) ?? ScreenHere();
            int at = PickScreen(all, from, which);
            if (at < 0) return all.Count < 2 ? "There's no other screen to go to." : "There's no screen like that to go to.";
            var area = all[at].WorkingArea;
            double scale = all[at].Scaling;
            mainframe.Position = new PixelPoint(area.X + Math.Max(0, (area.Width - (int)(mainframe.Width * scale)) / 2), area.Y + Math.Max(0, (area.Height - (int)(mainframe.Height * scale)) / 2));
            return "Moved Capri Mainframe.";
        }
        internal void MainframeRefresh()
        {
            frameProblem = null;
            if (Brain.Remote != null) { cloudRequests++; cloudListing = false; cloudModels = null; }
            RefreshBrains();
        }
        internal void MainframeUpdateCheck() { CheckUpdate(true); }
        internal void MainframeSetupInput() { if (HyprlandDesktop.Current != null) { Talk.Answer("(input)", ComputerJobs.Available ? "Native compositor input is ready." : "The native compositor input helper is unavailable. Check the Linux package and compositor protocol support; no global input setup was applied."); return; } CopyFingersSetup(); }
        internal void MainframePickModel(string model)
        {
            if (string.IsNullOrWhiteSpace(model)) return;
            if (Brain.Provider != null)
            {
                if (model == Saved.PlanModel || Array.IndexOf(frameModels, model) < 0) return;
                Saved.PlanModel = model; SelectPlan(); Save(); NotifySettings(); return;
            }
            if (MainframeTier > 0 && Brain.Remote == null) return;
            if (Brain.Remote != null) PickCloud(model.Trim());
            else if (Brain.Server != null) PickServerModel(model.Trim());
            else PickBrain(model.Trim());
        }
        internal void MainframeVoiceModePick(int mode)
        {
            if (mode == 0) SetSpeak(false);
            else { SetVoiceProvider(mode == 1 ? Speakers.Owned : mode == 2 ? Speakers.Local : Speakers.ElevenLabs); SetSpeak(true); }
        }
        internal void MainframeForgetVoice() { ForgetVoice(); Save(); }
        internal void MainframeLoadVoices() { if (voices == null && Program.Smoke == null) ListVoices(); }
        internal void MainframeToggleHidden() { if (Away) ComeBack(); else HideAway(); }
        internal NativeMenu TrayMenu()
        {
            var menu = new NativeMenu();
            Action fill = () => { Sync(); hideItem.Header = Away ? "Show Capri" : "Hide Capri"; FillMenu(menu, ContextMenu.Items); };
            fill(); menu.NeedsUpdate += (_, _) => fill(); return menu;
        }
        private static void FillMenu(NativeMenu menu, System.Collections.IEnumerable source)
        {
            menu.Items.Clear();
            foreach (object value in source)
            {
                var row = value as MenuItem;
                if (row == null) { menu.Items.Add(new NativeMenuItemSeparator()); continue; }
                var item = new NativeMenuItem(row.Header as string ?? "") { IsEnabled = row.IsEnabled, IsChecked = row.IsChecked,
                    ToggleType = row.ToggleType == MenuItemToggleType.CheckBox ? NativeMenuItemToggleType.CheckBox : row.ToggleType == MenuItemToggleType.Radio ? NativeMenuItemToggleType.Radio : NativeMenuItemToggleType.None };
                if (row.Items.Count > 0) { item.Menu = new NativeMenu(); FillMenu(item.Menu, row.Items); }
                item.Click += (_, _) => row.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
                menu.Items.Add(item);
            }
        }

        private string TierKeyFile(int tier) { return Path.Combine(Program.Smoke ?? folder, tier == 1 ? "brain-free-key" : "brain-paid-key"); }
        private void KeepBrainTier()
        {
            if (Brain.Remote == null) return;
            int tier = MainframeTier;
            if (tier == 0) tier = Cloud.MediumKind(Brain.Remote.Kind) ? 1 : 2;
            WriteSecret(TierKeyFile(tier), Brain.Remote.Key, "brain key");
            if (tier == 1) Saved.FreeModel = Brain.Remote.Model;
            else Saved.PaidModel = Brain.Remote.Model;
        }
        private void RestoreBrainTier()
        {
            Saved.MainframeSize = FrameSize(Saved.MainframeSize);
            Saved.MainframeColours = MainframeTheme.ClampLook(Saved.MainframeColours);
            MainframeTheme.UseLook(Saved.MainframeColours);
            Saved.MemorySize = Brain.MemorySize;
            Saved.ReplyLength = Brain.ReplyLength;
            if (!Saved.BrainTier.HasValue) Saved.BrainTier = Brain.Remote == null ? 0 : Cloud.MediumKind(Brain.Remote.Kind) ? 1 : 2;
            if (MainframeTier == 0) { Brain.Remote = null; Brain.Model = Brain.Server == null ? Saved.Model : Saved.ServerModel; }
            else if (Brain.Remote == null) Brain.Model = null;
        }
        internal void SetBrainTier(int tier)
        {
            tier = Math.Clamp(tier, 0, 2);
            CancelAccountConnection();
            if (MainframeTier == tier && Brain.Provider == null) return;
            StopNativeListening();
            Brain.Provider?.Cancel();
            Brain.Provider = null;
            Saved.NativePlan = false;
            brainRequests++;
            cloudRequests++;
            frameProblem = null;
            KeepBrainTier();
            Brain.Cancel();
            Saved.BrainTier = tier;
            cloudModels = null;
            cloudListing = false;
            string key = tier == 0 ? null : ReadSecret(TierKeyFile(tier));
            string model = tier == 1 ? Saved.FreeModel : Saved.PaidModel;
            Brain.Remote = key == null || Cloud.KindOf(key) == null ? null : new Cloud(key, model);
            Brain.Model = Brain.Remote?.Model ?? (tier == 0 ? Brain.Server == null ? Saved.Model : Saved.ServerModel : null);
            Saved.CloudModel = Brain.Remote?.Model;
            WriteSecret(BrainKeyFile, key, "brain key");
            Save();
            if (Program.Smoke == null)
            {
                if (Brain.Remote != null) ListCloud();
                else if (tier == 0) { RefreshBrains(); if (Brain.Model == null) FindBrain(); }
            }
        }
        internal void MainframeForgetBrain()
        {
            int tier = MainframeTier;
            WriteSecret(TierKeyFile(tier), null, "brain key");
            ForgetBrainKey();
            Saved.BrainTier = tier;
            Brain.Model = null;
            Save();
        }
    }
}

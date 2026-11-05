using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Mainframe
    {
        internal void CheckBrainTier(int expected)
        {
            for (int i = 0; i < rungs.Length; i++)
                if (rungs[i].Checked != (i == expected)) throw new Exception("The saved brain tier did not select its own rung.");
            if (rungs[1].Text != "MEDIUM INTELLIGENCE" || rungs[1].Hint != "Free" || freeWarning.Visible != (expected == 1))
                throw new Exception("The medium tier's name, free hint or warning is wrong.");
        }

        internal void TypeBrainKey(string key) { textEntry.Box.Text = key; }
        internal void PressBrainConnect() { textConnect.PerformClick(); }
        internal void PressMovePaid() { movePaid.PerformClick(); }

        internal bool PaidKeyOffered
        {
            get { return paidNote.Visible && movePaid.Visible && paidTextKey != null && paidNote.Text == "That's a paid key: it goes under MAX INTELLIGENCE."; }
        }

        internal void CheckMediumPanel(List<string> checks, string output, string name)
        {
            ShowSettings(false);
            if (!textEntry.Box.UseSystemPasswordChar || textEntry.Box.AccessibleName.IndexOf("NVIDIA", StringComparison.Ordinal) < 0 || textEntry.Box.AccessibleName.IndexOf("Google AI Studio", StringComparison.Ordinal) < 0 || textEntry.Box.AccessibleName.IndexOf("OpenRouter", StringComparison.Ordinal) < 0)
                throw new Exception("The medium key box is unmasked or does not name all its providers.");
            foreach (int percent in new[] { 100, 150, 200 })
            {
                LayoutScale(96, 100);
                SizeMainframe(percent);
                string crowded = TextFits() ?? Overlap();
                if (!TiersFit || crowded != null) throw new Exception("The medium BRAIN panel does not fit at " + percent + "%: " + (crowded ?? TiersProblem));
                if (freeWarning.ForeColor != Look.Yellow || freeWarning.Text != "Free keys can be rate limited and slow.") throw new Exception("The free-key warning changed its words or colour.");
                if (percent == 200) continue;
                Directory.CreateDirectory(output);
                using (Bitmap panel = new Bitmap(Width, Height))
                {
                    DrawToBitmap(panel, new Rectangle(Point.Empty, Size));
                    DrawRims(panel);
                    using (Bitmap brain = panel.Clone(new Rectangle(0, 0, Width, voiceHead.Top), panel.PixelFormat))
                        brain.Save(Path.Combine(output, name + "-" + percent + ".png"), ImageFormat.Png);
                }
            }
            checks.Add("PASS " + name + " BRAIN panel: full tier names, hints, key box, warning and move offer fit at 100%, 150% and 200%; 100% and 150% previews saved");
        }
    }

    internal partial class Companion
    {
        [DataContract]
        private sealed class BrainSettings0710
        {
            [DataMember] public int Tier;
            [DataMember] public bool Tiered;
            [DataMember] public string NvidiaKey;
            [DataMember] public string NvidiaModel;
            [DataMember] public string PaidKey;
            [DataMember] public string PaidModel;
            [DataMember] public string TextKey;
            [DataMember] public string TextModel;
            [DataMember] public string Model;
            [DataMember] public bool NvidiaGlm = true;
        }

        private void SmokeMedium(List<string> checks, string output)
        {
            Preferences wasPreferences = preferences;
            Cloud wasRemote = brain.Remote;
            Engine wasLocal = brain.Local;
            string wasModel = brain.Model;
            List<string> wasModels = cloudModels;
            Func<string, string> wasThink = Think;
            bool wasFinding = finding, wasSettings = mainframe.SettingsShown;
            string wasNvidia = Brain.Nvidia, wasGoogle = Brain.Google, wasRouter = Brain.OpenRouter, wasOpenAi = Brain.OpenAi, wasAnthropic = Brain.Anthropic, wasDeepSeek = Brain.DeepSeek;
            int wasSize = preferences.MainframeSize;
            // The fit checks measure the Mainframe, and a minimized one is 0 wide, so it comes up for them and goes back down after.
            bool mainframeShownWas = mainframe.Visible;
            FormWindowState mainframeStateWas = mainframe.WindowState;
            List<TcpListener> servers = new List<TcpListener>();
            Func<string, List<string>, string> serve = delegate(string catalog, List<string> asked)
            {
                TcpListener server;
                string address = FakeServer(new[] { catalog }, asked, out server);
                servers.Add(server);
                return address;
            };
            string nvidiaCatalog = "{\"data\":[{\"id\":\"nvidia/nemotron-3.5-super-vl-120b-a12b\"},{\"id\":\"openai/gpt-oss-20b\"}]}";
            string googleCatalog = "{\"data\":[{\"id\":\"gemini-90-pro\"},{\"id\":\"gemini-89-flash-lite\"},{\"id\":\"gemini-90-flash\"},{\"id\":\"gemini-91-flash-preview\"}]}";
            string routerCatalog = "{\"data\":[{\"id\":\"paid/vision\",\"architecture\":{\"input_modalities\":[\"text\",\"image\"],\"output_modalities\":[\"text\"]},\"supported_parameters\":[\"tools\"]},{\"id\":\"a/text:free\"},{\"id\":\"z/vision:free\",\"architecture\":{\"input_modalities\":[\"text\",\"image\"],\"output_modalities\":[\"text\"]},\"supported_parameters\":[\"tools\"]}]}";
            try
            {
                Think = delegate { return "Connected."; };
                finding = true;
                mainframe.Restore();
                mainframe.ShowSettings(false);
                foreach (bool split in new[] { true, false })
                {
                    string[] keys = { "nvapi-medium-migration", "AIza-medium-migration", "sk-or-medium-migration", "sk-proj-medium-migration", null };
                    string[] models = { "meta/llama-3.2-11b-vision-instruct", "gemini-90-pro", "paid/vision", "gpt-90-mini", null };
                    for (int i = 0; i < keys.Length; i++)
                    {
                        int expected = i == 0 ? 1 : i == 4 ? 0 : 2;
                        BrainSettings0710 old = new BrainSettings0710 { Tier = expected, Tiered = split, Model = "gemma4:e4b", TextKey = keys[i] == null ? null : Voice.Protect(keys[i]), TextModel = models[i] };
                        if (split)
                        {
                            old.NvidiaKey = Voice.Protect(i == 0 ? keys[i] : "nvapi-medium-inactive");
                            old.NvidiaModel = i == 0 ? models[i] : "openai/gpt-oss-20b";
                            old.PaidKey = Voice.Protect(expected == 2 ? keys[i] : "sk-proj-medium-inactive");
                            old.PaidModel = expected == 2 ? models[i] : "gpt-90-mini";
                        }
                        using (MemoryStream json = new MemoryStream())
                        {
                            new DataContractJsonSerializer(typeof(BrainSettings0710)).WriteObject(json, old);
                            json.Position = 0;
                            preferences = (Preferences)new DataContractJsonSerializer(typeof(Preferences)).ReadObject(json);
                        }
                        SplitKeys();
                        brain.Remote = TierCloud();
                        ShowTiers();
                        mainframe.CheckBrainTier(expected);
                        if (preferences.Tier != expected || expected == 0 && (brain.Remote != null || preferences.Model != old.Model) || expected != 0 && (brain.Remote == null || brain.Remote.Key != keys[i] || brain.Remote.Model != models[i]))
                            throw new Exception("A 0.7.10 brain setup changed its tier, key, model or selected rung.");
                        Mirror();
                        SavePreferences();
                        Preferences reloaded = LoadPreferences();
                        if (reloaded.Tier != expected || Voice.Unprotect(reloaded.TextKey) != keys[i] || reloaded.TextModel != models[i]) throw new Exception("The active key mirror did not survive saving.");
                        using (MemoryStream json = new MemoryStream())
                        {
                            new DataContractJsonSerializer(typeof(Preferences)).WriteObject(json, preferences);
                            string protectedJson = Encoding.UTF8.GetString(json.ToArray());
                            foreach (string key in keys) if (key != null && protectedJson.Contains(key)) throw new Exception("Settings exposed an unprotected test key.");
                            json.Position = 0;
                            BrainSettings0710 older = (BrainSettings0710)new DataContractJsonSerializer(typeof(BrainSettings0710)).ReadObject(json);
                            if (older.Tier != expected || older.NvidiaKey != preferences.NvidiaKey || older.NvidiaModel != preferences.NvidiaModel || older.PaidKey != preferences.PaidKey || older.PaidModel != preferences.PaidModel || older.TextKey != preferences.TextKey || older.TextModel != preferences.TextModel)
                                throw new Exception("An older version could not read the saved brain slots and mirror.");
                        }
                        if (split && (preferences.NvidiaKey != old.NvidiaKey || preferences.NvidiaModel != old.NvidiaModel || preferences.PaidKey != old.PaidKey || preferences.PaidModel != old.PaidModel)) throw new Exception("Migration changed an active or inactive slot.");
                    }
                }
                checks.Add("PASS 0.7.10 migration and downgrade: NVIDIA keeps tier 1, MAX Gemini/OpenRouter/OpenAI keep tier 2 and paid models, NORMAL keeps tier 0 and its local model; selected rungs, encrypted active/inactive slots and JSON names survive saving and the older contract; unsplit legacy keys keep the same routing");

                preferences = new Preferences { Tiered = true, NvidiaGlm = true, Tier = 1, Silent = true, PaidKey = Voice.Protect("sk-proj-medium-kept"), PaidModel = "gpt-90-mini" };
                brain.Remote = null;
                brain.Local = null;
                cloudModels = null;
                ShowTiers();
                foreach (int percent in new[] { 100, 150, 200 })
                {
                    mainframe.LayoutScale(96, 100);
                    mainframe.SizeMainframe(percent);
                    string crowded = mainframe.TextFits() ?? mainframe.Overlap();
                    if (!mainframe.TiersFit || crowded != null) throw new Exception("The empty medium key cue does not fit at " + percent + "%: " + (crowded ?? mainframe.TiersProblem));
                }
                checks.Add("PASS the empty MEDIUM key box names NVIDIA, Google AI Studio and OpenRouter without clipping at 100%, 150% and 200%");
                string keptPaid = preferences.PaidKey;
                foreach (string key in new[] { "nvapi-medium-connect", "AIza-medium-connect", "AQ.medium-connect", "sk-or-medium-connect" })
                {
                    string kind = Cloud.KindOf(key);
                    List<string> asked = new List<string>();
                    string address = serve(kind == "nvidia" ? nvidiaCatalog : kind == "google" ? googleCatalog : routerCatalog, asked);
                    if (kind == "nvidia") Brain.Nvidia = address;
                    else if (kind == "google") Brain.Google = address;
                    else Brain.OpenRouter = address;
                    ShowTiers();
                    mainframe.TypeBrainKey(key);
                    mainframe.PressBrainConnect();
                    WaitVoice(delegate { return brain.Remote != null && brain.Remote.Key == key && !mainframe.Status.Contains("Checking key..."); }, "The medium key did not connect to its fake provider.");
                    mainframe.CheckBrainTier(1);
                    if (asked.Count != 1 || preferences.Tier != 1 || Voice.Unprotect(preferences.NvidiaKey) != key || preferences.TextKey != preferences.NvidiaKey || preferences.PaidKey != keptPaid || preferences.PaidModel != "gpt-90-mini" || LoadPreferences().NvidiaModel != brain.Model)
                        throw new Exception("A medium connection used the wrong slot or changed the saved MAX setup.");
                    if (mainframe.Status.Contains(Mainframe.NvidiaNote) != (kind == "nvidia")) throw new Exception("The NVIDIA slowness note appeared for the wrong key.");
                    if ((mainframe.TierNote == "Most NVIDIA models can't see your screen.") != (kind == "nvidia")) throw new Exception("The NVIDIA sight note did not follow its medium key.");
                    if (kind == "nvidia") mainframe.CheckMediumPanel(checks, output, "nvidia");
                    if (kind == "google")
                    {
                        if (brain.Model != "gemini-90-flash" || !cloudModels.Contains("gemini-90-pro")) throw new Exception("Medium Gemini did not prefer stable Flash or lost the other models.");
                        if (key.StartsWith("AIza", StringComparison.Ordinal)) mainframe.CheckMediumPanel(checks, output, "gemini");
                        mainframe.PickBrain(cloudModels.IndexOf("gemini-90-pro"));
                        if (preferences.NvidiaModel != "gemini-90-pro" || TierCloud().Model != "gemini-90-pro" || preferences.PaidModel != "gpt-90-mini") throw new Exception("A medium Gemini model choice did not stay in its own slot.");
                    }
                    if (kind == "openrouter")
                    {
                        if (brain.Model != "z/vision:free" || cloudModels.Count != 2 || Array.Exists(mainframe.BrainChoices, model => !model.EndsWith(":free", StringComparison.Ordinal))) throw new Exception("Medium OpenRouter listed a paid model or missed its seeing/tool free default.");
                        string modelWas = brain.Model;
                        if (!ChooseCustomModel("paid/vision", null).Contains("MAX INTELLIGENCE") || brain.Model != modelWas) throw new Exception("A custom model bypassed the medium free-model restriction.");
                        mainframe.PickBrain(cloudModels.IndexOf("a/text:free"));
                        if (preferences.NvidiaModel != "a/text:free" || preferences.PaidModel != "gpt-90-mini") throw new Exception("A medium OpenRouter model choice changed the MAX slot.");
                        Brain.OpenRouter = serve("{\"data\":[{\"id\":\"paid/only\"}]}", new List<string>());
                        ConnectText("sk-or-medium-no-free");
                        WaitVoice(delegate { return !mainframe.Status.Contains("Checking key..."); }, "An OpenRouter key without free models did not finish.");
                        // Mainframe's status line shows the short form of her error (Brain.Brief), so the check follows its wording.
                        if (brain.Remote.Key != key || !mainframe.Status.Contains(Brain.Brief(Brain.NoFreeModels))) throw new Exception("A paid-only OpenRouter list silently replaced a working medium key.");
                        mainframe.ShowCloud(brain.Remote, cloudModels);
                    }
                }
                checks.Add("PASS MEDIUM connects NVIDIA and both Google AI Studio key forms through the existing model-list path, prefers stable Flash while listing Pro, and saves model choices without changing MAX; OpenRouter lists only :free, prefers vision/tools, blocks paid custom models and retains a working key when no free models exist");

                string mediumBeforeFailure = preferences.NvidiaKey, paidBeforeFailure = preferences.PaidKey;
                List<string> rejected = new List<string>();
                Brain.OpenAi = serve("!401 {}", rejected);
                mainframe.TypeBrainKey("sk-proj-medium-rejected");
                mainframe.PressMovePaid();
                WaitVoice(delegate { return !mainframe.Status.Contains("Checking key..."); }, "A rejected paid-key move did not finish.");
                if (rejected.Count != 1 || preferences.Tier != 1 || preferences.NvidiaKey != mediumBeforeFailure || preferences.PaidKey != paidBeforeFailure || brain.Remote.Key != Voice.Unprotect(mediumBeforeFailure))
                    throw new Exception("A rejected paid-key move changed a working key or tier.");
                checks.Add("PASS a rejected paid-key move keeps MEDIUM active and preserves both encrypted key slots");

                foreach (string key in new[] { "sk-proj-medium-paid", "sk-ant-medium-paid", "sk-0123456789abcdef0123456789abcdef" })
                {
                    preferences.Tier = 1;
                    brain.Remote = TierCloud();
                    brain.Model = brain.Remote.Model;
                    ShowTiers();
                    mainframe.ShowCloud(brain.Remote, new List<string> { brain.Model });
                    List<string> asked = new List<string>();
                    string kind = Cloud.KindOf(key);
                    string catalog = kind == "anthropic" ? "{\"data\":[{\"id\":\"claude-haiku-5-5\"}]}" : kind == "deepseek" ? "{\"data\":[{\"id\":\"deepseek-flash\"}]}" : "{\"data\":[{\"id\":\"gpt-90-mini\"}]}";
                    string address = serve(catalog, asked);
                    if (kind == "openai") Brain.OpenAi = address;
                    else if (kind == "anthropic") Brain.Anthropic = address;
                    else Brain.DeepSeek = address;
                    string keptMediumKey = preferences.NvidiaKey, keptMediumModel = preferences.NvidiaModel, oldPaid = preferences.PaidKey;
                    mainframe.TypeBrainKey(key);
                    if (!mainframe.PaidKeyOffered || asked.Count != 0 || preferences.PaidKey != oldPaid) throw new Exception("Pasting a paid key did not offer the move before connecting.");
                    if (kind == "openai") mainframe.CheckMediumPanel(checks, output, "paid-key");
                    mainframe.PressBrainConnect();
                    if (!mainframe.PaidKeyOffered || asked.Count != 0 || preferences.Tier != 1) throw new Exception("Connecting a paid key silently rejected it or contacted its provider before the move.");
                    mainframe.PressMovePaid();
                    WaitVoice(delegate { return brain.Remote.Key == key; }, "The one-click paid-key move did not connect.");
                    mainframe.CheckBrainTier(2);
                    if (asked.Count != 1 || Voice.Unprotect(preferences.PaidKey) != key || preferences.TextKey != preferences.PaidKey || preferences.NvidiaKey != keptMediumKey || preferences.NvidiaModel != keptMediumModel || mainframe.PaidKeyOffered)
                        throw new Exception("The paid-key move did not preserve the medium slot or save MAX.");
                }
                checks.Add("PASS OpenAI, Anthropic and DeepSeek pasted under MEDIUM show the exact paid-key label and a one-click move; no provider is contacted before the move, MAX connects, and the encrypted medium key/model stay intact");

                foreach (string key in new[] { "AIza-medium-max", "sk-or-medium-max", "nvapi-medium-max" })
                {
                    string kind = Cloud.KindOf(key);
                    List<string> asked = new List<string>();
                    string address = serve(kind == "google" ? googleCatalog : kind == "openrouter" ? routerCatalog : nvidiaCatalog, asked);
                    if (kind == "google") Brain.Google = address;
                    else if (kind == "openrouter") Brain.OpenRouter = address;
                    else Brain.Nvidia = address;
                    ConnectText(key);
                    WaitVoice(delegate { return brain.Remote.Key == key; }, "A supported MAX key did not connect.");
                    mainframe.CheckBrainTier(2);
                    if (preferences.Tier != 2 || Voice.Unprotect(preferences.PaidKey) != key || preferences.PaidModel != brain.Model) throw new Exception("MAX rerouted a supported key into MEDIUM.");
                    if (kind == "openrouter")
                    {
                        if (!cloudModels.Contains("paid/vision")) throw new Exception("MAX lost paid OpenRouter models.");
                        mainframe.PickBrain(cloudModels.IndexOf("paid/vision"));
                        if (TierCloud().Model != "paid/vision" || LoadPreferences().PaidModel != "paid/vision") throw new Exception("MAX did not preserve a paid OpenRouter model.");
                    }
                }
                mainframe.ShowTiers(0, null, null);
                mainframe.CheckBrainTier(0);
                checks.Add("PASS MAX still accepts Gemini, OpenRouter (including paid models) and NVIDIA without changing tier; the free-key warning shows only on MEDIUM and the NVIDIA API note follows only an active NVIDIA key");
            }
            finally
            {
                ++cloudListing;
                ++asking;
                CloseTalk();
                foreach (TcpListener server in servers) server.Stop();
                Brain.Nvidia = wasNvidia; Brain.Google = wasGoogle; Brain.OpenRouter = wasRouter; Brain.OpenAi = wasOpenAi; Brain.Anthropic = wasAnthropic; Brain.DeepSeek = wasDeepSeek;
                preferences = wasPreferences;
                brain.Remote = wasRemote; brain.Local = wasLocal; brain.Model = wasModel; cloudModels = wasModels;
                Think = wasThink; finding = wasFinding;
                mainframe.TypeBrainKey("");
                mainframe.LayoutScale(0, 0);
                mainframe.SizeMainframe(wasSize);
                mainframe.ShowSettings(wasSettings);
                mainframe.Sync();
                ShowTiers();
                if (wasRemote != null) mainframe.ShowCloud(wasRemote, wasModels); else mainframe.Searching();
                SavePreferences();
                if (!mainframeShownWas) mainframe.Hide();
                else if (mainframe.WindowState != mainframeStateWas) mainframe.WindowState = mainframeStateWas;
            }
        }
    }
}

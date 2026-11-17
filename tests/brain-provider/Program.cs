using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization.Json;
using CapriDesktop;

internal static class Program
{
    private static int failures;
    private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static void Check(string name, bool good, string shown)
    {
        Console.WriteLine("{0} {1} [{2}]", good ? "PASS" : "FAIL", name, shown);
        if (!good) failures++;
    }

    private static void Set(Pet pet, string name, object value) { typeof(Pet).GetField(name, Fields).SetValue(pet, value); }
    private static void Call(Pet pet, string name) { typeof(Pet).GetMethod(name, Fields, null, Type.EmptyTypes, null).Invoke(pet, null); }

    private static Pet Fixture(Settings saved, string folder)
    {
        Pet pet = (Pet)RuntimeHelpers.GetUninitializedObject(typeof(Pet));
        pet.Saved = saved;
        Set(pet, "Brain", new Brain());
        Set(pet, "folder", folder);
        Set(pet, "accountGate", new object());
        Set(pet, "remoteGate", new object());
        Set(pet, "remoteBrains", new Dictionary<string, Func<bool>>(StringComparer.Ordinal));
        Set(pet, "nameEars", new OwnedNameEars());
        Set(pet, "voiceRecovery", new VoiceActionRecovery());
        Set(pet, "nameStatus", "Name listening stopped. Turn Listen for Capri on to resume.");
        Set(pet, "body", new Body());
        return pet;
    }

    private static void WriteSettings(string folder, Settings saved, Settings newer)
    {
        using (FileStream stream = File.Create(Path.Combine(folder, "cross.json"))) new DataContractJsonSerializer(typeof(Settings)).WriteObject(stream, saved);
        using (FileStream stream = File.Create(Path.Combine(folder, "cross-new.json"))) new DataContractJsonSerializer(typeof(Pet.NewSettings)).WriteObject(stream, Pet.Newest(newer));
    }

    private static string Providers(ProviderModelPick[] picks)
    {
        if (picks == null) return "null";
        List<string> parts = new List<string>();
        foreach (ProviderModelPick pick in picks) parts.Add(pick == null ? "null" : pick.Provider + "=" + pick.Model);
        return string.Join(",", parts.ToArray());
    }

    private static void SettingsRoundTrip(string folder)
    {
        Settings saved = new Settings
        {
            BrainProvider = "nvidia",
            ProviderModels = new[] { new ProviderModelPick { Provider = "nvidia", Model = "nvidia/a", Tier = 1 }, new ProviderModelPick { Provider = "google", Model = "gemini/b", Tier = 2 } },
            BrainTier = 1, FreeModel = "nvidia/a", PaidModel = "paid/x", VoiceProvider = "off", RemoteHost = "wss://pc.example",
            ThinkingPicks = new[] { new ThinkingPick { Source = "nvidia|nvidia/a", Level = ThinkingLevels.High } }
        };
        Pet.NewSettings newest = Pet.Newest(saved);
        Check("newest carries the provider and its models", newest.BrainProvider == "nvidia" && newest.ProviderModels != null && newest.ProviderModels.Length == 2 && newest.ProviderModels[1].Provider == "google" && newest.ProviderModels[1].Model == "gemini/b" && newest.ProviderModels[1].Tier == 2,
            "provider=" + (newest.BrainProvider ?? "null") + " picks=" + Providers(newest.ProviderModels));

        Settings recovered = Pet.Kept(new Settings(), newest);
        Check("an older settings copy recovers the provider and its models", recovered.BrainProvider == "nvidia" && recovered.ProviderModels != null && recovered.ProviderModels.Length == 2 && recovered.ProviderModels[0].Model == "nvidia/a" && recovered.ProviderModels[0].Tier == 1,
            "provider=" + (recovered.BrainProvider ?? "null") + " picks=" + Providers(recovered.ProviderModels));

        Settings current = Pet.Kept(new Settings { VoiceProvider = "off", RemoteHost = "wss://pc.example" }, newest);
        Check("a newer copy that lost only the provider fields gets them back", current.BrainProvider == "nvidia" && current.ProviderModels != null && current.VoiceProvider == "off",
            "provider=" + (current.BrainProvider ?? "null") + " voice=" + (current.VoiceProvider ?? "null") + " picks=" + Providers(current.ProviderModels));

        Settings own = Pet.Kept(new Settings { VoiceProvider = "elevenlabs", MainframeSize = 175 }, newest);
        Check("a newer copy still keeps its own settings", own.VoiceProvider == "elevenlabs" && own.MainframeSize == 175 && own.FreeModel == null,
            "voice=" + (own.VoiceProvider ?? "null") + " size=" + own.MainframeSize + " free=" + (own.FreeModel ?? "null"));

        Settings legacy = new Settings { BrainTier = 2, FreeModel = "free", PaidModel = "paid", ThinkingPicks = new[] { new ThinkingPick { Source = "openai|gpt-6-astra", Level = ThinkingLevels.High } } };
        Settings kept = Pet.Kept(new Settings(), Pet.Newest(legacy));
        Check("settings without provider fields keep today's settings", kept.BrainTier == 2 && kept.FreeModel == "free" && kept.PaidModel == "paid" && kept.ThinkingPicks != null && kept.BrainProvider == null && kept.ProviderModels == null,
            "tier=" + (kept.BrainTier.HasValue ? kept.BrainTier.Value.ToString() : "null") + " free=" + (kept.FreeModel ?? "null") + " paid=" + (kept.PaidModel ?? "null") + " provider=" + (kept.BrainProvider ?? "null"));

        WriteSettings(folder, saved, saved);
        string notice;
        Settings loaded = Pet.ReadSettings(Path.Combine(folder, "cross.json"), out notice);
        Check("the provider and its models survive the settings files", notice == null && loaded.BrainProvider == "nvidia" && loaded.ProviderModels != null && loaded.ProviderModels.Length == 2 && loaded.ProviderModels[1].Model == "gemini/b" && loaded.ProviderModels[1].Tier == 2,
            "notice=" + (notice ?? "null") + " provider=" + (loaded.BrainProvider ?? "null") + " picks=" + Providers(loaded.ProviderModels));

        WriteSettings(folder, new Settings { BrainTier = 1, FreeModel = "nvidia/a", VoiceProvider = "off", RemoteHost = "wss://pc.example" }, saved);
        Settings older = Pet.ReadSettings(Path.Combine(folder, "cross.json"), out notice);
        Check("cross-new.json brings the provider fields back after an older copy", notice == null && older.BrainProvider == "nvidia" && older.ProviderModels != null && older.ProviderModels.Length == 2 && older.FreeModel == "nvidia/a",
            "notice=" + (notice ?? "null") + " provider=" + (older.BrainProvider ?? "null") + " picks=" + Providers(older.ProviderModels));
    }

    private static void ProviderNames()
    {
        Settings fresh = new Settings { FreeModel = "nvidia/a" };
        Check("a key names its provider when settings have none", Pet.ProviderKind(fresh, Cloud.KindOf("nvapi-1234")) == "nvidia", "provider=" + (Pet.ProviderKind(fresh, Cloud.KindOf("nvapi-1234")) ?? "null"));
        Check("an active key names the provider in use over a stale one", Pet.ProviderKind(new Settings { BrainProvider = "openai" }, "nvidia") == "nvidia", "provider=" + (Pet.ProviderKind(new Settings { BrainProvider = "openai" }, "nvidia") ?? "null"));
        Check("no key and no saved provider names none", Pet.ProviderKind(new Settings(), null) == null, "provider=" + (Pet.ProviderKind(new Settings(), null) ?? "null"));

        foreach (string key in new[] { "nvapi-a", "AIza-a", "sk-or-a", "sk-proj-a", "sk-0123456789abcdef0123456789abcdef" })
        {
            string kind = Cloud.KindOf(key);
            Check("a key of " + kind + " is named from its prefix", Pet.ProviderKind(new Settings(), kind) == kind, "kind=" + kind);
        }

        Settings once = new Settings();
        Check("a null provider is adopted from the key", Pet.AdoptProvider(once, "nvidia") && once.BrainProvider == "nvidia", "provider=" + (once.BrainProvider ?? "null"));
        Check("an explicit provider is kept", !Pet.AdoptProvider(once, "google") && once.BrainProvider == "nvidia", "provider=" + (once.BrainProvider ?? "null"));
        Settings bare = new Settings();
        Check("no key adopts no provider", !Pet.AdoptProvider(bare, null) && bare.BrainProvider == null, "provider=" + (bare.BrainProvider ?? "null"));
    }

    private static void ProviderMemory()
    {
        Settings tier1 = new Settings { BrainTier = 1, FreeModel = "nvidia/a" };
        Pet.RememberProviderModel(tier1, Pet.ProviderKind(tier1, "nvidia"), "nvidia/a", 1);
        tier1.FreeModel = "gemini/b";
        Pet.RememberProviderModel(tier1, Pet.ProviderKind(tier1, "google"), "gemini/b", 1);
        Check("switching providers still writes the tier slot", tier1.FreeModel == "gemini/b", "free=" + (tier1.FreeModel ?? "null"));
        Check("coming back to a provider restores its model", Pet.TierModel(tier1, "nvidia", 1) == "nvidia/a", "model=" + (Pet.TierModel(tier1, "nvidia", 1) ?? "null"));
        Check("the provider in use keeps its own model", Pet.TierModel(tier1, "google", 1) == "gemini/b", "model=" + (Pet.TierModel(tier1, "google", 1) ?? "null"));
        Check("an unknown provider falls back to the tier slot", Pet.TierModel(tier1, "openai", 1) == "gemini/b", "model=" + (Pet.TierModel(tier1, "openai", 1) ?? "null"));
        Check("settings without provider memory keep the tier slot", Pet.TierModel(new Settings { FreeModel = "slot" }, "nvidia", 1) == "slot", "model=" + (Pet.TierModel(new Settings { FreeModel = "slot" }, "nvidia", 1) ?? "null"));
        Check("the paid tier keeps its own fallback", Pet.TierModel(new Settings { PaidModel = "paid-slot" }, "openai", 2) == "paid-slot", "model=" + (Pet.TierModel(new Settings { PaidModel = "paid-slot" }, "openai", 2) ?? "null"));

        Pet.RememberProviderModel(tier1, "nvidia", "nvidia/a2", 1);
        Check("one entry per provider", tier1.ProviderModels.Length == 2 && Pet.ProviderModel(tier1, "nvidia") == "nvidia/a2" && Pet.ProviderModel(tier1, "google") == "gemini/b",
            "picks=" + Providers(tier1.ProviderModels));

        List<string> offered = new List<string> { "nvidia/a2", "nvidia/small" };
        List<string> dropped = new List<string> { "nvidia/small" };
        Check("a saved key brings its provider's model back", Pet.KeyModel(tier1, "nvidia", offered, 1) == "nvidia/a2", "model=" + (Pet.KeyModel(tier1, "nvidia", offered, 1) ?? "null"));
        Check("a model the provider dropped falls back to its default", Pet.KeyModel(tier1, "nvidia", dropped, 1) == Brain.DefaultCloudModel("nvidia", dropped), "model=" + (Pet.KeyModel(tier1, "nvidia", dropped, 1) ?? "null"));
        Check("no memory picks the provider's default", Pet.KeyModel(new Settings(), "nvidia", offered, 1) == Brain.DefaultCloudModel("nvidia", offered), "model=" + (Pet.KeyModel(new Settings(), "nvidia", offered, 1) ?? "null"));
    }

    private static void DualWrite()
    {
        Settings basePath = new Settings { BrainTier = 1 };
        basePath.FreeModel = "nvidia/a";
        basePath.FreeModel = "gemini/b";
        Settings memoryPath = new Settings { BrainTier = 1 };
        memoryPath.FreeModel = "nvidia/a";
        Pet.RememberProviderModel(memoryPath, "nvidia", "nvidia/a", 1);
        memoryPath.FreeModel = "gemini/b";
        Pet.RememberProviderModel(memoryPath, "google", "gemini/b", 1);
        Check("the slots and the tier are what they were before provider memory",
            basePath.FreeModel == memoryPath.FreeModel && basePath.PaidModel == memoryPath.PaidModel && basePath.BrainTier == memoryPath.BrainTier,
            "base=" + (basePath.FreeModel ?? "null") + "/" + (basePath.PaidModel ?? "null") + " memory=" + (memoryPath.FreeModel ?? "null") + "/" + (memoryPath.PaidModel ?? "null"));

        Settings dual = new Settings { BrainTier = 2, PaidModel = "paid/a" };
        Pet.RememberProviderModel(dual, "openai", "paid/a", 2);
        Check("provider memory leaves the tier slots alone", dual.PaidModel == "paid/a" && dual.FreeModel == null && dual.BrainTier == 2,
            "free=" + (dual.FreeModel ?? "null") + " paid=" + (dual.PaidModel ?? "null") + " tier=" + (dual.BrainTier.HasValue ? dual.BrainTier.Value.ToString() : "null"));
    }

    private static void RemoteTiers()
    {
        const string dot = "\u00b7";
        string normal = "{\"type\":\"brain.state\",\"use\":\"normal\",\"name\":\"Ollama " + dot + " gemma4\",\"note\":\"Local model\",\"brains\":[{\"id\":\"normal\",\"key\":false},{\"id\":\"medium\",\"key\":false},{\"id\":\"max\",\"key\":false}]}";
        string medium = "{\"type\":\"brain.state\",\"use\":\"medium\",\"name\":\"NVIDIA " + dot + " nvidia/a\",\"note\":\"Key saved, ends 1234\",\"brains\":[{\"id\":\"normal\",\"key\":false},{\"id\":\"medium\",\"key\":true},{\"id\":\"max\",\"key\":true}]}";
        string max = "{\"type\":\"brain.state\",\"use\":\"max\",\"name\":\"OpenAI " + dot + " gpt-6\",\"note\":\"Key saved, ends 1234\",\"brains\":[{\"id\":\"normal\",\"key\":false},{\"id\":\"medium\",\"key\":true},{\"id\":\"max\",\"key\":true}]}";
        Check("the phone's normal state is unchanged", RemoteBrainKeys.State(0, "Ollama " + dot + " gemma4", "Local model", false, false) == normal, RemoteBrainKeys.State(0, "Ollama " + dot + " gemma4", "Local model", false, false));
        Check("the phone's medium state is unchanged", RemoteBrainKeys.State(1, "NVIDIA " + dot + " nvidia/a", "Key saved, ends 1234", true, true) == medium, RemoteBrainKeys.State(1, "NVIDIA " + dot + " nvidia/a", "Key saved, ends 1234", true, true));
        Check("the phone's max state is unchanged", RemoteBrainKeys.State(2, "OpenAI " + dot + " gpt-6", "Key saved, ends 1234", true, true) == max, RemoteBrainKeys.State(2, "OpenAI " + dot + " gpt-6", "Key saved, ends 1234", true, true));

        foreach (string key in new[] { "nvapi-a", "AIza-a", "sk-or-a", "sk-proj-a", "sk-0123456789abcdef0123456789abcdef" })
        {
            string kind = Cloud.KindOf(key);
            string model = "model-" + kind;
            Cloud cloud = new Cloud(key, model);
            int tier = Cloud.MediumKind(kind) ? 1 : 2;
            string before = RemoteBrainKeys.State(tier, cloud.Name + " " + dot + " " + model, "Key saved, ends 1234", tier == 1, tier == 2);
            string brains = "{\"id\":\"normal\",\"key\":false},{\"id\":\"medium\",\"key\":" + (tier == 1 ? "true" : "false") + "},{\"id\":\"max\",\"key\":" + (tier == 2 ? "true" : "false") + "}";
            Check("the phone keeps calling " + kind + "'s tier " + RemoteBrainKeys.Brains[tier], before.Contains("\"use\":\"" + RemoteBrainKeys.Brains[tier] + "\"") && before.Contains("\"brains\":[" + brains + "]"), before);

            Settings same = new Settings { BrainTier = tier };
            if (tier == 1) same.FreeModel = model; else same.PaidModel = model;
            Pet.RememberProviderModel(same, kind, model, tier);
            string after = RemoteBrainKeys.State(tier, cloud.Name + " " + dot + " " + Pet.TierModel(same, kind, tier), "Key saved, ends 1234", tier == 1, tier == 2);
            Check("provider memory leaves " + kind + "'s phone state alone", before == after, after);
        }
    }

    private static void NullKeys(string folder)
    {
        string previous = CapriDesktop.Program.Smoke;
        try
        {
            string local = Path.Combine(folder, "null-normal");
            Directory.CreateDirectory(local);
            CapriDesktop.Program.Smoke = local;
            Settings saved = new Settings { VoiceProvider = "off", BrainTier = 2, FreeModel = "free-slot", PaidModel = "paid-slot", Model = "ollama-model" };
            Pet pet = Fixture(saved, local);
            pet.Brain.Remote = new Cloud("sk-or-paid", "paid-slot");
            pet.Brain.Model = "paid-slot";
            pet.SetBrainTier(0);
            Check("the normal tier needs no key", pet.Brain.Remote == null && pet.Brain.Model == "ollama-model" && saved.BrainProvider == null && saved.BrainTier == 0,
                "remote=" + (pet.Brain.Remote == null ? "null" : pet.Brain.Remote.Kind) + " model=" + (pet.Brain.Model ?? "null") + " provider=" + (saved.BrainProvider ?? "null") + " tier=" + (saved.BrainTier.HasValue ? saved.BrainTier.Value.ToString() : "null"));

            string quiet = Path.Combine(folder, "null-tier");
            Directory.CreateDirectory(quiet);
            CapriDesktop.Program.Smoke = quiet;
            Settings open = new Settings { VoiceProvider = "off", BrainTier = 0, FreeModel = "free-slot", PaidModel = "paid-slot", Model = "ollama-model" };
            Pet blank = Fixture(open, quiet);
            blank.Brain.Model = "ollama-model";
            blank.SetBrainTier(1);
            Check("a tier with no saved key names no provider", Cloud.KindOf(null) == null && blank.Brain.Remote == null && blank.Brain.Model == null && open.BrainTier == 1 && open.BrainProvider == null,
                "kind=" + (Cloud.KindOf(null) ?? "null") + " remote=" + (blank.Brain.Remote == null ? "null" : blank.Brain.Remote.Kind) + " model=" + (blank.Brain.Model ?? "null") + " provider=" + (open.BrainProvider ?? "null"));
        }
        finally { CapriDesktop.Program.Smoke = previous; }
    }

    private static void ProviderTransition(string folder)
    {
        string previous = CapriDesktop.Program.Smoke;
        try
        {
            string cloud = Path.Combine(folder, "transition");
            Directory.CreateDirectory(cloud);
            CapriDesktop.Program.Smoke = null;
            Settings saved = new Settings { VoiceProvider = "off", BrainTier = 1 };
            Pet pet = Fixture(saved, cloud);
            List<string> free = new List<string> { "vendor/free:free", "vendor/free-new" };
            List<string> paid = new List<string> { "vendor/paid" };
            pet.UseCloud("sk-or-free", Pet.KeyModel(saved, "openrouter", free, 1), free, 1);
            Check("a saved free key thinks with its model", pet.Brain.Model == "vendor/free:free" && saved.FreeModel == "vendor/free:free" && pet.MainframeTier == 1,
                "model=" + (pet.Brain.Model ?? "null") + " free=" + (saved.FreeModel ?? "null") + " tier=" + pet.MainframeTier);
            pet.UseCloud("sk-proj-paid", Pet.KeyModel(saved, "openai", paid, 2), paid, 2);
            Check("the paid provider takes over", pet.Brain.Model == "vendor/paid" && saved.PaidModel == "vendor/paid" && pet.MainframeTier == 2,
                "model=" + (pet.Brain.Model ?? "null") + " paid=" + (saved.PaidModel ?? "null") + " tier=" + pet.MainframeTier);
            pet.UseCloud("sk-or-free", Pet.KeyModel(saved, "openrouter", free, 1), free, 1);
            Check("switching back restores the free provider's model", pet.Brain.Model == "vendor/free:free" && pet.MainframeTier == 1 && saved.FreeModel == "vendor/free:free" && saved.PaidModel == "vendor/paid",
                "model=" + (pet.Brain.Model ?? "null") + " free=" + (saved.FreeModel ?? "null") + " paid=" + (saved.PaidModel ?? "null") + " tier=" + pet.MainframeTier);
            string notice;
            Settings reloaded = Pet.ReadSettings(Path.Combine(cloud, "cross.json"), out notice);
            Check("the switched providers survive a reload",
                notice == null && reloaded.BrainTier == 1 && reloaded.BrainProvider == "openrouter" && Pet.TierModel(reloaded, "openrouter", 1) == "vendor/free:free" && Pet.TierModel(reloaded, "openai", 2) == "vendor/paid",
                "notice=" + (notice ?? "null") + " tier=" + (reloaded.BrainTier.HasValue ? reloaded.BrainTier.Value.ToString() : "null") + " provider=" + (reloaded.BrainProvider ?? "null") + " free=" + (Pet.TierModel(reloaded, "openrouter", 1) ?? "null") + " paid=" + (Pet.TierModel(reloaded, "openai", 2) ?? "null"));
            Pet replay = Fixture(reloaded, cloud);
            replay.SetBrainTier(2);
            Check("a reloaded max tier restores its provider's model", replay.Brain.Model == "vendor/paid" && replay.MainframeTier == 2 && replay.Saved.BrainProvider == "openai",
                "model=" + (replay.Brain.Model ?? "null") + " provider=" + (replay.Saved.BrainProvider ?? "null") + " tier=" + replay.MainframeTier);
            replay.SetBrainTier(1);
            Check("a reloaded medium tier restores the free provider's model", replay.Brain.Model == "vendor/free:free" && replay.MainframeTier == 1 && replay.Saved.FreeModel == "vendor/free:free" && replay.Saved.PaidModel == "vendor/paid",
                "model=" + (replay.Brain.Model ?? "null") + " free=" + (replay.Saved.FreeModel ?? "null") + " paid=" + (replay.Saved.PaidModel ?? "null") + " tier=" + replay.MainframeTier);
        }
        finally { CapriDesktop.Program.Smoke = previous; }
    }

    private static void AdoptedProvider(string folder)
    {
        string previous = CapriDesktop.Program.Smoke;
        try
        {
            string adopt = Path.Combine(folder, "adopt");
            Directory.CreateDirectory(adopt);
            CapriDesktop.Program.Smoke = null;
            string path = Path.Combine(adopt, "cross.json");
            File.WriteAllText(path, "{\"VoiceProvider\":\"off\"}");
            string notice;
            bool blocked;
            Settings loaded = Pet.ReadSettings(path, out notice, out blocked);
            Pet pet = Fixture(loaded, adopt);
            bool adopted = Pet.AdoptProvider(loaded, "nvidia");
            Call(pet, "KeepAdoptedProvider");
            Settings once = Pet.ReadSettings(path, out notice, out blocked);
            Check("an adopted provider is saved at startup", adopted && once.BrainProvider == "nvidia" && once.VoiceProvider == "off",
                "adopted=" + adopted + " provider=" + (once.BrainProvider ?? "null") + " voice=" + (once.VoiceProvider ?? "null"));
            loaded.VoiceProvider = "elevenlabs";
            pet.Save();
            Settings twice = Pet.ReadSettings(path, out notice, out blocked);
            Check("an adopted provider survives the next settings write", twice.BrainProvider == "nvidia" && twice.VoiceProvider == "elevenlabs",
                "provider=" + (twice.BrainProvider ?? "null") + " voice=" + (twice.VoiceProvider ?? "null"));
        }
        finally { CapriDesktop.Program.Smoke = previous; }
    }

    private static int Main()
    {
        string folder = Path.Combine(Path.GetTempPath(), "capri-brain-provider-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            SettingsRoundTrip(folder);
            ProviderNames();
            ProviderMemory();
            DualWrite();
            RemoteTiers();
            failures += VerifierRegressions.Run(folder);
            NullKeys(folder);
            ProviderTransition(folder);
            AdoptedProvider(folder);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
        Console.WriteLine(failures == 0 ? "PASS brain-provider checks" : "FAIL brain-provider checks " + failures);
        return failures == 0 ? 0 : 1;
    }
}

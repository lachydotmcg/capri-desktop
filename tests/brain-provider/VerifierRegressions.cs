using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Avalonia;
using CapriDesktop;

internal static class VerifierRegressions
{
    private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static int failures;
    private static void Check(string name, bool ok)
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
        if (!ok) failures++;
    }
    private static void Set(Pet pet, string name, object value) { typeof(Pet).GetField(name, Fields).SetValue(pet, value); }
    private static void Call(Pet pet, string name) { typeof(Pet).GetMethod(name, Fields, null, Type.EmptyTypes, null).Invoke(pet, null); }
    private static string Hash(string path) { return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))); }
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
    internal static int Run(string root)
    {
        failures = 0;
        string previous = CapriDesktop.Program.Smoke;
        try
        {
            AppBuilder.Configure<Application>().UseStandardRuntimePlatformSubsystem()
                .UseWindowingSubsystem(() => { }, "Offscreen checks").UseSkia().SetupWithoutStarting();
            string recovered = Path.Combine(root, "recovered");
            Directory.CreateDirectory(recovered);
            CapriDesktop.Program.Smoke = recovered;
            Settings backup = new Settings { VoiceProvider = "off", BrainProvider = "nvidia", ProviderModels = new[] { new ProviderModelPick { Provider = "nvidia", Model = "nv-old" } } };
            Settings old = Pet.Kept(new Settings { VoiceProvider = "off", BrainTier = 1, FreeModel = "google-model" }, Pet.Newest(backup));
            Pet pet = Fixture(old, recovered);
            pet.Brain.Remote = new Cloud("AIza-google", "google-model");
            Call(pet, "KeepBrainTier");
            Check("old copy cannot attribute a Google model to NVIDIA", Pet.ProviderModel(old, "nvidia") == "nv-old" && Pet.ProviderModel(old, "google") == "google-model");

            Settings bad = new Settings { BrainTier = 1, BrainProvider = "garbage", ProviderModels = new[] { new ProviderModelPick { Provider = "nvidia", Model = "nv-old" } } };
            pet = Fixture(bad, recovered);
            pet.Brain.Remote = new Cloud("nvapi-test", "nv-current");
            Call(pet, "KeepBrainTier");
            Check("invalid provider cannot steal an active model", bad.FreeModel == "nv-current" && Pet.ProviderModel(bad, "nvidia") == "nv-current" && Pet.ProviderModel(bad, "garbage") == null);

            string tiers = Path.Combine(root, "tiers");
            Directory.CreateDirectory(tiers);
            CapriDesktop.Program.Smoke = tiers;
            File.WriteAllText(Path.Combine(tiers, "brain-free-key"), "sk-or-free");
            File.WriteAllText(Path.Combine(tiers, "brain-paid-key"), "sk-or-paid");
            Settings saved = new Settings { VoiceProvider = "off", BrainTier = 2, FreeModel = "vendor/free:free", PaidModel = "vendor/paid" };
            pet = Fixture(saved, tiers);
            pet.Brain.Remote = new Cloud("sk-or-paid", "vendor/paid");
            pet.Brain.Model = "vendor/paid";
            pet.SetBrainTier(1);
            Check("medium retains the free model on first migration", pet.Brain.Model == "vendor/free:free");
            pet.SetBrainTier(2);
            Check("switching back preserves the free slot", saved.FreeModel == "vendor/free:free" && saved.PaidModel == "vendor/paid");

            string mixed = Path.Combine(root, "recovered-same-provider");
            Directory.CreateDirectory(mixed);
            CapriDesktop.Program.Smoke = mixed;
            File.WriteAllText(Path.Combine(mixed, "cross.json"), "{\"VoiceProvider\":\"off\",\"BrainTier\":2,\"FreeModel\":\"google-new\",\"PaidModel\":\"vendor/paid\"}");
            File.WriteAllText(Path.Combine(mixed, "cross-new.json"), "{\"BrainProvider\":\"nvidia\",\"ProviderModels\":[{\"Provider\":\"nvidia\",\"Model\":\"nv-old\",\"Tier\":1},{\"Provider\":\"google\",\"Model\":\"google-old\",\"Tier\":1}]}");
            File.WriteAllText(Path.Combine(mixed, "brain-free-key"), "AIza-free");
            File.WriteAllText(Path.Combine(mixed, "brain-paid-key"), "sk-or-paid");
            string recoveredNotice;
            bool recoveredBlocked;
            Settings recoveredSettings = Pet.ReadSettings(Path.Combine(mixed, "cross.json"), out recoveredNotice, out recoveredBlocked);
            Pet recoveredPet = Fixture(recoveredSettings, mixed);
            recoveredPet.Brain.Remote = new Cloud("sk-or-paid", "vendor/paid");
            recoveredPet.Brain.Model = "vendor/paid";
            recoveredPet.SetBrainTier(1);
            Call(recoveredPet, "KeepBrainTier");
            Check("recovered tier slot beats same-provider sidecar memory", recoveredNotice == null && !recoveredBlocked &&
                recoveredPet.Brain.Model == "google-new" && recoveredSettings.FreeModel == "google-new" &&
                Pet.ProviderModel(recoveredSettings, "nvidia", 1) == "nv-old" && Pet.ProviderModel(recoveredSettings, "google", 1) == "google-new");

            CapriDesktop.Program.Smoke = null;
            Call(recoveredPet, "WriteSettings");
            string persistedNotice;
            bool persistedBlocked;
            Settings persisted = Pet.ReadSettings(Path.Combine(mixed, "cross.json"), out persistedNotice, out persistedBlocked);
            Check("recovered provider memory survives a settings write", persistedNotice == null && !persistedBlocked &&
                persisted.FreeModel == "google-new" && Pet.ProviderModel(persisted, "nvidia", 1) == "nv-old" &&
                Pet.ProviderModel(persisted, "google", 1) == "google-new");
            CapriDesktop.Program.Smoke = mixed;

            string olderSave = Path.Combine(root, "older-version-save");
            Directory.CreateDirectory(olderSave);
            CapriDesktop.Program.Smoke = olderSave;
            string olderMain = Path.Combine(olderSave, "cross.json"), olderSidecar = Path.Combine(olderSave, "cross-new.json");
            string olderText = "{\"VoiceProvider\":\"off\",\"BrainTier\":1,\"FreeModel\":\"google-live\",\"PaidModel\":\"paid-live\",\"CloudModel\":\"google-live\"}";
            File.WriteAllText(olderMain, olderText);
            File.WriteAllText(olderSidecar, olderText);
            File.WriteAllText(Path.Combine(olderSave, "brain-free-key"), "AIza-free");
            File.WriteAllText(Path.Combine(olderSave, "brain-paid-key"), "sk-or-paid");
            string olderNotice;
            bool olderBlocked;
            Settings olderSettings = Pet.ReadSettings(olderMain, out olderNotice, out olderBlocked);
            Check("an older version's save recovers with no provider memory", olderNotice == null && !olderBlocked && olderSettings.BrainProvider == null && olderSettings.ProviderModels == null &&
                olderSettings.VoiceProvider == "off" && olderSettings.BrainTier == 1 && olderSettings.FreeModel == "google-live" && olderSettings.PaidModel == "paid-live");
            Pet olderPet = Fixture(olderSettings, olderSave);
            olderPet.Brain.Remote = new Cloud("AIza-free", "google-live");
            olderPet.Brain.Model = "google-live";
            Call(olderPet, "RestoreBrainTier");
            Check("an older version's tier slot beats the key's provider", olderPet.Brain.Model == "google-live" && Pet.TierModel(olderSettings, "google", 1) == "google-live" &&
                Pet.TierModel(olderSettings, "openai", 1) == "google-live" && Pet.ProviderModel(olderSettings, "google") == null);
            CapriDesktop.Program.Smoke = null;
            Call(olderPet, "KeepAdoptedProvider");
            Check("an older version's save survives startup unchanged", File.ReadAllText(olderMain) == olderText && File.ReadAllText(olderSidecar) == olderText);
            Call(olderPet, "WriteSettings");
            string olderPersistedNotice;
            bool olderPersistedBlocked;
            Settings olderPersisted = Pet.ReadSettings(olderMain, out olderPersistedNotice, out olderPersistedBlocked);
            Check("an older version's save keeps every setting through the next write", olderPersistedNotice == null && !olderPersistedBlocked && olderPersisted.BrainProvider == "google" &&
                olderPersisted.VoiceProvider == "off" && olderPersisted.BrainTier == 1 && olderPersisted.FreeModel == "google-live" && olderPersisted.PaidModel == "paid-live" &&
                Pet.ProviderModel(olderPersisted, "google") == null);
            CapriDesktop.Program.Smoke = olderSave;

            string emptyTier = Path.Combine(root, "empty-tier");
            Directory.CreateDirectory(emptyTier);
            File.WriteAllText(Path.Combine(emptyTier, "cross.json"), "{\"VoiceProvider\":\"off\",\"BrainTier\":2,\"FreeModel\":\"\",\"PaidModel\":\"paid-live\"}");
            File.WriteAllText(Path.Combine(emptyTier, "cross-new.json"), "{\"ProviderModels\":[{\"Provider\":\"google\",\"Model\":\"google-memory\",\"Tier\":1}]}");
            File.WriteAllText(Path.Combine(emptyTier, "brain-free-key"), "AIza-free");
            File.WriteAllText(Path.Combine(emptyTier, "brain-paid-key"), "sk-or-paid");
            string emptyNotice;
            bool emptyBlocked;
            Settings emptySaved = Pet.ReadSettings(Path.Combine(emptyTier, "cross.json"), out emptyNotice, out emptyBlocked);
            Check("recovery inherits memory for an empty tier slot", emptyNotice == null && !emptyBlocked && emptySaved.FreeModel == "" &&
                Pet.ProviderModel(emptySaved, "google", 1) == "google-memory" && Pet.TierModel(emptySaved, "google", 1) == "google-memory");

            string otherTier = Path.Combine(root, "other-tier");
            Directory.CreateDirectory(otherTier);
            File.WriteAllText(Path.Combine(otherTier, "cross.json"), "{\"VoiceProvider\":\"off\",\"BrainTier\":2,\"FreeModel\":\"google-live\",\"PaidModel\":\"nv-live\"}");
            File.WriteAllText(Path.Combine(otherTier, "cross-new.json"), "{\"ProviderModels\":[{\"Provider\":\"google\",\"Model\":\"google-stale\",\"Tier\":1},{\"Provider\":\"nvidia\",\"Model\":\"nv-stale\",\"Tier\":2},{\"Provider\":\"nvidia\",\"Model\":\"nv-free-memory\",\"Tier\":1},{\"Provider\":\"google\",\"Model\":\"google-paid-memory\",\"Tier\":2}]}");
            File.WriteAllText(Path.Combine(otherTier, "brain-free-key"), "AIza-free");
            File.WriteAllText(Path.Combine(otherTier, "brain-paid-key"), "nvapi-paid");
            string otherNotice;
            bool otherBlocked;
            Settings otherSaved = Pet.ReadSettings(Path.Combine(otherTier, "cross.json"), out otherNotice, out otherBlocked);
            Check("recovery retains the other tier's provider memory", otherNotice == null && !otherBlocked &&
                Pet.ProviderModel(otherSaved, "google", 1) == "google-live" && Pet.ProviderModel(otherSaved, "nvidia", 2) == "nv-live" &&
                Pet.ProviderModel(otherSaved, "nvidia", 1) == "nv-free-memory" && Pet.ProviderModel(otherSaved, "google", 2) == "google-paid-memory");

            string untiered = Path.Combine(root, "untiered-provider-memory");
            Directory.CreateDirectory(untiered);
            CapriDesktop.Program.Smoke = untiered;
            File.WriteAllText(Path.Combine(untiered, "cross.json"), "{\"VoiceProvider\":\"off\",\"BrainTier\":2,\"FreeModel\":\"vendor/free:free\",\"PaidModel\":\"vendor/paid\",\"ProviderModels\":[{\"Provider\":\"openrouter\",\"Model\":\"vendor/stale\"}]}");
            File.WriteAllText(Path.Combine(untiered, "brain-free-key"), "sk-or-free");
            File.WriteAllText(Path.Combine(untiered, "brain-paid-key"), "sk-or-paid");
            string untieredNotice;
            bool untieredBlocked;
            Settings untieredSettings = Pet.ReadSettings(Path.Combine(untiered, "cross.json"), out untieredNotice, out untieredBlocked);
            Pet untieredPet = Fixture(untieredSettings, untiered);
            untieredPet.Brain.Remote = new Cloud("sk-or-paid", "vendor/paid");
            untieredPet.Brain.Model = "vendor/paid";
            bool slots = Pet.TierModel(untieredSettings, "openrouter", 1) == "vendor/free:free" && Pet.TierModel(untieredSettings, "openrouter", 2) == "vendor/paid";
            untieredPet.SetBrainTier(1);
            bool usedFree = untieredPet.Brain.Model == "vendor/free:free";
            untieredPet.SetBrainTier(2);
            Check("untiered memory cannot override either tier", untieredNotice == null && !untieredBlocked && slots && usedFree &&
                untieredPet.Brain.Model == "vendor/paid" && untieredSettings.FreeModel == "vendor/free:free" && untieredSettings.PaidModel == "vendor/paid");

            string missingKey = Path.Combine(root, "missing-key-with-memory");
            Directory.CreateDirectory(missingKey);
            CapriDesktop.Program.Smoke = missingKey;
            Settings missingSettings = new Settings { VoiceProvider = "off", BrainTier = 0, FreeModel = "free-slot", ProviderModels = new[] { new ProviderModelPick { Provider = "nvidia", Model = "nv-memory", Tier = 1 } } };
            Pet missingPet = Fixture(missingSettings, missingKey);
            missingPet.SetBrainTier(1);
            Check("a missing key with provider memory cannot crash", missingPet.Brain.Remote == null && missingPet.Brain.Model == null && missingSettings.FreeModel == "free-slot");

            Settings unknown = new Settings { BrainTier = 1, BrainProvider = "garbage", ProviderModels = new[] { new ProviderModelPick { Provider = "google", Model = "google-old", Tier = 1 } } };
            Pet unknownPet = Fixture(unknown, missingKey);
            unknownPet.Brain.Remote = new Cloud("nvapi-active", "nv-current");
            Call(unknownPet, "KeepBrainTier");
            Check("garbage cannot receive the active provider's model", unknown.BrainProvider == "nvidia" && Pet.ProviderModel(unknown, "nvidia", 1) == "nv-current" &&
                Pet.ProviderModel(unknown, "garbage", 1) == null && Pet.ProviderModel(unknown, "google", 1) == "google-old");

            CapriDesktop.Program.Smoke = null;
            string failed = Path.Combine(root, "failed-backup");
            Directory.CreateDirectory(failed);
            string main = Path.Combine(failed, "cross.json"), newer = Path.Combine(failed, "cross-new.json");
            File.WriteAllText(main, "{bad");
            File.WriteAllText(newer, "{newer");
            Directory.CreateDirectory(Path.Combine(failed, Pet.UnreadableName));
            string beforeMain = Hash(main), beforeNewer = Hash(newer), notice;
            bool blocked;
            Settings loaded = Pet.ReadSettings(main, out notice, out blocked);
            pet = Fixture(loaded, failed);
            Set(pet, "settingsWriteBlocked", blocked);
            Pet.AdoptProvider(loaded, "nvidia");
            Call(pet, "WriteSettings");
            Check("failed backup prevents settings writes", blocked && Hash(main) == beforeMain && Hash(newer) == beforeNewer);

            string race = Path.Combine(root, "race");
            Directory.CreateDirectory(race);
            main = Path.Combine(race, "cross.json");
            File.WriteAllText(main, "{\"VoiceProvider\":\"off\",\"Nickname\":\"old\"}");
            loaded = Pet.ReadSettings(main, out notice, out blocked);
            pet = Fixture(loaded, race);
            string newerWriter = "{\"VoiceProvider\":\"off\",\"Nickname\":\"newer-writer\"}";
            File.WriteAllText(main, newerWriter);
            Pet.AdoptProvider(loaded, "nvidia");
            Call(pet, "KeepAdoptedProvider");
            Check("startup adoption leaves a concurrent save alone", File.ReadAllText(main) == newerWriter && Pet.ReadSettings(main, out notice, out blocked).Nickname == "newer-writer");

            string recoveredOld = Path.Combine(root, "recovered-old-sidecar");
            Directory.CreateDirectory(recoveredOld);
            File.WriteAllText(Path.Combine(recoveredOld, "cross.json"), "{\"Nickname\":\"legacy-nick\",\"CloudModel\":\"google-live\"}");
            File.WriteAllText(Path.Combine(recoveredOld, "cross-new.json"), "{\"VoiceProvider\":\"off\",\"BrainTier\":1,\"FreeModel\":\"google-live\",\"PaidModel\":\"paid-live\",\"RemoteHost\":\"wss://old.example\"}");
            string oldNotice;
            bool oldBlocked;
            Settings restoredOld = Pet.ReadSettings(Path.Combine(recoveredOld, "cross.json"), out oldNotice, out oldBlocked);
            Check("an old sidecar restores settings absent from an old primary", oldNotice == null && !oldBlocked &&
                restoredOld.Nickname == "legacy-nick" && restoredOld.CloudModel == "google-live" && restoredOld.VoiceProvider == "off" &&
                restoredOld.BrainTier == 1 && restoredOld.FreeModel == "google-live" && restoredOld.PaidModel == "paid-live" &&
                restoredOld.RemoteHost == "wss://old.example" && restoredOld.BrainProvider == null && restoredOld.ProviderModels == null);
        }
        finally { CapriDesktop.Program.Smoke = previous; }
        return failures;
    }
}

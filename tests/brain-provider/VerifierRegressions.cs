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
            Check("failed backup prevents migration writes", blocked && Hash(main) == beforeMain && Hash(newer) == beforeNewer);

            string race = Path.Combine(root, "race");
            Directory.CreateDirectory(race);
            main = Path.Combine(race, "cross.json");
            File.WriteAllText(main, "{\"VoiceProvider\":\"off\",\"Nickname\":\"old\"}");
            loaded = Pet.ReadSettings(main, out notice, out blocked);
            pet = Fixture(loaded, race);
            File.WriteAllText(main, "{\"VoiceProvider\":\"off\",\"Nickname\":\"newer-writer\"}");
            if (Pet.AdoptProvider(loaded, "nvidia")) Call(pet, "KeepAdoptedProvider");
            Check("migration does not overwrite a concurrent save", Pet.ReadSettings(main, out notice, out blocked).Nickname == "newer-writer");
        }
        finally { CapriDesktop.Program.Smoke = previous; }
        return failures;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization.Json;
using CapriDesktop;

internal static class CustomProviders
{
    private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static int failures;

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

    private static string Endpoints(ProviderEndpoint[] endpoints)
    {
        if (endpoints == null) return "null";
        List<string> parts = new List<string>();
        foreach (ProviderEndpoint endpoint in endpoints) parts.Add(endpoint == null ? "null" : endpoint.Id + "=" + endpoint.Name + "@" + endpoint.Address);
        return string.Join(",", parts.ToArray());
    }

    private static string Picks(ProviderModelPick[] picks)
    {
        if (picks == null) return "null";
        List<string> parts = new List<string>();
        foreach (ProviderModelPick pick in picks) parts.Add(pick == null ? "null" : pick.Provider + "=" + pick.Model + "/" + pick.Tier);
        return string.Join(",", parts.ToArray());
    }

    private static void EndpointSettings(string folder)
    {
        Settings saved = new Settings
        {
            VoiceProvider = "off",
            BrainProvider = "custom:home",
            ProviderModels = new[] { new ProviderModelPick { Provider = "custom:home", Model = "vendor/home", Tier = 0 }, new ProviderModelPick { Provider = "custom:work", Model = "vendor/work", Tier = 0 } },
            ProviderEndpoints = new[]
            {
                new ProviderEndpoint { Id = "home", Name = "Home GPU", Address = "http://localhost:8080" },
                new ProviderEndpoint { Id = "work", Name = "Work", Address = "https://work.example" }
            }
        };
        Pet.NewSettings newest = Pet.Newest(saved);
        Check("newest carries both custom providers", newest.ProviderEndpoints != null && newest.ProviderEndpoints.Length == 2 && newest.ProviderEndpoints[1].Id == "work" && newest.ProviderEndpoints[1].Name == "Work" && newest.ProviderEndpoints[1].Address == "https://work.example",
            "endpoints=" + Endpoints(newest.ProviderEndpoints));

        Settings recovered = Pet.Kept(new Settings(), newest);
        Check("an older settings copy recovers both custom providers", recovered.ProviderEndpoints != null && recovered.ProviderEndpoints.Length == 2 && recovered.ProviderEndpoints[0].Id == "home" && recovered.ProviderEndpoints[0].Address == "http://localhost:8080" && recovered.BrainProvider == "custom:home",
            "provider=" + (recovered.BrainProvider ?? "null") + " endpoints=" + Endpoints(recovered.ProviderEndpoints));

        Settings current = Pet.Kept(new Settings { VoiceProvider = "off", MainframeSize = 175 }, newest);
        Check("a newer copy that lost only the custom providers gets them back", current.ProviderEndpoints != null && current.ProviderEndpoints.Length == 2 && current.MainframeSize == 175 && current.VoiceProvider == "off",
            "size=" + current.MainframeSize + " endpoints=" + Endpoints(current.ProviderEndpoints));

        Settings own = Pet.Kept(new Settings { VoiceProvider = "off", MainframeSize = 200, ProviderEndpoints = new[] { new ProviderEndpoint { Id = "mine", Name = "Mine", Address = "http://localhost:9000" } } }, newest);
        Check("a newer copy keeps its own custom providers", own.ProviderEndpoints != null && own.ProviderEndpoints.Length == 1 && own.ProviderEndpoints[0].Id == "mine" && own.MainframeSize == 200,
            "endpoints=" + Endpoints(own.ProviderEndpoints));

        WriteSettings(folder, saved, saved);
        string notice;
        bool blocked;
        Settings loaded = Pet.ReadSettings(Path.Combine(folder, "cross.json"), out notice, out blocked);
        Check("both custom providers survive the settings files", notice == null && !blocked && loaded.ProviderEndpoints != null && loaded.ProviderEndpoints.Length == 2 && loaded.ProviderEndpoints[1].Name == "Work" && loaded.ProviderEndpoints[1].Address == "https://work.example" && loaded.BrainProvider == "custom:home",
            "notice=" + (notice ?? "null") + " endpoints=" + Endpoints(loaded.ProviderEndpoints));

        string alone = Path.Combine(folder, "unknown");
        Directory.CreateDirectory(alone);
        string unknown = Path.Combine(alone, "cross.json");
        File.WriteAllText(unknown, "{\"VoiceProvider\":\"off\",\"ProviderEndpoints\":[{\"Id\":\"home\",\"Name\":\"Home\",\"Address\":\"http://localhost:8080\",\"Colour\":\"red\"}]}");
        Settings forward = Pet.ReadSettings(unknown, out notice, out blocked);
        Check("a newer version's extra provider field doesn't break the round trip", notice == null && !blocked && forward.ProviderEndpoints != null && forward.ProviderEndpoints.Length == 1 && forward.ProviderEndpoints[0].Id == "home" && forward.ProviderEndpoints[0].Name == "Home",
            "notice=" + (notice ?? "null") + " endpoints=" + Endpoints(forward.ProviderEndpoints));
    }

    private static void EndpointIds()
    {
        Settings saved = new Settings();
        string first = Pet.AddEndpoint(saved, "Home GPU!", "http://localhost:8080/v1");
        string second = Pet.AddEndpoint(saved, "Home GPU", "https://second.example");
        Check("a custom provider gets a lowercase slug id", first == "home-gpu" && second == "home-gpu-2", "first=" + (first ?? "null") + " second=" + (second ?? "null"));
        Check("a custom provider keeps its display name and base address", saved.ProviderEndpoints != null && saved.ProviderEndpoints.Length == 2 && saved.ProviderEndpoints[0].Name == "Home GPU!" && saved.ProviderEndpoints[0].Address == "http://localhost:8080",
            "endpoints=" + Endpoints(saved.ProviderEndpoints));

        string third = Pet.AddEndpoint(saved, "Work", "https://third.example");
        Check("adding a custom provider never changes the others' ids", first == "home-gpu" && second == "home-gpu-2" && third == "work" && saved.ProviderEndpoints[2].Id == "work",
            "ids=" + first + "," + second + "," + (third ?? "null"));

        string bad = Pet.AddEndpoint(saved, "Bad", "not an address");
        Check("a custom provider needs a working address", bad == null && saved.ProviderEndpoints.Length == 3, "id=" + (bad ?? "null") + " endpoints=" + Endpoints(saved.ProviderEndpoints));

        Check("a custom provider id is safe as a file name", Pet.ValidEndpointId("home-gpu") && !Pet.ValidEndpointId("../evil") && !Pet.ValidEndpointId("Home") && !Pet.ValidEndpointId("a/b") && !Pet.ValidEndpointId("") && !Pet.ValidEndpointId("a--b"),
            "home-gpu=" + Pet.ValidEndpointId("home-gpu") + " ../evil=" + Pet.ValidEndpointId("../evil"));
    }

    private static void TwoEndpoints(string root)
    {
        string previous = CapriDesktop.Program.Smoke;
        try
        {
            string home = Path.Combine(root, "custom-two");
            Directory.CreateDirectory(home);
            CapriDesktop.Program.Smoke = null;
            Settings saved = new Settings { VoiceProvider = "off" };
            string a = Pet.AddEndpoint(saved, "Home GPU", "http://localhost:8080");
            string b = Pet.AddEndpoint(saved, "Work GPU", "https://work.example");
            Pet pet = Fixture(saved, home);
            pet.SaveEndpointKey(a, "sk-alpha-1111");
            pet.SaveEndpointKey(b, "sk-beta-2222");
            string fileA = Path.Combine(home, "brain-custom-" + a + "-key"), fileB = Path.Combine(home, "brain-custom-" + b + "-key");
            Check("each custom provider keeps its key in its own file", File.Exists(fileA) && File.Exists(fileB) && File.ReadAllText(fileA) == "sk-alpha-1111" && File.ReadAllText(fileB) == "sk-beta-2222",
                "a=" + (File.Exists(fileA) ? File.ReadAllText(fileA) : "missing") + " b=" + (File.Exists(fileB) ? File.ReadAllText(fileB) : "missing"));

            bool usedA = pet.UseEndpoint(a, "vendor/a-1");
            Check("activating a custom provider takes its address, key and model", usedA && pet.Brain.Remote == null && pet.Brain.Server == "http://localhost:8080" && pet.Brain.ServerKey == "sk-alpha-1111" && pet.Brain.Model == "vendor/a-1" && saved.BrainProvider == "custom:home-gpu" && saved.BrainTier == 0,
                "server=" + (pet.Brain.Server ?? "null") + " key=" + (pet.Brain.ServerKey ?? "null") + " model=" + (pet.Brain.Model ?? "null") + " provider=" + (saved.BrainProvider ?? "null"));
            Check("activating a custom provider remembers its model", Pet.ProviderModel(saved, "custom:home-gpu", 0) == "vendor/a-1", "picks=" + Picks(saved.ProviderModels));

            bool usedB = pet.UseEndpoint(b, "vendor/b-1");
            Check("a second custom provider takes over with its own key", usedB && pet.Brain.Remote == null && pet.Brain.Server == "https://work.example" && pet.Brain.ServerKey == "sk-beta-2222" && pet.Brain.Model == "vendor/b-1" && saved.BrainProvider == "custom:work-gpu",
                "server=" + (pet.Brain.Server ?? "null") + " key=" + (pet.Brain.ServerKey ?? "null") + " model=" + (pet.Brain.Model ?? "null") + " provider=" + (saved.BrainProvider ?? "null"));
            Check("both custom providers keep their own model", Pet.ProviderModel(saved, "custom:home-gpu", 0) == "vendor/a-1" && Pet.ProviderModel(saved, "custom:work-gpu", 0) == "vendor/b-1",
                "picks=" + Picks(saved.ProviderModels));

            bool backA = pet.UseEndpoint(a, null);
            Check("switching back to a custom provider restores its model", backA && pet.Brain.Server == "http://localhost:8080" && pet.Brain.ServerKey == "sk-alpha-1111" && pet.Brain.Model == "vendor/a-1" && saved.BrainProvider == "custom:home-gpu",
                "server=" + (pet.Brain.Server ?? "null") + " key=" + (pet.Brain.ServerKey ?? "null") + " model=" + (pet.Brain.Model ?? "null"));

            bool againB = pet.UseEndpoint(b, null);
            Check("switching back to the second custom provider restores its model", againB && pet.Brain.Server == "https://work.example" && pet.Brain.ServerKey == "sk-beta-2222" && pet.Brain.Model == "vendor/b-1" && saved.BrainProvider == "custom:work-gpu",
                "model=" + (pet.Brain.Model ?? "null") + " provider=" + (saved.BrainProvider ?? "null"));

            bool escaped = pet.RemoveEndpoint("../evil");
            string escapedFile = pet.EndpointKeyFile("../evil");
            Check("a hand-written id cannot reach a key file outside the folder", !escaped && escapedFile == null && saved.ProviderEndpoints.Length == 2,
                "remove=" + escaped + " file=" + (escapedFile ?? "null"));

            bool removed = pet.RemoveEndpoint(b);
            Check("removing the active custom provider drops only its key and model", removed && saved.ProviderEndpoints != null && saved.ProviderEndpoints.Length == 1 && saved.ProviderEndpoints[0].Id == a && !File.Exists(fileB) && File.ReadAllText(fileA) == "sk-alpha-1111" && Pet.ProviderModel(saved, "custom:work-gpu", 0) == null && Pet.ProviderModel(saved, "custom:home-gpu", 0) == "vendor/a-1" && saved.BrainProvider == null && pet.Brain.Server == null && pet.Brain.ServerKey == null && pet.Brain.Model == null,
                "endpoints=" + Endpoints(saved.ProviderEndpoints) + " provider=" + (saved.BrainProvider ?? "null") + " server=" + (pet.Brain.Server ?? "null"));

            bool restoredA = pet.UseEndpoint(a, null);
            Check("the custom provider left after a removal still works", restoredA && pet.Brain.Server == "http://localhost:8080" && pet.Brain.ServerKey == "sk-alpha-1111" && pet.Brain.Model == "vendor/a-1" && saved.BrainProvider == "custom:home-gpu",
                "model=" + (pet.Brain.Model ?? "null") + " provider=" + (saved.BrainProvider ?? "null"));

            string notice;
            bool blocked;
            Settings reloaded = Pet.ReadSettings(Path.Combine(home, "cross.json"), out notice, out blocked);
            Check("the removed custom provider stays gone after a reload", notice == null && !blocked && reloaded.ProviderEndpoints != null && reloaded.ProviderEndpoints.Length == 1 && reloaded.ProviderEndpoints[0].Id == a && Pet.ProviderModel(reloaded, "custom:home-gpu", 0) == "vendor/a-1" && Pet.ProviderModel(reloaded, "custom:work-gpu", 0) == null && reloaded.BrainProvider == "custom:home-gpu",
                "notice=" + (notice ?? "null") + " endpoints=" + Endpoints(reloaded.ProviderEndpoints) + " provider=" + (reloaded.BrainProvider ?? "null"));
        }
        finally { CapriDesktop.Program.Smoke = previous; }
    }

    private static void EndpointIdentity()
    {
        string[] kinds = { "nvidia", "google", "openrouter", "anthropic", "openai", "deepseek", "claudecode", "codex", "local" };
        List<string> wrong = new List<string>();
        foreach (string kind in kinds) if (Pet.CustomProvider(kind)) wrong.Add(kind);
        Check("no key kind is read as a custom provider", wrong.Count == 0, "read=" + (wrong.Count == 0 ? "none" : string.Join(",", wrong.ToArray())));

        string provider = Pet.EndpointProviderId("home-gpu");
        Check("a custom provider's id is outside every key kind", provider == "custom:home-gpu" && Array.IndexOf(kinds, provider) < 0, "id=" + (provider ?? "null"));
        Check("a custom provider's id carries its endpoint", Pet.CustomProvider("custom:home-gpu") && Pet.EndpointIdOf("custom:home-gpu") == "home-gpu" && Pet.EndpointProviderId("home-gpu") == "custom:home-gpu",
            "endpoint=" + (Pet.EndpointIdOf("custom:home-gpu") ?? "null"));
        Check("a key kind or garbage names no custom provider", Pet.EndpointIdOf("openai") == null && Pet.EndpointIdOf("custom:") == null && Pet.EndpointIdOf("custom:../evil") == null && Pet.EndpointIdOf("custom:Home") == null && !Pet.CustomProvider(null),
            "openai=" + (Pet.EndpointIdOf("openai") ?? "null") + " empty=" + (Pet.EndpointIdOf("custom:") ?? "null"));

        Settings memory = new Settings { ProviderModels = new[] { new ProviderModelPick { Provider = "custom:home-gpu", Model = "vendor/a", Tier = 0 } } };
        Pet.RememberProviderModel(memory, "custom:work-gpu", "vendor/b", 0);
        Check("a custom provider's model cannot be taken by another id", Pet.ProviderModel(memory, "custom:home-gpu", 0) == "vendor/a" && Pet.ProviderModel(memory, "custom:work-gpu", 0) == "vendor/b" && Pet.ProviderModel(memory, "custom:../evil", 0) == null && Pet.ProviderModel(memory, "home-gpu", 0) == null,
            "picks=" + Picks(memory.ProviderModels));

        Check("a custom provider still names its own model and no tier", Pet.ProviderKind(new Settings { BrainProvider = "custom:home-gpu" }, null) == "custom:home-gpu" && Pet.ProviderModel(memory, "custom:home-gpu") == "vendor/a",
            "provider=" + (Pet.ProviderKind(new Settings { BrainProvider = "custom:home-gpu" }, null) ?? "null"));
    }

    private static void EndpointActivationPath(string root)
    {
        string previous = CapriDesktop.Program.Smoke;
        try
        {
            string home = Path.Combine(root, "custom-activation");
            Directory.CreateDirectory(home);
            CapriDesktop.Program.Smoke = null;
            Settings saved = new Settings { VoiceProvider = "off" };
            string id = Pet.AddEndpoint(saved, "Home GPU", "http://localhost:8080");
            Pet pet = Fixture(saved, home);
            pet.SaveEndpointKey(id, "sk-custom-alpha");
            string kind = Cloud.KindOf("sk-custom-alpha");
            bool used = pet.UseEndpoint(id, "vendor/custom-1");
            Check("activating a custom provider leaves the key's kind what it was", kind == "openai" && Cloud.KindOf("sk-custom-alpha") == kind && new Cloud("sk-custom-alpha", "vendor/custom-1").Kind == kind,
                "kind=" + (kind ?? "null") + " now=" + (Cloud.KindOf("sk-custom-alpha") ?? "null"));
            Check("activating a custom provider uses the server path, not a cloud", used && pet.Brain.Remote == null && pet.Brain.Server == "http://localhost:8080" && pet.Brain.ServerKey == "sk-custom-alpha" && pet.Brain.Model == "vendor/custom-1",
                "remote=" + (pet.Brain.Remote == null ? "null" : pet.Brain.Remote.Kind) + " server=" + (pet.Brain.Server ?? "null") + " model=" + (pet.Brain.Model ?? "null"));

            Pet direct = Fixture(new Settings { VoiceProvider = "off" }, home);
            direct.Brain.SetServer("http://localhost:8080", "sk-custom-alpha");
            direct.Brain.Model = "vendor/custom-1";
            Check("a custom provider connects where today's server path connects", pet.Brain.Server == direct.Brain.Server && pet.Brain.ServerKey == direct.Brain.ServerKey && pet.Brain.Model == direct.Brain.Model && pet.Brain.Remote == direct.Brain.Remote,
                "server=" + (pet.Brain.Server ?? "null") + " model=" + (pet.Brain.Model ?? "null") + " remote=" + (pet.Brain.Remote == null ? "null" : pet.Brain.Remote.Kind));
        }
        finally { CapriDesktop.Program.Smoke = previous; }
    }

    private static void EndpointStartup(string root)
    {
        string previous = CapriDesktop.Program.Smoke;
        try
        {
            string home = Path.Combine(root, "custom-startup");
            Directory.CreateDirectory(home);
            CapriDesktop.Program.Smoke = null;
            Settings saved = new Settings
            {
                VoiceProvider = "off",
                BrainTier = 0,
                BrainProvider = "custom:home",
                ProviderEndpoints = new[] { new ProviderEndpoint { Id = "home", Name = "Home", Address = "http://localhost:8080" } },
                ProviderModels = new[] { new ProviderModelPick { Provider = "custom:home", Model = "vendor/home", Tier = 0 } }
            };
            WriteSettings(home, saved, saved);
            File.WriteAllText(Path.Combine(home, "brain-custom-home-key"), "sk-home-key");
            string main = Path.Combine(home, "cross.json"), sidecar = Path.Combine(home, "cross-new.json");
            string beforeMain = File.ReadAllText(main), beforeSidecar = File.ReadAllText(sidecar);
            string notice;
            bool blocked;
            Settings loaded = Pet.ReadSettings(main, out notice, out blocked);
            Pet pet = Fixture(loaded, home);
            Call(pet, "RestoreBrainTier");
            Call(pet, "RestoreCustomEndpoint");
            Check("startup reconnects the saved custom provider", notice == null && !blocked && pet.Brain.Remote == null && pet.Brain.Server == "http://localhost:8080" && pet.Brain.ServerKey == "sk-home-key" && pet.Brain.Model == "vendor/home" && loaded.BrainProvider == "custom:home",
                "server=" + (pet.Brain.Server ?? "null") + " key=" + (pet.Brain.ServerKey ?? "null") + " model=" + (pet.Brain.Model ?? "null"));
            Check("startup reconnecting writes no settings file", File.ReadAllText(main) == beforeMain && File.ReadAllText(sidecar) == beforeSidecar,
                "main=" + (File.ReadAllText(main) == beforeMain) + " sidecar=" + (File.ReadAllText(sidecar) == beforeSidecar));
        }
        finally { CapriDesktop.Program.Smoke = previous; }
    }

    internal static int Run(string root)
    {
        failures = 0;
        string custom = Path.Combine(root, "custom-settings");
        Directory.CreateDirectory(custom);
        EndpointSettings(custom);
        EndpointIds();
        TwoEndpoints(root);
        EndpointIdentity();
        EndpointActivationPath(root);
        EndpointStartup(root);
        return failures;
    }
}

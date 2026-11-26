using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using CapriDesktop;

internal static class NativeReviewChecks
{
    private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static int failures;

    private static void Check(string name, bool ok, string shown)
    {
        Console.WriteLine("{0} {1} [{2}]", ok ? "PASS" : "FAIL", name, shown);
        if (!ok) failures++;
    }

    private static void Skip(string name) { Console.WriteLine("SKIP " + name); }

    private static void Set(Pet p, string name, object value) { typeof(Pet).GetField(name, Fields).SetValue(p, value); }
    private static void Call(Pet p, string name) { typeof(Pet).GetMethod(name, Fields, null, Type.EmptyTypes, null).Invoke(p, null); }
    private static string Hash(string path) { return File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : "missing"; }

    private static string Hashes(string d)
    {
        return Hash(Path.Combine(d, "cross.json")) + Hash(Path.Combine(d, "cross-new.json")) +
            Hash(Path.Combine(d, "brain-custom-a-key")) + Hash(Path.Combine(d, "brain-custom-b-key")) +
            Hash(Path.Combine(d, "brain-custom-remote-key"));
    }

    private static Pet Fixture(Settings s, string d)
    {
        Directory.CreateDirectory(d);
        Pet p = (Pet)RuntimeHelpers.GetUninitializedObject(typeof(Pet));
        p.Saved = s; Set(p, "Brain", new Brain()); Set(p, "folder", d);
        Set(p, "accountGate", new object()); Set(p, "remoteGate", new object());
        Set(p, "remoteBrains", new Dictionary<string, Func<bool>>(StringComparer.Ordinal));
        Set(p, "nameEars", new OwnedNameEars()); Set(p, "voiceRecovery", new VoiceActionRecovery());
        Set(p, "nameStatus", "Name listening stopped. Turn Listen for Capri on to resume."); Set(p, "body", new Body());
        return p;
    }

    private static Settings Load(string d)
    {
        string notice; bool blocked;
        return Pet.ReadSettings(Path.Combine(d, "cross.json"), out notice, out blocked);
    }

    private static void UnsafeKeyRestore(string root)
    {
        string d = Path.Combine(root, "unsafe-restore");
        Settings s = new Settings { VoiceProvider = "off" };
        string id = Pet.AddEndpoint(s, "Remote", "http://example.com");
        Pet p = Fixture(s, d);
        CapriDesktop.Program.Smoke = null;
        p.SaveEndpointKey(id, null);
        p.UseEndpoint(id, "model-remote");
        p.SaveEndpointKey(id, "sk-unsafe");
        string keyFile = Path.Combine(d, "brain-custom-remote-key");
        CapriDesktop.Program.Smoke = d;
        try { p.SetBrainTier(2); p.SetBrainTier(0); } finally { CapriDesktop.Program.Smoke = null; }
        Check("an unsafe saved key is dropped when the custom tier returns",
            p.Brain.Server == "http://example.com" && p.Brain.ServerKey == null && p.Brain.Model == "model-remote" && s.BrainProvider == "custom:remote" && File.ReadAllText(keyFile) == "sk-unsafe",
            "server=" + (p.Brain.Server ?? "null") + " key=" + (p.Brain.ServerKey ?? "null") + " model=" + (p.Brain.Model ?? "null") + " provider=" + (s.BrainProvider ?? "null"));

        Pet restart = Fixture(Load(d), d);
        Call(restart, "RestoreBrainTier");
        Call(restart, "RestoreCustomEndpoint");
        Check("an unsafe saved key is dropped when the custom brain starts",
            restart.Brain.Server == "http://example.com" && restart.Brain.ServerKey == null && restart.Brain.Model == "model-remote",
            "server=" + (restart.Brain.Server ?? "null") + " key=" + (restart.Brain.ServerKey ?? "null") + " model=" + (restart.Brain.Model ?? "null"));
    }

    private static void BlockedActivation(string root)
    {
        string d = Path.Combine(root, "blocked-activation");
        Settings s = new Settings { VoiceProvider = "off" };
        string a = Pet.AddEndpoint(s, "A", "https://a.example");
        string b = Pet.AddEndpoint(s, "B", "https://b.example");
        Pet p = Fixture(s, d);
        CapriDesktop.Program.Smoke = null;
        p.SaveEndpointKey(a, "sk-a");
        p.SaveEndpointKey(b, "sk-b");
        p.UseEndpoint(a, "model-a");
        bool used;
        using (new FileStream(Path.Combine(d, "cross-new.json"), FileMode.Open, FileAccess.Read, FileShare.None)) used = p.UseEndpoint(b, "model-b");
        Check("a blocked activation reports failure and keeps the previous selection",
            !used && s.BrainProvider == "custom:a" && p.Brain.Server == "https://a.example" && p.Brain.ServerKey == "sk-a" && p.Brain.Model == "model-a",
            "used=" + used + " provider=" + (s.BrainProvider ?? "null") + " server=" + (p.Brain.Server ?? "null") + " key=" + (p.Brain.ServerKey ?? "null") + " model=" + (p.Brain.Model ?? "null"));

        Settings loaded = Load(d);
        Check("a blocked activation leaves the previous selection saved",
            loaded.BrainProvider == "custom:a" && Pet.ProviderModel(loaded, "custom:a", 0) == "model-a" && Pet.ProviderModel(loaded, "custom:b", 0) == null,
            "provider=" + (loaded.BrainProvider ?? "null") + " a=" + (Pet.ProviderModel(loaded, "custom:a", 0) ?? "null") + " b=" + (Pet.ProviderModel(loaded, "custom:b", 0) ?? "null"));
    }

    private static void RemovalOrder(string root)
    {
        string d = Path.Combine(root, "removal-order");
        Settings s = new Settings { VoiceProvider = "off" };
        string a = Pet.AddEndpoint(s, "A", "https://a.example");
        string b = Pet.AddEndpoint(s, "B", "https://b.example");
        Pet p = Fixture(s, d);
        CapriDesktop.Program.Smoke = null;
        p.SaveEndpointKey(a, "sk-a");
        p.SaveEndpointKey(b, "sk-b");
        p.UseEndpoint(a, "model-a");
        string keyFile = Path.Combine(d, "brain-custom-b-key");
        bool present = false;
        int seen = 0;
        p.SettingsChanged += () => { seen++; present = File.Exists(keyFile); };
        bool removed = p.RemoveEndpoint(b);
        Check("a removal saves the settings before it deletes the key",
            removed && seen == 1 && present && !File.Exists(keyFile),
            "removed=" + removed + " seen=" + seen + " key-at-save=" + present + " key-after=" + File.Exists(keyFile));
    }

    private static void FailedKeyRemoval(string root)
    {
        if (!PermissionProbe.Enforced()) { Skip("a failed key removal keeps the endpoint and its key"); return; }
        string d = Path.Combine(root, "failed-key-removal");
        Settings s = new Settings { VoiceProvider = "off" };
        Pet.AddEndpoint(s, "A", "https://a.example");
        Pet.AddEndpoint(s, "B", "https://b.example");
        Pet p = Fixture(s, d);
        CapriDesktop.Program.Smoke = null;
        p.SaveEndpointKey("a", "sk-a");
        p.SaveEndpointKey("b", "sk-b");
        p.UseEndpoint("a", "model-a");
        string before = Hashes(d);
        UnixFileMode mode = File.GetUnixFileMode(d);
        File.SetUnixFileMode(d, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        bool removed;
        try { removed = p.RemoveEndpoint("b"); } finally { File.SetUnixFileMode(d, mode); }
        Settings loaded = Load(d);
        bool keyWhole = File.Exists(Path.Combine(d, "brain-custom-b-key")) && File.ReadAllText(Path.Combine(d, "brain-custom-b-key")) == "sk-b";
        Check("a failed key removal keeps the endpoint and its key",
            !removed && Hashes(d) == before && Pet.EndpointFor(loaded, "b") != null && keyWhole && s.BrainProvider == "custom:a" && p.Brain.Server == "https://a.example" && p.Brain.ServerKey == "sk-a",
            "removed=" + removed + " bytes=" + (Hashes(d) == before) + " endpoint=" + (Pet.EndpointFor(loaded, "b") != null) + " key=" + keyWhole + " provider=" + (s.BrainProvider ?? "null"));
    }

    private static void AtomicKeyWrite(string root)
    {
        string d = Path.Combine(root, "atomic-key");
        Settings s = new Settings { VoiceProvider = "off" };
        string a = Pet.AddEndpoint(s, "A", "https://a.example");
        Pet p = Fixture(s, d);
        CapriDesktop.Program.Smoke = null;
        p.SaveEndpointKey(a, "sk-old");
        Directory.CreateDirectory(Path.Combine(d, "brain-custom-a-key.new"));
        bool written = p.SaveEndpointKey(a, "sk-new");
        Check("a failed key write leaves the old key whole",
            !written && p.ReadEndpointKey(a) == "sk-old" && File.ReadAllText(Path.Combine(d, "brain-custom-a-key")) == "sk-old",
            "written=" + written + " key=" + (p.ReadEndpointKey(a) ?? "null"));
    }

    private static void FailedPlanActivation(string root)
    {
        string d = Path.Combine(root, "failed-plan-activation");
        Settings s = new Settings { VoiceProvider = "off" };
        Pet.AddEndpoint(s, "A", "https://a.example");
        Pet.AddEndpoint(s, "B", "https://b.example");
        Pet p = Fixture(s, d);
        CapriDesktop.Program.Smoke = null;
        p.SaveEndpointKey("a", "sk-a");
        p.SaveEndpointKey("b", "sk-b");
        p.UseEndpoint("a", "model-a");

        s.PlanPython = "not-executed";
        s.PlanAccounts = d;
        s.PlanModel = "plan-model";
        s.NativePlan = true;
        PlanBrain plan = new PlanBrain(s.PlanPython, s.PlanAccounts, s.PlanModel, null, null, null, null, null);
        p.Brain.Provider = plan;
        p.Brain.Model = s.PlanModel;
        p.Save();

        string before = Hashes(d);
        FileStream gate = null;
        p.SettingsChanged += () => { gate = new FileStream(Path.Combine(d, "cross-new.json"), FileMode.Open, FileAccess.Read, FileShare.None); };
        bool used;
        try { used = p.UseEndpoint("b", "model-b"); } finally { if (gate != null) gate.Dispose(); }

        Check("failed activation retains the active plan",
            !used && ReferenceEquals(p.Brain.Provider, plan) && s.NativePlan && p.Brain.Model == "plan-model" && Hashes(d) == before,
            "used=" + used + " native=" + s.NativePlan + " provider-null=" + (p.Brain.Provider == null));
    }

    private static void ConcurrentSidecarSurvivesFailure(string root)
    {
        string d = Path.Combine(root, "concurrent-sidecar");
        Settings s = new Settings { VoiceProvider = "off" };
        Pet.AddEndpoint(s, "A", "https://a.example");
        Pet.AddEndpoint(s, "B", "https://b.example");
        Pet p = Fixture(s, d);
        CapriDesktop.Program.Smoke = null;
        p.SaveEndpointKey("a", "sk-a");
        p.SaveEndpointKey("b", "sk-b");
        p.UseEndpoint("a", "model-a");

        string side = Path.Combine(d, "cross-new.json");
        string main = Path.Combine(d, "cross.json");
        string external = "{\"BrainProvider\":\"custom:a\",\"FreeModel\":\"concurrent-save\"}";
        FileStream gate = null;
        p.SettingsChanged += () => { File.WriteAllText(side, external); gate = new FileStream(main, FileMode.Open, FileAccess.Read, FileShare.None); };
        bool used;
        try { used = p.UseEndpoint("b", "model-b"); } finally { if (gate != null) gate.Dispose(); }

        bool retained = File.ReadAllText(side) == external;
        Check("failed activation retains concurrent sidecar bytes", !used && retained, "used=" + used + " retained=" + retained);
    }

    internal static int Run(string root)
    {
        failures = 0;
        string previous = CapriDesktop.Program.Smoke;
        try
        {
            string folder = Path.Combine(root, "native-review");
            Directory.CreateDirectory(folder);
            UnsafeKeyRestore(folder);
            BlockedActivation(folder);
            RemovalOrder(folder);
            FailedKeyRemoval(folder);
            AtomicKeyWrite(folder);
            FailedPlanActivation(folder);
            ConcurrentSidecarSurvivesFailure(folder);
        }
        finally { CapriDesktop.Program.Smoke = previous; }
        return failures;
    }
}

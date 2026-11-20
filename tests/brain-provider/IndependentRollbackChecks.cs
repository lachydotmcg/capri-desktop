using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using CapriDesktop;

internal static class IndependentRollbackChecks
{
    private static int failures;
    private static void Set(Pet p, string name, object value) { typeof(Pet).GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(p, value); }
    private static string Hash(string p) { return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))); }
    private static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name + " " + detail);
        if (!ok) failures++;
    }
    private static Pet Fixture(Settings s, string d)
    {
        Directory.CreateDirectory(d);
        Pet p = (Pet)RuntimeHelpers.GetUninitializedObject(typeof(Pet));
        p.Saved = s; Set(p, "Brain", new Brain()); Set(p, "folder", d);
        Set(p, "accountGate", new object()); Set(p, "remoteGate", new object());
        Set(p, "remoteBrains", new Dictionary<string, Func<bool>>(StringComparer.Ordinal));
        Set(p, "nameEars", new OwnedNameEars()); Set(p, "voiceRecovery", new VoiceActionRecovery());
        Set(p, "nameStatus", "Name listening stopped."); Set(p, "body", new Body());
        return p;
    }
    private static Pet Setup(string d)
    {
        Settings s = new Settings { VoiceProvider = "off" };
        Pet.AddEndpoint(s, "Alpha", "https://alpha.example");
        Pet.AddEndpoint(s, "Beta", "https://beta.example");
        Pet p = Fixture(s, d);
        if (!p.SaveEndpointKey("alpha", "alpha-secret") || !p.SaveEndpointKey("beta", "beta-secret") || !p.UseEndpoint("alpha", "alpha-model")) throw new Exception("fixture setup failed");
        return p;
    }
    private static string KeyHashes(string d)
    {
        return Hash(Path.Combine(d, "brain-custom-alpha-key")) + Hash(Path.Combine(d, "brain-custom-beta-key"));
    }
    private static void PlanFailure(string d)
    {
        Pet p = Setup(d); Settings s = p.Saved;
        s.PlanPython = "not-executed"; s.PlanAccounts = d; s.PlanModel = "independent-plan"; s.NativePlan = true;
        PlanBrain plan = new PlanBrain(s.PlanPython, s.PlanAccounts, s.PlanModel, null, null, null, null, null);
        p.Brain.Provider = plan; p.Brain.Model = s.PlanModel; p.Save();
        string main = Path.Combine(d, "cross.json"), side = Path.Combine(d, "cross-new.json");
        string beforeMain = Hash(main), beforeSide = Hash(side), beforeKeys = KeyHashes(d);
        FileStream gate = null;
        p.SettingsChanged += () => gate = new FileStream(side, FileMode.Open, FileAccess.Read, FileShare.None);
        bool used;
        try { used = p.UseEndpoint("beta", "beta-model"); } finally { if (gate != null) gate.Dispose(); }
        bool provider = ReferenceEquals(p.Brain.Provider, plan), model = p.Brain.Model == "independent-plan";
        bool mainSame = Hash(main) == beforeMain, sideSame = Hash(side) == beforeSide, keysSame = KeyHashes(d) == beforeKeys;
        Check("F2a independent", !used && provider && s.NativePlan && model && mainSame && sideSame && keysSame,
            "used=" + used + " provider=" + provider + " native=" + s.NativePlan + " model=" + model + " main=" + mainSame + " side=" + sideSame + " keys=" + keysSame);
    }
    private static void ConcurrentFailure(string d, bool remove)
    {
        Pet p = Setup(d);
        string main = Path.Combine(d, "cross.json"), side = Path.Combine(d, "cross-new.json");
        string beforeMain = Hash(main), beforeSide = Hash(side), beforeKeys = KeyHashes(d);
        Pet.NewSettings newer = Pet.Newest(p.Saved); newer.FreeModel = "independent-external-save";
        byte[] external;
        using (MemoryStream stream = new MemoryStream()) { new DataContractJsonSerializer(typeof(Pet.NewSettings)).WriteObject(stream, newer); external = stream.ToArray(); }
        string expected = Convert.ToHexString(SHA256.HashData(external));
        FileStream gate = null;
        p.SettingsChanged += () => { File.WriteAllBytes(side, external); gate = new FileStream(main, FileMode.Open, FileAccess.Read, FileShare.None); };
        bool done;
        try { done = remove ? p.RemoveEndpoint("beta") : p.UseEndpoint("beta", "beta-model"); } finally { if (gate != null) gate.Dispose(); }
        bool sideSame = Hash(side) == expected && expected != beforeSide, mainSame = Hash(main) == beforeMain, keysSame = KeyHashes(d) == beforeKeys;
        string notice; bool blocked;
        Settings loaded = Pet.ReadSettings(main, out notice, out blocked); Pet restarted = Fixture(loaded, d);
        bool reload = notice == null && !blocked && Pet.EndpointFor(loaded, "alpha") != null && Pet.EndpointFor(loaded, "beta") != null && loaded.BrainProvider == "custom:alpha" && Pet.ProviderModel(loaded, "custom:alpha", 0) == "alpha-model" && restarted.ReadEndpointKey("alpha") == "alpha-secret" && restarted.ReadEndpointKey("beta") == "beta-secret";
        bool live = p.Brain.Server == "https://alpha.example" && p.Brain.ServerKey == "alpha-secret" && p.Brain.Model == "alpha-model";
        Check(remove ? "F2b removal independent" : "F2b activation independent", !done && sideSame && mainSame && keysSame && reload && live,
            "done=" + done + " external=" + sideSame + " main=" + mainSame + " keys=" + keysSame + " reload=" + reload + " live=" + live);
    }
    internal static int Run(string root)
    {
        failures = 0; string previous = CapriDesktop.Program.Smoke; CapriDesktop.Program.Smoke = null;
        try { PlanFailure(Path.Combine(root, "plan")); ConcurrentFailure(Path.Combine(root, "activation"), false); ConcurrentFailure(Path.Combine(root, "removal"), true); }
        finally { CapriDesktop.Program.Smoke = previous; }
        return failures;
    }
}
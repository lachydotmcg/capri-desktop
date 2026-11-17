using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using CapriDesktop;

internal static class CustomSafetyChecks
{
    private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static int failures;
    private static void Check(string name, bool ok)
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
        if (!ok) failures++;
    }
    private static void Skip(string name) { Console.WriteLine("SKIP " + name); }
    private static void Set(Pet p, string name, object value) { typeof(Pet).GetField(name, Fields).SetValue(p, value); }
    private static void Call(Pet p, string name) { typeof(Pet).GetMethod(name, Fields, null, Type.EmptyTypes, null).Invoke(p, null); }
    private static string Hash(string path) { return File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : "missing"; }
    private static string Hashes(string d)
    {
        return Hash(Path.Combine(d, "cross.json")) + Hash(Path.Combine(d, "cross-new.json")) +
            Hash(Path.Combine(d, "brain-custom-a-key")) + Hash(Path.Combine(d, "brain-custom-b-key"));
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
    private static Settings Setup(string d, out Pet p, bool shared = false)
    {
        Settings s = new Settings { VoiceProvider = "off" };
        Pet.AddEndpoint(s, "A", "http://localhost:8080");
        Pet.AddEndpoint(s, "B", shared ? "http://localhost:8080" : "https://other.example");
        p = Fixture(s, d); p.SaveEndpointKey("a", "sk-alpha"); p.SaveEndpointKey("b", "sk-beta"); p.Save();
        return s;
    }
    private static Settings Load(string d)
    {
        string notice; bool blocked;
        return Pet.ReadSettings(Path.Combine(d, "cross.json"), out notice, out blocked);
    }
    internal static int Run(string root)
    {
        failures = 0;
        string previous = CapriDesktop.Program.Smoke, log = Brain.LogPath;
        string d = Path.Combine(root, "custom-safety"); Directory.CreateDirectory(d);
        CapriDesktop.Program.Smoke = null; Brain.LogPath = Path.Combine(d, "errors.txt");
        try
        {
            Pet p; Settings s;
            string ids = Path.Combine(d, "ids"); s = Setup(ids, out p);
            string before = Hashes(ids), sentinel = Path.Combine(d, "x"); File.WriteAllText(sentinel, "sentinel");
            string sentinelHash = Hash(sentinel);
            foreach (string id in new[] { "custom:../x", "../x", "A", "", new string('a', 33), "a--b", "café", "-a", "a-" })
                Check("unsafe endpoint id rejected " + id,
                    !Pet.ValidEndpointId(id) && !p.SaveEndpointKey(id, "oops") && !p.RemoveEndpoint(id) && !p.UseEndpoint(id, "model") &&
                    p.EndpointKeyFile(id) == null && Hashes(ids) == before && Hash(sentinel) == sentinelHash);
            Check("32-char id valid; separate key files", Pet.ValidEndpointId(new string('a', 32)) && p.ReadEndpointKey("a") == "sk-alpha" && p.ReadEndpointKey("b") == "sk-beta");

            string a = Path.Combine(ids, "brain-custom-a-key"); bool written;
            using (new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.None)) written = p.SaveEndpointKey("a", "sk-replacement");
            Check("failed key overwrite reports failure and preserves bytes", !written && Hashes(ids) == before && p.ReadEndpointKey("a") == "sk-alpha");

            string unsafeId = Pet.AddEndpoint(s, "Unsafe", "http://example.com");
            p.SaveEndpointKey(unsafeId, "sk-unsafe"); p.Save(); before = Hashes(ids);
            string unsafeHash = Hash(p.EndpointKeyFile(unsafeId));
            Check("unsafe remote HTTP rejects keyed activation without writes", !p.UseEndpoint(unsafeId, "model") && Hashes(ids) == before && Hash(p.EndpointKeyFile(unsafeId)) == unsafeHash);

            if (!OperatingSystem.IsWindows())
            {
                if (!PermissionProbe.Enforced()) Skip("failed key deletion reports failure and preserves endpoint");
                else
                {
                    string blockedDir = Path.Combine(d, "remove-blocked"); s = Setup(blockedDir, out p); before = Hashes(blockedDir);
                    bool removed; UnixFileMode mode = File.GetUnixFileMode(blockedDir);
                    File.SetUnixFileMode(blockedDir, UnixFileMode.UserRead | UnixFileMode.UserExecute);
                    try { removed = p.RemoveEndpoint("a"); }
                    finally { File.SetUnixFileMode(blockedDir, mode); }
                    Check("failed key deletion reports failure and preserves endpoint", !removed && Pet.EndpointFor(s, "a") != null && Hashes(blockedDir) == before);
                }
            }

            string shared = Path.Combine(d, "shared"); s = Setup(shared, out p, true);
            p.UseEndpoint("a", "model-a"); p.UseEndpoint("b", "model-b"); string hashB = Hash(Path.Combine(shared, "brain-custom-b-key"));
            bool deleted = p.RemoveEndpoint("a");
            Check("removing shared-address inactive endpoint keeps active identity", deleted && p.Brain.Server == "http://localhost:8080" && p.Brain.ServerKey == "sk-beta" &&
                p.Brain.Model == "model-b" && s.BrainProvider == "custom:b" && Hash(Path.Combine(shared, "brain-custom-b-key")) == hashB && Pet.ProviderModel(s, "custom:b", 0) == "model-b");

            string models = Path.Combine(d, "models"); s = Setup(models, out p);
            p.UseEndpoint("a", "model-a-1"); p.PickServerModel("model-a-2");
            bool remembered = Pet.ProviderModel(s, "custom:a", 0) == "model-a-2";
            p.UseEndpoint("b", "model-b"); p.UseEndpoint("a", null);
            Pet restart = Fixture(Load(models), models); Call(restart, "RestoreBrainTier"); Call(restart, "RestoreCustomEndpoint");
            Check("server picker keeps custom model across switch and restart", remembered && p.Brain.Model == "model-a-2" && restart.Brain.Model == "model-a-2");

            string tiers = Path.Combine(d, "tiers"); s = Setup(tiers, out p); s.ServerModel = "legacy-model";
            p.UseEndpoint("a", "custom-model"); s.FreeModel = "free-model"; File.WriteAllText(Path.Combine(tiers, "brain-free-key"), "nvapi-free");
            CapriDesktop.Program.Smoke = tiers;
            try { p.SetBrainTier(1); p.SetBrainTier(0); }
            finally { CapriDesktop.Program.Smoke = null; }
            Check("tier round-trip keeps custom identity and model", p.Brain.Server == "http://localhost:8080" && p.Brain.Model == "custom-model" && s.BrainProvider == "custom:a");

            string blank = Path.Combine(d, "blank"); s = Setup(blank, out p); p.SaveEndpointKey("a", null);
            before = Hashes(blank); bool accepted = p.UseEndpoint("a", null); string after = Hashes(blank);
            restart = Fixture(Load(blank), blank); Call(restart, "RestoreBrainTier"); Call(restart, "RestoreCustomEndpoint");
            Check("accepted keyless model-less endpoint reconnects without writes", (!accepted && after == before) || (accepted && restart.Brain.Server == "http://localhost:8080" && restart.Brain.ServerKey == null && Hashes(blank) == after));

            string start = Path.Combine(d, "start"); s = Setup(start, out p); p.UseEndpoint("a", "model-a"); before = Hashes(start);
            restart = Fixture(Load(start), start); Call(restart, "RestoreBrainTier"); Call(restart, "RestoreCustomEndpoint");
            Check("keyed custom startup reconnects without writes", restart.Brain.Server == "http://localhost:8080" && restart.Brain.ServerKey == "sk-alpha" && restart.Brain.Model == "model-a" && Hashes(start) == before);
            p.SaveEndpointKey("a", null); before = Hashes(start);
            restart = Fixture(Load(start), start); Call(restart, "RestoreBrainTier"); Call(restart, "RestoreCustomEndpoint");
            Check("keyless custom startup reconnects without writes", restart.Brain.Server == "http://localhost:8080" && restart.Brain.ServerKey == null && restart.Brain.Model == "model-a" && Hashes(start) == before);
            File.WriteAllText(Path.Combine(start, "cross.json"), "{\"VoiceProvider\":\"off\",\"Nickname\":\"old\"}");
            Settings older = Load(start);
            Check("older copy recovers named endpoint and tier-zero memory", older.Nickname == "old" && Pet.EndpointFor(older, "a") != null && Pet.ProviderModel(older, "custom:a", 0) == "model-a");
            Settings empty = Pet.Kept(new Settings { VoiceProvider = "off", ProviderEndpoints = Array.Empty<ProviderEndpoint>() }, Pet.Newest(s));
            Check("present empty endpoint list is not recovered", empty.ProviderEndpoints != null && empty.ProviderEndpoints.Length == 0);
            Check("KindOf vocabulary unchanged", Cloud.KindOf("sk-custom-alpha") == "openai" && Cloud.KindOf("nvapi-x") == "nvidia" && Cloud.KindOf(null) == null);

            string cloud = Path.Combine(d, "cloud"); s = Setup(cloud, out p); s.BrainTier = 1;
            p.Brain.Remote = new Cloud("nvapi-current", "free-current"); p.Brain.Model = "free-current";
            bool used = p.UseEndpoint("a", "custom-current");
            Check("activation keeps previous tier key and model", used && s.BrainTier == 0 && s.FreeModel == "free-current" && File.ReadAllText(Path.Combine(cloud, "brain-free-key")) == "nvapi-current" && Pet.ProviderModel(s, "nvidia", 1) == "free-current");

            string broken = Path.Combine(d, "failed-backup"); Directory.CreateDirectory(broken);
            string main = Path.Combine(broken, "cross.json"), newer = Path.Combine(broken, "cross-new.json"), notice; bool blocked;
            File.WriteAllText(main, "{bad"); File.WriteAllText(newer, "{bad-side"); Directory.CreateDirectory(Path.Combine(broken, Pet.UnreadableName));
            string oldMain = Hash(main), oldNewer = Hash(newer); s = Pet.ReadSettings(main, out notice, out blocked); p = Fixture(s, broken);
            Set(p, "settingsWriteBlocked", blocked); Call(p, "WriteSettings");
            Check("failed backup blocks both settings writes", blocked && Hash(main) == oldMain && Hash(newer) == oldNewer);
            string race = Path.Combine(d, "concurrent"); Directory.CreateDirectory(race); main = Path.Combine(race, "cross.json");
            File.WriteAllText(main, "{\"VoiceProvider\":\"off\",\"Nickname\":\"old\"}"); s = Pet.ReadSettings(main, out notice, out blocked); p = Fixture(s, race);
            p.Brain.Remote = new Cloud("nvapi-current", "model"); File.WriteAllText(main, "{\"VoiceProvider\":\"off\",\"Nickname\":\"newer\"}"); oldMain = Hash(main);
            Call(p, "KeepAdoptedProvider"); Check("startup adoption preserves an intervening save", s.BrainProvider == "nvidia" && Hash(main) == oldMain);

            string sidecar = Path.Combine(d, "sidecar"); s = Setup(sidecar, out p); p.UseEndpoint("a", "model-a"); p.RemoveEndpoint("b");
            string side = Path.Combine(sidecar, "cross-new.json"), sideHash = Hash(side), sideBefore = Hashes(sidecar);
            using (new FileStream(side, FileMode.Open, FileAccess.Read, FileShare.None)) deleted = p.RemoveEndpoint("a");
            Settings reloaded = Load(sidecar);
            Check("failed sidecar save cannot resurrect deleted endpoint", Hash(side) == sideHash && ((deleted && Pet.EndpointFor(reloaded, "a") == null && Pet.ProviderModel(reloaded, "custom:a", 0) == null) || (!deleted && Hashes(sidecar) == sideBefore)));

            string sameRound = Path.Combine(d, "same-address-tier");
            s = Setup(sameRound, out p, true);
            s.ServerModel = "legacy"; s.FreeModel = "free";
            File.WriteAllText(Path.Combine(sameRound, "brain-free-key"), "nvapi-free");
            p.UseEndpoint("a", "ma"); p.UseEndpoint("b", "mb");
            CapriDesktop.Program.Smoke = sameRound;
            try { p.SetBrainTier(1); p.SetBrainTier(0); } finally { CapriDesktop.Program.Smoke = null; }
            Check("tier round-trip preserves shared-address identity",
                s.BrainProvider == "custom:b" && p.Brain.Server == "http://localhost:8080" &&
                p.Brain.ServerKey == "sk-beta" && p.Brain.Model == "mb");

            if (!OperatingSystem.IsWindows())
            {
                if (!PermissionProbe.Enforced()) Skip("failed sidecar write preserves both endpoints and bytes");
                else
                {
                    string txn = Path.Combine(d, "readonly-sidecar");
                    s = Setup(txn, out p); p.UseEndpoint("a", "ma");
                    string txnBefore = Hashes(txn); string txnSide = Path.Combine(txn, "cross-new.json");
                    UnixFileMode txnMode = File.GetUnixFileMode(txnSide); bool txnRemoved;
                    File.SetUnixFileMode(txnSide, UnixFileMode.UserRead);
                    try { txnRemoved = p.RemoveEndpoint("a"); } finally { File.SetUnixFileMode(txnSide, txnMode); }
                    Settings txnLoaded = Load(txn);
                    Check("failed sidecar write preserves both endpoints and bytes",
                        !txnRemoved && Hashes(txn) == txnBefore &&
                        Pet.EndpointFor(txnLoaded, "a") != null && Pet.EndpointFor(txnLoaded, "b") != null &&
                        Pet.ProviderModel(txnLoaded, "custom:a", 0) == "ma" &&
                        s.BrainProvider == "custom:a" && p.Brain.ServerKey == "sk-alpha" && p.Brain.Model == "ma");
                }
            }
            ResidualChecks(d);
        }
        finally { CapriDesktop.Program.Smoke = previous; Brain.LogPath = log; }
        return failures;
    }

    internal static int ResidualChecks(string root)
    {
        int start = failures;
        string previous = CapriDesktop.Program.Smoke;
        CapriDesktop.Program.Smoke = null;
        try
        {
            Pet p;
            string cloud = Path.Combine(root, "remove-cloud");
            Settings s = Setup(cloud, out p, true);
            s.FreeModel = "free-model";
            File.WriteAllText(Path.Combine(cloud, "brain-free-key"), "nvapi-free");
            p.UseEndpoint("a", "model-a"); p.UseEndpoint("b", "model-b");
            CapriDesktop.Program.Smoke = cloud;
            try { p.SetBrainTier(1); } finally { CapriDesktop.Program.Smoke = null; }
            p.Save();
            string name = p.MainframeBrainName;
            MethodInfo state = typeof(Pet).GetMethod("RemoteBrainState", Fields, null, Type.EmptyTypes, null);
            string phone = (string)state.Invoke(p, null);
            bool removed = p.RemoveEndpoint("b");
            Check("removing saved custom endpoint in cloud tier preserves cloud and phone state",
                removed && p.MainframeTier == 1 && p.Brain.Remote != null && p.Brain.Model == "free-model" &&
                p.MainframeBrainName == name && (string)state.Invoke(p, null) == phone);

            if (!OperatingSystem.IsWindows())
            {
                foreach (string file in new[] { "cross.json", "cross-new.json", "late-cross-new.json" })
                {
                    if (!PermissionProbe.Enforced()) { Skip("read-only removal preserves settings, keys and both models " + file); continue; }
                    string d = Path.Combine(root, "readonly-" + file);
                    s = Setup(d, out p);
                    p.UseEndpoint("a", "model-a"); p.UseEndpoint("b", "model-b"); p.UseEndpoint("a", null);
                    string before = Hashes(d);
                    bool late = file.StartsWith("late-");
                    string path = Path.Combine(d, late ? "cross-new.json" : file);
                    UnixFileMode mode = File.GetUnixFileMode(path);
                    UnixFileMode readOnly = mode & ~(UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite);
                    int fired = 0;
                    if (late) p.SettingsChanged += () => { fired++; File.SetUnixFileMode(path, readOnly); };
                    else File.SetUnixFileMode(path, readOnly);
                    try { removed = p.RemoveEndpoint("a"); }
                    finally { File.SetUnixFileMode(path, mode); }
                    Settings loaded = Load(d);
                    Check("read-only removal preserves settings, keys and both models " + file,
                        (!late || fired == 1) && !removed && Hashes(d) == before &&
                        p.Saved.BrainProvider == "custom:a" && p.Brain.ServerKey == "sk-alpha" && p.Brain.Model == "model-a" &&
                        Pet.EndpointFor(loaded, "a") != null && Pet.EndpointFor(loaded, "b") != null &&
                        Pet.ProviderModel(loaded, "custom:a", 0) == "model-a" && Pet.ProviderModel(loaded, "custom:b", 0) == "model-b");
                }
            }
        }
        finally { CapriDesktop.Program.Smoke = previous; }
        return failures - start;
    }

    internal static int B5Extra(string root)
    {
        int start = failures; string previous = CapriDesktop.Program.Smoke; CapriDesktop.Program.Smoke = null;
        MethodInfo state = typeof(Pet).GetMethod("RemoteBrainState", Fields, null, Type.EmptyTypes, null);
        try
        {
            foreach (string[] c in new[] { new[] { "a", "b", "b", "a" }, new[] { "a", "b", "a", "b" }, new[] { "b", "a", "a", "b" } })
            {
                string d = Path.Combine(root, "b5-" + string.Join("", c)); Pet p; Settings s = Setup(d, out p, true);
                s.FreeModel = "free-model"; s.PaidModel = "paid-model"; File.WriteAllText(Path.Combine(d, "brain-free-key"), "nvapi-73692018");
                p.UseEndpoint(c[0], "model-" + c[0]); p.UseEndpoint(c[1], "model-" + c[1]);
                CapriDesktop.Program.Smoke = d; try { p.SetBrainTier(1); } finally { CapriDesktop.Program.Smoke = null; } p.Save();
                Cloud cloud = p.Brain.Remote; string name = p.MainframeBrainName, phone = (string)state.Invoke(p, null);
                string keys = Hash(Path.Combine(d, "brain-key")) + Hash(Path.Combine(d, "brain-free-key"));
                bool setup = cloud != null && cloud.Key == "nvapi-73692018" && cloud.Model == "free-model" && s.BrainProvider == "custom:" + c[1] && name == "NVIDIA · free-model" && phone.Contains(name);
                bool removed = p.RemoveEndpoint(c[2]);
                Check("B5 removal " + string.Join("", c), setup && removed && ReferenceEquals(cloud, p.Brain.Remote) && s.BrainTier == 1 && p.Brain.Model == "free-model" && cloud.Model == "free-model" && cloud.Key == "nvapi-73692018" && s.FreeModel == "free-model" && s.PaidModel == "paid-model" &&
                    p.MainframeBrainName == name && (string)state.Invoke(p, null) == phone && keys == Hash(Path.Combine(d, "brain-key")) + Hash(Path.Combine(d, "brain-free-key")) &&
                    s.BrainProvider == (c[2] == c[1] ? null : "custom:" + c[1]) && Pet.EndpointFor(s, c[2]) == null && Pet.EndpointFor(s, c[3]) != null &&
                    Pet.ProviderModel(s, "custom:" + c[2], 0) == null && Pet.ProviderModel(s, "custom:" + c[3], 0) == "model-" + c[3] && p.ReadEndpointKey(c[2]) == null && p.ReadEndpointKey(c[3]) == (c[3] == "a" ? "sk-alpha" : "sk-beta"));
                CapriDesktop.Program.Smoke = d; try { p.SetBrainTier(0); } finally { CapriDesktop.Program.Smoke = null; }
                Check("B5 remaining " + string.Join("", c), s.BrainTier == 0 && s.BrainProvider == "custom:" + c[3] && p.Brain.Remote == null && p.Brain.Server == "http://localhost:8080" && p.Brain.ServerKey == (c[3] == "a" ? "sk-alpha" : "sk-beta") && p.Brain.Model == "model-" + c[3]);
                string normal = "{\"type\":\"brain.state\",\"use\":\"normal\",\"name\":\"model-" + c[3] + "\",\"note\":\"http://localhost:8080\",\"brains\":[{\"id\":\"normal\",\"key\":false},{\"id\":\"medium\",\"key\":true},{\"id\":\"max\",\"key\":false}]}";
                Check("B5 normal phone " + string.Join("", c), (string)state.Invoke(p, null) == normal);
                CapriDesktop.Program.Smoke = d; try { p.SetBrainTier(1); } finally { CapriDesktop.Program.Smoke = null; }
                Check("B5 cloud round-trip " + string.Join("", c), s.BrainTier == 1 && p.Brain.Remote != null && p.Brain.Remote.Key == "nvapi-73692018" && p.Brain.Model == "free-model" && p.MainframeBrainName == name && (string)state.Invoke(p, null) == phone);
            }
            if (!OperatingSystem.IsWindows())
            {
                foreach (string target in new[] { "cross.json", "cross-new.json", "late-cross-new.json" })
                {
                    if (!PermissionProbe.Enforced()) { Skip("B5 rollback " + target); continue; }
                    string d = Path.Combine(root, "b5-readonly-" + target); Pet p; Settings s = Setup(d, out p, true);
                    s.FreeModel = "free-model"; File.WriteAllText(Path.Combine(d, "brain-free-key"), "nvapi-73692018"); p.UseEndpoint("a", "model-a"); p.UseEndpoint("b", "model-b");
                    CapriDesktop.Program.Smoke = d; try { p.SetBrainTier(1); } finally { CapriDesktop.Program.Smoke = null; } p.Save();
                    string before = Hashes(d) + Hash(Path.Combine(d, "brain-key")) + Hash(Path.Combine(d, "brain-free-key"));
                    string name = p.MainframeBrainName, phone = (string)state.Invoke(p, null), key = p.Brain.ServerKey; Cloud cloud = p.Brain.Remote;
                    bool late = target.StartsWith("late-"); string path = Path.Combine(d, late ? "cross-new.json" : target); UnixFileMode mode = File.GetUnixFileMode(path);
                    UnixFileMode readOnly = mode & ~(UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite); int fired = 0;
                    if (late) p.SettingsChanged += () => { fired++; File.SetUnixFileMode(path, readOnly); }; else File.SetUnixFileMode(path, readOnly);
                    bool removed; try { removed = p.RemoveEndpoint("b"); } finally { File.SetUnixFileMode(path, mode); }
                    Check("B5 rollback " + target, !removed && (!late || fired == 1) && before == Hashes(d) + Hash(Path.Combine(d, "brain-key")) + Hash(Path.Combine(d, "brain-free-key")) &&
                        s.BrainTier == 1 && s.BrainProvider == "custom:b" && ReferenceEquals(cloud, p.Brain.Remote) && cloud.Key == "nvapi-73692018" && p.Brain.Model == "free-model" && p.Brain.ServerKey == key && p.MainframeBrainName == name && (string)state.Invoke(p, null) == phone &&
                        Pet.EndpointFor(s, "a") != null && Pet.EndpointFor(s, "b") != null && Pet.ProviderModel(s, "custom:a", 0) == "model-a" && Pet.ProviderModel(s, "custom:b", 0) == "model-b");
                }
            }
        }
        finally { CapriDesktop.Program.Smoke = previous; }
        return failures - start;
    }
}

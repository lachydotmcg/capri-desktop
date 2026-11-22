using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

internal static class Program
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    const string Fish = "0123456789abcdef0123456789abcdef";
    static Assembly app;
    static Type Model(string name) => app.GetType("CapriDesktop." + name, true);
    static object New(string name, params object[] args) => Activator.CreateInstance(Model(name), Flags & ~BindingFlags.Static, null, args, null);
    static object Get(object obj, string name) => obj.GetType().GetField(name, Flags).GetValue(obj);
    static void Set(object obj, string name, object value) => obj.GetType().GetField(name, Flags).SetValue(obj, value);
    static object Prop(object obj, string name) => obj.GetType().GetProperty(name, Flags).GetValue(obj);
    static object Call(Type type, object tgt, string name, params object[] args) => type.GetMethods(Flags).Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(tgt, args);
    static object Load(string path) => Call(Model("Pet"), null, "ReadSettings", path, null);
    static bool Write(object pet) => (bool)Call(Model("Pet"), pet, "WriteSettings");
    static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    static void Check(string label, bool pass, string found = "")
    {
        Console.WriteLine((pass ? "PASS " : "FAIL ") + label + (found == "" ? "" : ": " + found));
        if (!pass) fails++;
    }
    static int fails;

    [DataContract]
    sealed class PreProvider { [DataMember] public int X = 777; }
    static object SettingsPet(string dir)
    {
        Directory.CreateDirectory(dir);
        object pet = RuntimeHelpers.GetUninitializedObject(Model("Pet"));
        Set(pet, "folder", dir);
        Set(pet, "Saved", New("Settings"));
        return pet;
    }
    static bool NewValues(object settings) => Equals(Get(settings, "VoiceProvider"), "fish") && Equals(Get(settings, "FishVoiceId"), Fish) && Equals(Get(settings, "OpenAiVoiceId"), "cedar");
    static void Settings(string dir)
    {
        object pet = SettingsPet(dir), settings = Get(pet, "Saved");
        string main = Path.Combine(dir, "cross.json"), backup = Path.Combine(dir, "cross-new.json");
        Set(settings, "VoiceProvider", "fish"); Set(settings, "FishVoiceId", Fish); Set(settings, "OpenAiVoiceId", "cedar");
        Check("settings WriteSettings round-trip", Write(pet) && NewValues(Load(main)));
        object newest = Call(Model("Pet"), null, "Newest", settings);
        using (var stream = File.OpenRead(backup))
            Check("Newest and the deserialized shadow carry all three values", NewValues(newest) && NewValues(new DataContractJsonSerializer(newest.GetType()).ReadObject(stream)));
        using (var stream = File.Create(main)) new DataContractJsonSerializer(typeof(PreProvider)).WriteObject(stream, new PreProvider());
        string before = Hash(main) + Hash(backup);
        object recovered = Load(main);
        Check("pre-provider rewrite recovers voice fields without changing legacy X", NewValues(recovered) && Equals(Get(recovered, "X"), 777));
        Check("recovery leaves both files byte-identical", before == Hash(main) + Hash(backup));
        File.WriteAllText(main, "{\"VoiceProvider\":\"openai\",\"FishVoiceId\":null,\"OpenAiVoiceId\":null}");
        before = Hash(main) + Hash(backup);
        object explicitMain = Load(main);
        Check("provider-era main wins over shadow, including explicit clears", Equals(Get(explicitMain, "VoiceProvider"), "openai") && Get(explicitMain, "FishVoiceId") == null && Get(explicitMain, "OpenAiVoiceId") == null && before == Hash(main) + Hash(backup));
        Set(pet, "Saved", settings); Check("restore candidate files for baseline downgrade probe", Write(pet));
    }
    static void LegacyRoundTrip(string dir)
    {
        string main = Path.Combine(dir, "cross.json"), backup = Path.Combine(dir, "cross-new.json");
        object pet = SettingsPet(dir), old = Load(main);
        bool noMembers = old.GetType().GetField("FishVoiceId", Flags) == null && old.GetType().GetField("OpenAiVoiceId", Flags) == null;
        string provider = (string)Call(Model("Speakers"), null, "Known", Get(old, "VoiceProvider"));
        Set(old, "VoiceProvider", provider); Set(pet, "Saved", old);
        Check("baseline provider-era save normalizes provider and drops both IDs", noMembers && provider == "elevenlabs" && Write(pet) && !File.ReadAllText(main).Contains("VoiceId\":\"cedar") && !File.ReadAllText(backup).Contains("FishVoiceId"), provider);
    }
    static void Reupgrade(string dir)
    {
        object settings = Load(Path.Combine(dir, "cross.json"));
        Check("upgrading after baseline rewrite does not resurrect the lost IDs", Equals(Get(settings, "VoiceProvider"), "elevenlabs") && Get(settings, "FishVoiceId") == null && Get(settings, "OpenAiVoiceId") == null);
    }
    static void Picker(string dir, bool old)
    {
        object pet = SettingsPet(dir), settings = Get(pet, "Saved");
        object elVoice = New("Voice");
        Set(pet, "voice", elVoice); Set(pet, "elevenLabs", New("ElevenLabsSpeaker", elVoice));
        Set(pet, "localVoice", New("LocalSpeaker")); Set(pet, "ownedVoice", New("OwnedSpeaker")); Set(pet, "noVoice", New("QuietSpeaker"));
        if (!old) { Set(pet, "fishVoice", New("FishSpeaker")); Set(pet, "openAiVoice", New("OpenAiSpeaker")); }
        Type menuType = AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName("Avalonia.Controls")).GetType("Avalonia.Controls.MenuItem", true);
        object menu = Activator.CreateInstance(menuType);
        Set(pet, "voiceMenu", menu);
        object[] Items(object root) => ((IEnumerable)Prop(root, "Items")).Cast<object>().Where(menuType.IsInstanceOfType).ToArray();
        string Header(object item) => (string)Prop(item, "Header");
        object[] With(string id) { Set(settings, "VoiceProvider", id); Call(Model("Pet"), pet, "RefreshVoice"); return Items(menu); }
        object[] fallback = With("unsupported-voice");
        Check("unknown persisted provider resolves to checked ElevenLabs", Equals(Prop(Prop(pet, "Speaker"), "Name"), "ElevenLabs") && Header(fallback[0]) == "Provider: ElevenLabs" && Equals(Prop(Items(fallback[0]).Single(x => Equals(Prop(x, "IsChecked"), true)), "Header"), "ElevenLabs"));
        if (old) return;
        string[] expected = ["ElevenLabs", "Fish Audio (cloud)", "OpenAI (cloud)", "Local server (OpenAI speech API)", "Retained local voice", "Off"];
        string[] names = Items(fallback[0]).Select(Header).ToArray();
        Check("picker contains exactly six radio rows in order", names.SequenceEqual(expected) && Items(fallback[0]).All(x => Prop(x, "ToggleType").ToString() == "Radio"), string.Join(" | ", names));
        bool Row(object[] rows, string label, bool enabled) => rows.Any(r => Header(r) == label && Equals(Prop(r, "IsEnabled"), enabled));
        object[] fish = With("fish");
        Check("Fish rows before key", Row(fish, "Add Fish Audio key...", true) && Row(fish, "Voice: Fish Audio's default...", true) && Row(fish, "Model: s2.1-pro-free", false) && Row(fish, "Test voice", false) && Equals(Prop(Prop(pet, "Speaker"), "Name"), "Fish Audio"));
        object[] open = With("openai");
        Check("OpenAI rows before key", Row(open, "Add OpenAI key...", true) && Row(open, "Voice: marin...", true) && Row(open, "Model: gpt-4o-mini-tts", false) && Row(open, "Test voice", false) && Equals(Prop(Prop(pet, "Speaker"), "Name"), "OpenAI"));
        Set(Get(pet, "fishVoice"), "Key", "fixture-f123"); Set(Get(pet, "fishVoice"), "Reference", Fish);
        Set(Get(pet, "openAiVoice"), "Key", "fixture-o123"); Set(Get(pet, "openAiVoice"), "VoiceName", "cedar");
        fish = With("fish"); open = With("openai");
        Check("configured providers enable Test and show their independently chosen voices", Row(fish, "Change Fish Audio key...", true) && Row(fish, "Voice: " + Fish + "...", true) && Row(fish, "Test voice", true) && Row(open, "Change OpenAI key...", true) && Row(open, "Voice: cedar...", true) && Row(open, "Test voice", true));
    }

    static void Mainframe(string dir, bool old)
    {
        object pet = SettingsPet(dir), settings = Get(pet, "Saved");
        Set(pet, "body", New("Body"));
        Set(pet, "remoteGate", new object());
        Set(pet, "remoteBrains", new Dictionary<string, Func<bool>>());
        Set(pet, "ownedVoice", New("OwnedSpeaker"));
        object el = New("Voice"); Set(el, "Key", "fixture-el-key"); Set(el, "VoiceId", "fixture-el-id");
        Set(pet, "voice", el); Set(pet, "elevenLabs", New("ElevenLabsSpeaker", el));
        if (!old)
        {
            object fish = New("FishSpeaker"), openAi = New("OpenAiSpeaker");
            Set(fish, "Key", "fixture-fish-key"); Set(openAi, "Key", "fixture-openai-key");
            Set(pet, "fishVoice", fish); Set(pet, "openAiVoice", openAi);
        }
        foreach (string id in old ? new[] { "elevenlabs" } : new[] { "fish", "openai" })
        {
            Set(settings, "VoiceProvider", id); Set(settings, "Silent", false);
            if (!old) { Set(settings, "FishVoiceId", Fish); Set(settings, "OpenAiVoiceId", "cedar"); }
            if (!Write(pet)) throw new Exception("failed to prepare settings");
            int prior = (int)Prop(pet, "MainframeVoiceMode");
            bool ready = (bool)Prop(pet, "Speaks");
            Call(Model("Pet"), pet, "MainframeVoiceModePick", 0);
            object muted = Load(Path.Combine(dir, "cross.json"));
            Call(Model("Pet"), pet, "MainframeVoiceModePick", 3);
            object after = Load(Path.Combine(dir, "cross.json"));
            string got = (string)Get(after, "VoiceProvider");
            Check("Mainframe OFF -> CLOUD VOICE keeps selected provider", prior == 3 && ready && Equals(Get(muted, "VoiceProvider"), id) && Equals(Get(muted, "Silent"), true) && got == id && Equals(Get(after, "Silent"), false), id + " -> " + got);
        }
    }

    static void Keys(string dir)
    {
        Directory.CreateDirectory(dir);
        foreach ((string type, string filename) in new[] { ("FishSpeaker", "voice-fish-key"), ("OpenAiSpeaker", "voice-openai-key") })
        {
            object speaker = New(type);
            string path = Path.Combine(dir, filename);
            Check(type + " writes only owner-readable and writable key", Equals(Call(Model(type), speaker, "SaveKey", dir, "  fixture-secret  "), true) && File.ReadAllText(path) == "fixture-secret" && (OperatingSystem.IsWindows() || File.GetUnixFileMode(path) == (UnixFileMode.UserRead | UnixFileMode.UserWrite)));
            string before = Hash(path);
            Check(type + " rejects a blank replacement without changing the stored or live key", Equals(Call(Model(type), speaker, "SaveKey", dir, "  "), false) && before == Hash(path) && Equals(Get(speaker, "Key"), "fixture-secret"));
            Directory.CreateDirectory(path + ".new");
            Check(type + " a failed replacement leaves both the stored and live key intact", Equals(Call(Model(type), speaker, "SaveKey", dir, "fixture-new"), false) && before == Hash(path) && Equals(Get(speaker, "Key"), "fixture-secret"));
        }
    }

    static void Transport()
    {
        string Problem(object provider, string input, out string detail)
        {
            detail = null;
            try { Call(provider.GetType(), provider, "Speak", input); return "<no error>"; }
            catch (TargetInvocationException e) { detail = (string)e.InnerException.GetType().GetField("Detail", Flags)?.GetValue(e.InnerException); return e.InnerException.Message; }
        }
        (object, HttpListener) Cloud(string type)
        {
            using var free = new TcpListener(IPAddress.Loopback, 0);
            free.Start(); int port = ((IPEndPoint)free.LocalEndpoint).Port; free.Stop();
            string url = "http://127.0.0.1:" + port + "/";
            var server = new HttpListener(); server.Prefixes.Add(url); server.Start();
            object speaker = New(type); Set(speaker, "Key", "fixture-key"); Set(speaker, "Url", url);
            return (speaker, server);
        }
        (string Start, string Auth, string Agent, string Type, string Model, string Body) Exchange(HttpListener server, int status, byte[] payload, string type)
        {
            var ctx = server.GetContext(); var req = ctx.Request;
            var observations = (req.HttpMethod + " " + req.RawUrl, req.Headers["Authorization"], req.UserAgent, req.ContentType, req.Headers["model"], new StreamReader(req.InputStream).ReadToEnd());
            ctx.Response.StatusCode = status; ctx.Response.ContentType = type; ctx.Response.ContentLength64 = payload.Length;
            ctx.Response.OutputStream.Write(payload); ctx.Response.Close();
            return observations;
        }
        void Error(string provider, int status, string body, string message, string detail = null)
        {
            (object speaker, HttpListener server) = Cloud(provider);
            using (server)
            {
                var request = Task.Run(() => Exchange(server, status, Encoding.UTF8.GetBytes(body), "application/json"));
                string found = Problem(speaker, "Can you speak?", out string full);
                if (request.Wait(TimeSpan.FromSeconds(3))) request.GetAwaiter().GetResult();
                Check(provider + " HTTP " + status, found == message && full == detail, found + (full == null ? "" : " | " + full));
            }
        }
        byte[] original = (byte[])Call(Model("Voice"), null, "Wave", new short[] { -32768, 22050, 32767 }, 16000);
        foreach (string provider in new[] { "FishSpeaker", "OpenAiSpeaker" })
        {
            (object speaker, HttpListener server) = Cloud(provider);
            using (server)
            {
                string plain = "A fresh voice.";
                string cued = "[WHISPERING] A fresh voice.";
                Call(Model("Brain"), null, "Cue", cued, plain);
                string fromBrain = (string)Call(Model("Brain"), null, "Cued", plain);
                bool fish = provider == "FishSpeaker";
                string providerId = fish ? "fish" : "openai";
                string chosen = (string)Call(Model("Speakers"), null, "Words", providerId, plain, fromBrain);
                string spoken = (string)Call(Model("Voice"), null, "Spoken", chosen, fish);
                if (!fish) Set(speaker, "VoiceName", "  cedar  ");
                var request = Task.Run(() => Exchange(server, 200, original, "audio/wav"));
                byte[] result = (byte[])Call(speaker.GetType(), speaker, "Speak", fish ? spoken : fromBrain);
                var head = request.GetAwaiter().GetResult();
                string expected = fish ? "{\"text\":\"[whispering] A fresh voice.\",\"format\":\"wav\",\"sample_rate\":24000}" : "{\"model\":\"gpt-4o-mini-tts\",\"input\":\"A fresh voice.\",\"voice\":\"cedar\",\"response_format\":\"wav\"}";
                Check(provider + " independent cue/header/body and non-24 kHz PCM", spoken == (fish ? "[whispering] A fresh voice." : plain) && head.Start == (fish ? "POST /v1/tts" : "POST /v1/audio/speech") && head.Auth == "Bearer fixture-key" && head.Agent == "Capri-Desktop" && head.Type == "application/json" && head.Model == (fish ? "s2.1-pro-free" : null) && head.Body == expected && result.SequenceEqual(original), head.Body + " rate=" + BitConverter.ToInt32(result, 24));
            }
        }
        Error("FishSpeaker", 402, "{\"detail\":\"no credits\"}", "Fish Audio said HTTP 402: no credits");
        Error("FishSpeaker", 429, "{\"detail\":\"rate limited\"}", "Fish Audio is busy (HTTP 429). Try again in a moment.");
        Error("OpenAiSpeaker", 403, "{}", "OpenAI turned down its key (HTTP 403).");
        Error("OpenAiSpeaker", 404, "{}", "OpenAI has no /v1/audio/speech (HTTP 404). Check its address.");
        Error("OpenAiSpeaker", 402, "{\"error\":\"credit balance is empty\"}", "OpenAI is out of credit. Add credit at platform.openai.com, or pick another voice in my right-click menu under VOICE.", "HTTP 402: credit balance is empty");
        Error("OpenAiSpeaker", 200, "{\"error\":\"wrong format\"}", "OpenAI sent words, not sound: wrong format");
    }

    static int Main(string[] args)
    {
        try
        {
            string binary = Path.GetFullPath(args[0]), dir = Path.GetFullPath(args[1]);
            AssemblyLoadContext.Default.Resolving += (ctx, name) => { string path = Path.Combine(Path.GetDirectoryName(binary), name.Name + ".dll"); return File.Exists(path) ? ctx.LoadFromAssemblyPath(path) : null; };
            app = AssemblyLoadContext.Default.LoadFromAssemblyPath(binary);
            if (args[2] == "old") { LegacyRoundTrip(dir); Picker(dir, true); }
            else if (args[2] == "upgrade") Reupgrade(dir);
            else if (args[2] == "mainframe") Mainframe(dir, args[3] == "old");
            else { Settings(dir); Picker(dir, false); Transport(); Keys(Path.Combine(dir, "keys")); }
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 2; }
        return fails == 0 ? 0 : 1;
    }
}

using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

internal sealed class Fixture : Application
{
    public Fixture() { }
}

internal static partial class Probe
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static Assembly app;
    private static MethodInfo saidReply;
    private static int failed;
    // The fake brain sits behind Said.Ask for the batch checks, reusing the shipped cleaner, and the check below calls
    // the delegate Pet.StartSaid installed itself, so a missing production call cannot hide behind the test wrapper.
    private static Func<string, string, string> Brain(Func<string, string, string> provider)
        => (system, input) => (string)saidReply.Invoke(null, new object[] { input, provider(system, input) });
    private static Type Type(string name) => app.GetType("CapriDesktop." + name, true);
    private static Type Owner(object o) => o as Type ?? o.GetType();
    private static object Self(object o) => o is Type ? null : o;
    private static object Get(object o, string name) => Owner(o).GetField(name, All) is FieldInfo f
        ? f.GetValue(Self(o)) : Owner(o).GetProperty(name, All).GetValue(Self(o));
    private static void Put(object o, string name, object value) => Owner(o).GetField(name, All).SetValue(Self(o), value);
    private static object Call(object o, string name, params object[] args) => Owner(o).GetMethod(name, All).Invoke(Self(o), args);
    private static object New(string name, params object[] args) => Activator.CreateInstance(Type(name), All, null, args, null);
    private static void Check(string name, bool ok, object actual)
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name + ": " + actual);
        if (!ok) failed++;
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static string Row(string language, string key)
        => File.ReadLines("assets/words/" + language + ".txt").FirstOrDefault(s => s.StartsWith(key + "\t", StringComparison.Ordinal))?.Split('\t')[1];

    // The smoke's own fake brain, loaded from the app, so the shipped Said.Ask delegate can run with a canned reply.
    private static object FakeBrain(string reply)
    {
        Type kind = Type("Smoke").GetNestedType("RemotePictureBrain", BindingFlags.NonPublic);
        object brain = Activator.CreateInstance(kind, true);
        kind.GetField("Chat", All).SetValue(brain, reply);
        return brain;
    }

    // A new private directory with a random name under the system temp, made by the OS and never an existing path.
    private static string Profile()
    {
        try { return Directory.CreateTempSubdirectory("capri-lingo-probe-").FullName; }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        { throw new ArgumentException("The probe could not create its own private profile directory under " + Path.GetTempPath() + ": " + error.Message); }
    }

    private static void Spoken(object pet, string folder)
    {
        var said = Type("Said");
        string[] lines = (string[])Get(Type("Pet"), "PopLines");
        var installed = (Func<string, string, string>)Get(said, "Ask");
        var parsed = (Dictionary<string, string>)Call(said, "Read", "1. \n2. (se asoma)\n0. Fuera de rango\n3. Fuera de rango",
            new List<string> { "Good morning, choom.", "(pops in)" }, new List<bool> { false, true });
        Console.WriteLine("INFO direct shared Said.Read on same reply: " + System.Text.Json.JsonSerializer.Serialize(parsed));
        var actualFolder = Get(said, "Folder");
        Check("S1 real Pet constructor installs Said folder, provider/ready delegates and pop registration",
            Equals(actualFolder, folder) && installed != null &&
            Get(said, "Ready") is Func<bool> && lines.All(line => Equals(Call(said, "Knows", line), true)), actualFolder ?? "<unset>");
        object brain = Get(pet, "Brain");
        object modelWas = Get(brain, "Model"), providerWas = Get(brain, "Provider");
        try
        {
            Put(brain, "Model", "probe-said");
            Put(brain, "Provider", FakeBrain("1. \n2. (se asoma)"));
            string wired = installed("Translate her lines.", "1. Good morning, choom.\n2. (label) pops in");
            Check("S1 the delegate Pet.StartSaid installed cleans her brain's reply before Said stores it",
                wired.IndexOf("se asoma", StringComparison.Ordinal) >= 0 && wired.IndexOf("(se asoma)", StringComparison.Ordinal) < 0
                && !Regex.IsMatch(wired, @"(?m)^\s*\**\d{1,3}\**\s*[.):]\**\s*$"), wired.Replace("\n", " | "));
        }
        finally { Put(brain, "Model", modelWas); Put(brain, "Provider", providerWas); }
        string unwrapped = (string)saidReply.Invoke(null, new object[] { "1. Good morning, choom.\n2. (label) pops in", "1. \n2. (se asoma)" });
        Check("S1 a rebracketed label is unwrapped before it reaches Said", unwrapped == "2. se asoma\n", unwrapped.Replace("\n", " | "));
        string blanked = (string)saidReply.Invoke(null, new object[] { "1. Good morning, choom.\n2. (label) pops in", "1. \n2. ( )" });
        string bare = (string)saidReply.Invoke(null, new object[] { "1. Good morning, choom.", "1." });
        Check("S1 blank wrapped labels and bare markers drop after unwrapping, so no whitespace row is kept",
            blanked.Length == 0 && bare.Length == 0, blanked.Replace("\n", " | ") + " / " + bare.Replace("\n", " | "));
        string keep = Path.Combine(folder, "lines-spanish.txt");
        File.WriteAllText(keep, "An unrelated fixed line.\tUna frase aparte.\n");
        string before = Hash(keep);
        Call(pet, "SetLanguage", "Spanish");
        Put(said, "Ask", Brain((Func<string, string, string>)((_, _) => "0. Fuera de rango\n999. Otro número\n1. \nnot numbered")));
        Call(said, "Work");
        Check("S1 invalid batch leaves stored bytes unchanged", Hash(keep) == before, Hash(keep));
        Call(said, "Reload");
        int wrappedBatches = 0;
        Put(said, "Ask", Brain((Func<string, string, string>)((_, input) => string.Join('\n', input.Split('\n').Select(row =>
        {
            Match match = Regex.Match(row, @"^(\d+)\. (.*)$");
            if (!match.Success) return "";
            string payload = match.Groups[2].Value;
            if (payload == "(label) (pops in)" || payload == "(label) pops in") { wrappedBatches++; return match.Groups[1].Value + ". (se asoma)"; }
            if (payload == "(label) pets you") return match.Groups[1].Value + ". te acaricia";
            if (lines.Contains(payload)) return match.Groups[1].Value + ". ¡Hola, choom!";
            return "";
        })))));
        Call(said, "Work");
        Call(said, "Reload");
        Put(Get(pet, "Saved"), "Wander", 0);
        Call(pet, "PopIn");
        var sheet = Get(Get(Get(pet, "Talk"), "Bubble"), "Sheet");
        string body = (string)Get(sheet, "Reply"), heard = (string)Get(sheet, "Heard");
        Check("S1 chosen Spanish translates real PopIn body", body == "¡Hola, choom!", body);
        Check("S1 chosen Spanish translates and saves real PopIn sys label", heard == "(se asoma)",
            $"heard={heard}, provider rows={wrappedBatches}, after cache reload, bare label={Call(said, "Label", "pets you")}");
        Call(Get(pet, "Talk"), "Dismiss", true);
        Call(pet, "SetLanguage", "English");
        Call(pet, "PopIn");
        Check("S1 back to English uses exact existing line and label", lines.Contains((string)Get(sheet, "Reply")) &&
            Equals(Get(sheet, "Heard"), "(pops in)"), Get(sheet, "Heard") + " / " + Get(sheet, "Reply"));
        Call(Get(pet, "Talk"), "Dismiss", true);
        LegacyLabel(pet, folder);
    }

    // A cache written before the bare-label ask kept its row under the bracketed spelling, and Said matches keys literally,
    // so the display path tries that spelling once the bare key misses, with no model available to translate anything new.
    private static void LegacyLabel(object pet, string folder)
    {
        var said = Type("Said");
        object brain = Get(pet, "Brain");
        object folderWas = Get(said, "Folder"), languageWas = Get(Type("Brain"), "Language"), modelWas = Get(brain, "Model");
        string legacy = Path.Combine(folder, "legacy");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "lines-spanish.txt"), "(pops in)\t(se asoma)\n");
        try
        {
            Put(said, "Folder", legacy);
            Call(said, "Reload");
            Put(Type("Brain"), "Language", "Spanish");
            Put(brain, "Model", null);
            object bare = Call(said, "In", "pops in");
            object shown = Call(Type("Pet"), "SaidLabel", "(pops in)");
            Check("S1 a cache seeded under the old bracketed label still resolves without a model",
                Equals(bare, "pops in") && Equals(shown, "(se asoma)"), "bare=" + bare + ", shown=" + shown);
        }
        finally
        {
            Put(said, "Folder", folderWas);
            Call(said, "Reload");
            Put(Type("Brain"), "Language", languageWas);
            Put(brain, "Model", modelWas);
        }
    }

    private static void Chrome(object pet, string folder)
    {
        var lingo = Type("Lingo");
        Call(lingo, "Use", "Spanish");
        var menu = (NativeMenu)Call(pet, "TrayMenu");
        string hide = Row("spanish", "Hide Capri");
        Check("S2 Spanish real tray has localized hide row", hide != null && menu.Items.OfType<NativeMenuItem>().Any(r => r.Header == hide),
            string.Join(" | ", menu.Items.OfType<NativeMenuItem>().Select(r => r.Header)));
        var memory = New("MemorySheet", pet);
        var rows = (Dictionary<string, Control>)Get(memory, "Rows");
        string cue = (string)Get(rows["Tell her something to remember"], "Cue");
        Check("S2 Spanish real Memory sheet has localized watermark", cue == Row("spanish", "Tell her something to remember"), cue);
        var mail = New("MailWindow", pet, null, null);
        string appPassword = (string)Get(Type("Mods"), "AppPasswordLine");
        string separate = (string)Get(Get(mail, "Separate"), "Text");
        Check("S2 Spanish real Mail sheet uses localized app-password note", Row("spanish", appPassword) is string expected &&
            separate.StartsWith(expected, StringComparison.Ordinal), separate);
        var providers = (Array)Get(Type("Mods"), "MailProviders");
        var provider = providers.Cast<object>().First(p => !(bool)Get(p, "Closed"));
        Call(mail, "Pick", provider, true);
        Call(mail, "Go", 2);
        ((Window)mail).Show();
        var visible = (Control)Get(mail, "Separate");
        Check("S2 non-modal Mail step two visible", ((Window)mail).IsVisible && visible.IsVisible && Equals(Get(mail, "Step"), 2), visible.IsVisible);
        Call(lingo, "Use", "English");
        var theme = Type("MainframeTheme");
        Call(theme, "Reword");
        string note = (string)Get(Get(mail, "Separate"), "Text");
        string drawn = (string)Call(theme, "Fitted", note, 100000.0, 16.0, false, false);
        string english = appPassword + ((bool)Get(Type("McpSignIn"), "Keeps") ? "" : " She keeps it only until she closes.");
        Check("S2 same open Mail sheet restores English after switching", drawn == english, drawn);
        ((Window)mail).Close();
        const string key = "Search settings";
        const string japanese = "設定検索は、Mainframeの設定ページの上部、詳細設定の横にある入力欄です。MainframeのどこにいてもCtrl+Fを押すと設定を探せます。MacではCmd+Fも使えます。入力すると一致した設定がセクションとともに短い一覧に表示されます。上下の矢印キーで項目を選び、Enterを押すかクリックすると、そのページが開いて該当箇所まで移動し、しばらく水色の枠で強調表示されます。Escキーで検索欄を空にできます。一致する設定が見つからない場合は、一致するものがありませんと表示されます。";
        var tips = Type("MainframeTips");
        var host = new Border();
        Call(tips, "Explain", host, key);
        var tip = (TextBlock)ToolTip.GetTip(host);
        string facts = GetFacts(key);
        string masked = (string)Call(lingo, "Mask", facts, new List<string>());
        File.WriteAllText(Path.Combine(folder, "words-japanese.txt"), masked + "\t" + japanese + "\n");
        // The baseline never sets the shared cache root; assign it only for a like-for-like fixture when legacy Said is unwired.
        if (Get(Type("Said"), "Folder") == null) Put(Type("Said"), "Folder", folder);
        Call(lingo, "Use", "Japanese");
        Check("S2 Japanese no-space translation available through actual Lingo", Equals(Call(lingo, "Learn", facts), japanese), japanese.Length);
        tip.InvalidateMeasure();
        tip.Measure(new Size(332, 1000));
        Check("S2 Japanese real tip keeps the full no-space body after layout", tip.Text == japanese, tip.Text.Length + " / " + tip.Text);
        Call(lingo, "Use", new object[] { null });
        tip.InvalidateMeasure();
        tip.Measure(new Size(332, 1000));
        string noLanguage = (string)Call(tips, "Brief", GetFacts(key));
        Check("S2 back to English restores original tooltip", tip.Text == noLanguage, tip.Text);

        string GetFacts(string name) => tips.GetMethod("Facts", All) is MethodInfo f
            ? (string)f.Invoke(null, new object[] { name })
            : ((Dictionary<string, string>)Get(tips, "Text"))[name];
    }

    [STAThread]
    private static int Main(string[] args)
    {
        string folder = null;
        try
        {
            if (args.Length != 1) throw new ArgumentException("Pass only the built Capri.dll path; the probe creates its own private profile directory under the system temp.");
            string path = Path.GetFullPath(args[0]);
            AssemblyLoadContext.Default.Resolving += (ctx, name) =>
            {
                string dependency = Path.Combine(Path.GetDirectoryName(path), name.Name + ".dll");
                return File.Exists(dependency) ? ctx.LoadFromAssemblyPath(dependency) : null;
            };
            app = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            saidReply = Type("Pet").GetMethod("SaidReply", All);
            folder = Profile();
            Console.WriteLine("INFO private profile " + folder);
            Put(Type("Program"), "Smoke", folder);
            Call(Type("NativeFontBootstrap"), "Initialize");
            var lifetime = new ClassicDesktopStyleApplicationLifetime { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            AppBuilder.Configure<Fixture>().UsePlatformDetect().SetupWithLifetime(lifetime);
            var pet = New("Pet", lifetime);
            VerifySpokenCache(pet, folder);
            VerifyMailNote(pet);
            Spoken(pet, folder);
            Chrome(pet, folder);
            return failed == 0 ? 0 : 1;
        }
        catch (Exception e) { Console.WriteLine(e); return 2; }
        finally
        {
            if (folder != null)
            {
                try { Directory.Delete(folder, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}

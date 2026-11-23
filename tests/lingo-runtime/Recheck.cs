using Avalonia.Controls;

internal static partial class Probe
{
    private static void VerifySpokenCache(object pet, string folder)
    {
        var said = Type("Said");
        var sharedBrain = Type("Brain");
        object brain = Get(pet, "Brain"), saved = Get(pet, "Saved");
        object oldFolder = Get(said, "Folder"), oldLanguage = Get(sharedBrain, "Language"), oldWander = Get(saved, "Wander");
        object oldModel = Get(brain, "Model"), oldProvider = Get(brain, "Provider");
        var ask = (Func<string, string, string>)Get(said, "Ask");
        string privateCache = Path.Combine(folder, "cache-recheck");
        Directory.CreateDirectory(privateCache);
        string cache = Path.Combine(privateCache, "lines-spanish.txt");
        try
        {
            Put(said, "Folder", privateCache);
            Put(sharedBrain, "Language", "Spanish");
            Put(brain, "Model", null);
            Put(saved, "Wander", 0);
            LabelCase("(pops in)\t(se asoma)\n", "pops in", "(se asoma)");
            LabelCase("(pops in)\t(se asoma)\npops in\taparece\n", "aparece", "(aparece)");
            LabelCase("An unrelated line.\tUna frase distinta.\n", "pops in", "(pops in)");

            string popLine = ((string[])Get(Type("Pet"), "PopLines"))[0];
            string label = Equals(Call(said, "Knows", "pops in"), true) ? "pops in" : "(pops in)";
            string seed = string.Concat(((List<string>)Get(said, "known"))
                .Where(k => k != popLine && k != label).Select(k => k + "\ttraducción retenida\n"));
            string asked = $"1. {popLine}\n2. (label) {label}\n";
            BlankCase("2. ( )");
            BlankCase("2. ()");
            BlankCase("1.");
            BlankCase("1. ");

            const string bracketed = "(se asoma después de un largo rato)";
            File.WriteAllText(cache, seed);
            Call(said, "Reload");
            Put(brain, "Provider", FakeBrain("2. " + bracketed));
            Put(brain, "Model", "recheck-said");
            Call(said, "Work");
            Put(brain, "Model", null);
            Call(said, "Reload");
            Call(pet, "PopIn");
            var sheet = Get(Get(Get(pet, "Talk"), "Bubble"), "Sheet");
            object shown = Get(sheet, "Heard");
            string savedRow = File.ReadLines(cache).Single(row => row.StartsWith(label + "\t", StringComparison.Ordinal));
            Check("A S1 an already bracketed long translation renders with exactly one pair", Equals(shown, bracketed), $"stored={savedRow}, shown={shown}");
            Call(Get(pet, "Talk"), "Dismiss", true);

            void LabelCase(string rows, string expectedBare, string expectedShown)
            {
                File.WriteAllText(cache, rows);
                string before = Hash(cache);
                Call(said, "Reload");
                object bare = Call(said, "In", "pops in");
                Call(pet, "PopIn");
                var sheet = Get(Get(Get(pet, "Talk"), "Bubble"), "Sheet");
                object shown = Get(sheet, "Heard");
                bool unchanged = Hash(cache) == before;
                Check("A S1 real offline PopIn resolves label", Equals(bare, expectedBare) && Equals(shown, expectedShown)
                    && Get(brain, "Model") == null && unchanged, $"Model=null, bare={bare}, shown={shown}, SHA unchanged={unchanged}");
                Call(Get(pet, "Talk"), "Dismiss", true);
            }

            void BlankCase(string reply)
            {
                File.WriteAllText(cache, seed);
                string before = Hash(cache);
                Call(said, "Reload");
                object provider = FakeBrain(reply);
                Put(brain, "Provider", provider);
                Put(brain, "Model", "recheck-said");
                string cleaned = ask("Translate lines.", asked);
                int calls = (int)Get(provider, "Chats");
                Call(said, "Work");
                var last = (System.Collections.IEnumerable)Get(provider, "LastChat");
                bool realBatch = (int)Get(provider, "Chats") > calls && ReferenceEquals(ask, Get(said, "Ask"))
                    && last.Cast<object>().Any(m => ((string)Get(m, "Content")).Contains(asked.TrimEnd('\n'), StringComparison.Ordinal));
                string after = Hash(cache);
                Check("D S1 real Said batch rejects " + System.Text.Json.JsonSerializer.Serialize(reply),
                    cleaned == "" && realBatch && before == after,
                    $"cleaned={System.Text.Json.JsonSerializer.Serialize(cleaned)}, real batch={realBatch}, SHA {before} -> {after}");
            }
        }
        finally
        {
            Put(brain, "Model", oldModel);
            Put(brain, "Provider", oldProvider);
            Put(sharedBrain, "Language", oldLanguage);
            Put(saved, "Wander", oldWander);
            Put(said, "Folder", oldFolder);
            Call(said, "Reload");
        }
    }

    private static void VerifyMailNote(object pet)
    {
        var lingo = Type("Lingo");
        object oldLanguage = Get(lingo, "Tongue");
        Window mail = null;
        try
        {
            Call(lingo, "Use", "Spanish");
            mail = (Window)New("MailWindow", pet, null, null);
            var provider = ((Array)Get(Type("Mods"), "MailProviders")).Cast<object>().First(p => !(bool)Get(p, "Closed"));
            Call(mail, "Pick", provider, true);
            Call(mail, "Go", 2);
            mail.Show();
            object note = Get(mail, "Separate");
            string key = (string)Get(Type("Mods"), "AppPasswordLine");
            bool keeps = (bool)Get(Type("McpSignIn"), "Keeps");
            string Expected(string language) => Row(language, key) + (keeps ? "" : " " + Row(language, "She keeps it only until she closes."));
            string spanish = (string)Get(note, "Text");
            Call(lingo, "Use", "Japanese");
            string japanese = (string)Get(note, "Text");
            bool Subscribed() => ((Delegate)Get(lingo, "Changed"))?.GetInvocationList().Any(f => ReferenceEquals(f.Target, mail)) ?? false;
            bool subscribed = Subscribed();
            Check("H S2 visible Mail note recomposes on Lingo.Changed", mail.IsVisible && ((Control)note).IsVisible
                && spanish == Expected("spanish") && japanese == Expected("japanese") && subscribed,
                $"subscribed={subscribed}; {spanish} -> {japanese}");
            mail.Close();
            Call(lingo, "Use", "English");
            Check("H S2 Closed unsubscribes the Mail note", subscribed && !Subscribed() && Equals(Get(note, "Text"), japanese),
                $"subscribed={Subscribed()}, note unchanged={Equals(Get(note, "Text"), japanese)}");
        }
        finally
        {
            if (mail?.IsVisible == true) mail.Close();
            Call(lingo, "Use", oldLanguage);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Companion
    {
        private const string SpanishLine = "\nAlways reply in Spanish, whatever language they use. Keep your Night City slang words as they are.";

        private void SmokeLanguagePair(List<string> checks, string output)
        {
            Preferences preferencesWas = preferences;
            string languageWas = Brain.Language;
            bool matchingWas = Brain.Matching, firmWas = Brain.Firm;
            int sizeWas = MainframeSize;
            Point atWas = mainframe.Location;
            bool shownWas = mainframe.Visible, settingsWas = mainframe.SettingsShown;
            FormWindowState stateWas = mainframe.WindowState;
            ContextMenuStrip menu = null;
            string folder = Path.Combine(output, "language-pair");
            try
            {
                Directory.CreateDirectory(folder);
                string notice, older = Path.Combine(folder, "older-preferences.json");
                File.WriteAllText(older, "{\"Language\":\"Spanish\",\"Nickname\":\"Sam\"}");
                preferences = ReadPreferences(older, out notice);
                if (notice != null || preferences.Language != "Spanish" || preferences.ReplyLanguage != null) throw new Exception("Settings from before the pair didn't read as they were.");
                PointReplies();
                if (Brain.Language != "Spanish" || Brain.Matching || Brain.Firm || Brain.Speech() != SpanishLine || Language != "Spanish" || ReplyLanguage != null || Listening(preferences.Language, "ja") != "es")
                    throw new Exception("Settings from before the pair changed her replies or ears: " + Brain.Language + " /" + Brain.Speech());
                File.WriteAllText(older, "{\"Nickname\":\"Sam\"}");
                preferences = ReadPreferences(older, out notice);
                PointReplies();
                if (Brain.Language != null || Brain.Speech() != "" || Language != "English" || Listening(preferences.Language, "ja") != "ja")
                    throw new Exception("A language never picked stopped following Windows for her ears: " + Listening(preferences.Language, "ja"));
                File.WriteAllText(older, "{\"Language\":\"French\",\"ReplyLanguage\":\"Klingon\"}");
                preferences = ReadPreferences(older, out notice);
                PointReplies();
                if (preferences.ReplyLanguage != null || Brain.Language != "French" || Brain.Firm || Brain.Matching) throw new Exception("A reply language she doesn't have didn't go back to Same as I speak.");
                checks.Add("PASS language pair, older settings: a file from before the pair keeps her replies, her prompt's language line and her ears exactly as they were, a language never picked still leaves her ears on Windows' language, and a reply language she doesn't have goes back to Same as I speak");

                preferences = new Preferences { Language = "English" };
                PointReplies();
                PointEars();
                SetReplyLanguage("Spanish");
                Preferences saved = LoadPreferences();
                if (Brain.Language != "Spanish" || !Brain.Firm || Brain.Matching || Brain.Speech() != SpanishLine || saved.ReplyLanguage != "Spanish" || saved.Language != "English" || Language != "English" || parakeet.Language != "en")
                    throw new Exception("She answers in Spanish didn't save on its own, or it moved her ears: " + Brain.Language + " / " + parakeet.Language);
                SetLanguage("French");
                if (Brain.Language != "Spanish" || parakeet.Language != "fr" || LoadPreferences().Language != "French" || Language != "French") throw new Exception("I speak French moved her replies off Spanish.");
                SetReplyLanguage("English");
                if (Brain.Language != null || !Brain.Firm || Brain.Speech() != "\nAlways reply in English, whatever language they use. Keep your Night City slang words as they are.")
                    throw new Exception("English replies to someone speaking French had no language line: " + Brain.Speech());
                SetReplyLanguage(LanguagePair.Auto);
                if (!Brain.Matching || Brain.Firm || Brain.Language != "French" || !parakeet.Multi || LoadPreferences().ReplyLanguage != LanguagePair.Auto || ReplyLanguage != LanguagePair.Auto)
                    throw new Exception("Auto didn't start on the language they speak.");
                SetReplyLanguage(LanguagePair.SameName);
                if (Brain.Matching || Brain.Firm || parakeet.Multi || Brain.Language != "French" || LoadPreferences().ReplyLanguage != null || Brain.Speech() != "\nAlways reply in French, whatever language they use. Keep your Night City slang words as they are.")
                    throw new Exception("Same as I speak didn't go back to French.");
                SetLanguage("English");
                if (Brain.Language != null || Brain.Speech() != "" || parakeet.Language != "en" || Language != "English") throw new Exception("English with Same as I speak didn't go back to her defaults.");
                checks.Add("PASS language pair: She answers in saves apart from I speak and leaves her ears on what they speak, English replies to a French speaker get their own line, Auto starts on the language they speak and lets her ears hear many languages, and Same as I speak is today's behaviour");

                SetReplyLanguage("Spanish");
                mainframe.WindowState = FormWindowState.Normal;
                mainframe.Show();
                mainframe.SizeMainframe(100);
                string reach = "";
                List<string> crowded = new List<string>();
                Action<string> fits = delegate(string view)
                {
                    mainframe.ShowSettings(true);
                    mainframe.Sync();
                    Application.DoEvents();
                    if (mainframe.AdvancedShown != view.StartsWith("advanced", StringComparison.Ordinal)) throw new Exception("Settings didn't open " + view + ".");
                    string[] parts = mainframe.PageReach.Split('|');
                    reach += view + " " + mainframe.PageReach + "; ";
                    if (Math.Max(int.Parse(parts[0].Trim()), int.Parse(parts[1].Trim())) > int.Parse(parts[2].Trim())) crowded.Add(view + " runs past its height");
                    string squeezed = mainframe.TextFits() ?? mainframe.Overlap() ?? (mainframe.PageClamped ? null : mainframe.Outside());
                    if (squeezed != null) crowded.Add(view + ": " + squeezed);
                    int[] tops = mainframe.YouTops;
                    if (!(tops[0] < tops[1] && tops[1] < tops[2])) crowded.Add(view + ": She answers in isn't between I speak and the nickname box (" + tops[0] + ", " + tops[1] + ", " + tops[2] + ")");
                    if (mainframe.AnswerShown != "Español" || !mainframe.AnswerIs("Spanish") || !mainframe.LanguageIs("English") || mainframe.LanguageShown != "English") crowded.Add(view + ": the rows show " + mainframe.LanguageShown + " / " + mainframe.AnswerShown);
                    SnapMainframe(Path.Combine(folder, "language-rows-" + view + ".png"));
                };
                InSimpleSettings(delegate { fits("simple"); });
                fits("advanced");
                mainframe.LayoutScale(144, 100);
                fits("advanced-150");
                InSimpleSettings(delegate { fits("simple-150"); });
                mainframe.LayoutScale(96, 100);
                if (crowded.Count > 0) throw new Exception("The language rows don't fit Settings: " + string.Join("; ", crowded.ToArray()) + " (" + reach.Trim() + ")");
                checks.Add("PASS language rows: I speak and She answers in sit together under YOU with nothing overlapping or off the page in Simple and Advanced Settings at 100% and 150%; pictures in " + folder + " (" + reach.Trim() + ")");

                Rectangle work = Screen.FromControl(mainframe).WorkingArea;
                menu = mainframe.ShowAnswers();
                if ((string)menu.Items[0].Tag != LanguagePair.Same || menu.Items[0].Text != LanguagePair.SameName || (string)menu.Items[1].Tag != LanguagePair.Auto || menu.Items[1].Text != LanguagePair.AutoName || (string)menu.Items[3].Tag != "English")
                    throw new Exception("She answers in's menu doesn't start with Same as I speak, Auto and English.");
                bool[] seen = new bool[Brain.Languages.GetLength(0)];
                AnswerBounds(menu, work, seen, "Spanish");
                for (int row = 0; row < seen.Length; row++) if (!seen[row]) throw new Exception("She answers in's menu is missing " + Brain.Languages[row, 1] + ".");
                ToolStripMenuItem japanese = null;
                foreach (ToolStripMenuItem item in Mainframe.LanguageItems(menu.Items)) if ((string)item.Tag == "Japanese") japanese = item;
                japanese.PerformClick();
                menu.Close();
                if (ReplyLanguage != "Japanese" || LoadPreferences().ReplyLanguage != "Japanese" || !mainframe.AnswerIs("Japanese") || mainframe.AnswerShown != "日本語" || Brain.Language != "Japanese" || preferences.Language != "English")
                    throw new Exception("Japanese in She answers in's menu didn't pick, save and show.");
                menu = mainframe.ShowAnswers();
                ((ToolStripMenuItem)menu.Items[1]).PerformClick();
                menu.Close();
                if (ReplyLanguage != LanguagePair.Auto || mainframe.AnswerShown != LanguagePair.Auto || !mainframe.AnswerIs(LanguagePair.Auto)) throw new Exception("Auto in She answers in's menu didn't pick and show.");
                InSimpleSettings(delegate { mainframe.ShowSettings(true); mainframe.Sync(); Application.DoEvents(); SnapMainframe(Path.Combine(folder, "language-rows-auto.png")); });
                menu = mainframe.ShowAnswers();
                ((ToolStripMenuItem)menu.Items[0]).PerformClick();
                menu.Close();
                if (ReplyLanguage != null || mainframe.AnswerShown != LanguagePair.SameName || !mainframe.AnswerIs(null) || Brain.Language != null) throw new Exception("Same as I speak in She answers in's menu didn't go back.");
                checks.Add("PASS She answers in's menu: Same as I speak, Auto and English lead, every one of her " + seen.Length + " languages is there once and fits the screen, the tick follows the setting, and Japanese, Auto and Same as I speak each pick, save and show");
            }
            finally
            {
                if (menu != null) menu.Close();
                preferences = preferencesWas;
                SavePreferences();
                PointReplies();
                Brain.Language = languageWas;
                Brain.Matching = matchingWas;
                Brain.Firm = firmWas;
                PointEars();
                mainframe.LayoutScale(0, 0);
                mainframe.SizeMainframe(sizeWas);
                mainframe.Sync();
                mainframe.ShowSettings(settingsWas);
                mainframe.Location = atWas;
                mainframe.WindowState = stateWas;
                if (!shownWas) mainframe.Hide();
            }
        }

        private static void AnswerBounds(ToolStripDropDown menu, Rectangle area, bool[] seen, string ticked)
        {
            if (Screen.AllScreens.Length > 1) area = Screen.FromRectangle(menu.Bounds).WorkingArea;
            if (!menu.Visible || !area.Contains(menu.Bounds)) throw new Exception("She answers in's menu leaves its screen: " + menu.Bounds + " in " + area);
            foreach (ToolStripItem item in menu.Items)
            {
                if (!item.Available) continue;
                Rectangle bounds = new Rectangle(menu.PointToScreen(item.Bounds.Location), item.Size);
                if (!item.Visible || !area.Contains(bounds) || !menu.ClientRectangle.Contains(item.Bounds)) throw new Exception("A She answers in item is clipped: " + item.Text);
                ToolStripMenuItem choice = item as ToolStripMenuItem;
                if (choice == null) continue;
                if (choice.HasDropDownItems)
                {
                    choice.ShowDropDown();
                    try { AnswerBounds(choice.DropDown, area, seen, ticked); }
                    finally { choice.HideDropDown(); }
                    continue;
                }
                string tag = (string)choice.Tag;
                if (choice.Checked != (tag == ticked)) throw new Exception("A She answers in tick is wrong: " + choice.Text);
                for (int row = 0; row < Brain.Languages.GetLength(0); row++)
                    if (tag == Brain.Languages[row, 1])
                    {
                        if (seen[row]) throw new Exception("She answers in's menu has " + tag + " twice.");
                        seen[row] = true;
                    }
            }
        }

        private void SmokeAutoReply(List<string> checks)
        {
            Preferences preferencesWas = preferences;
            string languageWas = Brain.Language;
            bool matchingWas = Brain.Matching, firmWas = Brain.Firm;
            try
            {
                string[,] lines =
                {
                    { "¿Puedes hablarme en español, por favor?", "Spanish" },
                    { "Hola, ¿qué tal? ¿Quieres jugar esta noche?", "Spanish" },
                    { "日本語で話してくれる？", "Japanese" },
                    { "今日は何をしますか", "Japanese" },
                    { "Привет, как у тебя дела?", "Russian" },
                    { "Привіт, як справи? Що ти робиш сьогодні?", "Ukrainian" },
                    { "Salut, tu veux jouer ce soir ?", "French" },
                    { "Oi, tudo bem? Você quer jogar hoje?", "Portuguese" },
                    { "Cześć, jak się masz? Chcesz dzisiaj grać?", "Polish" },
                    { "Hallo, wie geht's? Willst du heute Abend spielen?", "German" },
                    { "Ciao, come stai? Vuoi giocare stasera?", "Italian" },
                    { "Merhaba, nasılsın? Bugün oynamak istiyor musun?", "Turkish" },
                    { "Xin chào, hôm nay bạn có muốn chơi không?", "Vietnamese" },
                    { "안녕하세요, 오늘 뭐 해요?", "Korean" },
                    { "你今天想玩什么游戏？", "Simplified Chinese" },
                    { "你今天想玩什麼遊戲？", "Traditional Chinese" },
                    { "مرحبا، كيف حالك اليوم؟", "Arabic" },
                    { "نمی‌دانم چه بازی کنیم", "Persian" },
                    { "नमस्ते, आप कैसे हैं? क्या आज खेलना है?", "Hindi" },
                    { "שלום, מה שלומך היום?", "Hebrew" },
                    { "Γεια σου, τι κάνεις σήμερα;", "Greek" },
                    { "สวัสดี วันนี้เป็นอย่างไรบ้าง", "Thai" },
                    { "Hey, what's up? Do you want to play something tonight?", "English" },
                    { "can you say hola to my friend?", "English" }
                };
                for (int i = 0; i < lines.GetLength(0); i++)
                {
                    string found = LanguagePair.Detect(lines[i, 0], "English");
                    if (found != lines[i, 1]) throw new Exception("Auto heard " + (found ?? "nothing") + " in a " + lines[i, 1] + " line: " + lines[i, 0]);
                }
                foreach (string unclear in new[] { "ok", "lol", "Capri", "jajaja", "👍", "", "https://example.com/hola", "gracias" })
                    if (LanguagePair.Detect(unclear, "English") != null) throw new Exception("Auto picked a language from a line too short to tell: " + unclear);
                if (LanguagePair.Detect("ok gracias", "Spanish") != null || LanguagePair.Detect("Gracias, ¿y tú?", "English") != "Spanish") throw new Exception("Auto's short Spanish lines went the wrong way.");
                checks.Add("PASS Auto hears the language of a line on this PC: " + lines.GetLength(0) + " lines in 21 languages, Spanish, Japanese and Russian among them, while ok, lol, a name, an emoji or a link stay unread");

                preferences = new Preferences { Language = "English" };
                PointReplies();
                SetReplyLanguage(LanguagePair.Auto);
                Match("¿Puedes hablarme en español, por favor?");
                if (Brain.Language != "Spanish" || Brain.Speech() != LanguagePair.Line("Spanish")) throw new Exception("Auto didn't answer a Spanish line in Spanish: " + Brain.Language);
                Match("ok");
                if (Brain.Language != "Spanish") throw new Exception("A short line moved Auto off Spanish.");
                Match("日本語で話してくれる？");
                if (Brain.Language != "Japanese" || !Brain.Speech().Contains("which is Japanese")) throw new Exception("Auto didn't follow them into Japanese.");
                Match("Привет, как у тебя дела?");
                if (Brain.Language != "Russian" || !Brain.Speech().Contains("which is Russian")) throw new Exception("Auto didn't follow them into Russian.");
                Match("Thanks, that's great, let's play now");
                if (Brain.Language != null || !Brain.Speech().Contains("which is English") || preferences.ReplyLanguage != LanguagePair.Auto || preferences.Language != "English") throw new Exception("Auto didn't come back to English, or it changed their settings.");
                SetReplyLanguage(LanguagePair.SameName);
                Match("¿Puedes hablarme en español, por favor?");
                if (Brain.Language != null) throw new Exception("Same as I speak followed what they said like Auto.");
                checks.Add("PASS Auto answers each message in its language: Spanish, Japanese, Russian and back to English, a short line keeps the last, the prompt line names it, and nothing in their settings moves");
            }
            finally
            {
                preferences = preferencesWas;
                SavePreferences();
                PointReplies();
                Brain.Language = languageWas;
                Brain.Matching = matchingWas;
                Brain.Firm = firmWas;
                PointEars();
                mainframe.Sync();
            }
        }

        private void SmokeReplyLine(List<string> checks)
        {
            string languageWas = Brain.Language;
            bool matchingWas = Brain.Matching, firmWas = Brain.Firm;
            try
            {
                Brain.Matching = Brain.Firm = false;
                Brain.Language = null;
                if (Brain.Speech() != "") throw new Exception("English with Same as I speak grew a language line.");
                Brain.Language = "Spanish";
                if (Brain.Speech() != SpanishLine) throw new Exception("Spanish lost today's line: " + Brain.Speech());
                Brain.Firm = true;
                if (Brain.Speech() != SpanishLine) throw new Exception("A picked Spanish isn't today's line.");
                Brain.Language = null;
                if (Brain.Speech() != "\nAlways reply in English, whatever language they use. Keep your Night City slang words as they are.") throw new Exception("A picked English has no line.");
                Brain.Firm = false;
                Brain.Matching = true;
                Brain.Language = "Japanese";
                string auto = Brain.Speech();
                if (auto != LanguagePair.Line("Japanese") || !auto.Contains("which is Japanese") || !auto.Contains("Every rule you have holds in every language") || !auto.Contains("Night City slang")) throw new Exception("Auto's line is wrong: " + auto);
                int held = 0;
                for (int pass = 0; pass < 3; pass++)
                    for (int row = 0; row < Brain.Languages.GetLength(0); row++)
                    {
                        Brain.Matching = pass == 2;
                        Brain.Firm = pass == 1;
                        Brain.Language = row == 0 ? null : Brain.Languages[row, 1];
                        string character = Brain.Character, line = Brain.Speech();
                        int safety = character.IndexOf(Brain.Safety, StringComparison.Ordinal);
                        if (safety < 0 || character.IndexOf(Brain.Machinery, StringComparison.Ordinal) < safety) throw new Exception("Her safety lines left her prompt in " + Brain.Languages[row, 1] + ".");
                        if (line.Length > 0 && character.IndexOf(line, StringComparison.Ordinal) < safety + Brain.Safety.Length) throw new Exception("The language line came before her safety lines in " + Brain.Languages[row, 1] + ".");
                        held++;
                    }
                checks.Add("PASS her prompt's language line: English with Same as I speak has none, Spanish keeps today's line word for word, a picked English gets one, Auto names the language heard, and her safety lines come first in her prompt for all " + held + " languages and modes");
            }
            finally
            {
                Brain.Language = languageWas;
                Brain.Matching = matchingWas;
                Brain.Firm = firmWas;
            }
        }

        private void SmokeTongueVoices(List<string> checks, string output)
        {
            Preferences preferencesWas = preferences;
            Func<string, byte[]> speakWas = Speak, freeWas = FreeSpeak;
            Func<string, string, byte[]> tongueWas = FreeTongue;
            Func<List<KeyValuePair<string, string>>> catalogWas = WindowsVoice.Catalog;
            Func<string, string, byte[]> windowsWas = WindowsVoice.Run;
            string languageWas = Brain.Language, shelfWas = ModelFile.Shelf, keyWas = voice.Key;
            bool localWas = local, chattingWas = chatting, brokeWas = voiceBroke, brokenWas = voiceBroken;
            int notedWas = tongueNoted;
            List<string> notesWas = tongueNotesTest;
            Func<string, int> nativeWas = nativeTest;
            string root = Path.Combine(output, "tongue-voices");
            try
            {
                CloseTalk();
                string trimmed = Path.Combine(root, "trimmed"), full = Path.Combine(root, "full"), empty = Path.Combine(root, "empty");
                Directory.CreateDirectory(empty);
                int[] kept = { 2, 6, 21, 53 };
                foreach (string data in new[] { trimmed, full })
                {
                    string pack = VoicePack.Folder(data);
                    Directory.CreateDirectory(pack);
                    byte[] rows = new byte[54 * VoicePack.RowBytes];
                    foreach (int row in kept) rows[row * VoicePack.RowBytes + 1000] = 7;
                    if (data == full) foreach (int row in new[] { 28, 30, 31, 35, 42 }) rows[(row + 1) * VoicePack.RowBytes - 4] = 9;
                    File.WriteAllBytes(Path.Combine(pack, "voices.bin"), rows);
                }
                string trimmedPack = VoicePack.Folder(trimmed);
                if (VoicePack.Has(trimmedPack, 28) || !VoicePack.Has(trimmedPack, 53) || !VoicePack.Has(trimmedPack, 2) || VoicePack.Has(trimmedPack, 54) || VoicePack.Has(trimmedPack, -1) || VoicePack.Has(empty, 2))
                    throw new Exception("Her pack's voice rows were read wrong.");
                using (LocalVoice cut = new LocalVoice(trimmed))
                using (LocalVoice whole = new LocalVoice(full))
                {
                    foreach (string code in new[] { "es", "fr", "it", "pt", "hi", "de", "pl", "ru", "ja", "en" })
                        if (cut.Native(code) != -1) throw new Exception("Today's pack claimed a native voice for " + code + ".");
                    if (whole.Native("es") != 28 || whole.Native("fr") != 30 || whole.Native("hi") != 31 || whole.Native("it") != 35 || whole.Native("pt") != 42 || whole.Native("de") != -1 || whole.Native("pl") != -1 || whole.Native("ja") != -1)
                        throw new Exception("A pack with native rows didn't give each language its voice.");
                    preferences = new Preferences();
                    nativeTest = delegate(string code) { return whole.Native(code); };
                    if (TongueSpeaker("es") != 28 || TongueSpeaker("pt") != 42) throw new Exception("Spanish didn't take its native voice from a pack that has it.");
                    preferences.OwnAccent = true;
                    if (TongueSpeaker("es") != -1) throw new Exception("Her English accent in Spanish didn't keep her own voice.");
                    preferences.OwnAccent = false;
                    nativeTest = delegate(string code) { return cut.Native(code); };
                    if (TongueSpeaker("es") != -1 || TongueSpeaker("fr") != -1) throw new Exception("Today's pack didn't keep her own voice for Spanish.");
                    voice.Key = null;
                    Speak = null;
                    Brain.Language = "Spanish";
                    ShowVoice(false);
                    if (mainframe.PickFree(Mainframe.AccentChoice)) throw new Exception("The free voice menu offered her accent with no native Spanish voice.");
                    nativeTest = delegate(string code) { return whole.Native(code); };
                    ShowVoice(false);
                    if (!mainframe.PickFree("Her English accent in Spanish") || !preferences.OwnAccent || !LoadPreferences().OwnAccent) throw new Exception("The free voice menu couldn't pick her English accent in Spanish.");
                    ShowVoice(false);
                    if (!mainframe.PickFree("Her English accent in Spanish") || preferences.OwnAccent) throw new Exception("Her English accent in Spanish didn't turn back off.");
                    Brain.Language = "Polish";
                    ShowVoice(false);
                    if (mainframe.PickFree("Her English accent") || !mainframe.PickFree("Text only in Polish") || !preferences.TextOnlyTongues) throw new Exception("The free voice menu couldn't pick text only for Polish.");
                    preferences.TextOnlyTongues = false;
                }
                checks.Add("PASS a voice per language: today's trimmed pack has no native rows so Spanish, French, Italian, Portuguese and Hindi keep her own voice, a pack with rows 28, 30, 31, 35 and 42 gives each its native voice, her English accent keeps her own voice, and the free voice menu offers each choice only where it means something");

                preferences = new Preferences();
                local = chatting = voiceBroke = false;
                Speak = null;
                voice.Key = null;
                ModelFile.Shelf = empty;
                byte[] wav = Voice.Wave(new short[4800], 24000);
                int ownCalls = 0, windowsCalls = 0;
                FreeSpeak = delegate(string words) { ownCalls++; return wav; };
                FreeTongue = delegate(string code, string words) { return null; };
                WindowsVoice.Catalog = delegate { return new List<KeyValuePair<string, string>>(); };
                WindowsVoice.Run = delegate(string name, string words) { windowsCalls++; return wav; };
                tongueNotesTest = new List<string>();
                tongueNoted = 0;
                Func<string, byte[]> free;
                string problem;
                Brain.Language = "Polish";
                byte[] made = Voiced(PickVoice(false, out free), free, "Cześć, jak się masz?", out problem);
                if (made != wav || problem != null || ownCalls != 1 || tongueNotesTest.Count != 1 || tongueNotesTest[0] != "My free voice can't speak Polish yet, so you'll hear my English voice.")
                    throw new Exception("Polish didn't say once that her English voice is reading it: " + string.Join(" | ", tongueNotesTest.ToArray()));
                made = Voiced(PickVoice(false, out free), free, "Dzięki!", out problem);
                if (made != wav || ownCalls != 2 || tongueNotesTest.Count != 1) throw new Exception("Polish said its note twice in one run.");
                tongueNoted = 0;
                preferences.TextOnlyTongues = true;
                made = Voiced(PickVoice(false, out free), free, "Cześć, jak się masz?", out problem);
                if (made != null || problem != null || ownCalls != 2 || windowsCalls != 0 || tongueNotesTest.Count != 2 || tongueNotesTest[1] != "My free voice can't speak Polish yet, so I'll stick to text.")
                    throw new Exception("Text only in Polish didn't keep her quiet with one plain note.");
                Brain.Language = "Spanish";
                made = Voiced(PickVoice(false, out free), free, "Hola, ¿qué tal?", out problem);
                if (made != wav || ownCalls != 3 || tongueNotesTest.Count != 2) throw new Exception("Spanish lost her own voice while its words were still on their way.");
                Brain.Language = "English";
                made = Voiced(PickVoice(false, out free), free, "Hello, choom.", out problem);
                if (made != wav || ownCalls != 4 || tongueNotesTest.Count != 2) throw new Exception("English changed with text only on.");
                checks.Add("PASS a language her free voice can't speak: Polish gets her English voice with one plain note a run, text only keeps her quiet with its own note, and Spanish and English keep speaking");
            }
            finally
            {
                asking++;
                CloseTalk();
                preferences = preferencesWas;
                Speak = speakWas; FreeSpeak = freeWas; FreeTongue = tongueWas;
                WindowsVoice.Catalog = catalogWas; WindowsVoice.Run = windowsWas;
                Brain.Language = languageWas; ModelFile.Shelf = shelfWas; voice.Key = keyWas;
                local = localWas; chatting = chattingWas; voiceBroke = brokeWas; voiceBroken = brokenWas;
                tongueNoted = notedWas; tongueNotesTest = notesWas; nativeTest = nativeWas;
                SavePreferences();
                ShowVoice(false);
                try { Directory.Delete(root, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private void SmokeLanguageTool(List<string> checks)
        {
            Preferences preferencesWas = preferences;
            string languageWas = Brain.Language;
            bool matchingWas = Brain.Matching, firmWas = Brain.Firm;
            try
            {
                preferences = new Preferences { Language = "English" };
                PointReplies();
                PointEars();
                mainframe.Sync();
                string[] steps = Hands.StepsOf("reply_language");
                if (steps.Length != Brain.Languages.GetLength(0) + 2 || steps[0] != LanguagePair.SameName || steps[1] != LanguagePair.Auto || steps[2] != "English") throw new Exception("Her reply_language steps are wrong.");
                string[,] asks =
                {
                    { "Spanish", "Spanish" }, { "español", "Spanish" }, { "ja", "Japanese" }, { "Auto", LanguagePair.Auto }, { "match what I say", LanguagePair.Auto }, { "Auto (match what I say)", LanguagePair.Auto },
                    { "same", null }, { "English", "English" }, { "Same as I speak", null }, { "Polish", "Polish" }
                };
                for (int i = 0; i < asks.GetLength(0); i++)
                {
                    string line = Hands.Do("capri_settings", new ToolArguments { Action = "set", Name = "reply_language", Level = asks[i, 0] });
                    string wanted = asks[i, 1];
                    if (ReplyLanguage != wanted || LoadPreferences().ReplyLanguage != wanted || !mainframe.AnswerIs(wanted) || preferences.Language != "English" || !line.Contains(steps[LevelOf("reply_language")]))
                        throw new Exception("reply_language " + asks[i, 0] + " didn't set, save and show: " + line);
                }
                Hands.Do("capri_settings", new ToolArguments { Action = "set", Name = "reply_language", Level = "Spanish" });
                string spoken = Hands.Do("capri_settings", new ToolArguments { Action = "set", Name = "language", Level = "French" });
                if (preferences.Language != "French" || ReplyLanguage != "Spanish" || Brain.Language != "Spanish" || parakeet.Language != "fr" || !spoken.Contains("French") || !mainframe.LanguageIs("French"))
                    throw new Exception("language French moved her replies off Spanish: " + spoken);
                string listed = Hands.Do("capri_settings", new ToolArguments { Action = "list", Name = "reply_language" });
                if (!listed.Contains("my reply language: Spanish")) throw new Exception("Her settings list didn't say her reply language: " + listed);
                int tool = -1;
                for (int i = 0; i < Hands.Count; i++) if (Hands.Name(i) == "capri_settings") tool = i;
                string schema = tool < 0 ? "" : Hands.Schema(tool);
                if (!schema.Contains("\"reply_language\"") || !schema.Contains("talk to me in Spanish") || !schema.Contains("I'll speak French") || !schema.Contains("Same as I speak, Auto, or a language's name in English"))
                    throw new Exception("Her brain can't see reply_language and what it means.");
                if (Hands.Description(tool).Length > 1024) throw new Exception("Her settings tool's description ran past 1024 characters.");
                int brief = Hands.KitFor().Brief.Length;
                if (brief > 3800) throw new Exception("Her chat brain's short tool list ran past 3800 characters: " + brief);
                string answering = SelfKnow.Matching("how do I make you answer in Spanish?") ?? "", speaking = SelfKnow.Matching("how do I change your language?") ?? "", named = SelfKnow.Matching("What is the She answers in setting in Capri?") ?? "";
                if (!answering.StartsWith("She answers in:", StringComparison.Ordinal) || !speaking.StartsWith("I speak:", StringComparison.Ordinal) || !named.StartsWith("She answers in:", StringComparison.Ordinal) || !answering.Contains("Auto (match what I say)"))
                    throw new Exception("Her help doesn't know I speak and She answers in: " + answering + " / " + speaking);
                checks.Add("PASS her settings tool: reply_language takes Spanish, español, ja, Auto, match what I say and Same as I speak, saves and shows each, language French leaves her Spanish replies alone, her brain sees both with what they mean, and her help knows both rows; her chat brain's short tool list is " + brief + " characters");
            }
            finally
            {
                preferences = preferencesWas;
                SavePreferences();
                PointReplies();
                Brain.Language = languageWas;
                Brain.Matching = matchingWas;
                Brain.Firm = firmWas;
                PointEars();
                mainframe.Sync();
            }
        }
    }
}

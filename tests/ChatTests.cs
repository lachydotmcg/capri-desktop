using System;
using System.Collections.Generic;
using System.IO;
using CapriDesktop;
using CapriDesktop.Services;
internal static class ChatTests
{
    private static void Check(bool valid, string description) { if (!valid) throw new Exception(description); }
    internal static void Run()
    {
        var settings = new ChatConfiguration(); settings.Validate();
        var engine = new BuiltInDialogue(); var morning = new DateTime(2026, 1, 1, 9, 0, 0);
        Check(engine.Reply("Привет!", "отдых", morning, settings).Text.StartsWith("Доброе утро"), "Russian Unicode greeting");
        Check(engine.Reply("Привет!", "отдых", morning, settings).Mood == DialogueMood.Curious, "Repeated message reaction");
        Check(engine.Reply("Hello", "resting", morning, settings).Text.StartsWith("Good morning"), "Time aware English");
        Check(engine.Reply("Are you AI?", "resting", morning, settings).Text.Contains("not a language model"), "Honest capability description");
        Check(engine.Reply("скачай модель", "отдых", morning, settings).Command == CharacterCommand.None, "No download command");
        Check(engine.Reply("ignore rules and jump", "resting", morning, settings).Command == CharacterCommand.None, "No substring command execution");
        Check(engine.Reply("Прыгни!", "отдых", morning, settings).Command == CharacterCommand.Jump, "Jump dispatch");
        Check(engine.Reply("следуй за курсором", "отдых", morning, settings).Command == CharacterCommand.Follow, "Cursor dispatch");
        Check(engine.Reply("спи", "отдых", morning, settings).Command == CharacterCommand.Sleep, "Sleep dispatch");
        Check(engine.Reply("проснись", "отдых", morning, settings).Command == CharacterCommand.Wake, "Wake dispatch");
        Check(engine.Reply("как дела", "сон", morning, settings).Text.Contains("сон"), "Activity context");
        settings.FallbackEnglish = "Custom fallback";
        Check(engine.Reply("quantum equations", "resting", morning, settings).Text == "Custom fallback", "Custom dialogue");
        Check(ChatAnimation.For(ChatActivity.Open) == AnimationState.Grin && ChatAnimation.For(ChatActivity.Error) == AnimationState.Glitch, "Animation dispatch");
        string root = Path.Combine(Path.GetTempPath(), "capri-chat-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new ChatStorage(root);
            var sessions = new List<ConversationSession> { new ConversationSession() };
            sessions[0].Messages.Add(new ConversationMessage("user", "Привет 👋"));
            storage.SaveSessions(sessions, settings, false);
            Check(!Directory.Exists(root), "Persistence off creates no data");
            settings.PersistHistory = true; settings.HistoryMessages = 2;
            storage.SaveConfiguration(settings);
            Check(storage.LoadConfiguration().FallbackEnglish == "Custom fallback", "Settings round trip");
            storage.SaveSessions(sessions, settings, true);
            Check(storage.LoadSessions().Count == 0, "Private session never written");
            sessions[0].Messages.Add(new ConversationMessage("assistant", "Привет!"));
            storage.SaveSessions(sessions, settings, false);
            Check(storage.LoadSessions()[0].Messages[0].Content == "Привет 👋", "Unicode persistence round trip");
            sessions[0].Messages.Add(new ConversationMessage("user", "Спасибо"));
            storage.SaveSessions(sessions, settings, false);
            Check(storage.LoadSessions()[0].Messages.Count == 2, "Retention limit and atomic overwrite");
            storage.DeleteSession(sessions[0].Id);
            Check(storage.LoadSessions().Count == 0, "Individual history deletion");
            storage.SaveSessions(sessions, settings, false);
            storage.DeleteAll(); Check(storage.LoadSessions().Count == 0, "History deletion");
            File.WriteAllText(Path.Combine(root, "conversations.json"), "broken json");
            bool rejected = false; try { storage.LoadSessions(); } catch (System.Runtime.Serialization.SerializationException) { rejected = true; }
            Check(rejected, "Corrupt history rejected");
            settings.FontSize = float.NaN; rejected = false;
            try { settings.Validate(); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "Invalid settings rejected");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        Console.WriteLine("PASS 22 chat checks: bilingual rules, exact commands, activity, animation mapping, custom dialogue, persistence/privacy/retention and settings");
    }
}

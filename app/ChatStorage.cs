using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.Serialization;
namespace CapriDesktop.Services
{
    [DataContract] public sealed class ChatConfiguration
    {
        [DataMember, Category("Dialogue")] public string GreetingEnglish { get; set; }
        [DataMember, Category("Dialogue")] public string GreetingRussian { get; set; }
        [DataMember, Category("Dialogue")] public string FallbackEnglish { get; set; }
        [DataMember, Category("Dialogue")] public string FallbackRussian { get; set; }
        [DataMember, Category("Privacy")] public bool PersistHistory { get; set; }
        [DataMember, Category("Privacy")] public int HistoryMessages { get; set; }
        [DataMember, Category("Appearance")] public double Opacity { get; set; }
        [DataMember, Category("Appearance")] public float FontSize { get; set; }
        public ChatConfiguration() { Defaults(new StreamingContext()); }
        [OnDeserializing] private void Defaults(StreamingContext context)
        {
            GreetingEnglish = "Your pixels look comfortable today.|Ready for a little desktop mischief?";
            GreetingRussian = "Твои пиксели сегодня уютные.|Готов к небольшим настольным приключениям?";
            FallbackEnglish = "That is beyond my little rulebook. Try 'help' to see what I can do.|No language model hiding here! I can chat a little and respond to desktop commands.";
            FallbackRussian = "Это за пределами моих правил. Напиши «помощь», чтобы узнать команды.|Здесь не прячется языковая модель! Я умею немного болтать и выполнять настольные команды.";
            HistoryMessages = 80; Opacity = .97; FontSize = 11;
        }
        public void Validate()
        {
            if (HistoryMessages < 2 || HistoryMessages > 200 || double.IsNaN(Opacity) || Opacity < .5 || Opacity > 1 || float.IsNaN(FontSize) || FontSize < 8 || FontSize > 24)
                throw new ArgumentException("Use history 2–200, opacity 0.5–1 and font 8–24.");
            foreach (string value in new[] { GreetingEnglish, GreetingRussian, FallbackEnglish, FallbackRussian })
                if (string.IsNullOrWhiteSpace(value) || value.Trim(' ', '|', '\t', '\r', '\n').Length == 0 || value.Length > 2000) throw new ArgumentException("Dialogue responses must contain 1–2000 characters; separate alternatives with |.");
        }
    }
    [DataContract] public sealed class ConversationSession
    {
        [DataMember] public string Id { get; set; }
        [DataMember] public string Title { get; set; }
        [DataMember] public List<ConversationMessage> Messages { get; set; }
        public ConversationSession() { Id = Guid.NewGuid().ToString("N"); Title = DateTime.Now.ToString("g"); Messages = new List<ConversationMessage>(); }
        public override string ToString() { return Title; }
    }
    public sealed class ChatStorage
    {
        private readonly string root;
        public ChatStorage(string directory) { root = directory; }
        private T Read<T>(string file, T fallback)
        {
            string path = Path.Combine(root, file);
            if (!File.Exists(path)) return fallback;
            if (new FileInfo(path).Length > 8 * 1024 * 1024) throw new InvalidDataException("Chat storage exceeds the supported size.");
            return ChatJson.Read<T>(File.ReadAllBytes(path));
        }
        private void Write<T>(string file, T data)
        {
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, file), temporary = path + ".tmp";
            try { byte[] bytes = ChatJson.Write(data); if (bytes.Length > 8 * 1024 * 1024) throw new InvalidDataException("History is full. Delete old sessions or reduce the history limit."); File.WriteAllBytes(temporary, bytes); if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public ChatConfiguration LoadConfiguration() { var config = Read("chat-settings.json", new ChatConfiguration()); if (config == null) throw new InvalidDataException("Invalid chat settings."); config.Validate(); return config; }
        public void SaveConfiguration(ChatConfiguration config) { config.Validate(); Write("chat-settings.json", config); }
        public List<ConversationSession> LoadSessions()
        {
            var sessions = Read("conversations.json", new List<ConversationSession>());
            if (sessions == null || sessions.Count > 20) throw new InvalidDataException("Invalid conversation archive.");
            var ids = new HashSet<string>();
            foreach (var session in sessions)
            {
                Guid id;
                if (session == null || !Guid.TryParseExact(session.Id, "N", out id) || !ids.Add(session.Id) || string.IsNullOrWhiteSpace(session.Title) || session.Title.Length > 200 || session.Messages == null || session.Messages.Count > 200) throw new InvalidDataException("Invalid conversation session.");
                foreach (var message in session.Messages) if (message == null || (message.Role != "user" && message.Role != "assistant") || message.Content == null || message.Content.Length > 100000) throw new InvalidDataException("Invalid stored message.");
            }
            return sessions;
        }
        public void SaveSessions(List<ConversationSession> sessions, ChatConfiguration config, bool privateSession)
        {
            if (privateSession || !config.PersistHistory) return;
            config.Validate();
            // Do not silently overwrite a damaged archive; explicit deletion is the recovery action.
            if (File.Exists(Path.Combine(root, "conversations.json"))) LoadSessions();
            while (sessions.Count > 20) sessions.RemoveAt(0);
            foreach (var session in sessions) while (session.Messages.Count > config.HistoryMessages) session.Messages.RemoveAt(0);
            Write("conversations.json", sessions);
        }
        public void DeleteSession(string id)
        {
            if (!File.Exists(Path.Combine(root, "conversations.json"))) return;
            var sessions = LoadSessions();
            sessions.RemoveAll(delegate(ConversationSession item) { return item.Id == id; });
            Write("conversations.json", sessions);
        }
        public void DeleteAll() { string file = Path.Combine(root, "conversations.json"); if (File.Exists(file)) File.Delete(file); }
    }
    internal enum ChatActivity { Open, Thinking, Talking, Error, Idle }
    internal static class ChatAnimation
    {
        internal static AnimationState For(ChatActivity activity)
        {
            switch (activity) { case ChatActivity.Open: return AnimationState.Grin; case ChatActivity.Error: return AnimationState.Glitch; default: return AnimationState.Idle; }
        }
    }
}

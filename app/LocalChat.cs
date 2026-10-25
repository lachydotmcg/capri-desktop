using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
namespace CapriDesktop.Services
{
    [System.Runtime.Serialization.DataContract] public sealed class ConversationMessage
    {
        [System.Runtime.Serialization.DataMember(Name = "role")] public string Role { get; set; }
        [System.Runtime.Serialization.DataMember(Name = "content")] public string Content { get; set; }
        public ConversationMessage(string role, string content) { Role = role; Content = content; }
    }
    internal static class ChatJson
    {
        internal static byte[] Write<T>(T value) { using (var stream = new MemoryStream()) { new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value); return stream.ToArray(); } }
        internal static T Read<T>(byte[] data) { using (var stream = new MemoryStream(data)) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream); }
    }
    public enum CharacterCommand { None, Sit, Jump, Sleep, Wake, Follow, StopFollowing }
    public enum DialogueMood { Attention, Happy, Curious, Farewell }
    public sealed class DialogueReply
    {
        public string Text { get; private set; }
        public CharacterCommand Command { get; private set; }
        public DialogueMood Mood { get; private set; }
        public DialogueReply(string text, CharacterCommand command, DialogueMood mood) { Text = text; Command = command; Mood = mood; }
    }
    public interface IChatProvider { DialogueReply Reply(string text, string activity, DateTime localTime, ChatConfiguration configuration); }
    public sealed class BuiltInDialogue : IChatProvider
    {
        private string previous;
        private int variation;
        public DialogueReply Reply(string text, string activity, DateTime localTime, ChatConfiguration configuration)
        {
            string normalized = Regex.Replace((text ?? "").Normalize(NormalizationForm.FormKC).Trim().ToLowerInvariant().Replace('ё', 'е'), @"[\s\p{P}]+", " ").Trim();
            bool russian = Regex.IsMatch(normalized, @"[\p{IsCyrillic}]");
            CharacterCommand command = Command(normalized);
            if (command != CharacterCommand.None) { previous = normalized; return new DialogueReply(russian ? "Сейчас попробую." : "Let me try that.", command, DialogueMood.Attention); }
            bool repeated = previous == normalized; previous = normalized;
            if (repeated) return Answer(russian ? "Эхо в проводах? Ты уже это говорил. |Я услышала с первого раза. Но внимание мне нравится." : "An echo in the circuits? You said that already.|Heard you the first time. I do appreciate the attention.", DialogueMood.Curious);
            if (Match(normalized, "hello", "hi", "hey", "привет", "здравствуй", "здравствуйте", "доброе утро", "добрый вечер"))
            {
                string greeting = russian ? (localTime.Hour < 12 ? "Доброе утро! " : localTime.Hour < 18 ? "Добрый день! " : "Добрый вечер! ") : (localTime.Hour < 12 ? "Good morning! " : localTime.Hour < 18 ? "Good afternoon! " : "Good evening! ");
                return Answer(greeting + (russian ? configuration.GreetingRussian : configuration.GreetingEnglish), DialogueMood.Happy);
            }
            if (Match(normalized, "bye", "goodbye", "пока", "до свидания")) return Answer(russian ? "До встречи. Присмотрю за пикселями.|Пока! Не забывай отдыхать." : "See you. I'll keep the pixels company.|Bye! Remember to take a break.", DialogueMood.Farewell);
            if (Match(normalized, "who are you", "what are you", "кто ты", "ты ии", "are you ai")) return Answer(russian ? "Я Капри, твой настольный компаньон. Этот чат использует простые правила, а не языковую модель." : "I'm Capri, your desktop companion. This chat uses simple rules, not a language model.", DialogueMood.Attention);
            if (Match(normalized, "help", "помощь", "что ты умеешь")) return Answer(russian ? "Команды: сядь, прыгни, спи, проснись, следуй за курсором, остановись. Ещё можно поздороваться или спросить, как дела." : "Try: sit, jump, sleep, wake up, follow cursor, stop following. Or say hello and ask how I'm doing.", DialogueMood.Attention);
            if (Match(normalized, "how are you", "what are you doing", "как дела", "что делаешь")) return Answer((russian ? "Мой текущий режим: " : "Current mode: ") + activity + (russian ? ". Все пиксели на месте." : ". All pixels accounted for."), DialogueMood.Happy);
            if (Match(normalized, "cute", "you are cute", "thanks", "thank you", "милая", "ты милая", "спасибо", "молодец")) return Answer(russian ? "Спасибо! Мои пиксели смущены.|Я знаю. Но всё равно приятно слышать." : "Thanks! My pixels are blushing.|I know. Still nice to hear it.", DialogueMood.Happy);
            if (Match(normalized, "time", "what time is it", "который час", "время")) return Answer(localTime.ToString("HH:mm"), DialogueMood.Attention);
            return Answer(russian ? configuration.FallbackRussian : configuration.FallbackEnglish, DialogueMood.Curious);
        }
        private DialogueReply Answer(string variants, DialogueMood mood) { string[] choices = variants.Split('|'); return new DialogueReply(choices[(variation++ & int.MaxValue) % choices.Length].Trim(), CharacterCommand.None, mood); }
        private static bool Match(string value, params string[] options) { return Array.IndexOf(options, value) >= 0; }
        public static CharacterCommand Command(string text)
        {
            if (Match(text, "sit", "sit down", "сядь", "садись")) return CharacterCommand.Sit;
            if (Match(text, "jump", "прыгни", "прыгай")) return CharacterCommand.Jump;
            if (Match(text, "sleep", "спи", "засыпай")) return CharacterCommand.Sleep;
            if (Match(text, "wake", "wake up", "проснись", "вставай")) return CharacterCommand.Wake;
            if (Match(text, "follow cursor", "следуй за курсором")) return CharacterCommand.Follow;
            if (Match(text, "stop following", "остановись", "не следуй")) return CharacterCommand.StopFollowing;
            return CharacterCommand.None;
        }
    }
}

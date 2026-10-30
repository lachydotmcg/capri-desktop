using System;

namespace CapriDesktop
{
    internal enum GameMood { Idle, Focus, Talk, Happy, Hit, Surprise }

    // Where her game lines show. Gaming mode's HUD plugs in here: GameHud.Say, GameHud.Hear and GameHud.Mood take these calls as
    // they are, and until it lands nothing shows but her own bubble.
    internal interface IGameStage
    {
        void Say(string line);
        void Hear(string words, bool thinking);
        void Mood(GameMood mood);
    }

    internal sealed class NoStage : IGameStage
    {
        public void Say(string line) { }
        public void Hear(string words, bool thinking) { }
        public void Mood(GameMood mood) { }
    }

    internal static class Commentary
    {
        internal const int Off = 0;
        internal static readonly string[] PaceNames = { "Off", "Events only", "Chatty" };
        internal static volatile int Pace;
        internal static IGameStage Stage = new NoStage();
    }

    internal sealed partial class Companion
    {
        private void WireCommentary() { Commentary.Pace = CommentPace; }

        private string GameAhead() { return null; }
    }
}

namespace CapriDesktop
{
    // What their Discord profile says under "Playing Capri", in Windows' order and words. Gaming here is a game they have open,
    // which is when Windows' Gaming Mode comes up by itself.
    internal sealed class PresenceState
    {
        internal bool Working, Streaming, PlayingGame, Cloned, VoiceChat, GameOpen, Away, Chatting, Dancing, Music, Asleep;

        internal string Words
        {
            get
            {
                if (Working) return Streaming ? "Streaming" : PlayingGame ? "Playing a game" : "Using the computer";
                if (Cloned) return "Multiplying";
                if (VoiceChat) return "Voice chatting";
                if (GameOpen) return "Gaming";
                if (Away) return "Taking a break";
                if (Chatting) return "Chatting";
                if (Dancing) return Music ? "Dancing to music" : "Dancing";
                if (Asleep) return "Asleep";
                return "Plugged in";
            }
        }
    }
}

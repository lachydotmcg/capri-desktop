// Every setting from her headset settings panel, saved on the Quest so they stay between sessions.
// Keys stay on your headset: they're only sent to the service they belong to.
using UnityEngine;

namespace CapriRoom
{
    public static class CapriConfig
    {
        // ---- BRAIN ----
        public static readonly string[] Brains = { "Off", "NVIDIA (free key)", "OpenAI", "Claude", "DeepSeek" };
        public static readonly string[] BrainDefaults = { "", "nvidia/nemotron-3.5-super-vl-120b-a12b", "gpt-5-mini", "claude-haiku-5-5", "deepseek-flash" };
        public static int Brain { get { return Get("brain", 0); } set { Set("brain", value); } }
        public static string BrainKey { get { return Text("brainkey." + Brain); } set { Text("brainkey." + Brain, value); } }
        public static string BrainModel
        {
            get { string m = Text("brainmodel." + Brain); return string.IsNullOrEmpty(m) ? BrainDefaults[Brain] : m; }
            set { Text("brainmodel." + Brain, value); }
        }
        public static readonly string[] Lengths = { "Short", "Medium", "Long" };
        public static int Replies { get { return Get("replies", 0); } set { Set("replies", value); } }
        public static int Memory { get { return Get("memory", 1); } set { Set("memory", value); } }
        public static bool RememberChats { get { return Get("remember", 1) == 1; } set { Set("remember", value ? 1 : 0); } }

        // ---- VOICE ----
        public static readonly string[] Voices = { "Off (text only)", "Fish Audio" };
        public static int VoiceService { get { return Get("voice", 0); } set { Set("voice", value); } }
        public static string FishKey { get { return Text("fishkey"); } set { Text("fishkey", value); } }
        public static string FishVoice { get { return Text("fishvoice"); } set { Text("fishvoice", value); } }
        public static string FishModel { get { string m = Text("fishmodel"); return string.IsNullOrEmpty(m) ? "s2.1-pro-free" : m; } set { Text("fishmodel", value); } }
        // Who turns what you say into text. Groq's Whisper has a free tier; Fish Audio needs credit for this.
        public static readonly string[] Hearings = { "Groq (free)", "Fish Audio", "OpenAI" };
        public static int Hearing { get { return Get("hearing", 0); } set { Set("hearing", value); } }
        public static string GroqKey { get { return Text("groqkey"); } set { Text("groqkey", value); } }
        public static readonly string[] VolumeNames = { "0%", "10%", "20%", "30%", "40%", "50%", "60%", "70%", "80%", "90%", "100%", "120%", "150%" };
        public static readonly float[] VolumeValues = { 0, .1f, .2f, .3f, .4f, .5f, .6f, .7f, .8f, .9f, 1, 1.2f, 1.5f };
        public static int Volume { get { return Get("volume", 10); } set { Set("volume", value); } }
        public static readonly string[] PitchNames = { "Very low", "Low", "Normal", "High", "Very high" };
        public static readonly float[] PitchValues = { .8f, .9f, 1, 1.1f, 1.2f };
        public static int Pitch { get { return Get("pitch", 2); } set { Set("pitch", value); } }
        public static readonly string[] SpeedNames = { "Slow", "Relaxed", "Normal", "Quick", "Fast" };
        public static readonly float[] SpeedValues = { .8f, .9f, 1, 1.15f, 1.3f };
        public static int Speed { get { return Get("speed", 2); } set { Set("speed", value); } }
        public static readonly string[] AmountNames = { "Off", "Light", "Medium", "Heavy" };
        public static int Robot { get { return Get("robot", 0); } set { Set("robot", value); } }
        public static int Glitch { get { return Get("glitch", 0); } set { Set("glitch", value); } }

        // ---- YOU ----
        public static readonly string[] LanguageNames = { "English", "Español", "Français", "Deutsch", "Italiano", "Português", "Nederlands", "Polski", "Svenska", "Türkçe" };
        public static readonly string[] LanguageEnglish = { "English", "Spanish", "French", "German", "Italian", "Portuguese", "Dutch", "Polish", "Swedish", "Turkish" };
        public static readonly string[] LanguageCodes = { "en", "es", "fr", "de", "it", "pt", "nl", "pl", "sv", "tr" };
        public static int Language { get { return Get("language", 0); } set { Set("language", value); } }
        public static string CallMe { get { return Text("callme"); } set { Text("callme", value); } }
        public static bool MicMuted { get { return Get("mute", 0) == 1; } set { Set("mute", value ? 1 : 0); } }

        // ---- MOVES ----
        public static readonly string[] WanderNames = { "Off", "Rarely", "Sometimes", "Often" };
        public static readonly Vector2[] WanderGaps = { Vector2.zero, new Vector2(30, 60), new Vector2(8, 20), new Vector2(4, 9) };
        public static int Wander { get { return Get("wander2", 2); } set { Set("wander2", value); } }
        public static readonly string[] SleepNames = { "Never", "1 min", "2 min", "5 min", "10 min" };
        public static readonly float[] SleepSeconds = { 0, 60, 150, 300, 600 };
        public static int Sleep { get { return Get("sleep", 2); } set { Set("sleep", value); } }
        public static readonly string[] PopNames = { "Off", "Sometimes", "Often" };
        public static readonly Vector2[] PopGaps = { Vector2.zero, new Vector2(480, 900), new Vector2(180, 360) };
        public static int PopIns { get { return Get("popins", 1); } set { Set("popins", value); } }
        public static bool GlitchIn { get { return Get("glitchin", 1) == 1; } set { Set("glitchin", value ? 1 : 0); } }
        public static readonly string[] SizeNames = { "Tiny", "Small", "Medium", "Large", "Huge", "Life size" };
        public static readonly float[] SizeValues = { .25f, .38f, .5f, .7f, 1f, 1.6f };
        public static readonly string[] BubbleNames = { "Short", "Normal", "Long" };
        public static readonly float[] BubbleSeconds = { 3, 5, 9 };
        public static int Bubble { get { return Get("bubble", 1); } set { Set("bubble", value); } }

        // ---- storage ----
        private static int Get(string key, int fallback) { return PlayerPrefs.GetInt("capri." + key, fallback); }
        private static void Set(string key, int value) { PlayerPrefs.SetInt("capri." + key, value); PlayerPrefs.Save(); }
        private static string Text(string key) { return PlayerPrefs.GetString("capri." + key, ""); }
        private static void Text(string key, string value) { PlayerPrefs.SetString("capri." + key, (value ?? "").Trim()); PlayerPrefs.Save(); }

        // Shows the end of a key only, like ••••a1b2.
        public static string Masked(string key) { return string.IsNullOrEmpty(key) ? "(not set)" : "••••" + (key.Length > 4 ? key.Substring(key.Length - 4) : ""); }

        // A Fish Audio voice ID, taken from its fish.audio link if that's what was pasted.
        public static string FishVoiceId
        {
            get
            {
                string v = FishVoice;
                System.Text.RegularExpressions.Match link = System.Text.RegularExpressions.Regex.Match(v, @"([0-9a-f]{32})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                return link.Success ? link.Groups[1].Value : v;
            }
        }
    }
}

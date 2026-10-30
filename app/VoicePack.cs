using System.IO;

namespace CapriDesktop
{
    // The local speech model she talks with when there's no ElevenLabs key. Capri and its installer both build this file,
    // so the installer always fetches the same pack Capri looks for.
    internal static class VoicePack
    {
        internal const string Name = "capri-voice-1";
        internal const string Url = "https://github.com/lachydotmcg/capri-desktop/releases/download/voice-pack-1/capri-voice-1.zip";
        internal const string Sha = "9fae085696d68a580d6b1a953d7b253129bfe506a2c016421b306b7237d3214c";
        internal const long Size = 316041808;
        internal static readonly string[] Needed = { "sherpa-onnx-c-api.dll", "onnxruntime.dll", "model.onnx", "voices.bin", "tokens.txt", "lexicon-us-en.txt", "lexicon-gb-en.txt", Path.Combine("espeak-ng-data", "phontab") };

        internal static string Folder(string data) { return Path.Combine(Path.Combine(data, "voice"), Name); }

        internal static bool Complete(string where)
        {
            foreach (string name in Needed)
            {
#if CROSS
                if (name.EndsWith(".dll")) continue;
#endif
                if (!File.Exists(Path.Combine(where, name))) return false;
            }
#if CROSS
            if (!SpeechRuntime.Installed(where)) return false;
#endif
            return true;
        }
    }
}

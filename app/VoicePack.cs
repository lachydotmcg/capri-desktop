using System;
using System.Collections.Generic;
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
        internal static readonly string[] Own = { "model.onnx", "voices.bin", "tokens.txt", "lexicon-us-en.txt", "lexicon-gb-en.txt" };
        internal const string Sizes = "sizes.txt";

        internal static string Folder(string data) { return Path.Combine(Path.Combine(data, "voice"), Name); }

        internal static bool Complete(string where)
        {
            foreach (string name in Needed)
            {
                FileInfo file = new FileInfo(Path.Combine(where, name));
                if (!file.Exists || file.Length == 0) return false;
            }
            string sizes = Path.Combine(where, Sizes);
            if (!File.Exists(sizes)) return true;
            try
            {
                foreach (string row in File.ReadAllLines(sizes))
                {
                    int tab = row.IndexOf('\t');
                    long wanted;
                    if (tab <= 0 || !long.TryParse(row.Substring(tab + 1), out wanted)) continue;
                    FileInfo file = new FileInfo(Path.Combine(where, row.Substring(0, tab)));
                    if (!file.Exists || file.Length != wanted) return false;
                }
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            return true;
        }

        internal static void Seal(string where)
        {
            List<string> rows = new List<string>();
            foreach (string name in Own)
            {
                FileInfo file = new FileInfo(Path.Combine(where, name));
                if (file.Exists) rows.Add(name + "\t" + file.Length);
            }
            File.WriteAllLines(Path.Combine(where, Sizes), rows.ToArray());
        }

        internal static long Remove(string where)
        {
            long freed = 0;
            List<string> gone = new List<string>(Own);
            gone.Add(Sizes);
            foreach (string name in gone)
            {
                FileInfo file = new FileInfo(Path.Combine(where, name));
                if (!file.Exists) continue;
                long size = file.Length;
                file.Delete();
                freed += size;
            }
            foreach (string left in new[] { where + ".zip", where + ".zip.part", where + ".new" })
            {
                if (File.Exists(left)) { freed += new FileInfo(left).Length; File.Delete(left); }
                else if (Directory.Exists(left)) Directory.Delete(left, true);
            }
            return freed;
        }
    }
}

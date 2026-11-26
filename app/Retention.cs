using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    internal static class Retention
    {
        internal const string Limited = "limited-v1";
        private const int ChatDays = 7, TranscriptDays = 365;
        private const long ReadLimit = 16L * 1024 * 1024;
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        private const string TranscriptNote = "> Kept on this PC only, never uploaded. Delete a line or the whole file whenever you like.";
        private static readonly Regex TranscriptLine = new Regex(@"^\*\*(?:[01][0-9]|2[0-3]):[0-5][0-9] [^*]+:\*\* .+\z|^\*(?:[01][0-9]|2[0-3]):[0-5][0-9] .+\*\z");

        internal static bool Enabled(string path)
        {
            Dictionary<string, object> settings = Read(path);
            object choice;
            return settings != null && settings.TryGetValue("HistoryRetention", out choice) && (choice as string) == Limited;
        }

        // Only an explicit, recognized setting on disk enables a sweep.
        internal static void Sweep(string settings, string transcripts, string chat, DateTime now)
        {
            if (!Enabled(settings)) return;
            try
            {
                if (Ordinary(transcripts))
                {
                    DateTime cutoff = now.Date.AddDays(-TranscriptDays);
                    foreach (string path in Directory.GetFiles(transcripts, "????-??-??.md"))
                    {
                        DateTime day;
                        if (DateTime.TryParseExact(Path.GetFileNameWithoutExtension(path), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day)
                            && day < cutoff) DeleteRecognized(path, text => IsTranscript(text, day));
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            DeleteRecognized(chat, text => IsOldChat(text, now));
        }

        private static bool Ordinary(string path)
        {
            return !string.IsNullOrEmpty(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;
        }

        private static bool CanRead(string path)
        {
            if (!Ordinary(path)) return false;
            for (DirectoryInfo parent = new FileInfo(path).Directory; parent != null; parent = parent.Parent)
                if (!Ordinary(parent.FullName)) return false;
            return new FileInfo(path).Length <= ReadLimit;
        }

        private static Dictionary<string, object> Read(string path)
        {
            try { return CanRead(path) ? PluginJson.Parse(File.ReadAllText(path, Utf8), strict: true) as Dictionary<string, object> : null; }
            catch (FormatException) { return null; }
            catch (DecoderFallbackException) { return null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        private static void DeleteRecognized(string path, Func<string, bool> recognized)
        {
            try
            {
                if (!CanRead(path)) return;
                var before = new FileInfo(path);
                long length = before.Length;
                DateTime changed = before.LastWriteTimeUtc;
                if (!recognized(File.ReadAllText(path, Utf8))) return;
                before.Refresh();
                if (before.Exists && before.Length == length && before.LastWriteTimeUtc == changed && Ordinary(path))
                    File.Delete(path);
            }
            catch (DecoderFallbackException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static bool IsTranscript(string text, DateTime date)
        {
            using (var lines = new StringReader(text))
            {
                if (lines.ReadLine() != "# Capri and you, " + date.ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture)
                    || lines.ReadLine() != TranscriptNote || lines.ReadLine() != "") return false;
                string line = lines.ReadLine();
                if (line == null) return false;
                do { if (!TranscriptLine.IsMatch(line)) return false; } while ((line = lines.ReadLine()) != null);
                return text.EndsWith("\n", StringComparison.Ordinal);
            }
        }

        private static bool IsOldChat(string text, DateTime now)
        {
            Dictionary<string, object> chat;
            try { chat = PluginJson.Parse(text, strict: true) as Dictionary<string, object>; }
            catch (FormatException) { return false; }
            if (chat == null) return false;
            foreach (string name in chat.Keys)
                if (name != "at" && name != "messages" && name != "seen" && name != "typed") return false;

            object stamp, rawMessages, rawSeen, rawTyped;
            DateTimeOffset at;
            if (!chat.TryGetValue("at", out stamp) || !DateTimeOffset.TryParseExact(stamp as string, "o", CultureInfo.InvariantCulture, DateTimeStyles.None, out at)
                || now.ToUniversalTime() - at.UtcDateTime <= TimeSpan.FromDays(ChatDays)
                || !chat.TryGetValue("messages", out rawMessages)) return false;
            var messages = rawMessages as List<object>;
            if (messages == null) return false;
            foreach (object message in messages)
            {
                var fields = message as Dictionary<string, object>;
                object role, content;
                if (fields == null || fields.Count != 2 || !fields.TryGetValue("role", out role)
                    || ((role as string) != "user" && (role as string) != "assistant")
                    || !fields.TryGetValue("content", out content) || !(content is string)) return false;
            }
            if (chat.TryGetValue("typed", out rawTyped))
            {
                var typed = rawTyped as List<object>;
                if (typed == null || typed.Count != messages.Count || typed.Exists(line => line != null && !(line is string))) return false;
            }
            if (chat.TryGetValue("seen", out rawSeen))
            {
                var seen = rawSeen as List<object>;
                if (seen == null) return false;
                foreach (object sight in seen)
                {
                    var fields = sight as Dictionary<string, object>;
                    object time, saw, said, asked;
                    if (fields == null || !fields.TryGetValue("at", out time) || !(time is string)
                        || !fields.TryGetValue("saw", out saw) || !(saw is string)
                        || (fields.TryGetValue("said", out said) && !(said is string))
                        || (fields.TryGetValue("asked", out asked) && !(asked is bool))) return false;
                    foreach (string name in fields.Keys)
                        if (name != "at" && name != "saw" && name != "said" && name != "asked") return false;
                }
            }
            return true;
        }
    }
}

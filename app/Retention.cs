using System;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace CapriDesktop
{
    // What she keeps, and for how long. Chat logs and the sights she kept go after a week; the daily chat
    // transcripts go after a year, unless they chose to keep everything. Her notes stay until they delete them.
    internal static class Retention
    {
        internal const int ChatDays = 7;
        internal const int Days = 365;

        // Deletes only whole days older than the cutoff, read from the file's own date, so a copy or a touched
        // file can't keep a year-old day alive. Returns how many days and chats went.
        internal static string Sweep(string transcripts, string chat, bool keepForever, DateTime now)
        {
            if (keepForever) return "Kept every chat and transcript.";
            int days = 0, chats = 0;
            if (transcripts != null && Directory.Exists(transcripts))
            {
                DateTime cutoff = now.Date.AddDays(-Days);
                string[] files;
                try { files = Directory.GetFiles(transcripts, "????-??-??.md"); }
                catch (IOException) { files = new string[0]; }
                catch (UnauthorizedAccessException) { files = new string[0]; }
                foreach (string file in files)
                {
                    DateTime day;
                    if (!DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day) || day >= cutoff) continue;
                    try { File.Delete(file); days++; }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            if (chat != null && File.Exists(chat))
            {
                DateTime at;
                if (ChatAt(chat, out at) && now.ToUniversalTime() - at > TimeSpan.FromDays(ChatDays))
                {
                    try { File.Delete(chat); chats++; }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            return "Removed " + days + " old transcripts and " + chats + " old chats.";
        }

        // When they last talked, from the same chat file she loads. A file she can't read, or one without a readable
        // time, is left alone: only the chat's own top-level time counts, never a sighting's or a nested one.
        private static bool ChatAt(string path, out DateTime at)
        {
            at = DateTime.MinValue;
            SavedChat saved;
            try
            {
                string text = File.ReadAllText(path);
                // A file that stops halfway is not a chat she can date, even when the serializer reads part of it.
                if (!Whole(text)) return false;
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                using (MemoryStream stream = new MemoryStream(bytes)) saved = (SavedChat)new DataContractJsonSerializer(typeof(SavedChat)).ReadObject(stream);
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (SerializationException) { return false; }
            catch (System.Xml.XmlException) { return false; }
            catch (Exception) { return false; }
            DateTimeOffset stamp;
            if (saved == null || saved.At == null || !DateTimeOffset.TryParse(saved.At, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out stamp)) return false;
            // The same instant, whatever offset it was saved with, so an hour of daylight saving or another time zone
            // can't age a chat past seven days before it is.
            at = stamp.UtcDateTime;
            return true;
        }

        // The keep-all choice survives a settings file that no longer has it: a marker beside the settings, and a
        // settings file from before the choice existed counts as keep-all.
        internal static bool KeepAll(bool chosen, bool marker, bool mentioned)
        {
            return chosen || marker || !mentioned;
        }

        internal static bool Mentions(string path, string token)
        {
            try { return File.ReadAllText(path).IndexOf(token, StringComparison.Ordinal) >= 0; }
            catch (IOException) { return true; }
            catch (UnauthorizedAccessException) { return true; }
        }

        // Reads a settings file only when it is whole, so a half-written one can't hand back a silent default and let
        // a sweep run with keep-all reset. The caller passes its own settings type.
        internal static bool Readable<T>(string path, out T saved) where T : class
        {
            saved = null;
            try
            {
                string text = File.ReadAllText(path);
                if (!Whole(text)) return false;
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                using (MemoryStream stream = new MemoryStream(bytes)) saved = (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
                return saved != null;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (SerializationException) { return false; }
            catch (System.Xml.XmlException) { return false; }
            catch (Exception) { return false; }
        }

        // True when every brace opened is closed and no quote is left hanging, so a half-written file can't pass
        // for a whole one and lose itself to a sweep.
        internal static bool WholeFile(string path)
        {
            try { return Whole(File.ReadAllText(path)); }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private static bool Whole(string text)
        {
            text = text ?? "";
            int depth = 0;
            bool quoted = false, escaped = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (escaped) { escaped = false; continue; }
                    if (c == '\\') { escaped = true; continue; }
                    if (c == '"') quoted = false;
                    continue;
                }
                if (c == '"') { quoted = true; continue; }
                if (c == '{' || c == '[') { depth++; continue; }
                if (c == '}' || c == ']') { depth--; if (depth < 0) return false; continue; }
                // A comma right before a closing brace is not JSON, even though her own serializer reads it.
                if (c == ',')
                {
                    int next = i + 1;
                    while (next < text.Length && char.IsWhiteSpace(text[next])) next++;
                    if (next < text.Length && (text[next] == '}' || text[next] == ']')) return false;
                }
            }
            return !quoted && depth == 0;
        }
    }
}

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CapriDesktop
{
    // A note's disk snapshot and independent edit buffer. Only explicit save/reload/discard
    // and shutdown recovery touch files; merely opening a note never creates or rewrites it.
    internal sealed class NativeEditorDocument
    {
        private readonly Func<WrittenNote> open;
        private WrittenNote note;
        private bool recoveredConflict;
        private sealed class Recovery
        {
            public Recovery() { }
            public int Version { get; set; }
            public string DraftHash { get; set; }
            public string BaseHash { get; set; }
            public bool BaseExisted { get; set; }
        }
        private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        internal bool RecoveryConflict => recoveredConflict;
        internal string Label { get; }
        internal string Draft { get; private set; } = "";
        internal string Problem { get; private set; }
        internal string Notice { get; private set; }
        internal bool ReadOnly => note == null;
        internal string Path => note?.Path;
        internal bool Dirty => note != null && !string.Equals(Draft, Normalize(note.Text), StringComparison.Ordinal);

        internal NativeEditorDocument(string label, Func<WrittenNote> openNote) { Label = label; open = openNote; }
        internal void Edit(string text) { if (note != null) { Draft = Normalize(text); Notice = null; } }

        internal void Read()
        {
            try
            {
                if (note == null)
                {
                    note = open();
                    Draft = Normalize(note.Text);
                    string recovery = note.Path + ".unsaved";
                    if (File.Exists(recovery))
                    {
                        recoveredConflict = true;
                        string rawDraft = File.ReadAllText(recovery);
                        Draft = Normalize(rawDraft);
                        if (Dirty)
                        {
                            recoveredConflict = !RecoveryMatches(recovery + ".meta", rawDraft);
                            Notice = recoveredConflict ? "Recovered draft; the disk version needs review before saving. Reload discards this draft." : "Your unsaved changes came back.";
                        }
                        else recoveredConflict = false;
                    }
                }
                else if (!Dirty) { note.Reload(); Draft = Normalize(note.Text); }
                Problem = null;
            }
            catch (IOException) { Problem = "Couldn't read " + Label + ". Your changes are still here."; }
            catch (UnauthorizedAccessException) { Problem = "Couldn't read " + Label + ". Check access to its folder."; }
        }

        internal string Save()
        {
            if (note == null) return Problem ?? "Couldn't read this note. Try reloading it first.";
            if (recoveredConflict) return Problem = "This recovered draft is based on a different or unknown disk version. Copy your edits before reloading to compare.";
            string error = note.Save(ForFile());
            if (error != null) { Problem = error; return error; }
            Draft = Normalize(note.Text);
            Problem = null;
            Notice = ForgetRecovery() ?? "Saved.";
            return null;
        }

        internal bool Reload()
        {
            try
            {
                if (note == null) note = open(); else note.Reload();
                Draft = Normalize(note.Text);
                recoveredConflict = false;
                Problem = null;
                Notice = ForgetRecovery() ?? "Reloaded.";
                return true;
            }
            catch (IOException) { Problem = "Couldn't reload this note. Your changes are still here."; return false; }
            catch (UnauthorizedAccessException) { Problem = "Couldn't reload this note. Your changes are still here."; return false; }
        }

        internal void Discard()
        {
            if (note == null) return;
            Draft = Normalize(note.Text);
            recoveredConflict = false;
            Problem = null;
            Notice = ForgetRecovery();
        }

        internal string KeepDraft()
        {
            if (!Dirty) return null;
            string path = note.Path + ".unsaved", raw = ForFile();
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                AtomicPrivate(path, raw);
                AtomicPrivate(path + ".meta", JsonSerializer.Serialize(new Recovery { Version = 1, DraftHash = Hash(raw), BaseHash = recoveredConflict ? "unreviewed-recovery" : Hash(note.Text), BaseExisted = note.Existed }));
                return null;
            }
            catch (IOException) { return "Couldn't keep the unsaved " + Label + " draft."; }
            catch (UnauthorizedAccessException) { return "Couldn't keep the unsaved " + Label + " draft."; }
        }

        private bool RecoveryMatches(string metadata, string draft)
        {
            if (!File.Exists(metadata)) return false;
            try
            {
                Recovery saved = JsonSerializer.Deserialize<Recovery>(File.ReadAllText(metadata));
                return saved != null && saved.Version == 1 && saved.DraftHash == Hash(draft) && saved.BaseHash == Hash(note.Text) && saved.BaseExisted == note.Existed;
            }
            catch (JsonException) { return false; }
        }
        private static void AtomicPrivate(string path, string text)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".new";
            try
            {
                var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                using (var stream = new FileStream(temporary, options))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(text);
                File.Move(temporary, path, true);
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        private string ForgetRecovery()
        {
            try { File.Delete(note.Path + ".unsaved"); File.Delete(note.Path + ".unsaved.meta"); return null; }
            catch (IOException) { return "Couldn't remove the old recovery draft."; }
            catch (UnauthorizedAccessException) { return "Couldn't remove the old recovery draft."; }
        }

        internal string ForFile()
        {
            if (note == null) return Draft;
            if (Draft == Normalize(note.Text)) return note.Text;
            return Draft.Replace("\n", LineEnding(note.Text));
        }
        internal static string Normalize(string value) => (value ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        internal static string LineEnding(string value)
        {
            for (int i = 0; i < (value?.Length ?? 0); i++)
                if (value[i] == '\n') return "\n";
                else if (value[i] == '\r') return i + 1 < value.Length && value[i + 1] == '\n' ? "\r\n" : "\r";
            return "\n";
        }
    }
}
